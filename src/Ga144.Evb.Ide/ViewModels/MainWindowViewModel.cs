using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
  private readonly YamlConfigurationStore _configurationStore;
  private readonly Ga144RomLibraryStore _romLibraryStore;
  private readonly SerialPortDiscoveryService _discovery;
  private readonly Ga144Node708Probe _probe;
  private readonly CancellationTokenSource _shutdown = new();
  private readonly HashSet<string> _probedThisSession = new(StringComparer.OrdinalIgnoreCase);
  private CancellationTokenSource? _saveDebounce;
  private IdeWorkspace _workspace = IdeWorkspace.CreateDefault();
  private Ga144RomLibrary _romLibrary = Ga144RomLibrary.CreateDefault();
  private BoardViewModel? _selectedBoard;
  private ProjectViewModel? _selectedProject;
  private SerialPortViewModel? _selectedPort;
  private bool _isBusy;
  private string _statusText = "Starting...";
  private string _lastScanText = "Not scanned";
  private bool _workspaceDirty;
  private long _workspaceRevision;
  private bool _scanInProgress;
  private int _serialScanSuspendCount;
  private readonly Dictionary<(Guid ProjectId, Guid BoardId, Ga144ChipRole Role), KrakenLiveController> _krakenControllers = new();
  // Idle-handle policy applied to every new Kraken controller. HoldOpen is the
  // safe default on Port A (RTS = EVB RESET-); switch to CloseWhileIdle only if
  // the COM port must be shared with another process while the Kraken is idle.
  private readonly KrakenIdlePolicy _krakenIdlePolicy = KrakenIdlePolicy.CloseAfterIdleTimeout;
  private bool _disposed;

  public MainWindowViewModel(
      YamlConfigurationStore configurationStore,
      Ga144RomLibraryStore romLibraryStore,
      SerialPortDiscoveryService discovery,
      Ga144Node708Probe probe,
      string configurationPath,
      string romLibraryPath)
  {
    _configurationStore = configurationStore;
    _romLibraryStore = romLibraryStore;
    _discovery = discovery;
    _probe = probe;
    ConfigurationPath = configurationPath;
    RomLibraryPath = romLibraryPath;

    ScanNowCommand = new AsyncRelayCommand(
        () => ScanAsync(probeAllFtdiPorts: true),
        () => !IsBusy && !HasAnyLiveKraken());
    ProbeSelectedPortCommand = new AsyncRelayCommand(ProbeSelectedAsync, CanProbeSelectedPort);
    SaveWorkspaceCommand = new AsyncRelayCommand(SaveWorkspaceAsync, () => !IsBusy);
    AssignPortACommand = new AsyncRelayCommand(() => AssignAsync(EvalBoardPortRole.PortAHost), CanAssignSelectedPort);
    AssignPortBCommand = new AsyncRelayCommand(() => AssignAsync(EvalBoardPortRole.PortBGeneral), CanAssignSelectedPort);
    AssignPortCCommand = new AsyncRelayCommand(() => AssignAsync(EvalBoardPortRole.PortCTarget), CanAssignSelectedPort);
    ForgetAssignmentCommand = new AsyncRelayCommand(ForgetAssignmentAsync, CanOperateOnSelectedPort);
    AddBoardCommand = new RelayCommand(AddBoard, () => !IsBusy);
    RemoveBoardCommand = new AsyncRelayCommand(RemoveSelectedBoardAsync, () =>
        !IsBusy && SelectedBoard is not null && Boards.Count > 1 && !HasActiveKrakenForBoard(SelectedBoard.Id));
    AddProjectCommand = new RelayCommand(AddProject, () => !IsBusy);
    RemoveProjectCommand = new AsyncRelayCommand(RemoveSelectedProjectAsync, () =>
        !IsBusy && SelectedProject is not null && Projects.Count > 1 && !HasActiveKrakenForProject(SelectedProject.Id));
    OpenConfigurationFolderCommand = new RelayCommand(OpenConfigurationFolder);
  }

  public ObservableCollection<BoardViewModel> Boards { get; } = [];
  public ObservableCollection<ProjectViewModel> Projects { get; } = [];
  public ObservableCollection<SerialPortViewModel> Ports { get; } = [];
  public IReadOnlyList<EvalBoardModel> BoardModels { get; } = Enum.GetValues<EvalBoardModel>();
  public string ConfigurationPath { get; }
  public string RomLibraryPath { get; }
  public Ga144RomLibrary RomLibrary => _romLibrary;

  /// <summary>
  /// Resolves the selected board's saved FTDI binding to a COM port that is
  /// physically present now. Kraken windows use this instead of persisting a
  /// volatile COM number in the software project.
  /// </summary>
  public KrakenEndpointInfo? ResolveKrakenEndpoint(Ga144ChipRole role)
  {
    BoardViewModel? board = SelectedBoard;
    return board is null ? null : ResolveKrakenEndpoint(board.Id, role);
  }

  /// <summary>
  /// Returns the process-lifetime Kraken controller for this project/physical
  /// board/chip. The controller is NOT owned by a chip window: once it erects
  /// Kraken its COM endpoint remains reserved even if chip/node/check windows are
  /// closed and reopened. The native handle itself is parked while idle.
  /// </summary>
  public KrakenLiveController GetKrakenController(ProjectViewModel project, Ga144ChipRole role)
  {
    ArgumentNullException.ThrowIfNull(project);
    BoardViewModel board = SelectedBoard ?? throw new InvalidOperationException("Select an eval board before opening a GA144 chip.");
    var key = (ProjectId: project.Id, BoardId: board.Id, Role: role);
    if (_krakenControllers.TryGetValue(key, out KrakenLiveController? existing))
    {
      return existing;
    }

    KrakenLiveController? physicalOwner = _krakenControllers
        .Where(item => item.Key.BoardId == board.Id && item.Key.Role == role)
        .Select(item => item.Value)
        .FirstOrDefault(controller => controller.HasExclusiveSerialOwnership);
    if (physicalOwner is not null)
    {
      throw new InvalidOperationException(
          $"The selected {role} GA144 already has a live Kraken owned by another project runtime on {physicalOwner.CurrentEndpoint?.PortName ?? "its serial endpoint"}. " +
          "That physical COM endpoint is reserved by the resident Kraken and cannot be reassigned.");
    }

    KrakenLiveController controller = new(
        project.GetChip(role).Kraken,
        () => ResolveKrakenEndpoint(board.Id, role),
        _krakenIdlePolicy);
    controller.StateChanged += OnKrakenControllerStateChanged;
    _krakenControllers.Add(key, controller);
    return controller;
  }

  /// <summary>
  /// Erection is transient runtime state, never persisted to YAML. Drop it (and
  /// close the COM handle) for every cached controller. Called at startup after
  /// the workspace loads, so a resident Kraken from a previous run is never
  /// assumed to still be installed. Does not reset the GA144.
  /// </summary>
  private async Task ResetAllTransientErectionAsync()
  {
    foreach (KrakenLiveController controller in _krakenControllers.Values.ToArray())
    {
      try
      {
        await controller.ResetTransientErectionAsync();
      }
      catch
      {
        // Best-effort: startup invalidation must not block workspace load.
      }
    }
  }

  /// <summary>
  /// Drop transient erection for every controller bound to a given board (both
  /// roles). Called when the selected board changes: the previously erected chip
  /// must not appear resident under a different board selection.
  /// </summary>
  private async Task ResetTransientErectionForBoardAsync(Guid boardId)
  {
    foreach (KeyValuePair<(Guid ProjectId, Guid BoardId, Ga144ChipRole Role), KrakenLiveController> item
             in _krakenControllers.Where(entry => entry.Key.BoardId == boardId).ToArray())
    {
      try
      {
        await item.Value.ResetTransientErectionAsync();
      }
      catch
      {
        // Best-effort.
      }
    }
  }

  /// <summary>
  /// Drop transient erection for the single controller bound to a board + role.
  /// Called when that role's port binding changes: Port A change resets the host
  /// chip's erection, Port C change resets the target chip's. Only the affected
  /// role is invalidated.
  /// </summary>
  private async Task ResetTransientErectionForRoleAsync(Guid boardId, Ga144ChipRole role)
  {
    foreach (KeyValuePair<(Guid ProjectId, Guid BoardId, Ga144ChipRole Role), KrakenLiveController> item
             in _krakenControllers.Where(entry => entry.Key.BoardId == boardId && entry.Key.Role == role).ToArray())
    {
      try
      {
        await item.Value.ResetTransientErectionAsync();
      }
      catch
      {
        // Best-effort.
      }
    }
  }

  /// <summary>Map an eval-board port role to the GA144 chip role it drives.</summary>
  private static Ga144ChipRole? ChipRoleForPortRole(EvalBoardPortRole role) => role switch
  {
    EvalBoardPortRole.PortAHost => Ga144ChipRole.Host,
    EvalBoardPortRole.PortCTarget => Ga144ChipRole.Target,
    _ => null // Port B (general host serial) does not carry a Kraken erection.
  };

  public bool IsPortKrakenOwned(string portName) =>
      !string.IsNullOrWhiteSpace(portName) &&
      _krakenControllers.Values.Any(controller =>
          controller.HasExclusiveSerialOwnership &&
          string.Equals(controller.CurrentEndpoint?.PortName, portName, StringComparison.OrdinalIgnoreCase));

  private KrakenEndpointInfo? ResolveKrakenEndpoint(Guid boardId, Ga144ChipRole role)
  {
    BoardViewModel? board = Boards.FirstOrDefault(item => item.Id == boardId);
    if (board is null)
    {
      return null;
    }

    BoardPortBinding? binding = role switch
    {
      Ga144ChipRole.Host => board.Model.PortA,
      Ga144ChipRole.Target => board.Model.PortC,
      _ => null
    };

    if (binding is null)
    {
      return null;
    }

    SerialPortViewModel? live = Ports.FirstOrDefault(item => binding.Ftdi.Matches(item.Port));
    return live is null
        ? null
        : new KrakenEndpointInfo(board.Name, live.PortName, role);
  }


  public BoardViewModel? SelectedBoard
  {
    get => _selectedBoard;
    set
    {
      BoardViewModel? previous = _selectedBoard;
      if (SetProperty(ref _selectedBoard, value))
      {
        // Erection is transient: switching away from a board must not leave its
        // chips appearing resident. Invalidate the previous board's controllers
        // (both roles) and close their COM handles. No chip reset.
        if (previous is not null && previous.Id != value?.Id)
        {
          Guid previousBoardId = previous.Id;
          _ = ResetTransientErectionForBoardAsync(previousBoardId);
        }

        _workspace.ActiveBoardId = value?.Id;
        OnPropertyChanged(nameof(HasSelectedBoard));
        MarkWorkspaceDirty();
        RefreshAssignments();
        NotifyCommandStates();
      }
    }
  }

  public bool HasSelectedBoard => SelectedBoard is not null;

  public ProjectViewModel? SelectedProject
  {
    get => _selectedProject;
    set
    {
      if (SetProperty(ref _selectedProject, value))
      {
        _workspace.ActiveProjectId = value?.Id;
        OnPropertyChanged(nameof(HasSelectedProject));
        MarkWorkspaceDirty();
        NotifyCommandStates();
      }
    }
  }

  public bool HasSelectedProject => SelectedProject is not null;

  public SerialPortViewModel? SelectedPort
  {
    get => _selectedPort;
    set
    {
      if (SetProperty(ref _selectedPort, value))
      {
        NotifyCommandStates();
      }
    }
  }

  public bool AutoDetectEnabled
  {
    get => _workspace.Settings.AutoDetect;
    set
    {
      if (_workspace.Settings.AutoDetect == value)
      {
        return;
      }

      _workspace.Settings.AutoDetect = value;
      OnPropertyChanged();
      MarkWorkspaceDirty();
    }
  }

  public bool ActiveProbeNewPortsEnabled
  {
    get => _workspace.Settings.ActiveProbeNewFtdiPorts;
    set
    {
      if (_workspace.Settings.ActiveProbeNewFtdiPorts == value)
      {
        return;
      }

      _workspace.Settings.ActiveProbeNewFtdiPorts = value;
      OnPropertyChanged();
      MarkWorkspaceDirty();
    }
  }

  public bool IsBusy
  {
    get => _isBusy;
    private set
    {
      if (SetProperty(ref _isBusy, value))
      {
        OnPropertyChanged(nameof(BusyVisibility));
        NotifyCommandStates();
      }
    }
  }

  public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

  public string StatusText
  {
    get => _statusText;
    private set => SetProperty(ref _statusText, value);
  }

  public string LastScanText
  {
    get => _lastScanText;
    private set => SetProperty(ref _lastScanText, value);
  }

  public AsyncRelayCommand ScanNowCommand { get; }
  public AsyncRelayCommand ProbeSelectedPortCommand { get; }
  public AsyncRelayCommand SaveWorkspaceCommand { get; }
  public AsyncRelayCommand AssignPortACommand { get; }
  public AsyncRelayCommand AssignPortBCommand { get; }
  public AsyncRelayCommand AssignPortCCommand { get; }
  public AsyncRelayCommand ForgetAssignmentCommand { get; }
  public RelayCommand AddBoardCommand { get; }
  public AsyncRelayCommand RemoveBoardCommand { get; }
  public RelayCommand AddProjectCommand { get; }
  public AsyncRelayCommand RemoveProjectCommand { get; }
  public RelayCommand OpenConfigurationFolderCommand { get; }

  public async Task InitializeAsync()
  {
    if (IsBusy)
    {
      return;
    }

    IsBusy = true;
    try
    {
      _workspace = await _configurationStore.LoadAsync(_shutdown.Token);
      _romLibrary = await _romLibraryStore.LoadAsync(_shutdown.Token);
      Boards.Clear();
      foreach (Ga144Board board in _workspace.Boards)
      {
        Boards.Add(new BoardViewModel(board, MarkWorkspaceDirty));
      }

      Projects.Clear();
      foreach (Ga144Project project in _workspace.Projects)
      {
        Projects.Add(new ProjectViewModel(project, MarkWorkspaceDirty));
      }

      SelectedBoard = Boards.FirstOrDefault(board => board.Id == _workspace.ActiveBoardId)
                      ?? Boards.FirstOrDefault();
      SelectedProject = Projects.FirstOrDefault(project => project.Id == _workspace.ActiveProjectId)
                        ?? Projects.FirstOrDefault();

      OnPropertyChanged(nameof(AutoDetectEnabled));
      OnPropertyChanged(nameof(ActiveProbeNewPortsEnabled));
      StatusText = "Workspace ready";
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
    {
      StatusText = $"Workspace error: {exception.Message}";
    }
    finally
    {
      IsBusy = false;
    }

    // Erection is transient runtime state and is never persisted. Ensure no
    // controller from a prior workspace state is assumed still resident.
    await ResetAllTransientErectionAsync();

    await ScanAsync(probeAllFtdiPorts: false);
  }

  public async Task AssignSelectedPortToBoardPortAsync(EvalBoardPortRole role)
  {
    if (!CanAssignSelectedPort())
    {
      return;
    }

    await AssignAsync(role);
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _shutdown.Cancel();

    for (int attempt = 0; attempt < 100 && _scanInProgress; attempt++)
    {
      await Task.Delay(20);
    }

    foreach (KrakenLiveController controller in _krakenControllers.Values)
    {
      controller.StateChanged -= OnKrakenControllerStateChanged;
      try
      {
        await controller.DisposeAsync();
      }
      catch
      {
        // Process shutdown is already in progress. Windows will release
        // any remaining serial handle when the process terminates.
      }
    }
    _krakenControllers.Clear();

    if (_workspaceDirty)
    {
      try
      {
        await _configurationStore.SaveAsync(_workspace);
        _workspaceDirty = false;
      }
      catch
      {
        // The application is closing. The explicit save command reports errors while the UI is active.
      }
    }

    _saveDebounce?.Dispose();
    _configurationStore.Dispose();
    _romLibraryStore.Dispose();
    _shutdown.Dispose();
  }

  public void MarkProjectChanged()
  {
    SelectedProject?.NotifyProjectChanged();
  }

  public async Task SaveRomLibraryAsync()
  {
    await _romLibraryStore.SaveAsync(_romLibrary, _shutdown.Token);
  }

  public async Task SaveWorkspaceImmediatelyAsync()
  {
    MarkWorkspaceDirty();
    await SaveWorkspaceAsync();
  }

  /// <summary>
  /// Stops future background serial scans and waits until any scan/probe that
  /// was already in flight has completely finished.  This closes the race
  /// where a timer-started node-708 probe could still be using an FTDI port
  /// while a chip window began Kraken erection.
  /// </summary>
  public async Task SuspendSerialScanningAsync()
  {
    _serialScanSuspendCount++;
    while (_scanInProgress && !_shutdown.IsCancellationRequested)
    {
      await Task.Delay(20, _shutdown.Token);
    }
  }

  public void ResumeSerialScanning()
  {
    if (_serialScanSuspendCount > 0)
    {
      _serialScanSuspendCount--;
    }
  }

  /// <summary>
  /// Called by SerialDeviceChangeWatcher when Windows reports a real USB device
  /// arrival/removal. Replaces the former ~1.5 s polling tick: an idle system now
  /// performs no periodic WMI/GetPortNames enumeration at all, which removes the
  /// continuous USB device-tree activity that stalled a KVM-shared mouse.
  /// </summary>
  public async void RequestDeviceChangeScan()
  {
    // Same guards as the former timer tick: honour the auto-detect setting, and
    // never auto-enumerate while a chip window is doing online work, a scan is
    // already running, or a live Kraken owns the FTDI endpoint (its RTS = EVB
    // RESET- must not be disturbed).
    if (AutoDetectEnabled && _serialScanSuspendCount == 0 && !_scanInProgress && !HasAnyLiveKraken())
    {
      await ScanAsync(probeAllFtdiPorts: false);
    }
  }

  private async Task ScanAsync(bool probeAllFtdiPorts)
  {
    if (_scanInProgress || _shutdown.IsCancellationRequested)
    {
      return;
    }

    // Once Kraken has been erected, freeze the complete Windows serial/PnP
    // discovery path. Do not even enumerate COM ports: the resident Kraken
    // reserves its cached physical-board endpoint even while the native COM
    // handle is parked. This avoids unrelated setup-class enumeration/FTDI
    // driver activity on a USB controller shared with HID.
    if (HasAnyLiveKraken())
    {
      LastScanText = "Serial discovery frozen: live Kraken";
      StatusText = "Serial/USB discovery is frozen while Kraken reserves its endpoint; COM is parked while idle.";
      NotifyCommandStates();
      return;
    }

    _scanInProgress = true;
    IsBusy = true;
    try
    {
      StatusText = "Enumerating serial interfaces...";
      IReadOnlyList<SerialPortInfo> discovered = await Task.Run(_discovery.Enumerate, _shutdown.Token);
      ApplyDiscoveredPorts(discovered);
      bool identityChanged = ApplyEvalBoardSerialHints();
      RefreshAssignments();

      // The early live-Kraken guard above makes this branch unreachable
      // while a Kraken owns hardware. Before erection, probe only eligible
      // FTDI endpoints that are not reserved by another controller.
      IEnumerable<SerialPortViewModel> candidates =
          Ports.Where(port => port.ShouldProbeNode708 && !IsPortKrakenOwned(port.PortName));
      if (!probeAllFtdiPorts)
      {
        candidates = candidates.Where(port =>
            ActiveProbeNewPortsEnabled &&
            !_probedThisSession.Contains(port.StableId));
      }

      foreach (SerialPortViewModel candidate in candidates.ToArray())
      {
        _shutdown.Token.ThrowIfCancellationRequested();
        await ProbePortAsync(candidate);
      }

      LastScanText = $"Last scan: {DateTime.Now:T}";
      int verified = Ports.Count(port => port.IsGa144Node708);
      int identified = Ports.Count(port => port.HasEvalBoardHint);
      int connectedBoards = Boards.Count(board => board.IsConnected);
      StatusText = $"{Ports.Count} serial interface(s), {connectedBoards} board(s) connected, {verified} verified node-708 endpoint(s), {identified} EVB identity hint(s)";
      if (identityChanged)
      {
        MarkWorkspaceDirty();
      }

      await UpdateLastSeenBindingsAsync();
    }
    catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
    {
      StatusText = "Stopping";
    }
    catch (Exception exception)
    {
      StatusText = $"Scan failed: {exception.Message}";
    }
    finally
    {
      IsBusy = false;
      _scanInProgress = false;
    }
  }

  private void ApplyDiscoveredPorts(IReadOnlyList<SerialPortInfo> discovered)
  {
    var existing = Ports.ToDictionary(port => port.PortName, StringComparer.OrdinalIgnoreCase);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (SerialPortInfo port in discovered)
    {
      seen.Add(port.PortName);
      if (existing.TryGetValue(port.PortName, out SerialPortViewModel? current))
      {
        current.UpdatePort(port);
      }
      else
      {
        Ports.Add(new SerialPortViewModel(port));
      }
    }

    for (int index = Ports.Count - 1; index >= 0; index--)
    {
      if (seen.Contains(Ports[index].PortName))
      {
        continue;
      }

      if (ReferenceEquals(SelectedPort, Ports[index]))
      {
        SelectedPort = null;
      }

      Ports.RemoveAt(index);
    }

    SortPorts();
  }

  private bool ApplyEvalBoardSerialHints()
  {
    bool changed = false;
    IEnumerable<IGrouping<string, SerialPortViewModel>> boardGroups = Ports
        .Where(port => port.EvalBoardHint is not null)
        .GroupBy(port => port.EvalBoardHint!.BoardKey, StringComparer.OrdinalIgnoreCase);

    foreach (IGrouping<string, SerialPortViewModel> group in boardGroups)
    {
      SerialPortViewModel firstPort = group.First();
      EvalBoardSerialHint hint = firstPort.EvalBoardHint!;
      BoardViewModel board = FindBoardForHint(hint)
                             ?? AdoptOrCreateBoardForHint(hint);

      if (board.BoardModel != hint.Model)
      {
        board.BoardModel = hint.Model;
        changed = true;
      }

      if (!string.Equals(
              board.SerialNumber,
              hint.BoardSerialNumber,
              StringComparison.OrdinalIgnoreCase))
      {
        board.SerialNumber = hint.BoardSerialNumber;
        changed = true;
      }

      if (IsGenericBoardName(board.Name))
      {
        board.Name = $"{hint.Model} S{hint.BoardSerialNumber}";
        changed = true;
      }

      foreach (SerialPortViewModel port in group)
      {
        EvalBoardSerialHint portHint = port.EvalBoardHint!;
        changed |= ApplyHintBinding(board, port, portHint.PortRole);
      }

      board.Model.LastSeenUtc = DateTimeOffset.UtcNow;
    }

    UpdateBoardConnectionStates();
    return changed;
  }

  private BoardViewModel? FindBoardForHint(EvalBoardSerialHint hint)
  {
    return Boards.FirstOrDefault(board =>
        board.BoardModel == hint.Model &&
        string.Equals(
            board.SerialNumber,
            hint.BoardSerialNumber,
            StringComparison.OrdinalIgnoreCase));
  }

  private BoardViewModel AdoptOrCreateBoardForHint(EvalBoardSerialHint hint)
  {
    BoardViewModel? selectedBoard = SelectedBoard;
    BoardViewModel? adoptable = selectedBoard is not null && CanAdoptIdentity(selectedBoard)
        ? selectedBoard
        : Boards.FirstOrDefault(CanAdoptIdentity);

    if (adoptable is not null)
    {
      return adoptable;
    }

    Ga144Board model = Ga144Board.Create(
        $"{hint.Model} S{hint.BoardSerialNumber}",
        hint.Model);
    model.SerialNumber = hint.BoardSerialNumber;
    model.LastSeenUtc = DateTimeOffset.UtcNow;
    _workspace.Boards.Add(model);

    var board = new BoardViewModel(model, MarkWorkspaceDirty);
    Boards.Add(board);
    NotifyCommandStates();
    return board;
  }

  private static bool IsGenericBoardName(string name) =>
      name.StartsWith("Eval Board", StringComparison.OrdinalIgnoreCase) ||
      string.Equals(name, "GA144 Evalboard", StringComparison.OrdinalIgnoreCase);

  private static bool CanAdoptIdentity(BoardViewModel board)
  {
    Ga144Board model = board.Model;
    return string.IsNullOrWhiteSpace(model.SerialNumber) &&
           model.PortA is null &&
           model.PortB is null &&
           model.PortC is null;
  }

  private bool ApplyHintBinding(
      BoardViewModel board,
      SerialPortViewModel port,
      EvalBoardPortRole role)
  {
    BoardPortBinding? current = GetBinding(board.Model, role);
    if (current?.Ftdi.Matches(port.Port) == true)
    {
      bool changed = false;
      if (!string.Equals(current.LastKnownComPort, port.PortName, StringComparison.OrdinalIgnoreCase))
      {
        current.LastKnownComPort = port.PortName;
        changed = true;
      }

      current.LastSeenUtc = DateTimeOffset.UtcNow;
      return changed;
    }

    RemoveIdentityFromAllBindings(port.Port);
    BoardPortBinding binding = new()
    {
      Role = role,
      Ftdi = FtdiIdentity.FromPort(port.Port),
      LastKnownComPort = port.PortName,
      LastSeenUtc = DateTimeOffset.UtcNow
    };

    SetBinding(board.Model, role, binding);
    board.RefreshBindings();
    return true;
  }

  private static BoardPortBinding? GetBinding(
      Ga144Board board,
      EvalBoardPortRole role)
  {
    return role switch
    {
      EvalBoardPortRole.PortAHost => board.PortA,
      EvalBoardPortRole.PortBGeneral => board.PortB,
      EvalBoardPortRole.PortCTarget => board.PortC,
      _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported evalboard port role.")
    };
  }

  private static void SetBinding(
      Ga144Board board,
      EvalBoardPortRole role,
      BoardPortBinding binding)
  {
    switch (role)
    {
      case EvalBoardPortRole.PortAHost:
        board.PortA = binding;
        break;
      case EvalBoardPortRole.PortBGeneral:
        board.PortB = binding;
        break;
      case EvalBoardPortRole.PortCTarget:
        board.PortC = binding;
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported evalboard port role.");
    }
  }

  private async Task ProbeSelectedAsync()
  {
    if (SelectedPort is not null)
    {
      await ProbePortAsync(SelectedPort);
      RefreshAssignments();
    }
  }

  private async Task ProbePortAsync(SerialPortViewModel port)
  {
    if (HasAnyLiveKraken())
    {
      port.ProbeMessage = "Active probe suppressed while a resident Kraken reserves this serial endpoint";
      StatusText = "Resident Kraken active — serial/PnP discovery and node-708 probing are frozen";
      return;
    }

    if (IsPortKrakenOwned(port.PortName))
    {
      port.ProbeMessage = "Reserved by resident Kraken — reset/probe forbidden; COM handle is parked while idle";
      StatusText = $"{port.PortName} is exclusively owned by a live Kraken session";
      return;
    }

    if (!port.ShouldProbeNode708)
    {
      port.RestoreIdentityStateWhenUnmapped();
      return;
    }

    port.ProbeState = ProbeState.Probing;
    port.ProbeMessage = "Resetting and probing node 708...";
    StatusText = $"Probing {port.PortName}";

    // The verified standalone detector uses 921600 baud. Do not allow an
    // older workspace value to silently change the active probe protocol.
    ProbeResult result = await _probe.ProbeAsync(
        port.PortName,
        Ga144Node708Probe.DefaultBaudRate,
        _shutdown.Token);

    _probedThisSession.Add(port.StableId);
    port.IsGa144Node708 = result.Detected;
    port.ProbeState = result.Detected
        ? ProbeState.Detected
        : result.Exception is null ? ProbeState.NoResponse : ProbeState.Error;
    string identity = port.EvalBoardHint is null
        ? string.Empty
        : $"; FTDI identity: {port.EvalBoardHint.DisplayText}";
    port.ProbeMessage = $"{result.Message}{identity} ({result.Elapsed.TotalMilliseconds:F0} ms)";
  }

  private async Task AssignAsync(EvalBoardPortRole role)
  {
    BoardViewModel? board = SelectedBoard;
    SerialPortViewModel? port = SelectedPort;
    if (board is null || port is null)
    {
      return;
    }

    // Erection is transient: changing this role's port binding invalidates the
    // affected chip's erection (Port A -> host, Port C -> target) and closes its
    // COM handle. The chip is not reset here; the next Kraken operation will
    // erect again. Only the affected role is invalidated.
    if (ChipRoleForPortRole(role) is Ga144ChipRole chipRole)
    {
      await ResetTransientErectionForRoleAsync(board.Id, chipRole);
    }

    RemoveIdentityFromAllBindings(port.Port);
    BoardPortBinding binding = new()
    {
      Role = role,
      Ftdi = FtdiIdentity.FromPort(port.Port),
      LastKnownComPort = port.PortName,
      LastSeenUtc = DateTimeOffset.UtcNow
    };

    SetBinding(board.Model, role, binding);
    board.Model.LastSeenUtc = DateTimeOffset.UtcNow;
    board.RefreshBindings();
    MarkWorkspaceDirty();
    RefreshAssignments();
    await SaveWorkspaceAsync();
    StatusText = $"Assigned {port.PortName} to {board.Name} {FormatRole(role)}";
  }

  private async Task ForgetAssignmentAsync()
  {
    if (SelectedPort is null)
    {
      return;
    }

    bool removed = RemoveIdentityFromAllBindings(SelectedPort.Port);
    if (removed)
    {
      MarkWorkspaceDirty();
      RefreshAssignments();
      await SaveWorkspaceAsync();
      StatusText = $"Forgot assignment for {SelectedPort.PortName}";
    }
  }

  private bool RemoveIdentityFromAllBindings(SerialPortInfo port)
  {
    bool removed = false;
    foreach (BoardViewModel board in Boards)
    {
      if (board.Model.PortA?.Ftdi.Matches(port) == true)
      {
        board.Model.PortA = null;
        removed = true;
      }

      if (board.Model.PortB?.Ftdi.Matches(port) == true)
      {
        board.Model.PortB = null;
        removed = true;
      }

      if (board.Model.PortC?.Ftdi.Matches(port) == true)
      {
        board.Model.PortC = null;
        removed = true;
      }

      board.RefreshBindings();
    }

    return removed;
  }

  private void RefreshAssignments()
  {
    foreach (SerialPortViewModel port in Ports)
    {
      (Ga144Board? board, EvalBoardPortRole? role) = FindBinding(port.Port);
      if (board is not null && role is not null)
      {
        port.AssignmentText = $"{board.Name} — {FormatRole(role.Value)}";
        if (port.ProbeState == ProbeState.NotProbed)
        {
          port.ProbeState = ProbeState.Mapped;
          port.ProbeMessage = "Matched stored FTDI identity";
        }
      }
      else
      {
        port.AssignmentText = port.IsGa144Node708 ? "GA144 endpoint — unassigned" : "Unassigned";
        if (port.ProbeState == ProbeState.Mapped)
        {
          port.RestoreIdentityStateWhenUnmapped();
        }
      }
    }

    foreach (BoardViewModel board in Boards)
    {
      board.RefreshBindings();
    }

    UpdateBoardConnectionStates();
  }

  private (Ga144Board? Board, EvalBoardPortRole? Role) FindBinding(SerialPortInfo port)
  {
    foreach (Ga144Board board in _workspace.Boards)
    {
      if (board.PortA?.Ftdi.Matches(port) == true)
      {
        return (board, EvalBoardPortRole.PortAHost);
      }

      if (board.PortB?.Ftdi.Matches(port) == true)
      {
        return (board, EvalBoardPortRole.PortBGeneral);
      }

      if (board.PortC?.Ftdi.Matches(port) == true)
      {
        return (board, EvalBoardPortRole.PortCTarget);
      }
    }

    return (null, null);
  }

  private bool IsMapped(SerialPortInfo port) => FindBinding(port).Board is not null;

  private void UpdateBoardConnectionStates()
  {
    SerialPortInfo[] connected = Ports.Select(port => port.Port).ToArray();
    foreach (BoardViewModel board in Boards)
    {
      board.UpdateConnectionState(connected);
    }
  }

  private async Task UpdateLastSeenBindingsAsync()
  {
    bool changed = false;
    foreach (SerialPortViewModel port in Ports)
    {
      (Ga144Board? board, EvalBoardPortRole? role) = FindBinding(port.Port);
      BoardPortBinding? binding = role switch
      {
        EvalBoardPortRole.PortAHost => board?.PortA,
        EvalBoardPortRole.PortBGeneral => board?.PortB,
        EvalBoardPortRole.PortCTarget => board?.PortC,
        _ => null
      };

      if (binding is null || board is null)
      {
        continue;
      }

      if (!string.Equals(binding.LastKnownComPort, port.PortName, StringComparison.OrdinalIgnoreCase))
      {
        binding.LastKnownComPort = port.PortName;
        changed = true;
      }

      DateTimeOffset now = DateTimeOffset.UtcNow;
      binding.LastSeenUtc = now;
      board.LastSeenUtc = now;
    }

    if (changed)
    {
      MarkWorkspaceDirty();
      await SaveWorkspaceAsync();
    }
  }

  private void AddBoard()
  {
    EvalBoardModel model = SelectedBoard?.BoardModel ?? EvalBoardModel.EVB002;
    Ga144Board board = Ga144Board.Create($"Eval Board {Boards.Count + 1}", model);
    _workspace.Boards.Add(board);
    BoardViewModel viewModel = new(board, MarkWorkspaceDirty);
    Boards.Add(viewModel);
    SelectedBoard = viewModel;
    MarkWorkspaceDirty();
    StatusText = $"Created {board.Name}";
  }

  private async Task RemoveSelectedBoardAsync()
  {
    BoardViewModel? selectedBoard = SelectedBoard;
    if (selectedBoard is null || Boards.Count <= 1)
    {
      return;
    }

    BoardViewModel removed = selectedBoard;
    int index = Boards.IndexOf(removed);
    Boards.Remove(removed);
    _workspace.Boards.Remove(removed.Model);
    SelectedBoard = Boards[Math.Clamp(index - 1, 0, Boards.Count - 1)];
    MarkWorkspaceDirty();
    RefreshAssignments();
    await SaveWorkspaceAsync();
    StatusText = $"Removed {removed.Name}";
  }

  private void AddProject()
  {
    Ga144Project project = Ga144Project.Create($"GA144 Project {Projects.Count + 1}");
    _workspace.Projects.Add(project);
    ProjectViewModel viewModel = new(project, MarkWorkspaceDirty);
    Projects.Add(viewModel);
    SelectedProject = viewModel;
    MarkWorkspaceDirty();
    StatusText = $"Created {project.Name}";
  }

  private async Task RemoveSelectedProjectAsync()
  {
    ProjectViewModel? selectedProject = SelectedProject;
    if (selectedProject is null || Projects.Count <= 1)
    {
      return;
    }

    ProjectViewModel removed = selectedProject;
    int index = Projects.IndexOf(removed);
    Projects.Remove(removed);
    _workspace.Projects.Remove(removed.Model);
    SelectedProject = Projects[Math.Clamp(index - 1, 0, Projects.Count - 1)];
    MarkWorkspaceDirty();
    await SaveWorkspaceAsync();
    StatusText = $"Removed {removed.Name}";
  }

  private void MarkWorkspaceDirty()
  {
    _workspaceDirty = true;
    long revision = Interlocked.Increment(ref _workspaceRevision);

    _saveDebounce?.Cancel();
    _saveDebounce?.Dispose();
    _saveDebounce = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
    CancellationToken token = _saveDebounce.Token;

    _ = Task.Run(async () =>
    {
      try
      {
        await Task.Delay(700, token);
        Task saveTask = await Application.Current.Dispatcher.InvokeAsync(() =>
            revision == _workspaceRevision && _workspaceDirty && !IsBusy
                ? SaveWorkspaceAsync()
                : Task.CompletedTask);
        await saveTask;
      }
      catch (OperationCanceledException)
      {
        // A later edit restarted the debounce timer.
      }
    }, token);
  }

  private async Task SaveWorkspaceAsync()
  {
    if (_shutdown.IsCancellationRequested)
    {
      return;
    }

    long revision = _workspaceRevision;
    try
    {
      StatusText = "Saving YAML workspace...";
      await _configurationStore.SaveAsync(_workspace, _shutdown.Token);
      if (revision == _workspaceRevision)
      {
        _workspaceDirty = false;
      }

      StatusText = $"Saved {System.IO.Path.GetFileName(ConfigurationPath)}";
    }
    catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
    {
      // Shutdown owns cancellation.
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      StatusText = $"Save failed: {exception.Message}";
    }
  }

  private void OpenConfigurationFolder()
  {
    string? directory = System.IO.Path.GetDirectoryName(ConfigurationPath);
    if (string.IsNullOrWhiteSpace(directory))
    {
      return;
    }

    Directory.CreateDirectory(directory);
    Process.Start(new ProcessStartInfo
    {
      FileName = directory,
      UseShellExecute = true
    });
  }

  private void SortPorts()
  {
    List<SerialPortViewModel> sorted = Ports
        .OrderBy(port => PortNumber(port.PortName))
        .ThenBy(port => port.PortName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    for (int target = 0; target < sorted.Count; target++)
    {
      int current = Ports.IndexOf(sorted[target]);
      if (current != target)
      {
        Ports.Move(current, target);
      }
    }
  }

  private static int PortNumber(string portName)
  {
    return portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
           int.TryParse(portName.AsSpan(3), out int number)
        ? number
        : int.MaxValue;
  }

  private bool HasAnyLiveKraken() =>
      _krakenControllers.Values.Any(controller => controller.HasExclusiveSerialOwnership);

  private bool HasActiveKrakenForBoard(Guid boardId) =>
      _krakenControllers.Any(item => item.Key.BoardId == boardId && item.Value.HasExclusiveSerialOwnership);

  private bool HasActiveKrakenForProject(Guid projectId) =>
      _krakenControllers.Any(item => item.Key.ProjectId == projectId && item.Value.HasExclusiveSerialOwnership);

  private bool IsBoardRoleKrakenOwned(Guid boardId, EvalBoardPortRole role)
  {
    Ga144ChipRole? chipRole = role switch
    {
      EvalBoardPortRole.PortAHost => Ga144ChipRole.Host,
      EvalBoardPortRole.PortCTarget => Ga144ChipRole.Target,
      _ => null
    };

    return chipRole is Ga144ChipRole actualRole &&
           _krakenControllers.Any(item =>
               item.Key.BoardId == boardId &&
               item.Key.Role == actualRole &&
               item.Value.HasExclusiveSerialOwnership);
  }

  private bool CanOperateOnSelectedPort() =>
      !IsBusy && SelectedPort is not null && !IsPortKrakenOwned(SelectedPort.PortName);

  private bool CanProbeSelectedPort() =>
      !IsBusy && !HasAnyLiveKraken() && SelectedPort?.ShouldProbeNode708 == true && !IsPortKrakenOwned(SelectedPort.PortName);

  private bool CanAssignSelectedPort() =>
      !IsBusy && SelectedBoard is not null && SelectedPort is not null && SelectedPort.IsFtdi &&
      !IsPortKrakenOwned(SelectedPort.PortName);

  private void NotifyCommandStates()
  {
    ScanNowCommand.NotifyCanExecuteChanged();
    ProbeSelectedPortCommand.NotifyCanExecuteChanged();
    SaveWorkspaceCommand.NotifyCanExecuteChanged();
    AssignPortACommand.NotifyCanExecuteChanged();
    AssignPortBCommand.NotifyCanExecuteChanged();
    AssignPortCCommand.NotifyCanExecuteChanged();
    ForgetAssignmentCommand.NotifyCanExecuteChanged();
    AddBoardCommand.NotifyCanExecuteChanged();
    RemoveBoardCommand.NotifyCanExecuteChanged();
    AddProjectCommand.NotifyCanExecuteChanged();
    RemoveProjectCommand.NotifyCanExecuteChanged();
  }

  private void OnKrakenControllerStateChanged(object? sender, EventArgs e)
  {
    void Refresh()
    {
      // A completed Kraken erection permanently freezes serial discovery
      // for this IDE process. No device-change scan or manual Scan command may
      // enumerate COM/FTDI devices while the persistent handle is owned
      // (RequestDeviceChangeScan and ScanAsync both check HasAnyLiveKraken).
      NotifyCommandStates();
      if (sender is KrakenLiveController controller && controller.CurrentEndpoint is KrakenEndpointInfo endpoint)
      {
        SerialPortViewModel? port = Ports.FirstOrDefault(item =>
            string.Equals(item.PortName, endpoint.PortName, StringComparison.OrdinalIgnoreCase));
        if (port is not null && controller.HasExclusiveSerialOwnership)
        {
          LastScanText = "Serial discovery frozen: live Kraken";
          port.ProbeMessage = controller.TransportFaulted
              ? "LIVE KRAKEN RESERVED — COM parked while idle; transport fault; reset/probe/re-erection forbidden"
              : "LIVE KRAKEN RESERVED — COM parked while idle; serial/PnP discovery frozen";
        }
      }
    }

    Dispatcher? dispatcher = Application.Current?.Dispatcher;
    if (dispatcher is not null && !dispatcher.CheckAccess())
    {
      dispatcher.BeginInvoke(Refresh);
    }
    else
    {
      Refresh();
    }
  }

  private static string FormatRole(EvalBoardPortRole role) => role switch
  {
    EvalBoardPortRole.PortAHost => "USB A / Host",
    EvalBoardPortRole.PortBGeneral => "USB B / Host serial",
    EvalBoardPortRole.PortCTarget => "USB C / Target",
    _ => role.ToString()
  };
}