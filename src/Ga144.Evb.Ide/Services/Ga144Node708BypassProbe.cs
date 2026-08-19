using System.Diagnostics;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>Everything one <see cref="Ga144Node708BypassProbe.RunBypassProbeAsync"/>
/// run produced: whether erecting the real chain up to node 400 succeeded,
/// whether the redirected 'writeB' at node 400 (pointed south at node 300
/// instead of east at node 401) was acknowledged, and whether the final
/// 'focus' aimed at node 300 -- reached through node 400 instead of node
/// 301 -- came back with a reply.</summary>
public sealed record Node708BypassProbeReport(
    bool ChainToNode400Succeeded,
    string? ChainFailureMessage,
    bool RedirectedWriteBSucceeded,
    string? RedirectedWriteBFailureMessage,
    bool BypassFocusSucceeded,
    string? BypassFocusFailureMessage,
    TimeSpan Elapsed);

/// <summary>
/// Node 300 (row 3, col 0) has three real physical neighbors: node 301
/// (east), node 400 (north) and node 200 (south). Tentacle 1's fixed
/// topology reaches it via node 301. Ga144Node708AlternateRelayProbe proved
/// that redirecting node 301's own 'writeB' at the SAME 31-layer depth to a
/// different, already-verified leaf (node 401) fails identically to the
/// real node-300 call -- pointing at node 301's own ability to relay an
/// additional hop, independent of the destination, rather than at node 300
/// itself or at "31 layers" being some kind of generic limit (tentacles 2
/// and 3 both relay past that depth with no boot nodes and no failures).
///
/// This probe follows up on that finding directly: it erects tentacle 1's
/// real chain only as far as node 400 (position 20 -- reached early, long
/// before node 301 or node 300 are ever involved), focuses node 400
/// normally, then -- instead of node 400's real 'writeB' pointing east at
/// node 401 -- redirects it south, at node 300, and sends a final 'focus'
/// at that new relay depth (n=20, 21 layers) with node 300's own
/// north-facing incoming port as the payload. This reaches node 300 while
/// bypassing node 301 entirely.
///
/// If this succeeds, node 300 itself is healthy and reachable; the fault is
/// isolated to node 301's own relay mechanism, and the eventual fix is to
/// reroute tentacle 1 around node 301 (via node 400 or node 200) rather
/// than to change anything about node 300. If it ALSO fails, node 300
/// itself cannot be relayed into from any direction, and node 301 is not
/// the (sole) explanation.
///
/// Self-contained, following the same pattern as the other node-708
/// probes: its own NativeWindowsSerialPort connection, its own compile of
/// the exact current 'main'/'obit'/'readw'/'oword'/'obyt'/'sett'/'w/r'
/// source (must match <see cref="KrakenSession.BuildHeadProgram"/>
/// verbatim to exercise the real on-chip relay-wrapper mechanism), and its
/// own low-level word-transport helpers -- does not reuse KrakenSession's
/// or the other probes' private methods.
/// </summary>
public sealed class Ga144Node708BypassProbe
{
  public const int DefaultBaudRate = Ga144Serial.MaximumBaudRate;

  private const int ResetAssertMilliseconds = 20;
  private const int ResetReleaseMilliseconds = 1;
  private const int ProgramStartMilliseconds = 5;
  private const int ResponseTimeoutMilliseconds = 1_000;
  private const int InterWordSettleMilliseconds = 20;

  // Real tentacle-1 coordinates, positions 0 through 20 (node 707 down to
  // node 400) -- copied from KrakenTopology.CreateDefaultTentacles's own
  // Tentacle1Nodes so this probe does not depend on constructing a
  // KrakenConfiguration, only on matching the real, fixed topology exactly
  // for the portion this test actually erects.
  private static readonly int[] Tentacle1UpToNode400 =
  [
    707, 706, 705, 704, 703, 702, 701, 700,
    600, 601, 602, 603, 604, 605,
    505, 504, 503, 502, 501, 500,
    400
  ];

