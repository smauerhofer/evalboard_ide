namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 202's resident F18 source -- CVM2's floating-point subprocessor, step 4 (separation of exponent
/// and mantissa, with an optional magnitude comparison). Supplied by Stefan 2026-09-26 as part of the
/// complete FP subprocessor rewrite (Node 102's own remarks give the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role.).
///
/// Imports node 201 (a slave it drives directly). <c>f4/ignore</c>/<c>f4/calc</c> choose, per operation,
/// whether node 201 needs to compute a magnitude comparison, by injecting <c>f5/ignore</c> or
/// <c>f5/calc</c> into node 201's dispatch slot (the <c>A[ word ; ]] lit !</c> live-injection idiom used
/// throughout this subprocessor). <c>f4/norm</c> is a generic "shift the injected primitive until the
/// injected test word returns true" loop; <c>f4/mul</c>/<c>f4/div</c> use it to drive node 201's
/// <c>f5/shl2</c>/<c>f5/shl1</c> shift primitives, normalizing each mantissa independently for
/// multiplication or division while accumulating the matching exponent adjustment. <c>f4/main</c> forwards
/// <c>e1 e2</c> onward and relays <c>h1 h2 l1 l2</c> straight through to node 201.
///
/// This is a brand new coordinate for this project -- no earlier revision of node 202 existed here.
/// </summary>
internal static class Node202Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 4 (separation of exponent and mantissa, optional magnitude comparison). Added 2026-09-26.</summary>
  public const int Coordinate = 202;

  /// <summary>
  /// Node 202's full resident F18 source, verbatim from Stefan's 2026-09-26 paste.
  /// </summary>
  public const string Source = """
      ( CVM2 node 202. VM 32 bit floatingpoint step 4.

      separation of exponent and mantissa.

      optional magnitude comparison

        in: e1 e2 h1 h2 l1 l2    {from node 203}
        out: e1 e2               {to node 302}
        out: h1 h2 l1 l2         {to node 201}
      optional:
        in: c             {from node 201}
        out: c            {to node 203}

        c: magnitude compare result
          0x8000  : |f1| >  |f2|
          0       : |f1| == |f2|
          0x20000 : |f1| <  |f2|
      )

      # 201 import

      # down /a
      # right /b
      entry f4/main

      # 0 org

      : f4/sub inv . +


      : f4/gt ( d-c )
        -if
          drop 0x20000 ;       // f1 < f2
        then
        if
          drop 0x8000 ;        // f1 > f2
        then
        // d = 0 -> equal, leave 0
      ;


      : f4/out ( e1 e2 - e1 e2 )
        down a!
        over !
        dup !
      ;


      : f4/ignore // no compare calculation
        ( e1 e2 )
        f4/out
        left a!
        A[ f5/ignore ; ]] lit !
      ;


      : f4/calc   // compare calculation
        ( e1 e2 )

        f4/out

        left a!

        f4/sub
        if
          f4/gt
          !b
          A[ f5/ignore ; ]] lit !
          ;
        then

        A[ f5/calc ; ]] lit !
        @
        !b
      ;


      : f4/norm ( e code - e' )
        a!

        dup 1 xor
        if
          drop
          ;
        then
        drop

        begin
          a !b
          @b

          if
            drop
            ;
          then

          drop
          -1 . +
        again
      ;


      : f4/div ( e1 e2 - e1' e2' )
      : f4/mul ( e1 e2 - e1' e2' )
        left b!

        A[ f5/shl2 ]] lit
        f4/norm

        >r

        A[ f5/shl1 ]] lit
        f4/norm

        r>

        A[ f5/ignore ; ]] lit !b

        f4/out
      ;



      : f4/main
        right b!

        @b               // e1
        @b               // e2

        left a!
        @b !             // h1
        @b !             // h2
        @b !             // l1
        @b !             // l2

        ( e1 e2 )

        @b >r ex

        f4/main
      ;
      """;
}
