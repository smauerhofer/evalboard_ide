using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Simulator;

/// <summary>
/// One of the four physical F18A comm-port directions, named the way DB001 names them -- these are
/// LOCAL directions (the mirrored "up"/"left"/"down"/"right" port addresses,
/// <see cref="Ga144SimulatorEngine"/>'s own remarks), not fixed compass directions: which physical
/// (row, column) neighbor sits behind "Up" depends on this node's own row/column parity, exactly like
/// <see cref="Models.KrakenTopology.PortAddress"/> already accounts for everywhere else in this project.
/// </summary>
public enum F18PortDirection
{
  Up,
  Down,
  Left,
  Right
}

/// <summary>Whether a node's next slot is currently stalled on a port operation, and if so, which kind
/// -- see <see cref="Ga144SimulatorEngine"/>'s own remarks for the two-step ("tick"/"tock") resolution
/// this drives.</summary>
public enum F18PendingPortKind
{
  /// <summary>Not stalled: the node's next slot is ordinary RAM/ROM code (or the node is halted/errored).</summary>
  None,
  Read,
  Write
}

/// <summary>
/// One node's live, steppable runtime state inside the GA144 simulator (<see cref="Ga144SimulatorEngine"/>).
/// Unlike <see cref="Models.PostMortemNodeSnapshot"/> (a frozen, read-only capture of real silicon), this
/// is mutated in place, one F18A instruction SLOT at a time, by the engine's own <c>Step</c> -- see that
/// class's own remarks for why a "step" is one slot rather than one whole instruction word.
///
/// <see cref="ParameterStack"/>/<see cref="ReturnStack"/> use the same bottom-to-top convention as every
/// other stack display already in this project (<see cref="Models.PostMortemNodeSnapshot.ParameterStack"/>):
/// the last entry is T (top), the one before it S; the real F18A stack is a fixed-depth (10 for data, 9
/// for return) hardware shift register (DB001 2.3.2), modeled here as a capped <see cref="List{T}"/> --
/// see <see cref="Ga144SimulatorEngine"/>'s own push/pop helpers for the overflow/underflow behavior this
/// implies.
/// </summary>
public sealed class F18NodeSimulationState
{
  /// <summary>DB001 2.1 "After Reset": "B is set to the address of io." Also the fallback
  /// <see cref="Ga144SimulatorEngine.Preset"/> uses for a compiled program that used no <c>/b</c>
  /// directive of its own.</summary>
  public const int IoRegisterAddress = 0x15D; // F18InstructionSet.Constants["io"]

  /// <summary>DB001 2.1 "After Reset": "P is set to the address configured in the chip layout. For F18A,
  /// this will always be either a multiport execute or will be ROM address x0aa." Only
  /// <see cref="F18AwaitAddresses.BootNodeCoordinates"/>'s three dedicated boot nodes (705/708/300 --
  /// DB001 5.5.4/5.5.5/5.5.6) reset to this cold entry (Stefan's own clarification, 2026-09-23): every
  /// other node resets to its own <see cref="F18AwaitAddresses.ForNode"/> multiport address instead --
  /// see <see cref="ResetRuntimeState"/>, which is where this actually gets applied.</summary>
  public const int ColdEntryAddress = 0x0AA; // F18InstructionSet.CallableRomWords["cold"]

  public F18NodeSimulationState(int coordinate)
  {
    Coordinate = coordinate;
  }

  public int Coordinate { get; }
  public int Row => Coordinate / 100;
  public int Column => Coordinate % 100;

  /// <summary>Always exactly 64 words, address x000-x03F (mirrored at x040-x07F) -- see
  /// <see cref="Ga144SimulatorEngine.ResolveMemoryAddress"/> for the mirroring/indexing rule.</summary>
  public int[] Ram { get; } = new int[64];

  /// <summary>Always exactly 64 words, address x080-x0BF (mirrored at x0C0-x0FF) -- this node's real
  /// factory ROM, compiled from <see cref="Models.Ga144RomLibrary"/> and loaded by BOTH
  /// <see cref="Ga144SimulatorEngine.Reset"/> (a bare hardware reset) and <see cref="Ga144SimulatorEngine.Preset"/>
  /// (which also compiles and loads this project's own RAM), regardless of whether the project has
  /// configured this node at all: real silicon always runs its factory ROM even with nothing ever loaded
  /// into RAM, so even a bare Reset fills this (Stefan's own clarification, 2026-09-23). <see cref="Ram"/>
  /// is what actually differs between the two -- only <see cref="Ga144SimulatorEngine.Preset"/> compiles
  /// and loads the project's own source into it.</summary>
  public int[] Rom { get; } = new int[64];

