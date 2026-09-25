using Ga144.Evb.Ide.Models;
using System.Windows;
using System.Windows.Media;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class NodeViewModel
{
  private static readonly Brush NeutralBackgroundBrush = NodeColorOption.BrushFromHex("#F7FBF8");
  private static readonly Brush NeutralBorderBrush = NodeColorOption.BrushFromHex("#54705C");

  private readonly string _projectDefaultColor;

  public NodeViewModel(
      Ga144NodeConfiguration model,
      KrakenNodeRoute? krakenRoute = null,
      bool krakenActive = false,
      string projectDefaultColor = NodeColorPalette.DefaultColor)
  {
    Model = model;
    KrakenRoute = krakenRoute;
    KrakenActive = krakenActive;
    _projectDefaultColor = string.IsNullOrWhiteSpace(projectDefaultColor)
        ? NodeColorPalette.DefaultColor
        : projectDefaultColor;
  }

  public Ga144NodeConfiguration Model { get; }
  public KrakenNodeRoute? KrakenRoute { get; }

  /// <summary>
  /// Runtime truth: a Kraken is erected on the silicon this session. The
  /// coloured tentacle highlight/label are shown only when this is true, so a
  /// restart (which cannot know the chip state) shows no arrows/colours even
  /// though the topology (KrakenRoute) is still defined.
  /// </summary>
  public bool KrakenActive { get; }
  public int Row => Model.Coordinate / 100;
  public int Column => Model.Coordinate % 100;
  public string CoordinateText => Model.Coordinate.ToString("000");
  public string StateText => Model.Enabled ? "configured" : string.Empty;
  // Deliberately does NOT look at Model.RamWords: a compiled RAM image is
  // always the full 64-word memory span (F18Compiler.CreateImage pads every
  // unwritten word with the fixed fill value), so RamWords.Count is always
  // 64 after any successful compile and never goes back to empty just
  // because the source was cleared. Counting it here made the yellow
  // "configured" highlight sticky forever after a node's first compile,
  // even once the source was deleted and the boot checkbox unchecked.
  public bool IsConfigured => Model.Enabled || !string.IsNullOrWhiteSpace(Model.SourceCode);

  /// <summary>ADDED 2026-09-22, per Stefan directly: "in the GA144 window, i need a hint or little
  /// display to see which nodes are included in a post-mortem analysis." Drives the small "PM" badge
  /// (top-right corner of the node cell, ChipWindow.xaml) -- independent of <see cref="IsConfigured"/>:
  /// a node can be boot-configured, post-mortem-included, both, or neither. See
  /// <see cref="Ga144NodeConfiguration.PostMortemEnabled"/>'s own remarks for what this actually
  /// controls (Core Dump skips this node's own data read when false; the tentacle wire walk itself is
  /// unaffected either way).</summary>
  public bool PostMortemEnabled => Model.PostMortemEnabled;

  public Visibility PostMortemHintVisibility => PostMortemEnabled ? Visibility.Visible : Visibility.Collapsed;

  /// <summary>ADDED 2026-09-25, per Stefan directly: "in the GA144 window ... add the small node
  /// color number in the top left of each node." The 2-digit hex <see cref="NodeColorOption.Number"/>
  /// for <see cref="EffectiveColorHex"/>, shown as a small badge in the node's top-left corner
  /// (ChipWindow.xaml) -- the same 2-digit number already stamped on every NodeColorPalette picker
  /// swatch, so a node on the chip grid can be matched back to its picker entry at a glance.</summary>
  public string NodeColorNumber => NodeColorOption.NumberFor(EffectiveColorHex);

  /// <summary>Hidden until the node actually has a color to label -- mirrors the same
  /// <see cref="IsConfigured"/> gate already used for NodeBackgroundBrush/NodeBorderBrushColor
  /// above, so an unconfigured (neutral-background) node shows no color number either.</summary>
  public Visibility NodeColorNumberVisibility => IsConfigured ? Visibility.Visible : Visibility.Collapsed;

  /// <summary>
  /// This node's own color (Model.Color) if it has one, otherwise the owning project's
  /// DefaultNodeColor passed in at construction (ChipViewModel.RebuildNodes) -- always one of
  /// the 48 NodeColorPalette swatches either way. Recomputed live from Model.Color on every
  /// access, so picking "Use project default" in the node editor (which clears Model.Color to
  /// null) or changing the project's own default both show up the next time this chip's node
  /// grid is rebuilt.
  /// </summary>
  public string EffectiveColorHex => string.IsNullOrWhiteSpace(Model.Color) ? _projectDefaultColor : Model.Color;

  /// <summary>
  /// The chip grid's per-node background: the neutral, uncolored look for a node that isn't
  /// configured yet, or this node's EffectiveColorHex once it is -- replaces ChipWindow.xaml's
  /// old fixed "#FFF1C7 when IsConfigured" DataTrigger now that the color is configurable.
  /// </summary>
  public Brush NodeBackgroundBrush => IsConfigured ? NodeColorOption.BrushFromHex(EffectiveColorHex) : NeutralBackgroundBrush;

  /// <summary>A darker accent shade of NodeBackgroundBrush for the node's border (see
  /// NodeColorPalette.Darken), or the same fixed neutral border as before when unconfigured.</summary>
  public Brush NodeBorderBrushColor => IsConfigured
      ? NodeColorOption.BrushFromHex(NodeColorPalette.Darken(EffectiveColorHex))
      : NeutralBorderBrush;

  /// <summary>3px once configured (matching the old DataTrigger), 2px otherwise.</summary>
  public Thickness NodeBorderThickness => new(IsConfigured ? 3 : 2);

  public Visibility NorthVisibility => Row < 7 ? Visibility.Visible : Visibility.Collapsed;
  public Visibility SouthVisibility => Row > 0 ? Visibility.Visible : Visibility.Collapsed;
  public Visibility WestVisibility => Column > 0 ? Visibility.Visible : Visibility.Collapsed;
  public Visibility EastVisibility => Column < 17 ? Visibility.Visible : Visibility.Collapsed;

  // Single-letter local F18 port names (u/d/l/r) for the neighbour reached
  // geographically north/south/west/east of this node. Not the same as
  // compass direction: GA144 mirrors cell orientation on alternating
  // rows/columns (KrakenTopology.PortName), so e.g. geographic north is
  // local "up" on some rows and local "down" on others. Shown on the chip
  // grid so the mapping doesn't have to be memorized or recomputed by hand.
  public string NorthPortLabel => Row < 7 ? PortLetter(Model.Coordinate, Model.Coordinate + 100) : string.Empty;
  public string SouthPortLabel => Row > 0 ? PortLetter(Model.Coordinate, Model.Coordinate - 100) : string.Empty;
  public string WestPortLabel => Column > 0 ? PortLetter(Model.Coordinate, Model.Coordinate - 1) : string.Empty;
  public string EastPortLabel => Column < 17 ? PortLetter(Model.Coordinate, Model.Coordinate + 1) : string.Empty;

  private static string PortLetter(int from, int to) => KrakenTopology.PortName(from, to)[..1];
  public Visibility ExternalIoVisibility => IsEdge ? Visibility.Visible : Visibility.Collapsed;
  public bool IsEdge => Row is 0 or 7 || Column is 0 or 17;

  public Visibility KrakenVisibility =>
      KrakenActive && KrakenRoute is not null ? Visibility.Visible : Visibility.Collapsed;
  public string KrakenLabel => KrakenActive
      ? KrakenRoute switch
      {
        { IsHead: true } => "K HEAD",
        { } route => $"T{route.TentacleNumber}:{route.Position:00}",
        _ => string.Empty
      }
      : string.Empty;

  public Brush KrakenBrush => KrakenActive
      ? KrakenRoute?.TentacleNumber switch
      {
        1 => Brushes.SteelBlue,
        2 => Brushes.DarkOrange,
        3 => Brushes.MediumPurple,
        _ when KrakenRoute?.IsHead == true => Brushes.DarkSlateGray,
        _ => Brushes.Transparent
      }
      : Brushes.Transparent;

  public string KrakenDescription => KrakenRoute switch
  {
    null => string.Empty,
    { IsHead: true } => "Kraken head. Three tentacles start through west (T1), east (T2), and south (T3).",
    { NextCoordinate: int next, OutgoingBAddress: int b } route =>
        $"Kraken tentacle {route.TentacleNumber} ({route.TentacleName}), position {route.Position}. " +
        $"Input: {route.IncomingPort}; next node {next:000} via {route.OutgoingPort}; B = 0x{b:X3}.",
    { } route =>
        $"Kraken tentacle {route.TentacleNumber} ({route.TentacleName}), position {route.Position}. " +
        $"Input: {route.IncomingPort}; terminal node."
  };

  public string IoDescription => Model.Coordinate switch
  {
    708 => "Asynchronous serial boot node 708",
    705 => "SPI boot node 705",
    300 => "Synchronous boot node 300",
    7 => "Parallel/SRAM data node 007",
    8 => "Parallel/SRAM control node 008",
    9 => "Parallel/SRAM address node 009",
    _ when IsEdge => "Edge node with external I/O facilities; exact pins depend on the GA144 package node configuration.",
    _ => "Internal node"
  };

  public string ToolTip
  {
    get
    {
      string kraken = string.IsNullOrWhiteSpace(KrakenDescription)
          ? string.Empty
          : $"\n{KrakenDescription}";
      string postMortem = PostMortemEnabled
          ? "\nIncluded in Core Dump post-mortem reads (PM badge)."
          : "\nExcluded from Core Dump post-mortem reads -- still transited by the tentacle wire walk, just not read.";
      return $"Node {CoordinateText}\nCOM ports: {PortList()}\n{IoDescription}{kraken}{postMortem}";
    }
  }

  private string PortList()
  {
    var ports = new List<string>(4);
    if (Row < 7) ports.Add("N");
    if (Column < 17) ports.Add("E");
    if (Row > 0) ports.Add("S");
    if (Column > 0) ports.Add("W");
    return string.Join(", ", ports);
  }
}