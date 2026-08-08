using System.Text;
using Ga144.Evb.Ide.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ga144.Evb.Ide.Services;

public sealed class Ga144RomLibraryStore : IDisposable
{
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public Ga144RomLibraryStore(string path)
    {
        Path = path;
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public string Path { get; }

    public async Task<Ga144RomLibrary> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            var created = Ga144RomLibrary.CreateDefault();
            await SaveAsync(created, cancellationToken).ConfigureAwait(false);
            return created;
        }

        string yaml = await File.ReadAllTextAsync(Path, cancellationToken).ConfigureAwait(false);
        Ga144RomLibrary? library;
        try
        {
            library = _deserializer.Deserialize<Ga144RomLibrary>(yaml);
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException($"Invalid ROM-library YAML in '{Path}': {exception.Message}", exception);
        }

        library ??= Ga144RomLibrary.CreateDefault();
        library.Normalize();
        return library;
    }

    public async Task SaveAsync(Ga144RomLibrary library, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        library.Normalize();

        string yaml = _serializer.Serialize(library);
        byte[] bytes = Encoding.UTF8.GetBytes(yaml);
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The ROM-library path must include a directory.");
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

    public void Dispose() => _saveGate.Dispose();
}
