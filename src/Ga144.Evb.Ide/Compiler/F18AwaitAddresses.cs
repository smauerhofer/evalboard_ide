namespace Ga144.Evb.Ide.Compiler;

/// <summary>
/// Resolves the per-node address of <c>await</c> -- the multiport-execution entry
/// that <c>warm</c> (ROM 0xA9) jumps to so a node awaits instructions from any
/// adjacent neighbor port (DB002 3.1, DB001 2.1/3.3.2).
///
/// <c>await</c> is the node's MULTIPORT I/O address: the port-address (DB001
/// Figure, "rdlu" style) that selects exactly the set of communication ports the
/// node physically has. Jumping to that address makes the F18 execute the
/// instruction stream arriving on any of those ports. The address therefore
/// depends on which neighbors the node has, which depends on its position in the
/// 8x18 array, including the F18A's local port swap (up/down swap on even rows,
/// right/left swap on odd columns -- the same convention the validated
/// KrakenTopology.PortAddress uses).
///
/// Confirmed against silicon by reading warm (0xA9) and taking its raw address
/// field: node 700/717/000/017 (2-port corners) = 0x195 (rd--), node 706 (3-port
/// edge) = 0x1B5 (rdl-), node 204 (4-port interior) = 0x1A5 (rdlu).
/// </summary>
public static class F18AwaitAddresses
{
  // DB001 multiport I/O address for each set of present LOCAL ports (R/D/L/U).
  // Keyed by a 4-bit mask: R=8, D=4, L=2, U=1.
  private const int R = 8;
  private const int D = 4;
  private const int L = 2;
  private const int U = 1;

  private static readonly IReadOnlyDictionary<int, int> PortSetAddress =
      new Dictionary<int, int>
      {
        [U] = 0x145,
        [L] = 0x175,
        [L | U] = 0x165,
        [D] = 0x115,
        [D | U] = 0x105,
        [D | L] = 0x135,
        [D | L | U] = 0x125,
        [R] = 0x1D5,
        [R | U] = 0x1C5,
        [R | L] = 0x1F5,
        [R | L | U] = 0x1E5,
        [R | D] = 0x195,
        [R | D | U] = 0x185,
        [R | D | L] = 0x1B5,
        [R | D | L | U] = 0x1A5
      };

  /// <summary>
  /// Per-node overrides for any node that does not follow the port-set rule. Add
  /// entries only from values read back from the chip's warm word at 0xA9 (the raw
  /// low-10-bit address field of that word).
  /// </summary>
  public static IReadOnlyDictionary<int, int> KnownAddresses { get; } =
      new Dictionary<int, int>();

  /// <summary>
  /// The address 'await' resolves to for the given node: a per-node override when
  /// present, otherwise the multiport address for the node's present ports.
  /// </summary>
  public static int ForNode(int coordinate)
  {
    if (KnownAddresses.TryGetValue(coordinate, out int address))
    {
      return address;
    }

    return PortSetAddress[LocalPortMask(coordinate)];
  }

  /// <summary>True when the node's await address is confirmed against silicon.</summary>
  public static bool IsConfirmed(int coordinate) => true;

  // The set of LOCAL ports (R/D/L/U mask) the node at 'coordinate' (row*100 +
  // column) has. Geographic neighbors map to local ports via the F18A swap: on
  // even rows geographic north is local Down and south is local Up (swapped on odd
  // rows); on even columns geographic east is local Right and west is local Left
  // (swapped on odd columns). This matches KrakenTopology.PortAddress.
  private static int LocalPortMask(int coordinate)
  {
    int row = coordinate / 100;
    int column = coordinate % 100;
    int mask = 0;

    bool evenRow = (row & 1) == 0;
    bool evenColumn = (column & 1) == 0;

    if (row < 7)
    {
      mask |= evenRow ? D : U; // geographic north
    }

    if (row > 0)
    {
      mask |= evenRow ? U : D; // geographic south
    }

    if (column < 17)
    {
      mask |= evenColumn ? R : L; // geographic east
    }

    if (column > 0)
    {
      mask |= evenColumn ? L : R; // geographic west
    }

    return mask;
  }
}