namespace Ga144.Evb.Ide.Compiler;

/// <summary>
/// One decoded slot inside an F18A instruction word: either a plain 5-bit opcode
/// (<see cref="IsControlTransfer"/> false), or a slot-0/1/2 control-transfer
/// opcode (jump/call/next/if/-if) together with the destination address packed
/// into the word's remaining low bits (DB001 2.3.1) -- a control transfer, when
/// present, is always the LAST slot actually present in the word: no slot after
/// it exists, because its address field occupies the bits later slots would
/// otherwise use.
/// </summary>
public sealed record F18DisassembledSlot(int SlotIndex, byte Opcode, string Mnemonic, bool IsControlTransfer, int? Destination)
{
  public override string ToString() =>
      IsControlTransfer ? $"{Mnemonic} 0x{Destination:X3}" : Mnemonic;
}

/// <summary>
/// One decoded 18-bit F18A instruction word: 1 to 4 slots (fewer than 4 exactly
/// when a control transfer ends the word early -- see <see cref="F18DisassembledSlot"/>).
/// </summary>
public sealed record F18DisassembledWord(int Address, int RawWord, IReadOnlyList<F18DisassembledSlot> Slots)
{
  /// <summary>
  /// True when some slot in this word is '@p' or '!p' -- the two opcodes that
  /// advance P by fetching/storing the word AT P, which is how a compiled
  /// literal ends up inline in the following memory cell. This is a per-word
  /// HEURISTIC, not a certainty: recognizing an inline literal for sure requires
  /// tracing actual control flow from a known entry point (which word runs, and
  /// therefore which word is consumed as data, depends on execution order, not
  /// just static layout), and this disassembler decodes every word
  /// independently, the same way a raw memory dump has no other choice. Treat
  /// the following word's decode as unreliable when this is true.
  /// </summary>
  public bool MayConsumeNextWordAsLiteral => Slots.Any(slot => slot.Opcode is 0x08 or 0x0C);

  public override string ToString() => string.Join(" ", Slots);
}

/// <summary>
/// Decodes raw F18A instruction words (RAM or ROM, 18 bits each) back into their
/// slot-packed mnemonics. This is a genuine F18-opcode-space disassembler --
/// distinct from, and not reusable with, <see cref="CvmAssemblyLanguage.DisassemblePage0"/>,
/// which decodes an entirely different, higher-level CVM-interpreter bytecode
/// space out of a running CVM helper node's own RAM contents.
///
/// Every opcode value and bit layout here is taken directly from this project's
/// own ENCODERS in <see cref="F18InstructionSet"/> (TryEncodePackedInstruction,
/// EncodeSlot0Control, TryEncodePackedControl) and from F18Compiler's own
/// control-transfer opcode assignments (jump=0x02, call=0x03, unext=0x04 [a
/// plain, addressless opcode -- it loops within the same word], next=0x05,
/// if=0x06, -if=0x07) -- decoding is simply those encoders run in reverse, never
/// a separate guess at the ISA.
/// </summary>
public static class F18Disassembler
{
  // Canonical mnemonic per plain (non-control-transfer) opcode value. Built
  // explicitly rather than by inverting F18InstructionSet.Opcodes, because
  // several source spellings alias the same opcode there (e.g. "@p"/"@p+",
  // "inv"/"not", "."/"nop", "!p"/"!p+") -- one canonical spelling is picked per
  // value for display.
  private static readonly IReadOnlyDictionary<byte, string> PlainMnemonics = new Dictionary<byte, string>
  {
    [0x00] = "ret",
    [0x01] = "ex",
    [0x04] = "unext",
    [0x08] = "@p",
    [0x09] = "@+",
    [0x0A] = "@b",
    [0x0B] = "@",
    [0x0C] = "!p",
    [0x0D] = "!+",
    [0x0E] = "!b",
    [0x0F] = "!",
    [0x10] = "+*",
    [0x11] = "2*",
    [0x12] = "2/",
    [0x13] = "not",
    [0x14] = "+",
    [0x15] = "and",
    [0x16] = "xor",
    [0x17] = "drop",
    [0x18] = "dup",
    [0x19] = "r>",
    [0x1A] = "over",
    [0x1B] = "a",
    [0x1C] = "nop",
    [0x1D] = ">r",
    [0x1E] = "b!",
    [0x1F] = "a!",
  };

