using System.Diagnostics;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>Everything one <see cref="Ga144LegacyKrakenErectionProbe.RunLegacyErectionProbeAsync"/>
/// run produced: whether the old-method erection of the real, full three-tentacle
/// topology completed, and the outcome of two old-style, carrier-clocked
/// verification reads performed afterward -- one against node 301 (a control,
/// already known responsive under the CURRENT protocol, to prove the old read
/// mechanism itself works on this hardware/session) and one against node 300
/// (the node actually in question).</summary>
public sealed record Node708LegacyKrakenReport(
    bool ErectionCompleted,
    string? ErectionFailureMessage,
    bool ControlReadSucceeded,
    int? ControlReadValue,
    string? ControlReadFailureMessage,
    bool TargetReadSucceeded,
    int? TargetReadValue,
    string? TargetReadFailureMessage,
    TimeSpan Elapsed);

/// <summary>
/// Erects the real, full Kraken topology (all three tentacles, 143 nodes) using
/// the OLD, pre-redesign method found in the uploaded old-code zip
/// (Ga144EvalboardIde/src/Ga144.Evb.Ide/Services/KrakenSession.cs's ErectOnto and
/// KrakenProtocol.cs's WrapForward/BuildX1/BuildW1/BuildR1), completely
/// independent of the current implementation (KrakenSession.ErectOnto,
/// KrakenProtocol.BuildFocus/BuildWriteB, and the 'main'/'sett'/'w/r' head
/// program are none of this class's business -- this reimplements the old
/// on-chip and host-side mechanism from scratch, in its own file).
///
/// Old-method erection loads node 708 with an entirely different program (a
/// hand-authored 'reply' RAM word that answers reads bit-by-bit over a
/// carrier-clocked protocol -- see BuildReplyProgram) and never verifies any
/// reply during the tentacle-building focus/writeB frames themselves
/// (SendBootFrame is fire-and-forget). Success was historically inferred only
/// from a LATER, separate read using the fixed 18-bit carrier-clocked
/// WriteRequestAndRead mechanism -- a fundamentally different, hardware-level
/// transport from the current 'readw'/'oword'/'obyt' software echo scheme.
///
/// This probe reproduces that exact history: it erects for real (fire-and-
/// forget focus/writeB across all 143 nodes, exactly like old ErectOnto), then
/// performs that same old-style carrier-clocked read against node 301 (control)
/// and node 300 (target), each reading back the node's own 'a' register via
/// KrakenProtocol-old's BuildR1(position, ReadAInstruction) -- the same
/// verification old ConnectAndErect used to run before ever reporting a session
/// online. If node 300's read succeeds here where every current-protocol
/// attempt has failed, the old on-chip 'reply' mechanism reaches node 300 where
/// the new 'w/r' mechanism does not. If it also times out, "the old code
/// worked" was the same unverified-erection false positive already suspected
/// (old ErectOnto's own focus/writeB frames never checked a reply either).
///
/// Self-contained: opens its own NativeWindowsSerialPort, performs its own
/// reset and old-protocol boot, and closes when done. Does NOT touch
/// KrakenController/KrakenSession or mark any Kraken as installed/live --
/// node 708 ends this probe running the OLD 'reply' program, which the current
/// implementation's read/write code (built for 'readw'/'oword'/'obyt') cannot
/// talk to. Any subsequent normal operation resets the chip and loads its own
/// program first, exactly as every other probe in this file already does.
/// </summary>
public sealed class Ga144LegacyKrakenErectionProbe
{
  public const int DefaultBaudRate = Ga144Serial.MaximumBaudRate;

  // G144A12 node 708 asynchronous boot ROM continuation entry -- ser-exec, the
  // documented concatenation path for additional frames after the first.
  private const int AsyncSerialContinuationAddress = 0x0AE;
  private const int IoAddress = 0x15D;
  private const int ResetAssertMilliseconds = 20;
  private const int ResetReleaseMilliseconds = 1;
  private const int ResponseTimeoutMilliseconds = 1_000;
  private const int OnlineTransactionSettleMilliseconds = 5;

  public async Task<Node708LegacyKrakenReport> RunLegacyErectionProbeAsync(
      string portName,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);

