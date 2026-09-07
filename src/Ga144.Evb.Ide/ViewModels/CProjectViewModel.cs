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
  private readonly CProjectStore _store;
  private string _statusText = "Ready";
  private string? _selectedHeaderFile;
  private string? _selectedSourceFile;
  private CLibraryReferenceViewModel? _selectedLibraryReference;

  public CProjectViewModel(CProject model, CProjectStore store)
  {
    Model = model;
    _store = store;
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
  public ObservableCollection<CLibraryReferenceViewModel> LibraryReferences { get; } = [];

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

  public CLibraryReferenceViewModel? SelectedLibraryReference
  {
    get => _selectedLibraryReference;
    set => SetProperty(ref _selectedLibraryReference, value);
  }

  public string StatusText
  {
    get => _statusText;
    private set => SetProperty(ref _statusText, value);
  }

  // Updated 2026-09-07 alongside Build() below: the C compiler and gaasm are both now actually
  // wired into this window's "Build" button (see Build()'s own remarks), so this text no longer
  // says "not implemented yet" for those two steps. galink is still a stub, so a Program project's
  // own final link genuinely still doesn't happen -- that part of the text is unchanged.
  public string BuildDescription => IsLibrary
      ? "Build: compiles each src/*.c file to CVM assembly, assembles it with gaasm, " +
        "and archives the resulting object files into this project's own .galib -- automatically, with no separate step."
      : "Build: compiles each src/*.c file to CVM assembly and assembles it with gaasm. " +
        "Linking against this project's imported libraries with galink to produce a .gaimg is not implemented yet -- galink is still a stub.";

  public void Refresh()
  {
    ReplaceAll(HeaderFiles, ToDisplayPaths(Model.IncludeDirectoryPath, Model.GetHeaderFiles()));
    ReplaceAll(SourceFiles, ToDisplayPaths(Model.SourceDirectoryPath, Model.GetSourceFiles()));
    RefreshLibraryReferences();
  }

  private void RefreshLibraryReferences()
  {
    LibraryReferences.Clear();
    foreach (string stored in Model.LibraryReferences)
    {
      LibraryReferences.Add(CLibraryReferenceViewModel.Resolve(Model, stored, _store));
    }
  }

  public string AddExistingHeaderFile(string sourceFilePath) => AddExistingFile(sourceFilePath, isHeader: true);

  public string AddExistingSourceFile(string sourceFilePath) => AddExistingFile(sourceFilePath, isHeader: false);

  public string CreateNewHeaderFile(string fileName) => CreateNewFile(fileName, isHeader: true);

  public string CreateNewSourceFile(string fileName) => CreateNewFile(fileName, isHeader: false);

  public void RemoveHeaderFile(string displayPath) => RemoveFile(Model.IncludeDirectoryPath, displayPath);

  public void RemoveSourceFile(string displayPath) => RemoveFile(Model.SourceDirectoryPath, displayPath);

  public string ResolveHeaderFilePath(string displayPath) => Path.Combine(Model.IncludeDirectoryPath, displayPath);

  public string ResolveSourceFilePath(string displayPath) => Path.Combine(Model.SourceDirectoryPath, displayPath);

  public void AddLibraryReference(string targetRootPath)
  {
    string fullTarget = Path.GetFullPath(targetRootPath);
    if (string.Equals(fullTarget, Model.RootPath, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException("A project cannot import itself.");
    }

    if (!_store.IsProjectFolder(fullTarget))
    {
      throw new InvalidOperationException($"\"{fullTarget}\" does not contain a C project ({CProject.ProjectFileName} was not found).");
    }

    CProject target = _store.Load(fullTarget);
    if (target.Kind != CProjectKind.Library)
    {
      throw new InvalidOperationException($"\"{target.Name}\" is a Program project. Only a Library project can be imported.");
    }

    string stored = Model.MakeLibraryReferencePath(fullTarget);
    bool alreadyImported = Model.LibraryReferences.Any(existing =>
        string.Equals(Model.ResolveLibraryReferencePath(existing), fullTarget, StringComparison.OrdinalIgnoreCase));
    if (alreadyImported)
    {
      throw new InvalidOperationException($"\"{target.Name}\" is already imported.");
    }

    Model.LibraryReferences.Add(stored);
    Save();
    RefreshLibraryReferences();
    StatusText = $"Imported library \"{target.Name}\".";
  }

  public void RemoveLibraryReference(CLibraryReferenceViewModel reference)
  {
    ArgumentNullException.ThrowIfNull(reference);
    Model.LibraryReferences.Remove(reference.StoredPath);
    Save();
    RefreshLibraryReferences();
    StatusText = $"Removed library reference \"{reference.DisplayName}\".";
  }

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
  /// earlier one fails, so one broken file never hides problems in the others. A Program project's
  /// own build stops at "compiled and assembled": <c>galink</c> is still a stub (see
  /// <see cref="BuildDescription"/>), so no ".gaimg" is produced by this method at all.
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
    foreach (CLibraryReferenceViewModel reference in LibraryReferences)
    {
      if (reference.IsValid)
      {
        includeDirectories.Add(_store.Load(reference.ResolvedFullPath).IncludeDirectoryPath);
      }
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
    else
    {
      messages.Add("Linking is not implemented yet (galink is still a stub), so no .gaimg was produced.");
    }

    StatusText = success ? "Build succeeded." : "Build failed -- see the build results for details.";
    return new CBuildResult { Success = success, Messages = messages };
  }

  private string AddExistingFile(string sourceFilePath, bool isHeader)
  {
    string targetDirectory = isHeader ? Model.IncludeDirectoryPath : Model.SourceDirectoryPath;
    Directory.CreateDirectory(targetDirectory);
    string fileName = Path.GetFileName(sourceFilePath);
    string destination = Path.Combine(targetDirectory, fileName);
    if (File.Exists(destination))
    {
      throw new InvalidOperationException(
          $"\"{fileName}\" already exists in this project's {(isHeader ? CProject.IncludeDirectoryName : CProject.SourceDirectoryName)} folder.");
    }

    File.Copy(sourceFilePath, destination);
    Refresh();
    StatusText = $"Added {fileName}.";
    return destination;
  }

  private string CreateNewFile(string fileName, bool isHeader)
  {
    if (string.IsNullOrWhiteSpace(fileName))
    {
      throw new InvalidOperationException("Enter a file name.");
    }

    string expectedExtension = isHeader ? ".h" : ".c";
    fileName = fileName.Trim();
    if (!fileName.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
    {
      fileName += expectedExtension;
    }

    string targetDirectory = isHeader ? Model.IncludeDirectoryPath : Model.SourceDirectoryPath;
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
    File.WriteAllText(destination, isHeader ? DefaultHeaderContent(fileName) : DefaultSourceContent(fileName));
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
/// A resolved snapshot of one of a <see cref="CProject"/>'s library imports, for display in a
/// <c>CProjectWindow</c>'s library list. Recomputed wholesale on every
/// <see cref="CProjectViewModel.Refresh"/> rather than kept live, since resolving one means loading
/// the target project's own metadata off disk.
/// </summary>
public sealed class CLibraryReferenceViewModel
{
  public required string StoredPath { get; init; }
  public required string ResolvedFullPath { get; init; }
  public required bool IsValid { get; init; }
  public required string DisplayName { get; init; }
  public required string StatusText { get; init; }

  public string Summary => IsValid ? DisplayName : $"{DisplayName} ({StatusText})";

  public static CLibraryReferenceViewModel Resolve(CProject owner, string storedPath, CProjectStore store)
  {
    string fullPath = owner.ResolveLibraryReferencePath(storedPath);
    if (!store.IsProjectFolder(fullPath))
    {
      return new CLibraryReferenceViewModel
      {
        StoredPath = storedPath,
        ResolvedFullPath = fullPath,
        IsValid = false,
        DisplayName = storedPath,
        StatusText = "Not found",
      };
    }

    try
    {
      CProject target = store.Load(fullPath);
      bool isLibrary = target.Kind == CProjectKind.Library;
      return new CLibraryReferenceViewModel
      {
        StoredPath = storedPath,
        ResolvedFullPath = fullPath,
        IsValid = isLibrary,
        DisplayName = target.Name,
        StatusText = isLibrary ? "OK" : "Not a Library project",
      };
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
    {
      return new CLibraryReferenceViewModel
      {
        StoredPath = storedPath,
        ResolvedFullPath = fullPath,
        IsValid = false,
        DisplayName = storedPath,
        StatusText = $"Error: {exception.Message}",
      };
    }
  }
}