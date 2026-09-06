namespace Ga144.C.Toolchain;

/// <summary>The file an "#include" directive resolved to: its content, and a name to attribute
/// diagnostics and further relative-#include lookups to.</summary>
public sealed record CResolvedInclude(string FileName, string Content);

/// <summary>
/// Finds the file an "#include" directive names. <see cref="CPreprocessor"/> knows nothing about the
/// IDE's C-project model (its "include"/"src" folders, its library references) or about the
/// filesystem at all -- it only ever asks this interface to resolve a name, which is what lets the
/// same preprocessor engine be driven both by the standalone CLI (a plain filesystem resolver rooted
/// at the input file's folder plus any "-I" paths) and, later, by the IDE (a resolver built from a
/// <c>CProject</c>'s own include directory plus its library references' include directories) without
/// either one depending on the other.
/// </summary>
public interface ICIncludeResolver
{
  /// <summary>
  /// Resolves one "#include" directive. <paramref name="isAngled"/> is true for
  /// <c>#include &lt;name&gt;</c> and false for <c>#include "name"</c> -- the only difference the C
  /// standard mandates is where the search starts (the quoted form additionally checks alongside
  /// <paramref name="includingFileName"/> before falling back to the same search the angled form
  /// uses), so most implementations of this method can check the quoted-only location first and then
  /// share the rest of the search with the angled form.
  /// </summary>
  /// <param name="includeName">The text between the quotes or angle brackets, e.g. "cvm.h".</param>
  /// <param name="isAngled">True for "&lt;name&gt;", false for "\"name\"".</param>
  /// <param name="includingFileName">The file containing the #include directive, for resolving the
  /// quoted form relative to it.</param>
  bool TryResolve(string includeName, bool isAngled, string includingFileName, out CResolvedInclude? resolved);
}
