namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 510's resident F18 source -- CVM2's "extended arithmetic" relay node, supplied verbatim by
/// Stefan on 2026-09-07 alongside node 511 ("here are nodes 510 and 511. they must be added to the boot
/// stream and the opcode of 511 must be added to the assembler and disassembler. they support 32
/// register that can be used for parameter passing to functions."). Brand new file; no earlier revision
/// of node 510 existed in this project.
///
/// <b>Reached from node 509's own LEFT-port relay -- CONFIRMED directly from node 509's own already-
/// documented <c>u/main</c> cascade, not merely inferred.</b> <see cref="Node509Program"/>'s own
/// <c>u/main</c> tests "<c>1011_1???_????_????</c>" and relays it onward via <c>--l- ;</c> (its own LEFT
/// port), previously left open ("not yet answered by anything in CVM2's mesh"). This node's own header,
/// "extended arithmetic, <c>1011_1???_????_????</c>," is EXACTLY that same bit range, and its own
/// <c># left /b</c> binding uses the SAME local port name ("left") on this side too -- matching the
/// already-confirmed 508&lt;-&gt;509 link's own "same name on both sides" pattern (per
/// <see cref="Node509Program"/>'s own remarks: geographic adjacency in this mesh does not always mirror
/// names the way 407&lt;-&gt;507/506&lt;-&gt;507/508&lt;-&gt;507 do) rather than a naming mismatch --
/// not independently re-verified against <see cref="Models.KrakenConfiguration"/>'s own port tables this
/// time, but consistent with that established precedent. This resolves node 509's own open relay branch:
/// CVM2's mesh gains its fourth hop off of 507 through this specific chain (507 -&gt; 508 -&gt; 509 -&gt;
/// 510 -&gt; 511, via node 511 below).
///
/// <b>"( A: double register)" -- read here only at face value, not further analyzed.</b> This second
/// header line is reproduced verbatim; nothing in the rest of this source elaborates on what register
/// <c>a</c> being "double" means for node 510's own "extended arithmetic" role (no extended-arithmetic
/// opcode of node 510's OWN is actually defined anywhere in this source -- see below), so no further
/// claim is made about it here.
///
/// <b>Imports node 509.</b> <c># 509 import</c> brings <c>u/r@</c>/<c>u/r!</c>/<c>u/pop</c>/<c>u/push</c>/
/// <c>u/leave</c> into scope -- <c>x/r@</c>/<c>x/r!</c>/<c>x/pop</c>/<c>x/push</c>/<c>x/leave</c> here are
/// the same one-hop-further relay idiom used at every other link in this mesh, each embedding a call to
/// the corresponding <c>u/</c> import.
///
/// <b>FLAGGED: <c>x/main</c>'s own opening idiom textually matches node 509's ORIGINAL, pre-fix shape --
/// not its corrected one -- unconfirmed whether that matters here.</b> <see cref="Node509Program"/>'s own
/// remarks document a real, hardware-discovered bug in its ORIGINAL <c>u/main</c>: opening with
/// "<c>lit &gt;r A[ 2* !p !p ]] lit !b @b @b &gt;r</c>" (note the OP ORDER <c>2* !p !p</c> and the
/// TRAILING <c>&gt;r</c> after both port reads), later fixed to "<c>lit &gt;r A[ !p 2* !p ]] lit !b @b
/// @b</c>" (op order swapped, trailing <c>&gt;r</c> REMOVED) specifically because every branch's own
/// pointless push-then-immediately-pop round trip through the return stack was "WRONG-width." This
/// node's own <c>x/main</c> opens with "<c>\# x/leave lit &gt;r A[ 2* !p !p ]] lit !b @b @b &gt;r</c>" --
/// the OLD op order (<c>2* !p !p</c>) AND the trailing <c>&gt;r</c>, matching the shape node 509 moved
/// away from, not the one it moved to. Whether this is a real problem here is NOT established either
/// way: node 509's fix mattered because its OWN literal-load branch numerically masked/sign-extended the
/// round-tripped value, and a width bug there was directly observable; <c>x/main</c> below never does
/// arithmetic on its own round-tripped value at all -- its own <c>-if</c> branch just <c>r&gt;</c>s it
/// (discarding it) before jumping right, and its own fall-through branch never touches R again before
/// <c>ex</c>. Reproduced exactly as pasted, not silently rewritten to match node 509's own fixed shape --
/// this is called out for Stefan to confirm, not assumed either safe or broken.
///
/// <b><c>x/main</c>'s own dispatch cascade -- consumes one more bit within the already-fixed "1011_1"
/// prefix.</b> Reads two words via <c>@b</c> (port B, "left," the link to node 509) per the idiom above:
/// <list type="bullet">
/// <item><c>1011_11??_????_????</c> (<c>-if</c> taken) -- relayed onward via the RIGHT port (<c>r&gt;
/// r--- ;</c>): this is node 511, "register file," per that class's own header and its own
/// <c># left /b</c> binding (node 511 sits one column to node 510's right, calling that same link
/// "left" from its own side -- the same "left" precedent noted above, not a new pattern).</item>
/// <item><c>1011_10??_????_????</c> (<c>-if</c> not taken, falls to the trailing <c>then</c>) -- falls
/// through to a bare <c>ex ;</c>, the SAME "execute whatever's in R, then this definition's own implicit
/// return" tail idiom already established and accepted for node 407's own original working tail (see
/// <see cref="Node407Program"/>'s own remarks) -- unlike node 307's own incomplete final branch, this one
/// IS syntactically complete (ends in <c>;</c>). No local "extended arithmetic" opcode of node 510's own
/// is actually wired up behind this fall-through in this source -- despite the node's own header name,
/// nothing here defines one yet; this quarter is reserved/answered only by whatever "ex" jumps to,
/// exactly like every other still-open local-execute tail elsewhere in this mesh.</item>
/// </list>
///
/// <b>No new CVM mnemonics from this node.</b> Every word node 510 defines (<c>x/r@</c>, <c>x/r!</c>,
/// <c>x/pop</c>, <c>x/push</c>, <c>x/leave</c>, <c>x/main</c>) is a plain, non-tick-prefixed relay/
/// dispatch helper -- per Stefan's own naming rule, only <c>'</c>-prefixed words become CVM opcodes, and
/// this source defines none. Per Stefan's own message, only node 511's opcode needed adding to the
/// assembler/disassembler; node 510 needed only its own place in the boot stream, confirmed by this
/// source's own content.
///
/// <b>Not verified against a live compile.</b> No compiler is available in this environment to confirm
/// <c>x/main</c> actually compiles as pasted -- reproduced verbatim, not adjusted to compile.
/// </summary>
internal static class Node510Program
{
  /// <summary>The node this program is always deployed to -- CVM2's "extended arithmetic" relay node, one hop below node 509.</summary>
  public const int Coordinate = 510;

  /// <summary>
  /// Node 510's full resident F18 source, as supplied by Stefan on 2026-09-07 ("here are nodes 510 and
  /// 511"). See the class remarks for the FLAGGED opening-idiom shape (matching node 509's own pre-fix
  /// pattern, not its corrected one) and the confirmed relay chain back to node 509's own open branch.
  /// </summary>
  public const string Source = """
      ( CVM2 node 510. extended arithmetic, 1011_1???_????_???? )
      ( A: double register)
      # 509 import
      # 0 org
      entry x/main
      # 0 /a
      # left /b
      : x/r@ ( -w) A[ u/r@ ]] lit !b A[ !p ]] lit !b @b ;
      : x/r! ( w) A[ @p u/r! ]] lit !b !b ;
      : x/pop ( -w) A[ u/pop ]] lit !b A[ !p ]] lit !b @b ;
      : x/push ( w) A[ @p u/push ]] lit !b !b ;
      : x/leave A[ u/leave ; ]] lit !b
      : x/main # x/leave lit >r A[ 2* !p !p ]] lit !b @b @b >r
        -if // 1011_11??_????_????
          // register file
          r> r--- ;
        then // 1011_10??_????_????
        ex ;
      """;
}
