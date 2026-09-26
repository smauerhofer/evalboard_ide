using System.IO;
using Ga144.C.Toolchain;
using Ga144.Cvm.Toolchain;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// Drives the C Debugger window -- Stefan's own request, 2026-09-26: "next I need a C debugger similar
/// to the existing CVM debugger. It is used to test C code. the text in the debugger source is placed
/// inside a hidden 'void main () {...}' block. all include files from all libraries are automatically
/// included for the compiler. all libraries are automatically included for the linker."
///
/// <b>What this class actually owns</b>: only the C-specific half -- <see cref="SourceCodeText"/> (the
/// statements Stefan types, NOT a whole translation unit -- see <see cref="CompileAndLoad"/>'s own
/// remarks for exactly how the hidden wrapper is applied) and the compile/assemble/link pipeline that
/// turns it into a <see cref="CvmImage"/>. Everything else a debugger needs -- Start/Step/Continue/Run/
/// Pause/Stop, breakpoints, the transaction log, the simulated SRAM inspector with its PC/breakpoint
/// gutter column -- is <see cref="Debugger"/>, a genuine, fully independent <see cref="CvmDebuggerViewModel"/>
/// instance this class wraps rather than reimplements: <see cref="CompileAndLoad"/>'s only interaction
/// with it is a single <see cref="CvmDebuggerViewModel.LoadImage"/> call once linking succeeds, handing
/// it the freshly built image plus this compile's own per-address C source comments. This is exactly
/// what makes "use the same column for the old CVM debugger" (Stefan's own words, about the PC/breakpoint
/// gutter) literally true rather than merely similar in spirit: the C Debugger's own memory inspector
/// IS a <see cref="CvmDebuggerViewModel.MemoryRows"/> collection, the very same type and gutter
/// convention the CVM Debugger's own window already uses.
///
/// <b>"all include files/libraries from all libraries are automatically included"</b> -- deliberately
/// DIFFERENT from a <see cref="CProjectViewModel"/>'s own Build, which only looks at a project's own
/// checkbox-selected <c>LibraryReferences</c> (see that class's own <c>AvailableLibraries</c> remarks).
/// The C Debugger has no backing <see cref="CProject"/> of its own to hold such a checkbox list against
/// in the first place -- its "source file" is a snippet typed directly into this window, not a project on
/// disk -- so "automatically include everything" is the only sensible default for a quick scratch test:
/// every project folder found directly under the shared "libs" workspace directory
/// (<paramref name="librariesDirectoryPath"/>, the same directory <c>MainWindowViewModel.CLibsDirectoryPath</c>
/// names) whose own <see cref="CProject.Kind"/> is <see cref="CProjectKind.Library"/> contributes its own
/// "include" directory to the compile and its own already-built <see cref="CProject.LibraryOutputPath"/>
/// (".galib") to the link -- unconditionally, with no per-library opt-out. A library that hasn't been
/// built yet (no ".galib" on disk) is skipped with a warning rather than a hard failure, since a program
/// under test may not need it at all -- if it actually does, <see cref="CvmLinker"/>'s own "undefined
/// reference" error will say so clearly once linking runs.
/// </summary>
public sealed class CDebuggerViewModel : ObservableObject
{
  // The display name this compile's own object file is registered under with CvmLinker.Link -- also
  // what CompileAndLoad filters CvmImage.Symbols by (via CvmImageSymbol.DefiningObjectName) to resolve
  // its own per-statement source-comment labels back to a final address without colliding with a
  // same-named local symbol some OTHER linked library object happens to also define (every C compile
  // emits "__srcline_0", "__srcline_1", ... -- see CCodeGenerator.SourceCommentLabels's own remarks for
  // why that's fine).
  private const string DebuggerObjectDisplayName = "c-debugger";

  // Threaded through to CCompiler.Compile as the "fileName" a CSourceLocation reports itself against --
  // purely a label (never read from disk), but it's also the ONE file name CCodeGenerator will ever
  // accept when deciding whether to emit a source-comment label for a given statement's own location
  // (see that class's own _sourceFileName remarks), so this must stay the same string every time.
  private const string DebuggerSourceFileName = "<c debugger>";

  private readonly Ga144ChipConfiguration _chip;
  private readonly Ga144RomLibrary _romLibrary;
  private readonly IReadOnlyList<F18MacroDefinition> _userMacros;
  private readonly string? _librariesDirectoryPath;
  private readonly CProjectStore _projectStore = new();

  private string _sourceCodeText = DefaultSourceCodeText;
  private string _diagnosticsText = "Not compiled yet. Click \"Compile && Load\" to build this source and load it into the debugger below.";

  private const string DefaultSourceCodeText = "// Statements only -- this text is placed inside a hidden \"void main() { ... }\".\nint a = 3;\nint b = 4;\nint c = a + b;\n";

