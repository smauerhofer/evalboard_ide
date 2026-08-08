using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;
using System.Diagnostics;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// One live PC-to-GA144 Kraken session. The selected chip is reset once to erect
/// Kraken and node 708 remains the transport head; the 143 controlled nodes use
/// only port execution and therefore retain all 64 RAM and 64 ROM words for the
/// user. After erection the native COM handle is managed by the configured
/// <see cref="KrakenIdlePolicy"/>: HoldOpen (default) keeps the handle open for
/// the whole Kraken lifetime and only quiesces it between transactions, while
/// CloseWhileIdle closes it when idle and reopens it (transport-only, no reset,
/// retry-hardened) for the next operation. Neither policy ever pulses reset or
/// re-erects automatically.
/// </summary>
internal sealed class KrakenSession : IAsyncDisposable
{
  // Use the same hardware-verified line rate as the node-708 detector. The
  // response path is carrier-clocked and does not synthesize a UART baud rate
  // in the F18, so process/voltage/temperature variation is not converted into
  // serial bit-time error.
  public const int OnlineBaudRate = Ga144Serial.MaximumBaudRate;

  // G144A12 node 708 asynchronous boot ROM continuation entry. Unlike
  // `cold` (0x0AA), ser-exec is the documented concatenation path for
  // additional frames and does not rerun cold's wake/start-bit
  // reasonableness classifier. After the first frame is accepted following
  // reset, every persistent Kraken frame returns here.
  private const int AsyncSerialContinuationAddress = 0x0AE;
  private const int IoAddress = 0x15D;
  private const int ResetAssertMilliseconds = 20;
  private const int ResetReleaseMilliseconds = 1;
  private const int ResponseTimeoutMilliseconds = 1_000;

  // Deliberately pace small FTDI transfers. The F18/Kraken side is much
  // faster than the USB VCP path, so there is no benefit in immediately
  // issuing the next host transaction. A small quiet interval substantially
  // reduces bursts of tiny USB requests on hubs/root controllers shared with
  // HID devices. Check Kraken is intentionally even more conservative.
  private const int OnlineTransactionSettleMilliseconds = 5;
  private const int CheckTransactionSettleMilliseconds = 10;

  // Reopen hardening for the CloseWhileIdle policy. A single CreateFile can
  // transiently fail while the previous CloseHandle is still tearing the FTDI
  // endpoint down, or while the device wakes from USB selective suspend. Retry
  // with bounded back-off before allowing a permanent transport fault.
  private const int ReopenMaxAttempts = 4;
  private const int ReopenInitialBackoffMilliseconds = 15;
  private const int ReopenBackoffMultiplier = 3; // 15, 45, 135, ...
                                                 // The first read after a reopen must tolerate FTDI selective-suspend wake.
  private const int FirstReadAfterReopenTimeoutMilliseconds = 400;
  // CloseAfterIdleTimeout: how long the handle stays open after the last
  // transaction before the idle timer closes it.
  private const int IdleCloseTimeoutMilliseconds = 1_000;

  private readonly KrakenConfiguration _configuration;
  private readonly KrakenIdlePolicy _idlePolicy;
  private KrakenNodeRoute _targetRoute;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private NativeWindowsSerialPort? _port;
  private string? _portName;
  private bool _disposed;
  private bool _hardwareErectionCompleted;
  // CloseWhileIdle only: while > 0, ParkTransport keeps the handle OPEN so a batch
  // of exclusive operations (e.g. a full Check Kraken: erection verify + RAM scan)
  // reopens/closes the FTDI once for the whole batch instead of once per call.
  // Single operations outside a scope still close immediately, preserving the
  // KVM-friendly idle. Unused under HoldOpen (nothing ever closes while resident).
  private int _keepOpenDepth;
  // CloseAfterIdleTimeout only: fired 1 s after the last transaction to close the
  // otherwise-open FTDI handle. Re-armed by every transaction, so a burst holds
  // the handle open and it closes once ~1 s after activity stops.
  private readonly System.Threading.Timer? _idleCloseTimer;
  // CloseWhileIdle only: set after a genuine reopen; consumed by the next read
  // (or cleared by a write-only transaction) to widen the first read timeout.
  private bool _reopenedThisTransaction;

