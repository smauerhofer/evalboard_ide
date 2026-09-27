namespace Ga144.Evb.Ide.Models;

/// <summary>
/// The C Debugger's own built-in seed program (added 2026-09-27, alongside <see cref="Ga144CDebuggerProgramConfiguration"/>,
/// per Stefan: "the C debugger must support multiple programs like the CVM debugger") -- mirrors
/// <see cref="Cvm.CvmDebuggerDefaultProgram"/>'s role for the CVM Debugger, but holds the exact text that used
/// to live as <c>CDebuggerViewModel.DefaultSourceCodeText</c> (a private const) before this feature existed,
/// carried over verbatim so a chip that never had a saved C Debugger program still opens to the same text it
/// always has.
///
/// UNLIKE <see cref="Cvm.CvmDebuggerDefaultProgram"/>, nothing in <see cref="ViewModels.CDebuggerViewModel"/>
/// forces the program named "default" to keep showing this text forever -- see
/// <see cref="Ga144ChipConfiguration.CDebuggerPrograms"/>'s own remarks for that deliberate deviation. This
/// class only supplies the ONE-TIME seed text for a brand-new "default" entry.
/// </summary>
public static class CDebuggerDefaultProgram
{
  public const string Source = "// Statements only -- this text is placed inside a hidden \"void main() { ... }\".\nint a = 3;\nint b = 4;\nint c = a + b;\n";
}
