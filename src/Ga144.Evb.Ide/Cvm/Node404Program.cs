namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 404's resident F18 source -- CVM2's floating-point subprocessor, step 6 (rounds the 27-bit
/// extended mantissa to 24 bits and packs it). Supplied by Stefan 2026-09-26 as part of the complete FP
/// subprocessor rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design.</b> The previously stored <c>Source</c> for this coordinate
/// was a completely different "stage 8 node. rounding" (<c>fp8/*</c> words); it is replaced wholesale.
///
/// Reads <c>s e H L</c> from node 504. <c>f6/round</c> applies round-to-nearest-even by adding a bias of 3
/// plus the retained LSB to the 36-bit <c>H:L</c> value (under <c>+cy</c>). <c>f6/fix</c> corrects the
/// exponent/mantissa when rounding either carried out of the 24-bit significand (renormalize, e++, or
/// produce infinity if the exponent overflows to 255) or promoted the largest subnormal into the smallest
/// normal value (e: 0 -&gt; 1). <c>f6/pack</c> discards the three guard bits and assembles the final
/// <c>h,l</c> pair, which <c>f6/main</c> sends up to node 304.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node404Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 6 (round-to-nearest-even and pack to 24-bit significand). Replaced 2026-09-26.</summary>
  public const int Coordinate = 404;

  /// <summary>
  /// Node 404's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 8, rounding" design previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 404. VM 32 bit floatingpoint step 6.

      rounding and packing mantissa to 24 bit {8:16}.

        in: s e H L      {result from node 504}

        out: s e h l     {result to node 304}

      input format:

      normal:
        e = 1..254
        H:L 27-bit internal format
        hidden bit = H.8

      subnormal:
        e = 0
        H:L shifted right as required by node 503

      overflow:
        e = 255
        H:L = 0

      output mantissa:

        h = bits 23..16
        l = bits 15..0

      normal:
        h.7 is the hidden bit

      subnormal:
        h.7 = 0

      rounding:

        round 27 bits to 24 bits using round-to-nearest-even.

        bias = 3 + retained-LSB

        values below halfway round down.
        values above halfway round up.
        exact halfway cases round to an even result.

      during processing:
        stack: s e H
        A:     L

      )

      entry f6/main

      # 0 org


      +cy

      : f6/round ( s e H - s e H' ) // A=L -> A=L'
        clc

        // bias = 3 + L.bit3
        a 2/ 2/ 2/
        dup 2/ 2* xor
        3 . +

        // add bias to H:L
        a . + a!
        0 . +
      ;

      -cy


      : f6/fix ( s e h - s e' h' )
        // Handle carry out of the 24-bit significand.
        //
        // h=0x100 means:
        //
        //   1.111... rounded to 10.000...
        //
        // Renormalize to h=0x80 and increment exponent.

        dup 0x100 and
        if
          drop
          drop
          1 . +

          // e=255 after rounding means infinity.

          dup 255 xor
          if
            drop
            0x80
            ;
          then

          drop
          0                 // infinity: e=255, mantissa=0
          ;
        then
        drop

        // For a normal exponent there is nothing more to do.

        over
        if
          drop
          ;
        then
        drop

        // e=0:
        //
        // Rounding the largest subnormal may generate
        // h=0x80, which is the smallest normal value.

        dup 0x80 and
        if
          drop
          >r
          1 . +             // e: 0 -> 1
          r>
          ;
        then
        drop
      ;


      : f6/pack ( s e H - s e' h ) // A=L -> A=l
        // Round before discarding the three guard bits.

        f6/round

        // Save the upper eight bits of the rounded
        // 24-bit significand:
        //
        //   h = H[8:1]
        //
        // A rounding carry may temporarily produce
        // h=0x100; f6/fix handles that.

        dup 2/ >r

        // Shift the complete H:L value right three bits.
        //
        // S=0, T=H, A=L
        //
        // After the shifts, the low 16 bits of A are:
        //
        //   H[0] : L[17:3]

        0 over
        . +*
        . +*
        . +*

        // We no longer need original H, zero S,
        // or shifted H.

        drop drop drop

        r>                    // s e h

        // Keep the low sixteen packed mantissa bits.

        a 0xffff and a!

        // Correct exponent if rounding crossed a
        // normalization boundary.

        f6/fix
      ;


      : f6/main
        down b!               // node 504
        @b @b @b @b
        ( s e H L )

        a!
        ( s e H )             // A=L

        f6/pack
        ( s e h )             // A=l

        up b!                 // node 304

        >r >r !b              // s
        r> !b                 // e
        r> !b                 // h
        a !b                  // l

        f6/main
      ;
      """;
}
