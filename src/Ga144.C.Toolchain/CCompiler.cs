namespace Ga144.C.Toolchain;

/// <summary>Everything a single C source file went through on its way to CVM assembly text, for a
/// caller (the <c>gcc144</c> CLI, or eventually the IDE's own "Build" step) that wants to write out the
/// intermediate preprocessed text itself -- e.g. into a project's "pre" directory -- alongside the
/// final assembly.</summary>
public sealed class CCompileResult
{
  public required bool Success { get; init; }

  /// <summary>The reassembled preprocessed source text (what a <c>.i</c> file would hold), always
  /// populated once preprocessing itself succeeds, even if parsing or code generation later
  /// fails.</summary>
  public string? PreprocessedText { get; init; }

  /// <summary>The generated CVM assembly text (what a <c>.casm</c> file would hold), populated only
  /// once every phase succeeds.</summary>
  public string? Assembly { get; init; }

  /// <summary>Every diagnostic from every phase that ran, in the order preprocessing, parsing, code
  /// generation -- already formatted MSVC/Visual-Studio-style via <see
  /// cref="CSourceLocation.FormatDiagnostic"/>.</summary>
  public required IReadOnlyList<string> Diagnostics { get; init; }
}

/// <summary>
/// The whole C-to-CVM-assembly pipeline in one call: preprocess (<see cref="CPreprocessor"/>), parse
/// (<see cref="CParser"/>), generate code (<see cref="CCodeGenerator"/>) -- mirroring how
/// <c>Ga144.Cvm.Toolchain.CvmAssembler.Assemble</c> is the single entry point for its own stage of the
/// toolchain. Stops at the first phase that reports an error (per Stefan's own instruction, "if you
/// don't know how to implement a C feature then leave a stub in the compiler for later resolution" --
/// but a phase that DOES report an error has found a real problem, not a missing feature, so there is
/// no reason to keep going and risk cascading nonsense diagnostics from a later phase operating on a
/// broken tree).
/// </summary>
public static class CCompiler
{
  public static CCompileResult Compile(string fileName, string sourceText, ICIncludeResolver includeResolver, IReadOnlyList<(string Name, string Value)>? commandLineDefines = null)
  {
    var preprocessor = new CPreprocessor(includeResolver);
    foreach ((string name, string value) in commandLineDefines ?? [])
    {
      preprocessor.DefineFromCommandLine(name, value);
    }

    CPreprocessResult preprocessResult = preprocessor.Preprocess(fileName, sourceText);
    string preprocessedText = CTokenWriter.Write(preprocessResult.Tokens);

    if (!preprocessResult.Success)
    {
      return new CCompileResult { Success = false, PreprocessedText = preprocessedText, Diagnostics = preprocessResult.Diagnostics };
    }

    var parser = new CParser(preprocessResult.Tokens);
    (CTranslationUnit? unit, IReadOnlyList<string> parseDiagnostics) = parser.Parse();

    if (unit is null)
    {
      return new CCompileResult { Success = false, PreprocessedText = preprocessedText, Diagnostics = [.. preprocessResult.Diagnostics, .. parseDiagnostics] };
    }

    var generator = new CCodeGenerator();
    (string? assembly, IReadOnlyList<string> codeGenDiagnostics) = generator.Generate(unit);

    var allDiagnostics = new List<string>(preprocessResult.Diagnostics.Count + parseDiagnostics.Count + codeGenDiagnostics.Count);
    allDiagnostics.AddRange(preprocessResult.Diagnostics);
    allDiagnostics.AddRange(parseDiagnostics);
    allDiagnostics.AddRange(codeGenDiagnostics);

    return new CCompileResult { Success = assembly is not null, PreprocessedText = preprocessedText, Assembly = assembly, Diagnostics = allDiagnostics };
  }
}
