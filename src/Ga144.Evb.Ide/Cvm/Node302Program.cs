namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 302's resident F18 source -- CVM2's floating-point subprocessor, step 4a (mantissa preparation
/// and addition/subtraction). Supplied by Stefan 2026-09-26 as part of the complete FP subprocessor
/// rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design.</b> The previously stored <c>Source</c> for this coordinate
/// was a completely different "stage 4 node. split exponent/mantissa stream" (<c>fp4/*</c> words); it is
/// replaced wholesale.
///
/// A slave to node 303; imports node 301 and drives it via injection. <c>f4a/swap</c>/<c>f4a/in</c> mirror
/// node 301's <c>f5a/swap</c>/<c>f5a/in</c>. <c>f4a/align</c> repeatedly shifts the smaller-exponent
/// operand's mantissa right (via injected <c>f5a/shr2</c>) until the two exponents match, for ADD/SUB.
/// <c>f4a/add</c>/<c>f4a/sub</c> align then inject <c>f5a/add</c>/<c>f5a/sub</c>, falling through
/// (<c>ahead</c>/<c>then</c>) into the shared <c>f4a/pass2</c> tail that forwards the single resulting
/// exponent onward. <c>f4a/md</c> handles MUL/DIV: no alignment, just relays the raw four-word mantissa
/// packet to node 301's <c>f5a/pass4</c> and the incoming exponent to node 402. <c>f4a/pass1</c> supports
/// MIN/MAX (forwards one operand's exponent unchanged). The combined entries
/// (<c>f4a/iadd</c>/<c>sadd</c>/<c>isub</c>/<c>ssub</c>/<c>first</c>/<c>second</c>/<c>imul</c>/<c>idiv</c>)
/// pair the swap-or-not read with the operation, for node 303 to select by address.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node302Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 4a (mantissa preparation and addition/subtraction), a slave to node 303. Replaced 2026-09-26.</summary>
  public const int Coordinate = 302;

  /// <summary>
  /// Node 302's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 4, split exponent/mantissa stream" design previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 302. VM 32 bit floatingpoint step 4a.

      this node is a slave of node 303.

      mantissa preparation and addition/subtraction.

        in: e1 e2      {exponent from node 202}

        out: e         {ADD/SUB/select to node 402}
      alternative
        out: e2 e1     {MUL/DIV to node 402}

      )

      # 301 import

      # up /b
      # down /a
      entry f4a/main

      # 0 org

      : f4a/swap ( e1 e2 - e2 e1 )
        A[ f5a/swap ]] lit !b
        >r a! r> a
      ;

      : f4a/in ( e1 e2 - e1 e2 )
        A[ f5a/in ]] lit !b
      ;


      : f4a/inc ( a - b ) 1 . + ;

      : f4a/align ( e1 e2 - e )
        // e1 >= e2
        // align exponent for addition or subtraction
        over over xor
        if
          drop f4a/inc
          A[ f5a/shr2 ]] lit !b
          f4a/align ;
        then
        drop
        drop
        ;

      : f4a/add ( e1 e2 - e )
        f4a/align
        A[ f5a/add ]] lit !b
        ahead

      : f4a/sub ( e1 e2 - e )
        f4a/align
        A[ f5a/sub ]] lit !b
      : f4a/pass2 then
        A[ @p a! ! ]] lit !b
        # up lit !b
        A[ ! ]] lit !b
      ;

      : f4a/md ( e1 e2 - e1 )
        up a!
        !
        A[ f5a/pass4 ]] lit !b
      ;

      : f4a/pass1 ( e1 e2 - e1 )
        drop
        A[ drop >r ]] lit !b
        A[ drop r> ]] lit !b
        f4a/pass2
      ;

      // combined entry points for node 303

      : f4a/iadd  f4a/in   f4a/add ;
      : f4a/sadd  f4a/swap f4a/add ;

      : f4a/isub  f4a/in   f4a/sub ;
      : f4a/ssub  f4a/swap f4a/sub ;

      : f4a/first  f4a/in   f4a/pass1 ;
      : f4a/second f4a/swap f4a/pass1 ;

      : f4a/imul  f4a/in   f4a/md ;
      : f4a/idiv  f4a/in   f4a/md ;

      : f4a/main
        down b!
        @b @b
        left b!
        right a! @ >r ex
        up b!
        !b
        f4a/main
      ;
      """;
}