  public CDebuggerViewModel(
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<F18MacroDefinition> userMacros,
      KrakenLiveController krakenController,
      Func<KrakenEndpointInfo?> resolveEndpoint,
      Action notifyProjectChanged,
      string projectDefaultNodeColor,
      string? librariesDirectoryPath)
  {
    _chip = chip ?? throw new ArgumentNullException(nameof(chip));
    _romLibrary = romLibrary ?? throw new ArgumentNullException(nameof(romLibrary));
    _userMacros = userMacros ?? [];
    _librariesDirectoryPath = librariesDirectoryPath;

    // The CVM Debugger instance this window's own run/step/breakpoint/memory-inspector controls are
    // bound to -- see this class's own remarks for why composition, not reimplementation, is the right
    // shape here. Constructed with the exact same chip/romLibrary/userMacros/kraken/endpoint/save-back
    // this window's own caller would otherwise have handed a plain CvmDebuggerViewModel directly (see
    // Views.ChipWindow.xaml.cs's own "C Debugger..." button handler).
    Debugger = new CvmDebuggerViewModel(chip, romLibrary, _userMacros, krakenController, resolveEndpoint, notifyProjectChanged, projectDefaultNodeColor);

    CompileAndLoadCommand = new RelayCommand(CompileAndLoad, () => !Debugger.IsBusy);

    // Debugger.IsBusy flips on every Start/Step/Continue/Run/Assemble/CoreDump -- Debugger's own
    // NotifyCommandStates (see its own remarks) only ever re-queries ITS OWN commands, since it has no
    // idea this wrapper's CompileAndLoadCommand exists, so without this subscription "Compile && Load"
    // would stay enabled (or stay stuck disabled) independently of whether the wrapped debugger is
    // actually free to accept a new image right now.
    Debugger.PropertyChanged += (_, e) =>
    {
      if (e.PropertyName == nameof(CvmDebuggerViewModel.IsBusy))
      {
        CompileAndLoadCommand.NotifyCanExecuteChanged();
      }
    };
  }

  /// <summary>The wrapped CVM Debugger this window's Start/Step/Continue/Run/Pause/Stop/breakpoints/
  /// memory-inspector controls are bound to directly -- see this class's own remarks.</summary>
  public CvmDebuggerViewModel Debugger { get; }

  /// <summary>
  /// The C statements Stefan types -- NOT a whole translation unit: per his own instruction, "the text
  /// in the debugger source is placed inside a hidden 'void main () {...}' block", so this window's own
  /// text editor holds only a function BODY (declarations and statements), never a <c>void main()</c>
  /// line or its own braces, which <see cref="CompileAndLoad"/> supplies invisibly at compile time. The
  /// wrapper adds text to the FRONT of line 1 only (never a whole extra line), so a compile diagnostic's
  /// own reported line number still matches this editor's own line numbers exactly -- only a column
  /// offset on line 1 itself is affected.
  /// </summary>
  public string SourceCodeText { get => _sourceCodeText; set => SetProperty(ref _sourceCodeText, value ?? string.Empty); }

  /// <summary>Every message from the most recent <see cref="CompileAndLoadCommand"/> run -- warnings
  /// about a not-yet-built library, compile/assemble/link diagnostics on failure, or a plain success
  /// line once <see cref="Debugger"/>'s own <see cref="CvmDebuggerViewModel.LoadImage"/> has run.</summary>
  public string DiagnosticsText { get => _diagnosticsText; private set => SetProperty(ref _diagnosticsText, value); }

  public RelayCommand CompileAndLoadCommand { get; }

