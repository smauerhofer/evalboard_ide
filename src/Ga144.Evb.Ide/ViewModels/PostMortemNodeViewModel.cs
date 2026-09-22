using System.Windows;
using System.Windows.Media;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// One node's cell in the read-only post-mortem GA144 grid (<see cref="Views.PostMortemChipWindow"/>)
/// -- the post-mortem analogue of <see cref="NodeViewModel"/>, but backed by a captured
/// <see cref="PostMortemNodeSnapshot"/> instead of a live, editable <see cref="Ga144NodeConfiguration"/>.
/// Immutable: a fresh one is built for every node whenever the window's snapshot changes (Core Dump,
/// or Import).
///
/// <see cref="Snapshot"/> is null for node 708 (the Kraken head): a <see cref="PostMortemSnapshot"/>
/// never has an entry for it (no live-state read path of its own), but the grid still needs a cell
/// for coordinate 708 -- omitting it, rather than showing a distinct placeholder, would silently shift
/// every following node by one position in the fixed 18x8 grid (the bug this constructor overload
/// exists to avoid: <see cref="PostMortemChipViewModel"/> always builds exactly 144 of these, one per
/// coordinate, the same way <see cref="Ga144ChipConfiguration.Nodes"/> always has all 144 entries for
/// the live grid).
///
/// <b>ADDED 2026-09-22, per Stefan directly: "i want full dynamic bootstream and post-mortem analysis,
/// based on all the nodes that have been configured by the checkbox, thus belong to the current
/// project. no more static lists."</b> Kraken's own live wire walk still has to cover the fixed,
/// hardware-proven 3-tentacle topology regardless (see <see cref="PostMortemChipViewModel"/>'s own
/// remarks for why a genuinely dynamic per-branch wire walk is not safe to build) -- so what changed
/// here is what the capture is shown AS, not how it is taken.
///
/// <b>SUPERSEDED THE SAME DAY, per Stefan directly: "post-mortem is too slow now. i am not interested
/// in all nodes ... when reading post-mortem data, read only the selected nodes and grey out all other
/// nodes."</b> The graying above was originally driven by whether a node was "configured" for the CVM
/// boot stream (<see cref="Cvm.CvmBootStreamBuilder.GetConfiguredCoordinates"/>), checked live against
/// the current project. It is now driven instead by <see cref="Models.PostMortemNodeSnapshot.Included"/>
/// -- a NEW, separate per-node "include in post-mortem" checkbox
/// (<see cref="Models.Ga144NodeConfiguration.PostMortemEnabled"/>) that
/// <see cref="ViewModels.CvmDebuggerViewModel.CoreDumpAsync"/> now consults and records into the
/// snapshot itself at capture time. Reading the flag from the SNAPSHOT rather than re-querying the live
/// project is deliberate: a node's own checkbox may change after a dump is taken, but the dump should
/// keep showing exactly what it actually captured. A node with <c>Included == false</c> is shown in a
/// neutral gray rather than its own snapshot color, the same "colored means included, neutral means
/// not" convention <see cref="NodeViewModel"/> uses on the live GA144 grid for its own, unrelated
/// "configured for boot" distinction.
/// </summary>
public sealed class PostMortemNodeViewModel
{
  private static readonly Brush ErrorBackgroundBrush = NodeColorOption.BrushFromHex("#F8D7D7");
  private static readonly Brush ErrorBorderBrush = NodeColorOption.BrushFromHex("#B33A3A");
  private static readonly Brush HeadBackgroundBrush = NodeColorOption.BrushFromHex("#E8E8E8");
  private static readonly Brush HeadBorderBrushColor = NodeColorOption.BrushFromHex("#8A8A8A");

  // Same neutral swatches NodeViewModel uses for an unconfigured live node -- kept identical on
  // purpose, so "not part of this capture" reads the same way in both windows.
  private static readonly Brush NeutralBackgroundBrush = NodeColorOption.BrushFromHex("#F7FBF8");
  private static readonly Brush NeutralBorderBrush = NodeColorOption.BrushFromHex("#54705C");

  private readonly string _projectDefaultColor;

  public PostMortemNodeViewModel(int coordinate, PostMortemNodeSnapshot? snapshot, string projectDefaultColor)
  {
    Coordinate = coordinate;
    Snapshot = snapshot;
    _projectDefaultColor = string.IsNullOrWhiteSpace(projectDefaultColor) ? NodeColorPalette.DefaultColor : projectDefaultColor;
  }

  /// <summary>Null only for node 708 -- see this class's own remarks.</summary>
  public PostMortemNodeSnapshot? Snapshot { get; }

  public int Coordinate { get; }
  public int Row => Coordinate / 100;
  public int Column => Coordinate % 100;
  public string CoordinateText => $"{Coordinate:000}";

  public bool IsHeadPlaceholder => Snapshot is null;
  public bool HasError => Snapshot?.Error is not null;
  public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;

  /// <summary>Whether this node's own data was actually read during THIS capture -- see this class's
  /// own remarks. True (the default) for the 708 placeholder too, since <see cref="IsHeadPlaceholder"/>
  /// already takes priority everywhere this is consulted below.</summary>
  public bool IsIncluded => Snapshot?.Included ?? true;

  public Visibility NotIncludedVisibility => !IsHeadPlaceholder && !IsIncluded ? Visibility.Visible : Visibility.Collapsed;

  /// <summary>Same "own color, else the snapshot's project default" resolution as
  /// <see cref="NodeViewModel.EffectiveColorHex"/>, just read from a captured snapshot instead of a
  /// live project. Meaningless for the node-708 placeholder (<see cref="IsHeadPlaceholder"/>); its own
  /// colors below never consult this.</summary>
  public string EffectiveColorHex => string.IsNullOrWhiteSpace(Snapshot?.Color) ? _projectDefaultColor : Snapshot.Color;

  public Brush NodeBackgroundBrush => IsHeadPlaceholder
      ? HeadBackgroundBrush
      : HasError
          ? ErrorBackgroundBrush
          : !IsIncluded
              ? NeutralBackgroundBrush
              : NodeColorOption.BrushFromHex(EffectiveColorHex);

  public Brush NodeBorderBrushColor => IsHeadPlaceholder
      ? HeadBorderBrushColor
      : HasError
          ? ErrorBorderBrush
          : !IsIncluded
              ? NeutralBorderBrush
              : NodeColorOption.BrushFromHex(NodeColorPalette.Darken(EffectiveColorHex));

  public string ToolTip => IsHeadPlaceholder
      ? $"Node {CoordinateText} (Kraken head): no live-state read path of its own -- not captured."
      : HasError
          ? $"Node {CoordinateText}: read failed -- {Snapshot!.Error}"
          : !IsIncluded
              ? $"Node {CoordinateText}: not selected for post-mortem -- passed through by the tentacle wire walk but not read."
              : $"Node {CoordinateText}\nA: {PortAddressNames.Format(Snapshot!.A)}   IO: {Snapshot.Io:X5}\nClick for registers, both stacks, and a RAM/ROM disassembly.";
}