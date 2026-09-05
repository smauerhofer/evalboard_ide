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
/// fresh 64-word budget sidesteps that entirely.
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
/// <b>Renamed <c>b/</c> to <c>n/</c>, and a new <c>n/next</c> export added -- Stefan's own fix
/// (2026-09-05).</b> Node 406 (the new binary-arithmetic node hanging off this node's own RIGHT port --
/// see <see cref="Node406Program"/>) was originally supplied importing six names --
/// <c>n/r@</c>/<c>n/r!</c>/<c>n/pop</c>/<c>n/push</c>/<c>n/next</c>/<c>n/leave</c> -- from this node,
/// but the THEN-current source here exported only five, under the <c>b/</c> prefix, with no "next"
/// helper at all. Rather than guess a resolution, this was raised with Stefan directly; his own reply
/// ("use this node 407:") supplied this entire replacement source, renaming the whole node's own export
/// prefix from <c>b/</c> to <c>n/</c> and adding a genuinely new <c>n/next</c> word (structurally the
/// SAME <c>A[ ... ]] lit !b A[ !p ]] lit !b @b</c> idiom <c>n/pop</c>/<c>n/r@</c> already use, streaming
/// an embedded call to node 507's own imported <c>m/next</c> rather than <c>m/pop</c>) -- this makes
/// node 406's own ORIGINAL, unmodified source (which already referenced <c>n/</c>-prefixed names)
/// compile cleanly against this node with zero changes on node 406's own side. Verified via a standalone
/// harness compile of both together: 0 errors.
///
/// <b>FLAGGED, not silently reverted or silently accepted (2026-09-05).</b> Comparing this replacement
/// source to the immediately prior revision, the "1101_????_????_????" relay branch of <c>n/main</c>'s
/// own dispatch cascade (below) has changed from <c>r&gt; ---u ;</c> back to <c>r&gt; -d-- ;</c> -- i.e.
/// it UNDOES the 2026-09-02 fix documented lower in these remarks ("you are right: -d-- must be replaced
/// with ---u in node 407"). This is a real, functionally significant change, not a comment-only typo or
/// a rename side effect -- it was surfaced to Stefan directly rather than silently corrected back to
/// <c>---u</c> or silently kept without comment. Whether this reversion is intentional (e.g. the "1101"
/// branch's own destination changed again) or an oversight from editing an older copy of this file is
/// not yet resolved -- flagged as open, per this project's own practice of never guessing at unspecified
/// design decisions.
///
/// <b>Imports node 507.</b> <c># 507 import</c> brings node 507's exported symbols (<c>m/pop</c>,
/// <c>m/push</c>, <c>m/next</c>, and everything else node 507 exports) into scope here by name -- see
/// <see cref="Compiler.F18Compiler"/>'s own <c>InterpretNodeImport</c>/<c>CompileImportCoordinate</c>
/// for the mechanism: an imported name resolves to the OTHER node's address, so referencing <c>m/pop</c>
/// here compiles a reference to node 507's own compiled address for it, not a local definition.
///
/// <b>Register/stack helpers (<c>n/r@</c>/<c>n/r!</c>/<c>n/pop</c>/<c>n/push</c>/<c>n/next</c>/
/// <c>n/leave</c>).</b> Each uses the <c>A[ ... ]] lit !b</c> idiom: <c>A[ ... ]]</c> assembles up to
/// four primitive opcodes (or an embedded call to a named word, per
/// <c>F18Compiler.CompileQuotedInstruction</c>'s own remarks) into ONE raw instruction word and leaves
/// it on the compile-time stack without deciding what happens to it; <c>lit</c> compiles that word as an
/// ordinary object-code literal; <c>!b</c> writes it out over port B (bound to "down" by <c># down /b</c>
/// below). So each of these compiles a short sequence of literal instruction words and streams them
/// out, one per <c>!b</c>, rather than executing anything itself -- consistent with this node's own
/// stated purpose, "support for extending the VM to neighbour nodes." <c>n/r@</c>/<c>n/r!</c> read/write
/// a neighbour register (<c>over !p</c>/<c>@p over</c>, then a data word); <c>n/pop</c>/<c>n/push</c> do
/// the same but embed a CALL to node 507's own imported <c>m/pop</c>/<c>m/push</c> as the first streamed
/// word, plus an <c>!p</c>/<c>@p</c> as the second; <c>n/next</c> (NEW, see above) embeds a call to node
/// 507's own imported <c>m/next</c> the same way <c>n/pop</c> embeds <c>m/pop</c>; <c>n/leave</c>
/// streams a single raw <c>;</c> (return) word. The exact wire-level protocol/purpose of what receives
/// and executes these streamed words is not fully worked out here -- flagged as open rather than
/// guessed at further.
///
/// <b><c>n/main</c>'s own dispatch cascade (renamed from <c>b/main</c>, 2026-09-05) -- see the FLAGGED
/// note above on its own "1101" branch.</b> Reads two words via <c>@b</c> (from port B, the SEPARATE
/// down-bound link to 507 -- see this class's own remarks above on not confusing this cascade's own
/// branches with that link) into a register-r-held first word, per-word bit-testing further within the
/// already-consumed "11??" prefix: "111?" -&gt; "1111" hands off LEFT (<c>--l-</c>), else "1110" hands
/// off RIGHT (<c>r---</c>) -- THIS is the link to node 406 (see <see cref="Node406Program"/>'s own
/// remarks), filling what was previously an unsupplied further-relay branch; "110?" -&gt; "1101" hands
/// off DOWN (<c>-d--</c>, see the FLAGGED note above -- this was <c>---u</c>, UP, in the immediately
/// prior revision); else "1100" falls to <c>ex</c> ("execute", GA144's native multi-port-wait/idle
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
/// call 'lcall and 'ljmp because their address is already in R."</b> <c>n/main</c>'s own bit-cascade
/// does NOT distinguish between them at all -- <c>ex</c> is GA144's native instruction, executing
/// whatever address is already sitting in R (the F18 register <see cref="Node507Program"/>'s own header
/// calls <c>S</c>) as a direct jump/call. So the choice of which of the two gets run is made entirely
/// by whoever dispatches into node 407 in the first place, by loading R with <c>'lcall</c>'s address or
/// <c>'ljmp</c>'s before handing off -- not by any further bit pattern node 407 itself inspects.
///
/// <b>The CVM opcode tag (resolved 2026-09-02, unaffected by the 2026-09-05 rename).</b> Node 507's own
/// <c>m/main</c> hands off down-port to node 407 once a fetched opcode word's top bits read "11??" --
/// carrying, per the relay protocol traced in <see cref="Node507Program"/>'s own remarks (the
/// <c>x</c>/<c>y</c> stack convention: <c>x</c> is the ORIGINAL fetched word, unshifted, relayed via
/// <c>2* !p !p</c> to node 407 alongside the progressively-shifted <c>y</c>), the ORIGINAL opcode word
/// itself all the way to <c>ex</c> -- which jumps directly to whatever address is already in R. Since
/// <c>ex</c> is only reached once the cascade's own bit-tests have consumed exactly "1100" (this
/// method's own remarks above), and <c>ex</c> jumps straight to <c>x</c>, <c>x</c>'s own low bits must
/// equal <c>'lcall</c>'s or <c>'ljmp</c>'s real address ON NODE 407 -- so the CVM-level opcode word is
/// <c>0xC000 | (address on node 407)</c>, the SAME "tag | local address" scheme
/// <see cref="Node507Program"/>'s own local-execute already uses with 0x8800. Wired up in
/// <see cref="CvmInstructionSet.LongCallMnemonic"/>/<see cref="CvmInstructionSet.LongJumpMnemonic"/>
/// (shape: <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/>, exactly like <c>pushlit</c>)
/// and <see cref="Services.CvmAssemblyLanguage"/>'s own <c>Node407LongCallTagBits</c> (0xC000), resolved
/// against THIS node's live compile the same way <c>pushlit</c> resolves against node 507's -- the tag
/// itself does not depend on WHERE <c>'lcall</c>/<c>'ljmp</c> land in node 407's own RAM, only on which
/// node answers them, so it is unaffected by the address shift below.
///
/// <b>Verification (2026-09-05, this revision).</b> Compiled standalone against this project's real
/// <c>Compiler/F18Compiler.cs</c>, importing <see cref="Node507Program"/>'s own exports: 0 errors, 42/64
/// words used (up from 38, per the new <c>n/next</c> word), entry point <c>n/main</c> at 0x0010 (up from
/// 0x000C), every symbol resolves: <c>n/r@</c>=0x0000, <c>n/r!</c>=0x0002, <c>n/pop</c>=0x0004,
/// <c>n/push</c>=0x0008, <c>n/next</c>=0x000A (NEW), <c>n/leave</c>=0x000E, <c>n/main</c>=0x0010,
/// <c>'lcall</c>=0x001F, <c>'ljmp</c>=0x0026. Also confirmed: node 406's own ORIGINAL, unmodified source
/// (still referencing <c>n/r@</c> etc.) compiles cleanly against THIS node with 0 errors and no changes
/// needed on its own side.
///
/// <b><c>'lcall</c>'s own address shifted -- a direct, mechanical consequence of inserting
/// <c>n/next</c> earlier in this source, not a bug.</b> Before this revision, <c>'lcall</c> lived at a
/// lower address, giving the hardware-confirmed opcode 0xC01B (see the transaction log below). With
/// <c>n/next</c> now occupying two words ahead of everything that follows it, <c>'lcall</c> moved to
/// 0x001F, giving opcode 0xC01F instead. This is simply where the CVM opcode tag scheme (tag | whatever
/// address the symbol happens to compile to) always resolves to a live compile rather than a fixed
/// number -- see <see cref="Services.CvmAssemblyLanguage"/>'s own remarks on why every tagged mnemonic
/// works this way.
///
/// <b>CONFIRMED ON REAL HARDWARE (2026-09-02, address now STALE -- see the paragraph just above).</b> A
/// test program (<c>lcall label ... halt ... label: nop ret nop</c>) installed and run against a real
/// EVB, transaction log: <c>[READ] 0:0000 -&gt; C01B</c> / <c>[READ] 0:0001 -&gt; 0007</c> (the
/// <c>lcall</c> opcode and its trailing operand word, resolving to 0xC01B exactly as derived above, AT
/// THE TIME); <c>[WRITE] 1:FFFE &lt;- 0002</c> (the return address -- this instruction's own address + 2,
/// its own word length -- pushed onto the data stack by <c>'lcall</c>'s own <c>m/push</c>); <c>[READ]
/// 0:0007 -&gt; 8840</c> / <c>[READ] 0:0008 -&gt; 8831</c> (landing on <c>label</c>, executing <c>nop</c>
/// then <c>'ret</c>); <c>[READ] 1:FFFE -&gt; 0002</c> (<c>'ret</c> popping that same return address
/// back); <c>[READ] 0:0002 -&gt; 8840</c> / <c>[READ] 0:0003 -&gt; 8840</c> (execution resuming exactly
/// where <c>lcall</c> left off). The full MECHANISM -- opcode tag, trailing operand, the 507-&gt;407
/// relay, and <c>'lcall</c>'s own call/return semantics -- is proven to work end to end on real silicon
/// by this log; the EXACT addresses/opcode values it shows (0xC01B specifically) are now stale for a
/// fresh compile of this revised source, which resolves <c>'lcall</c> to 0xC01F instead (see above) --
/// a fresh hardware run has not yet been done against this revision.
/// </summary>
internal static class Node407Program
{
  /// <summary>The node this program is always deployed to -- CVM2's long call/long jump helper.</summary>
  public const int Coordinate = 407;

