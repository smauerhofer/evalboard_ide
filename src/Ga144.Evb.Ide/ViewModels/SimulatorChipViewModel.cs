using System.Windows;
using System.Windows.Media;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Simulator;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// Drives the "GA144 simulator" window (<see cref="Views.SimulatorChipWindow"/>): an 18x8 grid of every
/// node's live, steppable state (<see cref="Ga144SimulatorEngine"/>), the Reset/Step 1/Step 10/Step
/// 100/Go/Halt controls, and the external I/O pin panel. Built at Stefan's own request, 2026-09-23 -- see
/// <see cref="Ga144SimulatorEngine"/>'s own remarks for the full design (tick/tock stepping, opcode
/// semantics, port I/O scope).
/// </summary>
public sealed class SimulatorChipViewModel : ObservableObject
{
  /// <summary>Delay between steps while "Go" is running, so the display is actually watchable rather than
  /// refreshing 144 nodes' worth of bindings faster than anything could be read. Not currently exposed as
  /// a user setting -- a reasonable fixed pace for a first version.</summary>
  private static readonly TimeSpan GoStepDelay = TimeSpan.FromMilliseconds(60);

  private readonly Ga144ChipConfiguration _chip;
  private readonly Ga144RomLibrary _romLibrary;
  private readonly IReadOnlyList<F18MacroDefinition> _userMacros;
  private readonly string _projectDefaultColor;
  private bool _running;
  private string _statusText = "Not yet reset.";

  public SimulatorChipViewModel(
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<F18MacroDefinition> userMacros,
      string projectDefaultNodeColor)
  {
    _chip = chip ?? throw new ArgumentNullException(nameof(chip));
    _romLibrary = romLibrary ?? throw new ArgumentNullException(nameof(romLibrary));
    _userMacros = userMacros ?? [];
    _projectDefaultColor = string.IsNullOrWhiteSpace(projectDefaultNodeColor)
        ? NodeColorPalette.DefaultColor
        : projectDefaultNodeColor;
    Engine = new Ga144SimulatorEngine(chip, romLibrary, userMacros);

    var nodes = new List<SimulatorNodeCellViewModel>(144);
    for (int row = 7; row >= 0; row--)
    {
      for (int column = 0; column <= 17; column++)
      {
        int coordinate = row * 100 + column;
        nodes.Add(new SimulatorNodeCellViewModel(coordinate, Engine, ResolveNodeColor(coordinate)));
      }
    }

    Nodes = nodes;
    ExternalPins = BuildExternalPins();

    ResetCommand = new RelayCommand(Reset);
    Step1Command = new RelayCommand(() => StepAndRefresh(1), () => Engine.IsInitialized && !_running);
    Step10Command = new RelayCommand(() => StepAndRefresh(10), () => Engine.IsInitialized && !_running);
    Step100Command = new RelayCommand(() => StepAndRefresh(100), () => Engine.IsInitialized && !_running);
    GoCommand = new AsyncRelayCommand(GoAsync, () => Engine.IsInitialized && !_running);
    HaltCommand = new RelayCommand(() => _running = false, () => _running);
  }

  public Ga144SimulatorEngine Engine { get; }

  /// <summary>All 144 node cells, in the same descending-row/ascending-column order the live GA144 grid
  /// and the post-mortem grid both already use.</summary>
  public IReadOnlyList<SimulatorNodeCellViewModel> Nodes { get; }

  /// <summary>One row per real chip pin (an edge node's port direction with no physical neighbor) --
  /// Stefan's own "a section displaying all I/O pins and data ports (node 007 and 009)" requirement.</summary>
  public IReadOnlyList<SimulatorExternalPinViewModel> ExternalPins { get; }

  public string StatusText
  {
    get => _statusText;
    private set => SetProperty(ref _statusText, value);
  }

  public RelayCommand ResetCommand { get; }
  public RelayCommand Step1Command { get; }
  public RelayCommand Step10Command { get; }
  public RelayCommand Step100Command { get; }
  public AsyncRelayCommand GoCommand { get; }
  public RelayCommand HaltCommand { get; }

  /// <summary>Raised after every Reset/Step/Go tick so the window can refresh anything it owns beyond the
  /// grid cells themselves (currently: any open <see cref="Views.SimulatorNodeWindow"/>).</summary>
  public event EventHandler? Stepped;

