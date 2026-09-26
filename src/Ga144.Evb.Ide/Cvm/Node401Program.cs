namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 401's resident F18 source -- CVM2's floating-point subprocessor, step 5b (mantissa
/// multiplication and pass-through). Supplied by Stefan 2026-09-26 as part of the complete FP subprocessor
/// rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design.</b> The previously stored <c>Source</c> for this coordinate
/// was a completely different "stage 5a node. mantissa handling" (<c>fp5a/*</c> words, a single relay body
/// shared by add/sub/mul/div/nop); it is replaced wholesale.
///
/// A slave to node 402. <c>f5b/m9</c> is an 18x18-bit unsigned multiply primitive (17 iterations of
/// <c>+*</c>, matching this file's own header). <c>f5b/mul</c> is the full extended-mantissa multiply:
/// computes the low*low, high*high and both cross partial products via <c>f5b/m9</c>, reassembles them
/// into a 36-bit sum with two 36-bit adds (<c>f5b/add</c>, under <c>+cy</c>), folds in a sticky bit from
/// the discarded low bits (including the bit shifted out of the post-add byte), and shifts the 36-bit
/// product right by 8 to produce the final 18+18-bit <c>H,L</c> result forwarded down to node 501.
/// <c>f5b/pass</c>/<c>f5b/div</c> relay the mantissa words through unchanged for the non-multiply paths.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node401Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 5b (mantissa multiplication and pass-through), a slave to node 402. Replaced 2026-09-26.</summary>
  public const int Coordinate = 401;

  /// <summary>
  /// Node 401's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 5a, mantissa handling" design previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 401. VM 32 bit floatingpoint multiply step 5b.

      this node is a slave of node 402.

      mantissa multiplication and pass-through.

        in: H1 H2 L1 L2       {extended mantissas from node 301}
      alternative
        in: H L                {extended result mantissa from node 301}

        out: H1 H2 L1 L2      {pass-through to node 501}
      alternative
        out: H L               {multiplied/pass-through result to node 501}

      )

      # up /b
      # down /a
      # left /p
      # 0 org


      : f5b/m9 ( h l - H L ) // 18x18 multiplication
        a! 17. >r
        dup dup xor
        begin . +* unext
        >r drop r> a
      ;


      +cy

      : f5b/add ( H1 H2 L1 L2 - H L ) // add 36-bit values
        clc
        . + >r
        . +
        r>
      ;

      -cy


      : f5b/mul ( - ... 0 H L )

        @b
        @b
        @b
        @b
        ( H1 H2 L1 L2 )

        // ------------------------------------------------
        // z0 = high18(L1*L2), unsigned 18x18
        // ------------------------------------------------

        over over

        dup >r a!
        17. >r
        dup dup xor
        begin . +* unext

        // If L1 has bit17 set, +* interpreted it as negative.
        // unsigned product therefore needs +L2 in high word.

        over -if
          drop
          r> . +
        else
          drop
          r> drop
        then

        >r drop r>

        // Keep low18(L1*L2) for sticky generation.

        a >r
        >r


        // ------------------------------------------------
        // z2 = H1*H2
        // ------------------------------------------------

        >r >r
        over over f5b/m9
        drop drop
        r> r>

        a >r


        // ------------------------------------------------
        // reorder
        //
        // H1 H2 L1 L2
        // -> H1 L2 H2 L1
        // ------------------------------------------------

        a! >r a r>
        >r
        a! >r a r>
        r>


        // ------------------------------------------------
        // cross products
        //
        // p2 = H2*L1
        // p1 = H1*L2
        // ------------------------------------------------

        f5b/m9
        >r >r

        f5b/m9
        r> r>


        // reorder for 36-bit add:
        //
        // p1H p2H p1L p2L

        >r
        a! >r a r>
        r>

        ..
        f5b/add


        // ------------------------------------------------
        // add:
        //
        //     z2 << 18
        //   + z0
        //
        // R has: z2 z0 low18(L1*L2)
        // ------------------------------------------------

        r> r>

        >r
        a! >r a r>
        r>

        ..
        f5b/add


        // ------------------------------------------------
        // sticky =
        //
        //   low18(L1*L2) != 0
        //   OR
        //   bits 0..8 of C != 0
        //
        // C bit 8 becomes result bit 0 after C >> 8,
        // so it must be folded into sticky as well.
        // ------------------------------------------------

        dup 0x1ff and
        inv
        r> inv
        and inv

        if
          drop 1
        then
        >r


        // ------------------------------------------------
        // result = C >> 8
        // ------------------------------------------------

        a!
        7. >r
        0 over
        begin . +* unext

        0x3ff and
        a

        // Clear result bit 0 and replace it with sticky.

        2/ 2*
        r> xor

        ( ... 0 H L )

        down a!
        ! !
      ;


      : f5b/pass ( - )
        down a!
        @b !
        @b !
      ;

      : f5b/div ( - )
        down a!
        @b !
        @b !
        @b !
        @b !
      ;
      """;
}
