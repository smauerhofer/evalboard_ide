using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Cvm;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// The CVM's external-memory wire protocol -- reverse-engineered against real hardware over the
/// course of building <see cref="Ga144CvmHardwareInstaller"/>'s automatic runtime test, and now
/// shared by that automatic test and the interactive <see cref="CvmDebugSession"/> debugger so both
/// talk to the wire exactly the same way and log transactions in exactly the same shape ("p:aaaa"
/// page/address, raw words at the end of the line).
///
/// A READ command is two words: [page, address-in-page], both plain. The host answers with one
/// reply word: the simulated SRAM's contents at the combined address. A WRITE command is three
/// words: [page, address-in-page, value] -- bit 17 (0x20000) set on the page word marks a write, and
/// BOTH the page word and the address-in-page word are then bitwise-complemented over all 18 bits to
/// recover their real values; only the value word stays plain. The host performs the write and sends
/// no reply at all. See <see cref="Ga144CvmHardwareInstaller"/>'s own remarks for the full real-
/// hardware history (byte order, the 2-word-vs-3-word write correction, the address-in-page
/// inversion fix) that established this.
/// </summary>
internal static class CvmMemoryProtocol
{
  public const int SramWriteFlagBit = 0x20000; // bit 17.
  public const int ResponseTimeoutMilliseconds = 1_000;
  public const int InterWordSettleMilliseconds = 20;
  public const int WakeValue = 0x15555;

  // Node 607's own opcode convention, confirmed by Stefan against this project's real
  // Node607Program.cs remarks (e.g. 'plit at word 0x00E -> opcode 0x800E): opcode = 0x8000 |
  // wordAddress. 'nop, 'plit, 'pop, and 'push all live in this same node 607 source.
  public const int NopSourceNodeCoordinate = 607;
  public const string NopSymbolName = "'nop";
  public const string PlitSymbolName = "'plit";
  public const int PlitLiteralValue = 0x1234;
  public const string PopSymbolName = "'pop";
  public const string PushSymbolName = "'push";

  // How many leading 'nop opcodes the shared test program starts with before 'plit, and how much
  // trailing 'nop padding follows 'pop/'push -- see Ga144CvmHardwareInstaller.RunSramBackedProgramStep
  // for why the padding exists (so the interpreter has more of its own, already-understood 'nop
  // opcode to fetch rather than running into zero-initialized simulated SRAM).
  public const int LeadingNopCount = 5;
  public const int TrailingNopCount = 8;

  /// <summary>
  /// Resolves node 607's own compiled 'nop/'plit/'pop/'push opcodes from THIS run's own compile
  /// (never a frozen reference copy -- every address can move as the source evolves) and builds the
  /// fixed program both the automatic test and the interactive debugger load into simulated SRAM:
  /// <see cref="LeadingNopCount"/> 'nop, 'plit, its literal, 'pop, 'push, then
  /// <see cref="TrailingNopCount"/> trailing 'nop. Returns a null program with a description of
  /// whatever symbol was missing when a required word isn't defined in node 607's current source,
  /// rather than throwing -- callers decide how to surface that (an inconclusive test step here, an
  /// exception there).
  /// </summary>
  public static (List<int>? Program, string? MissingSymbolDescription) TryBuildTestProgram(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    if (!compiledRam.TryGetValue(NopSourceNodeCoordinate, out F18CompileResult? mainCompile))
    {
      return (null, $"node {NopSourceNodeCoordinate:000}'s compiled program is not available");
    }

    if (!mainCompile.Symbols.TryGetValue(NopSymbolName, out F18ExportedSymbol? nopSymbol))
    {
      return (null, $"\"{NopSymbolName}\"");
    }

    if (!mainCompile.Symbols.TryGetValue(PlitSymbolName, out F18ExportedSymbol? plitSymbol))
    {
      return (null, $"\"{PlitSymbolName}\"");
    }

    if (!mainCompile.Symbols.TryGetValue(PopSymbolName, out F18ExportedSymbol? popSymbol))
    {
      return (null, $"\"{PopSymbolName}\"");
    }

    if (!mainCompile.Symbols.TryGetValue(PushSymbolName, out F18ExportedSymbol? pushSymbol))
    {
      return (null, $"\"{PushSymbolName}\"");
    }

    int nopOpcode = 0x8000 | (nopSymbol.Value & F18InstructionSet.WordMask);
    int plitOpcode = 0x8000 | (plitSymbol.Value & F18InstructionSet.WordMask);
    int popOpcode = 0x8000 | (popSymbol.Value & F18InstructionSet.WordMask);
    int pushOpcode = 0x8000 | (pushSymbol.Value & F18InstructionSet.WordMask);

    var program = new List<int>();
    program.AddRange(Enumerable.Repeat(nopOpcode, LeadingNopCount));
    program.Add(plitOpcode);
    program.Add(PlitLiteralValue);
    program.Add(popOpcode);
    program.Add(pushOpcode);
    program.AddRange(Enumerable.Repeat(nopOpcode, TrailingNopCount));
    return (program, null);
  }

