namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 104's resident F18 source -- CVM2's floating-point subprocessor, special-case step 1 (special
/// operation dispatcher). Supplied by Stefan 2026-09-26 as part of the complete FP subprocessor rewrite
/// (see <see cref="Node102Program"/>'s own remarks for the full context and the list of nodes involved).
///
/// Routes to node 103 (ADD/SUB/MUL/DIV, left) or node 105 (MIN/MAX, right). <c>fs1/main</c> reads <c>o</c>
/// and dispatches via the address-token-on-return-stack-plus-<c>ex</c> idiom used throughout this
/// subprocessor, with <c>T</c> kept on the data stack as the argument. Each <c>fs1/add</c>/<c>sub</c>/
/// <c>min</c>/<c>max</c>/<c>mul</c>/<c>div</c> selects the right neighbor's port and injects the matching
/// entry (<c>fs3/add</c>, <c>fs2/min</c>, etc.) via <c>fs1/pass</c>, which forwards <c>T</c> plus the
/// remaining operand words through and reads the result back. <c>fs1/pass</c> also detects the canonical
/// qNaN sentinel (<c>S=0x0001</c>, tested via <c>0x7fff and</c>) coming back and substitutes the fixed
/// <c>e=0xFF, h=0x40 (quiet bit only), l=0, s=0</c> NaN encoding instead of passing the callee's own
/// leftover stack values through.
///
/// This is a brand new coordinate for this project -- no earlier revision of node 104 existed here.
/// </summary>
internal static class Node104Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, special step 1 (special operation dispatcher, routes to node 103 or node 105). Added 2026-09-26.</summary>
  public const int Coordinate = 104;

  /// <summary>
  /// Node 104's full resident F18 source, verbatim from Stefan's 2026-09-26 paste.
  /// </summary>
  public const string Source = """
      ( CVM2 node 104. VM 32 bit floatingpoint special step 1. special operation dispatcher

      MIN/MAX ar handled in node 105
      ADD/SUB/MUL/DIV are handled in node 103

        in:    T o t1 t2 s1 s2 e1 e2 h1 h2 l1 l2  {from node 204}
        out:   T t1 t2 s1 s2 e1 e2 h1 h2 l1 l2    {to node 103 or 105}
        in:    S e h l               {from node 103 or 105}
        out:   s e h l               {to node 204}

        S special sign:
          0: positive
          0x0001: qNaN
          0x8000: negative

      )

      # 105 import
      # 103 import
      # 6 org
      # up /b
      # left /a
      entry fs1/main

      : fs1/pass ( T call ) ! ! 9 for @b ! unext
        @ // S, might be a qNaN
        dup 0x7fff and if // canonical qNaN
          @ @ @ // fetch reimaining data
          dup xor
          dup >r
          0xff
          0x40
          r> ;
        then
        drop
        @
        @
        @
      ;

      : fs1/add ( T ) A[ fs3/add ]] lit fs1/pass ;
      : fs1/sub ( T ) A[ fs3/sub ]] lit fs1/pass ;
      : fs1/min ( T ) right a! A[ fs2/min ]] lit fs1/pass ;
      : fs1/max ( T ) right a! A[ fs2/max ]] lit fs1/pass ;
      : fs1/mul ( T ) A[ fs3/mul ]] lit fs1/pass ;
      : fs1/div ( T ) A[ fs3/div ]] lit fs1/pass ;

      : fs1/main
        @b @b >r
        ( T )
        ex
        ( s e h l )
        left a!
        >r >r >r !b r> !b r> !b r> !b
        fs1/main
      ;

      # 0 org

      fs1/add ;
      fs1/sub ;
      fs1/min ;
      fs1/max ;
      fs1/mul ;
      fs1/div ;
      """;
}