  /// <summary>
  /// Wraps <see cref="SourceCodeText"/> in the hidden <c>void main() { ... }</c>, compiles it
  /// (<see cref="CCompiler"/>) against every library found under the shared "libs" directory (see this
  /// class's own remarks), assembles the result (<see cref="CvmAssembler"/>), links it
  /// (<see cref="CvmLinker"/>) against this chip's own live CVM2 mesh (<see cref="CvmPrimitiveTableExporter"/> --
  /// the exact same primitive-table source a Program project's own Build links against, except there is
  /// no separate "Chip project..." picker here: this window is already opened against one specific chip,
  /// same as the CVM Debugger itself), and on success hands the resulting <see cref="CvmImage"/> straight
  /// to <see cref="Debugger"/>'s own <see cref="CvmDebuggerViewModel.LoadImage"/> -- along with a
  /// per-address map of the C source line that produced each address (<see cref="CCompileResult.SourceCommentLabels"/>,
  /// resolved to final addresses via <see cref="CvmImageSymbol.DefiningObjectName"/> filtered to this
  /// compile's own <see cref="DebuggerObjectDisplayName"/>, so a same-named local symbol some OTHER
  /// linked library object happens to also define never gets confused for this compile's own).
  /// Stops (leaving <see cref="Debugger"/> showing whatever it loaded last) the moment any stage fails,
  /// same "don't guess past a real error" discipline this whole toolchain already follows elsewhere.
  /// </summary>
  private void CompileAndLoad()
  {
    if (Debugger.IsBusy)
    {
      DiagnosticsText = "Cannot compile while the debugger is busy (Step/Continue/Start in progress).";
      return;
    }

    var messages = new List<string>();

    // "all include files from all libraries are automatically included for the compiler. all libraries
    // are automatically included for the linker." -- every Library-kind project directly under the
    // shared "libs" directory, unconditionally; see this class's own remarks for why this differs from
    // CProjectViewModel.Build's own checkbox-selected AvailableLibraries.
    var includeDirectories = new List<string>();
    var libraryInputs = new List<CvmLinkLibraryInput>();
    if (!string.IsNullOrWhiteSpace(_librariesDirectoryPath) && Directory.Exists(_librariesDirectoryPath))
    {
      foreach (string folder in Directory.EnumerateDirectories(_librariesDirectoryPath)
          .Where(_projectStore.IsProjectFolder)
          .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase))
      {
        CProject library;
        try
        {
          library = _projectStore.Load(folder);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
          messages.Add($"Skipped \"{Path.GetFileName(folder)}\" -- could not load it as a C project: {exception.Message}");
          continue;
        }

        if (library.Kind != CProjectKind.Library)
        {
          continue;
        }

        includeDirectories.Add(library.IncludeDirectoryPath);
        if (File.Exists(library.LibraryOutputPath))
        {
          using FileStream libraryStream = File.OpenRead(library.LibraryOutputPath);
          libraryInputs.Add(new CvmLinkLibraryInput(library.Name, CvmLibrary.Load(libraryStream)));
        }
        else
        {
          messages.Add($"\"{library.Name}\" has no built {CProject.LibraryFileExtension} yet -- skipped (build it first if this source needs it).");
        }
      }
    }

    // Adds text to the front of line 1 ONLY (never a whole extra line) -- see SourceCodeText's own
    // remarks for why this matters for diagnostic line numbers.
    string wrappedSource = "void main() { " + SourceCodeText + "\n}\n";

    var resolver = new FileSystemIncludeResolver(includeDirectories);
    CCompileResult compileResult = CCompiler.Compile(DebuggerSourceFileName, wrappedSource, resolver);
    if (!compileResult.Success || compileResult.Assembly is null)
    {
      messages.Add("Compile failed:");
      messages.AddRange(compileResult.Diagnostics);
      DiagnosticsText = string.Join(Environment.NewLine, messages);
      return;
    }

    (CvmObjectFile? objectFile, IReadOnlyList<string> assembleErrors) = CvmAssembler.Assemble(compileResult.Assembly);
    if (objectFile is null)
    {
      messages.Add("Assemble failed:");
      messages.AddRange(assembleErrors);
      DiagnosticsText = string.Join(Environment.NewLine, messages);
      return;
    }

    CvmPrimitiveTableExporter.Result exported = CvmPrimitiveTableExporter.Export(_chip, _romLibrary, _userMacros);
    if (!exported.Success)
    {
      messages.Add("Linking failed -- no primitive table could be exported from this chip's own live CVM2 mesh:");
      messages.AddRange(exported.Messages);
      DiagnosticsText = string.Join(Environment.NewLine, messages);
      return;
    }

    var objectInputs = new List<CvmLinkObjectInput> { new(DebuggerObjectDisplayName, objectFile) };
    CvmLinker.Result linkResult = CvmLinker.Link(objectInputs, libraryInputs, exported.Table, new CvmLinkOptions());
    messages.AddRange(linkResult.Messages);
    if (!linkResult.Success || linkResult.Image is null)
    {
      DiagnosticsText = string.Join(Environment.NewLine, messages);
      return;
    }

    CvmImage image = linkResult.Image;

    // Resolve this compile's OWN per-statement source-comment labels to their final addresses -- see
    // this method's own remarks on why DefiningObjectName is what makes this unambiguous.
    Dictionary<string, int> ownLabelAddresses = image.Symbols
        .Where(symbol => symbol.DefiningObjectName == DebuggerObjectDisplayName)
        .ToDictionary(symbol => symbol.Name, symbol => symbol.Address, StringComparer.Ordinal);

    var sourceComments = new Dictionary<int, string>();
    foreach ((string label, string text) in compileResult.SourceCommentLabels)
    {
      if (ownLabelAddresses.TryGetValue(label, out int address))
      {
        sourceComments[address] = text;
      }
    }

    Debugger.LoadImage(image, "Compiled and linked from the C source editor", sourceComments);
    messages.Add("Compiled, assembled, and linked successfully -- loaded into the debugger below.");
    DiagnosticsText = string.Join(Environment.NewLine, messages);
  }
}
