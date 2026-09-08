using Ga144.C.Toolchain;
using Ga144.Cvm.Toolchain;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;
using System.Collections.ObjectModel;
using System.Text;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// Backs a single <c>CProjectWindow</c>. One instance per open C project -- unlike the GA144
/// hardware <see cref="ProjectViewModel"/>, there is no single shared list of these: a C project is
/// just a folder on disk (<see cref="CProject"/>), so any number can be open, opened again later, or
/// edited by hand outside the IDE entirely.
/// </summary>
public sealed class CProjectViewModel : ObservableObject
{
  // The three kinds of managed source file this window edits, used to parameterize the shared
  // Add/Create/Remove helpers below instead of repeating them per kind.
  private enum CFileKind { Header, Source, Assembly }

  private readonly CProjectStore _store;

  // The shared "libs" directory (MainWindowViewModel.CLibsDirectoryPath) that AvailableLibraries is
  // scanned from -- added 2026-09-08 alongside the checkbox-based library-import list, replacing the
  // former manual browse-for-a-folder flow. Null (no C workspace root chosen yet) simply means the
  // list stays empty; that is not an error.
  private readonly string? _librariesDirectoryPath;

  // Resolves Model.ChipProjectId/ChipProjectRole into a real chip configuration at Build time, and
  // lists the GA144 projects a "Chip project..." picker can offer -- added 2026-09-08 wiring galink
  // into Build. Defaults to a fresh instance reading the app's own standard workspace location so the
  // one call site that constructs this view model (MainWindow.xaml.cs) needed no change.
  private readonly ChipProjectResolver _chipProjectResolver;

  private string _statusText = "Ready";
  private string? _selectedHeaderFile;
  private string? _selectedSourceFile;
  private string? _selectedAssemblyFile;
  private bool _showAssemblyFiles;

  public CProjectViewModel(CProject model, CProjectStore store, string? librariesDirectoryPath, ChipProjectResolver? chipProjectResolver = null)
  {
    Model = model;
    _store = store;
    _librariesDirectoryPath = librariesDirectoryPath;
    _chipProjectResolver = chipProjectResolver ?? new ChipProjectResolver();
    Refresh();
  }

  public CProject Model { get; }

  public string RootPath => Model.RootPath;

  public string DisplayName => $"{Name} ({KindLabel})";

  public string KindLabel => Kind == CProjectKind.Library ? "Library" : "Program";

  public bool IsLibrary => Kind == CProjectKind.Library;

  public string Name
  {
    get => Model.Name;
    set
    {
      string normalized = string.IsNullOrWhiteSpace(value) ? Model.Name : value.Trim();
      if (string.Equals(Model.Name, normalized, StringComparison.Ordinal))
      {
        return;
      }

      Model.Name = normalized;
      Save();
      OnPropertyChanged();
      OnPropertyChanged(nameof(DisplayName));
    }
  }

  public CProjectKind Kind
  {
    get => Model.Kind;
    set
    {
      if (Model.Kind == value)
      {
        return;
      }

      Model.Kind = value;
      Save();
      OnPropertyChanged();
      OnPropertyChanged(nameof(KindLabel));
      OnPropertyChanged(nameof(DisplayName));
      OnPropertyChanged(nameof(IsLibrary));
      OnPropertyChanged(nameof(IsProgramKind));
      OnPropertyChanged(nameof(IsLibraryKind));
      OnPropertyChanged(nameof(BuildDescription));
    }
  }

  public bool IsProgramKind
  {
    get => Kind == CProjectKind.Program;
    set
    {
      if (value)
      {
        Kind = CProjectKind.Program;
      }
    }
  }

  public bool IsLibraryKind
  {
    get => Kind == CProjectKind.Library;
    set
    {
      if (value)
      {
        Kind = CProjectKind.Library;
      }
    }
  }

  public ObservableCollection<string> HeaderFiles { get; } = [];
  public ObservableCollection<string> SourceFiles { get; } = [];

