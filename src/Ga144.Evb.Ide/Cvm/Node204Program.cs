namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 204's resident F18 source -- CVM2's floating-point subprocessor, step 2 (reorders the incoming
/// packet and splits normal processing from the special-case shortcut). Supplied by Stefan 2026-09-26 as
/// part of the complete FP subprocessor rewrite (Node 102's own remarks give the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role.).
///
/// <c>f2/or</c> tests whether either operand's type word is nonzero (zero/infinity/qNaN/sNaN) using a
/// De Morgan <c>inv</c>/<c>and</c>/<c>inv</c> pair. Normal case (both operands finite nonzero): forwards
/// <c>o s1 s2 e1 e2 h1 h2 l1 l2</c> on to node 203. Special case: forwards the full
/// <c>T o t1 t2 s1 s2 e1 e2 h1 h2 l1 l2</c> packet up to node 104, waits for <c>s e h l</c> back, and sends
/// that result straight down to node 304 -- the same shortcut node 304's own header documents ("the data
/// can also come from node 204").
///
/// This is a brand new coordinate for this project -- no earlier revision of node 204 existed here.
/// </summary>
internal static class Node204Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 2 (reorder and processing split between the normal pipeline and the special-case shortcut). Added 2026-09-26.</summary>
  public const int Coordinate = 204;

  /// <summary>
  /// Node 204's full resident F18 source, verbatim from Stefan's 2026-09-26 paste.
  /// </summary>
  public const string Source = """
      ( CVM2 node 204. VM 32 bit floatingpoint step 2.

      reorder and processing split

      this node prepares the parameter for the pipeline or returns the result if a special processing must be used.

        in:    otsehltsehl          {from node 205}

        if normal

        out:   o s1 s2 e1 e2 h1 h2 l1 l2            {to node 203}

        or if a special processing

        out:   T o t1 t2 s1 s2 e1 e2 h1 h2 l1 l2    {to node 104}
        in:    s e h l              {from node 104}
        out:   s e h l              {to node 304}

        where T = t1 or T2

      )

      # 0 org
      # right /b
      # left /a
      entry f2/main

      : f2/or >r inv r> inv and inv ;

      : f2/main

        // Read:
        //
        // o t1 s1 e1 h1 l1

        @b @b @b @b @b @b

        // Put first packet on return stack.

        >r >r >r >r

        ( o t1 / l1 h1 e1 s1 )

        // t2

        @b

        ( o t1 t2 / l1 h1 e1 s1 )

        over over f2/or
        if // special processing
          up a!
          !                       // T
          >r >r !                 // o
          r> !                    // t1
          r> !                    // t2

          r> !                    // s1
          @b !                    // s2

          r> !                    // e1
          @b !                    // e2

          r> !                    // h1
          @b !                    // h2

          r> !                    // l1
          @b !                    // l2

          @ @ @ @
          ( s e h l )

          down a!

          >r >r >r !              // s    
          r> !                    // e
          r> !                    // h
          r> !                    // l

          left a!
          f2/main ;
        then // normal processing
        drop

        drop drop                 // remove t2 and t1
        !                         // o

        r> !                      // s1
        @b !                      // s2

        r> !                      // e1
        @b !                      // e2

        r> !                      // h1
        @b !                      // h2

        r> !                      // l1
        @b !                      // l2

        f2/main
      ;
      """;
}
