using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>One selectable SRAM memory-master choice (AN003 permits 106, 108, or 207).</summary>
public sealed record SramMasterOption(int Coordinate, string Label)
{
  public override string ToString() => Label;
}

/// <summary>
/// Drives the SRAM Tentacle window: installs AN003's SRAM cluster firmware
/// (see <see cref="SramClusterInstaller"/>/<see cref="SramClusterPrograms"/>)
/// for a chosen memory-master node, then issues ex@/ex!/cx?/mk! requests to
/// that master through Kraken (see <see cref="KrakenSramProtocol"/>/the SRAM
/// methods on <see cref="KrakenLiveController"/>), plus a diagnostic-only
/// echo test (not AN003) that exercises the master's call/return plumbing in
/// isolation from node 107. Mirrors <see cref="KrakenNodeControlViewModel"/>'s
/// busy/status RunAsync shape rather than duplicating it under a different
/// name.
/// </summary>
public sealed class SramTentacleViewModel : ObservableObject
{
  public static readonly IReadOnlyList<SramMasterOption> MasterOptions =
  [
    new SramMasterOption(106, "106 (Stack node)"),
    new SramMasterOption(108, "108"),
    new SramMasterOption(207, "207")
  ];

  private readonly Ga144ChipConfiguration _chip;
  private readonly Ga144RomLibrary _romLibrary;
  private readonly IReadOnlyList<F18MacroDefinition> _userMacros;
  private readonly KrakenLiveController _controller;
  private readonly Func<IReadOnlyDictionary<int, KrakenNodeRoute>> _resolveRoutes;
  private readonly CancellationTokenSource _shutdown = new();
  private readonly List<string> _logLines = [];

  private SramMasterOption _selectedMaster = MasterOptions[0];
  private SramMasterSupportAddresses? _masterSupport;
  private bool _isInstalled;
  private bool _isBusy;
  private string _statusText = "Not installed. Choose a master node and click Install SRAM cluster.";
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

