namespace Ga144.Evb.Ide.Compiler;

public static class F18InstructionSet
{
  public const int WordMask = 0x3FFFF;
  public const int EncodingXor = 0x15555;
  public const byte NopOpcode = 0x1C;

  // Bit P9 of an address enables Extended Arithmetic Mode when execution reaches it
  // (DB001 2.1). Division/carry ROM words (clc, --u/mod, -u/mod) are entered at an
  // address with this bit set; the '+cy'/'-cy' assembler directives mark the region
  // whose labels carry it.
  public const int ExtendedArithmeticBit = 0x200;

  public static IReadOnlyDictionary<string, byte> Opcodes { get; } =
      new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
      {
        ["return"] = 0x00,
        ["ex"] = 0x01,
        ["unext"] = 0x04,
        ["@p"] = 0x08,
        ["@+"] = 0x09,
        ["@b"] = 0x0A,
        ["@"] = 0x0B,
        ["!p"] = 0x0C,
        ["!+"] = 0x0D,
        ["!b"] = 0x0E,
        ["!"] = 0x0F,
        ["+*"] = 0x10,
        ["2*"] = 0x11,
        ["2/"] = 0x12,
        ["inv"] = 0x13,
        ["not"] = 0x13,
        ["+"] = 0x14,
        ["and"] = 0x15,
        ["xor"] = 0x16,
        ["drop"] = 0x17,
        ["dup"] = 0x18,
        ["r>"] = 0x19,
        ["over"] = 0x1A,
        ["a"] = 0x1B,
        ["."] = 0x1C,
        ["nop"] = 0x1C,
        [">r"] = 0x1D,
        ["b!"] = 0x1E,
        ["a!"] = 0x1F
      };

  // DB013 4.2.7.1 "Named Literals": these register names normally assemble as a
  // literal directly in the instruction stream (like any other number); preceded
  // by '#' they instead leave their value on the compile-time stack (F18Compiler
  // handles the '#' condition uniformly for every name in Constants/
  // NamedMultiportCalls via TryResolveInterpretValue). NOT included here: the
  // per-node cardinal directions (4.2.7.2, F18Compiler injects those per-compile
  // from NodeCoordinate) and the 15 named multiport calls (4.2.7.3, see
  // NamedMultiportCalls below -- those default to a CALL, not a literal).
  public static IReadOnlyDictionary<string, int> Constants { get; } =
      new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
      {
        ["io"] = 0x15D,
        ["data"] = 0x141,
        ["ldata"] = 0x171,

        ["up"] = 0x145,
        ["left"] = 0x175,
        ["down"] = 0x115,
        ["right"] = 0x1D5,

        ["ram"] = 0x000,
        ["rom"] = 0x080,
        ["eam"] = 0x200,
        ["io-reset"] = 0x15555,
        ["word-mask"] = WordMask
      };

  public static IReadOnlyDictionary<string, int> CallableRomWords { get; } =
      new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
      {
        ["warm"] = 0x0A9,
        ["cold"] = 0x0AA
      };

  // DB013 4.2.7.3 "Named Calls": each of the 15 valid multiport addresses has a
  // named word that normally assembles a CALL to that address (a jump when in
  // tail position, same as any other word reference) -- NOT a literal. Preceded
  // by '#' the word instead leaves its (single-precision) address on the stack.
  // F18Compiler injects these as external Word-kind symbols so ordinary word
  // reference/tail-jump handling applies; unlike Constants, they must not be
  // resolved as a literal by TryResolveValue. The canonical DB013 spelling is the
  // dash notation; the un-dashed combinations are kept as convenience aliases for
  // the same 15 addresses (both spellings behave identically).
  public static IReadOnlyDictionary<string, int> NamedMultiportCalls { get; } =
      new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
      {
        ["---u"] = 0x145,
        ["--l-"] = 0x175,
        ["--lu"] = 0x165,
        ["-d--"] = 0x115,
        ["-d-u"] = 0x105,
        ["-dl-"] = 0x135,
        ["-dlu"] = 0x125,
        ["r---"] = 0x1D5,
        ["r--u"] = 0x1C5,
        ["r-l-"] = 0x1F5,
        ["r-lu"] = 0x1E5,
        ["rd--"] = 0x195,
        ["rd-u"] = 0x185,
        ["rdl-"] = 0x1B5,
        ["rdlu"] = 0x1A5,

        // Convenience aliases (not in DB013, kept for readability): same 15
        // addresses under the un-dashed combination names.
        ["lu"] = 0x165,
        ["du"] = 0x105,
        ["dl"] = 0x135,
        ["dlu"] = 0x125,
        ["ru"] = 0x1C5,
        ["rl"] = 0x1F5,
        ["rlu"] = 0x1E5,
        ["rd"] = 0x195,
        ["rdu"] = 0x185,
        ["rdl"] = 0x1B5
      };

