namespace Ga144.C.Toolchain;

/// <summary>
/// A macro as introduced by "#define" (or one of the predefined macros the preprocessor seeds itself
/// with, such as "__LINE__"): its name, whether it is object-like ("#define X 1") or function-like
/// ("#define MAX(a,b) ..."), its parameter names in order, and its replacement list as already-lexed
/// tokens. Held by <see cref="CPreprocessor"/> in a name-keyed table for the lifetime of one
/// preprocessing run; "#undef" removes an entry, a later "#define" of the same name replaces it.
///
/// Not supported, deliberately: variadic macros ("..." / "__VA_ARGS__"). GA144 C sources are not
/// expected to need them, and skipping them keeps macro expansion considerably simpler.
/// </summary>
public sealed class CMacroDefinition
{
  public required string Name { get; init; }
  public required bool IsFunctionLike { get; init; }
  public IReadOnlyList<string> Parameters { get; init; } = [];
  public IReadOnlyList<CToken> ReplacementList { get; init; } = [];
  public required CSourceLocation DefinedAt { get; init; }

  /// <summary>
  /// A macro may be "#define"d again as long as the new definition is identical to the old one (same
  /// function-like-ness, same parameter names in the same order, same replacement-list tokens) --
  /// harmless in practice because it is exactly what happens when the same header is reachable
  /// through two different #include paths. Anything else is a redefinition and gets a diagnostic (the
  /// newer definition still wins, matching how most C compilers recover from this).
  /// </summary>
  public bool IsIdenticalTo(CMacroDefinition other)
  {
    if (IsFunctionLike != other.IsFunctionLike || Parameters.Count != other.Parameters.Count || ReplacementList.Count != other.ReplacementList.Count)
    {
      return false;
    }

    for (int i = 0; i < Parameters.Count; i++)
    {
      if (Parameters[i] != other.Parameters[i])
      {
        return false;
      }
    }

    for (int i = 0; i < ReplacementList.Count; i++)
    {
      CToken a = ReplacementList[i];
      CToken b = other.ReplacementList[i];
      if (a.Kind != b.Kind || a.Text != b.Text || (i > 0 && a.HasLeadingWhitespace != b.HasLeadingWhitespace))
      {
        return false;
      }
    }

    return true;
  }
}
