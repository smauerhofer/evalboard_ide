namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 503's resident F18 source -- CVM2's floating-point subprocessor, step 3c (sign handling,
/// exponent range checking and subnormal preparation). Supplied by Stefan 2026-09-26 as part of the
/// complete FP subprocessor rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design.</b> The previously stored <c>Source</c> for this coordinate
/// was a completely different "stage 3b node. implement add and multiply" (<c>fp3b/*</c> words); it is
/// replaced wholesale.
///
/// A slave to node 403; imports node 502. <c>f3c/shr</c> is a sticky-preserving single-bit right shift;
/// <c>f3c/sub</c> repeatedly shifts the mantissa right by <c>1 - e</c> to prepare a subnormal result and
/// zeroes the exponent. <c>f3c/adjust</c> classifies the incoming exponent: <c>e &lt;= 0</c> -&gt;
/// subnormal (via <c>f3c/sub</c>); <c>1..254</c> -&gt; unchanged; <c>e &gt;= 255</c> -&gt; infinity
/// (exponent forced to 255, mantissa to zero). <c>f3c/div</c>/<c>f3c/other</c> select which of node 502's
/// operations runs before falling through into the shared <c>f3c/result</c> tail, which also receives and
/// forwards the sign from node 403 and sends <c>s e h l</c> on to node 504.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node503Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 3c (sign handling, exponent range checking, subnormal preparation), a slave to node 403. Replaced 2026-09-26.</summary>
  public const int Coordinate = 503;

  /// <summary>
  /// Node 503's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 3b, implement add and multiply" design previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 503. VM 32 bit floatingpoint step 3c.

      node 503 is a slave to node 403.

      sign handling, exponent range checking and
      subnormal preparation.

        in: s            {sign from node 403}
        in: e H L        {normalized result from node 502}

        out: s e H L     {range-adjusted result to node 504}

      output format:

      normal:
        e = 1..254
        H:L remains in 27-bit internal format

      subnormal:
        e = 0
        H:L shifted right as required

      overflow:
        e = 255
        H:L = 0

      during processing:
        stack: s e h
        A:     l

      )

      # 502 import

      entry down

      # 0 org


      : f3c/shr ( s e h - s e h' ) // A=l -> A=l'
        // Preserve the bit shifted out of L as sticky.

        a 1 and >r

        // H:L >>= 1
        //
        // S=0, T=H, A=L

        0 over
        . +*

        >r
        drop drop
        r>

        // sticky = new L0 OR discarded old L0

        r> inv
        a inv
        and inv
        a!
      ;

      : f3c/sub ( s e h - s 0 h' ) // A=l -> A=l'
        // e <= 0
        //
        // shift mantissa by:
        //
        //   1 - e
        //
        // For next the initial count is therefore -e.

        over inv 1 . +
        >r       // R = -e

        // replace exponent by zero

        >r
        drop
        0
        r>

        begin
          f3c/shr
        next
      ;


      : f3c/adjust ( s e h - s e' h' ) // A=l -> A=l'
        // e < 0   -> subnormal
        // e = 0   -> subnormal
        // 1..254  -> unchanged
        // e >=255 -> infinity

        over

        . -if
          // e < 0

          drop
          f3c/sub
          ;
        then

        . if
          // e > 0

          drop

          // Test e < 255.

          over
          -255 . +

          . -if
            // 1 <= e <= 254

            drop
            ;
          then

          // e >= 255

          drop

          // s e h -> s 255 0
          // A      -> 0

          drop drop
          255
          0 dup a!
          ;
        then

        // e == 0

        drop
        f3c/sub
      ;


      : f3c/div ( s )
        A[ f4c/div ]] lit
        ahead

      : f3c/other ( s )
        A[ f4c/other ]] lit

      : f3c/result then
        right a!
        !                       // invoke selected operation in node 502

        @ @ @                   // e h l from node 502
        ( s e h l )

        a!
        ( s e h )               // A=l

        f3c/adjust

        // stack: s e h
        // A:     l

        left b!                 // node 504

        >r >r !b                // s
        r> !b                   // e
        r> !b                   // h
        a !b                    // l
      ;
      """;
}
