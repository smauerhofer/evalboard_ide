namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 508's resident F18 source -- the CVM test-cluster register-t / comparison node (test-mirror
/// of real design node 308, register t). See <see cref="Node607Program"/>'s remarks for the full
/// test-mirror mapping table.
///
/// <b>A pure servant, one level further out than 507</b> -- the same relationship as
/// <see cref="Node506Program"/>'s (see its remarks in full). 507 reaches 508 via a named multiport
/// call (507's own <c>--l-</c>), parking 507's OWN P at the port; from then on every
/// <c>@p</c>/<c>!p</c> 507 executes is a live handshake with whatever 508 sends through ITS
/// reciprocal B port (pointed "left" back at 507). 508 imports 507's exported words via
/// <c># 507 import</c>.
///
/// <b>Local port directions on 508</b> (row 5 is odd, column 08 is even, per this project's
/// <c>KrakenTopology.PortAddress</c> mirroring rules):
/// <code>
///   left  (--l-, 0x175) -&gt; 507  the node that puppets 508 (matches "# left /b")
///   right (r---, 0x1D5) -&gt; 509  not part of this cluster
///   up    (---u, 0x145) -&gt; 608  not part of this cluster
///   down  (-d--, 0x115) -&gt; 408  not part of this cluster
/// </code>
///
/// <b>How control returns to 507, and 508's own loop-back:</b> the same mechanism worked out for
/// node 506 (see its remarks in full) -- 507's own multiport CALL leaves a return address on 507's
/// OWN R that 508's ships-a-{return}-opcode trick pops, and 508's own R is kept self-primed from
/// one dispatch to the next by whichever word calls <c>main</c> again at its own tail. Here that
/// word is <c>r!</c> (below) rather than a dedicated <c>leave</c> -- <c>r!</c> packs the
/// store-into-507's-r, the return-to-507 signal, AND the loop-back into one word, since (as far as
/// this source shows) every dispatch into this node ultimately needs its result stored into r via
/// <c>r!</c> before 507 is done with it. None of the comparison/arithmetic words below call
/// <c>r!</c> or <c>main</c> themselves -- consistent with 506's op-words never calling
/// <c>leave</c> either -- so this reading follows the same shape Stefan already confirmed for 506
/// ("cold start with main is ok"), just with <c>r!</c> standing in for <c>leave</c>. This is
/// inferred, not given.
///
/// <b>"if"/"-if"/"until"/"-until" semantics</b> are quoted directly from this project's own DB001
/// (F18A Technology Reference) and DB013 (arrayForth User's Manual), not guessed: <c>if</c>
/// continues (falls through) when T is NONZERO and jumps to the matching <c>then</c> when T is
/// ZERO; <c>-if</c> continues when T is NEGATIVE and jumps when T is non-negative; <c>until</c>
/// continues when T is nonzero and jumps (backward, to whatever <c>#</c> overrides it to) when T
/// is zero; <c>-until</c> continues when T is negative and jumps when T is non-negative. None of
/// these pop T.
///
/// <b>A naming convention worth noting:</b> the UNticked helper words below (<c>gt0</c>,
/// <c>ge0</c>, <c>le0</c>, <c>lt0</c>) are the raw, unsigned comparison primitives, shared by both
/// the unsigned CVM words (<c>'ugt</c>/<c>'uge</c>/<c>'ule</c>/<c>'ult</c>, which call them
/// directly) and the signed CVM words (<c>'gt</c>/<c>'ge</c>/<c>'le</c>/<c>'lt</c>, whose own
/// ticked <c>'*0</c> wrappers first re-normalize the sign bit with a "2* 2*" shift -- the same
/// idiom used by node 506's <c>'sext</c> and node 507's <c>'ssr</c> for a value that is logically
/// 16 bits wide sitting in an 18-bit F18 word -- before delegating to the shared helper).
///
/// <b>The typo fix applied in this version.</b> The source as given used <c>\a</c>/<c>\b</c>
/// instead of <c>/a</c>/<c>/b</c> for the node-configuration directives, and had a duplicated
/// <c>entry main</c> line. Both are confirmed typos (Stefan). The backslash is significant here:
/// this compiler treats a bare <c>\</c> as a line-comment starter (same as <c>//</c>), so
/// <c># 0 \a</c> tokenized as just <c># 0</c> with the rest of the line silently swallowed as a
/// comment -- confirmed empirically against the compiled result's own <c>InitialA</c>/
/// <c>InitialB</c> metadata, which came back unset with the original text (every other node in
/// this cluster has them populated). With <c>/a</c>/<c>/b</c> restored and the duplicate
/// <c>entry main</c> removed, <c>InitialA</c> is 0 and <c>InitialB</c> is 0x175 ("left"), and the
/// compiled words are otherwise byte-identical to the original (uncorrected) text.
///
/// <b>A note on confidence.</b> Stefan's own trailing comment block covers only <c>'-</c>/
/// <c>u@-</c> ("subtract"), <c>'xt</c>, <c>'ldt</c>, <c>'stt</c>, and <c>'bitcnt</c>. Everything
/// else below is inferred from the code, cross-checked against the compiled addresses and against
/// node 506/507's already-confirmed idioms -- treat it with the same lower confidence as node
/// 607's <c>exec</c> or node 506's own word-by-word notes.
///
/// <b>Verification.</b> Compiled with zero errors (<c>Success = true</c>) against this project's
/// real <c>Compiler/F18Compiler.cs</c>, importing node 507's exported symbols via
/// <c># 507 import</c>. 63 of 64 RAM words used, entry point <c>main</c> at word address 0x000.
/// One informational warning is expected and benign: F18C050, "'main' redefines the name imported
/// from node 507" -- both nodes define their own independent <c>main</c>, and 508 never needs to
/// call INTO 507's by name, so the shadowing is intentional. Adding the per-word documentation
/// comments to <see cref="Source"/> was re-verified to produce byte-for-byte identical compiled
/// output to the plain, typo-corrected version.
/// </summary>
internal static class Node508Program
{
  /// <summary>The node this program is always deployed to -- test-mirror of real design node 308 (register t / comparisons).</summary>
  public const int Coordinate = 508;

