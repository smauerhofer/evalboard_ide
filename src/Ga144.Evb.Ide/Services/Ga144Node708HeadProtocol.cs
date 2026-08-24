using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>The RAM addresses of node 708's head-protocol commands, resolved from
/// the compiler's own symbol table rather than hardcoded. Unlike this project's
/// other directly-booted node-708 probes, this program only fits node 708's
/// 64-word RAM with control-transfer packing ENABLED (see the remarks on
/// <see cref="Ga144Node708HeadProtocol"/>), so its compiled layout is not
/// guaranteed to stay fixed the way theirs is -- these addresses must be read
/// back after every build, not assumed.</summary>
public sealed record Node708HeadAddresses(int SetTentacle, int SetNode, int WriteRead);

/// <summary>
/// Host-side driver for node 708's new head protocol -- the replacement for the
/// old carrier-clocked reply mechanism (the previous BuildReplyProgram/hi/lo/
/// wait-high/wait-low scheme) AND for the old x1/w1/r1 Kraken transport's way of
/// talking to node 708 itself. All communication with node 708 is now plain
/// async-encoded words (3 bytes each -- the same wire shape as a boot frame
/// field), sent and received exactly as already hardware-validated by
/// <see cref="Ga144Node708EchoProbe"/>:
///  - host to node: <see cref="Ga144Node708Probe.EncodeAsynchronousWord"/>,
///    decoded on the node by '18ibits' (self-calibrating via 'sync', same as
///    every boot-frame field).
///  - node to host: node 708's own 'oword'/'obyt' direct-UART transmit (no
///    host-driven carrier clocking), decoded here by
///    <see cref="DecodeObywordReply"/> -- the inverse of the byte layout
///    <see cref="Ga144Node708EchoProbe"/> independently confirmed
///    (LSB-first, 3 bytes low-to-high, F18 arithmetic '2/' shift).
///
/// 'main' waits for one word and treats it as a CALL ADDRESS:
/// '18ibits drop &gt;r ex' lands on whichever of 'sett'/'setn'/'w/r' the host
/// selected. 'ex' is the F18A "execute" opcode -- per the G144A12/F18A
/// architecture reference (DB001 Figure 3), it SWAPS P and R, it does not just
/// jump-and-forget. That means R automatically receives the continuation right
/// after 'ex' (where 'main' tail-calls itself, compiled as a plain jump back to
/// 'main's start), so whichever command runs returns there via its own closing
/// ';' and the dispatch loop continues forever. This was confirmed against the
/// architecture reference rather than assumed, since a plain "pop R and jump"
/// reading of 'ex' would make that trailing self-call unreachable dead code.
///
/// This class implements only the three primitives node 708 itself exposes
/// (select tentacle, select node, write/read words), exactly as specified. It
/// deliberately does NOT touch or replace the existing KrakenSession/
/// KrakenProtocol tentacle-erection machinery: the exact sequence of these
/// primitives needed to reach a specific node several hops down a tentacle is
/// still being defined, and wiring this into (or in place of) that machinery is
/// a separate, later step -- the same phased approach already used for the
/// obit/oword direct-UART work this reuses.
///
/// Deliberately synchronous, matching <c>KrakenSession</c>'s own shape (a
/// stateful multi-call session) rather than the other probes' one-shot,
/// already-Task-wrapped design: a caller drives an arbitrary sequence of
/// SelectTentacle/SelectNode/WriteRead calls over time, so wrapping the whole
/// thing in one Task.Run the way the probes do would not fit. Callers should
/// wrap individual calls in Task.Run themselves if calling from a UI thread.
///
/// Same restriction as the other node-708 probes: only usable BEFORE a Kraken
/// is erected, since loading this program requires a chip reset.
/// </summary>
public sealed class Ga144Node708HeadProtocol : IDisposable
{
  public const int DefaultBaudRate = Ga144Serial.MaximumBaudRate;

  private const int ResetAssertMilliseconds = 20;
  private const int ResetReleaseToBootMilliseconds = 1;
  private const int ProgramStartMilliseconds = 5;
  private const int ResponseTimeoutMilliseconds = 1_000;

  private readonly System.IO.Ports.SerialPort _port;
  private bool _disposed;

  public Node708HeadAddresses Addresses { get; }

