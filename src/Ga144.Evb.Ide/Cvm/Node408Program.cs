namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 408's resident F18 source -- CVM2's comparison node, supplied verbatim by Stefan on 2026-09-06
/// ("add node 408 to the assembler, disassembler and boot stream"), then EXTENDED the same day with
/// three more binary ops ("updated node 408. add more opcode to assembler and disassembler"). A BRAND
/// NEW coordinate for CVM2 (no CVM1 namesake at all, unlike 407/506/508 which all reuse a CVM1
/// coordinate for unrelated content).
///
/// <b>Where it hangs off the mesh -- node 407's own previously-UNANSWERED "1111"/LEFT branch.</b> This
/// source's own header, <c># 407 import</c> plus <c># left /b</c>, says node 408 imports node 407's
/// exports and reaches it over node 408's OWN left port. <see cref="Node407Program"/>'s own <c>n/main</c>
/// dispatch cascade (see that class's own remarks) already has FOUR branches carved out of its "11??"
/// prefix -- "1100" (falls to <c>ex</c>, node 407's own local <c>'lcall</c>/<c>'ljmp</c>), "1101" (relays
/// DOWN, <c>-d--</c>, still an open question per that class's own FLAGGED note), "1110" (relays RIGHT,
/// <c>r---</c>, the link to <see cref="Node406Program"/>), and "1111" (relays LEFT, <c>--l-</c>) -- and
/// that FOURTH branch was, until now, a "previously unsupplied further-relay branch" with nothing to
/// answer it, exactly the same shape of gap 508's own RIGHT-port branch was in before
/// <see cref="Node509Program"/> filled it. Node 408 is that answer: its own declared range,
/// <c>1111_????_????_????</c> (0xF000-0xFFFF), is EXACTLY node 407's own "1111" branch, and per the same
/// alternating-mirror local-port-naming pattern already confirmed for 407&lt;-&gt;507 ("down"/"down") and
/// 406&lt;-&gt;407 ("right"/"right"), node 407 and node 408 use the SAME local name, "left", on both ends
/// of this link too -- node 407's own doc comments have been updated alongside this file to record that
/// its "1111" branch is no longer unanswered. No changes were needed to <see cref="Node407Program"/>'s
/// own SOURCE for this -- the relay was already there, just unwired to anything.
///
/// <b><c>c/main</c>'s own three-way dispatch cascade.</b> Structurally the same shape as
/// <see cref="Node406Program"/>'s own <c>y/main</c> (leaf node, imports its relay parent's
/// register/stack helpers, reads two words over <c>@b</c> into a held first word, then a cascade of
/// <c>-if</c>/<c>2*</c> tests consuming the already-known "1111" prefix two bits at a time):
/// <list type="bullet">
/// <item><c>1111_1???_????_????</c> (0xF800-0xFFFF) -- "register file", <c>r&gt; r--- ;</c>, the SAME
/// shape as <see cref="Node406Program"/>'s own unanswered "1110_1???" ("more arithmetic") branch: a
/// reserved, not-yet-implemented slot with no tick-prefixed word to hang a CVM mnemonic off of, so it is
/// NOT wired into <see cref="Services.CvmAssemblyLanguage"/> below, matching that same precedent of
/// leaving an unnamed reserved branch alone rather than guessing at a future design.</item>
/// <item><c>1111_01??_????_????</c> (0xF400-0xF7FF) -- "binary comparison": pops a second operand off the
/// CVM data stack via <c>c/pop</c> before falling into the shared tail, for ops whose own stack comment
/// takes two inputs (<c>( xy-f)</c>): <c>'eq</c>, <c>'ne</c>, <c>'lt</c>, and (added 2026-09-06)
/// <c>'ge</c>, <c>'gt</c>, <c>'le</c>.</item>
/// <item><c>1111_00??_????_????</c> (0xF000-0xF3FF) -- "unary comparison": falls straight into the shared
/// tail with NO extra pop, for ops whose own stack comment takes one input (<c>( x-f)</c>):
/// <c>'true</c>, <c>'false</c>, <c>'eq0</c>, <c>'ne0</c>, <c>'lt0</c>, <c>'le0</c>, <c>'gt0</c>,
/// <c>'ge0</c>. (<c>'true</c>/<c>'false</c> ignore their own operand entirely -- see their own bodies
/// below -- but still dispatch through this same unary branch, since neither needs a second stack pop.)
/// </item>
/// </list>
/// The shared tail, <c>c/r@ ex c/r! ;</c>, is the SAME "read the other operand from the shared register,
/// jump to whichever named op the tag+address selected, mask/write the result back" idiom
/// <see cref="Node406Program"/>'s own <c>y/r@ ex ... y/r! ;</c> tail uses (minus the 16-bit mask, since
/// none of these comparison ops overflows past 16 bits the way the arithmetic/shift ops could).
///
/// <b>The tag derivation, 0xF000/0xF400 (register-file reserved at 0xF800) -- straight from this
/// source's own trailing bit-pattern comments,</b> a 6-bit-tag/10-bit-address split matching
/// <see cref="Node406Program"/>'s own 0xE000/0xE400 scheme (and <see cref="Node509Program"/>'s own
/// 0xB000) exactly. Node 408's own RAM is only 64 words, so 0xF000-0xF03F/0xF400-0xF43F have no LIVE
/// collision with anything else wired in <see cref="Services.CvmAssemblyLanguage"/>. NOTE (not this
/// toolchain's concern to resolve, just to flag, exactly like node 406's own analogous note): this same
/// numeric range (0xF000-0xFFFF) is also <see cref="CvmInstructionSet"/>'s own purely-historical
/// "register w" comment range, from CVM1's old, permanently-orphaned node-407 register-w/port-op family
/// (<c>xpt</c>/<c>out</c>/<c>in</c>/<c>ldhi</c>/<c>ldlo</c>/<c>sthi</c>/<c>stlo</c>, deleted 2026-09-01,
/// with NO <see cref="Services.CvmAssemblyLanguage.NodeSymbolByMnemonic"/> entry any more) -- this
/// overlaps node 408's new tags only in COMMENT TEXT, never in any live wiring. NOT YET CONFIRMED ON REAL
/// HARDWARE (2026-09-06).
///
/// <b>Fourteen named comparison ops -- ALL FOURTEEN repoint pre-existing, previously-orphaned CVM
/// mnemonics, none are genuinely new.</b> Per "only update existing opcodes where possible": every one
/// of node 408's own tick-prefixed words (<c>'true</c>, <c>'false</c>, <c>'eq</c>, <c>'ne0</c>, <c>'ne</c>,
/// <c>'eq0</c>, <c>'ge</c>, <c>'lt0</c>, <c>'lt</c>, <c>'ge0</c>, <c>'gt</c>, <c>'le0</c>, <c>'le</c>,
/// <c>'gt0</c>) matches, BY NAME, one of node 508's old, permanently-orphaned CVM1 27-comparison-op
/// mnemonics exactly (<see cref="CvmInstructionSet.TrueMnemonic"/>, <see cref="CvmInstructionSet.FalseMnemonic"/>,
/// <see cref="CvmInstructionSet.EqualMnemonic"/>, <see cref="CvmInstructionSet.NotEqualToZeroMnemonic"/>,
/// <see cref="CvmInstructionSet.NotEqualMnemonic"/>, <see cref="CvmInstructionSet.EqualToZeroMnemonic"/>,
/// <see cref="CvmInstructionSet.GreaterOrEqualMnemonic"/>, <see cref="CvmInstructionSet.LessThanZeroMnemonic"/>,
/// <see cref="CvmInstructionSet.LessThanMnemonic"/>, <see cref="CvmInstructionSet.GreaterOrEqualToZeroMnemonic"/>,
/// <see cref="CvmInstructionSet.GreaterThanMnemonic"/>, <see cref="CvmInstructionSet.LessOrEqualToZeroMnemonic"/>,
/// <see cref="CvmInstructionSet.LessOrEqualMnemonic"/>, <see cref="CvmInstructionSet.GreaterThanZeroMnemonic"/>)
/// -- so all fourteen are REPOINTED, in <see cref="Services.CvmAssemblyLanguage"/>'s own
/// <c>NodeSymbolByMnemonic</c>, from their old dead <see cref="Node508Program.Coordinate"/>/tag-0xE800
/// pairing (node 508's REAL CVM2 source never defined any of these 27 F18 symbols, so they have sat
/// permanently orphaned since 2026-09-04) to node 408's own live compile instead. Unlike
/// <see cref="Node509Program"/>'s own repointing (eight of nine, one genuinely new) or
/// <see cref="Node406Program"/>'s (eight of twelve, four genuinely new), node 408 needs NO new
/// <see cref="CvmInstructionSet"/> mnemonic constants or <see cref="CvmInstructionSet.Instructions"/>
/// entries at all, even after this extension -- every mnemonic it needs already existed in that table
/// from the start, just newly resolvable. This also leaves node 508's old 27-mnemonic family with only
/// EIGHT still-permanently-orphaned entries (<c>ugt</c>, <c>ule</c>, <c>ult</c>, <c>uge</c>,
/// <c>negate</c>, <c>xt</c>, <c>ldt</c>, <c>stt</c> -- none of which node 408 defines a same-named word
/// for) out of the 22 that were still orphaned after <see cref="Node509Program"/>'s own repointing.
///
/// <b>Binary vs. unary classification, straight from this source's own trailing summary comment:</b>
/// <c>'eq</c>/<c>'ne</c>/<c>'lt</c>/<c>'le</c>/<c>'gt</c>/<c>'ge</c> are the six BINARY ops (<c>( xy-f)</c>,
/// reached via the "1111_01??" branch, needing <c>c/pop</c> for the second operand); <c>'true</c>/
/// <c>'false</c>/<c>'eq0</c>/<c>'ne0</c>/<c>'lt0</c>/<c>'le0</c>/<c>'gt0</c>/<c>'ge0</c> are the eight
/// UNARY ops (<c>( -f)</c> or <c>( x-f)</c>, reached via the "1111_00??" branch, no extra pop). This
/// matches the note above on node 508's OLD 27-op family: that family also had both "binary" (<c>eq</c>,
/// <c>ne</c>, <c>gt</c>, <c>ge</c>, <c>le</c>, ...) and unary/zero-comparison (<c>eq0</c>, <c>ne0</c>,
/// ...) shaped ops, so nothing about the arity split is new here, just which live node now answers each
/// one.
///
/// <b>FLAGGED, not silently investigated further (2026-09-06) -- TWO apparent polarity swaps in the
/// shared-tail fall-throughs, confirmed against this source's OWN unambiguous prior definition of
/// <c>'lt</c>.</b> Six of node 408's fourteen words have no trailing <c>;</c> of their own and instead
/// fall straight through, by plain address adjacency, into whatever colon-definition happens to follow
/// them in this source -- the SAME cross-definition fall-through idiom already established and confirmed
/// working in <see cref="Node509Program"/> (<c>'parity</c> into <c>'odd</c>) and
/// <see cref="Node406Program"/> (<c>'sub</c> into <c>'rsb</c>, etc.), and already flagged once for
/// <c>'eq</c>/<c>'ne</c> in this same file (see below). Compiling this revision confirms the physical
/// adjacency: <c>'ge</c> (0x0029) falls into <c>'lt0</c> (0x002A); <c>'lt</c> (0x002D) falls into
/// <c>'ge0</c> (0x002E); <c>'gt</c> (0x0030) falls into <c>'le0</c> (0x0031); <c>'le</c> (0x0034) falls
/// into <c>'gt0</c> (0x0035). <c>c/cmp</c>'s own bare computation (<c>inv . + 1 . +</c>, the exact same
/// sequence <see cref="Node406Program"/>'s own <c>'rsb</c> uses, there documented as "subtract r from
/// parameter") gives some value <c>z</c>; the PREVIOUS revision of this file defined <c>'lt</c> as the
/// fully self-contained, unambiguous <c>c/cmp 'lt0 ;</c> (call <c>c/cmp</c>, call <c>'lt0</c>, return) --
/// since that older <c>'lt</c> must, by its own name, compute "true if x &lt; y", this pins down
/// <c>z</c>'s own meaning exactly: <c>z &lt; 0</c> iff <c>x &lt; y</c>. Applying that same fact to the
/// four NEW fall-throughs above: <c>'ge</c> (falling into <c>'lt0</c>, i.e. testing <c>z &lt; 0</c>)
/// computes "x &lt; y" -- <c>'lt</c>'s own stated meaning, not <c>'ge</c>'s; <c>'lt</c> ITSELF now falls
/// into <c>'ge0</c> (testing <c>z &gt;= 0</c>) and so computes "x &gt;= y" -- <c>'ge</c>'s meaning; by
/// the same reasoning <c>'gt</c> (into <c>'le0</c>) computes <c>'le</c>'s meaning, and <c>'le</c> (into
/// <c>'gt0</c>) computes <c>'gt</c>'s meaning. In short: <c>'ge</c>&lt;-&gt;<c>'lt</c> and
/// <c>'gt</c>&lt;-&gt;<c>'le</c> each appear to compute EACH OTHER's stated meaning, a clean, systematic
/// swap rather than a random mistake -- which raises the possibility this is deliberate (a convention
/// this reviewer is not aware of) rather than an error, but it was not silently "corrected" either way.
/// Flagged exactly the way node 407's own "1101"/<c>-d--</c> reversion was flagged: wired in exactly as
/// supplied, unmodified, with this note left for Stefan to confirm (or correct) once tested against real
/// hardware or the F18 interpreter.
///
/// <b>FLAGGED, not silently investigated further (2026-09-06) -- <c>'eq</c>'s own fall-through into
/// <c>'ne0</c>'s body.</b> <c>'eq</c> has no trailing <c>;</c> of its own: its one-word body
/// (<c>xor</c>) falls straight through into <c>'ne0</c>'s own compiled code (<c>if 'true ; then 'false
/// ;</c>). Taken at face value, that shared tail reads as "jump to the FALSE case when the tested value
/// is zero, otherwise fall through to the TRUE case" (the standard Forth <c>IF ... THEN</c> compiled
/// shape) -- which for <c>'ne0</c> alone gives exactly its own stated meaning ("r is true if x != 0": a
/// nonzero x falls through to <c>'true</c>; a zero x skips to <c>'false</c>"). But <c>'eq</c> feeds
/// <c>x XOR y</c> into that SAME test, which by the identical logic would return true when <c>x XOR y</c>
/// is NONZERO -- i.e. when <c>x != y</c> -- the opposite of <c>'eq</c>'s own stated meaning ("r is true
/// if x == y") in this source's own trailing comment block; and since <c>'ne</c> is defined as <c>'eq</c>
/// immediately followed by <c>c/not</c> (a plain <c>1 xor</c> logical invert), the same swap would carry
/// through to <c>'ne</c> as well. Unlike the <c>'ge</c>/<c>'lt</c>/<c>'gt</c>/<c>'le</c> note above, this
/// one has no unambiguous prior self-contained definition to cross-check against, so it remains slightly
/// less certain -- it may simply be a misreading of this project's own compile-time control-flow
/// mechanics on this reviewer's part -- but the SAME systematic-swap pattern showing up twice, in two
/// independent groups of ops on the same node, makes it look less like a one-off misreading and more
/// like either a real, consistent bug or a real, consistent (if unfamiliar) convention. <c>'eq</c>/
/// <c>'ne</c> are wired in exactly as supplied, unmodified.
///
/// <b>Verification.</b> Compiled standalone against this project's real <c>Compiler/F18Compiler.cs</c>,
/// importing <see cref="Node407Program"/>'s own <c>n/</c>-prefixed exports: 0 errors, 55/64 words used,
/// entry point <c>c/main</c> at 0x000E. Every symbol resolves: <c>c/r@</c>=0x0000, <c>c/r!</c>=0x0004,
/// <c>c/pop</c>=0x0006, <c>c/push</c>=0x000A, <c>c/leave</c>=0x000C, <c>c/main</c>=0x000E,
/// <c>'true</c>=0x001A, <c>'false</c>=0x001C, <c>'eq</c>=0x001D, <c>'ne0</c>=0x001E, <c>'ne</c>=0x0021,
/// <c>c/not</c>=0x0022, <c>'eq0</c>=0x0024, <c>c/cmp</c>=0x0026, <c>c/inc</c>=0x0027, <c>'ge</c>=0x0029,
/// <c>'lt0</c>=0x002A, <c>'lt</c>=0x002D, <c>'ge0</c>=0x002E, <c>'gt</c>=0x0030, <c>'le0</c>=0x0031,
/// <c>'le</c>=0x0034, <c>'gt0</c>=0x0035.
/// </summary>
internal static class Node408Program
{
  /// <summary>The node this program is always deployed to -- CVM2's comparison node.</summary>
  public const int Coordinate = 408;