  /// <summary>Program counter. 10 bits: bits 0-7 address a RAM/ROM/port word (see
  /// <see cref="Ga144SimulatorEngine.ResolveMemoryAddress"/>), bit 8 (x100) selects I/O space, bit 9
  /// (x200, <see cref="F18InstructionSet.ExtendedArithmeticBit"/>) enables Extended Arithmetic Mode --
  /// carried along exactly like a real P register, never masked off here (only memory decoding masks
  /// it, per DB001 2.2 "P9 has no effect on memory decoding").</summary>
  public int P { get; set; }

  /// <summary>DB001 2.1 "After Reset": "Other registers ... are not directly affected by reset" -- unlike
  /// <see cref="P"/>/<see cref="B"/>/<see cref="Io"/>, <see cref="ResetRuntimeState"/> deliberately leaves
  /// this untouched (whatever it held before a reset is what it still holds after). Only a compiled
  /// program's own boot-configuration metadata (<see cref="Ga144SimulatorEngine.Preset"/>, for a
  /// "configured" node) ever assigns this explicitly.</summary>
  public int A { get; set; }
  public int B { get; set; }
  public int Io { get; set; }

  /// <summary>The ALU carry latch (0 or 1). "Not affected by reset and its state on power-up is
  /// unpredictable" (DB001 2.3.3) -- this simulator deterministically starts it at 0 rather than truly
  /// random, flagged here as a simulator-only simplification. Only <c>+</c> and <c>+*</c> touch it, and
  /// only when Extended Arithmetic Mode (P9) is active.</summary>
  public int Carry { get; set; }

  public List<int> ParameterStack { get; } = new(10);
  public List<int> ReturnStack { get; } = new(9);

  /// <summary>The word currently being executed slot-by-slot, decoded via <see cref="F18Disassembler.Decode"/>
  /// (the SAME decode this project's own disassembly displays use -- see this class's own file remarks:
  /// execution and disassembly can never disagree since they share one decoder). Null before this node's
  /// first fetch: a bare <c>Reset</c> leaves it null (nothing has been fetched -- <c>Step</c> fetches
  /// lazily), while <c>Preset</c> fetches immediately so the grid shows something right away.</summary>
  public F18DisassembledWord? CurrentWord { get; set; }

  /// <summary>Which slot of <see cref="CurrentWord"/> executes on the NEXT step (0-3, or beyond the
  /// word's own slot count when a control transfer earlier in the word already ended it).</summary>
  public int SlotIndex { get; set; }

  /// <summary>The address <see cref="CurrentWord"/> was fetched from -- kept alongside the word so a
  /// "highlight P" display can find it even while several steps into a multi-slot word.</summary>
  public int CurrentWordAddress { get; set; }

  /// <summary>True once this node has executed <c>;</c> (return) with an empty return stack, or hit an
  /// unrecoverable decode condition -- distinct from a real F18A, which never "halts" (an empty return
  /// stack pop just yields a simulator-chosen fallback address, see <see cref="Ga144SimulatorEngine"/>'s
  /// own remarks); this simulator stops advancing a node once <see cref="Error"/> is set instead of
  /// silently producing further nonsense, so a mistake is visible rather than free-running forever.</summary>
  public bool Halted { get; set; }

  /// <summary>Non-null when compilation failed at the last <c>Reset</c> or <c>Preset</c> -- the node still
  /// shows its (uncompiled) state rather than being omitted from the grid. A bare <c>Reset</c> can only
  /// report a ROM failure (it compiles the factory ROM but never reads this node's own project RAM
  /// source); <c>Preset</c> can report either a ROM or a RAM failure.</summary>
  public string? Error { get; set; }

