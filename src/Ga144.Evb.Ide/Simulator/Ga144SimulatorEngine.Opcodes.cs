using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Simulator;

/// <summary>What executing one slot did to the current instruction word's own flow -- consumed by
/// <see cref="Ga144SimulatorEngine.ExecuteNextSlot"/> to decide how <see cref="F18NodeSimulationState.SlotIndex"/>/
/// <see cref="F18NodeSimulationState.CurrentWord"/> change.</summary>
internal enum ExecuteOpcodeResult
{
  /// <summary>Ordinary opcode: move on to the next slot of the SAME word.</summary>
  Continue,

  /// <summary>A control transfer, <c>;</c>, or <c>ex</c> ended the word -- DB001: "skips any remaining
  /// slots and fetches next instruction word." The next <see cref="Ga144SimulatorEngine.ExecuteNextSlot"/>
  /// call fetches fresh at the (possibly just-changed) P.</summary>
  EndWord,

  /// <summary><c>unext</c> looped: continue at slot 0 of the SAME word without re-fetching it.</summary>
  RestartWord,

  /// <summary>The slot needs a port operation that could not complete this step -- see
  /// <see cref="Ga144SimulatorEngine.ResolvePendingPortOperations"/>. <see cref="F18NodeSimulationState.PendingKind"/>
  /// etc. are already set; <see cref="F18NodeSimulationState.SlotIndex"/> stays put.</summary>
  Stalled
}

public sealed partial class Ga144SimulatorEngine
{
  private const int IoAddress = 0x15D;
  private const int DataStackCapacity = 10;
  private const int ReturnStackCapacity = 9;

  /// <summary>One resolved port operation, computed by <see cref="ResolvePendingPortOperations"/> during
  /// the "tick" half of <see cref="Step"/> and committed by <see cref="ApplyPortResolution"/> during
  /// "tock". <see cref="Value"/> is the transferred word for a completed READ; unused for a completed
  /// WRITE (the value was already recorded on the writer's own <see cref="F18NodeSimulationState.PendingValue"/>
  /// at issue time).</summary>
  private readonly record struct PortResolution(F18PendingPortKind Kind, int Value);

  /// <summary>
  /// Executes exactly one slot for a node that was NOT stalled at the start of this step -- fetching a
  /// fresh word first if the previous step ended (or has never started) one. See
  /// <see cref="Ga144SimulatorEngine"/>'s own class-level remarks for why fetch-then-execute-slot-0 both
  /// happen within the same step (fetching itself is not a distinct "instruction").
  /// </summary>
  private void ExecuteNextSlot(F18NodeSimulationState state)
  {
    if (state.CurrentWord is null)
    {
      FetchNextWord(state);
      if (state.CurrentWord is null)
      {
        return; // Stalled fetching (port execution, DB001 3.3.2) -- PendingKind is already set.
      }
    }

    IReadOnlyList<F18DisassembledSlot> slots = state.CurrentWord.Slots;
    if (state.SlotIndex >= slots.Count)
    {
      // Every slot of the previous word ran without a control transfer/;/ex ending it early -- move on
      // to the next word and execute its first slot within this SAME step (see class remarks).
      FetchNextWord(state);
      if (state.CurrentWord is null)
      {
        return;
      }

      slots = state.CurrentWord.Slots;
    }

    F18DisassembledSlot slot = slots[state.SlotIndex];
    ExecuteOpcodeResult result = ExecuteOpcode(state, slot);
    switch (result)
    {
      case ExecuteOpcodeResult.Continue:
        state.SlotIndex++;
        break;
      case ExecuteOpcodeResult.EndWord:
        state.CurrentWord = null;
        break;
      case ExecuteOpcodeResult.RestartWord:
        state.SlotIndex = 0;
        break;
      case ExecuteOpcodeResult.Stalled:
        // SlotIndex/CurrentWord stay exactly where they are -- the same slot re-attempts once resolved.
        break;
    }
  }

