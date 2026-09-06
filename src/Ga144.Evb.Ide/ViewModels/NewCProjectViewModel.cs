using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>Backs the "New C project" dialog: a name, a kind, and the parent folder the new project's own folder is created under.</summary>
public sealed class NewCProjectViewModel : ObservableObject
{
  private string _name = "CProject1";
  private string _location;
  private bool _isLibraryKind;

  public NewCProjectViewModel(string defaultLocation)
  {
    _location = defaultLocation;
  }

  public string Name
  {
    get => _name;
    set => SetProperty(ref _name, value);
  }

  public string Location
  {
    get => _location;
    set => SetProperty(ref _location, value);
  }

  public bool IsProgramKind
  {
    get => !_isLibraryKind;
    set
    {
      if (value && SetProperty(ref _isLibraryKind, false, nameof(IsLibraryKind)))
      {
        OnPropertyChanged(nameof(IsProgramKind));
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
      }
    }
  }

  public CProjectKind Kind => _isLibraryKind ? CProjectKind.Library : CProjectKind.Program;

  public string ResolvedRootPath => string.IsNullOrWhiteSpace(Location) || string.IsNullOrWhiteSpace(Name)
      ? string.Empty
      : Path.Combine(Location.Trim(), Name.Trim());
}
