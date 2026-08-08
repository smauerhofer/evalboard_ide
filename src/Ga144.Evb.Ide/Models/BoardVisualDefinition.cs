namespace Ga144.Evb.Ide.Models;

public sealed record JumperVisualDefinition(
    string Id,
    string Label,
    double X,
    double Y,
    double Width,
    double Height,
    bool DefaultInstalled = false);

public sealed record ChipVisualDefinition(
    Ga144ChipRole Role,
    string Label,
    string ShortLabel,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record BoardPortVisualDefinition(
    EvalBoardPortRole Role,
    string Label,
    string ShortLabel,
    double X,
    double Y,
    double Width,
    double Height);

public sealed class BoardVisualDefinition
{
    public EvalBoardModel Model { get; init; }
    public string ImageResource { get; init; } = string.Empty;
    public IReadOnlyList<JumperVisualDefinition> Jumpers { get; init; } = [];
    public IReadOnlyList<ChipVisualDefinition> Chips { get; init; } = [];
    public IReadOnlyList<BoardPortVisualDefinition> Ports { get; init; } = [];
}

public static class BoardVisualCatalog
{
    // The supplied board artwork is 380 x 226 pixels. BoardViewControl displays it
    // on a 760 x 452 logical canvas, so all artwork coordinates are doubled here.
    private const double Scale = 2.0;

    private static readonly IReadOnlyList<ChipVisualDefinition> Chips =
    [
        // The Host is the lower GA144, immediately above the SRAM package.
        Chip(Ga144ChipRole.Host, "Host GA144", "H", 107, 161, 26, 26),

        // The Target is the upper-left GA144 inside the through-hole breakout field.
        Chip(Ga144ChipRole.Target, "Target GA144", "T", 135, 71, 26, 26)
    ];

    private static readonly IReadOnlyList<BoardPortVisualDefinition> Ports =
    [
        // The three FTDI devices are the small square packages below USB A, B and C.
        Port(EvalBoardPortRole.PortAHost, "USB A / Host", "A", 24, 42, 14, 14),
        Port(EvalBoardPortRole.PortBGeneral, "USB B / Host serial", "B", 58, 42, 14, 14),
        Port(EvalBoardPortRole.PortCTarget, "USB C / Target", "C", 91, 42, 14, 14)
    ];

    // These overlays intentionally cover only the relevant two- or three-pin shunt
    // positions. They no longer cover complete headers, chips, or large PCB regions.
    private static readonly IReadOnlyList<JumperVisualDefinition> Evb001Jumpers =
    [
        // J23: six individual two-pin shunts, top to bottom.
        Jumper("J23-A-RX", "Port A receive to Host 708.17", 106, 53, 13, 6, true),
        Jumper("J23-A-TX", "Port A transmit from Host 708.1", 106, 61, 13, 6, true),
        Jumper("J23-B-RX", "Port B receive to Host 200.17", 106, 69, 13, 6, true),
        Jumper("J23-B-TX", "Port B transmit from Host 100.17", 106, 77, 13, 6, true),
        Jumper("J23-C-RX", "Port C receive to Target 708.17", 106, 85, 13, 6, true),
        Jumper("J23-C-TX", "Port C transmit from Target 708.1", 106, 93, 13, 6, true),

        // J22: three two-pin Target reset paths.
        Jumper("J22-HOST", "Host 500.17 to Target RESET-", 104, 104, 7, 13, true),
        Jumper("J22-USB-C", "USB C RTS to Target RESET-", 113, 104, 7, 13, true),
        Jumper("J22-RC", "Target reset circuit to Target RESET-", 122, 104, 7, 13, true),

        // Power selection headers. The installed shunt is on pins 2-3.
        Jumper("J10", "Host core power: onboard 1.8 V", 56, 112, 8, 12, true),
        Jumper("J11", "Host I/O and analog power: onboard 1.8 V", 56, 125, 8, 12, true),
        Jumper("J14", "Target core power: onboard 1.8 V", 76, 112, 8, 12, true),
        Jumper("J15", "Target I/O power: onboard 1.8 V", 85, 112, 8, 12, true),
        Jumper("J16", "Target analog power: onboard 1.8 V", 85, 125, 8, 12, true),

        // Host reset and boot selection.
        Jumper("J20-RESET", "Host reset circuit to RESET-", 88, 143, 8, 12, true),
        Jumper("J20-USB-A", "USB A RTS to Host RESET-", 97, 143, 8, 12, true),
        Jumper("J25", "Host reset / SPI flash reset selection", 88, 156, 8, 12, true),
        Jumper("J26", "NO BOOT", 98, 156, 8, 12, false),

        // Host/Target synchronous bridge and SPI selection.
        Jumper("J34", "Host 300.1 to Target 300.1", 149, 131, 13, 7, true),
        Jumper("J35", "Host 300.17 to Target 300.17", 163, 131, 13, 7, true),
        Jumper("J39", "Host 600.17 to FLASHENABLE-", 176, 148, 8, 12, false),
        Jumper("J37", "Flash/MMC enable selection", 176, 163, 8, 12, true)
    ];

    private static readonly IReadOnlyList<JumperVisualDefinition> Evb002Jumpers = Evb001Jumpers
        .Where(jumper => jumper.Id != "J26")
        .Append(Jumper("J26", "NO BOOT", 98, 156, 8, 12, true))
        .ToArray();

    private static readonly BoardVisualDefinition Unknown = new()
    {
        Model = EvalBoardModel.Unknown,
        ImageResource = string.Empty,
        Chips = Chips,
        Ports = Ports,
        Jumpers = Evb002Jumpers
    };

    private static readonly BoardVisualDefinition Evb001 = new()
    {
        Model = EvalBoardModel.EVB001,
        ImageResource = "pack://application:,,,/Ga144.Evb.Ide;component/Assets/Boards/evb001_board.png",
        Chips = Chips,
        Ports = Ports,
        Jumpers = Evb001Jumpers
    };

    private static readonly BoardVisualDefinition Evb002 = new()
    {
        Model = EvalBoardModel.EVB002,
        ImageResource = "pack://application:,,,/Ga144.Evb.Ide;component/Assets/Boards/evb002_board.png",
        Chips = Chips,
        Ports = Ports,
        Jumpers = Evb002Jumpers
    };

    public static BoardVisualDefinition Get(EvalBoardModel model) => model switch
    {
        EvalBoardModel.EVB001 => Evb001,
        EvalBoardModel.EVB002 => Evb002,
        _ => Unknown
    };

    private static JumperVisualDefinition Jumper(
        string id,
        string label,
        double x,
        double y,
        double width,
        double height,
        bool installed) =>
        new(id, label, x * Scale, y * Scale, width * Scale, height * Scale, installed);

    private static ChipVisualDefinition Chip(
        Ga144ChipRole role,
        string label,
        string shortLabel,
        double x,
        double y,
        double width,
        double height) =>
        new(role, label, shortLabel, x * Scale, y * Scale, width * Scale, height * Scale);

    private static BoardPortVisualDefinition Port(
        EvalBoardPortRole role,
        string label,
        string shortLabel,
        double x,
        double y,
        double width,
        double height) =>
        new(role, label, shortLabel, x * Scale, y * Scale, width * Scale, height * Scale);
}
