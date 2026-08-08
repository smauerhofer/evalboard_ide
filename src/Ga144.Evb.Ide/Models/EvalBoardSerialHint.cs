using System.Text.RegularExpressions;

namespace Ga144.Evb.Ide.Models;

/// <summary>
/// Identity information inferred from the GreenArrays FTDI serial-number convention.
/// This is a naming convention hint, not an active electrical verification.
/// </summary>
public sealed partial record EvalBoardSerialHint(
    EvalBoardModel Model,
    string BoardSerialNumber,
    EvalBoardPortRole PortRole,
    char PortDesignator,
    char FtdiChannel,
    string RawSerialNumber)
{
  public bool IsNode708Port => PortRole is EvalBoardPortRole.PortAHost or EvalBoardPortRole.PortCTarget;

  public string BoardKey => $"{Model}:S{BoardSerialNumber}";

  public string DisplayText => $"{Model} S{BoardSerialNumber} / Port {PortDesignator}";

  public static EvalBoardSerialHint? TryParse(string? serialNumber)
  {
    if (string.IsNullOrWhiteSpace(serialNumber))
    {
      return null;
    }

    string normalized = serialNumber.Trim().ToUpperInvariant();
    Match match = GreenArraysEvalBoardSerialRegex().Match(normalized);
    if (!match.Success)
    {
      return null;
    }

    if (!Enum.TryParse(match.Groups["model"].Value, ignoreCase: true, out EvalBoardModel model) ||
        model is not (EvalBoardModel.EVB001 or EvalBoardModel.EVB002))
    {
      return null;
    }

    char port = match.Groups["port"].Value[0];
    EvalBoardPortRole role = port switch
    {
      'A' => EvalBoardPortRole.PortAHost,
      'B' => EvalBoardPortRole.PortBGeneral,
      'C' => EvalBoardPortRole.PortCTarget,
      _ => throw new InvalidOperationException($"Unsupported FTDI port designator '{port}'.")
    };

    return new EvalBoardSerialHint(
        model,
        match.Groups["board"].Value,
        role,
        port,
        match.Groups["channel"].Value[0],
        normalized);
  }

  // Observed examples:
  //   GAEVB001S0121AA -> EVB001, board 0121, physical Port A, FTDI channel A
  //   GAEVB001S0121CA -> EVB001, board 0121, physical Port C, FTDI channel A
  // The board field is allowed to contain letters and digits. Matching from the
  // end keeps the final two characters available as physical-port/channel suffixes.
  [GeneratedRegex(
      @"^GA(?<model>EVB(?:001|002))S(?<board>[A-Z0-9]+)(?<port>[ABC])(?<channel>[A-Z])$",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex GreenArraysEvalBoardSerialRegex();
}
