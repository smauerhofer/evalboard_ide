using System.Windows.Media;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// One swatch in a node-color picker (MainWindow's project "Default node color" picker, and
/// NodeEditorWindow's own per-node "Node color" picker) -- pairs a <see cref="NodeColorPalette"/>
/// hex value with the pre-converted <see cref="Brush"/> the swatch paints itself with, so the
/// XAML for either picker never needs its own hex-to-brush value converter.
/// </summary>
public sealed class NodeColorOption
{
  public NodeColorOption(string hex)
  {
    Hex = hex;
    Brush = BrushFromHex(hex);
  }

  public string Hex { get; }
  public Brush Brush { get; }

  /// <summary>The fixed 16-swatch palette, in <see cref="NodeColorPalette.Colors"/> order, shared
  /// by every picker in the app.</summary>
  public static IReadOnlyList<NodeColorOption> Palette { get; } =
      NodeColorPalette.Colors.Select(hex => new NodeColorOption(hex)).ToList();

  /// <summary>
  /// Converts any hex color string to a frozen <see cref="Brush"/> -- used for a node's
  /// effective background (always one of the 16 palette colors) and for its darkened border
  /// accent (<see cref="NodeColorPalette.Darken"/>'s result is deliberately NOT itself a
  /// palette color, so it is converted here directly rather than looked up in
  /// <see cref="Palette"/>).
  /// </summary>
  public static Brush BrushFromHex(string? hex)
  {
    if (!string.IsNullOrWhiteSpace(hex))
    {
      try
      {
        if (ColorConverter.ConvertFromString(hex) is Color color)
        {
          var brush = new SolidColorBrush(color);
          brush.Freeze();
          return brush;
        }
      }
      catch (FormatException)
      {
        // Falls through to the neutral fallback below.
      }
    }

    return Palette[0].Brush;
  }
}
