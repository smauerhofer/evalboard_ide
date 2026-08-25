namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 506's resident F18 source -- the CVM test-cluster register-d node (test-mirror of real
/// design node 306, register d). See <see cref="Node607Program"/>'s remarks for the full
/// test-mirror mapping table.
///
/// <b>A pure servant, one level further out than 507.</b> 507 reaches 506 the same way 607
/// reaches 507 (a named multiport call, here 507's own <c>r---</c>, which parks 507's OWN P at
/// the port -- see <see cref="Node507Program"/>'s remarks on 507's <c>main</c> dispatch). From
/// then on, every <c>@p</c>/<c>!p</c> 507 executes is a live handshake with whatever 506 sends
/// through ITS reciprocal B port (pointed "right" back at 507). 506 imports 507's exported words
/// via <c># 507 import</c> (not 607's -- 506 only ever talks to its immediate parent).
///
/// <b>Local port directions on 506</b> (row 5 is odd, column 06 is even, per this project's
/// <c>KrakenTopology.PortAddress</c> mirroring rules):
/// <code>
///   right (r---, 0x1D5) -&gt; 507  the node that puppets 506 (matches "# right /b")
///   left  (--l-, 0x175) -&gt; 505  not part of this cluster
///   up    (---u, 0x145) -&gt; 606  not part of this cluster
///   down  (-d--, 0x115) -&gt; 406  not part of this cluster
/// </code>
///
/// <b>How control returns to 507</b> (worked out, not given). 507's own "CALL r---" pushes a
/// return address onto 507's OWN R -- the address of "a leave ;" right after "r---" in 507's own
/// <c>main</c>. As long as 507's P stays parked at the port, every word 506 ships arrives as if it
/// were 507's own next instruction. 506's own <c>leave</c> ships a bare <c>{return}</c> opcode to
/// 507; when 507 executes that (still fetching over the port), it pops ITS OWN R -- landing back
/// on "a leave ;", 507's own local cleanup -- so 506's own R stack never has to carry anything for
/// 507's sake at all.
///
/// <b>506's own loop-back</b> is a separate, self-contained mechanism: <c>leave</c> also CALLs
/// <c>main</c> (pushing RA_leave = leave's own trailing <c>;</c>); <c>main</c> does <c>&gt;r</c>
/// (pushing the dispatch address 507 just sent, on top of RA_leave) then <c>ex</c> (pops ONLY that
/// top entry and jumps) -- RA_leave is left undisturbed underneath. Whichever ALU word <c>ex</c>
/// jumps into, its own plain <c>;</c> at the end therefore pops exactly RA_leave, landing back on
/// leave's own tail, which (via its own trailing <c>;</c>) returns to whoever called <c>leave</c>
/// before -- a clean, self-priming loop from the second dispatch onward. Stefan confirmed the
/// cold-start case (the very first "entry main", before <c>leave</c> has ever run once, when R has
/// nothing valid queued yet) is fine as-is.
///
/// <b>A second address-collision trick</b>, in the same spirit as 507's <c>s/2put</c>/<c>s/put</c>
/// (confirmed there by Stefan): <c>csr16</c> compiles to a single word containing only a CALL to
/// <c>sr16</c>; that CALL's return address is intrinsically csr16's own next word -- <c>c!</c>'s
/// start -- REGARDLESS of who calls csr16. <c>'+c</c> falls through into csr16 (no <c>;</c> of its
/// own), so after sr16's shift loop returns into c!, c! stores the carry bit, and only THEN
/// returns to '+c's own caller -- and <c>'lsh</c> explicitly CALLs csr16 for the same reason,
/// picking up c!'s carry-capture "for free" the same way <c>'rsh</c> does explicitly.
///
/// <b>A note on confidence.</b> No per-word descriptions were given for this drop, unlike node
/// 507's. Every word's meaning below is inferred from its code and naming, cross-checked against
/// the compiled addresses -- treat it with the same lower confidence as node 607's <c>exec</c> or
/// node 606's <c>enter</c>. The control-flow mechanics (the leave/main self-priming loop, and the
/// csr16/c! collision) were traced and confirmed structurally against the compiled word addresses.
///
/// <b>Verification.</b> Compiled with zero errors (<c>Success = true</c>) against this project's
/// real <c>Compiler/F18Compiler.cs</c>, importing node 507's exported symbols via
/// <c># 507 import</c>. 61 of 64 RAM words used, entry point <c>main</c> at word address 0x000.
/// Two informational warnings are expected and benign: F18C050 for both <c>main</c> and
/// <c>leave</c>, each redefining a name imported from node 507 -- both nodes define their own
/// independent pair, and 506 never needs to call INTO 507's versions by name, so the shadowing is
/// intentional. Adding the per-word documentation comments to <see cref="Source"/> was re-verified
/// to produce byte-for-byte identical compiled output to the plain, uncommented version.
/// </summary>
internal static class Node506Program
{
  /// <summary>The node this program is always deployed to -- test-mirror of real design node 306 (register d).</summary>
  public const int Coordinate = 506;

