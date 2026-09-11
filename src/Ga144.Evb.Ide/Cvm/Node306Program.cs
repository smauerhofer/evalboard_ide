namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 306's resident F18 source -- CVM2's 32-bit address-register node. Register COUNT IS NOT FIXED:
/// per Stefan's 2026-09-11 explanation, it depends on <c>org</c> -- capacity is <c>floor(org / 2)</c>
/// (register storage occupies word addresses <c>[0, org)</c> below this node's own code), capped at 8 by
/// <c>ar/main</c>'s own 3-bit register-select field. See <see cref="Source"/>'s own remarks for the
/// full history (an earlier belief that the count was a fixed "4," then "6," both superseded), reached
/// from node 307's own RIGHT port ("1101_10??_????_????", <c># right /b</c>), holding its registers as
/// word pairs (address word, page word) in its own local RAM, letting a fully-loaded register address any
/// word across the whole GA144 memory space, not just this node's own RAM. Imports node 307
/// (<c>k/r@</c>/<c>k/r!</c>/<c>k/pop</c>/<c>k/push</c>/<c>k/leave</c>). See
/// <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet.ArithmeticStoreAddressRegisterMnemonic"/>'s own
/// remarks for how the CVM assembler now reaches all six (a real embedded register-index operand,
/// <c>CvmOperandEncoding.NodeResolvedEmbeddedValue</c>), where it used to only ever emit register 0.
///
/// <b>RE-SYNCED 2026-09-09</b> against Stefan's own <c>workspace.yaml</c> project export, as part of
/// the opcode/assembler-vs-node reconciliation audit: this node's mnemonics changed from an EARLIER,
/// self-describing <c>ldar</c>/<c>star</c>/<c>inca</c>/<c>deca</c>/<c>lda</c>/<c>sta</c> scheme (fixed
/// 4-bit-tag/2-bit-register-index words needing no live compile) to the current <c>'lda</c>/<c>'sta</c>/
/// <c>'arinc</c>/<c>'ardec</c>/<c>'arld</c>/<c>'arst</c> -- six ordinary TICK-PREFIXED words, resolved
/// like every other tagged mnemonic in this mesh (<see cref="CvmInstructionSet.CvmOperandEncoding.None"/>,
/// node-resolved against a live compile, no embedded operand of their own). <c>ar/main</c>'s own
/// dispatch now masks the incoming call byte to 7 bits (<c>dup 0x07 and 2* a!</c>, selecting one of the
/// four registers by its own low bits) before jumping to the called word's compiled address, the same
/// "call word, address in opcode" idiom node 507/508/509/etc. all use elsewhere in this mesh -- NOT the
/// old self-describing shape. Per Stefan's own trailing comment block, all six now share the flat
/// <c>1101_10??_????_????</c> range (the earlier per-op sub-splits -- <c>1101_1011</c> for <c>ldar</c>,
/// etc. -- no longer apply): <c>lda</c>/<c>sta</c> load/store r via the address register named in the
/// low opcode bits; <c>arinc</c>/<c>ardec</c> increment/decrement it; <c>arld</c>/<c>arst</c> load/store
/// the register's own 32-bit value itself (address in r, page on the data stack).
///
/// <b>CONFIRMED WORKING 2026-09-11</b> -- see the CVM Debugger's own live disassembly, after the
/// "<c>arld 1</c> silently becomes nop" incident (see
/// <see cref="Ga144.Evb.Ide.Services.CvmAssemblyLanguage.DiagnoseUnresolvedWiredMnemonic"/>'s own remarks)
/// was root-caused and fixed and Stefan reported "node 306 now compiles fine": a test program
/// (<c>lit 1; pushlit 2; arld 1; lda 1; arst 1; push; nop; nop; nop; halt</c>) disassembled as
/// <c>D9A9 arld 0x0001</c> / <c>D919 lda 0x0001</c> / <c>D9C1 arst 0x0001</c> -- all three now real,
/// register-embedded opcodes (low bit of each = 1, matching the operand), not the bare, operand-dropping
/// <c>nop</c> from before. Decoding each against
/// <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet.Node306FunctionFieldBitMask"/>/
/// <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet.Node306FunctionFieldShift"/> recovers tag
/// <c>0xD800</c> plus function-select addresses 0x31 (<c>'arld</c>), 0x23 (<c>'lda</c>), and 0x38
/// (<c>'arst</c>) -- all comfortably inside the 0-0x3F window <c>ar/main</c>'s own <c>0x3f and</c> can
/// reach, confirming the CVM-opcode-level mechanism (unaffected by the <c>ar/main</c>/<c>'lda</c>/
/// <c>'sta</c> body revision documented on <see cref="Source"/> below, same day) needed no further change.
/// </summary>
internal static class Node306Program
{
  /// <summary>The node this program is always deployed to -- CVM2's address-register node (register count is <c>org</c>-dependent, see this class's own remarks).</summary>
  public const int Coordinate = 306;

