using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class ProjectViewModel : ObservableObject
{
  private readonly Action _changed;

  public ProjectViewModel(Ga144Project model, Action changed)
  {
    Model = model;
    _changed = changed;
    Model.Normalize();
  }

  public Ga144Project Model { get; }
  public Guid Id => Model.Id;

  public string Name
  {
    get => Model.Name;
    set
    {
      string normalized = string.IsNullOrWhiteSpace(value) ? "GA144 Project" : value.Trim();
      if (string.Equals(Model.Name, normalized, StringComparison.Ordinal))
      {
        return;
      }

      Model.Name = normalized;
      OnPropertyChanged();
      OnPropertyChanged(nameof(DisplayName));
      _changed();
    }
  }

  public string DisplayName => Name;

  public Ga144ChipConfiguration GetChip(Ga144ChipRole role) => Model.GetChip(role);

  public void NotifyProjectChanged()
  {
    _changed();
  }
}
