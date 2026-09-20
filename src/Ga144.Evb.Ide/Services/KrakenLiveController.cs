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
  private readonly Ga144ChipConfiguration _chip;
  private readonly Ga144RomLibrary _romLibrary;
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
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      Func<KrakenEndpointInfo?> endpointResolver,
      KrakenIdlePolicy idlePolicy = KrakenIdlePolicy.HoldOpen)
  {
    _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    _chip = chip ?? throw new ArgumentNullException(nameof(chip));
    _romLibrary = romLibrary ?? throw new ArgumentNullException(nameof(romLibrary));
    _endpointResolver = endpointResolver ?? throw new ArgumentNullException(nameof(endpointResolver));
    _idlePolicy = idlePolicy;
  }

  /// <summary>The idle-handle policy in effect for this controller's session.</summary>
  public KrakenIdlePolicy IdlePolicy => _idlePolicy;

  /// <summary>
  /// Tear down transient erection state and close the COM handle, WITHOUT
  /// resetting the GA144. Unlike InvalidateAsync (which forbids teardown while a
  /// Kraken is resident), this is the explicit path for treating erection as
  /// transient runtime state: it is called at startup, on board change, and on a
  /// port-binding change for the affected role. After this the controller reports
  /// not-erected, and the next operation must erect again (which performs the
  /// reset intrinsic to installing a Kraken).
  ///
  /// This does not itself pulse reset; it only drops IDE state and releases the
  /// native handle. The subsequent erection is what resets the chip, per the
  /// GA144 async-boot protocol, and only if/when the user starts a Kraken op.
  /// </summary>
  public async Task ResetTransientErectionAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      if (_disposed)
      {
        return;
      }

      bool wasErected = _hardwareErected;

      // Disposing the session closes the native COM handle (and stops the idle
      // timer / releases the held handle, depending on policy). No chip reset.
      if (_session is not null)
      {
        try
        {
          await _session.DisposeAsync();
        }
        catch
        {
          // Teardown is best-effort; a disappearing USB device must not block it.
        }
        _session = null;
      }

      _hardwareErected = false;
      _endpoint = null;
      _transportFaulted = false;
      _faultText = null;

      if (wasErected)
      {
        RaiseStateChanged();
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// Pulses the GA144's hardware reset line and releases it again, WITHOUT
  /// loading a head program or erecting Kraken -- every node in the array
  /// reboots into its own boot ROM and stays there. This briefly opens the
  /// board's endpoint purely to toggle DTR/RTS (see
  /// <see cref="KrakenSession.PulseResetOnPort"/>) and closes it again
  /// immediately, so it is safe to call as a standalone "reset the chip"
  /// operation from anywhere that needs one without also erecting a Kraken it
  /// does not want (for example, the first step of a Core Dump, before
  /// installing Kraken separately via <see cref="EnsureOnlineAsync"/>).
  ///
  /// Requires that no Kraken is currently resident: reset while erected would
  /// silently wipe the very Kraken this controller believes is still live.
  /// Call <see cref="ResetTransientErectionAsync"/> first to tear one down.
  /// </summary>
  public async Task ResetChipAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      if (_hardwareErected)
      {
        throw new InvalidOperationException(
            "A Kraken is currently resident. Call ResetTransientErectionAsync to release it before pulsing a standalone reset.");
      }

      KrakenEndpointInfo endpoint = ResolveEndpoint();
      await Task.Run(() => KrakenSession.PulseResetOnPort(endpoint.PortName, cancellationToken), cancellationToken);
    }
    finally
    {
      _gate.Release();
    }
  }

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

      // Only Port A (Host) has RTS wired to RESET-, so only there does reopening
      // the COM port reset the chip and require re-erection. Port C (Target) does
      // not, and must not be re-erected on reopen.
      bool reopenResetsChip = endpoint.Role == Ga144ChipRole.Host;
      var session = new KrakenSession(_configuration, route, _chip, _romLibrary, _idlePolicy, reopenResetsChip);
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

  /// <summary>
  /// Core-Dump-ONLY: brings up node 708's own head program and ALSO wires Tentacle 1's own first
  /// <paramref name="tentacle1BootFramePrefixNodeCount"/> nodes (707 onward) via the reliable
  /// boot-frame mechanism -- see <see cref="KrakenSession.ConnectAndErectHeadWithTentacle1PrefixAsync"/>
  /// for the full rationale (Stefan's own compromise for node 300: reach it and everything before it
  /// the reliable way, at the cost of the usual lossless top-of-stack capture for just those nodes;
  /// pass 0 for a plain head-only bring-up touching no tentacle node at all). Unlike
  /// <see cref="EnsureOnlineAsync"/>, this always performs a fresh reset + bring-up: it is only ever
  /// called at the very start of a Core Dump, never to "ensure" an existing session is usable, so
  /// there is no already-erected branch here. The caller (CvmDebuggerViewModel's Core Dump) reads the
  /// boot-framed prefix directly (no focus needed) and is responsible for wiring and reading every
  /// remaining tentacle node itself afterward, one hop at a time, via <see cref="FocusAsync"/>,
  /// <see cref="WriteBAsync"/>, and the normal per-node reads.
  /// </summary>
  public async Task EnsureHeadWithTentacle1PrefixOnlineAsync(
      KrakenNodeRoute initialTargetRoute, int tentacle1BootFramePrefixNodeCount, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(initialTargetRoute);
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      if (_hardwareErected)
      {
        throw new InvalidOperationException(
            "A Kraken is already resident. This erection is only for the start of a fresh Core Dump.");
      }

      KrakenEndpointInfo endpoint = ResolveEndpoint();
      if (_session is not null)
      {
        throw new InvalidOperationException("An unexpected Kraken session already exists before erection.");
      }

      bool reopenResetsChip = endpoint.Role == Ga144ChipRole.Host;
      var session = new KrakenSession(_configuration, initialTargetRoute, _chip, _romLibrary, _idlePolicy, reopenResetsChip);
      try
      {
        await session.ConnectAndErectHeadWithTentacle1PrefixAsync(endpoint.PortName, tentacle1BootFramePrefixNodeCount, cancellationToken);
        _session = session;
        _endpoint = endpoint;
        _hardwareErected = true;
        _transportFaulted = false;
        _faultText = null;
        RaiseStateChanged();
      }
      catch
      {
        // No partial-erection state is worth preserving here (unlike
        // EnsureOnlineAsync's own catch): ConnectAndErectHeadWithTentacle1PrefixAsync does no
        // post-erection verification of its own to fail after the fact.
        await session.DisposeAsync();
        throw;
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// Core-Dump-ONLY: focuses <paramref name="route"/>'s node onto the given
  /// port via a live transaction (<see cref="KrakenSession.FocusAsync"/>),
  /// returning the one reply word that transaction always sends -- T, the
  /// node's own parameter-stack top, popped as a side effect of the
  /// mandatory acknowledgment. The caller combines this with
  /// <see cref="ReadParameterStackTailAsync"/> (not the full
  /// <see cref="ReadParameterStackAsync"/>) to recover the complete,
  /// true pre-focus parameter stack; see that method's own remarks.
  /// </summary>
  public Task<int> FocusAsync(KrakenNodeRoute route, int port, CancellationToken cancellationToken = default) =>
      RunForRouteValueAsync(route, session => session.FocusAsync(port, cancellationToken), cancellationToken);

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

  /// <summary>
  /// Core-Dump-ONLY companion to <see cref="FocusAsync"/>: reads the remaining 9 parameter-stack
  /// words (S through the deepest slot) once T is already known from Focus's own reply -- see
  /// <see cref="KrakenSession.ReadParameterStackTailAsync"/>.
  /// </summary>
  public Task<IReadOnlyList<int>> ReadParameterStackTailAsync(KrakenNodeRoute route, CancellationToken cancellationToken = default) =>
      RunForRouteValueAsync(route, session => session.ReadParameterStackTailAsync(cancellationToken), cancellationToken);

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

  // ---- AN003 SRAM cluster (see KrakenSramProtocol / KrakenSession) -------
  // 'route' here is the memory-master node (106, 108, or 207) being puppeted,
  // not node 107 itself. 'subroutineAddress' is the address of that master's
  // OWN resident support subroutine for this op (see
  // SramClusterPrograms.BuildMasterSupportSource), resolved once at install
  // time (see SramClusterInstaller.SramMasterSupportAddresses). Both the
  // master's support code AND the cluster's other resident firmware
  // (007/008/009/107) must already be installed and running (see
  // SramClusterInstaller) before any of these are used.

  public Task<int> ReadSramWordAsync(KrakenNodeRoute route, int subroutineAddress, int page, int address, CancellationToken cancellationToken = default) =>
      RunForRouteValueAsync(route, session => session.ReadSramWordAsync(subroutineAddress, page, address, cancellationToken), cancellationToken);

  public Task WriteSramWordAsync(KrakenNodeRoute route, int subroutineAddress, int page, int address, int value, CancellationToken cancellationToken = default) =>
      RunForRouteAsync(route, session => session.WriteSramWordAsync(subroutineAddress, page, address, value, cancellationToken), cancellationToken);

  public Task<int> CompareExchangeSramWordAsync(
      KrakenNodeRoute route, int subroutineAddress, int page, int address, int compareValue, int newValue, CancellationToken cancellationToken = default) =>
      RunForRouteValueAsync(
          route,
          session => session.CompareExchangeSramWordAsync(subroutineAddress, page, address, compareValue, newValue, cancellationToken),
          cancellationToken);

  public Task SetSramMasterMaskAsync(KrakenNodeRoute route, int subroutineAddress, int mask, bool postStimuli, CancellationToken cancellationToken = default) =>
      RunForRouteAsync(route, session => session.SetSramMasterMaskAsync(subroutineAddress, mask, postStimuli, cancellationToken), cancellationToken);

  /// <summary>
  /// DIAGNOSTIC ONLY, not part of AN003: calls the master's own resident
  /// 'echo' subroutine (adds 1, touches neither B nor node 107). See the
  /// remarks on <see cref="KrakenSramProtocol.BuildEchoTest"/>.
  /// </summary>
  public Task<int> EchoTestAsync(KrakenNodeRoute route, int subroutineAddress, int value, CancellationToken cancellationToken = default) =>
      RunForRouteValueAsync(route, session => session.EchoTestAsync(subroutineAddress, value, cancellationToken), cancellationToken);

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