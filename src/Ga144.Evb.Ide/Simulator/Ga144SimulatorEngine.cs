using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Simulator;

/// <summary>
/// A behavioral, instruction-level simulator for an entire GA144 (144 F18A nodes), driven one
/// "step" at a time by <see cref="Step"/> and (re)initialized by <see cref="Reset"/> (a bare hardware
/// reset -- only the real factory ROM is compiled/loaded, exactly as real silicon always runs it; RAM and
/// registers are not) or <see cref="Preset"/> (a reset that also compiles and loads this project's own
/// RAM as if the boot stream had run -- Stefan's own split, 2026-09-23, of what used to be one "Reset"
/// button into "Reset"/"Preset"). Built at Stefan's
/// request for a "GA144 simulator window" (2026-09-23) showing every node's current instruction,
/// registers, ports, RAM, and ROM, steppable 1/10/100 at a time or freely ("Go"/"Halt").
///
/// <b>What a "step" is.</b> Stefan's own spec: "the simulator works with 2 steps for each virtual clock
/// cycle: tick &amp; tock. tick calculates the next output according to all inputs and the states of each
/// node. tock makes the output of each node to the input of the next tick." This engine takes that
/// literally as a classic two-phase (compute-then-commit) synchronous simulation cycle, with ONE F18A
/// instruction SLOT (not a whole 4-slot word, not a whole opcode's real variable-length execution time --
/// see DB002's own timing table, which this simulator does not model) as the unit of work a non-stalled
/// node performs per step:
///   TICK  -- for every node, decide what happens this step, reading only: (a) this node's own
///            RAM/ROM/registers, and (b) every OTHER node's <see cref="F18NodeSimulationState.PendingKind"/>
///            /<see cref="F18NodeSimulationState.PendingAddress"/>/<see cref="F18NodeSimulationState.PendingValue"/>
///            exactly as they stood at the START of this step (i.e. as published by the PREVIOUS step's
///            tock) -- nothing computed during this same tick is visible to any other node's tick.
///   TOCK  -- commit every decision computed above: mutate registers/memory/pending-state for every node
///            at once.
/// A direct consequence (deliberate, not a bug): a node whose slot THIS step turns out to need a port
/// operation always spends at least one full step "pending" before the earliest opposite step where a
/// neighbor's matching operation can resolve it -- see <see cref="ResolvePendingPortOperations"/>'s own
/// remarks. A neighbor that was ALREADY pending from an earlier step (its offer published by an earlier
/// tock) can resolve immediately, which is why two nodes that have both been sitting stalled on each
/// other for a while resolve on the very next <see cref="Step"/> call once both offers exist.
///
/// <b>Opcode semantics.</b> Every opcode's behavior below is transcribed directly from DB001 ("The F18A
/// Computer", this project's own attached datasheet), sections 2.3.2-2.3.5 -- not guessed at, and not
/// this session's own prior recollection (an earlier design pass for this same feature had guessed at
/// several of these before the datasheet was consulted; this implementation supersedes that guess
/// entirely). See <see cref="ExecuteOpcode"/> (in Ga144SimulatorEngine.Opcodes.cs, the partial class half
/// of this engine) for the opcode table itself, with each opcode's own doc comment quoting the exact DB001 wording it
/// implements. The two genuinely under-specified corners -- <c>+*</c>'s Extended Arithmetic Mode carry
/// handling, and exactly what an EMPTY circular stack pop/peek returns (DB001 describes the circular
/// stack's pointer mechanics but not what a client reads back once truly exhausted, since real hardware
/// stacks are never actually "empty", only rotated) -- are flagged individually at their own
/// implementation site.
///
/// <b>Port I/O scope (Stefan's own choice, 2026-09-23).</b> An edge node's port in a direction that has no
/// physical neighbor (row/column at the array boundary) is a real chip pin, not another F18 node. Per
/// Stefan's own decision when this feature was scoped, this simulator shows such a pin's driven/read
/// value but does not simulate any real device behind it: a write to an external pin always completes
/// immediately (<see cref="SetExternalPinOverride"/> is unrelated -- that is the SIMULATED read side, see
/// below); a read from an external pin completes immediately with a user-set override value
/// (<see cref="SetExternalPinOverride"/>) if one exists, or otherwise stalls forever (matching real
/// silicon suspending on an absent handshake) -- there is no SRAM/705/300/708-specific behavior modeled
/// here at all. <c>data</c>/<c>ldata</c> (DB001 3.1's "no handshake" Up/Left variants) are routed through
/// the SAME handshake logic as Up/Left for simplicity -- the "no handshake" distinction (an immediate,
/// unsynchronized shared-latch read/write) is NOT modeled; flagged here rather than at each call site.
/// </summary>
public sealed partial class Ga144SimulatorEngine
{
  private readonly Ga144ChipConfiguration _chip;
  private readonly Ga144RomLibrary _romLibrary;
  private readonly IReadOnlyList<F18MacroDefinition> _userMacros;
  private readonly Dictionary<int, F18NodeSimulationState> _nodes = new(144);
  private readonly Dictionary<int, Dictionary<F18PortDirection, int>> _neighborsByCoordinate = new(144);
  private readonly Dictionary<(int Coordinate, F18PortDirection Direction), int> _externalPinOverrides = [];
  private readonly Dictionary<(int Coordinate, F18PortDirection Direction), int> _externalPinLastWritten = [];