  public KrakenSession(
      KrakenConfiguration configuration,
      KrakenNodeRoute targetRoute,
      KrakenIdlePolicy idlePolicy = KrakenIdlePolicy.HoldOpen)
  {
    ArgumentNullException.ThrowIfNull(configuration);
    ArgumentNullException.ThrowIfNull(targetRoute);
    configuration.Normalize();

    if (!configuration.Enabled)
    {
      throw new InvalidOperationException("The Kraken topology is not installed on this chip.");
    }

    if (targetRoute.IsHead)
    {
      throw new InvalidOperationException("Node 708 is the Kraken head and cannot be controlled through a tentacle session.");
    }

    _configuration = configuration;
    _targetRoute = targetRoute;
    _idlePolicy = idlePolicy;

    if (_idlePolicy == KrakenIdlePolicy.CloseAfterIdleTimeout)
    {
      // Created idle (Timeout.Infinite). ArmIdleClose() schedules the one-shot
      // close after each transaction; the callback closes the handle if still
      // idle. Never periodic: each arm is a fresh single-shot delay.
      _idleCloseTimer = new System.Threading.Timer(
          OnIdleCloseElapsed, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }
  }

  /// <summary>Logical Kraken availability. The COM handle may be held open, closed while idle, or closed after an idle timeout depending on policy.</summary>
  public bool IsConnected => _hardwareErectionCompleted && !_disposed;
  public bool IsTransportOpen => _port?.IsOpen == true;
  public bool HardwareErectionCompleted => _hardwareErectionCompleted;
  public int TargetCoordinate => _targetRoute.Coordinate;
  public int KnownFocusP => KrakenTopology.PortAddress(
      _targetRoute.Coordinate,
      _targetRoute.PreviousCoordinate ?? KrakenTopology.HeadCoordinate);
  public int ExpectedB => _targetRoute.OutgoingBAddress ?? IoAddress;

  internal void SetTargetRoute(KrakenNodeRoute targetRoute)
  {
    ArgumentNullException.ThrowIfNull(targetRoute);
    if (targetRoute.IsHead)
    {
      throw new InvalidOperationException("Node 708 is the Kraken head and cannot be controlled through a tentacle session.");
    }

    _targetRoute = targetRoute;
  }

  public async Task ConnectAndErectAsync(string portName, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      await Task.Run(() => ConnectAndErect(portName, cancellationToken, verifyTarget: true, parkWhenComplete: true), cancellationToken);
    }
    finally
    {
      _gate.Release();
    }
  }

