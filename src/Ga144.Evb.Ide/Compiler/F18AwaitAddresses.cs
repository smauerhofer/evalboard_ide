namespace Ga144.Evb.Ide.Compiler;

/// <summary>
/// Resolves the per-node address of <c>await</c> -- the multiport-execution entry
/// that <c>warm</c> (ROM 0xA9) jumps to so a node awaits instructions from any
/// adjacent neighbor port (DB002 3.1, DB001 2.1/3.3.2).
///
/// Empirically (from reading each node's warm word at 0xA9 off silicon), the
/// address is keyed on how many communication ports the node has, i.e. its
/// position class:
///   * 2 ports (corner) -> 0x0C0  (confirmed: nodes 700, 717, 000, 017)
///   * 4 ports (interior) -> 0x0F0 (confirmed: node 204)
///   * 3 ports (edge) -> NOT YET CONFIRMED; see <see cref="EdgeAddress"/>.
/// The GA144 is an 8x18 array (rows 0..7, columns 0..17); a node has a Right
/// neighbor when column &lt; 17, Left when column &gt; 0, Up when row &lt; 7, and
/// Down when row &gt; 0, so interior nodes have four ports, edge nodes three, and
/// the four corners two.
///
/// Per-node overrides in <see cref="KnownAddresses"/> take precedence over the
/// port-count rule, for any node that turns out not to follow it.
/// </summary>
public static class F18AwaitAddresses
{
  /// <summary>Confirmed await address for a 2-port corner node.</summary>
  public const int CornerAddress = 0x0C0;

  /// <summary>Confirmed await address for a 4-port interior node.</summary>
  public const int InteriorAddress = 0x0F0;

  /// <summary>
  /// await address for a 3-port edge node. NOT YET CONFIRMED against silicon --
  /// provisional. Read warm (0xA9) from a top/bottom/left/right edge node and set
  /// this to the decoded jump target. Until then edge nodes will not match the
  /// chip's warm word.
  /// </summary>
  public const int EdgeAddress = 0x0D8;

  /// <summary>
  /// Per-node overrides for any node that does not follow the port-count rule.
  /// Add entries only from values read back from the chip's warm word at 0xA9.
  /// </summary>
  public static IReadOnlyDictionary<int, int> KnownAddresses { get; } =
      new Dictionary<int, int>();

  /// <summary>
  /// The address 'await' resolves to for the given node: a per-node override when
  /// present, otherwise the port-count class address. Never throws.
  /// </summary>
  public static int ForNode(int coordinate)
  {
    if (KnownAddresses.TryGetValue(coordinate, out int address))
    {
      return address;
    }

    return PortCount(coordinate) switch
    {
      2 => CornerAddress,
      4 => InteriorAddress,
      _ => EdgeAddress
    };
  }

  /// <summary>True when the node's await address is confirmed against silicon.</summary>
  public static bool IsConfirmed(int coordinate)
  {
    if (KnownAddresses.ContainsKey(coordinate))
    {
      return true;
    }

    // Corner (2 ports) and interior (4 ports) classes are confirmed; the 3-port
    // edge class is not yet.
    return PortCount(coordinate) != 3;
  }

  // Number of communication ports the node at 'coordinate' (row*100 + column) has,
  // given the 8x18 array geometry.
  private static int PortCount(int coordinate)
  {
    int row = coordinate / 100;
    int column = coordinate % 100;
    int count = 0;
    if (row < 7)
    {
      count++;
    }

    if (row > 0)
    {
      count++;
    }

    if (column < 17)
    {
      count++;
    }

    if (column > 0)
    {
      count++;
    }

    return count;
  }
}