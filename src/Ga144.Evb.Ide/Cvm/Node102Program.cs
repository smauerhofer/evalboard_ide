namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 102's resident F18 source -- CVM2's new 32-bit IEEE-754 floating-point subprocessor, special-case
/// step 4 (MUL/DIV special-case resolution). Supplied by Stefan 2026-09-26 as part of the complete,
/// hardware-working rewrite of the whole FP subprocessor ("my floatingpoint subprocessor is working ...
/// i will give you the source code of all nodes in the FP subprocessor"), which spans nodes 501-504,
/// 401-404, 301-306, 201-205, and 102-105; nodes 601-604 are explicitly retired from this role ("node
/// 601..604 are no longer used").
///
/// A slave to node 104 (reached via node 103): receives <c>T t1 t2 s1 s2 e1 e2 h1 h2 l1 l2</c> after a
/// remote call and returns <c>s e h l</c>. <c>fs4/head</c> computes the result sign (<c>s1 xor s2</c>) and
/// hands back <c>t1 t2 T</c>; <c>fs4/zero</c>/<c>fs4/inf</c>/<c>fs4/qnan</c> emit the fixed encodings for
/// each terminal case. <c>fs4/mul</c>: any NaN -&gt; qNaN; <c>T=3</c> (zero AND infinity both involved,
/// the 0*inf indeterminate case, tested first via <c>T xor 3</c>) -&gt; qNaN; infinity involved -&gt;
/// signed infinity; otherwise -&gt; signed zero. <c>fs4/div</c>: NaN -&gt; qNaN; since two matching
/// non-NaN special types can only be 0/0 or inf/inf, <c>t1==t2</c> -&gt; qNaN, otherwise the infinity
/// condition is computed as <c>(t1&amp;2) xor (t2&amp;1)</c> (numerator-infinity OR denominator-zero,
/// exploiting that the two tests use disjoint bits) -&gt; infinity or zero.
///
/// This is a brand new coordinate for this project -- no earlier revision of node 102 existed here.
/// </summary>
internal static class Node102Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, special step 4 (MUL/DIV special-case resolution), a slave to node 104 via node 103. Added 2026-09-26.</summary>
  public const int Coordinate = 102;

  /// <summary>
  /// Node 102's full resident F18 source, verbatim from Stefan's 2026-09-26 paste.
  /// </summary>
  public const string Source = """
      ( CVM2 node 102. VM 32 bit floatingpoint special step 4.
        handle special cases for MUL/DIV

      this is a slave node to node 104 via node 103.

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


      // Read T,t1,t2,s1,s2.
      //
      // result:
      //   t1 t2 T
      //
      // A = s1 xor s2
      //
      // remaining input:
      //   e1 e2 h1 h2 l1 l2

      : fs4/head
        @b >r
        @b @b
        @b @b xor a!
        r>
      ;


      // discard e1 e2 h1 h2 l1 l2

      : fs4/drop6
        5 for @b drop unext
      ;


      // signed zero

      : fs4/zero
        fs4/drop6
        a !b
        a dup xor
        dup !b dup !b !b
      ;


      // signed infinity

      : fs4/inf
        fs4/drop6
        a !b
        0xff !b
        a dup xor
        dup !b !b
      ;


      // canonical qNaN = qNaN sentinel S=1

      : fs4/qnan
        fs4/drop6
        1 !b !b !b !b
      ;


      // ------------------------------------------------------------
      // MUL
      //
      // NaN           -> qNaN
      // zero * inf    -> qNaN
      // infinity      -> signed infinity
      // otherwise     -> signed zero
      //
      // With NaNs removed:
      //
      //   T = 1   zero involved
      //   T = 2   infinity involved
      //   T = 3   zero and infinity involved
      // ------------------------------------------------------------

      : fs4/mul
        fs4/head
        ( t1 t2 T )

        dup 4 and
        if
          drop drop
          fs4/qnan ;
        then
        drop

        dup 3 xor
        if
          drop

          2 and
          if
            drop
            fs4/inf ;
          then

          drop
          fs4/zero ;
        then

        drop drop
        fs4/qnan
      ;


      // ------------------------------------------------------------
      // DIV
      //
      // NaN       -> qNaN
      // 0 / 0     -> qNaN
      // inf / inf -> qNaN
      //
      // otherwise:
      //
      // result infinity iff:
      //
      //   numerator is infinity
      //        OR
      //   denominator is zero
      //
      // This is:
      //
      //   (t1 & 2) OR (t2 & 1)
      //
      // The two terms use different bits, therefore XOR can be used.
      // ------------------------------------------------------------

      : fs4/div
        fs4/head
        ( t1 t2 T )

        dup 4 and
        if
          drop drop
          fs4/qnan ;
        then
        drop drop
        ( t1 t2 )

        // Equal non-NaN special types can only be
        // 0/0 or inf/inf.

        over over xor
        . if
          drop

          // infinity result =
          // (t1 & 2) | (t2 & 1)

          1 and >r
          2 and
          r> xor

          if
            drop
            fs4/inf ;
          then

          drop
          fs4/zero ;
        then

        drop
        fs4/qnan
      ;
      """;
}
