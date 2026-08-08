using Ga144.Evb.Ide.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ga144.Evb.Ide.Services;

public sealed class YamlConfigurationStore : IDisposable
{
  private readonly ISerializer _serializer;
  private readonly IDeserializer _deserializer;
  private readonly SemaphoreSlim _saveGate = new(1, 1);
  private readonly string? _legacyJsonPath;

  public YamlConfigurationStore(string path, string? legacyJsonPath = null)
  {
    Path = path;
    _legacyJsonPath = legacyJsonPath;
    _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();
    _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();
  }

  public string Path { get; }

  public async Task<IdeWorkspace> LoadAsync(CancellationToken cancellationToken = default)
  {
    if (!File.Exists(Path))
    {
      IdeWorkspace workspace = await TryMigrateLegacyJsonAsync(cancellationToken).ConfigureAwait(false)
                               ?? IdeWorkspace.CreateDefault();
      workspace.Normalize();
      await SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
      return workspace;
    }

    string yaml = await File.ReadAllTextAsync(Path, cancellationToken).ConfigureAwait(false);
    IdeWorkspace? loaded;
    try
    {
      loaded = _deserializer.Deserialize<IdeWorkspace>(yaml);
    }
    catch (YamlException exception)
    {
      throw new InvalidDataException($"Invalid YAML in '{Path}': {exception.Message}", exception);
    }

    loaded ??= IdeWorkspace.CreateDefault();
    loaded.Normalize();
    return loaded;
  }

  public async Task SaveAsync(IdeWorkspace workspace, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(workspace);
    workspace.Normalize();

    // Serialize a complete snapshot before the first await. The object graph is UI-bound.
    string yaml = _serializer.Serialize(workspace);
    byte[] bytes = Encoding.UTF8.GetBytes(yaml);

    string? directory = System.IO.Path.GetDirectoryName(Path);
    if (string.IsNullOrWhiteSpace(directory))
    {
      throw new InvalidOperationException("The workspace path must include a directory.");
    }

    await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      Directory.CreateDirectory(directory);
      string temporaryPath = Path + ".tmp";
      string backupPath = Path + ".bak";

      await using (FileStream stream = new(
          temporaryPath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          16_384,
          FileOptions.Asynchronous | FileOptions.WriteThrough))
      {
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
      }

      if (File.Exists(Path))
      {
        File.Replace(temporaryPath, Path, backupPath, ignoreMetadataErrors: true);
      }
      else
      {
        File.Move(temporaryPath, Path);
      }
    }
    finally
    {
      _saveGate.Release();
    }
  }

  private async Task<IdeWorkspace?> TryMigrateLegacyJsonAsync(CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(_legacyJsonPath) || !File.Exists(_legacyJsonPath))
    {
      return null;
    }

    var options = new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true,
      Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    await using FileStream stream = File.OpenRead(_legacyJsonPath);
    LegacyWorkspace? legacy = await JsonSerializer.DeserializeAsync<LegacyWorkspace>(
        stream,
        options,
        cancellationToken).ConfigureAwait(false);
    if (legacy is null)
    {
      return null;
    }

    var workspace = new IdeWorkspace
    {
      SchemaVersion = 7,
      Settings = legacy.Settings ?? new AppSettings(),
      Projects = [Ga144Project.Create("GA144 Project 1")]
    };
    workspace.ActiveProjectId = workspace.Projects[0].Id;

    foreach (LegacyBoard legacyBoard in legacy.Boards ?? [])
    {
      Ga144Board board = Ga144Board.Create(
          string.IsNullOrWhiteSpace(legacyBoard.Name) ? "Migrated evalboard" : legacyBoard.Name,
          legacyBoard.Model);
      board.Id = legacyBoard.Id == Guid.Empty ? Guid.NewGuid() : legacyBoard.Id;
      board.PortA = legacyBoard.PortA;
      board.PortC = legacyBoard.PortC;
      workspace.Boards.Add(board);

      if (legacy.ActiveBoardId == legacyBoard.Id)
      {
        workspace.ActiveBoardId = board.Id;
      }
    }

    workspace.Normalize();
    return workspace;
  }

  public void Dispose() => _saveGate.Dispose();

  private sealed class LegacyWorkspace
  {
    public Guid? ActiveBoardId { get; set; }
    public AppSettings? Settings { get; set; }
    public List<LegacyBoard>? Boards { get; set; }
  }

  private sealed class LegacyBoard
  {
    public Guid Id { get; set; }
    public string Name { get; set; } = "Eval Board";
    public EvalBoardModel Model { get; set; }
    public BoardPortBinding? PortA { get; set; }
    public BoardPortBinding? PortC { get; set; }
  }
}
