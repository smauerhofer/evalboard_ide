namespace Ga144.Evb.Ide.Models;

/// <summary>
/// The fixed Kraken structure for one GA144: head node 708 plus three tentacles
/// of adjacent F18A nodes. This is a constant of the array and the boot protocol,
/// not per-project configuration, and is never persisted. Use
/// <see cref="CreateFixed"/> to obtain the canonical instance. Whether a Kraken is
/// actually running on the silicon is transient runtime state (the live
/// controller's HardwareErected), unrelated to this structure.
/// </summary>
public sealed class KrakenConfiguration
{
  public int HeadCoordinate { get; } = KrakenTopology.HeadCoordinate;
  public List<KrakenTentacleConfiguration> Tentacles { get; }

  private KrakenConfiguration(List<KrakenTentacleConfiguration> tentacles)
  {
    Tentacles = tentacles;
  }

  /// <summary>
  /// The canonical fixed Kraken structure. Always present, always the same three
  /// tentacles. There is no "installed/removed" persisted state: a Kraken either
  /// is or is not erected on hardware at runtime, which this type does not track.
  /// </summary>
  public static KrakenConfiguration CreateFixed() =>
  new(KrakenTopology.CreateDefaultTentacles());

  /// <summary>
  /// Post-mortem-only structure with node 300 omitted from Tentacle 1's path -- see
  /// <see cref="KrakenTopology.CreatePostMortemTentaclesExcludingNode300"/> for why. Used ONLY by
  /// <see cref="ViewModels.CvmDebuggerViewModel"/>'s Core Dump; every other consumer (in particular
  /// the "Install Kraken" button's <see cref="Services.KrakenSession.ErectOnto"/>) keeps using
  /// <see cref="CreateFixed"/>, unaffected, with node 300 still in its normal place.
  /// </summary>
  public static KrakenConfiguration CreateForPostMortemExcludingNode300() =>
  new(KrakenTopology.CreatePostMortemTentaclesExcludingNode300());

  /// <summary>
  /// Always true: the structure is a constant and is always defined. Retained so
  /// existing consumers (route map building, session erection) that guarded on a
  /// former persisted flag keep compiling and behave as "structure present".
  /// </summary>
  public bool Enabled => true;
}

public sealed class KrakenTentacleConfiguration
{
  public int Number { get; set; }
  public string Name { get; set; } = string.Empty;
  public List<int> Nodes { get; set; } = [];
}

public sealed record KrakenNodeRoute(
  int Coordinate,
  bool IsHead,
  int TentacleNumber,
  string TentacleName,
  int Position,
  int? PreviousCoordinate,
  int? NextCoordinate,
  string IncomingPort,
  string OutgoingPort,
  int? OutgoingBAddress);

/// <summary>
/// Balanced three-tentacle topology for an 8 x 18 GA144 array.  Node 708 is
/// an especially useful head because its west, east and south COM ports lead
/// directly into three independent tentacles while its external asynchronous
/// serial interface remains available to the host PC.
/// </summary>
public static class KrakenTopology
{
  public const int HeadCoordinate = 708;

  // The routes cover every node other than the head exactly once.  Their
  // lengths are 50, 46 and 47 nodes (143 total), keeping the longest path
  // within four hops of the shortest while retaining simple, non-crossing
  // paths that are easy to inspect on the rectangular chip drawing.
  private static readonly int[] Tentacle1Nodes =
  [
    707, 706, 705, 704, 703, 702, 701, 700,
    600, 601, 602, 603, 604, 605,
    505, 504, 503, 502, 501, 500,
    400, 401, 402, 403, 404, 405,
    305, 304, 303, 302, 301, 300,
    200, 201, 202, 203, 204, 205,
    105, 104, 103, 102, 101, 100,
    000, 001, 002, 003, 004, 005
  ];

