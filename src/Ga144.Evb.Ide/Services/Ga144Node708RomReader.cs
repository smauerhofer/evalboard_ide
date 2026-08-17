using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Standalone, pre-Kraken direct-boot reader for node 708's own 64-word ROM
/// (0x080..0x0BF). Node 708 is the Kraken head: once a Kraken is erected it can
/// never be read through the normal tentacle R1 mechanism (there is no route to
/// it), and per <see cref="KrakenLiveController"/>'s lifetime rule a resident
/// Kraken must never be reset, reloaded, or re-probed. This reader is therefore
/// only usable BEFORE a Kraken owns the chip -- exactly the same restriction
/// that already applies to <see cref="Ga144Node708Probe"/>, whose proven
/// System.IO.Ports.SerialPort reset/boot pattern this class deliberately
/// mirrors instead of using the newer NativeWindowsSerialPort transport.
///
/// The uploaded "dump-rom" program walks its own ROM from 0x080 and serializes
/// each word back using the SAME already-hardware-verified carrier-clock
/// primitives as the Kraken node-708 <c>reply</c> helper (hi/lo/wait-high/
/// wait-low/consume/send-word, see KrakenSession.BuildReplyProgram) -- NOT the
/// user's new, not-yet-verified delay/obit/oword mechanism, so this tool does
/// not depend on the very thing it may end up being used to help verify.
/// </summary>
public sealed class Ga144Node708RomReader
{
  public const int DefaultBaudRate = Ga144Serial.MaximumBaudRate;

  private const int ResetAssertMilliseconds = 20;
  private const int ResetReleaseToBootMilliseconds = 1;
  private const int ProgramStartMilliseconds = 5;
  private const int ResponseTimeoutMilliseconds = 1_000;

  public async Task<int[]> ReadRomAsync(
      string portName,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    return await Task.Run(() => ReadRom(portName, cancellationToken), cancellationToken);
  }

  private static int[] ReadRom(string portName, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();

    using System.IO.Ports.SerialPort port = Ga144Serial.Create(portName);
    port.ReadTimeout = 40;
    port.WriteTimeout = 1_000;
    port.Open();

    // Same Open()-then-reassert discipline as Ga144Node708Probe: the managed
    // SerialPort only pushes RtsEnable/DtrEnable to the driver after Open()
    // creates the handle, so there is a brief driver-default RTS window that
    // can glitch RESET- on Port A. Re-assert immediately.
    port.RtsEnable = true;
    port.DtrEnable = true;

    try
    {
      port.DtrEnable = true;
      port.RtsEnable = false; // RTS low asserts RESET- (verified EVB polarity)
      Thread.Sleep(ResetAssertMilliseconds);

      cancellationToken.ThrowIfCancellationRequested();
      port.DiscardInBuffer();
      port.DiscardOutBuffer();

      port.RtsEnable = true; // release RESET-
      Thread.Sleep(ResetReleaseToBootMilliseconds);

      byte[] bootStream = BuildBootStream();
      port.Write(bootStream, 0, bootStream.Length);
      WaitForTransmitDrain(port, bootStream.Length);
      Thread.Sleep(ProgramStartMilliseconds);

      // Ignore framing debris from node 708's TX pin moving off its reset
      // weak-pulldown state into the dump-rom program's driven idle level.
      port.DiscardInBuffer();

      var words = new int[RomComparison.RomWordCount];
      var carriers = new byte[36];
      for (int bit = 0; bit < 18; bit++)
      {
        carriers[bit * 2] = 0x00;
        carriers[bit * 2 + 1] = 0xFF;
      }

      for (int index = 0; index < words.Length; index++)
      {
        cancellationToken.ThrowIfCancellationRequested();

        port.Write(carriers, 0, carriers.Length);
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

        words[index] = word & F18InstructionSet.WordMask;
      }

      return words;
    }
    finally
    {
      // Leave RESET- released and DTR high regardless of how this exits, the
      // same best-effort discipline Ga144Node708Probe uses.
      try { port.RtsEnable = true; } catch { }
      try { port.DtrEnable = true; } catch { }
    }
  }

  /// <summary>
  /// One asynchronous boot frame carrying the dump-rom program: completion
  /// address 0, transfer address 0 (RAM 0, where the program is entered),
  /// program word count, then the program words -- the same frame shape
  /// Ga144Node708Probe uses for its first post-reset frame.
  /// </summary>
  private static byte[] BuildBootStream()
  {
    int[] program = BuildDumpRomProgram();
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
  /// Compiles the resident "dump-rom" program: reads all 64 ROM words starting
  /// at 0x080 and streams each one out using the proven carrier-clock send
  /// primitives lifted from KrakenSession.BuildReplyProgram's 'reply' helper.
  /// Verified to compile to exactly 64 RAM words with entry point 0.
  /// </summary>
  private static int[] BuildDumpRomProgram()
  {
    const string source = """
        # 0 org
        entry dump-rom

        : dump-rom
            io b!
            lo
            0x080 a!
            63 for
                @+
                send-word
            next
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

    var compiler = new F18Compiler();
    // Same reasoning as BuildReplyProgram: this is a fixed-layout resident
    // artifact booted directly to node 708, not ordinary project source, so
    // backward-branch packing is disabled to keep its layout independent of
    // codegen changes.
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
      throw new InvalidOperationException("The node-708 dump-rom helper did not compile.\n" + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException($"The node-708 dump-rom helper requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (result.EntryPoint != 0)
    {
      int selectedEntry = result.EntryPoint ?? -1;
      throw new InvalidOperationException($"The node-708 dump-rom helper must enter at RAM 0, but the compiler selected 0x{selectedEntry:X3}.");
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
        // SerialPort.ReadTimeout is short (40 ms); keep polling within our own
        // overall timeout budget instead of surfacing every short read gap.
      }
    }

    if (offset != count)
    {
      throw new TimeoutException(
          $"Node 708 dump-rom read timed out after {timeoutMilliseconds} ms ({offset}/{count} bytes received).");
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
