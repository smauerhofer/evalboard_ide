using System.Collections.ObjectModel;
using System.Windows;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class ChipViewModel : ObservableObject, IAsyncDisposable
{
  private bool _disposed;

  public ChipViewModel(
    ProjectViewModel project,
    Ga144ChipRole role,
    Ga144RomLibrary romLibrary,
    string romLibraryPath,
    Func<Task> saveRomLibraryAsync,
    Func<KrakenEndpointInfo?> krakenEndpointResolver,
    KrakenLiveController krakenController)
  {
    Project = project;
    Role = role;
    Chip = project.GetChip(role);
    RomLibrary = romLibrary;
    RomLibraryPath = romLibraryPath;
    SaveRomLibraryAsync = saveRomLibraryAsync;
    KrakenEndpointResolver = krakenEndpointResolver;
    KrakenController = krakenController ?? throw new ArgumentNullException(nameof(krakenController));
    KrakenController.StateChanged += OnKrakenControllerStateChanged;
    ToggleKrakenCommand = new AsyncRelayCommand(ToggleKrakenAsync);
    RebuildNodes();
  }

  public ProjectViewModel Project { get; }
  public Ga144ChipRole Role { get; }
  public Ga144ChipConfiguration Chip { get; }
  public Ga144RomLibrary RomLibrary { get; }
  public string RomLibraryPath { get; }
  public Func<Task> SaveRomLibraryAsync { get; }
  public Func<KrakenEndpointInfo?> KrakenEndpointResolver { get; }
  public KrakenLiveController KrakenController { get; }
  public ObservableCollection<NodeViewModel> Nodes { get; } = [];
  public AsyncRelayCommand ToggleKrakenCommand { get; }
  public string Title => $"{Project.Name} — {Chip.Name}";

  /// <summary>
  /// Runtime truth: a Kraken is erected and running on the connected silicon THIS
  /// session. The IDE only knows this is true after it erects the Kraken itself;
  /// at startup it cannot know what the chip is doing, so this is false until an
  /// erection happens. This is the ONLY notion of "is there a Kraken" — the
  /// structure is a constant and is never a persisted install choice.
  /// </summary>
  public bool KrakenActive => KrakenController.HardwareErected;

  public string KrakenButtonText => KrakenActive ? "Remove Kraken" : "Install Kraken";
  public string KrakenStatusText => KrakenActive
    ? BuildKrakenStatus() + BuildRuntimeStatus()
    : "No Kraken running on this chip. Install to erect the 3-tentacle Kraken (this resets the chip once).";

  public IReadOnlyDictionary<int, KrakenNodeRoute> KrakenRoutes => KrakenTopology.BuildRouteMap(Chip.Kraken);

  public void RefreshNodes() => RebuildNodes();

  public void RefreshKrakenRuntimeStatus()
  {
    OnPropertyChanged(nameof(KrakenStatusText));
    OnPropertyChanged(nameof(KrakenActive));
    OnPropertyChanged(nameof(KrakenButtonText));
    ToggleKrakenCommand.NotifyCanExecuteChanged();
  }

  /// <summary>
  /// Chip windows do not own the Kraken controller. It is process-lifetime
  /// state owned by MainWindowViewModel so closing/reopening this window can
  /// never release the resident Kraken endpoint reservation.
  /// </summary>
  public ValueTask DisposeAsync()
  {
    if (!_disposed)
    {
      _disposed = true;
      KrakenController.StateChanged -= OnKrakenControllerStateChanged;
    }

    return ValueTask.CompletedTask;
  }

  private async Task ToggleKrakenAsync()
  {
    if (KrakenController.HardwareErected)
    {
      // Remove = release the running Kraken: drop erection state and close the
      // COM handle. No chip reset is issued here.
      await KrakenController.ResetTransientErectionAsync();
    }
    else
    {
      // Install = erect the Kraken now. This pulses RESET- and installs the head
      // + tentacles (the one reset intrinsic to bringing a Kraken online). We
      // anchor on the first non-head route of the fixed topology.
      KrakenNodeRoute? anchor = KrakenRoutes.Values
        .Where(route => !route.IsHead)
        .OrderBy(route => route.TentacleNumber)
        .ThenBy(route => route.Position)
        .FirstOrDefault();
      if (anchor is null)
      {
        return;
      }

      await KrakenController.EnsureOnlineAsync(
        anchor,
        verifyTarget: false,
        allowErect: true);
    }

    RebuildNodes();
    OnPropertyChanged(nameof(KrakenButtonText));
    OnPropertyChanged(nameof(KrakenStatusText));
    OnPropertyChanged(nameof(KrakenActive));
  }

  private void RebuildNodes()
  {
    IReadOnlyDictionary<int, KrakenNodeRoute> routes = KrakenTopology.BuildRouteMap(Chip.Kraken);
    bool krakenActive = KrakenController.HardwareErected;
    Nodes.Clear();
    foreach (Ga144NodeConfiguration node in Chip.Nodes
      .OrderByDescending(item => item.Coordinate / 100)
      .ThenBy(item => item.Coordinate % 100))
    {
      routes.TryGetValue(node.Coordinate, out KrakenNodeRoute? route);
      Nodes.Add(new NodeViewModel(node, route, krakenActive));
    }
  }

  private string BuildKrakenStatus()
  {
    string lengths = string.Join(", ", Chip.Kraken.Tentacles
      .OrderBy(item => item.Number)
      .Select(item => $"T{item.Number} {item.Nodes.Count} nodes"));
    int covered = Chip.Kraken.Tentacles.Sum(item => item.Nodes.Count);
    return $"Kraken head 708; {lengths}. {covered}/143 non-head nodes covered.";
  }

  private string BuildRuntimeStatus()
  {
    if (!KrakenController.HardwareErected)
    {
      // The topology is installed in the design, but nothing is running on
      // the silicon as far as the IDE knows. At startup the IDE cannot know
      // what the chip is doing, so it reports inactive until it erects the
      // Kraken itself. This is design state only, not a live Kraken.
      return " Not active: the topology is defined but no Kraken is running this session. Install/erect to bring it online.";
    }

    string endpoint = KrakenController.CurrentEndpoint?.PortName ?? "serial endpoint";
    if (KrakenController.TransportFaulted)
    {
      return $" Hardware Kraken RESERVED on {endpoint}; COM is parked while idle. " +
      $"Transport/topology fault: {KrakenController.FaultText} No reset/probe/re-erection will be attempted.";
    }

    return $" Hardware Kraken ONLINE on {endpoint}; the Win32 COM handle is opened only during explicit Kraken operations and closed/parked while idle; no SerialPort event loop is used. " +
    "Reset, node-708 probing, and re-erection are forbidden while Kraken is resident; the COM endpoint remains reserved.";
  }

  private void OnKrakenControllerStateChanged(object? sender, EventArgs e)
  {
    void Refresh()
    {
      // Erection state affects per-node Kraken colours/labels (built into
      // each NodeViewModel), so rebuild the node list; then notify the
      // runtime-status and active bindings. The view redraws the tentacle
      // arrows off the KrakenActive change.
      RebuildNodes();
      OnPropertyChanged(nameof(KrakenStatusText));
      OnPropertyChanged(nameof(KrakenActive));
      OnPropertyChanged(nameof(KrakenButtonText));
      ToggleKrakenCommand.NotifyCanExecuteChanged();
    }

    if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
    {
      dispatcher.BeginInvoke(Refresh);
    }
    else
    {
      Refresh();
    }
  }
}