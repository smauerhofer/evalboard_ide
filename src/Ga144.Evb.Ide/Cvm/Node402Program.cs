namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 402's resident F18 source -- CVM2's floating-point subprocessor, step 4b (selects the mantissa
/// operation in node 401 and computes the corresponding exponent). Supplied by Stefan 2026-09-26 as part of
/// the complete FP subprocessor rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design.</b> The previously stored <c>Source</c> for this coordinate
/// was a completely different "stage 4a node. exponent handling" (<c>fp4a/*</c> words); it is replaced
/// wholesale.
///
/// A slave to node 403; imports node 401. Injects <c>f5b/pass</c>, <c>f5b/mul</c> or <c>f5b/div</c> into
/// node 401 and computes the matching output exponent: pass-through for <c>f4b/pass</c>; <c>e1+e2-127</c>
/// for <c>f4b/mul</c>; <c>e1-e2+128</c> for <c>f4b/div</c> (both biases correcting for the doubled 127
/// exponent bias carried by each of the two operands). Forwards the result down to node 502.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node402Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 4b (selects node 401's mantissa operation and computes the exponent), a slave to node 403. Replaced 2026-09-26.</summary>
  public const int Coordinate = 402;

  /// <summary>
  /// Node 402's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 4a, exponent handling" design previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 402. VM 32 bit floatingpoint multiply exponent step 4b.

      this node is a slave of node 403.

      node 402 selects the mantissa operation in node 401
      and prepares the exponent for node 502.

        in: e              {ADD/SUB/select from node 302}
      alternative
        in: e2 e1          {MUL/DIV from node 302}

        out: e             {to node 502}

      )

      # 401 import

      # up /b            // node 302
      # down /a          // node 502
      entry f4b/main

      # 0 org


      : f4b/out ( e - )
        down a!
        !
      ;


      : f4b/pass ( - )
        left a!
        A[ f5b/pass ]] lit !
        @b
        f4b/out
      ;


      : f4b/mul ( - )
        left a!
        A[ f5b/mul ]] lit !

        @b @b
        +
        -127 . +

        f4b/out
      ;


      : f4b/div ( - )
        left a!
        A[ f5b/div ]] lit !

        @b inv @b
        . +
        128 . +

        f4b/out
      ;

      : f4b/main
        right a! @ >r
        ex
        f4b/main
      ;
      """;
}
