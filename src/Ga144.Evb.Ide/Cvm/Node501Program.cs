namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 501's resident F18 source -- CVM2's floating-point subprocessor, step 5c (result passing and
/// division). Supplied by Stefan 2026-09-26 as part of the complete FP subprocessor rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design that referenced the now-retired node 601/602 chain.</b> The
/// previously stored <c>Source</c> for this coordinate was a completely different "stage 5b node.
/// implement add and multiply" (<c>fp5b/*</c> words); it is replaced wholesale.
///
/// A slave to node 502, selected by whichever of <c>A[ f5c/div ]]</c>/<c>A[ f5c/pass ]]</c> node 502
/// injects. <c>f5c/sec</c> forces the F18 carry flag to 1 (the same idiom used in node 301).
/// <c>f5c/dbl</c>/<c>f5c/addd</c>/<c>f5c/subd</c>/<c>f5c/q0</c>/<c>f5c/q1</c> are the primitives of a
/// 26-iteration non-restoring binary division algorithm over a 36-bit (<c>RL,DH,RH</c>) remainder/divisor
/// pair and an 18+18-bit quotient (<c>QL,QH</c>); <c>f5c/div</c> runs the loop and finishes with an
/// exactness correction (adds the divisor back once and, if any remainder bits survive, forces the
/// quotient's LSB to 1). <c>f5c/pass</c> simply relays the already-computed mantissa through for every
/// non-divide operation.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node501Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 5c (non-restoring division and result pass-through), a slave to node 502. Replaced 2026-09-26.</summary>
  public const int Coordinate = 501;

  /// <summary>
  /// Node 501's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 5b, implement add and multiply" design (which referenced the now-retired node 601/602 chain) previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 501. VM 32 bit floatingpoint step 5c.

      this node is a slave of node 502.

      node 502 sends one of the following instructions:
       A[ f5c/div ]] for division
       A[ f5c/pass ]] for all other operations

      result passing and division.

        in: H1 H2 L1 L2 {extended mantissas from node 401}
      alternative
        in: H L          {extended result mantissa from node 401}

        out: H L         {extended result mantissa to node 502}

      register A is free to use.

      )

      # down /b    // node 401
      # 0 org
      entry left


      +cy

      : f5c/sec ( x - x )
        // force carry = 1
        dup dup xor inv dup . + drop
      ;


      : f5c/dbl ( RL DH RH - RL' DH RH' )
        // signed 36-bit R <<= 1
        // preserve DH in the middle

        >r >r
        clc
        dup . +
        r> r>
        dup . +
      ;


      : f5c/addd ( RL DH RH - RL' DH RH' )
        // R += D
        // DL is in A

        >r >r

        clc
        a . +

        r> r>
        over . +
      ;


      : f5c/subd ( RL DH RH - RL' DH RH' )
        // R -= D
        // DL is in A

        >r >r

        f5c/sec
        a inv . +

        r> r>
        over inv . +
      ;


      : f5c/q0 ( QL QH - QL' QH' )
        // Q <<= 1

        >r
        clc
        dup . +
        r>
        dup . +
      ;


      : f5c/q1 ( QL QH - QL' QH' )
        // Q = (Q << 1) | 1

        >r
        f5c/sec
        dup . +
        r>
        dup . +
      ;


      : f5c/div ( - H L )
        @b >r              // H1
        @b >r              // H2
        @b                 // L1
        @b a!              // A=L2
        r> r>

        ( L1 H2 H1 )       // A=L2

        f5c/subd

        0 0

        26. >r
        begin
          >r >r
          dup >r
          f5c/dbl

          r> -if
            drop
            f5c/addd
            r> r>
            f5c/q0
          else
            drop
            f5c/subd
            r> r>
            f5c/q1
          then
        next

        // The non-restoring remainder represents exact division as R=-D.
        // Therefore any nonzero R+D means that quotient bits remain.

        >r >r              // save QH QL

        f5c/addd            // R = R + D

        // Reduce the 36-bit R to a nonzero test.
        // stack: RL DH RH

        >r
        drop
        inv
        r> inv
        and inv             // RL OR RH

        if
          // Inexact: force quotient L bit 0 to one.

          drop
          r>
          2/ 2*
          1 xor
        else
          // Exact: leave quotient unchanged.

          drop
          r>
        then

        r>

      -cy

      : f5c/out ( L H - )
        left a!
        ! !
      ;

      : f5c/pass ( - )
        @b @b
        f5c/out
      ;
      """;
}
