using System.Collections.ObjectModel;
using System.Text;
using Ga144.Cvm.Toolchain;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Cvm;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// Drives the CVM Debugger window: starts an interactive <see cref="CvmDebugSession"/> against real
/// hardware (compile + boot the mesh, load the shared test program, wake node 708's <c>'start</c>,
/// same as "Install &amp; run CVM test"), then lets Stefan single-step or run to a breakpoint,
/// inspect the simulated SRAM, and watch the transaction log build up one memory-interface request
/// at a time instead of the automatic test's all-at-once summary. The Assembly Code editor
/// (<see cref="AssemblyCodeText"/>/<see cref="AssembleCommand"/>) lets that starting program be
/// replaced by hand-written CVM asm at any time -- trying a new opcode against the connected chip no
/// longer needs a code change and a rebuild, just an edit and a click. Stefan can keep SEVERAL such
/// programs on one chip (<see cref="Programs"/>, backed by <see cref="Ga144ChipConfiguration.DebuggerPrograms"/>
/// -- added 2026-09-20, replacing an earlier single-scratch-buffer design): <see cref="SelectedProgram"/>
/// is the program selector's own binding -- switching it AUTOMATICALLY loads that program's text into
/// the editor and re-assembles it (there is deliberately no separate "Load" button/step any more;
/// selecting IS loading), <see cref="AddProgramCommand"/> ("Add") creates a new, empty, uniquely-named
/// one, and <see cref="ProgramNameText"/> is a rename box that only actually takes effect when
/// <see cref="SaveCommand"/> runs (which also persists the whole list onto this chip's own project
/// data so it survives closing the debugger), while <see cref="RestoreCommand"/> (added 2026-09-20,
/// REPURPOSED from an earlier "reset to the built-in test program" behavior, per Stefan's own request)
/// reloads the CURRENTLY selected program from what is actually persisted on this chip -- i.e. undoes
/// any unsaved edits (both to the Assembly Code text and to a not-yet-saved rename in
/// <see cref="ProgramNameText"/>) back to whatever <see cref="SaveCommand"/> last actually wrote.
/// Selecting the
/// program literally named "default" is special-cased to always (re)load
/// <see cref="CvmDebuggerDefaultProgram.Source"/> itself into the editor, regardless of whatever text
/// happens to be saved under that name -- see <see cref="LoadEditorFromProgram"/>'s own remarks; every
/// OTHER program's own saved <see cref="Ga144DebuggerProgramConfiguration.Source"/> loads normally. The
/// constructor itself re-opens EXACTLY where Stefan left off: <see cref="InitializePrograms"/> rebuilds
/// <see cref="Programs"/> from this chip's own Saved list (migrating an older single-program save, or
/// seeding a fresh "default" one, the first time -- see <see cref="Ga144ChipConfiguration.Normalize"/>'s
/// own remarks), then reselects whichever program
/// <see cref="Ga144ChipConfiguration.LastSelectedDebuggerProgramName"/> names (falling back to the
/// first entry if that program no longer exists, or this is the very first time the chip's debugger
/// has ever been opened) -- persisted immediately whenever <see cref="SelectedProgram"/> changes, not
/// gated behind Save, since which program was last open is UI state, not program content. Either way
/// the constructor immediately calls <see cref="Assemble"/> once so the window opens with its program
/// already assembled into the standalone simulated SRAM -- not just sitting as unassembled text in the
/// editor waiting for a manual click.
///
/// Assemble itself never actually needed a connected chip -- node 607's source compiles the same way
/// whether or not one is attached -- so it no longer requires an active session: with none, it
/// assembles into a standalone simulated SRAM (<see cref="_standaloneSram"/>) that exists for this
/// view model's whole lifetime, so a program can be written and checked before ever clicking Start.
/// <see cref="StartAsync"/> re-applies <see cref="AssemblyCodeText"/> to the chip the moment it
/// connects, so nothing assembled standalone is lost.
///
/// <see cref="LoadImageFile"/>, added 2026-09-08 once `galink` existed to produce a `.gaimg`, is a
/// second, independent way to get a program running here -- for one built outside this window
/// entirely (a Program project's own "Build") rather than typed into the Assembly Code editor by hand.
/// It loads straight into whichever SRAM is currently live (the session's real one, or
/// <see cref="_standaloneSram"/>) with no assembly step, since a linked image's words are already
/// fully resolved, and its own symbol table then annotates the memory inspector's rows by name until
/// something else (Assemble, or a fresh Start) overwrites what it loaded. The loaded image itself is
/// also remembered (<see cref="_loadedImage"/>), so calling this with no session yet connected -- the
/// normal case right after a Program project's own "Debug" button opens this window -- is not just an
/// inert preview: the very next Start / Reinstall re-applies that same image to the freshly booted
/// chip, rather than reverting to whatever the Assembly Code editor separately holds.
/// </summary>
public sealed class CvmDebuggerViewModel : ObservableObject
{
  // Generous compared to the automatic test's own 96-transaction cap: an interactive Run is expected
  // to be aimed at a specific breakpoint, but should not spin forever chasing one that never fires.
  private const int ContinueTransactionCap = 2_000;

  // The FLOOR on how many words the memory inspector shows at once (and the default starting
  // point is page 0, address 0 -- the start of the loaded test program -- before Stefan jumps
  // elsewhere). This used to be the fixed count too, but that silently truncated the view well
  // short of a longer loaded program (the 156-word CvmDebuggerDefaultProgram in particular ends
  // at 0x009B, past this constant's own old 64-word/0x003F reach) -- see RefreshMemoryView's own
  // remarks for how the actual count is now sized to whatever program is currently loaded.
  private const int MemoryViewWordCount = 64;

  private readonly Ga144ChipConfiguration _chip;
  private readonly Ga144RomLibrary _romLibrary;
  private readonly IReadOnlyList<F18MacroDefinition> _userMacros;
  private readonly KrakenLiveController _krakenController;
  private readonly Func<KrakenEndpointInfo?> _resolveEndpoint;

  // Only used to color the post-mortem GA144 window a Core Dump opens -- the project's DefaultNodeColor
  // itself, not a live ProjectViewModel reference, same "just the hex string" convention already used
  // for NodeViewModel/NodeEditorViewModel's own projectDefaultColor parameter.
  private readonly string _projectDefaultNodeColor;

  // Bubbles up to the owning project (ProjectViewModel.NotifyProjectChanged, ultimately
  // MainWindowViewModel's debounced auto-save) -- the same mechanism every other edit in this IDE
  // rides, so clicking Save here needs no bespoke save-to-disk logic of its own.
  private readonly Action _notifyProjectChanged;

  // A plain in-memory simulated SRAM that exists for this view model's whole lifetime -- unlike
  // CvmDebugSession's own SRAM, it is NOT tied to a live session, so Assemble (and the memory
  // inspector) always have somewhere to write/read even before the first Start or after a Stop.
  // There is nothing hardware-specific about assembling: every CVM node's source compiles the same
  // way whether or not a chip is connected (CompileStandaloneCvmNodes), so there is no real reason
  // Assemble should ever have needed a session in the first place. Mirrored from the live session's
  // own program after every successful Start/Assemble (MirrorSessionProgramIntoStandaloneSram) so
  // that Stop leaves the memory inspector showing the last real state instead of stale pre-Start
  // content.
  private readonly CvmSimulatedSram _standaloneSram = new();
  private IReadOnlyList<int> _standaloneProgram = [];

  // Set by LoadImageFile, cleared the moment anything else (Assemble, or a fresh Start re-applying the
  // Assembly Code editor) overwrites what LoadImageFile put in the SRAM -- so RefreshMemoryView never
  // shows a linked program's own symbol names next to words that no longer belong to it.
  private IReadOnlyList<CvmImageSymbol> _loadedImageSymbols = [];

  // The full image LoadImageFile most recently loaded, kept around (not just its symbols) so a
  // subsequent Start / Reinstall re-applies THIS to the freshly booted chip instead of falling back to
  // AssemblyCodeText -- without this, a program loaded via the "Debug" button (a Program project's own
  // linked .gaimg) would sit correctly in the standalone SRAM for inspection right up until Start wiped
  // it out with whatever the Assembly Code editor happens to hold (the saved debugger scratch code, or
  // DefaultAssemblyCode), which defeats the entire point of Debug: the just-built C program would never
  // actually run on the connected hardware. Cleared by Assemble (the other, mutually exclusive way to
  // decide what Start should apply) so the two never fight over which one wins.
  private CvmImage? _loadedImage;

  // Source-form equivalent of CvmMemoryProtocol.TryBuildDebuggerTestProgram's own assembled words --
  // both are literally CvmDebuggerDefaultProgram.Source, so assembling this unedited reproduces
  // exactly the program Start already loads today (43 of the CVM's 73 opcodes, each with a
  // log-checkable expected value -- see CvmDebuggerDefaultProgram's own remarks for full coverage
  // details, the three deliberate exclusions (including 'adjust, which is excluded because it
  // actually corrupted a real run, not merely because it was unconfirmed), and the two exploratory
  // instructions), so clicking Assemble right after Start is a no-op on the simulated SRAM's
  // contents. Edit it and click Assemble to try a different program against the connected hardware
  // without a rebuild.
  private const string DefaultAssemblyCode = CvmDebuggerDefaultProgram.Source;

  private CvmDebugSession? _session;
  private CancellationTokenSource? _continueCts;
  private bool _isBusy;
  private string _statusText = "Not started. Click Start to compile, boot the mesh, and load the shared test program.";
  private string _installSummaryText = string.Empty;
  private string _logText = string.Empty;
  private string _memoryViewText = string.Empty;
  private string _memoryBaseText = "0:0000";
  private string _programCounterText = "-";
  private string _newBreakpointText = "0:0005";
  private string? _selectedBreakpoint;
  private string _assemblyCodeText = DefaultAssemblyCode;

