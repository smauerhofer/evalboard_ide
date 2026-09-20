namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 304's resident F18 source. Originally pasted 2026-09-16 as a pure rearrange/collect worker: node
/// 305 sent each operand already classified (a 5-field <c>otsehl</c> record per float, type included), and
/// this node's own <c>fp2/order</c> just reordered the two types (and everything else) into the grouped-
/// by-field shape node 303 expects.
///
/// <b>REPLACED, 2026-09-20, with Stefan's own final version</b> -- rewritten in lockstep with
/// <see cref="Node305Program"/>'s own same-day rewrite, which dropped its "type" classification entirely.
/// This node now ABSORBS that responsibility instead:
/// <list type="bullet">
/// <item>Input protocol shrank to match: <c>in: otsehltsehl {from node 305}</c> (11 fields, type
/// pre-included) became <c>in: osehlsehl {from node 305}</c> (9 fields, no type at all).</item>
/// <item><c>fp2/order</c> is gone. In its place: <c>fp2/fold</c> and <c>fp2/or</c> -- word-for-word the
/// same two words <see cref="Node305Program"/>'s OLD draft used to have as <c>fp1/fold</c>/<c>fp1/or</c> --
/// plus a new <c>fp2/calct ( l h e - l h e t )</c>, a faithful port of the classification branch that used
/// to live inside node 305's own <c>fp1/decompose</c> (same normal/NaN-with-qNaN-vs-sNaN/infinity/
/// subnormal/zero cases, same type codes 0x00/0x04/0x0c/0x02/0x00/0x01), restructured to return
/// <c>(l h e t)</c> on the stack instead of writing straight to memory. The new header's added
/// "calculate type" table is the exact table that used to sit in node 305's own header and is gone from
/// there now.</item>
/// <item><c>fp2/main</c> rewritten to call <c>fp2/calct</c> itself (once per operand, on the raw
/// <c>l h e</c> it just read) rather than relaying an already-computed type -- same final output shape
/// (<c>ottsseehhll</c>, unchanged) built from raw fields instead of a passed-through classification.</item>
/// <item>Port directive default changed, <c># left /a</c> -&gt; <c># up /a</c> -- cosmetic only: the body
/// still explicitly does <c>left a!</c> at the top of <c>fp2/main</c> and <c>up a!</c> at the top of
/// <c>fp2/return</c>, exactly as before, so this default is never actually relied on.</item>
/// </list>
/// <c>fp2/return</c>'s own missing closing <c>;</c> (falling through into <c>fp2/main</c>) is unchanged
/// from the original draft -- still not explained, still reproduced as pasted.
///
/// This move (classification 305 -&gt; 304) is corroborated by the first real hardware trace of this
/// pipeline (Stefan's "first hardware test with FP" Core Dump, 2026-09-19), which only ever pushed plain
/// finite floats (1.0, -2.0, -3.0) -- consistent with node 305 no longer needing to classify anything.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- deferred pending Stefan's own go-ahead for this whole pipeline.
/// </summary>
internal static class Node304Program
{
  /// <summary>The node this program is always deployed to -- CVM2's 32-bit floating-point pipeline, stage 2 (rearrange/collect, now also classifies operand type), added 2026-09-16, rewritten 2026-09-20.</summary>
  public const int Coordinate = 304;

  /// <summary>
  /// Node 304's full resident F18 source, verbatim from Stefan's 2026-09-20 paste (his final rewrite --
  /// see this class's own remarks above for what changed from the original 2026-09-16 draft and why).
  /// The missing <c>;</c> on <c>fp2/return</c> is reproduced exactly as pasted, not corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 304. VM 32 bit floatingpoint stage 2 node. rearrange data stream and collect return value )
      (
        in: osehlsehl   {from node 305}
        out: ottsseehhll  {to node 303}

        return in:  sehl  {from node 404}
        return out: sehl  {to node 305}

      calculate type:
            t = 0x00   finite nonzero
            t = 0x01   zero
            t = 0x02   infinity
            t = 0x04   qNaN
            t = 0x0c   sNaN       // NAN bit + signaling bit



      )

      entry fp2/main
      # right /b
      # up /a

      # 0 org


      : fp2/fold ( lh-lhf) over over 0x7f and
      : fp2/or ( ab-c) >r inv r> inv and inv ;

      : fp2/calct ( l h e - l h e t )
        ( l h e )

        if // exp != 0

          dup 0xff xor
          if // normal
            dup xor                      // t = 0
            ( l h e 0 )
            ;
          then

          ( l h 0xff 0 )
          drop drop
          ( l h )

          // NaN or infinity
          fp2/fold
          ( l h f )

          if // NaN
            drop
            ( l h )

            // h bit6 = quiet bit
            //
            // qNaN: h&40 = 40 -> 8 xor 0c = 04
            // sNaN: h&40 = 00 -> 0 xor 0c = 0c

            dup 0x40 and
            2/ 2/ 2/
            0x0c xor

            >r 0xff r>
            ( l h e t )
            ;
          then

          // infinity
          drop
          0xff 0x02
          ( l h e t )
          ;
        then

        // exponent = 0

        drop
        fp2/fold
        ( l h f )

        if // subnormal
          drop
          ( l h )

          dup dup xor                  // e = 0
          dup                          // t = 0

          ( l h 0 0 )
          ;
        then

        // zero
        //
        // l=h=f=0, so f already serves as e=0

        0x01
        ( 0 0 0 1 )
        ;



      : fp2/return
        up a!
        @ !b                  // s
        @ !b                  // e
        @ !b                  // h
        @ !b                  // l


      : fp2/main

        left a!
        @b !                  // o


        @b >r                 // s1
        @b >r                 // e1
        @b >r                 // h1
        @b                    // l1

        ( l1 / s1 e1 h1 )
        r> r>
        ( l1 h1 e1 / s1 )
        fp2/calct
        !                     // t1

        @b >r                 // s2
        @b >r                 // e2
        @b >r                 // h2
        @b                    // l2
        ( l1 h1 e1 l2 / s1 s2 e2 h2 )
        r> r>
        ( l1 h1 e1 l2 h2 e2 / s1 s2 )
        fp2/calct
        !                     // t2
        ( l1 h1 e1 l2 h2 e2 / s1 s2 )
        r> r>
        ( l1 h1 e1 l2 h2 e2 s2 s1 )
        !                     // s1
        !                     // s2
        ( l1 h1 e1 l2 h2 e2 )
        >r >r >r
        ( l1 h1 e1 / e2 h2 l2 )
        !                     // e1
        ( l1 h1 e1 / e2 h2 l2 )
        r> r> r>
        !                     // e2
        ( l1 h1 l2 h2 )
        >r >r
        ( l1 h1 / h2 l2 )
        !                     // h1
        r> r>
        ( l1 l2 h2 )
        !                     // h2
        ( l1 l2 )
        >r !                  // l1
        r> !                  // l2

        fp2/return
      ;
      """;
}