  // The hand-written CVM assembly files under this project's own "asm" directory (see
  // Model.AssemblyDirectoryPath), shown only when ShowAssemblyFiles is set. Added 2026-09-08, per
  // Stefan: "i want to have a separate list of assembler sources (located in the 'asm' directory)
  // which can be made visible through a checkbox. this is used to integrate assembler code into a C
  // library/program."
  public ObservableCollection<string> AssemblyFiles { get; } = [];

  // Every C project found under the shared "libs" directory (see _librariesDirectoryPath), each with
  // a checkbox selecting whether THIS project imports it. Replaces the former manual
  // Add/Remove-library-reference flow entirely (2026-09-08, per Stefan: "Replace it entirely" -- "the
  // library list must contain all the libraries from the 'libs' directory with a checkbox in front,
  // that select the library for this project").
  public ObservableCollection<CAvailableLibraryViewModel> AvailableLibraries { get; } = [];

  /// <summary>What "Chip project..." shows next to itself -- the chosen GA144 project's own name and
  /// chip role, or a plain "(none)" when <see cref="CProject.ChipProjectId"/> isn't set yet. Resolves
  /// the name fresh each time it's read (Refresh doesn't cache it) since the referenced GA144 project
  /// could have been renamed, or removed, since this window was opened.</summary>
  public string ChipProjectDescription
  {
    get
    {
      if (Model.ChipProjectId is not { } projectId)
      {
        return "Chip project: (none)";
      }

      ChipProjectResolver.Result resolved = _chipProjectResolver.Resolve(projectId, Model.ChipProjectRole);
      return resolved.Success
          ? $"Chip project: {resolved.ProjectName} ({Model.ChipProjectRole})"
          : $"Chip project: (missing -- {resolved.ErrorMessage})";
    }
  }

  public string? SelectedHeaderFile
  {
    get => _selectedHeaderFile;
    set => SetProperty(ref _selectedHeaderFile, value);
  }

  public string? SelectedSourceFile
  {
    get => _selectedSourceFile;
    set => SetProperty(ref _selectedSourceFile, value);
  }

  public string? SelectedAssemblyFile
  {
    get => _selectedAssemblyFile;
    set => SetProperty(ref _selectedAssemblyFile, value);
  }

  // Purely a UI toggle for this window -- not persisted to the project's own metadata (project.gacproj
  // stores what a project IS, not how one particular window happened to be displaying it last), so it
  // resets to hidden every time the project window is reopened.
  public bool ShowAssemblyFiles
  {
    get => _showAssemblyFiles;
    set => SetProperty(ref _showAssemblyFiles, value);
  }

  public string StatusText
  {
    get => _statusText;
    private set => SetProperty(ref _statusText, value);
  }

  // Updated 2026-09-07 alongside Build() below: the C compiler and gaasm are both now actually
  // wired into this window's "Build" button (see Build()'s own remarks), so this text no longer
  // says "not implemented yet" for those two steps. Updated again 2026-09-08: galink IS now wired
  // in -- a Program project whose "Chip project..." picker names a GA144 project has Build compile
  // that chip's own live CVM2 mesh (CvmPrimitiveTableExporter), link against it (CvmLinker), and
  // write a .gaimg (CProject.ImageOutputPath). A Program project with no chip project chosen yet
  // still just stops at "compiled and assembled", same as before this date.
  public string BuildDescription => IsLibrary
      ? "Build: compiles each src/*.c file to CVM assembly, assembles it with gaasm, " +
        "and archives the resulting object files into this project's own .galib -- automatically, with no separate step."
      : "Build: compiles each src/*.c file to CVM assembly and assembles it with gaasm, then links with galink " +
        "against the chosen \"Chip project...\"'s own live CVM2 mesh to produce a .gaimg. Choose a chip project " +
        "below if you haven't yet -- without one, Build stops after compiling and assembling, with no .gaimg.";

