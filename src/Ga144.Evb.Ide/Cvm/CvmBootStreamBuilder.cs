using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Compiles the CVM cluster's resident node programs and produces one <see cref="CvmBootDescriptor"/>
/// per node.
///
/// <b>CVM2 (2026-09-01).</b> Stefan is rewriting the whole CVM around new, differently-numbered nodes
/// and a more sophisticated inter-node communication scheme; CVM1's nine-node branching tree (607 as
/// CPU, with 507/606/608 and 507's own three children 506/508/407) is retired -- "the nodes from CVM1
/// ... will not be used in CVM2". CVM2's own topology, so far, is a simple four-node LINEAR chain, not
/// a tree at all: 507 (the entire CPU, see <see cref="Node507Program"/>) -&gt; 607 (the on-chip
/// SRAM-request router) -&gt; 707 (a permanent runtime relay between 607 and 708) -&gt; 708 (the real,
/// unmirrored async serial PC link, unchanged in role from CVM1). Node 508 is explicitly NOT part of
/// this chain -- Stefan: "node 508 must be ignored for now" -- see <see cref="Node508Program"/>'s own
/// remarks. "More nodes will be added later" (Stefan's own words) -- this builder's job is to stay
/// easy to extend as that happens, not to assume four is final.
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
/// <b>Load order is not compile order.</b> CVM2's four nodes form a plain chain -- one predecessor,
/// one successor, all the way from 507 out to 708 -- unlike CVM1's branching tree, so the DB013 6.1.2.4
/// "Root Node Programming" concern <see cref="CvmBootLoadStep"/>'s own remarks describe (a branch node
/// needing a temporary relay role while EACH child loads in turn) simplifies to the same leaves-first/
/// root-last idea with no branching to sequence: 507 (reached via 607 acting as relay), then 607
/// (via 707), then 707 (via 708), then 708 itself last, direct, no relay -- the same tail Stefan
/// already confirmed for CVM1 (607 via 707, 707 via 708, 708 last), just with 507 prepended as the new
/// innermost leaf. This is inferred from that same confirmed reasoning applied to CVM2's simpler,
/// non-branching case -- NOT yet independently reconfirmed by Stefan for CVM2 specifically.
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
    // diagnostics, UsedWordCount 60, EntryPoint 0x1D at m/main) -- see Node507Program's own remarks.
    // Node 508 is NOT compiled here -- it has no defined role in CVM2 yet.
    F18CompileResult result507 = Compile(compiler, Node507Program.Source, F18CompilerOptions.ForRam(Node507Program.Coordinate));
    ThrowIfFailed(result507);

    return
    [
      CvmBootDescriptor.FromCompileResult(result507),
      CvmBootDescriptor.FromCompileResult(result607),
      CvmBootDescriptor.FromCompileResult(result707),
      CvmBootDescriptor.FromCompileResult(result708),
    ];
  }

  /// <summary>
  /// CVM2's boot LOAD order (2026-09-01): a plain leaves-first/root-last chain, 507 -&gt; 607 -&gt; 707
  /// -&gt; 708, inferred from the same reasoning Stefan confirmed for CVM1's branching tree (2026-08-25)
  /// applied to CVM2's simpler non-branching case -- see this class's own remarks. NOT yet
  /// independently reconfirmed by Stefan for CVM2 specifically. Node 508 is deliberately absent -- it
  /// is not part of CVM2's active mesh (see Node508Program's own remarks).
  /// </summary>
  public static IReadOnlyList<CvmBootLoadStep> BuildLoadOrder() =>
  [
    new CvmBootLoadStep(507, 607),
    new CvmBootLoadStep(607, 707),
    new CvmBootLoadStep(707, 708),
    new CvmBootLoadStep(708, null),
  ];

  /// <summary>
  /// Pairs <see cref="BuildLoadOrder"/>'s sequence with each step's compiled <see cref="CvmBootDescriptor"/>
  /// from <see cref="BuildDescriptors"/>. Every step resolves to a real descriptor -- all four CVM2
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