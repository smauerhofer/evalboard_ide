using Ga144.Cvm.Toolchain;
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

  // Also node 607's own convention (0x8000 | wordAddress) -- confirmed against real hardware
  // together with 'nop/'plit/'pop/'push above. Node607.f18's own 'ret ( s-s) pops the return address
  // /call pushed and installs it into A (i.e. P), completing a call/return round trip.
  public const string RetSymbolName = "'ret";

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

    // 0x8000 | address is a CVM opcode -- a 16-bit CVM word (CvmWordCodec.WordMask), not the wider
    // 18-bit F18 wire word the symbol's own address happens to be stored as.
    int nopOpcode = 0x8000 | (nopSymbol.Value & CvmWordCodec.WordMask);
    int plitOpcode = 0x8000 | (plitSymbol.Value & CvmWordCodec.WordMask);
    int popOpcode = 0x8000 | (popSymbol.Value & CvmWordCodec.WordMask);
    int pushOpcode = 0x8000 | (pushSymbol.Value & CvmWordCodec.WordMask);

    var program = new List<int>();
    program.AddRange(Enumerable.Repeat(nopOpcode, LeadingNopCount));
    program.Add(plitOpcode);
    program.Add(PlitLiteralValue);
    program.Add(popOpcode);
    program.Add(pushOpcode);
    program.AddRange(Enumerable.Repeat(nopOpcode, TrailingNopCount));
    return (program, null);
  }

  /// <summary>
  /// The interactive debugger's own default test program -- a full CVM assembly language source
  /// (<see cref="CvmDebuggerDefaultProgram.Source"/>) exercising 43 of the CVM's 73 opcodes with a
  /// log-checkable expected value for each, rather than <see cref="TryBuildTestProgram"/>'s minimal
  /// 5 'nop/'plit/'pop/'push/8 'nop smoke test above. See <see cref="CvmDebuggerDefaultProgram"/>'s
  /// own remarks for exactly which opcodes are covered, which are deliberately excluded (and why),
  /// and which two blocks are exploratory rather than asserted-correct.
  ///
  /// Deliberately NOT used by <see cref="Ga144CvmHardwareInstaller.InstallAndRunAsync"/>'s automatic
  /// "Install &amp; run CVM test" step (that step still calls <see cref="TryBuildTestProgram"/>
  /// directly): that step's own pass/fail check requires every page-0 read to land at exactly the
  /// next sequential address, and this program deliberately jumps around (call/ret, and the
  /// exploratory br/ifbr) -- folding this into the shared program would make the automatic test
  /// report a read-order "failure" that isn't actually a regression, just a check that doesn't know
  /// about jumps yet. Per Stefan's own choice, this stays a debugger-only variant instead.
  /// </summary>
  public static (List<int>? Program, string? MissingSymbolDescription) TryBuildDebuggerTestProgram(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    (List<CvmAssemblyLanguage.CvmAsmInstruction>? instructions, string? parseError) =
        CvmAssemblyLanguage.ParseSource(CvmDebuggerDefaultProgram.Source);
    if (instructions is null)
    {
      // ParseSource only fails on a malformed literal source file -- this string is a project
      // constant, not user input, so a failure here would mean CvmDebuggerDefaultProgram.Source
      // itself was edited into something CvmAssemblyLanguage can no longer parse.
      return (null, parseError);
    }

    return CvmAssemblyLanguage.Assemble(instructions, compiledRam);
  }

  public static string DescribeRequiredSymbols() =>
      $"\"{NopSymbolName}\", \"{PlitSymbolName}\", \"{PopSymbolName}\", and \"{PushSymbolName}\"";

  public static int CombineAddress(int page, int addressInPage) =>
      ((page << 16) | (addressInPage & 0xFFFF)) & (CvmSimulatedSram.WordCapacity - 1);

  // Only page 0 (the low 16 bits of the flat address space) is ever code -- page 1 is the stack,
  // and node 607's own instruction fetches never leave page 0. This is exactly the point at which
  // CombineAddress's own page/address-in-page packing rolls over into page 1.
  public const int Page0WordCount = 0x10000;

  // The wire/memory-level opcode table (node 607's own F18 symbols -> opcode word + word length)
  // moved to CvmAssemblyLanguage, which layers the CVM assembly language's own mnemonics (nop,
  // pushlit, push, pop) on top of it for both assembly and disassembly. See that file's remarks for
  // why the two naming layers -- F18 source symbols here, CVM asm mnemonics there -- are kept
  // separate.

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