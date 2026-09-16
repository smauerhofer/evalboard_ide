namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 304's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the second of 17 new nodes
/// in this same batch (see <see cref="Node305Program"/>'s own remarks for the first). Per this source's
/// own header: "CVM2 node 304. VM 32 bit floatingpoint stage 2 node. rearrange data stream and collect
/// return value" -- stage 2 of the same multi-stage pipeline node 305 began (stage 1).
///
/// <b>This header is the first place this project has an explicit statement of this pipeline's own mesh
/// wiring</b> (previously only inferred): node 304 receives its main input from node 305
/// (<c>in: otsehltsehl {from node 305}</c>, over port B -- <c># right /b</c> on this node's own side, so
/// node 305 sits on node 304's own RIGHT), sends its main output onward to node 303
/// (<c>out: ottsseehhll {to node 303}</c>, over port A -- <c># left /a</c>, so node 303 sits on node
/// 304's own LEFT), and has a SEPARATE return path: it receives a return value from node 404
/// (<c>return in: sehl {from node 404}</c>, over the same port A redirected to <c>up</c> inside
/// <c>fp2/return</c> -- consistent with node 404 being node 304's vertical, same-column neighbor) and
/// relays that return value onward to node 305 (<c>return out: sehl {to node 305}</c>, over port B,
/// the same physical link used for the main input -- a single bidirectional connection to 305, not two
/// separate ports). This confirms node 305's own previously-flagged <c>up a!</c> (see
/// <see cref="Node305Program"/>'s own remarks) is very likely just the standard directional port word
/// <c>up</c>, the vertical counterpart to the <c>left</c>/<c>right</c> words this mesh already uses --
/// not an undefined variable -- though which of node 305's own two ports it corresponds to is still not
/// stated by this pair of sources alone. Like node 305, this source has NO <c># import</c> directive and
/// never calls <c>k/pop</c>/<c>k/push</c>/<c>k/leave</c> -- it is a pure port-protocol worker, not part
/// of the existing CVM tag-dispatch tree.
///
/// <c>fp2/order</c> reorders the two decomposed floats node 305 hands it -- grouped by field across both
/// numbers instead of grouped by number (<c>otsehltsehl</c> in, <c>ottsseehhll</c> out: <c>o</c>, then
/// both types, both signs, both exponents, both mantissa-highs, both mantissa-lows) -- matching this
/// node's own header exactly.
///
/// <b>FLAGGED, not silently resolved: <c>fp2/return</c>'s own definition has no closing <c>;</c></b> --
/// it flows directly into the next colon definition, <c>fp2/main</c>. Unlike node 306's/308's own
/// <c>leap</c>/<c>then</c> fallthrough (an explicit, if unexplained, pair of words), this is simply a
/// colon definition with nothing before the next <c>:</c> -- the same bare "missing semicolon" shape as
/// node 305's own <c>fp1/fold</c> (see <see cref="Node305Program"/>'s own remarks), now seen a second
/// time. Reproduced exactly as pasted, not corrected with an inferred <c>;</c>.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- same reasoning as <see cref="Node305Program"/>: this is a
/// port-protocol worker with no dispatched opcode of its own, and its role only makes sense once the
/// rest of this pipeline (the remaining nodes in Stefan's 17-node batch) is known.
/// </summary>
internal static class Node304Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 2 (rearrange/collect), added 2026-09-16.</summary>
  public const int Coordinate = 304;

  /// <summary>
  /// Node 304's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The missing
  /// <c>;</c> on <c>fp2/return</c> noted in this class's own remarks above is reproduced exactly as
  /// pasted, not corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 304. VM 32 bit floatingpoint stage 2 node. rearrange data stream and collect return value )
      (
        in: otsehltsehl   {from node 305}
        out: ottsseehhll  {to node 303}

        return in:  sehl  {from node 404}
        return out: sehl  {to node 305}
      )

      entry fp2/main
      # right /b
      # left /a

      # 0 org


      : fp2/order ( o t1 - t1 t2 o )
        @b                    // o t1 t2

        >r over r>            // o t1 o t2
        over >r >r drop r> r>
                               // ... t1 t2 o
      ;


      : fp2/return
        up a!
        @ !b                  // s
        @ !b                  // e
        @ !b                  // h
        @ !b                  // l


      : fp2/main
        left a!

        @b                    // o
        @b                    // o t1

        @b >r                 // s1
        @b >r                 // e1
        @b >r                 // h1
        @b >r                 // l1

        ( o t1 )

        fp2/order
        ( t1 t2 o )

        !                     // o
        >r ! r> !             // t1 t2

        r> r> r> r> !         // s1
        @b !                  // s2

        !                     // e1
        @b !                  // e2

        !                     // h1
        @b !                  // h2

        !                     // l1
        @b !                  // l2

        fp2/return
      ;
      """;
}
