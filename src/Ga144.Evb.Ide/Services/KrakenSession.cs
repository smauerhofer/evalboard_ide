using System.Diagnostics;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

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
///
/// Transport: node 708 is booted with the new sett/setn/w/r head protocol (see
/// <see cref="Ga144Node708HeadProtocol"/> for the host-side primitives this
/// reuses the same wire shapes as) instead of the old carrier-clocked
/// hi/lo/wait-high/wait-low reply helper. Every erection step and every online
/// transaction is now a plain async-encoded word request/reply pair, and every
/// tentacle hop is relayed with the focus/writeA/.../tentacle(n) word sequences
/// built by <see cref="KrakenProtocol"/>. There is no more host-driven carrier
/// clocking anywhere in this class.
/// </summary>
internal sealed class KrakenSession : IAsyncDisposable
{
  // Node 708's own async word transport runs at the same hardware-verified
  // line rate as the node-708 detector and the head-protocol probe.
  public const int OnlineBaudRate = Ga144Serial.MaximumBaudRate;

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

  // Every word sent to node 708 after boot is decoded by '18ibits', which
  // calls 'sync' to recalibrate bit timing fresh for that word (rom_async's
  // "sync sync dup start..." per-word preamble). The only hardware-proven
  // pacing of this receive path (Ga144Node708EchoProbe) sends one word,
  // waits for a full reply, then sends the next -- it never sends two words
  // back-to-back. 'w/r' asks node 708 to receive several words in a row
  // (wordsToRead, writeWords.Count, then each payload word) with no reply in
  // between, which is new and unproven, and a documented hardware hazard
  // (Ga144Node708DelayProbe) shows 'sync's polling loop can desync around an
  // unexpected gap on the wire. Pad every post-boot word send with extra
  // settle time beyond the bare wire-drain time, as a first, safe,
  // host-only experiment against a too-tight inter-word gap being the cause
  // of a totally silent (0-byte) reply timeout. Revert or retune once real
  // hardware confirms whether this actually matters.
  private const int InterWordSettleMilliseconds = 20;

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
  private readonly Ga144ChipConfiguration _chip;
  private readonly Ga144RomLibrary _romLibrary;
  private readonly KrakenIdlePolicy _idlePolicy;
  // True only when opening this endpoint's COM port pulses the GA144 RESET-.
  // On the EVB that is Port A (Host): RTS is wired to RESET- and the stock FTDI
  // driver briefly asserts RTS during CreateFile, which resets the chip and
  // destroys a resident Kraken, so every reopen must re-erect. Port C (Target)
  // has no such wiring, so its reopen is a plain transport reopen (no re-erect).
  private readonly bool _reopenResetsChip;
  private KrakenNodeRoute _targetRoute;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private NativeWindowsSerialPort? _port;
  private string? _portName;
  private bool _disposed;
  private bool _hardwareErectionCompleted;
  // Node 708's sett/setn/w/r RAM addresses, resolved from the compiler's own
  // symbol table when the head program is booted (see BuildHeadProgram's
  // remarks -- this program only fits 64 words with packing enabled, so
  // unlike this project's other fixed-layout probes these addresses are not
  // assumed constant across builds).
  private Node708HeadAddresses? _headAddresses;
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
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      KrakenIdlePolicy idlePolicy = KrakenIdlePolicy.HoldOpen,
      bool reopenResetsChip = true)
  {
    ArgumentNullException.ThrowIfNull(configuration);
    ArgumentNullException.ThrowIfNull(targetRoute);
    ArgumentNullException.ThrowIfNull(chip);
    ArgumentNullException.ThrowIfNull(romLibrary);

    if (targetRoute.IsHead)
    {
      throw new InvalidOperationException("Node 708 is the Kraken head and cannot be controlled through a tentacle session.");
    }

    _configuration = configuration;
    _targetRoute = targetRoute;
    _chip = chip;
    _romLibrary = romLibrary;
    _idlePolicy = idlePolicy;
    _reopenResetsChip = reopenResetsChip;

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
      RunExclusiveAsync(() => ReadA(_targetRoute, cancellationToken), cancellationToken);

  public Task WriteAAsync(int value, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        WriteA(_targetRoute, value, cancellationToken);
        return 0;
      }, cancellationToken);

  public Task<int> ReadIoAsync(CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        int savedA = ReadA(_targetRoute, cancellationToken);
        try
        {
          WriteA(_targetRoute, IoAddress, cancellationToken);
          return ReadMemory(_targetRoute, cancellationToken);
        }
        finally
        {
          WriteA(_targetRoute, savedA, cancellationToken);
        }
      }, cancellationToken);

  public Task WriteIoAsync(int value, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        int savedA = ReadA(_targetRoute, cancellationToken);
        try
        {
          WriteA(_targetRoute, IoAddress, cancellationToken);
          WriteMemory(_targetRoute, value, cancellationToken);
        }
        finally
        {
          WriteA(_targetRoute, savedA, cancellationToken);
        }

        return 0;
      }, cancellationToken);

  public Task<IReadOnlyList<int>> ReadRamAsync(CancellationToken cancellationToken = default) =>
      RunExclusiveAsync<IReadOnlyList<int>>(() =>
          Transact(_targetRoute, KrakenProtocol.BuildReadRam(), wordsToRead: 64, cancellationToken), cancellationToken);

  public Task WriteRamAsync(IReadOnlyList<int> words, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        if (words.Count != 64)
        {
          throw new ArgumentException("A GA144 node RAM image contains exactly 64 words.", nameof(words));
        }

        _ = Transact(_targetRoute, KrakenProtocol.BuildWriteRam(words), wordsToRead: 1, cancellationToken);
        return 0;
      }, cancellationToken);

  public Task<IReadOnlyList<int>> ReadRomAsync(CancellationToken cancellationToken = default) =>
      RunExclusiveAsync<IReadOnlyList<int>>(() =>
          Transact(_targetRoute, KrakenProtocol.BuildReadRom(), wordsToRead: 64, cancellationToken), cancellationToken);

  // 'readPStack' pops and sends back 10 words (see KrakenProtocol's remarks
  // on the write/read count asymmetry given for the parameter stack).
  public Task<IReadOnlyList<int>> ReadParameterStackAsync(CancellationToken cancellationToken = default) =>
      RunExclusiveAsync<IReadOnlyList<int>>(() =>
      {
        int[] topToBottom = Transact(_targetRoute, KrakenProtocol.BuildReadPStack(), wordsToRead: 10, cancellationToken);
        var bottomToTop = topToBottom.AsEnumerable().Reverse().ToArray();
        return bottomToTop;
      }, cancellationToken);

  // 'writePStack' pushes exactly 9 words (see KrakenProtocol's remarks).
  public Task WriteParameterStackAsync(IReadOnlyList<int> bottomToTop, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        if (bottomToTop.Count != 9)
        {
          throw new ArgumentException("'writePStack' restores exactly 9 parameter-stack words.", nameof(bottomToTop));
        }

        _ = Transact(_targetRoute, KrakenProtocol.BuildWritePStack(bottomToTop), wordsToRead: 1, cancellationToken);
        return 0;
      }, cancellationToken);

  // The F18A return stack is 9 words (R plus 8 circular cells).
  public Task<IReadOnlyList<int>> ReadReturnStackAsync(CancellationToken cancellationToken = default) =>
      RunExclusiveAsync<IReadOnlyList<int>>(() =>
      {
        int[] topToBottom = Transact(_targetRoute, KrakenProtocol.BuildReadRStack(), wordsToRead: 9, cancellationToken);
        var bottomToTop = topToBottom.AsEnumerable().Reverse().ToArray();
        return bottomToTop;
      }, cancellationToken);

  public Task WriteReturnStackAsync(IReadOnlyList<int> bottomToTop, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        if (bottomToTop.Count != 9)
        {
          throw new ArgumentException("'writeRStack' restores exactly 9 return-stack words (rs(8)..rs(0)).", nameof(bottomToTop));
        }

        // The caller supplies bottom-to-top (matching the parameter-stack
        // convention); writeRStack wants rs(8) first, i.e. top first.
        var rs8ToRs0 = bottomToTop.AsEnumerable().Reverse().ToArray();
        _ = Transact(_targetRoute, KrakenProtocol.BuildWriteRStack(rs8ToRs0), wordsToRead: 1, cancellationToken);
        return 0;
      }, cancellationToken);

  public Task WriteBAsync(int value, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        _ = Transact(_targetRoute, KrakenProtocol.BuildWriteB(value), wordsToRead: 1, cancellationToken);
        return 0;
      }, cancellationToken);

  // 'focus' is the same "pop a word, jump P to it" primitive whether the
  // popped word is a compass port address (erection) or an arbitrary 10-bit
  // RAM address (this Jump). Reused directly rather than duplicated. Note
  // its trailing '!p' destroys whatever was on the target's top of stack
  // (see KrakenProtocol.BuildFocus) -- an accepted side effect of a jump.
  public Task JumpAsync(int destination, CancellationToken cancellationToken = default) =>
      RunExclusiveAsync(() =>
      {
        if (destination is < 0 or > 0x3FF)
        {
          throw new ArgumentOutOfRangeException(nameof(destination), "P is a 10-bit address.");
        }

        _ = Transact(_targetRoute, KrakenProtocol.BuildFocus(destination), wordsToRead: 1, cancellationToken);
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
      ErectOnto(port, cancellationToken);
      _port = port;
      _hardwareErectionCompleted = true;

      if (verifyTarget)
      {
        // Do not report an online node session until a real read has
        // traversed the selected tentacle and returned through node 708.
        _ = ReadA(_targetRoute, cancellationToken);
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

  // Run the full erection sequence (reset pulse, node-708 head-protocol load, and
  // tentacle focus/A setup) on an already-open port. Used both for the initial
  // erection and to RE-erect after an idle-close reopen, because opening the COM
  // port on Port A pulses RESET- (the FTDI VCP driver briefly asserts RTS during
  // CreateFile) and wipes the resident Kraken. There is no way to reopen without
  // resetting on Port A, so the Kraken must be rebuilt on every reopen.
  private void ErectOnto(NativeWindowsSerialPort port, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    port.SetDtr(true);
    // Preserve the exact verified EVB reset polarity used by the node-708 detector.
    port.SetRts(false);
    Thread.Sleep(ResetAssertMilliseconds);
    port.PurgeInputOutput();
    port.SetRts(true);
    Thread.Sleep(ResetReleaseMilliseconds);

    // The frame is accepted by node 708 through `cold`. Only ONE boot frame is
    // ever sent for the whole Kraken session -- everything after this is
    // 'main's own single-word dispatch loop, not another boot frame -- so the
    // completion address must point directly at the freshly loaded payload's
    // entry ('main', which always compiles to 0x000 here), not at ser-exec
    // (the ROM entry that waits for yet another frame header). Pointing
    // completion at ser-exec left the chip parked in ROM waiting for a
    // frame that never came, silently swallowing every post-boot word as
    // bogus follow-up-frame data instead of running 'main' at all -- this
    // matches both observed failures: the old (pre-readw) design only
    // noticed at 'focus', its first acknowledged reply, while the readw
    // design notices immediately, at sett's own first echo. Confirmed against
    // Ga144Node708EchoProbe, which boots with completion == transfer ==
    // entry address == 0 and works (22/22 pattern-sweep match).
    (int[] headProgram, Node708HeadAddresses addresses) = BuildHeadProgram();
    SendBootFrame(port, 0x000, 0x000, headProgram);
    _headAddresses = addresses;

    foreach (KrakenTentacleConfiguration tentacle in _configuration.Tentacles.OrderBy(item => item.Number))
    {
      int tentacleHeadPort = KrakenTopology.PortAddress(KrakenTopology.HeadCoordinate, tentacle.Nodes[0]);
      // Node 708's A stays pointed at this tentacle's own single hop for
      // every node erected along it: deeper hops are reached purely by the
      // tentacle(n) wrapping below, not by moving A. 'sett' deliberately
      // targets A, not B -- node 708's own dispatch read ('main's bare
      // '18ibits', and 'readw's inside it) polls via 'sync'/'wait's '@b',
      // so B must stay pointed at the io register for every post-boot
      // receive. Pointing 'sett' at B (the original design) worked for
      // exactly one round trip and then went silent forever, because the
      // very first 'sett' call repointed B away from io and 'main' was
      // still listening on it for the next dispatch word -- confirmed via
      // Ga144Node708DispatchProbe (1st 'sett' call succeeds, 2nd times out
      // identically regardless of target).
      SelectTentacle708(port, tentacleHeadPort, cancellationToken);

      for (int position = 0; position < tentacle.Nodes.Count; position++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        int coordinate = tentacle.Nodes[position];
        int previous = position == 0
            ? KrakenTopology.HeadCoordinate
            : tentacle.Nodes[position - 1];

        // Erect this node's own focus first (anchor its P on its incoming
        // port), reaching it by relaying through the 'position' nodes ahead
        // of it that are already erected. focus's own trailing '!p' sends
        // back whatever was on that node's T (destroying it) -- not
        // meaningful data, just the 1 real reply word every relay hop
        // requires; discarded here as a pure sync pulse.
        int incomingPort = KrakenTopology.PortAddress(coordinate, previous);
        // Raw, un-wrapped leaf -- node 708's own 'w/r' now builds the relay
        // wrapper on-chip from 'position' (see WriteRead708's remarks), so
        // this must NOT also go through KrakenProtocol.BuildTentacle.
        _ = WriteRead708(
            port, KrakenProtocol.BuildFocus(incomingPort), wordsToRead: 1, position,
            context: $"erecting node {coordinate:000} (tentacle {tentacle.Number} position {position}), 'focus' -> port 0x{incomingPort:X3}",
            cancellationToken);

        // Then this node's B, so it can relay further out once the next
        // node's focus is erected. The last node in a tentacle has nothing
        // further to relay to; point it at IoAddress like the old scheme did.
        int b = position + 1 < tentacle.Nodes.Count
            ? KrakenTopology.PortAddress(coordinate, tentacle.Nodes[position + 1])
            : IoAddress;
        _ = WriteRead708(
            port, KrakenProtocol.BuildWriteB(b), wordsToRead: 1, position,
            context: $"erecting node {coordinate:000} (tentacle {tentacle.Number} position {position}), 'writeB' -> 0x{b:X3}",
            cancellationToken);
      }
    }

    SettleUsb(OnlineTransactionSettleMilliseconds, cancellationToken);
    port.PurgeInput();
  }

  // ---- node-708 word transport --------------------------------------------
  // Every request/reply to/from node 708 travels as plain async-encoded words
  // (3 bytes each, the same wire shape as a boot-frame field): host to node
  // via Ga144Node708Probe.EncodeAsynchronousWord (decoded on the node by
  // '18ibits'), node to host via node 708's own 'oword'/'obyt' direct-UART
  // transmit, decoded here by DecodeObywordReply. No host-driven carrier
  // clocking is involved anywhere in this path.
  //
  // Node 708's head program now reads every word EXCEPT the initial
  // dispatch/call address through 'readw' instead of bare '18ibits': 'readw'
  // echoes the word straight back over the port before returning it, so the
  // host can confirm receipt before sending the next one. This replaces the
  // earlier fixed-delay-only pacing (InterWordSettleMilliseconds) with a
  // real handshake for every word inside 'sett'/'setn'/'w/r's own bodies --
  // the part of this scheme that sends several words in a row with nothing
  // to naturally pace it, unlike 'main's own single dispatch-address read,
  // which still uses plain '18ibits' and is sent via SendWord708 (no ack).

  private static void SendWord708(NativeWindowsSerialPort port, int value)
  {
    SendWord708Raw(port, value);
    // Only used for the single, unacknowledged dispatch-address word 'main'
    // reads via bare '18ibits'; keep a fixed settle margin here since there
    // is no echo to naturally pace against.
    Thread.Sleep(InterWordSettleMilliseconds);
  }

  private static void SendWord708Raw(NativeWindowsSerialPort port, int value)
  {
    byte[] bytes = new byte[3];
    Ga144Node708Probe.EncodeAsynchronousWord(value, bytes);
    port.Write(bytes);
    WaitForTransmitDrain(port, bytes.Length);
  }

  // Sends one word that node 708 reads via 'readw' and immediately echoes
  // back, then blocks for that echo and verifies it matches. This is the
  // actual fix for the totally-silent timeouts: the read IS the pacing, so
  // there is no separate fixed delay here (unlike SendWord708) -- the block
  // on ReadWord708 already waits exactly as long as node 708 needs.
  private static void SendWord708AndVerify(
      NativeWindowsSerialPort port,
      int value,
      string context,
      CancellationToken cancellationToken,
      int responseTimeoutMilliseconds = ResponseTimeoutMilliseconds)
  {
    int expected = value & F18InstructionSet.WordMask;
    SendWord708Raw(port, expected);

    int echoed;
    try
    {
      echoed = ReadWord708(port, responseTimeoutMilliseconds, cancellationToken);
    }
    catch (TimeoutException exception)
    {
      throw new TimeoutException(
          $"Kraken word acknowledgment timed out ({context}, sent 0x{expected:X5}): {exception.Message}", exception);
    }

    if (echoed != expected)
    {
      throw new IOException(
          $"Kraken word acknowledgment mismatch ({context}): sent 0x{expected:X5}, node echoed 0x{echoed:X5}.");
    }
  }

  private static int ReadWord708(NativeWindowsSerialPort port, int timeoutMilliseconds, CancellationToken cancellationToken)
  {
    byte[] bytes = ReadExactly(port, 3, timeoutMilliseconds, cancellationToken);
    return DecodeObywordReply(bytes);
  }

  // Inverse of node 708's own 'oword'/'obyt' transmit encoding -- see the
  // identical helper and remarks on Ga144Node708HeadProtocol.DecodeObywordReply.
  private static int DecodeObywordReply(byte[] threeBytes)
  {
    if (threeBytes is null || threeBytes.Length != 3)
    {
      throw new ArgumentException("An obyt/oword reply is exactly 3 bytes.", nameof(threeBytes));
    }

    int value = threeBytes[0] | (threeBytes[1] << 8) | ((threeBytes[2] & 0x03) << 16);
    return value & F18InstructionSet.WordMask;
  }

  private void SelectTentacle708(NativeWindowsSerialPort port, int portAddress, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    Node708HeadAddresses addresses = RequireHeadAddresses();
    SendWord708(port, addresses.SetTentacle);
    SendWord708AndVerify(port, portAddress, "sett: port value", cancellationToken);
  }

  // Calls node 708's 'w/r': writes writeWords then reads back wordsToRead
  // words. Both counts must be at least 1, matching 'w/r's own "at least 1
  // word must be written, at least 1 word must be read" precondition. 'w/r'
  // itself no longer 'dec's either count (removed so the count words fit
  // through 'readw' without an extra step) -- its own 'begin...next' loops
  // are do-while shaped, running once for every unit of R plus one more, so
  // the value placed on the wire must already be one less than the real
  // count, matching the same "-1 convention" KrakenProtocol.cs already
  // documents and uses for every leaf/tentacle-hop literal. Sending the raw
  // (non-decremented) count here left the write-receive loop expecting one
  // extra word that never arrives -- node 708 blocks forever inside its own
  // 'readw', never reaching 'oword', which reads back as a silent, 0-byte
  // timeout on the FIRST reply word regardless of how many were requested.
  //
  // 'setn' is gone -- 'n' (how many hops out the node being addressed sits
  // along its tentacle) is now read directly by 'w/r' itself, via its own
  // 'readw' calls at the top of write-pre and write-post, instead of a
  // separate dispatch beforehand. 'position' is that value in the same
  // position-1 convention as before: position 0 (the tentacle's own first
  // node, reached directly) sends -1 and both 'n'-branches take their
  // "nothing to relay" path; position 1 sends 0, position 2 sends 1, and so
  // on. It must be sent on EVERY call, not cached, since there is no longer
  // anywhere on node 708 that remembers it between calls.
  //
  // When 'n' is non-negative, 'w/r's write-pre/write-post loops build a
  // relay wrapper on node 708 itself, one 6-word entry per hop: the
  // 'A[ @p >r ]] !' / 'A[ @p !b unext ]] !' pairs compile the literal
  // opcode words 'Pack("@p", ">r")' / 'Pack("@p", "!b", "unext")' -- the
  // exact same opcodes KrakenProtocol.WrapTentacleHop packs -- directly into
  // scratch RAM and store them via A. This replaces WrapTentacleHop's own
  // software-side wrapping: 'writeWords' below is now the RAW, un-wrapped
  // leaf (e.g. KrakenProtocol.BuildFocus's own 3 words), never passed
  // through KrakenProtocol.BuildTentacle -- wrapping it in software AND
  // telling node 708 to wrap it again on-chip would relay the same payload
  // twice. The relay still depends on each intermediate node's own B already
  // pointing at the next hop, exactly as WrapTentacleHop's software version
  // did -- ErectOnto's per-position 'writeB' step still establishes that
  // chain, unchanged.
  //
  // 'context' is a short human-readable label (which node/tentacle/position,
  // which operation) with no effect on the wire -- it exists purely so a
  // timeout, if one happens, says exactly where in the erection/transaction
  // sequence it happened instead of just "0 of 3 bytes", since this whole
  // sett/w/r path has not yet been fully exercised on real hardware and a
  // bare timeout alone does not say whether the stall is in node 708's own
  // dispatch, in a specific relay hop, or somewhere else entirely.
  private int[] WriteRead708(
      NativeWindowsSerialPort port,
      IReadOnlyList<int> writeWords,
      int wordsToRead,
      int position,
      string context,
      CancellationToken cancellationToken,
      int responseTimeoutMilliseconds = ResponseTimeoutMilliseconds)
  {
    ArgumentNullException.ThrowIfNull(writeWords);
    if (writeWords.Count == 0)
    {
      throw new ArgumentException("'w/r' requires at least 1 word to write.", nameof(writeWords));
    }

    if (wordsToRead <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(wordsToRead), "'w/r' requires at least 1 word to read.");
    }

    cancellationToken.ThrowIfCancellationRequested();
    Node708HeadAddresses addresses = RequireHeadAddresses();
    int n = position - 1;
    try
    {
      // Only the call address itself goes through 'main's raw, unacknowledged
      // '18ibits'; every word 'w/r' reads for itself (wordsToRead,
      // writeWords.Count, 'n' twice, and each forwarded payload word) now
      // goes through 'readw' and is verified here. wordsToRead and
      // writeWords.Count are sent one less than their real values -- see
      // this method's own remarks above -- since 'w/r' no longer 'dec's them
      // itself. 'n' is sent TWICE: once before write-pre's own '-if' check,
      // once more before write-post's, matching the two separate 'readw'
      // calls 'w/r' now makes for it.
      SendWord708(port, addresses.WriteRead);
      SendWord708AndVerify(port, wordsToRead - 1, $"{context}: wordsToRead", cancellationToken, responseTimeoutMilliseconds);
      SendWord708AndVerify(port, writeWords.Count - 1, $"{context}: writeWords.Count", cancellationToken, responseTimeoutMilliseconds);
      SendWord708AndVerify(port, n, $"{context}: n (write-pre)", cancellationToken, responseTimeoutMilliseconds);
      for (int index = 0; index < writeWords.Count; index++)
      {
        SendWord708AndVerify(port, writeWords[index], $"{context}: payload word {index + 1} of {writeWords.Count}", cancellationToken, responseTimeoutMilliseconds);
      }

      SendWord708AndVerify(port, n, $"{context}: n (write-post)", cancellationToken, responseTimeoutMilliseconds);
    }
    catch (Exception exception) when (exception is IOException or TimeoutException)
    {
      throw new IOException($"Kraken transaction failed while sending the 'w/r' request ({context}, {writeWords.Count} write word(s), {wordsToRead} to read).", exception);
    }

    var result = new int[wordsToRead];
    for (int index = 0; index < wordsToRead; index++)
    {
      try
      {
        result[index] = ReadWord708(port, responseTimeoutMilliseconds, cancellationToken);
      }
      catch (TimeoutException exception)
      {
        throw new TimeoutException(
            $"Kraken reply timed out ({context}, reply word {index + 1} of {wordsToRead}): {exception.Message}", exception);
      }
    }

    return result;
  }

  private Node708HeadAddresses RequireHeadAddresses() =>
      _headAddresses ?? throw new InvalidOperationException("Node 708's head protocol addresses are not known; Kraken has not been erected.");

  // Selects the given route's tentacle and relays 'leaf' out to its position,
  // returning wordsToRead reply words. This is the one online-transaction
  // primitive every per-node operation below builds on. 'leaf' is sent RAW
  // (un-wrapped) -- node 708's own 'w/r' builds the relay wrapper on-chip
  // from route.Position now, see WriteRead708's remarks; wrapping it here too
  // via KrakenProtocol.BuildTentacle would relay it twice. 'operationName' is
  // captured automatically from the calling method (e.g. "ReadA",
  // "WriteRamAsync") purely for diagnostics -- see WriteRead708.
  private int[] Transact(
      KrakenNodeRoute route,
      IReadOnlyList<int> leaf,
      int wordsToRead,
      CancellationToken cancellationToken,
      int settleMilliseconds = OnlineTransactionSettleMilliseconds,
      [System.Runtime.CompilerServices.CallerMemberName] string operationName = "")
  {
    NativeWindowsSerialPort port = RequirePort();
    cancellationToken.ThrowIfCancellationRequested();

    int headPort = HeadPortFor(route);
    string context = $"node {route.Coordinate:000} (tentacle {route.TentacleNumber} position {route.Position}), '{operationName}'";

    // Under the idle-close policies the first transaction after a reopen can lose
    // its request while the freshly opened FTDI is still initialising, so the
    // node returns nothing and the read times out. If that happens on the first
    // transaction after a reopen, purge and retry once; a single dropped frame
    // must not fail an entire operation. The flag is never set under HoldOpen,
    // so this retry is inert there.
    bool firstAfterReopen = _reopenedThisTransaction;
    _reopenedThisTransaction = false;
    int responseTimeout = firstAfterReopen
        ? Math.Max(ResponseTimeoutMilliseconds, FirstReadAfterReopenTimeoutMilliseconds)
        : ResponseTimeoutMilliseconds;

    int[] reply;
    try
    {
      SelectTentacle708(port, headPort, cancellationToken);
      reply = WriteRead708(port, leaf, wordsToRead, route.Position, context, cancellationToken, responseTimeout);
    }
    catch (TimeoutException) when (firstAfterReopen)
    {
      try { port.PurgeInput(); } catch { }
      SettleUsb(OnlineTransactionSettleMilliseconds, cancellationToken);
      SelectTentacle708(port, headPort, cancellationToken);
      reply = WriteRead708(port, leaf, wordsToRead, route.Position, context, cancellationToken, responseTimeout);
    }

    SettleUsb(settleMilliseconds, cancellationToken);
    return reply;
  }

  private int ReadA(KrakenNodeRoute route, CancellationToken cancellationToken, int settleMilliseconds = OnlineTransactionSettleMilliseconds) =>
      Transact(route, KrakenProtocol.BuildReadA(), wordsToRead: 1, cancellationToken, settleMilliseconds)[0];

  private void WriteA(KrakenNodeRoute route, int value, CancellationToken cancellationToken, int settleMilliseconds = OnlineTransactionSettleMilliseconds) =>
      Transact(route, KrakenProtocol.BuildWriteA(value), wordsToRead: 1, cancellationToken, settleMilliseconds);

  // Reads/writes the single word at whatever address is currently in A
  // (non-incrementing '@'/'!'). Not part of the given formula set -- added
  // to preserve arbitrary single-address access (e.g. IoAddress), which
  // none of focus/writeA/.../readRStack cover on their own. Mirrors the old
  // ReadMemoryInstruction/WriteMemoryInstruction opcode pairs exactly, just
  // packed through KrakenProtocol's own Pack helper -- see KrakenProtocol.
  private int ReadMemory(KrakenNodeRoute route, CancellationToken cancellationToken, int settleMilliseconds = OnlineTransactionSettleMilliseconds) =>
      Transact(route, KrakenProtocol.BuildReadMemory(), wordsToRead: 1, cancellationToken, settleMilliseconds)[0];

  private void WriteMemory(KrakenNodeRoute route, int value, CancellationToken cancellationToken, int settleMilliseconds = OnlineTransactionSettleMilliseconds) =>
      Transact(route, KrakenProtocol.BuildWriteMemory(value), wordsToRead: 1, cancellationToken, settleMilliseconds);

  private KrakenRamZeroCheckResult CheckRamZero(KrakenNodeRoute route, CancellationToken cancellationToken)
  {
    int savedA = ReadA(route, cancellationToken, CheckTransactionSettleMilliseconds);

    WriteA(route, 0, cancellationToken, CheckTransactionSettleMilliseconds);

    int savedRamZero = ReadMemory(route, cancellationToken, CheckTransactionSettleMilliseconds);

    int expected = route.Coordinate & F18InstructionSet.WordMask;
    int actual;
    bool replyCompleted = false;
    try
    {
      WriteMemory(route, expected, cancellationToken, CheckTransactionSettleMilliseconds);

      actual = ReadMemory(route, cancellationToken, CheckTransactionSettleMilliseconds);
      replyCompleted = true;
    }
    finally
    {
      // Do not transmit cleanup frames after a read timeout: node 708 is
      // then still mid-dispatch rather than idle in 'main'. The diagnostic
      // stops after this failure. Reset/re-erection recovery is forbidden while Kraken is running.
      if (replyCompleted)
      {
        WriteMemory(route, savedRamZero, cancellationToken, CheckTransactionSettleMilliseconds);
        WriteA(route, savedA, cancellationToken, CheckTransactionSettleMilliseconds);
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

  // Builds node 708's new sett/setn/w/r head program and resolves its
  // dispatch addresses from the compiler's own symbol table. Unlike this
  // project's other directly-booted node-708 probes, this program only fits
  // node 708's 64-word RAM with control-transfer packing ENABLED (it
  // compiles to ~74 words unpacked, exactly 64/64 packed), so its compiled
  // layout is not assumed fixed the way theirs is -- see the identical
  // remarks on Ga144Node708HeadProtocol, whose exact source this reuses.
  private (int[] Program, Node708HeadAddresses Addresses) BuildHeadProgram()
  {
    const string source = """
        # 0 org
        entry main
        : main 18ibits drop >r ex main;
        : obit ( dwn-dw) !b over >r delay ;
        : readw ( -dw) dup 18ibits drop over over
        : oword ( dw-d)  leap drop  leap drop leap drop  drop ;
        : obyt ( dw-dwx)  then then then  3 obit drop
            7 for dup 1 and 3 xor obit  drop 2/ next
            2 obit ;
        : sett .loc readw drop a! ; // set A register, used to select tentacle
        : w/r .loc // writes & reads words from a node. at least 1 word must be written, at least 1 word must be read
          readw drop >r // # of words to read -1
          readw drop dup >r // # of words to write -1
          // write pre
          ( w1)
          readw drop -if else >r over begin ( d w1)
            A[ @p >r ]] !
            r> dup >r // get current node
            dup dup . + . + 2* over . + ! // multiply by 6 + #write-1
            A[ @p !b unext ]] !
          next then
          //
          begin readw drop ! next
          // write post
          ( d)
          readw drop -if drop else for ( d)
            A[ @p >r ]] !
            r> r> dup ! >r >r  // send # of read words -1
            A[ @b !p unext ]] !
          next then
          //
          ( d)
          begin @ oword next main ;
        .loc
        """;

    var compileService = new F18NodeCompilationService(_chip, _romLibrary, _romLibrary.SystemMacros);
    F18NodeCompilationResult nodeResult = compileService.CompileNode(KrakenTopology.HeadCoordinate);

    if (!nodeResult.Rom.Success)
    {
      string romDiagnostics = string.Join(Environment.NewLine, nodeResult.Rom.Diagnostics.Select(item => item.ToString()));
      throw new InvalidOperationException("Node 708's ROM source did not compile.\n" + romDiagnostics);
    }

    var options = new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = KrakenTopology.HeadCoordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      PredefinedConstants = nodeResult.Rom.Constants,
      PredefinedSymbols = nodeResult.Rom.Symbols,
      MacroLookupScope = F18MacroLookupScope.UserAndSystem,
      PackControlTransfers = true
    };

    F18CompileResult result = new F18Compiler().Compile(source, options);
    if (!result.Success)
    {
      string diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.ToString()));
      throw new InvalidOperationException("The node-708 Kraken head protocol program did not compile.\n" + diagnostics);
    }

    if (result.Words.Count > 64)
    {
      throw new InvalidOperationException($"The node-708 Kraken head protocol program requires {result.Words.Count} RAM words; only 64 are available.");
    }

    if (result.EntryPoint != 0)
    {
      int selectedEntry = result.EntryPoint ?? -1;
      throw new InvalidOperationException($"The node-708 Kraken head protocol program must enter at RAM 0, but the compiler selected 0x{selectedEntry:X3}.");
    }

    // 'setn' is gone from this source (see WriteRead708's remarks) -- 'n' now
    // travels inline inside every 'w/r' call instead of through its own
    // dispatch address. Node708HeadAddresses.SetNode is a shared record type
    // (see Ga144Node708HeadProtocol.cs, which still uses it independently);
    // left at 0 here since nothing in this class dispatches to it anymore.
    var addresses = new Node708HeadAddresses(
        SetTentacle: RequireSymbol(result, "sett"),
        SetNode: 0,
        WriteRead: RequireSymbol(result, "w/r"));

    int[] words = result.Words.Select(item => item & F18InstructionSet.WordMask).ToArray();
    return (words, addresses);
  }

  private static int RequireSymbol(F18CompileResult result, string name)
  {
    if (!result.Symbols.TryGetValue(name, out F18ExportedSymbol? symbol) || symbol is null)
    {
      throw new InvalidOperationException($"The node-708 Kraken head protocol program did not define '{name}'.");
    }

    return symbol.Value;
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
      throw new TimeoutException($"Kraken reply timed out after receiving {offset} of {count} bytes.");
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

    // Opening the COM port on Port A pulses RESET- and destroys the resident
    // Kraken (the stock FTDI driver briefly asserts RTS during CreateFile, and
    // that glitch is a reset on the EVB). So a reopen cannot simply resume the
    // old session: the chip is blank again. Reopen the transport AND re-run the
    // full erection sequence, rebuilding the node-708 head program and all
    // tentacles before the pending transaction proceeds.
    //
    // A single CreateFile can transiently fail while the previous CloseHandle is
    // still tearing the FTDI endpoint down, or while the device wakes from
    // selective suspend. Retry with bounded back-off before allowing a fault.
    int backoff = ReopenInitialBackoffMilliseconds;
    IOException? lastError = null;

    for (int attempt = 1; attempt <= ReopenMaxAttempts; attempt++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        NativeWindowsSerialPort port = NativeWindowsSerialPort.Open(
            _portName!,
            OnlineBaudRate,
            readTimeoutMilliseconds: 50,
            writeTimeoutMilliseconds: 2_000);

        if (_reopenResetsChip)
        {
          // Port A: opening the port pulsed RESET- and wiped the Kraken, so
          // rebuild it on the freshly reset chip before the pending transaction.
          try
          {
            ErectOnto(port, cancellationToken);
          }
          catch
          {
            port.Dispose();
            throw;
          }
        }
        else
        {
          // Port C: no RTS-to-RESET- wiring, so the Kraken survived the reopen.
          // Just resume the transport: settle briefly and flush any stale RX so
          // the word-transport framing is clean for the next read.
          SettleUsb(OnlineTransactionSettleMilliseconds, cancellationToken);
          try
          {
            port.PurgeInput();
          }
          catch
          {
            // Best-effort; the widened first-read timeout still applies.
          }
        }

        _port = port;
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
        $"Could not reopen and re-erect the Kraken COM endpoint '{_portName}' after {ReopenMaxAttempts} attempts.",
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