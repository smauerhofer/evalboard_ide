using System.Collections.ObjectModel;
using System.Windows;
using Ga144.Evb.Ide.Cvm;
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
    KrakenLiveController krakenController,
    IReadOnlyList<ProjectViewModel> allProjects)
  {
    Project = project;
    Role = role;
    Chip = project.GetChip(role);
    RomLibrary = romLibrary;
    RomLibraryPath = romLibraryPath;
    SaveRomLibraryAsync = saveRomLibraryAsync;
    KrakenEndpointResolver = krakenEndpointResolver;
    KrakenController = krakenController ?? throw new ArgumentNullException(nameof(krakenController));
    AllProjects = allProjects ?? throw new ArgumentNullException(nameof(allProjects));
    KrakenController.StateChanged += OnKrakenControllerStateChanged;
    ToggleKrakenCommand = new AsyncRelayCommand(ToggleKrakenAsync);
    VerifyAllRomsCommand = new AsyncRelayCommand(VerifyAllRomsAsync, () => !_verifyBusy);
    VerifyNode708RomCommand = new AsyncRelayCommand(VerifyNode708RomAsync, () => !_verifyBusy);
    RunNode708EchoTestCommand = new AsyncRelayCommand(RunNode708EchoTestAsync, () => !_verifyBusy);
    RunNode708DispatchTestCommand = new AsyncRelayCommand(RunNode708DispatchTestAsync, () => !_verifyBusy);
    RunNode708SetNodeTestCommand = new AsyncRelayCommand(RunNode708SetNodeTestAsync, () => !_verifyBusy);
    InstallCvmTestCommand = new AsyncRelayCommand(InstallCvmTestAsync, () => !_verifyBusy);
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

  /// <summary>
  /// Every project currently open in the workspace, including this chip
  /// window's own project. Used by the node editor's "Copy to project…"
  /// action to offer the other projects a node's source can be copied into.
  /// </summary>
  public IReadOnlyList<ProjectViewModel> AllProjects { get; }
  public ObservableCollection<NodeViewModel> Nodes { get; } = [];
  public AsyncRelayCommand ToggleKrakenCommand { get; }
  public AsyncRelayCommand VerifyAllRomsCommand { get; }
  public AsyncRelayCommand VerifyNode708RomCommand { get; }
  public AsyncRelayCommand RunNode708EchoTestCommand { get; }
  public AsyncRelayCommand RunNode708DispatchTestCommand { get; }
  public AsyncRelayCommand RunNode708SetNodeTestCommand { get; }
  public AsyncRelayCommand InstallCvmTestCommand { get; }

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
        RunNode708EchoTestCommand.NotifyCanExecuteChanged();
        RunNode708DispatchTestCommand.NotifyCanExecuteChanged();
        RunNode708SetNodeTestCommand.NotifyCanExecuteChanged();
        InstallCvmTestCommand.NotifyCanExecuteChanged();
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

  /// <summary>
  /// Test node 708's own hand-written direct-UART transmit routines
  /// (obit/oword/obyt/echo) instead of the old carrier-clock
  /// wait-high/wait-low scheme: uploads a one-shot program that receives an
  /// 18-bit word the normal way (via the ROM's own already-verified
  /// <c>18ibits</c>, which self-calibrates via <c>sync</c>) and immediately
  /// transmits it straight back out as genuine, self-timed UART bytes via
  /// <c>delay</c> -- no host-driven carrier clocking on the return path. One
  /// boot then drives a whole suite over the same session: a sweep of fixed
  /// and walking-single-bit test patterns, each checked against an
  /// independent bit-level prediction of obit/oword/obyt's own algorithm, and
  /// a speed test estimating write and read throughput. See
  /// <see cref="Ga144Node708EchoProbe"/> for details. This supersedes the
  /// earlier "Read node 708 delay" probe (Ga144Node708DelayProbe, now
  /// removable from the project). Same reset requirement and
  /// Kraken-exclusivity restriction as "Verify node 708 ROM".
  /// </summary>
  private async Task RunNode708EchoTestAsync()
  {
    if (_verifyBusy)
    {
      return;
    }

    if (KrakenController.HardwareErected)
    {
      MessageBox.Show(
          "Node 708's echo test cannot run while a Kraken is erected on this chip. "
          + "This probe requires resetting node 708 to load a one-shot test program, and a "
          + "resident Kraken must never be reset. Remove the Kraken first, then try again.",
          "Node 708 echo test",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      return;
    }

    KrakenEndpointInfo? endpoint = KrakenEndpointResolver();
    if (endpoint is null)
    {
      MessageBox.Show(
          "No serial endpoint is assigned to this chip. Assign a COM port before running the node 708 echo test.",
          "Node 708 echo test",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      return;
    }

    VerifyBusy = true;
    VerifyStatus = "Running node 708 echo test…";
    try
    {
      var probe = new Ga144Node708EchoProbe();
      Node708EchoReport report = await probe.RunEchoSuiteAsync(endpoint.PortName, Chip, RomLibrary);

      int matched = report.PatternResults.Count(item => item.Matched);
      int mismatched = report.PatternResults.Count - matched;
      var mismatches = report.PatternResults.Where(item => !item.Matched).ToList();

      VerifyStatus = mismatched == 0
          ? $"Node 708 echo test: {matched}/{report.PatternResults.Count} patterns matched. "
              + $"~{report.SpeedResult.WriteBitsPerSecond:F0} bit/s write, ~{report.SpeedResult.ReadBitsPerSecond:F0} bit/s read."
          : $"Node 708 echo test: {mismatched}/{report.PatternResults.Count} pattern(s) MISMATCHED.";

      var summary = new System.Text.StringBuilder();
      summary.AppendLine($"Pattern sweep: {matched}/{report.PatternResults.Count} matched.");
      if (mismatches.Count > 0)
      {
        summary.AppendLine();
        summary.AppendLine("Mismatches:");
        foreach (Node708EchoPatternResult item in mismatches)
        {
          string received = string.Join(" ", item.ReceivedBytes.Select(b => $"{b:X2}"));
          string expected = string.Join(" ", item.ExpectedBytes.Select(b => $"{b:X2}"));
          summary.AppendLine($"  0x{item.SentWord:X5}: received {received}, expected {expected}");
        }
      }

      summary.AppendLine();
      Node708EchoSpeedResult speed = report.SpeedResult;
      summary.AppendLine($"Speed test ({speed.Iterations} round trips of a fixed 0x15555 test word):");
      summary.AppendLine($"  Write: avg {speed.AverageWriteTime.TotalMilliseconds:F2} ms  (~{speed.WriteBitsPerSecond:F0} data bit/s)");
      summary.AppendLine($"  Read:  avg {speed.AverageReadTime.TotalMilliseconds:F2} ms  (~{speed.ReadBitsPerSecond:F0} data bit/s)");
      summary.AppendLine($"  Round trip: avg {speed.AverageRoundTripTime.TotalMilliseconds:F2} ms  (~{speed.RoundTripsPerSecond:F1} round trips/s)");
      summary.AppendLine();
      summary.AppendLine("\"Write\" is the host write call plus draining the local output buffer "
          + "(the closest thing .NET's SerialPort API exposes to \"the bytes actually left\" -- "
          + "there is no true hardware transmit-complete signal). \"Read\" is everything after "
          + "that: remaining wire time, node 708's own receive/decode/reply work, and the wire "
          + "time of its reply -- so read is the more representative figure for real round-trip "
          + "latency; round trips/s is the most trustworthy single number here.");
      summary.AppendLine();
      summary.AppendLine("\"Expected\" bytes are computed independently in C# from obit/oword/obyt's own "
          + "bit-extraction algorithm (LSB-first, F18 arithmetic '2/' shift), not copied from any single "
          + "observed result, so a mismatch here is a real discrepancy worth investigating.");

      MessageBox.Show(
          summary.ToString(),
          "Node 708 echo test",
          MessageBoxButton.OK,
          mismatched > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
    catch (Exception exception)
    {
      VerifyStatus = "Node 708 echo test failed.";
      MessageBox.Show(
          $"Node 708 echo test could not complete:\n\n{exception.Message}",
          "Node 708 echo test",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
    }
    finally
    {
      VerifyBusy = false;
    }
  }

  /// <summary>
  /// Tests node 708's own main/sett dispatch loop specifically -- built after
  /// a hardware test found that calling 'sett' twice in a row (the exact same
  /// call, same value) succeeds the first time and times out (0 of 3 bytes)
  /// the second time, even after the boot-frame completion-address fix. That
  /// ruled out 'w/r's own complexity as the cause -- it never even ran -- and
  /// pointed at something about a SECOND dispatch through 'main' itself.
  /// This probe boots only main/obit/readw/oword/obyt/sett (no setn/dec/w/r)
  /// and repeats the exact same 'sett' call so a pass/fail pattern across
  /// repeated calls is visible on its own, decoupled from w/r entirely. Same
  /// reset requirement and Kraken-exclusivity restriction as the echo test.
  /// </summary>
  private async Task RunNode708DispatchTestAsync()
  {
    if (_verifyBusy)
    {
      return;
    }

    if (KrakenController.HardwareErected)
    {
      MessageBox.Show(
          "Node 708's dispatch test cannot run while a Kraken is erected on this chip. "
          + "This probe requires resetting node 708 to load a one-shot test program, and a "
          + "resident Kraken must never be reset. Remove the Kraken first, then try again.",
          "Node 708 dispatch test",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      return;
    }

    KrakenEndpointInfo? endpoint = KrakenEndpointResolver();
    if (endpoint is null)
    {
      MessageBox.Show(
          "No serial endpoint is assigned to this chip. Assign a COM port before running the node 708 dispatch test.",
          "Node 708 dispatch test",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      return;
    }

    // The same port value 'focus' erection sends node 707 (tentacle 1
    // position 0) -- the exact call that surfaced this on real hardware --
    // so a pass/fail here is directly comparable to that failure.
    const int testPortValue = 0x175;
    const int testCallCount = 2;

    VerifyBusy = true;
    VerifyStatus = "Running node 708 dispatch test…";
    try
    {
      var probe = new Ga144Node708DispatchProbe();
      Node708DispatchReport report = await probe.RunDispatchProbeAsync(
          endpoint.PortName, Chip, RomLibrary, testPortValue, testCallCount);

      int succeeded = report.Calls.Count(item => item.Succeeded);
      VerifyStatus = succeeded == report.Calls.Count
          ? $"Node 708 dispatch test: {succeeded}/{report.Calls.Count} 'sett' calls succeeded."
          : $"Node 708 dispatch test: {report.Calls.Count - succeeded}/{report.Calls.Count} 'sett' call(s) FAILED.";

      var summary = new System.Text.StringBuilder();
      summary.AppendLine($"Calling 'sett' with the same port value (0x{testPortValue:X3}) {testCallCount} times in a row, over the exact NativeWindowsSerialPort transport Kraken uses:");
      summary.AppendLine();
      foreach (Node708DispatchCallResult call in report.Calls)
      {
        if (call.Succeeded)
        {
          string echoed = call.EchoedBytes is null ? "" : string.Join(" ", call.EchoedBytes.Select(b => $"{b:X2}"));
          summary.AppendLine($"  Call {call.CallNumber}: OK ({call.Elapsed.TotalMilliseconds:F1} ms, echoed {echoed})");
        }
        else
        {
          summary.AppendLine($"  Call {call.CallNumber}: FAILED ({call.Elapsed.TotalMilliseconds:F1} ms) -- {call.FailureMessage}");
        }
      }

      MessageBox.Show(
          summary.ToString(),
          "Node 708 dispatch test",
          MessageBoxButton.OK,
          succeeded < report.Calls.Count ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
    catch (Exception exception)
    {
      VerifyStatus = "Node 708 dispatch test failed.";
      MessageBox.Show(
          $"Node 708 dispatch test could not complete:\n\n{exception.Message}",
          "Node 708 dispatch test",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
    }
    finally
    {
      VerifyBusy = false;
    }
  }

  private async Task RunNode708SetNodeTestAsync()
  {
    if (_verifyBusy)
    {
      return;
    }

    if (KrakenController.HardwareErected)
    {
      MessageBox.Show(
          "Node 708's setn test cannot run while a Kraken is erected on this chip. "
          + "This probe requires resetting node 708 to load a one-shot test program, and a "
          + "resident Kraken must never be reset. Remove the Kraken first, then try again.",
          "Node 708 setn test",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      return;
    }

    KrakenEndpointInfo? endpoint = KrakenEndpointResolver();
    if (endpoint is null)
    {
      MessageBox.Show(
          "No serial endpoint is assigned to this chip. Assign a COM port before running the node 708 setn test.",
          "Node 708 setn test",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      return;
    }

    // 'setn' now falls straight through into 'sett' (no '!n' of its own in
    // this source yet), so each call sends two words: a node-index value
    // (echoed back but not yet stored anywhere) and a tentacle/port value
    // (stored into A by the fallen-into 'sett' body) -- reusing the same
    // 0x175 test value 'focus' erection sends node 707, so a pass/fail on
    // the second word is directly comparable to the earlier dispatch test.
    const int testNodeValue = 0x001;
    const int testTentacleValue = 0x175;
    const int testCallCount = 2;

    VerifyBusy = true;
    VerifyStatus = "Running node 708 setn test…";
    try
    {
      var probe = new Ga144Node708SetNodeProbe();
      Node708SetNodeReport report = await probe.RunSetNodeProbeAsync(
          endpoint.PortName, Chip, RomLibrary, testNodeValue, testTentacleValue, testCallCount);

      int succeeded = report.Calls.Count(item => item.Succeeded);
      VerifyStatus = succeeded == report.Calls.Count
          ? $"Node 708 setn test: {succeeded}/{report.Calls.Count} 'setn' calls succeeded."
          : $"Node 708 setn test: {report.Calls.Count - succeeded}/{report.Calls.Count} 'setn' call(s) FAILED.";

      var summary = new System.Text.StringBuilder();
      summary.AppendLine($"Calling 'setn' with the same node/tentacle value pair (0x{testNodeValue:X3}, 0x{testTentacleValue:X3}) {testCallCount} times in a row, over the exact NativeWindowsSerialPort transport Kraken uses:");
      summary.AppendLine();
      foreach (Node708SetNodeCallResult call in report.Calls)
      {
        if (call.Succeeded)
        {
          string first = call.FirstEchoedBytes is null ? "" : string.Join(" ", call.FirstEchoedBytes.Select(b => $"{b:X2}"));
          string second = call.SecondEchoedBytes is null ? "" : string.Join(" ", call.SecondEchoedBytes.Select(b => $"{b:X2}"));
          summary.AppendLine($"  Call {call.CallNumber}: OK ({call.Elapsed.TotalMilliseconds:F1} ms, echoed {first} then {second})");
        }
        else
        {
          summary.AppendLine($"  Call {call.CallNumber}: FAILED ({call.Elapsed.TotalMilliseconds:F1} ms) -- {call.FailureMessage}");
        }
      }

      MessageBox.Show(
          summary.ToString(),
          "Node 708 setn test",
          MessageBoxButton.OK,
          succeeded < report.Calls.Count ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
    catch (Exception exception)
    {
      VerifyStatus = "Node 708 setn test failed.";
      MessageBox.Show(
          $"Node 708 setn test could not complete:\n\n{exception.Message}",
          "Node 708 setn test",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
    }
    finally
    {
      VerifyBusy = false;
    }
  }

  // Compile-and-display dry run only -- no hardware I/O. Delivering these
  // images and register/stack initializations across the mesh (a per-hop
  // relay through 707/607/507, since 607 and 507 each have to sit in a
  // temporary pass-through role while their own children load -- see
  // CvmBootStreamBuilder's remarks) is a separate, not-yet-built step.
  //
  // Unlike CvmBootStreamBuilder (which compiles this project's team's fixed
  // reference sources -- Node607Program.Source and so on, baked into the
  // assembly), this compiles the CURRENTLY SELECTED PROJECT's own node
  // sources for these coordinates (Chip.GetNode(coordinate).SourceCode),
  // through the same F18NodeCompilationService/RomLibrary path the node
  // editor's own "Compile ROM + RAM" button uses. That is the point of
  // "test": bring a CVM node into this project (e.g. via the node editor's
  // "Copy to project…" from the reference source), edit it here, and this
  // button compiles exactly what is currently in the project, live -- not a
  // frozen reference copy -- so a change can be tried immediately without
  // touching the shipped Node*Program.cs files at all.
  //
  // The confirmed load order/tree shape (leaves first, root last) still
  // comes from CvmBootStreamBuilder.BuildLoadOrder(), since that shape is
  // about the physical mesh topology, not about which source compiled it.
  private async Task InstallCvmTestAsync()
  {
    if (_verifyBusy)
    {
      return;
    }

    VerifyBusy = true;
    VerifyStatus = "Compiling CVM boot stream from this project's nodes…";
    try
    {
      IReadOnlyList<CvmBootLoadStep> loadOrder = CvmBootStreamBuilder.BuildLoadOrder();
      var compileService = new Compiler.F18NodeCompilationService(Chip, RomLibrary, Project.Model.UserMacros);

      var summary = new System.Text.StringBuilder();
      summary.AppendLine(
          $"CVM boot stream -- compiled from \"{Project.Name}\" ({Chip.Name})'s own node sources, "
          + "not the fixed reference copy. Compile + dry run only, no hardware I/O yet. Confirmed "
          + "load order, leaves first / root last:");
      summary.AppendLine();

      bool anyMissing = false;
      bool anyFailed = false;

      await Task.Run(() =>
      {
        foreach (CvmBootLoadStep step in loadOrder)
        {
          string via = step.ViaNodeCoordinate.HasValue ? $"via {step.ViaNodeCoordinate.Value:000}" : "(boot node, no via)";
          Ga144NodeConfiguration node = Chip.GetNode(step.NodeCoordinate);

          if (string.IsNullOrWhiteSpace(node.SourceCode))
          {
            anyMissing = true;
            summary.AppendLine($"Node {step.NodeCoordinate:000} {via} -- not configured in this project (no RAM source). Use \"Copy to project…\" in the node editor to bring in the reference source.");
            continue;
          }

          Compiler.F18NodeCompilationResult compiled = compileService.CompileNode(step.NodeCoordinate);
          if (!compiled.Success)
          {
            anyFailed = true;
            int errorCount = compiled.Rom.Diagnostics.Concat(compiled.Ram.Diagnostics)
                .Count(diagnostic => diagnostic.Severity == Compiler.F18DiagnosticSeverity.Error);
            summary.AppendLine($"Node {step.NodeCoordinate:000} {via} -- COMPILE FAILED ({errorCount} error(s)). Open this node in the editor and press \"Compile ROM + RAM\" for full diagnostics.");
            continue;
          }

          CvmBootDescriptor descriptor = CvmBootDescriptor.FromCompileResult(compiled.Ram);
          summary.AppendLine(
              $"Node {step.NodeCoordinate:000} {via} -- {descriptor.Words.Count} words, entry "
              + $"{(descriptor.EntryPoint.HasValue ? $"0x{descriptor.EntryPoint.Value:X3}" : "<none>")}, "
              + $"A={(descriptor.InitialA.HasValue ? $"0x{descriptor.InitialA.Value:X3}" : "-")} "
              + $"B={(descriptor.InitialB.HasValue ? $"0x{descriptor.InitialB.Value:X3}" : "-")} "
              + $"IO={(descriptor.InitialIo.HasValue ? $"0x{descriptor.InitialIo.Value:X3}" : "-")} "
              + $"stack=[{string.Join(",", descriptor.InitialStack)}]");
        }
      });

      summary.AppendLine();
      summary.AppendLine(anyMissing || anyFailed
          ? "Not every node in this project is ready yet -- see above."
          : "All 9 nodes in this project compiled successfully.");
      summary.AppendLine();
      summary.AppendLine("This does not touch hardware. Delivering these images across the mesh is a separate step, not yet built.");

      VerifyStatus = anyFailed
          ? "CVM boot stream compiled with errors -- see summary."
          : "CVM boot stream compiled -- see summary.";
      MessageBox.Show(
          summary.ToString(),
          "Install CVM test (dry run)",
          MessageBoxButton.OK,
          anyFailed ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
    catch (Exception exception)
    {
      VerifyStatus = "CVM boot stream compile failed.";
      MessageBox.Show(
          $"The CVM boot stream could not be compiled:\n\n{exception.Message}",
          "Install CVM test (dry run)",
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