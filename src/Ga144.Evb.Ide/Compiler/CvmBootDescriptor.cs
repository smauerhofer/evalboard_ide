using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// One CVM test-cluster node's boot-time configuration: the compiled RAM image to deposit into
/// that node, the register/data-stack initialization to apply once loading is complete, and the
/// entry address to jump to. The field names deliberately echo DB013 (arrayForth 3 User's
/// Manual) 5.5.1's own "Boot Descriptor Syntax" -- <c>/RAM</c>, <c>/A</c>, <c>/B</c>,
/// <c>/IO</c>, <c>/STACK</c>, <c>/P</c> -- since that is exactly what this record holds, just
/// produced directly from this project's own <see cref="F18Compiler"/> instead of hand-authored
/// Boot Descriptor Language. Every value here is either what the node's own source's
/// <c>/a</c>/<c>/b</c>/<c>/io</c>/<c>/stack</c>/<c>entry</c> directives specified, or the
/// documented reset default (see <see cref="F18CompileResult"/>'s own remarks) when a directive
/// was not used.
///
/// This record carries only the DATA a boot loader needs -- it says nothing about HOW that data
/// reaches the node across the mesh. See <see cref="CvmBootStreamBuilder"/>'s remarks for what
/// is (and is not yet) covered there.
/// </summary>
public sealed record CvmBootDescriptor(
    int NodeCoordinate,
    IReadOnlyList<int> Words,
    int MemoryBaseAddress,
    int? InitialA,
    int? InitialB,
    int? InitialIo,
    IReadOnlyList<int> InitialStack,
    int? EntryPoint)
{
  /// <summary>
  /// Lifts a compiled node's <see cref="F18CompileResult"/> directly into a
  /// <see cref="CvmBootDescriptor"/>. The caller is responsible for having already confirmed
  /// <see cref="F18CompileResult.Success"/> -- this does not re-check it, so that boot-descriptor
  /// construction stays a pure, side-effect-free projection of an already-verified compile.
  /// </summary>
  public static CvmBootDescriptor FromCompileResult(F18CompileResult result) => new(
      result.NodeCoordinate,
      result.Words,
      result.MemoryBaseAddress,
      result.InitialA,
      result.InitialB,
      result.InitialIo,
      result.InitialStack,
      result.EntryPoint);
}
