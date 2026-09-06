namespace Ga144.C.Toolchain;

/// <summary>The outcome of running <see cref="CPreprocessor"/> over one C source file.</summary>
public sealed class CPreprocessResult
{
  /// <summary>False if any error-level diagnostic was produced (an unterminated literal, a failed
  /// "#include", a "#error" directive, an unterminated "#if", and so on). A caller can still choose to
  /// look at <see cref="Tokens"/> when this is false -- preprocessing keeps going after most errors,
  /// the same way a compiler keeps parsing after one -- but the result should not be handed to a
  /// further compilation stage.</summary>
  public required bool Success { get; init; }

  /// <summary>The fully macro-expanded token stream, with all directives already consumed (none of
  /// "#define"/"#include"/"#if" and so on survive into this list) and only real source text left --
  /// ready either to be written back out as preprocessed source text (<see cref="CTokenWriter"/>) or
  /// consumed directly by a later compiler phase.</summary>
  public required IReadOnlyList<CToken> Tokens { get; init; }

  /// <summary>Diagnostics in Visual-Studio-style "file(line,col): severity: message" format, in the
  /// order they were produced.</summary>
  public required IReadOnlyList<string> Diagnostics { get; init; }

  /// <summary>Every file that was "#include"d while producing <see cref="Tokens"/> (not including the
  /// top-level file itself), in first-encountered order -- useful for a build system that wants to
  /// know when to re-run the preprocessor because a header changed.</summary>
  public IReadOnlyList<string> IncludedFiles { get; init; } = [];
}
