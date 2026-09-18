namespace Ga144.Evb.Ide.Models;

/// <summary>
/// Fixed set of 24 pale colors offered both for a project's default node color (MainWindow's
/// "Active project" panel) and for an individual node's own color override (NodeEditorWindow's
/// "Node color" picker) -- the same 24 swatches back both pickers, so a node's color and a
/// project's default color are always drawn from one consistent, readable set.
///
/// Listed in hue order (red through violet around the color wheel), with the achromatic Pale
/// Gray last since it has no hue to sort by, so the picker grids read as a smooth rainbow
/// instead of an arbitrary jumble -- this is purely a display-order convenience: nothing in the
/// app matches a color by its position in this list (a project's DefaultNodeColor and a node's
/// own Color are always compared/stored by hex string via IsPaletteColor/BrushFromHex), so this
/// list can be freely reordered later without touching any saved workspace file.
/// </summary>
public static class NodeColorPalette
{
  public const string DefaultColor = "#FFF1C7";

  public static readonly IReadOnlyList<string> Colors =
  [
      "#FFD6D6",    // Pale Rose
      "#FFCFC2",    // Pale Coral
      "#FFDDC7",    // Pale Peach
      "#F0E6D6",    // Pale Sand
      DefaultColor, // Pale Yellow (every project's fallback)
      "#F5E6B8",    // Pale Gold
      "#F5F7D6",    // Pale Cream
      "#E3E8C2",    // Pale Olive
      "#E3F7D6",    // Pale Lime
      "#D7E5D2",    // Pale Sage
      "#D6F7DC",    // Pale Mint
      "#D6EFEA",    // Pale Seafoam
      "#D6F5F0",    // Pale Teal
      "#C9F0EA",    // Pale Aqua
      "#D6ECF7",    // Pale Sky Blue
      "#DCEEFA",    // Pale Ice Blue
      "#D6DFF7",    // Pale Periwinkle
      "#E0D6EF",    // Pale Lavender
      "#E6D6F7",    // Pale Purple
      "#EAD6E8",    // Pale Plum
      "#FBD6EC",    // Pale Pink
      "#F7E0EA",    // Pale Blush
      "#E8D2D8",    // Pale Mauve
      "#E8E8E8",    // Pale Gray (no hue -- listed last)
  ];

  /// <summary>True only for one of the 24 fixed swatches above (case-insensitive). Used to
  /// validate a project's DefaultNodeColor on load -- it must always be a real palette entry,
  /// since MainWindow's picker only ever offers these 24.</summary>
  public static bool IsPaletteColor(string? hex) =>
      !string.IsNullOrWhiteSpace(hex) &&
      Colors.Any(color => string.Equals(color, hex, StringComparison.OrdinalIgnoreCase));

  /// <summary>
  /// A darker accent shade of <paramref name="hex"/> for a node's border -- computed the same
  /// way (a uniform 55% RGB scale toward black) for every swatch rather than hand-picking 24
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