  /// <summary>
  /// Dispatches one decoded slot. Every case below implements DB001 ("The F18A Computer") sections
  /// 2.3.2-2.3.5 as literally as this project's own state model allows -- see this class's own remarks
  /// for the two corners (<c>+*</c>'s Extended Arithmetic Mode carry handling; empty-stack peek/pop
  /// fallback) that are not fully nailed down by the datasheet's own wording.
  /// </summary>
  private ExecuteOpcodeResult ExecuteOpcode(F18NodeSimulationState state, F18DisassembledSlot slot)
  {
    if (slot.IsControlTransfer)
    {
      return ExecuteControlTransfer(state, slot);
    }

    switch (slot.Opcode)
    {
      case 0x00: // ; (return) -- DB001: "Moves R into P, popping the return stack."
        state.P = PopReturn(state);
        return ExecuteOpcodeResult.EndWord;

      case 0x01: // ex (execute) -- DB001: "Exchanges R and P."  Neither stack depth changes.
      {
        int returnTop = PeekReturn(state);
        int oldP = state.P;
        SetReturnTop(state, oldP);
        state.P = returnTop;
        return ExecuteOpcodeResult.EndWord;
      }

      case 0x04: // unext -- DB001: zero -> pop R, continue with next opcode; nonzero -> decrement R,
                 // loop to slot 0 of the SAME word.
      {
        int r = PeekReturn(state);
        if (r == 0)
        {
          PopReturn(state);
          return ExecuteOpcodeResult.Continue;
        }

        SetReturnTop(state, r - 1);
        return ExecuteOpcodeResult.RestartWord;
      }

      case 0x08: // @p -- Fetch-P: push, read [P] into T, increment P.
      {
        if (TryReadMemoryOrPort(state, state.P, 0x08, out int value))
        {
          PushParam(state, value);
          state.P = IncrementAddress(state.P);
          return ExecuteOpcodeResult.Continue;
        }

        return ExecuteOpcodeResult.Stalled;
      }

      case 0x09: // @+ -- Fetch-plus: push, read [A] into T, increment A.
      {
        if (TryReadMemoryOrPort(state, state.A, 0x09, out int value))
        {
          PushParam(state, value);
          state.A = IncrementAddress(state.A);
          return ExecuteOpcodeResult.Continue;
        }

        return ExecuteOpcodeResult.Stalled;
      }

      case 0x0A: // @b -- Fetch-B: push, read [B] into T. B unchanged.
      {
        if (TryReadMemoryOrPort(state, state.B, 0x0A, out int value))
        {
          PushParam(state, value);
          return ExecuteOpcodeResult.Continue;
        }

        return ExecuteOpcodeResult.Stalled;
      }

      case 0x0B: // @ -- Fetch: push, read [A] into T. A unchanged.
      {
        if (TryReadMemoryOrPort(state, state.A, 0x0B, out int value))
        {
          PushParam(state, value);
          return ExecuteOpcodeResult.Continue;
        }

        return ExecuteOpcodeResult.Stalled;
      }

      case 0x0C: // !p -- Store-P: write T into [P], pop, increment P.
      {
        int t = PeekParam(state);
        if (TryWriteMemoryOrPort(state, state.P, 0x0C, t))
        {
          PopParam(state);
          state.P = IncrementAddress(state.P);
          return ExecuteOpcodeResult.Continue;
        }

        return ExecuteOpcodeResult.Stalled;
      }

      case 0x0D: // !+ -- Store-plus: write T into [A], pop, increment A.
      {
        int t = PeekParam(state);
        if (TryWriteMemoryOrPort(state, state.A, 0x0D, t))
        {
          PopParam(state);
          state.A = IncrementAddress(state.A);
          return ExecuteOpcodeResult.Continue;
        }

        return ExecuteOpcodeResult.Stalled;
      }

      case 0x0E: // !b -- Store-B: write T into [B], pop. B unchanged.
      {
        int t = PeekParam(state);
        if (TryWriteMemoryOrPort(state, state.B, 0x0E, t))
        {
          PopParam(state);
          return ExecuteOpcodeResult.Continue;
        }

        return ExecuteOpcodeResult.Stalled;
      }

      case 0x0F: // ! -- Store: write T into [A], pop. A unchanged.
      {
        int t = PeekParam(state);
        if (TryWriteMemoryOrPort(state, state.A, 0x0F, t))
        {
          PopParam(state);
          return ExecuteOpcodeResult.Continue;
        }

        return ExecuteOpcodeResult.Stalled;
      }

      case 0x10: // +* -- multiply step. See this method's own remarks below.
        ExecuteMultiplyStep(state);
        return ExecuteOpcodeResult.Continue;

      case 0x11: // 2* -- DB001: "Shifts T left one bit logically (shifts zero into T0, discards T17)."
      {
        int t = PeekParam(state);
        SetTop(state, (t << 1) & F18InstructionSet.WordMask);
        return ExecuteOpcodeResult.Continue;
      }

      case 0x12: // 2/ -- DB001: "Shifts T right one bit arithmetically (propagates T17 ...; discards T0)."
      {
        int t = PeekParam(state);
        int bit17 = t & 0x20000;
        SetTop(state, bit17 | ((t >> 1) & 0x1FFFF));
        return ExecuteOpcodeResult.Continue;
      }

      case 0x13: // not -- DB001: "Inverts each bit of T."
        SetTop(state, (~PeekParam(state)) & F18InstructionSet.WordMask);
        return ExecuteOpcodeResult.Continue;

      case 0x14: // + -- DB001: "Replaces T with the twos complement sum of S and T. Pops data stack into S."
                 // In Extended Arithmetic Mode (P9), becomes add-with-carry: includes the latched carry
                 // in the sum and latches the carry out of bit 17.
      {
        int t = PopParam(state);
        int s = PopParam(state);
        bool eam = (state.CurrentWordAddress & F18InstructionSet.ExtendedArithmeticBit) != 0;
        int carryIn = eam ? state.Carry : 0;
        int raw = (s & F18InstructionSet.WordMask) + (t & F18InstructionSet.WordMask) + carryIn;
        if (eam)
        {
          state.Carry = (raw >> 18) & 1;
        }

        PushParam(state, raw & F18InstructionSet.WordMask);
        return ExecuteOpcodeResult.Continue;
      }

      case 0x15: // and -- DB001: "Replaces T with the Boolean AND of S and T. Pops data stack."
      {
        int t = PopParam(state);
        int s = PopParam(state);
        PushParam(state, s & t);
        return ExecuteOpcodeResult.Continue;
      }

      case 0x16: // xor -- DB001 labels this column "or"/"Exclusive Or"; this project's own encoder
                 // (F18InstructionSet.Opcodes) names it "xor", matching the actual bitwise operation.
      {
        int t = PopParam(state);
        int s = PopParam(state);
        PushParam(state, s ^ t);
        return ExecuteOpcodeResult.Continue;
      }

      case 0x17: // drop -- DB001: "Drops the top item ... by copying S into T and popping the data stack."
        PopParam(state);
        return ExecuteOpcodeResult.Continue;

      case 0x18: // dup -- DB001: "Duplicates the top item ... by pushing the data stack and copying T into S."
        PushParam(state, PeekParam(state));
        return ExecuteOpcodeResult.Continue;

      case 0x19: // pop (r>) -- DB001: "Moves R into T, popping the return stack and pushing the data stack."
        PushParam(state, PopReturn(state));
        return ExecuteOpcodeResult.Continue;

      case 0x1A: // over -- DB001: "pushing S onto the stack, moving T into S, and replacing T by the
                 // previous value of S" -- net effect: push a copy of S.
        PushParam(state, PeekSecond(state));
        return ExecuteOpcodeResult.Continue;

      case 0x1B: // a -- DB001: "Fetches the contents of register A into T, pushing the data stack."
        PushParam(state, state.A);
        return ExecuteOpcodeResult.Continue;

      case 0x1C: // . (nop)
        return ExecuteOpcodeResult.Continue;

      case 0x1D: // >r -- push from T to R.
        PushReturn(state, PopParam(state));
        return ExecuteOpcodeResult.Continue;

      case 0x1E: // b! -- store into register B.
        state.B = PopParam(state);
        return ExecuteOpcodeResult.Continue;

      case 0x1F: // a! -- store into register A.
        state.A = PopParam(state);
        return ExecuteOpcodeResult.Continue;

      default:
        state.Error = $"Unrecognized opcode 0x{slot.Opcode:X2} at 0x{state.CurrentWordAddress:X3} slot {slot.SlotIndex}.";
        state.Halted = true;
        return ExecuteOpcodeResult.EndWord;
    }
  }

