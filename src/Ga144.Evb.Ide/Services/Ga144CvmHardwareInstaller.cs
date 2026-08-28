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

    // Every node's own compiled RAM result (symbol table included) is kept here too, alongside
    // the boot descriptors above -- not just used to derive a memory image. The runtime test
    // below reads addresses out of THIS live compile of the project's actual current sources,
    // never a frozen/reference copy, so a source edit (label renamed, address shifted by a rebuild
    // of the underlying ga144-rom.yaml, etc.) is picked up automatically the next time this method
    // runs.
    var compiledRam = new Dictionary<int, F18CompileResult>();
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
      compiledRam[step.NodeCoordinate] = compiled.Ram;
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
      List<CvmTestStepResult> testSteps = RunTests(port, cancellationToken, compiledRam);
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
  // Earlier versions of this test hand-crafted one canned reply per expected request (wake, then
  // five 'nop echoes, then one 'plit-plus-literal exchange). That stopped scaling the moment a
  // WRITE command showed up unannounced in the middle of the 'plit exchange -- node 607 doesn't
  // just fetch its program one word at a time, it also reads and writes the same external memory
  // for other purposes (a stack push, in that instance), and there was no canned step for that.
  //
  // Per Stefan, this now behaves like the real memory instead of scripting each individual
  // request: (1) build a small test program and load it into a simulated 1 Mword SRAM
  // (CvmSimulatedSram) up front; (2) start the CVM with one wake word; (3) service whatever mix of
  // reads and writes actually comes back over the wire, exactly as real SRAM behind node 708
  // would, recording every transaction. See RunSramBackedProgramStep for the decode convention
  // (bit 17 / inversion) and how each transaction is checked.
  private static List<CvmTestStepResult> RunTests(
      NativeWindowsSerialPort port,
      CancellationToken cancellationToken,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    var results = new List<CvmTestStepResult>();

    if (!compiledRam.TryGetValue(NopSourceNodeCoordinate, out F18CompileResult? mainCompile))
    {
      results.Add(MissingSymbolStep($"node {NopSourceNodeCoordinate:000}'s compiled program is not available"));
      return results;
    }

    if (!mainCompile.Symbols.TryGetValue(NopSymbolName, out F18ExportedSymbol? nopSymbol))
    {
      results.Add(MissingSymbolStep($"\"{NopSymbolName}\""));
      return results;
    }

    if (!mainCompile.Symbols.TryGetValue(PlitSymbolName, out F18ExportedSymbol? plitSymbol))
    {
      results.Add(MissingSymbolStep($"\"{PlitSymbolName}\""));
      return results;
    }

    if (!mainCompile.Symbols.TryGetValue(PopSymbolName, out F18ExportedSymbol? popSymbol))
    {
      results.Add(MissingSymbolStep($"\"{PopSymbolName}\""));
      return results;
    }

    if (!mainCompile.Symbols.TryGetValue(PushSymbolName, out F18ExportedSymbol? pushSymbol))
    {
      results.Add(MissingSymbolStep($"\"{PushSymbolName}\""));
      return results;
    }

    // Resolved once, live, from this run's own compile -- never hardcoded, never a frozen
    // reference copy -- since every symbol's address can move as Stefan's sources evolve (as
    // 'nop's own address already has, more than once, this session).
    int nopOpcode = 0x8000 | (nopSymbol.Value & F18InstructionSet.WordMask);
    int plitOpcode = 0x8000 | (plitSymbol.Value & F18InstructionSet.WordMask);
    int popOpcode = 0x8000 | (popSymbol.Value & F18InstructionSet.WordMask);
    int pushOpcode = 0x8000 | (pushSymbol.Value & F18InstructionSet.WordMask);

    results.Add(RunSramBackedProgramStep(port, cancellationToken, nopOpcode, plitOpcode, popOpcode, pushOpcode));
    return results;
  }

  private static CvmTestStepResult MissingSymbolStep(string what) =>
      new($"Load a test program built from node {NopSourceNodeCoordinate:000}'s own compiled words",
          Attempted: false,
          Passed: null,
          SentWords: [],
          ReceivedWords: [],
          Detail: $"Could not build the test program: {what} was not found. Check node {NopSourceNodeCoordinate:000}'s source still defines " +
              $"\"{NopSymbolName}\", \"{PlitSymbolName}\", \"{PopSymbolName}\", and \"{PushSymbolName}\" before re-running the test.");

  // Node 607's own opcode convention, confirmed by Stefan against this project's real
  // Node607Program.cs remarks (e.g. 'plit at word 0x00E -> opcode 0x800E): opcode = 0x8000 |
  // wordAddress. 'nop, 'plit, 'pop, and 'push all live in this same node 607 source.
  private const int NopSourceNodeCoordinate = 607;
  private const string NopSymbolName = "'nop";
  private const string PlitSymbolName = "'plit";
  private const int PlitLiteralValue = 0x1234;
  private const string PopSymbolName = "'pop";
  private const string PushSymbolName = "'push";

  private const int WakeValue = 0x15555;

  // How many leading 'nop opcodes the test program starts with before 'plit.
  private const int LeadingNopCount = 5;

  // Padding appended after the interesting part of the test program (LeadingNopCount 'nop, then
  // 'plit + its literal, then 'pop and 'push) so the interpreter has more of its own,
  // already-understood 'nop opcode to fetch for a while afterward, rather than running into
  // zero-initialized simulated SRAM the moment the deliberately loaded words run out -- 0x00000
  // does not have bit 0x8000 set, so 'nop's own decode logic would treat it as something other
  // than a plain call and this test has no hypothesis yet for what that would do.
  private const int TrailingNopCount = 8;

  // A READ command is two words: [page, address-in-page]. This was established across 6+ real
  // hardware round trips as node 607's own fetch loop advanced through a run of 'nop opcodes (each
  // one a plain read: page 0, address-in-page = the fetch offset, incrementing by exactly one per
  // fetch) -- see this project's own nop-echo history for how that [address-word-is-second]
  // ordering was confirmed (a real bug in node 708's old obyt/oword transmit code briefly made it
  // look reversed; fixing that bug restored the expected order). The host answers a read with one
  // reply word: the simulated SRAM's contents at the combined address.
  //
  // A WRITE command is THREE words: [page, address-in-page, value] -- per Stefan: "A write must
  // read 3 words: first is page, then address inside page and then the value. no value is
  // written [back]." The first real write this test captured (page word 0x3FFFE, i.e. bit 17 set)
  // was originally misread as only two words, which desynchronized the rest of the exchange and
  // produced a spurious extra reply the CVM was never waiting for -- see the transcript that
  // prompted this fix. The host performs the write against the simulated SRAM and sends NO reply
  // at all (unlike a read).
  //
  // Per Stefan, bit 17 (0x20000) set on the page word marks a WRITE, and BOTH address words --
  // page and address-in-page -- are then bitwise-complemented over all 18 bits to recover their
  // real values; only the value word stays plain. This matches AN003's own SRAM Control Cluster,
  // which likewise inverts its whole two-word address for a write and leaves it positive for a
  // read. Confirmed against two real write samples: page word 0x3FFFE always inverts to page
  // 0x00001 (a stack area separate from the program's own page 0); address-in-page 0x30001
  // inverted to 0x0FFFE (== 0xFFFE) exactly matches a stack pointer initialized at 0xFFFF and
  // decremented once by the first push -- 'plit's own literal (0x01234) as the plain value word
  // both times. Page and address-in-page are combined the same way AN003 combines its 4-bit page
  // with a 16-bit in-page address (4 + 16 = 20 bits, exactly this class's declared 1 Mword/2^20
  // capacity) -- see CombineAddress below.
  private const int SramWriteFlagBit = 0x20000; // bit 17.

  private static int CombineAddress(int page, int addressInPage) =>
      ((page << 16) | (addressInPage & 0xFFFF)) & (CvmSimulatedSram.WordCapacity - 1);

  // Compact "p:aaaa" rendering of a page/address-in-page pair for the transaction log -- page is
  // the 4-bit page number (a single hex digit, 0-F) and aaaa is the 16-bit address-in-page (always
  // 4 hex digits), matching how the two words actually split up inside CombineAddress above.
  private static string FormatPageAddress(int page, int addressInPage) =>
      $"{page:X}:{addressInPage:X4}";

  // Hard stop on how many read/write transactions this step will service, so a real hardware
  // condition that makes the CVM chatter indefinitely cannot hang this test forever. Comfortably
  // above the transactions the test program below is expected to produce (one page-0 read per
  // loaded word, plus whatever mix of page-1 stack reads/writes 'plit/'pop/'push generate --
  // already observed to include at least one stack write from 'plit alone).
  private const int SramTransactionCap = 96;

  private static CvmTestStepResult RunSramBackedProgramStep(
      NativeWindowsSerialPort port,
      CancellationToken cancellationToken,
      int nopOpcode,
      int plitOpcode,
      int popOpcode,
      int pushOpcode)
  {
    // Stefan's step 1: build and load the test program into a simulated SRAM, starting at
    // address 0, before the CVM is started.
    var program = new List<int>();
    program.AddRange(Enumerable.Repeat(nopOpcode, LeadingNopCount));
    program.Add(plitOpcode);
    program.Add(PlitLiteralValue);
    program.Add(popOpcode);
    program.Add(pushOpcode);
    program.AddRange(Enumerable.Repeat(nopOpcode, TrailingNopCount));

    var sram = new CvmSimulatedSram();
    sram.LoadProgram(program);

    string description = $"Load a {program.Count}-word test program ({LeadingNopCount} 'nop, 'plit, literal 0x{PlitLiteralValue:X5}, 'pop, 'push, {TrailingNopCount} trailing 'nop) " +
        $"into a simulated {CvmSimulatedSram.WordCapacity:N0}-word SRAM, wake 'start, then service every read/write command as that SRAM would";

    var sentWords = new List<int>();
    var receivedWords = new List<int>();
    var transactionLog = new List<string>();
    string? failureNote = null;
    int expectedNextReadAddress = 0;
    bool allReadsMatchedExpectedAddress = true;

    try
    {
      // Stefan's step 2: start the CVM by writing a word.
      byte[] wakeBytes = new byte[3];
      Ga144Node708Probe.EncodeAsynchronousWord(WakeValue, wakeBytes);
      port.Write(wakeBytes);
      sentWords.Add(WakeValue);
      WaitForTransmitDrain(port, wakeBytes.Length);
      Thread.Sleep(InterWordSettleMilliseconds);

      // Stefan's step 3: act like a SRAM and record all read & write commands.
      for (int transactionCount = 0; transactionCount < SramTransactionCap; transactionCount++)
      {
        int pageWord = ReadWord(port, ResponseTimeoutMilliseconds, cancellationToken);
        receivedWords.Add(pageWord);

        bool isWrite = (pageWord & SramWriteFlagBit) != 0;

        if (isWrite)
        {
          // Three words in, no reply out. Both address words -- page and address-in-page -- are
          // inverted for a write; only the value word is plain.
          int page = (~pageWord) & F18InstructionSet.WordMask;
          int rawAddressInPage = ReadWord(port, ResponseTimeoutMilliseconds, cancellationToken);
          int value = ReadWord(port, ResponseTimeoutMilliseconds, cancellationToken);
          receivedWords.Add(rawAddressInPage);
          receivedWords.Add(value);

          int addressInPage = (~rawAddressInPage) & F18InstructionSet.WordMask;
          int address = CombineAddress(page, addressInPage);
          sram.Write(address, value);
          transactionLog.Add($"[WRITE] {FormatPageAddress(page, addressInPage)} <- 0x{value:X5}  " +
              $"(raw [{FormatWords([pageWord, rawAddressInPage, value])}])");
        }
        else
        {
          // Two words in, one reply word out.
          int page = pageWord & F18InstructionSet.WordMask;
          int addressInPage = ReadWord(port, ResponseTimeoutMilliseconds, cancellationToken);
          receivedWords.Add(addressInPage);

          int address = CombineAddress(page, addressInPage);
          int replyValue = sram.Read(address);
          byte[] replyBytes = new byte[3];
          Ga144Node708Probe.EncodeAsynchronousWord(replyValue, replyBytes);
          port.Write(replyBytes);
          sentWords.Add(replyValue);
          WaitForTransmitDrain(port, replyBytes.Length);
          Thread.Sleep(InterWordSettleMilliseconds);

          // Only page 0 (the program itself) is expected to be fetched in strict, sequential
          // order -- that is node 607's own instruction-fetch loop walking forward one word at a
          // time. Page 1 (and any other non-zero page) is the stack area 'plit/'pop/'push read
          // and write against, which has its own addressing (e.g. a decrementing stack pointer)
          // with no relationship to program order, so a page-1 read is recorded for review only,
          // exactly like a write already is -- neither judged against expectedNextReadAddress nor
          // eligible to trigger the "program fully read back" stop condition below.
          if (page == 0)
          {
            bool matchesExpectedAddress = address == expectedNextReadAddress;
            allReadsMatchedExpectedAddress &= matchesExpectedAddress;
            transactionLog.Add($"[READ ] {FormatPageAddress(page, addressInPage)} -> 0x{replyValue:X5}" +
                (matchesExpectedAddress ? string.Empty : $"  (expected flat address 0x{expectedNextReadAddress:X6})") +
                $"  (raw [{FormatWords([pageWord, addressInPage])}])");
            expectedNextReadAddress = address + 1;

            // Every deliberately loaded word has now been read back at least once -- stop here
            // rather than run past the trailing padding into undefined, zero-initialized territory.
            if (address >= program.Count - 1)
            {
              break;
            }
          }
          else
          {
            transactionLog.Add($"[READ ] {FormatPageAddress(page, addressInPage)} -> 0x{replyValue:X5}" +
                $"  (raw [{FormatWords([pageWord, addressInPage])}])");
          }
        }
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

    bool? passed = failureNote is not null ? null : allReadsMatchedExpectedAddress;

    var detail = new System.Text.StringBuilder();
    detail.Append($"Sent [{FormatWords(sentWords)}]. Received [{FormatWords(receivedWords)}] ({transactionLog.Count} transaction(s)).");
    foreach (string transaction in transactionLog)
    {
      detail.Append('\n').Append("    ").Append(transaction);
    }

    detail.Append('\n');
    if (failureNote is not null)
    {
      detail.Append(failureNote);
    }
    else if (passed == true)
    {
      detail.Append("All read commands landed on the expected address in program order. Any write(s) above are recorded for review only " +
          "-- there is no fixed expectation yet for their target address or value.");
    }
    else
    {
      detail.Append("At least one read command's address did not match the expected program order -- see the marked line(s) above.");
    }

    return new CvmTestStepResult(
        description,
        Attempted: true,
        Passed: passed,
        SentWords: sentWords,
        ReceivedWords: receivedWords,
        Detail: detail.ToString());
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