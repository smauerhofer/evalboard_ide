using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// Drives the SRAM Simulator window: installs the node-707 software SRAM stand-in (see
/// <see cref="SramSimulatorInstaller"/>/<see cref="SramSimulatorPrograms"/>), then issues the same
/// ex@/ex!/cx?/mk! requests <see cref="SramTentacleViewModel"/> issues against the real cluster,
/// plus the same diagnostic-only echo test -- all through the exact same, unmodified
/// <c>KrakenLiveController</c> SRAM methods, just aimed at node 707 instead of a real memory-master
/// node. Built for CVM (C virtual machine) development: exercise SRAM-shaped memory traffic over
/// Kraken without the real external SRAM cluster installed or wired up. Mirrors
/// <see cref="SramTentacleViewModel"/>'s shape closely (including its busy/status RunAsync
/// pattern), minus the master picker -- the target is always node 707.
/// </summary>
public sealed class SramSimulatorViewModel : ObservableObject
{
  private readonly Ga144ChipConfiguration _chip;
  private readonly Ga144RomLibrary _romLibrary;
  private readonly IReadOnlyList<F18MacroDefinition> _userMacros;
  private readonly KrakenLiveController _controller;
  private readonly Func<IReadOnlyDictionary<int, KrakenNodeRoute>> _resolveRoutes;
  private readonly CancellationTokenSource _shutdown = new();
  private readonly List<string> _logLines = [];

  private SramMasterSupportAddresses? _addresses;
  private bool _isInstalled;
  private bool _isBusy;
  private string _statusText = "Not installed. Click Install SRAM simulator (requires an online Kraken).";
  private string _logText = string.Empty;
  private string _pageText = "0x0";
  private string _addressText = "0x0000";
  private string _valueText = "0x0000";
  private string _readResultText = "-";
  private string _compareValueText = "0x0000";
  private string _newValueText = "0x0000";
  private string _compareExchangeResultText = "-";
  private string _maskText = "0x8A00";
  private bool _postStimuli;
  private string _echoValueText = "0x0000";
  private string _echoResultText = "-";

