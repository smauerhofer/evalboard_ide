using System.Collections.ObjectModel;
using System.Text;
using Ga144.Evb.Ide.Compiler;
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
/// longer needs a code change and a rebuild, just an edit and a click. <see cref="SaveCommand"/>/
/// <see cref="LoadCommand"/> persist that hand-written source onto this chip's own project data
/// (<see cref="Ga144ChipConfiguration.DebuggerAssemblyCode"/>) so it survives closing the debugger,
/// while <see cref="RestoreCommand"/> discards it in favor of the original built-in test program.
///
/// Assemble itself never actually needed a connected chip -- node 607's source compiles the same way
/// whether or not one is attached -- so it no longer requires an active session: with none, it
/// assembles into a standalone simulated SRAM (<see cref="_standaloneSram"/>) that exists for this
/// view model's whole lifetime, so a program can be written and checked before ever clicking Start.
/// <see cref="StartAsync"/> re-applies <see cref="AssemblyCodeText"/> to the chip the moment it
/// connects, so nothing assembled standalone is lost.
/// </summary>
public sealed class CvmDebuggerViewModel : ObservableObject
{
  // Generous compared to the automatic test's own 96-transaction cap: an interactive Run is expected
  // to be aimed at a specific breakpoint, but should not spin forever chasing one that never fires.
  private const int ContinueTransactionCap = 2_000;

  // How many words the memory inspector shows at once, and the default starting point (page 0,
  // address 0 -- the start of the loaded test program) before Stefan jumps elsewhere.
  private const int MemoryViewWordCount = 64;

  private readonly Ga144ChipConfiguration _chip;
  private readonly Ga144RomLibrary _romLibrary;
  private readonly IReadOnlyList<F18MacroDefinition> _userMacros;
  private readonly KrakenLiveController _krakenController;
  private readonly Func<KrakenEndpointInfo?> _resolveEndpoint;

  // Bubbles up to the owning project (ProjectViewModel.NotifyProjectChanged, ultimately
  // MainWindowViewModel's debounced auto-save) -- the same mechanism every other edit in this IDE
  // rides, so clicking Save here needs no bespoke save-to-disk logic of its own.
  private readonly Action _notifyProjectChanged;

  // A plain in-memory simulated SRAM that exists for this view model's whole lifetime -- unlike
  // CvmDebugSession's own SRAM, it is NOT tied to a live session, so Assemble (and the memory
  // inspector) always have somewhere to write/read even before the first Start or after a Stop.
  // There is nothing hardware-specific about assembling: node 607's source compiles the same way
  // whether or not a chip is connected (CompileStandaloneNode607), so there is no real reason
  // Assemble should ever have needed a session in the first place. Mirrored from the live session's
  // own program after every successful Start/Assemble (MirrorSessionProgramIntoStandaloneSram) so
  // that Stop leaves the memory inspector showing the last real state instead of stale pre-Start
  // content.
  private readonly CvmSimulatedSram _standaloneSram = new();
  private IReadOnlyList<int> _standaloneProgram = [];

  // Source-form equivalent of CvmMemoryProtocol.TryBuildDebuggerTestProgram's own hardcoded words --
  // assembling this unedited reproduces exactly the program Start already loads today (call 0x20 at
  // address 1, br 1 at address 2, 'ret at address 0x20, 'nop padding everywhere else), so clicking
  // Assemble right after Start is a no-op on the simulated SRAM's contents. Edit it and click
  // Assemble to try a different program against the connected hardware without a rebuild.
  private const string DefaultAssemblyCode =
      "; CVM Debugger test program -- matches what Start loads by default.\n" +
      "; Edit this, then click Assemble to load your own program into the simulated SRAM.\n" +
      "nop\n" +
      "call 0x20   ; address 1\n" +
      "br 1        ; address 2 -- branches past address 3 once the call above returns here\n" +
      "nop\n" +
      "nop\n" +
      "pushlit 0x1234\n" +
      "pop\n" +
      "push\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "nop\n" +
      "ret         ; address 0x20\n";

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

