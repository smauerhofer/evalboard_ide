using System.Collections.ObjectModel;
using System.Windows;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class ChipViewModel : ObservableObject, IAsyncDisposable
{
  private bool _disposed;
  private bool _verifyBusy;
  private string _verifyStatus = "";

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
    VerifyAllRomsCommand = new AsyncRelayCommand(VerifyAllRomsAsync, () => !_verifyBusy);
    VerifyNode708RomCommand = new AsyncRelayCommand(VerifyNode708RomAsync, () => !_verifyBusy);
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
  public AsyncRelayCommand VerifyAllRomsCommand { get; }
  public AsyncRelayCommand VerifyNode708RomCommand { get; }

  public string VerifyStatus
  {
    get => _verifyStatus;
    private set => SetProperty(ref _verifyStatus, value);
  }

  public bool VerifyBusy
  {
    get => _verifyBusy;
    private set
    {
      if (SetProperty(ref _verifyBusy, value))
      {
        VerifyAllRomsCommand.NotifyCanExecuteChanged();
        VerifyNode708RomCommand.NotifyCanExecuteChanged();
      }
    }
  }
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

      // Erection leaves the COM handle open (there is no follow-up transaction to
      // park it, unlike the check path which scans immediately after). Park it now
      // so the idle policy takes effect: under CloseAfterIdleTimeout this arms the
      // 1 s idle-close timer, so the handle does not stay open for the whole
      // session. Without this the handle would remain open indefinitely and keep
      // the FTDI/VCP driver active on a shared USB controller (continuous mouse
      // hiccups that never stop until the app exits).
      await KrakenController.ParkTransportAsync();
    }

    RebuildNodes();
    OnPropertyChanged(nameof(KrakenButtonText));
    OnPropertyChanged(nameof(KrakenStatusText));
    OnPropertyChanged(nameof(KrakenActive));
  }

  /// <summary>
  /// Verify every tentacle-reachable node's generated ROM against the ROM read
  /// from the live chip via Kraken. Erects once (if needed) and reads all nodes
  /// through the one session inside a keep-open scope, retargeting per node, so
  /// the FTDI handle is not idle-closed/reopened between nodes. Node 708 (the
  /// Kraken head) is skipped and noted; it must be verified later by a separate
  /// direct-boot mechanism. On a node with mismatches, one dialog lists all its
  /// differing words with Continue (skip to next node) and Abort (stop the sweep).
  /// </summary>
  private async Task VerifyAllRomsAsync()
  {
    if (_verifyBusy)
    {
      return;
    }

    VerifyBusy = true;
    try
    {
      // Order the non-head routes breadth-first (tentacle, then position), the
      // same order Check Kraken uses.
      var routes = KrakenRoutes.Values
          .Where(route => !route.IsHead)
          .OrderBy(route => route.TentacleNumber)
          .ThenBy(route => route.Position)
          .ToList();

      if (routes.Count == 0)
      {
        VerifyStatus = "No tentacle-reachable nodes to verify.";
        return;
      }

      // Erect once if not already online, anchored on the first route.
      if (!KrakenController.HardwareErected)
      {
        await KrakenController.EnsureOnlineAsync(routes[0], verifyTarget: false, allowErect: true);
      }

      var compileService = new Compiler.F18NodeCompilationService(
          Chip, RomLibrary, RomLibrary.SystemMacros);

      int matched = 0;
      int mismatched = 0;
      var notCompiled = new List<int>();
      bool aborted = false;

      await KrakenController.BeginKeepOpenAsync();
      try
      {
        for (int index = 0; index < routes.Count; index++)
        {
          KrakenNodeRoute route = routes[index];
          VerifyStatus = $"Verifying node {route.Coordinate:000} ({index + 1}/{routes.Count})…";

          // Compile the generated ROM for this node.
          IReadOnlyList<int>? generated = null;
          string? expandedRomSource = null;
          try
          {
            var result = compileService.CompileNode(route.Coordinate);
            if (result.Rom.Success)
            {
              generated = result.Rom.Words
                  .Select(word => word & Compiler.F18InstructionSet.WordMask)
                  .ToArray();
              expandedRomSource = result.Rom.ExpandedSource;
            }
          }
          catch
          {
            generated = null;
          }

          if (generated is null)
          {
            notCompiled.Add(route.Coordinate);
            continue;
          }

          IReadOnlyList<int> onChip = await KrakenController.ReadRomAsync(route);
          var comparison = RomComparison.Compare(route.Coordinate, generated, onChip);

          if (comparison.IsMatch)
          {
            matched++;
            continue;
          }

          mismatched++;
          var dialog = new Views.RomMismatchDialog(comparison, showAbort: true, expandedRomSource: expandedRomSource)
          {
            Owner = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive)
          };
          bool? result2 = dialog.ShowDialog();
          if (dialog.Aborted || result2 == false)
          {
            aborted = true;
            break;
          }
        }
      }
      finally
      {
        await KrakenController.EndKeepOpenAsync();
      }

      // Build the final summary. 708 is always unchecked here.
      var summary = new System.Text.StringBuilder();
      summary.AppendLine(aborted ? "ROM verification aborted." : "ROM verification complete.");
      summary.AppendLine($"Matched: {matched}");
      summary.AppendLine($"Mismatched: {mismatched}");
      if (notCompiled.Count > 0)
      {
        summary.AppendLine($"Not compared (ROM did not compile): {notCompiled.Count} node(s) — "
            + string.Join(", ", notCompiled.Select(coordinate => coordinate.ToString("000"))));
      }
      summary.AppendLine();
      summary.AppendLine("Node 708 was not checked: it is the Kraken head and cannot be read "
          + "through Kraken. Use the \"Verify node 708 ROM\" button instead (requires removing "
          + "the Kraken first).");

      VerifyStatus = aborted
          ? $"Aborted. {matched} matched, {mismatched} mismatched so far."
          : $"Done. {matched} matched, {mismatched} mismatched.";

      MessageBox.Show(
          summary.ToString(),
          "Verify all node ROMs",
          MessageBoxButton.OK,
          mismatched > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
    catch (Exception exception)
    {
      VerifyStatus = "Verification failed.";
      MessageBox.Show(
          $"ROM verification could not complete:\n\n{exception.Message}",
          "Verify all node ROMs",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
    }
    finally
    {
      VerifyBusy = false;
    }
  }

  /// <summary>
  /// Verify node 708's own ROM against a compiled-from-yaml expectation. Node
  /// 708 is the Kraken head and cannot be read through the normal tentacle R1
  /// mechanism (there is no route to it), so this uses a completely separate
  /// direct-boot readback (<see cref="Ga144Node708RomReader"/>) instead of
  /// KrakenController. That reader resets the chip to load its one-shot
  /// dump-rom program, which is fundamentally incompatible with a resident
  /// Kraken (whose lifetime rule forbids ever pulsing reset again), so this is
  /// blocked outright while a Kraken is erected.
  /// </summary>
  private async Task VerifyNode708RomAsync()
  {
    if (_verifyBusy)
    {
      return;
    }

    if (KrakenController.HardwareErected)
    {
      MessageBox.Show(
          "Node 708 cannot be verified while a Kraken is erected on this chip. "
          + "Reading node 708's own ROM requires resetting it to load a one-shot "
          + "readback program, and a resident Kraken must never be reset. "
          + "Remove the Kraken first, then verify node 708.",
          "Verify node 708 ROM",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      return;
    }

    KrakenEndpointInfo? endpoint = KrakenEndpointResolver();
    if (endpoint is null)
    {
      MessageBox.Show(
          "No serial endpoint is assigned to this chip. Assign a COM port before verifying node 708.",
          "Verify node 708 ROM",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      return;
    }

    VerifyBusy = true;
    VerifyStatus = "Verifying node 708…";
    try
    {
      var compileService = new Compiler.F18NodeCompilationService(
          Chip, RomLibrary, RomLibrary.SystemMacros);

      IReadOnlyList<int>? generated = null;
      string? expandedRomSource = null;
      try
      {
        var result = compileService.CompileNode(KrakenTopology.HeadCoordinate);
        if (result.Rom.Success)
        {
          generated = result.Rom.Words
              .Select(word => word & Compiler.F18InstructionSet.WordMask)
              .ToArray();
          expandedRomSource = result.Rom.ExpandedSource;
        }
      }
      catch
      {
        generated = null;
      }

      if (generated is null)
      {
        VerifyStatus = "Node 708 ROM did not compile.";
        MessageBox.Show(
            "Node 708's ROM source did not compile; nothing to compare against the chip.",
            "Verify node 708 ROM",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        return;
      }

      var reader = new Ga144Node708RomReader();
      int[] onChip = await reader.ReadRomAsync(endpoint.PortName);

      var comparison = RomComparison.Compare(KrakenTopology.HeadCoordinate, generated, onChip);
      if (comparison.IsMatch)
      {
        VerifyStatus = "Node 708: ROM matches.";
        MessageBox.Show(
            comparison.Summary(),
            "Verify node 708 ROM",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
      }
      else
      {
        VerifyStatus = $"Node 708: {comparison.Mismatches.Count} mismatch(es).";
        var dialog = new Views.RomMismatchDialog(comparison, showAbort: false, expandedRomSource: expandedRomSource)
        {
          Owner = Application.Current?.Windows
              .OfType<Window>()
              .FirstOrDefault(window => window.IsActive)
        };
        dialog.ShowDialog();
      }
    }
    catch (Exception exception)
    {
      VerifyStatus = "Node 708 verification failed.";
      MessageBox.Show(
          $"Node 708 ROM verification could not complete:\n\n{exception.Message}",
          "Verify node 708 ROM",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
    }
    finally
    {
      VerifyBusy = false;
    }
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