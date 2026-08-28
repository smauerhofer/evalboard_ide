namespace Ga144.Cvm.Toolchain;

/// <summary>
/// The CVM assembly language's own instruction set -- Stefan's mnemonics (<c>nop</c>,
/// <c>pushlit &lt;data&gt;</c>, <c>push</c>, <c>pop</c>) and, for each, how many words it occupies
/// once assembled and whether it carries a trailing literal operand word. This is the SHAPE of each
/// instruction only -- never a numeric opcode. Numeric opcodes (0x8000 | wordAddress, where
/// wordAddress comes from wherever node 607's interpreter loop for that primitive currently lives)
/// are a link-time concern: the assembler treats every one of these mnemonics as an external symbol
/// to be resolved later, so this project never needs to know node 607's F18 source at all.
///
/// This table is the single source of truth shared by <see cref="CvmAssembler"/> here and by the IDE
/// project's own disassembler (Ga144.Evb.Ide.Services.CvmAssemblyLanguage, which pairs each of these
/// mnemonics with node 607's live F18 symbol to actually resolve a numeric opcode for display).
/// Adding a fifth CVM opcode is a one-line change here; the IDE project references this project
/// specifically so both sides of the toolchain can never drift apart on what the instruction set is.
/// </summary>
public static class CvmInstructionSet
{
  public const string NopMnemonic = "nop";
  public const string PushLitMnemonic = "pushlit";
  public const string PushMnemonic = "push";
  public const string PopMnemonic = "pop";

  /// <summary>The shape of one CVM instruction: how many words it assembles to (its own opcode word included), and whether the last of those words is a literal operand supplied by the programmer.</summary>
  public sealed record CvmInstructionShape(string Mnemonic, int WordLength, bool HasOperand);

  /// <summary>
  /// Every known CVM instruction, in mnemonic order. Extend this list as more opcodes are defined;
  /// nothing else in this project (or in the IDE's disassembler) needs to change to pick up a new
  /// entry, beyond the IDE also being able to resolve the new mnemonic's F18 symbol name.
  /// </summary>
  public static readonly IReadOnlyList<CvmInstructionShape> Instructions =
  [
    new(NopMnemonic, 1, false),
    new(PushLitMnemonic, 2, true),
    new(PushMnemonic, 1, false),
    new(PopMnemonic, 1, false),
  ];

  private static readonly IReadOnlyDictionary<string, CvmInstructionShape> ByMnemonic =
      Instructions.ToDictionary(instruction => instruction.Mnemonic, StringComparer.OrdinalIgnoreCase);

  /// <summary>Looks up a mnemonic's shape case-insensitively. Null when it isn't a known CVM instruction (it may still be a valid label/import reference -- that's the caller's concern, not this lookup's).</summary>
  public static CvmInstructionShape? TryGetShape(string mnemonic) =>
      ByMnemonic.TryGetValue(mnemonic, out CvmInstructionShape? shape) ? shape : null;
}
