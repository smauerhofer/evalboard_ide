namespace Ga144.Evb.Ide.Models;

public sealed class FtdiIdentity
{
  public string StableId { get; set; } = string.Empty;
  public string? PnpDeviceId { get; set; }
  public string? Vid { get; set; }
  public string? Pid { get; set; }
  public string? SerialNumber { get; set; }
  public string? Manufacturer { get; set; }
  public string? FriendlyName { get; set; }

  public static FtdiIdentity FromPort(SerialPortInfo port) => new()
  {
    StableId = port.StableId,
    PnpDeviceId = port.PnpDeviceId,
    Vid = port.Vid,
    Pid = port.Pid,
    SerialNumber = port.SerialNumber,
    Manufacturer = port.Manufacturer,
    FriendlyName = port.FriendlyName
  };

  public bool Matches(SerialPortInfo port)
  {
    if (!string.IsNullOrWhiteSpace(StableId) &&
        string.Equals(StableId, port.StableId, StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    return !string.IsNullOrWhiteSpace(SerialNumber) &&
           string.Equals(SerialNumber, port.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(Vid, port.Vid, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(Pid, port.Pid, StringComparison.OrdinalIgnoreCase);
  }
}
