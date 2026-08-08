namespace Ga144.Evb.Ide.Models;

/// <summary>
/// Project-side description of a Kraken installed on one GA144.
/// The head is node 708.  The three tentacles are ordered paths of adjacent
/// F18A nodes; node zero of each tentacle is directly adjacent to the head.
/// </summary>
public sealed class KrakenConfiguration
{
    public bool Enabled { get; set; }
    public int HeadCoordinate { get; set; } = KrakenTopology.HeadCoordinate;
    public List<KrakenTentacleConfiguration> Tentacles { get; set; } = [];

    public void Normalize()
    {
        Tentacles ??= [];

        if (!Enabled)
        {
            HeadCoordinate = KrakenTopology.HeadCoordinate;
            return;
        }

        if (HeadCoordinate != KrakenTopology.HeadCoordinate || !KrakenTopology.IsValid(Tentacles))
        {
            InstallDefault();
        }
    }

    public void InstallDefault()
    {
        Enabled = true;
        HeadCoordinate = KrakenTopology.HeadCoordinate;
        Tentacles = KrakenTopology.CreateDefaultTentacles();
    }

    public void Remove()
    {
        Enabled = false;
        HeadCoordinate = KrakenTopology.HeadCoordinate;
        Tentacles ??= [];
        Tentacles.Clear();
    }
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
        configuration.Normalize();

        var routes = new Dictionary<int, KrakenNodeRoute>();
        if (!configuration.Enabled)
        {
            return routes;
        }

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
