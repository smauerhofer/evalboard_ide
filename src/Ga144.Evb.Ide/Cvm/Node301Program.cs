namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 301's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the fifth of 17 new nodes
/// in this batch (see <see cref="Node305Program"/>/<see cref="Node304Program"/>/<see cref="Node303Program"/>/
/// <see cref="Node302Program"/> for the first four -- this is the last of the five same-row "stage" nodes,
/// 301-305). Per its own header: "CVM2 node 301. VM 32 bit floatingpoint stage 5 node. prepare mantissa
/// stream" -- extends the 24-bit mantissa (8-bit <c>h</c> + 16-bit <c>l</c>, per the header) to 27 bits
/// (9-bit <c>H</c> + 18-bit <c>L</c>), appending three zero bits for guard/round/sticky rounding, for each
/// of the two floats in the stream. Confirms the direction-word pattern already seen: its link to node 302
/// is <c># left /b</c>, matching node 302's OWN "left"-labeled link to it (both sides use the same word,
/// same pattern flagged in <see cref="Node302Program"/>'s own remarks); its link to node 401 is
/// <c># up /a</c>, matching the "up = same column, one row down" convention nodes 302/303 already showed
/// (302-&gt;402, 303-&gt;403, now 301-&gt;401).
///
/// <b>What <c>fp5/extend</c> does, worked out from its own stack shape (not spelled out this plainly by
/// Stefan):</b> <c>h</c> only ever needs a ONE-bit left shift (with a possible carry-in from <c>l</c>'s
/// own top bit) to reach its extra bit of significance, while <c>l</c> needs a full THREE-bit left shift.
/// The `if`/`else` branch handles the 1-bit shift-with-carry for <c>h</c> (doubling it, then setting its
/// new low bit from <c>l</c>'s own top bit if that bit was set), and the shared <c>2* 2* 2*</c> after the
/// `then` applies the remaining three-bit shift to <c>l</c> alone (by then the only value left on the
/// stack). This reading implies this dialect's `if`/`-if` does NOT itself consume the tested value off the
/// stack the way ordinary Forth's `if` does -- both branches open with an explicit `drop` that only makes
/// sense as discarding the leftover `dup 0x8000 and` mask, not `h` or `l` themselves. This is inferred
/// from making the surrounding stack arithmetic balance, not confirmed by Stefan or by any earlier node in
/// this project, and is offered as a working reading rather than a settled fact.
///
/// <b>FLAGGED, not silently resolved.</b>
/// <list type="bullet">
/// <item>The bare <c>..</c> token reappears (inside <c>fp5/extend</c>'s `if`-true branch, right after the
/// carry computation) -- the same unresolved token already flagged in <see cref="Node305Program"/> and
/// <see cref="Node302Program"/>. This is its third appearance across three different stage nodes so far.
/// Under the reading above it doesn't need to do anything for the arithmetic to work out, but its actual
/// meaning is still not known.</item>
/// <item>The header states the output mantissa is "H = 9 significant bits" / "L = 18 significant bits."
/// 9+18 = 27 (matches "extend...to 27 bit" exactly), but a single F18 word is 16 bits, so an 18-bit
/// quantity cannot be a literal word value -- presumably some of those 18 "significant" bits are
/// conceptual (already reflected via the carry captured into <c>H</c>) rather than physically stored bits
/// of <c>L</c>'s own 16-bit word. Not resolved further here; reproduced as Stefan described it.</item>
/// </list>
///
/// Like its four siblings, this source has no <c># import</c> directive and never calls
/// <c>k/pop</c>/<c>k/push</c>/<c>k/leave</c>. Like node 303's own <c>fp3/main</c>, <c>fp5/main</c>
/// tail-calls itself as an explicit, self-contained service loop (stages 1/2/4 do not).
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- same reasoning as its siblings.
/// </summary>
internal static class Node301Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 5 (extend mantissa precision for rounding), added 2026-09-16.</summary>
  public const int Coordinate = 301;

  /// <summary>
  /// Node 301's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The unresolved
  /// <c>..</c> token noted in this class's own remarks above is reproduced exactly as pasted, not
  /// corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 301. VM 32 bit floatingpoint stage 5 node. prepare mantissa stream )
      (
        extend the mantissa from 24 bit to 27 bit

        input mantissa:
            h = 8 significant bits
            l = 16 significant bits

        output mantissa:
            H = 9 significant bits
            L = 18 significant bits

        Three zero bits are appended for guard/round/sticky processing.

        in: hhll        {from node 302}
        out: HHLL       {to node 401}
      )

      entry fp5/main
      # up /a
      # left /b

      # 0 org

      : fp5/extend ( h l-h' l')
        dup 0x8000 and
        if
          drop
          >r 2* 1 xor r> ..
        else
          drop
          >r 2* r>
        then
        2* 2* 2*
      ;

      : fp5/main
        @b            // h1
        @b            // h2
        ( h1 h2 )
        >r
        ( h1 / h2 )
        @b            // h1 l1
        ( h1 l1 / h2 )
        fp5/extend
        ( H1 L1 / h2 )
        >r ! r>       // output H1
        ( L1 / h2 )
        r> @b
        ( L1 h2 l2 )
        fp5/extend
        ( L1 H2 L2 )
        >r ! // output H2
        ( L1 / L2 )
        ! // output L1
        r> !  // output L2
        fp5/main
      ;
      """;
}