  /// <summary>
  /// DB001 2.3.3, x10 +*, transcribed bit-for-bit: "Signed Multiplicand is in S, unsigned multiplier in
  /// A, T=0 at start of a step sequence. Uses T:A as a 36-bit shift register with multiplier in A."
  /// (1) A0=0: arithmetic-right-shift the 36-bit T:A register (T17 unchanged, copied into T16; T0 copied
  /// to A17; A0 discarded). (2) A0=1: S+T as 19-bit signed values, then the 37-bit (sum:A) concatenation
  /// shifts right one bit to replace T:A. Extended Arithmetic Mode folds the carry latch into the case-2
  /// sum and re-latches the carry out of bit 17, per that same section's own caveat -- implemented here
  /// exactly as that text describes, but NOT independently verified against real silicon or another
  /// implementation, since this is the single most intricate opcode in the set and this project has no
  /// existing runnable reference for it to check against.
  /// </summary>
  private static void ExecuteMultiplyStep(F18NodeSimulationState state)
  {
    bool eam = (state.CurrentWordAddress & F18InstructionSet.ExtendedArithmeticBit) != 0;
    int a = state.A & F18InstructionSet.WordMask;

    if ((a & 1) == 0)
    {
      int t = PeekParam(state);
      int bit17 = t & 0x20000;
      int newT = bit17 | ((t >> 1) & 0x1FFFF);
      int newA = ((t & 1) << 17) | (a >> 1);
      SetTop(state, newT);
      state.A = newA & F18InstructionSet.WordMask;
    }
    else
    {
      int s = PeekSecond(state);
      int t = PeekParam(state);
      int carryIn = eam ? state.Carry : 0;
      long sum19 = ((long)(s & F18InstructionSet.WordMask) + (t & F18InstructionSet.WordMask) + carryIn) & 0x7FFFF;
      if (eam)
      {
        state.Carry = (int)((sum19 >> 18) & 1);
      }

      long combined = (sum19 << 18) | (uint)a;
      long shifted = combined >> 1;
      int newT = (int)((shifted >> 18) & F18InstructionSet.WordMask);
      int newA = (int)(shifted & F18InstructionSet.WordMask);
      SetTop(state, newT);
      state.A = newA;
    }
  }

