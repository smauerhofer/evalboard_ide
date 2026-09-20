namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 401's resident F18 source. Originally pasted 2026-09-16 with a variable-length input protocol
/// (<c>HHLL0</c> when no compare was needed, <c>HHLLcdd</c> when one was) and a body that read a compare
/// flag, conditionally selected between a high or low mantissa delta, ran the result through the same
/// 0/<c>0x8000</c> comparison convention used throughout this pipeline, and sent it on to node 402 --
/// mirroring node 403's own control-chain shape.
///
/// <b>REPLACED, 2026-09-20, with Stefan's own final version</b> -- pasted as part of a side-by-side
/// comparison of this whole pipeline against what was on file, and re-confirmed a second time (with an
/// updated header) once the mismatch below was flagged. This final version drops the mantissa-comparison
/// selection logic ENTIRELY:
/// <list type="bullet">
/// <item>Input/output protocol simplified to a single fixed shape, <c>in: HHLL {extended mantissa from
/// node 301}</c> / <c>out: HHLL {extended mantissa to node 501}</c> -- no more compare flag, no more
/// variable length. This is also the first time node 501 is named as this node's own destination
/// anywhere in this pipeline's sources.</item>
/// <item><c>fp5a/main</c> now just reads <c>H1 H2 L1 L2</c> and relays them straight through via
/// <c>--l-</c> -- no <c>left a!</c>, no compare-flag read, no delta selection, no comparison write, no
/// <c>down a!</c>. The header's own numbered description was updated to match: "1. read extended
/// mantissas from node 301 / 2. wait for operation instruction" (down from the original draft's four
/// steps, which explicitly described the now-removed selection/send logic).</item>
/// <item><c>fp5a/add</c>/<c>sub</c>/<c>mul</c>/<c>div</c> no longer each have their own independently-
/// closed, empty body (<c>: fp5a/add ;</c> etc.) -- none of the four closes with its own <c>;</c> any
/// more; all four fall straight through into <c>fp5a/nop</c>'s real body. Dispatching to any of the five
/// (<c>add</c>/<c>sub</c>/<c>mul</c>/<c>div</c>/<c>nop</c>) now performs the exact same straight relay,
/// instead of add/sub/mul/div being true no-ops as before.</item>
/// <item><c>fp5a/swap</c> and <c>fp5a/nop</c> reformatted with per-word comments; same computation as
/// before (crossed vs. straight write order).</item>
/// </list>
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- deferred pending Stefan's own go-ahead for this whole pipeline.
/// </summary>
internal static class Node401Program
{
  /// <summary>The node this program is always deployed to -- CVM2's 32-bit floating-point pipeline, stage 5a (mantissa relay, end of the control chain), added 2026-09-16, rewritten 2026-09-20.</summary>
  public const int Coordinate = 401;

  /// <summary>
  /// Node 401's full resident F18 source, verbatim from Stefan's 2026-09-20 paste (his final,
  /// simplified rewrite -- see this class's own remarks above for what changed from the original
  /// 2026-09-16 draft and why).
  /// </summary>
  public const string Source = """
      ( CVM2 node 401. VM 32 bit floatingpoint stage 5a node. mantissa handling )
      (
        1. read extended mantissas from node 301
        2. wait for operation instruction

        in: HHLL       {extended mantissa from node 301}
        out: HHLL      {extended mantissa to node 501}
      )

      entry fp5a/main
      # down /a
      # up /b

      # 0 org

      : fp5a/swap
        ( H1 H2 L1 L2 )
        >r >r
        !         //  H2
        !         //  H1
        r> r>
        !         // L2
        !         // L1
        ;

      : fp5a/add
      : fp5a/sub
      : fp5a/mul
      : fp5a/div
      : fp5a/nop
        >r
        >r
        >r
        !         // H1
        r> !      // H2
        r> !      // L1
        r> !      // L2
        ;



      : fp5a/main
        @b
        @b
        @b
        @b
        ( H1 H2 L1 L2 )
        --l-
        fp5a/main ;
      """;
}