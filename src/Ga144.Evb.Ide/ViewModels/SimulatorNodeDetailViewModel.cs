using System.Windows;
using System.Windows.Media;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Simulator;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>One row of a stack in the simulator node window -- same "T"/"S"/"R" top-label convention as
/// <see cref="PostMortemStackRow"/>.</summary>
public sealed record SimulatorStackRow(string Label, string ValueText);

/// <summary>One decoded RAM/ROM word row. <see cref="IsCurrentPosition"/> drives Stefan's own requirement
/// 5) "highlighting the current position P" -- true for the single word <see cref="F18NodeSimulationState.CurrentWordAddress"/>
/// currently points at (the word the node is stepping through slot-by-slot), false otherwise.</summary>
public sealed record SimulatorWordRow(string AddressHex, string? Label, string HexText, string Disassembly, bool IsCurrentPosition);

/// <summary>One of a node's (up to) four comm ports, for the "all ports" panel (Stefan's own requirement
/// 2) "all ports"). <see cref="TargetText"/> names the physical neighbor, or "external pin" at the array's
/// edge (see <see cref="Ga144SimulatorEngine"/>'s own remarks on that scope).</summary>
public sealed record SimulatorPortRow(
    string DirectionText,
    string TargetText,
    Visibility InboundVisibility,
    Visibility OutboundVisibility,
    string ValueText);

/// <summary>
/// Drives one non-modal simulator node window (<see cref="Views.SimulatorNodeWindow"/>): all registers
/// (T/S/R/A/B/P/IO/Carry), all ports, RAM, and ROM, refreshed live as the engine steps -- Stefan's own
/// five requirements for this window. Unlike <see cref="PostMortemNodeDetailViewModel"/> (a frozen
/// snapshot), <see cref="Refresh"/> re-reads the engine's live <see cref="F18NodeSimulationState"/> every
/// time it is called (after every Reset/Step/Go tick -- see <see cref="Views.SimulatorNodeWindow"/>'s own
/// code-behind for when that is).
/// </summary>
public sealed class SimulatorNodeDetailViewModel(int coordinate, Ga144SimulatorEngine engine, IReadOnlyDictionary<int, string> labelsByAddress) : ObservableObject
{
  public int Coordinate { get; } = coordinate;
  public string CoordinateText => $"{Coordinate:000}";

  public string TText { get; private set; } = "00000";
  public string SText { get; private set; } = "00000";
  public string RText { get; private set; } = "00000";
  public string AText { get; private set; } = "00000";
  public string BText { get; private set; } = "00000";
  public string PText { get; private set; } = "000";
  public string IoText { get; private set; } = "00000";
  public string CarryText { get; private set; } = "0";
  public string CurrentInstructionText { get; private set; } = string.Empty;
  public bool HasError { get; private set; }
  public string ErrorText { get; private set; } = string.Empty;
  public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;
  public string StatusText { get; private set; } = string.Empty;

  public IReadOnlyList<SimulatorStackRow> ParameterStackRows { get; private set; } = [];
  public IReadOnlyList<SimulatorStackRow> ReturnStackRows { get; private set; } = [];
  public IReadOnlyList<SimulatorWordRow> RamRows { get; private set; } = [];
  public IReadOnlyList<SimulatorWordRow> RomRows { get; private set; } = [];
  public IReadOnlyList<SimulatorPortRow> PortRows { get; private set; } = [];

  /// <summary>Re-reads the engine's live state for this node and re-announces every bound property in one
  /// shot (<c>OnPropertyChanged(null)</c> -- WPF's own "refresh every binding" convention).</summary>
  public void Refresh()
  {
    F18NodeSimulationState state = engine.Nodes[Coordinate];

    TText = PortAddressNames.Format(state.ParameterStack.Count > 0 ? state.ParameterStack[^1] : 0);
    SText = PortAddressNames.Format(state.ParameterStack.Count > 1 ? state.ParameterStack[^2] : 0);
    RText = PortAddressNames.Format(state.ReturnStack.Count > 0 ? state.ReturnStack[^1] : 0);
    AText = PortAddressNames.Format(state.A);
    BText = PortAddressNames.Format(state.B);
    PText = state.P.ToString("X3");
    IoText = state.Io.ToString("X5");
    CarryText = state.Carry.ToString();
    HasError = state.Error is not null;
    ErrorText = state.Error ?? string.Empty;
    CurrentInstructionText = BuildCurrentInstructionText(state);
    StatusText = BuildStatusText(state);

    ParameterStackRows = BuildStackRows(state.ParameterStack, ["T", "S"]);
    ReturnStackRows = BuildStackRows(state.ReturnStack, ["R"]);
    RamRows = BuildWordRows(state.Ram, 0x000, state);
    RomRows = BuildWordRows(state.Rom, 0x080, state);
    PortRows = BuildPortRows(state);

    OnPropertyChanged(null);
  }

