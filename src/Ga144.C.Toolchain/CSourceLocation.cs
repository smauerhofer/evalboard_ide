namespace Ga144.C.Toolchain;

/// <summary>
/// A single point in a C source file, as seen by the preprocessor: the file it came from (an
/// #include chain collapses back to whichever file actually holds the text), a 1-based line number,
/// and a 1-based column. Diagnostics are formatted MSVC/Visual-Studio-style,
/// "file(line,col): error: message", to match how build errors already appear in Visual Studio's
/// Error List and Output window.
/// </summary>
public sealed record CSourceLocation(string FileName, int Line, int Column)
{
  public override string ToString() => $"{FileName}({Line},{Column})";

  /// <summary>Formats a diagnostic the way Visual Studio expects: "file(line,col): severity: message".</summary>
  public string FormatDiagnostic(string severity, string message) => $"{this}: {severity}: {message}";
}