  /// <summary>
  /// Builds the detail view model for one node's simulator window. The engine itself only keeps compiled
  /// RAM/ROM WORDS (see <see cref="Ga144SimulatorEngine.Reset"/>) -- this recompiles that one node, same
  /// as <see cref="PostMortemChipViewModel.BuildNodeDetail"/> already does for the post-mortem window,
  /// purely to recover its symbol table for the RAM/ROM disassembly's "Label" column. A node whose source
  /// no longer compiles the same way it did at the last Reset just shows no labels rather than failing to
  /// open -- the live RAM/ROM words themselves (what actually matters for stepping) always come from the
  /// engine, never from this recompile.
  /// </summary>
  public SimulatorNodeDetailViewModel BuildNodeDetail(int coordinate)
  {
    Dictionary<int, string> labelsByAddress = [];
    try
    {
      var compileService = new F18NodeCompilationService(_chip, _romLibrary, _userMacros);
      F18NodeCompilationResult compiled = compileService.CompileNode(coordinate);
      IReadOnlyDictionary<string, F18ExportedSymbol>? ramSymbols = compiled.Ram.Success ? compiled.Ram.Symbols : null;
      IReadOnlyDictionary<string, F18ExportedSymbol>? ramExternal = compiled.Ram.Success ? compiled.Ram.ExternalSymbols : null;
      IReadOnlyDictionary<string, F18ExportedSymbol>? romSymbols = compiled.Rom.Success ? compiled.Rom.Symbols : null;
      IReadOnlyDictionary<string, F18ExportedSymbol>? romExternal = compiled.Rom.Success ? compiled.Rom.ExternalSymbols : null;

      // RAM and ROM each get their own address-keyed label map (an address space, not a single flat
      // range) -- merged here since SimulatorNodeDetailViewModel just wants "what does this project call
      // this address", and RAM (0x000-0x0FF) and ROM addresses never overlap.
      foreach (KeyValuePair<int, string> entry in F18Disassembler.BuildLabelsByAddress(ramSymbols, ramExternal, coordinate))
      {
        labelsByAddress[entry.Key] = entry.Value;
      }

      foreach (KeyValuePair<int, string> entry in F18Disassembler.BuildLabelsByAddress(romSymbols, romExternal, coordinate))
      {
        labelsByAddress.TryAdd(entry.Key, entry.Value);
      }
    }
    catch
    {
      // Labels are a nice-to-have; the detail window still opens showing the live simulated state.
    }

    return new SimulatorNodeDetailViewModel(coordinate, Engine, labelsByAddress);
  }

  private string? ResolveNodeColor(int coordinate) => _chip.GetNode(coordinate).Color;

  private void Reset()
  {
    Engine.Reset();
    StatusText = $"Reset. {Engine.Nodes.Values.Count(n => n.Error is not null)} node(s) failed to compile (see individual nodes).";
    RefreshAll();
    NotifyCommandsCanExecuteChanged();
  }

  private void StepAndRefresh(int count)
  {
    Engine.Step(count);
    StatusText = $"Step {Engine.StepCount}.";
    RefreshAll();
  }

  private async Task GoAsync()
  {
    _running = true;
    HaltCommand.NotifyCanExecuteChanged();
    NotifyCommandsCanExecuteChanged();
    try
    {
      while (_running)
      {
        Engine.Step();
        StatusText = $"Running -- step {Engine.StepCount}.";
        RefreshAll();
        await Task.Delay(GoStepDelay);
      }
    }
    finally
    {
      _running = false;
      StatusText = $"Halted at step {Engine.StepCount}.";
      HaltCommand.NotifyCanExecuteChanged();
      NotifyCommandsCanExecuteChanged();
    }
  }

  private void NotifyCommandsCanExecuteChanged()
  {
    Step1Command.NotifyCanExecuteChanged();
    Step10Command.NotifyCanExecuteChanged();
    Step100Command.NotifyCanExecuteChanged();
    GoCommand.NotifyCanExecuteChanged();
  }

  private void RefreshAll()
  {
    foreach (SimulatorNodeCellViewModel node in Nodes)
    {
      node.Refresh();
    }

    foreach (SimulatorExternalPinViewModel pin in ExternalPins)
    {
      pin.Refresh();
    }

    Stepped?.Invoke(this, EventArgs.Empty);
  }

