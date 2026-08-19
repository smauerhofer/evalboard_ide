using System.Diagnostics;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>Everything one <see cref="Ga144Node708AlternateRelayProbe.RunAlternateRelayProbeAsync"/>
/// run produced: whether erecting the real chain up to node 301 succeeded,
/// whether the redirected 'writeB' at node 301 was acknowledged, and whether
/// the final alternate-target 'focus' (aimed at node 401 instead of node
/// 300, at the SAME relay depth) came back with a reply.</summary>
public sealed record Node708AlternateRelayReport(
    bool ChainToNode301Succeeded,
    string? ChainFailureMessage,
    bool RedirectedWriteBSucceeded,
    string? RedirectedWriteBFailureMessage,
    bool AlternateFocusSucceeded,
    string? AlternateFocusFailureMessage,
    TimeSpan Elapsed);

/// <summary>
/// Isolates whether the node-300 (tentacle 1, position 31) failure belongs to
/// node 300 itself, or to node 301's own ability to relay a word onward via
/// its 'B' register at that same depth. Erects tentacle 1 for real out to
/// node 301 (positions 0-29, identical to <see cref="KrakenSession.ErectOnto"/>),
/// focuses node 301 normally, but then -- instead of the real 'writeB'
/// pointing node 301's B at node 300 -- points it at node 401 instead: a
/// real, physically adjacent neighbor of node 301 (one row north) that was
/// ALSO independently erected and verified very early in the SAME tentacle
/// (position 1), long before node 300 was ever a concern. A final 'focus'
/// call is then sent through the exact same relay depth (n=30, 31 layers --
/// matching the real node-300 call bit for bit) but with node 401's own
/// incoming-port value as the payload instead of node 300's.
///
/// If this succeeds, node 301's relay-out-via-B mechanism is proven healthy
/// at this exact depth, and the fault is isolated to node 300 itself (its
/// own reply, or its own reaction to 'focus'). If it ALSO fails, the fault
/// is upstream of node 300 -- something about node 301's own relay, or
/// about 31-layer-deep construction on this specific path, independent of
/// which node is on the receiving end.
///
/// Self-contained, following the same pattern as the other node-708 probes:
/// its own NativeWindowsSerialPort connection, its own compile of the exact
/// current 'main'/'obit'/'readw'/'oword'/'obyt'/'sett'/'w/r' source (must
/// match <see cref="KrakenSession.BuildHeadProgram"/> verbatim to exercise
/// the real on-chip relay-wrapper mechanism), and its own low-level
/// word-transport helpers -- does not reuse KrakenSession's private methods.
/// </summary>
public sealed class Ga144Node708AlternateRelayProbe
{
  public const int DefaultBaudRate = Ga144Serial.MaximumBaudRate;

  private const int ResetAssertMilliseconds = 20;
  private const int ResetReleaseMilliseconds = 1;
  private const int ProgramStartMilliseconds = 5;
  private const int ResponseTimeoutMilliseconds = 1_000;
  private const int InterWordSettleMilliseconds = 20;

  // Real tentacle-1 coordinates, positions 0 through 30 (node 707 down to
  // node 301) -- copied from KrakenTopology.CreateDefaultTentacles's own
  // Tentacle1Nodes so this probe does not depend on constructing a
  // KrakenConfiguration, only on matching the real, fixed topology exactly
  // for the portion this test actually erects.
  private static readonly int[] Tentacle1UpToNode301 =
  [
    707, 706, 705, 704, 703, 702, 701, 700,
    600, 601, 602, 603, 604, 605,
    505, 504, 503, 502, 501, 500,
    400, 401, 402, 403, 404, 405,
    305, 304, 303, 302, 301
  ];

  // Node 401's own incoming port facing node 301 -- KrakenTopology.PortAddress(401, 301) --
  // and node 301's own port facing node 401 -- KrakenTopology.PortAddress(301, 401) --
  // both hand-derived and cross-checked against KrakenTopology's parity table
  // (both resolve to the same local address, 0x145, the same way the
  // 301<->300 boundary resolves to a single shared value on both ends).
  private const int Node301ToNode401Port = 0x145;
  private const int Node401ToNode301Port = 0x145;

