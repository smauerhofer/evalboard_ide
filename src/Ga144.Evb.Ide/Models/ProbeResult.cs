namespace Ga144.Evb.Ide.Models;

public sealed record ProbeResult(
    bool Detected,
    string Message,
    TimeSpan Elapsed,
    Exception? Exception = null)
{
    public static ProbeResult Success(TimeSpan elapsed) =>
        new(true, "GA144 node 708 detected", elapsed);

    public static ProbeResult Failure(string message, TimeSpan elapsed) =>
        new(false, message, elapsed);

    public static ProbeResult Error(string message, TimeSpan elapsed, Exception exception) =>
        new(false, message, elapsed, exception);
}
