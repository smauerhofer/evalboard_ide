namespace Ga144.Evb.Ide.Models;

/// <summary>
/// Runtime resolution of the currently selected physical board endpoint used
/// for an online Kraken session. This is intentionally not persisted: COM
/// numbers can change, so the IDE resolves the saved FTDI identity against the
/// serial devices that are present right now.
/// </summary>
public sealed record KrakenEndpointInfo(
    string BoardName,
    string PortName,
    Ga144ChipRole Role);
