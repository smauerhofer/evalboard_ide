using System.Diagnostics;
using System.IO.Ports;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Destructive active probe for a GA144 asynchronous boot node (node 708).
///
/// IMPORTANT: The serial protocol, reset polarity, timings, challenge bytes,
/// boot image, and asynchronous word encoding in this class are intentionally
/// kept equivalent to the standalone Ga144PortDetector implementation that was
/// verified on real EVB001/EVB002 hardware. Do not replace this with the older
/// IDE probe implementation.
/// </summary>
public sealed class Ga144Node708Probe
{
  public const int DefaultBaudRate = Ga144Serial.MaximumBaudRate;

  private const int ResetAssertMilliseconds = 20;
  // Keep the reset-to-first-byte delay very short. EVB001 can be configured
  // to boot the Host chip from flash, so a long delay would allow the flash
  // boot stream to race with this node-708 probe. FTDI control and data
  // requests are ordered by the driver; one millisecond is sufficient for
  // reset release while still giving the asynchronous boot ROM priority.
  private const int ResetReleaseToBootMilliseconds = 1;
  private const int ProgramStartMilliseconds = 5;
  private const int BaselineTimeoutMilliseconds = 180;
  private const int EchoTimeoutMilliseconds = 750;

  // Exact challenge used by the verified standalone detector.
  private static readonly byte[] Challenge =
  {
        0x55, 0xA3, 0x00, 0xFF, 0x69, 0x96, 0xC7, 0x38,
        0x12, 0xED, 0x7E, 0x81, 0x5A, 0xA5, 0x3C, 0xC3
    };

  // Exact F18A RAM program used by the verified standalone detector.
  //
  //   0: @p b! . .       B := io
  //   1: x0015d
  //   2: @b -if low      read GPIO 17; bit 17 is the sign bit
  //   3: drop @p !b .    high: drive GPIO 1 high
  //   4: x15557
  //   5: jump 2
  //   6: drop @p !b .    low: drive GPIO 1 low
  //   7: x15556
  //   8: jump 2
  private static readonly int[] EchoProgramWords =
  {
        0x04BB2,
        0x0015D,
        0x01206,
        0x3BD22,
        0x15557,
        0x11402,
        0x3BD22,
        0x15556,
        0x11402
    };

  private static readonly byte[] BaselineStream = BuildWordStream(0, 0, 0x3FFFF);
  private static readonly byte[] BootStream = BuildBootStream();

  public Task<ProbeResult> ProbeAsync(
      string portName,
      int baudRate = DefaultBaudRate,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);

    // The proven detector is specifically verified at 921600 baud. Keeping
    // this check prevents a workspace/UI setting from silently changing the
    // on-wire protocol while preserving the existing IDE method signature.
    if (baudRate != DefaultBaudRate)
    {
      return Task.FromResult(ProbeResult.Failure(
          $"The verified node-708 probe requires {DefaultBaudRate} baud.",
          TimeSpan.Zero));
    }

