namespace Ga144.Evb.Ide.Models;

// A jumper overlay. Two-pin jumpers use one rectangle at (X,Y): DefaultInstalled
// controls install/remove (transparent when removed). Three-pin jumpers are a
// selector: DefaultInstalled = false means the shunt sits on pins 1-2 and the
// rectangle is drawn at (X,Y); true means pins 2-3, drawn at (X2,Y2). A three-pin
// rectangle is never transparent - clicking moves it between the two pin pairs.
public sealed record JumperVisualDefinition(
    string Id,
    string Label,
    double X,
    double Y,
    double Width,
    double Height,
    bool DefaultInstalled = false,
    int PinCount = 2,
    double X2 = 0,
    double Y2 = 0)
{
  public bool IsThreePin => PinCount == 3;

  // The rectangle origin for the currently-selected position. For three-pin
  // jumpers, 'selected' (the stored bool) picks pins 1-2 (false -> X,Y) or 2-3
  // (true -> X2,Y2). For two-pin jumpers the origin is always (X,Y).
  public (double X, double Y) OriginFor(bool selected) =>
      IsThreePin && selected ? (X2, Y2) : (X, Y);
}

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

  private static readonly IReadOnlyList<JumperVisualDefinition> Evb001Jumpers =
  [
      // Host / Target power selection: 3-pin horizontal, default shunt on 2-3.
      Jumper3H("J10", "Host core power: onboard 1.8 V", 56, 99, true),
      Jumper3H("J11", "Host I/O and analog power: onboard 1.8 V", 56, 106, true),
      Jumper3H("J14", "Target core power: onboard 1.8 V", 76, 99, true),
      Jumper3H("J15", "Target I/O power: onboard 1.8 V", 76, 104, true),
      Jumper3H("J16", "Target analog power: onboard 1.8 V", 76, 109, true),

      // J23: six two-pin horizontal shunts, top to bottom.
      JumperH2("J23-A-RX", "Port A receive to Host 708.17", 107, 57, true),
      JumperH2("J23-A-TX", "Port A transmit from Host 708.1", 107, 62, true),
      JumperH2("J23-B-RX", "Port B receive to Host 200.17", 107, 67, true),
      JumperH2("J23-B-TX", "Port B transmit from Host 100.17", 107, 72, true),
      JumperH2("J23-C-RX", "Port C receive to Target 708.17", 107, 77, true),
      JumperH2("J23-C-TX", "Port C transmit from Target 708.1", 107, 82, true),

      // J22: three two-pin vertical Target reset paths, side by side.
      JumperV2("J22-HOST", "Host 500.17 to Target RESET-", 102, 87, true),
      JumperV2("J22-USB-C", "USB C RTS to Target RESET-", 107, 87, true),
      JumperV2("J22-RC", "Target reset circuit to Target RESET-", 112, 87, true),

      // J20: two two-pin vertical Host reset paths (2x2 pin grid, one per column).
      JumperV2("J20-RESET", "Host reset circuit to RESET-", 94, 129, true),
      JumperV2("J20-USB-A", "USB A RTS to Host RESET-", 99, 129, true),

      // Host reset / boot selection.
      Jumper3V("J25", "Host reset / SPI flash reset selection", 106, 129, true),
      JumperV2("J26", "NO BOOT", 111, 134, false),

      // Host/Target synchronous bridge: two-pin horizontal.
      JumperH2("J34", "Host 300.1 to Target 300.1", 149, 118, true),
      JumperH2("J35", "Host 300.17 to Target 300.17", 159, 118, true),

      // Flash / MMC enable selection.
      Jumper3V("J39", "Host 600.17 to FLASHENABLE-", 178, 122, false),
      JumperH2("J37-1", "Flash/MMC enable selection (upper)", 176, 145, true),
      JumperH2("J37-2", "Flash/MMC enable selection (lower)", 176, 150, true),

      // J38/J40: five two-pin horizontal shunts mapping the flash/MMC socket
      // signals between MMC-mode (left, J38) and SPI-mode (right, J40), top down.
      JumperH2("J38-SPI-CLK-MMC", "CLK/SCLK ↔ SPI CLK MMC", 178, 36, true),
      JumperH2("J38-SPI-CS-MMC", "DAT3/CS- ↔ SPI CS- MMC", 178, 41, true),
      JumperH2("J38-SPI-DO", "CMD/SI ↔ SPI DO", 178, 46, true),
      JumperH2("J38-SPI-DI", "DAT0/SO ↔ SPI DI", 178, 51, true),
      JumperH2("J38-1V8", "Vdd ↔ 1.8V", 178, 56, true)
  ];

  private static readonly IReadOnlyList<JumperVisualDefinition> Evb002Jumpers = Evb001Jumpers
      .Where(jumper => jumper.Id != "J26")
      .Append(JumperV2("J26", "NO BOOT", 111, 134, true))
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

  // Geometry constants in 380x226 picture space (Jumper helpers apply Scale).
  // Pins are 2x2 px on a 5 px (1/10") raster. A 2-pin shunt spans 7 px along its
  // axis (pin1 left edge .. pin2 right edge) and 2 px across. Boxes are centred on
  // the shunt and extend +2 px past each pin end on the LONG axis (the +4 px the
  // board overlay needs), and reach the half-raster boundary on the short axis
  // (5 px) so neighbours touch but never overlap.
  private const double PinPitch = 5.0;
  private const double PinSize = 2.0;
  private const double ShortExtent = 5.0;   // across the pins
  private const double LongExtent = 7.0 + 2.0; // 7 px pin span + 2 px overshoot (1 each end)

  // Centre of a 2-pin shunt along its axis, given pin 1's top-left origin o:
  // pins occupy o..o+PinPitch+PinSize (o to o+7), centre at o + 3.5.
  private const double ShuntHalf = (PinPitch + PinSize) / 2.0; // 3.5

  // Build a horizontal 2-pin jumper from pin 1's top-left (px, py).
  private static JumperVisualDefinition JumperH2(string id, string label, double px, double py, bool installed)
  {
    double x = px + ShuntHalf - LongExtent / 2.0;
    double y = py + PinSize / 2.0 - ShortExtent / 2.0;
    return new(id, label, x * Scale, y * Scale, LongExtent * Scale, ShortExtent * Scale, installed, 2);
  }

  // Build a vertical 2-pin jumper from pin 1's top-left (px, py).
  private static JumperVisualDefinition JumperV2(string id, string label, double px, double py, bool installed)
  {
    double x = px + PinSize / 2.0 - ShortExtent / 2.0;
    double y = py + ShuntHalf - LongExtent / 2.0;
    return new(id, label, x * Scale, y * Scale, ShortExtent * Scale, LongExtent * Scale, installed, 2);
  }

  // Build a horizontal 3-pin selector from pin 1's top-left (px, py). Position
  // 1-2 spans pins at px and px+5; position 2-3 spans px+5 and px+10.
  // defaultTwoThree=true selects 2-3 (stored bool true), else 1-2.
  private static JumperVisualDefinition Jumper3H(string id, string label, double px, double py, bool defaultTwoThree)
  {
    double y = py + PinSize / 2.0 - ShortExtent / 2.0;
    double x12 = px + ShuntHalf - LongExtent / 2.0;
    double x23 = x12 + PinPitch;
    return new(id, label, x12 * Scale, y * Scale, LongExtent * Scale, ShortExtent * Scale,
        defaultTwoThree, 3, x23 * Scale, y * Scale);
  }

  // Build a vertical 3-pin selector from pin 1's top-left (px, py). Position 1-2
  // spans pins at py and py+5; position 2-3 spans py+5 and py+10.
  private static JumperVisualDefinition Jumper3V(string id, string label, double px, double py, bool defaultTwoThree)
  {
    double x = px + PinSize / 2.0 - ShortExtent / 2.0;
    double y12 = py + ShuntHalf - LongExtent / 2.0;
    double y23 = y12 + PinPitch;
    return new(id, label, x * Scale, y12 * Scale, ShortExtent * Scale, LongExtent * Scale,
        defaultTwoThree, 3, x * Scale, y23 * Scale);
  }

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