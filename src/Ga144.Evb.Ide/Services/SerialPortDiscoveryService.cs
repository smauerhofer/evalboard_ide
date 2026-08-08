using Ga144.Evb.Ide.Models;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace Ga144.Evb.Ide.Services;

public sealed partial class SerialPortDiscoveryService
{
  public IReadOnlyList<SerialPortInfo> Enumerate()
  {
    var result = new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);

    try
    {
      using var searcher = new ManagementObjectSearcher(
          "SELECT Name, DeviceID, PNPDeviceID, Manufacturer FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

      using ManagementObjectCollection devices = searcher.Get();
      foreach (ManagementObject device in devices)
      {
        using (device)
        {
          string friendlyName = Convert.ToString(device["Name"]) ?? string.Empty;
          Match portMatch = ComPortAtEndRegex().Match(friendlyName);
          if (!portMatch.Success)
          {
            continue;
          }

          string portName = portMatch.Groups["port"].Value.ToUpperInvariant();
          string? pnpDeviceId = Convert.ToString(device["PNPDeviceID"])
                                ?? Convert.ToString(device["DeviceID"]);
          string? manufacturer = Convert.ToString(device["Manufacturer"]);
          (string? vid, string? pid) = ExtractVidPid(pnpDeviceId);
          string? serialNumber = ExtractSerialNumber(pnpDeviceId, vid, pid);
          bool isFtdi = string.Equals(vid, "0403", StringComparison.OrdinalIgnoreCase) ||
                        (manufacturer?.Contains("FTDI", StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (pnpDeviceId?.StartsWith("FTDIBUS", StringComparison.OrdinalIgnoreCase) ?? false);
          string stableId = BuildStableId(pnpDeviceId, vid, pid, serialNumber, portName);
          EvalBoardSerialHint? evalBoardHint = EvalBoardSerialHint.TryParse(serialNumber);

          result[portName] = new SerialPortInfo(
              portName,
              friendlyName,
              manufacturer,
              pnpDeviceId,
              vid,
              pid,
              serialNumber,
              stableId,
              isFtdi,
              evalBoardHint);
        }
      }
    }
    catch (ManagementException)
    {
      // SerialPort.GetPortNames below still provides a usable fallback.
    }
    catch (UnauthorizedAccessException)
    {
      // Some managed Windows environments restrict WMI. Keep the COM fallback.
    }

    try
    {
      foreach (string portName in SerialPort.GetPortNames())
      {
        if (!result.ContainsKey(portName))
        {
          result[portName] = new SerialPortInfo(
              portName,
              portName,
              null,
              null,
              null,
              null,
              null,
              $"COM:{portName.ToUpperInvariant()}",
              false,
              null);
        }
      }
    }
    catch (IOException)
    {
      // Return any WMI results already collected.
    }
    catch (UnauthorizedAccessException)
    {
      // Return any WMI results already collected.
    }

    return result.Values
        .OrderBy(port => GetPortNumber(port.PortName))
        .ThenBy(port => port.PortName, StringComparer.OrdinalIgnoreCase)
        .ToArray();
  }

  private static (string? Vid, string? Pid) ExtractVidPid(string? pnpDeviceId)
  {
    if (string.IsNullOrWhiteSpace(pnpDeviceId))
    {
      return (null, null);
    }

    Match match = VidPidRegex().Match(pnpDeviceId);
    return match.Success
        ? (match.Groups["vid"].Value.ToUpperInvariant(), match.Groups["pid"].Value.ToUpperInvariant())
        : (null, null);
  }

  private static string? ExtractSerialNumber(string? pnpDeviceId, string? vid, string? pid)
  {
    if (string.IsNullOrWhiteSpace(pnpDeviceId))
    {
      return null;
    }

    Match ftdiBus = FtdiBusSerialRegex().Match(pnpDeviceId);
    if (ftdiBus.Success)
    {
      return ftdiBus.Groups["serial"].Value;
    }

    string[] parts = pnpDeviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length >= 3 && !parts[^1].Contains('&'))
    {
      return parts[^1];
    }

    if (!string.IsNullOrWhiteSpace(vid) && !string.IsNullOrWhiteSpace(pid))
    {
      Match generic = GenericSerialRegex().Match(pnpDeviceId);
      if (generic.Success)
      {
        return generic.Groups["serial"].Value;
      }
    }

    return null;
  }

  private static string BuildStableId(
      string? pnpDeviceId,
      string? vid,
      string? pid,
      string? serialNumber,
      string portName)
  {
    if (!string.IsNullOrWhiteSpace(serialNumber))
    {
      return $"USB:{vid}:{pid}:{serialNumber}".ToUpperInvariant();
    }

    if (!string.IsNullOrWhiteSpace(pnpDeviceId))
    {
      return pnpDeviceId.Trim().ToUpperInvariant();
    }

    return $"COM:{portName}".ToUpperInvariant();
  }

  private static int GetPortNumber(string portName)
  {
    Match match = ComPortNumberRegex().Match(portName);
    return match.Success && int.TryParse(match.Groups["number"].Value, out int value)
        ? value
        : int.MaxValue;
  }

  [GeneratedRegex(@"\((?<port>COM\d+)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex ComPortAtEndRegex();

  [GeneratedRegex(@"^COM(?<number>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex ComPortNumberRegex();

  [GeneratedRegex(@"VID_(?<vid>[0-9A-F]{4})(?:[&+])PID_(?<pid>[0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex VidPidRegex();

  [GeneratedRegex(@"PID_[0-9A-F]{4}\+(?<serial>[^\\]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex FtdiBusSerialRegex();

  [GeneratedRegex(@"\\(?<serial>[A-Z0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex GenericSerialRegex();
}