  /// <summary>
  /// Node 506's full resident F18 source, fully commented per-word (every description here is
  /// inferred from the code and naming -- Stefan did not supply per-word descriptions for this
  /// node) with a traced walkthrough of the leave/main self-priming return loop and the
  /// csr16/c! address-collision trick. See the class remarks for the compile verification this
  /// source was checked against, including its cross-node import of node 507's symbol table via
  /// <c># 507 import</c>.
  /// </summary>
  public const string Source = """
      // ============================================================================
      // Node 506 -- CVM test-cluster register-d node (test-mirror of real design
      // node 306, register d)
      // ============================================================================
      //
      // Real hardware role (per cvm_2.txt): node 306 holds d, a second working
      // register that pairs with r (on 307/507) for extended-precision/carry-aware
      // arithmetic: add-with-carry, multi-bit shifts with a carry-out, unsigned
      // multiply, and sign/zero extension. Node 506 is that same node, test-
      // mirrored (row' = 8-row, column unchanged) -- see Node607Program.cs's
      // remarks for the full mirror-mapping table.
      //
      // A pure servant, one level further out than 507: 507 reaches 506 the same
      // way 607 reaches 507 (a named multiport call, here 507's own "r---", which
      // parks 507's OWN P at the port -- see 507's 'main' dispatch). From then on,
      // every @p/!p 507 executes is a live handshake with whatever 506 sends
      // through ITS reciprocal B port (pointed "right" back at 507 by this file's
      // own "# right /b" directive). 506 imports 507's exported words via
      // '# 507 import' (not 607's -- 506 only ever talks to its immediate parent).
      //
      // Local port directions on 506 (row 5 is odd, column 06 is even, per this
      // project's KrakenTopology.PortAddress mirroring rules):
      //     right (r---, 0x1D5) -> 507  (the node that puppets 506; matches this
      //                                   file's own "# right /b")
      //     left  (--l-, 0x175) -> 505  (not part of this cluster)
      //     up    (---u, 0x145) -> 606  (not part of this cluster)
      //     down  (-d--, 0x115) -> 406  (not part of this cluster)
      //
      // How control returns to 507 (worked out, not given): 507's own "CALL r---"
      // pushes a return address onto 507's OWN R -- the address of "a leave ;"
      // right after "r---" in 507's own 'main'. As long as 507's P stays parked at
      // the port, every word 506 ships arrives as if it were 507's own next
      // instruction. 506's own 'leave' below ships a bare {return} opcode to 507;
      // when 507 executes THAT (still fetching over the port), it pops ITS OWN R
      // -- landing back on "a leave ;", 507's own local cleanup -- so 506's own R
      // stack never has to carry anything for 507's sake at all.
      //
      // 506's OWN loop-back is a separate, self-contained mechanism: 'leave' also
      // CALLs 'main' (pushing RA_leave = leave's own trailing ';'); 'main' does
      // '>r' (pushing the dispatch address 507 just sent, on top of RA_leave) then
      // 'ex' (pops ONLY that top entry and jumps) -- RA_leave is left undisturbed
      // underneath. Whichever ALU word 'ex' jumps into, its own plain ';' at the
      // end therefore pops exactly RA_leave, landing back on leave's own tail,
      // which (via its own trailing ';') returns to whoever called 'leave' before
      // -- a clean, self-priming loop from the second dispatch onward. Stefan
      // confirmed the cold-start case (the very first "entry main", before 'leave'
      // has ever run once, when R has nothing valid queued yet) is fine as-is.
      //
      // A second address-collision trick, in the same spirit as 507's
      // s/2put/s/put (confirmed there by Stefan): 'csr16' compiles to a single
      // word containing only a CALL to 'sr16; that CALL's return address is
      // intrinsically 'csr16's own next word -- 'c!'s start -- REGARDLESS of who
      // calls csr16. '+c falls through into csr16 (no ';' of its own), so after
      // sr16's shift loop returns into c!, c! stores the carry bit, and only THEN
      // returns to '+c's own caller -- and 'lsh explicitly CALLs csr16 for the
      // same reason, picking up c!'s carry-capture "for free" the same way 'rsh
      // does explicitly (see 'lsh/'rsh/c! below).
      //
      // No per-word descriptions were given for this drop, unlike node 507's --
      // every word's meaning below is inferred from its code and naming, cross-
      // checked against the compiled addresses. Treat it with the same lower
      // confidence as node 607's 'exec or node 606's 'enter.
      //
      // Verified: this source compiles against the real F18Compiler with 0 errors
      // (Success=true), importing node 507's exported symbols via '# 507 import'.
      // 61 of 64 RAM words used, entry point 'main' at word address 0x000. Two
      // informational warnings are expected and benign: F18C050 for both 'main'
      // and 'leave', each redefining a name imported from node 507 -- both nodes
      // define their own independent 'main'/'leave' pair, and 506 never needs to
      // call INTO 507's versions by name, so the shadowing is intentional.
      // ============================================================================

      # 507 import

      # 0 org
      entry main

      //  A holds this node's own working register, d. Initialised to 0 at cold
      //  start, matching every other node in this cluster.
      # 0 /a

      //  B is initialised to point "right", toward 507 -- the master node that
      //  puppets this one. Every !b/@b in this file talks to 507 through B.
      # right /b

      // ----------------------------------------------------------------------
      // main  --  wait for the next dispatch and jump to it (inferred)
      // ----------------------------------------------------------------------
      // Ships {drop, !p} to 507 as a packed literal: when 507 (its P parked at
      // the port from its own "CALL r---") executes this, its own 'drop'
      // discards a stack item and '!p' sends 507's new top of stack -- the ALU
      // word address 507's own dispatch already selected -- out over the port.
      // '@b' on 506's side receives that address, '>r' parks it, and 'ex' pops
      // it straight back off and jumps there -- reaching whichever of the ALU
      // words below 507 asked for. Because '>r' only added ONE entry on top of
      // whatever was already on R, and 'ex' consumes only that same entry, R is
      // left exactly as it was before 'main' ran -- see the header note on how
      // this keeps the loop self-priming from the second dispatch onward.
      : main A[ drop !p ]] lit !b @b >r ex

      // ----------------------------------------------------------------------
      // leave  --  signal 507 that this operation is done, then wait for the
      // next one (inferred)
      // ----------------------------------------------------------------------
      // Ships a single packed {return} word to 507 -- executed by 507 (still
      // fetching over the port from its own parked P), this pops 507's OWN R
      // and resumes 507's own local cleanup code (its "a leave ;" tail) --
      // see the header note. Then CALLs 'main' again: this is what re-primes
      // 506's own R with a fresh return address (this word's own trailing ';')
      // before 'main's '>r'/'ex' pair consumes just the dispatch entry on top
      // of it, so whichever ALU word runs next returns correctly back here.
      : leave A[ ; ]] lit !b main ;

      // ----------------------------------------------------------------------
      // spop ( -w)  --  pop a value relayed from 607's own extended memory, via
      // 507 (inferred)
      // ----------------------------------------------------------------------
      // 's/pop' resolves (via '# 507 import') to 507's own exported word of
      // that name, so this ships {CALL s/pop} to 507 -- 507 in turn relays a
      // further {CALL /pop} up to 607, which pops and returns a word from its
      // own extended-memory area. A second packed word ships {!p} -- what 507
      // itself executes to send that value back down over the port -- and '@b'
      // on 506's side is what actually receives it, landing on 506's own stack
      // (the (-w) effect).
      : spop ( -w) A[ s/pop ]] lit !b A[ !p ]] lit !b @b ;

      // ----------------------------------------------------------------------
      // spush ( w)  --  push a value up the chain to 607's own extended memory,
      // via 507 (inferred)
      // ----------------------------------------------------------------------
      // Ships {@p, CALL s/push} to 507 in one packed word: 507's own '@p'
      // fetches the literal w this word's own trailing '!b' just carried
      // across, then 507 falls into its own exported 's/push', which itself
      // relays {@p, CALL /push} further up to 607 to complete the push.
      : spush ( w) A[ @p s/push ]] lit !b !b ;

      // ----------------------------------------------------------------------
      // r@ ( -w)  --  read 507's own register r (inferred)
      // ----------------------------------------------------------------------
      // Ships {a, !p} to 507: 507's own 'a' pushes 507's A (which holds r, per
      // Node507Program.cs's remarks), and '!p' sends it back over the port.
      // '@b' on 506's side receives it. Distinct from 506's own local register
      // d (held in 506's own A) -- this reaches across to 507's register, for
      // the cross-register arithmetic every ALU word below needs (r and d
      // paired together).
      : r@ ( -w) A[ a !p ]] lit !b @b ;

      // ----------------------------------------------------------------------
      // r! ( w)  --  write 507's own register r (inferred)
      // ----------------------------------------------------------------------
      // Ships {@p, a!} to 507: 507's own '@p' fetches the literal w this
      // word's own trailing '!b' carried across, and 'a!' stores it into 507's
      // A (r). The write-side counterpart of r@ above.
      : r! ( w) A[ @p a! ]] lit !b !b ;

      // ----------------------------------------------------------------------
      // 'zext  --  zero-extend: clear d (inferred)
      // ----------------------------------------------------------------------
      // 'dup xor' XORs a value with itself, which is always 0 regardless of
      // what was there (the same "s-0" idiom node 607's own /r@ uses), and
      // 'a!' stores that 0 into 506's own A (d) -- clearing the extension
      // register ahead of a zero-extended value in r.
      : 'zext dup xor a! ;

      // ----------------------------------------------------------------------
      // sr16  --  shift the value on the stack right by a full 16 bits
      // (inferred, from the name and the loop count)
      // ----------------------------------------------------------------------
      // 'for'/'unext' is this dialect's counted-loop idiom (7 for -> 8
      // iterations); each iteration does '2/ 2/' (two right shifts), for 16
      // shifts total across 8 iterations -- moving a double-length value's
      // high half down into view, or discarding a lower half already
      // consumed.
      : sr16 7 for 2/ 2/ unext ;

      // ----------------------------------------------------------------------
      // '+c  --  add with carry: d + [popped value] + r, result stored back
      // into r, carry captured into d via csr16/c! below (inferred)
      // ----------------------------------------------------------------------
      // 'a' pushes d, 'spop' pops a value relayed all the way from 607's own
      // extended memory (the addend), '+' adds them, 'r@' fetches r (the
      // running sum), '+' adds again, 'dup r!' stores the combined sum back
      // into r while keeping a duplicate on the stack. Has no ';' of its own:
      // falls straight through into 'csr16' immediately below, which shifts
      // that duplicate right 16 bits to extract whatever carried past the
      // visible 16-bit result -- and, per the header note's collision, that
      // CALL chain lands in 'c! before finally returning, storing the
      // extracted carry bit into d.
      : '+c a spop + r@ + dup r!

      // ----------------------------------------------------------------------
      // csr16  --  carry-shift-right-16: shared shift helper that always lands
      // in c! before returning (inferred; the CALL-target collision with c! is
      // worked out in the header note, in the same spirit as node 507's
      // s/2put/s/put)
      // ----------------------------------------------------------------------
      // Compiles to a single word containing nothing but a CALL to 'sr16.
      // Reached either by falling through from '+c above (no CALL instruction
      // of its own is needed to get here) or by an explicit CALL from 'lsh
      // below. Either way, that CALL's own return address is intrinsically
      // 'csr16's own next compiled word -- 'c!'s start -- so 'sr16's own
      // trailing ';' always lands in 'c!, not back at csr16's caller; only
      // AFTER c! runs does control finally return further up the call chain.
      : csr16 sr16

      // ----------------------------------------------------------------------
      // c!  --  capture the low bit of whatever's on the stack as the new
      // carry flag into d (inferred)
      // ----------------------------------------------------------------------
      // '1 and' masks everything but bit 0, 'a!' stores it into 506's own A
      // (d). Reached explicitly at the tail of 'rsh below, and implicitly
      // (via the csr16 collision above) at the tail of '+c and 'lsh.
      : c! 1 and a! ;

      // ----------------------------------------------------------------------
      // sl16  --  shift the value on the stack left by a full 16 bits
      // (inferred, mirrors sr16)
      // ----------------------------------------------------------------------
      // Same 'for'/'unext' counted-loop idiom as sr16, but with '2* 2*'
      // (two left shifts per iteration) instead of '2/ 2/'.
      : sl16 7 for 2* 2* unext ;

      // ----------------------------------------------------------------------
      // 'ldd  --  load d: copy d's value into r (inferred, mirrors node
      // 606/608's naming for "load" words)
      // ----------------------------------------------------------------------
      // 'a' pushes 506's own A (d), 'r!' stores it into 507's r.
      : 'ldd a r! ;

      // ----------------------------------------------------------------------
      // 'std  --  store d: copy r's value into d (inferred, the reverse of
      // 'ldd)
      // ----------------------------------------------------------------------
      // 'r@' fetches 507's r, 'a!' stores it into 506's own A (d).
      : 'std r@ a! ;

      // ----------------------------------------------------------------------
      // 'xd  --  exchange d with r (inferred, matches node 606/608's 'xf/'xg
      // naming for a genuine two-way exchange)
      // ----------------------------------------------------------------------
      // 'a' pushes d, 'r@' fetches r, 'a!' stores r's value into d, and the
      // final 'r!' stores the ORIGINAL d value (still sitting where 'a' left
      // it) into r -- a true swap of the two registers' contents.
      : 'xd a r@ a! r! ;

      // ----------------------------------------------------------------------
      // 'lsh  --  shift the (r,d) register pair left by one bit, capturing the
      // carry-out into d (inferred)
      // ----------------------------------------------------------------------
      // 'r@' fetches r, '2*' shifts it left one bit (vacating its low bit),
      // 'a' pushes d, 'xor' merges d's own low bit into that vacated slot (a
      // shift-in via XOR, since the vacated bit is known to be 0), 'dup r!'
      // stores the merged result back into r while keeping a duplicate on the
      // stack. 'csr16' then shifts that duplicate down 16 bits to recover
      // whatever was shifted out of r's own top -- and, via the collision
      // documented above, ends up running 'c! before returning, storing that
      // bit into d as the new carry-out. Same net effect as 'rsh below, just
      // reached through the implicit csr16->c! landing instead of an explicit
      // call.
      : 'lsh r@ 2* a xor dup r! csr16 ;

      // ----------------------------------------------------------------------
      // 'rsh  --  shift the (r,d) register pair right by one bit, capturing
      // the carry-out into d (inferred)
      // ----------------------------------------------------------------------
      // 'r@' fetches r, 'a' pushes d, 'sl16' shifts d left a full 16 bits
      // (moving it up to align with r's own bit range), 'xor' merges d's
      // shifted-up low bit into r's high end, 'dup 2/ r!' shifts the merged
      // value right one bit and stores it back into r while keeping a
      // duplicate (the bit shifted OUT of the low end) on the stack, and the
      // explicit trailing 'c!' captures that duplicate's low bit into d as the
      // new carry-out.
      : 'rsh r@ a sl16 xor dup 2/ r! c! ;

      // ----------------------------------------------------------------------
      // 'sext  --  sign-extend: replicate r's own sign bit across d (inferred)
      // ----------------------------------------------------------------------
      // 'r@' fetches r, '2* 2* 2/ 2/' shifts it left then right by two bits
      // each (a no-op on magnitude, but 2/'s arithmetic/sign-preserving
      // right shift re-floods the top bits with copies of the sign bit, per
      // node 507's own 'ssr comment on this same idiom), then falls through
      // (no ';' of its own) into 'mask' immediately below, which shifts the
      // remaining distance via 'sr16 and stores the fully sign-extended
      // result into d.
      : 'sext r@ 2* 2* 2/ 2/ sr16

      // ----------------------------------------------------------------------
      // mask  --  finish extending a value and store it into d (inferred)
      // ----------------------------------------------------------------------
      // 'xffff and' masks to 16 bits, 'a!' stores into 506's own A (d).
      // Reached via fall-through from 'sext above, and via an explicit CALL
      // from 'umul below.
      : mask xffff and a! ;

      // ----------------------------------------------------------------------
      // 'umul  --  unsigned multiply: r * [popped value], low half in r, high
      // half in d (inferred, from the classic shift-and-add multiply structure)
      // ----------------------------------------------------------------------
      // 'spop' pops the multiplicand (relayed from 607's own extended memory),
      // 'dup r@ a! dup xor' stashes a copy into d (used as the running
      // accumulator's high half) and clears the top of stack to 0 (the
      // accumulator's low half, built up in r via the '+*' steps below).
      // '8 for +* . +* unext' repeats the F18A '+*' opcode (this chip's
      // single-step shift-and-conditionally-add multiply primitive) sixteen
      // times total (8 iterations of two '+*'s each, matching sr16/sl16's own
      // 8x2 pattern for a full 16-bit operand), building the double-length
      // product one bit at a time. The closing '2* 2* a xffff and r! a sr16
      // 3 and xor mask' finishes aligning the two halves, masks and stores the
      // low half into r, then shifts, masks and combines the remaining bits
      // through 'mask above to store the high half into d.
      : 'umul spop dup r@ a! dup xor 8 for +* . +* unext
      	2* 2* a xffff and r! a sr16 3 and xor mask ;
      """;
}
