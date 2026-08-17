using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>One pattern-sweep round trip: a chosen test word, the bytes actually
/// sent/received, the bytes independently predicted from node 708's own
/// obit/oword/obyt algorithm (see <see cref="Ga144Node708EchoProbe"/>), and
/// whether the two agree.</summary>
public sealed record Node708EchoPatternResult(
    int SentWord,
    byte[] SentBytes,
    byte[] ReceivedBytes,
    byte[] ExpectedBytes,
    bool Matched);

/// <summary>Aggregate timing over repeated round trips of one fixed test word,
/// all exchanged over the same booted <c>echo</c> session (no reboot between
/// iterations). Write and read are timed separately -- see the remarks on
/// <see cref="Ga144Node708EchoProbe.RunEchoSuiteAsync"/> for why they are kept
/// apart rather than measured as one combined burst.</summary>
public sealed record Node708EchoSpeedResult(
    int Iterations,
    TimeSpan AverageWriteTime,
    TimeSpan AverageReadTime,
    TimeSpan AverageRoundTripTime,
    double WriteBitsPerSecond,
    double ReadBitsPerSecond,
    double RoundTripsPerSecond);

/// <summary>Everything one <see cref="Ga144Node708EchoProbe.RunEchoSuiteAsync"/>
/// run produced: the pattern-sweep results and the speed-test result.</summary>
public sealed record Node708EchoReport(
    IReadOnlyList<Node708EchoPatternResult> PatternResults,
    Node708EchoSpeedResult SpeedResult);

/// <summary>
/// Standalone, pre-Kraken communication test for node 708's own hand-written
/// direct-UART transmit routines (<c>obit</c>/<c>oword</c>/<c>obyt</c>/
/// <c>echo</c>) -- the replacement for the old carrier-clock
/// wait-high/wait-low scheme, which drove the F18's output pin from raw host
/// bytes instead of letting the node itself frame and time real UART bytes.
/// This program instead calls the node's own already-verified <c>18ibits</c>
/// (which internally calibrates via <c>sync</c>) to receive one 18-bit word
/// the normal way boot frames are received, then transmits it straight back
/// out as genuine start/8-data/stop-bit framed UART bytes, timed by the
/// node's own <c>delay</c> -- no host-driven carrier clocking on the return
/// path at all.
///
/// One boot loads <c>echo</c>'s infinite receive/transmit loop, then this
/// probe drives it through two phases without ever rebooting the chip:
/// a pattern sweep (a handful of fixed words plus a full walking-single-bit
/// sweep of all 18 bit positions), each checked against an independent,
/// from-first-principles prediction of what obit/oword/obyt should produce
/// (<see cref="SimulateExpectedBytes"/>); and a speed test that repeats one
/// fixed word many times, timing the write and read phases of each round
/// trip separately to estimate throughput in both directions.
///
/// Same restriction as the other node-708 probes: only usable BEFORE a Kraken
/// is erected, since loading this program requires a chip reset.
/// </summary>
public sealed class Ga144Node708EchoProbe
{
  public const int DefaultBaudRate = Ga144Serial.MaximumBaudRate;

  // The word repeated throughout the speed test. Also included once in the
  // pattern sweep below, so a single run cross-checks both correctness and
  // speed against the same known-good pattern (0x15555 -> 55 55 01 on real
  // hardware, confirmed separately by hand).
  private const int SpeedTestWord = 0x15555;
  private const int SpeedTestIterations = 50;

  private const int ResetAssertMilliseconds = 20;
  private const int ResetReleaseToBootMilliseconds = 1;
  private const int ProgramStartMilliseconds = 5;
  private const int ResponseTimeoutMilliseconds = 1_000;

  // Four hand-picked patterns (all-zero, all-one, and the two alternating
  // bit patterns) plus a full walking-single-bit sweep (one bit set at a
  // time, all 18 positions) = 22 words total, all exchanged over the one
  // boot session that 'echo's infinite loop keeps alive.
  private static readonly int[] TestPatterns = BuildTestPatterns();

  public async Task<Node708EchoReport> RunEchoSuiteAsync(
      string portName,
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    ArgumentNullException.ThrowIfNull(chip);
    ArgumentNullException.ThrowIfNull(romLibrary);

    int[] program = BuildEchoProgram(chip, romLibrary);
    return await Task.Run(() => RunEchoSuite(portName, program, cancellationToken), cancellationToken);
  }

