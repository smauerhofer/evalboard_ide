namespace Ga144.Evb.Ide.Models;

public sealed record SerialPortInfo(
    string PortName,
    string FriendlyName,
    string? Manufacturer,
    string? PnpDeviceId,
    string? Vid,
    string? Pid,
    string? SerialNumber,
    string StableId,
    bool IsFtdi,
    EvalBoardSerialHint? EvalBoardHint = null);
