namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 407's resident F18 source -- CVM2's "long call"/"long jump" helper, supplied verbatim by
/// Stefan on 2026-09-02 in response to the page-0-memory-layout change on <see cref="Node507Program"/>
/// (see that class's own remarks): node 507's <c>call</c> opcode only carries a 15-bit embedded address
/// (<see cref="CvmInstructionSet.CallAddressMask"/>, 0x0000-0x7FFF), but page 0 now spans the full
/// 0x0000-0xFFFF, so reaching a function above 0x7FFF needs a wider mechanism. This is a BRAND NEW file
/// -- CVM1 also had a node 407 (register-w/port ops xpt/out/in/ldhi/ldlo/sthi/stlo), deleted on
/// 2026-09-01 along with 606/506/407 per <see cref="Services.CvmAssemblyLanguage"/>'s own remarks on
/// the CVM1 leftover NODE removal; this node 407 is unrelated CVM2 content that happens to reuse the
/// same coordinate, not a revival of the old one.
///
/// <b>Why a separate node rather than adding to node 507 directly.</b> Node 507's own RAM is
/// completely full as of the memory-layout revision (64/64 words -- see that class's own remarks), so
/// there was no room left there for new tick-labeled primitives even if <c>'lcall</c>/<c>'ljmp</c> were
/// otherwise a natural fit alongside <c>'jump</c>/<c>m/call</c>. Living on a separate node with its own
/// fresh 64-word budget sidesteps that entirely (this source uses only 38 of its 64 words).
///
/// <b>Likely resolves node 507's own open "-d--" dispatch question -- STRONGLY SUGGESTED, NOT YET
/// EXPLICITLY CONFIRMED BY STEFAN IN SO MANY WORDS.</b> This source's own header,
/// <c>( CVM2 node 407. VM extending, 11??_????_????_???? )</c>, states the exact same leading bit
/// pattern as the FIRST test in <see cref="Node507Program"/>'s own <c>m/main</c> dispatch cascade --
/// <c>2* -if // 11??_????_????_???? -d-- ;</c> -- which that class's own remarks could previously only
/// guess a destination for ("a future ALU/offload node, possibly node 508 ... but this is a guess, not
/// confirmed"). That guess was almost certainly wrong. Further evidence, from this node's own 2026-09-02
/// fix (see <c>b/main</c>'s own remarks below): its dispatch used to hand off <c>-d--</c> (down) for the
/// "1101" case, which Stefan corrected to <c>---u</c> (up) since down would have chained further away
/// rather than back toward whatever dispatched into node 407 in the first place -- confirming node 407's
/// OWN up port reaches back the way it came, consistent with (though still not an outright statement of)
/// node 407 being what node 507's <c>-d--</c> reaches. Circumstantial but now doubly so; still not a
/// sentence from Stefan saying it outright, so this file treats it as likely rather than settled.
///
/// <b>Imports node 507.</b> <c># 507 import</c> brings node 507's exported symbols (<c>m/pop</c>,
/// <c>m/push</c>, and everything else node 507 exports) into scope here by name -- see
/// <see cref="Compiler.F18Compiler"/>'s own <c>InterpretNodeImport</c>/<c>CompileImportCoordinate</c>
/// for the mechanism: an imported name resolves to the OTHER node's address, so referencing <c>m/pop</c>
/// here compiles a reference to node 507's own compiled address for it, not a local definition.
///
/// <b>Register/stack helpers (<c>b/r@</c>/<c>b/r!</c>/<c>b/pop</c>/<c>b/push</c>/<c>b/leave</c>).</b>
/// Each uses the <c>A[ ... ]] lit !b</c> idiom: <c>A[ ... ]]</c> assembles up to four primitive opcodes
/// (or an embedded call to a named word, per <c>F18Compiler.CompileQuotedInstruction</c>'s own remarks)
/// into ONE raw instruction word and leaves it on the compile-time stack without deciding what happens
/// to it; <c>lit</c> compiles that word as an ordinary object-code literal; <c>!b</c> writes it out over
/// port B (bound to "down" by <c># down /b</c> below). So each of these compiles a short sequence of
/// literal instruction words and streams them out, one per <c>!b</c>, rather than executing anything
/// itself -- consistent with this node's own stated purpose, "support for extending the VM to
/// neighbour nodes," and with <c>tmp1</c> being "a register available to the neighbouring nodes" (per
/// the source's own trailing comment block). <c>b/r@</c>/<c>b/r!</c> read/write that neighbour register
/// (<c>over !p</c>/<c>@p over</c>, then a data word); <c>b/pop</c>/<c>b/push</c> do the same but embed a
/// CALL to node 507's own imported <c>m/pop</c>/<c>m/push</c> as the first streamed word, plus an
/// <c>!p</c>/<c>@p</c> as the second; <c>b/leave</c> streams a single raw <c>;</c> (return) word. The
/// exact wire-level protocol/purpose of what receives and executes these streamed words is not fully
/// worked out here -- flagged as open rather than guessed at further.
///
/// <b><c>b/main</c>'s own dispatch cascade -- fixed 2026-09-02 (Stefan: "you are right: -d-- must be
/// replaced with ---u in node 407").</b> Reads two words via <c>@b</c> (from the down port) into a
/// register-r-held first word, per-word bit-testing further within the already-consumed "11??" prefix:
/// "111?" -&gt; "1111" hands off LEFT (<c>--l-</c>), else "1110" hands off RIGHT (<c>r---</c>); "110?"
/// -&gt; "1101" hands off UP (<c>---u</c>) -- corrected FROM <c>-d--</c> (down again), which would have
/// chained further away rather than back toward whoever dispatched into this node in the first place --
/// this is also the first explicit confirmation that node 407 sits directly below <see cref="Node507Program"/>
/// on the mesh (its <c>up</c> port is what reaches back to 507), strengthening (though still not
/// outright stating) the likely resolution of <see cref="Node507Program"/>'s own open <c>-d--</c>
/// question noted above; else "1100" falls to <c>ex</c> ("execute", GA144's native multi-port-wait/idle
/// opcode) -- this is where <c>'lcall</c>/<c>'ljmp</c> below are actually reached from.
///
/// <b><c>'lcall</c>/<c>'ljmp</c>.</b> Each streams a short instruction sequence the same way the
/// register/stack helpers above do. <c>'lcall</c> streams <c>m/next</c> (fetch the address in the
/// FOLLOWING word, per <c>m/next</c>'s own semantics on <see cref="Node507Program"/>), then <c>&gt;r
/// a</c>, <c>m/push</c>, <c>r&gt; a!</c> -- structurally the SAME body <see cref="Node507Program"/>'s
/// own <c>m/call</c> has (<c>begin drop &gt;r a m/push r&gt; a!</c>, minus the <c>begin drop</c> already
/// consumed by whatever calls in), i.e. a full call: push a return address, then jump. <c>'ljmp</c>
/// streams just <c>m/next</c> then <c>a!</c> -- structurally identical to <see cref="Node507Program"/>'s
/// own plain <c>'jump</c> (<c>m/next a!</c>), no return address pushed. Per the source's own trailing
/// comment: "'lcall long call. the next word defines the destination address. 'ljmp long jump. jumps to
/// the address in the next word" -- confirming both take a full-width address in the CVM word
/// immediately following the opcode, i.e. exactly the
/// <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/> shape <c>pushlit</c> already uses,
/// just with call/jump semantics instead of a stack push.
///
/// <b>How <c>'lcall</c> vs <c>'ljmp</c> is actually selected -- per Stefan: "the sequence 'ex ;' will
/// call 'lcall and 'ljmp because their address is already in R."</b> <c>b/main</c>'s own bit-cascade
/// does NOT distinguish between them at all -- <c>ex</c> is GA144's native instruction, executing
/// whatever address is already sitting in R (the F18 register <see cref="Node507Program"/>'s own header
/// calls <c>S</c>) as a direct jump/call. So the choice of which of the two gets run is made entirely
/// by whoever dispatches into node 407 in the first place, by loading R with <c>'lcall</c>'s address or
/// <c>'ljmp</c>'s before handing off -- not by any further bit pattern node 407 itself inspects.
///
/// <b>Still open, not yet reflected in <see cref="CvmInstructionSet"/>/<see cref="CvmAssembler"/>/
/// <see cref="Services.CvmAssemblyLanguage"/>.</b> Given the above, the real open question is no longer
/// "what bit pattern distinguishes 'lcall from 'ljmp inside node 407" -- there isn't one -- but HOW and
/// WHERE R gets loaded with the right one of the two addresses before the "11??"/down-port handoff
/// happens: presumably something on the CALLING side (<see cref="Node507Program"/>'s own <c>m/main</c>,
/// or a future revision of it) that isn't part of the source supplied so far. That mechanism -- and
/// therefore the exact top-level CVM opcode bit pattern/tag the ASSEMBLER should emit for <c>lcall</c>/
/// <c>ljmp</c> mnemonics -- is needed before either can be added to the toolchain's own instruction
/// table or assembler.
///
/// <b>Verification.</b> Compiled standalone against this project's real <c>Compiler/F18Compiler.cs</c>,
/// importing <see cref="Node507Program"/>'s own exports (<c>F18CompilerOptions.ForRam(407)</c> with an
/// import resolver supplying node 507's compiled <c>F18ExportSet</c>): 0 errors, 38/64 words used,
/// entry point <c>b/main</c> at 0x00C, every symbol (<c>b/r@</c>/<c>b/r!</c>/<c>b/pop</c>/<c>b/push</c>/
/// <c>b/leave</c>/<c>b/main</c>/<c>'lcall</c>/<c>'ljmp</c>) resolves.
/// </summary>
internal static class Node407Program
{
  /// <summary>The node this program is always deployed to -- CVM2's long call/long jump helper.</summary>
  public const int Coordinate = 407;

