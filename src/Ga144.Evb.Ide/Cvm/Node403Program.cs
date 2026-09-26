namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 403's resident F18 source -- CVM2's floating-point subprocessor, step 3b (multiply/divide control
/// and sign relay). Supplied by Stefan 2026-09-26 as part of the complete FP subprocessor rewrite
/// (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design.</b> The previously stored <c>Source</c> for this coordinate
/// was a completely different "stage 3a node. sign &amp; type handling" (<c>fp3a/*</c> words); it is
/// replaced wholesale.
///
/// A slave to node 303 (reached via node 203); imports node 402 and node 503. <c>f3b/mul</c>/
/// <c>f3b/div</c>/<c>f3b/other</c> each select the matching operation in node 402 (up) and pre-arm node
/// 503 (down) with the address of its own <c>f3c/div</c> or <c>f3c/other</c> resident word (via
/// <c>A[ @p f3c/other ]]</c>-style injection), so that once the sign eventually arrives from node 303 it is
/// forwarded straight into the pre-armed handler in node 503.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node403Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 3b (multiply/divide control and sign relay), a slave to node 303. Replaced 2026-09-26.</summary>
  public const int Coordinate = 403;

  /// <summary>
  /// Node 403's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 3a, sign & type handling" design previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 403. VM 32 bit floatingpoint step 3b.

      node 403 is a slave to node 303.

      multiply/divide control and sign relay.

      operation selection and sign are supplied independently.

        in: operation     {from node 303}
        in: s             {from node 303}

        out: s            {to node 503}

      )

      # 402 import
      # 503 import

      # down /b          // node 503
      entry f3b/main

      # 0 org


      : f3b/mul ( - )
        right a!
        # f4b/mul lit !

        A[ @p f3c/other ]] lit !b

        up a!
        @ !b
      ;


      : f3b/div ( - )
        right a!
        # f4b/div lit !

        A[ @p f3c/div ]] lit !b

        up a!
        @ !b
      ;


      : f3b/other ( - )
        right a!
        # f4b/pass lit !

        A[ @p f3c/other ]] lit !b

        up a!
        @ !b
      ;


      : f3b/main
        up a!
        @ >r
        ex
        f3b/main
      ;
      """;
}
