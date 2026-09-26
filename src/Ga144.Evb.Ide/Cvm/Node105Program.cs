namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 105's resident F18 source -- CVM2's floating-point subprocessor, special-case step 2 (MIN/MAX
/// special-case handling). Supplied by Stefan 2026-09-26 as part of the complete FP subprocessor rewrite
/// (see <see cref="Node102Program"/>'s own remarks for the full context and the list of nodes involved).
///
/// A slave to node 104. Any NaN -&gt; qNaN sentinel (<c>fs2/qnan</c>). Different signs -&gt; MIN picks the
/// negative operand, MAX the positive, via <c>s1 xor m</c> (<c>m=0</c> for MIN via <c>fs2/min</c>'s own
/// fallthrough into <c>fs2/mm</c>, <c>m=0x8000</c> for MAX). Equal signs -&gt; <c>fs2/select</c> picks by
/// preference (zero preferred when <c>sign xor m == 0</c>, i.e. +MIN/-MAX; infinity preferred otherwise,
/// i.e. -MIN/+MAX), falling back to whichever operand is finite, and to operand 1 if both share the same
/// special type.
///
/// This is a brand new coordinate for this project -- no earlier revision of node 105 existed here.
/// </summary>
internal static class Node105Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, special step 2 (MIN/MAX special-case handling), a slave to node 104. Added 2026-09-26.</summary>
  public const int Coordinate = 105;

  /// <summary>
  /// Node 105's full resident F18 source, verbatim from Stefan's 2026-09-26 paste.
  /// </summary>
  public const string Source = """
      ( CVM2 node 105. VM 32 bit floatingpoint special step 2.
        handle special cases for MIN/MAX

      this is a slave node to node 104.

      input after remote call:

        T t1 t2 s1 s2 e1 e2 h1 h2 l1 l2

      output:

        s e h l

      register A is free to use.

      type:
        0 = finite nonzero
        1 = zero
        2 = infinity
        4 = qNaN
        c = sNaN

      )


      # right /b
      # r--- /p


      // Return operand 1.
      // On entry the remaining input is:
      // e1 e2 h1 h2 l1 l2
      // sign is on T.

      : fs2/ehl1
        @b @b drop
        @b @b drop
        @b @b drop

      : fs2/ret
        >r >r >r
        !b r> !b r> !b r> !b
      ;


      // Return operand 2.

      : fs2/ehl2
        @b drop @b
        @b drop @b
        @b drop @b
        fs2/ret
      ;


      // Select between operands of equal sign.
      //
      // mask = 1  -> zero is preferred
      // mask = 2  -> infinity is preferred
      //
      // If the preferred special type is absent,
      // the finite operand wins.  If both operands
      // have the same special type, operand 1 wins.
      //
      // A contains the common sign.

      : fs2/select ( t1 t2 mask )

        >r

        // operand 1 has preferred type?

        over r> dup >r and
        if
          drop
          r> drop
          a
          fs2/ehl1 ;
        then
        drop

        // operand 2 has preferred type?

        dup r> and
        if
          drop
          a
          fs2/ehl2 ;
        then
        drop

        // Preferred type is absent.
        // Select finite operand if there is one.

        over
        . if
          drop

          dup
          . if
            drop
            a
            fs2/ehl1 ;
          then

          drop
          a
          fs2/ehl2 ;
        then

        drop
        a
        fs2/ehl1
      ;


      // Any NaN -> qNaN sentinel S=1.
      //
      // T remains on the data stack and is used
      // to manufacture zero.

      : fs2/qnan

        9 for @b drop unext

        1 dup dup dup

        fs2/ret
      ;


      // Common MIN/MAX processing.
      //
      // m = 0000 for MIN
      // m = 8000 for MAX

      : fs2/min
        dup xor

      : fs2/mm ( m )

        >r

        // T

        @b dup 4 and

        if                        // either operand is NaN
          drop
          r> drop
          fs2/qnan ;
        then

        drop drop                 // test, T

        // t1 t2 s1 s2

        @b @b @b @b


        // Different signs.
        //
        // MIN selects negative.
        // MAX selects positive.
        //
        // s1 XOR m != 0 means operand 1 wins.

        over over xor

        if
          drop

          over r> xor
          if
            drop drop
            fs2/ehl1 ;
          then

          drop
          fs2/ehl2 ;
        then


        // Equal signs.

        drop                      // sign difference
        drop                      // s2; s1 == s2

        // Keep common sign in A.
        //
        // p = sign XOR operation
        //
        // p=0:
        //   +MIN or -MAX
        //   zero is preferred
        //
        // p!=0:
        //   -MIN or +MAX
        //   infinity is preferred

        dup r> xor

        . if
          drop
          a!
          2
          fs2/select ;
        then

        drop
        a!
        1
        fs2/select
      ;


      : fs2/max
        0x8000
        fs2/mm
      ;
      """;
}
