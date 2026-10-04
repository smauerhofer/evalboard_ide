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
///
/// <b>CVM2 (2026-09-01): this wire-level shape itself is UNCHANGED, only which node answers it.</b>
/// CVM2 moves the CPU from node 607 to node 507 (<see cref="Node507Program"/> -- corrected 2026-09-01
/// from an earlier, mistaken attribution to node 508 in this project's own session history; 508 is
/// not part of CVM2's active mesh at all, see <see cref="Node508Program"/>'s own remarks), but node
/// 507's own external-memory primitives (<c>m/@</c>: <c>!b !b @b</c> -- two plain writes then one
/// read; <c>m/!</c>: <c>inv !b inv !b !b</c> -- two INVERTED writes then one plain write, zero reads)
/// are a byte-for-byte structural match for this class's existing READ/WRITE shape above (2-plain-word
/// read; 3-word write with the first two inverted) -- confirmed against this project's own AN003
/// (SRAM Control Cluster) reference documentation, which documents that exact convention
/// (<c>ex@</c>: "write 2 words [+p +a]... read the result [w]"; <c>ex!</c>: "write 3 words [-p -a
/// w]. The first two words are inverted"). Node 607 (the SRAM-request router) and node 707/708
/// (the relay chain to the PC) relay these words through -- their real, verified CVM2 source confirms
/// this directly now (see each node's own remarks) rather than the earlier "appears to" hedge -- so
/// this class's <see cref="ReadWord"/>/<see cref="CombineAddress"/>/<see cref="SramWriteFlagBit"/>
/// logic, and every caller in <see cref="Ga144CvmHardwareInstaller"/>, needs NO changes for CVM2. This
/// has been checked against node 507's own source and this project's AN003 doc, but NOT yet against
/// real hardware -- treat it as strong evidence, not a certainty, until Stefan confirms it on the
/// bench.
/// </summary>
internal static class CvmMemoryProtocol
{
  public const int SramWriteFlagBit = 0x20000; // bit 17.
  public const int ResponseTimeoutMilliseconds = 1_000;
  public const int InterWordSettleMilliseconds = 20;
  public const int WakeValue = 0x15555;

  // CVM2 (2026-09-01): moved from node 607 (CVM1's old CPU) to node 507 (CVM2's entire CPU -- see
  // Node507Program's own remarks). Corrected 2026-09-01 from node 508, this project's own earlier
  // mistaken attribution -- 508 is not part of CVM2's active mesh, see Node508Program's own remarks.
  // The opcode convention also changed: node 507's own "local execute" dispatch tag is 0x8800, not
  // node 607's old flat 0x8000 -- see CvmAssemblyLanguage.Node507Cvm2LocalExecuteTagBits's own remarks
  // for the derivation (also not yet confirmed with Stefan). 'nop, 'plit, 'pop, and 'push all live in
  // this same node 507 source, under the SAME tick-names CVM1's node 607 used.
  public const int NopSourceNodeCoordinate = 507;
  public const string NopSymbolName = "'nop";
  public const string PlitSymbolName = "'plit";
  public const int PlitLiteralValue = 0x1234;
  public const string PopSymbolName = "'pop";
  public const string PushSymbolName = "'push";

  // Also node 507's own convention now (0x8800 | wordAddress, see above) -- CVM1's old value here was
  // 0x8000, node 607's own flat tag. Node507Program.Source's own 'ret ( rs-rs) pops the return address
  // /call pushed and installs it into A (i.e. P), completing a call/return round trip -- same
  // semantics as CVM1's node 607 'ret, just relocated.
  public const string RetSymbolName = "'ret";

  // CVM2 (2026-09-01): node 507's own "local execute" tag -- see the remarks on NopSourceNodeCoordinate
  // above and CvmAssemblyLanguage.Node507Cvm2LocalExecuteTagBits's own remarks for the derivation.
  // TryBuildTestProgram below ORs this into each of the four opcodes it builds, replacing CVM1's flat
  // 0x8000.
  public const int LocalExecuteTagBits = 0x8800;

  // How many leading 'nop opcodes the shared test program starts with before 'plit, and how much
  // trailing 'nop padding follows 'pop/'push -- padding exists so the interpreter has more of its
  // own, already-understood 'nop opcode to fetch rather than running into zero-initialized simulated
  // SRAM. (Originally sized for Ga144CvmHardwareInstaller's now-removed RunSramBackedProgramStep,
  // 2026-09-18 -- this program is still built the same way for StartDebugSession's own fallback.)
  //
  // RETIRED, 2026-09-30 (new-VM reset): TryBuildTestProgram no longer builds a 'plit/'pop/'push
  // sequence at all -- see that method's own remarks below -- so LeadingNopCount/TrailingNopCount are
  // no longer read by it. Kept, unused, rather than deleted, per this project's own "do not remove"
  // discipline; TestProgramWordCount (below) is what the rewritten method actually uses.
  public const int LeadingNopCount = 5;
  public const int TrailingNopCount = 8;

  // ADDED 2026-09-30 (new-VM reset): total word count for the rewritten TryBuildTestProgram below --
  // chosen to match the old program's own total length (5 leading 'nop + 'plit + its literal + 'pop +
  // 'push + 8 trailing 'nop = 17 words) purely so the smoke test still exercises the same number of
  // sequential page-0 reads as before, even though every word is now a plain nop.
  public const int TestProgramWordCount = 17;