  public static bool IsSlot3Compatible(byte opcode) => (opcode & 0x03) == 0;

  public static int EncodePackedInstruction(IReadOnlyList<byte> opcodes)
  {
    if (opcodes.Count > 4)
    {
      throw new ArgumentOutOfRangeException(nameof(opcodes), "An F18A instruction word has at most four slots.");
    }

    // Unused trailing slots are filled with nop ('.', 0x1C) so that, if execution
    // reaches them, nothing happens. But when the word already ends in a
    // control-flow terminator (ret 0x00 or ex 0x01) execution never falls through,
    // and the F18A ROM leaves the remaining slots as raw 0 -- so we must match that
    // to be byte-identical (filling with nop would set those bits and mismatch).
    var terminated = opcodes.Count > 0 &&
        (opcodes[^1] == 0x00 || opcodes[^1] == 0x01);
    var fill = terminated ? (byte)0x00 : NopOpcode;

    var slots = new byte[4] { fill, fill, fill, fill };
    for (var index = 0; index < opcodes.Count; index++)
    {
      slots[index] = opcodes[index];
    }

    if (!IsSlot3Compatible(slots[3]))
    {
      throw new ArgumentException("The selected slot 3 opcode is not encodable in slot 3.", nameof(opcodes));
    }

    var raw =
        (slots[0] << 13) |
        (slots[1] << 8) |
        (slots[2] << 3) |
        (slots[3] >> 2);

    return (raw ^ EncodingXor) & WordMask;
  }

  public static int EncodeSlot0Control(byte opcode, int destination)
  {
    if (opcode is < 0x02 or > 0x07)
    {
      throw new ArgumentOutOfRangeException(nameof(opcode), "The opcode is not a slot-0 control-transfer opcode.");
    }

    if (destination is < 0 or > 0x3FF)
    {
      throw new ArgumentOutOfRangeException(nameof(destination), "An F18A P address is ten bits.");
    }

    // Slot-0 jump format (DB001 Figure 4): opcode in bits 13..17, an UNUSED region
    // in bits 10..12, and the destination in the low 10 bits (0..9). The address
    // field is inserted unencoded, but the unused bits are NOT forced to zero: like
    // every non-address bit they carry the XOR encoding, so a raw zero there stores
    // as EncodingXor's bits 10..12. Mask the encoded opcode to bits 10..17 (0x3FC00)
    // -- keeping the encoded unused region -- then OR in the raw destination.
    // (Masking to 0x3E000 instead, i.e. zeroing bits 10..12, was wrong: it dropped
    // the XOR pattern there, e.g. turning warm's 0x11595 into 0x10195.)
    var encodedOpcode = ((opcode << 13) ^ EncodingXor) & 0x3FC00;
    return (encodedOpcode | (destination & 0x3FF)) & WordMask;
  }

  // ---- Packed control transfers (DB001 2.3.1) --------------------------------
  // A jump-class opcode (jump/call/next/if/-if) may occupy slot 0, 1, or 2. When
  // it does, the remainder of the word is its destination address field: the low
  // n bits of the word, where n is 10, 8, or 3 for slots 0, 1, and 2. Slot 3 can
  // never hold a transfer. The stored field replaces the low n bits of the
  // (incremented) value of P at execution time, so a slot 1 or 2 transfer can
  // only reach a destination whose high bits match those of the next word.

  // Width in bits of the destination address field for a transfer in the given slot.
  public static int AddressFieldWidth(int slot) => slot switch
  {
    0 => 10,
    1 => 8,
    2 => 3,
    _ => 0
  };

