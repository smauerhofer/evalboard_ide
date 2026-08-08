using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class KrakenCheckItemViewModel : ObservableObject
{
  private string _status = "PENDING";
  private string _actual = "—";
  private string _message = "Waiting.";

  public KrakenCheckItemViewModel(KrakenNodeRoute route)
  {
    Route = route;
  }

  public KrakenNodeRoute Route { get; }
  public string Node => Route.Coordinate.ToString("000");
  public string Tentacle => $"T{Route.TentacleNumber}";
  public int Position => Route.Position;
  public string PositionText => Route.Position.ToString("00");
  public string IncomingPort => Route.IncomingPort;
  public string OutgoingPort => Route.OutgoingPort;
  public string Expected => $"{Route.Coordinate} / 0x{Route.Coordinate:X5}";
  public string Status { get => _status; private set => SetProperty(ref _status, value); }
  public string Actual { get => _actual; private set => SetProperty(ref _actual, value); }
  public string Message { get => _message; private set => SetProperty(ref _message, value); }

  internal void Apply(KrakenRamZeroCheckResult result)
  {
    Status = result.Outcome switch
    {
      KrakenCheckOutcome.Passed => "PASS",
      KrakenCheckOutcome.Failed => "FAIL",
      KrakenCheckOutcome.Skipped => "SKIP",
      _ => "PENDING"
    };
    Actual = result.Actual is int actual ? $"{actual} / 0x{actual:X5}" : "—";
    Message = result.Message;
  }

  internal void MarkSkipped(string message)
  {
    if (Status != "PENDING")
    {
      return;
    }

    Status = "SKIP";
    Message = message;
  }
}