  // The CVM Debugger's own named programs (Stefan's own request, 2026-09-20) -- see InitializePrograms's
  // own remarks for how this is (re)built from Ga144ChipConfiguration.DebuggerPrograms, and
  // Ga144DebuggerProgramConfiguration's own remarks for the "always >=1 entry, 'default' always first"
  // contract the underlying model guarantees. This is a working COPY (fresh Ga144DebuggerProgramConfiguration
  // instances, not the same object references _chip.DebuggerPrograms holds) so switching/editing/adding
  // here never touches the project's own persisted data until SaveProgram writes the whole list back --
  // same "nothing lands on disk before Save" contract the single-program editor always had.
  private Ga144DebuggerProgramConfiguration? _selectedProgram;
  private string _programNameText = string.Empty;

  // Debug stack-poison toggle -- see FillStacksWithDebugPoison's own remarks. Defaults to ON per
  // Stefan: he wants this visible by default and to opt OUT for the rare run where a clean (real
  // values only, no 0x1555x filler) stack matters instead.
  private bool _fillStacksWithDebugPoison = true;

  public CvmDebuggerViewModel(
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<F18MacroDefinition> userMacros,
      KrakenLiveController krakenController,
      Func<KrakenEndpointInfo?> resolveEndpoint,
      Action notifyProjectChanged,
      string projectDefaultNodeColor)
  {
    _chip = chip ?? throw new ArgumentNullException(nameof(chip));
    _romLibrary = romLibrary ?? throw new ArgumentNullException(nameof(romLibrary));
    _userMacros = userMacros ?? [];
    _krakenController = krakenController ?? throw new ArgumentNullException(nameof(krakenController));
    _resolveEndpoint = resolveEndpoint ?? throw new ArgumentNullException(nameof(resolveEndpoint));
    _notifyProjectChanged = notifyProjectChanged ?? throw new ArgumentNullException(nameof(notifyProjectChanged));
    _projectDefaultNodeColor = string.IsNullOrWhiteSpace(projectDefaultNodeColor) ? NodeColorPalette.DefaultColor : projectDefaultNodeColor;

    StartCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy);
    StepCommand = new AsyncRelayCommand(StepAsync, () => !IsBusy && IsSessionActive);
    ContinueCommand = new AsyncRelayCommand(ContinueAsync, () => !IsBusy && IsSessionActive);
    PauseCommand = new RelayCommand(Pause, () => IsContinuing);
    StopCommand = new RelayCommand(Stop, () => IsSessionActive);
    AddBreakpointCommand = new RelayCommand(AddBreakpoint, () => IsSessionActive);
    RemoveBreakpointCommand = new RelayCommand(RemoveSelectedBreakpoint, () => IsSessionActive && SelectedBreakpoint is not null);
    ClearBreakpointsCommand = new RelayCommand(ClearBreakpoints, () => IsSessionActive && Breakpoints.Count > 0);
    RefreshMemoryCommand = new RelayCommand(RefreshMemoryView);
    AssembleCommand = new RelayCommand(Assemble, () => !IsBusy);
    AddProgramCommand = new RelayCommand(AddProgram, () => !IsBusy);
    SaveCommand = new RelayCommand(SaveProgram);
    RestoreCommand = new RelayCommand(RestoreProgram);
    CoreDumpCommand = new AsyncRelayCommand(CoreDumpAsync, () => !IsBusy);

    // Reopen EXACTLY where Stefan left off: rebuilds Programs from this chip's own Saved
    // Ga144ChipConfiguration.DebuggerPrograms (migrating an older single-program save the first time,
    // and seeding a brand-new chip with one "default" program, if either applies -- see
    // Ga144ChipConfiguration.Normalize's own remarks), reselects whichever program
    // LastSelectedDebuggerProgramName names (falling back to the first entry -- "default" -- if that
    // program no longer exists or this chip's debugger has never been opened before), and loads its
    // text/name into the editor (LoadEditorFromProgram's own "default always loads the built-in
    // program" special case applies here too, same as any other selection).
    InitializePrograms();

