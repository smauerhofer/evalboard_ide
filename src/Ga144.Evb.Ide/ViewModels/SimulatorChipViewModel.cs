using System.Windows;
using System.Windows.Media;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Simulator;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// Drives the "GA144 simulator" window (<see cref="Views.SimulatorChipWindow"/>): an 18x8 grid of every
/// node's live, steppable state (<see cref="Ga144SimulatorEngine"/>), the Reset/Preset/Step 1/Step 10/Step
/// 100/Go/Halt controls, and the external I/O pin panel. Built at Stefan's own request, 2026-09-23 (Reset
/// split into "Reset"/"Preset" the same day, per Stefan's own follow-up -- see <see cref="ResetCommand"/>/
/// <see cref="PresetCommand"/>'s own remarks) -- see <see cref="Ga144SimulatorEngine"/>'s own remarks for
/// the full design (tick/tock stepping, opcode semantics, port I/O scope).
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
    (DigitalPins, TextFieldPins) = BuildExternalPins();

    ResetCommand = new RelayCommand(Reset);
    PresetCommand = new RelayCommand(Preset);
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

  /// <summary>One row per real chip GPIO/SERDES pin (a single bit of an edge node's port, per
  /// <see cref="Ga144PinCatalog"/>) -- radio-button driven ("in"/"hi"/"low"/"pull", Stefan's own
  /// 2026-09-23 follow-up request), named with the chip's own pin names (e.g. "705.17") rather than
  /// "coordinate direction".</summary>
  public IReadOnlyList<SimulatorDigitalPinViewModel> DigitalPins { get; }

  /// <summary>One row per pin whose value is a whole word rather than a single bit -- the analog nodes'
  /// ai/ao pins (an 18-bit "value in the F18 range", Stefan's own wording) and the SRAM cluster's own
  /// 18-bit "data" (node 007) and "address" (node 009) buses. All three share the same hex-textfield
  /// widget this project already used for every external pin before the 2026-09-23 follow-up split it
  /// into this list and <see cref="DigitalPins"/>.</summary>
  public IReadOnlyList<SimulatorExternalPinViewModel> TextFieldPins { get; }

  public string StatusText
  {
    get => _statusText;
    private set => SetProperty(ref _statusText, value);
  }

  /// <summary>Performs a bare hardware reset (<see cref="Ga144SimulatorEngine.Reset"/>) -- registers and
  /// RAM cleared, nothing of this project's own loaded, but each node's real factory ROM is still compiled
  /// and loaded (real silicon always runs it regardless of RAM state). Stefan's own split (2026-09-23) of
  /// what used to be one "Reset" button into this and <see cref="PresetCommand"/>.</summary>
  public RelayCommand ResetCommand { get; }

  /// <summary>Resets AND loads this project (<see cref="Ga144SimulatorEngine.Preset"/>) -- what the
  /// "Reset" button used to do before Stefan's 2026-09-23 split: compile every node and initialize
  /// registers as if freshly reset and then loaded by the boot stream.</summary>
  public RelayCommand PresetCommand { get; }

  public RelayCommand Step1Command { get; }
  public RelayCommand Step10Command { get; }
  public RelayCommand Step100Command { get; }
  public AsyncRelayCommand GoCommand { get; }
  public RelayCommand HaltCommand { get; }

  /// <summary>Raised after every Reset/Preset/Step/Go tick so the window can refresh anything it owns
  /// beyond the grid cells themselves (currently: any open <see cref="Views.SimulatorNodeWindow"/>).</summary>
  public event EventHandler? Stepped;

  /// <summary>
  /// Builds the detail view model for one node's simulator window. The engine itself only keeps compiled
  /// RAM/ROM WORDS (see <see cref="Ga144SimulatorEngine.Preset"/>) -- this recompiles that one node, same
  /// as <see cref="PostMortemChipViewModel.BuildNodeDetail"/> already does for the post-mortem window,
  /// purely to recover its symbol table for the RAM/ROM disassembly's "Label" column. A node whose source
  /// no longer compiles the same way it did at the last Preset just shows no labels rather than failing to
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
    StatusText = $"Reset. Registers/RAM cleared; factory ROM loaded. {Engine.Nodes.Values.Count(n => n.Error is not null)} node(s) failed to compile their ROM (see individual nodes).";
    RefreshAll();
    NotifyCommandsCanExecuteChanged();
  }

  private void Preset()
  {
    Engine.Preset();
    StatusText = $"Preset. {Engine.Nodes.Values.Count(n => n.Error is not null)} node(s) failed to compile (see individual nodes).";
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

    foreach (SimulatorDigitalPinViewModel pin in DigitalPins)
    {
      pin.Refresh();
    }

    foreach (SimulatorExternalPinViewModel pin in TextFieldPins)
    {
      pin.Refresh();
    }

    Stepped?.Invoke(this, EventArgs.Empty);
  }

  /// <summary>
  /// Walks every (coordinate, direction) with no in-grid neighbor -- exactly the same "real chip pin"
  /// test <see cref="Ga144SimulatorEngine.GetNeighbor"/> always used -- and, for each one, looks up
  /// <see cref="Ga144PinCatalog"/> to find out which named physical pin(s) actually live there (falling
  /// back to a generic "&lt;coordinate&gt;.17" name for an edge direction the catalog does not know about,
  /// per that class's own remarks). A <see cref="PinKind.Digital"/> pin becomes one
  /// <see cref="SimulatorDigitalPinViewModel"/> (radio buttons); <see cref="PinKind.Analog"/> and
  /// <see cref="PinKind.BusWord"/> pins both become one <see cref="SimulatorExternalPinViewModel"/>
  /// (the pre-existing hex textfield) -- Stefan's own 2026-09-23 follow-up request.
  /// </summary>
  private (List<SimulatorDigitalPinViewModel> Digital, List<SimulatorExternalPinViewModel> TextField) BuildExternalPins()
  {
    var digitalPins = new List<SimulatorDigitalPinViewModel>();
    var textFieldPins = new List<SimulatorExternalPinViewModel>();

    void AddDigitalPin(int coordinate, F18PortDirection direction, PinSpec pin)
    {
      var group = new DigitalPinGroup(Engine, coordinate, direction);
      var digital = new SimulatorDigitalPinViewModel(coordinate, direction, pin.Name, pin.RoleHint, pin.BitIndex, group, Engine);
      group.Register(digital);
      group.Recompute();
      digitalPins.Add(digital);
    }

    for (int row = 0; row < 8; row++)
    {
      for (int column = 0; column < 18; column++)
      {
        int coordinate = row * 100 + column;
        IReadOnlyList<F18PortDirection> externalDirections = Enum.GetValues<F18PortDirection>()
            .Where(direction => Engine.GetNeighbor(coordinate, direction) is null)
            .ToList();

        if (externalDirections.Count == 0)
        {
          continue;
        }

        // Every node has 2, 3, or 4 neighbors (a corner has 2, a plain edge node has 3, an interior node
        // has 4 -- Stefan's own correction, 2026-09-23), so a corner is the only place more than one
        // direction can be externally open at once: exactly the four corners 000, 017, 700, 717.
        IReadOnlyList<PinSpec>? catalogPins = Ga144PinCatalog.TryResolve(coordinate);
        if (catalogPins is null)
        {
          // No named pin at all for this coordinate (the great majority of the grid's edge nodes; on
          // real G144A12 silicon most are simply unbonded -- DB002 2.3's "Basic" rows). Every externally
          // open direction gets its own generic fallback pin (this class's own remarks on why), each
          // named distinctly when there is more than one (a corner) so two rows never share one name.
          bool suffixDirection = externalDirections.Count > 1;
          foreach (F18PortDirection direction in externalDirections)
          {
            AddDigitalPin(coordinate, direction, Ga144PinCatalog.Fallback(coordinate, suffixDirection ? direction : null));
          }

          continue;
        }

        // DB002 2.3's own configuration table already names R/D/L/U pins using this project's own LOCAL
        // direction convention (F18NodeSimulationState's own remarks on F18PortDirection), so the
        // catalog can name the exact direction a pin sits on with no separate mirroring/translation step
        // -- Ga144PinCatalog.PreferredDirection returns it for the one node (717) that needs it; every
        // other catalog coordinate has only one externally open direction, found automatically.
        F18PortDirection primaryDirection = Ga144PinCatalog.PreferredDirection(coordinate) ?? externalDirections[0];
        DigitalPinGroup? group = null;

        foreach (PinSpec pin in catalogPins)
        {
          switch (pin.Kind)
          {
            case PinKind.Digital:
              group ??= new DigitalPinGroup(Engine, coordinate, primaryDirection);
              var digital = new SimulatorDigitalPinViewModel(coordinate, primaryDirection, pin.Name, pin.RoleHint, pin.BitIndex, group, Engine);
              group.Register(digital);
              digitalPins.Add(digital);
              break;
            case PinKind.Analog:
            case PinKind.BusWord:
              textFieldPins.Add(new SimulatorExternalPinViewModel(coordinate, primaryDirection, pin.Name, pin.RoleHint, Engine));
              break;
          }
        }

        group?.Recompute();

        foreach (F18PortDirection extraDirection in externalDirections.Where(direction => direction != primaryDirection))
        {
          // A corner's OTHER externally-open direction (717 has one real Analog pin on its "U", per
          // PreferredDirection above, and its plain unbonded "L" lands here) always falls back to a
          // generic digital pin -- there is nothing left in the catalog to attach to it. Always suffixed
          // with the direction here, since the coordinate's "real" pin above already used the plain name.
          AddDigitalPin(coordinate, extraDirection, Ga144PinCatalog.Fallback(coordinate, extraDirection));
        }
      }
    }

    return (digitalPins, textFieldPins);
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

  /// <summary>Requirements 3/4 ("a port read/write is displayed with an inbound/outbound arrow to the
  /// node"), refined at Stefan's own follow-up request (2026-09-23) into one pair of read/write arrows
  /// PER direction, rendered in the grid's own gutter between neighboring nodes (see
  /// <see cref="Views.SimulatorChipWindow"/>'s DataTemplate) rather than two generic corner glyphs -- so
  /// the arrow itself now shows which of the four ports (Up/Down/Left/Right) is actually stalled, not
  /// just that some port is.</summary>
  public Visibility UpReadVisibility => DirectionPending(F18PortDirection.Up) == F18PendingPortKind.Read ? Visibility.Visible : Visibility.Collapsed;
  public Visibility UpWriteVisibility => DirectionPending(F18PortDirection.Up) == F18PendingPortKind.Write ? Visibility.Visible : Visibility.Collapsed;
  public Visibility DownReadVisibility => DirectionPending(F18PortDirection.Down) == F18PendingPortKind.Read ? Visibility.Visible : Visibility.Collapsed;
  public Visibility DownWriteVisibility => DirectionPending(F18PortDirection.Down) == F18PendingPortKind.Write ? Visibility.Visible : Visibility.Collapsed;
  public Visibility LeftReadVisibility => DirectionPending(F18PortDirection.Left) == F18PendingPortKind.Read ? Visibility.Visible : Visibility.Collapsed;
  public Visibility LeftWriteVisibility => DirectionPending(F18PortDirection.Left) == F18PendingPortKind.Write ? Visibility.Visible : Visibility.Collapsed;
  public Visibility RightReadVisibility => DirectionPending(F18PortDirection.Right) == F18PendingPortKind.Read ? Visibility.Visible : Visibility.Collapsed;
  public Visibility RightWriteVisibility => DirectionPending(F18PortDirection.Right) == F18PendingPortKind.Write ? Visibility.Visible : Visibility.Collapsed;

  /// <summary>Stefan's own "RDLU" port indicator (2026-09-23): a fixed "RDLU" header (Right/Down/Left/Up
  /// -- the literal order he asked for) with this one line directly below it, one status character per
  /// direction in that same order -- 'r' reading, 'W' writing, '-' idle. Rendered in <c>Consolas</c> in
  /// both this and the header so the two lines line up column-for-column.</summary>
  public string PortStatusText => new(
  [
    CharFor(DirectionPending(F18PortDirection.Right)),
    CharFor(DirectionPending(F18PortDirection.Down)),
    CharFor(DirectionPending(F18PortDirection.Left)),
    CharFor(DirectionPending(F18PortDirection.Up))
  ]);

  private static char CharFor(F18PendingPortKind? kind) => kind switch
  {
    F18PendingPortKind.Read => 'r',
    F18PendingPortKind.Write => 'W',
    _ => '-'
  };

  /// <summary>Null unless this node's next slot is stalled on a port op that involves <paramref name="direction"/>
  /// specifically (a multiport address can name more than one direction at once -- DB001 3.3/3.3.1's
  /// read-any/write-all rules -- so more than one direction can come back non-null for the same pending
  /// op). Deliberately returns null (idle) for a stalled instruction-word fetch via a comm port
  /// (<see cref="F18NodeSimulationState.PendingOpcode"/> == 0, DB001 3.3.2) -- that is not an explicit
  /// @/! op on any one direction, and is already shown via <see cref="CurrentInstructionText"/>'s own
  /// "fetch…" text instead, matching this cell's pre-existing (now-removed) generic inbound-arrow rule.</summary>
  private F18PendingPortKind? DirectionPending(F18PortDirection direction)
  {
    F18NodeSimulationState state = State;
    if (state.PendingKind == F18PendingPortKind.None)
    {
      return null;
    }

    if (state.PendingKind == F18PendingPortKind.Read && state.PendingOpcode == 0)
    {
      return null;
    }

    return Ga144SimulatorEngine.GetDirections(state.PendingAddress).Contains(direction) ? state.PendingKind : null;
  }

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
/// One real chip pin in the simulator's I/O panel whose value is a whole word rather than a single bit --
/// an analog node's ai/ao pin, or the SRAM cluster's own 18-bit "data" (node 007)/"address" (node 009)
/// bus -- <see cref="Ga144PinCatalog"/> decides which. Per Stefan's own scoping decision, this simulator
/// shows such a pin's driven/read value but does not simulate any device behind it -- see
/// <see cref="Ga144SimulatorEngine"/>'s own remarks.
/// </summary>
public sealed class SimulatorExternalPinViewModel : ObservableObject
{
  private readonly Ga144SimulatorEngine _engine;
  private string _overrideText = string.Empty;

  public SimulatorExternalPinViewModel(int coordinate, F18PortDirection direction, string label, string? roleHint, Ga144SimulatorEngine engine)
  {
    Coordinate = coordinate;
    Direction = direction;
    Label = label;
    RoleHint = roleHint;
    _engine = engine;
    SetOverrideCommand = new RelayCommand(ApplyOverride);
    ClearOverrideCommand = new RelayCommand(ClearOverride);
  }

  public int Coordinate { get; }
  public F18PortDirection Direction { get; }

  /// <summary>The chip's own pin name (e.g. "709.ai"), or "data"/"address" for nodes 007/009's own 18-bit
  /// buses -- never "coordinate direction" (Stefan's own 2026-09-23 follow-up request).</summary>
  public string Label { get; }

  public string? RoleHint { get; }

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

/// <summary>What kind of widget one named physical pin needs -- decided by <see cref="Ga144PinCatalog"/>,
/// at Stefan's own 2026-09-23 follow-up request.</summary>
public enum PinKind
{
  /// <summary>A single bit, driven by four radio buttons ("in"/"hi"/"low"/"pull" -- <see cref="DigitalPinMode"/>,
  /// PB004/DB001's own four GPIO drive states).</summary>
  Digital,

  /// <summary>An analog node's own ai/ao pin -- a whole word, shown with the pre-existing hex textfield
  /// (<see cref="SimulatorExternalPinViewModel"/>), Stefan's own "values in the F18 range" wording.</summary>
  Analog,

  /// <summary>The SRAM cluster's own 18-bit "data" (node 007) or "address" (node 009) bus -- also a whole
  /// word, also the hex textfield, just under its own plain name rather than a "xxx.ai" pin name.</summary>
  BusWord
}

/// <summary>One named physical GA144 pin, as <see cref="Ga144PinCatalog"/> describes it.
/// <paramref name="BitIndex"/> only matters for <see cref="PinKind.Digital"/> -- the bit position within
/// the node's own <c>io</c> register this pin reads/drives (DB001/PB004: "identified by the bit position
/// in io at which their state is read"; pin 17 is always assigned first, then 1, 3, 5 for a node with
/// more than one such pin).</summary>
public sealed record PinSpec(string Name, PinKind Kind, string? RoleHint, int BitIndex = 0);

/// <summary>
/// The chip's own real pin names and types for every GA144 edge pin this simulator can show, transcribed
/// from the project's own attached chip reference (DB002 "G144A12 Chip Reference", sections 2.2 Pin
/// Functions and 2.3 Configuration of each node) and the EVB001/EVB002 board references (DB003/DB014,
/// which name the same pins again against their own header/jumper labels) -- built at Stefan's own
/// 2026-09-23 follow-up request to stop showing pins as "007 Up" and use the chip's own names instead.
///
/// Keyed by coordinate: <see cref="SimulatorChipViewModel.BuildExternalPins"/> finds which direction(s)
/// of a coordinate actually have no in-grid neighbor at runtime (exactly the existing
/// <see cref="Ga144SimulatorEngine.GetNeighbor"/> test -- every node has 2, 3, or 4 neighbors, so only a
/// corner ever has more than one open direction at once) and attaches this catalog's pins to that one
/// direction, or to whichever direction <see cref="PreferredDirection"/> names for the rare coordinate
/// (717) where more than one direction is open and they are not interchangeable.
///
/// A coordinate not listed here (the great majority of the grid's edge nodes) still gets one row, via
/// <see cref="Fallback"/>: a single generic "&lt;coordinate&gt;.17" digital pin. On real G144A12 silicon
/// most of these are simply unbonded (no package pin at all, DB002 2.3's "Basic" rows) -- this simulator
/// still gives them a nameable, drivable pin for testing purposes, on the same architectural basis as a
/// DB001 3.4.3 "phantom" pin (bit 17 of a port address that has no real neighbor), clearly labeled so as
/// not to be mistaken for a real EVB header signal.
/// </summary>
public static class Ga144PinCatalog
{
  private static readonly IReadOnlyDictionary<int, IReadOnlyList<PinSpec>> Entries = new Dictionary<int, IReadOnlyList<PinSpec>>
  {
    // Single-pin general purpose GPIO nodes (DB002 2.2/2.3: "General purpose 1-pin nodes").
    [100] = new[] { new PinSpec("100.17", PinKind.Digital, "General purpose GPIO", 17) },
    [200] = new[] { new PinSpec("200.17", PinKind.Digital, "General purpose GPIO", 17) },
    [500] = new[] { new PinSpec("500.17", PinKind.Digital, "General purpose GPIO -- may reset the Target chip (EVB)", 17) },
    [600] = new[] { new PinSpec("600.17", PinKind.Digital, "General purpose GPIO -- may select the expanded SPI bus (EVB)", 17) },
    [317] = new[] { new PinSpec("317.17", PinKind.Digital, "General purpose GPIO", 17) },
    [417] = new[] { new PinSpec("417.17", PinKind.Digital, "General purpose GPIO", 17) },
    [715] = new[] { new PinSpec("715.17", PinKind.Digital, "Shared (read-only) by analog nodes 709/713/717", 17) },
    [517] = new[] { new PinSpec("517.17", PinKind.Digital, "Shared (read-only) by analog node 617", 17) },
    [217] = new[] { new PinSpec("217.17", PinKind.Digital, "Shared (read-only) by analog node 117", 17) },

    // Boot / dedicated multi-pin GPIO nodes (DB002 2.2/2.3, DB003/DB014 8.2.2).
    [300] = new[]
    {
      new PinSpec("300.17", PinKind.Digital, "Synchronous boot: clock", 17),
      new PinSpec("300.1", PinKind.Digital, "Synchronous boot: data", 1)
    },
    [705] = new[]
    {
      new PinSpec("705.17", PinKind.Digital, "SPI boot: Data In", 17),
      new PinSpec("705.5", PinKind.Digital, "SPI boot: Data Out", 5),
      new PinSpec("705.3", PinKind.Digital, "SPI boot: Chip Enable-", 3),
      new PinSpec("705.1", PinKind.Digital, "SPI boot: Clock", 1)
    },
    [708] = new[]
    {
      new PinSpec("708.17", PinKind.Digital, "Asynchronous boot: Rx (input)", 17),
      new PinSpec("708.1", PinKind.Digital, "Asynchronous boot: Tx (output)", 1)
    },
    [8] = new[]
    {
      new PinSpec("008.17", PinKind.Digital, "SRAM control", 17),
      new PinSpec("008.5", PinKind.Digital, "SRAM control", 5),
      new PinSpec("008.3", PinKind.Digital, "SRAM control", 3),
      new PinSpec("008.1", PinKind.Digital, "SRAM control", 1)
    },

    // SERDES nodes: still a pair of real physical pins (clock/data), but no SERDES bit-stream timing is
    // simulated here, so they are shown as plain digital pins rather than a dedicated SERDES widget.
    [1] = new[]
    {
      new PinSpec("001.17", PinKind.Digital, "SERDES clock (bit-stream timing not simulated)", 17),
      new PinSpec("001.1", PinKind.Digital, "SERDES data (bit-stream timing not simulated)", 1)
    },
    [701] = new[]
    {
      new PinSpec("701.17", PinKind.Digital, "SERDES clock (bit-stream timing not simulated)", 17),
      new PinSpec("701.1", PinKind.Digital, "SERDES data (bit-stream timing not simulated)", 1)
    },

    // Analog nodes (DB002 2.2/2.3) -- a textfield holding an F18-range value, not radio buttons.
    [709] = new[] { new PinSpec("709.ai", PinKind.Analog, "Analog In (VDDA bus)"), new PinSpec("709.ao", PinKind.Analog, "Analog Out (VDDA bus)") },
    [713] = new[] { new PinSpec("713.ai", PinKind.Analog, "Analog In (VDDA bus)"), new PinSpec("713.ao", PinKind.Analog, "Analog Out (VDDA bus)") },
    [717] = new[] { new PinSpec("717.ai", PinKind.Analog, "Analog In (VDDA bus)"), new PinSpec("717.ao", PinKind.Analog, "Analog Out (VDDA bus)") },
    [617] = new[] { new PinSpec("617.ai", PinKind.Analog, "Analog In (VDDI bus)"), new PinSpec("617.ao", PinKind.Analog, "Analog Out (VDDI bus)") },
    [117] = new[] { new PinSpec("117.ai", PinKind.Analog, "Analog In (VDDI bus)"), new PinSpec("117.ao", PinKind.Analog, "Analog Out (VDDI bus)") },

    // The SRAM Control Cluster's own 18-bit parallel buses (DB002 2.2, AN003) -- one textfield each
    // (Stefan's own naming: "data" / "address"), not one row per bit (d00-d17 / a00-a17).
    [7] = new[] { new PinSpec("data", PinKind.BusWord, "Node 007 UP port -- SRAM data bus (18 bits)") },
    [9] = new[] { new PinSpec("address", PinKind.BusWord, "Node 009 UP port -- SRAM address bus (18 bits)") }
  };

  /// <summary>Which LOCAL direction (DB002 2.3's own R/D/L/U notation, the same convention as this
  /// project's own <see cref="F18PortDirection"/> -- see <see cref="SimulatorChipViewModel.BuildExternalPins"/>'s
  /// own remarks) this coordinate's catalog pins sit on, for the handful of nodes where more than one
  /// direction is externally open at once and it is NOT simply "whichever one is found". Only a corner
  /// (2 neighbors, 2 open directions) can need this; among this catalog's own entries only node 717 does
  /// -- its "L" is a plain unbonded corner edge (DB002 2.3's own "RD--" pattern) and its "U" carries the
  /// real Analog pin. Every other coordinate returns null here and is resolved by simply finding its one
  /// open direction at runtime.</summary>
  private static readonly IReadOnlyDictionary<int, F18PortDirection> PreferredDirections = new Dictionary<int, F18PortDirection>
  {
    [717] = F18PortDirection.Up
  };

  public static F18PortDirection? PreferredDirection(int coordinate) =>
      PreferredDirections.TryGetValue(coordinate, out F18PortDirection direction) ? direction : null;

  /// <summary>This catalog's own named pins for a coordinate, or null if it does not name that
  /// coordinate at all (the caller then falls back to <see cref="Fallback"/> for every externally-open
  /// direction it finds -- see <see cref="SimulatorChipViewModel.BuildExternalPins"/>).</summary>
  public static IReadOnlyList<PinSpec>? TryResolve(int coordinate) =>
      Entries.TryGetValue(coordinate, out IReadOnlyList<PinSpec>? pins) ? pins : null;

  /// <summary>A generic single-bit digital pin for a coordinate/direction this catalog does not
  /// otherwise name -- see this class's own remarks on why such a coordinate still gets a nameable,
  /// drivable pin. <paramref name="direction"/> is appended to disambiguate the rare case (a corner)
  /// where the same coordinate gets more than one of these; left null for the ordinary one-pin case, so
  /// that pin keeps the plain "&lt;coordinate&gt;.17" name.</summary>
  public static PinSpec Fallback(int coordinate, F18PortDirection? direction = null) => new(
      direction is null ? $"{coordinate:000}.17" : $"{coordinate:000}.17 ({direction})",
      PinKind.Digital,
      null,
      17);
}

/// <summary>The four states DB001 (Figure 10, "F18A GPIO Pin Circuit") and PB004/PB006 define for every
/// GPIO pin's own drive circuitry -- the literal wording Stefan asked for ("in"/"hi"/"low"/"pull"). Reset
/// state is always <see cref="Pull"/> ("weak pulldown... the reset state of all GPIO pins").</summary>
public enum DigitalPinMode
{
  /// <summary>High impedance (tristate, ~2.8pF/~250MΩ) -- no drive at all, from either the node or this
  /// simulator's external side. Matches this project's pre-existing "no override set" convention: a read
  /// stalls rather than resolving to a guessed value.</summary>
  In,

  /// <summary>This simulator's external side drives the pin to VDD (logic 1), regardless of what the
  /// node's own program has set its drive state to -- a testbench-style forced stimulus.</summary>
  Hi,

  /// <summary>This simulator's external side drives the pin to VSS (logic 0) -- a forced stimulus, same
  /// as <see cref="Hi"/> but the opposite level.</summary>
  Low,

  /// <summary>Weak pulldown (~38µA) -- the chip's own reset default. Reads as logic 0 here (this
  /// simulator does not model drive strength/contention), but kept as its own state, distinct from
  /// <see cref="Low"/>, so the UI can show which one Stefan actually chose.</summary>
  Pull
}

/// <summary>
/// Combines however many <see cref="SimulatorDigitalPinViewModel"/>s share one (coordinate, direction)
/// port address (a multi-pin GPIO node such as 705 has up to four -- DB002 2.3's "GPIOx4") into the single
/// 18-bit word <see cref="Ga144SimulatorEngine.SetExternalPinOverride"/> actually stores per port: each
/// <see cref="DigitalPinMode.Hi"/> member contributes a 1 bit at its own <see cref="PinSpec.BitIndex"/>;
/// every other member contributes 0. If every member is <see cref="DigitalPinMode.In"/> the whole group
/// clears its override (a read stalls, this project's own established "no value supplied" convention) --
/// otherwise the composed word is pushed even for members still <see cref="DigitalPinMode.In"/>, since the
/// engine only has room for one word per port and cannot keep an individual bit floating once ANY sibling
/// bit forces the word to resolve. This is a simulator-only simplification, not a modeled 4-state-per-bit
/// bus; it is called out here rather than silently, so it's easy to find if it ever needs revisiting.
/// </summary>
public sealed class DigitalPinGroup(Ga144SimulatorEngine engine, int coordinate, F18PortDirection direction)
{
  private readonly List<SimulatorDigitalPinViewModel> _members = [];

  public void Register(SimulatorDigitalPinViewModel pin) => _members.Add(pin);

  public void Recompute()
  {
    int word = 0;
    bool anyDriven = false;
    foreach (SimulatorDigitalPinViewModel pin in _members)
    {
      if (pin.Mode == DigitalPinMode.In)
      {
        continue;
      }

      anyDriven = true;
      if (pin.Mode == DigitalPinMode.Hi)
      {
        word |= 1 << pin.BitIndex;
      }
    }

    engine.SetExternalPinOverride(coordinate, direction, anyDriven ? word : null);
  }
}

/// <summary>
/// One named, single-bit GA144 GPIO/SERDES pin in the simulator's I/O panel -- Stefan's own 2026-09-23
/// follow-up request: radio buttons for "in"/"hi"/"low"/"pull" (<see cref="DigitalPinMode"/>) in place of
/// the free-form hex textfield every external pin used before, and the chip's own pin name (e.g. "705.5")
/// in place of "coordinate direction". Several of these can share one <see cref="DigitalPinGroup"/> when
/// more than one physical pin lives on the same port address (see that class's own remarks).
/// </summary>
public sealed class SimulatorDigitalPinViewModel : ObservableObject
{
  private readonly Ga144SimulatorEngine _engine;
  private readonly int _coordinate;
  private readonly F18PortDirection _direction;
  private readonly DigitalPinGroup _group;
  private DigitalPinMode _mode = DigitalPinMode.Pull;

  public SimulatorDigitalPinViewModel(int coordinate, F18PortDirection direction, string label, string? roleHint, int bitIndex, DigitalPinGroup group, Ga144SimulatorEngine engine)
  {
    _coordinate = coordinate;
    _direction = direction;
    _engine = engine;
    Label = label;
    RoleHint = roleHint;
    BitIndex = bitIndex;
    _group = group;
  }

  public string Label { get; }
  public string? RoleHint { get; }
  public int BitIndex { get; }
  public DigitalPinMode Mode => _mode;

  public bool IsIn { get => _mode == DigitalPinMode.In; set { if (value) SetMode(DigitalPinMode.In); } }
  public bool IsHi { get => _mode == DigitalPinMode.Hi; set { if (value) SetMode(DigitalPinMode.Hi); } }
  public bool IsLow { get => _mode == DigitalPinMode.Low; set { if (value) SetMode(DigitalPinMode.Low); } }
  public bool IsPull { get => _mode == DigitalPinMode.Pull; set { if (value) SetMode(DigitalPinMode.Pull); } }

  private void SetMode(DigitalPinMode mode)
  {
    if (_mode == mode)
    {
      return;
    }

    _mode = mode;
    _group.Recompute();
    OnPropertyChanged(null);
  }

  public Visibility PendingReadVisibility => IsPending(F18PendingPortKind.Read) ? Visibility.Visible : Visibility.Collapsed;
  public Visibility PendingWriteVisibility => IsPending(F18PendingPortKind.Write) ? Visibility.Visible : Visibility.Collapsed;

  private bool IsPending(F18PendingPortKind kind)
  {
    F18NodeSimulationState state = _engine.Nodes[_coordinate];
    return state.PendingKind == kind && Ga144SimulatorEngine.GetDirections(state.PendingAddress).Contains(_direction);
  }

  public void Refresh() => OnPropertyChanged(null);
}