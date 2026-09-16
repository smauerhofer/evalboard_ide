namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 501's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the thirteenth of 17 new
/// nodes in this batch, and the last link in the arithmetic control chain <see cref="Node503Program"/>
/// started (503 controls 502, 502 controls this node). Per its own header: "CVM2 node 501. VM 32 bit
/// floatingpoint stage 5b node. implement add and multiply" -- the mantissa-arithmetic counterpart to
/// <see cref="Node401Program"/>'s own stage 5a (which handled the comparison/control side of the same
/// numbering row).
///
/// <b>Likely resolves the long-open "<c>.</c>" mystery, with the same confidence level already given to
/// <c>+*</c> in <see cref="Node404Program"/>'s own remarks.</b> <c>fp5b/add</c>'s body --
/// <c>clc / . + &gt;r / . +</c> -- is the first place in this batch where the bare <c>.</c> token sits
/// directly between a carry-clearing instruction and the additions that use that carry, with fully
/// unambiguous surrounding arithmetic. <c>.</c> is the real, documented F18A opcode for a no-op cycle
/// (a genuine "do nothing to the stack" instruction, distinct from a comment) -- which matches EVERY
/// prior observation in this project that <c>.</c> never changes the traced stack effect, and additionally
/// explains WHY it recurs specifically next to carry-sensitive operations: this async, self-timed
/// architecture most likely needs one settle cycle between setting the carry flag (<c>clc</c>, or the
/// forced-overflow "set carry" trick below) and consuming it in a chained <c>+</c>. This reading is offered
/// with the same "identified, not guessed" confidence as <c>+*</c>, though -- as with <c>+*</c> -- it isn't
/// something Stefan states explicitly in this paste. It isn't retroactively applied to the <c>.</c>/<c>..</c>
/// occurrences already documented in <see cref="Node302Program"/>, <see cref="Node504Program"/>, etc.,
/// since those files were already delivered, but it is consistent with (and now explains) every one of
/// them.
///
/// <b><c>clc</c> is used again with no <c># import</c>, for a second time in this batch</b> (the first was
/// <see cref="Node404Program"/>) -- two independent nodes using it bare now makes "native per-node F18A
/// primitive" a much stronger reading than "specific to node 405's own compiled program," though still not
/// confirmed outright.
///
/// <b><c>fp5b/sub</c> manually forces the carry flag to 1 instead of calling a <c>sec</c>-style primitive.</b>
/// <c>dup dup xor inv dup . + drop</c> takes the top-of-stack value (L2), XORs it with itself (0), inverts
/// it (0xFFFF), adds that to itself (guaranteed 18-bit overflow, forcing carry=1), and drops the resulting
/// junk sum -- leaving L2 untouched underneath but the carry flag now set, ready for the standard two's-
/// complement subtraction identity <c>A - B = A + NOT(B) + carry(1)</c> that the rest of the word carries
/// out (<c>inv . + &gt;r</c> for the low limb, <c>inv . +</c> for the high limb using the borrow chained
/// from the low subtraction). No <c>sec</c>-shaped primitive appears anywhere in this batch so far; this
/// workaround suggests one may not exist, though that isn't stated outright and isn't asserted here.
///
/// <b><c>fp5b/shr2</c> is a fully traced, clean confirmation of the <c>+*</c> "shift, not multiply" trick</b>
/// (first identified in <see cref="Node404Program"/>'s own <c>fp8/reduce</c>): pushing a fixed <c>0</c> as
/// the multiplier-bit source before <c>. +*</c> guarantees no add occurs, leaving a pure one-bit right
/// shift of the combined H2:L2 double-word (register <c>A</c> holding the low limb) -- exactly what
/// <see cref="Node502Program"/>'s own <c>fp4b/adjust</c> needs to align a smaller-exponent operand's
/// mantissa before addition.
///
/// <b><c>fp5b/mul</c> implements a full 18x18-bit unsigned multiply via classic split cross-product
/// decomposition</b> (low cross <c>z0 = high18(L1*L2)</c>, <c>z2 = H1*H2</c> which fits in 18 bits per the
/// header's own <c>0x1ff*0x1ff &lt; 0x40000</c> note, then the two 9-bit-limb cross terms <c>p1</c>/<c>p2</c>
/// via the shared <c>fp5b/m9</c> helper, combined with two calls to this file's own <c>fp5b/add</c> and a
/// final 7-step <c>. +*</c> right-shift-by-8 to reduce the 36-bit intermediate back to the pipeline's
/// working precision). This routine is unusually thoroughly self-documented by Stefan's own inline
/// comments (more so than any other node processed in this batch) -- reproduced verbatim and taken as
/// authoritative rather than independently re-derived step by step. The trailing <c>0x3ff and</c> masks the
/// final high limb to 10 bits; not analyzed further here.
///
/// <b>FLAGGED: <c>--l-</c> reappears for a third time (after two sightings in
/// <see cref="Node401Program"/>), and here it demonstrably does NOT change the data stack's depth.</b>
/// <c>fp5b/main</c> reads all four <c>H1 H2 L1 L2</c> values, calls <c>--l-</c>, then immediately does
/// <c>up a! ! ! ! !</c> -- FOUR explicit port writes that account for all four original values (matching
/// the header's own "always sends 4 words"). This is a clean data point that <c>--l-</c> here leaves the
/// stack untouched, consistent with a true hardware side-channel relay independent of the Forth stack (the
/// ORIGINAL reading offered when this notation was first seen, before <see cref="Node502Program"/>'s own
/// <c>r---</c> -- which DID appear to reduce two pending values to one -- suggested a "receive and execute"
/// alternative). <b>These two pieces of evidence are now in tension</b>: either the notation behaves
/// differently depending on context, or one of the stack-depth comments involved (here, in node 502, or in
/// node 401) doesn't precisely reflect what the hardware does. Not resolved here; now spans three nodes
/// (401, 502, 501) and is flagged as worth confirming with Stefan directly rather than guessed at further.
///
/// <b>Port-word relativity, once more consistent with (not contradicting) the existing finding:</b> this
/// node's <c>up</c> reaches node 601 (the header's own "always sends 4 words to node 601"), the same
/// relative sense already seen for 502's <c>up</c>-&gt;602 and 503/504's <c>up</c>-ish neighbors -- but
/// opposite to node 401's own <c>up</c>, which read FROM node 301 (a lower-numbered neighbor). Both are
/// consistent with the already-established "same word, different absolute direction per node" finding, not
/// a new contradiction.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired into
/// the CVM instruction set</b> -- same reasoning as its siblings; this branch (503-&gt;502-&gt;501) is now
/// fully pasted, but its own dispatch mechanism (how 503's remote-sent operation address actually reaches
/// and executes on 502, and by extension how any equivalent signal reaches 501) is still not confirmed, so
/// wiring stays deferred pending node 601 and, likely, direct confirmation from Stefan.
/// </summary>
internal static class Node501Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 5b (implements 36-bit add/subtract/multiply, end of the arithmetic control chain), added 2026-09-16.</summary>
  public const int Coordinate = 501;

  /// <summary>
  /// Node 501's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The unresolved
  /// <c>--l-</c> token noted in this class's own remarks above is reproduced exactly as pasted, not
  /// corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 501. VM 32 bit floatingpoint stage 5b node. implement add and multiply )
      (
        calculates result for add and multiply.

        in: HHLL      {from node 401}

        always sends 4 words to node 601:

          add: L H x x
          sub: L H x x
          mul: L H 0 x
          div: L2 L1 H2 H1
      )

      entry fp5b/main
      # down /b
      # up /a

      # 0 org


      +cy

      : fp5b/add ( H1 H2 L1 L2 - H L )
        clc                 // carry = 0

        . + >r              // L = L1+L2, carry-out retained
                            // H1 H2 / L

        . +                 // H = H1+H2+carry
                            // H / L

        r>                  // H L
      ;

      : fp5b/sub ( H1 H2 L1 L2 - H L )

        // force carry = 1
        dup dup xor inv dup . + drop

        // L = L1 - L2
        inv . + >r

        // H = H1 - H2 - borrow
        inv . +

        r>
      ;

      -cy


      : fp5b/shr2 ( H1 H2 L1 L2 - H1 H2' L1 L2' )
        a!                    // A = L2
        >r                    // save L1
        0 over                // H1 H2 0 H2
        . +*                   // S=0, T:A = H2:L2 >> 1
        >r drop drop           // save H2', remove 0 and old H2
        r> r> a               // H1 H2' L1 L2'
      ;


      : fp5b/m9 ( h l - H L )
        a! 17. >r
        dup dup xor
        begin . +* unext
        >r drop r> a
      ;


      : fp5b/mul ( H1 H2 L1 L2 - ... 0 H L )

        // ------------------------------------------------
        // z0 = high18(L1*L2), unsigned 18x18
        // ------------------------------------------------

        over over                 // H1 H2 L1 L2 L1 L2

        dup >r a!                 // save L2, A=L2
        17. >r
        dup dup xor               // L1 L1 0
        begin . +* unext          // signed L1*unsigned L2

        // If L1 has bit17 set, +* interpreted it as negative.
        // unsigned product therefore needs +L2 in high word.
        over -if
          drop
          r> . +
        else
          drop
          r> drop
        then

        >r drop r>                // H1 H2 L1 L2 z0
        >r                        // / z0


        // ------------------------------------------------
        // z2 = H1*H2
        //
        // This fits completely in 18 bits because
        // 0x1ff * 0x1ff < 0x40000.
        // Leave it temporarily in A.
        // ------------------------------------------------

        >r >r                     // save L2,L1
        over over fp5b/m9         // H1 H2 0 z2
        drop drop                 // A still contains z2
        r> r>                     // restore L1,L2

        a >r                      // / z2 z0


        // ------------------------------------------------
        // reorder
        //
        // H1 H2 L1 L2
        // -> H1 L2 H2 L1
        // ------------------------------------------------

        a! >r a r>                // swap L1,L2
        >r
        a! >r a r>                // swap H2,L2
        r>


        // ------------------------------------------------
        // cross products
        //
        // p2 = H2*L1
        // p1 = H1*L2
        // ------------------------------------------------

        fp5b/m9                   // H1 L2 p2H p2L
        >r >r                     // save p2

        fp5b/m9                   // p1H p1L
        r> r>                     // p1H p1L p2H p2L


        // reorder for 36-bit fp5b/add:
        //
        // p1H p2H p1L p2L
        //
        >r
        a! >r a r>
        r>

        fp5b/add                  // crossH crossL


        // ------------------------------------------------
        // add:
        //
        //     z2 << 18
        //   + z0
        //
        // R has: z2 z0
        // ------------------------------------------------

        r> r>                     // crossH crossL z2 z0

        >r
        a! >r a r>
        r>                        // crossH z2 crossL z0

        fp5b/add                  // C_H C_L


        // ------------------------------------------------
        // result = C >> 8
        //
        // T:A is shifted arithmetically by +*, but the low
        // 28 bits are correct. Mask the sign-fill from H.
        // ------------------------------------------------

        a!                        // A=C_L
        7. >r
        0 over                    // C_H 0 C_H
        begin . +* unext          // shift T:A eight times

        0x3ff and                // ... 0 H
        a                        // ... 0 H L
      ;


      : fp5b/main
        @b @b @b @b
        ( H1 H2 L1 L2 )

        --l-

        up a!
        ! ! ! !                  // always send four words

        fp5b/main
      ;
      """;
}