  public async Task ConnectAndErectForCheckAsync(string portName, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      await Task.Run(() => ConnectAndErect(portName, cancellationToken, verifyTarget: false, parkWhenComplete: false), cancellationToken);
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// Verifies the live Kraken outward from node 708. Routes are expected in
  /// breadth order (T1/T2/T3 position 0, then position 1, ...). For every
  /// reachable node the test saves A and RAM[0], writes the decimal node
  /// coordinate into RAM[0], reads it back through the same tentacle, then
  /// restores RAM[0] and A.
  ///
  /// Once Kraken is erected, reset/re-erection is forbidden. Therefore a
  /// transport failure stops the remainder of the check and leaves the exact
  /// same Kraken endpoint reserved for diagnosis; no recovery reset/re-erection is attempted.
  /// </summary>
  public Task<IReadOnlyList<KrakenRamZeroCheckResult>> CheckRamZeroAsync(
      IReadOnlyList<KrakenNodeRoute> routes,
      IProgress<KrakenRamZeroCheckResult>? progress = null,
      CancellationToken cancellationToken = default) =>
      RunExclusiveAsync<IReadOnlyList<KrakenRamZeroCheckResult>>(() =>
      {
        ArgumentNullException.ThrowIfNull(routes);
        _ = RequirePort();
        var results = new List<KrakenRamZeroCheckResult>(routes.Count);
        bool transportFailed = false;

        foreach (KrakenNodeRoute route in routes)
        {
          cancellationToken.ThrowIfCancellationRequested();
          if (route.IsHead)
          {
            continue;
          }

          if (transportFailed)
          {
            var skipped = KrakenRamZeroCheckResult.Skipped(route,
                  "Check stopped after an earlier Kraken transport failure. Reset/re-erection recovery is forbidden while Kraken is running.");
            results.Add(skipped);
            progress?.Report(skipped);
            continue;
          }

          try
          {
            KrakenRamZeroCheckResult result = CheckRamZero(route, cancellationToken);
            results.Add(result);
            progress?.Report(result);
          }
          catch (OperationCanceledException)
          {
            throw;
          }
          catch (Exception exception)
          {
            var failed = KrakenRamZeroCheckResult.TransportFailure(route, exception.Message);
            results.Add(failed);
            progress?.Report(failed);
            transportFailed = true;
            // Intentionally do NOT reset or re-erect here. The current
            // scan owns the COM handle until the exclusive operation exits;
            // it is then parked normally.
          }
        }

        return results;
      }, cancellationToken);

  public Task<int> ReadAAsync(CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() => ReadWord(KrakenProtocol.BuildR1(_targetRoute.Position, KrakenProtocol.ReadAInstruction), cancellationToken), cancellationToken);

  public Task WriteAAsync(int value, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteAInstruction, value), cancellationToken);
        return 0;
      }, cancellationToken);

  public Task<int> ReadIoAsync(CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        int savedA = ReadWord(KrakenProtocol.BuildR1(_targetRoute.Position, KrakenProtocol.ReadAInstruction), cancellationToken);
        try
        {
          WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteAInstruction, IoAddress), cancellationToken);
          return ReadWord(KrakenProtocol.BuildR1(_targetRoute.Position, KrakenProtocol.ReadMemoryInstruction), cancellationToken);
        }
        finally
        {
          WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteAInstruction, savedA), cancellationToken);
        }
      }, cancellationToken);

  public Task WriteIoAsync(int value, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        int savedA = ReadWord(KrakenProtocol.BuildR1(_targetRoute.Position, KrakenProtocol.ReadAInstruction), cancellationToken);
        try
        {
          WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteAInstruction, IoAddress), cancellationToken);
          WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteMemoryInstruction, value), cancellationToken);
        }
        finally
        {
          WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteAInstruction, savedA), cancellationToken);
        }

        return 0;
      }, cancellationToken);

  public Task<IReadOnlyList<int>> ReadRamAsync(CancellationToken cancellationToken = default) =>
      RunExclusiveAsync<IReadOnlyList<int>>(() => ReadMemoryBlock(0x000, 64, cancellationToken), cancellationToken);

  public Task WriteRamAsync(IReadOnlyList<int> words, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        if (words.Count != 64)
        {
          throw new ArgumentException("A GA144 node RAM image contains exactly 64 words.", nameof(words));
        }

        WriteMemoryBlock(0x000, words, cancellationToken);
        return 0;
      }, cancellationToken);

  public Task<IReadOnlyList<int>> ReadRomAsync(CancellationToken cancellationToken = default) =>
      RunExclusiveAsync<IReadOnlyList<int>>(() => ReadMemoryBlock(0x080, 64, cancellationToken), cancellationToken);

  public Task<IReadOnlyList<int>> ReadParameterStackAsync(CancellationToken cancellationToken = default) =>
      RunExclusiveAsync<IReadOnlyList<int>>(() =>
      {
        var topToBottom = new List<int>(10);
        for (int index = 0; index < 10; index++)
        {
          topToBottom.Add(ReadWord(KrakenProtocol.BuildR1(_targetRoute.Position, KrakenProtocol.PopDataInstruction), cancellationToken));
        }

        var bottomToTop = topToBottom.AsEnumerable().Reverse().ToArray();
        RestoreDataStack(bottomToTop, cancellationToken);
        return bottomToTop;
      }, cancellationToken);

  public Task WriteParameterStackAsync(IReadOnlyList<int> bottomToTop, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        if (bottomToTop.Count != 10)
        {
          throw new ArgumentException("The F18A parameter stack view contains exactly 10 words.", nameof(bottomToTop));
        }

        // Ten pushes overwrite one complete F18A parameter-stack image,
        // matching the Kraken setup procedure.
        RestoreDataStack(bottomToTop, cancellationToken);
        return 0;
      }, cancellationToken);

  public Task<IReadOnlyList<int>> ReadReturnStackAsync(CancellationToken cancellationToken = default) =>
      RunExclusiveAsync<IReadOnlyList<int>>(() =>
      {
        var topToBottom = new List<int>(9);
        for (int index = 0; index < 9; index++)
        {
          topToBottom.Add(ReadWord(KrakenProtocol.BuildR1(_targetRoute.Position, KrakenProtocol.PopReturnInstruction), cancellationToken));
        }

        var bottomToTop = topToBottom.AsEnumerable().Reverse().ToArray();
        RestoreReturnStack(bottomToTop, cancellationToken);
        return bottomToTop;
      }, cancellationToken);

  public Task WriteReturnStackAsync(IReadOnlyList<int> bottomToTop, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        if (bottomToTop.Count != 9)
        {
          throw new ArgumentException("The F18A return stack view contains exactly 9 words.", nameof(bottomToTop));
        }

        // Nine pushes overwrite R plus the eight circular return-stack
        // registers, matching the Kraken node setup sequence.
        RestoreReturnStack(bottomToTop, cancellationToken);
        return 0;
      }, cancellationToken);

  public Task WriteBAsync(int value, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteBInstruction, value), cancellationToken);
        return 0;
      }, cancellationToken);

  public Task JumpAsync(int destination, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        if (destination is < 0 or > 0x3FF)
        {
          throw new ArgumentOutOfRangeException(nameof(destination), "P is a 10-bit address.");
        }

        int jump = F18InstructionSet.EncodeSlot0Control(0x02, destination);
        WriteSequence(KrakenProtocol.BuildX1(_targetRoute.Position, jump), cancellationToken);
        return 0;
      }, cancellationToken);


  public async Task ParkTransportAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      ParkTransport();
    }
    finally
    {
      _gate.Release();
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;

    // Stop and dispose the idle-close timer before taking the gate, so its
    // callback cannot run after teardown. Timer.Dispose() waits for any in-flight
    // callback that has not yet acquired the gate to finish its Wait(0) attempt.
    if (_idleCloseTimer is not null)
    {
      _idleCloseTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
      _idleCloseTimer.Dispose();
    }

    await _gate.WaitAsync();
    try
    {
      NativeWindowsSerialPort? port = _port;
      _port = null;
      if (port is not null)
      {
        try
        {
          // Real teardown for both policies: hold RESET- inactive/high,
          // then CloseHandle. This is the only place a HoldOpen handle is
          // ever closed.
          try { port.SetRts(true); } catch { }
          try { port.SetDtr(true); } catch { }
          port.Dispose();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
          // Process-shutdown close of a disappearing USB device should not keep the UI open.
        }
      }
    }
    finally
    {
      _gate.Release();
      _gate.Dispose();
    }
  }

  private void ConnectAndErect(string portName, CancellationToken cancellationToken, bool verifyTarget, bool parkWhenComplete)
  {
    if (_port is not null || _hardwareErectionCompleted)
    {
      throw new InvalidOperationException("Kraken has already been erected for this session.");
    }

    _portName = portName;
    NativeWindowsSerialPort port = NativeWindowsSerialPort.Open(
        portName,
        OnlineBaudRate,
        readTimeoutMilliseconds: 50,
        writeTimeoutMilliseconds: 2_000);

    try
    {
      cancellationToken.ThrowIfCancellationRequested();
      port.SetDtr(true);
      // Preserve the exact verified EVB reset polarity used by the node-708 detector.
      port.SetRts(false);
      Thread.Sleep(ResetAssertMilliseconds);
      port.PurgeInputOutput();
      port.SetRts(true);
      Thread.Sleep(ResetReleaseMilliseconds);

      // The first frame is accepted by node 708 through `cold` because
      // reset starts there. Its completion address is ser-exec, the ROM
      // concatenation entry specifically intended for additional boot
      // frames. Persistent online traffic must use that continuation path
      // instead of repeatedly re-entering cold's wake/start-bit
      // reasonableness classifier.
      int[] replyProgram = BuildReplyProgram();
      SendBootFrame(port, AsyncSerialContinuationAddress, 0x000, replyProgram);

      foreach (KrakenTentacleConfiguration tentacle in _configuration.Tentacles.OrderBy(item => item.Number))
      {
        int tentacleHeadPort = KrakenTopology.PortAddress(KrakenTopology.HeadCoordinate, tentacle.Nodes[0]);
        for (int position = 0; position < tentacle.Nodes.Count; position++)
        {
          cancellationToken.ThrowIfCancellationRequested();
          int coordinate = tentacle.Nodes[position];
          int previous = position == 0
              ? KrakenTopology.HeadCoordinate
              : tentacle.Nodes[position - 1];

          // A reset F18 normally executes from a MULTIPORT address.  That
          // is sufficient to accept the first instruction, but it is not
          // sufficient for Kraken readback: !p on a multiport P is a
          // multiport write and would wait for every selected neighbor.
          // Kraken requires each tentacle node to execute from the SINGLE
          // incoming port.  Focus it explicitly before assigning B.
          int incomingPort = KrakenTopology.PortAddress(coordinate, previous);
          int focusJump = F18InstructionSet.EncodeSlot0Control(0x02, incomingPort);
          IReadOnlyList<int> focusSequence = KrakenProtocol.BuildX1(position, focusJump);
          SendBootFrame(port, AsyncSerialContinuationAddress, tentacleHeadPort, focusSequence);

          int b = position + 1 < tentacle.Nodes.Count
              ? KrakenTopology.PortAddress(coordinate, tentacle.Nodes[position + 1])
              : IoAddress;
          IReadOnlyList<int> bSequence = KrakenProtocol.BuildW1(position, KrakenProtocol.WriteBInstruction, b);
          SendBootFrame(port, AsyncSerialContinuationAddress, tentacleHeadPort, bSequence);
        }
      }

      SettleUsb(OnlineTransactionSettleMilliseconds, cancellationToken);
      port.PurgeInput();
      _port = port;
      _hardwareErectionCompleted = true;

      if (verifyTarget)
      {
        // Do not report an online node session until a real read has
        // traversed the selected tentacle and returned through node 708.
        _ = ReadWord(
            _targetRoute,
            KrakenProtocol.BuildR1(_targetRoute.Position, KrakenProtocol.ReadAInstruction),
            cancellationToken);
      }

      // Kraken remains resident in the GA144. A normal Connect parks the
      // Windows/FTDI handle immediately. Check Kraken deliberately keeps
      // this first handle open because the scan follows immediately; the
      // scan's exclusive operation parks it when the last node is done.
      if (parkWhenComplete)
      {
        ParkTransport();
      }
    }
    catch
    {
      // Before a complete erection, restore the reset line inactive/high on
      // failure. After erection, never issue another reset pulse or implicit
      // re-erection; the host handle may still be parked normally.
      if (!_hardwareErectionCompleted)
      {
        try
        {
          port.SetRts(true);
        }
        catch
        {
          // Preserve the original error.
        }
      }

      if (_hardwareErectionCompleted)
      {
        // The Kraken is already resident. Do not reset or re-erect it.
        // Park the host COM handle even on a later verification fault so
        // the USB VCP is not left continuously active.
        _port = port;
        ParkTransport();
      }
      else
      {
        _port = null;
        port.Dispose();
      }

      throw;
    }
  }

  private IReadOnlyList<int> ReadMemoryBlock(int startAddress, int count, CancellationToken cancellationToken)
  {
    int savedA = ReadWord(KrakenProtocol.BuildR1(_targetRoute.Position, KrakenProtocol.ReadAInstruction), cancellationToken);
    var words = new int[count];
    try
    {
      WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteAInstruction, startAddress), cancellationToken);
      for (int index = 0; index < count; index++)
      {
        words[index] = ReadWord(
            KrakenProtocol.BuildR1(_targetRoute.Position, KrakenProtocol.ReadMemoryIncrementInstruction),
            cancellationToken);
      }
    }
    finally
    {
      WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteAInstruction, savedA), cancellationToken);
    }

    return words;
  }

  private void WriteMemoryBlock(int startAddress, IReadOnlyList<int> words, CancellationToken cancellationToken)
  {
    int savedA = ReadWord(KrakenProtocol.BuildR1(_targetRoute.Position, KrakenProtocol.ReadAInstruction), cancellationToken);
    try
    {
      WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteAInstruction, startAddress), cancellationToken);
      foreach (int word in words)
      {
        WriteSequence(
            KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteMemoryIncrementInstruction, word),
            cancellationToken);
      }
    }
    finally
    {
      WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.WriteAInstruction, savedA), cancellationToken);
    }
  }

  private void RestoreDataStack(IReadOnlyList<int> bottomToTop, CancellationToken cancellationToken)
  {
    foreach (int word in bottomToTop)
    {
      WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.PushDataInstruction, word), cancellationToken);
    }
  }

  private void RestoreReturnStack(IReadOnlyList<int> bottomToTop, CancellationToken cancellationToken)
  {
    foreach (int word in bottomToTop)
    {
      WriteSequence(KrakenProtocol.BuildW1(_targetRoute.Position, KrakenProtocol.PushReturnInstruction, word), cancellationToken);
    }
  }

  private int ReadWord(IReadOnlyList<int> sequence, CancellationToken cancellationToken) =>
      ReadWord(_targetRoute, sequence, cancellationToken);

  private int ReadWord(
      KrakenNodeRoute route,
      IReadOnlyList<int> sequence,
      CancellationToken cancellationToken,
      int settleMilliseconds = OnlineTransactionSettleMilliseconds)
  {
    NativeWindowsSerialPort port = RequirePort();
    cancellationToken.ThrowIfCancellationRequested();

    // The receive side is synchronized once after erection. Do not issue
    // PurgeComm on every online transaction: the persistent
    // Kraken path consumes exactly 18 reply bytes and otherwise leaves the
    // FTDI driver state untouched. A protocol error now fails visibly
    // instead of being hidden by repeated driver-buffer purges.
    int headPort = HeadPortFor(route);
    byte[] frame = EncodeBootFrame(completionAddress: 0, transferAddress: headPort, sequence);
    port.Write(frame);
    WaitForTransmitDrain(port, frame.Length);

    // Each logical bit gets a pair of host-generated UART carriers. The
    // EVB serial path presents the documented asynchronous polarity to the
    // F18 (idle low, start high), while the board/FTDI path converts it back
    // for the PC. The node-708 helper mirrors the 0x00 carrier for a zero or
    // the following 0xFF carrier for a one. Thus every returned bit is
    // clocked entirely by the FTDI UART; no F18 delay-loop calibration is
    // needed.
    byte[] carriers = new byte[36];
    for (int bit = 0; bit < 18; bit++)
    {
      carriers[bit * 2] = 0x00;
      carriers[bit * 2 + 1] = 0xFF;
    }

    port.Write(carriers);
    WaitForTransmitDrain(port, carriers.Length);

    // Under CloseWhileIdle the reply can be late on the first transaction
    // after a reopen (FTDI selective-suspend wake). Widen the timeout once,
    // then revert. The flag is never set under HoldOpen, so this is inert.
    int responseTimeout = ResponseTimeoutMilliseconds;
    if (_reopenedThisTransaction)
    {
      responseTimeout = Math.Max(ResponseTimeoutMilliseconds, FirstReadAfterReopenTimeoutMilliseconds);
      _reopenedThisTransaction = false;
    }

    byte[] response = ReadExactly(port, 18, responseTimeout, cancellationToken);
    int word = 0;
    for (int bit = 0; bit < 18; bit++)
    {
      if (response[bit] >= 0x80)
      {
        word |= 1 << bit;
      }
    }

    SettleUsb(settleMilliseconds, cancellationToken);
    return word & F18InstructionSet.WordMask;
  }

  private void WriteSequence(IReadOnlyList<int> sequence, CancellationToken cancellationToken) =>
      WriteSequence(_targetRoute, sequence, cancellationToken);

  private void WriteSequence(
      KrakenNodeRoute route,
      IReadOnlyList<int> sequence,
      CancellationToken cancellationToken,
      int settleMilliseconds = OnlineTransactionSettleMilliseconds)
  {
    NativeWindowsSerialPort port = RequirePort();
    cancellationToken.ThrowIfCancellationRequested();
    int headPort = HeadPortFor(route);
    byte[] frame = EncodeBootFrame(AsyncSerialContinuationAddress, headPort, sequence);
    port.Write(frame);
    WaitForTransmitDrain(port, frame.Length);
    SettleUsb(settleMilliseconds, cancellationToken);

    // A write-only transaction consumes the reopen: clear the flag so a later
    // read does not inherit the widened first-read-after-reopen timeout.
    // Inert under HoldOpen (the flag is never set there).
    _reopenedThisTransaction = false;
  }

  private KrakenRamZeroCheckResult CheckRamZero(KrakenNodeRoute route, CancellationToken cancellationToken)
  {
    int savedA = ReadWord(
        route,
        KrakenProtocol.BuildR1(route.Position, KrakenProtocol.ReadAInstruction),
        cancellationToken,
        CheckTransactionSettleMilliseconds);

    WriteSequence(
        route,
        KrakenProtocol.BuildW1(route.Position, KrakenProtocol.WriteAInstruction, 0),
        cancellationToken,
        CheckTransactionSettleMilliseconds);

    int savedRamZero = ReadWord(
        route,
        KrakenProtocol.BuildR1(route.Position, KrakenProtocol.ReadMemoryInstruction),
        cancellationToken,
        CheckTransactionSettleMilliseconds);

    int expected = route.Coordinate & F18InstructionSet.WordMask;
    int actual;
    bool replyCompleted = false;
    try
    {
      WriteSequence(
          route,
          KrakenProtocol.BuildW1(route.Position, KrakenProtocol.WriteMemoryInstruction, expected),
          cancellationToken,
          CheckTransactionSettleMilliseconds);

      actual = ReadWord(
          route,
          KrakenProtocol.BuildR1(route.Position, KrakenProtocol.ReadMemoryInstruction),
          cancellationToken,
          CheckTransactionSettleMilliseconds);
      replyCompleted = true;
    }
    finally
    {
      // Do not transmit cleanup frames after a read timeout: node 708 is
      // then still inside the reply helper rather than its async boot ROM.
      // The diagnostic stops after this failure. Reset/re-erection recovery is forbidden while Kraken is running.
      if (replyCompleted)
      {
        WriteSequence(
            route,
            KrakenProtocol.BuildW1(route.Position, KrakenProtocol.WriteMemoryInstruction, savedRamZero),
            cancellationToken,
            CheckTransactionSettleMilliseconds);
        WriteSequence(
            route,
            KrakenProtocol.BuildW1(route.Position, KrakenProtocol.WriteAInstruction, savedA),
            cancellationToken,
            CheckTransactionSettleMilliseconds);
      }
    }

    return actual == expected
        ? KrakenRamZeroCheckResult.Passed(route, expected, actual)
        : KrakenRamZeroCheckResult.ValueMismatch(route, expected, actual);
  }

  private int HeadPortFor(KrakenNodeRoute route)
  {
    KrakenTentacleConfiguration tentacle = _configuration.Tentacles
        .Single(item => item.Number == route.TentacleNumber);
    return KrakenTopology.PortAddress(KrakenTopology.HeadCoordinate, tentacle.Nodes[0]);
  }

  private static int[] BuildReplyProgram()
  {
    string source = $$"""
            0 org
            entry reply

            : reply
                io b!
                lo
                @
                17 for
                    dup dup 2/ 2* xor
                    if send-one else send-zero then
                    drop 2/
                next
                drop
                lo
                jump 0x0AE
            ;

            : hi 0x15557 !b ;
            : lo 0x15556 !b ;

            : wait-high
                begin
                    @b
                    -if
                        drop exit
                    then
                    drop
                again
            ;

            : wait-low
                begin
                    @b
                    -if
                        drop
                    else
                        drop exit
                    then
                again
            ;

            : consume wait-high wait-low ;

            : send-zero
                wait-high hi wait-low lo
                consume
            ;

            : send-one
                consume
                wait-high hi wait-low lo
            ;
            """;

    var compiler = new F18Compiler();
    F18CompileResult result = compiler.Compile(source, F18CompilerOptions.ForRam(KrakenTopology.HeadCoordinate));
    if (!result.Success)
    {
      string diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.ToString()));
      throw new InvalidOperationException("The node-708 Kraken reply helper did not compile.\n" + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException($"The node-708 Kraken reply helper requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (result.EntryPoint != 0)
    {
      int selectedEntry = result.EntryPoint ?? -1;
      throw new InvalidOperationException($"The node-708 Kraken reply helper must enter at RAM 0, but the compiler selected 0x{selectedEntry:X3}.");
    }

    return result.Words.Select(item => item & F18InstructionSet.WordMask).ToArray();
  }

  private static void SendBootFrame(NativeWindowsSerialPort port, int completionAddress, int transferAddress, IReadOnlyList<int> payload)
  {
    byte[] frame = EncodeBootFrame(completionAddress, transferAddress, payload);
    port.Write(frame);
    WaitForTransmitDrain(port, frame.Length);
    SettleUsb(OnlineTransactionSettleMilliseconds, CancellationToken.None);
  }

  private static byte[] EncodeBootFrame(int completionAddress, int transferAddress, IReadOnlyList<int> payload)
  {
    var words = new int[3 + payload.Count];
    words[0] = completionAddress & F18InstructionSet.WordMask;
    words[1] = transferAddress & F18InstructionSet.WordMask;
    words[2] = payload.Count & F18InstructionSet.WordMask;
    for (int index = 0; index < payload.Count; index++)
    {
      words[3 + index] = payload[index] & F18InstructionSet.WordMask;
    }

    var bytes = new byte[words.Length * 3];
    for (int index = 0; index < words.Length; index++)
    {
      // Reuse the exact, hardware-verified asynchronous encoding without
      // modifying the detector itself.
      Ga144Node708Probe.EncodeAsynchronousWord(words[index], bytes.AsSpan(index * 3, 3));
    }

    return bytes;
  }

  private static byte[] ReadExactly(NativeWindowsSerialPort port, int count, int timeoutMilliseconds, CancellationToken cancellationToken)
  {
    var result = new byte[count];
    int offset = 0;
    Stopwatch stopwatch = Stopwatch.StartNew();
    while (offset < count && stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int read = port.Read(result, offset, count - offset);
      if (read > 0)
      {
        offset += read;
      }
    }

    if (offset != count)
    {
      throw new TimeoutException($"Kraken reply timed out after receiving {offset} of {count} carrier-clocked bytes.");
    }

    return result;
  }

  private static void SettleUsb(int milliseconds, CancellationToken cancellationToken)
  {
    if (milliseconds <= 0)
    {
      return;
    }

    // Use short slices so cancellation remains responsive without creating
    // additional serial/USB operations during the quiet interval.
    int remaining = milliseconds;
    while (remaining > 0)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int slice = Math.Min(remaining, 5);
      Thread.Sleep(slice);
      remaining -= slice;
    }
  }

  private static void WaitForTransmitDrain(NativeWindowsSerialPort port, int byteCount)
  {
    // The native COM transport deliberately avoids FlushFileBuffers so no extra driver
    // request is generated here. Compute the wire time and add a small USB margin.
    double milliseconds = (byteCount * 10.0 * 1000.0 / port.BaudRate) + 3.0;
    Thread.Sleep((int)Math.Ceiling(milliseconds));
  }

  private async Task<T> RunExclusiveAsync<T>(Func<T> action, CancellationToken cancellationToken)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      await Task.Run(() => EnsureTransportOpen(cancellationToken), cancellationToken);
      return await Task.Run(action, cancellationToken);
    }
    finally
    {
      // No Kraken operation owns the COM endpoint beyond this exclusive
      // action. Closing the native handle here is intentional: the GA144
      // Kraken/tentacles remain resident and the same COM name is reopened
      // for the next action without issuing a reset.
      try
      {
        ParkTransport();
      }
      finally
      {
        _gate.Release();
      }
    }
  }

  private void EnsureTransportOpen(CancellationToken cancellationToken)
  {
    ThrowIfDisposed();
    cancellationToken.ThrowIfCancellationRequested();

    if (_port is { IsOpen: true })
    {
      return;
    }

    if (!_hardwareErectionCompleted)
    {
      throw new InvalidOperationException("Kraken has not yet been erected.");
    }

    if (_idlePolicy == KrakenIdlePolicy.HoldOpen)
    {
      // In HoldOpen a missing/closed handle AFTER erection means the USB
      // device was physically removed or the driver dropped it. We must not
      // silently reopen and glitch RESET- on the live GA144. Surface it as a
      // transport fault so KrakenLiveController blocks further use without
      // any automatic reset or re-erection.
      throw new IOException(
          "The Kraken COM handle is no longer open (USB device removed or driver reset). " +
          "The GA144 will not be reset automatically; re-erect the Kraken to continue.");
    }

    if (string.IsNullOrWhiteSpace(_portName))
    {
      throw new InvalidOperationException("The Kraken COM endpoint is unknown.");
    }

    // A transaction is (re)opening: cancel any pending idle-close so it cannot
    // fire during this transaction. ParkTransport re-arms it afterward.
    _idleCloseTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

    // CloseWhileIdle: reopen only the host transport. NativeWindowsSerialPort
    // .Open forces DTR/RTS inactive-high immediately after CreateFile. No
    // reset pulse, node-708 probe, boot helper reload, or tentacle erection
    // is issued.
    //
    // A single CreateFile can transiently fail while the previous CloseHandle
    // is still tearing the FTDI endpoint down, or while the device wakes from
    // selective suspend. Retry with bounded back-off before allowing a fault,
    // so one unlucky reopen does not permanently brick the resident Kraken.
    int backoff = ReopenInitialBackoffMilliseconds;
    IOException? lastError = null;

    for (int attempt = 1; attempt <= ReopenMaxAttempts; attempt++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        _port = NativeWindowsSerialPort.Open(
            _portName!,
            OnlineBaudRate,
            readTimeoutMilliseconds: 50,
            writeTimeoutMilliseconds: 2_000);

        // A tiny line-settle interval avoids beginning the first boot byte
        // in the same instant as the FTDI modem-control state is restored.
        Thread.Sleep(2);
        _reopenedThisTransaction = true;
        return;
      }
      catch (IOException exception)
      {
        lastError = exception;
        if (attempt == ReopenMaxAttempts)
        {
          break;
        }

        // Best-effort wait for the endpoint to settle, honouring cancel.
        if (cancellationToken.WaitHandle.WaitOne(backoff))
        {
          cancellationToken.ThrowIfCancellationRequested();
        }
        backoff *= ReopenBackoffMultiplier;
      }
    }

    throw new IOException(
        $"Could not reopen the parked Kraken COM endpoint '{_portName}' after {ReopenMaxAttempts} attempts. " +
        "No reset or re-erection was attempted.",
        lastError);
  }

  private void ParkTransport()
  {
    if (_idlePolicy == KrakenIdlePolicy.HoldOpen)
    {
      // Do NOT close the FTDI handle while the Kraken is resident. Closing
      // on Port A risks a driver-default RTS assertion on the next
      // CreateFile, which is a RESET- glitch on the live GA144, and lets the
      // device enter selective suspend. Quiesce the open handle only; _port
      // is deliberately left non-null so it stays valid for the next call.
      NativeWindowsSerialPort? open = _port;
      if (open is null)
      {
        return;
      }

      try
      {
        open.ParkIdle();
      }
      catch
      {
        // Parking is best-effort and must never cause an automatic reset.
      }

      return;
    }

    if (_idlePolicy == KrakenIdlePolicy.CloseAfterIdleTimeout)
    {
      // Keep the handle open and (re)arm the 1 s idle timer. A following
      // transaction within the window re-arms it, so a burst stays open and the
      // handle closes once ~1 s after the last transaction. A keep-open scope, if
      // present, suppresses the close entirely until it ends.
      NativeWindowsSerialPort? open = _port;
      if (open is not null)
      {
        try
        {
          open.ParkIdle();
        }
        catch
        {
          // Best-effort quiesce; a real transaction will surface any fault.
        }
      }

      ArmIdleClose();
      return;
    }

    // CloseWhileIdle: release the native handle so the COM port can be shared
    // while idle. The next transaction reopens it via EnsureTransportOpen.
    //
    // Exception: inside a keep-open scope (a batch such as a full Check Kraken)
    // leave the handle open so the whole batch reopens/closes the FTDI once
    // instead of once per operation. The scope's EndKeepOpen performs the single
    // close. Quiesce the still-open handle so the next operation starts clean.
    if (_keepOpenDepth > 0)
    {
      NativeWindowsSerialPort? held = _port;
      if (held is null)
      {
        return;
      }

      try
      {
        held.ParkIdle();
      }
      catch
      {
        // Best-effort quiesce; a real transaction will surface any fault.
      }

      return;
    }

    NativeWindowsSerialPort? port = _port;
    _port = null;
    if (port is null)
    {
      return;
    }

    try
    {
      port.CloseForIdle();
    }
    catch
    {
      // The caller will observe any actual Kraken transaction failure.
      // Parking is best-effort and must never cause an automatic reset.
    }
  }

  /// <summary>
  /// CloseAfterIdleTimeout: (re)arm the one-shot idle timer. Called while the
  /// session gate is held (from ParkTransport). Inside a keep-open scope the timer
  /// is left disarmed so the scope, not the timer, controls the close.
  /// </summary>
  private void ArmIdleClose()
  {
    if (_disposed || _idleCloseTimer is null)
    {
      return;
    }

    if (_keepOpenDepth > 0)
    {
      // A batch owns the handle; the scope's end will close it. Ensure no stale
      // timer is pending so it cannot fire mid-batch.
      _idleCloseTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
      return;
    }

    // Single-shot: fire once after the idle window, then stay disarmed.
    _idleCloseTimer.Change(IdleCloseTimeoutMilliseconds, System.Threading.Timeout.Infinite);
  }

  /// <summary>
  /// Idle-timer callback (thread-pool thread). Closes the handle if the session
  /// is still idle: takes the gate so it cannot race a transaction, and re-checks
  /// disposal, keep-open depth, and that no transaction re-armed in the meantime.
  /// </summary>
  private void OnIdleCloseElapsed(object? state)
  {
    // Do not block the timer thread indefinitely; if a transaction holds the gate
    // the close is unnecessary anyway (that transaction will re-arm on its park).
    if (!_gate.Wait(0))
    {
      return;
    }

    try
    {
      if (_disposed || _keepOpenDepth > 0)
      {
        return;
      }

      NativeWindowsSerialPort? port = _port;
      _port = null;
      if (port is null)
      {
        return;
      }

      try
      {
        port.CloseForIdle();
      }
      catch
      {
        // Best-effort idle close; must never cause an automatic reset.
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// Begin a keep-open scope. Under CloseWhileIdle, ParkTransport will quiesce
  /// but not close the FTDI handle until the matching EndKeepOpenAsync, so a batch
  /// of exclusive operations reopens/closes the device once instead of per call.
  /// No-op under HoldOpen (the handle is already held open while resident).
  /// Nesting is supported via a depth counter.
  /// </summary>
  public async Task BeginKeepOpenAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      ThrowIfDisposed();
      _keepOpenDepth++;
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// End a keep-open scope. When the outermost scope closes under CloseWhileIdle,
  /// the FTDI handle is closed once. No-op under HoldOpen.
  /// </summary>
  public async Task EndKeepOpenAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      if (_disposed || _keepOpenDepth == 0)
      {
        return;
      }

      _keepOpenDepth--;
      if (_keepOpenDepth == 0)
      {
        if (_idlePolicy == KrakenIdlePolicy.CloseWhileIdle)
        {
          // The batch is finished: perform the single deferred close now.
          NativeWindowsSerialPort? port = _port;
          _port = null;
          if (port is not null)
          {
            try
            {
              port.CloseForIdle();
            }
            catch
            {
              // Best-effort close; parking must never cause an automatic reset.
            }
          }
        }
        else if (_idlePolicy == KrakenIdlePolicy.CloseAfterIdleTimeout)
        {
          // The batch is finished: don't close now, arm the 1 s idle timer so a
          // follow-up operation can still reuse the open handle.
          ArmIdleClose();
        }
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  private NativeWindowsSerialPort RequirePort()
  {
    ThrowIfDisposed();
    return _port is { IsOpen: true } port
        ? port
        : throw new InvalidOperationException("The Kraken COM transport is currently parked; an explicit operation must open it first.");
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}