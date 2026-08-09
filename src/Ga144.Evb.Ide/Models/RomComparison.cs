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
  /// Compare generated ROM words against chip ROM words. Both lists are expected
  /// to start at <paramref name="baseAddress"/> (default 0x080) and to be 64 words
  /// long. If they differ in length, the overlap is compared and the shortfall is
  /// reported in <see cref="Coverage"/>. All words are masked to 18 bits so stray
  /// high bits never produce phantom mismatches.
  /// </summary>
  public static RomComparison Compare(
      int coordinate,
      IReadOnlyList<int> generated,
      IReadOnlyList<int> onChip,
      int baseAddress = RomBaseAddress)
  {
    ArgumentNullException.ThrowIfNull(generated);
    ArgumentNullException.ThrowIfNull(onChip);

    int compareCount = Math.Min(generated.Count, onChip.Count);
    var mismatches = new List<RomWordMismatch>();
    for (int index = 0; index < compareCount; index++)
    {
      int g = generated[index] & WordMask;
      int c = onChip[index] & WordMask;
      if (g != c)
      {
        mismatches.Add(new RomWordMismatch(baseAddress + index, g, c));
      }
    }

    string? coverage = null;
    if (generated.Count != onChip.Count)
    {
      coverage =
          $"Length mismatch: {generated.Count} generated word(s) vs {onChip.Count} read from chip. " +
          $"Only the first {compareCount} were compared.";
    }
    else if (generated.Count != RomWordCount)
    {
      coverage =
          $"Expected {RomWordCount} ROM words but both sides had {generated.Count}.";
    }

    return new RomComparison(coordinate, baseAddress, compareCount, mismatches, coverage);
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