  /// <summary>
  /// Builds the fixed program both the automatic test and the interactive debugger load into
  /// simulated SRAM: <see cref="TestProgramWordCount"/> repetitions of the new VM's own fixed
  /// <c>nop</c> opcode (<c>0x0000</c>).
  ///
  /// <b>REWRITTEN, 2026-09-30 (new-VM reset).</b> Before this reset, this method resolved node 507's
  /// own live-compiled <c>'nop</c>/<c>'plit</c>/<c>'pop</c>/<c>'push</c> symbols and tagged each with
  /// <see cref="LocalExecuteTagBits"/> -- a CVM2-era, tagged/node-resolved scheme. Per Stefan's
  /// 2026-09-30 instruction ("there is a new virtual machine. all opcodes are invalid. except that
  /// 'nop' has the opcode '0'."), <c>nop</c> is now <see cref="CvmInstructionSet.CvmOperandEncoding.FixedOpcode"/>
  /// -- a fully self-describing, no-live-node-resolution-needed word -- and <c>plit</c>/<c>pop</c>/
  /// <c>push</c> no longer exist as CVM opcodes at all, so there is nothing left for this method to
  /// resolve against a live node compile. <paramref name="compiledRam"/> is accepted only to keep this
  /// method's signature (and its caller in <see cref="Ga144CvmHardwareInstaller"/>) unchanged; it is
  /// no longer read.
  ///
  /// This is, if anything, a MORE faithful smoke test under the new VM than the old one: the exact
  /// property this program is meant to check -- "every page-0 read lands at exactly the next
  /// sequential address" (see <see cref="TryBuildDebuggerTestProgram"/>'s own remarks) -- is exercised
  /// just as well by a straight run of <c>nop</c> words as it was by the old, more elaborate sequence,
  /// and needs no live node compile to succeed at all.
  /// </summary>
  public static (List<int>? Program, string? MissingSymbolDescription) TryBuildTestProgram(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    CvmInstructionSet.CvmInstructionShape? nopShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.NopMnemonic);
    if (nopShape is null || nopShape.Encoding != CvmInstructionSet.CvmOperandEncoding.FixedOpcode)
    {
      // Should not happen in practice -- nop's FixedOpcode shape is wired directly in
      // CvmInstructionSet.Instructions, not resolved against compiledRam -- but reported the same
      // "missing symbol" way as before rather than thrown, so callers don't need a new case.
      return (null, $"\"{CvmInstructionSet.NopMnemonic}\"");
    }

    var program = new List<int>(Enumerable.Repeat(nopShape.Tag, TestProgramWordCount));
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
  /// Deliberately kept separate from <see cref="TryBuildTestProgram"/>'s own minimal smoke test
  /// (used by <see cref="Ga144CvmHardwareInstaller.StartDebugSessionAsync"/>'s own fallback, and
  /// formerly by the GA144 window's "Install &amp; run CVM test" button, removed 2026-09-18): that
  /// simpler program's own pass/fail check requires every page-0 read to land at exactly the next
  /// sequential address, and this program deliberately jumps around (call/ret, and the exploratory
  /// br/cbr) -- folding this into the shared program would make that check report a read-order
  /// "failure" that isn't actually a regression, just a check that doesn't know about jumps yet. Per
  /// Stefan's own choice, this stays a debugger-only variant instead.
  /// </summary>
  public static (List<int>? Program, string? MissingSymbolDescription) TryBuildDebuggerTestProgram(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    // Labels aren't needed here -- this program's own words are re-assembled again moments later by
    // CvmDebuggerViewModel.StartAsync (via the Assembly Code editor, which defaults to this exact same
    // source text), and THAT call site is the one that actually captures labels for the memory
    // inspector's "Label" column -- so the label map from this one, install-time assemble is discarded.
    // (2026-10-04: assembled by the ONE CVM assembler, CvmAssembler, then linked against the live primitive
    // table -- see CvmAssemblyLanguage.AssembleProgram. A failure here means CvmDebuggerDefaultProgram.Source
    // no longer assembles/links against the current mesh; the installer then falls back to the minimal test.)
    (List<int>? words, IReadOnlyDictionary<string, int>? _, string? error) =
        CvmAssemblyLanguage.AssembleProgram(CvmDebuggerDefaultProgram.Source, compiledRam);
    return (words, error);
  }

  public static string DescribeRequiredSymbols() =>
      $"\"{NopSymbolName}\", \"{PlitSymbolName}\", \"{PopSymbolName}\", and \"{PushSymbolName}\"";

  public static int CombineAddress(int page, int addressInPage) =>
      ((page << 16) | (addressInPage & 0xFFFF)) & (CvmSimulatedSram.WordCapacity - 1);

  // Only page 0 (the low 16 bits of the flat address space) is ever code -- page 1 is the stack,
  // and the CPU node's own instruction fetches never leave page 0 (node 507 now, CVM2; node 607
  // before it, CVM1). This is exactly the point at which CombineAddress's own page/address-in-page
  // packing rolls over into page 1.
  public const int Page0WordCount = 0x10000;

  // The wire/memory-level opcode table (the CPU node's own F18 symbols -> opcode word + word length --
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