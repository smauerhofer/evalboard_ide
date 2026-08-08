namespace Ga144.Evb.Ide.Models;

public enum EvalBoardModel
{
  Unknown,
  EVB001,
  EVB002
}

public enum EvalBoardPortRole
{
  PortAHost,
  PortBGeneral,
  PortCTarget
}

public enum Ga144ChipRole
{
  Host,
  Target
}

public enum ProbeState
{
  NotProbed,
  Identified,
  Mapped,
  Probing,
  Detected,
  NoResponse,
  Error
}

/// <summary>
/// How a resident Kraken session manages the native COM handle between
/// transactions.
/// </summary>
public enum KrakenIdlePolicy
{
  /// <summary>
  /// Hold the FTDI handle open for the whole Kraken lifetime; between
  /// transactions only quiesce it (purge RX, hold RESET- inactive/high). This
  /// keeps continuous control of the Port A reset line and keeps the device out
  /// of USB selective suspend. Recommended, and the only glitch-free option on
  /// Port A. The handle is closed exactly once, at session dispose.
  /// </summary>
  HoldOpen,

  /// <summary>
  /// Close the FTDI handle while idle and reopen it (transport-only, no reset)
  /// for each transaction, so the COM port can be shared with another process
  /// while the Kraken is idle. Reopen is retry-hardened and the first read after
  /// a reopen is given extra time for FTDI selective-suspend wake. NOTE: on
  /// Port A the stock VCP driver can still glitch RESET- for a sub-millisecond
  /// window on CreateFile; this policy cannot fully eliminate that.
  /// </summary>
  CloseWhileIdle,

  /// <summary>
  /// Hybrid of HoldOpen and CloseWhileIdle. The FTDI handle opens lazily on the
  /// first transaction and stays open; a 1 s idle timer then closes it. Every
  /// transaction re-arms the timer, so a burst (e.g. a full Check Kraken) keeps
  /// the handle open for its whole duration and closes once, ~1 s after the last
  /// transaction. This bounds the KVM-disruptive open state to at most ~1 s of
  /// trailing idle instead of holding the handle open for the process lifetime
  /// (HoldOpen) or re-enumerating on every single transaction (CloseWhileIdle).
  /// </summary>
  CloseAfterIdleTimeout
}