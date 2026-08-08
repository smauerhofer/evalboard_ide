using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Runtime owner for one physical GA144 Kraken connection.
///
/// IMPORTANT LIFETIME RULE:
/// Once a Kraken has been successfully erected in hardware, the physical COM
/// endpoint remains exclusively reserved by this controller. The native handle
/// is deliberately CLOSED while idle and reopened only around explicit Kraken
/// operations. Reopening is transport-only: it must never pulse reset, probe
/// node 708, reload the helper, or re-erect the tentacles.
/// </summary>
public sealed class KrakenLiveController : IAsyncDisposable
{
  private readonly KrakenConfiguration _configuration;
  private readonly Func<KrakenEndpointInfo?> _endpointResolver;
  private readonly KrakenIdlePolicy _idlePolicy;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private KrakenSession? _session;
  private KrakenEndpointInfo? _endpoint;
  private bool _hardwareErected;
  private bool _transportFaulted;
  private string? _faultText;
  private bool _disposed;

  public KrakenLiveController(
      KrakenConfiguration configuration,
      Func<KrakenEndpointInfo?> endpointResolver,
      KrakenIdlePolicy idlePolicy = KrakenIdlePolicy.HoldOpen)
  {
    _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    _endpointResolver = endpointResolver ?? throw new ArgumentNullException(nameof(endpointResolver));
    _idlePolicy = idlePolicy;
  }

  /// <summary>The idle-handle policy in effect for this controller's session.</summary>
  public KrakenIdlePolicy IdlePolicy => _idlePolicy;

  public event EventHandler? StateChanged;

  /// <summary>True while the resident hardware Kraken is usable, even if the host COM handle is parked.</summary>
  public bool IsConnected => _hardwareErected && _session?.IsConnected == true && !_transportFaulted;
  public bool IsTransportOpen => _session?.IsTransportOpen == true;

  /// <summary>
  /// True after a complete hardware erection. This remains true even if a
  /// later Kraken transaction faults: a fault must not trigger reset/re-erection.
  /// </summary>
  public bool HardwareErected => _hardwareErected;

  /// <summary>
  /// The hardware/COM endpoint is under exclusive Kraken ownership. While
  /// true, normal serial discovery/probing and port reassignment may not touch
  /// this endpoint, even though the native handle is normally parked/closed.
  /// </summary>
  public bool HasExclusiveSerialOwnership => _hardwareErected;

  public bool IsOperational => IsConnected;
  public bool TransportFaulted => _transportFaulted;
  public string? FaultText => _faultText;
  public KrakenEndpointInfo? CurrentEndpoint => _endpoint;