  /// <summary>
  /// Node 508's full resident F18 source, fully commented per-word (Stefan's own descriptions are
  /// quoted where given -- '-/u@-, 'xt, 'ldt, 'stt, 'bitcnt -- everything else is inferred) with a
  /// traced control-flow walkthrough of every comparison word's shared-helper structure. See the
  /// class remarks for the compile verification this source was checked against, including its
  /// cross-node import of node 507's symbol table via <c># 507 import</c>, and the
  /// <c>\a</c>/<c>\b</c>-&gt;<c>/a</c>/<c>/b</c> typo fix.
  /// </summary>
  public const string Source = """
      ( cvm2 comparison)
      // ============================================================================
      // Node 508 -- CVM test-cluster register-t / comparison node (test-mirror of
      // real design node 308, register t)
      // ============================================================================
      //
      // Real hardware role (per cvm_2.txt): node 308 holds t, a third working
      // register alongside r (307/507) and d (306/506), and is where the CVM's
      // comparison and single-operand ALU opcodes live: signed/unsigned equality
      // and ordering tests, absolute value/negate, invert, T<->register exchange,
      // and population count. Node 508 is that same node, test-mirrored (row' =
      // 8-row, column unchanged) -- see Node607Program.cs's remarks for the full
      // mirror-mapping table.
      //
      // A pure servant, one level further out than 507, the same relationship as
      // 506's (see Node506Program.cs's remarks in full): 507 reaches 508 via a
      // named multiport call (507's own "--l-"), parking 507's OWN P at the port;
      // from then on every @p/!p 507 executes is a live handshake with whatever
      // 508 sends through ITS reciprocal B port (pointed "left" back at 507).
      // 508 imports 507's exported words via '# 507 import'.
      //
      // Local port directions on 508 (row 5 is odd, column 08 is even, per this
      // project's KrakenTopology.PortAddress mirroring rules):
      //     left  (--l-, 0x175) -> 507  (the node that puppets 508; matches this
      //                                   file's own "# left /b")
      //     right (r---, 0x1D5) -> 509  (not part of this cluster)
      //     up    (---u, 0x145) -> 608  (not part of this cluster)
      //     down  (-d--, 0x115) -> 408  (not part of this cluster)
      //
      // How control returns to 507 and 508's own loop-back: the same mechanism
      // worked out for node 506 (see its remarks in full) -- 507's own multiport
      // CALL leaves a return address on 507's OWN R that 508's ships-a-{return}-
      // opcode trick pops, and 508's own R is kept self-primed from one dispatch
      // to the next by whichever word calls 'main' again at its own tail. Here
      // that word is 'r!' (below) rather than a dedicated 'leave' -- 'r!' packs
      // the store-into-507's-r, the return-to-507 signal, AND the loop-back into
      // one word, since (as far as this source shows) every dispatch into this
      // node ultimately needs its result stored into r via 'r!' before 507 is
      // done with it. None of the comparison/arithmetic words below call 'r!' or
      // 'main' themselves -- consistent with 506's op-words never calling 'leave'
      // either -- so this reading follows the same shape Stefan already confirmed
      // for 506 ("cold start with main is ok"), just with 'r!' standing in for
      // 'leave'. Flagging this explicitly since it's inferred, not given.
      //
      // The "if"/"-if"/"until"/"-until" semantics below are quoted directly from
      // this project's own DB001 (F18A Technology Reference) and DB013 (arrayForth
      // User's Manual), not guessed: 'if' continues (falls through) when T is
      // NONZERO and jumps to the matching 'then' when T is ZERO; '-if' continues
      // when T is NEGATIVE and jumps when T is non-negative; 'until' continues
      // when T is nonzero and jumps (backward, to whatever '#' overrides it to)
      // when T is zero; '-until' continues when T is negative and jumps when T is
      // non-negative. None of these pop T.
      //
      // A naming convention worth noting: the UNticked helper words below (gt0,
      // ge0, le0, lt0) are the raw, unsigned comparison primitives, shared by both
      // the unsigned CVM words ('ugt/'uge/'ule/'ult, which call them directly) and
      // the signed CVM words ('gt/'ge/'le/'lt, whose own ticked '*0 wrappers first
      // re-normalize the sign bit with a "2* 2*" shift -- the same idiom used by
      // node 506's 'sext and node 507's 'ssr for a value that is logically 16 bits
      // wide sitting in an 18-bit F18 word -- before delegating to the shared
      // helper).
      //
      // No per-word descriptions were given for most of this drop; Stefan's own
      // trailing comment block covers only '-/u@- ("subtract"), 'xt, 'ldt, 'stt,
      // and 'bitcnt. Everything else below is inferred from the code, cross-
      // checked against the compiled addresses and against node 506/507's already-
      // confirmed idioms -- treat it with the same lower confidence as node 607's
      // 'exec or node 506's own word-by-word notes.
      //
      // Verified: this source (with '\a'/'\b' corrected to '/a'/'/b', and the
      // duplicate 'entry main' line removed -- both confirmed as typos by Stefan)
      // compiles against the real F18Compiler with 0 errors (Success=true),
      // importing node 507's exported symbols via '# 507 import'. 63 of 64 RAM
      // words used, entry point 'main' at word address 0x000. One informational
      // warning is expected and benign: F18C050, "'main' redefines the name
      // imported from node 507" -- both nodes define their own independent
      // 'main', and 508 never needs to call INTO 507's by name, so the shadowing
      // is intentional.
      // ============================================================================

      # 507 import

      # 0 org
      entry main

      //  A holds this node's own working register, t. Initialised to 0 at cold
      //  start, matching every other node in this cluster.
      # 0 /a

      //  B is initialised to point "left", toward 507 -- the master node that
      //  puppets this one. Every !b/@b in this file talks to 507 through B.
      # left /b

      // ----------------------------------------------------------------------
      // main  --  wait for the next dispatch, jump to it, and leave 507's own r
      // value on the stack for it (inferred)
      // ----------------------------------------------------------------------
      // Ships {drop, !p, a, !p} to 507 in one packed word: 507's own 'drop'
      // discards a stack item, the first '!p' sends 507's new top of stack --
      // the op-word address 507's own dispatch already selected -- out over the
      // port, 507's own 'a' then pushes ITS OWN A (r), and the second '!p' sends
      // that too. 508 receives both: '@b' (the dispatch address), '>r' (parked
      // on R), a second '@b' (r's value, left on 508's own data stack -- this
      // is what u@- below immediately expects to find on top), and 'ex' pops
      // the parked dispatch address back off R and jumps there. Because '>r'
      // only added ONE entry on top of whatever R already held, and 'ex'
      // consumes only that same entry, R is left exactly as it was before
      // 'main' ran -- see the header note on how 'r!' keeps this self-primed.
      : main A[ drop !p a !p ]] lit !b @b >r @b ex

      // ----------------------------------------------------------------------
      // r! ( w)  --  store w into 507's own register r, signal 507 that this
      // operation is done, then wait for the next one (inferred)
      // ----------------------------------------------------------------------
      // Ships {@p, a!, return} to 507 in a single packed word: 507's own '@p'
      // fetches the literal w this word's own trailing '!b' carried across,
      // 'a!' stores it into 507's A (r), and the packed 'return' opcode is what
      // 507 executes next (still fetching over the port) to pop ITS OWN R and
      // resume its own local cleanup code -- the same mechanism worked out for
      // node 506, just combined with the store into one word here instead of
      // two separate packed sends. Then CALLs 'main' again: this is what
      // re-primes 508's own R with a fresh return address (this word's own
      // trailing ';') before 'main's '>r'/'ex' pair consumes just the dispatch
      // entry on top of it, so whichever word runs next returns correctly back
      // here.
      : r! ( w) A[ @p a! ; ]] lit !b !b main ;

      // ----------------------------------------------------------------------
      // '-  --  subtract (Stefan's own description, paired with u@- below)
      // ----------------------------------------------------------------------
      // Compiles to nothing: an empty body, so this name is a pure alias --
      // calling '- is identical to calling u@- immediately below (same
      // compiled address). Given as a separate, CVM-facing name for the same
      // subtraction u@- implements internally for every comparison word below.
      : '- ( w-w)

      // ----------------------------------------------------------------------
      // u@-  --  subtract: [value popped via 507/607] - [507's own r, already
      // on the stack from main's dispatch] (Stefan's own description, "subtract")
      // ----------------------------------------------------------------------
      // 'inv' inverts whatever main's dispatch left on top of the stack (r's
      // value) -- the first step of a two's-complement negation, preparing to
      // ADD instead of subtract. Ships {CALL s/pop} to 507 -- resolved (via
      // '# 507 import') to 507's own exported word, itself relaying a further
      // {CALL /pop} up to 607 to fetch a word w from 607's own extended-memory
      // area -- then a second packed word ships {!p}: what 507 itself executes
      // to send that popped value back down over the port, received here via
      // '@b'. '. +' pads and adds the inverted r to the fetched w, completing
      // the two's-complement subtraction: result = w + (-r) = w - r. Every
      // comparison word below calls this (directly or via '-) to get that
      // difference, then tests its sign/zero-ness.
      : u@- ( w-w) inv A[ s/pop ]] lit !b A[ !p ]] lit !b @b . +

      // ----------------------------------------------------------------------
      // 'inc / 'dec  --  add/subtract 1 (inferred)
      // ----------------------------------------------------------------------
      // '. +' pads and adds a signed literal (1 or -1) to whatever is on top of
      // the stack.
      : 'inc 1 . + ; : 'dec -1 . + ;

      // ----------------------------------------------------------------------
      // 'eq / 'eq0 / 'false / 'true  --  signed/unsigned equality test, and the
      // shared true(1)/false(0) result words every comparison below reuses
      // (inferred)
      // ----------------------------------------------------------------------
      // 'eq calls u@- to get the difference D, then enters 'eq0: 'if' continues
      // (falls through) when D is NONZERO, executing ''false's body ('dup xor',
      // always 0) and returning via 'false's own ';' -- or, when D IS ZERO,
      // jumps forward to the matching 'then', which is exactly where 'true's
      // own body starts ('then 1 ;'), returning 1. Net effect: 'eq returns 1
      // when the two operands are equal (D==0), 0 otherwise -- and 'true/'false
      // (0x011/0x010) remain independently callable words, reused by 'ne below.
      : 'eq u@- : 'eq0 if : 'false dup xor ; : 'true then 1 ;

      // ----------------------------------------------------------------------
      // 'ne / 'ne0  --  not-equal test, reusing 'true above (inferred)
      // ----------------------------------------------------------------------
      // 'ne0: 'if' continues when D is nonzero, CALLing the already-defined
      // 'true (pushing 1) and returning via this word's own ';' -- or, when D
      // is zero, jumps to 'then' (right here, opening the else-branch: 'dup
      // xor ;', pushing 0). Net effect: the exact opposite of 'eq, as expected.
      : 'ne u@- : 'ne0 if 'true ; then dup xor ;

      // ----------------------------------------------------------------------
      // 'ugt / gt0 / ge0  --  unsigned greater-than, and the shared "D>0" /
      // "D>=0" helpers every ordering comparison below reuses (inferred)
      // ----------------------------------------------------------------------
      // 'ugt calls u@- for D, then 'gt0: '# 'false until' overrides 'until's
      // backward target to 'false -- 'until' continues when D is nonzero,
      // falling into 'ge0 below; when D IS ZERO, jumps straight to 'false
      // (equal is not "greater than"). 'ge0: '# 'true -until' overrides
      // '-until's target to 'true -- '-until' continues when D is NEGATIVE,
      // falling through to 'dup xor ;' (0/false, since D<0 means the left
      // operand is smaller); when D is non-negative (and, having fallen through
      // gt0's gate, is known nonzero, so strictly positive), jumps to 'true.
      // Net effect: 'ugt is true iff D>0 strictly. 'ge0 alone (skipping gt0's
      // equal-gate) is reused directly by 'uge below for ">=": true iff D>=0.
      : 'ugt u@- : gt0 # 'false until
      : ge0 # 'true -until dup xor ;

      // ----------------------------------------------------------------------
      // 'gt / 'gt0  --  signed greater-than: re-normalizes D's sign bit, then
      // delegates to the shared gt0 helper above (inferred)
      // ----------------------------------------------------------------------
      // '2* 2*' re-floods the correct sign-bit position for a logically-16-bit
      // value (the same idiom node 506's 'sext and node 507's 'ssr use), then
      // calls the UNticked 'gt0' helper above -- reusing its D>0-strictly logic
      // with a properly sign-normalized D.
      : 'gt u@- : 'gt0 2* 2* gt0 ;

      // ----------------------------------------------------------------------
      // 'ge / 'ge0  --  signed greater-or-equal: same sign re-normalization,
      // delegating to the shared ge0 helper above (inferred)
      // ----------------------------------------------------------------------
      : 'ge u@- : 'ge0 2* 2* ge0 ;

      // ----------------------------------------------------------------------
      // 'ule / le0 / lt0  --  unsigned less-or-equal, and the shared "D<=0" /
      // "D<0" helpers every remaining ordering comparison below reuses
      // (inferred)
      // ----------------------------------------------------------------------
      // Mirrors 'ugt/gt0/ge0's structure with the opposite sense: 'ule calls
      // u@- for D, then le0: '# 'true until' -- continues (falls into lt0
      // below) when D is nonzero; when D IS ZERO, jumps to 'true (equal counts
      // as "less or equal"). lt0: '# 'false -until' -- continues when D is
      // NEGATIVE, CALLing 'true (D<0, strictly less) and returning via this
      // word's own trailing ';'; when D is non-negative (and, having fallen
      // through le0's gate, known nonzero, so strictly positive), jumps to
      // 'false. Net effect: 'ule is true iff D<=0. lt0 alone (skipping le0's
      // equal-gate) is reused directly by 'ult below for strict "<": true iff
      // D<0.
      : 'ule u@- : le0 # 'true until
      : lt0 # 'false -until 'true ;

      // ----------------------------------------------------------------------
      // 'le / 'le0  --  signed less-or-equal: sign re-normalization, delegating
      // to the shared le0 helper above (inferred)
      // ----------------------------------------------------------------------
      : 'le u@- : 'le0 2* 2* le0 ;

      // ----------------------------------------------------------------------
      // 'lt / 'lt0  --  signed less-than: sign re-normalization, delegating to
      // the shared lt0 helper above (inferred)
      // ----------------------------------------------------------------------
      : 'lt u@- : 'lt0 2* 2* lt0 ;

      // ----------------------------------------------------------------------
      // 'ult / 'uge  --  unsigned strict less-than / greater-or-equal: delegate
      // straight to the shared helpers above with NO sign re-normalization,
      // since these test the raw unsigned difference directly (inferred)
      // ----------------------------------------------------------------------
      : 'ult u@- lt0 ; : 'uge u@- ge0 ;

      // ----------------------------------------------------------------------
      // '2* / 'u2/  --  multiply/divide by 2 (inferred)
      // ----------------------------------------------------------------------
      // The plain F18A '2*' (left shift) and '2/' (right shift, sign-preserving
      // on real hardware, though this bare single-step form is exposed under
      // the CVM's "unsigned" shift-right name).
      : '2* 2* ; : 'u2/ 2/ ;

      // ----------------------------------------------------------------------
      // '2/  --  signed divide by 2 (inferred)
      // ----------------------------------------------------------------------
      // '2* 2*' then '2/ 2/ 2/': the same "shift up 2, then shift down 3" net
      // -1 idiom as node 507's 'ssr and this file's own 'gt0/'ge0/'le0/'lt0 --
      // the extra up-then-down pair re-floods the correct sign bit for a
      // logically-16-bit value before the arithmetic right shift, so the sign
      // extends correctly across the 16/18-bit boundary.
      : '2/ 2* 2* 2/ 2/ 2/ ;

      // ----------------------------------------------------------------------
      // 'abs / 'negate / 'nop  --  absolute value (inferred)
      // ----------------------------------------------------------------------
      // '2* 2* 2/ 2/' re-floods the correct sign bit (net zero shift, purely
      // to refresh sign-extension) then '-if' tests it: continues (falls
      // through to 'negate) when NEGATIVE, executing 'inv 'inc' (two's-
      // complement negation) and returning; otherwise (non-negative) jumps to
      // 'then, which opens 'nop's empty body -- already positive, nothing to
      // do.
      : 'abs 2* 2* 2/ 2/ -if : 'negate inv 'inc ; : 'nop then ;

      // ----------------------------------------------------------------------
      // 'invert  --  bitwise NOT (inferred)
      // ----------------------------------------------------------------------
      // The plain F18A 'inv' opcode.
      : 'invert inv ;

      // ----------------------------------------------------------------------
      // 'xt  --  exchange T with this node's own register (t, held in A)
      // (Stefan's own description: "exchanges T with R" -- R here reads as this
      // node's own working register, the same loose usage as node 507's
      // 'leave' comment, not 507's own r)
      // ----------------------------------------------------------------------
      // 'a' pushes A (t) on top of T; 'over' copies the item now second-from-
      // top (the ORIGINAL T) back on top; 'a!' pops that copy into A. Tracing
      // the stack precisely: starting [S, T], after 'a' it's [S, T, A_old],
      // after 'over' [S, T, A_old, T], after 'a!' (A:=T, pop) it's [S, T,
      // A_old] -- so the new T is A_old and the new S is the original T, while
      // A itself now holds the original T. A genuine two-way swap of T and A.
      : 'xt a over a! ;

      // ----------------------------------------------------------------------
      // 'ldt  --  fetch this node's own register (t) onto T (Stefan's own
      // description: "loads R from T" -- read as "loads T from the register",
      // matching the code)
      // ----------------------------------------------------------------------
      // The plain F18A 'a' opcode: pushes A (t) without disturbing it.
      : 'ldt a ;

      // ----------------------------------------------------------------------
      // 'stt  --  store T into this node's own register (t), leaving T
      // unchanged (Stefan's own description: "stores R in T" -- read as "stores
      // the register from T", matching the code)
      // ----------------------------------------------------------------------
      // 'dup' keeps a copy of T on the stack for the caller; 'a!' stores the
      // other copy into A (t).
      : 'stt dup a! ;

      // ----------------------------------------------------------------------
      // 'bitcnt  --  count 1 bits (Stefan's own description) -- the classic
      // Kernighan popcount loop
      // ----------------------------------------------------------------------
      // 'dup dup xor' leaves [T, 0] (the same "always-0" idiom used throughout
      // this cluster); '>r' parks that 0 on R as the running bit-count. 'begin'
      // marks the loop's backward-branch target. 'if' tests T: continues
      // (enters the loop body) while T is NONZERO; jumps to 'then (exiting the
      // loop) once T reaches zero. Loop body: 'r> 'inc >r' increments the
      // parked count; 'dup 'dec and' computes T & (T-1) -- the standard trick
      // that clears exactly the lowest set bit -- replacing T with that result
      // for the next pass. '[ swap ]' is a COMPILE-TIME-only step (switches to
      // interpret mode, swaps the two pending compile-time control handles,
      // switches back): 'begin' and 'if' each pushed a handle, in that order,
      // so without the swap 'again' (which expects a 'begin'-style handle on
      // top) would grab 'if's forward-branch handle by mistake. After the
      // swap, 'again' correctly jumps back to 'begin', and the later 'then'
      // correctly resolves 'if's forward branch, closing a loop with a
      // mid-body conditional exit. Once T reaches 0, 'if' jumps to 'then' and
      // 'r>' pops the final count as the result.
      : 'bitcnt dup dup xor >r begin if r> 'inc >r dup 'dec and [ swap ] again then r> ;
      """;
}
