using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// Backs the "New C project" dialog: just a name and a kind. The parent folder is no longer a
/// free-form field (removed 2026-09-08, per Stefan: "Remove it" -- new C projects always live under
/// the shared C workspace's "libs" or "prgs" folder, picked by <see cref="Kind"/>) -- the dialog only
/// previews where the project will land.
/// </summary>
public sealed class NewCProjectViewModel : ObservableObject
{
  private readonly string _libsRootPath;
  private readonly string _prgsRootPath;
  private string _name = "CProject1";
  private bool _isLibraryKind;

  public NewCProjectViewModel(string libsRootPath, string prgsRootPath)
  {
    _libsRootPath = libsRootPath;
    _prgsRootPath = prgsRootPath;
  }

  public string Name
  {
    get => _name;
    set
    {
      if (SetProperty(ref _name, value))
      {
        OnPropertyChanged(nameof(ResolvedRootPath));
      }
    }
  }

  public bool IsProgramKind
  {
    get => !_isLibraryKind;
    set
    {
      if (value && SetProperty(ref _isLibraryKind, false, nameof(IsLibraryKind)))
      {
        OnPropertyChanged(nameof(IsProgramKind));
        OnPropertyChanged(nameof(ResolvedRootPath));
      }
    }
  }

  public bool IsLibraryKind
  {
    get => _isLibraryKind;
    set
    {
      if (value && SetProperty(ref _isLibraryKind, true))
      {
        OnPropertyChanged(nameof(IsProgramKind));
        OnPropertyChanged(nameof(ResolvedRootPath));
      }
    }
  }

  public CProjectKind Kind => _isLibraryKind ? CProjectKind.Library : CProjectKind.Program;

  public string ResolvedRootPath => string.IsNullOrWhiteSpace(Name)
      ? string.Empty
      : Path.Combine(_isLibraryKind ? _libsRootPath : _prgsRootPath, Name.Trim());
}