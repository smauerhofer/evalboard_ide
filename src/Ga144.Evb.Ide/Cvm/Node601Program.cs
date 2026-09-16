namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 601's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the SEVENTEENTH and last of
/// the 17 new nodes in this batch, and the final link in the "3c" chain <see cref="Node603Program"/>
/// started (603 controls 602, 602 controls this node). Per its own header: "CVM2 node 601. VM 32 bit
/// floatingpoint stage 5c node. implement divide."
///
/// <b><c>fp5c/sec</c> confirms, by finally being given its own name, that "set carry" is NOT a native F18A
/// primitive in this dialect.</b> Its body is exactly <see cref="Node501Program"/>'s own inlined carry-
/// forcing trick (<c>dup dup xor inv dup . + drop</c>), now factored into a reusable word -- closing the
/// question raised in that file's own remarks about whether a <c>sec</c>-shaped primitive exists (it
/// doesn't; Stefan builds his own from a deliberate forced-overflow instead, once per node that needs it,
/// since each F18 core's dictionary is private to that chip).
///
/// <b><c>fp5c/dbl</c>/<c>fp5c/q0</c>/<c>fp5c/q1</c> reveal a clean unifying insight: the SAME carry-chained
/// "double via <c>dup . +</c>" idiom, with the carry pre-set to 0 (<c>clc</c>) or 1 (<c>fp5c/sec</c>)
/// beforehand, implements not just an arithmetic shift-left but a "shift and inject a new low bit"
/// operation</b> -- <c>fp5c/q0</c> appends a 0 bit to the growing quotient, <c>fp5c/q1</c> appends a 1 bit,
/// simply by choosing which carry state to force before doubling. This is a satisfying generalization of
/// the doubling trick already confirmed in <see cref="Node404Program"/>, <see cref="Node501Program"/>, and
/// <see cref="Node602Program"/>.
///
/// <b><c>fp5c/div</c> is a complete, textbook, non-restoring binary division algorithm, and closes the
/// loop on this entire batch's division story.</b> It seeds the remainder as <c>M1 - M2</c> (numerator
/// minus divisor, via <c>fp5c/subd</c>), then for 27 iterations (<c>26.</c> pushed, giving 26+1 passes via
/// <c>unext</c> -- matching this pipeline's own established 27-bit extended mantissa precision, one integer
/// bit plus 26 fractional bits): doubles the remainder, and -- based on the REMAINDER'S SIGN BEFORE
/// doubling (saved via <c>dup &gt;r</c>, tested via <c>-if</c> after restoring it) -- either adds the
/// divisor back and records quotient bit 0 (remainder was negative), or subtracts the divisor again and
/// records quotient bit 1 (remainder was nonnegative). This is the standard non-restoring division
/// algorithm, letter for letter. The final <c>QL QH</c> (the raw 27-bit quotient mantissa) is exactly what
/// <see cref="Node602Program"/>'s own <c>fp4c/norm</c> expects as its H:L input, alongside the exponent
/// <see cref="Node502Program"/>'s own <c>fp4b/div</c> already computed -- meaning nodes 502 (exponent),
/// 601 (mantissa quotient), and 602 (normalize) together form the complete division implementation this
/// batch's "controls division path" language (first seen in node 603's header) was describing all along.
/// Every branch's stack bookkeeping (the sign-save/restore around <c>fp5c/dbl</c>, the Q-save/restore
/// around the remainder update) checks out exactly against the "<c>if</c>/<c>-if</c> doesn't auto-consume"
/// hypothesis, one more clean confirming instance to close out the batch.
///
/// <b><c>fp5c/pass</c> finally reveals what node 602's own <c>fp4c/pass</c> remote-dispatch target
/// actually does on this end</b>: for add/sub/mul (whose real mantissa arithmetic already finished at node
/// 501), it simply discards the two placeholder trailing words node 501 always sends (<c>x x</c> per that
/// node's own header) and keeps <c>L H</c>, closing the loop opened by <see cref="Node502Program"/>'s own
/// <c>fp4b/div</c> "501 passes operands through" remark and by <see cref="Node602Program"/>'s own
/// <c>fp4c/add</c>/<c>sub</c>/<c>mul</c>/<c>pass</c> fallthrough.
///
/// <b>Likely a fourth confirming (though not textually captioned) instance of the "give control" relay
/// mechanism <see cref="Node602Program"/>'s own <c>r---</c> comment first named.</b> <c>fp5c/main</c>'s
/// <c>--l-</c> sits in the exact same structural position as node 602's own captioned <c>r---</c> -- right
/// after reading this node's primary inputs (the four words from node 501, via <c>up</c>/<c>b</c>), and
/// BEFORE any operation-specific dispatch of <c>fp5c/div</c> vs. <c>fp5c/pass</c> is visible (neither is
/// ever called explicitly in this pasted <c>fp5c/main</c>, the fourth node in this batch -- after 402, 502,
/// 602 -- with this exact "defined but never locally dispatched" gap). Given <c>left</c> is this node's
/// declared link to node 602 (<c># left /a</c>, confirmed by the header's own "out: HL {to node 602}"),
/// <c>--l-</c> here most plausibly means "give control to node 602," mirroring node 602's own "give control
/// to node 603" exactly one stage up the chain. This is offered as the strongest reading yet of this
/// notation, though it remains an inference from structural position rather than a captioned confirmation,
/// and it does not fully reconcile with every earlier sighting (node 502's own <c>r---</c> appeared to
/// change the stack depth, which this reading doesn't require).
///
/// <b>Batch-wide open question, now spanning FOUR nodes (402, 502, 602, 601):</b> every "acceptor" node in
/// each of this batch's three control chains defines remote-dispatch target words (<c>fp4a/*</c>,
/// <c>fp5b/*</c> is the exception -- 501 DOES call its own arithmetic directly, only 502/602/402 show the
/// gap) that are never invoked locally, with a "give control" relay token sitting at the position where
/// dispatch would be expected. How that relay token actually triggers execution of the address its own
/// controller sent it is not shown in ANY of these four nodes' own pasted sources, and remains open across
/// the whole batch pending direct confirmation from Stefan.
///
/// <b>The <c>+cy</c>/<c>-cy</c> region here spans SEVEN word definitions</b> (<c>fp5c/sec</c> through
/// <c>fp5c/div</c>) -- the largest bracketed region seen in this batch (previous largest was two, in
/// <see cref="Node501Program"/>), reinforcing that it is a flexible-extent compiler/mode directive, not
/// tied to any fixed word count.
///
/// <b>This is the LAST of the 17 nodes in this batch.</b> With all 17 now in hand, the full pipeline shape
/// is: 305-&gt;304-&gt;303 (stages 1-3, unpack/rearrange/split), 303 fanning out into three parallel chains
/// (3a: 403-&gt;402-&gt;401 comparison/control; 3b: 503-&gt;502-&gt;501 add/sub/mul arithmetic; 3c:
/// 603-&gt;602-&gt;601 sign/normalization/division), all three converging back at 604 (stage 6, classify),
/// then 504 (stage 7, classification override), then 404 (stage 8, round and reduce to binary32). NOT YET
/// added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired into the CVM
/// instruction set for any of the 17 -- per this project's own practice, wiring is deferred until the
/// several batch-wide open questions (the relay-token dispatch mechanism above; the up/right port ambiguity
/// flagged in <see cref="Node603Program"/>; whether opcode <c>o</c> truly arrives pre-scaled; the
/// <c>+cy</c>/<c>-cy</c>, <c>.</c>/<c>..</c> token semantics) are confirmed directly with Stefan.
/// </summary>
internal static class Node601Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 5c (non-restoring binary division, end of the sign/normalization control chain), added 2026-09-16.</summary>
  public const int Coordinate = 601;

  /// <summary>
  /// Node 601's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The unresolved
  /// <c>--l-</c> token noted in this class's own remarks above is reproduced exactly as pasted, not
  /// corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 601. VM 32 bit floatingpoint stage 5c node. implement divide )
      (
        calculates result for divide.

        always receives 4 words from node 501:

          add: L H x x
          sub: L H x x
          mul: L H 0 x
          div: L2 L1 H2 H1

        out: HL      {to node 602}
      )

      entry fp5c/main
      # up /b
      # left /a

      # 0 org


      +cy

      : fp5c/sec ( x-x )
        // force carry = 1
        dup dup xor inv dup . + drop
      ;


      : fp5c/dbl ( RL DH RH - RL' DH RH' )
        // signed 36-bit R <<= 1
        // preserve DH in the middle
        >r >r
        clc
        dup . +             // RL <<= 1
        r> r>
        dup . +             // RH = RH*2 + carry
      ;


      : fp5c/addd ( RL DH RH - RL' DH RH' )
        // R += D
        // DL is held in A

        >r >r               // RL / RH DH

        clc
        a . +               // RL += DL

        r> r>               // RL' DH RH
        over . +            // RH += DH + carry
      ;


      : fp5c/subd ( RL DH RH - RL' DH RH' )
        // R -= D
        // DL is held in A

        >r >r               // RL / RH DH

        fp5c/sec
        a inv . +           // RL -= DL

        r> r>               // RL' DH RH
        over inv . +        // RH -= DH + borrow
      ;


      : fp5c/q0 ( QL QH - QL' QH' )
        // Q = Q << 1
        >r
        clc
        dup . +
        r>
        dup . +
      ;


      : fp5c/q1 ( QL QH - QL' QH' )
        // Q = (Q << 1) | 1
        >r
        fp5c/sec
        dup . +
        r>
        dup . +
      ;


      : fp5c/div ( L2 L1 H2 H1 - ... QL QH )

        // Put divisor low word in A.
        //
        // input:
        //     L2 L1 H2 H1
        //
        // result:
        //     L1 H2 H1
        //
        // interpreted as:
        //     RL DH RH

        >r >r >r
        a!                   // A = L2 = DL
        r> r> r>             // L1 H2 H1

        // Initial non-restoring remainder:
        //
        // R = M1 - M2

        fp5c/subd            // RL DH RH

        // quotient starts at zero

        0 0                  // RL DH RH QL QH


        // Generate:
        //
        // integer bit + 26 fractional bits
        //
        // Q = floor((M1 << 26) / M2)

        26. >r
        begin

          // temporarily remove Q so remainder is on top

          >r >r               // RL DH RH / QH QL

          // Remember sign of R before doubling.

          dup >r

          // R <<= 1

          fp5c/dbl

          // Original R negative?
          //
          // negative:
          //     bit = 0
          //     R = 2R + D
          //
          // nonnegative:
          //     bit = 1
          //     R = 2R - D

          r> -if
            drop

            fp5c/addd

            r> r>             // restore QL QH
            fp5c/q0

          else
            drop

            fp5c/subd

            r> r>             // restore QL QH
            fp5c/q1

          then

        unext

        // Stack is now:
        //
        //   ... RL DH RH QL QH
        //
        // QH is T, QL is S, exactly what main needs.
      ;

      -cy


      : fp5c/pass
        // ADD/SUB/MUL input is:
        //
        //     L H x x
        //
        // Discard the two unused words, leaving L H.
        drop drop
      ;


      : fp5c/main
        @b @b @b @b

        // ADD/SUB: L H x x
        // MUL:     L H 0 x
        // DIV:     L2 L1 H2 H1

        --l-

        // operation handler leaves:
        //
        //     L H
        //
        // or, for DIV:
        //
        //     ... QL QH

        left a!
        ! !                   // send H then L

        fp5c/main
      ;
      """;
}
