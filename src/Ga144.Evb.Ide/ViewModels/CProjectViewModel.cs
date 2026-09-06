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

  // The C compiler (.c -> CVM assembly) does not exist yet, so Build has nothing to run today.
  // This describes the pipeline once it does, so the window is honest about what "Build" will
  // mean rather than pretending to compile anything: gaasm and (for libraries) the librarian
  // already exist (Ga144.Cvm.Toolchain); the linker is still a stub, so a Program project's own
  // final link is not possible yet either.
  public string BuildDescription => IsLibrary
      ? "Build: compile each src/*.c file to CVM assembly (not implemented yet), assemble it with gaasm, " +
        "and archive the resulting object files into this project's own .galib -- automatically, with no separate step."
      : "Build: compile each src/*.c file to CVM assembly (not implemented yet), assemble it with gaasm, " +
        "then link against this project's imported libraries with galink to produce a .gaimg. galink itself is " +
        "still a stub, so linking isn't possible yet either.";

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