  private Ga144Node708HeadProtocol(System.IO.Ports.SerialPort port, Node708HeadAddresses addresses)
  {
    _port = port;
    Addresses = addresses;
  }

  /// <summary>Resets node 708, boots it with the head protocol program, and
  /// returns a session ready for SelectTentacle/SelectNode/WriteRead calls. The
  /// caller owns the returned session and must Dispose it.</summary>
  public static Ga144Node708HeadProtocol BootAndConnect(
      string portName,
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    ArgumentNullException.ThrowIfNull(chip);
    ArgumentNullException.ThrowIfNull(romLibrary);

    (int[] program, Node708HeadAddresses addresses) = BuildHeadProgram(chip, romLibrary);

    System.IO.Ports.SerialPort port = Ga144Serial.Create(portName);
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
    }
    catch
    {
      port.Dispose();
      throw;
    }

    return new Ga144Node708HeadProtocol(port, addresses);
  }

  /// <summary>Calls 'sett': sets node 708's B register (the port node 708 itself
  /// drives 'w/r' traffic through). Pass the compass port address for the
  /// desired tentacle -- the same value KrakenTopology.PortAddress(HeadCoordinate,
  /// tentacle.Nodes[0]) already computes for the existing erection code.</summary>
  public void SelectTentacle(int portAddress, CancellationToken cancellationToken = default)
  {
    SendWord(Addresses.SetTentacle, cancellationToken);
    SendWord(portAddress, cancellationToken);
  }

  /// <summary>Calls 'setn': sets the target node index (0..last node) within the
  /// currently selected tentacle. How this index drives 'w/r's relay hops is not
  /// yet defined on the host side -- see the class remarks.</summary>
  public void SelectNode(int nodeIndex, CancellationToken cancellationToken = default)
  {
    SendWord(Addresses.SetNode, cancellationToken);
    SendWord(nodeIndex, cancellationToken);
  }

  /// <summary>
  /// Calls 'w/r': writes <paramref name="writeWords"/> then reads back
  /// <paramref name="wordsToRead"/> words. Both counts must be at least 1 --
  /// 'w/r' unconditionally subtracts 1 from each ('dec') to prime its loop
  /// counters, so a count of 0 would wrap to a huge loop instead of doing
  /// nothing, matching the "at least 1 word must be written, at least 1 word
  /// must be read" precondition documented on 'w/r' itself. Enforced here
  /// before anything is sent, rather than left to fail unpredictably on
  /// hardware.
  /// </summary>
  public int[] WriteRead(IReadOnlyList<int> writeWords, int wordsToRead, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(writeWords);
    if (writeWords.Count == 0)
    {
      throw new ArgumentException("'w/r' requires at least 1 word to write.", nameof(writeWords));
    }

    if (wordsToRead <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(wordsToRead), "'w/r' requires at least 1 word to read.");
    }

    SendWord(Addresses.WriteRead, cancellationToken);
    SendWord(wordsToRead, cancellationToken);
    SendWord(writeWords.Count, cancellationToken);
    foreach (int word in writeWords)
    {
      SendWord(word, cancellationToken);
    }

    var readWords = new int[wordsToRead];
    for (int index = 0; index < wordsToRead; index++)
    {
      readWords[index] = ReadWord(cancellationToken);
    }

    return readWords;
  }

  private void SendWord(int value, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    byte[] bytes = new byte[3];
    Ga144Node708Probe.EncodeAsynchronousWord(value, bytes);
    _port.Write(bytes, 0, bytes.Length);
    WaitForTransmitDrain(_port, bytes.Length);
  }

  private int ReadWord(CancellationToken cancellationToken)
  {
    byte[] bytes = ReadExactly(_port, 3, ResponseTimeoutMilliseconds, cancellationToken);
    return DecodeObywordReply(bytes);
  }

  /// <summary>
  /// Inverse of node 708's own 'oword'/'obyt' transmit encoding
  /// (<see cref="Ga144Node708EchoProbe.SimulateExpectedBytes"/>, hardware-confirmed
  /// against 0x15555 -&gt; 55 55 01): 3 bytes, LSB-first, low byte to high --
  /// byte0 = bits 0-7, byte1 = bits 8-15, byte2's low 2 bits = bits 16-17. The
  /// remaining 6 bits of byte2 are sign-extension padding from obyt's arithmetic
  /// '2/' shift, not real data, and are discarded here by masking rather than
  /// carried through.
  /// </summary>
  internal static int DecodeObywordReply(byte[] threeBytes)
  {
    if (threeBytes is null || threeBytes.Length != 3)
    {
      throw new ArgumentException("An obyt/oword reply is exactly 3 bytes.", nameof(threeBytes));
    }

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

  private static (int[] Program, Node708HeadAddresses Addresses) BuildHeadProgram(
      Ga144ChipConfiguration chip, Ga144RomLibrary romLibrary)
  {
    // Transcribed as given, with only the decorative trailing dots this
    // text compiler doesn't accept stripped from earlier probes' number
    // literals -- this source has none, so it is otherwise verbatim.
    const string source = """
        # 0 org
        entry main

        : main 18ibits drop >r ex main ;
        : obit ( dwn-dw) !b over >r delay ;
        : oword ( dw-d)  leap drop  leap drop leap drop  drop ;
        : obyt ( dw-dwx)  then then then  3 obit drop
            7 for dup 1 and 3 xor obit  drop 2/ next
            2 obit ;
        # 0 f18var n
        : sett .loc 18ibits drop b! ;
        : setn .loc 18ibits drop !n ;
        : dec ( w-w') -1 . + ;
        : w/r .loc
          18ibits drop dec dup >r a!
          18ibits drop dec dup >r
          n dup if dec for
            A[ @p >r ]] lit !b r> dup >r
            dup dup . + . + 2* over . +
            A[ @p !b unext ]] lit !b
          next then
          begin 18ibits drop !b next
          n dup if dec for
            A[ @p >r ]] lit !b a !b
            A[ @b !p unext ]] lit !b
          next then
          begin @b oword next ;
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
      // Unlike this project's other directly-booted 708 probes, this program
      // does NOT fit node 708's 64-word RAM with control-transfer packing
      // disabled (it compiles to ~74 words unpacked; only exactly 64/64 with
      // packing on). Packing must stay ON here -- which is why, unlike those
      // other probes, sett/setn/w/r's addresses are read back from the
      // compiler's own symbol table below instead of being hardcoded.
      PackControlTransfers = true
    };

    F18CompileResult result = new F18Compiler().Compile(source, options);
    if (!result.Success)
    {
      string diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.ToString()));
      throw new InvalidOperationException(
          "The node-708 head protocol program did not compile. If this fails with "
          + "\"Unknown callable word '18ibits'\" or \"...'delay'\", node 708's ROM source "
          + "does not currently define them -- populate it (e.g. with rom_async) before "
          + "using this probe.\n"
          + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException(
          $"The node-708 head protocol program requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (result.EntryPoint != 0)
    {
      int selectedEntry = result.EntryPoint ?? -1;
      throw new InvalidOperationException(
          $"The node-708 head protocol program must enter at RAM 0, but the compiler selected 0x{selectedEntry:X3}.");
    }

    var addresses = new Node708HeadAddresses(
        SetTentacle: RequireSymbol(result, "sett"),
        SetNode: RequireSymbol(result, "setn"),
        WriteRead: RequireSymbol(result, "w/r"));

    int[] words = result.Words.Select(item => item & F18InstructionSet.WordMask).ToArray();
    return (words, addresses);
  }

  private static int RequireSymbol(F18CompileResult result, string name)
  {
    if (!result.Symbols.TryGetValue(name, out F18ExportedSymbol? symbol) || symbol is null)
    {
      throw new InvalidOperationException($"The node-708 head protocol program did not define '{name}'.");
    }

    return symbol.Value;
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
          $"Node 708 head protocol read timed out after {timeoutMilliseconds} ms ({offset}/{count} bytes received).");
    }

    return result;
  }

  private static void WaitForTransmitDrain(System.IO.Ports.SerialPort port, int byteCount)
  {
    double wireMilliseconds = byteCount * 10_000.0 / port.BaudRate;
    int delay = Math.Max(2, (int)Math.Ceiling(wireMilliseconds) + 3);
    Thread.Sleep(delay);
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    try { _port.RtsEnable = true; } catch { }
    try { _port.DtrEnable = true; } catch { }
    _port.Dispose();
  }
}