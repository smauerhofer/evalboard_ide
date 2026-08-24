using System.Diagnostics;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>One 'focus'+'writeB' erection step attempted against a single
/// tentacle position, mirroring exactly what KrakenSession.ErectOnto does
/// for that position, but recorded rather than thrown on failure so a whole
/// tentacle can be swept in one run.</summary>
public sealed record Node708TentacleDepthPositionResult(
    int Position,
    int Coordinate,
    bool Succeeded,
    string? FailureMessage,
    TimeSpan Elapsed);

/// <summary>Everything one tentacle's sweep produced: every position
/// attempted, in order, stopping at the first failure -- every later
/// position relays through the ones before it, so one broken hop makes
/// anything past it meaningless to attempt.</summary>
public sealed record Node708TentacleDepthTentacleResult(
    int TentacleNumber,
    string TentacleName,
    int NodeCount,
    IReadOnlyList<Node708TentacleDepthPositionResult> Positions);

/// <summary>Everything one <see cref="Ga144Node708TentacleDepthProbe.RunTentacleDepthProbeAsync"/>
/// run produced: one result per tentacle tested, in order.</summary>
public sealed record Node708TentacleDepthReport(IReadOnlyList<Node708TentacleDepthTentacleResult> Tentacles);

/// <summary>Node 708's 'sett'/'w/r' dispatch addresses, resolved from this
/// probe's own compile of KrakenSession.BuildHeadProgram's exact source.
/// Kept local to this probe rather than reusing KrakenSession's shared
/// Node708HeadAddresses record, since this probe never dispatches to
/// 'setn' (removed from the current source) and does not want a stray
/// SetNode field implying otherwise.</summary>
public sealed record Node708TentacleDepthAddresses(int SetTentacle, int WriteRead);

/// <summary>
/// Standalone diagnostic built to separate two live hypotheses about the
/// node-300 (tentacle 1, position 31) erection failure: (a) a generic
/// depth/capacity limit in node 708's on-chip relay-wrapper mechanism that
/// happens to first show up around 30-31 hops, or (b) something specific to
/// node 300's own nature as a documented GA144 Synchronous Boot node (DB002
/// 5.5.6) that a boot-settle delay alone did not fix.
///
/// Tentacles 2 (46 nodes) and 3 (47 nodes) -- see KrakenTopology -- both run
/// PAST position 31 and contain NO boot nodes, so sweeping 'focus'+'writeB'
/// out to their full depth is a direct, apples-to-apples test of the SAME
/// on-chip relay mechanism ErectOnto uses, with node 300 itself removed
/// from the picture. Full success on both -> points at node 300
/// specifically. A failure at a similar depth on a boot-node-free tentacle
/// -> points at a real capacity limit instead.
///
/// This probe deliberately mirrors ErectOnto's own per-position sequence
/// (this node's 'focus', then this node's 'writeB' so the NEXT position can
/// relay through it) rather than only calling 'focus' alone, since a
/// capacity limit could plausibly depend on the accumulated writeB chain
/// just as much as on the relay-wrapper depth itself. Each tentacle gets
/// its own fresh chip reset and head-program reboot, so one tentacle's
/// failure cannot leave a stuck/desynced relay chain that corrupts the next
/// tentacle's results.
///
/// Self-contained: its own NativeWindowsSerialPort connection (matching
/// KrakenSession's transport class, not the ordinary
/// System.IO.Ports.SerialPort the echo probe uses), its own copy of the
/// EXACT current 'main'/'obit'/'readw'/'oword'/'obyt'/'sett'/'w/r' source
/// from KrakenSession.BuildHeadProgram (including the on-chip relay-wrapper
/// write-pre/write-post bodies), and its own low-level word-transport
/// helpers -- does not reuse KrakenSession's private methods.
/// </summary>
public sealed class Ga144Node708TentacleDepthProbe
{
  public const int DefaultBaudRate = Ga144Serial.MaximumBaudRate;

  private const int ResetAssertMilliseconds = 20;
  private const int ResetReleaseMilliseconds = 1;
  private const int ProgramStartMilliseconds = 5;
  private const int ResponseTimeoutMilliseconds = 1_000;

  // Same value KrakenSession.SendWord708 sleeps after every unacknowledged
  // dispatch-address send -- reused unchanged so this probe paces the
  // dispatch word exactly the way the real erection does.
  private const int InterWordSettleMilliseconds = 20;