  public void Refresh()
  {
    ReplaceAll(HeaderFiles, ToDisplayPaths(Model.IncludeDirectoryPath, Model.GetHeaderFiles()));
    ReplaceAll(SourceFiles, ToDisplayPaths(Model.SourceDirectoryPath, Model.GetSourceFiles()));
    ReplaceAll(AssemblyFiles, ToDisplayPaths(Model.AssemblyDirectoryPath, Model.GetAssemblyFiles()));
    RefreshAvailableLibraries();
  }

  private void RefreshAvailableLibraries()
  {
    AvailableLibraries.Clear();
    if (string.IsNullOrWhiteSpace(_librariesDirectoryPath) || !Directory.Exists(_librariesDirectoryPath))
    {
      return;
    }

    string ownFullPath = Path.GetFullPath(Model.RootPath);
    IEnumerable<string> candidateFolders = Directory.EnumerateDirectories(_librariesDirectoryPath)
        .Where(_store.IsProjectFolder)
        .Where(path => !string.Equals(Path.GetFullPath(path), ownFullPath, StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    foreach (string folder in candidateFolders)
    {
      string fullPath = Path.GetFullPath(folder);
      string name = TryLoadName(fullPath) ?? Path.GetFileName(fullPath);
      bool isReferenced = Model.LibraryReferences.Any(stored =>
          string.Equals(Model.ResolveLibraryReferencePath(stored), fullPath, StringComparison.OrdinalIgnoreCase));
      AvailableLibraries.Add(new CAvailableLibraryViewModel(this, name, fullPath, isReferenced));
    }
  }

  private string? TryLoadName(string rootPath)
  {
    try
    {
      return _store.Load(rootPath).Name;
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
    {
      return null;
    }
  }

  // Called by CAvailableLibraryViewModel.IsReferenced's setter to actually add/remove the reference
  // in this project's own metadata and persist it.
  internal void SetLibraryReferenced(CAvailableLibraryViewModel library, bool isReferenced)
  {
    if (isReferenced)
    {
      if (!Model.LibraryReferences.Any(existing =>
          string.Equals(Model.ResolveLibraryReferencePath(existing), library.RootPath, StringComparison.OrdinalIgnoreCase)))
      {
        Model.LibraryReferences.Add(Model.MakeLibraryReferencePath(library.RootPath));
      }

      StatusText = $"Imported library \"{library.Name}\".";
    }
    else
    {
      Model.LibraryReferences.RemoveAll(existing =>
          string.Equals(Model.ResolveLibraryReferencePath(existing), library.RootPath, StringComparison.OrdinalIgnoreCase));
      StatusText = $"Removed library reference \"{library.Name}\".";
    }

    Save();
  }

  /// <summary>Every GA144 project the "Chip project..." picker can offer -- see <see cref="ChipProjectResolver.ListProjects"/>.</summary>
  public IReadOnlyList<ChipProjectResolver.ProjectSummary> ListAvailableChipProjects() => _chipProjectResolver.ListProjects();

  /// <summary>Sets (or clears, when <paramref name="projectId"/> is null) which GA144 project/chip
  /// Build should compile the CVM2 interpreter mesh from before linking, and persists it immediately --
  /// same "mutate Model, Save, notify" pattern every other setter in this class already follows.</summary>
  public void SetChipProject(Guid? projectId, Ga144ChipRole role)
  {
    Model.ChipProjectId = projectId;
    Model.ChipProjectRole = role;
    Save();
    OnPropertyChanged(nameof(ChipProjectDescription));
    StatusText = projectId is null
        ? "Cleared this project's chip project -- Build will not link a .gaimg."
        : "Set this project's chip project.";
  }

  public string AddExistingHeaderFile(string sourceFilePath) => AddExistingFile(sourceFilePath, CFileKind.Header);

  public string AddExistingSourceFile(string sourceFilePath) => AddExistingFile(sourceFilePath, CFileKind.Source);

  public string AddExistingAssemblyFile(string sourceFilePath) => AddExistingFile(sourceFilePath, CFileKind.Assembly);

  public string CreateNewHeaderFile(string fileName) => CreateNewFile(fileName, CFileKind.Header);

  public string CreateNewSourceFile(string fileName) => CreateNewFile(fileName, CFileKind.Source);

  public string CreateNewAssemblyFile(string fileName) => CreateNewFile(fileName, CFileKind.Assembly);

  public void RemoveHeaderFile(string displayPath) => RemoveFile(Model.IncludeDirectoryPath, displayPath);

  public void RemoveSourceFile(string displayPath) => RemoveFile(Model.SourceDirectoryPath, displayPath);

  public void RemoveAssemblyFile(string displayPath) => RemoveFile(Model.AssemblyDirectoryPath, displayPath);

  public string ResolveHeaderFilePath(string displayPath) => Path.Combine(Model.IncludeDirectoryPath, displayPath);

  public string ResolveSourceFilePath(string displayPath) => Path.Combine(Model.SourceDirectoryPath, displayPath);

  public string ResolveAssemblyFilePath(string displayPath) => Path.Combine(Model.AssemblyDirectoryPath, displayPath);

  /// <summary>
  /// Runs this project's actual build pipeline -- added 2026-09-07, replacing the "Build" button's
  /// previous behavior of just showing <see cref="BuildDescription"/> in a dialog and doing nothing:
  /// compile every "src/*.c" file to CVM assembly (<see cref="CCompiler"/>), assemble every resulting
  /// ".casm" -- both compiler-generated and this project's own hand-written "asm/*.casm" files (see
  /// <see cref="CProject.AssemblyDirectoryName"/>) -- with <see cref="CvmAssembler"/>, and, only for a
  /// <see cref="CProjectKind.Library"/> project whose ENTIRE build succeeded, archive the resulting
  /// object files into this project's own ".galib" (<see cref="CProject.LibraryOutputPath"/>) -- a
  /// library with some members silently missing would be worse than no library at all.
  ///
  /// Every source file and every hand-written assembly file is always attempted, even after an
  /// earlier one fails, so one broken file never hides problems in the others.
  ///
  /// <b>Updated 2026-09-08: galink is now wired in for a <see cref="CProjectKind.Program"/> project.</b>
  /// If <see cref="CProject.ChipProjectId"/> is set (via "Chip project..."), a build whose compile and
  /// assemble steps all succeed goes on to: export a primitive table from that chip's own live CVM2
  /// mesh (<see cref="CvmPrimitiveTableExporter"/>), load each checked <see cref="AvailableLibraries"/>
  /// entry's own already-built ".galib", and call <see cref="Ga144.Cvm.Toolchain.CvmLinker.Link"/>,
  /// writing a successful link's output to <see cref="CProject.ImageOutputPath"/>. A Program project
  /// with no chip project chosen yet still just stops at "compiled and assembled" -- exactly the
  /// previous behavior -- since there is nothing to compile primitives against. A library referenced
  /// by this project that hasn't been BUILT yet (no ".galib" on disk) is a link error naming which
  /// library and telling Stefan to build it first, not a crash or a silent skip.
  ///
  /// Updated 2026-09-08 (separately): include resolution and object linkage now walk <see cref="AvailableLibraries"/>
  /// (checkbox-selected, physically found under the shared "libs" directory) instead of the former
  /// manually-added <c>LibraryReferences</c> list -- the underlying persisted data
  /// (<see cref="CProject.LibraryReferences"/>) is unchanged, only how this method enumerates it.
  ///
  /// <b>Flagged:</b> include resolution only looks at this project's own "include" directory plus
  /// each DIRECTLY imported library's "include" directory -- not a library's own further library
  /// references (transitive includes). Stefan has not said whether transitive includes should be
  /// visible; today, a header inside "a library of a library" is not found.
  /// </summary>
  public CBuildResult Build()
  {
    Model.EnsureDirectoriesExist();

    var messages = new List<string>();
    bool success = true;

    var includeDirectories = new List<string> { Model.IncludeDirectoryPath };
    foreach (CAvailableLibraryViewModel library in AvailableLibraries.Where(l => l.IsReferenced))
    {
      includeDirectories.Add(Path.Combine(library.RootPath, CProject.IncludeDirectoryName));
    }

    var resolver = new FileSystemIncludeResolver(includeDirectories);
    var assemblySources = new List<string>();

    foreach (string sourceFile in Model.GetSourceFiles())
    {
      string relativeName = Path.GetRelativePath(Model.SourceDirectoryPath, sourceFile);
      CCompileResult result = CCompiler.Compile(sourceFile, File.ReadAllText(sourceFile), resolver);

      if (result.PreprocessedText is not null)
      {
        File.WriteAllText(Model.GetPreprocessedFilePath(sourceFile), result.PreprocessedText);
      }

      if (result.Success && result.Assembly is not null)
      {
        File.WriteAllText(Model.GetTargetAssemblyFilePath(sourceFile), result.Assembly);
        assemblySources.Add(Model.GetTargetAssemblyFilePath(sourceFile));
        messages.Add($"Compiled {relativeName}.");
      }
      else
      {
        success = false;
        messages.Add($"{relativeName}: compile failed.");
        messages.AddRange(result.Diagnostics);
      }
    }

    assemblySources.AddRange(Model.GetAssemblyFiles());

    var objectMembers = new List<CvmLibraryMember>();
    foreach (string assemblySource in assemblySources)
    {
      string relativeName = Path.GetRelativePath(Model.RootPath, assemblySource);
      (CvmObjectFile? objectFile, IReadOnlyList<string> errors) = CvmAssembler.Assemble(File.ReadAllText(assemblySource));

      if (objectFile is null)
      {
        success = false;
        messages.Add($"{relativeName}: assembly failed.");
        messages.AddRange(errors);
        continue;
      }

      string objectFilePath = Model.GetObjectFilePath(assemblySource);
      using (var stream = File.Create(objectFilePath))
      {
        objectFile.Save(stream);
      }

      objectMembers.Add(new CvmLibraryMember { Name = Path.GetFileName(objectFilePath), ObjectBytes = File.ReadAllBytes(objectFilePath) });
      messages.Add($"Assembled {relativeName}.");
    }

    if (IsLibrary)
    {
      string libraryPath = Model.LibraryOutputPath;
      if (success)
      {
        var library = new CvmLibrary();
        library.Members.AddRange(objectMembers);

        string tempPath = libraryPath + ".tmp";
        bool librarySuccess;
        IReadOnlyList<string> libraryErrors;
        using (var stream = File.Create(tempPath))
        {
          (librarySuccess, libraryErrors) = library.Save(stream);
        }

        if (!librarySuccess)
        {
          File.Delete(tempPath);
          success = false;
          messages.Add($"{Path.GetFileName(libraryPath)}: archiving failed.");
          messages.AddRange(libraryErrors);
        }
        else
        {
          File.Move(tempPath, libraryPath, overwrite: true);
          messages.Add($"Archived {objectMembers.Count} object file(s) into {Path.GetFileName(libraryPath)}.");
        }
      }
      else
      {
        messages.Add($"Skipped {Path.GetFileName(libraryPath)} -- the build has errors above.");
      }
    }
    else if (Model.ChipProjectId is not { } chipProjectId)
    {
      messages.Add("No chip project is chosen (see \"Chip project...\") to compile primitives from -- Build stops here, with no .gaimg produced.");
    }
    else if (!success)
    {
      messages.Add("Skipped linking -- the build has errors above.");
    }
    else
    {
      ChipProjectResolver.Result chipResult = _chipProjectResolver.Resolve(chipProjectId, Model.ChipProjectRole);
      if (!chipResult.Success || chipResult.Chip is null || chipResult.RomLibrary is null)
      {
        success = false;
        messages.Add($"Linking failed -- {chipResult.ErrorMessage}");
      }
      else
      {
        CvmPrimitiveTableExporter.Result exported = CvmPrimitiveTableExporter.Export(chipResult.Chip, chipResult.RomLibrary, chipResult.UserMacros);
        messages.AddRange(exported.Messages);

        if (!exported.Success)
        {
          success = false;
          messages.Add("Linking failed -- no primitive table could be exported from the chosen chip project.");
        }
        else
        {
          var libraryInputs = new List<CvmLinkLibraryInput>();
          foreach (CAvailableLibraryViewModel library in AvailableLibraries.Where(l => l.IsReferenced))
          {
            string libraryPath = _store.Load(library.RootPath).LibraryOutputPath;
            if (!File.Exists(libraryPath))
            {
              success = false;
              messages.Add($"Linking failed -- \"{library.Name}\" has no {Path.GetFileName(libraryPath)} yet; build \"{library.Name}\" first.");
              continue;
            }

            using var libraryStream = File.OpenRead(libraryPath);
            libraryInputs.Add(new CvmLinkLibraryInput(library.Name, CvmLibrary.Load(libraryStream)));
          }

          if (success)
          {
            var objectInputs = objectMembers
                .Select(member => new CvmLinkObjectInput(member.Name, CvmObjectFile.Load(new MemoryStream(member.ObjectBytes))))
                .ToList();

            CvmLinker.Result linkResult = CvmLinker.Link(objectInputs, libraryInputs, exported.Table, new CvmLinkOptions());
            messages.AddRange(linkResult.Messages);

            if (!linkResult.Success || linkResult.Image is null)
            {
              success = false;
            }
            else
            {
              string imagePath = Model.ImageOutputPath;
              string tempImagePath = imagePath + ".tmp";
              using (var imageStream = File.Create(tempImagePath))
              {
                linkResult.Image.Save(imageStream);
              }

              File.Move(tempImagePath, imagePath, overwrite: true);
              messages.Add($"Linked {Path.GetFileName(imagePath)}.");
            }
          }
        }
      }
    }

    StatusText = success ? "Build succeeded." : "Build failed -- see the build results for details.";
    return new CBuildResult { Success = success, Messages = messages };
  }

  private string AddExistingFile(string sourceFilePath, CFileKind kind)
  {
    string targetDirectory = TargetDirectoryFor(kind);
    Directory.CreateDirectory(targetDirectory);
    string fileName = Path.GetFileName(sourceFilePath);
    string destination = Path.Combine(targetDirectory, fileName);
    if (File.Exists(destination))
    {
      throw new InvalidOperationException($"\"{fileName}\" already exists in this project's {DirectoryLabelFor(kind)} folder.");
    }

    File.Copy(sourceFilePath, destination);
    Refresh();
    StatusText = $"Added {fileName}.";
    return destination;
  }

  private string CreateNewFile(string fileName, CFileKind kind)
  {
    if (string.IsNullOrWhiteSpace(fileName))
    {
      throw new InvalidOperationException("Enter a file name.");
    }

    string expectedExtension = ExpectedExtensionFor(kind);
    fileName = fileName.Trim();
    if (!fileName.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
    {
      fileName += expectedExtension;
    }

    string targetDirectory = TargetDirectoryFor(kind);
    string destination = Path.GetFullPath(Path.Combine(targetDirectory, fileName));
    if (!destination.StartsWith(Path.GetFullPath(targetDirectory), StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException("The file name must stay inside the project's own folder.");
    }

    if (File.Exists(destination))
    {
      throw new InvalidOperationException($"\"{fileName}\" already exists.");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllText(destination, DefaultContentFor(kind, fileName));
    Refresh();
    StatusText = $"Created {fileName}.";
    return destination;
  }

  private void RemoveFile(string baseDirectory, string displayPath)
  {
    string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, displayPath));
    if (File.Exists(fullPath))
    {
      File.Delete(fullPath);
    }

    Refresh();
    StatusText = $"Removed {displayPath}.";
  }

  private void Save() => _store.Save(Model);

  private string TargetDirectoryFor(CFileKind kind) => kind switch
  {
    CFileKind.Header => Model.IncludeDirectoryPath,
    CFileKind.Source => Model.SourceDirectoryPath,
    CFileKind.Assembly => Model.AssemblyDirectoryPath,
    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
  };

  private static string ExpectedExtensionFor(CFileKind kind) => kind switch
  {
    CFileKind.Header => ".h",
    CFileKind.Source => ".c",
    CFileKind.Assembly => ".casm",
    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
  };

  private static string DirectoryLabelFor(CFileKind kind) => kind switch
  {
    CFileKind.Header => CProject.IncludeDirectoryName,
    CFileKind.Source => CProject.SourceDirectoryName,
    CFileKind.Assembly => CProject.AssemblyDirectoryName,
    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
  };

  private static string DefaultContentFor(CFileKind kind, string fileName) => kind switch
  {
    CFileKind.Header => DefaultHeaderContent(fileName),
    CFileKind.Source => DefaultSourceContent(fileName),
    CFileKind.Assembly => DefaultAssemblyContent(fileName),
    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
  };

  private static IEnumerable<string> ToDisplayPaths(string baseDirectory, IReadOnlyList<string> fullPaths) =>
      fullPaths.Select(path => Path.GetRelativePath(baseDirectory, path));

  private static void ReplaceAll(ObservableCollection<string> collection, IEnumerable<string> items)
  {
    collection.Clear();
    foreach (string item in items)
    {
      collection.Add(item);
    }
  }

  private static string DefaultHeaderContent(string fileName)
  {
    string guard = ToHeaderGuard(fileName);
    return $"#ifndef {guard}{Environment.NewLine}#define {guard}{Environment.NewLine}{Environment.NewLine}#endif{Environment.NewLine}";
  }

  private static string DefaultSourceContent(string fileName) => $"// {fileName}{Environment.NewLine}";

  private static string DefaultAssemblyContent(string fileName) => $"; {fileName}{Environment.NewLine}";

  private static string ToHeaderGuard(string fileName)
  {
    var builder = new StringBuilder();
    foreach (char c in Path.GetFileNameWithoutExtension(fileName))
    {
      builder.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
    }

    builder.Append("_H");
    return builder.ToString();
  }
}

/// <summary>The outcome of one <see cref="CProjectViewModel.Build"/> run: whether the whole build
/// succeeded, and a message per step -- one line for each file compiled, assembled, or archived, plus
/// every diagnostic from any step that failed, in the order the steps ran (compile, then assemble,
/// then -- Library projects only -- archive).</summary>
public sealed class CBuildResult
{
  public required bool Success { get; init; }
  public required IReadOnlyList<string> Messages { get; init; }
}

/// <summary>
/// One row in a <c>CProjectWindow</c>'s "Imported libraries" checkbox list: one C project found under
/// the shared "libs" directory, with a mutable <see cref="IsReferenced"/> that adds/removes it from
/// the owning project's own <see cref="CProject.LibraryReferences"/> as soon as it is toggled. Added
/// 2026-09-08, replacing the former <c>CLibraryReferenceViewModel</c> (a read-only resolved snapshot
/// of an already-added reference) entirely, per Stefan's "Replace it entirely" answer.
/// </summary>
public sealed class CAvailableLibraryViewModel
{
  private readonly CProjectViewModel _owner;
  private bool _isReferenced;

  internal CAvailableLibraryViewModel(CProjectViewModel owner, string name, string rootPath, bool isReferenced)
  {
    _owner = owner;
    Name = name;
    RootPath = rootPath;
    _isReferenced = isReferenced;
  }

  public string Name { get; }

  public string RootPath { get; }

  public bool IsReferenced
  {
    get => _isReferenced;
    set
    {
      if (_isReferenced == value)
      {
        return;
      }

      _isReferenced = value;
      _owner.SetLibraryReferenced(this, value);
    }
  }
}