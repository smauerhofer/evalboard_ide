using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Models;

/// <summary>
/// Symbolic display names for a register or stack value that happens to equal a
/// known GA144 port or multiport address -- the same convention arrayForth's own
/// debugger uses (DB013 SS7.1.4): a value that matches one of the 19 documented
/// port addresses (4 single-direction "up"/"left"/"down"/"right", plus the 15
/// combined multiport addresses DB013 SS4.2.7.3 names "---u"/"--l-"/.../"rdlu") is
/// shown by that name instead of as a bare hex literal, since a register or stack
/// slot holding a port address is a common, meaningful state to recognize at a
/// glance -- not a claim that the value is "unused" or a sentinel. This is purely
/// a display convenience: nothing here changes what value is actually stored.
/// </summary>
public static class PortAddressNames
{
  private static readonly IReadOnlyDictionary<int, string> ByAddress = BuildReverseMap();

  /// <summary>The canonical display name for a known port/multiport address, or
  /// null when <paramref name="value"/> does not match any of the 19 documented
  /// addresses.</summary>
  public static string? TryGetName(int value) =>
      ByAddress.TryGetValue(value, out string? name) ? name : null;

  /// <summary>True when <paramref name="value"/> is one of the 19 documented port/multiport addresses.</summary>
  public static bool IsPortAddress(int value) => ByAddress.ContainsKey(value);

  /// <summary>
  /// Formats <paramref name="value"/> as a zero-padded hex literal with no "0x" prefix
  /// -- 5 digits by default, matching the 18-bit width of a full register/stack word
  /// (e.g. "00175") -- followed by its symbolic port name in parentheses when known
  /// (e.g. "00175 (left)"). Showing the hex value always, so nothing is hidden behind
  /// the translated name, with the name appended only where the value is recognized.
  /// Pass <paramref name="hexDigits"/> = 3 for a value known to be a 10-bit address
  /// instead of a full word.
  /// </summary>
  public static string Format(int value, int hexDigits = 5)
  {
    string hex = value.ToString("X" + hexDigits);
    return TryGetName(value) is { } name ? $"{hex} ({name})" : hex;
  }

  private static IReadOnlyDictionary<int, string> BuildReverseMap()
  {
    var map = new Dictionary<int, string>();

    // The 15 documented multiport addresses (DB013 SS4.2.7.3), using the canonical
    // dash-padded spelling as the display name -- skip the shorter convenience
    // aliases F18InstructionSet also defines for the same 15 values.
    //
    // BUG FIX 2026-09-30, per Stefan directly ("jump 0x1A5 (rlu) in the disassembly is wrong. it
    // should be jump 0x1A5 (rdlu)"): this used to test `entry.Key.Contains('-')` to tell a canonical
    // name from a convenience alias, which is wrong for exactly one of the 15 -- "rdlu" -- since it
    // has ALL FOUR directions present and so, unlike its 14 siblings, needs no dash to mark an
    // absent one. That excluded "rdlu" here entirely (leaving 0x1A5 unmapped by this loop), and, in
    // F18Disassembler's own separate but related bug fixed the same day, let the address-keyed
    // symbol map fall back to "rlu" (0x1E5's own alias, wrongly collapsed onto 0x1A5) instead. Every
    // one of the 15 canonical spellings is dash-PADDED to exactly 4 characters (one slot per
    // direction, "-" marking an absent one -- "---u", "--l-", ..., "rdlu"), while every one of the 10
    // convenience aliases is shorter (2-3 characters, the absent-direction slots dropped rather than
    // dashed) -- so length, not dash content, is what actually tells the two apart.
    foreach (KeyValuePair<string, int> entry in F18InstructionSet.NamedMultiportCalls)
    {
      if (entry.Key.Length == 4 && !map.ContainsKey(entry.Value))
      {
        map[entry.Value] = entry.Key;
      }
    }

    // The 4 single-direction addresses are a subset of the 15 above (e.g. both
    // "---u" and "up" are 0x145) -- prefer the short cardinal name for those, it
    // reads better than its single-letter dashed spelling.
    map[0x145] = "up";
    map[0x175] = "left";
    map[0x115] = "down";
    map[0x1D5] = "right";

    return map;
  }
}