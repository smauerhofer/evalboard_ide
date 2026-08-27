using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>One 'sett' dispatch call: the dispatch (call) address and port
/// value sent, whether node 708 echoed the port value back correctly (via
/// 'readw'), what it actually echoed (if anything), and how long the whole
/// call took.</summary>
public sealed record Node708DispatchCallResult(
    int CallNumber,
    int PortValueSent,
    bool Succeeded,
    byte[]? EchoedBytes,
    string? FailureMessage,
    TimeSpan Elapsed);

/// <summary>Everything one <see cref="Ga144Node708DispatchProbe.RunDispatchProbeAsync"/>
/// run produced: one result per 'sett' call, in order.</summary>
public sealed record Node708DispatchReport(IReadOnlyList<Node708DispatchCallResult> Calls);

/// <summary>
/// Standalone, pre-Kraken test of node 708's own <c>main</c>/<c>sett</c>
/// dispatch loop -- NOT node 708's direct-UART transmit routines (that is
/// what <see cref="Ga144Node708EchoProbe"/> already proved correct via
/// <c>readw</c>). This probe exists because a hardware test found something
/// <see cref="Ga144Node708EchoProbe"/> could not: calling the original
/// (B-targeting) <c>sett</c> twice in a row through Kraken's own
/// <c>main</c> dispatch loop -- the exact same call, with the exact same
/// value, that just succeeded -- timed out (0 of 3 bytes) on the SECOND
/// call. Root cause: <c>main</c>'s own dispatch-address read (bare
/// <c>18ibits</c>) and <c>readw</c>'s receive both poll via <c>sync</c>'s
/// <c>@b</c>, which only works while B still points at the io register (its
/// value on reset). The original <c>sett</c> pointed B at the tentacle
/// instead, so after the FIRST 'sett' call, every subsequent receive polled
/// the tentacle port instead of the host UART -- total silence, forever.
/// <c>sett</c> now targets A instead (see the source below), leaving B
/// untouched for node 708's own receive; this probe now exists to CONFIRM
/// that fix in isolation, decoupled from 'w/r's own complexity.
///
/// This program is 'main'/'obit'/'readw'/'oword'/'obyt'/'sett' only --
/// 'setn'/'dec'/'w/r' deliberately omitted, so a failure here is
/// unambiguously about the dispatch loop, not about 'w/r's own body.
/// Boots once, then dispatches to 'sett' with the SAME port value
/// <paramref name="callCount"/> times in a row (default 2, matching the
/// hardware test that surfaced this), recording each call's outcome
/// independently so a pass/fail pattern (e.g. "1st passes, 2nd+ fail") is
/// visible rather than just an aggregate.
///
/// Uses <see cref="NativeWindowsSerialPort"/> (not the ordinary
/// System.IO.Ports.SerialPort the echo probe uses) so the transport class
/// itself -- the one real difference between this probe and
/// <see cref="Ga144Node708EchoProbe"/> -- is controlled for rather than
/// left as an unexamined variable.
/// </summary>
public sealed class Ga144Node708DispatchProbe
{
  public const int DefaultBaudRate = Ga144Serial.MaximumBaudRate;

  private const int ResetAssertMilliseconds = 20;
  private const int ResetReleaseToBootMilliseconds = 1;
  private const int ProgramStartMilliseconds = 5;
  private const int ResponseTimeoutMilliseconds = 1_000;

  // Same value KrakenSession.SendWord708 sleeps after every unacknowledged
  // dispatch-address send -- reused here unchanged so this probe paces the
  // dispatch word exactly the way the real erection does.
  private const int InterWordSettleMilliseconds = 20;

  public async Task<Node708DispatchReport> RunDispatchProbeAsync(
      string portName,
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      int portValue,
      int callCount = 2,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    ArgumentNullException.ThrowIfNull(chip);
    ArgumentNullException.ThrowIfNull(romLibrary);
    if (callCount < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(callCount), "At least one dispatch call is required.");
    }