  public CvmDebuggerViewModel(
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<F18MacroDefinition> userMacros,
      KrakenLiveController krakenController,
      Func<KrakenEndpointInfo?> resolveEndpoint,
      Action notifyProjectChanged)
  {
    _chip = chip ?? throw new ArgumentNullException(nameof(chip));
    _romLibrary = romLibrary ?? throw new ArgumentNullException(nameof(romLibrary));
    _userMacros = userMacros ?? [];
    _krakenController = krakenController ?? throw new ArgumentNullException(nameof(krakenController));
    _resolveEndpoint = resolveEndpoint ?? throw new ArgumentNullException(nameof(resolveEndpoint));
    _notifyProjectChanged = notifyProjectChanged ?? throw new ArgumentNullException(nameof(notifyProjectChanged));

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
    SaveCommand = new RelayCommand(SaveAssemblyCode);
    LoadCommand = new RelayCommand(LoadAssemblyCode, () => _chip.DebuggerAssemblyCode is not null);
    RestoreCommand = new RelayCommand(RestoreAssemblyCode);

    // The standalone SRAM exists from construction, before Start has ever run -- show it (all-zero
    // words) right away instead of leaving the memory inspector blank until the first Start.
    RefreshMemoryView();
  }

  public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) NotifyCommandStates(); } }

  public bool IsContinuing => _continueCts is not null;

  public bool IsSessionActive => _session is not null;

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
  /// The CVM Debugger's own Assembly Code editor contents -- prefilled with <see cref="DefaultAssemblyCode"/>,
  /// which assembles to byte-identical words as the hardcoded test program Start already loads, so
  /// clicking Assemble unedited right after Start is a no-op. Freely editable; <see cref="AssembleCommand"/>
  /// is what actually does anything with it.
  /// </summary>
  public string AssemblyCodeText { get => _assemblyCodeText; set => SetProperty(ref _assemblyCodeText, value ?? string.Empty); }

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
  public RelayCommand SaveCommand { get; }
  public RelayCommand LoadCommand { get; }
  public RelayCommand RestoreCommand { get; }

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
      _session = await installer.StartDebugSessionAsync(endpoint.PortName, _chip, compileService);

      InstallSummaryText = $"Install: {_session.Install.Steps.Count} boot frame(s) sent, fire-and-forget. Loaded a {_session.Program.Count}-word test program " +
          "(5 'nop, 'plit, literal, 'pop, 'push, 8 trailing 'nop) into the simulated SRAM and woke node 708's 'start.";

      // The Assembly Code editor is the single source of truth for what should be running, whether
      // it was edited before or after Start -- re-apply it to the freshly connected chip now, so
      // nothing already typed (or assembled standalone while no session existed) needs a second,
      // manual click of Assemble. Assembling an untouched DefaultAssemblyCode reproduces the exact
      // same words the install line above just described, so this is always safe and a no-op when
      // nothing was edited.
      (bool assembleSuccess, string? assembleError) = _session.AssembleAndLoadProgram(AssemblyCodeText);
      if (!assembleSuccess)
      {
        InstallSummaryText += $" Could not apply the Assembly Code editor's current contents ({assembleError}) -- the install's own default program above is still what's loaded.";
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
  /// state, same as before the very first Start. Unlike closing the window, the CVM Debugger stays
  /// open afterward -- Start reconnects and reinstalls from scratch, same as it would after a fresh
  /// launch.
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
    RefreshLog();
    RefreshMemoryView();
    RefreshProgramCounter();
    OnPropertyChanged(nameof(IsSessionActive));
    NotifyCommandStates();
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
    if (_session is not null)
    {
      (bool success, string? error) = _session.AssembleAndLoadProgram(AssemblyCodeText);
      StatusText = success
          ? $"Assembled {_session.Program.Count} word(s) and loaded them into the simulated SRAM starting at address 0."
          : $"Assemble failed: {error}";

      if (success)
      {
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

    RefreshMemoryView();
    RefreshProgramCounter();
  }

  /// <summary>
  /// The no-session half of <see cref="Assemble"/>: compiles node 607's CURRENT source locally
  /// (<see cref="CompileStandaloneNode607"/> -- a pure software compile, no port or connected chip
  /// needed) and, on success, assembles <see cref="AssemblyCodeText"/> against it straight into
  /// <see cref="_standaloneSram"/>/<see cref="_standaloneProgram"/> via the same
  /// <see cref="CvmAssemblyLanguage.AssembleAndLoadProgram"/> a live session uses.
  /// </summary>
  private (bool Success, string? Error, int WordCount) AssembleStandalone()
  {
    (bool compileSuccess, IReadOnlyDictionary<int, F18CompileResult> compiledRam) = CompileStandaloneNode607();
    if (!compileSuccess)
    {
      return (false, $"node {CvmMemoryProtocol.NopSourceNodeCoordinate:000}'s RAM source does not currently compile -- fix it in the Node Editor first.", 0);
    }

    (List<int>? words, string? error) = CvmAssemblyLanguage.AssembleAndLoadProgram(AssemblyCodeText, _standaloneSram, _standaloneProgram, compiledRam);
    if (words is null)
    {
      return (false, error, 0);
    }

    _standaloneProgram = words;
    return (true, null, words.Count);
  }

  /// <summary>
  /// Compiles node 607's current source (<see cref="F18NodeCompilationService"/>) purely in software
  /// -- no serial port, no connected chip -- for <see cref="AssembleStandalone"/> and the memory
  /// inspector's no-session disassembly to resolve tagged mnemonics (<c>nop</c>/<c>pushlit</c>/
  /// <c>push</c>/<c>pop</c>/<c>ret</c>) against. Self-describing opcodes (<c>call</c>/<c>br</c>/
  /// <c>ifbr</c>/<c>slit</c>) never need this at all. Returns an empty table (never throws) if node
  /// 607's RAM source doesn't currently compile.
  /// </summary>
  private (bool Success, IReadOnlyDictionary<int, F18CompileResult> CompiledRam) CompileStandaloneNode607()
  {
    F18NodeCompilationResult compiled = new F18NodeCompilationService(_chip, _romLibrary, _userMacros)
        .CompileNode(CvmMemoryProtocol.NopSourceNodeCoordinate);
    return compiled.Ram.Success
        ? (true, new Dictionary<int, F18CompileResult> { [CvmMemoryProtocol.NopSourceNodeCoordinate] = compiled.Ram })
        : (false, new Dictionary<int, F18CompileResult>());
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
  /// Saves <see cref="AssemblyCodeText"/> onto this chip's <see cref="Ga144ChipConfiguration.DebuggerAssemblyCode"/>
  /// and notifies the owning project, which rides the IDE's normal debounced auto-save -- same
  /// mechanism as every other edit in this app, so there is nothing further to do here to actually
  /// land it on disk.
  /// </summary>
  private void SaveAssemblyCode()
  {
    _chip.DebuggerAssemblyCode = AssemblyCodeText;
    _notifyProjectChanged();
    LoadCommand.NotifyCanExecuteChanged();
    StatusText = "Saved the current Assembly Code to the project.";
  }

  /// <summary>Replaces <see cref="AssemblyCodeText"/> with whatever <see cref="SaveAssemblyCode"/> last saved for this chip. Disabled (see the constructor's <see cref="LoadCommand"/> wiring) until a save has happened at least once.</summary>
  private void LoadAssemblyCode()
  {
    if (_chip.DebuggerAssemblyCode is not { } saved)
    {
      StatusText = "No assembly code has been saved for this chip yet -- click Save first.";
      return;
    }

    AssemblyCodeText = saved;
    StatusText = "Loaded the last saved Assembly Code from the project.";
  }

  /// <summary>Replaces <see cref="AssemblyCodeText"/> with the original built-in test program (<see cref="DefaultAssemblyCode"/>), bypassing whatever was saved -- a clean way back to a known-good starting point.</summary>
  private void RestoreAssemblyCode()
  {
    AssemblyCodeText = DefaultAssemblyCode;
    StatusText = "Restored the original test Assembly Code.";
  }

  private void RefreshMemoryView()
  {
    if (!TryParseAddress(MemoryBaseText, out int baseAddress))
    {
      MemoryViewText = $"'{MemoryBaseText}' is not a valid address. Use \"p:aaaa\" or a flat hex address.";
      return;
    }

    int count = Math.Min(MemoryViewWordCount, CvmSimulatedSram.WordCapacity - baseAddress);
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
      (_, IReadOnlyDictionary<int, F18CompileResult> compiledRam) = CompileStandaloneNode607();
      disassembly = baseAddress < CvmMemoryProtocol.Page0WordCount
          ? CvmAssemblyLanguage.DisassemblePage0(_standaloneSram, compiledRam, Math.Min(baseAddress + count, CvmMemoryProtocol.Page0WordCount))
          : new Dictionary<int, string>();
    }

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
    LoadCommand.NotifyCanExecuteChanged();
    OnPropertyChanged(nameof(IsContinuing));
    OnPropertyChanged(nameof(IsSessionActive));
  }
}