  public Ga144SimulatorEngine(Ga144ChipConfiguration chip, Ga144RomLibrary romLibrary, IReadOnlyList<F18MacroDefinition>? userMacros = null)
  {
    _chip = chip ?? throw new ArgumentNullException(nameof(chip));
    _romLibrary = romLibrary ?? throw new ArgumentNullException(nameof(romLibrary));
    _userMacros = userMacros ?? [];

    for (int row = 0; row < 8; row++)
    {
      for (int column = 0; column < 18; column++)
      {
        int coordinate = row * 100 + column;
        _nodes[coordinate] = new F18NodeSimulationState(coordinate);
        _neighborsByCoordinate[coordinate] = BuildNeighborMap(coordinate);
      }
    }
  }

  /// <summary>All 144 nodes, keyed by coordinate -- always exactly 144 entries, one per (row, column)
  /// regardless of project configuration (every physical node exists whether or not Stefan's project has
  /// put a program on it).</summary>
  public IReadOnlyDictionary<int, F18NodeSimulationState> Nodes => _nodes;

  /// <summary>Total completed <see cref="Step"/> calls since the last <see cref="Reset"/> or
  /// <see cref="Preset"/> -- shown in the simulator window so Stefan can see how far a run has
  /// progressed.</summary>
  public long StepCount { get; private set; }

  /// <summary>True once <see cref="Reset"/> or <see cref="Preset"/> has run at least once.</summary>
  public bool IsInitialized { get; private set; }

  /// <summary>
  /// Sets (or, with <paramref name="value"/> null, clears) the value an external-pin READ on
  /// <paramref name="coordinate"/>'s <paramref name="direction"/> completes with -- only meaningful when
  /// that direction has no physical neighbor (an edge node's real chip pin; see this class's own remarks).
  /// Clearing means a pending read on that pin goes back to stalling forever, exactly like real silicon
  /// suspended on a handshake that never arrives.
  /// </summary>
  public void SetExternalPinOverride(int coordinate, F18PortDirection direction, int? value)
  {
    if (value is int actual)
    {
      _externalPinOverrides[(coordinate, direction)] = actual & F18InstructionSet.WordMask;
    }
    else
    {
      _externalPinOverrides.Remove((coordinate, direction));
    }
  }

  public int? GetExternalPinOverride(int coordinate, F18PortDirection direction) =>
      _externalPinOverrides.TryGetValue((coordinate, direction), out int value) ? value : null;

  /// <summary>The most recent value a WRITE to this external pin carried, for display only -- nothing
  /// consumes it (see this class's own remarks: an external write always completes immediately and goes
  /// nowhere in this simulator).</summary>
  public int? GetExternalPinLastWritten(int coordinate, F18PortDirection direction) =>
      _externalPinLastWritten.TryGetValue((coordinate, direction), out int value) ? value : null;

