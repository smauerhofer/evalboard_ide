using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Compiles the CVM test cluster's resident node programs -- in the cross-node import order each
/// source's own <c># NNN import</c> directive requires -- and produces one
/// <see cref="CvmBootDescriptor"/> per node.
///
/// <b>Scope of this pass.</b> Covers all nine of the cluster's nodes: the seven with a finished,
/// hand-verified resident program as of 2026-08-25 (607, 507, 506, 508, 407, 606, 608), node 708
/// (added 2026-08-26, then updated 2026-08-27 to a new <c>send</c>/<c>send2</c>/<c>recv</c>
/// protocol with a <c>'cx</c> compare-and-exchange operation -- see
/// <see cref="Node708Program"/>'s remarks), and node 707 (added 2026-08-27, Stefan's first
/// resident source for it -- the memory/PC interface node that imports 708; see
/// <see cref="Node707Program"/>'s remarks, including two harmless
/// <c>'warm'</c>/<c>'cold'</c>-shadowing import warnings). This builder produces boot-descriptor
/// DATA only -- the compiled RAM image, register/stack init, and entry point for each of these
/// nine nodes -- and says nothing yet about how that data reaches its node across the mesh.
///
/// <b>Compile order</b> (forced by import dependencies, cross-checked against every node's own
/// class remarks): 607 first (no imports); then 507, 606, and 608, each of which does
/// <c># 607 import</c>; then 506, 508, and 407, each of which does <c># 507 import</c>. This
/// mirrors the exact chain every per-node verification harness in this project has already used
/// (607 -&gt; 507 -&gt; {506, 508, 407}; 607 -&gt; 606; 607 -&gt; 608).
///
/// <b>Compile order is not load order.</b> The mesh topology these seven nodes sit in is a
/// branching tree, not a simple chain: 607 has THREE children (507 via its up port, 606 via
/// right, 608 via left), and 507 itself has three more (506, 508, 407). Every node this project
/// has erected so far for the (unrelated) Kraken tentacles was a simple linear chain -- one
/// predecessor, one successor -- and <c>KrakenSession.ErectOnto</c>'s old-style, hardware-proven
/// per-hop relay technique (<c>focus</c> + <c>writeB</c>, sent as host-precomputed boot frames
/// while every intermediate node still sits in its ROM default) is built around that assumption.
/// Loading a BRANCHING node like 607 or 507 needs something extra: per DB013 6.1.2.4 ("Root Node
/// Programming"), a branch node must first be held in a temporary pass-through/relay role while
/// each of its children is loaded in turn (re-pointing its B register at a different child before
/// each child's payload), and only as the LAST step be given its own real resident program and
/// entry jump -- otherwise loading a later sibling would require relaying back through a node
/// that has already switched over to running its own unrelated CVM firmware.
/// <see cref="BuildDescriptors"/> below still returns descriptors in COMPILE order (the order
/// each source's own imports force); see <see cref="BuildLoadOrder"/> and
/// <see cref="CvmBootLoadStep"/> for the separate, definitive LOAD order Stefan confirmed
/// (2026-08-25) -- leaves first, root last, a post-order walk of the physical tree. Turning that
/// load order into an actual delivery sequence is the next step (and deciding whether to reuse
/// <c>KrakenSession</c>/<c>LegacyKrakenProtocol</c>'s relay primitives for it), now that all nine
/// nodes' resident programs exist.
/// </summary>
public static class CvmBootStreamBuilder
{
  public static IReadOnlyList<CvmBootDescriptor> BuildDescriptors()
  {
    var compiler = new F18Compiler();

    F18CompileResult result607 = Compile(compiler, Node607Program.Source, F18CompilerOptions.ForRam(Node607Program.Coordinate));
    ThrowIfFailed(result607);

    F18CompileResult result507 = Compile(compiler, Node507Program.Source, ImportingRam(Node507Program.Coordinate, result607));
    ThrowIfFailed(result507);

    F18CompileResult result606 = Compile(compiler, Node606Program.Source, ImportingRam(Node606Program.Coordinate, result607));
    ThrowIfFailed(result606);

    F18CompileResult result608 = Compile(compiler, Node608Program.Source, ImportingRam(Node608Program.Coordinate, result607));
    ThrowIfFailed(result608);

    F18CompileResult result506 = Compile(compiler, Node506Program.Source, ImportingRam(Node506Program.Coordinate, result507));
    ThrowIfFailed(result506);

    F18CompileResult result508 = Compile(compiler, Node508Program.Source, ImportingRam(Node508Program.Coordinate, result507));
    ThrowIfFailed(result508);

    F18CompileResult result407 = Compile(compiler, Node407Program.Source, ImportingRam(Node407Program.Coordinate, result507));
    ThrowIfFailed(result407);

    // 708 needs no cross-node import (nothing in its source imports another CVM node's
    // exports), but unlike every node above it is not an ordinary internal F18A node: it is
    // the real, unmirrored async serial boot node, so its RAM compile needs ITS OWN real
    // factory ROM's exports (18ibits, delay) in scope -- the same same-node ROM-then-RAM
    // pairing F18NodeCompilationService uses, not a cross-node ImportResolver.
    F18CompileResult rom708 = CompileNode708Rom(compiler);
    ThrowIfFailed(rom708);

    F18CompileResult result708 = Compile(compiler, Node708Program.Source, ImportingRom(Node708Program.Coordinate, rom708));
    ThrowIfFailed(result708);

    // 707 has an ordinary '# 708 import' directive, but 708 is not an ordinary node: its
    // exports come from BOTH its own custom ROM (warm, cold, 18ibits, delay, ...) and its RAM
    // ('left, 'wr, 'cx, 'rd, ...), so 707's import needs both combined -- the same merge
    // F18NodeCompilationService.ResolveRamImport performs (TryCombineExports) for every
    // cross-node import, reproduced here as CombineExports.
    F18CompileResult result707 = Compile(compiler, Node707Program.Source, ImportingCombinedRam(Node707Program.Coordinate, rom708, result708));
    ThrowIfFailed(result707);

    return
    [
      CvmBootDescriptor.FromCompileResult(result607),
      CvmBootDescriptor.FromCompileResult(result507),
      CvmBootDescriptor.FromCompileResult(result606),
      CvmBootDescriptor.FromCompileResult(result608),
      CvmBootDescriptor.FromCompileResult(result506),
      CvmBootDescriptor.FromCompileResult(result508),
      CvmBootDescriptor.FromCompileResult(result407),
      CvmBootDescriptor.FromCompileResult(result708),
      CvmBootDescriptor.FromCompileResult(result707),
    ];
  }

