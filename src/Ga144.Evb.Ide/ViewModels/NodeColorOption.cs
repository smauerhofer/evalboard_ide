using System.Windows.Media;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// One swatch in a node-color picker (MainWindow's project "Default node color" picker, and
/// NodeEditorWindow's own per-node "Node color" picker) -- pairs a <see cref="NodeColorPalette"/>
/// hex value with the pre-converted <see cref="Brush"/> the swatch paints itself with, so the
/// XAML for either picker never needs its own hex-to-brush value converter. Also carries
/// <see cref="Number"/>, the color's fixed 2-digit hex position in <see cref="NodeColorPalette.Colors"/>
/// (Stefan's own request, 2026-09-25: "enumerate the colors and give each color a 2 digit hex
/// number... display the number wherever the color button is displayed"), so every swatch --
/// and the single "current color" swatch each picker shows next to its grid -- can label itself
/// with that number without any picker needing its own index-formatting logic.
/// </summary>
public sealed class NodeColorOption
{
  public NodeColorOption(string hex, int index)
  {
    Hex = hex;
    Brush = BrushFromHex(hex);
    Number = index.ToString("X2");
  }

  public string Hex { get; }
  public Brush Brush { get; }

  /// <summary>This color's fixed position in <see cref="NodeColorPalette.Colors"/>, as an
  /// uppercase 2-digit hex string ("00".."2F" for the current 48-color palette) -- stamped on
  /// this swatch everywhere it's shown. Stable across a session (see NodeColorPalette's own
  /// remarks on why the list's order must not change), but NOT persisted anywhere itself: saved
  /// project/node data still stores and compares colors by <see cref="Hex"/> alone, so this
  /// number is purely a display label, recomputed fresh from list position every time.</summary>
  public string Number { get; }

  /// <summary>The fixed 48-swatch palette, in <see cref="NodeColorPalette.Colors"/> order, shared
  /// by every picker in the app.</summary>
  public static IReadOnlyList<NodeColorOption> Palette { get; } =
      NodeColorPalette.Colors.Select((hex, index) => new NodeColorOption(hex, index)).ToList();

  /// <summary>The 2-digit hex <see cref="Number"/> of the <see cref="Palette"/> entry matching
  /// <paramref name="hex"/> (case-insensitive), or "--" if <paramref name="hex"/> isn't one of
  /// the 48 palette colors -- used to label a "current color" swatch that isn't itself one of
  /// this picker's own DataTemplate-bound <see cref="NodeColorOption"/> instances.</summary>
  public static string NumberFor(string? hex) =>
      Palette.FirstOrDefault(option => string.Equals(option.Hex, hex, StringComparison.OrdinalIgnoreCase))
          ?.Number ?? "--";

  /// <summary>
  /// Converts any hex color string to a frozen <see cref="Brush"/> -- used for a node's
  /// effective background (always one of the 48 palette colors) and for its darkened border
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