    // Assemble immediately on open (the no-session half of Assemble() -- see its own remarks; needs
    // no connected chip) so whatever ends up in the editor (the selected program's saved text above)
    // is already assembled into the standalone simulated SRAM the moment the window appears, instead
    // of showing a program that LOOKS loaded in the editor but has not actually been assembled until
    // Stefan clicks Assemble by hand. This also calls RefreshMemoryView()/RefreshProgramCounter()
    // itself, so no separate RefreshMemoryView() call is needed here any more.
    Assemble();
  }

  public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) NotifyCommandStates(); } }

  public bool IsContinuing => _continueCts is not null;

  public bool IsSessionActive => _session is not null;

  /// <summary>
  /// When on (the default), Start fills BOTH the return and parameter stack of every non-root CVM
  /// mesh node with a recognizable 0x1555x sentinel pattern (position 0 = top, the highest position
  /// = bottom -- so a freshly booted, untouched stack reads 0x15550 on top down to 0x15558 at the
  /// bottom of the 9-word return stack, and down to 0x15559 at the bottom of the 10-word data
  /// stack -- the two are NOT the same depth, see Ga144CvmHardwareInstaller's own remarks) before
  /// that node's own real /rstack//stack (and IO/A/B) initialization is applied. The point is
  /// purely diagnostic: any of these sentinel values still showing up once the program is running
  /// immediately identifies a stack slot the program never actually touched, which is otherwise
  /// indistinguishable from a slot that happens to hold a real, coincidentally-similar value. Only
  /// affects what Start loads -- turning it off does not remove or alter a node's own real
  /// /stack//rstack directives, it just stops the extra poison push that precedes them.
  /// </summary>
  public bool FillStacksWithDebugPoison { get => _fillStacksWithDebugPoison; set => SetProperty(ref _fillStacksWithDebugPoison, value); }

  public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

  public string InstallSummaryText { get => _installSummaryText; private set => SetProperty(ref _installSummaryText, value); }

  // Read-only, selectable TextBox binding (not a ListBox/TextBlock) -- same reasoning as every other
  // transcript in this project: Stefan needs to be able to Ctrl+C a transaction line out of here.
  public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }

  public string MemoryViewText { get => _memoryViewText; private set => SetProperty(ref _memoryViewText, value); }

  public string MemoryBaseText { get => _memoryBaseText; set => SetProperty(ref _memoryBaseText, value ?? string.Empty); }

  public string ProgramCounterText { get => _programCounterText; private set => SetProperty(ref _programCounterText, value); }

  public string NewBreakpointText { get => _newBreakpointText; set => SetProperty(ref _newBreakpointText, value ?? string.Empty); }

  /// <summary>
  /// The CVM Debugger's own Assembly Code editor contents -- prefilled, on open or whenever
  /// <see cref="SelectedProgram"/> changes, from that program's own saved <see cref="Ga144DebuggerProgramConfiguration.Source"/>.
  /// Freely editable; <see cref="AssembleCommand"/> is what actually does anything with a subsequent
  /// edit, though the constructor already calls <see cref="Assemble"/> once up front so the window
  /// never opens with unassembled text sitting in the editor. Not written back into
  /// <see cref="SelectedProgram"/> on every keystroke -- only when it stops being the active program
  /// (switching <see cref="SelectedProgram"/>) or <see cref="SaveCommand"/> runs, same "nothing
  /// committed until you act on it" contract this editor always had.
  /// </summary>
  public string AssemblyCodeText { get => _assemblyCodeText; set => SetProperty(ref _assemblyCodeText, value ?? string.Empty); }

  /// <summary>
  /// This chip's own named CVM Debugger programs -- a working copy of
  /// <see cref="Ga144ChipConfiguration.DebuggerPrograms"/> (see <see cref="InitializePrograms"/>), so
  /// the program selector ComboBox has something to bind its <c>ItemsSource</c> to. Always has at
  /// least one entry (the "default" program, first) once the constructor has run.
  /// </summary>
  public ObservableCollection<Ga144DebuggerProgramConfiguration> Programs { get; } = [];

  /// <summary>
  /// The program selector's own <c>SelectedItem</c> -- there is deliberately no separate "Load" step
  /// any more (Stefan's own request): switching this IS loading. Switching first commits
  /// <see cref="AssemblyCodeText"/>'s CURRENT contents back into the program being switched AWAY from
  /// (so bouncing between programs before ever clicking Save does not silently discard edited source
  /// text -- only a not-yet-saved RENAME of the program name box is discarded that way, per
  /// <see cref="ProgramNameText"/>'s own remarks), then loads the newly selected program's own text via
  /// <see cref="LoadEditorFromProgram"/> (which special-cases "default" -- see its own remarks) and
  /// immediately re-assembles it (same reasoning as the constructor's own up-front
  /// <see cref="Assemble"/> call: the editor should never show text that looks loaded but isn't
  /// actually assembled yet). Also persists <see cref="Ga144ChipConfiguration.LastSelectedDebuggerProgramName"/>
  /// right away, not gated behind Save -- see that property's own remarks for why.
  /// </summary>
  public Ga144DebuggerProgramConfiguration? SelectedProgram
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
        Assemble();
      }

      _chip.LastSelectedDebuggerProgramName = value?.Name;
      _notifyProjectChanged();
    }
  }

  /// <summary>
  /// The program-name text box's own contents -- always shows <see cref="SelectedProgram"/>'s current
  /// name (reset to match it every time <see cref="SelectedProgram"/> changes), but a typed EDIT here
  /// only actually renames the program when <see cref="SaveCommand"/> runs (Stefan's own spec: "rename
  /// when using save") -- switching to a different program first, without saving, discards a
  /// not-yet-saved rename attempt, unlike an edited-but-unsaved <see cref="AssemblyCodeText"/> (which
  /// IS carried over across a switch -- see <see cref="SelectedProgram"/>'s own remarks). This
  /// asymmetry is deliberate: renaming is meant to visibly take effect (in the ComboBox list, and in
  /// what Save persists) only at the moment Save is clicked, never silently while just switching around.
  /// </summary>
  public string ProgramNameText { get => _programNameText; set => SetProperty(ref _programNameText, value ?? string.Empty); }

  // Bound to the breakpoint ListBox's SelectedItem so RemoveBreakpointCommand knows which entry to
  // remove -- kept as a plain RelayCommand (no parameterized command type exists elsewhere in this
  // codebase) rather than introducing a new generic command class for this one use.
  public string? SelectedBreakpoint
  {
    get => _selectedBreakpoint;
    set { if (SetProperty(ref _selectedBreakpoint, value)) RemoveBreakpointCommand.NotifyCanExecuteChanged(); }
  }

  public ObservableCollection<string> Breakpoints { get; } = [];

  public AsyncRelayCommand StartCommand { get; }
  public AsyncRelayCommand StepCommand { get; }
  public AsyncRelayCommand ContinueCommand { get; }
  public RelayCommand PauseCommand { get; }
  public RelayCommand StopCommand { get; }
  public RelayCommand AddBreakpointCommand { get; }
  public RelayCommand RemoveBreakpointCommand { get; }
  public RelayCommand ClearBreakpointsCommand { get; }
  public RelayCommand RefreshMemoryCommand { get; }
  public RelayCommand AssembleCommand { get; }
  public RelayCommand AddProgramCommand { get; }
  public RelayCommand SaveCommand { get; }
  public RelayCommand RestoreCommand { get; }
  public AsyncRelayCommand CoreDumpCommand { get; }

  /// <summary>Exposed only so Views.CvmDebuggerWindow can construct a PostMortemChipViewModel for a
  /// completed Core Dump -- this view model does no window creation of its own (same convention as
  /// every other file-picking/window-opening action in this IDE).</summary>
  public Ga144ChipConfiguration Chip => _chip;
  public Ga144RomLibrary RomLibrary => _romLibrary;
  public IReadOnlyList<F18MacroDefinition> UserMacros => _userMacros;

  /// <summary>Raised once a Core Dump finishes building its snapshot -- the window's code-behind
  /// opens a new post-mortem GA144 window from it.</summary>
  public event Action<PostMortemSnapshot>? CoreDumpCompleted;

  /// <summary>Tied to the debugger window's Closed event -- stops any in-flight Continue and releases the port.</summary>
  public void Cancel()
  {
    CloseSession();
  }

  /// <summary>Cancels any in-flight Continue and disposes the session's serial port, same as closing the window -- but the window (and this view model) stay alive, so callers that need the UI reset to "no session" afterward do that themselves.</summary>
  private void CloseSession()
  {
    _continueCts?.Cancel();
    _session?.Dispose();
    _session = null;
  }

  private async Task StartAsync()
  {
    if (_krakenController.HardwareErected)
    {
      StatusText = "CVM Debugger cannot start while a Kraken is erected on this chip. Remove the Kraken first -- starting resets the whole chip, and a resident Kraken must never be reset.";
      return;
    }

    KrakenEndpointInfo? endpoint = _resolveEndpoint();
    if (endpoint is null)
    {
      StatusText = "No serial endpoint is assigned to this chip. Assign a COM port before starting the debugger.";
      return;
    }

    IsBusy = true;
    StatusText = "Starting: compiling, resetting the chip, and booting the mesh…";
    try
    {
      // Starting again resets the whole chip -- release whatever session (and port) is currently
      // open first, same as re-running "Install & run CVM test" would.
      _session?.Dispose();
      _session = null;
      Breakpoints.Clear();

      var compileService = new F18NodeCompilationService(_chip, _romLibrary, _userMacros);
      var installer = new Ga144CvmHardwareInstaller();
      _session = await installer.StartDebugSessionAsync(endpoint.PortName, _chip, compileService, fillStacksWithDebugPoison: FillStacksWithDebugPoison);

      InstallSummaryText = $"Install: {_session.Install.Steps.Count} boot frame(s) sent, fire-and-forget. Loaded a {_session.Program.Count}-word test program " +
          "(43 of the CVM's 73 opcodes, each with a log-checkable expected value -- see CvmDebuggerDefaultProgram's own remarks) into the simulated SRAM and woke node 708's 'start.";

      // What should actually run on the freshly booted chip: whichever of the two mutually-exclusive
      // "what to load" mechanisms was used most recently. A loaded linked image (_loadedImage, from
      // LoadImageFile -- e.g. a Program project's own "Debug" button) takes priority when present,
      // since it is already fully resolved and needs no re-assembly; otherwise the Assembly Code
      // editor is the single source of truth, whether it was edited before or after Start, so
      // re-applying it here needs no second, manual click of Assemble. Assembling an untouched
      // DefaultAssemblyCode reproduces the exact same words the install line above just described, so
      // that fallback is always safe and a no-op when nothing was edited.
      if (_loadedImage is { } loadedImage)
      {
        _session.LoadImage(loadedImage);
        InstallSummaryText += $" Reapplied the last loaded linked image ({loadedImage.Words.Count} word(s), entry " +
            $"{DescribeFlatAddress(loadedImage.EntryAddress)}) instead of the install's own default program -- " +
            "exactly what \"Load linked image (.gaimg)...\" (or a Program project's own \"Debug\" button) most recently loaded.";
        // _loadedImageSymbols already describes this exact image -- leave it alone.
      }
      else
      {
        _loadedImageSymbols = [];
        (bool assembleSuccess, string? assembleError) = _session.AssembleAndLoadProgram(AssemblyCodeText);
        if (!assembleSuccess)
        {
          InstallSummaryText += $" Could not apply the Assembly Code editor's current contents ({assembleError}) -- the install's own default program above is still what's loaded.";
        }
      }

      MirrorSessionProgramIntoStandaloneSram(_session.Program);
      StatusText = "Started -- paused before the first transaction. Step or Continue to begin servicing the wire.";
      MemoryBaseText = "0:0000";
      RefreshLog();
      RefreshMemoryView();
      RefreshProgramCounter();
    }
    catch (Exception exception)
    {
      _session?.Dispose();
      _session = null;
      StatusText = "Start failed: " + exception.Message;
      InstallSummaryText = string.Empty;
    }
    finally
    {
      IsBusy = false;
      OnPropertyChanged(nameof(IsSessionActive));
      NotifyCommandStates();
    }
  }

  private async Task StepAsync()
  {
    if (_session is null)
    {
      return;
    }

    IsBusy = true;
    StatusText = "Stepping…";
    try
    {
      // Step always ignores breakpoints (CvmDebugSession.Step's own contract), so it either
      // completes a fresh transaction outright or -- if a Continue() had earlier left a read's
      // reply withheld at a breakpoint -- finishes sending that one reply now. Either way exactly
      // one line lands in the log; the memory view's own [BP] marker still shows which addresses
      // are armed.
      CvmDebugTransaction transaction = await Task.Run(() => _session.Step());
      StatusText = $"Stepped one transaction ({CvmMemoryProtocol.FormatPageAddress(transaction.Page, transaction.AddressInPage)}).";
    }
    catch (Exception exception)
    {
      StatusText = "Step failed: " + exception.Message;
    }
    finally
    {
      RefreshLog();
      RefreshMemoryView();
      RefreshProgramCounter();
      IsBusy = false;
    }
  }

  private async Task ContinueAsync()
  {
    if (_session is null)
    {
      return;
    }

    IsBusy = true;
    StatusText = "Running…";
    _continueCts = new CancellationTokenSource();
    NotifyCommandStates();
    try
    {
      CancellationToken token = _continueCts.Token;
      await Task.Run(() => _session.Continue(ContinueTransactionCap, token));
      StatusText = _session.PauseReason switch
      {
        CvmDebugPauseReason.Breakpoint => "Paused at a breakpoint.",
        CvmDebugPauseReason.TransactionCapReached => $"Stopped after {ContinueTransactionCap} transactions with no breakpoint hit -- raise the cap or check the breakpoint address if this is unexpected.",
        CvmDebugPauseReason.UserPaused => "Paused by request.",
        CvmDebugPauseReason.Faulted => "Run stopped: " + (_session.FaultMessage ?? "unknown error."),
        _ => "Run stopped."
      };
    }
    finally
    {
      _continueCts?.Dispose();
      _continueCts = null;
      RefreshLog();
      RefreshMemoryView();
      RefreshProgramCounter();
      IsBusy = false;
      NotifyCommandStates();
    }
  }

  private void Pause() => _continueCts?.Cancel();

  /// <summary>
  /// Ends the debugging session outright: cancels any in-flight Continue and closes the serial port
  /// (<see cref="CloseSession"/> -- the same cleanup the window's own Closed handler runs via
  /// <see cref="Cancel"/>), then resets every session-derived display back to its "nothing started"
  /// state, same as before the very first Start -- EXCEPT the transaction log, which is deliberately
  /// left exactly as it was: Stop is for pausing/disconnecting, not for discarding a record of what
  /// the wire just did, so <see cref="RefreshLog"/> is never called here (it would otherwise blank
  /// <see cref="LogText"/> the moment <see cref="_session"/> goes null -- see its own remarks).
  /// <see cref="StartAsync"/> is what clears the log, by starting an entirely new session whose own
  /// transaction log begins empty. Unlike closing the window, the CVM Debugger stays open afterward --
  /// Start reconnects and reinstalls from scratch, same as it would after a fresh launch.
  /// </summary>
  private void Stop()
  {
    if (_session is null)
    {
      return;
    }

    CloseSession();
    Breakpoints.Clear();
    StatusText = "Stopped -- communication with the chip closed. Click Start / Reinstall to reconnect.";
    InstallSummaryText = string.Empty;
    RefreshMemoryView();
    RefreshProgramCounter();
    OnPropertyChanged(nameof(IsSessionActive));
    NotifyCommandStates();
  }

  /// <summary>
  /// "Core Dump...": captures the ENTIRE chip's live state through Kraken for the post-mortem
  /// window. Sequence, per Stefan's own spec and follow-up answers: (1) an active CVM Debug session
  /// is stopped first (it owns the same COM port directly, bypassing Kraken entirely -- the two
  /// cannot run at once, and "the debug session is done, Kraken is only needed to read out the
  /// current chip state" is Stefan's own framing for why stopping it here, rather than refusing, is
  /// the right call); (2) the chip is reset (<see cref="KrakenLiveController.ResetChipAsync"/> -- the
  /// standalone reset primitive, not a fused reset+erect); (3) node 708's head program is brought up
  /// AND Tentacle 1's own boot-frame prefix is wired in the same step
  /// (<see cref="KrakenLiveController.EnsureHeadWithTentacle1PrefixOnlineAsync"/>, see below) -- NOT
  /// the ordinary bulk "Install Kraken" erection (<see cref="KrakenLiveController.EnsureOnlineAsync"/>,
  /// <see cref="ViewModels.ChipViewModel.ToggleKrakenAsync"/>'s own call); (4) every tentacle node is
  /// then read, in tentacle/position order (708 itself has no live-state read path of its own and is
  /// skipped) -- one node's transport failure is recorded and skipped, not fatal to the rest of the
  /// dump, though for a LIVE (non-boot-framed) node it does mean every LATER node on the SAME tentacle
  /// becomes unreachable too, since the physical relay chain breaks at that point, and each of them
  /// will fail its own Focus call in turn and be recorded the same way.
  ///
  /// Node 300 (Tentacle 1, position 31) is a known problem for the live method below: its live focus
  /// call has been observed timing out on real hardware, the same failure the project's own
  /// node-300-erection-investigation found for BULK erection via this mechanism. Per Stefan's own
  /// compromise, Tentacle 1's prefix -- its first <see cref="KrakenTopology.PostMortemTentacle1BootFramePrefixNodeCount"/>
  /// nodes (707 through, and including, node 300) -- is instead wired up front via the RELIABLE
  /// fire-and-forget boot-frame mechanism <see cref="KrakenSession.ErectOnto"/> already uses for
  /// "Install Kraken" (confirmed by the hardware investigation to reliably reach node 300). Every one
  /// of these nodes is then simply READ (no live Focus/WriteB -- the boot frame already wired every
  /// B register in that prefix in one shot), at the cost of the same 1-word top-of-stack disturbance
  /// any Kraken erection always pays (an accepted, KNOWN loss, unlike an unpredictable live timeout).
  /// Every node beyond the prefix -- the rest of Tentacle 1 (200 series onward), plus all of Tentacles
  /// 2 and 3, neither of which has shown this problem -- keeps the fully lossless live method below.
  /// This is scoped to Core Dump alone; routes still come from the chip's own, ordinary
  /// <see cref="Ga144ChipConfiguration.Kraken"/> (the normal Tentacle 1 node list, node 300 included,
  /// untouched) -- only HOW the prefix gets wired differs from a live Core Dump node.
  ///
  /// Per-node sequence for every node BEYOND the boot-framed prefix, Stefan's own explicit correction
  /// (his exact numbering):
  ///   1) send a focusing call (<see cref="KrakenLiveController.FocusAsync"/>) to this node, reached
  ///      through however many hops are ALREADY wired from earlier iterations (boot-framed or live).
  ///      Its one mandatory reply word IS T (this node's own parameter-stack top), popped as part of
  ///      the acknowledgment (a plain, unavoidable pop of whatever is genuinely on top -- see
  ///      <see cref="Services.KrakenProtocol.BuildFocus"/>) -- Stefan's own point: this is T "for
  ///      free," so it is captured from focus's own return value rather than read a second time.
  ///   2) read the REMAINING 9 parameter-stack words (<see cref="KrakenLiveController.ReadParameterStackTailAsync"/>,
  ///      NOT the full <see cref="KrakenLiveController.ReadParameterStackAsync"/>, which would pop an
  ///      11th, misaligned word off the same 10-word ring since step 1 already popped the first one)
  ///      -- MUST happen immediately after focusing and before anything else touches this node, to
  ///      recover the rest of its true, as-found stack before anything else disturbs it.
  ///      Focus no longer touches the return stack at all (it did, silently, via a push+pop the old
  ///      "jump port" implementation needed and this project's own docs did not account for -- very
  ///      likely the actual cause of the return-stack corruption Stefan found in earlier Core Dumps),
  ///      so no equivalent "read immediately" requirement exists for <see cref="KrakenLiveController.ReadReturnStackAsync"/>.
  ///   3) read the rest of this node's state (A, IO, Carry, RAM, ROM, return stack) -- order among
  ///      these no longer matters, since each is independently expected to round-trip through T. Carry
  ///      (<see cref="KrakenLiveController.ReadCarryAsync"/>) is this node's own real F18A hardware
  ///      carry flag, per Stefan's own recipe: a temporary re-focus with address bit 9 set (Extended
  ///      Arithmetic Mode) to read it via '@p dup . +', then a third focus back to plain -- unrelated
  ///      to the CVM's own node-405 software carry emulation, a separate, higher-level concept.
  ///   4) only THEN write B (<see cref="KrakenLiveController.WriteBAsync"/>) to extend the tentacle one
  ///      hop further, making the NEXT node reachable for the next iteration. Doing this any earlier
  ///      would let writeB's own reply mechanism disturb this node's stack before step 2 had read it.
  /// A boot-framed prefix node skips straight to step 3 (no focus, no writeB -- both already done by
  /// the boot frame). Reading the stacks is destructive (confirmed acceptable: a Core Dump is taken
  /// right after a reset, before anything is running); nothing here writes them back.
  ///
  /// Unlike every other Kraken operation in this app, this erection is torn down again as soon as the
  /// dump is over (success, partial failure, or exception alike): Core Dump's head-only/per-node
  /// erection is a diagnostic scan, not an install, so it must never linger the way "Install Kraken"
  /// deliberately does. The teardown is <see cref="KrakenLiveController.ResetTransientErectionAsync"/>
  /// -- exactly what the GA144 window's own Kraken button calls to "remove" a Kraken (drops IDE
  /// erection state and parks the COM handle, no chip reset) -- so once Core Dump finishes, the GA144
  /// window again shows no Kraken erected and <see cref="StartAsync"/>'s resident-Kraken guard no
  /// longer blocks a fresh debug session.
  ///
  /// <b>REWORKED 2026-09-22, per Stefan directly: "post-mortem is too slow now. i am not interested in
  /// all nodes. in the node window make a checkbox that decides if the node should be read in
  /// post-mortem analysis. ... when reading post-mortem data, read only the selected nodes and grey out
  /// all other nodes. the tentacle mechanism for reading the nodes do not change."</b> Per node, this
  /// method now checks <see cref="Models.Ga144NodeConfiguration.PostMortemEnabled"/> (the new Node
  /// Editor checkbox) BEFORE doing any of steps 2/3 above. When it is false: a boot-framed prefix node
  /// needs nothing at all (its B was already set, once, for the whole prefix); a live node still gets
  /// step 1 (focus) and step 4 (writeB) -- it MUST, since those are what make the next node on the same
  /// tentacle reachable at all, and Stefan's own instruction was explicit that the wire walk itself does
  /// not change -- but skips steps 2 and 3 entirely: no stack tail, no A/IO/Carry, and critically no
  /// 64-word RAM or 64-word ROM read, by far the most expensive part of reading any one node. The
  /// resulting <see cref="Models.PostMortemNodeSnapshot"/> for a skipped node carries only
  /// <see cref="Models.PostMortemNodeSnapshot.Coordinate"/>/<see cref="Models.PostMortemNodeSnapshot.Color"/>
  /// and <see cref="Models.PostMortemNodeSnapshot.Included"/> set to false; the post-mortem window greys
  /// it out on that recorded flag (see <see cref="ViewModels.PostMortemNodeViewModel"/>'s own remarks)
  /// rather than re-querying the live project, so a snapshot keeps showing exactly what it actually
  /// captured even after the project's own checkboxes are later changed.
  /// </summary>
  private async Task CoreDumpAsync()
  {
    if (_krakenController.HardwareErected)
    {
      StatusText = "Core Dump cannot run while a Kraken is already erected on this chip -- remove it first (the GA144 window's Kraken button), then try again. Core Dump always starts from a fresh reset.";
      return;
    }

    KrakenEndpointInfo? endpoint = _resolveEndpoint();
    if (endpoint is null)
    {
      StatusText = "No serial endpoint is assigned to this chip. Assign a COM port before running Core Dump.";
      return;
    }

    if (IsSessionActive)
    {
      Stop();
    }

    IsBusy = true;
    StatusText = "Core Dump: resetting the chip…";
    var nodeSnapshots = new Dictionary<int, PostMortemNodeSnapshot>();
    var failedNodes = new List<string>();
    try
    {
      await _krakenController.ResetChipAsync();

      // The chip's own, ordinary Kraken structure -- node 300 included, in its normal place. See
      // this method's own remarks above: only HOW the Tentacle-1 prefix gets wired differs below.
      IReadOnlyDictionary<int, KrakenNodeRoute> routes = KrakenTopology.BuildRouteMap(_chip.Kraken);
      List<KrakenNodeRoute> orderedRoutes = routes.Values
          .Where(route => !route.IsHead)
          .OrderBy(route => route.TentacleNumber)
          .ThenBy(route => route.Position)
          .ToList();

      if (orderedRoutes.Count == 0)
      {
        StatusText = "Core Dump failed: no Kraken routes are defined for this chip.";
        return;
      }

      const int tentacle1BootFramePrefixNodeCount = KrakenTopology.PostMortemTentacle1BootFramePrefixNodeCount;

      StatusText = "Core Dump: bringing up the Kraken head (node 708) and node 300's boot-frame prefix…";
      await _krakenController.EnsureHeadWithTentacle1PrefixOnlineAsync(orderedRoutes[0], tentacle1BootFramePrefixNodeCount);

      try
      {
        await _krakenController.BeginKeepOpenAsync();
        try
        {
          for (int index = 0; index < orderedRoutes.Count; index++)
          {
            KrakenNodeRoute route = orderedRoutes[index];
            bool isBootFramePrefixNode = route.TentacleNumber == 1 && route.Position < tentacle1BootFramePrefixNodeCount;
            Ga144NodeConfiguration nodeConfig = _chip.GetNode(route.Coordinate);
            string? nodeColor = nodeConfig.Color;
            // REWORKED 2026-09-22 -- see this method's own remarks: "not selected" skips this node's
            // own data read entirely, but never the wire walk itself.
            bool includeInDump = nodeConfig.PostMortemEnabled;
            StatusText = !includeInDump
                ? $"Core Dump: passing through node {route.Coordinate:000} (not selected for post-mortem, skipping its read) ({index + 1} of {orderedRoutes.Count})…"
                : isBootFramePrefixNode
                    ? $"Core Dump: reading boot-frame-wired node {route.Coordinate:000} ({index + 1} of {orderedRoutes.Count})…"
                    : $"Core Dump: wiring and reading node {route.Coordinate:000} ({index + 1} of {orderedRoutes.Count})…";
            try
            {
              // Same physical port either way -- which compass port this node's boot-frame prefix
              // wiring (or a live Focus) already has it relaying through. Needed below for the carry
              // read regardless of which branch wired it, since re-focusing with/without address bit
              // 9 only ever re-targets THIS SAME port -- see KrakenSession.ReadCarryAsync's remarks.
              int incomingPort = KrakenTopology.PortAddress(route.Coordinate, route.PreviousCoordinate ?? KrakenTopology.HeadCoordinate);

              if (!includeInDump)
              {
                // Not ticked in the Node Editor's "Include this node in post-mortem analysis" checkbox.
                // A boot-framed prefix node needs nothing further at all -- its B was already set, once,
                // for the whole prefix, by the boot frame itself, independent of whether it is ever
                // read. A live node still MUST be focused and have its B written, exactly as it would if
                // selected: those two steps are what make the NEXT node on this same tentacle reachable
                // at all, and Stefan's own instruction was explicit that the wire walk does not change --
                // only steps 2 and 3 (stack tail, A/IO/Carry, and the 64+64-word RAM/ROM reads that
                // dominate the cost of reading any one node) are skipped.
                if (!isBootFramePrefixNode)
                {
                  await _krakenController.FocusAsync(route, incomingPort);
                  int skippedNextPort = route.OutgoingBAddress ?? KrakenSession.IoAddress;
                  await _krakenController.WriteBAsync(route, skippedNextPort);
                }

                nodeSnapshots[route.Coordinate] = new PostMortemNodeSnapshot
                {
                  Coordinate = route.Coordinate,
                  Color = nodeColor,
                  Included = false
                };
                continue;
              }

              IReadOnlyList<int> parameterStack;
              if (isBootFramePrefixNode)
              {
                // Already focused AND wired by the boot-frame prefix erection above
                // (EnsureHeadWithTentacle1PrefixOnlineAsync/KrakenSession.ErectHeadWithTentacle1Prefix)
                // -- no live focus/writeB needed for this node, just read. This node pays the same
                // 1-word top-of-stack disturbance every Kraken erection always costs, in exchange for
                // reliably including it -- see this method's own remarks on Stefan's compromise.
                parameterStack = await _krakenController.ReadParameterStackAsync(route);
              }
              else
              {
                // 1) Focus this node -- reached through whatever hops earlier iterations already
                // wired (boot-framed, for the first live node right after the prefix, or live).
                // Focus's own mandatory reply word IS T (the parameter stack's own top), popped as
                // part of its acknowledgment -- Stefan's own point: this comes for free, with no
                // extra effort spent retrieving it, so it is captured here rather than discarded.
                int topOfStack = await _krakenController.FocusAsync(route, incomingPort);

                // 2) The REMAINING 9 parameter-stack words, immediately -- see this method's own
                // remarks on why this must come right after focus and before anything else.
                // Destructive (Stefan confirmed acceptable): 'readPStack' pops the stack off the live
                // node as part of reading it. Reading all 10 again here (instead of just these 9)
                // would pop an 11th, misaligned word off the same 10-word ring, since focus's own
                // reply already popped the first one -- see KrakenSession.ReadParameterStackTailAsync.
                IReadOnlyList<int> parameterStackTail = await _krakenController.ReadParameterStackTailAsync(route);
                parameterStack = [.. parameterStackTail, topOfStack];
              }

              // 3) Everything else -- order no longer matters among these. Carry is read here too
              // (real per-node F18A hardware state, via a temporary re-focus with address bit 9 set --
              // see KrakenSession.ReadCarryAsync's remarks); the parameter stack is already fully
              // recovered by this point, so its own extra destructive pops cost nothing new.
              int a = await _krakenController.ReadAAsync(route);
              int io = await _krakenController.ReadIoAsync(route);
              int carry = await _krakenController.ReadCarryAsync(route, incomingPort);
              IReadOnlyList<int> ram = await _krakenController.ReadRamAsync(route);
              IReadOnlyList<int> rom = await _krakenController.ReadRomAsync(route);
              IReadOnlyList<int> returnStack = await _krakenController.ReadReturnStackAsync(route);

              if (!isBootFramePrefixNode)
              {
                // 4) Only now extend the tentacle one hop further, so the NEXT node becomes
                // reachable. A boot-framed prefix node's B is already set by the boot frame itself.
                int nextPort = route.OutgoingBAddress ?? KrakenSession.IoAddress;
                await _krakenController.WriteBAsync(route, nextPort);
              }

              nodeSnapshots[route.Coordinate] = new PostMortemNodeSnapshot
              {
                Coordinate = route.Coordinate,
                A = a,
                Io = io,
                Carry = carry,
                Ram = [.. ram],
                Rom = [.. rom],
                ParameterStack = [.. parameterStack],
                ReturnStack = [.. returnStack],
                Color = nodeColor,
                Included = true
              };
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or InvalidOperationException)
            {
              failedNodes.Add($"{route.Coordinate:000} ({exception.Message})");
              nodeSnapshots[route.Coordinate] = new PostMortemNodeSnapshot
              {
                Coordinate = route.Coordinate,
                Error = exception.Message,
                Color = nodeColor,
                Included = includeInDump
              };
              // One node's transport failure does not abort the rest of the dump -- same policy as
              // ChipViewModel.VerifyAllRomsAsync's own per-node scan. NOTE: for a LIVE node (beyond
              // the boot-framed prefix), this also means every LATER node on the SAME tentacle is now
              // unreachable too (the physical relay chain breaks here) -- each will fail its own
              // Focus call in turn and be recorded the same way. A boot-framed prefix node's own read
              // failure does NOT break that chain (every prefix node's B was already set independently
              // by the boot frame), so later prefix nodes, and the live nodes beyond them, are
              // unaffected by one prefix node's read failing.
            }
          }
        }
        finally
        {
          await _krakenController.EndKeepOpenAsync();
        }

        var snapshot = new PostMortemSnapshot
        {
          CapturedAtUtc = DateTime.UtcNow,
          ChipRole = _chip.Role,
          ChipName = _chip.Name,
          ProjectDefaultNodeColor = _projectDefaultNodeColor,
          Nodes = nodeSnapshots
        };

        // REWORKED 2026-09-22: reports the read/skipped split explicitly now that a node can be
        // deliberately excluded (Included = false) rather than only ever failed or successfully read.
        int readCount = nodeSnapshots.Values.Count(node => node.Error is null && node.Included);
        int skippedCount = nodeSnapshots.Values.Count(node => node.Error is null && !node.Included);
        string skippedSuffix = skippedCount > 0 ? $", skipped {skippedCount} not selected for post-mortem" : string.Empty;
        StatusText = failedNodes.Count == 0
            ? $"Core Dump complete: read {readCount} node(s){skippedSuffix}. The Kraken used for the dump has been released."
            : $"Core Dump complete with {failedNodes.Count} failed node(s): {string.Join(", ", failedNodes)}{skippedSuffix}. The Kraken used for the dump has been released.";

        CoreDumpCompleted?.Invoke(snapshot);
      }
      finally
      {
        // Core Dump's erection is diagnostic-only and must never linger: release it exactly like the
        // GA144 window's own "remove Kraken" button would (drops IDE erection state and parks the COM
        // handle, no chip reset), regardless of whether the dump above succeeded, partially failed, or
        // threw. Without this the GA144 window would go on showing this chip's Kraken as erected, and
        // StartAsync's own resident-Kraken guard would block every subsequent debug session until
        // Stefan noticed and removed it by hand.
        await _krakenController.ResetTransientErectionAsync();
      }
    }
    catch (Exception exception)
    {
      StatusText = "Core Dump failed: " + exception.Message;
    }
    finally
    {
      IsBusy = false;
      NotifyCommandStates();
    }
  }

  private void AddBreakpoint()
  {
    if (_session is null)
    {
      return;
    }

    if (!TryParseAddress(NewBreakpointText, out int flatAddress))
    {
      StatusText = $"'{NewBreakpointText}' is not a valid breakpoint address. Use \"p:aaaa\" (page hex digit, colon, 4-digit address-in-page) or a flat hex address.";
      return;
    }

    _session.AddBreakpoint(flatAddress);
    RefreshBreakpointList();
    RefreshMemoryView();
    StatusText = $"Breakpoint set at {DescribeFlatAddress(flatAddress)}.";
  }

  private void RemoveSelectedBreakpoint()
  {
    if (_session is null || SelectedBreakpoint is null || !TryParseAddress(SelectedBreakpoint, out int flatAddress))
    {
      return;
    }

    _session.RemoveBreakpoint(flatAddress);
    SelectedBreakpoint = null;
    RefreshBreakpointList();
    RefreshMemoryView();
  }

  private void ClearBreakpoints()
  {
    _session?.ClearBreakpoints();
    RefreshBreakpointList();
    RefreshMemoryView();
  }

  /// <summary>
  /// Assembles <see cref="AssemblyCodeText"/> against node 607's current source and loads the result
  /// into a simulated SRAM -- WHICH one depends only on whether a chip happens to be connected right
  /// now, never on whether Assemble is allowed to run at all (<see cref="AssembleCommand"/> only
  /// checks <see cref="IsBusy"/>). With an active session this is
  /// <see cref="CvmDebugSession.AssembleAndLoadProgram"/>: a live reprogram of whatever the connected
  /// chip is actually reading from, not a reset, so breakpoints, the transaction log, and the chip's
  /// own run state are all left alone. With no session, this compiles node 607's source locally
  /// (<see cref="AssembleStandalone"/> -- no hardware needed at all) into <see cref="_standaloneSram"/>
  /// instead, so a program can be written and checked before ever connecting; <see cref="StartAsync"/>
  /// then re-applies this exact text to the chip the moment it does connect, so nothing done here is
  /// lost just because Assemble was clicked before Start.
  /// </summary>
  private void Assemble()
  {
    // CVM2 (2026-09-01): wrapped in try/catch, which this method never had before -- Assemble() used
    // to only ever fail gracefully (CvmAssemblyLanguage/CompileStandaloneCvmNodes both return a
    // (false, error) pair, never throw, for every failure this method's own authors anticipated), so
    // there was never a case an exception could reach here. That stopped being true the moment a
    // mnemonic's resolution could depend on a node (F18NodeCompilationService.CompileNode reading THIS
    // chip's own live Node Editor source, not a reference file) that might not exist yet, or whose
    // compile might throw instead of failing gracefully for a reason this file's own authors didn't
    // anticipate -- silently doing nothing on an unhandled exception (which is what an uncaught
    // exception inside an ICommand.Execute tends to look like from the UI) is worse than a wrong-
    // looking error message, so this now always leaves SOME visible trace in StatusText.
    try
    {
      if (_session is not null)
      {
        (bool success, string? error) = _session.AssembleAndLoadProgram(AssemblyCodeText);
        StatusText = success
            ? $"Assembled {_session.Program.Count} word(s) and loaded them into the simulated SRAM starting at address 0."
            : $"Assemble failed: {error}";

        if (success)
        {
          // this program just replaced whatever LoadImageFile last loaded, if anything -- Assemble
          // and LoadImageFile are mutually exclusive, so both its symbols and the image itself go.
          _loadedImageSymbols = [];
          _loadedImage = null;
          MirrorSessionProgramIntoStandaloneSram(_session.Program);
        }

        RefreshMemoryView();
        RefreshProgramCounter();
        return;
      }

      (bool standaloneSuccess, string? standaloneError, int wordCount) = AssembleStandalone();
      StatusText = standaloneSuccess
          ? $"Assembled {wordCount} word(s) into a standalone simulated SRAM -- no chip connected yet. Click Start; this program loads automatically."
          : $"Assemble failed: {standaloneError}";

      if (standaloneSuccess)
      {
        _loadedImageSymbols = [];
        _loadedImage = null;
      }

      RefreshMemoryView();
      RefreshProgramCounter();
    }
    catch (Exception exception)
    {
      StatusText = "Assemble failed (unexpected error): " + exception.Message;
    }
  }

  /// <summary>
  /// The no-session half of <see cref="Assemble"/>: compiles every standalone-relevant node's CURRENT
  /// source locally (<see cref="CompileStandaloneCvmNodes"/> -- a pure software compile, no port or
  /// connected chip needed) and, on success, assembles <see cref="AssemblyCodeText"/> against it
  /// straight into <see cref="_standaloneSram"/>/<see cref="_standaloneProgram"/> via the same
  /// <see cref="CvmAssemblyLanguage.AssembleAndLoadProgram"/> a live session uses.
  /// </summary>
  private (bool Success, string? Error, int WordCount) AssembleStandalone()
  {
    (bool compileSuccess, IReadOnlyDictionary<int, F18CompileResult> compiledRam, string? compileError) = CompileStandaloneCvmNodes();
    if (!compileSuccess)
    {
      return (false, compileError, 0);
    }

    (List<int>? words, string? error) = CvmAssemblyLanguage.AssembleAndLoadProgram(AssemblyCodeText, _standaloneSram, _standaloneProgram, compiledRam);
    if (words is null)
    {
      return (false, error, 0);
    }

    _standaloneProgram = words;
    return (true, null, words.Count);
  }

  // Moved 2026-09-08 to Ga144.Evb.Ide.Cvm.CvmNodeMesh.StandaloneCoordinates -- now the one shared list
  // behind both this standalone compile path and the newer build-time CvmPrimitiveTableExporter, so a
  // node addition/repointing (an ongoing, expected thing -- "the exact instruction set is still a
  // moving target") only ever needs updating in one place. See that class's own remarks for the full
  // per-node history this comment used to carry.

  /// <summary>
  /// Compiles every node in <see cref="CvmNodeMesh.StandaloneCoordinates"/> (<see cref="F18NodeCompilationService"/>)
  /// purely in software -- no serial port, no connected chip -- for <see cref="AssembleStandalone"/>
  /// and the memory inspector's no-session disassembly to resolve tagged mnemonics against. IMPORTANT:
  /// this compiles from THIS CHIP'S OWN LIVE PROJECT DATA for each node coordinate (whatever is
  /// currently saved in the Node Editor), never from this repo's <c>NodeXxxProgram.cs</c> reference/
  /// documentation copies -- so a reference file being updated (e.g. for a new CVM revision) has NO
  /// effect here until the same source is also copied into the live project via the Node Editor.
  ///
  /// CVM2 (2026-09-01): no longer aborts entirely the moment ONE node fails to compile -- skips that
  /// node (so it is simply absent from <c>compiledRam</c>, the same graceful-omission
  /// <see cref="CvmAssemblyLanguage"/> already applies to a node missing from <c>compiledRam</c> for
  /// any other reason) and keeps going, collecting every node that DOES compile. Only returns failure
  /// if the list ends up completely empty. This means a mnemonic whose own node compiles fine still
  /// resolves even if some OTHER node in the list is currently broken -- the previous all-or-nothing
  /// behavior is exactly what could make Assemble appear to "do nothing" (a confusing error naming an
  /// unrelated node) for a program that never touched the broken one at all.
  /// </summary>
  private (bool Success, IReadOnlyDictionary<int, F18CompileResult> CompiledRam, string? Error) CompileStandaloneCvmNodes()
  {
    var compilationService = new F18NodeCompilationService(_chip, _romLibrary, _userMacros);
    var compiledRam = new Dictionary<int, F18CompileResult>();
    var failures = new List<string>();
    foreach (int coordinate in CvmNodeMesh.StandaloneCoordinates)
    {
      F18NodeCompilationResult compiled = compilationService.CompileNode(coordinate);
      if (!compiled.Ram.Success)
      {
        failures.Add($"node {coordinate:000}'s RAM source does not currently compile -- fix it in the Node Editor first.");
        continue;
      }

      compiledRam[coordinate] = compiled.Ram;
    }

    if (compiledRam.Count == 0)
    {
      return (false, new Dictionary<int, F18CompileResult>(), string.Join(" ", failures));
    }

    return (true, compiledRam, null);
  }

  /// <summary>
  /// Keeps <see cref="_standaloneSram"/>/<see cref="_standaloneProgram"/> in sync with whatever a
  /// live session actually has loaded, called after every successful Start/Assemble against a real
  /// session -- so if Stop is clicked afterward, the memory inspector's no-session fallback shows the
  /// chip's last real state instead of stale content from before Start.
  /// </summary>
  private void MirrorSessionProgramIntoStandaloneSram(IReadOnlyList<int> words)
  {
    int previousLength = _standaloneProgram.Count;
    _standaloneSram.LoadProgram(words);
    if (words.Count < previousLength)
    {
      _standaloneSram.LoadProgram(new int[previousLength - words.Count], words.Count);
    }

    _standaloneProgram = words;
  }

  /// <summary>
  /// (Re)builds <see cref="Programs"/>/<see cref="SelectedProgram"/>/<see cref="AssemblyCodeText"/>/
  /// <see cref="ProgramNameText"/> from this chip's own <see cref="Ga144ChipConfiguration.DebuggerPrograms"/>
  /// -- called once, by the constructor. <see cref="Ga144ChipConfiguration.Normalize"/> is what
  /// actually guarantees at least one ("default") entry exists (including migrating an older single-
  /// program save the first time) -- this method just mirrors whatever it produces into fresh, working
  /// copies (never the SAME <see cref="Ga144DebuggerProgramConfiguration"/> instances _chip holds) so
  /// nothing typed here reaches the project's own data ahead of <see cref="SaveProgram"/>. Reselects
  /// whichever program <see cref="Ga144ChipConfiguration.LastSelectedDebuggerProgramName"/> names
  /// (falling back to the first entry -- always "default" for a chip that has never renamed it away --
  /// if that name is null or no longer matches any program), so the window reopens on exactly the
  /// program Stefan had selected last time, not always "default".
  /// </summary>
  private void InitializePrograms()
  {
    _chip.Normalize();
    Programs.Clear();
    foreach (Ga144DebuggerProgramConfiguration saved in _chip.DebuggerPrograms)
    {
      Programs.Add(new Ga144DebuggerProgramConfiguration { Name = saved.Name, Source = saved.Source });
    }

    Ga144DebuggerProgramConfiguration selected = Programs.FirstOrDefault(
        program => string.Equals(program.Name, _chip.LastSelectedDebuggerProgramName, StringComparison.OrdinalIgnoreCase))
        ?? Programs[0];

    _selectedProgram = selected;
    OnPropertyChanged(nameof(SelectedProgram));
    LoadEditorFromProgram(selected);
  }

  /// <summary>
  /// Loads one program's text/name into <see cref="AssemblyCodeText"/>/<see cref="ProgramNameText"/> --
  /// shared by <see cref="InitializePrograms"/> (opening the window) and <see cref="SelectedProgram"/>'s
  /// own setter (switching programs), so both go through the exact same rule: <b>the program literally
  /// named "default" always loads <see cref="CvmDebuggerDefaultProgram.Source"/> itself</b> (Stefan's
  /// own request), regardless of whatever text happens to be sitting in that program's own
  /// <see cref="Ga144DebuggerProgramConfiguration.Source"/> -- "default" is the one fixed, always-known-
  /// good starting point that selecting it can always get back to. <see cref="RestoreCommand"/> reuses
  /// this exact same method (and so this exact same override) when reloading "default" from storage, so
  /// restoring "default" shows the same canonical text as simply selecting it, never whatever raw text a
  /// hand-edited project file might hold under that name.
  /// Every OTHER program loads its own actually-saved <see cref="Ga144DebuggerProgramConfiguration.Source"/>
  /// normally. Does not call <see cref="Assemble"/> itself -- both call sites do that on their own right
  /// after, once they also know whether this is the very first load or a live switch.
  /// </summary>
  private void LoadEditorFromProgram(Ga144DebuggerProgramConfiguration program)
  {
    _assemblyCodeText = IsDefaultProgramName(program.Name) ? DefaultAssemblyCode : program.Source;
    OnPropertyChanged(nameof(AssemblyCodeText));
    _programNameText = program.Name;
    OnPropertyChanged(nameof(ProgramNameText));
  }

  /// <summary>The exact "default" name test <see cref="LoadEditorFromProgram"/> uses -- same
  /// case-insensitive comparison <see cref="Ga144ChipConfiguration.Normalize"/>'s own "default" check
  /// and <see cref="SaveProgram"/>'s own name-collision check use, kept here as its own named helper
  /// only so this one call site reads clearly; the Models-side check lives separately since this
  /// ViewModels-side helper is not reachable from there.</summary>
  private static bool IsDefaultProgramName(string name) => string.Equals(name, "default", StringComparison.OrdinalIgnoreCase);

  /// <summary>Writes <see cref="AssemblyCodeText"/>'s current contents back into <see cref="SelectedProgram"/>'s
  /// own <see cref="Ga144DebuggerProgramConfiguration.Source"/> -- called before switching
  /// <see cref="SelectedProgram"/> away (so the edit is not lost) and again at the top of
  /// <see cref="SaveProgram"/> (so Save always persists whatever is actually on screen, even if the
  /// selection was never switched away from in between). Deliberately does NOT also copy
  /// <see cref="ProgramNameText"/> into <see cref="SelectedProgram"/>'s own Name -- see that property's
  /// own remarks for why a rename is held back until Save specifically.</summary>
  private void CommitEditorSourceIntoSelectedProgram()
  {
    if (_selectedProgram is not null)
    {
      _selectedProgram.Source = AssemblyCodeText;
    }
  }

  /// <summary>
  /// "Add": appends a new, empty, uniquely-named program (e.g. "New program", "New program 2", ...)
  /// to <see cref="Programs"/> and selects it -- selecting it is what actually loads its (empty) text
  /// into the editor and re-assembles (see <see cref="SelectedProgram"/>'s own remarks). Purely an
  /// in-memory addition: like every other edit here, it is not persisted onto
  /// <see cref="Ga144ChipConfiguration.DebuggerPrograms"/> until <see cref="SaveProgram"/> runs, so
  /// closing the CVM Debugger without Saving afterward discards it.
  /// </summary>
  private void AddProgram()
  {
    // No explicit CommitEditorSourceIntoSelectedProgram() call needed here -- the SelectedProgram
    // setter below already flushes the currently-selected program's edited text before switching.
    string name = GenerateUniqueProgramName("New program");
    var program = new Ga144DebuggerProgramConfiguration { Name = name, Source = string.Empty };
    Programs.Add(program);
    SelectedProgram = program;
    StatusText = $"Added a new program \"{name}\". Edit its Assembly Code, then click Save to keep it in the project.";
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
  /// "Save": commits the editor's current Assembly Code into <see cref="SelectedProgram"/>'s own
  /// Source, renames it to whatever <see cref="ProgramNameText"/> currently holds (Stefan's own spec:
  /// "rename when using save" -- a no-op if it was not actually changed), then persists the WHOLE
  /// <see cref="Programs"/> list -- every program, not just the selected one, so an edit made to
  /// another program before switching away, or a brand-new one from <see cref="AddProgramCommand"/>,
  /// is never silently lost -- onto <see cref="Ga144ChipConfiguration.DebuggerPrograms"/> and notifies
  /// the owning project, same auto-save mechanism the single-program editor always rode. Refuses an
  /// empty name or one that collides with a DIFFERENT program already in the list, leaving everything
  /// else (including the source-text commit) exactly where it stood so a rename mistake never blocks
  /// getting the assembly text itself saved -- Stefan can fix the name and click Save again.
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
      StatusText = "Cannot save: the program name cannot be empty.";
      return;
    }

    if (Programs.Any(program => !ReferenceEquals(program, selected) && string.Equals(program.Name, newName, StringComparison.OrdinalIgnoreCase)))
    {
      StatusText = $"Cannot save: another program is already named \"{newName}\".";
      return;
    }

    // BUG FIX (Stefan, 2026-09-20): renaming used to leave the program selector's dropdown showing the
    // OLD name after Save. Ga144DebuggerProgramConfiguration.Name now raises INotifyPropertyChanged
    // (see its own remarks), so this plain assignment is enough for the ComboBox's DisplayMemberPath
    // binding to pick up the new text immediately -- no extra collection manipulation needed to force
    // a visual refresh.
    bool renamed = !string.Equals(selected.Name, newName, StringComparison.Ordinal);
    selected.Name = newName;
    ProgramNameText = newName;

    _chip.DebuggerPrograms = [.. Programs.Select(program => new Ga144DebuggerProgramConfiguration { Name = program.Name, Source = program.Source })];
    _notifyProjectChanged();
    StatusText = renamed
        ? $"Saved and renamed the program to \"{newName}\"."
        : $"Saved \"{newName}\" to the project.";
  }

  /// <summary>
  /// "Restore" (REPURPOSED, 2026-09-20, per Stefan's own request -- previously reset the editor to the
  /// built-in test program regardless of what was selected): reloads the CURRENTLY selected program
  /// from what is actually persisted on this chip (<see cref="Ga144ChipConfiguration.DebuggerPrograms"/>
  /// -- "storage"), undoing whatever has been typed or renamed here since the last
  /// <see cref="SaveCommand"/>. Matches the selected program's persisted counterpart by
  /// <see cref="SelectedProgram"/>'s own <see cref="Ga144DebuggerProgramConfiguration.Name"/> -- which
  /// only ever changes at Save time, so this still finds the right persisted entry even with an
  /// unsaved, not-yet-saved rename sitting in <see cref="ProgramNameText"/> -- and reuses
  /// <see cref="LoadEditorFromProgram"/> so the "default" always-loads-the-built-in-program override
  /// applies exactly the same way it does when simply selecting "default" (see that method's own
  /// remarks). Loading also overwrites <see cref="ProgramNameText"/> back to the persisted name, which
  /// is what discards an in-progress, not-yet-saved rename attempt. A program <see cref="AddProgramCommand"/>
  /// created that has never been saved has no persisted counterpart at all -- restoring it would mean
  /// deleting it, which is not what "undo unsaved writing" means for a program that was never written
  /// anywhere yet, so this is a no-op (with an explanatory status message) instead.
  /// </summary>
  private void RestoreProgram()
  {
    if (SelectedProgram is not { } selected)
    {
      return;
    }

    Ga144DebuggerProgramConfiguration? persisted = _chip.DebuggerPrograms.FirstOrDefault(
        program => string.Equals(program.Name, selected.Name, StringComparison.Ordinal));
    if (persisted is null)
    {
      StatusText = $"\"{selected.Name}\" has never been saved -- nothing in the project to restore from.";
      return;
    }

    LoadEditorFromProgram(persisted);
    selected.Source = _assemblyCodeText;
    Assemble();
    StatusText = $"Restored \"{selected.Name}\" to what was last saved.";
  }

  /// <summary>
  /// Loads a linked <c>.gaimg</c> (<see cref="CvmImage.Load"/>) straight into whichever simulated SRAM
  /// is currently live -- <see cref="_session"/>'s (<see cref="CvmDebugSession.LoadImage"/>) if a chip
  /// is connected, otherwise <see cref="_standaloneSram"/> directly (via
  /// <see cref="MirrorSessionProgramIntoStandaloneSram"/>, reused here for its own zero-fill-the-tail
  /// logic even though nothing session-related is happening in that branch) -- entirely independent of
  /// the Assembly Code editor: this is a second, alternate way to get a program running, for a program
  /// that was compiled/assembled/linked outside this window (a Program project's own "Build", via
  /// `galink`) rather than typed here by hand. Called from <see cref="Views.CvmDebuggerWindow"/>'s own
  /// code-behind, which owns the actual <c>OpenFileDialog</c> (this view model has no UI dependency of
  /// its own, same convention as every other file-picking action in this IDE) -- <paramref name="path"/>
  /// is just the file the person picked. Never throws; a bad/missing file just becomes a StatusText
  /// message, the same contract <see cref="Assemble"/> already has for a bad assembly-text edit.
  ///
  /// Also remembered in <see cref="_loadedImage"/> so a Start / Reinstall AFTER this call -- with no
  /// session yet connected, e.g. right after a Program project's own "Debug" button opens this window
  /// for the first time -- re-applies this exact image to the freshly booted chip instead of silently
  /// reverting to whatever the Assembly Code editor holds; see <see cref="StartAsync"/>'s own remarks.
  /// </summary>
  public void LoadImageFile(string path)
  {
    if (IsBusy)
    {
      StatusText = "Cannot load an image while a Step/Continue/Start is in progress.";
      return;
    }

    try
    {
      using FileStream stream = File.OpenRead(path);
      CvmImage image = CvmImage.Load(stream);

      _session?.LoadImage(image);
      MirrorSessionProgramIntoStandaloneSram(image.Words);
      _loadedImageSymbols = image.Symbols;
      _loadedImage = image;

      StatusText = $"Loaded \"{Path.GetFileName(path)}\": {image.Words.Count} word(s), entry address " +
          $"{DescribeFlatAddress(image.EntryAddress)}, {image.Symbols.Count} symbol(s).";
      RefreshMemoryView();
      RefreshProgramCounter();
    }
    catch (Exception exception)
    {
      StatusText = $"Could not load \"{Path.GetFileName(path)}\": {exception.Message}";
    }
  }

  /// <summary>
  /// Refreshes <see cref="MemoryViewText"/> starting at <see cref="MemoryBaseText"/>. The number of
  /// words shown is <see cref="MemoryViewWordCount"/>, however many words the CURRENTLY loaded
  /// program actually occupies, or one past the highest address any <see cref="_loadedImageSymbols"/>
  /// entry resolves to -- whichever is largest -- so opening the CVM Debugger with a short program (or
  /// none) still gets a reasonable-sized view, but a longer one like
  /// <see cref="CvmDebuggerDefaultProgram"/>'s own 156 words is never silently truncated the way a
  /// fixed 64-word window would, AND a loaded image's own <c>__exit</c>/<c>__start</c>/named-function
  /// symbols are always at least reachable by scrolling, even in a link whose real body sits far past
  /// its own vector table -- see <see cref="CvmLinker"/>'s own entry-layout remarks for that shape.
  /// EXTENDED 2026-09-09, per Stefan, "the simulated sram show only memory until 0x3f, not the whole
  /// program": the symbol-address term is the new part here; <see cref="MemoryViewWordCount"/> and
  /// loadedProgramLength alone were already believed sufficient (see git history for the original
  /// Math.Max-of-those-two version this replaces) but evidently were not always reaching every symbol
  /// in practice -- this is a defensive belt-and-suspenders addition, not a confirmed root-cause fix:
  /// no concrete gap between a loaded image's own word count and its highest symbol address was found
  /// by static review of <see cref="CvmLinker"/>/<see cref="CvmImage"/> (both appear to keep those two
  /// numbers consistent), so if the view still comes up short after this, the actual defect is
  /// probably elsewhere -- re-open with the exact steps that reproduced it. Recomputed on every call
  /// (not cached) since the loaded program can change between calls (Assemble, Start, LoadImageFile).
  /// A row also gets a "&lt;name&gt;" note for every symbol <see cref="_loadedImageSymbols"/> resolves
  /// to that exact address, alongside the existing PC/breakpoint/disassembly notes -- empty, and so
  /// silently a no-op here, unless <see cref="LoadImageFile"/> is what's currently loaded.
  /// </summary>
  private void RefreshMemoryView()
  {
    if (!TryParseAddress(MemoryBaseText, out int baseAddress))
    {
      MemoryViewText = $"'{MemoryBaseText}' is not a valid address. Use \"p:aaaa\" or a flat hex address.";
      return;
    }

    int loadedProgramLength = _session?.Program.Count ?? _standaloneProgram.Count;
    // +1 because "reaches this symbol" means the window must include its own address, not stop right
    // before it -- an empty _loadedImageSymbols (nothing loaded via LoadImageFile) makes this 0, a
    // no-op against the Math.Max below.
    int highestSymbolAddressPlusOne = _loadedImageSymbols.Count == 0 ? 0 : _loadedImageSymbols.Max(symbol => symbol.Address) + 1;
    int desiredWordCount = Math.Max(MemoryViewWordCount, Math.Max(loadedProgramLength, highestSymbolAddressPlusOne));
    int count = Math.Min(desiredWordCount, CvmSimulatedSram.WordCapacity - baseAddress);
    if (count <= 0)
    {
      MemoryViewText = $"{DescribeFlatAddress(baseAddress)} is at or past the end of the simulated SRAM.";
      return;
    }

    IReadOnlyList<int> words;
    HashSet<int> breakpoints;
    int? programCounter;

    // The disassembly is only meaningful on page 0 (the only page that is ever code) and it MUST
    // be re-scanned from address 0 every time -- 'plit's trailing literal is only recognizable as
    // DATA, not another opcode, because of the stateful scan that walked over its opcode word first.
    IReadOnlyDictionary<int, string> disassembly;

    if (_session is not null)
    {
      words = _session.ReadMemory(baseAddress, count);
      breakpoints = [.. _session.Breakpoints];
      programCounter = _session.LastFetchAddress;
      disassembly = baseAddress < CvmMemoryProtocol.Page0WordCount
          ? _session.DisassemblePage0(Math.Min(baseAddress + count, CvmMemoryProtocol.Page0WordCount))
          : new Dictionary<int, string>();
    }
    else
    {
      // No chip connected (or Stop was clicked) -- the standalone SRAM is always there to inspect,
      // whether or not anything has been assembled into it yet (untouched, it just reads back as
      // all-zero words). There is no "last fetch" or breakpoints without a live chip actually
      // reading from it, so those stay empty/null here.
      words = _standaloneSram.ReadRange(baseAddress, count);
      breakpoints = [];
      programCounter = null;
      (_, IReadOnlyDictionary<int, F18CompileResult> compiledRam, _) = CompileStandaloneCvmNodes();
      disassembly = baseAddress < CvmMemoryProtocol.Page0WordCount
          ? CvmAssemblyLanguage.DisassemblePage0(_standaloneSram, compiledRam, Math.Min(baseAddress + count, CvmMemoryProtocol.Page0WordCount))
          : new Dictionary<int, string>();
    }

    // Grouped by address up front (an image can legitimately have several symbols at the same
    // address, e.g. __exit and a library-internal alias for it) so the row loop below is a plain
    // dictionary lookup rather than an O(symbols) scan per row.
    ILookup<int, string> loadedImageSymbolsByAddress = _loadedImageSymbols.ToLookup(symbol => symbol.Address, symbol => symbol.Name);

    var builder = new StringBuilder();
    builder.Append("Address   Value   Notes").Append('\n');
    for (int index = 0; index < words.Count; index++)
    {
      int flatAddress = baseAddress + index;
      var notes = new List<string>();
      if (programCounter == flatAddress)
      {
        notes.Add("<- PC");
      }

      if (breakpoints.Contains(flatAddress))
      {
        notes.Add("[BP]");
      }

      foreach (string symbolName in loadedImageSymbolsByAddress[flatAddress])
      {
        notes.Add($"<{symbolName}>");
      }

      if (disassembly.TryGetValue(flatAddress, out string? note))
      {
        notes.Add(note);
      }

      // The SRAM is 16-bit (CvmWordCodec.WordMask = 0xFFFF), so 4 hex digits always suffice; the
      // leading "0x" is dropped since every value in this column is already known to be hex.
      builder.Append(DescribeFlatAddress(flatAddress).PadRight(10))
          .Append($"{words[index]:X4}".PadRight(8))
          .Append(string.Join(' ', notes))
          .Append('\n');
    }

    MemoryViewText = builder.ToString();
  }

  private void RefreshProgramCounter()
  {
    if (_session?.LastFetchAddress is int address)
    {
      string? mnemonic = _session.DisassemblePage0(address + 1).GetValueOrDefault(address);
      ProgramCounterText = mnemonic is null ? DescribeFlatAddress(address) : $"{DescribeFlatAddress(address)} ({mnemonic})";
    }
    else
    {
      ProgramCounterText = "-";
    }
  }

  // The _session-is-null branch below is what blanks LogText. Stop() deliberately never calls this
  // method at all (see its own remarks) specifically to avoid hitting that branch -- Stop leaves
  // LogText exactly as it was rather than blanking it. Every other call site (StartAsync, StepAsync,
  // ContinueAsync) only reaches this method with a non-null _session, so in today's code this branch
  // is purely defensive (e.g. a session that goes null out from under an in-flight await).
  private void RefreshLog()
  {
    if (_session is null)
    {
      LogText = string.Empty;
      return;
    }

    LogText = string.Join(Environment.NewLine, _session.TransactionLog);
  }

  private void RefreshBreakpointList()
  {
    Breakpoints.Clear();
    if (_session is null)
    {
      return;
    }

    foreach (int flatAddress in _session.Breakpoints.OrderBy(address => address))
    {
      Breakpoints.Add(DescribeFlatAddress(flatAddress));
    }

    NotifyCommandStates();
  }

  // Accepts either the "p:aaaa" page/address-in-page shorthand every transaction log line and the
  // memory inspector already use, or a plain flat hex address (with or without a leading "0x") --
  // whichever is more convenient for the address Stefan has in hand.
  private static bool TryParseAddress(string text, out int flatAddress)
  {
    flatAddress = 0;
    string trimmed = (text ?? string.Empty).Trim();
    if (trimmed.Length == 0)
    {
      return false;
    }

    int colonIndex = trimmed.IndexOf(':');
    if (colonIndex >= 0)
    {
      string pageText = trimmed[..colonIndex].Trim();
      string addressText = trimmed[(colonIndex + 1)..].Trim();
      if (!int.TryParse(pageText, System.Globalization.NumberStyles.HexNumber, null, out int page) ||
          !int.TryParse(addressText, System.Globalization.NumberStyles.HexNumber, null, out int addressInPage))
      {
        return false;
      }

      flatAddress = CvmMemoryProtocol.CombineAddress(page, addressInPage);
      return true;
    }

    string hex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed[2..] : trimmed;
    if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int parsed) ||
        parsed < 0 || parsed >= CvmSimulatedSram.WordCapacity)
    {
      return false;
    }

    flatAddress = parsed;
    return true;
  }

  private static string DescribeFlatAddress(int flatAddress)
  {
    int page = (flatAddress >> 16) & 0xF;
    int addressInPage = flatAddress & 0xFFFF;
    return CvmMemoryProtocol.FormatPageAddress(page, addressInPage);
  }

  private void NotifyCommandStates()
  {
    StartCommand.NotifyCanExecuteChanged();
    StepCommand.NotifyCanExecuteChanged();
    ContinueCommand.NotifyCanExecuteChanged();
    PauseCommand.NotifyCanExecuteChanged();
    StopCommand.NotifyCanExecuteChanged();
    AddBreakpointCommand.NotifyCanExecuteChanged();
    RemoveBreakpointCommand.NotifyCanExecuteChanged();
    ClearBreakpointsCommand.NotifyCanExecuteChanged();
    RefreshMemoryCommand.NotifyCanExecuteChanged();
    AssembleCommand.NotifyCanExecuteChanged();
    AddProgramCommand.NotifyCanExecuteChanged();
    CoreDumpCommand.NotifyCanExecuteChanged();
    OnPropertyChanged(nameof(IsContinuing));
    OnPropertyChanged(nameof(IsSessionActive));
  }
}