  /// <summary>
  /// Performs a bare hardware reset of every one of the 144 nodes -- literally "resets the chip", Stefan's
  /// own words (2026-09-23) drawing the line between this and <see cref="Preset"/>, refined three times the
  /// same day as Stefan clarified exactly what DB001 2.1 ("After Reset") actually says should happen:
  ///  1. "after simulator 'Reset' the ROM must be filled with the node's ROM code. the ROM code must
  ///     always be filled." -- real silicon always runs its factory ROM no matter whether RAM has ever
  ///     been loaded (see <see cref="F18NodeSimulationState.Rom"/>'s own remarks), so a bare reset is not
  ///     "nothing loaded" after all -- it is "nothing of THIS PROJECT'S OWN loaded".
  ///  2. DB001 2.1 itself, quoted verbatim by Stefan: "P is set to the address configured in the chip
  ///     layout... io is set to the state it would have after a program wrote x15555 into the register. B
  ///     is set to the address of io. Stack pointers are set to the same initial condition on every reset.
  ///     Other registers, stack contents, and RAM are not directly affected by reset." -- so a bare reset
  ///     does NOT clear RAM (it is a real hardware reset, not a factory-fresh chip), and does NOT touch A.
  ///  3. "after reset all nodes initialize P to their multiport address, except some special boot nodes" --
  ///     resolving DB001 2.1's own "either a multiport execute or... x0aa" into a concrete per-node rule:
  ///     <see cref="F18NodeSimulationState.ResetRuntimeState"/> gives <see cref="Compiler.F18AwaitAddresses.BootNodeCoordinates"/>'s
  ///     three dedicated boot nodes (705/708/300) the ROM cold entry, and every other node its own
  ///     <see cref="Compiler.F18AwaitAddresses.ForNode"/> multiport address -- so an ordinary node comes out
  ///     of a bare Reset already sitting stalled awaiting instructions on a port, exactly as real silicon
  ///     does, rather than running whatever its factory ROM happens to hold at cold entry.
  /// Concretely, per node:
  ///  1. Registers/stacks/pending-port-state go back to their
  ///     <see cref="F18NodeSimulationState.ResetRuntimeState"/> defaults -- P to its boot-node-or-multiport
  ///     address (see point 3 above), B/Io to their other DB001-specified values, both stacks cleared,
  ///     <see cref="F18NodeSimulationState.A"/> left exactly as it was (see that property's own remarks).
  ///  2. RAM is left completely untouched -- this node's own project source is never even read by a bare
  ///     Reset (unlike <see cref="Preset"/>, which compiles and loads it), and whatever RAM held before
  ///     (from an earlier Preset, or Steps since) survives the reset exactly as DB001 says it should.
  ///  3. ROM is (re)compiled from <see cref="Ga144RomLibrary"/> and loaded via
  ///     <see cref="F18NodeCompilationService.CompileRom"/> -- the same compilation <see cref="Preset"/>
  ///     itself uses for ROM, so the two can never disagree about what a node's factory ROM contains. A
  ///     node whose ROM source fails to compile keeps a blank (all-zero) ROM image and records why in
  ///     <see cref="F18NodeSimulationState.Error"/>, the same "one node's failure does not abort the rest"
  ///     policy used everywhere else in this project.
  /// <see cref="F18NodeSimulationState.CurrentWord"/> is left null (nothing has been fetched yet;
  /// <see cref="Step"/> fetches lazily on the first step). Use <see cref="Preset"/> instead to also compile
  /// and load this project's own RAM, as if the boot stream had run.
  /// </summary>
  public void Reset()
  {
    var compileService = new F18NodeCompilationService(_chip, _romLibrary, _userMacros);

    foreach (F18NodeSimulationState state in _nodes.Values)
    {
      state.ResetRuntimeState();
      Array.Clear(state.Rom);
      // RAM is deliberately left untouched here -- DB001 2.1: "RAM ... [is] not directly affected by
      // reset." Whatever an earlier Preset/Step run left behind survives a bare Reset exactly as real
      // silicon would leave it.

      try
      {
        F18CompileResult rom = compileService.CompileRom(state.Coordinate);
        if (rom.Success)
        {
          CopyWords(rom.Words, state.Rom);
        }
        else
        {
          state.Error = DescribeFirstError(rom, "ROM");
        }
      }
      catch (Exception exception)
      {
        state.Error = $"ROM compilation threw: {exception.Message}";
      }

      state.IsInitialized = true;
    }

    _externalPinLastWritten.Clear();
    StepCount = 0;
    IsInitialized = true;
  }