  /// <summary>Plain-text dump of this node's registers and both stacks (top first), exactly as the window's
  /// own registers panel shows them -- same "Copy registers &amp; stacks" convention as
  /// <see cref="PostMortemNodeDetailViewModel"/>'s own clipboard button.</summary>
  public string BuildClipboardText()
  {
    var lines = new List<string>
    {
      $"Node {CoordinateText}",
      $"T:  {TText}",
      $"S:  {SText}",
      $"A:  {AText}",
      $"B:  {BText}",
      $"P:  {PText}",
      $"IO: {IoText}",
      $"Carry: {CarryText}",
      string.Empty,
      "Parameter stack (top first):"
    };

    foreach (SimulatorStackRow row in ParameterStackRows)
    {
      lines.Add($"  {row.Label}: {row.ValueText}");
    }

    lines.Add(string.Empty);
    lines.Add("Return stack (top first):");
    foreach (SimulatorStackRow row in ReturnStackRows)
    {
      lines.Add($"  {row.Label}: {row.ValueText}");
    }

    return string.Join(Environment.NewLine, lines);
  }

  private static string BuildCurrentInstructionText(F18NodeSimulationState state)
  {
    if (state.Error is not null)
    {
      return "(compile error -- see below)";
    }

    if (state.CurrentWord is null)
    {
      return state.PendingKind == F18PendingPortKind.Read && state.PendingOpcode == 0
          ? "(stalled: fetching an instruction word via a comm port)"
          : "(halted)";
    }

    if (state.SlotIndex >= state.CurrentWord.Slots.Count)
    {
      return "(word exhausted -- fetching next)";
    }

    return $"0x{state.CurrentWordAddress:X3} slot {state.SlotIndex}: {state.CurrentWord.Slots[state.SlotIndex]}";
  }

  private static string BuildStatusText(F18NodeSimulationState state) => state.PendingKind switch
  {
    F18PendingPortKind.Read when state.PendingOpcode == 0 => "Stalled fetching an instruction word via a comm port.",
    F18PendingPortKind.Read => $"Stalled reading via {string.Join("/", Ga144SimulatorEngine.GetDirections(state.PendingAddress))}.",
    F18PendingPortKind.Write => $"Stalled writing via {string.Join("/", Ga144SimulatorEngine.GetDirections(state.PendingAddress))}.",
    _ => state.Halted ? "Halted." : "Running."
  };

  // Storage is bottom-to-top (same convention as Models.PostMortemNodeSnapshot) -- labels are assigned
  // counting IN from the end, then reversed so the display shows the top of stack first.
  private static IReadOnlyList<SimulatorStackRow> BuildStackRows(IReadOnlyList<int> bottomToTop, IReadOnlyList<string> topLabels)
  {
    var rows = new List<SimulatorStackRow>(bottomToTop.Count);
    for (int index = 0; index < bottomToTop.Count; index++)
    {
      int fromTop = bottomToTop.Count - 1 - index;
      string label = fromTop < topLabels.Count ? topLabels[fromTop] : $"[{fromTop}]";
      rows.Add(new SimulatorStackRow(label, PortAddressNames.Format(bottomToTop[index])));
    }

    rows.Reverse();
    return rows;
  }

  private IReadOnlyList<SimulatorWordRow> BuildWordRows(IReadOnlyList<int> words, int baseAddress, F18NodeSimulationState state)
  {
    IReadOnlyList<F18DisassembledWord> decoded = F18Disassembler.DisassembleImage(words, baseAddress);
    bool hasCurrentWord = state.CurrentWord is not null;

    var rows = new List<SimulatorWordRow>(decoded.Count);
    foreach (F18DisassembledWord word in decoded)
    {
      string? label = labelsByAddress.TryGetValue(word.Address, out string? name) ? name : null;
      bool isCurrent = hasCurrentWord && word.Address == state.CurrentWordAddress;
      rows.Add(new SimulatorWordRow(
          word.Address.ToString("X3"),
          label,
          word.RawWord.ToString("X5"),
          word.Format(labelsByAddress),
          isCurrent));
    }

    return rows;
  }

  private IReadOnlyList<SimulatorPortRow> BuildPortRows(F18NodeSimulationState state)
  {
    var rows = new List<SimulatorPortRow>(4);
    foreach (F18PortDirection direction in Enum.GetValues<F18PortDirection>())
    {
      int? neighbor = engine.GetNeighbor(Coordinate, direction);
      bool pendingRead = state.PendingKind == F18PendingPortKind.Read &&
          Ga144SimulatorEngine.GetDirections(state.PendingAddress).Contains(direction);
      bool pendingWrite = state.PendingKind == F18PendingPortKind.Write &&
          Ga144SimulatorEngine.GetDirections(state.PendingAddress).Contains(direction);

      string targetText = neighbor is int n
          ? $"node {n:000}"
          : engine.GetExternalPinLastWritten(Coordinate, direction) is int written
              ? $"external pin -- last written 0x{written:X5}"
              : "external pin";

      rows.Add(new SimulatorPortRow(
          direction.ToString(),
          targetText,
          pendingRead ? Visibility.Visible : Visibility.Collapsed,
          pendingWrite ? Visibility.Visible : Visibility.Collapsed,
          pendingWrite ? $"0x{state.PendingValue:X5}" : string.Empty));
    }

    return rows;
  }
}
