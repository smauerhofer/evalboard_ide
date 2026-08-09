namespace Ga144.Evb.Ide.Models;

/// <summary>
/// One differing ROM address: the generated (compiled) word versus the word read
/// from the live chip via Kraken. Both are already masked to 18 bits.
/// </summary>
public sealed record RomWordMismatch(int Address, int Generated, int OnChip)
{
  public string AddressHex => $"0x{Address:X3}";
  public string GeneratedHex => $"0x{Generated:X5}";
  public string OnChipHex => $"0x{OnChip:X5}";
}

/// <summary>
/// Result of comparing a node's generated ROM against the ROM read from the chip.
/// A comparison is only meaningful when both sides cover the same address range;
/// <see cref="Coverage"/> records mismatches in length so a short compile or a
/// short read is reported explicitly rather than silently truncating.
/// </summary>
public sealed class RomComparison
{
  // F18A ROM is 64 words. The Kraken read and the compiler both address it from
  // 0x080, so a full comparison covers 0x080..0x0BF.
  public const int RomBaseAddress = 0x080;
  public const int RomWordCount = 64;
  private const int WordMask = Compiler.F18InstructionSet.WordMask; // 18 bits, shared with the compiler
  private const int EmptyWord = Compiler.F18InstructionSet.EncodingXor; // 0x15555 unwritten ROM word

  public int Coordinate { get; }
  public int BaseAddress { get; }
  public int ComparedWordCount { get; }
  public IReadOnlyList<RomWordMismatch> Mismatches { get; }

  /// <summary>Non-empty when the two sides could not be fully compared (length differs).</summary>
  public string? Coverage { get; }

  public bool IsMatch => Mismatches.Count == 0 && Coverage is null;

  private RomComparison(
      int coordinate,
      int baseAddress,
      int comparedWordCount,
      IReadOnlyList<RomWordMismatch> mismatches,
      string? coverage)
  {
    Coordinate = coordinate;
    BaseAddress = baseAddress;
    ComparedWordCount = comparedWordCount;
    Mismatches = mismatches;
    Coverage = coverage;
  }

  /// <summary>
  /// Compare generated ROM words against chip ROM words. Both are expected to be
  /// 64 words starting at <paramref name="baseAddress"/>. The comparison never
  /// treats a differing length as a failure by itself: ROM is always 64 physical
  /// words, and any word absent from either side is treated as the F18A empty-word
  /// value 0x15555 (the same value the compiler pre-fills and the chip reads back
  /// for unwritten ROM). All words are masked to 18 bits so stray high bits never
  /// produce phantom mismatches.
  /// </summary>
  public static RomComparison Compare(
      int coordinate,
      IReadOnlyList<int> generated,
      IReadOnlyList<int> onChip,
      int baseAddress = RomBaseAddress)
  {
    ArgumentNullException.ThrowIfNull(generated);
    ArgumentNullException.ThrowIfNull(onChip);

    // Always compare the full ROM window. A short list on either side is padded
    // with the empty-word value rather than reported as a length mismatch.
    int count = Math.Max(RomWordCount, Math.Max(generated.Count, onChip.Count));
    var mismatches = new List<RomWordMismatch>();
    for (int index = 0; index < count; index++)
    {
      int g = (index < generated.Count ? generated[index] : EmptyWord) & WordMask;
      int c = (index < onChip.Count ? onChip[index] : EmptyWord) & WordMask;
      if (g != c)
      {
        mismatches.Add(new RomWordMismatch(baseAddress + index, g, c));
      }
    }

    return new RomComparison(coordinate, baseAddress, count, mismatches, coverage: null);
  }

  /// <summary>A one-line human summary for logs and the sweep report.</summary>
  public string Summary()
  {
    if (IsMatch)
    {
      return $"Node {Coordinate:000}: ROM matches ({ComparedWordCount}/{RomWordCount} words).";
    }

    string counts = $"Node {Coordinate:000}: {Mismatches.Count} mismatch(es) in {ComparedWordCount} compared word(s)";
    return Coverage is null ? counts + "." : counts + $". {Coverage}";
  }
}