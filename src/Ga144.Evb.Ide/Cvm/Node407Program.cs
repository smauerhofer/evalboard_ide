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
/// design decisions. STILL UNRESOLVED as of the 2026-09-06 revision below, which left this "1101" branch
/// completely untouched.
///
/// <b>Renamed header, three new tick-prefixed words, and the "fetch trailing word" step hoisted into
/// <c>n/main</c> itself -- Stefan's own follow-up (2026-09-06, "more opcodes to node 407 added. use them
/// in assembler and disassembler").</b> The node's own header comment changed from "VM extending" to
/// "VM secondary main" (the leading bit pattern, <c>11??_????_????_????</c>, and every export name are
/// unchanged -- confirmed via a standalone harness compile that node 406's and node 408's own sources,
/// both importing this node by name, still compile cleanly against it with zero changes needed on their
/// own sides). <c>n/main</c>'s own dispatch cascade is structurally IDENTICAL -- same four branches,
/// same bit tests, same "1100" prefix falling through to the local-execute tail -- except that tail
/// itself grew a leading <c>A[ m/next ]] lit !b</c> (was bare <c>ex ;</c>, now
/// <c>A[ m/next ]] lit !b ex ;</c>): this streams a call to node 507's own <c>m/next</c> (per that node's
/// own remarks, "read the current word and increment p" -- i.e. fetch the CVM program's own next word
/// and advance its program counter) BEFORE jumping via <c>ex</c>. This is exactly the step <c>'lcall</c>
/// and <c>'ljmp</c> USED TO perform themselves, as their own first streamed word (see the 2026-09-05
/// verification numbers below, where each began with its own <c>A[ m/next ]] lit !b</c>) -- hoisted up
/// into the shared tail so it happens once for every op reached this way, not duplicated per op. This
/// matters because THREE new ops were added alongside it, all of which also need that same trailing-word
/// fetch: <c>'clbr</c> (conditional long branch), <c>'lbr</c> (long branch), and <c>'cljmp</c>
/// (conditional long jump) -- per this source's own trailing comment, respectively "branch with offset in
/// the next word if r == 0", "branch with offset in the next word" (unconditional), and "jumps to the
/// address in the next word if r == 0". Since ALL FIVE named ops (<c>'lcall</c>/<c>'ljmp</c>/<c>'clbr</c>/
/// <c>'lbr</c>/<c>'cljmp</c>) are reached the exact same way -- <c>n/main</c> falling to its "1100"
/// tail, which now fetches the trailing word FIRST, then jumps via <c>ex</c> to whichever address was
/// already in R -- the CVM-level opcode TAG itself is completely unaffected (still <c>0xC000</c>,
/// resolved the same way as before): only the ADDRESSES the five ops land at, and which of node 507's own
/// exports each pulls in, changed. <c>'lbr</c>/<c>'clbr</c> both stream a call to node 507's own
/// <c>m/branch</c> (<c>: m/branch ( rso-rs) a . + a! ;</c> -- adds the just-fetched offset to node 507's
/// own program-counter register <c>a</c> and stores it back, i.e. a genuine relative branch), a
/// PREVIOUSLY-UNUSED node 507 export that neither <c>'lcall</c> nor <c>'ljmp</c> ever needed;
/// <c>'cljmp</c> instead streams <c>@p a!</c> (an absolute jump, like <c>'ljmp</c>'s own tail) but only
/// after the SAME conditional test <c>'clbr</c> uses.
///
/// <b><c>'clbr</c>/<c>'cljmp</c>'s own shared conditional-test shape.</b> Both open with the IDENTICAL
/// streamed sequence, <c>A[ !p over !p ]] lit !b @b @b</c> (relays two words out, reads two back), then
/// <c>if // no branch ; then // branch drop A[ ... ]] lit !b !b ;</c> -- per real GA144 "if" semantics
/// (branch to "then" when the tested value is ZERO, otherwise fall through), this means: if the tested
/// value is NONZERO, the word takes the "if" body immediately (a bare <c>;</c>, "no branch" per the
/// source's own comment) and returns without doing anything further; if it IS zero, execution falls to
/// "then" instead, drops something, and streams the actual branch/jump (<c>m/branch</c> for <c>'clbr</c>,
/// <c>a!</c> for <c>'cljmp</c>). This matches both ops' own stated meaning ("... if r == 0") exactly --
/// unlike the polarity concerns flagged on <see cref="Node408Program"/>'s own fall-through idioms, this
/// one reads consistently with its own documentation at face value, so no flag is raised here.
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
/// native multi-port-wait/idle opcode -- see the 2026-09-06 remarks above for why the fetch moved here)
/// -- this is where <c>'lcall</c>/<c>'ljmp</c>/<c>'clbr</c>/<c>'lbr</c>/<c>'cljmp</c> below are actually
/// reached from.
///
/// <b><c>'lcall</c>/<c>'ljmp</c> (bodies simplified 2026-09-06 -- see above).</b> Each streams a short
/// instruction sequence the same way the register/stack helpers above do. <c>'lcall</c> now streams just
/// <c>&gt;r a</c>, <c>m/push</c>, <c>r&gt; a!</c> (its own former leading <c>A[ m/next ]] lit !b</c> moved
/// into <c>n/main</c>'s own shared tail, above) -- structurally the SAME body <see cref="Node507Program"/>'s
/// own <c>m/call</c> has (<c>begin drop &gt;r a m/push r&gt; a!</c>, minus the <c>begin drop</c> already
/// consumed by whatever calls in), i.e. a full call: push a return address, then jump. <c>'ljmp</c> now
/// streams just <c>a!</c> (likewise minus its own former leading <c>m/next</c>) -- structurally identical
/// to <see cref="Node507Program"/>'s own plain <c>'jump</c> (<c>m/next a!</c>, where the <c>m/next</c>
/// half now lives in <c>n/main</c> instead), no return address pushed. Per the source's own trailing
/// comment: "'lcall long call. the next word defines the destination address. 'ljmp long jump. jumps to
/// the address in the next word" -- confirming both still take a full-width address in the CVM word
/// immediately following the opcode, i.e. exactly the
/// <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/> shape <c>pushlit</c> already uses,
/// just with call/jump semantics instead of a stack push. This body simplification is a pure internal
/// refactor -- see the CVM opcode tag remarks below for why it changes neither mnemonic's own tag.
///
/// <b>How <c>'lcall</c> vs <c>'ljmp</c> (and now <c>'clbr</c>/<c>'lbr</c>/<c>'cljmp</c>) is actually
/// selected -- per Stefan: "the sequence 'ex ;' will call 'lcall and 'ljmp because their address is
/// already in R."</b> <c>n/main</c>'s own bit-cascade does NOT distinguish between any of the five at
/// all -- <c>ex</c> is GA144's native instruction, executing whatever address is already sitting in R
/// (the F18 register <see cref="Node507Program"/>'s own header calls <c>S</c>) as a direct jump/call. So
/// the choice of which of the five gets run is made entirely by whoever dispatches into node 407 in the
/// first place, by loading R with the target op's own address before handing off -- not by any further
/// bit pattern node 407 itself inspects.
///
/// <b>The CVM opcode tag (resolved 2026-09-02, unaffected by the 2026-09-05 rename or the 2026-09-06
/// extension).</b> Node 507's own <c>m/main</c> hands off down-port to node 407 once a fetched opcode
/// word's top bits read "11??" -- carrying, per the relay protocol traced in
/// <see cref="Node507Program"/>'s own remarks (the <c>x</c>/<c>y</c> stack convention: <c>x</c> is the
/// ORIGINAL fetched word, unshifted, relayed via <c>2* !p !p</c> to node 407 alongside the
/// progressively-shifted <c>y</c>), the ORIGINAL opcode word itself all the way to the "1100" tail, which
/// now fetches the trailing word first and THEN jumps directly to whatever address is already in R
/// (unchanged by that reordering). Since this tail is only reached once the cascade's own bit-tests have
/// consumed exactly "1100" (this method's own remarks above), and it jumps straight to <c>x</c>, <c>x</c>'s
/// own low bits must equal whichever of <c>'lcall</c>/<c>'ljmp</c>/<c>'clbr</c>/<c>'lbr</c>/<c>'cljmp</c>'s
/// real address ON NODE 407 the caller intends -- so the CVM-level opcode word is
/// <c>0xC000 | (address on node 407)</c> for ALL FIVE, the SAME "tag | local address" scheme
/// <see cref="Node507Program"/>'s own local-execute already uses with 0x8800. Wired up in
/// <see cref="CvmInstructionSet.LongCallMnemonic"/>/<see cref="CvmInstructionSet.LongJumpMnemonic"/>/
/// <see cref="CvmInstructionSet.LongConditionalBranchMnemonic"/>/<see cref="CvmInstructionSet.LongBranchMnemonic"/>/
/// <see cref="CvmInstructionSet.LongConditionalJumpMnemonic"/> (shape:
/// <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/>, exactly like <c>pushlit</c>) and
/// <see cref="Services.CvmAssemblyLanguage"/>'s own <c>Node407LongCallTagBits</c> (0xC000), resolved
/// against THIS node's live compile the same way <c>pushlit</c> resolves against node 507's -- the tag
/// itself does not depend on WHERE any of the five land in node 407's own RAM, only on which node answers
/// them.
///
/// <b>Verification (2026-09-06, this revision).</b> Compiled standalone against this project's real
/// <c>Compiler/F18Compiler.cs</c>, importing <see cref="Node507Program"/>'s own exports: 0 errors, 55/64
/// words used (up from 42), entry point <c>n/main</c> at 0x0010 (UNCHANGED -- everything before
/// <c>n/main</c> in the source is unchanged, so its own address doesn't move). Every symbol resolves:
/// <c>n/r@</c>=0x0000, <c>n/r!</c>=0x0002, <c>n/pop</c>=0x0004, <c>n/push</c>=0x0008, <c>n/next</c>=0x000A,
/// <c>n/leave</c>=0x000E, <c>n/main</c>=0x0010, <c>'lcall</c>=0x0020, <c>'ljmp</c>=0x0025,
/// <c>'clbr</c>=0x0027 (NEW), <c>'lbr</c>=0x002E (NEW), <c>'cljmp</c>=0x0030 (NEW). Also confirmed: both
/// <see cref="Node406Program"/>'s and <see cref="Node408Program"/>'s own current sources, each importing
/// this node by name, still compile cleanly against THIS revision with 0 errors and no changes needed on
/// their own side (the six exports they depend on -- <c>n/r@</c>/<c>n/r!</c>/<c>n/pop</c>/<c>n/push</c>/
/// <c>n/next</c>/<c>n/leave</c> -- are byte-for-byte unchanged from the 2026-09-05 revision).
///
/// <b><c>'lcall</c>'s own address shifted again -- a direct, mechanical consequence of this revision's
/// own restructuring, not a bug.</b> <c>'lcall</c> moved from 0x001F (the 2026-09-05 revision) to 0x0020
/// now, and <c>'ljmp</c> from 0x0026 to 0x0025 -- both because words were added/removed/reordered ahead
/// of them (the <c>A[ m/next ]] lit !b</c> hoist shrinks each of THEIR own bodies by one embedded call,
/// while growing <c>n/main</c>'s own tail by the same amount, net word-count changes depending on exactly
/// how the compiler packs each). This is simply where the CVM opcode tag scheme (tag | whatever address
/// the symbol happens to compile to) always resolves to a live compile rather than a fixed number -- see
/// <see cref="Services.CvmAssemblyLanguage"/>'s own remarks on why every tagged mnemonic works this way.
///
/// <b>CONFIRMED ON REAL HARDWARE (2026-09-02, address now STALE -- see the paragraph just above, twice
/// over).</b> A test program (<c>lcall label ... halt ... label: nop ret nop</c>) installed and run
/// against a real EVB, transaction log: <c>[READ] 0:0000 -&gt; C01B</c> / <c>[READ] 0:0001 -&gt; 0007</c>
/// (the <c>lcall</c> opcode and its trailing operand word, resolving to 0xC01B exactly as derived at the
/// time); <c>[WRITE] 1:FFFE &lt;- 0002</c> (the return address -- this instruction's own address + 2,
/// its own word length -- pushed onto the data stack by <c>'lcall</c>'s own <c>m/push</c>); <c>[READ]
/// 0:0007 -&gt; 8840</c> / <c>[READ] 0:0008 -&gt; 8831</c> (landing on <c>label</c>, executing <c>nop</c>
/// then <c>'ret</c>); <c>[READ] 1:FFFE -&gt; 0002</c> (<c>'ret</c> popping that same return address
/// back); <c>[READ] 0:0002 -&gt; 8840</c> / <c>[READ] 0:0003 -&gt; 8840</c> (execution resuming exactly
/// where <c>lcall</c> left off). The full MECHANISM -- opcode tag, trailing operand, the 507-&gt;407
/// relay, and <c>'lcall</c>'s own call/return semantics -- is proven to work end to end on real silicon
/// by this log; the EXACT addresses/opcode values it shows (0xC01B specifically) are now doubly stale for
/// a fresh compile of this revised source (0xC01F after the 2026-09-05 revision, 0xC020 now) -- a fresh
/// hardware run has not yet been done against either later revision, nor has <c>'clbr</c>/<c>'lbr</c>/
/// <c>'cljmp</c> been tested on hardware at all yet.
/// </summary>
internal static class Node407Program
{
  /// <summary>The node this program is always deployed to -- CVM2's long call/long jump/long branch helper.</summary>
  public const int Coordinate = 407;

