namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 507's resident F18 source -- the CVM test-cluster register-r / ALU node (test-mirror of
/// real design node 307, register r). See <see cref="Node607Program"/>'s remarks for the full
/// test-mirror mapping table.
///
/// <b>Not a pure servant.</b> Unlike 606/608 (which only ever ship raw instruction words for 607
/// to execute, with no computation of their own -- see <see cref="Node606Program"/>'s remarks),
/// 507 does genuine local work: every CVM binary/unary ALU opcode (shift, add, subtract, and, or,
/// xor, invert) actually executes here, on 507's own native F18 data stack, using 507's own r
/// value. 507 is also, in several of its own words, a relay in the other direction:
/// <c>s/push</c>/<c>s/@</c>/<c>s/!</c>/<c>s/pop</c> each end their packed payload with a CALL into
/// one of 607's OWN exported words (<c>/push</c>, <c>/@</c>, <c>/!</c>, <c>/pop</c>, resolved via
/// <c># 607 import</c> per DB002 3.1) so that, once shipped back to 607 over the port, 607
/// finishes a memory-class operation using the value 507 just supplied.
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
/// <b>This revision replaces an earlier one</b> (which still had <c>'cx?</c>, compare-and-exchange):
/// <c>'cx?</c> has been removed entirely, and eight new ALU ops have been added -- <c>'-</c>,
/// <c>'inv</c>, <c>'inc</c>, <c>'dec</c> join the existing <c>'usl</c>/<c>'ssr</c>/<c>'usr</c>/
/// <c>'+</c>/<c>'and</c>/<c>'xor</c>/<c>'or</c>, and Stefan's own trailing comment block now gives
/// each one an explicit CVM assembler mnemonic (<c>usl</c>, <c>ssr</c>, <c>usr</c>, <c>add</c>,
/// <c>sub</c>, <c>and</c>, <c>xor</c>, <c>or</c>, <c>inv</c>, <c>inc</c>, <c>dec</c>) -- all eleven
/// are registered as tagged CVM instructions in <c>Ga144.Cvm.Toolchain.CvmInstructionSet</c> (Ids
/// 9-19), the same way <c>nop</c>/<c>pushlit</c>/<c>push</c>/<c>pop</c>/<c>ret</c> are, so
/// <c>gaasm</c> accepts them with no operand: the values they act on already live in r and/or on
/// the CVM data stack, not in the instruction word. <c>main</c>'s own dispatch is also rewritten --
/// the unary/binary split (the last two branches) is now an explicit bit test with its own inline
/// comments, rather than the previous "-until override" idiom, so the bit-level reading is
/// Stefan's own this time, not inferred.
///
/// <b>Verification.</b> Compiled with zero errors and zero warnings (<c>Success = true</c>, no
/// diagnostics beyond the debug-only <c>.loc</c> info notes) against this project's real
/// <c>Compiler/F18Compiler.cs</c>, importing node 607's exported symbols via <c># 607 import</c>.
/// An EARLIER revision of this file would have produced an F18C050 warning here ("'main'
/// redefines the name imported from node 607") -- that warning no longer fires because node 607's
/// own fetch/decode/execute loop was itself renamed from <c>main</c> to <c>'nop</c> (see
/// <see cref="Node607Program"/>'s own remarks), so node 607 no longer exports anything named
/// <c>main</c> for this file's own <c>main</c> to shadow. This is exactly the same effect the
/// same 607 rename has on <see cref="Node606Program"/>'s own doc history.
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
      // servant -- see s/push/s/@/s/! below, each of which ends its packed payload
      // with a CALL into one of 607's OWN exported words (/push, /@, /!, resolved
      // via '# 607 import' per DB002 3.1) so that, once shipped back to 607 over
      // the port, 607 finishes a memory-class operation using the value 507 just
      // supplied.
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
      // and 0 warnings (Success=true, no diagnostics beyond debug-only '.loc' info
      // notes), importing node 607's exported symbols via '# 607 import'. An
      // EARLIER revision of this file would have produced an F18C050 warning here
      // ("'main' redefines the name imported from node 607") -- that warning no
      // longer fires because node 607's own fetch/decode/execute loop was itself
      // renamed from 'main' to 'nop (see Node607Program.cs's own remarks), so node
      // 607 no longer exports anything named 'main' for this file's own 'main' to
      // shadow.
      //
      // This version replaces an earlier one: 'cx? (compare-and-exchange) has been
      // removed, and eight new ALU ops have been added -- '-, 'inv, 'inc, 'dec join
      // the existing 'usl/'ssr/'usr/'+/'and/'xor/'or, and Stefan's own trailing
      // comment block below now gives each one an explicit CVM assembler mnemonic
      // (usl, ssr, usr, add, sub, and, xor, or, inv, inc, dec) -- all eleven are now
      // registered as tagged CVM instructions in Ga144.Cvm.Toolchain.CvmInstructionSet
      // (Ids 9-19), the same way nop/pushlit/push/pop/ret are, so gaasm accepts them
      // with no operand: the values they act on already live in r and/or on the CVM
      // data stack, not in the instruction word. 'main's own dispatch is also
      // rewritten below -- the unary/binary split (the last two branches) is now an
      // explicit bit test with its own inline comments, rather than the previous
      // "-until override" idiom, so the bit-level reading is Stefan's own this time,
      // not inferred.
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
      // returns the popped word t over the port. A second packed word, {!p},
      // then has 507 write out a value 607 sent back... no -- {!p} is what runs
      // on 607's OWN side of this same handshake pair, matching the s/r@/s/r!
      // pattern above: '@b' on 507's side is what actually receives 607's
      // popped value w, leaving it on 507's stack ((-w) effect).
      : s/pop ( -w) A[ /pop ]] lit !b A[ !p ]] lit !b @b ;

      // ----------------------------------------------------------------------
      // binary  --  calls binary function (Stefan's own description)
      // ----------------------------------------------------------------------
      // Compiles to a single word: a CALL to s/pop above, fetching the second
      // operand a binary ALU opcode needs (the first is already on 507's own
      // stack, r). Has no ';' of its own -- s/pop's own return address (the word
      // immediately after this one-word CALL) lands exactly on 'unary' below, so
      // once s/pop returns, execution falls straight through into unary's own
      // "a ex" dispatch tail (see 'unary' below) to reach the actual ALU word.
      // Reached from 'main's own "1100_1???" branch (drop >r binary ;) above.
      : binary s/pop

      // ----------------------------------------------------------------------
      // unary  --  calls unary function (Stefan's own description)
      // ----------------------------------------------------------------------
      // 'a' pushes 507's own A (its working register) onto the data stack; 'ex'
      // (F18A opcode 0x01, the same "jump to whatever address was parked on the
      // return stack with '>r'" idiom used throughout this project) then jumps
      // to the ALU word address 'main' parked on R just before dispatching here
      // (see main's own two ALU branches above) -- reaching whichever of
      // 'usl/'ssr/'usr/'+/'-/'and/'xor/'or/'inv/'inc/'dec that address selected.
      // Reached either directly (main's "1100_0???" branch, drop >r unary ;, for a
      // genuinely unary op) or by falling through from 'binary' above (after
      // s/pop supplies a second operand, for a binary op) -- both paths converge
      // on this same "a ex" tail, which is exactly what "unary calls unary
      // function" / "binary calls binary function" describe: the actual dispatch
      // machinery is shared, only the operand count differs.
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
      // What follows is a 4-way dispatch on the received word's own top bits --
      // Stefan's own inline comments (kept verbatim below) give the bit pattern
      // each branch matches, so unlike the previous version this reading is given,
      // not inferred:
      //   1) 11??: a further 2-bit choice among three of 507's own children --
      //      407 (-d--, register w, pattern 1111), 508 (--l-, register t, pattern
      //      1110_1), or 506 (r---, register d, the fallback pattern 1110_0) --
      //      each followed by 'a leave ;' (push A, mask+store the port reply into
      //      A, ship the return signal, and return).
      //   2) 1101: the "short literal" case -- shifts the value down (2* then six
      //      2/'s) to extract an immediate literal encoded directly in the opcode
      //      bits, then 'leave ;' as above.
      //   3) 1100_1???: a binary ALU op -- 'drop' discards the now-fully-consumed
      //      tag, '>r' parks the jump-table target address (computed from the
      //      opcode's low bits) for 'unary'/'ex' to consume, and 'binary' fetches
      //      the second operand (s/pop) before falling through into 'unary's own
      //      "a ex" dispatch tail.
      //   4) 1100_0???: a unary ALU op -- same 'drop >r', but straight into
      //      'unary' (no second operand to fetch first).
      : main A[ !p !p ]] lit !b @b >r @b r> ..
      	( xy)
      	// 11??_????_????_????
      	2* -if // 111?_????_????_????
      		2* -if // 1111_????_????_???? node 407
      			( xy) -d-- a leave ;
      		then // 1110_????_????_????
      		2* -if  // 1110_1???_????_???? node 508
      			( xy) --l- a leave ;
      		then // 1110_0???_????_???? node 506
      		( xy) r--- a leave ;
      	then // 110?_????_????_????
      	( xy )
      	2* -if // 1101_????_????_????
      		// short literal
      		2* 2/ 2/ 2/ 2/ 2/ 2/ leave ;
      	then // 1100_????_????_????
      	( xy )
      	2* -if // 1100_1???_????_????
      		drop >r binary ;
      	then  // 1100_0???_????_????
      		drop >r unary ;

      // ----------------------------------------------------------------------
      // 'usl  --  binary. unsigned shift left. CVM assembler mnemonic: usl
      // ----------------------------------------------------------------------
      // 'for'/'unext' is this dialect's counted-loop idiom: repeats '2*'
      // (shift left one bit) a number of times taken from the loop counter,
      // implementing an unsigned left shift by an arbitrary count.
      : 'usl for 2* unext ;

      // ----------------------------------------------------------------------
      // 'ssr  --  binary. signed shift right. CVM assembler mnemonic: ssr
      // ----------------------------------------------------------------------
      // '>r 2* 2*' moves the shift count aside and doubles the value being
      // shifted twice (aligning it so the following pair of '2/'s -- F18A's
      // arithmetic/sign-preserving right shift -- divide back down by four net
      // of the two 2*'s), 'r>' restores the count. The double 2*/2/ pairing is
      // how this project realises a signed shift, as opposed to 'usr below. Has
      // no ';' of its own: falls straight through into 'usr immediately below,
      // whose own 'unext' loop-closer supplies the actual return both words
      // share -- 'ssr's own trailing 'r>' just needs to run before that shared
      // tail, not before a return of its own.
      : 'ssr >r 2* 2* 2/ 2/ r>

      // ----------------------------------------------------------------------
      // 'usr  --  binary. unsigned shift right. CVM assembler mnemonic: usr
      // ----------------------------------------------------------------------
      // Same 'for'/'unext' counted-loop idiom as 'usl, but with '2/' (logical
      // shift right, no sign extension) instead of '2*'. Its own 'unext' is also
      // 'ssr's return (see 'ssr's remarks above).
      : 'usr for 2/ unext ;

      // ----------------------------------------------------------------------
      // '+  --  binary. add. CVM assembler mnemonic: add
      // ----------------------------------------------------------------------
      // The plain F18A '+' opcode.
      : '+ + ;

      // ----------------------------------------------------------------------
      // 'and  --  binary. bitwise and. CVM assembler mnemonic: and
      // ----------------------------------------------------------------------
      // The plain F18A 'and' opcode.
      : 'and and ;

      // ----------------------------------------------------------------------
      // 'xor  --  binary. bitwise exclusive or. CVM assembler mnemonic: xor
      // ----------------------------------------------------------------------
      // The plain F18A 'xor' opcode.
      : 'xor xor ;

      // ----------------------------------------------------------------------
      // 'or  --  binary. bitwise or. CVM assembler mnemonic: or
      // ----------------------------------------------------------------------
      // F18A has no native 'or' opcode, so this builds one from the ones it does
      // have, via De Morgan's law: 'inv over inv and' inverts the top, inverts a
      // copy of the item below it, ands the two inversions together (giving
      // inv(a) and inv(b) = inv(a or b)) -- leaving one inversion still to apply
      // to recover the plain bitwise or. Has no ';' of its own: falls straight
      // through into 'inv immediately below, whose single 'inv ;' supplies
      // exactly that last inversion AND 'or's own return in one shared word,
      // the same sharing trick as 'ssr/'usr above.
      : 'or inv over inv and

      // ----------------------------------------------------------------------
      // 'inv  --  unary. bitwise invert. CVM assembler mnemonic: inv
      // ----------------------------------------------------------------------
      // The plain F18A '-' (not/invert) opcode. Also serves as 'or's own shared
      // return (see 'or's remarks above) -- 'inv is reached both as a genuine
      // unary op in its own right and as the tail end of 'or's body.
      : 'inv inv ;

      // ----------------------------------------------------------------------
      // '-  --  binary. subtract. CVM assembler mnemonic: sub
      // ----------------------------------------------------------------------
      // Two's-complement subtraction without a native subtract opcode: 'inv'
      // negates T (bitwise, one's complement), '+' adds S and the inverted T.
      // Has no ';' of its own -- falls straight through into 'inc immediately
      // below, whose '1 . +' adds the missing "+1" that turns one's-complement
      // negation into two's-complement negation, completing S - T in one shared
      // tail (the same sharing trick as 'ssr/'usr and 'or/'inv above).
      : '- inv . +

      // ----------------------------------------------------------------------
      // 'inc  --  unary. increment. CVM assembler mnemonic: inc
      // ----------------------------------------------------------------------
      // Adds the literal 1 to T. Also serves as '-'s own shared return (see '-'s
      // remarks above) -- reached both as a genuine unary op in its own right and
      // as the tail end of '-'s body.
      : 'inc 1 . + ;

      // ----------------------------------------------------------------------
      // 'dec  --  unary. decrement. CVM assembler mnemonic: dec
      // ----------------------------------------------------------------------
      // Adds the literal -1 to T.
      : 'dec -1 . + ;
      """;
}