  /// <summary>
  /// (Re)initializes every one of the 144 nodes: "first performing a reset and second with loading the
  /// node as if it was loaded by the boot stream" (Stefan's own words) -- the "Preset" button, split off
  /// from a bare <see cref="Reset"/> at Stefan's own request (2026-09-23) so "reset the chip" and "reset
  /// AND load the project" are two separate, explicit actions rather than one. Concretely, per node:
  ///  1. Compile this node's ROM (its real factory ROM, <see cref="Ga144RomLibrary"/>) and RAM (this
  ///     project's own current source for the node, which may be blank) via
  ///     <see cref="F18NodeCompilationService"/> -- the exact same compilation pathway every other tool in
  ///     this app uses (Core Dump's comparison, Verify ROMs, the CVM installer), so the simulator can
  ///     never disagree with what a real deploy would compile.
  ///  2. Every node starts from <see cref="F18NodeSimulationState.ResetRuntimeState"/>'s own DB001 2.1
  ///     "After Reset" baseline (P to its boot-node-cold-entry-or-multiport address, B/Io to their other
  ///     documented values, both stacks cleared, A left untouched -- see that method's own remarks) --
  ///     exactly the same starting point a bare <see cref="Reset"/> uses. A node the project considers
  ///     "configured" (same test as
  ///     <see cref="Cvm.CvmBootStreamBuilder.GetConfiguredCoordinates"/>/<see cref="ViewModels.NodeViewModel.IsConfigured"/>:
  ///     <c>Enabled || SourceCode not blank</c>) then has its RAM's own compiled boot-configuration
  ///     metadata applied ON TOP of that baseline -- P := <see cref="F18CompileResult.EntryPoint"/>
  ///     (defaults to the compile's own first word address when no <c>entry</c>/<c>/p</c> directive was
  ///     used), A/B/Io from <see cref="F18CompileResult.InitialA"/>/<see cref="F18CompileResult.InitialB"/>/
  ///     <see cref="F18CompileResult.InitialIo"/> (falling back to the same DB001 defaults when the source
  ///     used no <c>/a</c>/<c>/b</c>/<c>/io</c> directive), and both stacks from
  ///     <see cref="F18CompileResult.InitialStack"/>/<see cref="F18CompileResult.InitialReturnStack"/>.
  ///     This models what a real boot-stream load (multiport-executing the compiled program into RAM,
  ///     then handing control to it) leaves behind, without simulating the actual word-by-word multiport
  ///     transfer itself.
  ///  3. An UNCONFIGURED node is simply left at that DB001 2.1 baseline -- the genuine real-hardware
  ///     "never programmed" reset state, and (unlike a bare <see cref="Reset"/>) its RAM has already been
  ///     cleared and recompiled as all-zero above, matching what a node that never receives a boot stream
  ///     actually keeps running.
  /// A node whose own project source fails to compile is not skipped: it keeps a blank (all-zero) RAM
  /// image, its real factory ROM still loads normally, and <see cref="F18NodeSimulationState.Error"/>
  /// records why -- exactly the same "one node's failure does not abort the rest" policy this project
  /// already uses for Verify ROMs and Core Dump.
  /// </summary>
  public void Preset()
  {
    _chip.Normalize();
    _romLibrary.Normalize();
    var compileService = new F18NodeCompilationService(_chip, _romLibrary, _userMacros);

    // Snapshotted with ToList(): CompileNode below (via F18NodeCompilationService) calls
    // _chip.GetNode(...), which always Normalize()s the chip first -- including an in-place
    // Nodes.Sort() -- so enumerating _chip.Nodes directly here would have the compile step for
    // one node invalidate the very enumerator walking this loop (.NET throws "Collection was
    // modified; enumeration operation may not execute." the next time MoveNext() runs). A
    // snapshot sidesteps that regardless of what GetNode does internally.
    foreach (Ga144NodeConfiguration node in _chip.Nodes.ToList())
    {
      F18NodeSimulationState state = _nodes[node.Coordinate];
      state.ResetRuntimeState();
      Array.Clear(state.Ram);
      Array.Clear(state.Rom);

      bool isConfigured = node.Enabled || !string.IsNullOrWhiteSpace(node.SourceCode);

      F18NodeCompilationResult? compiled = null;
      try
      {
        compiled = compileService.CompileNode(node.Coordinate);
      }
      catch (Exception exception)
      {
        state.Error = $"Compilation threw: {exception.Message}";
      }

      if (compiled is not null)
      {
        if (compiled.Rom.Success)
        {
          CopyWords(compiled.Rom.Words, state.Rom);
        }
        else
        {
          state.Error ??= DescribeFirstError(compiled.Rom, "ROM");
        }

        if (compiled.Ram.Success)
        {
          CopyWords(compiled.Ram.Words, state.Ram);
        }
        else if (isConfigured)
        {
          // Blank/unconfigured RAM compiling "successfully" as all zeros is expected and not an error;
          // a CONFIGURED node whose own source fails to compile is the real, worth-reporting failure.
          state.Error ??= DescribeFirstError(compiled.Ram, "RAM");
        }
      }

      if (isConfigured && compiled is { Ram.Success: true })
      {
        F18CompileResult ram = compiled.Ram;
        state.P = ram.EntryPoint ?? 0x000;
        state.A = ram.InitialA ?? 0;
        state.B = ram.InitialB ?? F18NodeSimulationState.IoRegisterAddress;
        state.Io = ram.InitialIo ?? 0x15555;
        state.ParameterStack.AddRange(ram.InitialStack);
        state.ReturnStack.AddRange(ram.InitialReturnStack);
      }
      // else: ResetRuntimeState() above already left P/B/Io at the correct DB001 2.1 "after reset, never
      // configured" baseline, and A untouched -- nothing more to do for an unconfigured node.

      FetchNextWord(state);
      state.IsInitialized = true;
    }

    _externalPinLastWritten.Clear();
    StepCount = 0;
    IsInitialized = true;
  }

