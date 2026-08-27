using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Cvm;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>One boot frame sent while installing the CVM cluster -- which node it concerns, at what relay depth, and whether the host actually got the bytes onto the wire (this is fire-and-forget: "sent" is not "confirmed").</summary>
public sealed record CvmInstallStep(string Description, int NodeCoordinate, int Position, int WordCount);

/// <summary>Everything one CVM hardware install attempt produced.</summary>
public sealed record CvmInstallReport(bool Success, string? FailureMessage, IReadOnlyList<CvmInstallStep> Steps);

/// <summary>
/// One post-install runtime test step's outcome. <see cref="Passed"/> is null when the step could
/// not be evaluated (e.g. a timeout) rather than run and found wanting. <see cref="SentWords"/> and
/// <see cref="ReceivedWords"/> record every word actually put on the wire and actually decoded for
/// this step, in order, EVEN when the step ends in a timeout partway through -- a timeout on word 2
/// no longer discards word 1's already-received value, so a failing run's summary always shows
/// exactly how far the exchange actually got, not just that it failed.
/// </summary>
public sealed record CvmTestStepResult(string Description, bool Attempted, bool? Passed, IReadOnlyList<int> SentWords, IReadOnlyList<int> ReceivedWords, string? Detail);

/// <summary>Everything one "Install &amp; run CVM test" attempt produced, install and runtime test together.</summary>
public sealed record CvmInstallAndTestReport(CvmInstallReport Install, IReadOnlyList<CvmTestStepResult> TestSteps);

/// <summary>
/// Delivers the CVM test cluster's 9 compiled node images across the physical mesh through node
/// 708 and, once installed, runs a first live functional check.
///
/// <b>Loading technique.</b> Generalizes <see cref="KrakenSession.ErectOnto"/>'s own
/// hardware-proven, fire-and-forget boot-frame erection (see the project's
/// node-300-erection-investigation notes: this is the technique that replaced an earlier
/// "dynamic", per-word-acknowledged relay construction which reliably failed on real hardware for
/// reasons never fully isolated -- this class deliberately does not repeat that mistake by
/// inventing something new) from Kraken's flat/linear tentacles to the CVM's branching tree
/// (708 -&gt; 707 -&gt; 607 -&gt; {507 -&gt; {407, 506, 508}, 606, 608}). Per DB013 6.1.2.4 ("Root Node
/// Programming"), a branch node (607, 507) is held in a temporary relay role -- focused onto its
/// incoming port, its B re-pointed once per child -- while each child loads in turn, and only
/// receives its OWN real program and entry jump as the last step addressed to it; leaves get their
/// real program directly once their ancestor chain is wired. The exact node-to-parent shape comes
/// from <see cref="CvmBootStreamBuilder.BuildLoadOrder"/>, not a hardcoded tree, so this loader
/// tracks any future change to that load order automatically.
///
/// <b>Node 708 itself is never "focused."</b> Unlike every other node in this cluster, 708 stays
/// parked in its own ROM boot-frame receiver (ser-exec) for the entire install -- it is the
/// physical serial entry point, not a puppet reached through a compass port. Only the very last
/// step writes 708's own compiled RAM image (transfer address 0x000, completion still ser-exec)
/// and then sends one final empty-payload frame whose completion address is 708's own real entry
/// point, exactly mirroring how <c>KrakenSession.ErectOnto</c> loads and then enters its head
/// program.
///
/// <b>Genuinely new, first-of-its-kind code in this project.</b> Every other hardware feature here
/// (Kraken's tentacles, the SRAM Tentacle/Simulator) has gone through multiple rounds of
/// real-hardware-discovered bugs even for simpler, linear topologies -- see
/// claude/sram-tentacle-implementation.md. This is the first attempt at a BRANCHING install, and
/// has not yet been exercised against real silicon. <see cref="CvmInstallReport"/> reports each
/// frame this class actually put on the wire so a failure can be localized to a specific node.
/// </summary>
public sealed class Ga144CvmHardwareInstaller
{
  private const int ResetAssertMilliseconds = 20;
  private const int ResetReleaseMilliseconds = 1;

