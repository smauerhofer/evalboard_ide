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
/// at a time instead of the automatic test's all-at-once summary.
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

  public CvmDebuggerViewModel(
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<F18MacroDefinition> userMacros,
      KrakenLiveController krakenController,
      Func<KrakenEndpointInfo?> resolveEndpoint)
  {
    _chip = chip ?? throw new ArgumentNullException(nameof(chip));
    _romLibrary = romLibrary ?? throw new ArgumentNullException(nameof(romLibrary));
    _userMacros = userMacros ?? [];
    _krakenController = krakenController ?? throw new ArgumentNullException(nameof(krakenController));
    _resolveEndpoint = resolveEndpoint ?? throw new ArgumentNullException(nameof(resolveEndpoint));

    StartCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy);
    StepCommand = new AsyncRelayCommand(StepAsync, () => !IsBusy && IsSessionActive);
    ContinueCommand = new AsyncRelayCommand(ContinueAsync, () => !IsBusy && IsSessionActive);
    PauseCommand = new RelayCommand(Pause, () => IsContinuing);
    AddBreakpointCommand = new RelayCommand(AddBreakpoint, () => IsSessionActive);
    RemoveBreakpointCommand = new RelayCommand(RemoveSelectedBreakpoint, () => IsSessionActive && SelectedBreakpoint is not null);
    ClearBreakpointsCommand = new RelayCommand(ClearBreakpoints, () => IsSessionActive && Breakpoints.Count > 0);
    RefreshMemoryCommand = new RelayCommand(RefreshMemoryView, () => IsSessionActive);
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
  public RelayCommand AddBreakpointCommand { get; }
  public RelayCommand RemoveBreakpointCommand { get; }
  public RelayCommand ClearBreakpointsCommand { get; }
  public RelayCommand RefreshMemoryCommand { get; }

  /// <summary>Tied to the debugger window's Closed event -- stops any in-flight Continue and releases the port.</summary>
  public void Cancel()
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

  private void RefreshMemoryView()
  {
    if (_session is null)
    {
      MemoryViewText = string.Empty;
      return;
    }

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

    IReadOnlyList<int> words = _session.ReadMemory(baseAddress, count);
    HashSet<int> breakpoints = [.. _session.Breakpoints];
    int? programCounter = _session.LastFetchAddress;

    var builder = new StringBuilder();
    builder.Append("Address   Value      Notes").Append('\n');
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

      string? symbol = _session.DescribeProgramSymbol(flatAddress);
      if (symbol is not null)
      {
        notes.Add(symbol);
      }

      builder.Append(DescribeFlatAddress(flatAddress).PadRight(10))
          .Append($"0x{words[index]:X5}".PadRight(11))
          .Append(string.Join(' ', notes))
          .Append('\n');
    }

    MemoryViewText = builder.ToString();
  }

  private void RefreshProgramCounter()
  {
    if (_session?.LastFetchAddress is int address)
    {
      string? symbol = _session.DescribeProgramSymbol(address);
      ProgramCounterText = symbol is null ? DescribeFlatAddress(address) : $"{DescribeFlatAddress(address)} ({symbol})";
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
    AddBreakpointCommand.NotifyCanExecuteChanged();
    RemoveBreakpointCommand.NotifyCanExecuteChanged();
    ClearBreakpointsCommand.NotifyCanExecuteChanged();
    RefreshMemoryCommand.NotifyCanExecuteChanged();
    OnPropertyChanged(nameof(IsContinuing));
    OnPropertyChanged(nameof(IsSessionActive));
  }
}
