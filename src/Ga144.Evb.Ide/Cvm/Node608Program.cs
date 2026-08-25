namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 608's resident F18 source -- the CVM test-cluster global-pointer node (test-mirror of
/// real design node 208, register g). See <see cref="Node607Program"/>'s remarks for the full
/// test-mirror mapping table, and <see cref="Node606Program"/>'s remarks for the shared servant
/// relationship (608 is a mirror image of that design applied to globals instead of locals).
///
/// <b>Servant, not master.</b> 608 is only ever reached because 607's own <c>exec</c> jumps into
/// it (the <c>--l-</c> branch, the other half of the frame/global-class opcodes alongside 606's
/// <c>r---</c> branch). From then on 607's own <c>@p</c>/<c>!p</c> is a live handshake across the
/// wire with whatever 608 does with ITS reciprocal port (B, pointed "left" back at 607). 608 feeds
/// 607 raw instruction words via the same <c>A[ ... ]] lit !b</c> idiom, importing 607's own
/// exported words via <c># 607 import</c> exactly as 606 does.
///
/// <b>Local port directions on 608</b> (row 6 is even, column 08 is even, per this project's
/// <c>KrakenTopology.PortAddress</c> mirroring rules):
/// <code>
///   left  (--l-, 0x175) -&gt; 607  the CPU/master node that puppets 608
///   right (r---, 0x1D5) -&gt; 609  not part of this cluster
///   down  (-d--, 0x115) -&gt; 708  not part of this cluster
///   up    (---u, 0x145) -&gt; 508  not part of this cluster
/// </code>
/// -- matching this node's own "# left /b // master node" directive. A holds the global pointer g
/// itself; unlike node 606's frame pointer, g is never used to address 608's own local RAM here --
/// every global access goes back out to 607 via the port.
///
/// <b>The fix applied in this version.</b> The shared relay subroutine was originally named
/// <c>x3</c> (matching its job -- "writes the next 3 words..."). That collides with this
/// compiler's hex-literal syntax (DB014 3.3.2: a leading 'x' means the rest is hexadecimal), so a
/// bare <c>x3</c> in ordinary code silently compiled as the LITERAL 3 rather than a CALL to the
/// word. Renaming it to <c>'x3</c> (leading tick, matching this file's own convention for
/// CVM-instruction-implementing words) removes the ambiguity. Verified: with the old name, this
/// source failed to compile (RAM addresses 0x040-0x042 written more than once -- caused by the
/// address shift from the three spurious literal-3 words); with <c>'x3</c>, it compiles cleanly.
///
/// <b>Verification.</b> Compiled with zero errors (<c>Success = true</c>) against this project's
/// real <c>Compiler/F18Compiler.cs</c>, importing node 607's exported symbols via
/// <c># 607 import</c>. 64 of 64 RAM words used (no headroom left), entry point <c>main</c> at
/// word address 0x017. The same benign F18C050 "'main' redefines the name imported from node 607"
/// warning appears as it does for node 606, for the same reason. Adding the per-word documentation
/// comments to <see cref="Source"/> was re-verified to produce byte-for-byte identical compiled
/// output to the plain, uncommented version.
///
/// <b>A note on one word.</b> <c>leap</c> (in <c>'stx</c>) is not a typo and not a cross-node
/// reference -- it is a genuine, documented F18 control-flow keyword (DB013 5.3.2.1, confirmed
/// directly against this project's own compiler source): like <c>ahead</c>, it opens a forward
/// branch resolved by the next unmatched <c>then</c> the compiler encounters, wherever that
/// falls -- but <c>leap</c> compiles a CALL (with a real return) where <c>ahead</c> compiles a
/// plain JUMP. Here it reaches across into <c>'long</c>'s own body, the same "shared tail spanning
/// a word boundary" idiom already seen in node 607's <c>/cx?</c>-&gt;<c>/@</c> and in this node's
/// own <c>'x3</c>-&gt;<c>main</c>.
///
/// <b>A second pass</b> renamed <c>long</c> to <c>'long</c> (matching <c>'x3</c>/<c>'mk!</c>'s own
/// leading-tick convention) and, separately, tried folding <c>'ldx</c> into the <c>'x3</c> relay the
/// way <c>'ld</c>/<c>'st</c>/<c>'ldph</c> already do. That last change was reverted: <c>'x3</c>
/// always ends by jumping into <c>main</c> (per its own description, confirmed by Stefan, and its
/// compile-time-resolved <c>ahead</c>, which patches to whichever <c>then</c> textually follows it
/// regardless of caller), so a version of <c>'ldx</c> that called <c>'x3</c> and still had its own
/// <c>/r!</c> tail after the 3 relayed words left that tail compiled but structurally unreachable --
/// confirmed empirically (it still compiled with 0 diagnostics; the compiler does not detect dead
/// code) and then confirmed with Stefan before reverting. <c>'ldx</c> below is back to its original
/// four independent, individually-shipped words.
///
/// <b>A note on confidence.</b> Every word below carries one of Stefan's own given descriptions
/// (this drop included one for every word, unlike node 606's). The control-flow structure was
/// traced directly against the source and cross-checked against the compiled memory dump. The
/// precise internal bookkeeping of a few multi-step relays is inferred from the code and the
/// target words' own signatures on node 607, in the same spirit as the lower-confidence notes on
/// node 607's own <c>exec</c> and node 606's <c>enter</c> -- a well-supported reading, not
/// independently bit-traced against running hardware.
/// </summary>
internal static class Node608Program
{
  /// <summary>The node this program is always deployed to -- test-mirror of real design node 208 (register g).</summary>
  public const int Coordinate = 608;