  private static readonly int[] Tentacle2Nodes =
  [
    709, 710, 711, 712, 713, 714, 715, 716, 717,
    617, 616, 615, 614, 613, 612,
    512, 513, 514, 515, 516, 517,
    417, 416, 415, 414, 413, 412,
    312, 313, 314, 315, 316, 317,
    217, 216, 215, 214, 213, 212,
    112, 113, 114, 115, 116, 117,
    017
  ];

  private static readonly int[] Tentacle3Nodes =
  [
    608, 609, 610, 611, 511, 411, 311, 211, 111,
    110, 109, 209, 210, 310, 309, 409, 410, 510,
    509, 508, 408, 407, 507, 607, 606, 506, 406,
    306, 307, 308, 208, 207, 206, 106, 006, 007,
    107, 108, 008, 009, 010, 011, 012, 013, 014,
    015, 016
  ];

  // ---- AN003 SRAM cluster: short, purpose-built Tentacle 3 ----------------
  // The fixed Tentacle3Nodes array above places the AN003 interface nodes
  // (007/107) ahead of several other nodes (108, 008, 009, 010-016) in relay
  // order. Once 007/107 are jumped into the SRAM cluster's own resident
  // firmware they stop relaying Kraken's "sett"/"w-r" traffic, which strands
  // everything the fixed array happens to place after them -- this is the
  // "Kraken word acknowledgment timed out" failure reaching node 107/108
  // after the cluster is installed.
  //
  // Per the fix: instead of trying to reorder the full 47-node array (proven,
  // by exhaustive search over its induced grid subgraph, unable to keep all
  // three candidate masters simultaneously reachable), Tentacle 3 is
  // reorganized into a short, direct path from 608 straight to whichever
  // node (106, 108 or 207) is chosen as SRAM memory master, continuing on
  // past the master to the four cluster nodes. Reaching the master BEFORE
  // any cluster node is jumped keeps it puppetable for the rest of the
  // session (Kraken never needs to relay through a cluster node to reach
  // it); the cluster nodes are then jumped tail-first (see
  // SramClusterInstaller), each one "redacted" from the live chain the
  // moment it is programmed, since nothing beyond it is still needed.
  //
  // Only Tentacle 3 is ever touched this way -- Tentacles 1 and 2 keep their
  // full fixed arrays untouched, and every node this short path omits simply
  // never gets wired into relay mode in the first place (nothing to strand:
  // it was never reachable this session), matching the user's own
  // "unchanged nodes simply get inaccessible" framing. See
  // BuildSramMasterPath/ApplySramMasterTentacle.
  private static readonly int[] SramMasterPath106 = [608, 607, 606, 506, 406, 306, 206, 106, 107, 007, 008, 009];
  private static readonly int[] SramMasterPath108 = [608, 508, 408, 308, 208, 108, 107, 007, 008, 009];
  private static readonly int[] SramMasterPath207 = [608, 607, 507, 407, 307, 207, 107, 007, 008, 009];

  /// <summary>
  /// The short, direct Tentacle-3 path (starting at 608, the fixed Tentacle-3
  /// head-adjacent node -- same convention as Tentacle3Nodes above) to
  /// <paramref name="masterCoordinate"/> (106, 108 or 207), continuing on to
  /// the AN003 cluster nodes 007/008/009/107 in an order that keeps the
  /// master reachable for the whole session -- see the remarks above.
  /// </summary>
  public static int[] BuildSramMasterPath(int masterCoordinate) => masterCoordinate switch
  {
    106 => [.. SramMasterPath106],
    108 => [.. SramMasterPath108],
    207 => [.. SramMasterPath207],
    _ => throw new ArgumentOutOfRangeException(
        nameof(masterCoordinate), masterCoordinate, "SRAM memory master must be node 106, 108, or 207.")
  };