  public static string DescribeRequiredSymbols() =>
      $"\"{NopSymbolName}\", \"{PlitSymbolName}\", \"{PopSymbolName}\", and \"{PushSymbolName}\"";

  public static int CombineAddress(int page, int addressInPage) =>
      ((page << 16) | (addressInPage & 0xFFFF)) & (CvmSimulatedSram.WordCapacity - 1);

  // Only page 0 (the low 16 bits of the flat address space) is ever code -- page 1 is the stack,
  // and node 607's own instruction fetches never leave page 0. This is exactly the point at which
  // CombineAddress's own page/address-in-page packing rolls over into page 1.
  public const int Page0WordCount = 0x10000;

  // The disassembler's opcode table: each known node-607 opcode's mnemonic and how many words
  // (its own opcode word included) the instruction occupies in memory. 'plit is the only
  // multi-word instruction right now -- its second word is the literal it pushes, which must be
  // skipped as DATA by the disassembler rather than decoded as if it were itself another opcode.
  // Extend this list as more opcodes become relevant to the debugger; nothing else needs to change.
  public static readonly IReadOnlyList<(string SymbolName, int WordLength)> KnownOpcodes =
  [
    (NopSymbolName, 1),
    (PlitSymbolName, 2),
    (PopSymbolName, 1),
    (PushSymbolName, 1),
  ];

  /// <summary>
  /// Resolves each of <see cref="KnownOpcodes"/> against THIS run's own node 607 compile (never a
  /// frozen reference copy -- every address can move as the source evolves) and returns a map from
  /// the opcode's actual wire/memory value (0x8000 | wordAddress) to its mnemonic and word length,
  /// for <see cref="CvmDebugSession.DisassemblePage0"/> to consume. An opcode whose symbol isn't
  /// defined in the current source is simply omitted -- "for now only a few opcodes are defined" is
  /// expected to grow over time without this method needing to change.
  /// </summary>
  public static IReadOnlyDictionary<int, (string Mnemonic, int WordLength)> BuildOpcodeTable(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    var table = new Dictionary<int, (string, int)>();
    if (!compiledRam.TryGetValue(NopSourceNodeCoordinate, out F18CompileResult? compile))
    {
      return table;
    }

    foreach ((string symbolName, int wordLength) in KnownOpcodes)
    {
      if (compile.Symbols.TryGetValue(symbolName, out F18ExportedSymbol? symbol))
      {
        int opcode = 0x8000 | (symbol.Value & F18InstructionSet.WordMask);
        table[opcode] = (symbolName, wordLength);
      }
    }

    return table;
  }

  // Compact "p:aaaa" rendering of a page/address-in-page pair -- page is the 4-bit page number (a
  // single hex digit, 0-F) and aaaa is the 16-bit address-in-page (always 4 hex digits), matching
  // how the two words actually split up inside CombineAddress above.
  public static string FormatPageAddress(int page, int addressInPage) =>
      $"{page:X}:{addressInPage:X4}";

  // "none" rather than an empty "[]" -- an empty pair of brackets in the middle of a longer
  // transcript line reads as a rendering glitch; a word, this makes the zero case unambiguous.
  public static string FormatWords(IReadOnlyList<int> words) =>
      words.Count == 0 ? "none" : string.Join(", ", words.Select(word => $"0x{word:X5}"));

  public static int ReadWord(NativeWindowsSerialPort port, int timeoutMilliseconds, CancellationToken cancellationToken)
  {
    byte[] bytes = ReadExactly(port, 3, timeoutMilliseconds, cancellationToken);
    int value = bytes[0] | (bytes[1] << 8) | ((bytes[2] & 0x03) << 16);
    return value & F18InstructionSet.WordMask;
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
      throw new TimeoutException($"Timed out after receiving {offset} of {count} bytes.");
    }

    return result;
  }

  public static void WaitForTransmitDrain(NativeWindowsSerialPort port, int byteCount)
  {
    double milliseconds = (byteCount * 10.0 * 1000.0 / port.BaudRate) + 3.0;
    Thread.Sleep((int)Math.Ceiling(milliseconds));
  }
}