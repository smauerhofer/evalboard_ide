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

    // The address field is inserted unencoded after the opcode field is XOR encoded.
    // Bits 10..12 are unused for a slot-zero destination and are kept at zero.
    var encodedOpcode = ((opcode << 13) ^ EncodingXor) & 0x3E000;
    return (encodedOpcode | (destination & 0x3FF)) & WordMask;
  }
}