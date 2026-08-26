namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 507's resident F18 source -- the CVM test-cluster register-r / ALU node (test-mirror of
/// real design node 307, register r). See <see cref="Node607Program"/>'s remarks for the full
/// test-mirror mapping table.
///
/// <b>Not a pure servant.</b> Unlike 606/608 (which only ever ship raw instruction words for 607
/// to execute, with no computation of their own -- see <see cref="Node606Program"/>'s remarks),
/// 507 does genuine local work: every CVM binary/unary ALU opcode (shift, add, and, or, xor,
/// atomic compare-exchange) actually executes here, on 507's own native F18 data stack, using
/// 507's own r value. 507 is also, in several of its own words, a relay in the other direction:
/// <c>s/push</c>/<c>s/@</c>/<c>s/!</c>/<c>s/pop</c>/<c>'cx?</c> each end their packed payload with
/// a CALL into one of 607's OWN exported words (<c>/push</c>, <c>/@</c>, <c>/!</c>, <c>/pop</c>,
/// <c>/cx?</c>, resolved via <c># 607 import</c> per DB002 3.1) so that, once shipped back to 607
/// over the port, 607 finishes a memory-class operation using the value 507 just supplied.
///
/// <b>Local port directions on 507</b> (row 5 is odd, column 07 is odd, per this project's
/// <c>KrakenTopology.PortAddress</c> mirroring rules):
/// <code>
///   up    (---u, 0x145) -&gt; 607  the CPU/master node that puppets 507 (matches "# up /b")
///   down  (-d--, 0x115) -&gt; 407  register w, mirrors 407 (self-mapping node)
///   left  (--l-, 0x175) -&gt; 508  register t, mirrors 308
///   right (r---, 0x1D5) -&gt; 506  register d, mirrors 306
/// </code>
/// 607 reaches 507 the same way it reaches 606/608: a named multiport call (607's own
/// <c>/r@</c> = "dup dup xor dup ---u", <c>/r!</c> = "3 dup ---u") parks 607's P at the port and
/// hands control here. 507's own <c>main</c>, in turn, uses <c>-d--</c>/<c>--l-</c>/<c>r---</c> to
/// reach ITS OWN children (407/508/506) for the other CVM registers w/t/d -- the same
/// one-hop-further tree structure 607 uses to reach 606/608/707, just one level down.
///
/// <b>The s/2put/s/put trick.</b> <c>s/2put</c> compiles to exactly one word containing nothing
/// but <c>leap</c> -- a CALL to the matching <c>then</c>, which is the very next token compiled:
/// the <c>then</c> opening <c>s/put</c>'s own body immediately below, with no other code in
/// between. Because of that, the CALL's runtime return address (the word right after s/2put's own
/// single-word body) and its jump target (s/put's own start) are the SAME address. Confirmed with
/// Stefan: this is a deliberate trick, not a bug -- s/2put calls s/put, and when s/put's own
/// trailing <c>;</c> returns, it returns to s/put's own start again rather than to s/2put's
/// caller, so s/put's body runs a second time; only that second pass's own <c>;</c> finally pops
/// the return address s/2put's real caller (<c>s/@</c> or <c>s/!</c>) is waiting on. Net effect of
/// one call to <c>s/2put</c>: s/put's single-cell relay happens twice, consuming two stack cells
/// with one call -- e.g. an extended address's two halves.
///
/// <b>Verification.</b> Compiled with zero errors (<c>Success = true</c>) against this project's
/// real <c>Compiler/F18Compiler.cs</c>, importing node 607's exported symbols via
/// <c># 607 import</c>. 64 of 64 RAM words used, entry point <c>main</c> at word address 0x01A.
/// The same benign F18C050 "'main' redefines the name imported from node 607" warning appears as
/// it does for every other node in this cluster, for the same reason (each node defines its own
/// independent <c>main</c> loop). Adding the per-word documentation comments to
/// <see cref="Source"/> was re-verified to produce byte-for-byte identical compiled output to the
/// plain, uncommented version.
///
/// <b>A note on confidence.</b> Every word below carries one of Stefan's own given descriptions.
/// The s/2put/s/put mechanism above was confirmed directly by Stefan. The 4-way dispatch inside
/// <c>main</c> and the shared "a ex" dispatch tail <c>binary</c> falls through into <c>unary</c>
/// were traced against the compiled word addresses (all 4 <c>-if</c>/<c>then</c> pairs balance,
/// the same LIFO nesting node 607's own <c>exec</c> uses) but are not independently described by
/// Stefan beyond "start address" / "calls unary function" / "calls binary function" -- a
/// well-supported reading, not independently bit-traced against running hardware, in the same
/// spirit as the lower-confidence notes on node 607's own <c>exec</c> and node 606's <c>enter</c>.
/// </summary>
internal static class Node507Program
{
  /// <summary>The node this program is always deployed to -- test-mirror of real design node 307 (register r / ALU).</summary>
  public const int Coordinate = 507;