  // Node 708's io register, used the same way ErectOnto uses IoAddress: the
  // 'writeB' target for a tentacle's last node, which has nothing further
  // to relay to.
  private const int IoAddress = 0x15D;

  public async Task<Node708TentacleDepthReport> RunTentacleDepthProbeAsync(
      string portName,
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<int>? tentacleNumbers = null,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    ArgumentNullException.ThrowIfNull(chip);
    ArgumentNullException.ThrowIfNull(romLibrary);

    List<KrakenTentacleConfiguration> allTentacles = KrakenTopology.CreateDefaultTentacles();
    IReadOnlyList<int> wanted = tentacleNumbers ?? [2, 3];
    List<KrakenTentacleConfiguration> tentacles = allTentacles
        .Where(item => wanted.Contains(item.Number))
        .OrderBy(item => item.Number)
        .ToList();

    if (tentacles.Count == 0)
    {
      throw new ArgumentException("No matching tentacle numbers were found.", nameof(tentacleNumbers));
    }

    (int[] program, Node708TentacleDepthAddresses addresses) = BuildHeadProgram(chip, romLibrary);

    var results = new List<Node708TentacleDepthTentacleResult>(tentacles.Count);
    foreach (KrakenTentacleConfiguration tentacle in tentacles)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Node708TentacleDepthTentacleResult result = await Task.Run(
          () => RunOneTentacle(portName, program, addresses, tentacle, cancellationToken),
          cancellationToken);
      results.Add(result);
    }