  /// <summary>
  /// DB001 2.3.1/2.3.5: reconstructs a control transfer's real destination. Slot 0 already carries the
  /// full 10-bit address (<see cref="F18InstructionSet.AddressFieldWidth"/> = 10, the same width as P
  /// itself). Slots 1/2 carry only their own narrow field (8/3 bits); the real destination replaces the
  /// LOW n bits of "the current (incremented) value of P at the time the jump instruction executes" --
  /// which, in this engine, is simply the LIVE <see cref="F18NodeSimulationState.P"/> at this exact
  /// moment: <see cref="FetchNextWord"/> already increments P immediately at fetch time (before any slot
  /// of the word runs), and any <c>@p</c>/<c>!p</c> earlier in the SAME word has already advanced it
  /// further by the time a later slot's own transfer executes -- so no separate "nextP" bookkeeping is
  /// needed here at all. DB001 2.3.1 also notes that "slot 1 and 2 jumps have no effect upon P9 ... [and]
  /// additionally force P8 to zero" -- P9 (Extended Arithmetic Mode) is left alone by construction here
  /// (the mask never reaches that high a bit for an 8- or 3-bit field), but P8 (x080, RAM/ROM select) is
  /// cleared explicitly below for slot 1/2, since it otherwise sits INSIDE a slot-1 field's own 8 bits and
  /// would need this override to match real hardware.
  /// </summary>
  private ExecuteOpcodeResult ExecuteControlTransfer(F18NodeSimulationState state, F18DisassembledSlot slot)
  {
    int nextP = state.P;
    int rawField = slot.Destination!.Value;
    int width = F18InstructionSet.AddressFieldWidth(slot.SlotIndex);
    int mask = width <= 0 ? 0 : (1 << width) - 1;
    int destination = slot.SlotIndex == 0
        ? rawField
        : ((nextP & ~mask) | (rawField & mask)) & ~0x080;

    switch (slot.Opcode)
    {
      case 0x02: // jump
        state.P = destination;
        return ExecuteOpcodeResult.EndWord;

      case 0x03: // call
        PushReturn(state, nextP);
        state.P = destination;
        return ExecuteOpcodeResult.EndWord;

      case 0x05: // next
      {
        int r = PeekReturn(state);
        if (r == 0)
        {
          PopReturn(state);
          return ExecuteOpcodeResult.EndWord; // P already = nextP -- falls through normally.
        }

        SetReturnTop(state, r - 1);
        state.P = destination;
        return ExecuteOpcodeResult.EndWord;
      }

      case 0x06: // if -- DB001: nonzero T continues (no jump, T unchanged); zero T jumps.
        if (PeekParam(state) != 0)
        {
          return ExecuteOpcodeResult.EndWord;
        }

        state.P = destination;
        return ExecuteOpcodeResult.EndWord;

      case 0x07: // -if -- DB001: negative T (T17 set) continues; positive T jumps. T unchanged either way.
      {
        int t = PeekParam(state);
        if ((t & 0x20000) != 0)
        {
          return ExecuteOpcodeResult.EndWord;
        }

        state.P = destination;
        return ExecuteOpcodeResult.EndWord;
      }

      default:
        state.Error = $"Unrecognized control-transfer opcode 0x{slot.Opcode:X2} at 0x{state.CurrentWordAddress:X3}.";
        state.Halted = true;
        return ExecuteOpcodeResult.EndWord;
    }
  }

