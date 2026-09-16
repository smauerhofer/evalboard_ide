namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 401's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the ninth of 17 new nodes
/// in this batch, and the last link in the control chain <see cref="Node403Program"/> started (403
/// controls 402, 402 controls this node). Per its own header: "CVM2 node 401. VM 32 bit floatingpoint
/// stage 5a node. mantissahandling."
///
/// <b>Confirms the field shapes hypothesized in <see cref="Node402Program"/>'s own remarks.</b> The two
/// input shapes -- <c>HHLL0</c> (5 fields: no compare needed, trailing literal 0) and <c>HHLLcdd</c> (7
/// fields: a nonzero compare flag, then high-mantissa delta AND low-mantissa delta, "dd" being exactly
/// two deltas) -- line up exactly with <c>fp5a/main</c>'s own body: read <c>H1 H2 L1 L2</c>, then a
/// compare flag; if set, read the high delta and conditionally the low delta too (same "exponent-first,
/// mantissa as tie-breaker" shape as the rest of this pipeline), run it through the same 0/<c>0x8000</c>
/// comparison convention, and send the result to node 402 (over <c>left</c> -- matching node 402's own
/// "left reaches 401" from ITS side, the same mutual-labeling pattern already seen between 401/402/403).
///
/// <c>fp5a/swap</c> and <c>fp5a/nop</c> have real bodies here (traced through by hand): <c>fp5a/swap</c>
/// writes <c>H2, H1, L2, L1</c> (crossed order) and <c>fp5a/nop</c> writes <c>H1, H2, L1, L2</c> (straight
/// order) -- the same straight-vs-crossed relay <see cref="Node403Program"/>'s own <c>fp3a/send</c>/
/// <c>fp3a/xsend</c> implement, just via stack shuffling instead of two separately-redirected ports.
/// <b><c>fp5a/add</c>/<c>fp5a/sub</c>/<c>fp5a/mul</c>/<c>fp5a/div</c> are all EMPTY (<c>: word ;</c>, no
/// body)</b> -- since node 401 is the end of this control chain and actual mantissa arithmetic happens on
/// the separate data branch (301/501-504/601-604, some not yet pasted), these four exist only so the
/// remote-dispatch mechanism always lands on a valid (if inert) address for those four operations.
///
/// <b>Likely resolves node 402's own "missing dispatch" puzzle -- a hypothesis, not confirmed.</b> Both
/// this node's trailing <c>--l-</c> and node 402's own trailing <c>r---</c> (same structural position:
/// right after <c>down a!</c> and a stack comment, right before the tail-recursive self-call) closely
/// match this project's own ALREADY-CONFIRMED hardware relay-direction notation from the boot-time
/// dispatch tree (e.g. node 407's <c>r&gt; ---u ;</c> for "relay up", <c>-d--</c> for "relay down" -- see
/// `claude/cvm-node407-clbr-cljmp-removal.md` and <c>Node407Program</c>'s own remarks). Reading the same
/// 4-slot convention (slot 2 = down, slot 4 = up, confirmed; by the same pattern, slot 1 = right, slot 3 =
/// left, inferred here): <c>r---</c> = relay RIGHT, <c>--l-</c> = relay LEFT. If correct, node 402 isn't
/// meant to receive-and-execute an incoming operation address itself at all -- it (and, by the same
/// token, this node) may simply RELAY certain words straight through in hardware, bypassing the CPU,
/// which would explain why <c>fp4a/swap</c>/etc. are defined but never called in node 402's own
/// <c>fp4a/main</c>. This project has only ever seen this relay notation used in the FIXED boot-time
/// dispatch tree before, never inside a live, `entry`-declared word body like this -- so this remains a
/// hypothesis worth confirming with Stefan, not a settled fact.
///
/// <b>FLAGGED, not resolved even under that hypothesis: neither <c>r---</c> nor <c>--l-</c> obviously
/// accounts for ALL the pending output values.</b> By the time <c>--l-</c> runs, this node has FOUR
/// live values on the stack (<c>H1 H2 L1 L2</c>, per its own comment) meant to satisfy the header's
/// "out: HHLL" -- but the established relay notation (as seen in the boot dispatch tree) relays a SINGLE
/// word per use, not four. Whether <c>--l-</c>/<c>r---</c> here are a different, multi-word variant of the
/// same notation, or the other three values are written some other way this pasted source doesn't show,
/// is not stated and is not guessed at here. The identical gap in node 402's own <c>r---</c> (only two of
/// its own three live values were accounted for by adjacent port writes) suggests this is the same
/// open question, not two unrelated ones.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- same reasoning as its siblings, compounded by the still-open
/// dispatch/relay question shared with node 402.
/// </summary>
internal static class Node401Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 5a (mantissa handling, end of the control chain), added 2026-09-16.</summary>
  public const int Coordinate = 401;

  /// <summary>
  /// Node 401's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The unresolved
  /// <c>--l-</c> token noted in this class's own remarks above is reproduced exactly as pasted, not
  /// corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 401. VM 32 bit floatingpoint stage 5a node. mantissahandling )
      (
        1. read extended mantissas and comparison data from node 301
        2. select high or low mantissa difference
        3. send mantissa comparison to node 402
        4. wait for operation instruction

        in: HHLL0      {comparison not required}
        in: HHLLcdd    {comparison required}
        out: HHLL
      )

      entry fp5a/main
      # down /a
      # up /b

      # 0 org

      : fp5a/swap >r >r ! ! r> r> ! ! ;
      : fp5a/nop >r >r >r ! r> ! r> ! r> ! ;

      : fp5a/add  ;
      : fp5a/sub  ;
      : fp5a/mul  ;
      : fp5a/div  ;


      : fp5a/main
        @b @b @b @b
        left a!

        @b if                  // compare required
          drop                  // discard c = 0x8000

          @b if                 // high mantissa delta != 0
            @b drop             // retain high delta, consume low delta
          else
            drop @b             // high equal: use low delta
          then
        then

        ( H1 H2 L1 L2 comp )
        !                       // send mantissa comparison to 402

        down a!
        ( H1 H2 L1 L2 )
        --l-
        fp5a/main ;
      """;
}
