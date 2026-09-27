using Ga144.C.Toolchain;
using Ga144.Cvm.Toolchain;
using Ga144.Evb.Ide.Cvm;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;
using System.Collections.ObjectModel;
using System.IO;

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
  private readonly Action _notifyProjectChanged;

  // The C Debugger's own named programs (added 2026-09-27, per Stefan: "the C debugger must support
  // multiple programs like the CVM debugger") -- see this class's own remarks on Programs/SelectedProgram
  // below for the full mirrored design, and Ga144ChipConfiguration.CDebuggerPrograms's own remarks for
  // the one deliberate deviation from CvmDebuggerViewModel's own "default" special case. _sourceCodeText
  // is now populated by InitializePrograms (constructor) / SelectedProgram's own setter, never left at a
  // hardcoded default the way it was before this feature existed.
  private Ga144CDebuggerProgramConfiguration? _selectedProgram;
  private string _programNameText = string.Empty;

  private string _sourceCodeText = CDebuggerDefaultProgram.Source;
  private string _diagnosticsText = "Not compiled yet. Click \"Compile & Load\" to build this source and load it into the debugger below.";

  // Added 2026-09-27, per Stefan: "in the C debugger i want to be able to select the compiler
  // optimization options." CCompiler.Compile has taken both of these as optional parameters since
  // 2026-09-10 (see its own doc comment, "the IDE's own C project settings... expose both as a
  // per-project, persisted choice") -- CProjectViewModel's Build already exposes them via two checkboxes
  // bound to its own backing CProject (see EnableConstantFolding/EnablePeepholeOptimization there, and
  // CProjectWindow.xaml's own matching CheckBoxes). This window has no backing CProject at all (see this
  // class's own doc comment -- it's a standalone snippet compiler, not a project), so there is nothing to
  // persist these into; they simply live here as plain view-model state, defaulting to the exact same
  // values CCompiler.Compile itself defaults to when neither is specified (constant folding on, peephole
  // optimization off), so leaving both untouched compiles exactly as this window already did before this
  // feature existed.
  private bool _enableConstantFolding = true;
  private bool _enablePeepholeOptimization;

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
    _notifyProjectChanged = notifyProjectChanged ?? throw new ArgumentNullException(nameof(notifyProjectChanged));

    // The CVM Debugger instance this window's own run/step/breakpoint/memory-inspector controls are
    // bound to -- see this class's own remarks for why composition, not reimplementation, is the right
    // shape here. Constructed with the exact same chip/romLibrary/userMacros/kraken/endpoint/save-back
    // this window's own caller would otherwise have handed a plain CvmDebuggerViewModel directly (see
    // Views.ChipWindow.xaml.cs's own "C Debugger..." button handler).
    Debugger = new CvmDebuggerViewModel(chip, romLibrary, _userMacros, krakenController, resolveEndpoint, notifyProjectChanged, projectDefaultNodeColor);

    CompileAndLoadCommand = new RelayCommand(CompileAndLoad, () => !Debugger.IsBusy);
    AddProgramCommand = new RelayCommand(AddProgram);
    SaveCommand = new RelayCommand(SaveProgram);
    RestoreCommand = new RelayCommand(RestoreProgram);

    // Debugger.IsBusy flips on every Start/Step/Continue/Run/Assemble/CoreDump -- Debugger's own
    // NotifyCommandStates (see its own remarks) only ever re-queries ITS OWN commands, since it has no
    // idea this wrapper's CompileAndLoadCommand exists, so without this subscription "Compile & Load"
    // would stay enabled (or stay stuck disabled) independently of whether the wrapped debugger is
    // actually free to accept a new image right now.
    Debugger.PropertyChanged += (_, e) =>
    {
      if (e.PropertyName == nameof(CvmDebuggerViewModel.IsBusy))
      {
        CompileAndLoadCommand.NotifyCanExecuteChanged();
      }
    };

    // Multiple named programs (added 2026-09-27, per Stefan: "the C debugger must support multiple
    // programs like the CVM debugger") -- rebuilds Programs/SelectedProgram/SourceCodeText/ProgramNameText
    // from this chip's own Ga144ChipConfiguration.CDebuggerPrograms, reselecting whichever program was
    // selected last time. See InitializePrograms's own remarks for the one deliberate difference from
    // CvmDebuggerViewModel's own constructor: no auto Compile & Load runs here (or when SelectedProgram
    // changes below) -- switching a program only loads its text into the editor, since a full compile is a
    // much heavier, more visible operation than the CVM Debugger's own in-memory Assemble.
    InitializePrograms();
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
  ///
  /// <b>Multiple programs (added 2026-09-27)</b>: this is now always <see cref="SelectedProgram"/>'s own
  /// editable text, mirroring <see cref="CvmDebuggerViewModel.AssemblyCodeText"/>'s exact relationship to
  /// its own <c>SelectedProgram</c> -- see <see cref="Programs"/>'s own remarks for the full design.
  /// </summary>
  public string SourceCodeText { get => _sourceCodeText; set => SetProperty(ref _sourceCodeText, value ?? string.Empty); }

  /// <summary>Every message from the most recent <see cref="CompileAndLoadCommand"/> run -- warnings
  /// about a not-yet-built library, compile/assemble/link diagnostics on failure, or a plain success
  /// line once <see cref="Debugger"/>'s own <see cref="CvmDebuggerViewModel.LoadImage"/> has run. Also
  /// doubles as the status line for <see cref="AddProgramCommand"/>/<see cref="SaveCommand"/>/
  /// <see cref="RestoreCommand"/> (this class has no separate "StatusText" the way
  /// <see cref="CvmDebuggerViewModel"/> does).</summary>
  public string DiagnosticsText { get => _diagnosticsText; private set => SetProperty(ref _diagnosticsText, value); }

  /// <summary>Added 2026-09-27 -- see this class's own remarks on <see cref="_enableConstantFolding"/>.
  /// Threaded straight into the next <see cref="CompileAndLoadCommand"/> run's own
  /// <see cref="CCompiler.Compile"/> call; changing it has no effect on a program already loaded into
  /// <see cref="Debugger"/> until "Compile &amp; Load" runs again.</summary>
  public bool EnableConstantFolding { get => _enableConstantFolding; set => SetProperty(ref _enableConstantFolding, value); }

  /// <summary>Added 2026-09-27 -- see this class's own remarks on <see cref="_enableConstantFolding"/>.
  /// Off by default, same as <see cref="CCompiler.Compile"/>'s own default and
  /// <see cref="CProjectViewModel.EnablePeepholeOptimization"/>'s own starting value, since
  /// <see cref="CvmPeepholeOptimizer"/>'s two reload-elision rules rely on a "a store leaves r unchanged"
  /// hardware assumption not yet confirmed against real hardware (see that class's own remarks and
  /// <see cref="OptimizerDescription"/> below).</summary>
  public bool EnablePeepholeOptimization { get => _enablePeepholeOptimization; set => SetProperty(ref _enablePeepholeOptimization, value); }

  /// <summary>Added 2026-09-27 -- shown next to the two checkboxes above so it's clear from the window
  /// itself, not just a tooltip, what each one does. Same wording as
  /// <see cref="CProjectViewModel.OptimizerDescription"/>'s own (not shared via a common base type, since
  /// the two view models otherwise have nothing else in common).</summary>
  public string OptimizerDescription =>
      "Constant folding evaluates compile-time-constant arithmetic in the C source before code generation; " +
      "peephole optimization removes redundant instruction sequences the code generator itself introduces " +
      "(not yet confirmed against real hardware -- see the C compiler design doc).";

  // ---------------------------------------------------------------------------------------------
  // Multiple programs (added 2026-09-27), per Stefan: "the C debugger must support multiple programs
  // like the CVM debugger." Mirrors CvmDebuggerViewModel's own Programs/SelectedProgram/ProgramNameText/
  // AddProgramCommand/SaveCommand/RestoreCommand design (see that class's own doc comment and each
  // member's own remarks there) almost exactly -- same "always at least one, 'default' always first"
  // contract (Ga144ChipConfiguration.CDebuggerPrograms/EnsureCDebuggerPrograms, mirroring
  // DebuggerPrograms/EnsureDebuggerPrograms), same working-copy-instances-until-Save discipline, same
  // "switching a program commits its source but discards an unsaved rename" asymmetry, same
  // empty-name/name-collision refusal on Save, same "Restore reloads from what's actually persisted, or
  // no-ops with a message for a program that was never saved" behavior.
  //
  // Two deliberate differences from the CVM Debugger's own version, both flagged rather than silently
  // carried over:
  // 1. No "default" special case. CvmDebuggerViewModel.LoadEditorFromProgram always shows the built-in
  //    CvmDebuggerDefaultProgram.Source for a program literally named "default", regardless of what is
  //    actually saved under that name -- a deliberate "always one known-good, always-assemblable test
  //    program" guarantee tied to that debugger's own hardware-confidence-test role (see that method's
  //    own remarks). Nothing here calls for the same guarantee, so this class's own "default" is just an
  //    ordinary, normally-editable, normally-persisted program that merely happens to be seeded from
  //    CDebuggerDefaultProgram.Source the first time a chip gets one and to sort first in the list.
  // 2. No auto Compile & Load on selection or at startup. CvmDebuggerViewModel's own SelectedProgram
  //    setter (and its constructor) immediately re-Assembles after loading a program's text, because
  //    Assemble is a cheap, side-effect-limited, purely in-memory operation ("selecting IS loading",
  //    Stefan's own words). CompileAndLoad here is a much heavier, more visible pipeline (parse, codegen,
  //    assemble, resolve every library on disk, link, and hand the result to Debugger.LoadImage, which
  //    can replace whatever the wrapped debugger currently has loaded) -- silently re-running all of that
  //    every time the program dropdown changes, or the moment this window opens, seemed like the wrong
  //    default to assume unasked. Switching programs (or opening the window) only loads the selected
  //    program's own text into SourceCodeText; "Compile & Load" still has to be clicked explicitly, same
  //    as before this feature existed. Happy to wire up auto-compile-on-switch too if that's what was
  //    actually wanted.
  // ---------------------------------------------------------------------------------------------

  /// <summary>The C Debugger's own named programs, mirroring
  /// <see cref="CvmDebuggerViewModel.Programs"/> -- see this section's own remarks for the two deliberate
  /// differences. Fresh working-copy <see cref="Ga144CDebuggerProgramConfiguration"/> instances (never the
  /// same references <see cref="Ga144ChipConfiguration.CDebuggerPrograms"/> holds), so nothing typed here
  /// reaches the project's own persisted data until <see cref="SaveCommand"/> runs.</summary>
  public ObservableCollection<Ga144CDebuggerProgramConfiguration> Programs { get; } = [];

  /// <summary>The program selector's own <c>SelectedItem</c>, mirroring
  /// <see cref="CvmDebuggerViewModel.SelectedProgram"/> -- switching first commits
  /// <see cref="SourceCodeText"/>'s current contents back into the program being switched AWAY from (an
  /// unsaved RENAME in <see cref="ProgramNameText"/> is discarded instead, same asymmetry as the CVM
  /// Debugger's own version), then loads the newly selected program's own saved text into
  /// <see cref="SourceCodeText"/> -- but, unlike the CVM Debugger, does NOT auto-compile (see this
  /// section's own remarks, point 2). Persists
  /// <see cref="Ga144ChipConfiguration.LastSelectedCDebuggerProgramName"/> immediately, not gated behind
  /// Save, same as the CVM Debugger's own version.</summary>
  public Ga144CDebuggerProgramConfiguration? SelectedProgram
  {
    get => _selectedProgram;
    set
    {
      if (ReferenceEquals(_selectedProgram, value))
      {
        return;
      }

      CommitEditorSourceIntoSelectedProgram();
      _selectedProgram = value;
      OnPropertyChanged();
      if (value is not null)
      {
        LoadEditorFromProgram(value);
      }

      _chip.LastSelectedCDebuggerProgramName = value?.Name;
      _notifyProjectChanged();
    }
  }

  /// <summary>The program-name text box's own contents, mirroring
  /// <see cref="CvmDebuggerViewModel.ProgramNameText"/> exactly -- always shows
  /// <see cref="SelectedProgram"/>'s current name, but a typed edit here only actually renames the
  /// program when <see cref="SaveCommand"/> runs; switching <see cref="SelectedProgram"/> away first
  /// discards a not-yet-saved rename attempt (unlike an edited-but-unsaved <see cref="SourceCodeText"/>,
  /// which IS carried over across a switch).</summary>
  public string ProgramNameText { get => _programNameText; set => SetProperty(ref _programNameText, value ?? string.Empty); }

  public RelayCommand CompileAndLoadCommand { get; }

  /// <summary>"Add", mirroring <see cref="CvmDebuggerViewModel.AddProgramCommand"/>.</summary>
  public RelayCommand AddProgramCommand { get; }

  /// <summary>"Save", mirroring <see cref="CvmDebuggerViewModel.SaveCommand"/>.</summary>
  public RelayCommand SaveCommand { get; }

  /// <summary>"Restore", mirroring <see cref="CvmDebuggerViewModel.RestoreCommand"/>.</summary>
  public RelayCommand RestoreCommand { get; }

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

    // Added 2026-09-26, after an unhandled exception from deep inside this pipeline (compile/assemble/
    // export-primitives/link -- several thousand lines of code, most of it never before exercised
    // end-to-end for a bare, freshly-typed snippet with no backing .c file at all) propagated all the
    // way out of this RelayCommand's Execute and was caught by the UNRELATED try/catch around
    // ChipWindow's own ShowDialog() in MainWindow.xaml.cs -- because that window is shown modally, its
    // catch block stays on the call stack for as long as the window is open, so it ends up catching an
    // exception from a completely different, later action (this one) and mislabels it "Unable to open
    // the GA144 chip window". Every other risky command in this codebase already wraps itself this way
    // (see CvmDebuggerViewModel.Assemble's own remarks on the exact same class of problem) -- this
    // method never had that safety net. The exception's own type/message/stack trace are surfaced
    // directly in DiagnosticsText rather than swallowed, since a silent no-op on an unhandled exception
    // is worse than a wrong-looking error message, and because pinning down exactly which stage failed
    // is the whole point of catching it here at all.
    try
    {
      CompileAndLoadCore(messages);
    }
    catch (Exception exception)
    {
      messages.Add($"Compile & Load failed unexpectedly, inside the C toolchain itself rather than as an ordinary compile/link diagnostic ({exception.GetType().FullName}): {exception.Message}");
      messages.Add(exception.StackTrace ?? "(no stack trace available)");
      DiagnosticsText = string.Join(Environment.NewLine, messages);
    }
  }

  private void CompileAndLoadCore(List<string> messages)
  {
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
    CCompileResult compileResult = CCompiler.Compile(
        DebuggerSourceFileName,
        wrappedSource,
        resolver,
        enableConstantFolding: EnableConstantFolding,
        enablePeepholeOptimization: EnablePeepholeOptimization);
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

  /// <summary>
  /// (Re)builds <see cref="Programs"/>/<see cref="SelectedProgram"/>/<see cref="SourceCodeText"/>/
  /// <see cref="ProgramNameText"/> from this chip's own <see cref="Ga144ChipConfiguration.CDebuggerPrograms"/>
  /// -- called once, by the constructor. Mirrors <see cref="CvmDebuggerViewModel.InitializePrograms"/>
  /// exactly, except it does not re-compile at the end (see this class's own remarks on
  /// <see cref="Programs"/>, point 2). <see cref="Ga144ChipConfiguration.Normalize"/> is what actually
  /// guarantees at least one ("default") entry exists -- this method just mirrors whatever it produces
  /// into fresh working copies (never the SAME <see cref="Ga144CDebuggerProgramConfiguration"/> instances
  /// <c>_chip</c> holds) so nothing typed here reaches the project's own data ahead of
  /// <see cref="SaveCommand"/>.
  /// </summary>
  private void InitializePrograms()
  {
    _chip.Normalize();
    Programs.Clear();
    foreach (Ga144CDebuggerProgramConfiguration saved in _chip.CDebuggerPrograms)
    {
      Programs.Add(new Ga144CDebuggerProgramConfiguration { Name = saved.Name, Source = saved.Source });
    }

    Ga144CDebuggerProgramConfiguration selected = Programs.FirstOrDefault(
        program => string.Equals(program.Name, _chip.LastSelectedCDebuggerProgramName, StringComparison.OrdinalIgnoreCase))
        ?? Programs[0];

    _selectedProgram = selected;
    OnPropertyChanged(nameof(SelectedProgram));
    LoadEditorFromProgram(selected);
  }

  /// <summary>Loads one program's text/name into <see cref="SourceCodeText"/>/<see cref="ProgramNameText"/>
  /// -- shared by <see cref="InitializePrograms"/> (opening the window) and <see cref="SelectedProgram"/>'s
  /// own setter (switching programs). UNLIKE <see cref="CvmDebuggerViewModel.LoadEditorFromProgram"/>,
  /// there is no "default" name override here -- every program, including one named "default", always
  /// loads its own actually-saved <see cref="Ga144CDebuggerProgramConfiguration.Source"/> (see this
  /// class's own remarks on <see cref="Programs"/>, point 1).</summary>
  private void LoadEditorFromProgram(Ga144CDebuggerProgramConfiguration program)
  {
    _sourceCodeText = program.Source;
    OnPropertyChanged(nameof(SourceCodeText));
    _programNameText = program.Name;
    OnPropertyChanged(nameof(ProgramNameText));
  }

  /// <summary>Writes <see cref="SourceCodeText"/>'s current contents back into <see cref="SelectedProgram"/>'s
  /// own <see cref="Ga144CDebuggerProgramConfiguration.Source"/> -- called before switching
  /// <see cref="SelectedProgram"/> away (so the edit is not lost) and again at the top of
  /// <see cref="SaveProgram"/>. Deliberately does NOT also copy <see cref="ProgramNameText"/> into
  /// <see cref="SelectedProgram"/>'s own Name -- see that property's own remarks for why a rename is held
  /// back until Save specifically.</summary>
  private void CommitEditorSourceIntoSelectedProgram()
  {
    if (_selectedProgram is not null)
    {
      _selectedProgram.Source = SourceCodeText;
    }
  }

  /// <summary>
  /// "Add": appends a new, empty, uniquely-named program (e.g. "New program", "New program 2", ...) to
  /// <see cref="Programs"/> and selects it -- selecting it is what actually loads its (empty) text into
  /// the editor (see <see cref="SelectedProgram"/>'s own remarks). Purely an in-memory addition: like
  /// every other edit here, it is not persisted onto <see cref="Ga144ChipConfiguration.CDebuggerPrograms"/>
  /// until <see cref="SaveCommand"/> runs, so closing the C Debugger without Saving afterward discards it.
  /// </summary>
  private void AddProgram()
  {
    // No explicit CommitEditorSourceIntoSelectedProgram() call needed here -- the SelectedProgram setter
    // below already flushes the currently-selected program's edited text before switching.
    string name = GenerateUniqueProgramName("New program");
    var program = new Ga144CDebuggerProgramConfiguration { Name = name, Source = string.Empty };
    Programs.Add(program);
    SelectedProgram = program;
    DiagnosticsText = $"Added a new program \"{name}\". Edit its C source, then click Save to keep it in the project.";
  }

  private string GenerateUniqueProgramName(string baseName)
  {
    if (Programs.All(program => !string.Equals(program.Name, baseName, StringComparison.OrdinalIgnoreCase)))
    {
      return baseName;
    }

    int suffix = 2;
    while (Programs.Any(program => string.Equals(program.Name, $"{baseName} {suffix}", StringComparison.OrdinalIgnoreCase)))
    {
      suffix++;
    }

    return $"{baseName} {suffix}";
  }

  /// <summary>
  /// "Save": commits the editor's current <see cref="SourceCodeText"/> into <see cref="SelectedProgram"/>'s
  /// own Source, renames it to whatever <see cref="ProgramNameText"/> currently holds (a no-op if it was
  /// not actually changed), then persists the WHOLE <see cref="Programs"/> list -- every program, not just
  /// the selected one, so an edit made to another program before switching away, or a brand-new one from
  /// <see cref="AddProgramCommand"/>, is never silently lost -- onto
  /// <see cref="Ga144ChipConfiguration.CDebuggerPrograms"/> and notifies the owning project. Refuses an
  /// empty name or one that collides with a DIFFERENT program already in the list, leaving everything else
  /// (including the source-text commit) exactly where it stood so a rename mistake never blocks getting
  /// the source itself saved.
  /// </summary>
  private void SaveProgram()
  {
    if (SelectedProgram is not { } selected)
    {
      return;
    }

    CommitEditorSourceIntoSelectedProgram();

    string newName = ProgramNameText.Trim();
    if (newName.Length == 0)
    {
      DiagnosticsText = "Cannot save: the program name cannot be empty.";
      return;
    }

    if (Programs.Any(program => !ReferenceEquals(program, selected) && string.Equals(program.Name, newName, StringComparison.OrdinalIgnoreCase)))
    {
      DiagnosticsText = $"Cannot save: another program is already named \"{newName}\".";
      return;
    }

    // Ga144CDebuggerProgramConfiguration.Name raises INotifyPropertyChanged (same bug-fix precedent as
    // Ga144DebuggerProgramConfiguration -- see that class's own remarks), so this plain assignment is
    // enough for the ComboBox's DisplayMemberPath binding to pick up the new text immediately.
    bool renamed = !string.Equals(selected.Name, newName, StringComparison.Ordinal);
    selected.Name = newName;
    ProgramNameText = newName;

    _chip.CDebuggerPrograms = [.. Programs.Select(program => new Ga144CDebuggerProgramConfiguration { Name = program.Name, Source = program.Source })];
    _notifyProjectChanged();
    DiagnosticsText = renamed
        ? $"Saved and renamed the program to \"{newName}\"."
        : $"Saved \"{newName}\" to the project.";
  }

  /// <summary>
  /// "Restore": reloads the CURRENTLY selected program from what is actually persisted on this chip
  /// (<see cref="Ga144ChipConfiguration.CDebuggerPrograms"/>), undoing whatever has been typed or renamed
  /// here since the last <see cref="SaveCommand"/>. Matches the selected program's persisted counterpart
  /// by <see cref="SelectedProgram"/>'s own <see cref="Ga144CDebuggerProgramConfiguration.Name"/> -- which
  /// only ever changes at Save time, so this still finds the right persisted entry even with an unsaved,
  /// not-yet-saved rename sitting in <see cref="ProgramNameText"/>. A program <see cref="AddProgramCommand"/>
  /// created that has never been saved has no persisted counterpart at all -- restoring it would mean
  /// deleting it, which is not what "undo unsaved writing" means for a program that was never written
  /// anywhere yet, so this is a no-op (with an explanatory message) instead.
  /// </summary>
  private void RestoreProgram()
  {
    if (SelectedProgram is not { } selected)
    {
      return;
    }

    Ga144CDebuggerProgramConfiguration? persisted = _chip.CDebuggerPrograms.FirstOrDefault(
        program => string.Equals(program.Name, selected.Name, StringComparison.Ordinal));
    if (persisted is null)
    {
      DiagnosticsText = $"\"{selected.Name}\" has never been saved -- nothing in the project to restore from.";
      return;
    }

    LoadEditorFromProgram(persisted);
    selected.Source = _sourceCodeText;
    DiagnosticsText = $"Restored \"{selected.Name}\" to what was last saved.";
  }
}