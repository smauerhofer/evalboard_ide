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

  /// <summary>
  /// One of the 16 <see cref="NodeColorPalette"/> swatches, applied to every configured node in
  /// this project that has no color of its own -- see MainWindow's "Active project" panel for
  /// the picker, and <see cref="NodeViewModel.EffectiveColorHex"/> for where the two combine.
  /// </summary>
  public string DefaultNodeColor
  {
    get => Model.DefaultNodeColor;
    set
    {
      string normalized = NodeColorPalette.IsPaletteColor(value) ? value : NodeColorPalette.DefaultColor;
      if (string.Equals(Model.DefaultNodeColor, normalized, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      Model.DefaultNodeColor = normalized;
      OnPropertyChanged();
      _changed();
    }
  }

  /// <summary>The 16 fixed swatches MainWindow's "Default node color" picker offers.</summary>
  public IReadOnlyList<NodeColorOption> DefaultNodeColorOptions => NodeColorOption.Palette;

  public Ga144ChipConfiguration GetChip(Ga144ChipRole role) => Model.GetChip(role);

  public void NotifyProjectChanged()
  {
    _changed();
  }
}