  /// <summary>Performs exactly one tick/tock cycle -- see this class's own remarks for what that means
  /// for a single node. No-op (but still counts toward <see cref="StepCount"/>) if neither <see cref="Reset"/>
  /// nor <see cref="Preset"/> has ever been called.</summary>
  public void Step()
  {
    if (!IsInitialized)
    {
      return;
    }

    // ---- TICK: decide, reading only state as it stood BEFORE this step. ----
    // Captured up front, before anything below mutates a single node, so "already pending" means
    // exactly "pending as of the end of the PREVIOUS step's tock" -- see this class's own remarks on
    // why a node's port offer is only visible to others starting the step AFTER it is first raised.
    var pendingBefore = new HashSet<int>(
        _nodes.Values.Where(state => state.PendingKind != F18PendingPortKind.None).Select(state => state.Coordinate));
    Dictionary<int, PortResolution> resolutions = ResolvePendingPortOperations(pendingBefore);

    // ---- TOCK: commit every resolved port operation first, ... ----
    foreach (KeyValuePair<int, PortResolution> entry in resolutions)
    {
      ApplyPortResolution(_nodes[entry.Key], entry.Value);
    }

    // ...then let every node that was NOT already pending execute its next slot. A node resolved just
    // above is deliberately excluded here too: completing its stalled slot IS this step's one unit of
    // work for it, same as any other node -- it does not also execute a further slot this same step.
    foreach (F18NodeSimulationState state in _nodes.Values)
    {
      if (state.Halted || state.Error is not null || pendingBefore.Contains(state.Coordinate))
      {
        continue;
      }

      ExecuteNextSlot(state);
    }

    StepCount++;
  }

  /// <summary>Performs <paramref name="count"/> steps back-to-back with no intermediate work beyond the
  /// stepping itself -- the caller (the simulator window's "Step 10"/"Step 100") decides whether/when to
  /// refresh its own display; this method does not raise any UI notification of its own.</summary>
  public void Step(int count)
  {
    for (int i = 0; i < count; i++)
    {
      Step();
    }
  }

  private static void CopyWords(IReadOnlyList<int> words, int[] destination)
  {
    int count = Math.Min(words.Count, destination.Length);
    for (int i = 0; i < count; i++)
    {
      destination[i] = words[i] & F18InstructionSet.WordMask;
    }
  }

  private static string DescribeFirstError(F18CompileResult result, string memoryName)
  {
    F18Diagnostic? firstError = result.Diagnostics.FirstOrDefault(d => d.Severity == F18DiagnosticSeverity.Error);
    return firstError is null
        ? $"{memoryName} compilation failed."
        : $"{memoryName}: {firstError.Message}";
  }