  /// <summary>
  /// The definitive boot LOAD order for this cluster, confirmed by Stefan (2026-08-25): a
  /// post-order walk of the physical tree, leaves first / root last, so that every child is
  /// fully loaded and running its own real program before its parent gives up its temporary
  /// relay role. See <see cref="CvmBootLoadStep"/>'s remarks for the full tree diagram and the
  /// DB013 6.1.2.4 rationale. All nine steps now have a resident program (707's arrived
  /// 2026-08-27 -- see <see cref="Node707Program"/>).
  /// </summary>
  public static IReadOnlyList<CvmBootLoadStep> BuildLoadOrder() =>
  [
    new CvmBootLoadStep(407, 507),
    new CvmBootLoadStep(506, 507),
    new CvmBootLoadStep(508, 507),
    new CvmBootLoadStep(507, 607),
    new CvmBootLoadStep(606, 607),
    new CvmBootLoadStep(608, 607),
    new CvmBootLoadStep(607, 707),
    new CvmBootLoadStep(707, 708),
    new CvmBootLoadStep(708, null),
  ];

  /// <summary>
  /// Pairs <see cref="BuildLoadOrder"/>'s confirmed sequence with each step's compiled
  /// <see cref="CvmBootDescriptor"/> from <see cref="BuildDescriptors"/>. Every step now
  /// resolves to a real descriptor -- all nine nodes have a resident program as of 2026-08-27.
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

  // Every CVM node source in this cluster only ever imports exactly one other node (its own
  // immediate master), so this resolver only needs to answer for that single coordinate --
  // matching the same pattern every per-node verification harness for this project already uses.
  private static F18CompilerOptions ImportingRam(int coordinate, F18CompileResult upstream) => new()
  {
    MemorySpace = F18MemorySpace.Ram,
    NodeCoordinate = coordinate,
    MemoryBaseAddress = 0x000,
    MemoryWordCount = 64,
    IncludeCommonRomWords = true,
    ImportResolver = importedCoordinate => importedCoordinate == upstream.NodeCoordinate
        ? F18ImportResolution.FromExports(upstream.Exports)
        : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
  };

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
  // ROM-then-RAM pattern F18NodeCompilationService.CompileRam uses -- as opposed to
  // ImportingRam above, which pairs a node's RAM compile with a DIFFERENT node's exports via
  // that different node's own '# NNN import' directive. Node 708 needs this one: it has no
  // '# NNN import' directive of its own, but its RAM source calls words (18ibits, delay) that
  // live in its own real ROM, not in the compiler's built-in common ROM words.
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

  // 707's '# 708 import' is an ordinary cross-node RAM import, EXCEPT that 708's own exports
  // span both its custom ROM and its RAM (unlike every other node ImportingRam above already
  // handles, none of which layer real custom ROM under their RAM). Reproduces
  // F18NodeCompilationService.ResolveRamImport's exact combine-then-import sequence for that one
  // case: merge 708's ROM exports with its RAM exports (CombineExports, mirroring that service's
  // private TryCombineExports), then hand the merged set to 707 as its resolved import.
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