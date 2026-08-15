using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;
using System.Collections.ObjectModel;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class KrakenNodeControlViewModel : ObservableObject, IAsyncDisposable
{
  private readonly KrakenNodeRoute _route;
  private readonly KrakenLiveController _controller;
  private readonly Func<IReadOnlyList<int>?>? _compileGeneratedRom;
  private readonly Func<string?>? _compileExpandedRomSource;
  private readonly CancellationTokenSource _shutdown = new();
  private bool _isConnected;
  private bool _isBusy;
  private string _statusText = "Offline. Erect Kraken once to take persistent online control of this chip.";
  private string _endpointText = "No live board endpoint resolved yet.";
  private string _aValue = "0x00000";
  private string _ioValue = "0x15555";
  private string _bValue;
  private string _jumpValue = "0x000";

  public KrakenNodeControlViewModel(
      KrakenNodeRoute route,
      KrakenLiveController controller,
      Func<IReadOnlyList<int>?>? compileGeneratedRom = null,
      Func<string?>? compileExpandedRomSource = null)
  {
    _route = route;
    _controller = controller;
    _compileGeneratedRom = compileGeneratedRom;
    _compileExpandedRomSource = compileExpandedRomSource;
    int ioAddress = F18InstructionSet.Constants["io"];
    _bValue = $"0x{(route.OutgoingBAddress ?? ioAddress):X3}";
    if (_controller.IsOperational)
    {
      _isConnected = true;
      _statusText = $"Online through the already erected Kraken. Node {route.Coordinate:000} is immediately available.";
      _endpointText = FormatEndpoint(_controller.CurrentEndpoint);
    }
    else if (_controller.HardwareErected)
    {
      _statusText = _controller.TransportFaulted
          ? "Kraken remains resident and its COM endpoint is reserved, but the transport/topology is faulted. No reset/re-erection will be attempted."
          : "Kraken hardware is resident, but the host transport is not usable. No reset/re-erection will be attempted while Kraken is running.";
    }

    for (int index = 0; index < 64; index++)
    {
      RamWords.Add(new KrakenWordCellViewModel(index.ToString("00"), index));
      RomWords.Add(new KrakenWordCellViewModel(index.ToString("00"), 0x080 + index, isReadOnly: true));
    }

    for (int index = 0; index < 10; index++)
    {
      ParameterStack.Add(new KrakenWordCellViewModel(index == 0 ? "0 (bottom)" : index == 9 ? "9 (top)" : index.ToString()));
    }

    for (int index = 0; index < 9; index++)
    {
      ReturnStack.Add(new KrakenWordCellViewModel(index == 0 ? "0 (bottom)" : index == 8 ? "8 (top / R)" : index.ToString()));
    }

    ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy && !IsConnected && !_controller.HardwareErected);
    RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync, CanOperate);
    ReadRamCommand = new AsyncRelayCommand(ReadRamAsync, CanOperate);
    WriteRamCommand = new AsyncRelayCommand(WriteRamAsync, CanOperate);
    ReadRomCommand = new AsyncRelayCommand(ReadRomAsync, CanOperate);
    VerifyRomCommand = new AsyncRelayCommand(VerifyRomAsync, CanVerifyRom);
    ReadACommand = new AsyncRelayCommand(ReadAAsync, CanOperate);
    WriteACommand = new AsyncRelayCommand(WriteAAsync, CanOperate);
    ReadIoCommand = new AsyncRelayCommand(ReadIoAsync, CanOperate);
    WriteIoCommand = new AsyncRelayCommand(WriteIoAsync, CanOperate);
    ReadParameterStackCommand = new AsyncRelayCommand(ReadParameterStackAsync, CanOperate);
    WriteParameterStackCommand = new AsyncRelayCommand(WriteParameterStackAsync, CanOperate);
    ReadReturnStackCommand = new AsyncRelayCommand(ReadReturnStackAsync, CanOperate);
    WriteReturnStackCommand = new AsyncRelayCommand(WriteReturnStackAsync, CanOperate);
    WriteBCommand = new AsyncRelayCommand(WriteBAsync, CanOperate);
    JumpCommand = new AsyncRelayCommand(JumpAsync, CanOperate);
  }

  public string NodeCoordinate => _route.Coordinate.ToString("000");
  public string RouteText => $"T{_route.TentacleNumber} / {_route.TentacleName}, position {_route.Position:00}; input {_route.IncomingPort}; output {_route.OutgoingPort}.";
  public string KnownPText => _route.PreviousCoordinate is int previous
      ? $"0x{KrakenTopology.PortAddress(_route.Coordinate, previous):X3}  (known Kraken focus port from node {previous:000})"
      : "Unavailable";
  public string BReadLimitation => "B is write-only in the F18A. The value below is the configured/expected Kraken B value, not a hardware readback.";
  public string ILimitation => "I (instruction register) has no direct read instruction. It is not faked by the online monitor.";
  public string DestructiveWarning => "Kraken is erected at most once. After erection the COM endpoint remains exclusively reserved, but its native handle is closed while idle and reopened only for explicit Kraken operations. Reopening does not intentionally reset, probe node 708, reload the helper, or re-erect the tentacles. Controlled node RAM/ROM is not consumed by Kraken.";
  public ObservableCollection<KrakenWordCellViewModel> RamWords { get; } = [];
  public ObservableCollection<KrakenWordCellViewModel> RomWords { get; } = [];
  public ObservableCollection<KrakenWordCellViewModel> ParameterStack { get; } = [];
  public ObservableCollection<KrakenWordCellViewModel> ReturnStack { get; } = [];

  public bool IsConnected
  {
    get => _isConnected;
    private set
    {
      if (SetProperty(ref _isConnected, value))
      {
        OnPropertyChanged(nameof(ConnectionText));
        NotifyCommandStates();
      }
    }
  }

  public bool IsBusy
  {
    get => _isBusy;
    private set
    {
      if (SetProperty(ref _isBusy, value))
      {
        NotifyCommandStates();
      }
    }
  }

  public string ConnectionText => IsConnected ? "ONLINE" : "OFFLINE";
  public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
  public string EndpointText { get => _endpointText; private set => SetProperty(ref _endpointText, value); }
  public string AValue { get => _aValue; set => SetProperty(ref _aValue, value ?? string.Empty); }
  public string IoValue { get => _ioValue; set => SetProperty(ref _ioValue, value ?? string.Empty); }
  public string BValue { get => _bValue; set => SetProperty(ref _bValue, value ?? string.Empty); }
  public string JumpValue { get => _jumpValue; set => SetProperty(ref _jumpValue, value ?? string.Empty); }

  public AsyncRelayCommand ConnectCommand { get; }
  public AsyncRelayCommand RefreshAllCommand { get; }
  public AsyncRelayCommand ReadRamCommand { get; }
  public AsyncRelayCommand WriteRamCommand { get; }
  public AsyncRelayCommand ReadRomCommand { get; }
  public AsyncRelayCommand VerifyRomCommand { get; }
  public AsyncRelayCommand ReadACommand { get; }
  public AsyncRelayCommand WriteACommand { get; }
  public AsyncRelayCommand ReadIoCommand { get; }
  public AsyncRelayCommand WriteIoCommand { get; }
  public AsyncRelayCommand ReadParameterStackCommand { get; }
  public AsyncRelayCommand WriteParameterStackCommand { get; }
  public AsyncRelayCommand ReadReturnStackCommand { get; }
  public AsyncRelayCommand WriteReturnStackCommand { get; }
  public AsyncRelayCommand WriteBCommand { get; }
  public AsyncRelayCommand JumpCommand { get; }

  public ValueTask DisposeAsync()
  {
    _shutdown.Cancel();
    IsConnected = false;
    return ValueTask.CompletedTask;
  }

  public Task InitializeAsync()
  {
    if (_controller.IsOperational)
    {
      IsConnected = true;
      EndpointText = FormatEndpoint(_controller.CurrentEndpoint);
      StatusText = $"Online through the resident Kraken. Node {NodeCoordinate} is immediately available; the COM handle is parked while idle and opened only for explicit operations.";
      return Task.CompletedTask;
    }

    IsConnected = false;
    if (_controller.HardwareErected)
    {
      EndpointText = FormatEndpoint(_controller.CurrentEndpoint);
      StatusText = _controller.TransportFaulted
          ? "Kraken is still resident and its COM endpoint remains exclusively reserved, but online transactions are blocked after a fault: " + (_controller.FaultText ?? "unknown fault")
          : "Kraken is resident, but the host transport is no longer usable. Automatic reset/re-erection is forbidden.";
    }
    else
    {
      StatusText = "Offline. No hardware Kraken has been erected yet; use Connect / erect Kraken or Check Kraken once.";
    }

    return Task.CompletedTask;
  }

  private bool CanOperate() => !IsBusy && IsConnected && _controller.IsOperational;

  private async Task ConnectAsync()
  {
    IsBusy = true;
    try
    {
      bool resetPerformed = await _controller.EnsureOnlineAsync(
          _route,
          verifyTarget: true,
          allowErect: true,
          _shutdown.Token);

      EndpointText = FormatEndpoint(_controller.CurrentEndpoint);
      IsConnected = true;
      StatusText = resetPerformed
          ? $"Online control established for node {NodeCoordinate}; Kraken was erected once and the COM handle is now parked whenever no operation is active."
          : $"Online control established for node {NodeCoordinate} through the resident Kraken; COM is opened only for each explicit operation.";
    }
    catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
    {
      StatusText = "Kraken connection cancelled.";
    }
    catch (Exception exception)
    {
      IsConnected = false;
      StatusText = "Kraken connection failed: " + exception.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }


  private async Task RefreshAllAsync()
  {
    await RunAsync("Refreshing A, IO, RAM, ROM and both stacks", async () =>
    {
      AValue = Format(await _controller.ReadAAsync(_route, _shutdown.Token));
      IoValue = Format(await _controller.ReadIoAsync(_route, _shutdown.Token));
      Load(RamWords, await _controller.ReadRamAsync(_route, _shutdown.Token));
      Load(RomWords, await _controller.ReadRomAsync(_route, _shutdown.Token));
      Load(ParameterStack, await _controller.ReadParameterStackAsync(_route, _shutdown.Token));
      Load(ReturnStack, await _controller.ReadReturnStackAsync(_route, _shutdown.Token));
    });
  }

  private Task ReadRamAsync() => RunAsync("Reading 64 RAM words", async () => Load(RamWords, await _controller.ReadRamAsync(_route, _shutdown.Token)));

  private Task WriteRamAsync() => RunAsync("Writing 64 RAM words", async () =>
  {
    int[] values = ParseCells(RamWords, "RAM");
    await _controller.WriteRamAsync(_route, values, _shutdown.Token);
  });

  private Task ReadRomAsync() => RunAsync("Reading 64 ROM words", async () => Load(RomWords, await _controller.ReadRomAsync(_route, _shutdown.Token)));

  private bool CanVerifyRom() => CanOperate() && _compileGeneratedRom is not null;

  private Task VerifyRomAsync() => RunAsync("Verifying ROM against chip", async () =>
  {
    IReadOnlyList<int>? generated = _compileGeneratedRom?.Invoke();
    if (generated is null)
    {
      System.Windows.MessageBox.Show(
          "The generated ROM could not be compiled for this node, so it cannot be compared. Fix the ROM source and try again.",
          "Verify ROM",
          System.Windows.MessageBoxButton.OK,
          System.Windows.MessageBoxImage.Warning);
      return;
    }

    IReadOnlyList<int> onChip = await _controller.ReadRomAsync(_route, _shutdown.Token);
    Load(RomWords, onChip);

    var comparison = RomComparison.Compare(_route.Coordinate, generated, onChip);
    if (comparison.IsMatch)
    {
      System.Windows.MessageBox.Show(
          $"Node {_route.Coordinate:000}: the generated ROM matches the chip ({comparison.ComparedWordCount}/{RomComparison.RomWordCount} words).",
          "Verify ROM",
          System.Windows.MessageBoxButton.OK,
          System.Windows.MessageBoxImage.Information);
      return;
    }

    // Single-node verify: show the mismatch list with only "Close" (no Abort).
    var dialog = new Views.RomMismatchDialog(
        comparison,
        showAbort: false,
        expandedRomSource: _compileExpandedRomSource?.Invoke())
    {
      Owner = System.Windows.Application.Current?.Windows
          .OfType<System.Windows.Window>()
          .FirstOrDefault(window => window.IsActive)
    };
    dialog.ContinueButton.Content = "Close";
    dialog.ShowDialog();
  });

  private Task ReadAAsync() => RunAsync("Reading A", async () => AValue = Format(await _controller.ReadAAsync(_route, _shutdown.Token)));

  private Task WriteAAsync() => RunAsync("Writing A", async () => await _controller.WriteAAsync(_route, ParseWord(AValue, "A"), _shutdown.Token));

  private Task ReadIoAsync() => RunAsync("Reading IO register", async () => IoValue = Format(await _controller.ReadIoAsync(_route, _shutdown.Token)));

  private Task WriteIoAsync() => RunAsync("Writing IO register", async () => await _controller.WriteIoAsync(_route, ParseWord(IoValue, "IO"), _shutdown.Token));

  private Task ReadParameterStackAsync() => RunAsync("Reading and restoring parameter stack", async () => Load(ParameterStack, await _controller.ReadParameterStackAsync(_route, _shutdown.Token)));

  private Task WriteParameterStackAsync() => RunAsync("Writing parameter stack", async () => await _controller.WriteParameterStackAsync(_route, ParseCells(ParameterStack, "parameter stack"), _shutdown.Token));

  private Task ReadReturnStackAsync() => RunAsync("Reading and restoring return stack", async () => Load(ReturnStack, await _controller.ReadReturnStackAsync(_route, _shutdown.Token)));

  private Task WriteReturnStackAsync() => RunAsync("Writing return stack", async () => await _controller.WriteReturnStackAsync(_route, ParseCells(ReturnStack, "return stack"), _shutdown.Token));

  private Task WriteBAsync() => RunAsync("Writing B", async () =>
  {
    int value = ParseBAddress(BValue);
    await _controller.WriteBAsync(_route, value, _shutdown.Token);
    int expected = _route.OutgoingBAddress ?? F18InstructionSet.Constants["io"];
    if (value != expected)
    {
      await _controller.MarkTopologyAlteredAsync(
            $"Node {NodeCoordinate} B changed from the configured Kraken route 0x{expected:X3} to 0x{value:X3}.",
            _shutdown.Token);
      IsConnected = false;
    }
  });

  private async Task JumpAsync()
  {
    if (!IsConnected || !_controller.IsOperational)
    {
      StatusText = _controller.HardwareErected ? "Kraken serial remains reserved, but the live topology is not operational. No reset/re-erection will be attempted." : "The Kraken session is offline.";
      return;
    }

    int destination;
    try
    {
      destination = ParseAddress(JumpValue, "P jump address");
    }
    catch (Exception exception) when (exception is FormatException or OverflowException)
    {
      StatusText = "Jump not sent: " + exception.Message;
      return;
    }

    IsBusy = true;
    StatusText = $"Jumping node {NodeCoordinate} to 0x{destination:X3}...";
    try
    {
      await _controller.JumpAsync(_route, destination, _shutdown.Token);
      await _controller.MarkTopologyAlteredAsync(
          $"Node {NodeCoordinate} jumped from its incoming Kraken port to 0x{destination:X3}.",
          _shutdown.Token);
      IsConnected = false;
      StatusText = $"Jump sent to 0x{destination:X3}. The COM endpoint remains reserved, but this Kraken topology is now marked altered. No reset/re-erection will be attempted automatically.";
    }
    catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
    {
      StatusText = "Jump cancelled.";
    }
    catch (Exception exception)
    {
      StatusText = "Jump failed: " + exception.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  private async Task RunAsync(string activity, Func<Task> action)
  {
    if (!IsConnected || !_controller.IsOperational)
    {
      IsConnected = false;
      StatusText = _controller.HardwareErected ? "Kraken serial remains reserved, but the live topology is not operational. No reset/re-erection will be attempted." : "The Kraken session is offline.";
      return;
    }

    IsBusy = true;
    StatusText = activity + "...";
    try
    {
      await action();
      StatusText = activity + " complete.";
    }
    catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
    {
      StatusText = activity + " cancelled.";
    }
    catch (Exception exception)
    {
      StatusText = activity + " failed: " + exception.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  private static string FormatEndpoint(KrakenEndpointInfo? endpoint) => endpoint is null
      ? "Live Kraken endpoint."
      : $"{endpoint.BoardName} — {endpoint.Role} — {endpoint.PortName} @ {KrakenSession.OnlineBaudRate:N0} baud";

  private static void Load(IReadOnlyList<KrakenWordCellViewModel> cells, IReadOnlyList<int> values)
  {
    if (cells.Count != values.Count)
    {
      throw new InvalidOperationException($"Expected {cells.Count} words, received {values.Count}.");
    }

    for (int index = 0; index < cells.Count; index++)
    {
      cells[index].SetValue(values[index]);
    }
  }

  private static int[] ParseCells(IEnumerable<KrakenWordCellViewModel> cells, string description)
  {
    var result = new List<int>();
    foreach (KrakenWordCellViewModel cell in cells)
    {
      if (!cell.TryGetValue(out int value))
      {
        throw new FormatException($"Invalid 18-bit value '{cell.ValueText}' in {description} row {cell.Label}.");
      }

      result.Add(value);
    }

    return result.ToArray();
  }

  private static int ParseWord(string text, string description)
  {
    if (!KrakenWordFormatting.TryParse(text, out int value))
    {
      throw new FormatException($"'{text}' is not a valid 18-bit {description} value.");
    }

    return value;
  }

  private static int ParseAddress(string text, string description)
  {
    int value = ParseWord(text, description);
    if (value > 0x3FF)
    {
      throw new FormatException($"{description} must be a 10-bit address from 0x000 through 0x3FF.");
    }

    return value;
  }

  private static int ParseBAddress(string text)
  {
    int value = ParseWord(text, "B");
    if (value > 0x1FF)
    {
      throw new FormatException("B is a 9-bit address register; use 0x000 through 0x1FF.");
    }

    return value;
  }

  private static string Format(int value) => $"0x{value & F18InstructionSet.WordMask:X5}";

  private void NotifyCommandStates()
  {
    ConnectCommand?.NotifyCanExecuteChanged();
    RefreshAllCommand?.NotifyCanExecuteChanged();
    ReadRamCommand?.NotifyCanExecuteChanged();
    WriteRamCommand?.NotifyCanExecuteChanged();
    ReadRomCommand?.NotifyCanExecuteChanged();
    VerifyRomCommand?.NotifyCanExecuteChanged();
    ReadACommand?.NotifyCanExecuteChanged();
    WriteACommand?.NotifyCanExecuteChanged();
    ReadIoCommand?.NotifyCanExecuteChanged();
    WriteIoCommand?.NotifyCanExecuteChanged();
    ReadParameterStackCommand?.NotifyCanExecuteChanged();
    WriteParameterStackCommand?.NotifyCanExecuteChanged();
    ReadReturnStackCommand?.NotifyCanExecuteChanged();
    WriteReturnStackCommand?.NotifyCanExecuteChanged();
    WriteBCommand?.NotifyCanExecuteChanged();
    JumpCommand?.NotifyCanExecuteChanged();
  }
}