  /// <summary>
  /// Node 306's full resident F18 source. <b>RE-SYNCED 2026-09-09</b> against Stefan's own
  /// <c>workspace.yaml</c> project export (the "CVM2" project's Host chip) as part of the opcode/
  /// assembler-vs-node reconciliation audit -- the PRIOR "SYNCED, 2026-09-08" copy here had itself
  /// drifted from Stefan's live source (mislabeled header comment, a differently-shaped <c>ar/main</c>
  /// prelude). This copy is verbatim from that export. The six opcodes (<c>ldar</c>/<c>star</c>/
  /// <c>inca</c>/<c>deca</c>/<c>lda</c>/<c>sta</c>) and their bit patterns, described in the class
  /// remarks above, are UNCHANGED by this re-sync -- only the body of <c>ar/main</c>'s own dispatch
  /// prelude and the source's own header line differ from the prior copy. Any other specific claim
  /// above (a compiled address, a verification result) predates this sync and should be treated as
  /// stale until re-confirmed against a fresh compile.
  ///
  /// <b>RE-PASTED 2026-09-11</b> by Stefan, correcting only the header/trailing COMMENTS (the F18 code
  /// itself -- every actual opcode line -- is byte-for-byte identical to the 2026-09-09 copy above): "(
  /// contains 4 32-bit address register )" was wrong and is now "( contains 6 32-bit address register
  /// )", and the trailing opcode table's own register-count note ("register encoded in aaa") is now
  /// spelled out per-op as "0..5". This confirms node 306 was ALWAYS a six-register node -- ar/main's
  /// own "dup 0x07 and 2* a!" (extracting a 3-bit register-select field, values 0-7, of which only 0-5
  /// are wired to a real register) never changed -- it was this project's OWN prior documentation and
  /// tooling (CvmInstructionSet/CvmAssemblyLanguage/CCodeGenerator all "fixed to address register 0")
  /// that undercounted it, not the hardware.
  ///
  /// <b>REVISED 2026-09-11 (same day, "here are the fixed nodes" -- supplied alongside node 307/407's own
  /// fixes, see those classes' own remarks), confirmed compiling via the disassembly on this class's own
  /// remarks above.</b> Runtime-mechanics changes only -- the CVM-opcode-level tag/shift/mask math
  /// (<see cref="Ga144.Cvm.Toolchain.CvmInstructionSet.Node306FunctionFieldBitMask"/> and siblings) is
  /// UNAFFECTED, confirmed by the disassembly on this class's own remarks above:
  /// <list type="bullet">
  /// <item><c>ar/reg</c> is now commented out (<c>// : ar/reg 0x06 and a! ;</c>) -- unused by any of the
  /// six ops' own bodies even before this (each already sets <c>a</c> directly via <c>ar/main</c>'s own
  /// <c>dup 0x07 and 2* a!</c>), so this changes nothing observable; reproduced verbatim rather than
  /// silently dropped.</item>
  /// <item><c>ar/main</c>'s own body changed from <c>A[ 2* !p !p ]] lit !b @b @b ...</c> (two fetches via
  /// <c>@b</c>) to <c>A[ !p drop ]] lit !b @b ...</c> (one fetch), and gained a <c>&gt;r</c> immediately
  /// before its own trailing <c>ex</c> (was bare <c>... 0x3f and ex ar/leave ;</c>, now
  /// <c>... 0x3f and &gt;r ex ar/leave ;</c>) -- pushing the computed function address onto the return
  /// stack before executing it, the same "push target, then <c>ex</c>" idiom node 407's own <c>n/main</c>
  /// tail already uses. Reproduced verbatim; not independently re-derived here.</item>
  /// <item><c>'lda</c>'s own body no longer ends with its own <c>;</c> -- it now reads
  /// <c>: 'lda A[ k/@ ]] lit</c>, flowing directly into a brand new shared helper,
  /// <c>: ar/adr ( ) A[ @p @p ]] lit !b @+ !b @ !b !b ;</c>, which <c>'sta</c> now also calls explicitly
  /// (<c>: 'sta A[ k/! ]] lit ar/adr ;</c>) -- the same "one definition's body flows into the next" idiom
  /// this source already uses for <c>ar/leave</c>/<c>ar/main</c> above. Reproduced verbatim.</item>
  /// <item><b>FLAGGED, not silently corrected:</b> this same re-paste's own header comment regressed from
  /// "<c>( contains 6 32-bit address register )</c>" back to "<c>( contains 4 32-bit address register )</c>"
  /// -- while the trailing opcode table (unchanged) still spells out "0..5" for every op, and the
  /// disassembly on this class's own remarks above confirms all six are real and reachable either way.
  /// Reproduced verbatim per this project's own practice rather than silently re-corrected to "6."</item>
  /// </list>
  ///
  /// <b>REVISED AGAIN 2026-09-11 (same day): register count is NOT fixed -- it depends on <c>org</c>.</b>
  /// Stefan: "the number of address register depends on the remaining free memory cells. currently there
  /// is room for 7 register depending on the org offset." Register storage occupies word addresses
  /// <c>[0, org)</c> below node 306's own code (each register a word pair, address+page), so capacity is
  /// <c>floor(org / 2)</c>, capped at 8 by <c>ar/main</c>'s own 3-bit register-select field
  /// (<c>dup 0x07 and</c>). Only the header line and the <c># 0x08 org</c> -&gt; <c># 0x0e org</c> directive
  /// change from the immediately preceding revision above; every opcode line is otherwise byte-for-byte
  /// unchanged, reproduced verbatim below.
  ///
  /// <b>FLAGGED, not silently corrected: the pasted comments still do not match Stefan's own stated count
  /// for this <c>org</c> value.</b> With <c>org = 0x0e</c> (14), Stefan's own formula gives
  /// <c>floor(14 / 2) = 7</c> registers (0-6) -- but the header comment below reads "<c>contains 6 32-bit
  /// address register</c>" and the trailing opcode table still reads "0..5" throughout, and neither is
  /// "7." Reproduced exactly as pasted rather than guessed-and-corrected; Stefan should say which of "6",
  /// "7", or "0..5" is the one to trust for this <c>org</c> value.
  ///
  /// <b>OPEN QUESTION, not yet raised for a decision or implemented:</b> now that capacity is known to vary
  /// with <c>org</c> rather than being a fixed hardware constant, the CVM assembler's own register-operand
  /// range check (<see cref="Ga144.Cvm.Toolchain.CvmInstructionSet.Node306RegisterFieldBitMask"/>, a fixed
  /// <c>0x0007</c> that unconditionally accepts operands 0-7) cannot catch a register operand that is
  /// in-range for the 3-bit field but out-of-range for node 306's CURRENT <c>org</c> -- such an operand
  /// would assemble "successfully" and then alias into node 306's own live code at runtime. Making this
  /// check dynamic (derived from the live-compiled node 306's own <c>org</c>/entry address at assemble
  /// time, instead of the fixed mask) would close that gap, but nothing has been implemented here -- it is
  /// only flagged for Stefan to decide.
  /// </summary>
  public const string Source = """
      ( CVM2 node 306. VM 32 bit addres pointer, 1101_10??_????_???? )
      ( contains 6 32-bit address register )
      ( address word in x, page word in x+1 )
      # 307 import
      # 0x0e org
      entry ar/main
      # 0 /a
      # right /b

      : ar/inc 1 . + ;
      : ar/dec -1 . + ;
      // : ar/reg 0x06 and a! ;

      : ar/r@ ( -w) A[ k/r@ ]] lit !b A[ !p ]] lit !b @b ;
      : ar/r! ( w) A[ @p k/r! ]] lit !b !b ;
      : ar/pop ( -w) A[ k/pop ]] lit !b A[ !p ]] lit !b @b ;
      : ar/push ( w) A[ @p k/push ]] lit !b !b ;

      : ar/leave A[ k/leave ; ]] lit !b
      : ar/main  A[ !p drop ]] lit !b @b
        // set register in a
        dup 0x07 and 2* a!
        // call word
        2/ 2/ 2/ 0x3f and >r ex ar/leave ;

      : 'lda A[ k/@ ]] lit
      : ar/adr ( ) A[ @p @p ]] lit !b @+ !b @ !b !b ;
      : 'sta A[ k/! ]] lit ar/adr ;
      : 'arinc @ ar/inc !+ 0x10000 and # ar/leave until @ ar/inc ! ;
      : 'ardec @ ar/dec !+ # ar/leave -until @ ar/dec ! ;
      : 'arld ar/r@ !+ ar/pop ! ;
      : 'arst @+ ar/r! @ ar/push ;


      (
      opcode lda   1101_10??_????_?aaa load r from address in address register 0..5. register encoded in aaa
      opcode sta   1101_10??_????_?aaa store r to address in address register 0..5. register encoded in aaa
      opcode arinc 1101_10??_????_?aaa increment address register 0..5. register encoded in aaa
      opcode ardec 1101_10??_????_?aaa decrement address register 0..5. register encoded in aaa
      opcode arld  1101_1000_1???_?aa0 load address register 0..5, address in r, page in stack. register encoded in aaa
      opcode arst  1101_1000_0???_?aa0 store address register 0..5, address in r, page in stack. register encoded in aaa
      )
      """;
}