  /// <summary>
  /// Node 608's full resident F18 source, fully commented per-word (using Stefan's own
  /// descriptions, given for every word this time) with a traced control-flow walkthrough of
  /// <c>main</c>'s command dispatch. See the class remarks for the compile verification this
  /// source was checked against, including its cross-node import of node 607's symbol table via
  /// <c># 607 import</c>, and the <c>x3</c>-&gt;<c>'x3</c> hex-literal-collision fix.
  /// </summary>
  public const string Source = """
      // ============================================================================
      // Node 608 -- CVM test-cluster global-pointer node (test-mirror of real design
      // node 208, register g)
      // ============================================================================
      //
      // Real hardware role (per cvm_2.txt): node 208 holds g, the CVM's global
      // pointer, and implements every global-variable operation the CPU (207/607)
      // needs: compute the address of a global, load a global's value, store a
      // value into a global, and the extended-address load/store + memory-control
      // primitives ('ldx/'stx/'mk!) used for pointer-indirect and memory-mapped
      // access. Node 608 is that same node, test-mirrored (row' = 8-row, column
      // unchanged) -- see Node607Program.cs's remarks for the full mirror-mapping
      // table.
      //
      // Same servant relationship as node 606 (see Node606Program.cs's remarks in
      // full): 608 is only ever reached because 607's own 'exec' jumps into it
      // (the '--l-' branch, for the other half of the frame/global-class opcodes),
      // and from then on 607's own @p/!p is a live handshake across the wire with
      // whatever 608 does with ITS reciprocal port (B, pointed "left" back at 607).
      // 608 feeds 607 raw instruction words to run via the same 'A[ ... ]] lit !b'
      // idiom, importing 607's own exported words via '# 607 import' exactly as
      // 606 does.
      //
      // Local port directions on 608 (row 6 is even, column 08 is even, per this
      // project's KrakenTopology.PortAddress mirroring rules):
      //     left  (--l-, 0x175) -> 607  (the CPU/master node that puppets 608)
      //     right (r---, 0x1D5) -> 609  (not part of this cluster)
      //     down  (-d--, 0x115) -> 708  (not part of this cluster)
      //     up    (---u, 0x145) -> 508  (not part of this cluster)
      // -- matching this node's own "# left /b // master node" directive. A holds
      // the global pointer g itself; unlike node 606's frame pointer, g is never
      // used to address 608's OWN local RAM here -- every global access goes back
      // out to 607 via the port, with g only ever appearing as a value shipped
      // across (see 'adr'/'glob'/'xg below).
      //
      // The fix applied in this version: the shared relay subroutine was
      // originally named 'x3' (matching its job -- "writes the next 3 words...").
      // That collides with this compiler's hex-literal syntax (DB014 3.3.2: a
      // leading 'x' means the rest is hexadecimal), so a bare 'x3' in ordinary code
      // silently compiled as the LITERAL 3 rather than a CALL to the word -- see
      // the conversation this was diagnosed in. Renaming it to ''x3' (leading
      // tick, matching this file's own convention for CVM-instruction-implementing
      // words) removes the ambiguity. Verified: with the old name, this source
      // failed to compile (RAM addresses 0x040-0x042 written more than once --
      // caused by the address shift from the three spurious literal-3 words); with
      // ''x3', it compiles cleanly.
      //
      // Verified: this source compiles against the real F18Compiler with 0 errors
      // (Success=true), importing node 607's exported symbols via '# 607 import'
      // exactly as node 606 does. 64 of 64 RAM words used (no headroom left in
      // this node), entry point 'main' at word address 0x017. The same benign
      // F18C050 "'main' redefines the name imported from node 607" warning appears
      // as it does for node 606, for the same reason (both nodes define their own
      // independent 'main' loop). Adding the per-word documentation comments below
      // was re-verified to produce byte-for-byte identical compiled output to the
      // plain, uncommented version.
      //
      // A note on one word: 'leap' (in 'stx, below) is not a typo and not a
      // cross-node reference -- it is a genuine, documented F18 control-flow
      // keyword (DB013 5.3.2.1, confirmed directly against this project's own
      // compiler source): like 'ahead', it opens a forward branch resolved by the
      // next unmatched 'then' the compiler encounters, wherever that falls -- but
      // 'leap' compiles a CALL (with a real return) where 'ahead' compiles a plain
      // JUMP (no return). Here it reaches across into 'long's own body (which
      // begins with 'then', below), the same "shared tail spanning a word
      // boundary" idiom already seen in node 607's /cx?->/@ and in this node's own
      // ''x3'->'main.
      //
      // A second pass renamed 'long' to ''long' (matching 'x3'/'mk!'s own leading-
      // tick convention) and considered folding 'ldx into the ''x3' relay the way
      // 'ld/'st/'ldph already do. That last change was tried and then reverted:
      // ''x3' always ends by jumping into 'main (per its own description --
      // confirmed by Stefan -- and by its compile-time-resolved 'ahead', which
      // patches to whichever 'then' textually follows it, here 'main's own,
      // regardless of caller), so a version of 'ldx that called ''x3' and then
      // still had its own '/r!' tail after the 3 relayed words left that tail
      // compiled but structurally unreachable -- confirmed empirically (it still
      // compiled with 0 diagnostics; the compiler does not detect dead code) and
      // then confirmed with Stefan before reverting. 'ldx below is back to its
      // original four independent, individually-shipped words.
      //
      // A note on confidence: everything below carries one of Stefan's own given
      // descriptions (this drop included one for every word, unlike node 606's).
      // The control-flow structure (which 'if'/'then'/'leap' pairs with which) was
      // traced directly against the source and cross-checked against the compiled
      // memory dump (e.g. confirming la/ldo/sto/glob land exactly where the
      // if/then nesting predicts). The precise internal bookkeeping of a few
      // multi-step relays (particularly ''x3's own re-use of A as scratch space,
      // and exactly which operand 'long relays in which order) is inferred from
      // the code and the target words' own signatures on node 607, in the same
      // spirit as the lower-confidence notes on node 607's own 'exec' and node
      // 606's 'enter -- clearly a well-supported reading, not independently
      // bit-traced against running hardware.
      // ============================================================================

      // Import node 607's exported dictionary so the A[ ... ]] blocks below can
      // reference 607's own words (/r@, /r!, /1@, /1!, /@, /!, /push, /pop) by
      // name -- DB002 3.1 "imported from another node".
      ( cvm2 1011 global)
      # 607 import

      # 0 org

      //  A holds g, the CVM's global pointer.
      # 0 /a

      //  master node: B is pointed "left", at 607 -- the CPU node that puppets
      //  608 via a named multiport call.
      # left /b
      entry main

      // ----------------------------------------------------------------------
      // adr ( w)  --  adds T to R
      // ----------------------------------------------------------------------
      // Ships {@p, CALL /r@} to 607 (fetch a literal -- sent by the following
      // plain '!b' of this word's own argument w, i.e. T -- then call /r@ to
      // fetch r's current value onto 607's stack). Ships a second word,
      // {+, tail-jump /r!}: 607 adds the two values (r's old value plus the just
      // received T) and jumps into /r! to store the sum back into r -- literally
      // "adds T to R", in place.
      : adr ( w) A[ @p /r@ ]] lit !b !b A[ + /r! ]] lit !b ;

      // ----------------------------------------------------------------------
      // 'x3  --  writes the next 3 words from the return address to node 207 (607
      // in this test mirror) and jumps to main (Stefan's own description)
      // ----------------------------------------------------------------------
      // 'a' saves 608's current global pointer (about to repurpose A as scratch).
      // 'r>' pops the return address the CALL into 'x3 just pushed -- the address
      // of the 3 raw 'A[ ... ]] ,' data words compiled right after that call, in
      // whichever of 'ld/'st/'ldph invoked it -- and 'a!' installs it into A.
      // '@+ !b' three times (the last without the post-increment) fetches and
      // relays each of those 3 words to 607 in turn. The final 'a!' restores
      // 608's real global pointer from what the first 'a' saved. 'ahead' then
      // jumps -- not returns -- to the 'then' that opens 'main's own body below,
      // which is exactly "and jumps to main".
      : 'x3 .loc a r> a! @+ !b @+ !b @ !b a! ahead

      // ----------------------------------------------------------------------
      // 'ld  --  loads word with address in R (Stefan's own description)
      // ----------------------------------------------------------------------
      // Calls ''x3, which relays these 3 raw words to 607 in order: {CALL /r@}
      // (fetch r -- currently holding an address), {CALL /1@} (fetch the word
      // stored at that address, page 1), {tail-jump /r!} (store the fetched word
      // back into r). Net effect: r goes in holding an address and comes out
      // holding the word found there.
      : 'ld .loc 'x3 A[ /r@ ]] , A[ /1@ ]] , A[ /r! ; ]] ,

      // ----------------------------------------------------------------------
      // 'st  --  pops and stores word with address in R (Stefan's own
      // description)
      // ----------------------------------------------------------------------
      // Relays: {CALL /pop} (607 pops a word off its own parameter/return area --
      // the value to store), {CALL /r@} (fetch r -- the address), {tail-jump
      // /1!} (store the popped value at that address, page 1).
      : 'st .loc 'x3 A[ /pop ]] , A[ /r@ ]] , A[ /1! ; ]] ,

      // ----------------------------------------------------------------------
      // 'ldph  --  push word with address in R (Stefan's own description)
      // ----------------------------------------------------------------------
      // Relays: {CALL /r@} (fetch r -- the address), {CALL /1@} (fetch the word
      // there, page 1), {tail-jump /push} (push that word onto 607's own
      // parameter/return area, rather than into r as 'ld does).
      : 'ldph .loc 'x3 A[ /r@ ]] , A[ /1@ ]] , A[ /push ; ]] ,

      // ----------------------------------------------------------------------
      // leave  --  gives control back to node 607 (in this test mirror; Stefan's
      // own description names the real design node, 207)
      // ----------------------------------------------------------------------
      // Ships a single packed {return} word to 607 (telling it to resume its own
      // normal flow), then falls through, with no ';', into 'local' immediately
      // below, sharing its 'drop ex' tail.
      : leave A[ ; ]] lit !b @p

      // ----------------------------------------------------------------------
      // local  --  calls local instruction (Stefan's own description)
      // ----------------------------------------------------------------------
      // 'drop' discards the leftover dispatch tag, 'ex' (F18A opcode 0x01) jumps
      // to whatever address was previously parked on the return stack with '>r'
      // -- the same "push an address on R, then EX to it" idiom used throughout
      // this project.
      : local drop ex

      // ----------------------------------------------------------------------
      // main  --  entry point (Stefan's own description)
      // ----------------------------------------------------------------------
      // The 'then' here resolves ''x3's own 'ahead' above. Ships {2*, !p, !p} to
      // 607 (the same shift-and-relay idiom node 606 uses: strips a tag bit and
      // writes 607's own top two stack items back out over the port), receives
      // them back via the two '@b's. 'x1ff and' masks to 9 bits this time (wider
      // than node 606's 8-bit 'xff' mask -- global addressing needs more range
      // than a frame offset), '>r' parks it, and '# local -until' overrides the
      // following '-until' to fall through to 'local' (see above) on failure.
      //
      // On success, execution falls into a 2-level dispatch (traced directly
      // against the source's 3 matched -if/then/leap pairs): bit1 selects
      // {la or ldo} vs {sto or glob}; within each half, bit2 chooses between the
      // two. Unlike node 606, there is no separate positive/negative-offset
      // split here -- 'adr' folds the offset in with a single '+', so one path
      // per operation suffices.
      : main then A[ 2* !p !p ]] lit !b @b
        @b x1ff and >r # local -until
        2* -if 2* -if
      // ----------------------------------------------------------------------
      // la  --  loads address with offset (Stefan's own description). Reached
      // when bit1 true, bit2 true.
      // ----------------------------------------------------------------------
      // 'r>' recovers the offset T main's dispatch parked, 'adr' adds it directly
      // into r (r += T, computing the target address in place -- no memory
      // access), 'leave' hands control straight back to 607 without relaying any
      // further data. This is the "just compute the address, don't dereference
      // it" case.
      : la r> adr leave ; then
      // ----------------------------------------------------------------------
      // ldo  --  loads word with offset (Stefan's own description). Reached when
      // bit1 true, bit2 false.
      // ----------------------------------------------------------------------
      // 'r>' recovers T, 'adr' folds it into r (address = base + offset), then
      // tail-calls 'ld (above) to actually fetch the word stored there into r.
      : ldo r> adr 'ld ; then 2* -if
      // ----------------------------------------------------------------------
      // sto  --  pops and stores word with offset (Stefan's own description).
      // Reached when bit1 false, bit2' true.
      // ----------------------------------------------------------------------
      // 'r>' recovers T, 'adr' folds it into r, then tail-calls 'st (above) to
      // pop a value off 607's own parameter area and store it at that address.
      : sto r> adr 'st ; then
      // ----------------------------------------------------------------------
      // glob  --  address of global variable (Stefan's own description). Reached
      // when bit1 false, bit2' false -- the last case, so no further branch/then
      // closes it; it simply runs and tail-calls 'main.
      // ----------------------------------------------------------------------
      // 'a' pushes the current global pointer g, 'r>' recovers the offset T,
      // '.' pads, '+' adds them (g + T = the global's actual address). Ships
      // {@p, tail-jump /r!} to 607 (fetch the literal -- sent by the following
      // plain '!b' of that computed address -- then jump into /r!, storing the
      // address itself into r, not the word at that address -- the address-of
      // operation, distinct from 'ldo which dereferences it).
      : glob a r> . + A[ @p /r! ; ]] lit !b !b main ;

      // ----------------------------------------------------------------------
      // 'xg ( s-s)  --  exchanges global pointer with R (Stefan's own
      // description)
      // ----------------------------------------------------------------------
      // Identical shape to node 606's 'xf (which exchanges the frame pointer):
      // 'a' pushes 608's current global pointer; the first packed word ships
      // {@p, CALL /r@} to 607 (fetch a literal -- the just-pushed pointer value,
      // sent by the following '!b' -- then fetch r's current value); the second
      // ships {!p, tail-jump /r!} (607 relays r's old value back over the port,
      // then stores the received pointer value into r). '@b'/'a!' on 608's side
      // capture that relayed old r value and install it as 608's new global
      // pointer -- a genuine two-way exchange.
      : 'xg A[ @p /r@ ]] lit !b a !b A[ !p /r! ; ]] lit !b @b a! ;

      // ----------------------------------------------------------------------
      // 'ldx  --  loads word from extended address (Stefan's own description)
      // ----------------------------------------------------------------------
      // Four separately-shipped single-purpose words (not chained through 'x3's
      // relay): {CALL /r@} (fetch r, one address component -- likely the page),
      // {CALL /pop} (pop another value off 607's own parameter area -- the other
      // address component), {CALL /@} (607's own "fetch word from extended
      // address", combining the two into the actual fetch, unlike 'ld's
      // page-1-only /1@), {tail-jump /r!} (store the fetched word into r).
      : 'ldx A[ /r@ ]] lit !b A[ /pop ]] lit !b A[ /@ ]] lit !b A[ /r! ; ]] lit !b ;

      // ----------------------------------------------------------------------
      // 'stx  --  stores word to extended address (Stefan's own description)
      // ----------------------------------------------------------------------
      // 'leap' is a genuine F18 control-flow keyword (DB013 5.3.2.1), not a typo
      // or a cross-node reference: like 'ahead', it opens a forward branch
      // resolved by the next unmatched 'then' -- here, the 'then' that opens
      // 'long's own body below -- but compiles as a CALL rather than a plain
      // jump, so 'long's own trailing ';' returns control back to right after
      // this 'leap, where 'stx's own remaining code runs. That remaining code
      // ships {tail-jump /!} to 607 (using the three values 'long already
      // relayed -- see 'long below -- to complete the extended-address store).
      : 'stx leap A[ /! ; ]] lit !b ;

      // ----------------------------------------------------------------------
      // 'long  --  word shared by 'stx and 'mk (Stefan's own description: 'mk
      // refers to 'mk! below)
      // ----------------------------------------------------------------------
      // The 'then' here is what 'stx's 'leap (above) calls into, and what 'mk!'s
      // own plain word-reference (below) also targets -- both land at the same
      // address, since a word's compiled address IS wherever its body starts,
      // here immediately at this 'then'. Relays three values to 607 needed by
      // /!'s ( wab) signature: {CALL /pop} (pop one value), {>r, CALL /pop} (park
      // it, pop a second value), {r>, CALL /r@} (recover the first, then fetch
      // r too) -- assembling the word/address-low/address-high triple /! needs,
      // leaving the caller (either 'stx or 'mk!) to ship the final tail-jump
      // into /! itself.
      : 'long then A[ /pop ]] lit !b A[ >r /pop ]] lit !b A[ r> /r@ ]] lit !b ;

      // ----------------------------------------------------------------------
      // 'mk!  --  used to manage memory access (Stefan's own description)
      // ----------------------------------------------------------------------
      // A plain (non-leap) call into 'long, reusing the exact same 3-value relay
      // 'stx uses. Ships {inv, tail-jump /!} to 607: inverts one operand (AN003's
      // write/control convention -- see node 607's own /! and /cx?) before
      // storing, distinguishing this "managed" write from 'stx's plain one.
      : 'mk! 'long A[ inv /! ; ]] lit !b ;
      """;
}