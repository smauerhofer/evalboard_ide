using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>One 'setn' dispatch call: the two words sent (see the class
/// remarks on why there are two, not one) and node 708's own 'readw' echo
/// of each, whether both echoes matched, and how long the whole call
/// took.</summary>
public sealed record Node708SetNodeCallResult(
    int CallNumber,
    int NodeValueSent,
    int TentacleValueSent,
    bool Succeeded,
    byte[]? FirstEchoedBytes,
    byte[]? SecondEchoedBytes,
    string? FailureMessage,
    TimeSpan Elapsed);

/// <summary>Everything one <see cref="Ga144Node708SetNodeProbe.RunSetNodeProbeAsync"/>
/// run produced: one result per 'setn' call, in order.</summary>
public sealed record Node708SetNodeReport(IReadOnlyList<Node708SetNodeCallResult> Calls);

/// <summary>
/// Standalone, pre-Kraken test of node 708's newest 'setn' source -- the
/// version where 'setn' has no terminating ';' of its own and simply falls
/// through into 'sett' (the same fall-through idiom already proven for
/// 'readw'/'oword'): 'setn' compiles to nothing but 'readw drop', so calling
/// its own dispatch address runs THAT, then continues straight into 'sett's
/// own 'readw drop a! ;' with no jump in between -- 'setn' and 'sett' are the
/// literal same instruction stream entered from two different addresses.
///
/// Net effect on the wire: calling 'setn' now receives and echoes TWO words,
/// not one -- the first via 'setn's own 'readw drop' (received, echoed, then
/// its value simply discarded -- this source does not yet contain a '!n'
/// anywhere, so nothing is actually stored into the 'n' f18var by this call),
/// the second via the fallen-into 'sett' body, which stores it into A exactly
/// like calling 'sett' directly would. This probe exists to test exactly
/// that shape on real hardware -- two receive/echo round trips per call, in
/// the right order, with node 708's own dispatch loop -- before wiring it
/// into KrakenSession. It does not (and cannot yet) confirm that a node
/// index survives anywhere, since this source doesn't keep one.
///
/// This program is 'main'/'obit'/'readw'/'oword'/'obyt'/'setn'/'sett' only --
/// 'w/r' deliberately omitted, so a failure here is unambiguously about the
/// setn/sett dispatch and fall-through, not about 'w/r's own body. Boots
/// once, then dispatches to 'setn' with the same pair of values
/// <paramref name="callCount"/> times in a row (default 2, matching the
/// repeat-call shape <see cref="Ga144Node708DispatchProbe"/> already used to
/// catch the earlier B-register bug), recording each call's outcome
/// independently.
///
/// Uses <see cref="NativeWindowsSerialPort"/> (not the ordinary
/// System.IO.Ports.SerialPort the echo probe uses), matching
/// <see cref="Ga144Node708DispatchProbe"/>'s own choice for the same reason:
/// the transport class is controlled for, not left as an unexamined
/// variable.
/// </summary>
public sealed class Ga144Node708SetNodeProbe
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

  public async Task<Node708SetNodeReport> RunSetNodeProbeAsync(
      string portName,
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      int nodeValue,
      int tentacleValue,
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

    (int[] program, int setnAddress) = BuildSetNodeProgram(chip, romLibrary);
    return await Task.Run(
        () => RunSetNodeProbe(portName, program, setnAddress, nodeValue, tentacleValue, callCount, cancellationToken),
        cancellationToken);
  }

  private static Node708SetNodeReport RunSetNodeProbe(
      string portName,
      int[] program,
      int setnAddress,
      int nodeValue,
      int tentacleValue,
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

      var calls = new List<Node708SetNodeCallResult>(callCount);
      for (int callNumber = 1; callNumber <= callCount; callNumber++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        calls.Add(RunOneSetNodeCall(port, setnAddress, nodeValue, tentacleValue, callNumber, cancellationToken));
      }

      return new Node708SetNodeReport(calls);
    }
    finally
    {
      try { port.SetRts(true); } catch { }
      try { port.SetDtr(true); } catch { }
    }
  }

  private static Node708SetNodeCallResult RunOneSetNodeCall(
      NativeWindowsSerialPort port,
      int setnAddress,
      int nodeValue,
      int tentacleValue,
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
      Ga144Node708Probe.EncodeAsynchronousWord(setnAddress, dispatchBytes);
      port.Write(dispatchBytes);
      WaitForTransmitDrain(port, dispatchBytes.Length);
      Thread.Sleep(InterWordSettleMilliseconds);

      // First word: consumed by 'setn's own 'readw drop' -- echoed back but,
      // per this source, its value is not stored anywhere afterward.
      byte[] firstEchoed = SendAndReadEcho(port, nodeValue, cancellationToken);
      int firstDecoded = DecodeObywordReply(firstEchoed);
      int firstExpected = nodeValue & F18InstructionSet.WordMask;
      if (firstDecoded != firstExpected)
      {
        stopwatch.Stop();
        return new Node708SetNodeCallResult(
            callNumber, nodeValue, tentacleValue, Succeeded: false, firstEchoed, SecondEchoedBytes: null,
            $"First echo mismatch (node value): sent 0x{firstExpected:X5}, echoed 0x{firstDecoded:X5}.", stopwatch.Elapsed);
      }

      // Second word: consumed by the fallen-into 'sett' body's own 'readw
      // drop a!' -- echoed back, then stored into A, exactly like calling
      // 'sett' directly would.
      byte[] secondEchoed = SendAndReadEcho(port, tentacleValue, cancellationToken);
      stopwatch.Stop();

      int secondDecoded = DecodeObywordReply(secondEchoed);
      int secondExpected = tentacleValue & F18InstructionSet.WordMask;
      if (secondDecoded != secondExpected)
      {
        return new Node708SetNodeCallResult(
            callNumber, nodeValue, tentacleValue, Succeeded: false, firstEchoed, secondEchoed,
            $"Second echo mismatch (tentacle value): sent 0x{secondExpected:X5}, echoed 0x{secondDecoded:X5}.", stopwatch.Elapsed);
      }

      return new Node708SetNodeCallResult(
          callNumber, nodeValue, tentacleValue, Succeeded: true, firstEchoed, secondEchoed, FailureMessage: null, stopwatch.Elapsed);
    }
    catch (TimeoutException exception)
    {
      stopwatch.Stop();
      return new Node708SetNodeCallResult(
          callNumber, nodeValue, tentacleValue, Succeeded: false, FirstEchoedBytes: null, SecondEchoedBytes: null,
          exception.Message, stopwatch.Elapsed);
    }
  }

  private static byte[] SendAndReadEcho(NativeWindowsSerialPort port, int value, CancellationToken cancellationToken)
  {
    byte[] valueBytes = new byte[3];
    Ga144Node708Probe.EncodeAsynchronousWord(value, valueBytes);
    port.Write(valueBytes);
    WaitForTransmitDrain(port, valueBytes.Length);
    return ReadExactly(port, 3, ResponseTimeoutMilliseconds, cancellationToken);
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
  /// Compiles 'main'/'obit'/'readw'/'oword'/'obyt'/'setn'/'sett' -- 'w/r'
  /// deliberately omitted, since 'setn' falls into 'sett' and never into
  /// 'w/r' (which starts its own fresh definition after 'sett's own ';') --
  /// against node 708's REAL, currently configured ROM exports, exactly the
  /// way <c>KrakenSession.BuildHeadProgram</c> does. 'main' is defined first
  /// so it lands at RAM address 0, matching the boot frame's transfer
  /// address (0x000) below.
  /// </summary>
  private static (int[] Program, int SetNodeAddress) BuildSetNodeProgram(Ga144ChipConfiguration chip, Ga144RomLibrary romLibrary)
  {
    const string source = """
        # 0 org
        entry main

        : main 18ibits drop >r ex main ;
        : obit ( dwn-dw) !b over >r delay ;
        : readw ( -dwx) dup 18ibits drop over over
        : oword ( dw-d)  leap drop  leap drop leap drop  drop ;
        : obyt ( dw-dwx)  then then then  3 obit drop
            7 for dup 1 and 3 xor obit  drop 2/ next
            2 obit ;
        : setn .loc readw drop // set node, used to select node in a tentacle
        # -1 f18var n // target node(-1..last node-1)
        : sett .loc readw drop a! ; // set A register, used to select tentacle
        .loc
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
      throw new InvalidOperationException("The node-708 setn probe did not compile.\n" + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException($"The node-708 setn probe requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (!result.Symbols.TryGetValue("setn", out F18ExportedSymbol? setnSymbol) || setnSymbol is null)
    {
      throw new InvalidOperationException("The node-708 setn probe did not export a 'setn' symbol.");
    }

    int[] words = result.Words.Select(item => item & F18InstructionSet.WordMask).ToArray();
    return (words, setnSymbol.Value);
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
      throw new TimeoutException($"Node 708 setn probe timed out after receiving {offset} of {count} bytes.");
    }

    return result;
  }

  private static void WaitForTransmitDrain(NativeWindowsSerialPort port, int byteCount)
  {
    double milliseconds = (byteCount * 10.0 * 1000.0 / port.BaudRate) + 3.0;
    Thread.Sleep((int)Math.Ceiling(milliseconds));
  }
}