  private static Node708EchoReport RunEchoSuite(string portName, int[] program, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();

    using System.IO.Ports.SerialPort port = Ga144Serial.Create(portName);
    port.ReadTimeout = 40;
    port.WriteTimeout = 1_000;
    port.Open();

    port.RtsEnable = true;
    port.DtrEnable = true;

    try
    {
      port.DtrEnable = true;
      port.RtsEnable = false;
      Thread.Sleep(ResetAssertMilliseconds);

      cancellationToken.ThrowIfCancellationRequested();
      port.DiscardInBuffer();
      port.DiscardOutBuffer();

      port.RtsEnable = true;
      Thread.Sleep(ResetReleaseToBootMilliseconds);

      byte[] bootStream = BuildBootStream(program);
      port.Write(bootStream, 0, bootStream.Length);
      WaitForTransmitDrain(port, bootStream.Length);
      Thread.Sleep(ProgramStartMilliseconds);

      port.DiscardInBuffer();

      // 'echo' loops forever, so every exchange below -- every pattern, every
      // speed-test iteration -- rides the SAME boot. No reset/reload between
      // them; each is just one more word in, one more word back.
      var patternResults = new List<Node708EchoPatternResult>(TestPatterns.Length);
      foreach (int word in TestPatterns)
      {
        cancellationToken.ThrowIfCancellationRequested();
        ExchangeOutcome outcome = ExchangeWord(port, word, cancellationToken);
        byte[] expected = SimulateExpectedBytes(word);
        bool matched = outcome.ReceivedBytes.AsSpan().SequenceEqual(expected);
        patternResults.Add(new Node708EchoPatternResult(word, outcome.SentBytes, outcome.ReceivedBytes, expected, matched));
      }

      long writeTicksTotal = 0;
      long readTicksTotal = 0;
      for (int iteration = 0; iteration < SpeedTestIterations; iteration++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        ExchangeOutcome outcome = ExchangeWord(port, SpeedTestWord, cancellationToken);
        writeTicksTotal += outcome.WriteElapsed.Ticks;
        readTicksTotal += outcome.ReadElapsed.Ticks;
      }

      var averageWrite = TimeSpan.FromTicks(writeTicksTotal / SpeedTestIterations);
      var averageRead = TimeSpan.FromTicks(readTicksTotal / SpeedTestIterations);
      TimeSpan averageRoundTrip = averageWrite + averageRead;

      // Reported as data-bit throughput (3 bytes = 24 data bits per
      // exchange), not wire-bit throughput -- the actual 8-N-1 framing adds a
      // start and stop bit per byte on top of this, so real wire bit rate
      // runs a bit higher than these figures.
      const int dataBitsPerExchange = 3 * 8;
      double writeBitsPerSecond = averageWrite.TotalSeconds > 0 ? dataBitsPerExchange / averageWrite.TotalSeconds : 0;
      double readBitsPerSecond = averageRead.TotalSeconds > 0 ? dataBitsPerExchange / averageRead.TotalSeconds : 0;
      double roundTripsPerSecond = averageRoundTrip.TotalSeconds > 0 ? 1.0 / averageRoundTrip.TotalSeconds : 0;

      var speedResult = new Node708EchoSpeedResult(
          SpeedTestIterations,
          averageWrite,
          averageRead,
          averageRoundTrip,
          writeBitsPerSecond,
          readBitsPerSecond,
          roundTripsPerSecond);

      return new Node708EchoReport(patternResults, speedResult);
    }
    finally
    {
      try { port.RtsEnable = true; } catch { }
      try { port.DtrEnable = true; } catch { }
    }
  }