  /// <summary>
  /// Node 407's full resident F18 source, as supplied by Stefan on 2026-09-06 ("more opcodes to node 407
  /// added"), replacing the 2026-09-05 revision. See the class remarks for the renamed header, the
  /// hoisted trailing-word fetch, the three new ops (<c>'clbr</c>/<c>'lbr</c>/<c>'cljmp</c>), and the
  /// updated verification numbers.
  /// </summary>
  public const string Source = """
      ( CVM2 node 407. VM secondary main, 11??_????_????_???? )
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
        A[ m/next ]] lit !b ex ;
      : 'lcall A[ >r a ]] lit !b A[ m/push ]] lit !b A[ r> a! ]] lit !b ;
      : 'ljmp A[ a! ]] lit !b ;
      : 'clbr A[ !p over !p ]] lit !b @b @b
        if // no branch
          ;
        then // branch
        drop A[ @p m/branch ]] lit !b !b ;
      : 'lbr A[ m/branch ]] lit !b ;
      : 'cljmp A[ !p over !p ]] lit !b @b @b
        if // no branch
          ;
        then // branch
        drop A[ @p a! ]] lit !b !b ;
      (
      this node provides support for extending the VM to neighbour nodes.
      tmp1 is a register available to the neighbouring nodes.
      'lcall long call. the next word defines the destination address.
      'ljmp long jump. jumps to the address in the next word
      'cljmp long jump. jumps to the address in the next word if r == 0
      'lbr long branch. branch with offset in the next word
      'clbr conditional long branch. branch with offset in the next word if r == 0
      )
      """;
}