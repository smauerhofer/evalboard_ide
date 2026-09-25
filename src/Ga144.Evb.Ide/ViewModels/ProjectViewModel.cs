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
  /// One of the 48 <see cref="NodeColorPalette"/> swatches, applied to every configured node in
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
      OnPropertyChanged(nameof(SelectedColorOption));
      _changed();
    }
  }

  /// <summary>The 48 fixed swatches MainWindow's "Default node color" picker offers.</summary>
  public IReadOnlyList<NodeColorOption> DefaultNodeColorOptions => NodeColorOption.Palette;

  /// <summary>ADDED 2026-09-25, per Stefan directly: "in the main window replace the color grid
  /// with the same pulldown menu" (as NodeEditorWindow's own per-node color dropdown). Backs the
  /// "Default node color" ComboBox's two-way SelectedItem binding (MainWindow.xaml): the getter
  /// finds the <see cref="DefaultNodeColorOptions"/> entry matching <see cref="DefaultNodeColor"/>
  /// so the dropdown's own closed-state display always shows the current default as its selected
  /// item, and the setter writes a newly picked entry back through <see cref="DefaultNodeColor"/>.
  /// </summary>
  public NodeColorOption? SelectedColorOption
  {
    get => DefaultNodeColorOptions.FirstOrDefault(option => string.Equals(option.Hex, DefaultNodeColor, StringComparison.OrdinalIgnoreCase));
    set
    {
      if (value is not null)
      {
        DefaultNodeColor = value.Hex;
      }
    }
  }

  public Ga144ChipConfiguration GetChip(Ga144ChipRole role) => Model.GetChip(role);

  public void NotifyProjectChanged()
  {
    _changed();
  }
}