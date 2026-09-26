namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 103's resident F18 source -- CVM2's floating-point subprocessor, special-case step 3 (ADD/SUB
/// special-case handling; MUL/DIV are delegated on to node 102). Supplied by Stefan 2026-09-26 as part of
/// the complete FP subprocessor rewrite (see <see cref="Node102Program"/>'s own remarks for the full
/// context and the list of nodes involved).
///
/// A slave to node 104. <c>fs3/add</c> falls through into <c>fs3/as</c> with <c>m=0</c> (via <c>dup
/// xor</c>); <c>fs3/sub</c> calls it explicitly with <c>m=0x8000</c>, XORed into operand 2's effective
/// sign -- the same "SUB is ADD with a flipped sign" convention used throughout this subprocessor (see
/// nodes 203/303). <c>fs3/as</c>: NaN -&gt; qNaN; infinity cases -&gt; qNaN only for opposite-signed
/// infinity+infinity (via <c>fs3/r1</c>), otherwise the infinite operand passes through unchanged
/// (<c>fs3/r1</c>/<c>fs3/r2</c> selecting operand 1 or 2's <c>e h l</c> under the already-computed sign);
/// zero cases -&gt; sign = <c>s1 AND s2</c> for zero+zero (the standard round-to-nearest-even convention),
/// otherwise the nonzero operand passes through unchanged. <c>fs3/pass</c> forwards the whole packet
/// unchanged to node 102 (right), injecting <c>fs4/mul</c>/<c>fs4/div</c> as the remote call target.
///
/// This is a brand new coordinate for this project -- no earlier revision of node 103 existed here.
/// </summary>
internal static class Node103Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, special step 3 (ADD/SUB special-case handling; MUL/DIV delegated to node 102), a slave to node 104. Added 2026-09-26.</summary>
  public const int Coordinate = 103;

  /// <summary>
  /// Node 103's full resident F18 source, verbatim from Stefan's 2026-09-26 paste.
  /// </summary>
  public const string Source = """
      ( CVM2 node 103. VM 32 bit floatingpoint special step 3.
       handle special cases for ADD/SUB/MUL/DIV.
       MUL/DIV are delegated to node 102.

      this is a slave node to node 104.

      input after remote call:

        T t1 t2 s1 s2 e1 e2 h1 h2 l1 l2

      output:

        s e h l

      register A is free to use.
      register A is set to right for MUL/DIV

      )

      # 102 import
      # left /b
      # --l- /p


      : fs3/out ( e h l - )
        >r >r
        a !b
        !b
        r> !b
        r> !b
      ;

      : fs3/@3 @b drop @b @b drop @b @b drop ;

      : fs3/r1
        a!                       // save sign
        @b fs3/@3
        fs3/out
      ;

      : fs3/r2
        a!                       // save sign
        fs3/@3 @b
        fs3/out
      ;


      // canonical qNaN

      : fs3/qnan
        @b @b @b @b @b @b
        1 // s == 1 means canonical qNaN
        !b !b !b !b
      ;


      // ADD/SUB common.
      //
      // m = 0000 ADD
      // m = 8000 SUB
      //
      // A = effective sign of operand 2

      : fs3/add
        dup xor

      : fs3/as ( m )
        a!

        @b >r                    // T
        @b @b                    // t1 t2
        @b                       // s1
        @b a xor a!              // effective s2
        r>
        ( t1 t2 s1 T )


        // NaN

        dup 4 and
        if
          fs3/qnan ;
        then
        drop


        // Infinity

        2 and
        if
          drop
          >r
          ( t1 t2 / s1 )

          over 2 and
          if                       // operand 1 infinity
            drop

            dup 2 and
            if                     // both infinity
              drop
              r> dup a xor

              if                   // opposite effective signs
                fs3/qnan ;
              then

              drop
              fs3/r1 ;
            then

            drop
            r>
            fs3/r1 ;
          then

          drop
          r> drop
          a
          fs3/r2 ;
        then
        drop


        // Zero cases

        >r
        ( t1 t2 / s1 )

        over
        if                        // operand 1 zero
          drop

          dup
          . if                      // both zero
            drop
            r> a and
            fs3/r1 ;
          then

          drop                    // 0 +/- x
          r> drop
          a
          fs3/r2 ;
        then

        drop                      // x +/- 0
        r>
        fs3/r1
      ;


      : fs3/sub
        0x8000
        fs3/as
      ;


      // pass complete packet unchanged to node 102

      : fs3/pass
        right a!
        !
        10 for @b ! unext

        @ !b
        @ !b
        @ !b
        @ !b
      ;


      : fs3/mul
        A[ fs4/mul ]] lit
        fs3/pass
      ;


      : fs3/div
        A[ fs4/div ]] lit
        fs3/pass
      ;
      """;
}