  // Same rationale and value as KrakenSession's own constant: pad past DB002 3.3.2/3.3.3's
  // documented ~4.1 mS (262144 cycle) boot-node reasonableness-check bound before the very first
  // relay frame is sent, so every ordinary node has already reverted from its own boot check
  // (if any) to 'warm' and is listening on its ports.
  private const int BootNodeReasonablenessCheckSettleMilliseconds = 10;

  // Node 708's ROM async-boot concatenation address (DB002 3.3.3's "ser-exec") -- every frame
  // sent while more frames are still coming must target this, never 'cold' (0x0AA), which would
  // re-run the reasonableness/wake classifier instead of just accepting another frame.
  private const int AsyncSerialContinuationAddress = 0x0AE;

  private const int OnlineTransactionSettleMilliseconds = 5;
  private const int InterWordSettleMilliseconds = 20;
  private const int ResponseTimeoutMilliseconds = 1_000;

  // Same value as Ga144Serial.MaximumBaudRate / KrakenSession.OnlineBaudRate -- inlined directly
  // rather than depending on either class, so this installer only couples to the low-level
  // NativeWindowsSerialPort transport and the async word encode/decode primitives it actually
  // uses, nothing else.
  private const int BaudRate = 921_600;

  /// <summary>
  /// Compiles the given chip's 9 CVM node sources (via <paramref name="compileService"/>, so this
  /// tests whatever the current project actually contains -- not a frozen reference copy),
  /// resets the chip, loads all 9 nodes across the mesh, and runs the first live functional test:
  /// wake node 708's <c>'start</c> with one word, then read back what should be the CVM's first
  /// memory request (address 0, expected as two words both equal to 0).
  /// </summary>
  public Task<CvmInstallAndTestReport> InstallAndRunAsync(
      string portName,
      Ga144ChipConfiguration chip,
      F18NodeCompilationService compileService,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    ArgumentNullException.ThrowIfNull(chip);
    ArgumentNullException.ThrowIfNull(compileService);

    return Task.Run(() => InstallAndRun(portName, chip, compileService, cancellationToken), cancellationToken);
  }

