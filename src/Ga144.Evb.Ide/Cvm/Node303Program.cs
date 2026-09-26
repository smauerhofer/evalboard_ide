namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 303's resident F18 source -- CVM2's floating-point subprocessor, step 3a (operation selection,
/// operand ordering and sign processing). Supplied by Stefan 2026-09-26 as part of the complete FP
/// subprocessor rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design.</b> The previously stored <c>Source</c> for this coordinate
/// was a completely different "stage 3 node. split data stream" (<c>fp3/main</c>); it is replaced
/// wholesale.
///
/// A slave to node 203; imports node 302. <c>f3a/first</c>/<c>f3a/second</c> select and forward one
/// original operand's sign while arming node 302's matching <c>first</c>/<c>second</c> exponent path.
/// <c>f3a/select</c>/<c>f3a/smaler</c>/<c>f3a/larger</c> use the magnitude-compare code relayed from node
/// 202 (via node 203) to pick the smaller or larger-magnitude operand. <c>f3a/add</c> implements the "SUB
/// is ADD with operand 2's sign flipped" convention used throughout this subprocessor (see nodes 203/403):
/// same signs pick the iadd/sadd exponent path by the compare code, opposite signs pick isub/ssub and
/// resolve the sign (with exact cancellation forced to positive zero). <c>f3a/min</c>/<c>f3a/max</c>
/// implement the IEEE tie-breaking rules by sign and magnitude. <c>f3a/mul</c>/<c>f3a/div</c> select node
/// 302's <c>imul</c>/<c>idiv</c> exponent path and combine the two signs with <c>xor</c>.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node303Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 3a (operation selection, operand ordering and sign processing), a slave to node 203. Replaced 2026-09-26.</summary>
  public const int Coordinate = 303;

  /// <summary>
  /// Node 303's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 3, split data stream" design previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 303. VM 32 bit floatingpoint step 3a.

      this node is a slave of node 203.

      operation selection, operand ordering and sign processing.

      node 203 supplies the operation-specific control stream.

        in: s1 s2         {from node 203}
      optional:
        in: c             {from node 203}

        out: s            {to node 403}

        c: magnitude compare result
          0x8000  : |f1| >  |f2|
          0       : |f1| == |f2|
          0x20000 : |f1| <  |f2|
      )

      # 302 import

      # up /a
      # right /b
      entry down

      # 0 org


      // select and forward original operand 1

      : f3a/first ( s1 s2 - s1 )
        # f4a/first lit !b
        drop
      ;


      // select and forward original operand 2

      : f3a/second ( s1 s2 - s2 )
        # f4a/second lit !b
        >r drop r>
      ;


      // select operand 2 iff c == code,
      // otherwise select operand 1.
      //
      // equality of magnitudes may therefore be assigned
      // to operand 1 by choosing code 8000 or 20000.

      : f3a/smaler 0x8000
      : f3a/select ( s1 s2 c code - s )
        xor
        if
          drop f3a/first ;
        then
        drop f3a/second
      ;
      : f3a/larger 0x20000 f3a/select ;




      // ADD and SUB
      //
      // SUB has already been converted to ADD semantics by
      // node 203 by flipping s2.
      //
      // same signs:
      //   c=8000  -> iadd, sign s1
      //   c=0000  -> iadd, sign s1
      //   c=20000 -> sadd, sign s1 (=s2)
      //
      // different signs:
      //   c=8000  -> isub, sign s1
      //   c=0000  -> isub, sign +0
      //   c=20000 -> ssub, sign s2

      : f3a/add ( s1 s2 c - s )
        >r
        over over xor
        if
          // different signs
          drop
          r>

          // 20000 is negative as an 18-bit F18 value
          . -if
            drop
            # f4a/ssub lit !b
            >r drop r>
            ;
          then

          // c is now either 8000 or 0.
          // Both use original orientation.
          >r
          # f4a/isub lit !b
          drop
          r>

          // nonzero c -> operand 1 supplied the sign
          if
            drop ;
          then

          // exact cancellation
          dup xor
          ;
        then

        // same signs
        drop
        r>

        // only c=20000 requires swapped orientation
        . -if
          drop
          # f4a/sadd lit !b
          drop
          ;
        then

        drop
        # f4a/iadd lit !b
        drop
      ;


      // MIN
      //
      // opposite signs:
      //   negative operand wins; c is ignored.
      //
      // same positive signs:
      //   smaller magnitude wins.
      //   operand 2 only when c=8000.
      //
      // same negative signs:
      //   larger magnitude wins.
      //   operand 2 only when c=20000.

      : f3a/min ( s1 s2 c - s )
        >r
        over over xor
        if
          // opposite signs
          drop
          r> drop

          // s2 negative -> operand 2
          if
            f3a/second ;
          then

          // s2 positive -> operand 1
          f3a/first ;
        then

        // same signs
        drop
        over

        // both negative: larger magnitude
        . if
          drop
          r>
          f3a/larger ;
        then

        // both positive: smaller magnitude
        drop
        r>
        f3a/smaler
      ;


      // MAX
      //
      // opposite signs:
      //   positive operand wins; c is ignored.
      //
      // same positive signs:
      //   larger magnitude wins.
      //   operand 2 only when c=20000.
      //
      // same negative signs:
      //   smaller magnitude wins.
      //   operand 2 only when c=8000.

      : f3a/max ( s1 s2 c - s )
        >r
        over over xor
        if
          // opposite signs
          drop
          r> drop

          // s2 negative -> operand 1
          if
            f3a/first ;
          then

          // s2 positive -> operand 2
          f3a/second ;
        then

        // same signs
        drop
        over

        // both negative: smaller magnitude
        if
          drop
          r>
          f3a/smaler ;
        then

        // both positive: larger magnitude
        drop
        r>
        f3a/larger
      ;


      : f3a/mul ( s1 s2 - s )
        # f4a/imul lit !b
        xor
      ;

      : f3a/div ( s1 s2 - s )
        # f4a/idiv lit !b
        xor
      ;
      """;
}