    int[] replyProgram = BuildReplyProgram();
    KrakenConfiguration configuration = KrakenConfiguration.CreateFixed();
    return await Task.Run(
        () => RunLegacyErectionProbe(portName, replyProgram, configuration, cancellationToken),
        cancellationToken);
  }

  private static Node708LegacyKrakenReport RunLegacyErectionProbe(
      string portName,
      int[] replyProgram,
      KrakenConfiguration configuration,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var stopwatch = Stopwatch.StartNew();

    using NativeWindowsSerialPort port = NativeWindowsSerialPort.Open(
        portName,
        DefaultBaudRate,
        readTimeoutMilliseconds: 50,
        writeTimeoutMilliseconds: 2_000);

    try
    {
      try
      {
        ErectOnto(port, replyProgram, configuration, cancellationToken);
      }
      catch (Exception exception) when (exception is IOException or TimeoutException)
      {
        stopwatch.Stop();
        return new Node708LegacyKrakenReport(
            false, $"Old-method erection failed: {exception.Message}",
            false, null, null, false, null, null, stopwatch.Elapsed);
      }

      IReadOnlyDictionary<int, KrakenNodeRoute> routes = KrakenTopology.BuildRouteMap(configuration);

      bool controlSucceeded;
      int? controlValue = null;
      string? controlFailure = null;
      try
      {
        controlValue = ReadWordLegacy(port, configuration, routes[301], cancellationToken);
        controlSucceeded = true;
      }
      catch (Exception exception) when (exception is IOException or TimeoutException)
      {
        controlSucceeded = false;
        controlFailure = exception.Message;
      }

      bool targetSucceeded;
      int? targetValue = null;
      string? targetFailure = null;
      try
      {
        targetValue = ReadWordLegacy(port, configuration, routes[300], cancellationToken);
        targetSucceeded = true;
      }
      catch (Exception exception) when (exception is IOException or TimeoutException)
      {
        targetSucceeded = false;
        targetFailure = exception.Message;
      }

      stopwatch.Stop();
      return new Node708LegacyKrakenReport(
          true, null,
          controlSucceeded, controlValue, controlFailure,
          targetSucceeded, targetValue, targetFailure,
          stopwatch.Elapsed);
    }
    finally
    {
      try { port.SetRts(true); } catch { }
      try { port.SetDtr(true); } catch { }
    }
  }

  // Verbatim port of old KrakenSession.ErectOnto: reset, load the 'reply'
  // helper into node 708's RAM, then fire-and-forget every tentacle's
  // focus/writeB boot frame across the real, fixed topology. No reply is ever
  // read here -- exactly as old code never checked one during erection.
  private static void ErectOnto(
      NativeWindowsSerialPort port,
      int[] replyProgram,
      KrakenConfiguration configuration,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    port.SetDtr(true);
    port.SetRts(false);
    Thread.Sleep(ResetAssertMilliseconds);
    port.PurgeInputOutput();
    port.SetRts(true);
    Thread.Sleep(ResetReleaseMilliseconds);

    SendBootFrame(port, AsyncSerialContinuationAddress, 0x000, replyProgram);

    foreach (KrakenTentacleConfiguration tentacle in configuration.Tentacles.OrderBy(item => item.Number))
    {
      int tentacleHeadPort = KrakenTopology.PortAddress(KrakenTopology.HeadCoordinate, tentacle.Nodes[0]);
      for (int position = 0; position < tentacle.Nodes.Count; position++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        int coordinate = tentacle.Nodes[position];
        int previous = position == 0
            ? KrakenTopology.HeadCoordinate
            : tentacle.Nodes[position - 1];

        int incomingPort = KrakenTopology.PortAddress(coordinate, previous);
        int focusJump = F18InstructionSet.EncodeSlot0Control(0x02, incomingPort);
        IReadOnlyList<int> focusSequence = LegacyKrakenProtocol.BuildX1(position, focusJump);
        SendBootFrame(port, AsyncSerialContinuationAddress, tentacleHeadPort, focusSequence);

        int b = position + 1 < tentacle.Nodes.Count
            ? KrakenTopology.PortAddress(coordinate, tentacle.Nodes[position + 1])
            : IoAddress;
        IReadOnlyList<int> bSequence = LegacyKrakenProtocol.BuildW1(position, LegacyKrakenProtocol.WriteBInstruction, b);
        SendBootFrame(port, AsyncSerialContinuationAddress, tentacleHeadPort, bSequence);
      }
    }

    SettleUsb(OnlineTransactionSettleMilliseconds, cancellationToken);
    port.PurgeInput();
  }

  // Verbatim port of old KrakenSession.ReadWord: a request frame (completion
  // address 0, so node 708 jumps straight into 'reply' on receipt) carrying a
  // BuildR1-wrapped read-A sequence for the target node, followed by 18 pairs
  // of 0x00/0xFF carrier bytes -- one pair per bit -- which the running
  // 'reply' word mirrors back bit-for-bit as it walks the node's answer.
  private static int ReadWordLegacy(
      NativeWindowsSerialPort port,
      KrakenConfiguration configuration,
      KrakenNodeRoute route,
      CancellationToken cancellationToken)
  {
    int headPort = HeadPortFor(configuration, route);
    IReadOnlyList<int> sequence = LegacyKrakenProtocol.BuildR1(route.Position, LegacyKrakenProtocol.ReadAInstruction);
    byte[] frame = EncodeBootFrame(completionAddress: 0, transferAddress: headPort, sequence);

    byte[] carriers = new byte[36];
    for (int bit = 0; bit < 18; bit++)
    {
      carriers[bit * 2] = 0x00;
      carriers[bit * 2 + 1] = 0xFF;
    }

    port.Write(frame);
    WaitForTransmitDrain(port, frame.Length);
    port.Write(carriers);
    WaitForTransmitDrain(port, carriers.Length);
    byte[] response = ReadExactly(port, 18, ResponseTimeoutMilliseconds, cancellationToken);

    int word = 0;
    for (int bit = 0; bit < 18; bit++)
    {
      if (response[bit] >= 0x80)
      {
        word |= 1 << bit;
      }
    }

    SettleUsb(OnlineTransactionSettleMilliseconds, cancellationToken);
    return word & F18InstructionSet.WordMask;
  }

  private static int HeadPortFor(KrakenConfiguration configuration, KrakenNodeRoute route)
  {
    KrakenTentacleConfiguration tentacle = configuration.Tentacles.Single(item => item.Number == route.TentacleNumber);
    return KrakenTopology.PortAddress(KrakenTopology.HeadCoordinate, tentacle.Nodes[0]);
  }

  private static void SendBootFrame(NativeWindowsSerialPort port, int completionAddress, int transferAddress, IReadOnlyList<int> payload)
  {
    byte[] frame = EncodeBootFrame(completionAddress, transferAddress, payload);
    port.Write(frame);
    WaitForTransmitDrain(port, frame.Length);
    SettleUsb(OnlineTransactionSettleMilliseconds, CancellationToken.None);
  }

  private static byte[] EncodeBootFrame(int completionAddress, int transferAddress, IReadOnlyList<int> payload)
  {
    var words = new int[3 + payload.Count];
    words[0] = completionAddress & F18InstructionSet.WordMask;
    words[1] = transferAddress & F18InstructionSet.WordMask;
    words[2] = payload.Count & F18InstructionSet.WordMask;
    for (int index = 0; index < payload.Count; index++)
    {
      words[3 + index] = payload[index] & F18InstructionSet.WordMask;
    }

    var bytes = new byte[words.Length * 3];
    for (int index = 0; index < words.Length; index++)
    {
      Ga144Node708Probe.EncodeAsynchronousWord(words[index], bytes.AsSpan(index * 3, 3));
    }

    return bytes;
  }

  private static byte[] ReadExactly(NativeWindowsSerialPort port, int count, int timeoutMilliseconds, CancellationToken cancellationToken)
  {
    var result = new byte[count];
    int offset = 0;
    Stopwatch stopwatch = Stopwatch.StartNew();
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
      throw new TimeoutException($"Legacy Kraken read timed out after receiving {offset} of {count} carrier-clocked bytes.");
    }

    return result;
  }

  private static void SettleUsb(int milliseconds, CancellationToken cancellationToken)
  {
    if (milliseconds <= 0)
    {
      return;
    }

    int remaining = milliseconds;
    while (remaining > 0)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int slice = Math.Min(remaining, 5);
      Thread.Sleep(slice);
      remaining -= slice;
    }
  }

  private static void WaitForTransmitDrain(NativeWindowsSerialPort port, int byteCount)
  {
    double milliseconds = (byteCount * 10.0 * 1000.0 / port.BaudRate) + 3.0;
    Thread.Sleep((int)Math.Ceiling(milliseconds));
  }

  // Verbatim port of old KrakenSession.BuildReplyProgram's F18 source -- the
  // node-708 RAM program old-method erection loads at RAM 0. Transcribed
  // exactly as found in the old code; not rewritten or "fixed" in any way.
  private static int[] BuildReplyProgram()
  {
    string source = $$"""
            # 0 org
            entry reply

            : reply
                io b!
                lo
                @
                17 for
                    dup dup 2/ 2* xor
                    if send-one else send-zero then
                    drop 2/
                next
                drop
                lo
                jump 0x0AE
            ;

            : hi 0x15557 !b ;
            : lo 0x15556 !b ;

            : wait-high
                begin
                    @b
                    -if
                        drop exit
                    then
                    drop
                again
            ;

            : wait-low
                begin
                    @b
                    -if
                        drop
                    else
                        drop exit
                    then
                again
            ;

            : consume wait-high wait-low ;

            : send-zero
                wait-high hi wait-low lo
                consume
            ;

            : send-one
                consume
                wait-high hi wait-low lo
            ;
            """;

    var compiler = new F18Compiler();
    var options = new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = KrakenTopology.HeadCoordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      MacroLookupScope = F18MacroLookupScope.UserAndSystem,
      PackControlTransfers = false
    };
    F18CompileResult result = compiler.Compile(source, options);
    if (!result.Success)
    {
      string diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.ToString()));
      throw new InvalidOperationException("The legacy-erection probe's node-708 reply helper did not compile.\n" + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException($"The legacy-erection probe's node-708 reply helper requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (result.EntryPoint != 0)
    {
      int selectedEntry = result.EntryPoint ?? -1;
      throw new InvalidOperationException($"The legacy-erection probe's node-708 reply helper must enter at RAM 0, but the compiler selected 0x{selectedEntry:X3}.");
    }

    return result.Words.Select(item => item & F18InstructionSet.WordMask).ToArray();
  }
}

