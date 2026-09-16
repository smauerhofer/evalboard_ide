namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 303's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the third of 17 new nodes
/// in this batch (see <see cref="Node305Program"/>/<see cref="Node304Program"/> for the first two). Per
/// its own header: "CVM2 node 303. VM 32 bit floatingpoint stage 3 node. split data stream" -- stage 3 of
/// the same pipeline, and the first node in it whose header describes MORE than a single in/out pair:
/// it takes the 11-field stream node 304 hands it (<c>ottsseehhll</c>) and splits it in two, sending the
/// opcode/type/sign fields (<c>ottss</c>, 5 fields) to node 403 and the exponent/mantissa fields
/// (<c>eehhll</c>, 6 fields) to node 302 -- plus a third, single-word channel (<c>c</c>) exchanged with
/// both neighbors (read from node 302, forwarded on to node 403). Body confirms this exactly: all 11
/// <c>@b</c> reads (5 then 6) match <c>ottsseehhll</c>'s own 11 letters one-for-one, the first 5 writes
/// (through <c>a</c>, initially bound <c>up</c> per this node's own <c># up /a</c> directive) match
/// <c>ottss</c>, and the last 6 (through <c>a</c>, reassigned <c>right a!</c> mid-body) match
/// <c>eehhll</c>.
///
/// <b>FLAGGED, not silently resolved: two apparent direction inconsistencies with
/// <see cref="Node304Program"/>'s own header, reproduced as-is.</b>
/// <list type="bullet">
/// <item>This node's own <c># left /b</c> directive names its (sole) link to node 304 as its own LEFT
/// port. But node 304's own header names that exact same link as ITS OWN left port too (node 304 reads
/// its main input from node 305 over the port it calls "right", and sends output to node 303 over the
/// port it calls "left" -- see <see cref="Node304Program"/>'s own remarks). Every prior pair of linked
/// nodes in this project had opposite left/right labels for a shared link (A calls it left, B calls the
/// same wire right); here BOTH sides call it "left." Not resolved or explained here.</item>
/// <item>Within this node, <c>right a!</c> is confirmed (via the header's own "from/to node 302" labels)
/// to reach node 302 -- the LOWER-numbered column neighbor. But node 304's own "right" reached node 305,
/// the HIGHER-numbered column neighbor. So "right" points toward a lower column here but a higher column
/// there, for two nodes in the very same row. Possibly a real, non-uniform hardware wiring quirk of this
/// mesh (this project has already seen relay directions and tag ranges vary node-by-node in ways not
/// otherwise explained); possibly something else. Not guessed at here -- reproduced exactly as pasted.
/// </item>
/// </list>
///
/// Like <see cref="Node305Program"/> and <see cref="Node304Program"/>, this source has no
/// <c># import</c> directive and never calls <c>k/pop</c>/<c>k/push</c>/<c>k/leave</c> -- a pure
/// port-protocol worker, not part of the existing CVM tag-dispatch tree. Unlike its two siblings so far,
/// <c>fp3/main</c> tail-calls itself as its own last action before its closing <c>;</c> -- an explicit,
/// self-contained infinite service loop, rather than a single pass left for something else to re-invoke.
/// Whether stages 1/2 are meant to loop the same way (their own sources simply end after one pass) is not
/// stated and is not guessed at here.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- same reasoning as its two siblings: no dispatched opcode of its
/// own, and this pipeline's full shape is still being pasted.
/// </summary>
internal static class Node303Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 3 (split), added 2026-09-16.</summary>
  public const int Coordinate = 303;

  /// <summary>
  /// Node 303's full resident F18 source, verbatim from Stefan's 2026-09-16 paste.
  /// </summary>
  public const string Source = """
      ( CVM2 node 303. VM 32 bit floatingpoint stage 3 node. split data stream )
      (
        in: ottsseehhll   {from node 304}
        out: ottss        {to node 403}
        out: eehhll       {to node 302}
        in: c             {from node 302}
        out: c            {to node 403}
      )
      entry fp3/main
      # up /a
      # left /b

      # 0 org

      : fp3/main
        @b ! // o operation
        @b ! // t1
        @b ! // t2
        @b ! // s1
        @b ! // s2
        right a!
        @b ! // e1
        @b ! // e2
        @b ! // h1
        @b ! // h2
        @b ! // l1
        @b ! // l2
        @ // read c
        up a!
        ! // c
        fp3/main
      ;
      """;
}