  /// <summary>
  /// Node 507's full resident F18 source, fully commented per-word (using Stefan's own
  /// descriptions, given for every word this time) with a traced control-flow walkthrough of
  /// <c>main</c>'s dispatch and the s/2put/s/put double-relay trick. See the class remarks for the
  /// compile verification this source was checked against, including its cross-node import of
  /// node 607's symbol table via <c># 607 import</c>.
  /// </summary>
  public const string Source = """
      // ============================================================================
      // Node 507 -- CVM test-cluster register-r / ALU node (test-mirror of real
      // design node 307, register r)
      // ============================================================================
      //
      // Real hardware role (per cvm_2.txt): node 307 holds r, the CVM's general
      // working register, and is also where the CVM's arithmetic/logic unit lives
      // -- every binary and unary ALU opcode (shift, add, and, or, xor, compare-
      // exchange) executes here, not on 607. Node 507 is that same node, test-
      // mirrored (row' = 8-row, column unchanged) -- see Node607Program.cs's
      // remarks for the full mirror-mapping table.
      //
      // Unlike 606/608 (pure "servant" nodes that only ever ship raw instruction
      // words for 607 to execute, with no real computation of their own), 507 does
      // genuine local work: it runs its own arithmetic on its own native F18 data
      // stack, using its own r value, and only touches 607 to fetch operands and
      // deliver results. It is also, in several of its own words, a second kind of
      // servant -- see s/push/s/@/s/!/s/pop/'cx? below, each of which ends its
      // packed payload with a CALL into one of 607's OWN exported words (/push,
      // /@, /!, /pop, /cx?, resolved via '# 607 import' per DB002 3.1) so that,
      // once shipped back to 607 over the port, 607 finishes a memory-class
      // operation using the value 507 just supplied.
      //
      // Local port directions on 507 (row 5 is odd, column 07 is odd, per this
      // project's KrakenTopology.PortAddress mirroring rules):
      //     up    (---u, 0x145) -> 607  (the CPU/master node that puppets 507;
      //                                   matches this file's own "# up /b")
      //     down  (-d--, 0x115) -> 407  (register w, mirrors 407 -- self-mapping
      //                                   node, see Node607Program.cs's table)
      //     left  (--l-, 0x175) -> 508  (register t, mirrors 308)
      //     right (r---, 0x1D5) -> 506  (register d, mirrors 306)
      // 607 reaches 507 the same way it reaches 606/608: a named multiport call
      // (607's own /r@ = "dup dup xor dup ---u", /r! = "3 dup ---u") parks 607's
      // P at the port and hands control here. 507's own 'main', in turn, uses
      // -d--/--l-/r--- to reach ITS OWN children (407/508/506) for the other CVM
      // registers w/t/d -- the same one-hop-further tree structure 607 uses to
      // reach 606/608/707, just one level down.
      //
      // Stefan confirmed a deliberate trick in s/2put/s/put (see the comments on
      // those two words below): 'leap' compiles a CALL whose runtime return
      // address and jump target are the SAME address here, which does not hang
      // the node -- it makes a single call to s/put execute s/put's body twice
      // before finally returning to whichever of s/@/s/! invoked it. This is used
      // to relay both halves of an extended (page, address) pair with one call.
      //
      // Verified: this source compiles against the real F18Compiler with 0 errors
      // (Success=true), importing node 607's exported symbols via '# 607 import'.
      // 64 of 64 RAM words used, entry point 'main' at word address 0x01A. One
      // informational warning is expected and benign: F18C050, "'main' redefines
      // the name imported from node 607" -- both 607 and 507 each define their
      // own local word called 'main' (their own, independent loops), and 507
      // never needs to call INTO 607's main by name, so the shadowing is
      // intentional, not a conflict.
      // ============================================================================

      # 607 import

      # 0 org
      entry main

      //  A holds this node's own working/result register -- see 'leave' below,
      //  which stores into it, and the several places that read it back with the
      //  bare 'a' opcode ('push A onto the stack'). Initialised to 0 at cold start,
      //  matching every other node in this cluster.
      # 0 /a

      //  B is initialised to point "up", toward 607 -- the master node that
      //  puppets this one. Every !b/@b in this file that isn't part of the
      //  down/left/right dispatch to 407/508/506 talks to 607 through B.
      # up /b

      // ----------------------------------------------------------------------
      // s/r@  --  used by 607 to read R (Stefan's own description)
      // ----------------------------------------------------------------------
      // Ships {@p} to 607 as a packed literal (through B, "A[ @p ]] lit !b"):
      // when 607 receives and runs this word, its own '@p' fetches whatever
      // literal 607's caller sent -- so this half of the handshake reads a value
      // coming FROM 607's side. 'a' then pushes 507's own current register value
      // (r, held in A) onto the data stack, 'dup' duplicates it, and the final
      // '!b' relays one copy back to 607 over the port -- delivering r's value
      // to 607 while leaving a copy of it on 507's own stack too.
      : s/r@ A[ @p ]] lit !b a dup !b ;

      // ----------------------------------------------------------------------
      // s/r!  --  used by 607 to write R (Stefan's own description)
      // ----------------------------------------------------------------------
      // Ships {!p} to 607 as a packed literal: when 607 runs this word, its own
      // '!p' sends its current top-of-stack value out over the port. '@b' on
      // 507's side receives that value -- becoming the new value of r (left on
      // 507's own stack for whatever code, such as 'leave' below, stores it into
      // A next).
      : s/r! A[ !p ]] lit !b @b ;

      // ----------------------------------------------------------------------
      // s/2put ( ww)  --  relays TWO stack cells to 607, one call, one word
      // ----------------------------------------------------------------------
      // 's/2put' compiles to exactly one word containing nothing but 'leap' -- a
      // CALL (F18A opcode 0x03) to the matching 'then', which is the very next
      // token compiled: the 'then' that opens s/put's own body immediately
      // below. Because there is no other code between 'leap' and that 'then',
      // the CALL's runtime return address (the word right after s/2put's own,
      // single-word body) and its jump target (s/put's own start) land on
      // exactly the same address. Per Stefan: this is a deliberate trick, not a
      // bug -- s/2put calls s/put, and when s/put's own trailing ';' returns, it
      // returns to s/put's own start again (not to s/2put's caller), so s/put's
      // body runs a SECOND time; only that second pass's own ';' finally pops
      // the return address s/2put's original caller (s/@ or s/! below) is
      // actually waiting on. Net effect of calling 's/2put': s/put's single-cell
      // relay happens twice, consuming two stack cells (the ( ww) in this word's
      // stack comment) with one call -- e.g. an extended address's two halves.
      : s/2put ( ww) leap

      // ----------------------------------------------------------------------
      // s/put ( w)  --  pushs cell on stack of control node 607 (Stefan's own
      // description)
      // ----------------------------------------------------------------------
      // The 'then' here resolves s/2put's 'leap' above. Ships {@p} to 607 as a
      // packed literal, then a second '!b' actually transmits it -- '@p' on
      // 607's side fetches a literal value FROM whatever 607's own instruction
      // stream supplies next, letting 507's own top-of-stack word (already
      // staged for the '!b' to carry across) land as a literal on 607's side.
      // Reached directly (as a single relay) from s/push below, or twice in a
      // row via s/2put's trick (from s/@/s/! below).
      : s/put ( w) then A[ @p ]] lit !b !b ;

      // ----------------------------------------------------------------------
      // s/push ( w)  --  pushs cell on stack (Stefan's own description)
      // ----------------------------------------------------------------------
      // Ships {@p, CALL /push} to 607 in one packed word: '/push' resolves (via
      // '# 607 import' above) to 607's own exported word of that name, so when
      // 607 receives and runs this payload it fetches the literal w this word's
      // own '!b' just carried across, then falls straight into its own /push
      // (( pt-p), 607's extended-memory-area push) to complete the operation --
      // pushing w onto the CVM's own parameter/return area.
      : s/push ( w) A[ @p /push ]] lit !b !b ;

      // ----------------------------------------------------------------------
      // s/@ ( ba-w)  --  read cell from any page (Stefan's own description)
      // ----------------------------------------------------------------------
      // First relays BOTH halves of an extended (page b, address a) pair to 607
      // via s/2put's double-relay trick (see above), then ships {CALL /@} to
      // 607: '/@' resolves to 607's own exported ( ab-w) extended-address fetch,
      // so 607 completes the read using the two values 507 just relayed, and
      // '@b' on 507's side receives the fetched word w back over the port.
      : s/@ ( ba-w) s/2put A[ /@ ]] lit !b @b ;

      // ----------------------------------------------------------------------
      // s/! ( baw)  --  write cell to any page (Stefan's own description)
      // ----------------------------------------------------------------------
      // Mirrors s/@ for a write: s/2put relays the (page, address) pair, then
      // ships {@p, CALL /!} to 607 -- '@p' fetches the value w to store (this
      // word's own '!b' carries it across), then falls into 607's own exported
      // ( wab) extended-address store to complete the write.
      : s/! ( baw) s/2put A[ @p /! ]] lit !b !b ;

      // ----------------------------------------------------------------------
      // s/pop ( -w)  --  pops cell from stack (Stefan's own description)
      // ----------------------------------------------------------------------
      // Ships {CALL /pop} to 607 -- '/pop' resolves to 607's own exported
      // ( p-pt) extended-memory pop, so 607 reads and advances its own p and
      // returns the popped word t over the port. A second packed word ships
      // {!p} -- what runs on 607's OWN side of this same handshake, matching
      // the s/r@/s/r! pattern above -- and '@b' on 507's side is what actually
      // receives 607's popped value w, leaving it on 507's own stack (the
      // (-w) effect).
      : s/pop ( -w) A[ /pop ]] lit !b A[ !p ]] lit !b @b ;

      // ----------------------------------------------------------------------
      // binary  --  calls binary function (Stefan's own description)
      // ----------------------------------------------------------------------
      // Compiles to a single word: a CALL to s/pop above, fetching the second
      // operand a binary ALU opcode needs (the first is already on 507's own
      // stack). Has no ';' of its own -- s/pop's own return address (the word
      // immediately after this one-word CALL) lands exactly on 'unary' below, so
      // once s/pop returns, execution falls straight through into unary's own
      // "a ex" dispatch tail (see 'unary' below) to reach the actual ALU word.
      : binary s/pop

      // ----------------------------------------------------------------------
      // unary  --  calls unary function (Stefan's own description)
      // ----------------------------------------------------------------------
      // 'a' pushes 507's own A (its working register) onto the data stack; 'ex'
      // (F18A opcode 0x01, the same "jump to whatever address was parked on the
      // return stack with '>r'" idiom used throughout this project) then jumps
      // to the ALU word address parked on R just before dispatching here (see
      // main's own closing line below) -- reaching whichever of
      // 'usl/'ssr/'usr/'+/'and/'xor/'or/'cx? that address selected. Reached
      // either directly (main's -until loop-back, for a genuinely unary op) or
      // by falling through from 'binary' above (after s/pop supplies a second
      // operand, for a binary op) -- both paths converge on this same "a ex"
      // tail, which is exactly what "unary calls unary function" / "binary
      // calls binary function" describe: the actual dispatch machinery is
      // shared, only the operand count differs.
      : unary a ex

      // ----------------------------------------------------------------------
      // leave  --  store cell in R and exit arithmetic operation (Stefan's own
      // description)
      // ----------------------------------------------------------------------
      // 'xffff and' masks the result to 16 bits, 'a!' stores it into A -- this
      // node's own r/result register, matching "store cell in R". Has no ';' of
      // its own: falls straight through into 'ctrl' immediately below, which
      // ships the actual "exit arithmetic operation" signal back to 607. Called
      // directly, with an explicit trailing ';' supplied at each call site, from
      // all three of main's down/left/right dispatch branches (see 'main'
      // below).
      : leave xffff and a!

      // ----------------------------------------------------------------------
      // ctrl  --  returns control to node 607 (Stefan's own description)
      // ----------------------------------------------------------------------
      // Ships a single packed {return} word to 607 (telling it to resume its
      // own normal flow -- the same "; " packed-return idiom node 608's 'leave'
      // uses to hand control back). Has no ';' of its own: relies on whichever
      // call site reached it (via 'leave's fall-through) to supply the actual
      // return, exactly like 608's leave/local pair.
      : ctrl A[ ; ]] lit !b

      // ----------------------------------------------------------------------
      // main  --  start address (Stefan's own description)
      // ----------------------------------------------------------------------
      // Begins with the same @p/!p/@b handshake idiom used by every servant
      // node's own 'main' in this cluster: {!p, !p} relays two outgoing words to
      // 607, '@b' receives one word back, '>r' parks it, a second '@b' receives
      // another, 'r>' recovers the first -- collecting the two-word request 607
      // sent when it multiport-called into 507 (via ---u).
      //
      // What follows is a 4-way dispatch, traced directly against the compiled
      // addresses (all 4 '-if'/'then' pairs balance, the same LIFO nesting
      // 607's own 'exec' uses). Exact bit meanings are inferred from this
      // structure -- Stefan's description only says "start address" -- so
      // treat the bit-level reading below as inferred, not as given:
      //   1) Outer bit set: a further 2-bit choice between three of 507's own
      //      children -- 508 (--l-, register t), 407 (-d--, register w), or 506
      //      (r---, register d, the fallback of the inner pair) -- each
      //      followed by 'a leave ;' (push A, mask+store the port reply into A,
      //      ship the return signal, and return).
      //   2) Outer bit clear, next bit set: a "short literal" case (Stefan's own
      //      inline comment, kept verbatim below) -- shifts the value down
      //      (2* then six 2/'s) to extract an immediate literal encoded directly
      //      in the opcode bits, then 'leave ;' as above.
      //   3) Neither: the ALU fallback -- 'over >r' parks a jump-table target
      //      address (computed from the opcode's low bits by whatever produced
      //      this dispatch value) onto R for 'unary'/'ex' to consume, '2*'
      //      shifts and tests one more bit, and '# unary -until' overrides the
      //      following '-until' to loop BACK to 'unary' (above) when that bit
      //      is clear -- reaching it as a genuine unary op -- or fall through to
      //      'binary' (also above) when set, for a binary op, before the
      //      explicit trailing ';' returns.
      : main A[ !p !p ]] lit !b @b >r @b r> ..
      	2* -if 2* -if --l- a leave ;
      	then 2* -if -d-- a leave ;
      	then r--- a leave ;
      	then 2* -if // short literal
      	2* 2/ 2/ 2/ 2/ 2/ 2/ leave ;
      	then over >r 2* # unary -until binary ;

      // ----------------------------------------------------------------------
      // 'usl  --  binary. unsigned shift left (Stefan's own description)
      // ----------------------------------------------------------------------
      // 'for'/'unext' is this dialect's counted-loop idiom: repeats '2*'
      // (shift left one bit) a number of times taken from the loop counter,
      // implementing an unsigned left shift by an arbitrary count.
      : 'usl for 2* unext ;

      // ----------------------------------------------------------------------
      // 'ssr  --  binary. signed shift right (Stefan's own description)
      // ----------------------------------------------------------------------
      // '>r 2* 2*' moves the shift count aside and doubles the value being
      // shifted twice (aligning it so the following pair of '2/'s -- F18A's
      // arithmetic/sign-preserving right shift -- divide back down by four net
      // of the two 2*'s), 'r>' restores the count. The double 2*/2/ pairing is
      // how this project realises a signed shift, as opposed to 'usr below.
      : 'ssr >r 2* 2* 2/ 2/ r> ;

      // ----------------------------------------------------------------------
      // 'usr  --  binary. unsigned shift right (Stefan's own description)
      // ----------------------------------------------------------------------
      // Same 'for'/'unext' counted-loop idiom as 'usl, but with '2/' (logical
      // shift right, no sign extension) instead of '2*'.
      : 'usr for 2/ unext ;

      // ----------------------------------------------------------------------
      // '+  --  binary. add (Stefan's own description)
      // ----------------------------------------------------------------------
      // The plain F18A '+' opcode.
      : '+ + ;

      // ----------------------------------------------------------------------
      // 'and  --  binary. bitwise and (Stefan's own description)
      // ----------------------------------------------------------------------
      // The plain F18A 'and' opcode.
      : 'and and ;

      // ----------------------------------------------------------------------
      // 'xor  --  binary. bitwise exclusive or (Stefan's own description)
      // ----------------------------------------------------------------------
      // The plain F18A 'xor' opcode.
      : 'xor xor ;

      // ----------------------------------------------------------------------
      // 'or  --  binary. bitwise or (Stefan's own description)
      // ----------------------------------------------------------------------
      // F18A has no native 'or' opcode, so this builds one from the ones it
      // does have, via De Morgan's law: 'inv over inv and inv' inverts the top,
      // inverts a copy of the item below it, ands the two inversions together
      // (giving inv(a) and inv(b) = inv(a or b)), then inverts the result once
      // more to recover the plain bitwise or.
      : 'or inv over inv and inv ;

      // ----------------------------------------------------------------------
      // 'cx?  --  binary. atomically compares and exchanges a cell in memory and
      // return true if successful (Stefan's own description)
      // ----------------------------------------------------------------------
      // Ships {CALL /pop} to 607 first (fetching one of the extended-address
      // components /cx? needs, duplicating and parking a copy of it via
      // 'dup >r' for use after the reply comes back), receives it via '!b', then
      // 'r> !b' relays the parked copy too. A further packed word ships
      // {CALL /cx?} -- '/cx?' resolves (via '# 607 import') to 607's own
      // exported ( wabn-f) compare-and-exchange word, so 607 completes the
      // atomic operation using the components 507 just relayed. The final
      // packed word {!p} plus '@b' on 507's side receives the resulting flag f
      // back over the port, matching the s/r@/s/pop reply pattern above.
      : 'cx? A[ @p /pop ]] lit dup !b
      	>r !b r> !b !b A[ /cx? ]] lit !b
      	A[ !p ]] lit !b @b ;
      """;
}