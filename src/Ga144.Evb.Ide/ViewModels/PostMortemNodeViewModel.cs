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
/// </summary>
public sealed class PostMortemNodeViewModel
{
  private static readonly Brush ErrorBackgroundBrush = NodeColorOption.BrushFromHex("#F8D7D7");
  private static readonly Brush ErrorBorderBrush = NodeColorOption.BrushFromHex("#B33A3A");
  private static readonly Brush HeadBackgroundBrush = NodeColorOption.BrushFromHex("#E8E8E8");
  private static readonly Brush HeadBorderBrushColor = NodeColorOption.BrushFromHex("#8A8A8A");

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

  /// <summary>Same "own color, else the snapshot's project default" resolution as
  /// <see cref="NodeViewModel.EffectiveColorHex"/>, just read from a captured snapshot instead of a
  /// live project. Meaningless for the node-708 placeholder (<see cref="IsHeadPlaceholder"/>); its own
  /// colors below never consult this.</summary>
  public string EffectiveColorHex => string.IsNullOrWhiteSpace(Snapshot?.Color) ? _projectDefaultColor : Snapshot.Color;

  public Brush NodeBackgroundBrush => IsHeadPlaceholder ? HeadBackgroundBrush : HasError ? ErrorBackgroundBrush : NodeColorOption.BrushFromHex(EffectiveColorHex);

  public Brush NodeBorderBrushColor => IsHeadPlaceholder ? HeadBorderBrushColor : HasError ? ErrorBorderBrush : NodeColorOption.BrushFromHex(NodeColorPalette.Darken(EffectiveColorHex));

  public string ToolTip => IsHeadPlaceholder
      ? $"Node {CoordinateText} (Kraken head): no live-state read path of its own -- not captured."
      : HasError
          ? $"Node {CoordinateText}: read failed -- {Snapshot!.Error}"
          : $"Node {CoordinateText}\nA: {PortAddressNames.Format(Snapshot!.A)}   IO: 0x{Snapshot.Io:X5}\nClick for registers, both stacks, and a RAM/ROM disassembly.";
}