  /// <summary>
  /// Reorganizes ONLY Tentacle 3 of <paramref name="configuration"/> in
  /// place, replacing its node list with <see cref="BuildSramMasterPath"/>
  /// for <paramref name="masterCoordinate"/>. Tentacles 1 and 2 are never
  /// touched. Returns true if the tentacle's node list actually changed
  /// (false if it already matched, e.g. re-installing for the same master).
  /// This mutates the live <see cref="KrakenConfiguration"/> in place -- per
  /// its own remarks the structure is never persisted, so this is
  /// session-scoped, exactly like the transient hardware erection state that
  /// must follow it (the physical relay wiring can only be set at erection
  /// time; callers must re-erect after calling this if a Kraken is already
  /// resident).
  /// </summary>
  public static bool ApplySramMasterTentacle(KrakenConfiguration configuration, int masterCoordinate)
  {
    ArgumentNullException.ThrowIfNull(configuration);
    int[] desired = BuildSramMasterPath(masterCoordinate);

    KrakenTentacleConfiguration tentacle3 = configuration.Tentacles.SingleOrDefault(item => item.Number == 3)
        ?? throw new InvalidOperationException("Kraken configuration has no Tentacle 3.");

    int previous = HeadCoordinate;
    foreach (int coordinate in desired)
    {
      if (!AreAdjacent(previous, coordinate))
      {
        throw new InvalidOperationException(
            $"SRAM master path for node {masterCoordinate:000} is not a valid adjacency chain (node {coordinate:000}).");
      }

      previous = coordinate;
    }

    if (tentacle3.Nodes.SequenceEqual(desired))
    {
      return false;
    }

    tentacle3.Nodes = [.. desired];
    return true;
  }

  // ---- Post-mortem: Tentacle 1 rerouted around node 300 -------------------
  // Core Dump's node-by-node live erect-and-read (CvmDebuggerViewModel.CoreDumpAsync) sends node 300
  // a live focusing call (KrakenSession.FocusAsync) as part of wiring the tentacle one hop at a time.
  // That is the same dynamic, per-word-acknowledged mechanism the project's own
  // node-300-erection-investigation found unreliable at this exact node, and on real hardware it has
  // reproduced here too: node 300's focus reply times out, and since the tentacle is a physical relay
  // chain, every node after it (200 through 005 in the fixed array) becomes unreachable in the same
  // run.
  //
  // Rather than lose the rest of Tentacle 1 to one bad node, this path omits node 300 entirely and
  // reroutes around it: where the fixed array goes 301 -> 300 -> 200 (300 is the only link between
  // row 3 and row 2 at column 0), this one goes 301 -> 201 instead, then snakes column-by-column
  // through rows 0-2 so every other node in that corner (200 included) still gets visited exactly
  // once. Verified by hand as a valid chain of physically adjacent nodes (KrakenTopology.AreAdjacent),
  // same as the fixed arrays above.
  //
  // This is post-mortem-only, per Stefan's own direction ("let's include node 300 for now, maybe we
  // will have to skip node 300 for post-mortem") -- it is never used to build the real, resident
  // Kraken (KrakenConfiguration.CreateFixed/ErectOnto keep the normal 50-node Tentacle 1, node 300
  // included), so it has no effect on ordinary CVM Debug use. Node 300 simply has no entry in a Core
  // Dump snapshot taken this way -- the post-mortem chip window already renders a node with no
  // captured data as blank (the same way it already renders head node 708, which is never captured).
  private static readonly int[] PostMortemTentacle1NodesExcludingNode300 =
  [
    707, 706, 705, 704, 703, 702, 701, 700,
    600, 601, 602, 603, 604, 605,
    505, 504, 503, 502, 501, 500,
    400, 401, 402, 403, 404, 405,
    305, 205, 105, 005, 004, 104, 204, 304, 303, 203, 103, 003, 002, 102, 202, 302, 301, 201, 101, 001, 000, 100, 200
  ];

