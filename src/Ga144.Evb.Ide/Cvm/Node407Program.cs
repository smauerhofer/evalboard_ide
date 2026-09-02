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
/// <b>Resolves node 507's own "-d--" dispatch question -- CONFIRMED by Stefan (2026-09-02): "the port
/// between 407 and 507 is still 'down'."</b> This source's own header,
/// <c>( CVM2 node 407. VM extending, 11??_????_????_???? )</c>, states the exact same leading bit
/// pattern as the FIRST test in <see cref="Node507Program"/>'s own <c>m/main</c> dispatch cascade --
/// <c>2* -if // 11??_????_????_???? -d-- ;</c> -- which that class's own remarks could previously only
/// guess a destination for. Node 407 is that destination, and the physical link between the two nodes
/// is node 507's local "down" port / node 407's local "down" port (bound to port B here, <c># down /b</c>
/// below) -- see <see cref="Models.KrakenConfiguration.PortAddress"/>'s own geographic-adjacency table,
/// which independently computes the SAME local port name ("down") on BOTH sides of this link (407 at an
/// even row, 507 at an odd row -- exactly the alternating-mirror pattern that already gives node
/// 507&lt;-&gt;607 the same local port name, "up", on both of ITS sides too, confirmed working on real
/// hardware). This makes 407&lt;-&gt;507 the SAME kind of symmetric-local-name link, not an exception.
///
/// <b>Do not confuse this with <c>b/main</c>'s own <c>-d--</c>-&gt;<c>---u</c> fix below.</b> An earlier
/// revision of this file's own remarks mistakenly conflated the two: <c>b/main</c>'s dispatch cascade
/// (below) hands off to <c>--l-</c>/<c>r---</c>/<c>---u</c> for FURTHER relay to OTHER neighbour nodes
/// (plausibly 406/408/307, per Stefan's own "a similar pattern repeats then in nodes 406, 408 and 307") --
/// none of those three branches is the link back to 507 at all. The link back to 507 is the separate,
/// dedicated port B (<c># down /b</c>), used throughout this file's own register/stack helpers AND by
/// <c>b/main</c>'s own opening <c>@b @b</c> receive -- unrelated to which further-relay branch a given
/// opcode's remaining bits select. Per Stefan directly (2026-09-02): "you misunderstood my -d-- remark.
/// only valid inside the RAM source. the port between 407 and 507 is still 'down'."
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
/// replaced with ---u in node 407").</b> Reads two words via <c>@b</c> (from port B, the SEPARATE
/// down-bound link to 507 -- see this class's own remarks above on not confusing this cascade's own
/// branches with that link) into a register-r-held first word, per-word bit-testing further within the
/// already-consumed "11??" prefix: "111?" -&gt; "1111" hands off LEFT (<c>--l-</c>), else "1110" hands
/// off RIGHT (<c>r---</c>); "110?" -&gt; "1101" hands off UP (<c>---u</c>) -- corrected FROM <c>-d--</c>
/// (down), per Stefan's own fix -- these three are further relay hand-offs to OTHER neighbour nodes
/// entirely (plausibly 406/408/307), not the link back to 507; else "1100" falls to <c>ex</c> ("execute",
/// GA144's native multi-port-wait/idle opcode) -- this is where <c>'lcall</c>/<c>'ljmp</c> below are
/// actually reached from.
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
/// <b>Resolved and implemented (2026-09-02): the CVM opcode tag.</b> Node 507's own <c>m/main</c> hands
/// off down-port to node 407 once a fetched opcode word's top bits read "11??" -- carrying, per the
/// relay protocol traced in <see cref="Node507Program"/>'s own remarks (the <c>x</c>/<c>y</c> stack
/// convention: <c>x</c> is the ORIGINAL fetched word, unshifted, relayed via <c>2* !p !p</c> to node 407
/// alongside the progressively-shifted <c>y</c>), the ORIGINAL opcode word itself all the way to
/// <c>ex</c> -- which jumps directly to whatever address is already in R. Since <c>ex</c> is only
/// reached once the cascade's own bit-tests have consumed exactly "1100" (this method's own remarks
/// above), and <c>ex</c> jumps straight to <c>x</c>, <c>x</c>'s own low bits must equal <c>'lcall</c>'s
/// or <c>'ljmp</c>'s real address ON NODE 407 -- so the CVM-level opcode word is
/// <c>0xC000 | (address on node 407)</c>, the SAME "tag | local address" scheme
/// <see cref="Node507Program"/>'s own local-execute already uses with 0x8800. Wired up in
/// <see cref="CvmInstructionSet.LongCallMnemonic"/>/<see cref="CvmInstructionSet.LongJumpMnemonic"/>
/// (shape: <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/>, exactly like <c>pushlit</c>)
/// and <see cref="Services.CvmAssemblyLanguage"/>'s own <c>Node407LongCallTagBits</c> (0xC000), resolved
/// against THIS node's live compile the same way <c>pushlit</c> resolves against node 507's.
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