  /// <summary>
  /// Attempts an immediate read of <paramref name="address"/> (RAM/ROM, or the plain <c>io</c> register --
  /// both always complete this same step) and returns true with <paramref name="value"/> set. For a real
  /// comm-port address it instead records a pending read (<paramref name="opcode"/> identifies which of
  /// @p/@+/@b/@ issued it, consulted at resolution time to know which register to post-increment) and
  /// returns false -- see <see cref="ResolvePendingPortOperations"/> for how/when that resolves. An
  /// unrecognized I/O address (bit x100 set but not one of the documented ones) completes immediately as
  /// 0 rather than hanging the node on behavior this simulator does not model.
  /// </summary>
  private bool TryReadMemoryOrPort(F18NodeSimulationState state, int address, byte opcode, out int value)
  {
    (bool isPort, bool isRom, int index) = ResolveMemoryAddress(address);
    if (!isPort)
    {
      value = isRom ? state.Rom[index] : state.Ram[index];
      return true;
    }

    if (address == IoAddress)
    {
      value = state.Io;
      return true;
    }

    if (GetDirections(address).Count == 0)
    {
      value = 0;
      return true;
    }

    state.PendingKind = F18PendingPortKind.Read;
    state.PendingAddress = address;
    state.PendingOpcode = opcode;
    value = 0;
    return false;
  }

  /// <summary>Write counterpart of <see cref="TryReadMemoryOrPort"/> -- a ROM address is accepted but
  /// silently discarded (real ROM is not writable; a correctly compiled program never does this).</summary>
  private bool TryWriteMemoryOrPort(F18NodeSimulationState state, int address, byte opcode, int value)
  {
    (bool isPort, bool isRom, int index) = ResolveMemoryAddress(address);
    if (!isPort)
    {
      if (!isRom)
      {
        state.Ram[index] = value & F18InstructionSet.WordMask;
      }

      return true;
    }

    if (address == IoAddress)
    {
      state.Io = value & F18InstructionSet.WordMask;
      return true;
    }

    if (GetDirections(address).Count == 0)
    {
      return true;
    }

    state.PendingKind = F18PendingPortKind.Write;
    state.PendingAddress = address;
    state.PendingOpcode = opcode;
    state.PendingValue = value & F18InstructionSet.WordMask;
    return false;
  }

