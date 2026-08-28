namespace Ga144.Cvm.Toolchain;

/// <summary>
/// The CVM assembly language's own instruction set -- Stefan's mnemonics (<c>nop</c>,
/// <c>pushlit &lt;data&gt;</c>, <c>push</c>, <c>pop</c>, <c>call &lt;address&gt;</c>, <c>ret</c>,
/// <c>br &lt;offset&gt;</c>, <c>ifbr &lt;offset&gt;</c>) and, for each, how many words it occupies once
/// assembled, how its operand (if any) is encoded, and a stable numeric
/// <see cref="CvmInstructionShape.Id"/>. This is the SHAPE of each instruction only -- for the three
/// tagged-dispatch mnemonics (<see cref="CvmOperandEncoding.None"/>/<see cref="CvmOperandEncoding.TrailingWord"/>),
/// never a real numeric opcode: a real opcode there depends on which node(s) the CVM's primitives are
/// actually compiled into (originally just node 607; Stefan has since said the full instruction set is
/// spread across node 607 plus 606/608/507/506/508/407, each primitive living in exactly one of them,
/// distinguished by opcode-value ranges like 0xA??? / 0xB??? whose exact assignment he'll provide
/// separately) -- so resolving one of those mnemonics to its real opcode is entirely the linker's job,
/// once that per-node/range mapping exists. This project deliberately never needs to know any node's
/// F18 source to assemble a program: <see cref="CvmAssembler"/> treats every tagged-dispatch mnemonic
/// as an external symbol, portable across however many nodes and whatever ranges end up implementing
/// it. The two self-describing encodings (<see cref="CvmOperandEncoding.EmbeddedAddress"/>,
/// <see cref="CvmOperandEncoding.EmbeddedSignedOffset"/>) need no such resolution at all -- their whole
/// opcode word is fully known the moment the operand is, with no node/linker involvement.
///
/// This table is the single source of truth shared by <see cref="CvmAssembler"/> here and by the IDE
/// project's own disassembler (Ga144.Evb.Ide.Services.CvmAssemblyLanguage, which today only knows how
/// to pair each TAGGED mnemonic with node 607's live F18 symbol -- extending it to the other 6 nodes is
/// separate, later work; the two self-describing mnemonics need no such pairing and are recognized
/// directly by <see cref="TryDescribeSelfDecodingWord"/> instead). Adding a new CVM opcode is a
/// one-line change here; the IDE project references this project specifically so both sides of the
/// toolchain can never drift apart on what the instruction set is.
/// </summary>
public static class CvmInstructionSet
{
  public const string NopMnemonic = "nop";
  public const string PushLitMnemonic = "pushlit";
  public const string PushMnemonic = "push";
  public const string PopMnemonic = "pop";
  public const string CallMnemonic = "call";
  public const string RetMnemonic = "ret";
  public const string BranchMnemonic = "br";
  public const string ConditionalBranchMnemonic = "ifbr";

  /// <summary>
  /// The widest word address <c>call</c> can directly encode into its own opcode word: 0x7FFF, i.e.
  /// 15 bits. Bit 15 (0x8000) must stay clear on a <c>call</c> word -- that is the only thing that
  /// tells a linked program's interpreter "this word is a call to the address it contains" apart from
  /// "this word is a tagged instruction dispatch," so a <c>call</c> target that doesn't fit in 15 bits
  /// is a hard assemble/link error, never silently masked.
  /// </summary>
  public const int CallAddressMask = 0x7FFF;

  // br/ifbr's own encoding, straight from Stefan's bit-pattern table:
  //   1001 0xxx xxxx xxxx   -0x400..0x3FF   br   (branch, signed offset)
  //   1001 1xxx xxxx xxxx   -0x400..0x3FF   ifbr (conditional branch, signed offset)
  // -- a fixed 5-bit tag (bits 15-11) OR'd with an 11-bit two's-complement signed offset (bits 10-0).
  // -0x400..0x3FF is exactly an 11-bit signed value's own range, confirming the field width.

  /// <summary>The fixed high-bit pattern (bits 15-11) of a <c>br</c> word: binary 10010.</summary>
  public const int BranchTag = 0x9000;