  /// <summary>
  /// Node 408's full resident F18 source. See the class remarks for the <c>c/main</c> dispatch cascade,
  /// the tag derivation, the repointed mnemonics, and the two FLAGGED fall-through polarity notes.
  /// <b>RE-SYNCED 2026-09-09</b> against Stefan's own <c>workspace.yaml</c> project export as part of the
  /// opcode/assembler-vs-node reconciliation audit -- this is the exact gap that prompted the audit
  /// ("there are 'ugt' opcodes in the nodes"): the PRIOR "SYNCED, 2026-09-08" copy here was missing a
  /// whole helper word, <c>c/u</c> (<c>a xor over a xor over ;</c> -- XORs both operands with register
  /// <c>a</c>'s own held toggle-bit constant, converting a signed comparison into its unsigned equivalent
  /// by flipping each operand's own sign bit before the shared <c>c/cmp</c> subtraction runs), and the
  /// four unsigned comparison words that call it -- <c>'ugt</c>, <c>'ule</c>, <c>'ult</c>, <c>'uge</c> --
  /// were entirely absent. <c>'ugt</c>/<c>'ult</c> share <c>c/u</c>+<c>c/cmp</c> the same fall-through
  /// shape the four signed comparisons already use; <c>'uge</c>/<c>'ule</c> likewise. The register
  /// initializer also changed from <c>\# 0 /a</c> to <c>\# 0x8000 /a</c> -- <c>a</c> now holds the sign-bit
  /// toggle mask <c>c/u</c> needs, not zero. These four mnemonics were previously permanently orphaned
  /// (node 508's own real CVM2 source never defined them, only its retired CVM1 27-op family named them)
  /// -- they now repoint to this node's own live compile exactly like the other ten comparison ops. No
  /// other content differs from the 2026-09-08 sync; every other specific claim in the class remarks above
  /// predates this fix and should be treated as stale until re-confirmed.
  /// </summary>
  public const string Source = """
      ( CVM2 node 408. comparison, 1111_????_????_???? )
      # 407 import
      # 0 org
      entry c/main
      # 0x8000 /a // toggle bit for unsigned operation
      # left /b
      : c/r@ ( -w) A[ n/r@ ]] lit !b A[ !p ]] lit !b @b ;
      : c/r! ( w) A[ @p n/r! ]] lit !b !b ;
      : c/pop ( -w) A[ n/pop ]] lit !b A[ !p ]] lit !b @b ;
      : c/u ( xy-yx) // modify parameter for unsigned operation
        a xor over a xor over ;
      : c/push ( w) A[ @p n/push ]] lit !b !b ;
      : c/leave A[ n/leave ; ]] lit !b
      : c/main # c/leave lit >r A[ !p 2* !p ]] lit !b @b >r @b
        -if // 1111_1???_????_????
          // register file
          r> r--- ;
        then // 1111_0???_????_????
        2* -if // 1111_01??_????_????
          // binary comparison
          // 'eq
          // 'ne
          // 'lt
          // 'le
          // 'gt
          // 'ge
          // 'ult
          // 'ule
          // 'ugt
          // 'uge
          c/pop
        then // 1111_00??_????_????
          // unary comparison
          // 'true
          // 'false
          // 'eq0
          // 'ne0
          // 'lt0
          // 'ge0
          // 'le0
          // 'gt0
        c/r@ ex c/r! ;
      : 'true ( -f) 1 ;
      : 'false ( -f) dup xor ;
      : 'eq ( xy-f) xor
      : 'ne0 ( x-f) if 'true ; then 'false ;
      : 'ne ( xy-f) 'eq
      : c/not ( f-f) 1 xor ;
      : 'eq0 ( x-f) 'ne0 c/not ;
      : c/cmp ( xy-z) inv . +
      : c/inc ( x-y) 1 . + ;
      : 'uge ( xy-f) c/u
      : 'ge ( xy-f) c/cmp
      : 'lt0 ( x-f) 2* 2* # 'true -until 'false ;
      : 'ult ( xy-f) c/u
      : 'lt ( xy-f) c/cmp
      : 'ge0 ( x-f) 'lt0 c/not ;
      : 'ugt ( xy-f) c/u
      : 'gt ( xy-f) c/cmp
      : 'le0 ( x-f) 2* 2* # 'true -until 'eq0 ;
      : 'ule ( xy-f) c/u
      : 'le ( xy-f) c/cmp
      : 'gt0 ( x-f) 'le0 c/not ;
      (
        1111_01??_????_???? binary comparison
        1111_00??_????_???? unary comparison
        'true unary. store true constant in r
        'false unary. store false constant in r
        'eq binary. r is true if x == y
        'ne0 unary. r is true if x != 0
        'ne binary. r is true if x != y
        'eq0 unary. r is true if x == 0
        'lt0 unary. r is true if x < 0
        'le0 unary. r is true if x <= 0
        'gt0 unary. r is true if x > 0
        'ge0 unary. r is true if x >= 0
        'lt binary. r is true if x < y
        'le binary. r is true if x <= y
        'gt binary. r is true if x > y
        'ge binary. r is true if x >= y
        'ult unsigned binary. r is true if x < y
        'ule unsigned binary. r is true if x <= y
        'ugt unsigned binary. r is true if x > y
        'uge unsigned binary. r is true if x >= y
      )
      """;
}