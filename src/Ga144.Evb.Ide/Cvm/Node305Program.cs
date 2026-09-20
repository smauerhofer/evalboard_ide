namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 305's resident F18 source. Originally pasted 2026-09-16 as an elaborate draft that classified
/// each decomposed float into a 5-field record (sign, a "type" byte -- finite/zero/infinity/qNaN/sNaN --
/// exponent, mantissa-high, mantissa-low), with dedicated NaN/infinity/denormalized branches, an unused
/// <c>fp1/or</c> word, and a mysterious third ("up") port beyond the node's own declared <c>left</c>/
/// <c>right</c> pair.
///
/// <b>REPLACED, 2026-09-20, with Stefan's own final version</b> -- a full rewrite, not an incremental
/// edit, pasted as part of a side-by-side comparison of this entire 17-node floating-point pipeline
/// against what was on file. This final version:
/// <list type="bullet">
/// <item>Drops the "type" classification entirely -- only sign/exponent/mantissa-high/mantissa-low are
/// decomposed now (4 fields, not 5). The classification responsibility MOVED to <see cref="Node304Program"/>
/// instead (confirmed there: its own new <c>fp2/fold</c>/<c>fp2/or</c>/<c>fp2/calct</c> are a faithful port
/// of what used to live here, restructured to return <c>(l h e t)</c> rather than write straight to
/// memory).</item>
/// <item><c>fp1/decompose</c> collapses to one test: <c>e != 0</c> (add the explicit hidden bit) vs.
/// everything else (zero/subnormal/infinity/NaN, all now handled downstream at node 304), always calling a
/// simple <c>fp1/final</c> at the end -- no more per-case branching, no more <c>fp1/fold</c>/<c>fp1/or</c>
/// (both are gone, since they existed only to support the removed classification logic).</item>
/// <item><c>fp1/final</c> moved out of <c>fp1/decompose</c>'s NaN branch (where it sat, oddly, in the
/// prior draft) to its own top-level definition, now with <c>// h</c>/<c>// l</c> comments on its two
/// stores.</item>
/// <item>The mysterious third ("up") port is GONE -- <c>fp1/compose</c> no longer does <c>up a!</c> and
/// <c>fp1/main</c> no longer does <c>left a!</c>; this node now only ever uses the two ports its own
/// header declares (<c>/a</c>, <c>/b</c>), resolving the open question the original draft's own remarks
/// raised about why a 2-port node needed a third direction -- it didn't, once the extra port uses were
/// removed.</item>
/// <item>The header's own port-protocol comment now names actual neighbors directly (<c>{from node
/// 306}</c>, <c>{to node 304}</c>, <c>{from node 304}</c>, <c>{to node 306}</c>) instead of the abstract,
/// nodeless <c>in:</c>/<c>out:</c>/<c>reply:</c> shape the draft used -- answering this file's own
/// long-standing "which parent talks to it" question directly from the source rather than by inference.
/// </item>
/// </list>
/// This reading is corroborated by the very first real hardware trace of this pipeline (Stefan's "first
/// hardware test with FP" Core Dump, 2026-09-19): every value pushed was a plain finite float (1.0, -2.0,
/// -3.0), never a NaN/infinity/denormal edge case -- consistent with this node no longer needing to
/// classify those cases at all.
///
/// <b>Port assignment also swapped</b> from the draft's <c># left /a</c> / <c># right /b</c> to
/// <c># left /b</c> / <c># right /a</c>.
///
/// <b>Confirms node 304's own current header</b> (<c>in: osehlsehl {from node 305}</c>, no <c>t</c>
/// field) exactly: this node's own <c>out: osehlsehl {to node 304}</c> matches it field-for-field.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into <c>Ga144.Cvm.Toolchain.CvmInstructionSet</c>/<c>Services.CvmAssemblyLanguage</c></b> -- this whole
/// pipeline's wiring is still deferred pending Stefan's own go-ahead, per this project's usual practice.
/// </summary>
internal static class Node305Program
{
  /// <summary>The node this program is always deployed to -- CVM2's 32-bit floating-point pipeline, stage 1 (unpack/pack), added 2026-09-16, rewritten 2026-09-20.</summary>
  public const int Coordinate = 305;

  /// <summary>
  /// Node 305's full resident F18 source, verbatim from Stefan's 2026-09-20 paste (his final,
  /// simplified rewrite -- see this class's own remarks above for what changed from the original
  /// 2026-09-16 draft and why).
  /// </summary>
  public const string Source = """
      ( CVM2 node 305. VM 32 bit floatingpoint stage 1 node. unpack and pack )
      (
        receives 2 floatingpoint numbers and decomposes each into

        1. sign:
             0       => positive
             0x8000  => negative

        2. exponent:
             IEEE-754 biased exponent, 0x00 .. 0xff

        3. mantissa high:
             normal numbers have implicit leading 1 made explicit in bit 7

        4. mantissa low

        in:    ohlhl           {from node 306}
        out:   osehlsehl       {to node 304}
        in:    sehl            {from node 304}
        out:   hl              {to node 306}
      )

      # 0 org
      entry fp1/main

      # left /b
      # right /a


      : fp1/final ( l h - )
        0x7f and
        !                       // h
        !                       // l
      ;


      : fp1/decompose ( - )
        @b >r                   // h
        @b r>                   // l h

        ( l h )
        // sign

        dup 0x8000 and
        !                       // s


        // exponent

        dup
        2/ 2/ 2/ 2/ 2/ 2/ 2/
        0xff and

        dup !                   // e


        // Add explicit hidden bit only for a normal number:
        //
        //   e = 0       zero/subnormal
        //   e = ff      infinity/NaN
        //   otherwise   normal

        if                      // e != 0

          0xff xor ..

          if                    // e != ff
            drop
            ( l h )

            0x7f and
            0x80 xor            // explicit hidden bit
            !                   // h
            !                   // l
            ;
          then
        then

        // exponent 0 or ff:
        // no hidden bit

        drop
        fp1/final
      ;


      // read sehl from node 304 and compose binary32

      : fp1/compose

        // sign
        @

        // exponent
        @
        2* 2* 2* 2* 2* 2* 2*
        xor

        // mantissa high
        // remove explicit hidden bit
        @
        0x7f and
        xor
        !b

        // mantissa low
        @
        0xffff and
        !b


      : fp1/main
        @b !                    // o
        fp1/decompose           // operand 1
        fp1/decompose           // operand 2

        fp1/compose
      ;
      """;
}