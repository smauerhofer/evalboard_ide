using System.IO.Ports;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Serial settings used by the verified EVB001/EVB002 node-708 detector.
/// </summary>
internal static class Ga144Serial
{
  public const int MaximumBaudRate = 921_600;

  public static SerialPort Create(string portName) => new(portName)
  {
    BaudRate = MaximumBaudRate,
    DataBits = 8,
    Parity = Parity.None,
    StopBits = StopBits.One,
    Handshake = Handshake.None,
    // Verified EVB idle state. RTS low asserts RESET-; RTS high releases it.
    // NOTE: with System.IO.Ports these RtsEnable/DtrEnable initializers are
    // only applied to the driver AFTER Open() creates the handle, so they do
    // not prevent a brief driver-default RTS state at Open. Callers on Port A
    // (where RTS = RESET-) should re-assert RtsEnable = true immediately after
    // Open() and again before Dispose(); see Ga144Node708Probe.
    DtrEnable = true,
    RtsEnable = true,
    ReadTimeout = 250,
    WriteTimeout = 1_000,
    ReadBufferSize = 4_096,
    WriteBufferSize = 4_096,
    DiscardNull = false
  };
}