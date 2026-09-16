namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 502's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the twelfth of 17 new nodes
/// in this batch. Per its own header: "CVM2 node 502. VM 32 bit floatingpoint stage 4b node. exponent
/// arithmetic" -- continuing the arithmetic branch <see cref="Node503Program"/> started (503 controls 502,
/// and this node in turn imports node 501, per the same "control chain always imports and controls the
/// next node down" pattern already seen on the 403-&gt;402-&gt;401 branch).
///
/// <b>Confirms, a further time, the "<c>if</c> doesn't auto-consume its tested value" hypothesis.</b>
/// <c>fp4b/eff ( e - e' )</c> is <c>if ; then drop 1 ;</c> -- if <c>e</c> is nonzero the true branch is
/// empty and falls straight through to <c>;</c>, returning <c>e</c> UNCHANGED only because <c>if</c> left
/// it on the stack; if <c>e</c> is zero, the false branch's <c>drop</c> removes that same (zero) value and
/// replaces it with the literal <c>1</c>. This exactly matches <c>fp4b/main</c>'s own comments ("E1: 0 -&gt;
/// 1", "E2: 0 -&gt; 1") -- the standard IEEE-754 "treat a zero exponent field as an effective exponent of 1"
/// subnormal/zero convention. A clean, self-consistent data point for a hypothesis this project has now
/// leaned on since <see cref="Node301Program"/>.
///
/// <b><c>fp4b/adjust ( E1 E2 - E E )</c> is a real, fully-traceable mantissa-alignment shift loop</b> --
/// the classic floating-point add/sub algorithm: given the invariant (guaranteed upstream by this
/// pipeline's own operand-ordering stage) that <c>E1 &gt;= E2</c>, it compares the two exponents; while they
/// differ, it remote-dispatches <c>fp5b/shr2</c> (imported from node 501, presumably "shift mantissa 2 right
/// by one bit") to align the smaller operand, increments E2 by one (<c>1 . +</c>, the same add idiom seen
/// throughout this batch), and recurses -- worst case up to 254 iterations for maximally-different
/// exponents, noted here only as an observation, not a judgment.
///
/// <b><c>fp4b/mul</c>/<c>fp4b/div</c> confirm this pipeline implements real IEEE-754 binary32 bias
/// arithmetic, and further confirm the "<c>.</c> = add-with-carry" reading.</b> <c>fp4b/mul</c> computes
/// <c>E1+E2</c> then <c>0x7e inv . +</c>: since <c>inv(0x7e) = -0x7e-1 = -0x7f</c>, this adds <c>-127</c>,
/// giving <c>E1+E2-127</c> -- exactly the correct single-bias-removal formula when multiplying two already-
/// biased exponents. <c>fp4b/div</c> computes <c>inv . +</c> (E1-E2-1) then <c>0x80 . +</c> (+128), netting
/// <c>E1-E2+127</c> -- exactly the correct division exponent-bias formula. Both are strong, mutually
/// consistent confirmations of the bias value (127, i.e. binary32) and of the <c>N inv . +</c> "subtract
/// N, or equivalently add -(N+1) via NOT(N)" idiom already inferred from node 302/504.
///
/// <b>FLAGGED: a syntax deviation from the "remote control" idiom's own established shape, seen for the
/// first time in this batch.</b> <c>fp4b/adjust</c>'s <c>A[ fp5b/shr2 ]] lit !</c> is missing the <c>;</c>
/// that every other instance of this idiom in this project has had (compare this SAME file's own
/// <c>A[ fp5b/add ; ]]</c>/<c>A[ fp5b/sub ; ]]</c>/<c>A[ fp5b/mul ; ]]</c>, and every instance in nodes
/// 403/402/401/503). Reproduced exactly as pasted, not corrected; whether this changes what address is
/// taken (e.g. addressing the word's entry point rather than one-past-its-body) or is a harmless omission
/// is not stated by Stefan and not guessed at here.
///
/// <b>FLAGGED, not resolved: which port carries the operation dispatch address FROM node 503, and which
/// port carries the mantissa-shift/operation commands TO node 501, are both entirely absent from this
/// pasted source.</b> Neither <c>left</c> nor <c>right</c> appears anywhere in this file (only <c>a</c>/
/// <c>b</c>, bound per the header to <c>down</c>/<c>up</c> respectively), yet <see cref="Node503Program"/>'s
/// own <c>fp3b/main</c> explicitly sends an operation address to this node via a port bound to
/// <c>right</c>, and <c>fp4b/adjust</c>'s own <c>A[ fp5b/shr2 ]] lit !</c> must reach node 501 somehow. This
/// mirrors the same "control link reaches its target through a port never declared in the target's own
/// header" gap already flagged for node 402 (which never declares a port for node 403's incoming control
/// signal either) -- consistent, but still unexplained.
///
/// <b>A new, more concrete (but still unconfirmed) angle on the <c>r---</c>/<c>--l-</c> puzzle.</b> Here,
/// unlike in <see cref="Node402Program"/> or <see cref="Node401Program"/>, <c>r---</c> sits at a position
/// with an EXPLICIT stack-depth-changing comment: <c>( E1 E2 )</c> before it, <c>( E )</c> after -- i.e.
/// two values in, one out. <c>fp4b/main</c> never itself calls <c>fp4b/add</c>/<c>sub</c>/<c>mul</c>/
/// <c>div</c>, and (unlike node 403/503) this file has no local opcode-driven jump table at all -- which
/// makes sense if node 503 already resolved the exact desired operation and sent node 502 the target
/// word's OWN compiled address directly (per <see cref="Node503Program"/>'s own remote-control sends), so
/// node 502 has nothing left to dispatch on. Read together, this suggests <c>r---</c> may be this dialect's
/// single combined "receive a compiled word address from the implicit control port and <c>ex</c> it"
/// primitive, rather than (or perhaps in addition to) the simple single-word hardware relay hypothesized
/// from the node 407 boot-dispatch tree -- which would also retroactively explain why node 402's own
/// <c>fp4a/add</c>/etc. are defined but never explicitly called (they'd run via node 402's own <c>r---</c>).
/// However, this does not cleanly reconcile with the header's own explicit "out: always 2 words to node
/// 602" claim, since only ONE explicit <c>!</c> follows <c>r---</c> here -- so either <c>r---</c> ALSO
/// performs one of the two writes itself (as a side effect, alongside whatever it dispatches), or the
/// header's "always 2 words" is imprecise. Both readings remain live; this is flagged as evidence worth
/// putting to Stefan directly rather than a resolved finding.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired into
/// the CVM instruction set</b> -- same reasoning as its siblings, compounded by the open dispatch/relay
/// question above and node 501 not yet being in hand.
/// </summary>
internal static class Node502Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 4b (exponent arithmetic, controls node 501), added 2026-09-16.</summary>
  public const int Coordinate = 502;

  /// <summary>
  /// Node 502's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The missing <c>;</c> in
  /// <c>A[ fp5b/shr2 ]] lit !</c>, noted in this class's own remarks above, is reproduced exactly as
  /// pasted, not corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 502. VM 32 bit floatingpoint stage 4b node. exponent arithmetic )
      (
        calculates result exponent for add/multiply.
        passes both exponents for division.

        in: EE        {from node 402}

        out: always 2 words to node 602

          add: E E       {second E may be ignored}
          sub: E E
          mul: ...
          div: E2 E1     {native ! ! transmission order}
      )

      # 501 import

      entry fp4b/main
      # down /b
      # up /a

      # 0 org

      : fp4b/eff ( e-e' )
        if ; then
        drop 1 ;


      : fp4b/adjust ( E1 E2 - E E )
        // invariant: E1 >= E2

        over over xor
        if
          drop

          // divide mantissa2 by 2
          A[ fp5b/shr2 ]] lit !

          // compensate by raising exponent2
          1 . +

          fp4b/adjust ;
        then
        drop     // compare result
        drop     // discard E2, leave E1
      ;


      : fp4b/add ( E1 E2-E E )
        fp4b/adjust
        A[ fp5b/add ; ]] lit !
      ;

      : fp4b/sub ( E1 E2-E E )
        fp4b/adjust
        A[ fp5b/sub ; ]] lit !
      ;

      : fp4b/mul ( E1 E2 - E )
        +
        0x7e inv . +            // E1'+E2'-127

        A[ fp5b/mul ; ]] lit !
      ;

      : fp4b/div ( E1 E2 - E )
        inv . +                 // E1'-E2'-1
        0x80 . +                // +128 => E1'-E2'+127

        A[ ; ]] lit !           // 501 passes operands through
      ;


      : fp4b/main
        @b fp4b/eff               // E1: 0 -> 1
        @b fp4b/eff               // E2: 0 -> 1
        ( E1 E2 )

        r---

        ( E )
        !                         // one exponent to node 602

        fp4b/main
      ;
      """;
}
