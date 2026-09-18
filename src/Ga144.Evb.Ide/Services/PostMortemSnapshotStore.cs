using System.Text;
using Ga144.Evb.Ide.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Exports/imports a <see cref="PostMortemSnapshot"/> as a standalone YAML file, at a path Stefan
/// picks himself (the post-mortem GA144 window's own Export/Import buttons own the SaveFileDialog/
/// OpenFileDialog; this class only ever sees the resulting path). Unlike
/// <see cref="Ga144RomLibraryStore"/>/<c>YamlConfigurationStore</c>, this is not one of the app's own
/// live-managed state files -- there is no concurrent writer to guard against, so a plain write is
/// enough; a half-written file left behind by a crash mid-export is no worse than any other
/// interrupted "Save As" in this app.
/// </summary>
public static class PostMortemSnapshotStore
{
  private static readonly ISerializer Serializer = new SerializerBuilder()
      .WithNamingConvention(CamelCaseNamingConvention.Instance)
      .Build();

  private static readonly IDeserializer Deserializer = new DeserializerBuilder()
      .WithNamingConvention(CamelCaseNamingConvention.Instance)
      .IgnoreUnmatchedProperties()
      .Build();

  public static async Task ExportAsync(string path, PostMortemSnapshot snapshot, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(snapshot);
    snapshot.Normalize();

    string yaml = Serializer.Serialize(snapshot);
    await File.WriteAllTextAsync(path, yaml, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
  }

  public static async Task<PostMortemSnapshot> ImportAsync(string path, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    string yaml = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

    PostMortemSnapshot? snapshot;
    try
    {
      snapshot = Deserializer.Deserialize<PostMortemSnapshot>(yaml);
    }
    catch (YamlException exception)
    {
      throw new InvalidDataException($"Invalid post-mortem snapshot YAML in '{path}': {exception.Message}", exception);
    }

    snapshot ??= new PostMortemSnapshot();
    snapshot.Normalize();
    return snapshot;
  }
}