  private static CvmInstallAndTestReport InstallAndRun(
      string portName,
      Ga144ChipConfiguration chip,
      F18NodeCompilationService compileService,
      CancellationToken cancellationToken)
  {
    // Compile every node from the PROJECT's own current sources first -- entirely offline, no
    // hardware touched yet -- and require every one of the 9 to succeed with a full 64-word RAM
    // image before any reset/relay happens. Fail closed: a half-compiled cluster must never reach
    // the chip.
    IReadOnlyList<CvmBootLoadStep> loadOrder = CvmBootStreamBuilder.BuildLoadOrder();
    var descriptors = new Dictionary<int, CvmBootDescriptor>();
    foreach (CvmBootLoadStep step in loadOrder)
    {
      Ga144NodeConfiguration node = chip.GetNode(step.NodeCoordinate);
      if (string.IsNullOrWhiteSpace(node.SourceCode))
      {
        return Failed($"Node {step.NodeCoordinate:000} has no source in this project. Use \"Copy to project…\" in the node editor for every CVM node before installing on hardware.");
      }

      F18NodeCompilationResult compiled = compileService.CompileNode(step.NodeCoordinate);
      if (!compiled.Success)
      {
        int errorCount = compiled.Rom.Diagnostics.Concat(compiled.Ram.Diagnostics)
            .Count(diagnostic => diagnostic.Severity == F18DiagnosticSeverity.Error);
        return Failed($"Node {step.NodeCoordinate:000} failed to compile ({errorCount} error(s)). Fix it in the node editor before installing on hardware.");
      }

      var descriptor = CvmBootDescriptor.FromCompileResult(compiled.Ram);
      if (descriptor.Words.Count != 64)
      {
        return Failed($"Node {step.NodeCoordinate:000} compiled to {descriptor.Words.Count} RAM words, not the required 64. Not installing.");
      }

      if (descriptor.EntryPoint is null)
      {
        return Failed($"Node {step.NodeCoordinate:000} has no entry point. Not installing.");
      }

      descriptors[step.NodeCoordinate] = descriptor;
    }

    // Parent-of map, derived from the load order itself (not hardcoded), so a future change to
    // the tree shape is picked up automatically. 707's own step names 708 as its "via" --
    // 708 is the physical root, excluded from every ancestor chain below.
    var parentOf = new Dictionary<int, int>();
    foreach (CvmBootLoadStep step in loadOrder)
    {
      if (step.ViaNodeCoordinate.HasValue)
      {
        parentOf[step.NodeCoordinate] = step.ViaNodeCoordinate.Value;
      }
    }

    CvmBootLoadStep rootStep = loadOrder.SingleOrDefault(step => step.ViaNodeCoordinate is null)
        ?? throw new InvalidOperationException("The CVM load order has no root (via = null) step.");
    int rootCoordinate = rootStep.NodeCoordinate; // 708

    List<int> AncestorChain(int node)
    {
      var chain = new List<int>();
      int current = parentOf[node];
      while (current != rootCoordinate)
      {
        chain.Insert(0, current);
        current = parentOf[current];
      }

      return chain;
    }

    using NativeWindowsSerialPort port = NativeWindowsSerialPort.Open(
        portName,
        BaudRate,
        readTimeoutMilliseconds: 50,
        writeTimeoutMilliseconds: 2_000);

    try
    {
      cancellationToken.ThrowIfCancellationRequested();
      port.SetDtr(true);
      port.SetRts(false);
      Thread.Sleep(ResetAssertMilliseconds);
      port.PurgeInputOutput();
      port.SetRts(true);
      Thread.Sleep(ResetReleaseMilliseconds);
      Thread.Sleep(BootNodeReasonablenessCheckSettleMilliseconds);

      int transferAddress = KrakenTopology.PortAddress(rootCoordinate, FirstHopFrom(rootStep, loadOrder));

      var steps = new List<CvmInstallStep>();
      var focused = new HashSet<int>();

      foreach (CvmBootLoadStep step in loadOrder)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (step.NodeCoordinate == rootCoordinate)
        {
          continue; // 708 itself is handled after the loop -- it is never a relay-focused puppet.
        }

        List<int> chain = AncestorChain(step.NodeCoordinate);
        for (int index = 0; index < chain.Count; index++)
        {
          int ancestor = chain[index];
          int position = index;

          if (focused.Add(ancestor))
          {
            int incomingPort = KrakenTopology.PortAddress(ancestor, parentOf[ancestor]);
            IReadOnlyList<int> focusLeaf = CvmRelayProtocol.WrapForward(position, [CvmRelayProtocol.BuildBareJump(incomingPort)]);
            SendBootFrame(port, transferAddress, focusLeaf);
            steps.Add(new CvmInstallStep($"Focus node {ancestor:000} onto its port facing {parentOf[ancestor]:000}", ancestor, position, focusLeaf.Count));
          }

          int nextTarget = index + 1 < chain.Count ? chain[index + 1] : step.NodeCoordinate;
          int outgoingPort = KrakenTopology.PortAddress(ancestor, nextTarget);
          IReadOnlyList<int> writeBLeaf = CvmRelayProtocol.WrapForward(position, CvmRelayProtocol.BuildWriteBNoReply(outgoingPort));
          SendBootFrame(port, transferAddress, writeBLeaf);
          steps.Add(new CvmInstallStep($"Point node {ancestor:000}'s B at node {nextTarget:000}", ancestor, position, writeBLeaf.Count));
        }

        CvmBootDescriptor descriptor = descriptors[step.NodeCoordinate];
        IReadOnlyList<int> programLeaf = BuildProgramLeaf(descriptor);
        IReadOnlyList<int> wrapped = CvmRelayProtocol.WrapForward(chain.Count, programLeaf);
        SendBootFrame(port, transferAddress, wrapped);
        steps.Add(new CvmInstallStep($"Load node {step.NodeCoordinate:000}'s own program (entry 0x{descriptor.EntryPoint:X3})", step.NodeCoordinate, chain.Count, wrapped.Count));
      }

      // Node 708 itself, last: write its own compiled RAM directly (transfer address 0x000 is a
      // RAM address, not a port, so ROM writes it locally instead of relaying it), completion
      // still pointed at ser-exec since this is not yet the final frame.
      CvmBootDescriptor rootDescriptor = descriptors[rootCoordinate];
      SendBootFrame(port, AsyncSerialContinuationAddress, 0x000, rootDescriptor.Words);
      steps.Add(new CvmInstallStep("Write node 708's own RAM image", rootCoordinate, 0, rootDescriptor.Words.Count));

      // Final empty frame: completion = 708's real entry point, already resident from the frame
      // just above -- this is what actually starts the CVM running.
      SendBootFrame(port, rootDescriptor.EntryPoint!.Value, 0x000, []);
      steps.Add(new CvmInstallStep($"Enter node 708's real program (0x{rootDescriptor.EntryPoint:X3})", rootCoordinate, 0, 0));

      SettleUsb(OnlineTransactionSettleMilliseconds, cancellationToken);
      port.PurgeInput();

      var install = new CvmInstallReport(true, null, steps);
      List<CvmTestStepResult> testSteps = RunTests(port, cancellationToken);
      return new CvmInstallAndTestReport(install, testSteps);
    }
    finally
    {
      try { port.SetRts(true); } catch { }
      try { port.SetDtr(true); } catch { }
    }
  }

  // The tree has exactly one node whose via is the root (708) -- 707. Reusing AncestorChain's own
  // parentOf-driven approach here would be circular before it exists, so this one lookup is done
  // directly against the load order instead.
  private static int FirstHopFrom(CvmBootLoadStep rootStep, IReadOnlyList<CvmBootLoadStep> loadOrder)
  {
    CvmBootLoadStep? firstHop = loadOrder.FirstOrDefault(step => step.ViaNodeCoordinate == rootStep.NodeCoordinate);
    return firstHop?.NodeCoordinate
        ?? throw new InvalidOperationException($"No node in the load order is reached directly via node {rootStep.NodeCoordinate:000}.");
  }

  // WriteRam (no reply) + whatever register/stack initialization this node's own source directives
  // specified + a bare (no-reply) jump into its real entry point. Order follows DB013 6.1.2.3's
  // own listed sequence (IO, A, B, then stacks, then P last).
  private static IReadOnlyList<int> BuildProgramLeaf(CvmBootDescriptor descriptor)
  {
    var leaf = new List<int>(CvmRelayProtocol.BuildWriteRamNoReply(descriptor.Words));

    if (descriptor.InitialIo.HasValue)
    {
      leaf.AddRange(CvmRelayProtocol.BuildSetIo(descriptor.InitialIo.Value));
    }

    if (descriptor.InitialA.HasValue)
    {
      leaf.AddRange(CvmRelayProtocol.BuildSetA(descriptor.InitialA.Value));
    }

    if (descriptor.InitialB.HasValue)
    {
      leaf.AddRange(CvmRelayProtocol.BuildSetB(descriptor.InitialB.Value));
    }

    foreach (int value in descriptor.InitialStack)
    {
      leaf.AddRange(CvmRelayProtocol.BuildPushS(value));
    }

    leaf.Add(CvmRelayProtocol.BuildBareJump(descriptor.EntryPoint!.Value));
    return leaf;
  }

  // ---- post-install runtime test -------------------------------------------------------------
  // Structured as an ordered list of independent steps so more can be appended later (per
  // Stefan's own stated intent: "from there we can go on adding more instructions to the test")
  // without reworking anything above. Only one step exists so far.
  private static List<CvmTestStepResult> RunTests(NativeWindowsSerialPort port, CancellationToken cancellationToken)
  {
    var results = new List<CvmTestStepResult>();

    // Step 1: node 708's own 'start (its compiled entry, just jumped into above) begins with
    // "io b! 18ibits drop drop !bitdelay r-l-" -- 18ibits both RECEIVES this first word and
    // CALIBRATES 'bitdelay' from its own bit timing (see 'start's remarks in Node708Program).
    // That calibration -- not the word's payload value -- is what obit/oword/obyt's own transmit
    // delay loop uses for every reply this node ever sends afterward. An all-zero word (0x00000)
    // has no bit transitions of its own to calibrate against; 0x15555 (alternating bits) is the
    // one word this project's own hand-confirmed-on-real-hardware note (see
    // Ga144Node708EchoProbe.SpeedTestWord's remarks: "0x15555 -> 55 55 01 on real hardware,
    // confirmed separately by hand") is actually known to calibrate correctly, so it is used here
    // too rather than an untested all-zero wake. Per Stefan: once 'start has woken, the next
    // traffic on the wire should be the CVM's own first memory request -- node 607's very first
    // instruction fetch, address 0 -- relayed back up through 707 and out via 708, arriving as
    // two words that should both read 0.
    const int wakeValue = 0x15555;
    const string description = "Wake 'start with one word, then read the CVM's first memory request (expect two words, both 0)";
    const int expectedReplyWords = 2;

    var sentWords = new List<int>();
    var receivedWords = new List<int>();
    string? failureNote = null;

    try
    {
      byte[] wakeBytes = new byte[3];
      Ga144Node708Probe.EncodeAsynchronousWord(wakeValue, wakeBytes);
      port.Write(wakeBytes);
      sentWords.Add(wakeValue);
      WaitForTransmitDrain(port, wakeBytes.Length);
      Thread.Sleep(InterWordSettleMilliseconds);

      // Each ReadWord is its own timeout window. A timeout on word 2 still leaves word 1 (if it
      // was already decoded) sitting in receivedWords below -- the catch blocks only set
      // failureNote, they never discard progress the try block already made.
      for (int index = 0; index < expectedReplyWords; index++)
      {
        receivedWords.Add(ReadWord(port, ResponseTimeoutMilliseconds, cancellationToken));
      }
    }
    catch (TimeoutException exception)
    {
      failureNote = $"Timed out waiting for a reply: {exception.Message}";
    }
    catch (IOException exception)
    {
      failureNote = $"Serial I/O failed: {exception.Message}";
    }

    bool? passed = failureNote is not null
        ? null
        : receivedWords.Count == expectedReplyWords && receivedWords.All(word => word == 0);

    string transcript = $"Sent [{FormatWords(sentWords)}]. Received [{FormatWords(receivedWords)}] "
        + $"({receivedWords.Count} of {expectedReplyWords} expected word(s)).";
    string detail = failureNote is null
        ? transcript + (passed == true
            ? " Matches expectation -- node 607's first instruction fetch (address 0)."
            : " Expected both received words to be 0.")
        : transcript + " " + failureNote;

    results.Add(new CvmTestStepResult(
        description,
        Attempted: true,
        Passed: passed,
        SentWords: sentWords,
        ReceivedWords: receivedWords,
        Detail: detail));

    return results;
  }

  private static CvmInstallAndTestReport Failed(string message) =>
      new(new CvmInstallReport(false, message, []), []);

  // "none" rather than an empty "[]" -- an empty pair of brackets in the middle of a longer
  // transcript line reads as a rendering glitch; a word, this makes the zero case unambiguous.
  private static string FormatWords(IReadOnlyList<int> words) =>
      words.Count == 0 ? "none" : string.Join(", ", words.Select(word => $"0x{word:X5}"));

  private static void SendBootFrame(NativeWindowsSerialPort port, int transferAddress, IReadOnlyList<int> payload) =>
      SendBootFrame(port, AsyncSerialContinuationAddress, transferAddress, payload);

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
      Ga144Node708Probe.EncodeAsynchronousWord(words[index], bytes.AsSpan(index * 3, 3));
    }

    return bytes;
  }

  private static int ReadWord(NativeWindowsSerialPort port, int timeoutMilliseconds, CancellationToken cancellationToken)
  {
    byte[] bytes = ReadExactly(port, 3, timeoutMilliseconds, cancellationToken);
    int value = bytes[0] | (bytes[1] << 8) | ((bytes[2] & 0x03) << 16);
    return value & F18InstructionSet.WordMask;
  }

  private static byte[] ReadExactly(NativeWindowsSerialPort port, int count, int timeoutMilliseconds, CancellationToken cancellationToken)
  {
    var result = new byte[count];
    int offset = 0;
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
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
      throw new TimeoutException($"Timed out after receiving {offset} of {count} bytes.");
    }

    return result;
  }

  private static void WaitForTransmitDrain(NativeWindowsSerialPort port, int byteCount)
  {
    double milliseconds = (byteCount * 10.0 * 1000.0 / port.BaudRate) + 3.0;
    Thread.Sleep((int)Math.Ceiling(milliseconds));
  }

  private static void SettleUsb(int milliseconds, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    Thread.Sleep(milliseconds);
  }
}