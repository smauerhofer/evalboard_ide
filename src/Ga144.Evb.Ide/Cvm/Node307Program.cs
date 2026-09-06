namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 307's resident F18 source -- CVM2's "VM ternary main" relay node. Stefan supplied the first
/// revision on 2026-09-06 alongside node 306 ("I change node 307 and added node 306... here are nodes
/// 307 and 306:"), then a follow-up revision the same day ("here are the nodes without the typos you
/// mentioned") that fixes two of the three things flagged against that first revision -- see below for
/// which one is NOT fixed. This is a BRAND NEW file to this project -- no earlier revision of node 307
/// existed here before the first message, despite Stefan's own wording ("I change node 307") suggesting
/// it already existed somewhere in his own design; this class's own <see cref="Source"/> reflects only
/// what has been supplied so far.
///
/// <b>Fills node 407's own previously-FLAGGED, unresolved "1101" relay branch.</b> Node 407's own
/// <c>n/main</c> dispatch (<see cref="Node407Program"/>) has, since 2026-09-05, carried an open question
/// about its "1101_????_????_????" sub-branch: an earlier revision changed it from <c>r&gt; ---u ;</c>
/// (relay UP) back to <c>r&gt; -d-- ;</c> (relay DOWN) with no explanation, flagged as unresolved rather
/// than silently reverted. This node's own header, "VM ternary main, 1101_????_????_????", claims
/// EXACTLY that bit range, and its own port binding (<c># up /b</c> -- port B is "up" from node 307's
/// own side) is the SAME physical link named from the OTHER end: node 307 sits at coordinate row 3,
/// column 07, one row BELOW node 407 (row 4, column 07) -- the same column, adjacent row, alternating-
/// local-name pattern already confirmed for 507&lt;-&gt;407 ("down" on both sides) and 507&lt;-&gt;607
/// ("up" on both sides). <see cref="Models.KrakenConfiguration"/>'s own SRAM master path lists
/// (<c>SramMasterPath207</c>: <c>...507, 407, 307, 207...</c>) independently confirm 407 and 307 are
/// physically adjacent. This STRONGLY SUGGESTS node 407's own "1101" relay (down) correctly reaches node
/// 307, resolving that earlier flag -- but Stefan has not explicitly confirmed this, and node 407's own
/// file is left untouched here (no code change was needed there either way, per the established pattern
/// that a physical relay just works once the node it reaches exists -- see node 408's own history in
/// <see cref="Node407Program"/>'s remarks for the same shape of fix).
///
/// <b>Imports node 407.</b> <c># 407 import</c> brings <c>n/@</c>, <c>n/!</c>, <c>n/r@</c>, <c>n/r!</c>,
/// <c>n/pop</c>, <c>n/push</c>, <c>n/next</c>, <c>n/leave</c> into scope by name -- all of node 407's own
/// non-tick exports (see <see cref="Node407Program"/>'s own remarks, including that class's own flag on
/// <c>n/@</c>'s unusual body). <c>k/@</c>/<c>k/!</c>/<c>k/r@</c>/<c>k/r!</c>/<c>k/pop</c>/<c>k/push</c>/
/// <c>k/leave</c> here are structurally the SAME relay idiom node 407's own <c>n/</c>-prefixed helpers
/// use, one hop further out (each embeds a call to the corresponding <c>n/</c> import instead of
/// node 507's own <c>m/</c> exports) -- exactly the same "each node re-exports the chain one link
/// further" pattern already used at every other hop in this mesh (507 -&gt; 407 -&gt; 307 here; 507 -&gt;
/// 508 -&gt; 509 elsewhere).
///
/// <b>FLAGGED: <c>k/next</c> is commented out.</b> Every other node in this relay chain (407's own
/// <c>n/next</c>, 508's own <c>g/next</c>) exports a <c>next</c> helper alongside <c>pop</c>. Here, the
/// line exists in the source but is prefixed with <c>//</c> (<c>// : k/next ( -w) ...</c>), so it is NOT
/// a real definition -- reproduced verbatim as a comment, not silently uncommented or removed. Present
/// unchanged in both revisions. Whether this is deliberate (node 306 and whatever else eventually
/// imports node 307 turn out not to need it) or an oversight is left for Stefan to say.
///
/// <b>Fixed by Stefan's 2026-09-06 follow-up ("without the typos"), no longer flagged:</b> the first
/// revision's stray header line "<c>( contains 8 32-bit address register )</c>" (node 307 itself defines
/// no address registers at all -- that is entirely node 306's own job, see
/// <see cref="Node306Program"/> -- and the figure also disagreed with node 306's own header and Stefan's
/// own narration, both of which say "4") is simply GONE from this revision's header, rather than
/// corrected to "4"; and the final dispatch branch's own trailing comment, which previously misread
/// "<c>1100_00??_????_????</c>", now correctly reads "<c>1101_00??_????_????</c>", matching what the
/// cascade's own bit tests actually reach.
///
/// <b><c>k/main</c>'s own dispatch cascade -- consumes two more bits within the already-fixed "1101"
/// prefix.</b> Reads two words via <c>@b</c> (port B, the "up" link to 407) the same "relay two bits
/// per level" idiom every other node in this mesh uses (<c>2* !p !p ... @b @b</c>):
/// <list type="bullet">
/// <item><c>1101_1???_????_????</c> (outer <c>-if</c> taken): further split by one more bit --
/// <c>1101_11??</c> relays LEFT (<c>--l-</c>); else <c>1101_10??</c> relays RIGHT (<c>r---</c>) -- THIS
/// is node 306, per that class's own header and <c># right /b</c> port binding.</item>
/// <item><c>1101_0???_????_????</c> (outer <c>-if</c> not taken, fallen through <c>then</c>): further
/// split by one more bit -- <c>1101_01??</c> relays DOWN (<c>-d--</c>); else falls through the final
/// <c>then</c> to whatever comes next.</item>
/// </list>
///
/// <b>STILL FLAGGED, NOT fixed by the 2026-09-06 follow-up: the final branch remains unterminated,
/// with no dispatch code of its own.</b> The very last line is still
/// "<c>then // 1101_00??_????_????</c>" (only its own comment's leading nibble changed, from "1100" to
/// "1101" -- see above) followed immediately by an empty comment block (<c>( )</c>) and nothing else --
/// no dispatch code for this branch (unlike every other cascade in this project's own nodes, which
/// either relay onward or fall to a local "ex" tail), and no closing <c>;</c> for the <c>k/main</c>
/// definition itself. This is a DIFFERENT, deeper issue than the two comment-text slips Stefan's
/// follow-up fixed (a wrong header figure, a mislabeled bit-pattern comment) -- it is missing CODE, not
/// a miscopied comment -- so it is called out again here rather than assumed resolved:
/// <list type="bullet">
/// <item>As pasted (both revisions), this looks like an open stub for a fourth (local?) dispatch branch
/// not yet written, the same shape node 407's own original "1100" tail was before
/// <c>'lcall</c>/<c>'ljmp</c>/<c>'lbr</c> existed (see <see cref="Node407Program"/>'s own remarks) --
/// except THERE the tail at least had SOME code (<c>ex ;</c>) from the start. Because of this, this
/// class's own <see cref="Source"/> below is very likely still NOT compilable as it stands; no semicolon
/// or dispatch body has been invented here to make it compile, since guessing at what belongs in an
/// unfinished branch is exactly the kind of unconfirmed design decision this project's own practice says
/// to flag rather than fill in.</item>
/// <item>Whether node 306 (the only node currently reachable through node 307 at all) is affected by
/// this depends on whether the toolchain's own "# import" resolution needs a full, successful compile of
/// node 307 or only its own declared export list -- not established here. See
/// <see cref="CvmBootStreamBuilder"/>'s own remarks: its <c>BuildDescriptors()</c> compiles node 307 and
/// is still expected to fail on this branch until it is completed.</item>
/// </list>
/// </summary>
internal static class Node307Program
{
  /// <summary>The node this program is always deployed to -- CVM2's "VM ternary main" relay node, one hop below node 407.</summary>
  public const int Coordinate = 307;