/// <summary>
/// Verbatim port of the old KrakenProtocol.cs's x1/w1/r1 stream builders --
/// the host-precomputed relay-wrapper opcodes used by old-method erection and
/// old-method reads. Kept private to this file and distinct from the CURRENT
/// KrakenProtocol (BuildFocus/BuildWriteB), which this probe does not use.
/// </summary>
internal static class LegacyKrakenProtocol
{
  private static readonly int PumpPrefix = Pack("@p", ">r");
  private static readonly int PumpBody = Pack("@p", "!b", "unext");
  private static readonly int ReturnHop = Pack("@b", "!p");

  public static int WriteBInstruction { get; } = Pack("@p", "b!");
  public static int ReadAInstruction { get; } = Pack("a", "!p");

  public static IReadOnlyList<int> BuildX1(int position, int instruction) =>
      WrapForward(position, [Mask(instruction)], appendReturnHop: false);

  public static IReadOnlyList<int> BuildW1(int position, int instruction, int value) =>
      WrapForward(position, [Mask(instruction), Mask(value)], appendReturnHop: false);

  public static IReadOnlyList<int> BuildR1(int position, int instruction) =>
      WrapForward(position, [Mask(instruction)], appendReturnHop: true);

  private static IReadOnlyList<int> WrapForward(int position, IReadOnlyList<int> leaf, bool appendReturnHop)
  {
    if (position < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(position));
    }

    var stream = new List<int>(leaf);
    for (int hop = 0; hop < position; hop++)
    {
      int forwardCountMinusOne = stream.Count - 1;
      var wrapped = new List<int>(stream.Count + (appendReturnHop ? 4 : 3))
            {
                PumpPrefix,
                Mask(forwardCountMinusOne),
                PumpBody
            };
      wrapped.AddRange(stream);
      if (appendReturnHop)
      {
        wrapped.Add(ReturnHop);
      }

      stream = wrapped;
    }

    return stream;
  }

  private static int Pack(params string[] names)
  {
    var opcodes = new List<byte>(names.Length);
    foreach (string name in names)
    {
      if (!F18InstructionSet.Opcodes.TryGetValue(name, out byte opcode))
      {
        throw new InvalidOperationException($"Unknown F18 opcode '{name}'.");
      }

      opcodes.Add(opcode);
    }

    return F18InstructionSet.EncodePackedInstruction(opcodes);
  }

  private static int Mask(int value) => value & F18InstructionSet.WordMask;
}
