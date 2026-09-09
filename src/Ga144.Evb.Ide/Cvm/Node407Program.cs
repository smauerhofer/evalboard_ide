namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 407's resident F18 source -- CVM2's "long call"/"long jump"/"long branch" helper, supplied
/// verbatim by Stefan on 2026-09-02 in response to the page-0-memory-layout change on
/// <see cref="Node507Program"/> (see that class's own remarks): node 507's <c>call</c> opcode only
/// carries a 15-bit embedded address (<see cref="CvmInstructionSet.CallAddressMask"/>, 0x0000-0x7FFF),
/// but page 0 now spans the full 0x0000-0xFFFF, so reaching a function above 0x7FFF needs a wider
/// mechanism. This is a BRAND NEW file -- CVM1 also had a node 407 (register-w/port ops
/// xpt/out/in/ldhi/ldlo/sthi/stlo), deleted on 2026-09-01 along with 606/506/407 per
/// <see cref="Services.CvmAssemblyLanguage"/>'s own remarks on the CVM1 leftover NODE removal; this node
/// 407 is unrelated CVM2 content that happens to reuse the same coordinate, not a revival of the old one.
///
/// <b>Why a separate node rather than adding to node 507 directly.</b> Node 507's own RAM is
/// completely full as of the memory-layout revision (64/64 words -- see that class's own remarks), so
/// there was no room left there for new tick-labeled primitives even if <c>'lcall</c>/<c>'ljmp</c> were
/// otherwise a natural fit alongside <c>'jump</c>/<c>m/call</c>. Living on a separate node with its own
/// fresh 64-word budget sidesteps that entirely.
///
/// <b>Resolves node 507's own "-d--" dispatch question -- CONFIRMED by Stefan (2026-09-02): "the port
/// between 407 and 507 is still 'down'."</b> This source's own header (renamed 2026-09-06, see below --
/// the leading bit pattern itself is unchanged) states the exact same leading bit pattern as the FIRST
/// test in <see cref="Node507Program"/>'s own <c>m/main</c> dispatch cascade --
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
/// design decisions. STILL UNRESOLVED as of both 2026-09-06 revisions below, which left this "1101"
/// branch completely untouched.
///
/// <b>Renamed header and three new tick-prefixed words added, then two of them removed again the SAME
/// day -- Stefan's own two follow-up messages (2026-09-06).</b> First message ("more opcodes to node 407
/// added. use them in assembler and disassembler") renamed the node's own header comment from "VM
/// extending" to "VM secondary main" (the leading bit pattern, <c>11??_????_????_????</c>, is unchanged)
/// and hoisted the shared "fetch the trailing operand word" step (<c>A[ m/next ]] lit !b</c>) out of
/// <c>'lcall</c>/<c>'ljmp</c>'s own bodies and into <c>n/main</c>'s own dispatch tail itself (was bare
/// <c>ex ;</c>, now <c>A[ m/next ]] lit !b ex ;</c>) -- this streams a call to node 507's own
/// <c>m/next</c> ("read the current word and increment p") BEFORE jumping via <c>ex</c>, a step
/// <c>'lcall</c>/<c>'ljmp</c> USED TO perform themselves as their own first streamed word, hoisted up so
/// it happens once for every op reached this way rather than duplicated per op. That first message added
/// THREE new ops needing this same trailing-word fetch: <c>'clbr</c> (conditional long branch), <c>'lbr</c>
/// (long branch), and <c>'cljmp</c> (conditional long jump). His SECOND message the same day ("I remove
/// clbr and cljmp from node 407. remove it also from the CVM assembler and disassembler") then supplied a
/// further replacement source removing <c>'clbr</c> and <c>'cljmp</c> again, before either was ever
/// confirmed on real hardware -- only <c>'lbr</c> (long branch, unconditional) survives from that trio.
/// THIS class's own <see cref="Source"/> reflects that second, current message; <c>'clbr</c>/<c>'cljmp</c>
/// briefly existed (each streaming a shared conditional-test idiom, <c>A[ !p over !p ]] lit !b @b @b if
/// // no branch ; then // branch drop A[ ... ]] lit !b !b ;</c>, branching only when the tested value was
/// zero, then <c>m/branch</c> for <c>'clbr</c> or a plain <c>a!</c> jump for <c>'cljmp</c>) but are gone
/// from both this source and <see cref="CvmInstructionSet"/>/<see cref="Services.CvmAssemblyLanguage"/>
/// (see those classes' own remarks) -- deleted outright rather than kept-and-orphaned, since neither ever
/// reached a shipped <c>.gaobj</c> to stay backward-compatible with.
///
/// <b>Reordered exports, and two brand new ones -- present in Stefan's second 2026-09-06 message, but NOT
/// called out in his own words and NOT yet confirmed with him. FLAGGED, not silently normalized.</b>
/// Comparing that message's source line-by-line against the immediately prior revision, three further
/// differences exist beyond the clbr/cljmp removal Stefan actually described:
/// <list type="bullet">
/// <item><c>n/pop</c> now appears FIRST in the source (ahead of <c>n/r@</c>/<c>n/r!</c>), and its own body
/// gained an inserted <c>begin</c> token: <c>A[ m/pop ]] lit !b begin A[ !p ]] lit !b @b ;</c> (previously
/// no <c>begin</c> at all). Mere reordering of top-level definitions does not change compiled behavior on
/// its own (each is still a separate colon-defined word), but an inserted <c>begin</c> with no matching
/// <c>until</c>/<c>again</c>/<c>repeat</c> before the closing <c>;</c> does not match any GA144 colonForth
/// looping idiom used ANYWHERE else in this project's own nodes -- it is reproduced here completely
/// verbatim rather than silently dropped as a presumed typo.</item>
/// <item>Two brand new exports appear, <c>n/@ ( ba-w) A[ @p @p ]] lit !b !b !b A[ m/@ ]] lit !b end</c>
/// and <c>n/! (baw) A[ @p @p @p ]] lit !b !b !b !b A[ m/! ]] lit !b ;</c> -- global-style indirect
/// fetch/store by address, shaped similarly to <see cref="Node508Program"/>'s own <c>g/@</c>/<c>g/!</c>
/// (relay to node 507's own <c>m/@</c>/<c>m/!</c>, if those exist on node 507 -- not independently
/// confirmed here). <c>n/@</c>'s own definition ends with the bare word <c>end</c> rather than the
/// semicolon every other definition in this source (and every other node in this project) uses to close a
/// colon definition -- this is likewise reproduced verbatim, not corrected to <c>;</c>, since neither
/// <c>begin</c> nor a definition-closing <c>end</c> has an established meaning elsewhere in this project's
/// own confirmed CVM2 sources.</item>
/// <item>Neither <c>n/@</c> nor <c>n/!</c> is a tick-prefixed (<c>'</c>-leading) name, so per Stefan's own
/// tick-naming rule ("every word that begins with a ' is an opcode for the CVM with the mnemonic using the
/// same name without the leading '") neither gets a <see cref="CvmInstructionSet"/> mnemonic or a
/// <see cref="Services.CvmAssemblyLanguage"/> entry -- exactly the same choice already made for this
/// node's own untagged internal helpers (<c>n/r@</c>/<c>n/r!</c>/<c>n/pop</c>/<c>n/push</c>/<c>n/next</c>/
/// <c>n/leave</c>) and for <see cref="Node508Program"/>'s own four untagged embedded-offset forms.</item>
/// </list>
/// Per this project's own practice of never guessing at unspecified design decisions: the <see
/// cref="Source"/> below is synced to Stefan's message verbatim, including the <c>begin</c>/<c>end</c>
/// tokens and the reordering, and this paragraph is the flag -- only Stefan can say whether <c>begin</c>/
/// <c>end</c> are intentional (a real GA144 forth construct not yet otherwise used in this project) or a
/// transcription slip, and whether <c>n/@</c>/<c>n/!</c> are meant to be wired up further (e.g. into node
/// 406/408/509's own imports) or are simply not yet used by anything.
///
/// <b><c>n/main</c>'s own dispatch cascade (renamed from <c>b/main</c>, 2026-09-05) -- see the FLAGGED
/// note above on its own "1101" branch.</b> Reads two words via <c>@b</c> (from port B, the SEPARATE
/// down-bound link to 507 -- see this class's own remarks above on not confusing this cascade's own
/// branches with that link) into a register-r-held first word, per-word bit-testing further within the
/// already-consumed "11??" prefix: "111?" -&gt; "1111" hands off LEFT (<c>--l-</c>) -- THIS is the link
/// to node 408 (see <see cref="Node408Program"/>'s own remarks, added 2026-09-06), filling what was
/// PREVIOUSLY an unsupplied further-relay branch (no code change needed here -- the relay was already
/// wired, just unanswered until node 408 existed) -- else "1110" hands off RIGHT (<c>r---</c>) -- THIS is
/// the link to node 406 (see <see cref="Node406Program"/>'s own remarks); "110?" -&gt; "1101" hands off
/// DOWN (<c>-d--</c>, see the FLAGGED note above -- this was <c>---u</c>, UP, in an earlier revision);
/// else "1100" falls to <c>A[ m/next ]] lit !b ex ;</c> (fetch the trailing word, THEN "execute", GA144's
/// native multi-port-wait/idle opcode -- see the remarks above for why the fetch moved here) -- this is
/// where <c>'lcall</c>/<c>'ljmp</c>/<c>'lbr</c> below are actually reached from.
///
/// <b><c>'lcall</c>/<c>'ljmp</c> (bodies simplified 2026-09-06).</b> Each streams a short instruction
/// sequence the same way the register/stack helpers above do. <c>'lcall</c> streams just
/// <c>&gt;r a</c>, <c>m/push</c>, <c>r&gt; a!</c> (its own former leading <c>A[ m/next ]] lit !b</c> moved
/// into <c>n/main</c>'s own shared tail, above) -- structurally the SAME body <see cref="Node507Program"/>'s
/// own <c>m/call</c> has (<c>begin drop &gt;r a m/push r&gt; a!</c>, minus the <c>begin drop</c> already
/// consumed by whatever calls in), i.e. a full call: push a return address, then jump. <c>'ljmp</c> streams
/// just <c>a!</c> (likewise minus its own former leading <c>m/next</c>) -- structurally identical
/// to <see cref="Node507Program"/>'s own plain <c>'jump</c> (<c>m/next a!</c>, where the <c>m/next</c>
/// half now lives in <c>n/main</c> instead), no return address pushed. Per the source's own trailing
/// comment: "'lcall long call. the next word defines the destination address. 'ljmp long jump. jumps to
/// the address in the next word" -- confirming both still take a full-width address in the CVM word
/// immediately following the opcode, i.e. exactly the
/// <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/> shape <c>pushlit</c> already uses,
/// just with call/jump semantics instead of a stack push. This body simplification is a pure internal
/// refactor -- see the CVM opcode tag remarks below for why it changes neither mnemonic's own tag.
///
/// <b><c>'lbr</c> (added 2026-09-06, survives the same-day <c>'clbr</c>/<c>'cljmp</c> removal).</b>
/// Streams just <c>m/branch</c> (<c>: m/branch ( rso-rs) a . + a! ;</c> -- adds the just-fetched trailing
/// offset to node 507's own program-counter register <c>a</c> and stores it back, i.e. a genuine
/// unconditional relative branch), a PREVIOUSLY-UNUSED node 507 export that neither <c>'lcall</c> nor
/// <c>'ljmp</c> ever needed. Per the source's own trailing comment: "'lbr long branch. branch with offset
/// in the next word".
///
/// <b>How <c>'lcall</c>/<c>'ljmp</c>/<c>'lbr</c> is actually selected -- per Stefan: "the sequence
/// 'ex ;' will call 'lcall and 'ljmp because their address is already in R."</b> <c>n/main</c>'s own
/// bit-cascade does NOT distinguish between any of the three at all -- <c>ex</c> is GA144's native
/// instruction, executing whatever address is already sitting in R (the F18 register
/// <see cref="Node507Program"/>'s own header calls <c>S</c>) as a direct jump/call. So the choice of which
/// of the three gets run is made entirely by whoever dispatches into node 407 in the first place, by
/// loading R with the target op's own address before handing off -- not by any further bit pattern node
/// 407 itself inspects.
///
/// <b>The CVM opcode tag (resolved 2026-09-02, unaffected by the 2026-09-05 rename or either 2026-09-06
/// revision).</b> Node 507's own <c>m/main</c> hands off down-port to node 407 once a fetched opcode
/// word's top bits read "11??" -- carrying, per the relay protocol traced in
/// <see cref="Node507Program"/>'s own remarks (the <c>x</c>/<c>y</c> stack convention: <c>x</c> is the
/// ORIGINAL fetched word, unshifted, relayed via <c>2* !p !p</c> to node 407 alongside the
/// progressively-shifted <c>y</c>), the ORIGINAL opcode word itself all the way to the "1100" tail, which
/// fetches the trailing word first and THEN jumps directly to whatever address is already in R. Since
/// this tail is only reached once the cascade's own bit-tests have consumed exactly "1100" (this method's
/// own remarks above), and it jumps straight to <c>x</c>, <c>x</c>'s own low bits must equal whichever of
/// <c>'lcall</c>/<c>'ljmp</c>/<c>'lbr</c>'s real address ON NODE 407 the caller intends -- so the CVM-level
/// opcode word is <c>0xC000 | (address on node 407)</c> for all three, the SAME "tag | local address"
/// scheme <see cref="Node507Program"/>'s own local-execute already uses with 0x8800. Wired up in
/// <see cref="CvmInstructionSet.LongCallMnemonic"/>/<see cref="CvmInstructionSet.LongJumpMnemonic"/>/
/// <see cref="CvmInstructionSet.LongBranchMnemonic"/> (shape:
/// <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/>, exactly like <c>pushlit</c>) and
/// <see cref="Services.CvmAssemblyLanguage"/>'s own <c>Node407LongCallTagBits</c> (0xC000), resolved
/// against THIS node's live compile the same way <c>pushlit</c> resolves against node 507's -- the tag
/// itself does not depend on WHERE any of the three land in node 407's own RAM, only on which node answers
/// them.
///
/// <b>Verification numbers are STALE as of both 2026-09-06 revisions -- not re-derived here.</b> The
/// previously recorded standalone-compile numbers (55/64 words, <c>'lcall</c>=0x0020, <c>'ljmp</c>=0x0025,
/// plus the now-removed <c>'clbr</c>/<c>'lbr</c>/<c>'cljmp</c> addresses) described the FIRST 2026-09-06
/// revision, before <c>'clbr</c>/<c>'cljmp</c> were removed and before the reordering/<c>n/@</c>/<c>n/!</c>
/// changes flagged above. This class's own <see cref="Source"/> has NOT been re-compiled against a live
/// harness since (no compiler is available in this environment to do so, and the flagged <c>begin</c>/
/// <c>end</c> tokens above may not even compile cleanly) -- every export's own address in a fresh build,
/// including whether <c>n/main</c> still lands at 0x0010, is unconfirmed until Stefan or a real build
/// reports it. This is the same "resolves against a live compile, never a hardcoded number" property
/// <see cref="Services.CvmAssemblyLanguage"/> already relies on for every tagged mnemonic, so no numeric
/// address anywhere in <see cref="CvmInstructionSet"/>/<see cref="Services.CvmAssemblyLanguage"/> needed
/// updating for this -- only mnemonics that no longer exist (<c>'clbr</c>/<c>'cljmp</c>) needed removing.
///
/// <b>CONFIRMED ON REAL HARDWARE (2026-09-02, address now doubly stale -- see the paragraph just above).</b>
/// A test program (<c>lcall label ... halt ... label: nop ret nop</c>) installed and run against a real
/// EVB, transaction log: <c>[READ] 0:0000 -&gt; C01B</c> / <c>[READ] 0:0001 -&gt; 0007</c> (the
/// <c>lcall</c> opcode and its trailing operand word, resolving to 0xC01B exactly as derived at the time);
/// <c>[WRITE] 1:FFFE &lt;- 0002</c> (the return address -- this instruction's own address + 2, its own
/// word length -- pushed onto the data stack by <c>'lcall</c>'s own <c>m/push</c>); <c>[READ]
/// 0:0007 -&gt; 8840</c> / <c>[READ] 0:0008 -&gt; 8831</c> (landing on <c>label</c>, executing <c>nop</c>
/// then <c>'ret</c>); <c>[READ] 1:FFFE -&gt; 0002</c> (<c>'ret</c> popping that same return address
/// back); <c>[READ] 0:0002 -&gt; 8840</c> / <c>[READ] 0:0003 -&gt; 8840</c> (execution resuming exactly
/// where <c>lcall</c> left off). The full MECHANISM -- opcode tag, trailing operand, the 507-&gt;407
/// relay, and <c>'lcall</c>'s own call/return semantics -- is proven to work end to end on real silicon
/// by this log; the EXACT addresses/opcode values it shows are stale for every later revision of this
/// source, and no fresh hardware run has been done against any revision since, so <c>'ljmp</c>/<c>'lbr</c>
/// remain unconfirmed on hardware (and <c>'clbr</c>/<c>'cljmp</c> were removed before ever being tested at
/// all).
/// </summary>
internal static class Node407Program
{
  /// <summary>The node this program is always deployed to -- CVM2's long call/long jump/long branch helper.</summary>
  public const int Coordinate = 407;