  /// <summary>
  /// Node 307's full resident F18 source, as supplied by Stefan on 2026-09-06, in its second revision
  /// ("here are the nodes without the typos you mentioned"). See the class remarks for what this
  /// revision fixed (the stray "8 register" header line, the final branch's mislabeled bit-pattern
  /// comment) and what it did NOT fix (the same final branch is still missing its own dispatch code and
  /// the definition's closing <c>;</c>) -- the commented-out <c>k/next</c> is also unchanged.
  /// </summary>
  public const string Source = """
      ( CVM2 node 307. VM ternary main, 1101_????_????_???? )
      # 407 import
      # 0x10 org
      entry k/main
      # 0 /a
      # up /b
      : k/@ ( ab-) A[ @p @p ]] lit !b !b !b A[ n/@ ]] lit !b A[ n/r! ]] lit !b ;
      : k/! ( ab-) A[ n/r@ ]] lit !b A[ @p @p ]] lit !b !b !b A[ n/! ]] lit !b ;
      : k/r@ ( -w) A[ n/r@ ]] lit !b A[ !p ]] lit !b @b ;
      : k/r! ( w) A[ @p n/r! ]] lit !b !b ;
      : k/pop ( -w) A[ n/pop ]] lit !b A[ !p ]] lit !b @b ;
      : k/push ( w) A[ @p n/push ]] lit !b !b ;
      // : k/next ( -w) A[ n/next ]] lit !b A[ !p ]] lit !b @b ;
      : k/leave A[ n/leave ; ]] lit !b
      : k/main # k/leave lit >r A[ 2* !p !p ]] lit !b @b @b >r
        -if // 1101_1???_????_????
          2* -if // 1101_11??_????_????
            r> --l- ;
          then // 1101_10??_????_????
          r> r--- ;
        then // 1101_0???_????_????
        2* -if // 1101_01??_????_????
          r> -d-- ;
        then // 1101_00??_????_????
      (

      )
      """;
}