using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Cvm;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>One boot frame sent while installing the CVM cluster -- which node it concerns, at what relay depth, and whether the host actually got the bytes onto the wire (this is fire-and-forget: "sent" is not "confirmed").</summary>
public sealed record CvmInstallStep(string Description, int NodeCoordinate, int Position, int WordCount);

/// <summary>Everything one CVM hardware install attempt produced.</summary>
public sealed record CvmInstallReport(bool Success, string? FailureMessage, IReadOnlyList<CvmInstallStep> Steps);

// CvmTestStepResult and CvmInstallAndTestReport (the "Install & run CVM test" button's own runtime
// test result records) were REMOVED, 2026-09-18, along with that button, its ChipViewModel command,
// and InstallAndRunAsync/InstallAndRun/RunTests/MissingSymbolStep/RunSramBackedProgramStep below --
// see those removal notes for why. CvmInstallReport/CvmInstallStep just above are unaffected: they're
// still produced by OpenAndBootMesh, shared with StartDebugSession (the CVM Debugger window's own
// install path).

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

  // Same value as Ga144Serial.MaximumBaudRate / KrakenSession.OnlineBaudRate -- inlined directly
  // rather than depending on either class, so this installer only couples to the low-level
  // NativeWindowsSerialPort transport and the async word encode/decode primitives it actually
  // uses, nothing else.
  private const int BaudRate = 921_600;

  // InstallAndRunAsync/InstallAndRun (the "Install & run CVM test" button's own dry-run-to-hardware
  // path) were REMOVED, 2026-09-18, along with that button itself and its ChipViewModel command --
  // per Stefan, the GA144 window's "Compile CVM test"/"Install & run CVM test" buttons were retired
  // in favor of the interactive CVM Debugger window, which already exercises the exact same
  // OpenAndBootMesh/StartDebugSession path below (see that method's own remarks) without needing a
  // separate scripted-test entry point. RunTests/MissingSymbolStep/RunSramBackedProgramStep/
  // SramTransactionCap (the automatic post-install runtime check InstallAndRun used) and the
  // CvmInstallAndTestReport/CvmTestStepResult records were removed alongside it, since nothing else
  // called them -- CvmMemoryProtocol.TryBuildTestProgram, which RunTests called, is unaffected: it
  // is still used as StartDebugSession's own fallback (see that method's own remarks).

  /// <summary>
  /// Starts an interactive <see cref="CvmDebugSession"/> against real hardware: compiles and boots
  /// the mesh via <see cref="OpenAndBootMesh"/> below (the same install path the now-removed
  /// "Install &amp; run CVM test" button used to share with this method -- see that removal note
  /// above), then builds and loads the debugger's own default test program
  /// (<see cref="CvmMemoryProtocol.TryBuildDebuggerTestProgram"/>, assembling
  /// <see cref="CvmDebuggerDefaultProgram.Source"/>) into a fresh <see cref="CvmSimulatedSram"/> and
  /// wakes node 708's <c>'start</c> -- but stops there instead of automatically servicing the
  /// resulting read/write traffic to completion, leaving the port open and handing back a session
  /// the CVM Debugger window drives one transaction (or one breakpoint run) at a time.
  ///
  /// <b>CVM2 (2026-09-01): the default program can currently fail to build, and that's tolerated.</b>
  /// <see cref="CvmDebuggerDefaultProgram.Source"/> is still CVM1-era content exercising opcodes (ALU
  /// ops like <c>inv</c> among them) that CVM2's mesh has no node for any more -- every one of CVM1's
  /// old node 507 ALU mnemonics is now permanently orphaned (see
  /// <see cref="Services.CvmAssemblyLanguage"/>'s own remarks), so assembling it throws. Rather than
  /// let that abort Start Debug Session entirely, this method falls back to a minimal
  /// <c>nop</c>/<c>plit</c>/<c>pop</c>/<c>push</c> program (<see cref="CvmMemoryProtocol.TryBuildTestProgram"/>)
  /// and only throws if THAT also fails. <see cref="CvmDebuggerDefaultProgram"/> itself stays
  /// untouched, per Stefan's own standing instruction.
  /// </summary>
  public Task<CvmDebugSession> StartDebugSessionAsync(
      string portName,
      Ga144ChipConfiguration chip,
      F18NodeCompilationService compileService,
      CancellationToken cancellationToken = default,
      bool fillStacksWithDebugPoison = true)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    ArgumentNullException.ThrowIfNull(chip);
    ArgumentNullException.ThrowIfNull(compileService);

    return Task.Run(
        () => StartDebugSession(portName, chip, compileService, cancellationToken, fillStacksWithDebugPoison),
        cancellationToken);
  }

  private static CvmDebugSession StartDebugSession(
      string portName,
      Ga144ChipConfiguration chip,
      F18NodeCompilationService compileService,
      CancellationToken cancellationToken,
      bool fillStacksWithDebugPoison)
  {
    NativeWindowsSerialPort? port = OpenAndBootMesh(
        portName, chip, compileService, cancellationToken, fillStacksWithDebugPoison, out CvmInstallReport install, out var compiledRam);
    if (port is null)
    {
      throw new InvalidOperationException(install.FailureMessage ?? "CVM install failed; the debug session cannot start.");
    }

    try
    {
      (List<int>? program, string? error) = CvmMemoryProtocol.TryBuildDebuggerTestProgram(compiledRam);
      if (program is null)
      {
        // CVM2 (2026-09-01): CvmDebuggerDefaultProgram.Source is still CVM1-era content exercising
        // many opcodes (including ALU ops like 'inv) that have no defined node under CVM2's mesh at
        // all -- every one of CVM1's old node 507 ALU-op mnemonics is now permanently orphaned, see
        // CvmAssemblyLanguage's own remarks. Rather than block Start Debug Session entirely on a
        // default program CVM2 cannot currently satisfy, fall back to a minimal
        // nop/plit/pop/push smoke-test program (CvmMemoryProtocol.TryBuildTestProgram).
        // CvmDebuggerDefaultProgram.cs itself is left untouched per Stefan's own standing
        // instruction ("just keep it for now") -- this only changes what StartDebugSession does when
        // that program fails to build, not the program's own content.
        (program, error) = CvmMemoryProtocol.TryBuildTestProgram(compiledRam);
        if (program is null)
        {
          throw new InvalidOperationException($"Could not build a debugger test program: {error}");
        }
      }

      var sram = new CvmSimulatedSram();
      sram.LoadProgram(program);

      byte[] wakeBytes = new byte[3];
      Ga144Node708Probe.EncodeAsynchronousWord(CvmMemoryProtocol.WakeValue, wakeBytes);
      port.Write(wakeBytes);
      CvmMemoryProtocol.WaitForTransmitDrain(port, wakeBytes.Length);
      Thread.Sleep(CvmMemoryProtocol.InterWordSettleMilliseconds);

      return new CvmDebugSession(port, sram, program, install, compiledRam);
    }
    catch
    {
      try { port.SetRts(true); } catch { }
      try { port.SetDtr(true); } catch { }
      port.Dispose();
      throw;
    }
  }

  // What StartDebugSession (and, before its 2026-09-18 removal, InstallAndRun) needs: compile every
  // node in the current load order from the project's own current sources (fail closed before any
  // hardware is touched), reset the chip, and deliver every node across the mesh through node 708. On
  // success the returned port is left OPEN and reset-pin cleanup is the caller's responsibility --
  // StartDebugSession hands it to a long-lived CvmDebugSession. On a compile/validation failure
  // (before any hardware is touched) this returns null and no port was ever opened.
  //
  // CVM2 (2026-09-01), per Stefan: "take the nodes code in the project and not the NodeXxxProgram
  // code, which should only be used if no code in the project is defined." So a node's own project
  // source (chip.GetNode(coordinate).SourceCode) is ALWAYS tried first; CvmBootStreamBuilder's fixed
  // NodeXxxProgram.Source reference copy is used ONLY as a fallback for a coordinate whose project
  // source is still blank, via F18NodeCompilationService.CompileNode's own ramSourceOverride
  // parameter -- and every step description below that used the fallback says so explicitly, since
  // that source is about to be written onto real hardware and Stefan should be able to tell.
  private static NativeWindowsSerialPort? OpenAndBootMesh(
      string portName,
      Ga144ChipConfiguration chip,
      F18NodeCompilationService compileService,
      CancellationToken cancellationToken,
      bool fillStacksWithDebugPoison,
      out CvmInstallReport install,
      out IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    // Compile every node from the PROJECT's own current sources first -- entirely offline, no
    // hardware touched yet -- falling back to the fixed reference source only for a node with no
    // project source of its own (see this method's own remarks above) -- and require every node to
    // succeed with a full 64-word RAM image before any reset/relay happens. Fail closed: a
    // half-compiled cluster must never reach the chip.
    //
    // REWORKED 2026-09-22, per Stefan directly: "stop this static madness. i want full dynamic
    // bootstream and post-mortem analysis, based on all the nodes that have been configured by the
    // checkbox, thus belong to the current project. no more static lists." BuildLoadOrder now takes
    // this same chip and derives the roster live from each node's own Enabled/SourceCode ("configured")
    // state (see CvmBootStreamBuilder.GetConfiguredCoordinates' own remarks) instead of a fixed,
    // hand-maintained coordinate array -- so a node newly ticked or given source in the Node Editor is
    // picked up here automatically, with no further code change needed in this class.
    IReadOnlyList<CvmBootLoadStep> loadOrder = CvmBootStreamBuilder.BuildLoadOrder(chip);
    var descriptors = new Dictionary<int, CvmBootDescriptor>();

    // Every node's own compiled RAM result (symbol table included) is kept here too, alongside
    // the boot descriptors above -- not just used to derive a memory image. The runtime test
    // below reads addresses out of THIS live compile of the project's actual current sources,
    // never a frozen/reference copy, so a source edit (label renamed, address shifted by a rebuild
    // of the underlying ga144-rom.yaml, etc.) is picked up automatically the next time this method
    // runs.
    var compiledRamDictionary = new Dictionary<int, F18CompileResult>();

    // Per Stefan (2026-09-01): "take the nodes code in the project and not the NodeXxxProgram code,
    // which should only be used if no code in the project is defined." So every node still compiles
    // from chip.GetNode(coordinate).SourceCode by default (compileService.CompileNode's own normal
    // behavior, no override) -- this set only records which nodes had NO project source at all and
    // had to fall back to CvmBootStreamBuilder.ReferenceSourceFor's fixed reference copy instead, so
    // that fallback can be called out in this run's own step descriptions below rather than installed
    // onto real hardware silently.
    var referenceFallbackCoordinates = new HashSet<int>();
    foreach (CvmBootLoadStep step in loadOrder)
    {
      Ga144NodeConfiguration node = chip.GetNode(step.NodeCoordinate);
      string? ramSourceOverride = null;
      if (string.IsNullOrWhiteSpace(node.SourceCode))
      {
        string? referenceSource = CvmBootStreamBuilder.ReferenceSourceFor(step.NodeCoordinate);
        if (referenceSource is null)
        {
          install = FailedInstall($"Node {step.NodeCoordinate:000} has no source in this project. Use \"Copy to project…\" in the node editor for every CVM node before installing on hardware.");
          compiledRam = compiledRamDictionary;
          return null;
        }

        ramSourceOverride = referenceSource;
        referenceFallbackCoordinates.Add(step.NodeCoordinate);
      }

      F18NodeCompilationResult compiled = compileService.CompileNode(step.NodeCoordinate, ramSourceOverride: ramSourceOverride);
      if (!compiled.Success)
      {
        int errorCount = compiled.Rom.Diagnostics.Concat(compiled.Ram.Diagnostics)
            .Count(diagnostic => diagnostic.Severity == F18DiagnosticSeverity.Error);
        install = FailedInstall($"Node {step.NodeCoordinate:000} failed to compile ({errorCount} error(s)). Fix it in the node editor before installing on hardware.");
        compiledRam = compiledRamDictionary;
        return null;
      }

      var descriptor = CvmBootDescriptor.FromCompileResult(compiled.Ram);
      if (descriptor.Words.Count != 64)
      {
        install = FailedInstall($"Node {step.NodeCoordinate:000} compiled to {descriptor.Words.Count} RAM words, not the required 64. Not installing.");
        compiledRam = compiledRamDictionary;
        return null;
      }

      if (descriptor.EntryPoint is null)
      {
        install = FailedInstall($"Node {step.NodeCoordinate:000} has no entry point. Not installing.");
        compiledRam = compiledRamDictionary;
        return null;
      }

      descriptors[step.NodeCoordinate] = descriptor;
      compiledRamDictionary[step.NodeCoordinate] = compiled.Ram;
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

    NativeWindowsSerialPort port = NativeWindowsSerialPort.Open(
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
        IReadOnlyList<int> programLeaf = BuildProgramLeaf(descriptor, fillStacksWithDebugPoison);
        IReadOnlyList<int> wrapped = CvmRelayProtocol.WrapForward(chain.Count, programLeaf);
        SendBootFrame(port, transferAddress, wrapped);
        string loadDescription = $"Load node {step.NodeCoordinate:000}'s own program (entry 0x{descriptor.EntryPoint:X3})"
            + (referenceFallbackCoordinates.Contains(step.NodeCoordinate) ? " [reference source -- not yet defined in this project]" : "");
        steps.Add(new CvmInstallStep(loadDescription, step.NodeCoordinate, chain.Count, wrapped.Count));
      }

      // Node 708 itself, last: write its own compiled RAM directly (transfer address 0x000 is a
      // RAM address, not a port, so ROM writes it locally instead of relaying it), completion
      // still pointed at ser-exec since this is not yet the final frame.
      CvmBootDescriptor rootDescriptor = descriptors[rootCoordinate];
      SendBootFrame(port, AsyncSerialContinuationAddress, 0x000, rootDescriptor.Words);
      string rootDescription = "Write node 708's own RAM image"
          + (referenceFallbackCoordinates.Contains(rootCoordinate) ? " [reference source -- not yet defined in this project]" : "");
      steps.Add(new CvmInstallStep(rootDescription, rootCoordinate, 0, rootDescriptor.Words.Count));

      // Final empty frame: completion = 708's real entry point, already resident from the frame
      // just above -- this is what actually starts the CVM running.
      SendBootFrame(port, rootDescriptor.EntryPoint!.Value, 0x000, []);
      steps.Add(new CvmInstallStep($"Enter node 708's real program (0x{rootDescriptor.EntryPoint:X3})", rootCoordinate, 0, 0));

      SettleUsb(OnlineTransactionSettleMilliseconds, cancellationToken);
      port.PurgeInput();

      install = new CvmInstallReport(true, null, steps);
      compiledRam = compiledRamDictionary;
      return port;
    }
    catch
    {
      try { port.SetRts(true); } catch { }
      try { port.SetDtr(true); } catch { }
      port.Dispose();
      throw;
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

  // The two hardware stacks are NOT the same depth, per DB001 (F18A datasheet) 2.3.2 and DB013
  // (arrayForth 3 User's Manual) 5.1.3.2's own "UPD" command description:
  //
  //   - Return stack: R (1 directly-addressable register) + an 8-word circular buffer behind it =
  //     9 words total -- matches KrakenSession's own ReadReturnStackAsync remarks and this
  //     project's own F18CompileTimeInterpreter.ReturnStackCapacity.
  //   - Data/parameter stack: T AND S (TWO directly-addressable registers, not one) + the SAME
  //     8-word circular buffer behind THEM = 10 words total -- DB013 5.1.3.2 is explicit: "UPD
  //     retrieves all TEN words of the data stack ... indexed (T, S, and the eight elements in the
  //     F18 stack)". This project's own F18CompileTimeInterpreter.DataStackCapacity already uses
  //     10 for exactly this reason (see its own remarks) -- an earlier version of this file used a
  //     single shared depth of 9 for BOTH stacks, which under-filled the data stack's own real
  //     10th (deepest) word, leaving whatever was already there before this boot untouched. That
  //     surfaced on real hardware as a "10th" post-mortem row that looked lost/duplicated rather
  //     than freshly poisoned -- this project's own node-301 post-mortem investigation notes.
  //
  // Stefan's own numbering for the debug fill below: position 0 = top of stack (T or R), the
  // HIGHEST position number = the deepest/bottom cell for that stack (8 for the return stack, 9
  // for the data stack).
  private const int DebugPoisonReturnStackDepth = 9;
  private const int DebugPoisonDataStackDepth = 10;

  // 0x1555x sentinel base -- Stefan's own chosen pattern ("filled up with a 0x1555x pattern were x
  // is the stack position"), deliberately similar to the silicon's own 0x15555 idle/NOP fill so it
  // reads as "this is filler," while the low nibble still identifies exactly which stack position a
  // value came from once read back (0-8 for the return stack, 0-9 for the data stack).
  private const int DebugPoisonBaseValue = 0x15550;

  // WriteRam (no reply) + this node's own real register/stack initialization + a bare (no-reply)
  // jump into its real entry point. Order follows DB013 6.1.2.3's own listed sequence (IO, A, B,
  // then stacks -- return before parameter -- then P last).
  //
  // When fillStacksWithDebugPoison is set, an EXTRA debug-only step runs just before that real
  // stack initialization: both stacks are first fully overwritten with the 0x1555x sentinel
  // pattern (return stack, then parameter stack -- Stefan's own requested order), so that any
  // sentinel value still on a stack once the program is actually running identifies a slot the
  // program never touched. The node's own real InitialReturnStack/InitialStack values are pushed
  // afterward exactly as before, landing on top of (and, past 9 real values, overwriting) that
  // poison fill -- this step never changes what a node's own directives asked for, only what sat
  // underneath it beforehand.
  private static IReadOnlyList<int> BuildProgramLeaf(CvmBootDescriptor descriptor, bool fillStacksWithDebugPoison)
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

    if (fillStacksWithDebugPoison)
    {
      foreach (int value in DebugPoisonFillValues(DebugPoisonReturnStackDepth))
      {
        leaf.AddRange(CvmRelayProtocol.BuildPushR(value));
      }

      foreach (int value in DebugPoisonFillValues(DebugPoisonDataStackDepth))
      {
        leaf.AddRange(CvmRelayProtocol.BuildPushS(value));
      }
    }

    foreach (int value in descriptor.InitialReturnStack)
    {
      leaf.AddRange(CvmRelayProtocol.BuildPushR(value));
    }

    foreach (int value in descriptor.InitialStack)
    {
      leaf.AddRange(CvmRelayProtocol.BuildPushS(value));
    }

    leaf.Add(CvmRelayProtocol.BuildBareJump(descriptor.EntryPoint!.Value));
    return leaf;
  }

  // Values in PUSH order (first element pushed first), for a stack of the given real depth (9 for
  // the return stack, 10 for the data stack -- see DebugPoisonReturnStackDepth/
  // DebugPoisonDataStackDepth's own remarks). The LAST value pushed ends up on TOP, so to land
  // position 0 (0x15550) on top per Stefan's own numbering, this pushes the deepest position
  // (depth-1, e.g. 0x15559 for a 10-deep stack) first and the shallowest (0, value 0x15550) last.
  private static IReadOnlyList<int> DebugPoisonFillValues(int depth)
  {
    var values = new int[depth];
    for (int i = 0; i < depth; i++)
    {
      int position = depth - 1 - i;
      values[i] = DebugPoisonBaseValue + position;
    }

    return values;
  }

  // The "post-install runtime test" section that used to live here (RunTests, MissingSymbolStep,
  // SramTransactionCap, RunSramBackedProgramStep) was REMOVED, 2026-09-18, along with
  // InstallAndRunAsync/InstallAndRun above -- see that removal note for why. It was only ever
  // reached from InstallAndRun, so nothing else depends on it; CvmMemoryProtocol itself (the
  // wire-decode helpers RunSramBackedProgramStep called) is unaffected -- see the removal note above.

  private static CvmInstallReport FailedInstall(string message) => new(false, message, []);

  private static void SendBootFrame(NativeWindowsSerialPort port, int transferAddress, IReadOnlyList<int> payload) =>
      SendBootFrame(port, AsyncSerialContinuationAddress, transferAddress, payload);

  private static void SendBootFrame(NativeWindowsSerialPort port, int completionAddress, int transferAddress, IReadOnlyList<int> payload)
  {
    byte[] frame = EncodeBootFrame(completionAddress, transferAddress, payload);
    port.Write(frame);
    CvmMemoryProtocol.WaitForTransmitDrain(port, frame.Length);
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

  private static void SettleUsb(int milliseconds, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    Thread.Sleep(milliseconds);
  }
}