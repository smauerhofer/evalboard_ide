using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Compiles the CVM test cluster's resident node programs -- in the cross-node import order each
/// source's own <c># NNN import</c> directive requires -- and produces one
/// <see cref="CvmBootDescriptor"/> per node.
///
/// <b>Scope of this pass.</b> Covers eight of the cluster's nine nodes: the seven with a finished,
/// hand-verified resident program as of 2026-08-25 (607, 507, 506, 508, 407, 606, 608), plus node
/// 708 (added 2026-08-26 -- Stefan supplied 708's resident source, its trailing documentation
/// comment and an unused, unterminated <c>readw</c> word were reviewed and fixed with his
/// confirmation; see <see cref="Node708Program"/>'s remarks). Node 707 remains deliberately NOT
/// covered (Stefan: "node 707 will come later. leave it empty for now"). This builder produces
/// boot-descriptor DATA only -- the compiled RAM image, register/stack init, and entry point for
/// each of those eight nodes -- and says nothing yet about how that data reaches its node across
/// the mesh. Delivery still depends on node 707: it is currently a bare, unprogrammed relay
/// position between 708 and 607 -- see <see cref="Node607Program"/>'s own remarks, "707 has no
/// local storage of its own... a stateless interface".
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
/// load order into an actual delivery sequence (and deciding whether to reuse
/// <c>KrakenSession</c>/<c>LegacyKrakenProtocol</c>'s relay primitives for it) is the next step,
/// once node 707 exists.
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
    ];
  }

  /// <summary>
  /// The definitive boot LOAD order for this cluster, confirmed by Stefan (2026-08-25): a
  /// post-order walk of the physical tree, leaves first / root last, so that every child is
  /// fully loaded and running its own real program before its parent gives up its temporary
  /// relay role. See <see cref="CvmBootLoadStep"/>'s remarks for the full tree diagram and the
  /// DB013 6.1.2.4 rationale. Node 707 is included here because it is part of the confirmed
  /// sequence's shape, even though it has no resident program yet -- see
  /// <see cref="BuildLoadPlan"/> for how that absence is surfaced rather than guessed at.
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
  /// <see cref="CvmBootDescriptor"/> from <see cref="BuildDescriptors"/>. The descriptor is
  /// <c>null</c> only for node 707 -- it has no resident source yet ("node 707 will come
  /// later") -- so a caller can already see and reason about the full 9-step sequence's shape
  /// without this builder pretending to know content that has not been dictated.
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