  private List<SimulatorExternalPinViewModel> BuildExternalPins()
  {
    var pins = new List<SimulatorExternalPinViewModel>();
    for (int row = 0; row < 8; row++)
    {
      for (int column = 0; column < 18; column++)
      {
        int coordinate = row * 100 + column;
        foreach (F18PortDirection direction in Enum.GetValues<F18PortDirection>())
        {
          if (Engine.GetNeighbor(coordinate, direction) is null)
          {
            pins.Add(new SimulatorExternalPinViewModel(coordinate, direction, Engine));
          }
        }
      }
    }

    return pins;
  }
}

/// <summary>
/// One node's cell in the simulator's 18x8 grid -- a thin, refreshable wrapper over the engine's own
/// <see cref="F18NodeSimulationState"/> (never rebuilt; <see cref="Refresh"/> just re-announces every
/// bound property after each engine step, matching how <see cref="Views.SimulatorChipWindow"/> avoids
/// rebuilding the whole 144-item grid on every single tick).
/// </summary>
public sealed class SimulatorNodeCellViewModel : ObservableObject
{
  private static readonly Brush ErrorBackgroundBrush = NodeColorOption.BrushFromHex("#F8D7D7");
  private static readonly Brush ErrorBorderBrush = NodeColorOption.BrushFromHex("#B33A3A");
  private static readonly Brush HaltedBackgroundBrush = NodeColorOption.BrushFromHex("#E8E8E8");
  private static readonly Brush HaltedBorderBrush = NodeColorOption.BrushFromHex("#8A8A8A");

  private readonly Ga144SimulatorEngine _engine;
  private readonly string? _nodeColor;

  public SimulatorNodeCellViewModel(int coordinate, Ga144SimulatorEngine engine, string? nodeColor)
  {
    Coordinate = coordinate;
    _engine = engine;
    _nodeColor = nodeColor;
  }

  public int Coordinate { get; }
  public string CoordinateText => Coordinate.ToString("000");

  private F18NodeSimulationState State => _engine.Nodes[Coordinate];

  private string EffectiveColorHex => string.IsNullOrWhiteSpace(_nodeColor) ? NodeColorPalette.DefaultColor : _nodeColor;

  public Brush NodeBackgroundBrush => State.Error is not null
      ? ErrorBackgroundBrush
      : State.Halted
          ? HaltedBackgroundBrush
          : NodeColorOption.BrushFromHex(EffectiveColorHex);

  public Brush NodeBorderBrushColor => State.Error is not null
      ? ErrorBorderBrush
      : State.Halted
          ? HaltedBorderBrush
          : NodeColorOption.BrushFromHex(NodeColorPalette.Darken(EffectiveColorHex));

  public bool HasError => State.Error is not null;
  public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;

  /// <summary>The current instruction's mnemonic (the single slot about to execute next) -- Stefan's own
  /// requirement 1) "the current instruction". Full register/RAM/ROM detail lives in the per-node
  /// simulator window; this is just enough to glance at the whole chip at once.</summary>
  public string CurrentInstructionText
  {
    get
    {
      F18NodeSimulationState state = State;
      if (state.Error is not null)
      {
        return "error";
      }

      if (state.CurrentWord is null)
      {
        return state.PendingKind == F18PendingPortKind.Read && state.PendingOpcode == 0
            ? "fetch…" // port-execution fetch stalled -- DB001 3.3.2.
            : string.Empty;
      }

      if (state.SlotIndex >= state.CurrentWord.Slots.Count)
      {
        return string.Empty;
      }

      return state.CurrentWord.Slots[state.SlotIndex].ToString();
    }
  }

  /// <summary>Requirement 3) "a port read is displayed with an inbound arrow to the node."</summary>
  public Visibility InboundArrowVisibility =>
      State.PendingKind == F18PendingPortKind.Read && State.PendingOpcode != 0 ? Visibility.Visible : Visibility.Collapsed;

  /// <summary>Requirement 4) "a port write is displayed with an outbound arrow to the node."</summary>
  public Visibility OutboundArrowVisibility =>
      State.PendingKind == F18PendingPortKind.Write ? Visibility.Visible : Visibility.Collapsed;

  public string PendingDirectionsText
  {
    get
    {
      F18NodeSimulationState state = State;
      if (state.PendingKind == F18PendingPortKind.None)
      {
        return string.Empty;
      }

      IReadOnlyList<F18PortDirection> directions = Ga144SimulatorEngine.GetDirections(state.PendingAddress);
      return directions.Count == 0 ? string.Empty : string.Join("/", directions);
    }
  }

