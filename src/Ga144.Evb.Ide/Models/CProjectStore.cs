using Ga144.Evb.Ide.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Creates, loads, and saves C projects (<see cref="CProject.ProjectFileName"/> plus its "include"
/// and "src" subdirectories). Unlike <see cref="YamlConfigurationStore"/>, there is exactly one small
/// YAML document per project folder rather than one big workspace file, so a project can be opened,
/// moved, or handed to someone else independently of the IDE's own hardware workspace.
/// </summary>
public sealed class CProjectStore
{
  private readonly ISerializer _serializer = new SerializerBuilder()
      .WithNamingConvention(CamelCaseNamingConvention.Instance)
      .Build();
  private readonly IDeserializer _deserializer = new DeserializerBuilder()
      .WithNamingConvention(CamelCaseNamingConvention.Instance)
      .IgnoreUnmatchedProperties()
      .Build();

  public bool IsProjectFolder(string rootPath) =>
      File.Exists(Path.Combine(rootPath, CProject.ProjectFileName));

  public CProject Create(string rootPath, string name, CProjectKind kind)
  {
    string fullRoot = Path.GetFullPath(rootPath);
    if (IsProjectFolder(fullRoot))
    {
      throw new InvalidOperationException($"\"{fullRoot}\" already contains a C project.");
    }

    if (Directory.Exists(fullRoot) && Directory.EnumerateFileSystemEntries(fullRoot).Any())
    {
      throw new InvalidOperationException($"\"{fullRoot}\" is not empty. Choose an empty or new folder for a new C project.");
    }

    var metadata = new CProjectMetadata { Name = name, Kind = kind };
    metadata.Normalize();
    var project = new CProject { RootPath = fullRoot, Metadata = metadata };
    project.EnsureDirectoriesExist();
    Save(project);
    return project;
  }

  public CProject Load(string rootPath)
  {
    string fullRoot = Path.GetFullPath(rootPath);
    string projectFilePath = Path.Combine(fullRoot, CProject.ProjectFileName);
    if (!File.Exists(projectFilePath))
    {
      throw new FileNotFoundException(
          $"\"{fullRoot}\" does not contain a {CProject.ProjectFileName} file.",
          projectFilePath);
    }

    string yaml = File.ReadAllText(projectFilePath);
    CProjectMetadata? metadata;
    try
    {
      metadata = _deserializer.Deserialize<CProjectMetadata>(yaml);
    }
    catch (YamlException exception)
    {
      throw new InvalidDataException($"Invalid YAML in \"{projectFilePath}\": {exception.Message}", exception);
    }

    metadata ??= new CProjectMetadata();
    metadata.Normalize();
    var project = new CProject { RootPath = fullRoot, Metadata = metadata };
    project.EnsureDirectoriesExist();
    return project;
  }

  public void Save(CProject project)
  {
    ArgumentNullException.ThrowIfNull(project);
    project.Metadata.Normalize();
    project.EnsureDirectoriesExist();

    string yaml = _serializer.Serialize(project.Metadata);
    string path = project.ProjectFilePath;
    string temporaryPath = path + ".tmp";
    File.WriteAllText(temporaryPath, yaml);
    File.Move(temporaryPath, path, overwrite: true);
  }
}