  // Reachability for a slot 1/2 transfer. 'nextP' is the value the P register holds
  // when the transfer computes its target: the address immediately after the
  // transfer's own instruction word AND after any inline literals that '@p'
  // instructions in the same word have consumed (each '@p' advances P by one). The
  // n-bit field replaces the low n bits of nextP; the destination is reachable when
  // the reconstructed target lands on the same physical word as the destination.
  // 'wordCount' is the node's physical word count (64), so a target that differs
  // from the destination by a whole multiple of it (i.e. wraps around the mirror,
  // DB001 Figure 2) still reaches -- e.g. a 'next' at 0x0FF looping back to 0x0EB
  // reconstructs to 0x1EB, which is the same physical cell. Slot 0 carries the full
  // 10-bit P address and always reaches within a node.
  public static bool ControlFitsSlot(int slot, int nextP, int destination, int wordCount)
  {
    var width = AddressFieldWidth(slot);
    if (width == 0)
    {
      return false;
    }

    if (slot == 0)
    {
      return destination is >= 0 and <= 0x3FF;
    }

    var mask = (1 << width) - 1;
    var reconstructed = (nextP & ~mask) | (destination & mask);
    if (reconstructed == destination)
    {
      return true;
    }

    // Accept a target that reaches the destination's physical word through address
    // wrapping: the difference is a whole number of word-count spans.
    var difference = reconstructed - destination;
    return wordCount > 0 && difference % wordCount == 0;
  }

  // Encode a word whose slots 0..slotIndex-1 hold ordinary (non-transfer) opcodes
  // and whose slot slotIndex holds a jump-class opcode with 'destination' in the
  // low AddressFieldWidth(slotIndex) bits. slotIndex must be 0, 1, or 2.
  public static int EncodePackedControl(
      IReadOnlyList<byte> leadingSlots,
      byte transferOpcode,
      int destination,
      int slotIndex)
  {
    if (slotIndex is < 0 or > 2)
    {
      throw new ArgumentOutOfRangeException(nameof(slotIndex), "A transfer opcode may only occupy slot 0, 1, or 2.");
    }

    if (leadingSlots.Count != slotIndex)
    {
      throw new ArgumentException("The leading slots must exactly fill slots 0..slotIndex-1.", nameof(leadingSlots));
    }

    if (transferOpcode is < 0x02 or > 0x07)
    {
      throw new ArgumentOutOfRangeException(nameof(transferOpcode), "The opcode is not a control-transfer opcode.");
    }

    var width = AddressFieldWidth(slotIndex);
    var mask = (1 << width) - 1;

    // The address field is inserted UNENCODED, exactly as EncodeSlot0Control does:
    // on the F18A only the opcode slots are XOR-encoded with EncodingXor; the
    // destination occupies the low 'width' bits raw. Build the opcode-only word and
    // XOR-encode it, keeping ONLY the five-bit opcode slots (at shifts 13/8/3) from
    // the encoded result -- the bits between the transfer opcode and the field
    // (e.g. bits 10..12 for a slot-0 ten-bit field) must stay zero, just as
    // EncodeSlot0Control masks with 0x3E000. Then OR in the raw destination. (An
    // earlier version XOR-encoded the whole word including the field, which flipped
    // it by EncodingXor's low bits -- 0x5 in a 3-bit slot, 0x55 in an 8-bit slot --
    // corrupting the target.)
    var shifts = new[] { 13, 8, 3 };
    var opcodeBits = 0;
    var opcodeMask = 0;
    for (var index = 0; index < slotIndex; index++)
    {
      opcodeBits |= leadingSlots[index] << shifts[index];
      opcodeMask |= 0x1F << shifts[index];
    }

    opcodeBits |= transferOpcode << shifts[slotIndex];
    opcodeMask |= 0x1F << shifts[slotIndex];

    // A slot-0 transfer has a 10-bit field (bits 0..9) with an UNUSED gap in bits
    // 10..12 that still carries the XOR encoding (DB001 Figure 4, "Jump | Unused |
    // Destination"), just as EncodeSlot0Control handles. Slots 1 and 2 place the
    // opcode directly above the field with no gap, so only slot 0 needs the gap
    // bits kept in the encoded region.
    if (slotIndex == 0)
    {
      opcodeMask |= 0x1C00;
    }

    var encodedOpcodes = (opcodeBits ^ EncodingXor) & opcodeMask;
    return (encodedOpcodes | (destination & mask)) & WordMask;
  }
}