namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 201's resident F18 source -- CVM2's floating-point subprocessor, step 5 (mantissa preparation,
/// with an optional magnitude comparison). Supplied by Stefan 2026-09-26 as part of the complete FP
/// subprocessor rewrite (Node 102's own remarks give the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role.).
///
/// A slave to node 202 (left neighbor): <c>f5/main</c> reads <c>h1 h2 l1 l2</c> from the left port, then
/// runs whichever resident word node 202 has just injected into this node's <c>--l-</c> port slot --
/// either <c>f5/ignore</c> (a plain fallthrough, no comparison) or <c>f5/calc</c> (compares the high
/// mantissa words first via <c>f5/sub</c>/<c>f5/gt</c>, falling back to the low words on a tie, and sends
/// the resulting compare code back to node 202). <c>f5/extend</c> widens one <c>h,l</c> pair into the
/// 27-bit extended <c>H,L</c> mantissa format this subprocessor uses internally, and <c>f5/main</c> sends
/// both extended pairs on to node 301. <c>f5/shl1</c>/<c>f5/shl2</c> are single-position mantissa shift
/// primitives with no local caller -- they are injected into and driven by node 202's <c>f4/mul</c>/
/// <c>f4/div</c> (via <c>A[ f5/shl2 ]]</c>/<c>A[ f5/shl1 ]]</c>) as part of that node's MUL/DIV exponent
/// normalization loop.
///
/// This is a brand new coordinate for this project -- no earlier revision of node 201 existed here.
/// </summary>
internal static class Node201Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 5 (mantissa preparation, optional magnitude comparison), a slave to node 202. Added 2026-09-26.</summary>
  public const int Coordinate = 201;

  /// <summary>
  /// Node 201's full resident F18 source, verbatim from Stefan's 2026-09-26 paste.
  /// </summary>
  public const string Source = """
      ( CVM2 node 201. VM 32 bit floatingpoint step 5.

      mantissa preparation.

      optional magnitude comparison

        in:  h1 h2 l1 l2  {from node 202}
        out: H1 H2 L1 L2  {to node 301}
      optional:
        out: c             {to node 202}

        c: magnitude compare result
          0x8000  : |f1| >  |f2|
          0       : |f1| == |f2|
          0x20000 : |f1| <  |f2|
      )

      # down /a
      # left /b
      entry f5/main

      # 0 org

      : f5/sub inv . +  1 . + ;

      : f5/gt ( diff - c )
        -if
          drop 0x20000 ;       // f1 < f2
        then
        if
          drop 0x8000 ;        // f1 > f2
        then
        // diff = 0 -> equal, leave 0
      : f5/ignore
      ;

      : f5/extend ( h l - H L)
        dup 0x8000 and
        if
          drop
          >r 2* 1 xor r> ..
        else
          drop
          >r 2* r>
        then
        2* 2* 2*
      ;


      : f5/calc ( h1 h2 l1 l2 - h1 h2 l1 l2 )
        // Compare high mantissa words first,
        // while preserving all four input words.

        >r >r
        ( h1 h2 / l2 l1 )

        over over f5/sub

        if
          f5/gt
          !b
          r> r>
          ;
        then

        drop

        // High words equal: compare low words.

        r> r>
        ( h1 h2 l1 l2 )

        over over f5/sub
        f5/gt
        !b
      ;

      : f5/shl2 ( h1 h2 l1 l2 - h1 h2' l1 l2' )
        >r a!

        dup 0x80 and
        if
          !b
          a r>
          ;
        then
        drop

        r>

        dup 0x8000 and
        if
          drop
          >r 2* 1 xor r>
        else
          drop
          >r 2* r>
        then
        2* 0xffff and

        0 !b

        >r a r>
      ;

      : f5/shl1 ( h1 h2 l1 l2 - h1' h2 l1' l2 )
        >r f5/shl2 r>
      ;




      : f5/main
        @b               // h1
        @b               // h2
        @b               // l1
        @b               // l2
        ( h1 h2 l1 l2 )

        // First allow node 202 to request either
        // f5/ignore or f5/calc.
        //
        // f5/calc preserves h1 h2 l1 l2.

        left a!
        --l-

        ( h1 h2 l1 l2 )

        // Rearrange into the two h,l pairs
        // required by f5/extend.

        >r
        ( h1 h2 l1 / l2 )

        a!
        ( h1 h2 / l2 )

        >r a
        ( h1 l1 / l2 h2 )

        f5/extend
        ( H1 L1 / l2 h2 )

        r> r>
        ( H1 L1 h2 l2 )

        f5/extend
        ( H1 L1 H2 L2 )

        down a!

        >r >r >r !       // H1
        r> r> !           // H2
        !                 // L1
        r> !              // L2

        f5/main
      ;
      """;
}
