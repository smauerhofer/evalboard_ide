namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 404's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the sixth of 17 new nodes
/// in this batch (see <see cref="Node305Program"/> through <see cref="Node301Program"/> for the first
/// five, the 301-305 row). Per its own header: "CVM2 node 404. VM 32 bit floatingpoint stage 8 node.
/// rounding" -- final round-to-nearest-even, reducing the extended 27-bit mantissa (<c>H</c>/<c>L</c>)
/// back down to binary32's 24-bit mantissa (<c>h</c>/<c>l</c>). <b>The jump in stage numbers (stage 5 was
/// node 301; this is stage 8)</b> means stages 6 and 7 exist somewhere in the rest of Stefan's 17-node
/// batch, not yet pasted/identified -- not guessed at here.
///
/// <b>FLAGGED: this node's own port-direction words contradict the "up"/"down" meaning established by
/// nodes 301/302/303, reinforcing (and extending to a THIRD direction) the working hypothesis already
/// raised in <see cref="Node302Program"/>'s own remarks that these words are per-node local port aliases,
/// not global compass directions.</b> Nodes 301/302/303 each used <c>up</c> to reach the node one row
/// HIGHER at the same column (301-&gt;401, 302-&gt;402, 303-&gt;403). Node 404's own header says its
/// <c># up /a</c> link reaches node 304 -- one row LOWER, not higher -- while its <c># down /b</c> link
/// reaches node 504, one row higher. So within this one node, <c>up</c>/<c>down</c> are still each
/// other's opposite (self-consistent), but which physical direction each word names has flipped relative
/// to every earlier node in this pipeline. Reproduced exactly as pasted; not treated as an error.
///
/// <b>New, previously-unseen syntax in this project, flagged rather than resolved:</b>
/// <list type="bullet">
/// <item><c>+cy</c> (immediately before <c>fp8/round</c>'s own colon definition) and <c>-cy</c>
/// (immediately after it) are bare top-level words, outside any colon definition -- not a runtime F18
/// instruction shape this project has seen before. Most likely a compile-time directive of some kind
/// (perhaps toggling a carry-aware compilation mode for the one word they bracket), but this is a guess,
/// not a confirmed reading.</item>
/// <item><c>fp8/round</c> calls <c>clc</c> (clear carry) directly, with NO <c># import</c> directive
/// anywhere in this source. Every other use of <c>clc</c>/<c>sec</c>/<c>stc</c>/etc. in this project has
/// been on node 405 itself (CVM2's dedicated "carry/multiword arithmetic" node, reached only via an
/// import from node 406). Since this node cannot import from 405 without a <c># 405 import</c> line it
/// doesn't have, either <c>clc</c> is actually a NATIVE per-node F18 primitive (real ALU carry-flag
/// hardware every node has, with node 405 simply being the first place this project happened to observe
/// it being used) rather than something exclusive to node 405's own compiled program, or something else
/// is going on. Not resolved here -- flagged for Stefan to confirm.</item>
/// <item>The bare <c>.</c> token (already seen in <see cref="Node302Program"/>'s <c>fp4/sub</c>/
/// <c>fp4/inc</c>) reappears here too, always in the same <c>N . +</c> shape, and always right after a
/// <c>clc</c>: <c>clc / 8 . + &gt;r / 0 . +</c> reads like a textbook add-with-carry idiom (clear carry;
/// add 8 to the low word, capturing the carry out; add 0 -- i.e. just the carry -- into the high word).
/// This is consistent with (and reinforces) the same guess offered in <see cref="Node302Program"/>'s own
/// remarks, still not a confirmed reading of what <c>.</c> itself does.</item>
/// <item><c>+*</c> (in <c>fp8/reduce</c>: <c>0 over . +*</c>) is very likely the well-known real F18/
/// arrayForth "multiply step" primitive (conditionally add-and-shift a double-length register pair one
/// bit right) -- not a mystery token like the others, just not previously used in this project. Its use
/// here for a plain "cross-word right shift" (per the source's own inline comment) rather than for actual
/// multiplication is a well-known trick with that instruction, which lends confidence this reading is
/// right.</item>
/// </list>
///
/// <c>fp8/round</c>, <c>fp8/fix</c>, and <c>fp8/reduce</c> are each fully self-contained (every branch
/// ends in its own <c>;</c>) -- unlike several earlier nodes in this batch, nothing here falls through
/// from one colon definition into the next. Like this source has no <c># import</c> directive at all
/// (same as its five siblings so far), and <c>fp8/main</c> tail-calls itself as a continuous service loop
/// (matching <see cref="Node303Program"/>'s and <see cref="Node301Program"/>'s own pattern).
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- same reasoning as its siblings: no dispatched opcode of its own,
/// and this pipeline's full shape (including the still-missing stage 6/7 nodes) isn't known yet.
/// </summary>
internal static class Node404Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 8 (round-to-nearest-even, reduce to binary32), added 2026-09-16.</summary>
  public const int Coordinate = 404;

  /// <summary>
  /// Node 404's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The <c>+cy</c>/
  /// <c>-cy</c>, bare <c>.</c>, and other unresolved tokens noted in this class's own remarks above are
  /// reproduced exactly as pasted, not corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 404. VM 32 bit floatingpoint stage 8 node. rounding )
      (
        performs final round-to-nearest-even and reduces
        the extended 27-bit mantissa to binary32 precision.

        in:  sehl       {from node 504}
        out: sehl       {to node 304}

        input mantissa:
          H = high 9+ bits
          L = low 18 bits

          L bit3 = retained LSB
          L bit2 = guard
          L bit1 = round
          L bit0 = sticky

        output mantissa:
          h = high 8 bits, explicit hidden bit in bit7
          l = low 16 bits
      )

      entry fp8/main
      # up /a
      # down /b

      # 0 org


      +cy

      : fp8/round ( E H L - E H' L' )
        // round-to-nearest-even:
        //
        // guard && (round || sticky || retained LSB)

        dup 4 and
        if
          drop

          dup 0xb and
          if
            drop

            // one final-precision ULP = 8 in extended format

            clc
            8 . + >r            // L += 8
            0 . +               // propagate carry into H
            r>
            ;
          then
          drop
          ;
        then
        drop
      ;

      -cy


      : fp8/fix ( E H L - E' H' L' )

        // ------------------------------------------------
        // Rounding overflow:
        //
        //   1.111... -> 10.000...
        //
        // H=0x200 means increment exponent and restore
        // normalized extended mantissa 0x100:0.
        // ------------------------------------------------

        .. over 0x200 and
        if
          drop

          drop drop             // discard H,L

          1 . +                 // E++
          0x100                 // normalized extended mantissa
          0
          ;
        then
        drop


        // ------------------------------------------------
        // Rounded subnormal -> minimum normal.
        //
        // E=0 and rounding produced hidden bit 0x100.
        // Change encoded exponent from 0 to 1.
        // ------------------------------------------------

        >r >r                  // E / L H

        if                     // E != 0
          r> r>
          ;
        then

        r>                     // E H / L

        dup 0x100 and
        if
          drop

          >r                   // E / L H
          1 . +                // 0 -> 1
          r> r>
          ;
        then
        drop

        r>                     // E H L
      ;


      : fp8/reduce ( E H L - E h l )
        // Convert:
        //
        //   27-bit H:L
        //
        // to:
        //
        //   24-bit h:l
        //
        // equivalent to (H:L) >> 3.

        a!                     // A=L

        0 over
        . +*                   // one cross-word right shift

        >r drop drop
        r>                     // E h

        // A now contains:
        //
        //   H0 in bit17
        //   L>>1 below it
        //
        // another two shifts produce final low 16 bits.
        // Arithmetic 2/ is harmless because bits17..16 are
        // discarded by the mask.

        a 2/ 2/
        0xffff and
      ;


      : fp8/main

        @b !                   // sign can be forwarded immediately

        @b                     // e
        @b                     // H
        @b                     // L

        fp8/round
        fp8/fix
        fp8/reduce

        // fp8/reduce used A, restore output port.

        up a!

        >r >r
        !                      // e
        r> !                   // h
        r> !                   // l

        fp8/main
      ;
      """;
}
