namespace Ga144.Evb.Ide.Models;

public sealed class BoardPortBinding
{
  public EvalBoardPortRole Role { get; set; }
  public FtdiIdentity Ftdi { get; set; } = new();
  public string? LastKnownComPort { get; set; }
  public DateTimeOffset? LastSeenUtc { get; set; }
}
