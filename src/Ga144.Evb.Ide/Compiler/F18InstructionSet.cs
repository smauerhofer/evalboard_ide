namespace Ga144.Evb.Ide.Compiler;

public static class F18InstructionSet
{
  public const int WordMask = 0x3FFFF;
  public const int EncodingXor = 0x15555;
  public const byte NopOpcode = 0x1C;

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

        ["lu"] = 0x165,
        ["du"] = 0x105,
        ["dl"] = 0x135,
        ["dlu"] = 0x125,
        ["ru"] = 0x1C5,
        ["rl"] = 0x1F5,
        ["rlu"] = 0x1E5,
        ["rd"] = 0x195,
        ["rdu"] = 0x185,
        ["rdl"] = 0x1B5,
        ["rdlu"] = 0x1A5,

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

  public static bool IsSlot3Compatible(byte opcode) => (opcode & 0x03) == 0;

  public static int EncodePackedInstruction(IReadOnlyList<byte> opcodes)
  {
    if (opcodes.Count > 4)
    {
      throw new ArgumentOutOfRangeException(nameof(opcodes), "An F18A instruction word has at most four slots.");
    }

    var slots = new byte[4] { NopOpcode, NopOpcode, NopOpcode, NopOpcode };
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

  // True if a transfer occupying 'slot' of the word at 'wordAddress' can reach
  // 'destination'. The n-bit field replaces the low n bits of (wordAddress + 1),
  // so the destination's high bits must equal those of the next word. Slot 0
  // carries the full 10-bit P address and always reaches within a node.
  public static bool ControlFitsSlot(int slot, int wordAddress, int destination)
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
    var reconstructed = ((wordAddress + 1) & ~mask) | (destination & mask);
    return reconstructed == destination;
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