  public async Task<Node708AlternateRelayReport> RunAlternateRelayProbeAsync(
      string portName,
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    ArgumentNullException.ThrowIfNull(chip);
    ArgumentNullException.ThrowIfNull(romLibrary);

    (int[] program, Node708AlternateRelayAddresses addresses) = BuildHeadProgram(chip, romLibrary);
    return await Task.Run(
        () => RunAlternateRelayProbe(portName, program, addresses, cancellationToken),
        cancellationToken);
  }

  private static Node708AlternateRelayReport RunAlternateRelayProbe(
      string portName,
      int[] program,
      Node708AlternateRelayAddresses addresses,
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
      port.SetDtr(true);
      port.SetRts(false);
      Thread.Sleep(ResetAssertMilliseconds);

      cancellationToken.ThrowIfCancellationRequested();
      port.PurgeInputOutput();

      port.SetRts(true);
      Thread.Sleep(ResetReleaseMilliseconds);

      byte[] bootStream = BuildBootStream(program);
      port.Write(bootStream);
      WaitForTransmitDrain(port, bootStream.Length);
      Thread.Sleep(ProgramStartMilliseconds);

      port.PurgeInput();

      int tentacle1HeadPort = KrakenTopology.PortAddress(KrakenTopology.HeadCoordinate, Tentacle1UpToNode301[0]);
      SendWord708(port, addresses.SetTentacle);
      SendWord708AndVerify(port, tentacle1HeadPort, "sett: tentacle 1 head port", cancellationToken);

      // Positions 0-29: node 707 through node 302, exactly like real
      // erection -- both focus and writeB, using the real topology.
      for (int position = 0; position < Tentacle1UpToNode301.Length - 1; position++)
      {
        int coordinate = Tentacle1UpToNode301[position];
        int previous = position == 0 ? KrakenTopology.HeadCoordinate : Tentacle1UpToNode301[position - 1];
        int incomingPort = KrakenTopology.PortAddress(coordinate, previous);
        try
        {
          WriteRead708(
              port, addresses, KrakenProtocol.BuildFocus(incomingPort), wordsToRead: 1, position,
              context: $"chain position {position} (node {coordinate:000}), 'focus' -> port 0x{incomingPort:X3}",
              cancellationToken);

          int b = KrakenTopology.PortAddress(coordinate, Tentacle1UpToNode301[position + 1]);
          WriteRead708(
              port, addresses, KrakenProtocol.BuildWriteB(b), wordsToRead: 1, position,
              context: $"chain position {position} (node {coordinate:000}), 'writeB' -> 0x{b:X3}",
              cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
          stopwatch.Stop();
          return new Node708AlternateRelayReport(
              ChainToNode301Succeeded: false,
              $"Failed building the real chain before reaching node 301, at {Tentacle1UpToNode301[position]:000} (position {position}): {exception.Message}",
              RedirectedWriteBSucceeded: false, RedirectedWriteBFailureMessage: null,
              AlternateFocusSucceeded: false, null, stopwatch.Elapsed);
        }
      }

      // Position 30: focus node 301 itself, normally.
      int node301Position = Tentacle1UpToNode301.Length - 1;
      int node301 = Tentacle1UpToNode301[node301Position];
      int node301Previous = Tentacle1UpToNode301[node301Position - 1];
      int node301IncomingPort = KrakenTopology.PortAddress(node301, node301Previous);
      try
      {
        WriteRead708(
            port, addresses, KrakenProtocol.BuildFocus(node301IncomingPort), wordsToRead: 1, node301Position,
            context: $"chain position {node301Position} (node {node301:000}), 'focus' -> port 0x{node301IncomingPort:X3}",
            cancellationToken);
      }
      catch (Exception exception) when (exception is IOException or TimeoutException)
      {
        stopwatch.Stop();
        return new Node708AlternateRelayReport(
            ChainToNode301Succeeded: false,
            $"Focusing node 301 itself (position {node301Position}) failed: {exception.Message}",
            RedirectedWriteBSucceeded: false, null,
            AlternateFocusSucceeded: false, null, stopwatch.Elapsed);
      }

      // Redirected writeB: point node 301's B at node 401 (0x145) instead
      // of the real target, node 300 (0x1D5).
      bool redirectSucceeded;
      string? redirectFailure = null;
      try
      {
        WriteRead708(
            port, addresses, KrakenProtocol.BuildWriteB(Node301ToNode401Port), wordsToRead: 1, node301Position,
            context: $"REDIRECTED chain position {node301Position} (node {node301:000}), 'writeB' -> 0x{Node301ToNode401Port:X3} (node 401, not node 300)",
            cancellationToken);
        redirectSucceeded = true;
      }
      catch (Exception exception) when (exception is IOException or TimeoutException)
      {
        redirectSucceeded = false;
        redirectFailure = exception.Message;
      }

      if (!redirectSucceeded)
      {
        stopwatch.Stop();
        return new Node708AlternateRelayReport(
            true, null, false, redirectFailure, false, null, stopwatch.Elapsed);
      }

      // Final alternate-target focus: SAME relay depth (n=30, 31 layers) as
      // the real, failing node-300 call, but aimed at node 401's own
      // incoming port instead.
      bool alternateSucceeded;
      string? alternateFailure = null;
      try
      {
        WriteRead708(
            port, addresses, KrakenProtocol.BuildFocus(Node401ToNode301Port), wordsToRead: 1, node301Position + 1,
            context: $"ALTERNATE target position {node301Position + 1} (node 401 via redirected node 301), 'focus' -> port 0x{Node401ToNode301Port:X3}",
            cancellationToken);
        alternateSucceeded = true;
      }
      catch (Exception exception) when (exception is IOException or TimeoutException)
      {
        alternateSucceeded = false;
        alternateFailure = exception.Message;
      }

      stopwatch.Stop();
      return new Node708AlternateRelayReport(true, null, true, null, alternateSucceeded, alternateFailure, stopwatch.Elapsed);
    }
    finally
    {
      try { port.SetRts(true); } catch { }
      try { port.SetDtr(true); } catch { }
    }
  }