  /// <summary>
  /// Post-mortem-only tentacle set: Tentacle 1 is <see cref="PostMortemTentacle1NodesExcludingNode300"/>
  /// (node 300 omitted, everything else rerouted around it); Tentacles 2 and 3 are the ordinary fixed
  /// arrays, untouched. See the remarks above <see cref="PostMortemTentacle1NodesExcludingNode300"/>
  /// for why this exists and why it must never replace <see cref="CreateDefaultTentacles"/>.
  /// </summary>
  public static List<KrakenTentacleConfiguration> CreatePostMortemTentaclesExcludingNode300()
  {
    var result = new List<KrakenTentacleConfiguration>
    {
      CreateTentacle(1, "West", PostMortemTentacle1NodesExcludingNode300),
      CreateTentacle(2, "East", Tentacle2Nodes),
      CreateTentacle(3, "South", Tentacle3Nodes)
    };

    foreach (KrakenTentacleConfiguration tentacle in result)
    {
      int previous = HeadCoordinate;
      foreach (int coordinate in tentacle.Nodes)
      {
        if (coordinate == 300)
        {
          throw new InvalidOperationException("Post-mortem Tentacle topology must not include node 300.");
        }

        if (!AreAdjacent(previous, coordinate))
        {
          throw new InvalidOperationException(
              $"Post-mortem Tentacle {tentacle.Number} path is not a valid adjacency chain at node {coordinate:000}.");
        }

        previous = coordinate;
      }
    }

    return result;
  }

  public static List<KrakenTentacleConfiguration> CreateDefaultTentacles()
  {
    var result = new List<KrakenTentacleConfiguration>
    {
      CreateTentacle(1, "West", Tentacle1Nodes),
      CreateTentacle(2, "East", Tentacle2Nodes),
      CreateTentacle(3, "South", Tentacle3Nodes)
    };

    if (!IsValid(result))
    {
      throw new InvalidOperationException("The built-in Kraken topology is invalid.");
    }

    return result;
  }

  public static IReadOnlyDictionary<int, KrakenNodeRoute> BuildRouteMap(KrakenConfiguration configuration)
  {
    ArgumentNullException.ThrowIfNull(configuration);

    var routes = new Dictionary<int, KrakenNodeRoute>();

    routes[HeadCoordinate] = new KrakenNodeRoute(
      HeadCoordinate,
      true,
      0,
      "Head",
      0,
      null,
      null,
      "PC / asynchronous serial",
      "W -> T1, E -> T2, S -> T3",
      null);

    foreach (KrakenTentacleConfiguration tentacle in configuration.Tentacles.OrderBy(item => item.Number))
    {
      for (int index = 0; index < tentacle.Nodes.Count; index++)
      {
        int coordinate = tentacle.Nodes[index];
        int previous = index == 0 ? HeadCoordinate : tentacle.Nodes[index - 1];
        int? next = index + 1 < tentacle.Nodes.Count ? tentacle.Nodes[index + 1] : null;
        string incoming = PortName(coordinate, previous);
        string outgoing = next is int nextCoordinate ? PortName(coordinate, nextCoordinate) : "end";
        int? bAddress = next is int target ? PortAddress(coordinate, target) : null;

        routes[coordinate] = new KrakenNodeRoute(
          coordinate,
          false,
          tentacle.Number,
          tentacle.Name,
          index,
          previous,
          next,
          incoming,
          outgoing,
          bAddress);
      }
    }

    return routes;
  }