  // Slot-0/1/2-only control-transfer opcodes (DB001 2.3.1, DB013 5.3): each
  // carries an embedded destination-address field in place of the word's
  // remaining slots, so they are never in F18InstructionSet.Opcodes/
  // PlainMnemonics -- see F18Compiler.cs's own CompileExplicitControl call
  // sites (0x02 "jump", 0x03 "call", 0x05 "next", 0x06 "if", 0x07 "-if") for
  // the same 5 values, confirmed against this project's own compiler.
  private static readonly IReadOnlyDictionary<byte, string> ControlMnemonics = new Dictionary<byte, string>
  {
    [0x02] = "jump",
    [0x03] = "call",
    [0x05] = "next",
    [0x06] = "if",
    [0x07] = "-if",
  };

  /// <summary>Decodes one raw 18-bit F18A word at the given address into its 1-4 slots.</summary>
  public static F18DisassembledWord Decode(int address, int rawWord)
  {
    int word = rawWord & F18InstructionSet.WordMask;

    // Only the opcode-bit positions (13-17, 8-12, 3-7) are ever XOR-encoded on
    // the wire -- a control transfer's destination field is always inserted
    // RAW (see TryEncodePackedControl's remarks: "The address field is
    // inserted UNENCODED"). So this XOR-decoded view is only ever used to pull
    // out an opcode value, never an address -- addresses always come from
    // 'word' directly.
    int decodedOpcodeBits = word ^ F18InstructionSet.EncodingXor;

    var slots = new List<F18DisassembledSlot>(4);

    byte slot0 = (byte)((decodedOpcodeBits >> 13) & 0x1F);
    if (ControlMnemonics.TryGetValue(slot0, out string? slot0Mnemonic))
    {
      slots.Add(new F18DisassembledSlot(0, slot0, slot0Mnemonic, IsControlTransfer: true, word & 0x3FF));
      return new F18DisassembledWord(address, rawWord, slots);
    }

    slots.Add(new F18DisassembledSlot(0, slot0, PlainMnemonics.GetValueOrDefault(slot0, $"?0x{slot0:X2}"), false, null));

    byte slot1 = (byte)((decodedOpcodeBits >> 8) & 0x1F);
    if (ControlMnemonics.TryGetValue(slot1, out string? slot1Mnemonic))
    {
      slots.Add(new F18DisassembledSlot(1, slot1, slot1Mnemonic, IsControlTransfer: true, word & 0xFF));
      return new F18DisassembledWord(address, rawWord, slots);
    }

    slots.Add(new F18DisassembledSlot(1, slot1, PlainMnemonics.GetValueOrDefault(slot1, $"?0x{slot1:X2}"), false, null));

    byte slot2 = (byte)((decodedOpcodeBits >> 3) & 0x1F);
    if (ControlMnemonics.TryGetValue(slot2, out string? slot2Mnemonic))
    {
      slots.Add(new F18DisassembledSlot(2, slot2, slot2Mnemonic, IsControlTransfer: true, word & 0x07));
      return new F18DisassembledWord(address, rawWord, slots);
    }

    slots.Add(new F18DisassembledSlot(2, slot2, PlainMnemonics.GetValueOrDefault(slot2, $"?0x{slot2:X2}"), false, null));

    // Slot 3 can never hold a transfer (DB001 2.3.1: "Slot 3 can never hold a
    // transfer"), and only stores its top 3 bits on the wire -- its low 2 bits
    // are always zero (F18InstructionSet.IsSlot3Compatible), so reconstructing
    // shifts the 3 stored bits back up by 2.
    byte slot3 = (byte)((decodedOpcodeBits & 0x07) << 2);
    slots.Add(new F18DisassembledSlot(3, slot3, PlainMnemonics.GetValueOrDefault(slot3, $"?0x{slot3:X2}"), false, null));

    return new F18DisassembledWord(address, rawWord, slots);
  }

  /// <summary>
  /// Decodes a full memory image (typically 64 RAM or 64 ROM words) into one
  /// <see cref="F18DisassembledWord"/> per address, starting at
  /// <paramref name="baseAddress"/> (0x000 for RAM, 0x080 for ROM). Every word
  /// is decoded independently -- see <see cref="F18DisassembledWord.MayConsumeNextWordAsLiteral"/>
  /// for why an inline literal cannot be told apart from real code with
  /// certainty from a static image alone.
  /// </summary>
  public static IReadOnlyList<F18DisassembledWord> DisassembleImage(IReadOnlyList<int> words, int baseAddress = 0x000)
  {
    ArgumentNullException.ThrowIfNull(words);
    var result = new List<F18DisassembledWord>(words.Count);
    for (int index = 0; index < words.Count; index++)
    {
      result.Add(Decode(baseAddress + index, words[index]));
    }

    return result;
  }
}
