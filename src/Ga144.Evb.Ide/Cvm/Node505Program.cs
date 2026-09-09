namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 505's resident F18 source -- CVM2's "frame2, more frame operations" node, filling node 506's own
/// previously-unwired "1001_01??" relay branch (see <see cref="Node506Program"/>'s own remarks: "call node
/// 505," left unwired until now). Imports node 506 (<c>f/r@</c>/<c>f/r!</c>/<c>f/pop</c>/<c>f/push</c>/
/// <c>f/next</c>/<c>f/leave</c>). BRAND NEW to this toolchain: added 2026-09-09 as part of the opcode/
/// assembler-vs-node reconciliation audit against Stefan's <c>workspace.yaml</c> project export -- no
/// earlier revision of this file existed here.
///
/// <c>f2/main</c> uses the same "no bit cascade, fall straight to ex" shape as node 405's own
/// <c>mw/main</c> (see <see cref="Node405Program"/>) -- reads two words via <c>@b</c>, holds the first in
/// the return register, then falls to <c>ex</c>. Both tick-prefixed words defined here are
/// <see cref="CvmInstructionSet.CvmOperandEncoding.None"/>-shaped, node-resolved, sharing node 506's own
/// "1001_01??" tag OR'd with each op's own address on this node.
///
/// <b>FLAGGED, not silently resolved -- a genuine mnemonic collision with node 506's own <c>'f</c>.</b>
/// Node 506 (<see cref="Node506Program"/>) already defines a tick-prefixed word named <c>'f</c>
/// ("move f to register r"). This node ALSO defines a tick-prefixed word named <c>'f</c> (<c>A[ a f/r! ]]
/// lit !b ;</c>) -- a DIFFERENT compiled word on a DIFFERENT node, but the SAME CVM mnemonic string. Per
/// this node's own trailing comment block, this second <c>'f</c> is very likely meant to be named
/// <c>'fx</c> instead (the comment documents "'f store current frame pointer f in r" followed immediately
/// by "'f exchange current frame pointer f and r" -- two DIFFERENT descriptions both attached to the name
/// <c>'f</c>, and the source's own second word definition is in fact named <c>'fx</c>, matching the SECOND
/// description, "exchange" -- <c>: 'fx A[ a f/r@ ]] lit !b A[ a! f/r! ]] lit !b ;</c> reads <c>f</c> out,
/// then writes r's own old value back into <c>f</c>, i.e. a genuine exchange). So the collision is very
/// likely a copy-paste comment typo (the SECOND opcode line's own leading <c>'f</c> should read <c>'fx</c>)
/// rather than an intended name clash with node 506. Per this project's own practice of never guessing at
/// an unconfirmed design decision: this class's own <see cref="Source"/> reproduces the comment block
/// verbatim (including the apparent typo), and <b>only <c>'f</c> from node 505 (not <c>'fx</c>) is left OUT
/// of <see cref="Services.CvmAssemblyLanguage.NodeSymbolByMnemonic"/> for now</b> to avoid silently
/// shadowing node 506's own already-wired <c>'f</c> mnemonic -- <c>'fx</c> (the actual second word defined
/// here) is wired normally, since its own name does not collide with anything. Confirm with Stefan whether
/// node 505's first word should be renamed (e.g. to something else entirely) or whether it is meant to
/// replace/repoint node 506's own <c>'f</c>.
/// </summary>
internal static class Node505Program
{
  /// <summary>The node this program is always deployed to -- CVM2's "frame2, more frame operations" node.</summary>
  public const int Coordinate = 505;

  /// <summary>
  /// Node 505's full resident F18 source, verbatim from Stefan's own <c>workspace.yaml</c> project export
  /// (the "CVM2" project's Host chip), added 2026-09-09 as part of the opcode/assembler-vs-node
  /// reconciliation audit. See the class remarks for the FLAGGED <c>'f</c> naming collision with node 506.
  /// </summary>
  public const string Source = """
      ( CVM2 node 505. frame2, more frame operations, 1001_01??_????_???? )
      # 506 import
      # 0 org
      entry f2/main
      # 0 /a
      # left /b
      : f2/r@ ( -w) A[ f/r@ ]] lit !b A[ !p ]] lit !b @b ;
      : f2/r! ( w) A[ @p f/r! ]] lit !b !b ;
      : f2/pop ( -w) A[ f/pop ]] lit !b A[ !p ]] lit !b @b ;
      : f2/push ( w) A[ @p f/push ]] lit !b !b ;
      : f2/next ( w) A[ f/next ]] lit !b A[ !p ]] lit !b @b ;
      : f2/leave A[ f/leave ; ]] lit !b
      : f2/main # f2/leave lit >r A[ 2* !p !p ]] lit !b @b >r @b
        ex ;

       : 'f A[ a f/r! ]] lit !b ;
       : 'fx A[ a f/r@ ]] lit !b A[ a! f/r! ]] lit !b ;

      (
        'f store current frame pointer f in r
        'f exchange current frame pointer f and r

      )
      """;
}
