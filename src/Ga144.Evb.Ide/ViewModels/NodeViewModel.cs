using Ga144.Evb.Ide.Models;
using System.Windows;
using System.Windows.Media;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class NodeViewModel
{
  public NodeViewModel(Ga144NodeConfiguration model, KrakenNodeRoute? krakenRoute = null, bool krakenActive = false)
  {
    Model = model;
    KrakenRoute = krakenRoute;
    KrakenActive = krakenActive;
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
  public bool IsConfigured => Model.Enabled || !string.IsNullOrWhiteSpace(Model.SourceCode) || Model.RamWords.Count > 0;

  public Visibility NorthVisibility => Row < 7 ? Visibility.Visible : Visibility.Collapsed;
  public Visibility SouthVisibility => Row > 0 ? Visibility.Visible : Visibility.Collapsed;
  public Visibility WestVisibility => Column > 0 ? Visibility.Visible : Visibility.Collapsed;
  public Visibility EastVisibility => Column < 17 ? Visibility.Visible : Visibility.Collapsed;
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
      return $"Node {CoordinateText}\nCOM ports: {PortList()}\n{IoDescription}{kraken}";
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