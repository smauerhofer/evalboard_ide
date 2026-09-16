namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 602's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the sixteenth of 17 new nodes
/// in this batch. Per its own header: "CVM2 node 602. VM 32 bit floatingpoint stage 4c node. normalize
/// result" -- continuing the "3c" sign/normalization chain <see cref="Node603Program"/> started (603
/// controls 602, and this node in turn imports node 601, per the same "control chain imports and controls
/// the next node down" pattern already seen on both other control branches).
///
/// <b>Likely resolves the multi-node "<c>r---</c>"/"<c>--l-</c>" mystery, for the first time with a direct
/// textual label from Stefan.</b> <c>fp4c/main</c>'s own <c>r---  // give control to node 603</c> is the
/// first place in this whole batch this notation is explicitly captioned. This confirms the family
/// (<c>r---</c>/<c>--l-</c>, and by extension the boot-dispatch tree's own <c>---u</c>/<c>-d--</c> that
/// first suggested this reading -- see `claude/cvm-node407-clbr-cljmp-removal.md`) really is a hardware
/// control/handoff signal toward a specific fixed direction (here, right), most plausibly a
/// synchronization "go ahead" between two independently-clocked F18 cores rather than a literal shared
/// program-counter jump (GA144 nodes each run their own PC; there is no shared execution to hand off in
/// the literal sense). Notably, this token executes here while register <c>a</c> is bound to <c>left</c>
/// (reassigned just before it, for reading node 601), yet the comment says it targets node 603 (this
/// node's <c>right</c>, matching <c>b</c>'s own declared directive default) -- confirming these relay
/// tokens carry their OWN fixed direction baked into the token's shape, entirely independent of whatever
/// the <c>a</c>/<c>b</c> registers currently point to. This does not, however, fully close the question:
/// exactly what "control" is hand off, or why it happens here (mid-<c>fp4c/main</c>, before this node's
/// own normalized E/H/L are ready to send), is still not stated -- the most plausible reading offered here
/// is that it lets node 603 begin its own next cycle (reading fresh <c>ottss</c> from node 503 and
/// dispatching) while this node continues normalizing in parallel, with node 603's own later <c>right</c>-
/// bound reads simply blocking until this node's <c>!b</c> writes catch up -- offered as a plausible,
/// not confirmed, reading.
///
/// <b>Cross-reference update for <see cref="Node603Program"/>'s own open "up vs right" port question:</b>
/// this node's <c>right</c> (matching <c>b</c>'s declared default, <c># right /b</c>) demonstrably reaches
/// node 603, both via the header's own "out: E H L {to node 603}" and via the "give control to node 603"
/// comment landing on a token that targets the direction named "right". By this batch's own repeatedly-
/// confirmed "adjacent nodes call their shared link by the SAME word" pattern, this makes it MORE likely
/// that node 603's own analogous link to this node is ALSO called <c>right</c> (matching that file's own
/// hypothesis), and correspondingly makes it LESS likely that node 603's <c>up</c> genuinely reaches this
/// node -- suggesting node 603's own "<c>ex</c> ... controls 602" comment is the imprecise element there
/// (in the same vein as node 402's stale header prose or node 604's likely "404"/"504" mix-up), not a
/// deeper hardware mystery. Still not proven either way; <see cref="Node603Program"/>'s own remarks are
/// left as originally written rather than retroactively "corrected" here.
///
/// <b><c>fp4c/shl</c>, <c>fp4c/shr</c>, and <c>fp4c/norm</c> together give this project's clearest, most
/// complete confirmation yet of the standard mantissa-normalization algorithm, and confirm a NEW variant of
/// the add-with-carry idiom.</b> <c>fp4c/shl</c> decrements E (<c>0 inv . +</c>, the same NOT(x)=-x-1
/// idiom used for subtraction elsewhere) and then doubles the combined H:L via <c>dup . + &gt;r / dup . +</c>
/// -- i.e. <c>X+X</c> as a carry-chained left-shift-by-one, a simpler sibling to the <c>+*</c>-based
/// right-shift trick already confirmed in nodes 404/501/603. <c>fp4c/shr</c> mirrors those nodes' own
/// right-shift routines exactly (increment E, <c>0 over . +*</c> shift). <c>fp4c/norm</c> is a fully
/// traced, classic normalize-the-mantissa loop: shift right (and increment E) while any bit above the
/// "overflow bit" (<c>0x200</c>) is set; if the "hidden bit" (<c>0x100</c>) is already set, it's done;
/// otherwise shift left (and decrement E) while H or L is still nonzero; and if H and L both reach zero,
/// the result is an exact zero, left as-is. Every branch's drop-count again matches the "<c>if</c> doesn't
/// consume" hypothesis exactly, and the bare <c>..</c> token reappears (before the H!=0 check) in its by-now
/// familiar, functionally-inert position.
///
/// <b>Resolves node 603's own "controls division path" phrasing.</b> <c>fp4c/div</c> is the ONLY one of
/// this node's five operation words with distinct behavior (<c>A[ fp5c/div ; ]] lit !</c>, remote-
/// controlling node 601's own division routine); <c>fp4c/add</c>, <c>fp4c/sub</c>, <c>fp4c/mul</c>, and
/// <c>fp4c/pass</c> all fall through into ONE shared body (<c>A[ fp5c/pass ; ]] lit !</c>) -- the widest
/// use of the missing-<c>;</c> fallthrough idiom in this batch (four colon headers sharing one body). This
/// makes complete architectural sense: add/sub/mul's real mantissa arithmetic already happened back at
/// node 501, and min/max/nop/swap's operand was already selected by node 403, so this normalize stage only
/// needs to pass those results through node 601 unchanged -- ONLY division's actual long-division mantissa
/// math still needs to happen at node 601 itself, which is exactly what node 603's own "controls division
/// path" header line was describing.
///
/// <b>FLAGGED, not resolved -- the SAME "defined but never locally dispatched" gap seen in nodes 402 and
/// 502, now a third time.</b> <c>fp4c/div</c>/<c>add</c>/<c>sub</c>/<c>mul</c>/<c>pass</c> are never called
/// anywhere in this pasted <c>fp4c/main</c> -- they exist only as remote-dispatch targets for the literal
/// addresses <see cref="Node603Program"/>'s own <c>fp3c/add</c>/<c>sub</c>/<c>pass</c>/<c>mul</c>/<c>div</c>
/// send (matching these exact names). How node 602 actually receives and executes that incoming address --
/// whether via the same still-unexplained mechanism flagged for nodes 402/502, or via the "give control"
/// relay tokens discussed above -- is not shown in any of the three receiving nodes' own pasted sources so
/// far. This is now a consistent, batch-wide open question rather than an isolated one, and is flagged as
/// such rather than guessed at further.
///
/// <b>Port mapping, all consistent with this batch's established conventions:</b> <c>up</c> (undeclared in
/// the header, used only transiently at the very top of <c>fp4c/main</c>) reads the single <c>E</c> value
/// from node 502 (matching the header's own "in: E {from node 502}"); <c>left</c> (also undeclared) reads
/// <c>H L</c> from node 601 (the imported node); <c>right</c> (the one declared default, <c># right /b</c>)
/// sends the normalized <c>E H L</c> onward to node 603.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired into
/// the CVM instruction set</b> -- same reasoning as its siblings; node 601 (imported here, and the batch's
/// final remaining node) has not yet been pasted.
/// </summary>
internal static class Node602Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 4c (normalizes the result exponent/mantissa, controls node 601's division path), added 2026-09-16.</summary>
  public const int Coordinate = 602;

  /// <summary>
  /// Node 602's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The <c>..</c> token and
  /// the still partially-open "receive and dispatch" mechanism noted in this class's own remarks above are
  /// reproduced exactly as pasted, not corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 602. VM 32 bit floatingpoint stage 4c node. normalize result )
      (
        normalizes exponent and mantissa.

        in:
          E           {from node 502}
          H L         {from node 601}

        out:
          E H L       {to node 603}

        Mantissa is 27 bits:
          H = high part
          L = low 18 bits

        hidden bit   = 0x100 in H
        overflow bit = 0x200 in H
      )
      # 601 import

      entry fp4c/main
      # right /b

      # 0 org


      +cy

      : fp4c/shl ( E H L - E' H' L' )
        // E--

        >r >r
        clc
        0 inv . +              // E-1
        r> r>                   // E' H L

        // H:L <<= 1

        clc
        dup . + >r              // L'=L*2, retain carry
        dup . +                 // H'=H*2+carry
        r>
      ;

      -cy


      : fp4c/shr ( E H L - E' H' L' )
        // H:L >>= 1

        a!                      // A=L
        >r                      // save H

        1 . +                   // E++

        r>                      // E' H
        0 over
        . +*                    // shift H:A right

        >r drop drop
        r> a                    // E' H' L'
      ;


      : fp4c/norm ( E H L - E' H' L' )

        // any bits above hidden bit:
        // result is too large, shift right until H < 0x200

        over 0x1ff inv and
        if
          drop
          fp4c/shr
          fp4c/norm ;
        then
        drop

        // normalized?
        over 0x100 and
        if
          drop ;
        then
        drop

        // H != 0
        .. over
        if
          drop
          fp4c/shl
          fp4c/norm ;
        then
        drop

        // H=0, but L != 0
        dup
        if
          drop
          fp4c/shl
          fp4c/norm ;
        then
        drop

        // zero
      ;

      : fp4c/div
        A[ fp5c/div ; ]] lit !
      ;

      : fp4c/add
      : fp4c/sub
      : fp4c/mul
      : fp4c/pass
        A[ fp5c/pass ; ]] lit !
      ;


      : fp4c/main
        up a!
        @                        // E from 502

        left a!

        r--- // give control to node 603

        @                        // H from 601
        @                        // L from 601

        ( E H L )

        fp4c/norm

        ( E H L )

        // send E H L to node 603

        >r >r
        !b                       // E
        r> !b                    // H
        r> !b                    // L

        fp4c/main
      ;
      """;
}
