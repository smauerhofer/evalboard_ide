namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 301's resident F18 source -- CVM2's floating-point subprocessor, step 5a (mantissa preparation
/// and addition/subtraction). Supplied by Stefan 2026-09-26 as part of the complete FP subprocessor
/// rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design.</b> The previously stored <c>Source</c> for this coordinate
/// was a completely different "stage 5 node. prepare mantissa stream" (<c>fp5/extend</c>/<c>fp5/main</c>)
/// from an earlier iteration of this pipeline; it shared no words, no calling convention, and no
/// relationship with the design below, so it is replaced wholesale rather than diffed against.
///
/// A slave to node 302: has no local entry point of its own and is driven entirely by resident words node
/// 302 injects via <c>A[ word ]] lit !</c>/<c>A[ word ; ]] lit !</c>. <c>f5a/shr2</c> shifts an extended
/// mantissa right by one bit (carry/sign-aware, via <c>+*</c>). <c>f5a/add</c>/<c>f5a/sub</c> perform the
/// 36-bit extended-mantissa add/subtract under <c>+cy</c>, with <c>f5a/sub</c> using the "force carry"
/// idiom (<c>dup dup xor inv dup . + drop</c>) also seen in node 501. <c>f5a/swap</c>/<c>f5a/in</c> read
/// the four mantissa words from node 201 in swapped or natural order; <c>f5a/pass4</c> forwards all four
/// on to node 401 unchanged.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node301Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 5a (mantissa preparation and addition/subtraction), a slave to node 302. Replaced 2026-09-26.</summary>
  public const int Coordinate = 301;

  /// <summary>
  /// Node 301's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 5, prepare mantissa stream" design previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 301. VM 32 bit floatingpoint step 5a.

      this node is a slave of node 302.

      mantissa preparation and addition/subtraction.

        in: H1 H2 L1 L2       {extended mantissa from node 201}

        out: H1 H2 L1 L2      {extended mantissa to node 401}
      alternative
        out: H L              {extended result mantissa to node 401}

      )

      # down /b
      # up /a
      # left /p
      # 0 org


      : f5a/shr2 ( H1 H2 L1 L2 - H1 H2' L1 L2' )
        dup >r
        a!
        >r
        0 over
        . +*
        >r drop drop
        r> r> a
        r> over 2* xor
        over inv and xor
      ;


      +cy

      : f5a/add ( H1 H2 L1 L2 - H L ) // add mantissa
        clc

        . + >r
        . +
        r>
      ;

      : f5a/sub ( H1 H2 L1 L2 - H L ) // subtract mantissa
        dup dup xor inv dup . + drop

        inv . + >r
        inv . +
        r>
      ;

      -cy


      : f5a/swap ( - H2 H1 L2 L1 ) // read swapped mantissa from 201
        @b >r
        @b r>
        @b >r
        @b r>
      ;

      : f5a/in ( - H1 H2 L1 L2 ) // read mantissa from node 201
        @b
        @b
        @b
        @b
      ;

      : f5a/pass4 ( H1 H2 L1 L2 - )
        >r >r >r >r

        up a!

        r> !
        r> !
        r> !
        r> !
      ;
      """;
}
