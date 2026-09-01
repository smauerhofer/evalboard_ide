namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 508: NOT DEFINED YET, and deliberately excluded from CVM2's active mesh -- per Stefan
/// (2026-09-01): "node 508 must be ignored for now." This corrects a mistake earlier in this
/// project's own session: the CPU source now on <see cref="Node507Program"/> had been placed here
/// under the wrong assumption that 508, not 507, was CVM2's CPU node. It never was; 507 is.
///
/// This file previously also carried a stale CVM1-era source (the "register-t / comparison" node, a
/// servant to CVM1's old node 507's ALU) as an <c>.f18</c> reference mirror that was never kept in
/// sync with this class's own <c>Source</c> field and had drifted out of date regardless -- that
/// mirror (<c>Node508.f18</c>) has been deleted rather than carried forward as more stale content.
///
/// <b>A plausible future role, not yet confirmed.</b> <see cref="Node507Program"/>'s own <c>m/main</c>
/// dispatch hands control to the DOWN port (<c>-d--</c>) on one of its four top-level cases, and that
/// port is not currently answered by anything in CVM2's mesh -- node 508 is a natural physical
/// candidate for whatever eventually answers it (Stefan's own words elsewhere: "more nodes will be
/// added later"), but this is a guess, not a confirmed design decision, and nothing here should be
/// read as staking out that role in advance.
///
/// <see cref="Cvm.CvmBootStreamBuilder"/> does not compile or load this node.
/// <see cref="Services.CvmAssemblyLanguage"/> has no <c>NodeSymbolByMnemonic</c> entries pointing at
/// it either (the six CVM2 primitives that were mistakenly pointed here now resolve against
/// <see cref="Node507Program"/> instead). This class is kept only as a placeholder so the coordinate
/// has an obvious home once Stefan defines it.
/// </summary>
internal static class Node508Program
{
  /// <summary>The node this program would be deployed to, once defined. Not currently part of CVM2's active mesh.</summary>
  public const int Coordinate = 508;

  /// <summary>Not defined yet -- see the class remarks. Deliberately empty; do not compile or load this node.</summary>
  public const string Source = """
      ( CVM2 node 508. Not defined yet -- ignore for now, per Stefan, 2026-09-01. )
      """;
}