  /// <summary>
  /// Fetches the word at <see cref="F18NodeSimulationState.P"/> into <see cref="F18NodeSimulationState.CurrentWord"/>
  /// and resets <see cref="F18NodeSimulationState.SlotIndex"/> to 0, THEN increments P past it (DB001's own
  /// model: P always points to the NEXT word to fetch; a real F18A increments P at fetch time, before any
  /// slot of the just-fetched word executes -- see <see cref="ExecuteControlTransfer"/>'s own remarks on why this is what
  /// makes a slot-1/2 control transfer's "current (incremented) P" reconstruction correct without any
  /// special-casing at the transfer site itself). A port-space P (I/O execution, DB001 3.3.2 "An F18 may
  /// execute instruction streams directly from a comm port") does NOT increment -- <see cref="ResolveMemoryAddress"/>
  /// distinguishes RAM/ROM from port addresses the same way every other memory access does; a fetch from a
  /// port address that has no matching neighbor/pin offering the complementary write simply STALLS the
  /// whole node (recorded as a pending READ at the fetch itself, same mechanism as any other stalled port
  /// op) rather than ever producing a "word".
  /// </summary>
  private void FetchNextWord(F18NodeSimulationState state)
  {
    int address = state.P;
    (bool isPort, bool isRom, int index) = ResolveMemoryAddress(address);
    if (isPort)
    {
      // Port execution: stall exactly like any other pending read, using the SAME resolution path --
      // PendingOpcode 0 is a sentinel meaning "this is a word fetch, not @/@+/@b/@p", consulted by
      // ApplyPortResolution to know it must install the fetched word as CurrentWord (slot 0) rather than
      // push a data-stack value.
      state.PendingKind = F18PendingPortKind.Read;
      state.PendingAddress = address;
      state.PendingOpcode = 0;
      state.CurrentWord = null;
      return;
    }

    int raw = isRom ? state.Rom[index] : state.Ram[index];
    state.CurrentWordAddress = address;
    state.CurrentWord = F18Disassembler.Decode(address, raw);
    state.SlotIndex = 0;
    state.P = IncrementAddress(address);
  }

  /// <summary>
  /// DB001 2.2: "Address incrementing, when applied to A or P, continues from x03F to x040 but wraps from
  /// x07F to x000. The same process occurs with addresses in the ROM space. Incrementing does not occur at
  /// all when the address lies in the region of I/O ports and registers. Incrementing never affects bits
  /// P8 or P9." P8 is the RAM/ROM select bit (x080); P9 is Extended Arithmetic Mode (x200,
  /// <see cref="F18InstructionSet.ExtendedArithmeticBit"/>) -- both are masked out before wrapping the low
  /// 7 bits (x000-x07F) and restored afterward, so incrementing can never flip a RAM address into ROM (or
  /// silently drop/duplicate the EAM bit) purely as a side effect of crossing x07F/x0FF.
  /// </summary>
  private static int IncrementAddress(int address)
  {
    int eam = address & F18InstructionSet.ExtendedArithmeticBit;
    int romSelect = address & 0x080;
    if ((address & 0x100) != 0)
    {
      // I/O space: "Incrementing does not occur at all."
      return address;
    }

    int low7 = address & 0x07F;
    low7 = (low7 + 1) & 0x07F;
    return eam | romSelect | low7;
  }

  /// <summary>
  /// Classifies an 18-bit address exactly the way this project's own established heuristic already does
  /// elsewhere (see this project's prior simulator-design research): bit x100 set means I/O/port space
  /// (<paramref name="isPort"/> true, <paramref name="isRom"/>/<paramref name="index"/> meaningless);
  /// otherwise bit x080 (P8) selects ROM vs RAM, and the low 6 bits (masking OUT the mirror bit x040,
  /// <see cref="F18InstructionSet.MemoryMirrorBit"/>, per that constant's own remarks: both mirrored
  /// copies address the exact same 64 physical words) give the physical word index 0-63. P9
  /// (<see cref="F18InstructionSet.ExtendedArithmeticBit"/>) plays no part in this decision either way,
  /// matching DB001 2.2's "P9 has no effect on memory decoding."
  /// </summary>
  internal static (bool IsPort, bool IsRom, int Index) ResolveMemoryAddress(int address)
  {
    int masked = address & ~F18InstructionSet.ExtendedArithmeticBit;
    if ((masked & 0x100) != 0)
    {
      return (true, false, 0);
    }

    bool isRom = (masked & 0x080) != 0;
    int index = masked & 0x3F;
    return (false, isRom, index);
  }

