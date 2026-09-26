namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 304's resident F18 source -- CVM2's floating-point subprocessor, step 7 (packs the final sign,
/// exponent and mantissa into two IEEE-754 32-bit words). Supplied by Stefan 2026-09-26 as part of the
/// complete FP subprocessor rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design.</b> The previously stored <c>Source</c> for this coordinate
/// was a completely different "stage 2 node. rearrange data stream and collect return value"
/// (<c>fp2/*</c> words); it is replaced wholesale.
///
/// Reads <c>s e h l</c> from either node 404's normal rounding/packing pipeline or, per its own header
/// comment, node 204's special-case shortcut ("the data can also come from node 204, this is a shortcut
/// for special values"). For zero/subnormal results (no explicit hidden bit in <c>h</c>) the packed
/// exponent field is forced to zero; infinity/NaN results (exponent already <c>0xff</c>) and normal
/// results pass their exponent through unchanged. Sends the packed <c>h l</c> pair on to node 305.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node304Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 7 (packs sign/exponent/mantissa into IEEE-754 words for node 305). Replaced 2026-09-26.</summary>
  public const int Coordinate = 304;

  /// <summary>
  /// Node 304's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 2, rearrange data stream" design previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 304. VM 32 bit floatingpoint step 7 node. pack data

      this node reads sign exponent and mantissa and converts it into a 32-bit IEEE 754 number

      the data can also come from node 204, this is a shortcut for special values

      effective exponent:
        normal      raw exponent
        subnormal   1
        zero        0
        inf/nan     0xff

        in:  s e h l  {from node 404 or node 204}
        out: h l      {to node 305}

      )

      entry fx/main
      # -d-u /b
      # right /a

      # 0 org

      : fx/main
        @b >r                    // sign used at the end
        @b
        ( e / s )

        dup 0xff xor .
        if                       // finite
          drop
          @b
          ( e h / s )

          // no explicit hidden bit means zero/subnormal:
          // packed exponent must be zero

          dup 0x80 and
          if                     // normal
            drop
          else                   // zero or subnormal
            drop
            >r dup xor r>
          then

        else                     // infinity or NaN
          drop
          @b
          ( ff h / s )
        then

        ( e h / s )
        // pack
        >r
        2* 2* 2* 2* 2* 2* 2*
        r> 0x7f and
        xor
        ( h / s )

        r> xor                   // add sign
        ( h )
        !                        // h
        @b !                     // l
        fx/main
      ;
      """;
}