  /// <summary>
  /// Node 407's full resident F18 source, as supplied by Stefan on 2026-09-02. See the class remarks
  /// for the register/stack helpers, <c>b/main</c>'s dispatch cascade, <c>'lcall</c>/<c>'ljmp</c>'s own
  /// bodies, and what is still open before the toolchain assembler can emit either mnemonic.
  /// </summary>
  public const string Source = """
      ( CVM2 node 407. VM extending, 11??_????_????_???? )
      ( A: temporary register tmp1 )
      # 507 import
      # 0 org
      entry b/main
      # 0 /a
      # down /b
      : b/r@ ( -w) A[ over !p ]] lit !b @b ;
      : b/r! ( w) A[ @p over ]] lit !b !b ;
      : b/pop ( -w) A[ m/pop ]] lit !b A[ !p ]] lit !b @b ;
      : b/push ( w) A[ @p m/push ]] lit !b !b ;
      : b/leave A[ ; ]] lit !b
      : b/main # b/leave lit >r A[ 2* !p !p ]] lit !b @b @b >r
        -if // 111?_????_????_????
          2* -if // 1111_????_????_????
            r> --l- ;
          then // 1110_????_????_????
          r> r--- ;
        then // 110?_????_????_????
        2* -if // 1101_????_????_????
          r> ---u ;
        then // 1100_????_????_????
        ex ;
      : 'lcall A[ m/next ]] lit !b A[ >r a ]] lit !b A[ m/push ]] lit !b A[ r> a! ]] lit !b ;
      : 'ljmp A[ m/next ]] lit !b A[ a! ]] lit !b ;
      (
      this node provides support for extending the VM to neighbour nodes.
      tmp1 is a register available to the neighbouring nodes.
      'lcall long call. the next word defines the destination address.
      'ljmp long jump. jumps to the address in the next word
      )
      """;
}