  /// <summary>The fixed high-bit pattern (bits 15-11) of an <c>ifbr</c> word: binary 10011.</summary>
  public const int ConditionalBranchTag = 0x9800;

  /// <summary>Isolates a word's top 5 bits, for testing against <see cref="BranchTag"/>/<see cref="ConditionalBranchTag"/>.</summary>
  public const int BranchTagMask = 0xF800;

  /// <summary>Isolates a word's low 11 bits -- the raw (not yet sign-extended) branch offset field.</summary>
  public const int BranchOffsetBitMask = 0x7FF;

  /// <summary>The most negative offset an 11-bit two's-complement field can hold: -0x400 (-1024).</summary>
  public const int BranchOffsetMinValue = -0x400;

  /// <summary>The largest offset an 11-bit two's-complement field can hold: 0x3FF (1023).</summary>
  public const int BranchOffsetMaxValue = 0x3FF;

  /// <summary>
  /// How a CVM instruction's operand (if it has one) is actually encoded into its word(s). See each
  /// member for which mnemonics use it.
  /// </summary>
  public enum CvmOperandEncoding
  {
    /// <summary>No operand at all -- the instruction is exactly one tagged opcode word (<c>nop</c>, <c>push</c>, <c>pop</c>, <c>ret</c>).</summary>
    None,

    /// <summary>The operand (a literal, label, or import) occupies its own word immediately after the tagged opcode word (<c>pushlit</c>).</summary>
    TrailingWord,

    /// <summary>
    /// The instruction's one and only word directly IS the (eventually resolved) target address, with
    /// no tag at all -- restricted to <see cref="CallAddressMask"/> so bit 15 stays clear (<c>call</c>).
    /// </summary>
    EmbeddedAddress,

    /// <summary>
    /// The instruction's one and only word is a fixed <see cref="CvmInstructionShape.Tag"/> (its own
    /// high bits) OR'd with a signed offset packed into <see cref="BranchOffsetBitMask"/>'s low 11
    /// bits (<c>br</c>, <c>ifbr</c>). Fully self-describing and known at assemble time from a literal
    /// operand alone -- unlike the tagged mnemonics, it involves no node, no linker, and (for now, see
    /// <see cref="CvmAssembler"/>'s own remarks) no label/import operand either. Confirmed against real
    /// hardware (a <c>br 1</c> placed right where a call/ret round trip resumes, at address 2, jumped
    /// straight to address 4, skipping address 3 entirely): the target address is
    /// <c>(this instruction's own address + 1) + offset</c> -- i.e. relative to the address of the
    /// word immediately AFTER the branch's own opcode word, not relative to the branch word's own
    /// address. This is exactly the fact a future label operand would need (see
    /// <see cref="CvmAssembler"/>'s own remarks) to turn "jump to that label" into the right literal
    /// offset; it just isn't wired up yet.
    /// </summary>
    EmbeddedSignedOffset,
  }

  /// <summary>
  /// The shape of one CVM instruction: a stable numeric <see cref="Id"/>, how many words it assembles
  /// to (its own opcode word included), and how its operand (if any) is encoded (see
  /// <see cref="CvmOperandEncoding"/>). <see cref="Tag"/> is only meaningful for
  /// <see cref="CvmOperandEncoding.EmbeddedSignedOffset"/> shapes -- the fixed high-bit pattern that
  /// distinguishes <c>br</c> from <c>ifbr</c>; every other encoding ignores it (default 0).
  /// </summary>
  public sealed record CvmInstructionShape(int Id, string Mnemonic, int WordLength, CvmOperandEncoding Encoding, int Tag = 0)
  {
    /// <summary>True for every encoding except <see cref="CvmOperandEncoding.None"/> -- whether the assembler requires exactly one operand argument for this mnemonic.</summary>
    public bool HasOperand => Encoding != CvmOperandEncoding.None;
  }