  /// <summary>
  /// Node 407's full resident F18 source, as supplied by Stefan on 2026-09-05 ("use this node 407:"),
  /// replacing the 2026-09-02 revision. See the class remarks for the <c>b/</c>-&gt;<c>n/</c> rename,
  /// the new <c>n/next</c> export, the FLAGGED "1101" branch reversion, <c>n/main</c>'s dispatch
  /// cascade, <c>'lcall</c>/<c>'ljmp</c>'s own bodies, and the updated verification numbers.
  /// </summary>
  public const string Source = """
      ( CVM2 node 407. VM extending, 11??_????_????_???? )
      ( A: temporary register tmp1 )
      # 507 import
      # 0 org
      entry n/main
      # 0 /a
      # down /b
      : n/r@ ( -w) A[ over !p ]] lit !b @b ;
      : n/r! ( w) A[ @p over ]] lit !b !b ;
      : n/pop ( -w) A[ m/pop ]] lit !b A[ !p ]] lit !b @b ;
      : n/push ( w) A[ @p m/push ]] lit !b !b ;
      : n/next ( -w) A[ m/next ]] lit !b A[ !p ]] lit !b @b ;
      : n/leave A[ ; ]] lit !b
      : n/main # n/leave lit >r A[ 2* !p !p ]] lit !b @b @b >r
        -if // 111?_????_????_????
          2* -if // 1111_????_????_????
            r> --l- ;
          then // 1110_????_????_????
          r> r--- ;
        then // 110?_????_????_????
        2* -if // 1101_????_????_????
          r> -d-- ;
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