  /// <summary>
  /// The "tick" half of port resolution: for every node that was ALREADY pending before this step (its
  /// offer published by an earlier tock), looks for a currently-matching complementary neighbor (or, for
  /// an edge direction with no neighbor, a user-set external-pin override -- see this class's own
  /// remarks). Returns one <see cref="PortResolution"/> per node whose stall resolves this step; a node
  /// left out simply stays pending. Reads only -- nothing is mutated until <see cref="ApplyPortResolution"/>
  /// runs, so which nodes were "already pending" is exactly <paramref name="pendingCoordinates"/> as
  /// captured at the very start of <see cref="Step"/>, before anything this step changes.
  ///
  /// Multiport rules (DB001 3.3.1), applied literally: a multiport READ resolves against ANY ONE ready
  /// complementary WRITE; a multiport WRITE requires EVERY one of its target directions to already have a
  /// matching pending READ (or be a naturally-immediate external pin) before it resolves at all.
  /// </summary>
  private Dictionary<int, PortResolution> ResolvePendingPortOperations(HashSet<int> pendingCoordinates)
  {
    var resolutions = new Dictionary<int, PortResolution>();
    var consumed = new HashSet<int>();

    foreach (int coordinate in pendingCoordinates)
    {
      if (consumed.Contains(coordinate))
      {
        continue;
      }

      F18NodeSimulationState state = _nodes[coordinate];
      IReadOnlyList<F18PortDirection> directions = GetDirections(state.PendingAddress);
      if (directions.Count == 0)
      {
        continue; // io/unrecognized addresses never stall in the first place -- defensive only.
      }

      if (state.PendingKind == F18PendingPortKind.Read)
      {
        foreach (F18PortDirection direction in directions)
        {
          int? neighbor = GetNeighbor(coordinate, direction);
          if (neighbor is int neighborCoordinate)
          {
            if (consumed.Contains(neighborCoordinate) || !pendingCoordinates.Contains(neighborCoordinate))
            {
              continue;
            }

            F18NodeSimulationState other = _nodes[neighborCoordinate];
            if (other.PendingKind != F18PendingPortKind.Write ||
                !GetDirections(other.PendingAddress).Contains(Reverse(direction)))
            {
              continue;
            }

            resolutions[coordinate] = new PortResolution(F18PendingPortKind.Read, other.PendingValue);
            resolutions[neighborCoordinate] = new PortResolution(F18PendingPortKind.Write, 0);
            consumed.Add(coordinate);
            consumed.Add(neighborCoordinate);
            break;
          }

          int? pinValue = GetExternalPinOverride(coordinate, direction);
          if (pinValue is int value)
          {
            resolutions[coordinate] = new PortResolution(F18PendingPortKind.Read, value);
            consumed.Add(coordinate);
            break;
          }
        }
      }
      else // Write
      {
        bool allReady = true;
        foreach (F18PortDirection direction in directions)
        {
          int? neighbor = GetNeighbor(coordinate, direction);
          if (neighbor is null)
          {
            continue; // external pin: writes always complete immediately, never blocks.
          }

          int neighborCoordinate = neighbor.Value;
          if (consumed.Contains(neighborCoordinate) || !pendingCoordinates.Contains(neighborCoordinate))
          {
            allReady = false;
            break;
          }

          F18NodeSimulationState other = _nodes[neighborCoordinate];
          if (other.PendingKind != F18PendingPortKind.Read ||
              !GetDirections(other.PendingAddress).Contains(Reverse(direction)))
          {
            allReady = false;
            break;
          }
        }

        if (!allReady)
        {
          continue;
        }

        foreach (F18PortDirection direction in directions)
        {
          int? neighbor = GetNeighbor(coordinate, direction);
          if (neighbor is int neighborCoordinate)
          {
            resolutions[neighborCoordinate] = new PortResolution(F18PendingPortKind.Read, state.PendingValue);
            consumed.Add(neighborCoordinate);
          }
          else
          {
            _externalPinLastWritten[(coordinate, direction)] = state.PendingValue;
          }
        }

        resolutions[coordinate] = new PortResolution(F18PendingPortKind.Write, 0);
        consumed.Add(coordinate);
      }
    }

    return resolutions;
  }

