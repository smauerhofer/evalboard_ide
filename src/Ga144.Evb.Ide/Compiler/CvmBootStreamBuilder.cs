using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Compiles the CVM test cluster's resident node programs -- in the cross-node import order each
/// source's own <c># NNN import</c> directive requires -- and produces one
/// <see cref="CvmBootDescriptor"/> per node.
///
/// <b>Scope of this pass.</b> Covers exactly the seven nodes with a finished, hand-verified
/// resident program as of 2026-08-25: 607, 507, 506, 508, 407, 606, 608. Nodes 707 and 708 are
/// deliberately NOT covered (Stefan: "node 707 will come later. leave it empty for now" /
/// "node 708 should come later"). This builder produces boot-descriptor DATA only -- the compiled
/// RAM image, register/stack init, and entry point for each of those seven nodes -- and says
/// nothing yet about how that data reaches its node across the mesh. Delivery depends on both
/// deferred pieces: node 707 (currently a bare, unprogrammed relay position between 708 and 607 --
/// see <see cref="Node607Program"/>'s own remarks, "707 has no local storage of its own... a
/// stateless interface") and node 708 (the only node in this topology with an external boot
/// interface -- the sole possible entry point for any of this data).
///
/// <b>Compile order</b> (forced by import dependencies, cross-checked against every node's own
/// class remarks): 607 first (no imports); then 507, 606, and 608, each of which does
/// <c># 607 import</c>; then 506, 508, and 407, each of which does <c># 507 import</c>. This
/// mirrors the exact chain every per-node verification harness in this project has already used
/// (607 -&gt; 507 -&gt; {506, 508, 407}; 607 -&gt; 606; 607 -&gt; 608).
///
/// <b>Compile order is not load order -- an open design question, not yet resolved here.</b> The
/// mesh topology these seven nodes sit in is a branching tree, not a simple chain: 607 has THREE
/// children (507 via its up port, 606 via right, 608 via left), and 507 itself has three more
/// (506, 508, 407). Every node this project has erected so far for the (unrelated) Kraken
/// tentacles was a simple linear chain -- one predecessor, one successor -- and
/// <c>KrakenSession.ErectOnto</c>'s old-style, hardware-proven per-hop relay technique
/// (<c>focus</c> + <c>writeB</c>, sent as host-precomputed boot frames while every intermediate
/// node still sits in its ROM default) is built around that assumption. Loading a BRANCHING node
/// like 607 or 507 needs something extra: per DB013 6.1.2.4 ("Root Node Programming"), a branch
/// node must first be held in a temporary pass-through/relay role while each of its children is
/// loaded in turn (re-pointing its B register at a different child before each child's payload),
/// and only as the LAST step be given its own real resident program and entry jump -- otherwise
/// loading a later sibling would require relaying back through a node that has already switched
/// over to running its own unrelated CVM firmware. This builder does not attempt that sequencing
/// yet; <see cref="BuildDescriptors"/> below returns descriptors in compile order only. Turning
/// this into an actual delivery sequence (and deciding whether to reuse
/// <c>KrakenSession</c>/<c>LegacyKrakenProtocol</c>'s relay primitives for it) is the next step,
/// once node 707 exists and this load-order approach has been reviewed.
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

    return
    [
      CvmBootDescriptor.FromCompileResult(result607),
      CvmBootDescriptor.FromCompileResult(result507),
      CvmBootDescriptor.FromCompileResult(result606),
      CvmBootDescriptor.FromCompileResult(result608),
      CvmBootDescriptor.FromCompileResult(result506),
      CvmBootDescriptor.FromCompileResult(result508),
      CvmBootDescriptor.FromCompileResult(result407),
    ];
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
