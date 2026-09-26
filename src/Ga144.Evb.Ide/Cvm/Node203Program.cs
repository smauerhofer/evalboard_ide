namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 203's resident F18 source -- CVM2's floating-point subprocessor, step 3 (opcode decoding, with an
/// optional magnitude compare calculation). Supplied by Stefan 2026-09-26 as part of the complete FP
/// subprocessor rewrite (Node 102's own remarks give the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role.).
///
/// Imports nodes 202, 303 and 403. <c>f3/start</c> makes node 303 hand a selector on to node 403 while
/// node 303 itself stays parked on its down port. <c>f3/with</c> (ADD/SUB/MIN/MAX) arms node 403's "other"
/// path, asks node 202 to compute the magnitude-compare code, loads <c>s1 s2 c</c> into node 303, invokes
/// the selected resident word there, and forwards the resulting sign on to node 403. <c>f3/without</c>
/// (MUL/DIV) skips the magnitude compare entirely, since the caller has already selected the vertical
/// path. The six opcode words (<c>f3/add</c>/<c>sub</c>/<c>min</c>/<c>max</c>/<c>mul</c>/<c>div</c>) wire
/// these onto node 303's matching <c>f3a/*</c> words and node 202's <c>f4/calc</c>/<c>f4/mul</c>/
/// <c>f4/div</c>; <c>f3/main</c> reads the opcode and the eight operand words from node 204 (left),
/// forwards <c>e1 e2 h1 h2 l1 l2</c> down to node 202, and runs the jump table at <c># 0 org</c>.
///
/// This is a brand new coordinate for this project -- no earlier revision of node 203 existed here.
/// </summary>
internal static class Node203Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 3 (opcode decoding, optional magnitude compare calculation). Added 2026-09-26.</summary>
  public const int Coordinate = 203;

  /// <summary>
  /// Node 203's full resident F18 source, verbatim from Stefan's 2026-09-26 paste.
  /// </summary>
  public const string Source = """
      ( CVM2 node 203. VM 32 bit floatingpoint step 3.

      opcode decoding and optional magnitude compare calculation

        in: o s1 s2 e1 e2 h1 h2 l1 l2   {from node 204}
        out: s1 s2                       {to node 303}
        out: e1 e2 h1 h2 l1 l2          {to node 202}
      optional:
        in: c             {from node 202}
        out: c            {to node 303}

        c: magnitude compare result
          0x8000  : |f1| >  |f2|
          0       : |f1| == |f2|
          0x20000 : |f1| <  |f2|
      )

      # 202 import
      # 303 import
      # 403 import

      entry f3/main
      # right /a
      # left /b

      # 6 org


      : f3/sign ( s1 s2 - )
        >r !b r> !b
      ;


      // Make node 303 send a selector through A to node 403.
      // Node 303 remains executing from its down port.

      : f3/start ( selector - )
        A[ @p ! ]] lit !b
        !b
      ;


      // ADD/SUB/MIN/MAX:
      //
      // first start the vertical OTHER path,
      // then ask node 202 to calculate c,
      // then load s1 s2 c into node 303,
      // call the selected resident operation,
      // and finally send its resulting sign through A to node 403.

      : f3/with ( s1 s2 call - )
        # f3b/other lit f3/start

        # f4/calc lit !

        A[ @p @p @p ]] lit !b

        >r
        f3/sign
        @ !b
        r> !b

        A[ ! ]] lit !b
      ;


      // MUL/DIV:
      //
      // the caller has already selected the vertical path.
      // No magnitude comparison is required.

      : f3/without ( s1 s2 call303 addr202 - )
        !

        A[ @p @p ]] lit !b

        >r
        f3/sign
        r> !b

        A[ ! ]] lit !b
      ;


      : f3/sub ( s1 s2 )
        0x8000 xor

      : f3/add ( s1 s2 )
        A[ f3a/add ]] lit f3/with ;

      : f3/min ( s1 s2 )
        A[ f3a/min ]] lit f3/with ;

      : f3/max ( s1 s2 )
        A[ f3a/max ]] lit f3/with ;


      : f3/mul ( s1 s2 )
        # f3b/mul lit f3/start
        A[ f3a/mul ]] lit
        # f4/mul lit
        f3/without ;

      : f3/div ( s1 s2 )
        # f3b/div lit f3/start
        A[ f3a/div ]] lit
        # f4/div lit
        f3/without ;


      : f3/main
        left b!
        @b >r
        ( / o )

        @b @b
        ( s1 s2 / o )

        // pass e1 e2 h1 h2 l1 l2 to node 202

        @b !           // e1
        @b !           // e2
        @b !           // h1
        @b !           // h2
        @b !           // l1
        @b !           // l2

        down b!
        ( s1 s2 / o )

        ex
        f3/main
      ;


      # 0 org

      // opcode jump table

      f3/add ;
      f3/sub ;
      f3/min ;
      f3/max ;
      f3/mul ;
      f3/div ;
      """;
}