  /// <summary>The "tock" half: commits one node's resolved port operation -- installing a fetched word
  /// (port execution), or completing the stalled memory opcode (push/pop, register post-increment) and
  /// advancing past it, exactly as a same-step (non-stalled) completion would have.</summary>
  private void ApplyPortResolution(F18NodeSimulationState state, PortResolution resolution)
  {
    state.PendingKind = F18PendingPortKind.None;

    if (resolution.Kind == F18PendingPortKind.Read)
    {
      if (state.PendingOpcode == 0)
      {
        // Word fetch via port execution (DB001 3.3.2): "P is not incremented."
        state.CurrentWordAddress = state.PendingAddress;
        state.CurrentWord = F18Disassembler.Decode(state.PendingAddress, resolution.Value);
        state.SlotIndex = 0;
        state.PendingOpcode = 0;
        return;
      }

      PushParam(state, resolution.Value);
      if (state.PendingOpcode == 0x08)
      {
        state.P = IncrementAddress(state.P);
      }
      else if (state.PendingOpcode == 0x09)
      {
        state.A = IncrementAddress(state.A);
      }
    }
    else
    {
      PopParam(state);
      if (state.PendingOpcode == 0x0C)
      {
        state.P = IncrementAddress(state.P);
      }
      else if (state.PendingOpcode == 0x0D)
      {
        state.A = IncrementAddress(state.A);
      }
    }

    state.PendingOpcode = 0;
    state.SlotIndex++;
  }

  private static F18PortDirection Reverse(F18PortDirection direction) => direction switch
  {
    F18PortDirection.Up => F18PortDirection.Down,
    F18PortDirection.Down => F18PortDirection.Up,
    F18PortDirection.Left => F18PortDirection.Right,
    F18PortDirection.Right => F18PortDirection.Left,
    _ => throw new ArgumentOutOfRangeException(nameof(direction))
  };

  // ---- Stack helpers -------------------------------------------------------------------------------
  // DB001 2.3.2 describes a fixed-depth (10 data / 9 return) hardware circular stack: pushing replaces
  // the bottom entry once full, popping exposes whatever the previous second-from-top entry was. A truly
  // EMPTY stack cannot occur on real silicon (there is always SOME value sitting in the circular buffer,
  // even if never meaningfully written) -- this simulator starts every stack genuinely empty instead, so
  // peek/pop below need a fallback for that case the datasheet itself never has to define. Returning 0 is
  // a simulator-only convention, flagged here rather than at every call site.

  private static void PushParam(F18NodeSimulationState state, int value)
  {
    List<int> stack = state.ParameterStack;
    if (stack.Count == DataStackCapacity)
    {
      stack.RemoveAt(0);
    }

    stack.Add(value & F18InstructionSet.WordMask);
  }

  private static int PopParam(F18NodeSimulationState state)
  {
    List<int> stack = state.ParameterStack;
    if (stack.Count == 0)
    {
      return 0;
    }

    int value = stack[^1];
    stack.RemoveAt(stack.Count - 1);
    return value;
  }

  private static int PeekParam(F18NodeSimulationState state) =>
      state.ParameterStack.Count == 0 ? 0 : state.ParameterStack[^1];

  private static int PeekSecond(F18NodeSimulationState state) =>
      state.ParameterStack.Count < 2 ? 0 : state.ParameterStack[^2];

  private static void SetTop(F18NodeSimulationState state, int value)
  {
    List<int> stack = state.ParameterStack;
    if (stack.Count == 0)
    {
      stack.Add(value & F18InstructionSet.WordMask);
      return;
    }

    stack[^1] = value & F18InstructionSet.WordMask;
  }

  private static void PushReturn(F18NodeSimulationState state, int value)
  {
    List<int> stack = state.ReturnStack;
    if (stack.Count == ReturnStackCapacity)
    {
      stack.RemoveAt(0);
    }

    stack.Add(value & F18InstructionSet.WordMask);
  }

  private static int PopReturn(F18NodeSimulationState state)
  {
    List<int> stack = state.ReturnStack;
    if (stack.Count == 0)
    {
      return 0;
    }

    int value = stack[^1];
    stack.RemoveAt(stack.Count - 1);
    return value;
  }

  private static int PeekReturn(F18NodeSimulationState state) =>
      state.ReturnStack.Count == 0 ? 0 : state.ReturnStack[^1];

  private static void SetReturnTop(F18NodeSimulationState state, int value)
  {
    List<int> stack = state.ReturnStack;
    if (stack.Count == 0)
    {
      PushReturn(state, value);
      return;
    }

    stack[^1] = value & F18InstructionSet.WordMask;
  }
}
