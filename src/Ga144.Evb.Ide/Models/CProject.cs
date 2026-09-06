namespace Ga144.Evb.Ide.Models;

/// <summary>
/// What a C project builds. A <see cref="CProjectKind.Library"/> project's compiled object files are
/// archived into its own .galib automatically -- there is no separate "build library" step to run. A
/// <see cref="CProjectKind.Program"/> project is the only kind that ever reaches the linker,
/// producing a .gaimg.
/// </summary>
public enum CProjectKind
{
  Program,
  Library,
}

/// <summary>
/// The on-disk metadata for a C project (<see cref="CProject.ProjectFileName"/>), living at the root
/// of the project's own folder alongside its "include" and "src" subdirectories. This is the only
/// thing actually serialized -- header and source files themselves are just ordinary files under
/// those two subdirectories, discovered by scanning the disk (<see cref="CProject.GetHeaderFiles"/>/
/// <see cref="CProject.GetSourceFiles"/>) rather than being listed here, so nothing about this format
/// needs to change when a file is added, renamed, or removed on disk -- including from outside the
/// IDE.
/// </summary>
public sealed class CProjectMetadata
{
  public int SchemaVersion { get; set; } = 1;
  public string Name { get; set; } = "C Project";
  public CProjectKind Kind { get; set; } = CProjectKind.Program;

  /// <summary>
  /// Other C projects this project imports as libraries. Each entry names a folder containing its
  /// own <see cref="CProject.ProjectFileName"/> whose Kind is <see cref="CProjectKind.Library"/>.
  /// Stored relative to this project's own root folder when possible -- so a project and the
  /// libraries it imports keep working if moved together -- falling back to an absolute path when no
  /// relative path applies (e.g. a different drive on Windows).
  /// </summary>
  public List<string> LibraryReferences { get; set; } = [];

  public void Normalize()
  {
    Name = string.IsNullOrWhiteSpace(Name) ? "C Project" : Name.Trim();
    LibraryReferences ??= [];
    LibraryReferences = LibraryReferences
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    SchemaVersion = 1;
  }
}

/// <summary>
/// A C project, rooted at an ordinary folder on disk:
/// <code>
/// &lt;RootPath&gt;/
///   project.gacproj    -- this project's CProjectMetadata, as YAML
///   include/           -- this project's own public headers (.h)
///   src/                -- this project's own sources (.c)
/// </code>
/// Loaded and saved through <see cref="Ga144.Evb.Ide.Services.CProjectStore"/>. Multiple <see cref="CProject"/>
/// instances -- one per open <c>CProjectWindow</c> -- can exist at once, each rooted at a different
/// folder, since nothing about a C project depends on the IDE's own hardware workspace.
/// </summary>
public sealed class CProject
{
  public const string ProjectFileName = "project.gacproj";
  public const string IncludeDirectoryName = "include";
  public const string SourceDirectoryName = "src";
  public const string HeaderSearchPattern = "*.h";
  public const string SourceSearchPattern = "*.c";

  public required string RootPath { get; init; }
  public required CProjectMetadata Metadata { get; init; }

  public string ProjectFilePath => Path.Combine(RootPath, ProjectFileName);
  public string IncludeDirectoryPath => Path.Combine(RootPath, IncludeDirectoryName);
  public string SourceDirectoryPath => Path.Combine(RootPath, SourceDirectoryName);

  public string Name
  {
    get => Metadata.Name;
    set => Metadata.Name = value;
  }

  public CProjectKind Kind
  {
    get => Metadata.Kind;
    set => Metadata.Kind = value;
  }

  public List<string> LibraryReferences => Metadata.LibraryReferences;

  public void EnsureDirectoriesExist()
  {
    Directory.CreateDirectory(RootPath);
    Directory.CreateDirectory(IncludeDirectoryPath);
    Directory.CreateDirectory(SourceDirectoryPath);
  }

  public IReadOnlyList<string> GetHeaderFiles() => GetFiles(IncludeDirectoryPath, HeaderSearchPattern);

  public IReadOnlyList<string> GetSourceFiles() => GetFiles(SourceDirectoryPath, SourceSearchPattern);

  /// <summary>Resolves a possibly-relative library reference (as stored in <see cref="LibraryReferences"/>) against this project's own root folder.</summary>
  public string ResolveLibraryReferencePath(string storedPath) =>
      Path.GetFullPath(Path.Combine(RootPath, storedPath));

  /// <summary>
  /// The path to store for a library reference to <paramref name="targetRootPath"/>: relative to
  /// this project's own root when a relative path exists at all -- including a sibling or cousin
  /// folder reached through one or more ".." segments -- so a project and the libraries it imports
  /// keep working if moved together; falls back to an absolute path only when no relative path
  /// applies at all (e.g. a different drive on Windows), which is what
  /// <see cref="Path.GetRelativePath(string, string)"/> itself signals by returning the target
  /// unchanged.
  /// </summary>
  public string MakeLibraryReferencePath(string targetRootPath)
  {
    string fullTarget = Path.GetFullPath(targetRootPath);
    string relative = Path.GetRelativePath(RootPath, fullTarget);
    return Path.IsPathRooted(relative) ? fullTarget : relative;
  }

  private static IReadOnlyList<string> GetFiles(string directoryPath, string searchPattern) =>
      Directory.Exists(directoryPath)
          ? Directory.EnumerateFiles(directoryPath, searchPattern, SearchOption.AllDirectories)
              .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
              .ToList()
          : [];
}
