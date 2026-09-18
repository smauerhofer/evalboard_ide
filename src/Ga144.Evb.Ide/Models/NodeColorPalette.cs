namespace Ga144.Evb.Ide.Models;

/// <summary>
/// Fixed set of 16 pale colors offered both for a project's default node color (MainWindow's
/// "Active project" panel) and for an individual node's own color override (NodeEditorWindow's
/// "Node color" picker) -- the same 16 swatches back both pickers, so a node's color and a
/// project's default color are always drawn from one consistent, readable set. Index 0 is the
/// pale yellow the GA144 chip window has always used for "configured" nodes (ChipWindow.xaml's
/// old hardcoded #FFF1C7/#B57A16 DataTrigger, now computed from this palette instead -- see
/// NodeViewModel), kept first so an existing project's workspace file (with no defaultNodeColor
/// yet) renders exactly as it always has.
/// </summary>
public static class NodeColorPalette
{
  public const string DefaultColor = "#FFF1C7";

  public static readonly IReadOnlyList<string> Colors =
  [
      DefaultColor, // Pale Yellow (the original, and every project's fallback)
      "#FFDDC7",    // Pale Peach
      "#FFD6D6",    // Pale Rose
      "#FBD6EC",    // Pale Pink
      "#E6D6F7",    // Pale Purple
      "#D6DFF7",    // Pale Periwinkle
      "#D6ECF7",    // Pale Sky Blue
      "#D6F5F0",    // Pale Teal
      "#D6F7DC",    // Pale Mint
      "#E3F7D6",    // Pale Lime
      "#F5F7D6",    // Pale Cream
      "#F0E6D6",    // Pale Sand
      "#E8E8E8",    // Pale Gray
      "#D6EFEA",    // Pale Seafoam
      "#E0D6EF",    // Pale Lavender
      "#F7E0EA",    // Pale Blush
  ];

  /// <summary>True only for one of the 16 fixed swatches above (case-insensitive). Used to
  /// validate a project's DefaultNodeColor on load -- it must always be a real palette entry,
  /// since MainWindow's picker only ever offers these 16.</summary>
  public static bool IsPaletteColor(string? hex) =>
      !string.IsNullOrWhiteSpace(hex) &&
      Colors.Any(color => string.Equals(color, hex, StringComparison.OrdinalIgnoreCase));

  /// <summary>
  /// A darker accent shade of <paramref name="hex"/> for a node's border -- computed the same
  /// way (a uniform 55% RGB scale toward black) for every swatch rather than hand-picking 16
  /// border colors, so it generalizes to any hex, including one that did not come from this
  /// palette. Close enough to the original hand-picked pale-yellow/brown pairing (#FFF1C7 with
  /// border #B57A16) to read the same way.
  /// </summary>
  public static string Darken(string hex, double factor = 0.55)
  {
    (byte r, byte g, byte b) = ParseRgb(hex);
    byte dr = (byte)Math.Clamp(r * factor, 0, 255);
    byte dg = (byte)Math.Clamp(g * factor, 0, 255);
    byte db = (byte)Math.Clamp(b * factor, 0, 255);
    return $"#{dr:X2}{dg:X2}{db:X2}";
  }

  private static (byte r, byte g, byte b) ParseRgb(string hex)
  {
    string clean = hex.TrimStart('#');
    if (clean.Length == 8)
    {
      // "#AARRGGBB" -- drop the alpha; every color here is fully opaque.
      clean = clean[2..];
    }

    if (clean.Length == 6
        && byte.TryParse(clean[..2], System.Globalization.NumberStyles.HexNumber, null, out byte r)
        && byte.TryParse(clean[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte g)
        && byte.TryParse(clean[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte b))
    {
      return (r, g, b);
    }

    // Falls back to the chip window's own long-standing neutral node background (#F7FBF8) if
    // handed something unparseable, rather than throwing while rendering the chip grid.
    return (0xF7, 0xFB, 0xF8);
  }
}
