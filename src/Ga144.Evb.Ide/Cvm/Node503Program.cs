namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 503's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the eleventh of 17 new
/// nodes in this batch. Per its own header: "CVM2 node 503. VM 32 bit floatingpoint stage 3b node.
/// implement add and multiply."
///
/// <b>Resolves node 403's own previously-open question about where its "down"-bound <c>ottss</c> output
/// goes.</b> This node's own <c>in: ottss {from node 403}</c> confirms it: node 403's control decisions
/// (the possibly-reordered <c>o t1 t2 s1 s2</c>) are relayed here, not back to node 303 -- see
/// <see cref="Node403Program"/>'s own remarks, which had guessed exactly this. Node 303 (stage 3) thus
/// splits into a "3a" control chain (403-&gt;402-&gt;401) that decides comparisons and operand order, AND
/// this "3b" chain (503-&gt;502-&gt;...) that carries out the actual arithmetic, structurally parallel to
/// how the numeric data itself flows down the separate 302-&gt;301 branch.
///
/// <b>Structurally mirrors node 403's own shape closely</b> (opcode-driven jump table dispatched via
/// <c>ex</c>, a "remote control" send of an imported word's own compiled address to the next node down
/// the chain, <c># N import</c>), but simpler: node 503 doesn't itself compute any comparisons (403/402/
/// 401 already resolved those), so its own <c>fp3b/main</c> just relays <c>o t1 t2 s1 s2</c> onward
/// (to node 603, per its own <c>out: ottss</c>) AND separately tells node 502 which operation to run.
///
/// <b>Only <c>add</c>, <c>sub</c>, and <c>mul</c> have real targets</b> (<c>A[ fp4b/add ; ]]</c>,
/// <c>A[ fp4b/sub ; ]]</c>, <c>A[ fp4b/mul ; ]]</c>, all imported from node 502) -- <c>min</c>/<c>max</c>/
/// <c>div</c>/<c>nop</c>/<c>swap</c> all use an EMPTY <c>A[ ; ]]</c> (the address of a bare <c>;</c>,
/// i.e. an inert do-nothing target). Given this node's header claims to "implement add and multiply"
/// (not sub), and floating-point subtraction is already implemented as add-with-a-flipped-sign everywhere
/// else in this pipeline (see node 403's own <c>fp3a/sub</c>/<c>fp3a/add</c>), <c>fp4b/sub</c> is most
/// likely itself just a thin alias for <c>fp4b/add</c> on node 502's own side, not a third real
/// implementation -- consistent with the header's own "add and multiply" framing, though not confirmed
/// until node 502 is pasted. Why <c>div</c> specifically has no real target (rather than being deferred to
/// yet another node down the chain, the way <c>min</c>/<c>max</c>/<c>nop</c>/<c>swap</c> plausibly are,
/// since this branch doesn't need to reorder or compare anything) is not stated and is not guessed at
/// here -- possibly simply not implemented yet.
///
/// <b>FLAGGED: the first explicit runtime override of a port register's OWN directive default anywhere
/// in this batch.</b> This node's header declares <c># down /b</c>, but <c>fp3b/main</c>'s very first
/// action is <c>up b!</c> -- immediately overriding it, before <c>b</c> is ever read. Every other node so
/// far has only ever reassigned <c>a</c> at runtime, relying on <c>b</c>'s own directive default
/// unchanged. The most likely reading is that the <c># down /b</c> directive itself is stale/incorrect
/// (perhaps copied from a similar sibling node where <c>b</c> really was <c>down</c>) and the explicit
/// <c>up b!</c> is Stefan's own working code silently overriding it -- offered as a plausible reading,
/// not a confirmed one; reproduced exactly as pasted either way. Once reassigned, <c>b</c> stays
/// <c>up</c> for the rest of the loop body -- its own <c>down</c> default is never actually used for
/// anything shown here.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- same reasoning as its siblings: this branch isn't complete without
/// node 502 (imported here but not yet pasted).
/// </summary>
internal static class Node503Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 3b (implements add/multiply, controls node 502), added 2026-09-16.</summary>
  public const int Coordinate = 503;

  /// <summary>
  /// Node 503's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The <c>up b!</c>
  /// override of the node's own <c># down /b</c> directive, noted in this class's own remarks above, is
  /// reproduced exactly as pasted, not corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 503. VM 32 bit floatingpoint stage 3b node. implement add and multiply )
      (
        calculates result for add and multiply.

        in: ottss      {from node 403}
        out: ottss     {to node 603}
      )
      # 502 import


      entry fp3b/main
      # down /b
      # up /a

      # 8 org

      : fp3b/add A[ fp4b/add ; ]] lit ! ;
      : fp3b/sub A[ fp4b/sub ; ]] lit ! ;
      : fp3b/min A[ ; ]] lit ! ;
      : fp3b/max A[ ; ]] lit ! ;
      : fp3b/mul A[ fp4b/mul ; ]] lit ! ;
      : fp3b/div A[ ; ]] lit ! ;
      : fp3b/nop A[ ; ]] lit ! ;
      : fp3b/swap A[ ; ]] lit ! ;


      : fp3b/main up b!
        @b dup ! >r // o
        @b !        // t1
        @b !        // t2
        @b !        // s1
        @b !        // s2
        right a!    // control node 502
        ex   // call jump table
        fp3b/main ;


      # 0 org // jump table

      fp3b/add ;
      fp3b/sub ;
      fp3b/min ;
      fp3b/max ;
      fp3b/mul ;
      fp3b/div ;
      fp3b/nop ;
      fp3b/swap ;
      """;
}
