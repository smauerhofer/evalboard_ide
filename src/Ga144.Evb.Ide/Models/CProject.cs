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

  /// <summary>
  /// Added 2026-09-08, wiring <c>galink</c> into Build: which GA144 project's chip configuration a
  /// <see cref="CProjectKind.Program"/> project's Build should compile the live CVM2 interpreter node
  /// mesh from (<see cref="Ga144.Evb.Ide.Services.CvmPrimitiveTableExporter"/>) before linking. Null
  /// means no chip project has been chosen yet -- Build then skips linking entirely, same as before
  /// this field existed, rather than guessing.
  ///
  /// A GA144 project is never a file of its own (see <see cref="Ga144Project"/>'s own remarks) -- it is
  /// one entry, by <see cref="Ga144Project.Id"/>, inside the single shared workspace document every
  /// window in this app already reads from a fixed, well-known location
  /// (<see cref="Ga144.Evb.Ide.Services.ConfigurationPathProvider"/>). Storing the Id here (rather than
  /// a path, which is what Stefan's own answer named, since at the time a GA144 "project file" seemed
  /// like it might be a stand-alone thing) resolves independently of whatever project happens to be
  /// open in the IDE right now -- Build can load it fresh even if no GA144 project window is open at
  /// all -- while still meaning exactly what Stefan asked for: reference a SPECIFIC GA144 project, not
  /// "whichever one is currently active."
  /// </summary>
  public Guid? ChipProjectId { get; set; }

  /// <summary>
  /// Which of that project's two physical chips (<see cref="Ga144ChipRole.Host"/> or
  /// <see cref="Ga144ChipRole.Target"/>) to compile the interpreter mesh from. Defaults to
  /// <see cref="Ga144ChipRole.Target"/> since, on an EVB002, that is the chip a program built by this
  /// IDE would normally be installed onto and run from -- the Host chip's own role is to talk to the
  /// PC over serial, not to run an arbitrary C project's compiled program.
  /// </summary>
  public Ga144ChipRole ChipProjectRole { get; set; } = Ga144ChipRole.Target;

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

  /// <summary>Holds the preprocessor's own output (one ".i" file per compiled ".c" file) -- entirely
  /// generated, safe to delete, and overwritten every time a source file is (re)compiled.</summary>
  public const string PreprocessedDirectoryName = "pre";

  /// <summary>Holds the CVM assembly the C compiler generates from this project's own ".c" files (one
  /// ".casm" per compiled source) -- also entirely generated. Named "tasm" (target/translated
  /// assembly) specifically to keep it apart from <see cref="AssemblyDirectoryName"/>, which holds
  /// assembly the person wrote themselves.</summary>
  public const string TargetAssemblyDirectoryName = "tasm";

  /// <summary>Hand-written CVM assembly (".casm") source files that are simply part of this project --
  /// assembled directly by <c>gaasm</c>, never compiled from C. This is where a hand-written primitive
  /// or a CVM feature the compiler doesn't (yet) generate on its own belongs, per the project's own
  /// resolution rule: something the compiler can't yet do becomes either a CVM opcode or a call into a
  /// library, and a call into a library needs that library's own assembly to exist somewhere -- this is
  /// that somewhere, for this project's own use (as opposed to a separate library project entirely).
  /// </summary>
  public const string AssemblyDirectoryName = "asm";

  /// <summary>Holds this project's own assembled object files (one ".gaobj" per assembled ".casm" --
  /// compiler-generated or hand-written, see <see cref="AssemblyDirectoryName"/>) -- entirely
  /// generated by Build, safe to delete, and overwritten every time. Added 2026-09-07 alongside
  /// <see cref="Ga144.Evb.Ide.ViewModels.CProjectViewModel.Build"/>: there was previously nowhere
  /// for Build to put an assembled object file at all.</summary>
  public const string ObjectDirectoryName = "obj";

  public const string HeaderSearchPattern = "*.h";
  public const string SourceSearchPattern = "*.c";
  public const string PreprocessedSearchPattern = "*.i";
  public const string AssemblySearchPattern = "*.casm";
  public const string ObjectSearchPattern = "*.gaobj";

  /// <summary>The file extension Build gives a Library project's own archived output (see
  /// <see cref="LibraryOutputPath"/>).</summary>
  public const string LibraryFileExtension = ".galib";

  /// <summary>The file extension Build gives a Program project's own linked output (see
  /// <see cref="ImageOutputPath"/>), added 2026-09-08 alongside wiring <c>galink</c> into Build.</summary>
  public const string ImageFileExtension = ".gaimg";

  public required string RootPath { get; init; }
  public required CProjectMetadata Metadata { get; init; }

  public string ProjectFilePath => Path.Combine(RootPath, ProjectFileName);
  public string IncludeDirectoryPath => Path.Combine(RootPath, IncludeDirectoryName);
  public string SourceDirectoryPath => Path.Combine(RootPath, SourceDirectoryName);
  public string PreprocessedDirectoryPath => Path.Combine(RootPath, PreprocessedDirectoryName);
  public string TargetAssemblyDirectoryPath => Path.Combine(RootPath, TargetAssemblyDirectoryName);
  public string AssemblyDirectoryPath => Path.Combine(RootPath, AssemblyDirectoryName);
  public string ObjectDirectoryPath => Path.Combine(RootPath, ObjectDirectoryName);

  /// <summary>
  /// Where Build archives this project's own object files when <see cref="Kind"/> is
  /// <see cref="CProjectKind.Library"/> -- named after this project's own root FOLDER name, not
  /// <see cref="Name"/>, because <see cref="Name"/> is a free-form display name that may contain
  /// characters that are not valid in a file name, while a folder that already exists on disk is
  /// guaranteed to be a valid name already.
  ///
  /// <b>Flagged:</b> Stefan has not specified this naming choice one way or the other. If a different
  /// name is wanted instead (e.g. always "library.galib", or a sanitized form of <see cref="Name"/>),
  /// this is the one place to change it.
  /// </summary>
  public string LibraryOutputPath =>
      Path.Combine(RootPath, Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootPath))) + LibraryFileExtension);

  /// <summary>Where Build writes this project's own linked output when <see cref="Kind"/> is
  /// <see cref="CProjectKind.Program"/> and <see cref="ChipProjectId"/> is set -- named the same way
  /// <see cref="LibraryOutputPath"/> is, after this project's own root folder name, for the same
  /// reason (a folder name that already exists on disk is guaranteed valid; <see cref="Name"/> is not).</summary>
  public string ImageOutputPath =>
      Path.Combine(RootPath, Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootPath))) + ImageFileExtension);

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

  /// <summary>See <see cref="CProjectMetadata.ChipProjectId"/>.</summary>
  public Guid? ChipProjectId
  {
    get => Metadata.ChipProjectId;
    set => Metadata.ChipProjectId = value;
  }

  /// <summary>See <see cref="CProjectMetadata.ChipProjectRole"/>.</summary>
  public Ga144ChipRole ChipProjectRole
  {
    get => Metadata.ChipProjectRole;
    set => Metadata.ChipProjectRole = value;
  }

  public void EnsureDirectoriesExist()
  {
    Directory.CreateDirectory(RootPath);
    Directory.CreateDirectory(IncludeDirectoryPath);
    Directory.CreateDirectory(SourceDirectoryPath);
    Directory.CreateDirectory(PreprocessedDirectoryPath);
    Directory.CreateDirectory(TargetAssemblyDirectoryPath);
    Directory.CreateDirectory(AssemblyDirectoryPath);
    Directory.CreateDirectory(ObjectDirectoryPath);
  }

  public IReadOnlyList<string> GetHeaderFiles() => GetFiles(IncludeDirectoryPath, HeaderSearchPattern);

  public IReadOnlyList<string> GetSourceFiles() => GetFiles(SourceDirectoryPath, SourceSearchPattern);

  /// <summary>The hand-written CVM assembly files that are simply part of this project (see
  /// <see cref="AssemblyDirectoryName"/>) -- as opposed to <see cref="TargetAssemblyDirectoryPath"/>,
  /// which holds only what the compiler itself generated and is never scanned as a source list.</summary>
  public IReadOnlyList<string> GetAssemblyFiles() => GetFiles(AssemblyDirectoryPath, AssemblySearchPattern);

  /// <summary>Where the C compiler should write the preprocessed ("pre") output for a given ".c" file
  /// in this project's own <see cref="SourceDirectoryPath"/>, and where it should write the generated
  /// ("tasm") CVM assembly for that same file -- both named after the source file itself, with its
  /// extension replaced, so a build can always find (or safely overwrite) both without guessing.</summary>
  public string GetPreprocessedFilePath(string sourceFilePath) =>
      Path.Combine(PreprocessedDirectoryPath, Path.GetFileNameWithoutExtension(sourceFilePath) + ".i");

  public string GetTargetAssemblyFilePath(string sourceFilePath) =>
      Path.Combine(TargetAssemblyDirectoryPath, Path.GetFileNameWithoutExtension(sourceFilePath) + ".casm");

  /// <summary>Where Build should write the assembled object file for a given ".casm" file -- whether
  /// it came from <see cref="TargetAssemblyDirectoryPath"/> (compiler-generated) or
  /// <see cref="AssemblyDirectoryPath"/> (hand-written) -- named after that ".casm" file itself, with
  /// its extension replaced.</summary>
  public string GetObjectFilePath(string assemblyFilePath) =>
      Path.Combine(ObjectDirectoryPath, Path.GetFileNameWithoutExtension(assemblyFilePath) + ".gaobj");

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