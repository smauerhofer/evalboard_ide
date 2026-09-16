namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 302's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the fourth of 17 new nodes
/// in this batch (see <see cref="Node305Program"/>/<see cref="Node304Program"/>/<see cref="Node303Program"/>
/// for the first three). Per its own header: "CVM2 node 302. VM 32 bit floatingpoint stage 4 node. split
/// exponent/mantissa stream."
///
/// <b>What this node does, worked out from the header plus the body's own stack shape (not stated this
/// plainly by Stefan, but consistent throughout):</b> it receives the 6-field exponent/mantissa stream
/// node 303 hands it (<c>eehhll</c>, over its one declared port, <c># right /b</c>) and, for each pair
/// (e1/e2, then h1/h2, then l1/l2), DUPLICATES the value it reads: one copy is forwarded onward
/// unchanged (e1/e2 sent <c>up</c>, to node 402, matching <c>out: ee</c>; h1/h2/l1/l2 sent <c>left</c>,
/// to node 401 -- the header says node 301, see below -- matching <c>out: hhll</c>), while the OTHER
/// copy feeds <c>fp4/sub</c> to compute a signed difference (<c>de</c>, <c>dh</c>, <c>dl</c> in turn).
/// Once all three differences are on the stack, the tail of <c>fp4/main</c> checks them in order --
/// exponent difference first, then mantissa-high, then mantissa-low as the final, unconditional
/// tie-breaker -- and sends the first nonzero comparison (via <c>fp4/gt</c>, converting the signed
/// difference into a 0/<c>0x8000</c> flag using the same sign-bit convention node 305 uses for a float's
/// own sign) back to node 303 as <c>c</c>, over the same <c># right /b</c> link. This reads as the
/// magnitude-ordering step of a floating-point add/subtract pipeline (decide which operand is larger, to
/// align mantissas before combining them) -- a plausible reading of the data flow, not a claim Stefan has
/// stated in these terms.
///
/// <b>FLAGGED, not silently resolved.</b>
/// <list type="bullet">
/// <item>Two single-character tokens appear that this project has not resolved: a bare <c>.</c> (inside
/// both <c>fp4/sub</c> and <c>fp4/inc</c>, sandwiched between an arithmetic op and its neighbor) and a
/// bare <c>..</c> (right after the two <c>&gt;r</c>s near the end of <c>fp4/main</c>) -- the latter is the
/// same token already flagged, unresolved, in <see cref="Node305Program"/>'s own <c>fp1/decompose</c>.
/// Whether the single- and double-dot tokens are related is not stated and is not guessed at here.</item>
/// <item><c>fp4/sub</c>'s own definition (<c>: fp4/sub inv . +</c>) has no closing <c>;</c> -- it flows
/// directly into <c>fp4/inc</c>, the same missing-semicolon fallthrough idiom already seen in
/// <see cref="Node305Program"/>'s <c>fp1/fold</c> and <see cref="Node304Program"/>'s <c>fp2/return</c>.
/// Numerically, <c>fp4/sub</c> falling through into <c>fp4/inc</c> (<c>inv . + [fallthrough] 1 . + ;</c>)
/// is at least CONSISTENT with a standard two's-complement subtraction idiom (invert, add, then add 1) --
/// offered here only as a plausible reading given the surrounding arithmetic makes sense that way, not as
/// a confirmed fact, since the <c>.</c> token itself is still unresolved.</item>
/// <item><b>The header says node 302's mantissa output goes "to node 301," but does not otherwise name
/// which physical port reaches it beyond the body's own <c>left a!</c>.</b> Taken together with
/// <see cref="Node303Program"/>'s own flagged findings, the direction words in this pipeline do not
/// appear to follow one consistent global convention: node 303's own <c># left /b</c> and node 304's own
/// left-labeled link to it are called "left" by BOTH sides (not mirrored), while node 302's own
/// <c># right /b</c> link to node 303 is likewise called "right" by both 302 (this node) and node 303
/// (whose own body reaches node 302 via <c>right a!</c> -- see <see cref="Node303Program"/>). With three
/// nodes now showing the same both-sides-use-the-same-word pattern, the most likely reading is that these
/// direction words are just this pipeline's own per-node port aliases (each one bound to whichever of a
/// node's four physical ports Stefan wired it to), not global compass directions consistent across nodes
/// -- offered as a working hypothesis, not confirmed by Stefan and not treated as settled here.</item>
/// </list>
///
/// Like its three siblings so far, this source has no <c># import</c> directive and never calls
/// <c>k/pop</c>/<c>k/push</c>/<c>k/leave</c> -- a pure port-protocol worker, not part of the existing CVM
/// tag-dispatch tree. <c>fp4/inc</c>, like node 305's own <c>fp1/or</c>, is never called by name anywhere
/// in this source (it is only ever reached via <c>fp4/sub</c>'s own fallthrough).
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- same reasoning as its siblings.
/// </summary>
internal static class Node302Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 4 (split exponent/mantissa, compare magnitudes), added 2026-09-16.</summary>
  public const int Coordinate = 302;

  /// <summary>
  /// Node 302's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The missing <c>;</c>
  /// on <c>fp4/sub</c> and the unresolved <c>.</c>/<c>..</c> tokens noted in this class's own remarks
  /// above are reproduced exactly as pasted, not corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 302. VM 32 bit floatingpoint stage 4 node. split exponent/mantissa stream )
      (
        in: eehhll      {from node 303}
        out: ee         {to node 402, pass exponent}
        out: hhll       {to node 301, pass mantissa}
        out: c          {to node 303, return compare result}
      )
      entry fp4/main
      # right /b

      # 0 org

      : fp4/sub inv . +
      : fp4/inc 1 . + ;

      : fp4/gt ( d-c )
        -if
          drop 0 ;
        then
        if
          drop 0x8000 ;
        then
        // d = 0, leave it
      ;

      : fp4/result
        fp4/gt
        !b
      : fp4/main

        up a!
        @b dup ! // e1
        @b dup ! // e2
        fp4/sub
        ( de ) // difference of exponenets

        left a!
        @b dup ! // h1
        @b dup ! // h2
        fp4/sub
        ( de dh ) // difference of high
        @b dup ! // l1
        @b dup ! // l2
        fp4/sub
        ( de dh dl ) // difference of low
        // send back compare output
        >r >r .. if fp4/result ; then
        r> if fp4/result ; then
        r> fp4/result ;
      """;
}