  public SramTentacleViewModel(
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

  public IReadOnlyList<SramMasterOption> Masters => MasterOptions;

  public SramMasterOption SelectedMaster
  {
    get => _selectedMaster;
    set
    {
      if (SetProperty(ref _selectedMaster, value ?? MasterOptions[0]))
      {
        IsInstalled = false;
        _masterSupport = null;
        StatusText = $"Master changed to {SelectedMaster.Label}. Install (or re-install) the SRAM cluster for this master before using it.";
        NotifyCommandStates();
      }
    }
  }

  /// <summary>
  /// True once Install has succeeded for the CURRENTLY selected master this
  /// session. This is a UI convenience, not a hardware readback -- there is
  /// no way to ask the chip "is the cluster installed"; switching masters (or
  /// closing and reopening this window) forgets it, and re-Install is always
  /// safe (idempotent: it just recompiles and redeploys all four nodes).
  /// </summary>
  public bool IsInstalled { get => _isInstalled; private set => SetProperty(ref _isInstalled, value); }

  public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) NotifyCommandStates(); } }
  public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

  // Plain, read-only, selectable text (bound into a read-only TextBox, not a
  // ListBox) so the user can click-drag and Ctrl+C an error message out of
  // the window -- a ListBox/TextBlock's text cannot be selected in WPF.
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

  /// <summary>
  /// Explains the echo panel's purpose, since it looks like it belongs to
  /// AN003 but isn't: it's a diagnostic-only call/return sanity check
  /// against the master node itself, independent of node 107 and the SRAM
  /// cluster. See the remarks on <see cref="KrakenSramProtocol.BuildEchoTest"/>.
  /// </summary>
  public string EchoReferenceText =>
      "Not an AN003 operation. Calls the master node's own resident 'echo' subroutine, " +
      "which adds 1 and returns it -- tests Kraken's push/call/read-back to the master " +
      "itself, without touching node 107 or the rest of the cluster. Available as soon as " +
      "the master's own support code installs, even if 007/008/009/107 fail.";

  /// <summary>
  /// Reference text for the mask panel: AN003 section 3's port write-signal
  /// bits, shown so the mask value doesn't have to be recalled from memory.
  /// With this project's single-fixed-master node 107 (see the remarks on
  /// <see cref="SramClusterPrograms.BuildNode107Source"/>), mk! is accepted
  /// on the wire but does not actually enable/disable anything.
  /// </summary>
  public string MaskReferenceText =>
      "Port write bits (AN003 section 3): 106 = x8000, 108 = x0800, 207 = x0200. " +
      "mk! is protocol-compatible only in this build -- there is exactly one fixed master " +
      "per installed cluster, so nothing is actually enabled or disabled.";

  public AsyncRelayCommand InstallCommand { get; }
  public AsyncRelayCommand ReadCommand { get; }
  public AsyncRelayCommand WriteCommand { get; }
  public AsyncRelayCommand CompareExchangeCommand { get; }
  public AsyncRelayCommand SetMaskCommand { get; }
  public AsyncRelayCommand EchoTestCommand { get; }

  public void Cancel() => _shutdown.Cancel();

  private KrakenNodeRoute? CurrentRoute =>
      _resolveRoutes().TryGetValue(SelectedMaster.Coordinate, out KrakenNodeRoute? route) ? route : null;

  private bool CanOperate() => !IsBusy && IsInstalled && _masterSupport is not null && _controller.IsOperational && CurrentRoute is not null;

  // Deliberately NOT gated on IsInstalled (full-cluster success): the echo
  // subroutine is part of the master's own support code, which the installer
  // deploys and resolves BEFORE attempting 007/008/009/107 (see
  // SramClusterInstaller.InstallAsync), so _masterSupport can be non-null
  // even when the overall Install failed partway through the rest of the
  // cluster. That is the whole point of this diagnostic -- it needs to stay
  // usable precisely when the rest of the cluster might not be working.
  private bool CanEcho() => !IsBusy && _masterSupport is not null && _controller.IsOperational && CurrentRoute is not null;

  private async Task InstallAsync()
  {
    if (!_controller.IsOperational)
    {
      StatusText = "Kraken is not online. Erect/connect Kraken first.";
      return;
    }

    if (CurrentRoute is null)
    {
      StatusText = $"No Kraken route to node {SelectedMaster.Coordinate:000}. Is the Kraken erected?";
      return;
    }

    IsBusy = true;
    string activity = $"Installing SRAM cluster (master {SelectedMaster.Label})";
    StatusText = activity + "...";
    Append(activity + "...");
    Append("  Reorganizing Tentacle 3 to a short, direct path from 608 to "
        + $"{SelectedMaster.Label} + the cluster nodes (007/008/009/107); "
        + "other nodes on the old Tentacle 3 become inaccessible this session. "
        + "If Tentacle 3 is not already erected this way, this re-erects Kraken, "
        + "which resets the whole chip (Tentacles 1/2 keep their full node lists).");
    try
    {
      var installer = new SramClusterInstaller(_chip, _romLibrary, _userMacros);
      SramClusterInstallResult result = await installer.InstallAsync(
          _controller, SelectedMaster.Coordinate, _shutdown.Token);

      foreach (SramClusterInstallNodeResult node in result.Nodes)
      {
        Append(node.Success
            ? $"  node {node.Coordinate:000}: installed."
            : $"  node {node.Coordinate:000}: FAILED - {DescribeFirstError(node.Diagnostics)}");
      }

      IsInstalled = result.Success;
      _masterSupport = result.MasterSupport;
      StatusText = result.Success
          ? $"SRAM cluster installed for master {SelectedMaster.Label} (its own support code, plus nodes 007, 008, 009, 107)."
          : "SRAM cluster install failed; see log below.";
    }
    catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
    {
      StatusText = activity + " cancelled.";
    }
    catch (Exception exception)
    {
      IsInstalled = false;
      _masterSupport = null;
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
        CurrentRoute!, _masterSupport!.ReadSubroutineAddress, page, address, _shutdown.Token);
    ReadResultText = Format(value);
    Append($"  ex@ page {page:X}, address 0x{address:X4} -> 0x{value:X4}");
  });

  private Task WriteAsync() => RunAsync("ex! (write)", async () =>
  {
    int page = ParsePage(PageText);
    int address = ParseWord(AddressText, "address");
    int value = ParseWord(ValueText, "value");
    await _controller.WriteSramWordAsync(
        CurrentRoute!, _masterSupport!.WriteSubroutineAddress, page, address, value, _shutdown.Token);
    Append($"  ex! page {page:X}, address 0x{address:X4} <- 0x{value:X4}");
  });

  private Task CompareExchangeAsync() => RunAsync("cx? (compare-and-exchange)", async () =>
  {
    int page = ParsePage(PageText);
    int address = ParseWord(AddressText, "address");
    int compareValue = ParseWord(CompareValueText, "compare value");
    int newValue = ParseWord(NewValueText, "new value");
    int result = await _controller.CompareExchangeSramWordAsync(
        CurrentRoute!, _masterSupport!.CompareExchangeSubroutineAddress, page, address, compareValue, newValue, _shutdown.Token);
    bool stored = (result & 0xFFFF) == 0xFFFF;
    CompareExchangeResultText = stored ? "stored (matched)" : "unchanged (mismatch)";
    Append($"  cx? page {page:X}, address 0x{address:X4}, compare 0x{compareValue:X4}, new 0x{newValue:X4} -> {CompareExchangeResultText}");
  });

  private Task SetMaskAsync() => RunAsync("mk! (set mask)", async () =>
  {
    int mask = ParseWord(MaskText, "mask");
    await _controller.SetSramMasterMaskAsync(
        CurrentRoute!, _masterSupport!.SetMaskSubroutineAddress, mask, PostStimuli, _shutdown.Token);
    Append($"  mk! mask 0x{mask:X4}, postStimuli {PostStimuli} (protocol-compatible only; see mask reference note)");
  });

  private Task EchoTestAsync() => RunAsync("echo (node 106 support-code call/return test)", async () =>
  {
    int value = ParseWord(EchoValueText, "value");
    int result = await _controller.EchoTestAsync(
        CurrentRoute!, _masterSupport!.EchoSubroutineAddress, value, _shutdown.Token);
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
      StatusText = $"No Kraken route to node {SelectedMaster.Coordinate:000}.";
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
  }
}