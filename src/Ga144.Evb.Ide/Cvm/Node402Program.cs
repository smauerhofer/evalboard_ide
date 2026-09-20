namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 402's resident F18 source. Originally pasted 2026-09-16 with a variable-length input protocol
/// (<c>ee0</c> / <c>eecd</c>), a stale header line ("read sign &amp; types from node 302" -- the actual
/// fields were always exponents, not sign/type), an <c>fp4a/gt</c> comparison word, and a body that read a
/// compare-request flag, conditionally selected between the exponent delta and a mantissa comparison read
/// from node 401, and sent the result on.
///
/// <b>REPLACED, 2026-09-20, with Stefan's own final version</b> -- rewritten in the same direction as
/// <see cref="Node401Program"/>'s own same-day rewrite (which lost its own comparison-selection logic):
/// <list type="bullet">
/// <item>Header prose fixed: "read sign &amp; types from node 302" -&gt; "read exponents from node 302" --
/// a real correction of the stale wording the original draft's own remarks had flagged, not just a
/// rewording. Protocol simplified to a single fixed shape, <c>in: ee {from node 302}</c> (down from the
/// two variable <c>ee0</c>/<c>eecd</c> shapes); <c>out: ee {to node 502}</c> unchanged.</item>
/// <item><c>fp4a/gt</c> removed entirely -- the whole three-way sign-comparison word is gone.</item>
/// <item>New word <c>fp4a/swapc</c> (<c>A[ fp5a/swap ]] lit !</c>, no <c>;</c> inside the brackets --
/// distinct from <c>fp4a/swap</c>'s own <c>A[ fp5a/swap ; ]] lit !</c>, which does have one). This exists
/// specifically so <see cref="Node403Program"/>'s own <c>fp3a/add</c> can dispatch through it instead of
/// plain <c>fp4a/swap</c>/<c>fp3a/swap</c> -- confirmed by node 403's own matching 2026-09-20 change (its
/// "normalize so |1| &gt;= |2|" block now calls <c>A[ fp4a/swapc ]] lit !</c> where it used to call
/// <c>A[ fp4a/swap ]] lit !</c>).</item>
/// <item><c>fp4a/main</c> simplified to match node 401: reads <c>e1 e2</c> and relays them straight
/// through -- no more <c>left a!</c>, no compare flag, no <c>fp4a/gt</c> call, no read from node 401.</item>
/// <item><b>Likely bugfix:</b> <c>fp4a/main</c> now opens with <c>up b!</c> before its two <c>@b</c> reads.
/// The prior draft never reset B back to <c>up</c> -- since the body's own tail leaves B pointed at
/// <c>down</c> (for the final <c>!b !b</c> writes) and the word tail-calls itself, every iteration AFTER
/// the first would have read <c>e1</c>/<c>e2</c> from the wrong port (node 502, not node 302). The new
/// <c>up b!</c> at the top of the loop resets B to the input port on every pass, closing what looks like a
/// real "only works on the first call" bug in the original draft.</item>
/// </list>
/// <c>fp4a/nop</c>'s own unexplained trailing <c>over</c> is unchanged, and the <c>r---</c> token stays in
/// the same structural position, same open question as before.
///
/// <b>REVISED AGAIN, 2026-09-20 (later the same day), with Stefan's own further paste of nodes 401 and
/// 402 together</b> -- posted as a matched pair, so cross-checked against <see cref="Node401Program"/>'s
/// own same-day companion revision below:
/// <list type="bullet">
/// <item>The trailing <c>over</c> flagged above as "unexplained" on <c>fp4a/nop</c> has moved: it is now
/// gone from <c>fp4a/nop</c> (<c>A[ fp5a/nop ; ]] lit ! ;</c>) and instead appears on BOTH <c>fp4a/swapc</c>
/// and <c>fp4a/swap</c> (each now ends <c>lit ! over ;</c>). Resolves the asymmetry flagged when this
/// pair was first reviewed.</item>
/// <item><c>fp4a/swapc</c> now dispatches through node 401's new <c>fp5a/swapc</c> (see
/// <see cref="Node401Program"/>) instead of plain <c>fp5a/swap</c> -- <c>A[ fp5a/swapc ]] lit ! over ;</c>,
/// no <c>;</c> inside the brackets, same as before.</item>
/// <item><c>fp4a/main</c>'s tail changed from <c>r--- !b !b fp4a/main ;</c> (write <c>e2</c> then
/// <c>e1</c>, since <c>!b</c> consumes the stack top first) to <c>r--- &gt;r !b r&gt; !b fp4a/main ;</c>
/// (write <c>e1</c> then <c>e2</c> -- the return-stack round trip reverses which of the two reaches the
/// down port first). Comments <c>// e1</c> / <c>// e2</c> added at each <c>!b</c> to make the new order
/// explicit.</item>
/// </list>
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- deferred pending Stefan's own go-ahead for this whole pipeline.
/// </summary>
internal static class Node402Program
{
  /// <summary>The node this program is always deployed to -- CVM2's 32-bit floating-point pipeline, stage 4a (exponent relay, controls node 401), added 2026-09-16, rewritten 2026-09-20 (twice).</summary>
  public const int Coordinate = 402;

  /// <summary>
  /// Node 402's full resident F18 source, verbatim from Stefan's second 2026-09-20 paste (posted
  /// together with <see cref="Node401Program"/>'s own matching revision) -- see this class's own remarks
  /// above for what changed since the first same-day rewrite and why. The unresolved <c>r---</c> token is
  /// reproduced exactly as pasted, not corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 402. VM 32 bit floatingpoint stage 4a node. exponent handling )
      (
      1. read exponents from node 302
      2. wait for instructions on right

        in: ee      {from node 302}
        out: ee     {to node 502}
      )
      # 401 import

      entry fp4a/main
      # left /a
      # up /b

      # 0 org

      : fp4a/swapc A[ fp5a/swapc ]] lit ! over ;

      : fp4a/swap A[ fp5a/swap ; ]] lit ! over ;
      : fp4a/nop A[ fp5a/nop ; ]] lit ! ;

      : fp4a/add A[ fp5a/add ; ]] lit ! ;
      : fp4a/sub A[ fp5a/sub ; ]] lit ! ;
      : fp4a/mul A[ fp5a/mul ; ]] lit ! ;
      : fp4a/div A[ fp5a/div ; ]] lit ! ;

      : fp4a/main
        up b!
        @b
        ( e1 )
        @b
        ( e1 e2 )

        down b!
        ( e1 e2 )

        r---
        >r !b	           // e1
        r> !b           // e2
        fp4a/main ;
      """;
}