  private Dictionary<F18PortDirection, int> BuildNeighborMap(int coordinate)
  {
    var map = new Dictionary<F18PortDirection, int>(4);
    int row = coordinate / 100;
    int column = coordinate % 100;

    (int Row, int Column)[] candidates =
    [
      (row - 1, column),
      (row + 1, column),
      (row, column - 1),
      (row, column + 1)
    ];

    foreach ((int candidateRow, int candidateColumn) in candidates)
    {
      if (candidateRow is < 0 or > 7 || candidateColumn is < 0 or > 17)
      {
        continue;
      }

      int neighbor = candidateRow * 100 + candidateColumn;
      int localAddress = KrakenTopology.PortAddress(coordinate, neighbor);
      F18PortDirection direction = localAddress switch
      {
        0x145 => F18PortDirection.Up,
        0x115 => F18PortDirection.Down,
        0x175 => F18PortDirection.Left,
        0x1D5 => F18PortDirection.Right,
        _ => throw new InvalidOperationException($"Unexpected single-port address 0x{localAddress:X3}.")
      };

      map[direction] = neighbor;
    }

    return map;
  }

  /// <summary>Every documented single/multiport I/O address (DB001 Figure 8) that names one or more of the
  /// four comm ports, mapped to exactly which directions it names -- <c>data</c>/<c>ldata</c> included as
  /// aliases of Up/Left per this class's own remarks. <c>io</c> (x15D) is deliberately absent: it is a
  /// plain, always-immediate register, handled separately in <see cref="TryReadMemoryOrPort"/>, never a port wait.</summary>
  internal static readonly IReadOnlyDictionary<int, F18PortDirection[]> DirectionsByAddress = new Dictionary<int, F18PortDirection[]>
  {
    [0x145] = [F18PortDirection.Up],
    [0x115] = [F18PortDirection.Down],
    [0x175] = [F18PortDirection.Left],
    [0x1D5] = [F18PortDirection.Right],
    [0x165] = [F18PortDirection.Left, F18PortDirection.Up],
    [0x105] = [F18PortDirection.Down, F18PortDirection.Up],
    [0x135] = [F18PortDirection.Down, F18PortDirection.Left],
    [0x125] = [F18PortDirection.Down, F18PortDirection.Left, F18PortDirection.Up],
    [0x1C5] = [F18PortDirection.Right, F18PortDirection.Up],
    [0x1F5] = [F18PortDirection.Right, F18PortDirection.Left],
    [0x1E5] = [F18PortDirection.Right, F18PortDirection.Left, F18PortDirection.Up],
    [0x195] = [F18PortDirection.Right, F18PortDirection.Down],
    [0x185] = [F18PortDirection.Right, F18PortDirection.Down, F18PortDirection.Up],
    [0x1B5] = [F18PortDirection.Right, F18PortDirection.Down, F18PortDirection.Left],
    [0x1A5] = [F18PortDirection.Right, F18PortDirection.Down, F18PortDirection.Left, F18PortDirection.Up],
    [0x141] = [F18PortDirection.Up],   // data  -- unhandshaked Up alias, see this class's own remarks.
    [0x171] = [F18PortDirection.Left], // ldata -- unhandshaked Left alias, see this class's own remarks.
  };

  /// <summary>The directions named by <paramref name="address"/>, or an empty array when it does not name
  /// any comm port at all (an ordinary RAM/ROM address, or <c>io</c>/an unrecognized I/O address --
  /// resolved immediately, never a port wait; see <see cref="TryReadMemoryOrPort"/>).</summary>
  internal static IReadOnlyList<F18PortDirection> GetDirections(int address) =>
      DirectionsByAddress.TryGetValue(address, out F18PortDirection[]? directions) ? directions : [];

  internal int? GetNeighbor(int coordinate, F18PortDirection direction) =>
      _neighborsByCoordinate[coordinate].TryGetValue(direction, out int neighbor) ? neighbor : null;
}