  /// <summary>
  /// Set when the slot about to execute needs a port operation that could not complete THIS step (no
  /// matching neighbor/pin was already offering the complementary operation) -- see
  /// <see cref="Ga144SimulatorEngine"/>'s own remarks on the tick/tock resolution this drives.
  /// <see cref="SlotIndex"/>/<see cref="CurrentWord"/> stay exactly where they were: the stalled slot has
  /// not executed yet, and will not until this clears.
  /// </summary>
  public F18PendingPortKind PendingKind { get; set; }

  /// <summary>The raw port/multiport address the stalled slot is operating on (whatever A, B, or P held
  /// when the stall was recorded).</summary>
  public int PendingAddress { get; set; }

  /// <summary>For a pending WRITE, the value that will be written once resolved (T at the moment the
  /// stall was recorded). Unused for a pending read.</summary>
  public int PendingValue { get; set; }

  /// <summary>The opcode of the stalled slot (one of @p/@+/@b/@/!p/!+/!b/!) -- needed at resolution time
  /// to know which register (P, A, or B) to post-increment and whether to push (read) or pop (write) the
  /// data stack.</summary>
  public byte PendingOpcode { get; set; }

  /// <summary>True once this node has been through a bare <see cref="Ga144SimulatorEngine.Reset"/> (DB001
  /// 2.1's real hardware reset, factory ROM compiled and loaded, RAM left exactly as it was) or a
  /// <see cref="Ga144SimulatorEngine.Preset"/> (RAM compiled and loaded too) -- the grid and detail window
  /// show placeholder/neutral state only before either has ever run once.</summary>
  public bool IsInitialized { get; set; }

  /// <summary>
  /// DB001 2.1 "After Reset", transcribed directly (quoted verbatim by Stefan, 2026-09-23): "P is set to
  /// the address configured in the chip layout. For F18A, this will always be either a multiport execute
  /// or will be ROM address x0aa... io is set to the state it would have after a program wrote x15555 into
  /// the register. B is set to the address of io. Stack pointers are set to the same initial condition on
  /// every reset. Other registers, stack contents, and RAM are not directly affected by reset." Concretely:
  /// <see cref="P"/> := <see cref="ColdEntryAddress"/> for <see cref="F18AwaitAddresses.BootNodeCoordinates"/>'s
  /// three dedicated boot nodes (705/708/300 -- DB001 5.5.4/5.5.5/5.5.6, the ones that actively run their
  /// own boot-detection ROM code), or <see cref="F18AwaitAddresses.ForNode"/>'s multiport address
  /// for every other node instead (which then simply sits waiting to receive instructions on a port,
  /// exactly as a real "never-programmed" node does -- Stefan's own clarification, 2026-09-23). <see cref="Io"/>
  /// := x15555, <see cref="B"/> := <see cref="IoRegisterAddress"/>, both stacks cleared (this simulator's
  /// List-based stacks have no separate "pointer" to reset -- clearing them is the closest equivalent:
  /// nothing below an empty pointer is reachable either way). <see cref="A"/> is deliberately left untouched
  /// (DB001: "not directly affected"), and RAM/ROM are the CALLER's own concern -- both
  /// <see cref="Ga144SimulatorEngine.Reset"/> and <see cref="Ga144SimulatorEngine.Preset"/> start a freshly
  /// (re)initialized node from this same baseline before they diverge (a bare Reset then leaves RAM alone
  /// entirely, per DB001 above; Preset clears and recompiles it, "loading the project" being a distinct
  /// operation from the reset itself). <see cref="Carry"/> is the one exception to "not directly affected":
  /// DB001 2.3.3 says it is genuinely unpredictable, and this simulator picks a deterministic 0 instead
  /// purely as its own simplification (see that property's own remarks) -- not something DB001 2.1 itself
  /// specifies.
  /// </summary>
  public void ResetRuntimeState()
  {
    P = F18AwaitAddresses.BootNodeCoordinates.Contains(Coordinate)
        ? ColdEntryAddress
        : F18AwaitAddresses.ForNode(Coordinate);
    B = IoRegisterAddress;
    Io = 0x15555;
    Carry = 0;
    ParameterStack.Clear();
    ReturnStack.Clear();
    CurrentWord = null;
    SlotIndex = 0;
    CurrentWordAddress = 0;
    Halted = false;
    Error = null;
    PendingKind = F18PendingPortKind.None;
    PendingAddress = 0;
    PendingValue = 0;
    PendingOpcode = 0;
  }
}