  /// <summary>
  /// Node 407's full resident F18 source, as supplied by Stefan on 2026-09-06 ("I remove clbr and cljmp
  /// from node 407. remove it also from the CVM assembler and disassembler"), replacing the earlier
  /// same-day revision that had briefly added <c>'clbr</c>/<c>'lbr</c>/<c>'cljmp</c> together. See the
  /// class remarks for the removal itself, and for the reordered/new <c>n/pop</c>/<c>n/@</c>/<c>n/!</c>
  /// content this message also included but did not call out (FLAGGED there, not silently normalized).
  /// <b>SYNCED, 2026-09-08.</b> The source below was re-synced verbatim to Stefan's current live
  /// project source for node 407 (from his own uploaded <c>workspace.yaml</c>, used to bisect the
  /// <see cref="CvmBootStreamBuilder"/> node 306/307 load-order bug). This supersedes whatever the
  /// remarks above describe -- those record an EARLIER revision's shape (word counts, addresses,
  /// port bindings, exact wording) and have NOT been re-verified against this content. Treat any
  /// specific claim above (a compiled address, a port name, a verification result) as possibly
  /// stale until re-confirmed against a fresh compile.
  ///
  /// </summary>
  public const string Source = """
      ( CVM2 node 407. VM secondary main, 11??_????_????_???? )
      ( A: temporary register tmp1 )
      # 507 import
      # 0 org
      entry n/main
      # 0 /a
      # down /b
      : n/pop ( -w) A[ m/pop ]] lit !b begin A[ !p ]] lit !b @b ;
      : n/@ ( ba-w) A[ @p @p ]] lit !b !b !b A[ m/@ ]] lit !b end
      : n/! ( baw) A[ @p @p @p ]] lit !b !b !b !b A[ m/! ]] lit !b ;
      : n/r@ ( -w) A[ over !p ]] lit !b @b ;
      : n/r! ( w) A[ @p over ]] lit !b !b ;
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
        A[ m/next ]] lit !b ex ;
      : 'lcall A[ >r a ]] lit !b A[ m/push ]] lit !b A[ r> a! ]] lit !b ;
      : 'ljmp A[ a! ]] lit !b ;
      : 'lbr A[ m/branch ]] lit !b ;



      (
      this node provides support for extending the VM to neighbour nodes.
      tmp1 is a register available to the neighbouring nodes.
      'lcall long call. the next word defines the destination address.
      'ljmp long jump. jumps to the address in the next word
      'lbr long branch. branch with offset in the next word
      )
      """;
}