  /// <summary>
  /// Every known CVM instruction, in mnemonic order. Extend this list as more opcodes are defined,
  /// giving each new entry the next unused <see cref="CvmInstructionShape.Id"/> -- IDs are
  /// append-only and must never be renumbered or reused, since <see cref="CvmAssembler"/> bakes a
  /// given tagged mnemonic's ID into every object file it has ever produced (see
  /// <see cref="CvmAssembler"/>'s own remarks on why). Nothing else in this project (or in the IDE's
  /// disassembler) needs to change to pick up a new tagged-dispatch entry, beyond the IDE also being
  /// able to resolve the new mnemonic's real opcode(s); a new self-describing entry (like <c>call</c>,
  /// <c>br</c>, <c>ifbr</c>) needs its own encode/decode logic in <see cref="CvmAssembler"/> and
  /// <see cref="TryDescribeSelfDecodingWord"/> instead, since there's no live compile involved.
  /// </summary>
  public static readonly IReadOnlyList<CvmInstructionShape> Instructions =
  [
    new(Id: 0, NopMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 1, PushLitMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 2, PushMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 3, PopMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 4, CallMnemonic, 1, CvmOperandEncoding.EmbeddedAddress),
    new(Id: 5, RetMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 6, BranchMnemonic, 1, CvmOperandEncoding.EmbeddedSignedOffset, Tag: BranchTag),
    new(Id: 7, ConditionalBranchMnemonic, 1, CvmOperandEncoding.EmbeddedSignedOffset, Tag: ConditionalBranchTag),
  ];

  private static readonly IReadOnlyDictionary<string, CvmInstructionShape> ByMnemonic =
      Instructions.ToDictionary(instruction => instruction.Mnemonic, StringComparer.OrdinalIgnoreCase);

  /// <summary>Looks up a mnemonic's shape case-insensitively. Null when it isn't a known CVM instruction (it may still be a valid label/import reference -- that's the caller's concern, not this lookup's).</summary>
  public static CvmInstructionShape? TryGetShape(string mnemonic) =>
      ByMnemonic.TryGetValue(mnemonic, out CvmInstructionShape? shape) ? shape : null;

  /// <summary>Looks up an instruction by its stable ID -- the inverse of encoding a <c>0x8000 | Id</c> placeholder, useful for a tool that wants to describe an unlinked object file's raw words without consulting its relocation table.</summary>
  public static CvmInstructionShape? TryGetShapeById(int id) =>
      Instructions.FirstOrDefault(shape => shape.Id == id);

  /// <summary>
  /// Extracts a branch/conditional-branch word's signed offset field, sign-extending its low 11 bits.
  /// This only recovers the raw offset, not an absolute target address -- doing that also needs the
  /// branch word's OWN address, since real hardware resolves the target as
  /// <c>(this word's own address + 1) + offset</c> (confirmed against a real <c>br 1</c> run -- see
  /// <see cref="CvmOperandEncoding.EmbeddedSignedOffset"/>'s own remarks), not as an offset from the
  /// word's own address.
  /// </summary>
  public static int DecodeBranchOffset(int word)
  {
    int raw = word & BranchOffsetBitMask;
    return raw > BranchOffsetMaxValue ? raw - (BranchOffsetBitMask + 1) : raw;
  }

  /// <summary>
  /// Decodes a single already-fetched CVM word using ONLY the two self-describing encodings
  /// (<see cref="CvmOperandEncoding.EmbeddedAddress"/>, <see cref="CvmOperandEncoding.EmbeddedSignedOffset"/>)
  /// -- the ones fully determined by the word's own bit pattern, needing no live F18 compile at all to
  /// recognize (unlike the tagged/<c>0x8000 | address</c> family, whose real opcode values only exist
  /// once resolved against a specific compile -- see the IDE's own
  /// Ga144.Evb.Ide.Services.CvmAssemblyLanguage.BuildDecodeTable for that half). Returns null when the
  /// word matches neither pattern, letting the caller fall back to that live, compile-specific table
  /// next.
  /// </summary>
  public static string? TryDescribeSelfDecodingWord(int word)
  {
    if (word <= CallAddressMask)
    {
      return $"{CallMnemonic} 0x{word:X4}";
    }

    int tag = word & BranchTagMask;
    if (tag == BranchTag)
    {
      return $"{BranchMnemonic} {DecodeBranchOffset(word)}";
    }

    if (tag == ConditionalBranchTag)
    {
      return $"{ConditionalBranchMnemonic} {DecodeBranchOffset(word)}";
    }

    return null;
  }
}