  public string ToolTip
  {
    get
    {
      F18NodeSimulationState state = State;
      if (state.Error is not null)
      {
        return $"Node {CoordinateText}: {state.Error}";
      }

      string pending = state.PendingKind switch
      {
        F18PendingPortKind.Read when state.PendingOpcode == 0 => "\nStalled fetching an instruction word via a comm port.",
        F18PendingPortKind.Read => $"\nStalled reading via {PendingDirectionsText}.",
        F18PendingPortKind.Write => $"\nStalled writing via {PendingDirectionsText}.",
        _ => string.Empty
      };

      return $"Node {CoordinateText}\nP: 0x{state.P:X3}   A: 0x{state.A:X5}   B: 0x{state.B:X5}\nNext: {CurrentInstructionText}{pending}\nClick to open this node's simulator window.";
    }
  }

  /// <summary>Re-announces every bound property -- called by <see cref="SimulatorChipViewModel"/> after
  /// every Reset/Step rather than rebuilding this cell (or the grid's own <c>Nodes</c> collection).</summary>
  public void Refresh() => OnPropertyChanged(null);
}

/// <summary>
/// One real chip pin in the simulator's external I/O panel -- an edge node's port direction that has no
/// physical GA144 neighbor (<see cref="Ga144SimulatorEngine.GetNeighbor"/> returns null). Per Stefan's own
/// scoping decision, this simulator shows such a pin's driven/read value but does not simulate any device
/// behind it -- see <see cref="Ga144SimulatorEngine"/>'s own remarks.
/// </summary>
public sealed class SimulatorExternalPinViewModel : ObservableObject
{
  private readonly Ga144SimulatorEngine _engine;
  private string _overrideText = string.Empty;

  public SimulatorExternalPinViewModel(int coordinate, F18PortDirection direction, Ga144SimulatorEngine engine)
  {
    Coordinate = coordinate;
    Direction = direction;
    _engine = engine;
    SetOverrideCommand = new RelayCommand(ApplyOverride);
    ClearOverrideCommand = new RelayCommand(ClearOverride);
  }

  public int Coordinate { get; }
  public F18PortDirection Direction { get; }
  public string Label => $"{Coordinate:000} {Direction}";

  /// <summary>Extra context for the three named SRAM-cluster nodes Stefan called out by name.</summary>
  public string? RoleHint => Coordinate switch
  {
    7 => "SRAM data",
    8 => "SRAM control",
    9 => "SRAM address",
    705 => "SPI boot",
    300 => "Sync boot",
    708 => "Async serial boot",
    _ => null
  };

  /// <summary>The hex text Stefan can type to drive this pin's next read -- applied via
  /// <see cref="SetOverrideCommand"/>, not live per keystroke, so a partially-typed value never takes
  /// effect early.</summary>
  public string OverrideText
  {
    get => _overrideText;
    set => SetProperty(ref _overrideText, value);
  }

  public RelayCommand SetOverrideCommand { get; }
  public RelayCommand ClearOverrideCommand { get; }

  private void ApplyOverride()
  {
    string text = OverrideText.Trim();
    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      text = text[2..];
    }

    if (text.Length == 0)
    {
      text = "0";
    }

    if (int.TryParse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int value))
    {
      _engine.SetExternalPinOverride(Coordinate, Direction, value);
      Refresh();
    }
  }

  private void ClearOverride()
  {
    _engine.SetExternalPinOverride(Coordinate, Direction, null);
    Refresh();
  }

  public string CurrentOverrideText
  {
    get
    {
      int? value = _engine.GetExternalPinOverride(Coordinate, Direction);
      return value is int v ? $"0x{v:X5}" : "(none -- reads stall)";
    }
  }

  public string LastWrittenText
  {
    get
    {
      int? value = _engine.GetExternalPinLastWritten(Coordinate, Direction);
      return value is int v ? $"0x{v:X5}" : "--";
    }
  }

  public Visibility PendingReadVisibility => IsPending(F18PendingPortKind.Read) ? Visibility.Visible : Visibility.Collapsed;
  public Visibility PendingWriteVisibility => IsPending(F18PendingPortKind.Write) ? Visibility.Visible : Visibility.Collapsed;

  private bool IsPending(F18PendingPortKind kind)
  {
    F18NodeSimulationState state = _engine.Nodes[Coordinate];
    return state.PendingKind == kind && Ga144SimulatorEngine.GetDirections(state.PendingAddress).Contains(Direction);
  }

  public void Refresh() => OnPropertyChanged(null);
}