  public static bool IsValid(IReadOnlyList<KrakenTentacleConfiguration>? tentacles)
  {
    if (tentacles is null || tentacles.Count != 3)
    {
      return false;
    }

    var seen = new HashSet<int> { HeadCoordinate };
    int[] expectedStarts = [707, 709, 608];
    int minimumLength = int.MaxValue;
    int maximumLength = 0;

    foreach (KrakenTentacleConfiguration tentacle in tentacles.OrderBy(item => item.Number))
    {
      if (tentacle.Number is < 1 or > 3 || tentacle.Nodes is null || tentacle.Nodes.Count == 0)
      {
        return false;
      }

      if (tentacle.Nodes[0] != expectedStarts[tentacle.Number - 1])
      {
        return false;
      }

      minimumLength = Math.Min(minimumLength, tentacle.Nodes.Count);
      maximumLength = Math.Max(maximumLength, tentacle.Nodes.Count);

      int previous = HeadCoordinate;
      foreach (int coordinate in tentacle.Nodes)
      {
        if (!IsNodeCoordinate(coordinate) || !AreAdjacent(previous, coordinate) || !seen.Add(coordinate))
        {
          return false;
        }

        previous = coordinate;
      }
    }

    return seen.Count == 144 && maximumLength - minimumLength <= 4;
  }

  public static bool AreAdjacent(int first, int second)
  {
    int firstRow = first / 100;
    int firstColumn = first % 100;
    int secondRow = second / 100;
    int secondColumn = second % 100;
    return Math.Abs(firstRow - secondRow) + Math.Abs(firstColumn - secondColumn) == 1;
  }

  /// <summary>
  /// Returns the LOCAL F18 port name that connects <paramref name="from"/>
  /// to the geographically adjacent node <paramref name="to"/>.  This is
  /// deliberately not just the compass direction.  GA144 cells are mirrored
  /// in alternating rows/columns; the Ganglia Mark 2 ewns tables document the
  /// same four orientation classes.  Consequently, for example, geographic
  /// East is local RIGHT in an even column and local LEFT in an odd column.
  /// </summary>
  public static string PortName(int from, int to)
  {
    int address = PortAddress(from, to);
    return address switch
    {
      0x145 => "up",
      0x175 => "left",
      0x115 => "down",
      0x1D5 => "right",
      _ => throw new InvalidOperationException($"Unexpected single-port address 0x{address:X3}.")
    };
  }

  /// <summary>
  /// Returns the LOCAL F18 single-COM-port address connecting two adjacent
  /// geographic nodes.  The GA144 alternates physical node orientation:
  ///
  ///   odd row/even col (oee): E=right W=left  N=up   S=down
  ///   odd row/odd  col (ooo): E=left  W=right N=up   S=down
  ///   even row/even col (eee): E=right W=left N=down S=up
  ///   even row/odd  col (eoo): E=left  W=right N=down S=up
  ///
  /// Those are the four ewns tables used by GreenArrays Ganglia Mark 2.
  /// Treating right/left/up/down as fixed geographic directions erects the
  /// wrong Kraken as soon as it reaches a mirrored cell.
  /// </summary>
  public static int PortAddress(int from, int to)
  {
    (int rowDelta, int columnDelta) = Delta(from, to);
    int row = from / 100;
    int column = from % 100;

    return (rowDelta, columnDelta) switch
    {
      // Geographic north/south swap local UP/DOWN on even rows.
      (1, 0) => (row & 1) == 0 ? 0x115 : 0x145,
      (-1, 0) => (row & 1) == 0 ? 0x145 : 0x115,

      // Geographic east/west swap local RIGHT/LEFT on odd columns.
      (0, 1) => (column & 1) == 0 ? 0x1D5 : 0x175,
      (0, -1) => (column & 1) == 0 ? 0x175 : 0x1D5,
      _ => throw new ArgumentException($"Nodes {from:000} and {to:000} are not adjacent.")
    };
  }

  private static KrakenTentacleConfiguration CreateTentacle(int number, string name, IReadOnlyCollection<int> nodes) =>
  new()
  {
    Number = number,
    Name = name,
    Nodes = [.. nodes]
  };

  private static (int RowDelta, int ColumnDelta) Delta(int from, int to) =>
  (to / 100 - from / 100, to % 100 - from % 100);

  private static bool IsNodeCoordinate(int coordinate)
  {
    int row = coordinate / 100;
    int column = coordinate % 100;
    return row is >= 0 and < 8 && column is >= 0 and < 18;
  }
}