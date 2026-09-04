using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Compiles the CVM cluster's resident node programs and produces one <see cref="CvmBootDescriptor"/>
/// per node.
///
/// <b>CVM2 (2026-09-01).</b> Stefan is rewriting the whole CVM around new, differently-numbered nodes
/// and a more sophisticated inter-node communication scheme; CVM1's nine-node branching tree (607 as
/// CPU, with 507/606/608 and 507's own three children 506/508/407) is retired -- "the nodes from CVM1
/// ... will not be used in CVM2". CVM2's own topology BRANCHES AT 507 (added 2026-09-04, node 506 --
/// see below): 708 (the real, unmirrored async serial PC link, unchanged in role from CVM1) -&gt; 707 (a
/// permanent runtime relay between 607 and 708) -&gt; 607 (the on-chip SRAM-request router) -&gt; 507 (the
/// entire CPU, see <see cref="Node507Program"/>), which itself has TWO sibling leaf children reached
/// from its own <c>m/main</c> dispatch: 407 (the long-call/long-jump helper, added 2026-09-02, reached
/// via 507's DOWN port -- see <see cref="Node407Program"/>) and 506 (the stack-frame node, added
/// 2026-09-04, reached via 507's RIGHT port -- see <see cref="Node506Program"/>). Node 508 is
/// explicitly NOT part of this mesh -- Stefan: "node 508 must be ignored for now" -- see
/// <see cref="Node508Program"/>'s own remarks. "More nodes will be added later" (Stefan's own words) --
/// this builder's job is to stay easy to extend as that happens, not to assume six is final.
///
/// <b>Node 507 (CPU), not 508 -- corrected 2026-09-01.</b> This project's own session briefly placed
/// CVM2's CPU source on node 508 under a mistaken attribution; Stefan corrected it directly: the CPU
/// is node 507, and 508 has no defined role yet. All of this builder's own compiling/loading of "the
/// CPU node" now targets <see cref="Node507Program"/>; <see cref="Node508Program"/> is not compiled or
/// loaded by anything below.
///
/// <b>Node 607's CVM2 source (2026-09-01).</b> <see cref="Node607Program"/> now carries Stefan's own
/// CVM2 source -- the on-chip SRAM-request router between 507 and 707 -- replacing the earlier
/// placeholder gap left by a prior context-limit compaction that lost the first copy pasted into this
/// session.
///
/// <b>Nodes 707 and 708's REAL CVM2 source (2026-09-01).</b> This project's earlier copies of 707 and
/// 708 -- carried since 2026-08-27 and wrongly treated as already-correct CVM2 content -- have both
/// now been replaced with Stefan's real source for each. The real 708 names its three request words
/// <c>/wr</c>/<c>/rd</c>/<c>/cx</c> (a leading slash, not a tick) and exports nothing named <c>'left</c>
/// at all; the real 707 imports 708 by those same slash names (<c>A[ /wr ; ]]</c> etc.) with a plain
/// three-way dispatch (write / compare-exchange / read, no "mark" branch, no leading <c>'left</c>
/// read) -- see <see cref="Node707Program"/>/<see cref="Node708Program"/>'s own remarks. The two were
/// compiled together to confirm the match, not just assert it: <c>Success = true</c> for both, with
/// only the same two harmless <c>'warm'</c>/<c>'cold'</c> shadowing warnings 707's own import of 708
/// has always produced.
///
/// <b>Compile order.</b> None of CVM2's four nodes import another CVM2 node's exports BY NAME except
/// 707, which does <c># 708 import</c> to reach 708's exports by name via a multiport call. 507 talks
/// to 607, and 607 talks to 707, purely through raw port I/O (<c>@</c>/<c>!</c>/<c>@b</c>/<c>!b</c>)
/// -- no named symbol resolution needed, so both compile completely standalone. So: 708 (ROM then
/// RAM) first, then 707 (importing 708's combined ROM+RAM exports); 607 and 507 are independent
/// standalone compiles with no ordering constraint relative to anything else.
///
/// <b>Load order is not compile order.</b> CVM2's mesh DOES branch, right at 507 -- exactly the DB013
/// 6.1.2.4 "Root Node Programming" concern <see cref="CvmBootLoadStep"/>'s own remarks describe (a
/// branch node needing a temporary relay role while EACH child loads in turn), now genuinely in play
/// again once node 506 was added (2026-09-04) as a second leaf sibling of 407 under 507. Load order:
/// 407 (reached via 507 acting as relay, added 2026-09-02), then 506 (also via 507 acting as relay,
/// added 2026-09-04 -- 507 relays BOTH of its children in turn, re-pointing its own B port at whichever
/// child is loading next), then 507 itself (via 607), then 607 (via 707), then 707 (via 708), then 708
/// itself last, direct, no relay -- the same tail Stefan already confirmed for CVM1 (607 via 707, 707
/// via 708, 708 last). <see cref="Services.Ga144CvmHardwareInstaller.OpenAndBootMesh"/>'s own
/// <c>parentOf</c>/<c>AncestorChain</c>/<c>focused</c>-set relay logic needed NO code changes to support
/// this: each load step unconditionally re-points its via-node's B port at that step's own target, so
/// loading 407 then 506 back-to-back through the same relay parent (507) already works correctly --
/// 507 gets "focused" only once (during the 407 step), and the 506 step simply re-points 507's B port
/// again without re-focusing it. This load order is inferred from the same confirmed CVM1 reasoning
/// applied to CVM2's branching case -- the 407 step IS confirmed on real hardware (see
/// <see cref="Node407Program"/>'s own remarks), but the NEW 506 step is not yet real-hardware-tested.
///
/// <b>This builder's own compiles are reference-only, never what real hardware installs
/// (2026-09-01).</b> <see cref="BuildDescriptors"/>/<see cref="BuildLoadPlan"/> below compile the
/// fixed <c>NodeXxxProgram.Source</c> strings baked into this assembly, and neither is called by the
/// shipped app's real install/dry-run paths any more -- both <see cref="Services.Ga144CvmHardwareInstaller"/>
/// and <see cref="ViewModels.ChipViewModel"/>'s "Compile CVM Test" compile each node's OWN CURRENT
/// PROJECT source first, per Stefan's explicit instruction ("take the nodes code in the project and
/// not the NodeXxxProgram code, which should only be used if no code in the project is defined"), and
/// reach for <see cref="ReferenceSourceFor"/> below only as a fallback when a node's project source is
/// still blank. <see cref="BuildDescriptors"/>/<see cref="BuildLoadPlan"/> remain here only as this
/// session's own throwaway-harness/reference-verification tool for the reference sources themselves.
/// </summary>
public static class CvmBootStreamBuilder
{
  public static IReadOnlyList<CvmBootDescriptor> BuildDescriptors()
  {
    var compiler = new F18Compiler();

    // 708 needs no cross-node import (nothing in its source imports another CVM node's exports), but
    // unlike every other node it is not an ordinary internal F18A node: it is the real, unmirrored
    // async serial boot node, so its RAM compile needs ITS OWN real factory ROM's exports (18ibits,
    // delay) in scope -- the same same-node ROM-then-RAM pairing F18NodeCompilationService uses, not a
    // cross-node ImportResolver. CVM2 (2026-09-01): this is now Stefan's own real 708 source -- see
    // Node708Program's own remarks -- which exports /wr//rd//cx (no leading tick) and no 'left at all.
    F18CompileResult rom708 = CompileNode708Rom(compiler);
    ThrowIfFailed(rom708);

    F18CompileResult result708 = Compile(compiler, Node708Program.Source, ImportingRom(Node708Program.Coordinate, rom708));
    ThrowIfFailed(result708);

    // 707's '# 708 import' is an ordinary cross-node RAM import, EXCEPT that 708's own exports span
    // both its custom ROM and its RAM (unlike 607/508 below, neither of which layers real custom ROM
    // under their RAM). Reproduces F18NodeCompilationService.ResolveRamImport's exact combine-then-
    // import sequence for that one case: merge 708's ROM exports with its RAM exports, then hand the
    // merged set to 707 as its resolved import. CVM2 (2026-09-01): this is now Stefan's own real 707
    // source -- see Node707Program's own remarks -- matched to the real 708 above (imports /wr//rd//cx
    // by name, three-way dispatch, no 'left read).
    F18CompileResult result707 = Compile(compiler, Node707Program.Source, ImportingCombinedRam(Node707Program.Coordinate, rom708, result708));
    ThrowIfFailed(result707);

    // CVM2 (2026-09-01): 607 is now the on-chip SRAM-request router, reached from 507 above and
    // reaching 707 below purely via raw port I/O -- no '# NNN import' directive of its own (unlike
    // CVM1's 507/606/608, which all imported 607 by name). Standalone compile, same shape as 507
    // below. Verified via a standalone harness compile of this exact source (0 diagnostics, 18/64
    // words used, entry point 'main' at 0x000) -- see Node607Program's own remarks.
    F18CompileResult result607 = Compile(compiler, Node607Program.Source, F18CompilerOptions.ForRam(Node607Program.Coordinate));
    ThrowIfFailed(result607);

    // CVM2 (2026-09-01): 507 is the entire CPU -- corrected from an earlier, mistaken attribution to
    // node 508 in this project's own session (see Node507Program/Node508Program's own remarks).
    // Standalone compile -- confirmed via a standalone harness compile of this exact source (0
    // errors, UsedWordCount 61, EntryPoint 0x01C at m/main, as of the 2026-09-02 '# m/main lit >r'
    // fix that Stefan confirmed working on real hardware) -- see Node507Program's own remarks.
    // Node 508 is NOT compiled here -- it has no defined role in CVM2 yet.
    F18CompileResult result507 = Compile(compiler, Node507Program.Source, F18CompilerOptions.ForRam(Node507Program.Coordinate));
    ThrowIfFailed(result507);

    // CVM2 (2026-09-02): node 407, the long-call/long-jump helper -- reached from 507's own m/main
    // dispatch (down port, per Node507Program's own remarks) once a fetched opcode word's top bits read
    // "11??". Imports 507 by name ('# 507 import', m/pop/m/push/m/next/a/a!), so must compile AFTER
    // result507 above. See Node407Program's own remarks for the full source and the confirmed physical
    // link (Stefan: "the port between 407 and 507 is still 'down'").
    F18CompileResult result407 = Compile(compiler, Node407Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node407Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node507Program.Coordinate
          ? F18ImportResolution.FromExports(result507.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result407);

    // CVM2 (2026-09-04): node 506, the stack-frame node (enter/leave/...) -- reached from 507's own
    // m/main dispatch via its RIGHT port, a SIBLING of 407 (both are leaves hanging directly off 507,
    // not a further link in the chain past 407 -- confirmed independently by
    // Models.KrakenConfiguration.PortAddress, which computes "right" on BOTH sides of 507<->506, same
    // as node 407's own remarks confirmed "down" on both sides of 507<->407). Imports 507 by name
    // ('# 507 import', m/pop/m/push/m/next/a/a!), so must compile AFTER result507 above, same as 407.
    // See Node506Program's own remarks for the full source.
    F18CompileResult result506 = Compile(compiler, Node506Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node506Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node507Program.Coordinate
          ? F18ImportResolution.FromExports(result507.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result506);

    return
    [
      CvmBootDescriptor.FromCompileResult(result407),
      CvmBootDescriptor.FromCompileResult(result506),
      CvmBootDescriptor.FromCompileResult(result507),
      CvmBootDescriptor.FromCompileResult(result607),
      CvmBootDescriptor.FromCompileResult(result707),
      CvmBootDescriptor.FromCompileResult(result708),
    ];
  }

  /// <summary>
  /// CVM2's boot LOAD order: leaves-first/root-last, with a branch at 507. Originally just
  /// 507 -&gt; 607 -&gt; 707 -&gt; 708 (2026-09-01, inferred from the same reasoning Stefan confirmed for
  /// CVM1's branching tree applied to CVM2's then-simpler non-branching case). Extended 2026-09-02 with
  /// node 407 -- the long-call/long-jump helper -- as a new leaf reached VIA 507
  /// (<c>new CvmBootLoadStep(407, 507)</c>): 407 is one hop further out than 507 (host&lt;-&gt;708&lt;-&gt;
  /// 707&lt;-&gt;607&lt;-&gt;507&lt;-&gt;407), so it must load FIRST, before 507 itself starts running its
  /// own compiled program and stops passively relaying. Extended again 2026-09-04 with node 506 -- the
  /// stack-frame node -- as a SECOND leaf ALSO reached via 507 (<c>new CvmBootLoadStep(506, 507)</c>),
  /// a sibling of 407 rather than a further link past it: both hang directly off 507's own <c>m/main</c>
  /// dispatch (407 via 507's down port, 506 via 507's right port), so both must load before 507 itself
  /// runs. Each via-node's local port name toward its target (407: "down" both sides, confirmed by
  /// Stefan -- see Node407Program's own remarks; 506: "right" both sides, matching node 507's own
  /// dispatch cascade's <c>r---</c> for the "1001" prefix -- see Node506Program's own remarks) is, in
  /// both cases, independently confirmed by <see cref="Models.KrakenConfiguration.PortAddress"/>'s own
  /// geographic-adjacency table, and resolved generically from <c>parentOf</c> by
  /// <see cref="Services.Ga144CvmHardwareInstaller"/> -- which needed NO code changes to support 506 as
  /// a second child of the same relay parent (507): its own <c>focused</c>-set logic re-points 507's B
  /// port at whichever child is currently loading, unconditionally, on every step, and only "focuses"
  /// 507 itself once, the first time it appears in any load step's ancestor chain (i.e. during the 407
  /// step, so the subsequent 506 step just re-points, no re-focus needed).
  ///
  /// <b>CONFIRMED ON REAL HARDWARE (2026-09-02) for the 407 step.</b> The load order through node 407
  /// was installed and run on a real EVB: a test program's <c>lcall</c>/<c>'ret</c> round-tripped
  /// correctly through node 407 (see Node407Program's own remarks for the transaction log), which could
  /// only happen if every hop's relay/focus/port-write sequence, all the way out to 407, was correct.
  /// <b>The new 506 step (2026-09-04) is NOT yet real-hardware-tested</b> -- it follows the same
  /// generic relay mechanism the 407 step already validated, but has not itself been confirmed by a
  /// transaction log the way 407 was. Node 508 is deliberately absent -- it is not part of CVM2's
  /// active mesh (see Node508Program's own remarks).
  /// </summary>
  public static IReadOnlyList<CvmBootLoadStep> BuildLoadOrder() =>
  [
    new CvmBootLoadStep(407, 507),
    new CvmBootLoadStep(506, 507),
    new CvmBootLoadStep(507, 607),
    new CvmBootLoadStep(607, 707),
    new CvmBootLoadStep(707, 708),
    new CvmBootLoadStep(708, null),
  ];

  /// <summary>
  /// The fixed reference source for one CVM2 node, keyed by coordinate -- the SAME strings
  /// <see cref="BuildDescriptors"/> itself compiles, exposed here so real hardware/dry-run callers
  /// (<see cref="Services.Ga144CvmHardwareInstaller"/>, <see cref="ViewModels.ChipViewModel"/>) can use
  /// them as a FALLBACK, never as the primary source. Per Stefan (2026-09-01): "I hope for the CVM2
  /// boot stream you take the nodes code in the project and not the NodeXxxProgram code, which should
  /// only be used if no code in the project is defined" -- so a caller must always try
  /// <c>chip.GetNode(coordinate).SourceCode</c> first and reach for this only when that is blank.
  /// Returns null for any coordinate with no CVM2 reference source of its own -- in particular node
  /// 508, which is deliberately not part of CVM2's active mesh yet (see
  /// <see cref="Node508Program"/>'s own remarks) and has nothing meaningful to fall back to.
  /// </summary>
  public static string? ReferenceSourceFor(int coordinate) => coordinate switch
  {
    Node407Program.Coordinate => Node407Program.Source,
    Node506Program.Coordinate => Node506Program.Source,
    Node507Program.Coordinate => Node507Program.Source,
    Node607Program.Coordinate => Node607Program.Source,
    Node707Program.Coordinate => Node707Program.Source,
    Node708Program.Coordinate => Node708Program.Source,
    _ => null,
  };

  /// <summary>
  /// Pairs <see cref="BuildLoadOrder"/>'s sequence with each step's compiled <see cref="CvmBootDescriptor"/>
  /// from <see cref="BuildDescriptors"/>. Every step resolves to a real descriptor -- all six CVM2
  /// nodes compile.
  /// </summary>
  public static IReadOnlyList<(CvmBootLoadStep Step, CvmBootDescriptor? Descriptor)> BuildLoadPlan()
  {
    Dictionary<int, CvmBootDescriptor> descriptorsByCoordinate =
        BuildDescriptors().ToDictionary(descriptor => descriptor.NodeCoordinate);

    return BuildLoadOrder()
        .Select(step => (
            step,
            descriptorsByCoordinate.TryGetValue(step.NodeCoordinate, out CvmBootDescriptor? descriptor)
                ? descriptor
                : null))
        .ToList();
  }

  private static F18CompileResult Compile(F18Compiler compiler, string source, F18CompilerOptions options) =>
      compiler.Compile(source, options);

  // Compiles node 708's real factory ROM (Node708Rom -- this project's own byte-for-byte copy
  // of data/ga144-rom.yaml's node 708 entry, "macro rom_async_boot"), including the same
  // predefined 'await' symbol injection F18NodeCompilationService.CompileRom uses for every
  // node's ROM compile.
  private static F18CompileResult CompileNode708Rom(F18Compiler compiler)
  {
    var predefinedSymbols = new Dictionary<string, F18ExportedSymbol>(StringComparer.OrdinalIgnoreCase)
    {
      ["await"] = new F18ExportedSymbol(
          "await",
          F18AwaitAddresses.ForNode(Node708Program.Coordinate),
          F18ExportKind.Word,
          Node708Program.Coordinate,
          F18MemorySpace.Rom),
    };

    var options = new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Rom,
      NodeCoordinate = Node708Program.Coordinate,
      MemoryBaseAddress = 0x080,
      MemoryWordCount = 64,
      IncludeCommonRomWords = false,
      PredefinedSymbols = predefinedSymbols,
      MacroResolver = Node708Rom.ResolveSystemMacro,
      MacroLookupScope = F18MacroLookupScope.SystemOnly,
    };

    return compiler.Compile(Node708Rom.RomAsyncBootSource, options);
  }

  // Pairs a node's RAM compile with its OWN already-compiled ROM's exports -- the same-node
  // ROM-then-RAM pattern F18NodeCompilationService.CompileRam uses -- as opposed to a cross-node
  // import. Node 708 needs this one: it has no '# NNN import' directive of its own, but its RAM
  // source calls words (18ibits, delay) that live in its own real ROM, not in the compiler's built-in
  // common ROM words. (607 and 507 layer no real custom ROM under their RAM, so neither needs this.)
  private static F18CompilerOptions ImportingRom(int coordinate, F18CompileResult ownRom) => new()
  {
    MemorySpace = F18MemorySpace.Ram,
    NodeCoordinate = coordinate,
    MemoryBaseAddress = 0x000,
    MemoryWordCount = 64,
    IncludeCommonRomWords = true,
    PredefinedConstants = ownRom.Constants,
    PredefinedSymbols = ownRom.Symbols,
  };

  // 707's '# 708 import' is an ordinary cross-node RAM import, EXCEPT that 708's own exports span
  // both its custom ROM and its RAM (unlike 607/507 above, neither of which layers real custom ROM
  // under their RAM). Reproduces F18NodeCompilationService.ResolveRamImport's exact combine-then-import
  // sequence for that one case: merge 708's ROM exports with its RAM exports (CombineExports,
  // mirroring that service's private TryCombineExports), then hand the merged set to 707 as its
  // resolved import.
  private static F18CompilerOptions ImportingCombinedRam(int coordinate, F18CompileResult rom, F18CompileResult ram) => new()
  {
    MemorySpace = F18MemorySpace.Ram,
    NodeCoordinate = coordinate,
    MemoryBaseAddress = 0x000,
    MemoryWordCount = 64,
    IncludeCommonRomWords = true,
    ImportResolver = importedCoordinate => importedCoordinate == ram.NodeCoordinate
        ? CombineExports(ram.NodeCoordinate, rom.Exports, ram.Exports)
        : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
  };

  // Merges a node's ROM export set and RAM export set into one, RAM names taking precedence on a
  // name that (unexpectedly) appears in both -- same rule and same failure-on-genuine-conflict
  // behavior as F18NodeCompilationService's private TryCombineExports, which this reproduces for
  // node 708's combined (ROM + RAM) export set.
  private static F18ImportResolution CombineExports(int coordinate, F18ExportSet rom, F18ExportSet ram)
  {
    var constants = new Dictionary<string, int>(rom.Constants, StringComparer.OrdinalIgnoreCase);
    var symbols = new Dictionary<string, F18ExportedSymbol>(rom.Symbols, StringComparer.OrdinalIgnoreCase);

    foreach (KeyValuePair<string, int> pair in ram.Constants)
    {
      if (constants.ContainsKey(pair.Key) || symbols.ContainsKey(pair.Key))
      {
        return F18ImportResolution.Failure($"Node {coordinate:000} exports '{pair.Key}' from both ROM and RAM.");
      }

      constants[pair.Key] = pair.Value;
    }

    foreach (KeyValuePair<string, F18ExportedSymbol> pair in ram.Symbols)
    {
      if (constants.ContainsKey(pair.Key) || symbols.ContainsKey(pair.Key))
      {
        return F18ImportResolution.Failure($"Node {coordinate:000} exports '{pair.Key}' from both ROM and RAM.");
      }

      symbols[pair.Key] = pair.Value;
    }

    return F18ImportResolution.FromExports(new F18ExportSet
    {
      NodeCoordinate = coordinate,
      Constants = constants,
      Symbols = symbols
    });
  }

  private static void ThrowIfFailed(F18CompileResult result)
  {
    if (result.Success)
    {
      return;
    }

    string diagnostics = string.Join(
        Environment.NewLine,
        result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    throw new InvalidOperationException(
        $"Node {result.NodeCoordinate:000} failed to compile while building the CVM boot stream:{Environment.NewLine}{diagnostics}");
  }
}