  /// <summary>One send-then-receive round trip against the already-booted
  /// 'echo' loop. The write phase covers issuing the host write and draining
  /// it out of the local output buffer (<see cref="WaitForWriteBufferDrain"/>
  /// -- the closest .NET's SerialPort API gets to a real "bytes are on the
  /// wire" signal); the read phase covers everything after that: whatever
  /// remains of the wire time out, node 708's own receive/decode/re-encode
  /// work, and the wire time of its reply back. That split makes "read" the
  /// more representative number when you want a feel for real round-trip
  /// latency, since neither .NET nor this hardware exposes a way to see
  /// node 708's receive-complete or reply-start moments directly. 'echo' is
  /// strictly receive-then-transmit with no concurrency of its own, so round
  /// trips are paced one at a time here rather than attempted as an
  /// overlapped burst -- sending a second word while node 708 is still
  /// mid-transmit of the previous reply would risk desynchronizing the next
  /// 18ibits/sync calibration.</summary>
  private static ExchangeOutcome ExchangeWord(System.IO.Ports.SerialPort port, int word, CancellationToken cancellationToken)
  {
    byte[] sentBytes = new byte[3];
    Ga144Node708Probe.EncodeAsynchronousWord(word, sentBytes);

    // Deliberately NOT using WaitForTransmitDrain's fixed formula-based sleep
    // here (that is still fine for the one-shot boot stream above, where
    // nothing is timed against it). For per-exchange timing it would be
    // actively misleading: that sleep is generous enough to already cover
    // the whole round trip, so the reply would already be sitting in the OS
    // receive buffer before the read timer ever started, making "read" look
    // artificially instant and dumping all the real latency into "write".
    // Polling BytesToWrite down to zero is the closest thing .NET's
    // SerialPort API offers to "our bytes actually left the local output
    // buffer" (there is no hardware transmit-complete signal exposed), so it
    // is used as the write/read boundary instead.
    var writeStopwatch = System.Diagnostics.Stopwatch.StartNew();
    port.Write(sentBytes, 0, sentBytes.Length);
    WaitForWriteBufferDrain(port);
    writeStopwatch.Stop();

    var readStopwatch = System.Diagnostics.Stopwatch.StartNew();
    byte[] receivedBytes = ReadExactly(port, 3, ResponseTimeoutMilliseconds, cancellationToken);
    readStopwatch.Stop();

    return new ExchangeOutcome(sentBytes, receivedBytes, writeStopwatch.Elapsed, readStopwatch.Elapsed);
  }

  // At this baud rate, draining 3 bytes out of the local output buffer is
  // expected to take a small fraction of a millisecond -- far under the
  // ~15 ms granularity Thread.Sleep gets from the Windows scheduler, which
  // would swamp the very thing being measured. A tight spin-wait keeps the
  // write-phase measurement meaningful. Capped so a driver that never
  // reports BytesToWrite == 0 can't hang the speed test.
  private const int WriteDrainTimeoutMilliseconds = 50;

  private static void WaitForWriteBufferDrain(System.IO.Ports.SerialPort port)
  {
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    while (port.BytesToWrite > 0 && stopwatch.ElapsedMilliseconds < WriteDrainTimeoutMilliseconds)
    {
      Thread.SpinWait(200);
    }
  }

  private sealed record ExchangeOutcome(byte[] SentBytes, byte[] ReceivedBytes, TimeSpan WriteElapsed, TimeSpan ReadElapsed);

  private static int[] BuildTestPatterns()
  {
    var patterns = new List<int> { 0x00000, 0x3FFFF, 0x15555, 0x2AAAA };
    for (int bit = 0; bit < 18; bit++)
    {
      patterns.Add(1 << bit);
    }

    return patterns.ToArray();
  }

  /// <summary>
  /// Independently predicts the 3 bytes obyt/oword should transmit for a
  /// given 18-bit word, mirroring obyt's own algorithm from first principles
  /// rather than copying the one hardware-confirmed answer (0x15555 -> 55 55
  /// 01): each of the 3 bytes is formed by taking the current register's low
  /// bit 8 times (obyt's "7 for dup 1 and 3 xor obit drop 2/ next" loop),
  /// then the whole 18-bit register is shifted down by one more byte for the
  /// next obyt call (the "dwx" oword threads from one leap into the next).
  ///
  /// Crucially, that shift is F18's arithmetic (sign-preserving) "2/" --
  /// confirmed against F18CompileTimeInterpreter's own ArithmeticShiftRight
  /// -- not a logical shift. Because the word is only 18 bits wide, bit 17 is
  /// effectively its sign bit: once the third obyt call shifts past bit 17,
  /// the vacated high bits of that byte fill with copies of bit 17 rather
  /// than zero. This only shows up for words with bit 17 set (e.g. 0x3FFFF
  /// or 0x2AAAA), which the fixed pattern list above exercises on purpose.
  /// </summary>
  private static byte[] SimulateExpectedBytes(int word)
  {
    long signed = ToSigned18(word);
    var bytes = new byte[3];
    for (int byteIndex = 0; byteIndex < 3; byteIndex++)
    {
      int value = 0;
      for (int bit = 0; bit < 8; bit++)
      {
        value |= (int)(signed & 1) << bit;
        signed >>= 1; // arithmetic shift on a signed long -- matches F18 '2/'
      }

      bytes[byteIndex] = (byte)value;
    }

    return bytes;
  }

