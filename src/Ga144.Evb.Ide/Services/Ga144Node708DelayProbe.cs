using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Standalone, pre-Kraken diagnostic that captures the live-calibrated timing
/// constant node 708's own ROM computes for the CURRENT incoming transmission
/// (what the user's <c>rom_async</c> ROM source calls <c>d</c>, the value
/// <c>sync</c> leaves on the stack and <c>start</c>/<c>delay</c> reuse for
/// mid-bit sampling). <c>d</c> is never stored anywhere at rest -- it is
/// recomputed by <c>sync</c> at the head of essentially every 18-bit field a
/// boot frame carries -- so the only way to observe it is to catch it live
/// during a real exchange.
///
/// This probe does that by uploading a tiny RAM program that calls the node's
/// OWN already-hardware-verified <c>sync</c> ROM word directly (resolved by
/// address from compiling the node's real, currently configured ROM source --
/// not a reimplementation), waits for one genuine line transition from the
/// host to measure against, and streams the resulting <c>d</c> back using the
/// same proven carrier-clock send primitives as
/// <see cref="Ga144Node708RomReader"/>'s dump-rom program.
///
/// Same restriction as <see cref="Ga144Node708RomReader"/>: only usable BEFORE
/// a Kraken is erected, since loading this program requires a chip reset.
///
/// One inference this probe cannot fully verify statically: the seed value
/// pushed before calling <c>sync</c> (see <see cref="SyncSeed"/>). Reading
/// <c>sync</c>'s own body, its <c>wait</c> helper appears to discard that seed
/// unconditionally inside its polling loop rather than testing it, so 0 is
/// used here as a low-risk default -- but this has not been confirmed against
/// whatever precondition <c>cold</c> actually supplies during a real boot. If
/// the returned value looks implausible, that seed is the first thing to
/// revisit.
/// </summary>
public sealed class Ga144Node708DelayProbe
{
  public const int DefaultBaudRate = Ga144Serial.MaximumBaudRate;

  // Best-effort seed pushed on the data stack before calling the ROM's sync.
  // See the class remarks: sync's 'wait' helper appears to drop this
  // unconditionally rather than testing it, so its exact value is believed
  // to be inert, but that is inferred from the transcribed source, not
  // confirmed against the real cold-boot precondition.
  private const int SyncSeed = 0;

  private const int ResetAssertMilliseconds = 20;
  private const int ResetReleaseToBootMilliseconds = 1;
  private const int ProgramStartMilliseconds = 5;

  // Lead-in byte count prefixed onto the real 36-byte carrier train, all sent
  // as one unbroken alternating-level stream (see ReadDelay). Generous on
  // purpose: at 921600 baud, 64 bytes is only ~0.7 ms of line time, well
  // inside the ~4.1 ms reasonableness window the datasheet documents for
  // these boot ROMs, so this comfortably covers however many edges sync's
  // wait loop actually needs without risking a silent gap.
  private const int TriggerByteCount = 64;
  private const int ResponseTimeoutMilliseconds = 1_000;

  public async Task<int> ReadDelayAsync(
      string portName,
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    ArgumentNullException.ThrowIfNull(chip);
    ArgumentNullException.ThrowIfNull(romLibrary);

    int[] program = BuildCaptureDelayProgram(chip, romLibrary);
    return await Task.Run(() => ReadDelay(portName, program, cancellationToken), cancellationToken);
  }

  private static int ReadDelay(string portName, int[] program, CancellationToken cancellationToken)
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

      // One continuous, uninterrupted alternating-level stream: a generous
      // lead-in for sync's wait loop to time against, immediately followed by
      // the standard 36-byte per-word carrier train for send-word -- with NO
      // gap and no intervening discard. An earlier version paused between a
      // short trigger burst and the real carrier train; if sync's wait loop
      // had not yet finished when that silent gap began, it would sit through
      // the silence and then consume the first few pulses of the real carrier
      // train once it resumed, leaving send-word short of carrier for its own
      // bits -- observed as a response that started correctly but stalled
      // partway (e.g. 15/18 bytes). Sending everything as one unbroken stream
      // removes that silent window entirely: whichever routine is currently
      // polling @b just keeps consuming edges from the same ongoing stream.
      var stream = new byte[TriggerByteCount + 36];
      for (int index = 0; index < stream.Length; index++)
      {
        stream[index] = (index % 2 == 0) ? (byte)0x00 : (byte)0xFF;
      }

      port.Write(stream, 0, stream.Length);
      WaitForTransmitDrain(port, stream.Length);

      byte[] response = ReadExactly(port, 18, ResponseTimeoutMilliseconds, cancellationToken);

      int word = 0;
      for (int bit = 0; bit < 18; bit++)
      {
        if (response[bit] >= 0x80)
        {
          word |= 1 << bit;
        }
      }

      return word & F18InstructionSet.WordMask;
    }
    finally
    {
      try { port.RtsEnable = true; } catch { }
      try { port.DtrEnable = true; } catch { }
    }
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
  /// Compiles the "capture-delay" RAM program against node 708's REAL,
  /// currently configured ROM exports (the same PredefinedSymbols mechanism
  /// used when compiling any node's ordinary RAM source) so that referencing
  /// <c>sync</c> resolves to that ROM's own, already hardware-verified
  /// address -- not a guessed or reimplemented one.
  /// </summary>
  private static int[] BuildCaptureDelayProgram(Ga144ChipConfiguration chip, Ga144RomLibrary romLibrary)
  {
    string source = $$"""
        # 0 org
        entry capture-delay

        # {{SyncSeed}} constant sync-seed

        : capture-delay
            io b!
            lo
            sync-seed sync
            send-word
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

        : send-word ( w-)
            17 for
                dup dup 2/ 2* xor
                if send-one else send-zero then
                drop 2/
            next
            drop
        ;
        """;

    // Compile node 708's REAL, currently configured ROM only -- to resolve
    // 'sync' to its real, already hardware-verified address. The node's
    // current project RAM (if any) is compiled alongside by CompileNode but
    // is irrelevant here and deliberately ignored; this probe supplies its
    // own RAM image below instead of using it.
    var compileService = new F18NodeCompilationService(chip, romLibrary, romLibrary.SystemMacros);
    F18NodeCompilationResult nodeResult = compileService.CompileNode(KrakenTopology.HeadCoordinate);

    if (!nodeResult.Rom.Success)
    {
      string diagnostics = string.Join(Environment.NewLine, nodeResult.Rom.Diagnostics.Select(item => item.ToString()));
      throw new InvalidOperationException("Node 708's ROM source did not compile.\n" + diagnostics);
    }

    // Compile the capture-delay RAM program directly (not through
    // CompileNode's ramSourceOverride) so PackControlTransfers can be
    // disabled: this program's forward branches (inside send-word's
    // if/else, same shape as BuildReplyProgram's 'reply') do not all fit
    // when the compiler's default greedy slot-packing is active.
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
          "The node-708 delay-capture probe did not compile. If this fails with "
          + "\"Unknown callable word 'sync'\", node 708's ROM source does not currently "
          + "define 'sync' -- populate it (e.g. with rom_async) before using this probe.\n"
          + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException($"The node-708 delay-capture probe requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (result.EntryPoint != 0)
    {
      int selectedEntry = result.EntryPoint ?? -1;
      throw new InvalidOperationException($"The node-708 delay-capture probe must enter at RAM 0, but the compiler selected 0x{selectedEntry:X3}.");
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
          $"Node 708 delay-capture read timed out after {timeoutMilliseconds} ms ({offset}/{count} bytes received).");
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