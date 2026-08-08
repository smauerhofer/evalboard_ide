using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

internal enum KrakenCheckOutcome
{
    Pending,
    Passed,
    Failed,
    Skipped
}

internal sealed record KrakenRamZeroCheckResult(
    int Coordinate,
    int TentacleNumber,
    int Position,
    KrakenCheckOutcome Outcome,
    int Expected,
    int? Actual,
    string Message)
{
    public static KrakenRamZeroCheckResult Passed(KrakenNodeRoute route, int expected, int actual) =>
        new(route.Coordinate, route.TentacleNumber, route.Position, KrakenCheckOutcome.Passed,
            expected, actual, "RAM[0] write/read matched; original RAM[0] and A restored.");

    public static KrakenRamZeroCheckResult ValueMismatch(KrakenNodeRoute route, int expected, int actual) =>
        new(route.Coordinate, route.TentacleNumber, route.Position, KrakenCheckOutcome.Failed,
            expected, actual, $"RAM[0] mismatch: expected {expected} (0x{expected:X5}), received {actual} (0x{actual:X5}). Original RAM[0] and A restored.");

    public static KrakenRamZeroCheckResult TransportFailure(KrakenNodeRoute route, string message) =>
        new(route.Coordinate, route.TentacleNumber, route.Position, KrakenCheckOutcome.Failed,
            route.Coordinate, null, "Transport failed: " + message + " RAM[0]/A restoration could not be guaranteed for this node.");

    public static KrakenRamZeroCheckResult Skipped(KrakenNodeRoute route, string message) =>
        new(route.Coordinate, route.TentacleNumber, route.Position, KrakenCheckOutcome.Skipped,
            route.Coordinate, null, message);
}