  /// <summary>
  /// Ensures there is a usable resident Kraken for the requested route. A
  /// reset/full erection is permitted ONLY before the first successful
  /// erection. Once hardware is erected the COM handle may be opened briefly
  /// for verification and is parked again immediately afterward; no reset or
  /// re-erection is performed.
  /// </summary>
  public async Task<bool> EnsureOnlineAsync(
      KrakenNodeRoute route,
      bool verifyTarget,
      bool allowErect,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(route);
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();

      if (_hardwareErected)
      {
        if (_transportFaulted)
        {
          throw new InvalidOperationException(
              "The live Kraken is locked to its original serial endpoint but a transport/topology fault has occurred. " +
              "The IDE will not reset or re-erect the GA144 automatically. " +
              (_faultText ?? string.Empty));
        }

        KrakenSession live = RequireSession();
        live.SetTargetRoute(route);
        if (verifyTarget)
        {
          _ = await live.ReadAAsync(cancellationToken);
        }

        return false;
      }

      if (!allowErect)
      {
        throw new InvalidOperationException(
            "No live Kraken has been erected for this chip. Erect it once before using online node control.");
      }

      KrakenEndpointInfo endpoint = ResolveEndpoint();
      if (_session is not null)
      {
        throw new InvalidOperationException("An unexpected Kraken session already exists before erection.");
      }

      var session = new KrakenSession(_configuration, route, _idlePolicy);
      try
      {
        if (verifyTarget)
        {
          await session.ConnectAndErectAsync(endpoint.PortName, cancellationToken);
        }
        else
        {
          await session.ConnectAndErectForCheckAsync(endpoint.PortName, cancellationToken);
        }

        _session = session;
        _endpoint = endpoint;
        _hardwareErected = true;
        _transportFaulted = false;
        _faultText = null;
        RaiseStateChanged();
        return true;
      }
      catch (Exception exception)
      {
        // If the complete Kraken was already erected before a final
        // verification failed, the serial handle MUST be retained.
        if (session.HardwareErectionCompleted && session.IsConnected)
        {
          _session = session;
          _endpoint = endpoint;
          _hardwareErected = true;
          _transportFaulted = true;
          _faultText = "Initial post-erection verification failed: " + exception.Message;
          RaiseStateChanged();
          throw new InvalidOperationException(
              "Kraken erection completed, but verification failed. The serial endpoint remains exclusively reserved; no reset or re-erection was attempted.",
              exception);
        }

        // Before a complete erection there is no live Kraken to
        // preserve, so normal cleanup of the failed setup is safe.
        await session.DisposeAsync();
        throw;
      }
    }
    catch (Exception exception) when (_hardwareErected && exception is not OperationCanceledException)
    {
      if (!_transportFaulted && (exception is IOException or TimeoutException or InvalidOperationException))
      {
        _transportFaulted = true;
        _faultText = exception.Message;
        RaiseStateChanged();
      }

      throw;
    }
    finally
    {
      _gate.Release();
    }
  }

  public Task<int> ReadAAsync(KrakenNodeRoute route, CancellationToken cancellationToken = default) =>
      RunForRouteValueAsync(route, session => session.ReadAAsync(cancellationToken), cancellationToken);

  public Task WriteAAsync(KrakenNodeRoute route, int value, CancellationToken cancellationToken = default) =>
      RunForRouteAsync(route, session => session.WriteAAsync(value, cancellationToken), cancellationToken);

  public Task<int> ReadIoAsync(KrakenNodeRoute route, CancellationToken cancellationToken = default) =>
      RunForRouteValueAsync(route, session => session.ReadIoAsync(cancellationToken), cancellationToken);

  public Task WriteIoAsync(KrakenNodeRoute route, int value, CancellationToken cancellationToken = default) =>
      RunForRouteAsync(route, session => session.WriteIoAsync(value, cancellationToken), cancellationToken);

  public Task<IReadOnlyList<int>> ReadRamAsync(KrakenNodeRoute route, CancellationToken cancellationToken = default) =>
      RunForRouteValueAsync(route, session => session.ReadRamAsync(cancellationToken), cancellationToken);

  public Task WriteRamAsync(KrakenNodeRoute route, IReadOnlyList<int> words, CancellationToken cancellationToken = default) =>
      RunForRouteAsync(route, session => session.WriteRamAsync(words, cancellationToken), cancellationToken);

  public Task<IReadOnlyList<int>> ReadRomAsync(KrakenNodeRoute route, CancellationToken cancellationToken = default) =>
      RunForRouteValueAsync(route, session => session.ReadRomAsync(cancellationToken), cancellationToken);

  public Task<IReadOnlyList<int>> ReadParameterStackAsync(KrakenNodeRoute route, CancellationToken cancellationToken = default) =>
      RunForRouteValueAsync(route, session => session.ReadParameterStackAsync(cancellationToken), cancellationToken);

  public Task WriteParameterStackAsync(KrakenNodeRoute route, IReadOnlyList<int> words, CancellationToken cancellationToken = default) =>
      RunForRouteAsync(route, session => session.WriteParameterStackAsync(words, cancellationToken), cancellationToken);

  public Task<IReadOnlyList<int>> ReadReturnStackAsync(KrakenNodeRoute route, CancellationToken cancellationToken = default) =>
      RunForRouteValueAsync(route, session => session.ReadReturnStackAsync(cancellationToken), cancellationToken);

  public Task WriteReturnStackAsync(KrakenNodeRoute route, IReadOnlyList<int> words, CancellationToken cancellationToken = default) =>
      RunForRouteAsync(route, session => session.WriteReturnStackAsync(words, cancellationToken), cancellationToken);

  public Task WriteBAsync(KrakenNodeRoute route, int value, CancellationToken cancellationToken = default) =>
      RunForRouteAsync(route, session => session.WriteBAsync(value, cancellationToken), cancellationToken);

  public Task JumpAsync(KrakenNodeRoute route, int destination, CancellationToken cancellationToken = default) =>
      RunForRouteAsync(route, session => session.JumpAsync(destination, cancellationToken), cancellationToken);

  internal async Task<IReadOnlyList<KrakenRamZeroCheckResult>> CheckRamZeroAsync(
      IReadOnlyList<KrakenNodeRoute> routes,
      IProgress<KrakenRamZeroCheckResult>? progress = null,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(routes);
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      EnsureOperational();
      KrakenSession session = RequireSession();
      IReadOnlyList<KrakenRamZeroCheckResult> results = await session.CheckRamZeroAsync(routes, progress, cancellationToken);

      KrakenRamZeroCheckResult? transportFailure = results.FirstOrDefault(item =>
          item.Outcome == KrakenCheckOutcome.Failed && item.Actual is null);
      if (transportFailure is not null)
      {
        _transportFaulted = true;
        _faultText = $"Kraken check transport failure at node {transportFailure.Coordinate:000}: {transportFailure.Message}";
        RaiseStateChanged();
      }

      return results;
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// Begin a keep-open scope so a batch of operations (e.g. a full Check Kraken:
  /// the erection/verify plus the RAM[0] scan) reopens/closes the FTDI once for
  /// the whole batch under CloseWhileIdle, instead of once per operation. No-op
  /// under HoldOpen. Always pair with EndKeepOpenAsync in a finally.
  /// </summary>
  public async Task BeginKeepOpenAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      if (_hardwareErected && !_transportFaulted && _session is not null)
      {
        await _session.BeginKeepOpenAsync(cancellationToken);
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// End a keep-open scope. When the outermost scope closes under CloseWhileIdle,
  /// the FTDI handle is closed once. No-op under HoldOpen. Safe to call even if
  /// BeginKeepOpenAsync was skipped (e.g. no session yet); it simply does nothing.
  /// </summary>
  public async Task EndKeepOpenAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(CancellationToken.None);
    try
    {
      if (_session is not null)
      {
        await _session.EndKeepOpenAsync(cancellationToken);
      }
    }
    finally
    {
      _gate.Release();
    }
  }


  internal async Task ParkTransportAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      if (_disposed || _session is null)
      {
        return;
      }

      await _session.ParkTransportAsync(cancellationToken);
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// There is no user-visible disconnect of the resident Kraken. The host COM
  /// handle is managed automatically by the idle policy (held open, or parked
  /// between operations); the endpoint itself remains reserved until process
  /// shutdown.
  /// </summary>
  public Task DisconnectAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (_hardwareErected)
    {
      throw new InvalidOperationException(
          "The Kraken endpoint remains reserved while Kraken is resident. Its COM handle is managed automatically while idle.");
    }

    return Task.CompletedTask;
  }

  /// <summary>
  /// Marks the tentacle topology unusable after an intentionally destructive
  /// Kraken operation (for example Jump or a B write that changes routing).
  /// The serial endpoint remains exclusively reserved. No reset/re-erection
  /// is attempted automatically.
  /// </summary>
  public async Task MarkTopologyAlteredAsync(string reason, CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      if (!_hardwareErected)
      {
        return;
      }

      _transportFaulted = true;
      _faultText = string.IsNullOrWhiteSpace(reason) ? "Kraken topology was altered." : reason.Trim();
      RaiseStateChanged();
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// Project-side topology changes are not permitted while hardware Kraken
  /// is live. This method only clears a controller that has never erected.
  /// </summary>
  public async Task InvalidateAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      if (_disposed)
      {
        return;
      }

      if (_hardwareErected)
      {
        throw new InvalidOperationException(
            "The Kraken topology cannot be invalidated while hardware Kraken is resident. " +
            "Reset/re-erection are deliberately forbidden.");
      }

      if (_session is not null)
      {
        await _session.DisposeAsync();
        _session = null;
      }
      _endpoint = null;
      _transportFaulted = false;
      _faultText = null;
      RaiseStateChanged();
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// Process-shutdown cleanup only. Normal chip/node/check-window closure must
  /// never clear the resident Kraken runtime or release its endpoint reservation.
  /// </summary>
  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    await _gate.WaitAsync();
    try
    {
      KrakenSession? session = _session;
      _session = null;
      if (session is not null)
      {
        await session.DisposeAsync();
      }

      _endpoint = null;
      _hardwareErected = false;
      _transportFaulted = false;
      _faultText = null;
    }
    finally
    {
      _gate.Release();
      _gate.Dispose();
    }
  }

  private async Task<T> RunForRouteValueAsync<T>(
      KrakenNodeRoute route,
      Func<KrakenSession, Task<T>> action,
      CancellationToken cancellationToken)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      EnsureOperational();
      KrakenSession session = RequireSession();
      session.SetTargetRoute(route);
      try
      {
        return await action(session);
      }
      catch (Exception exception) when (exception is IOException or TimeoutException or InvalidOperationException)
      {
        MarkFault(exception);
        throw;
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  private async Task RunForRouteAsync(
      KrakenNodeRoute route,
      Func<KrakenSession, Task> action,
      CancellationToken cancellationToken)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      EnsureOperational();
      KrakenSession session = RequireSession();
      session.SetTargetRoute(route);
      try
      {
        await action(session);
      }
      catch (Exception exception) when (exception is IOException or TimeoutException or InvalidOperationException)
      {
        MarkFault(exception);
        throw;
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  private KrakenEndpointInfo ResolveEndpoint() =>
      _endpointResolver() ?? throw new InvalidOperationException(
          "No connected COM port matches the selected board's USB A/C FTDI binding.");

  private KrakenSession RequireSession() =>
      _session is { IsConnected: true } session
          ? session
          : throw new InvalidOperationException(
              _hardwareErected
                  ? "The resident Kraken session is unavailable."
                  : "The Kraken transport is offline.");

  private void EnsureOperational()
  {
    if (!_hardwareErected)
    {
      throw new InvalidOperationException("No hardware Kraken has been erected for this chip.");
    }

    if (_transportFaulted)
    {
      throw new InvalidOperationException(
          "The Kraken endpoint remains reserved, but online transactions are blocked after a transport/topology fault. " +
          (_faultText ?? string.Empty));
    }
  }

  private void MarkFault(Exception exception)
  {
    if (!_hardwareErected || _transportFaulted)
    {
      return;
    }

    _transportFaulted = true;
    _faultText = exception.Message;
    RaiseStateChanged();
  }

  private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}