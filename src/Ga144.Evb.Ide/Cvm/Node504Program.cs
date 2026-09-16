namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 504's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the tenth of 17 new nodes
/// in this batch. Per its own header: "CVM2 node 504. VM 32 bit floatingpoint stage 7 node. final
/// values" -- fills in "stage 7", the gap between node 301's own stage 5 and node 404's own stage 8 (see
/// <see cref="Node404Program"/>'s own remarks, which flagged that gap when it was first noticed). Its
/// own <c>out: sehl {to node 404}</c> matches node 404's own <c>in: sehl {from node 504}</c> exactly,
/// confirming the two sources describe the same real link. Its input, <c>ksehl</c>, comes from node 604
/// (not yet pasted) -- meaning stage 6 lives there.
///
/// <b>Reinforces (now with a fourth data point) that adjacent nodes in this batch consistently use the
/// SAME direction word for a shared link, rather than mirrored left/right-style labels.</b> This node's
/// <c># down /a</c> defaults to the link with node 404 -- and node 404 itself also calls that same link
/// <c>down</c> (see <see cref="Node404Program"/>'s own remarks). Combined with the already-confirmed
/// 302/303 ("right"), 303/304 ("left"), and 401/402 ("left") pairs, this is now a well-established pattern
/// across this whole batch, not just a hypothesis.
///
/// <b>Purpose, worked out from the body (matches the header's own <c>k</c> table exactly):</b> given a
/// classification code <c>k</c> (0 = ordinary calculated result, 1 = zero, 2 = infinity, 3 = qNaN, 4 = an
/// add/sub-of-two-infinities special case needing one more decision) and the always-computed <c>s e h l</c>
/// stream, this node either passes the calculated value through (<c>fp7/pass</c>, itself checking for
/// exponent overflow -&gt; infinity and for an exact-zero mantissa -&gt; forced clean zero encoding) or
/// overwrites it with a fixed zero/infinity/qNaN pattern (<c>fp7/zero</c>/<c>fp7/inf</c>/<c>fp7/qnan</c>).
/// For <c>k=4</c>, bit 14 of the incoming packed sign word (per the header's own note) picks between
/// treating the result as qNaN (opposite effective signs -- e.g. <c>+inf + -inf</c>) or infinity (same
/// effective sign). <c>fp7/main</c> dispatches to one of the four via <c>ex</c> against the small,
/// UNIFORM (2-word) jump table at the end of this source, having staged the target on the return stack
/// with <c>&gt;r</c> -- consistent with <c>ex</c> consuming the return stack as a computed jump, an F18
/// idiom this project hasn't needed to name explicitly before.
///
/// <b>FLAGGED, not resolved: how <c>k</c>'s small values (0-4, matching the header's own plain-integer
/// table) become the jump table's own word addresses (0, 2, 4, 6, at <c># 0 org</c>) is not shown.</b>
/// No <c>2*</c> or other scaling is visible before the <c>&gt;r</c>/<c>ex</c> pair. Either <c>k</c>
/// arrives from node 604 already pre-scaled to a real address (paralleling the same open question raised
/// for node 403's own opcode value <c>o</c> -- see <see cref="Node403Program"/>'s own remarks), or
/// <c>ex</c>/this dialect's own compiled <c>: word ;</c> shape does something with table addressing this
/// project doesn't yet model. Not guessed at here.
///
/// <b>Other observations, mostly confirming existing findings rather than raising new ones:</b> the bare
/// <c>.</c> token reappears in <c>0xfe inv . +</c>, computing <c>e - 255</c> via the identity
/// <c>NOT(x) = -x-1</c> -- notably WITHOUT the extra <c>+1</c> step node 302's own <c>fp4/sub</c> needed,
/// which is fine for a pure less-than comparison (the constant off-by-one is already absorbed by using
/// <c>0xfe</c> instead of <c>0xff</c>), and is consistent with (not contradicting) the add-with-carry
/// reading of <c>.</c> already offered elsewhere. The bare <c>..</c> token reappears too (in
/// <c>fp7/pass</c>, in the same "doesn't affect the surrounding logic" position already seen in every
/// other node that has it). Every branch in every word here ends in its own <c>;</c> -- no fallthrough
/// idiom in this source.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- same reasoning as its siblings, plus the open jump-table-addressing
/// question above.
/// </summary>
internal static class Node504Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 7 (classify and finalize: pass-through/zero/infinity/qNaN), added 2026-09-16.</summary>
  public const int Coordinate = 504;

  /// <summary>
  /// Node 504's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The unresolved
  /// <c>.</c>/<c>..</c> tokens noted in this class's own remarks above are reproduced exactly as pasted,
  /// not corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 504. VM 32 bit floatingpoint stage 7 node. final values )
      (
        in:  ksehl      {from node 604}
        out: sehl       {to node 404}

        output mantissa is still extended 27-bit format.

        k:
          0 calculated result
          1 zero
          2 infinity
          3 qNaN
          4 ADD/SUB both infinity

        for k=4:
          sign bit14 = effective s1 xor s2
      )

      entry fp7/main
      # down /a
      # up /b

      # 4 org


      : fp7/send ( E H L - )
        >r >r
        !                      // e
        r> !                   // h
        r> !                   // l
      ;


      : fp7/pass ( s - )
        !                      // final sign

        @b                     // e

        // finite overflow?
        //
        // e - 255 < 0 => e < 255

        dup 0xfe inv . +
        -if
          drop                 // leave original e

          @b                   // h
          @b                   // l

          // exact finite zero:
          // force exponent to zero.

          dup
          if
            drop
            fp7/send ;
          then
          drop

          .. over
          if
            drop
            fp7/send ;
          then
          drop

          // H=L=0

          0 !                  // e
          0 !                  // h
          0 !                  // l
          ;
        then

        // e >= 255 -> infinity

        drop drop              // comparison, original e
        @b @b                  // discard calculated h,l

        0xff !                 // e
        0 !                    // h
        0 !                    // l
      ;


      : fp7/zero ( s - )
        !                      // preserve sign

        @b @b @b               // discard calculated e,h,l

        0 !                    // e
        0 !                    // h
        0 !                    // l
      ;


      : fp7/inf ( s - )
        !                      // preserve sign

        @b @b @b               // discard calculated e,h,l

        0xff !                 // e
        0 !                    // h
        0 !                    // l
      ;


      : fp7/qnan ( s - )
        drop

        @b @b @b               // discard calculated e,h,l

        0 !                    // s
        0xff !                 // e

        // qNaN bit in extended 27-bit representation.
        // Node 404 reduces this to final h=0x40.

        0x80 !                 // h
        0 !                    // l
      ;


      : fp7/main
        @b                     // k

        // k=4 = ADD/SUB with both operands infinity.
        // bit14 of sign resolves Inf versus qNaN.

        dup 4 xor
        if
          drop
          >r                    // dispatch k
          @b                    // s
        else
          drop drop             // comparison, k

          @b                    // packed sign

          dup 0x4000 and
          if
            drop
            3 >r                // opposite effective signs -> qNaN
          else
            drop
            2 >r                // same effective sign -> infinity
          then
        then

        0x8000 and             // remove temporary bit14

        ex

        fp7/main
      ;


      # 0 org

      fp7/pass ;
      fp7/zero ;
      fp7/inf ;
      fp7/qnan ;
      """;
}
