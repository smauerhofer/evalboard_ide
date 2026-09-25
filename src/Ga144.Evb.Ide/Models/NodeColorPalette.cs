namespace Ga144.Evb.Ide.Models;

/// <summary>
/// Fixed set of 48 colors offered both for a project's default node color (MainWindow's
/// "Active project" panel) and for an individual node's own color override (NodeEditorWindow's
/// "Node color" picker) -- the same 48 swatches back both pickers, so a node's color and a
/// project's default color are always drawn from one consistent, readable set. The first 24
/// (index 0x00-0x17) are the original pale band; the next 24 (index 0x18-0x2F, Stefan's own
/// request 2026-09-25: "add another 24 non-pale light colors to the existing 24 colors") are a
/// second, more saturated "light" band -- clearly more colorful than the pale band without
/// tipping into dark. Each entry's position in this list IS now meaningful (see
/// <see cref="NodeColorOption.Number"/>): every picker shows a color's fixed 2-digit hex index
/// stamped on its swatch, so a color can be named/referenced by that number instead of only by
/// its hex string or a hand-picked name.
///
/// Within each band, colors are listed in hue order (red through violet around the color
/// wheel), with the achromatic gray last since it has no hue to sort by, so each band of the
/// picker grid reads as a smooth rainbow instead of an arbitrary jumble. Unlike before this
/// numbering existed, this list's ORDER is no longer a free-to-reorder display convenience: a
/// color's index is now a stable identifier shown in the UI, so appending is safe but reordering
/// or removing an entry would silently renumber every swatch after it. A project's
/// DefaultNodeColor and a node's own Color are still compared/stored by hex string, never by
/// index (see IsPaletteColor/BrushFromHex), so this list is still safe to EXTEND without
/// touching any saved workspace file -- only reordering/removing existing entries is now
/// something to avoid.
/// </summary>
public static class NodeColorPalette
{
  public const string DefaultColor = "#FFF1C7";

  public static readonly IReadOnlyList<string> Colors =
  [
      // ---- Band 1: the original 24 pale swatches (index 0x00-0x17) ----
      "#FFD6D6",    // 00  Pale Rose
      "#FFCFC2",    // 01  Pale Coral
      "#FFDDC7",    // 02  Pale Peach
      "#F0E6D6",    // 03  Pale Sand
      DefaultColor, // 04  Pale Yellow (every project's fallback)
      "#F5E6B8",    // 05  Pale Gold
      "#F5F7D6",    // 06  Pale Cream
      "#E3E8C2",    // 07  Pale Olive
      "#E3F7D6",    // 08  Pale Lime
      "#D7E5D2",    // 09  Pale Sage
      "#D6F7DC",    // 0A  Pale Mint
      "#D6EFEA",    // 0B  Pale Seafoam
      "#D6F5F0",    // 0C  Pale Teal
      "#C9F0EA",    // 0D  Pale Aqua
      "#D6ECF7",    // 0E  Pale Sky Blue
      "#DCEEFA",    // 0F  Pale Ice Blue
      "#D6DFF7",    // 10  Pale Periwinkle
      "#E0D6EF",    // 11  Pale Lavender
      "#E6D6F7",    // 12  Pale Purple
      "#EAD6E8",    // 13  Pale Plum
      "#FBD6EC",    // 14  Pale Pink
      "#F7E0EA",    // 15  Pale Blush
      "#E8D2D8",    // 16  Pale Mauve
      "#E8E8E8",    // 17  Pale Gray (no hue -- listed last)

      // ---- Band 2: 24 new non-pale, light swatches (index 0x18-0x2F), added 2026-09-25 ----
      // Same 24-hue-step progression as the pale band above, but at S=60%/L=68% (HSL) instead
      // of the pale band's ~85-100% lightness -- clearly more saturated/colorful, while still
      // reading as a LIGHT color rather than a dark or muddy one.
      "#DE7C7C",    // 18  Light Red
      "#DE957C",    // 19  Light Vermilion
      "#DEAD7C",    // 1A  Light Orange
      "#DEC67C",    // 1B  Light Amber
      "#DEDE7C",    // 1C  Light Yellow
      "#C6DE7C",    // 1D  Light Lime
      "#ADDE7C",    // 1E  Light Chartreuse
      "#95DE7C",    // 1F  Light Pear
      "#7CDE7C",    // 20  Light Green
      "#7CDE95",    // 21  Light Emerald
      "#7CDEAD",    // 22  Light Spring Green
      "#7CDEC6",    // 23  Light Sea Green
      "#7CDEDE",    // 24  Light Teal
      "#7CC6DE",    // 25  Light Cyan
      "#7CADDE",    // 26  Light Sky Blue
      "#7C95DE",    // 27  Light Azure
      "#7C7CDE",    // 28  Light Blue
      "#957CDE",    // 29  Light Indigo
      "#AD7CDE",    // 2A  Light Violet
      "#C67CDE",    // 2B  Light Purple
      "#DE7CDE",    // 2C  Light Magenta
      "#DE7CC6",    // 2D  Light Fuchsia
      "#DE7CAD",    // 2E  Light Rose
      "#DE7C95",    // 2F  Light Crimson
  ];

  /// <summary>True only for one of the 48 fixed swatches above (case-insensitive). Used to
  /// validate a project's DefaultNodeColor on load -- it must always be a real palette entry,
  /// since MainWindow's picker only ever offers these 48.</summary>
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