  private static long ToSigned18(int word)
  {
    int masked = word & F18InstructionSet.WordMask;
    bool negative = (masked & 0x20000) != 0;
    return negative ? masked - 0x40000 : masked;
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
  /// Compiles the user's obit/oword/obyt/echo program against node 708's
  /// REAL, currently configured ROM exports (the same PredefinedSymbols
  /// mechanism used when compiling any node's ordinary RAM source), so
  /// referencing <c>18ibits</c> and <c>delay</c> resolves to that ROM's own,
  /// already hardware-verified addresses.
  /// </summary>
  private static int[] BuildEchoProgram(Ga144ChipConfiguration chip, Ga144RomLibrary romLibrary)
  {
    // 'echo' is defined FIRST so it lands at RAM address 0. The boot frame's
    // single "transfer address" field is both where the payload is written
    // AND where execution begins once loading completes (see
    // BuildReplyProgram/Ga144Node708RomReader's dump-rom, which rely on the
    // same thing) -- it cannot be two different addresses. Putting 'echo'
    // last (after obit/oword/obyt, matching how the words were given) made
    // it compile to a non-zero address while the boot frame still loaded the
    // payload starting at 0, so the chip began executing 'obit' instead of
    // 'echo'. Forth call order doesn't care about textual definition order
    // (forward references are supported), so reordering here changes
    // nothing about the program's behavior -- only where 'echo' physically
    // lands.
    const string source = """
        # 0 org
        entry echo

        : echo 18ibits drop oword echo ;
        : obit ( dwn-dw) !b over >r delay ;
        : oword ( dw-d)  leap drop  leap drop leap drop  drop ;
        : obyt ( dw-dwx)  then then then  3 obit drop
            7 for dup 1 and 3 xor obit  drop 2/ next
            2 obit ;
        """;

    // Compile node 708's REAL, currently configured ROM only -- to resolve
    // '18ibits'/'delay' to their real, already hardware-verified addresses.
    // The node's current project RAM (if any) is compiled alongside by
    // CompileNode but is irrelevant here and deliberately ignored; this probe
    // supplies its own RAM image below instead of using it.
    var compileService = new F18NodeCompilationService(chip, romLibrary, romLibrary.SystemMacros);
    F18NodeCompilationResult nodeResult = compileService.CompileNode(KrakenTopology.HeadCoordinate);

    if (!nodeResult.Rom.Success)
    {
      string diagnostics = string.Join(Environment.NewLine, nodeResult.Rom.Diagnostics.Select(item => item.ToString()));
      throw new InvalidOperationException("Node 708's ROM source did not compile.\n" + diagnostics);
    }

    // Compile directly (not through CompileNode's ramSourceOverride) so
    // PackControlTransfers can be disabled, matching the other directly
    // booted node-708 probes: this keeps the program's fixed layout
    // independent of the compiler's default greedy slot-packing.
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
      PackControlTransfers = false
    };

    F18CompileResult result = new F18Compiler().Compile(source, options);
    if (!result.Success)
    {
      string diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.ToString()));
      throw new InvalidOperationException(
          "The node-708 echo probe did not compile. If this fails with "
          + "\"Unknown callable word '18ibits'\" or \"...'delay'\", node 708's ROM source "
          + "does not currently define them -- populate it (e.g. with rom_async) before "
          + "using this probe.\n"
          + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException($"The node-708 echo probe requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (result.EntryPoint != 0)
    {
      int selectedEntry = result.EntryPoint ?? -1;
      throw new InvalidOperationException($"The node-708 echo probe must enter at RAM 0, but the compiler selected 0x{selectedEntry:X3}.");
    }

    return result.Words.Select(item => item & F18InstructionSet.WordMask).ToArray();
  }

  private static byte[] ReadExactly(
      System.IO.Ports.SerialPort port,
      int count,
      int timeoutMilliseconds,
      CancellationToken cancellationToken)
  {
    var result = new byte[count];
    int offset = 0;
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    while (offset < count && stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        int read = port.Read(result, offset, count - offset);
        if (read > 0)
        {
          offset += read;
        }
      }
      catch (TimeoutException)
      {
      }
    }

    if (offset != count)
    {
      throw new TimeoutException(
          $"Node 708 echo test timed out after {timeoutMilliseconds} ms ({offset}/{count} bytes received).");
    }

    return result;
  }

  private static void WaitForTransmitDrain(System.IO.Ports.SerialPort port, int byteCount)
  {
    double wireMilliseconds = byteCount * 10_000.0 / port.BaudRate;
    int delay = Math.Max(2, (int)Math.Ceiling(wireMilliseconds) + 3);
    Thread.Sleep(delay);
  }
}