using System.Collections.ObjectModel;
using System.IO;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class KrakenCheckViewModel : ObservableObject, IAsyncDisposable
{
  private readonly KrakenConfiguration _configuration;
  private readonly KrakenLiveController _controller;
  private readonly Func<string, Task<bool>>? _confirmResetAndRetryAsync;
  private readonly CancellationTokenSource _shutdown = new();
  private bool _isBusy;
  private string _statusText = "Ready to check the installed Kraken.";
  private string _endpointText = "No live board endpoint resolved yet.";
  private string _progressText = "0 / 143";
  private int _completed;

  public KrakenCheckViewModel(
    KrakenConfiguration configuration,
    KrakenLiveController controller,
    Func<string, Task<bool>>? confirmResetAndRetryAsync = null)
  {
    _configuration = configuration;
    _controller = controller;
    _confirmResetAndRetryAsync = confirmResetAndRetryAsync;

    IReadOnlyDictionary<int, KrakenNodeRoute> routes = KrakenTopology.BuildRouteMap(_configuration);
    foreach (KrakenNodeRoute route in routes.Values
      .Where(item => !item.IsHead)
      .OrderBy(item => item.Position)
      .ThenBy(item => item.TentacleNumber))
    {
      Items.Add(new KrakenCheckItemViewModel(route));
    }

    CancelCommand = new RelayCommand(
      () => _shutdown.Cancel(),
      () => IsBusy && !_shutdown.IsCancellationRequested);
  }

  public ObservableCollection<KrakenCheckItemViewModel> Items { get; } = [];
  public RelayCommand CancelCommand { get; }
  public string OrderText => "Order: 707 / 709 / 608 first (the three neighbors of 708), then position 01 of T1/T2/T3, position 02, and so on outward.";
  public string CheckDescription => "For each reachable node the IDE saves A and RAM[0], writes the decimal node coordinate into RAM[0], reads RAM[0] back via that Kraken route, compares it, then restores RAM[0] and A. If Kraken is not yet running it is erected once. The COM endpoint then remains exclusively reserved, but its native handle is closed whenever no Kraken operation is active. Check Kraken opens it for the scan and parks it immediately when the scan ends. Check transactions are deliberately paced with a 10 ms USB quiet interval. Once Kraken is live, all IDE serial/COM discovery is frozen and the Kraken transport uses synchronous Win32 ReadFile/WriteFile only during explicit operations (no SerialPort event loop). Reopening never intentionally pulses reset or re-erects the tentacles. A transport failure stops the remaining check instead of resetting the chip.";

  public bool IsBusy
  {
    get => _isBusy;
    private set
    {
      if (SetProperty(ref _isBusy, value))
      {
        CancelCommand.NotifyCanExecuteChanged();
      }
    }
  }

  public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
  public string EndpointText { get => _endpointText; private set => SetProperty(ref _endpointText, value); }
  public string ProgressText { get => _progressText; private set => SetProperty(ref _progressText, value); }

  public async Task RunAsync()
  {
    if (IsBusy)
    {
      return;
    }

    if (!_configuration.Enabled || Items.Count == 0)
    {
      StatusText = "No Kraken is installed on this chip.";
      return;
    }

    IsBusy = true;
    _completed = 0;
    ProgressText = $"0 / {Items.Count}";

    try
    {
      while (true)
      {
        try
        {
          await RunOnceAsync();
          break;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception exception) when (IsTransportFault(exception) && _confirmResetAndRetryAsync is not null)
        {
          // A Kraken transport/topology fault leaves the erection latch believing
          // hardware is live while the link is actually down, so every retry that
          // reuses the resident session fails the same way. Offer the user a
          // reset-and-re-erect recovery; on confirmation we drop the transient
          // erection state (which does not itself pulse reset) and erect again
          // (the erection is what resets the chip on a Host/Port-A endpoint).
          StatusText = "Kraken transport fault: " + exception.Message;
          bool retry = await _confirmResetAndRetryAsync(
              "Kraken communication failed: " + exception.Message +
              "\n\nReset and re-erect the chip, then retry the check?");
          if (!retry)
          {
            foreach (KrakenCheckItemViewModel item in Items)
            {
              if (item.Status == "PENDING")
              {
                item.MarkSkipped("Check stopped before this node was reached.");
              }
            }

            StatusText = "Kraken check stopped after a transport fault; the user declined reset/re-erection.";
            break;
          }

          await RecoverForRetryAsync();
          // Loop and attempt the full erect + scan again. Per the chosen policy the
          // user is re-prompted after every subsequent failure until it passes,
          // they decline, or they cancel.
        }
      }
    }
    catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
    {
      foreach (KrakenCheckItemViewModel item in Items)
      {
        item.MarkSkipped("Check cancelled before this node was reached.");
      }
      StatusText = "Kraken check cancelled.";
    }
    catch (Exception exception)
    {
      foreach (KrakenCheckItemViewModel item in Items)
      {
        if (item.Status == "PENDING")
        {
          item.MarkSkipped("Check stopped before this node was reached.");
        }
      }
      StatusText = "Kraken check stopped: " + exception.Message;
    }
    finally
    {
      // Even if the scan was cancelled between erection and the first
      // check transaction, never leave the FTDI/VCP handle open while the
      // Check Kraken window is idle. This does not reset or re-erect the
      // GA144; it only releases the host COM handle.
      try
      {
        await _controller.ParkTransportAsync(CancellationToken.None);
      }
      catch
      {
        // Preserve the scan result/status; parking is best effort.
      }

      IsBusy = false;
      ProgressText = $"{Items.Count(item => item.Status != "PENDING")} / {Items.Count}";
    }
  }

  // A single erect + full RAM[0] scan attempt. Throws on a transport/topology
  // fault so RunAsync can offer reset-and-retry recovery.
  private async Task RunOnceAsync()
  {
    KrakenNodeRoute anchor = Items[0].Route;
    bool resetPerformed = await _controller.EnsureOnlineAsync(
      anchor,
      verifyTarget: false,
      allowErect: true,
      _shutdown.Token);

    KrakenEndpointInfo? endpoint = _controller.CurrentEndpoint;
    EndpointText = endpoint is null
      ? "Kraken endpoint connected."
      : $"{endpoint.BoardName} — {endpoint.Role} — {endpoint.PortName} @ {KrakenSession.OnlineBaudRate:N0} baud";

    StatusText = resetPerformed
      ? "Kraken erected once; opening COM only for the active RAM[0] scan. It will be parked when the scan finishes..."
      : "Resident Kraken found; opening its reserved COM endpoint for this RAM[0] scan without reset/re-erection...";

    var progress = new Progress<KrakenRamZeroCheckResult>(ApplyProgress);
    IReadOnlyList<KrakenNodeRoute> orderedRoutes = Items.Select(item => item.Route).ToArray();
    IReadOnlyList<KrakenRamZeroCheckResult> results = await _controller.CheckRamZeroAsync(
      orderedRoutes,
      progress,
      _shutdown.Token);

    int passed = results.Count(item => item.Outcome == KrakenCheckOutcome.Passed);
    int failed = results.Count(item => item.Outcome == KrakenCheckOutcome.Failed);
    int skipped = results.Count(item => item.Outcome == KrakenCheckOutcome.Skipped);

    if (failed == 0 && skipped == 0)
    {
      // Stop USB activity immediately after the last node's restore
      // transaction. There is deliberately no post-check verification
      // read: once this status is shown, Check Kraken issues no further
      // COM/USB operation while leaving the same native handle open.
      StatusText = $"Kraken check passed: all {passed} tentacle nodes passed. The native COM handle is now CLOSED/PARKED; the endpoint remains reserved for Kraken.";
    }
    else
    {
      StatusText = $"Kraken check finished: {passed} passed, {failed} failed, {skipped} skipped. No reset/re-erection recovery was attempted; the COM handle is parked and the endpoint remains reserved.";
    }
  }

  // Clear the faulted erection latch and reset every node row to pending so the
  // retried scan starts clean. ResetTransientErectionAsync drops IDE erection
  // state and releases the native handle without pulsing reset; the reset happens
  // on the next erection, inside RunOnceAsync.
  private async Task RecoverForRetryAsync()
  {
    await _controller.ResetTransientErectionAsync(_shutdown.Token);
    _completed = 0;
    ProgressText = $"0 / {Items.Count}";
    foreach (KrakenCheckItemViewModel item in Items)
    {
      item.MarkPending();
    }

    StatusText = "Re-erecting the Kraken and retrying the check...";
  }

  // Transport/topology faults that a reset-and-re-erect can plausibly clear. A
  // cancellation is handled separately and must never be treated as a fault.
  private static bool IsTransportFault(Exception exception) =>
      exception is TimeoutException or IOException or InvalidOperationException;

  public ValueTask DisposeAsync()
  {
    _shutdown.Cancel();
    return ValueTask.CompletedTask;
  }

  private void ApplyProgress(KrakenRamZeroCheckResult result)
  {
    KrakenCheckItemViewModel? item = Items.FirstOrDefault(candidate => candidate.Route.Coordinate == result.Coordinate);
    item?.Apply(result);
    _completed++;
    ProgressText = $"{_completed} / {Items.Count}";
    StatusText = result.Outcome switch
    {
      KrakenCheckOutcome.Passed => $"PASS node {result.Coordinate:000} on T{result.TentacleNumber}:{result.Position:00}.",
      KrakenCheckOutcome.Failed => $"FAIL node {result.Coordinate:000} on T{result.TentacleNumber}:{result.Position:00}: {result.Message}",
      KrakenCheckOutcome.Skipped => $"Skipping node {result.Coordinate:000} on T{result.TentacleNumber}:{result.Position:00}.",
      _ => StatusText
    };
  }
}