  private static int[] WriteRead708(
      NativeWindowsSerialPort port,
      Node708AlternateRelayAddresses addresses,
      IReadOnlyList<int> writeWords,
      int wordsToRead,
      int position,
      string context,
      CancellationToken cancellationToken)
  {
    int n = position - 1;
    try
    {
      SendWord708(port, addresses.WriteRead);
      SendWord708AndVerify(port, wordsToRead - 1, $"{context}: wordsToRead", cancellationToken);
      SendWord708AndVerify(port, writeWords.Count - 1, $"{context}: writeWords.Count", cancellationToken);
      SendWord708AndVerify(port, n, $"{context}: n (write-pre)", cancellationToken);
      for (int index = 0; index < writeWords.Count; index++)
      {
        SendWord708AndVerify(port, writeWords[index], $"{context}: payload word {index + 1} of {writeWords.Count}", cancellationToken);
      }

      SendWord708AndVerify(port, n, $"{context}: n (write-post)", cancellationToken);
    }
    catch (Exception exception) when (exception is IOException or TimeoutException)
    {
      throw new IOException($"Alternate-relay probe transaction failed while sending the 'w/r' request ({context}).", exception);
    }

    var result = new int[wordsToRead];
    for (int index = 0; index < wordsToRead; index++)
    {
      try
      {
        result[index] = ReadWord708(port, ResponseTimeoutMilliseconds, cancellationToken);
      }
      catch (TimeoutException exception)
      {
        throw new TimeoutException($"Alternate-relay probe reply timed out ({context}, reply word {index + 1} of {wordsToRead}): {exception.Message}", exception);
      }
    }

    return result;
  }

  private static void SendWord708(NativeWindowsSerialPort port, int value)
  {
    SendWord708Raw(port, value);
    Thread.Sleep(InterWordSettleMilliseconds);
  }

  private static void SendWord708Raw(NativeWindowsSerialPort port, int value)
  {
    byte[] bytes = new byte[3];
    Ga144Node708Probe.EncodeAsynchronousWord(value, bytes);
    port.Write(bytes);
    WaitForTransmitDrain(port, bytes.Length);
  }