  public SramSimulatorViewModel(
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<F18MacroDefinition> userMacros,
      KrakenLiveController controller,
      Func<IReadOnlyDictionary<int, KrakenNodeRoute>> resolveRoutes)
  {
    _chip = chip ?? throw new ArgumentNullException(nameof(chip));
    _romLibrary = romLibrary ?? throw new ArgumentNullException(nameof(romLibrary));
    _userMacros = userMacros ?? [];
    _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    _resolveRoutes = resolveRoutes ?? throw new ArgumentNullException(nameof(resolveRoutes));

    InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy && _controller.IsOperational && CurrentRoute is not null);
    ReadCommand = new AsyncRelayCommand(ReadAsync, CanOperate);
    WriteCommand = new AsyncRelayCommand(WriteAsync, CanOperate);
    CompareExchangeCommand = new AsyncRelayCommand(CompareExchangeAsync, CanOperate);
    SetMaskCommand = new AsyncRelayCommand(SetMaskAsync, CanOperate);
    EchoTestCommand = new AsyncRelayCommand(EchoTestAsync, CanEcho);
  }

  /// <summary>
  /// True once Install has succeeded this session. A UI convenience, not a hardware readback --
  /// there is no way to ask the chip "is this installed"; closing and reopening this window
  /// forgets it, and re-Install is always safe (idempotent: it just recompiles and redeploys).
  /// </summary>
  public bool IsInstalled { get => _isInstalled; private set => SetProperty(ref _isInstalled, value); }

  public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) NotifyCommandStates(); } }
  public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

  // Plain, read-only, selectable text (bound into a read-only TextBox, not a ListBox/TextBlock) so
  // the user can click-drag and Ctrl+C an error message out of the window -- same reasoning as
  // SramTentacleViewModel.LogText.
  public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }

  public string PageText { get => _pageText; set => SetProperty(ref _pageText, value ?? string.Empty); }
  public string AddressText { get => _addressText; set => SetProperty(ref _addressText, value ?? string.Empty); }
  public string ValueText { get => _valueText; set => SetProperty(ref _valueText, value ?? string.Empty); }
  public string ReadResultText { get => _readResultText; private set => SetProperty(ref _readResultText, value); }

  public string CompareValueText { get => _compareValueText; set => SetProperty(ref _compareValueText, value ?? string.Empty); }
  public string NewValueText { get => _newValueText; set => SetProperty(ref _newValueText, value ?? string.Empty); }
  public string CompareExchangeResultText { get => _compareExchangeResultText; private set => SetProperty(ref _compareExchangeResultText, value); }

  public string MaskText { get => _maskText; set => SetProperty(ref _maskText, value ?? string.Empty); }
  public bool PostStimuli { get => _postStimuli; set => SetProperty(ref _postStimuli, value); }

  public string EchoValueText { get => _echoValueText; set => SetProperty(ref _echoValueText, value ?? string.Empty); }
  public string EchoResultText { get => _echoResultText; private set => SetProperty(ref _echoResultText, value); }

  public string EchoReferenceText =>
      "Not an AN003 operation. Calls node 707's own resident 'echo' subroutine, which adds 1 and " +
      "returns it -- tests Kraken's push/call/read-back to node 707 itself, independent of the " +
      "read/write/compare-exchange logic. Available as soon as Install succeeds.";

  public string PageAddressReferenceText =>
      $"Page is accepted for wire compatibility with the real SRAM Tentacle but is otherwise " +
      $"ignored: node 707 simulates one page, {SramSimulatorPrograms.CapacityWords} words " +
      $"(address masked to 0x{SramSimulatorPrograms.CapacityMask:X}). mk! is a protocol no-op here " +
      "(nothing to enable/disable with a single simulated interface) and just echoes the mask, " +
      "matching AN003 section 6.3's degenerate single-master node 107.";

  /// <summary>
  /// Same warning, same reasoning, as <see cref="SramTentacleViewModel.HasIdlePolicyWarning"/>: a
  /// resident Kraken keeps whatever idle policy it erected under for its whole life, so a closing
  /// policy left in place lets a pause between operations silently reopen (and, on a Host-role
  /// endpoint, reset) the chip, wiping node 707's installed simulator firmware with no visible
  /// error until the next operation times out.
  /// </summary>
  public bool HasIdlePolicyWarning => _controller.IdlePolicy != KrakenIdlePolicy.HoldOpen;

  public string IdlePolicyWarningText => HasIdlePolicyWarning
      ? $"Warning: Kraken idle policy is '{DescribeIdlePolicy(_controller.IdlePolicy)}', not 'Hold open " +
        "while resident'. Any pause between operations in this window can silently reopen the transport -- " +
        "which resets the whole chip on this connection and wipes node 707's installed simulator firmware -- " +
        "with no visible error until something later fails or misbehaves. Switch the main window's Kraken " +
        "idle-policy dropdown to 'Hold open while resident' before installing or using the simulator."
      : string.Empty;

  private static string DescribeIdlePolicy(KrakenIdlePolicy policy) => policy switch
  {
    KrakenIdlePolicy.CloseAfterIdleTimeout => "Close after 1 s idle",
    KrakenIdlePolicy.CloseWhileIdle => "Close between transactions",
    KrakenIdlePolicy.HoldOpen => "Hold open while resident",
    _ => policy.ToString()
  };

  public AsyncRelayCommand InstallCommand { get; }
  public AsyncRelayCommand ReadCommand { get; }
  public AsyncRelayCommand WriteCommand { get; }
  public AsyncRelayCommand CompareExchangeCommand { get; }
  public AsyncRelayCommand SetMaskCommand { get; }
  public AsyncRelayCommand EchoTestCommand { get; }

  public void Cancel() => _shutdown.Cancel();

  private KrakenNodeRoute? CurrentRoute =>
      _resolveRoutes().TryGetValue(SramSimulatorPrograms.SimulatedCoordinate, out KrakenNodeRoute? route) ? route : null;

  private bool CanOperate() => !IsBusy && IsInstalled && _addresses is not null && _controller.IsOperational && CurrentRoute is not null;

  // Deliberately NOT gated on IsInstalled's full state, matching SramTentacleViewModel.CanEcho's own
  // reasoning -- _addresses is set the moment Install succeeds, so this stays consistent with
  // IsInstalled here (there is only ever one node to install, unlike the real cluster's four).
  private bool CanEcho() => !IsBusy && _addresses is not null && _controller.IsOperational && CurrentRoute is not null;

  private async Task InstallAsync()
  {
    if (!_controller.IsOperational)
    {
      StatusText = "Kraken is not online. Erect/connect Kraken first.";
      return;
    }

    if (CurrentRoute is null)
    {
      StatusText = $"No Kraken route to node {SramSimulatorPrograms.SimulatedCoordinate:000}. Is the Kraken erected?";
      return;
    }

    IsBusy = true;
    const string activity = "Installing SRAM simulator on node 707";
    StatusText = activity + "...";
    Append(activity + "...");
    try
    {
      var installer = new SramSimulatorInstaller(_chip, _romLibrary, _userMacros);
      SramSimulatorInstallResult result = await installer.InstallAsync(_controller, CurrentRoute!, _shutdown.Token);

      Append(result.Success
          ? $"  node {SramSimulatorPrograms.SimulatedCoordinate:000}: installed."
          : $"  node {SramSimulatorPrograms.SimulatedCoordinate:000}: FAILED - {DescribeFirstError(result.Diagnostics)}");

      if (result.Success && HasIdlePolicyWarning)
      {
        Append("  " + IdlePolicyWarningText);
      }

      IsInstalled = result.Success;
      _addresses = result.Addresses;
      StatusText = result.Success
          ? "SRAM simulator installed on node 707."
          : "SRAM simulator install failed; see log below.";
    }
    catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
    {
      StatusText = activity + " cancelled.";
    }
    catch (Exception exception)
    {
      IsInstalled = false;
      _addresses = null;
      StatusText = activity + " failed: " + exception.Message;
      Append("  " + exception.Message);
    }
    finally
    {
      IsBusy = false;
    }
  }

  private Task ReadAsync() => RunAsync("ex@ (read)", async () =>
  {
    int page = ParsePage(PageText);
    int address = ParseWord(AddressText, "address");
    int value = await _controller.ReadSramWordAsync(
        CurrentRoute!, _addresses!.ReadSubroutineAddress, page, address, _shutdown.Token);
    ReadResultText = Format(value);
    Append($"  ex@ page {page:X}, address 0x{address:X4} -> 0x{value:X4}");
  });

  private Task WriteAsync() => RunAsync("ex! (write)", async () =>
  {
    int page = ParsePage(PageText);
    int address = ParseWord(AddressText, "address");
    int value = ParseWord(ValueText, "value");
    await _controller.WriteSramWordAsync(
        CurrentRoute!, _addresses!.WriteSubroutineAddress, page, address, value, _shutdown.Token);
    Append($"  ex! page {page:X}, address 0x{address:X4} <- 0x{value:X4}");
  });

  private Task CompareExchangeAsync() => RunAsync("cx? (compare-and-exchange)", async () =>
  {
    int page = ParsePage(PageText);
    int address = ParseWord(AddressText, "address");
    int compareValue = ParseWord(CompareValueText, "compare value");
    int newValue = ParseWord(NewValueText, "new value");
    int result = await _controller.CompareExchangeSramWordAsync(
        CurrentRoute!, _addresses!.CompareExchangeSubroutineAddress, page, address, compareValue, newValue, _shutdown.Token);
    bool stored = (result & 0xFFFF) == 0xFFFF;
    CompareExchangeResultText = stored ? "stored (matched)" : "unchanged (mismatch)";
    Append($"  cx? page {page:X}, address 0x{address:X4}, compare 0x{compareValue:X4}, new 0x{newValue:X4} -> {CompareExchangeResultText}");
  });

  private Task SetMaskAsync() => RunAsync("mk! (set mask, protocol no-op here)", async () =>
  {
    int mask = ParseWord(MaskText, "mask");
    await _controller.SetSramMasterMaskAsync(
        CurrentRoute!, _addresses!.SetMaskSubroutineAddress, mask, PostStimuli, _shutdown.Token);
    Append($"  mk! mask 0x{mask:X4}, postStimuli {PostStimuli} (no-op: echoed back only)");
  });

  private Task EchoTestAsync() => RunAsync("echo (node 707 call/return test)", async () =>
  {
    int value = ParseWord(EchoValueText, "value");
    int result = await _controller.EchoTestAsync(
        CurrentRoute!, _addresses!.EchoSubroutineAddress, value, _shutdown.Token);
    int masked = result & 0xFFFF;
    int expected = (value + 1) & 0xFFFF;
    bool matched = masked == expected;
    EchoResultText = matched ? $"0x{masked:X4} (matches value+1)" : $"0x{masked:X4} (EXPECTED 0x{expected:X4} -- mismatch)";
    Append($"  echo 0x{value:X4} -> 0x{masked:X4}, expected 0x{expected:X4} ({(matched ? "OK" : "MISMATCH")})");
  });

  private async Task RunAsync(string activity, Func<Task> action)
  {
    if (!_controller.IsOperational)
    {
      StatusText = "Kraken is not online.";
      return;
    }

    if (CurrentRoute is null)
    {
      StatusText = $"No Kraken route to node {SramSimulatorPrograms.SimulatedCoordinate:000}.";
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
    catch (FormatException exception)
    {
      StatusText = activity + " not sent: " + exception.Message;
    }
    catch (Exception exception)
    {
      StatusText = activity + " failed: " + exception.Message;
      Append("  " + exception.Message);
    }
    finally
    {
      IsBusy = false;
    }
  }

  private void Append(string line)
  {
    _logLines.Add(line);
    while (_logLines.Count > 200)
    {
      _logLines.RemoveAt(0);
    }

    LogText = string.Join(Environment.NewLine, _logLines);
  }

  private static string DescribeFirstError(IReadOnlyList<F18Diagnostic> diagnostics)
  {
    F18Diagnostic? first = diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == F18DiagnosticSeverity.Error);
    return first?.Message ?? "compilation failed.";
  }

  private static int ParsePage(string text)
  {
    int value = ParseWord(text, "page");
    if (value is < 0 or > 0xF)
    {
      throw new FormatException("Page must be a 4-bit value from 0x0 through 0xF.");
    }

    return value;
  }

  private static int ParseWord(string text, string description)
  {
    if (!KrakenWordFormatting.TryParse(text, out int value) || value is < 0 or > 0xFFFF)
    {
      throw new FormatException($"'{text}' is not a valid 16-bit {description} (0x0000 through 0xFFFF).");
    }

    return value;
  }

  private static string Format(int value) => $"0x{value & 0xFFFF:X4}";

  private void NotifyCommandStates()
  {
    InstallCommand?.NotifyCanExecuteChanged();
    ReadCommand?.NotifyCanExecuteChanged();
    WriteCommand?.NotifyCanExecuteChanged();
    CompareExchangeCommand?.NotifyCanExecuteChanged();
    SetMaskCommand?.NotifyCanExecuteChanged();
    EchoTestCommand?.NotifyCanExecuteChanged();

    OnPropertyChanged(nameof(HasIdlePolicyWarning));
    OnPropertyChanged(nameof(IdlePolicyWarningText));
  }
}
