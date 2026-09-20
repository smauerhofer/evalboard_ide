namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 603's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the fifteenth of 17 new nodes
/// in this batch. Per its own header: "CVM2 node 603. VM 32 bit floatingpoint stage 3c node. sign and
/// normalization" -- a THIRD control branch rooted at node 303/403/503's own stage-3 split, parallel to
/// the "3a" comparison/control chain (403-&gt;402-&gt;401, see <see cref="Node403Program"/>) and the "3b"
/// arithmetic chain (503-&gt;502-&gt;501, see <see cref="Node503Program"/>). This "3c" chain (603-&gt;602-&gt;
/// presumably 601) computes the RESULT's final sign, remote-controls node 602 (imported here, per
/// <c># 602 import</c>) for the division-specific path, and completes subnormal normalization on the
/// (e,h,l) triple node 602 hands back -- combining it with the ottss metadata relayed from node 503 into
/// the final <c>ottsehl</c> node 604 needs for classification.
///
/// <b><c>fp3c/shr</c>, <c>fp3c/subnormal</c>, and <c>fp3c/norm</c> are fully traced and give this project's
/// first confirmed IEEE-754 subnormal encoding routine.</b> <c>fp3c/shr</c> shifts the combined H:L
/// mantissa right by one bit (the same <c>0 X . +*</c> trick already confirmed in
/// <see cref="Node501Program"/>'s own <c>fp5b/shr2</c> and <see cref="Node404Program"/>'s <c>fp8/reduce</c>)
/// while incrementing E, matching the standard technique of raising a too-small working exponent toward the
/// valid range by trading exponent for mantissa precision. <c>fp3c/subnormal</c> then loops this shift
/// while E &lt;= 0, and once E reaches exactly 1 (verified by the invariant: entry requires E &lt;= 0, so one
/// increment can only land on 0 or 1, never overshoot), it discards that E and forces the OUTPUT exponent
/// field to the literal <c>0</c> -- exactly the IEEE-754 subnormal encoding (exponent field 0, no implicit
/// leading mantissa bit). <c>fp3c/norm</c> is the entry dispatcher: E &lt; 0 or E = 0 both route to
/// <c>fp3c/subnormal</c>, E &gt; 0 passes the triple through unchanged -- consistent with
/// <c>fp3c/subnormal</c>'s own declared precondition. Every branch's drop-count matches the "<c>if</c>
/// doesn't auto-consume its tested value" hypothesis exactly, another clean confirming instance.
///
/// <b><c>fp3c/add</c>/<c>fp3c/sub</c> show, for the first time, EXACTLY how this pipeline's packed
/// bit15/bit14 sign word (first seen consumed, not produced, in <see cref="Node504Program"/>'s own
/// <c>fp7/main</c>) is actually constructed.</b> <c>over xor 2/ xor</c>: <c>over xor</c> computes
/// <c>s1 xor s2</c> (call it x, with the sign difference naturally landing in bit15 since s1/s2 carry their
/// sign there); <c>2/</c> shifts x right by one bit, moving that difference down to bit14; the final
/// <c>xor</c> against s1 combines bit15=s1 (the final sign -- correct, since node 403 already guaranteed
/// <c>|operand1| &gt;= |operand2|</c>, so the larger-magnitude operand's sign wins) with bit14=the
/// original <c>s1 xor s2</c> (the "effective signs differ" flag <see cref="Node504Program"/>'s own header
/// already named). This is a fully self-consistent, satisfying resolution of a data shape this project has
/// referenced since node 504 without yet seeing how it was built.
///
/// <b>FLAGGED, not resolved: an apparent conflict between the header's port directives and Stefan's own
/// inline comment on what <c>ex</c> does.</b> The header declares <c># up /a</c> and pairs it, by position,
/// with <c>in: ottss {from node 503}</c> -- consistent with this batch's established column-alignment
/// pattern (603 and 503 share a column, one row apart, matching the vertical up/down links already seen
/// between e.g. 502/402). <c>fp3c/main</c> never reassigns <c>a</c> before or during <c>ex</c> (the
/// reassignment to <c>right</c> happens only AFTER <c>ex</c> returns), so every jump-table word's own
/// <c>A[ fp4c/word ; ]] lit !</c> -- which sends the remote-dispatch address that is supposed to control
/// node 602 -- executes with <c>a</c> still bound to <c>up</c>. Yet <c>fp3c/main</c>'s own comment on
/// <c>ex</c> reads "sends final sign and controls 602" -- explicitly claiming node 602 is controlled during
/// this same step, through whatever port is bound at that moment (i.e. <c>up</c>). Read literally, this
/// would mean <c>up</c> reaches BOTH node 503 (for the initial reads) and node 602 (for the control send),
/// which is only possible if the two are the same physical neighbor or if node 602 itself relays the
/// ottss stream in from 503 -- neither confirmed. The alternative reading (that <c>right</c>, not
/// <c>up</c>, is the real link to node 602, matching where <c>ehl</c> is read immediately after <c>right
/// a!</c>) doesn't explain why the control-send during <c>ex</c> uses <c>a</c>-bound-to-up rather than
/// waiting for the later <c>right</c> reassignment. Not resolved here; flagged as a genuine open question,
/// unlike this batch's more common "port simply undeclared" gaps.
///
/// <b>Observation, not an inconsistency: unlike node 403's <c>fp3a/sub</c>-&gt;<c>fp3a/add</c> fallthrough,
/// or node 604's fully-identical <c>fp6/add</c>/<c>fp6/sub</c> fallthrough, this file's <c>fp3c/add</c> and
/// <c>fp3c/sub</c> have IDENTICAL bodies (<c>over xor 2/ xor !b</c> in both) yet are written as two fully
/// separate definitions, each with its own <c>;</c>, rather than sharing code via the missing-<c>;</c>
/// idiom.</b> This reinforces that the fallthrough idiom is a deliberate, optional stylistic choice in
/// Stefan's own dialect, not something required whenever two definitions happen to coincide.
///
/// <b><c>fp3c/pass</c> (min/max/nop/swap) confirms "final sign is s1"</b> exactly as its own comment states
/// (node 403 already selected/reordered the operand, so s1 is already the right answer -- <c>drop</c>
/// discards s2, <c>!b</c> sends s1). <c>fp3c/mul</c>/<c>fp3c/div</c> both confirm "final sign = s1 xor s2"
/// with a plain, unsplit <c>xor</c> (no bit14/15 packing needed for these operations).
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired into
/// the CVM instruction set</b> -- same reasoning as its siblings, compounded now by the open up/right port
/// question above; node 602 (imported here) and node 601 have not yet been pasted.
///
/// <b>RESOLVED, 2026-09-20: the up/right dispatch conflict flagged above is fixed.</b> A side-by-side
/// comparison against what was on file shows <c>fp3c/main</c>'s <c>right a!</c> moved to immediately
/// BEFORE <c>ex</c> (it used to run only after <c>ex</c> returned). Now, by the time <c>ex</c> dispatches
/// into the jump table and each word sends its remote-control address via <c>A[ fp4c/word ; ]] lit !</c>,
/// <c>A</c> is correctly bound to <c>right</c> -- matching the <c>ex</c> line's own comment ("sends final
/// sign and controls 602") for the first time, and confirming <c>right</c> really is this node's link to
/// node 602. This reads as a genuine fix, not a stylistic reshuffle.
/// </summary>
internal static class Node603Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 3c (final sign, division control, subnormal normalization), added 2026-09-16.</summary>
  public const int Coordinate = 603;

  /// <summary>
  /// Node 603's full resident F18 source, verbatim from Stefan's 2026-09-16 paste, updated 2026-09-20 with
  /// the <c>right a!</c>/<c>ex</c> reordering in <c>fp3c/main</c> that resolves the up/right port
  /// ambiguity noted in this class's own remarks above.
  /// </summary>
  public const string Source = """
      ( CVM2 node 603. VM 32 bit floatingpoint stage 3c node. sign and normalization )
      (
        calculates final result sign.
        controls division path.
        completes subnormal result normalization.

        in:  ottss      {from node 503}
        in:  ehl        {from node 602}
        out: ottsehl    {to node 604}

        ADD/SUB sign:
          bit15 = final sign
          bit14 = effective s1 xor s2
      )

      # 602 import

      entry fp3c/main
      # up /a
      # left /b

      # 8 org


      : fp3c/shr ( E H L - E' H' L' )
        // E++, H:L >>= 1

        a!                    // A=L
        >r                    // / H

        1 . +                 // E++

        r>                    // E' H
        0 over
        . +*                  // H:A >>= 1

        >r drop drop
        r> a                  // E' H' L'
      ;


      : fp3c/subnormal ( E H L - 0 H' L' )
        // Called with E <= 0.
        // Shift until E reaches 1, then encode E as 0.

        fp3c/shr

        >r >r                 // E / L H

        -if                   // E < 0
          r> r>
          fp3c/subnormal ;
        then

        if                    // E > 0; must be E=1
          drop
          0
          r> r>
          ;
        then

        // E=0
        r> r>
        fp3c/subnormal ;
      ;


      : fp3c/norm ( E H L - E' H' L' )
        >r >r                 // E / L H

        -if                   // E < 0
          r> r>
          fp3c/subnormal ;
        then

        if                    // E > 0
          r> r>
          ;
        then

        // E=0
        r> r>
        fp3c/subnormal ;
      ;


      // ADD/SUB:
      // bit15 = s1
      // bit14 = effective s1 xor s2

      : fp3c/add
        over xor 2/ xor
        !b              // s
        A[ fp4c/add ; ]] lit !
      ;

      : fp3c/sub
        over xor 2/ xor !b
        A[ fp4c/sub ; ]] lit !
      ;


      // MIN/MAX/NOP/SWAP:
      // 403 already selected/reordered the desired operand,
      // so final sign is s1.

      : fp3c/pass
        drop !b
        A[ fp4c/pass ; ]] lit !
      ;


      // MUL/DIV:
      // final sign = s1 xor s2

      : fp3c/mul
        xor !b
        A[ fp4c/mul ; ]] lit !
      ;

      : fp3c/div
        xor !b
        A[ fp4c/div ; ]] lit !
      ;


      : fp3c/main
        up a!

        @ dup !b >r           // o
        @ !b                  // t1
        @ !b                  // t2
        @                     // s1
        @                     // s2
        right a!
        ex                    // sends final sign and controls 602


        @                     // e
        @                     // h
        @                     // l

        fp3c/norm

        // B still points to output node 604.
        // fp3c/norm may have destroyed A, but that does not matter.

        >r >r
        !b                    // e
        r> !b                 // h
        r> !b                 // l

        fp3c/main
      ;


      # 0 org

      fp3c/add ;
      fp3c/sub ;
      fp3c/pass ;             // min
      fp3c/pass ;             // max
      fp3c/mul ;
      fp3c/div ;
      fp3c/pass ;             // nop
      fp3c/pass ;             // swap
      """;
}