    (int[] program, int settAddress) = BuildDispatchProgram(chip, romLibrary);
    return await Task.Run(
        () => RunDispatchProbe(portName, program, settAddress, portValue, callCount, cancellationToken),
        cancellationToken);
  }

  private static Node708DispatchReport RunDispatchProbe(
      string portName,
      int[] program,
      int settAddress,
      int portValue,
      int callCount,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();

    using NativeWindowsSerialPort port = NativeWindowsSerialPort.Open(
        portName,
        DefaultBaudRate,
        readTimeoutMilliseconds: 50,
        writeTimeoutMilliseconds: 2_000);

    try
    {
      port.SetDtr(true);
      port.SetRts(false);
      Thread.Sleep(ResetAssertMilliseconds);

      cancellationToken.ThrowIfCancellationRequested();
      port.PurgeInputOutput();

      port.SetRts(true);
      Thread.Sleep(ResetReleaseToBootMilliseconds);

      byte[] bootStream = BuildBootStream(program);
      port.Write(bootStream);
      WaitForTransmitDrain(port, bootStream.Length);
      Thread.Sleep(ProgramStartMilliseconds);

      port.PurgeInput();

      var calls = new List<Node708DispatchCallResult>(callCount);
      for (int callNumber = 1; callNumber <= callCount; callNumber++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        calls.Add(RunOneDispatchCall(port, settAddress, portValue, callNumber, cancellationToken));
      }

      return new Node708DispatchReport(calls);
    }
    finally
    {
      try { port.SetRts(true); } catch { }
      try { port.SetDtr(true); } catch { }
    }
  }

  private static Node708DispatchCallResult RunOneDispatchCall(
      NativeWindowsSerialPort port,
      int settAddress,
      int portValue,
      int callNumber,
      CancellationToken cancellationToken)
  {
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    try
    {
      // Unacknowledged dispatch-address send -- identical shape to
      // KrakenSession.SendWord708, deliberately not verified: 'main' reads
      // this through bare '18ibits', never through 'readw'.
      byte[] dispatchBytes = new byte[3];
      Ga144Node708Probe.EncodeAsynchronousWord(settAddress, dispatchBytes);
      port.Write(dispatchBytes);
      WaitForTransmitDrain(port, dispatchBytes.Length);
      Thread.Sleep(InterWordSettleMilliseconds);

      // The port value, read (and echoed) by 'sett' via 'readw' -- this is
      // the acknowledged half, mirroring KrakenSession.SelectTentacle708.
      byte[] valueBytes = new byte[3];
      Ga144Node708Probe.EncodeAsynchronousWord(portValue, valueBytes);
      port.Write(valueBytes);
      WaitForTransmitDrain(port, valueBytes.Length);

      byte[] echoed = ReadExactly(port, 3, ResponseTimeoutMilliseconds, cancellationToken);
      stopwatch.Stop();

      int decoded = DecodeObywordReply(echoed);
      int expected = portValue & F18InstructionSet.WordMask;
      if (decoded != expected)
      {
        return new Node708DispatchCallResult(
            callNumber, portValue, Succeeded: false, echoed,
            $"Echo mismatch: sent 0x{expected:X5}, echoed 0x{decoded:X5}.", stopwatch.Elapsed);
      }

      return new Node708DispatchCallResult(callNumber, portValue, Succeeded: true, echoed, FailureMessage: null, stopwatch.Elapsed);
    }
    catch (TimeoutException exception)
    {
      stopwatch.Stop();
      return new Node708DispatchCallResult(callNumber, portValue, Succeeded: false, EchoedBytes: null, exception.Message, stopwatch.Elapsed);
    }
  }

  private static int DecodeObywordReply(byte[] threeBytes)
  {
    int value = threeBytes[0] | (threeBytes[1] << 8) | ((threeBytes[2] & 0x03) << 16);
    return value & F18InstructionSet.WordMask;
  }

  private static byte[] BuildBootStream(int[] program)
  {
    var words = new int[3 + program.Length];
    words[0] = 0;
    words[1] = 0;
    words[2] = program.Length;
    Array.Copy(program, 0, words, 3, program.Length);

    var bytes = new byte[words.Length * 3];
    for (int index = 0; index < words.Length; index++)
    {
      Ga144Node708Probe.EncodeAsynchronousWord(words[index], bytes.AsSpan(index * 3, 3));
    }

    return bytes;
  }

  /// <summary>
  /// Compiles 'main'/'obit'/'readw'/'oword'/'obyt'/'sett' -- deliberately
  /// omitting 'setn'/'dec'/'w/r' -- against node 708's REAL, currently
  /// configured ROM exports, exactly the way
  /// <c>KrakenSession.BuildHeadProgram</c> does. 'main' is defined first so
  /// it lands at RAM address 0, matching the boot frame's transfer address
  /// (0x000) below.
  /// </summary>
  private static (int[] Program, int SettAddress) BuildDispatchProgram(Ga144ChipConfiguration chip, Ga144RomLibrary romLibrary)
  {
    const string source = """
        # 0 org
        entry main

        : main 18ibits drop >r ex main ;
        : obit ( dwn-dw) !b over >r delay ;
        : readw dup 18ibits drop over over
        : oword ( dw-d)  leap drop  leap drop leap drop  drop ;
        : obyt ( dw-dwx)  then then then  3 obit drop
            7 for dup 1 and 3 xor obit  drop 2/ next
            2 obit ;
        : sett readw drop a! ;
        """;

    var compileService = new F18NodeCompilationService(chip, romLibrary, romLibrary.SystemMacros);
    F18NodeCompilationResult nodeResult = compileService.CompileNode(KrakenTopology.HeadCoordinate);

    if (!nodeResult.Rom.Success)
    {
      string diagnostics = string.Join(Environment.NewLine, nodeResult.Rom.Diagnostics.Select(item => item.ToString()));
      throw new InvalidOperationException("Node 708's ROM source did not compile.\n" + diagnostics);
    }

    var options = new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = KrakenTopology.HeadCoordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      PredefinedConstants = nodeResult.Rom.Constants,
      PredefinedSymbols = nodeResult.Rom.Symbols,
      MacroLookupScope = F18MacroLookupScope.UserAndSystem,
      PackControlTransfers = true
    };

    F18CompileResult result = new F18Compiler().Compile(source, options);
    if (!result.Success)
    {
      string diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.ToString()));
      throw new InvalidOperationException("The node-708 dispatch probe did not compile.\n" + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException($"The node-708 dispatch probe requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (!result.Symbols.TryGetValue("sett", out F18ExportedSymbol? settSymbol) || settSymbol is null)
    {
      throw new InvalidOperationException("The node-708 dispatch probe did not export a 'sett' symbol.");
    }

    int[] words = result.Words.Select(item => item & F18InstructionSet.WordMask).ToArray();
    return (words, settSymbol.Value);
  }

  private static byte[] ReadExactly(NativeWindowsSerialPort port, int count, int timeoutMilliseconds, CancellationToken cancellationToken)
  {
    var result = new byte[count];
    int offset = 0;
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    while (offset < count && stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int read = port.Read(result, offset, count - offset);
      if (read > 0)
      {
        offset += read;
      }
    }

    if (offset != count)
    {
      throw new TimeoutException($"Node 708 dispatch probe timed out after receiving {offset} of {count} bytes.");
    }

    return result;
  }

  private static void WaitForTransmitDrain(NativeWindowsSerialPort port, int byteCount)
  {
    double milliseconds = (byteCount * 10.0 * 1000.0 / port.BaudRate) + 3.0;
    Thread.Sleep((int)Math.Ceiling(milliseconds));
  }
}