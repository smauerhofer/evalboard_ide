namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 502's resident F18 source -- CVM2's floating-point subprocessor, step 4c (exponent finalization
/// and division dispatch). Supplied by Stefan 2026-09-26 as part of the complete FP subprocessor rewrite
/// (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design.</b> The previously stored <c>Source</c> for this coordinate
/// was a completely different "stage 4b node. exponent arithmetic" (<c>fp4b/*</c> words); it is replaced
/// wholesale.
///
/// A slave to node 503; imports node 501. <c>f4c/shl</c>/<c>f4c/shr</c> are single-position mantissa
/// renormalization shifts (sticky-bit preserving on the right-shift side), driven by <c>f4c/adjust</c>,
/// which repeatedly renormalizes <c>H:L</c> (testing bits 9 and 8 of <c>H</c>) until the implicit hidden
/// bit sits at bit 8, adjusting the exponent by the same number of steps. <c>f4c/norm</c> reads the
/// exponent from node 402 and the mantissa from node 501, normalizes it, and forwards <c>e h l</c> down to
/// node 503. <c>f4c/div</c>/<c>f4c/other</c> select whether node 501 runs <c>f5c/div</c> or <c>f5c/pass</c>
/// before falling through into the shared <c>f4c/result</c> tail.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node502Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 4c (exponent finalization and division dispatch), a slave to node 503. Replaced 2026-09-26.</summary>
  public const int Coordinate = 502;

  /// <summary>
  /// Node 502's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 4b, exponent arithmetic" design previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 502. VM 32 bit floatingpoint step 4c.

      node 502 is a slave to node 503.

      exponent finalization and division calculation

      node 502 sends one of the following instructions:
       A[ f5c/div ]] for division
       A[ f5c/pass ]] for all other operations

      result passing and division.

        in: e            {exponent from node 402}

        in: hl           {mantissa from node 501}

        out: e h l       {normalized result to node 503}

      )

      # 501 import

      # down /b         // node 402
      # left /a         // node 501
      entry right

      # 0 org

      +cy

      : f4c/shl ( e h - e' h' ) // A=l -> A=l'
        // normalize one position to the left:
        //
        // H:L <<= 1
        // e--

        clc

        a dup . + a!         // L <<= 1, retain carry

        dup . +              // H = H*2 + carry

        // f4c/shl is only called with H < 0x100.
        // Therefore the H addition cannot carry out,
        // so exponent decrement is safe in EAM.

        >r
        -1 . +
        r>
      ;

      -cy


      : f4c/shr ( e h - e' h' ) // A=l -> A=l'
        // Preserve the bit shifted out of L as sticky.

        a 1 and >r

        // H:L >>= 1
        // e++

        0 over
        . +*

        >r
        drop drop

        1 . +

        r>

        // sticky = new L0 OR discarded old L0

        r> inv
        a inv
        and inv
        a!
      ;

      : f4c/adjust ( e h - e' h' ) // A=l -> A=l'
        // Normalize H:L so H bit 8 is the hidden bit.
        //
        //   H & 0x200 != 0   shift right once, e++
        //   H & 0x100 != 0   normalized
        //   otherwise        shift left, e--
        //
        // Zero produces e=0, H=0, L=0.

        if
          // H != 0

          // H bit 9:
          // result is in [2,4)

          dup 0x200 and
          if
            drop
            f4c/shr
            ;
          then
          drop

          // H bit 8:
          // result is in [1,2)

          dup 0x100 and
          if
            drop
            ;
          then
          drop

          // result is below 1

          ..
          f4c/shl
          f4c/adjust
          ;
        then

        // H == 0
        // Test L directly from A.

        a
        if
          drop
          ..
          f4c/shl
          f4c/adjust
          ;
        then

        // H:L == 0.
        //
        // stack here:
        //   e 0 0
        //
        // Leave stale values underneath and make the
        // top two values e'=0 h'=0.  A is already L'=0.

        dup
      ;


      : f4c/norm ( e h l - ) // normalize and send result

        // Save A = node 501 port.
        // Use A for L during normalization.

        a >r
        a!                    // e h, A=l

        f4c/adjust
        ( e h )               // A=l

        a                     // e h l
        r> a!                 // restore node 501 port

        // B already points to node 503.

        >r >r !b              // e
        r> !b                 // h
        r> !b                 // l
      ;


      : f4c/div
        A[ f5c/div ]] lit !
        ahead

      : f4c/other
        A[ f5c/pass ]] lit !

      : f4c/result then
        down b!               // node 402
        @b                     // e

        @ @                    // H L from node 501
        ( e h l )

        right b!              // node 503

        f4c/norm
      ;
      """;
}