  // Node 400's own port facing node 300 (south) -- KrakenTopology.PortAddress(400, 300) --
  // used for the redirected 'writeB' instead of node 400's real forward
  // target, node 401 (east, KrakenTopology.PortAddress(400, 401) = 0x1D5).
  // Node 300's own port facing node 400 (north) --
  // KrakenTopology.PortAddress(300, 400) -- used as the final 'focus'
  // payload. Both hand-derived and cross-checked against KrakenTopology's
  // parity table; both happen to resolve to the same local address, 0x145,
  // the same way the 301<->300 and 301<->401 boundaries each resolve to a
  // single shared value on both ends.
  private const int Node400ToNode300Port = 0x145;
  private const int Node300ToNode400Port = 0x145;

  public async Task<Node708BypassProbeReport> RunBypassProbeAsync(
      string portName,
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    ArgumentNullException.ThrowIfNull(chip);
    ArgumentNullException.ThrowIfNull(romLibrary);

    (int[] program, Node708BypassProbeAddresses addresses) = BuildHeadProgram(chip, romLibrary);
    return await Task.Run(
        () => RunBypassProbe(portName, program, addresses, cancellationToken),
        cancellationToken);
  }

  private static Node708BypassProbeReport RunBypassProbe(
      string portName,
      int[] program,
      Node708BypassProbeAddresses addresses,
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

      int tentacle1HeadPort = KrakenTopology.PortAddress(KrakenTopology.HeadCoordinate, Tentacle1UpToNode400[0]);
      SendWord708(port, addresses.SetTentacle);
      SendWord708AndVerify(port, tentacle1HeadPort, "sett: tentacle 1 head port", cancellationToken);

      // Positions 0-19: node 707 through node 500, exactly like real
      // erection -- both focus and writeB, using the real topology.
      for (int position = 0; position < Tentacle1UpToNode400.Length - 1; position++)
      {
        int coordinate = Tentacle1UpToNode400[position];
        int previous = position == 0 ? KrakenTopology.HeadCoordinate : Tentacle1UpToNode400[position - 1];
        int incomingPort = KrakenTopology.PortAddress(coordinate, previous);
        try
        {
          WriteRead708(
              port, addresses, KrakenProtocol.BuildFocus(incomingPort), wordsToRead: 1, position,
              context: $"chain position {position} (node {coordinate:000}), 'focus' -> port 0x{incomingPort:X3}",
              cancellationToken);

          int b = KrakenTopology.PortAddress(coordinate, Tentacle1UpToNode400[position + 1]);
          WriteRead708(
              port, addresses, KrakenProtocol.BuildWriteB(b), wordsToRead: 1, position,
              context: $"chain position {position} (node {coordinate:000}), 'writeB' -> 0x{b:X3}",
              cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
          stopwatch.Stop();
          return new Node708BypassProbeReport(
              ChainToNode400Succeeded: false,
              $"Failed building the real chain before reaching node 400, at {Tentacle1UpToNode400[position]:000} (position {position}): {exception.Message}",
              RedirectedWriteBSucceeded: false, RedirectedWriteBFailureMessage: null,
              BypassFocusSucceeded: false, null, stopwatch.Elapsed);
        }
      }

      // Position 20: focus node 400 itself, normally.
      int node400Position = Tentacle1UpToNode400.Length - 1;
      int node400 = Tentacle1UpToNode400[node400Position];
      int node400Previous = Tentacle1UpToNode400[node400Position - 1];
      int node400IncomingPort = KrakenTopology.PortAddress(node400, node400Previous);
      try
      {
        WriteRead708(
            port, addresses, KrakenProtocol.BuildFocus(node400IncomingPort), wordsToRead: 1, node400Position,
            context: $"chain position {node400Position} (node {node400:000}), 'focus' -> port 0x{node400IncomingPort:X3}",
            cancellationToken);
      }
      catch (Exception exception) when (exception is IOException or TimeoutException)
      {
        stopwatch.Stop();
        return new Node708BypassProbeReport(
            ChainToNode400Succeeded: false,
            $"Focusing node 400 itself (position {node400Position}) failed: {exception.Message}",
            RedirectedWriteBSucceeded: false, null,
            BypassFocusSucceeded: false, null, stopwatch.Elapsed);
      }

      // Redirected writeB: point node 400's B south, at node 300 (0x145)
      // instead of the real tentacle-1 target, node 401 (0x1D5, east).
      bool redirectSucceeded;
      string? redirectFailure = null;
      try
      {
        WriteRead708(
            port, addresses, KrakenProtocol.BuildWriteB(Node400ToNode300Port), wordsToRead: 1, node400Position,
            context: $"REDIRECTED chain position {node400Position} (node {node400:000}), 'writeB' -> 0x{Node400ToNode300Port:X3} (node 300, not node 401)",
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
        return new Node708BypassProbeReport(
            true, null, false, redirectFailure, false, null, stopwatch.Elapsed);
      }

      // Final bypass focus: node 300, reached through node 400 (n=20, 21
      // layers) instead of through node 301 (the real, failing path, n=30,
      // 31 layers). Payload is node 300's own north-facing incoming port.
      bool bypassSucceeded;
      string? bypassFailure = null;
      try
      {
        WriteRead708(
            port, addresses, KrakenProtocol.BuildFocus(Node300ToNode400Port), wordsToRead: 1, node400Position + 1,
            context: $"BYPASS target position {node400Position + 1} (node 300 via redirected node 400, not node 301), 'focus' -> port 0x{Node300ToNode400Port:X3}",
            cancellationToken);
        bypassSucceeded = true;
      }
      catch (Exception exception) when (exception is IOException or TimeoutException)
      {
        bypassSucceeded = false;
        bypassFailure = exception.Message;
      }

      stopwatch.Stop();
      return new Node708BypassProbeReport(true, null, true, null, bypassSucceeded, bypassFailure, stopwatch.Elapsed);
    }
    finally
    {
      try { port.SetRts(true); } catch { }
      try { port.SetDtr(true); } catch { }
    }
  }

  private static int[] WriteRead708(
      NativeWindowsSerialPort port,
      Node708BypassProbeAddresses addresses,
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
      throw new IOException($"Bypass probe transaction failed while sending the 'w/r' request ({context}).", exception);
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
        throw new TimeoutException($"Bypass probe reply timed out ({context}, reply word {index + 1} of {wordsToRead}): {exception.Message}", exception);
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
      throw new TimeoutException($"Bypass probe word acknowledgment timed out ({context}, sent 0x{expected:X5}): {exception.Message}", exception);
    }

    if (echoed != expected)
    {
      throw new IOException($"Bypass probe word acknowledgment mismatch ({context}): sent 0x{expected:X5}, node echoed 0x{echoed:X5}.");
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

  private static (int[] Program, Node708BypassProbeAddresses Addresses) BuildHeadProgram(Ga144ChipConfiguration chip, Ga144RomLibrary romLibrary)
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
      throw new InvalidOperationException("The bypass probe's node-708 head program did not compile.\n" + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException($"The bypass probe's node-708 head program requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (result.EntryPoint != 0)
    {
      int selectedEntry = result.EntryPoint ?? -1;
      throw new InvalidOperationException($"The bypass probe's node-708 head program must enter at RAM 0, but the compiler selected 0x{selectedEntry:X3}.");
    }

    var addresses = new Node708BypassProbeAddresses(
        SetTentacle: RequireSymbol(result, "sett"),
        WriteRead: RequireSymbol(result, "w/r"));

    int[] words = result.Words.Select(item => item & F18InstructionSet.WordMask).ToArray();
    return (words, addresses);
  }

  private static int RequireSymbol(F18CompileResult result, string name)
  {
    if (!result.Symbols.TryGetValue(name, out F18ExportedSymbol? symbol) || symbol is null)
    {
      throw new InvalidOperationException($"The bypass probe's node-708 head program did not define '{name}'.");
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
      throw new TimeoutException($"Bypass probe timed out after receiving {offset} of {count} bytes.");
    }

    return result;
  }

  private static void WaitForTransmitDrain(NativeWindowsSerialPort port, int byteCount)
  {
    double milliseconds = (byteCount * 10.0 * 1000.0 / port.BaudRate) + 3.0;
    Thread.Sleep((int)Math.Ceiling(milliseconds));
  }
}

internal sealed record Node708BypassProbeAddresses(int SetTentacle, int WriteRead);
