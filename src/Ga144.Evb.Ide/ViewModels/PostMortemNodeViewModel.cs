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
/// here is what the capture is shown AS, not how it is taken: <see cref="IsConfigured"/> now marks
/// which of the captured nodes are actually "configured" (enabled, or given source) in the CURRENT
/// project, live, using the exact same roster
/// <see cref="Cvm.CvmBootStreamBuilder.GetConfiguredCoordinates"/> computes for the boot stream. A node
/// captured but NOT configured (most of silicon, on a project that only uses a fraction of the mesh) is
/// shown in a neutral gray rather than its own snapshot color, the same "colored means configured,
/// neutral means not" convention <see cref="NodeViewModel"/> already uses for the live GA144 grid.
/// </summary>
public sealed class PostMortemNodeViewModel
{
  private static readonly Brush ErrorBackgroundBrush = NodeColorOption.BrushFromHex("#F8D7D7");
  private static readonly Brush ErrorBorderBrush = NodeColorOption.BrushFromHex("#B33A3A");
  private static readonly Brush HeadBackgroundBrush = NodeColorOption.BrushFromHex("#E8E8E8");
  private static readonly Brush HeadBorderBrushColor = NodeColorOption.BrushFromHex("#8A8A8A");

  // Same neutral swatches NodeViewModel uses for an unconfigured live node -- kept identical on
  // purpose, so "not part of the current project" reads the same way in both windows.
  private static readonly Brush NeutralBackgroundBrush = NodeColorOption.BrushFromHex("#F7FBF8");
  private static readonly Brush NeutralBorderBrush = NodeColorOption.BrushFromHex("#54705C");

  private readonly string _projectDefaultColor;

  public PostMortemNodeViewModel(int coordinate, PostMortemNodeSnapshot? snapshot, string projectDefaultColor, bool? isConfigured = null)
  {
    Coordinate = coordinate;
    Snapshot = snapshot;
    IsConfigured = isConfigured;
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

  /// <summary>Whether this node is "configured" (enabled, or given source) in the live project passed
  /// into <see cref="PostMortemChipViewModel"/> at construction -- see this class's own remarks. Null
  /// when no live project chip was available at all (e.g. a snapshot opened/imported standalone), in
  /// which case no configured/unconfigured claim is made and this node is shown exactly as it was
  /// before this property existed.</summary>
  public bool? IsConfigured { get; }

  public Visibility NotConfiguredVisibility => IsConfigured == false ? Visibility.Visible : Visibility.Collapsed;

  /// <summary>Same "own color, else the snapshot's project default" resolution as
  /// <see cref="NodeViewModel.EffectiveColorHex"/>, just read from a captured snapshot instead of a
  /// live project. Meaningless for the node-708 placeholder (<see cref="IsHeadPlaceholder"/>); its own
  /// colors below never consult this.</summary>
  public string EffectiveColorHex => string.IsNullOrWhiteSpace(Snapshot?.Color) ? _projectDefaultColor : Snapshot.Color;

  public Brush NodeBackgroundBrush => IsHeadPlaceholder
      ? HeadBackgroundBrush
      : HasError
          ? ErrorBackgroundBrush
          : IsConfigured == false
              ? NeutralBackgroundBrush
              : NodeColorOption.BrushFromHex(EffectiveColorHex);

  public Brush NodeBorderBrushColor => IsHeadPlaceholder
      ? HeadBorderBrushColor
      : HasError
          ? ErrorBorderBrush
          : IsConfigured == false
              ? NeutralBorderBrush
              : NodeColorOption.BrushFromHex(NodeColorPalette.Darken(EffectiveColorHex));

  public string ToolTip => IsHeadPlaceholder
      ? $"Node {CoordinateText} (Kraken head): no live-state read path of its own -- not captured."
      : HasError
          ? $"Node {CoordinateText}: read failed -- {Snapshot!.Error}"
          : $"Node {CoordinateText}{(IsConfigured == false ? " -- not configured in the current project" : string.Empty)}\nA: {PortAddressNames.Format(Snapshot!.A)}   IO: {Snapshot.Io:X5}\nClick for registers, both stacks, and a RAM/ROM disassembly.";
}