    return new Node708TentacleDepthReport(results);
  }

  // Fresh reset + head-program reboot for THIS tentacle only, then sweeps
  // every position out to the tentacle's full depth, stopping at the first
  // failed hop. Mirrors ErectOnto's own per-tentacle/per-position sequence
  // (SelectTentacle708 once, then focus+writeB per position) but records
  // outcomes instead of throwing, so one run reports the whole tentacle.
  private static Node708TentacleDepthTentacleResult RunOneTentacle(
      string portName,
      int[] program,
      Node708TentacleDepthAddresses addresses,
      KrakenTentacleConfiguration tentacle,
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
      Thread.Sleep(ResetReleaseMilliseconds);

      byte[] bootStream = BuildBootStream(program);
      port.Write(bootStream);
      WaitForTransmitDrain(port, bootStream.Length);
      Thread.Sleep(ProgramStartMilliseconds);

      port.PurgeInput();

      int tentacleHeadPort = KrakenTopology.PortAddress(KrakenTopology.HeadCoordinate, tentacle.Nodes[0]);
      SendWord708(port, addresses.SetTentacle);
      SendWord708AndVerify(port, tentacleHeadPort, $"tentacle {tentacle.Number} 'sett'", cancellationToken);

      var positions = new List<Node708TentacleDepthPositionResult>(tentacle.Nodes.Count);
      for (int position = 0; position < tentacle.Nodes.Count; position++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        int coordinate = tentacle.Nodes[position];
        int previous = position == 0 ? KrakenTopology.HeadCoordinate : tentacle.Nodes[position - 1];
        var stopwatch = Stopwatch.StartNew();
        try
        {
          int incomingPort = KrakenTopology.PortAddress(coordinate, previous);
          _ = WriteRead708(
              port, addresses, KrakenProtocol.BuildFocus(incomingPort), wordsToRead: 1, position,
              context: $"tentacle {tentacle.Number} position {position} (node {coordinate:000}), 'focus' -> port 0x{incomingPort:X3}",
              cancellationToken);

          int b = position + 1 < tentacle.Nodes.Count
              ? KrakenTopology.PortAddress(coordinate, tentacle.Nodes[position + 1])
              : IoAddress;
          _ = WriteRead708(
              port, addresses, KrakenProtocol.BuildWriteB(b), wordsToRead: 1, position,
              context: $"tentacle {tentacle.Number} position {position} (node {coordinate:000}), 'writeB' -> 0x{b:X3}",
              cancellationToken);

          stopwatch.Stop();
          positions.Add(new Node708TentacleDepthPositionResult(position, coordinate, Succeeded: true, FailureMessage: null, stopwatch.Elapsed));
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
          stopwatch.Stop();
          positions.Add(new Node708TentacleDepthPositionResult(position, coordinate, Succeeded: false, exception.Message, stopwatch.Elapsed));
          // Every later position relays through this one -- once a hop
          // fails there is nothing further to learn from this tentacle.
          break;
        }
      }

      return new Node708TentacleDepthTentacleResult(tentacle.Number, tentacle.Name, tentacle.Nodes.Count, positions);
    }
    finally
    {
      try { port.SetRts(true); } catch { }
      try { port.SetDtr(true); } catch { }
    }
  }

  // Identical shape to KrakenSession.WriteRead708 (see its own remarks on
  // the on-chip relay-wrapper mechanism and the -1 count convention) --
  // duplicated here rather than reused because it is private to
  // KrakenSession and this probe intentionally owns its own transport
  // end-to-end.
  private static int[] WriteRead708(
      NativeWindowsSerialPort port,
      Node708TentacleDepthAddresses addresses,
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
      throw new IOException($"Tentacle-depth probe transaction failed while sending the 'w/r' request ({context}).", exception);
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
        throw new TimeoutException($"Tentacle-depth probe reply timed out ({context}, reply word {index + 1} of {wordsToRead}): {exception.Message}", exception);
      }
    }

    return result;
  }

  private static void SendWord708(NativeWindowsSerialPort port, int value)
  {
    SendWord708Raw(port, value);
    // Only used for the single, unacknowledged dispatch-address word 'main'
    // reads via bare '18ibits'; keep a fixed settle margin here since there
    // is no echo to naturally pace against.
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
      throw new TimeoutException($"Tentacle-depth probe word acknowledgment timed out ({context}, sent 0x{expected:X5}): {exception.Message}", exception);
    }

    if (echoed != expected)
    {
      throw new IOException($"Tentacle-depth probe word acknowledgment mismatch ({context}): sent 0x{expected:X5}, node echoed 0x{echoed:X5}.");
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

  /// <summary>
  /// Compiles node 708's EXACT current head program -- the same
  /// 'main'/'obit'/'readw'/'oword'/'obyt'/'sett'/'w/r' source as
  /// KrakenSession.BuildHeadProgram, copied verbatim so this probe exercises
  /// the real on-chip relay-wrapper mechanism, not a simplified stand-in --
  /// against node 708's REAL, currently configured ROM exports.
  /// </summary>
  private static (int[] Program, Node708TentacleDepthAddresses Addresses) BuildHeadProgram(Ga144ChipConfiguration chip, Ga144RomLibrary romLibrary)
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
            A[ @p >r ]] lit !
            r> dup >r // get current node
            dup dup . + . + 2* over . + ! // multiply by 6 + #write-1
            A[ @p !b unext ]] lit !
          next then
          //
          begin readw drop ! next
          // write post
          ( d)
          readw drop -if drop else for ( d)
            A[ @p >r ]] lit !
            r> r> dup ! >r >r  // send # of read words -1
            A[ @b !p unext ]] lit !
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
      throw new InvalidOperationException("The tentacle-depth probe's node-708 head program did not compile.\n" + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException($"The tentacle-depth probe's node-708 head program requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (result.EntryPoint != 0)
    {
      int selectedEntry = result.EntryPoint ?? -1;
      throw new InvalidOperationException($"The tentacle-depth probe's node-708 head program must enter at RAM 0, but the compiler selected 0x{selectedEntry:X3}.");
    }

    var addresses = new Node708TentacleDepthAddresses(
        SetTentacle: RequireSymbol(result, "sett"),
        WriteRead: RequireSymbol(result, "w/r"));

    int[] words = result.Words.Select(item => item & F18InstructionSet.WordMask).ToArray();
    return (words, addresses);
  }

  private static int RequireSymbol(F18CompileResult result, string name)
  {
    if (!result.Symbols.TryGetValue(name, out F18ExportedSymbol? symbol) || symbol is null)
    {
      throw new InvalidOperationException($"The tentacle-depth probe's node-708 head program did not define '{name}'.");
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
      throw new TimeoutException($"Tentacle-depth probe timed out after receiving {offset} of {count} bytes.");
    }

    return result;
  }

  private static void WaitForTransmitDrain(NativeWindowsSerialPort port, int byteCount)
  {
    double milliseconds = (byteCount * 10.0 * 1000.0 / port.BaudRate) + 3.0;
    Thread.Sleep((int)Math.Ceiling(milliseconds));
  }
}