    return Task.Run(() => Probe(portName, cancellationToken), cancellationToken);
  }

  private static ProbeResult Probe(string portName, CancellationToken cancellationToken)
  {
    var stopwatch = Stopwatch.StartNew();
    try
    {
      cancellationToken.ThrowIfCancellationRequested();

      using SerialPort port = Ga144Serial.Create(portName);
      port.ReadTimeout = 40;
      port.WriteTimeout = 1_000;
      port.Open();

      // Immediately after Open, force the reset-released (RTS high) state
      // before any slower setup. With managed SerialPort the RtsEnable
      // initializer is only pushed to the driver after the handle exists,
      // so there is a brief driver-default RTS window at Open; on Port A
      // that is a RESET- glitch. Re-asserting high here minimizes it.
      port.RtsEnable = true;
      port.DtrEnable = true;

      // On Port A, RTS is the EVB host RESET-. The managed SerialPort close
      // path does not guarantee the modem-control lines are left high, so a
      // dispose that drops RTS low would assert RESET- on the live board as
      // the handle closes. Force RTS (and DTR) into the inactive/high,
      // reset-released state on every exit path before the using-dispose
      // runs. This mirrors NativeWindowsSerialPort's close discipline and
      // removes the probe's USB reset-glitch on Port A.
      try
      {
        // GreenArrays' EVB001 arrayForth reset sequence explicitly uses
        // CLRRTS (RTS low) to assert RESET-, then SETRTS (RTS high) to
        // release RESET-. System.IO.Ports maps those states to false/true.
        // DTR is held high to match the board's standard serial setup.
        port.DtrEnable = true;
        port.RtsEnable = false;
        Thread.Sleep(ResetAssertMilliseconds);

        cancellationToken.ThrowIfCancellationRequested();
        port.DiscardInBuffer();
        port.DiscardOutBuffer();

        // Reject a pre-existing hardware/software loopback while the GA144
        // is held in reset. A genuine node 708 cannot echo in this state.
        port.Write(BaselineStream, 0, BaselineStream.Length);
        WaitForTransmitDrain(port, BaselineStream.Length);
        bool echoedWhileReset = ReadUntilSequence(
            port,
            BaselineStream,
            BaselineTimeoutMilliseconds,
            cancellationToken);

        if (echoedWhileReset)
        {
          port.RtsEnable = true;
          stopwatch.Stop();
          return ProbeResult.Failure(
              "The port echoed data while RTS held the GA144 in reset; this is a cable/driver loopback, not proof of node 708.",
              stopwatch.Elapsed);
        }

        cancellationToken.ThrowIfCancellationRequested();
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        port.RtsEnable = true;
        Thread.Sleep(ResetReleaseToBootMilliseconds);

        port.Write(BootStream, 0, BootStream.Length);
        WaitForTransmitDrain(port, BootStream.Length);
        Thread.Sleep(ProgramStartMilliseconds);

        // Ignore any framing debris caused while node 708 changes its TX
        // pin from reset weak-pulldown to actively mirrored idle state.
        port.DiscardInBuffer();

        port.Write(Challenge, 0, Challenge.Length);
        WaitForTransmitDrain(port, Challenge.Length);

        bool echoed = ReadUntilSequence(
            port,
            Challenge,
            EchoTimeoutMilliseconds,
            cancellationToken);
        stopwatch.Stop();

        return echoed
            ? new ProbeResult(
                true,
                "GA144 asynchronous boot node 708 accepted the boot frame and executed the echo probe.",
                stopwatch.Elapsed)
            : ProbeResult.Failure(
                "No valid node-708 echo. Verify J23 and the applicable J20/J22 reset jumper, install J26 NO-BOOT for Host USB A, and confirm that RTS low resets while RTS high releases the chip.",
                stopwatch.Elapsed);
      }
      finally
      {
        // Leave RESET- released (RTS high) and DTR high before the port is
        // disposed, regardless of how the probe body exited. Best-effort:
        // if the device is already gone, there is nothing to restore.
        try { port.RtsEnable = true; } catch { }
        try { port.DtrEnable = true; } catch { }
      }
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (UnauthorizedAccessException ex)
    {
      stopwatch.Stop();
      return ProbeResult.Error("Port is busy or access was denied", stopwatch.Elapsed, ex);
    }
    catch (ArgumentOutOfRangeException ex)
    {
      stopwatch.Stop();
      return ProbeResult.Error(
          $"The driver rejected {Ga144Serial.MaximumBaudRate} baud",
          stopwatch.Elapsed,
          ex);
    }
    catch (TimeoutException ex)
    {
      stopwatch.Stop();
      return ProbeResult.Error("Serial operation timed out", stopwatch.Elapsed, ex);
    }
    catch (IOException ex)
    {
      stopwatch.Stop();
      return ProbeResult.Error("Serial I/O failed", stopwatch.Elapsed, ex);
    }
    catch (InvalidOperationException ex)
    {
      stopwatch.Stop();
      return ProbeResult.Error("Serial port could not be opened", stopwatch.Elapsed, ex);
    }
    catch (ArgumentException ex)
    {
      stopwatch.Stop();
      return ProbeResult.Error("Invalid serial port settings", stopwatch.Elapsed, ex);
    }
  }

  internal static byte[] BuildBootStream()
  {
    // One standard boot frame:
    //   completion address = RAM 0
    //   transfer address   = RAM 0
    //   transfer count     = program word count
    var words = new int[3 + EchoProgramWords.Length];
    words[0] = 0;
    words[1] = 0;
    words[2] = EchoProgramWords.Length;
    Array.Copy(EchoProgramWords, 0, words, 3, EchoProgramWords.Length);

    return BuildWordStream(words);
  }

  private static byte[] BuildWordStream(params int[] words)
  {
    var bytes = new byte[words.Length * 3];
    for (int index = 0; index < words.Length; index++)
    {
      EncodeAsynchronousWord(words[index], bytes.AsSpan(index * 3, 3));
    }

    return bytes;
  }

  internal static void EncodeAsynchronousWord(int word, Span<byte> destination)
  {
    if ((uint)word > 0x3FFFFu)
    {
      throw new ArgumentOutOfRangeException(nameof(word), "An F18 word must be 18 bits.");
    }

    if (destination.Length < 3)
    {
      throw new ArgumentException("Three output bytes are required.", nameof(destination));
    }

    // Exact BOOT-02 asynchronous encoding from the verified detector:
    // byte 0 low six calibration bits are 010010 (0x12), and the F18 word
    // bits are inverted before being placed in the UART bytes.
    uint inverted = (~(uint)word) & 0x3FFFFu;
    destination[0] = (byte)(0x12u | ((inverted & 0x03u) << 6));
    destination[1] = (byte)(inverted >> 2);
    destination[2] = (byte)(inverted >> 10);
  }

  private static bool ReadUntilSequence(
      SerialPort port,
      byte[] expected,
      int timeoutMilliseconds,
      CancellationToken cancellationToken)
  {
    int matched = 0;
    var stopwatch = Stopwatch.StartNew();
    var buffer = new byte[256];

    while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
    {
      cancellationToken.ThrowIfCancellationRequested();

      int available = port.BytesToRead;
      if (available <= 0)
      {
        Thread.Sleep(1);
        continue;
      }

      int read = port.Read(buffer, 0, Math.Min(buffer.Length, available));
      for (int index = 0; index < read; index++)
      {
        byte value = buffer[index];
        if (value == expected[matched])
        {
          matched++;
          if (matched == expected.Length)
          {
            return true;
          }
        }
        else
        {
          // The challenge has no long self-overlap. Retaining a one-
          // byte prefix is sufficient and also tolerates leading noise.
          matched = value == expected[0] ? 1 : 0;
        }
      }
    }

    return false;
  }

  private static void WaitForTransmitDrain(SerialPort port, int byteCount)
  {
    // SerialPort has no portable "TX empty" operation. Allow at least the
    // wire time plus a margin for the FTDI USB latency/buffering path.
    double wireMilliseconds = byteCount * 10_000.0 / port.BaudRate;
    int delay = Math.Max(2, (int)Math.Ceiling(wireMilliseconds) + 3);
    Thread.Sleep(delay);
  }
}