  private static void SendWord708AndVerify(
      NativeWindowsSerialPort port,
      int value,
      string context,
      CancellationToken cancellationToken,
      int responseTimeoutMilliseconds = ResponseTimeoutMilliseconds)
  {
    int expected = value & F18InstructionSet.WordMask;
    SendWord708Raw(port, expected);

    int echoed;
    try
    {
      echoed = ReadWord708(port, responseTimeoutMilliseconds, cancellationToken);
    }
    catch (TimeoutException exception)
    {
      throw new TimeoutException($"Alternate-relay probe word acknowledgment timed out ({context}, sent 0x{expected:X5}): {exception.Message}", exception);
    }

    if (echoed != expected)
    {
      throw new IOException($"Alternate-relay probe word acknowledgment mismatch ({context}): sent 0x{expected:X5}, node echoed 0x{echoed:X5}.");
    }
  }

  private static int ReadWord708(NativeWindowsSerialPort port, int timeoutMilliseconds, CancellationToken cancellationToken)
  {
    byte[] bytes = ReadExactly(port, 3, timeoutMilliseconds, cancellationToken);
    return DecodeObywordReply(bytes);
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

  private static (int[] Program, Node708AlternateRelayAddresses Addresses) BuildHeadProgram(Ga144ChipConfiguration chip, Ga144RomLibrary romLibrary)
  {
    const string source = """
        # 0 org
        entry main
        : main 18ibits drop >r ex main;
        : obit ( dwn-dw) !b over >r delay ;
        : readw ( -dw) dup 18ibits drop over over
        : oword ( dw-d)  leap drop  leap drop leap drop  drop ;
        : obyt ( dw-dwx)  then then then  3 obit drop
            7 for dup 1 and 3 xor obit  drop 2/ next
            2 obit ;
        : sett .loc readw drop a! ; // set A register, used to select tentacle
        : w/r .loc // writes & reads words from a node. at least 1 word must be written, at least 1 word must be read
          readw drop >r // # of words to read -1
          readw drop dup >r // # of words to write -1
          // write pre
          ( w1)
          readw drop -if else >r over begin ( d w1)
            A[ @p >r ]] !
            r> dup >r // get current node
            dup dup . + . + 2* over . + ! // multiply by 6 + #write-1
            A[ @p !b unext ]] !
          next then
          //
          begin readw drop ! next
          // write post
          ( d)
          readw drop -if drop else for ( d)
            A[ @p >r ]] !
            r> r> dup ! >r >r  // send # of read words -1
            A[ @b !p unext ]] !
          next then
          //
          ( d)
          begin @ oword next main ;
        .loc
        """;

    var compileService = new F18NodeCompilationService(chip, romLibrary, romLibrary.SystemMacros);
    F18NodeCompilationResult nodeResult = compileService.CompileNode(KrakenTopology.HeadCoordinate);

    if (!nodeResult.Rom.Success)
    {
      string romDiagnostics = string.Join(Environment.NewLine, nodeResult.Rom.Diagnostics.Select(item => item.ToString()));
      throw new InvalidOperationException("Node 708's ROM source did not compile.\n" + romDiagnostics);
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
      throw new InvalidOperationException("The alternate-relay probe's node-708 head program did not compile.\n" + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException($"The alternate-relay probe's node-708 head program requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (result.EntryPoint != 0)
    {
      int selectedEntry = result.EntryPoint ?? -1;
      throw new InvalidOperationException($"The alternate-relay probe's node-708 head program must enter at RAM 0, but the compiler selected 0x{selectedEntry:X3}.");
    }

    var addresses = new Node708AlternateRelayAddresses(
        SetTentacle: RequireSymbol(result, "sett"),
        WriteRead: RequireSymbol(result, "w/r"));

    int[] words = result.Words.Select(item => item & F18InstructionSet.WordMask).ToArray();
    return (words, addresses);
  }

  private static int RequireSymbol(F18CompileResult result, string name)
  {
    if (!result.Symbols.TryGetValue(name, out F18ExportedSymbol? symbol) || symbol is null)
    {
      throw new InvalidOperationException($"The alternate-relay probe's node-708 head program did not define '{name}'.");
    }

    return symbol.Value;
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
      throw new TimeoutException($"Alternate-relay probe timed out after receiving {offset} of {count} bytes.");
    }

    return result;
  }

  private static void WaitForTransmitDrain(NativeWindowsSerialPort port, int byteCount)
  {
    double milliseconds = (byteCount * 10.0 * 1000.0 / port.BaudRate) + 3.0;
    Thread.Sleep((int)Math.Ceiling(milliseconds));
  }
}

internal sealed record Node708AlternateRelayAddresses(int SetTentacle, int WriteRead);
