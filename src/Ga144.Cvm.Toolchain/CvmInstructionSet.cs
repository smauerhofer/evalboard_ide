namespace Ga144.Cvm.Toolchain;

/// <summary>
/// The CVM assembly language's own instruction set -- Stefan's mnemonics (<c>nop</c>,
/// <c>pushlit &lt;data&gt;</c>, <c>push</c>, <c>pop</c>, <c>call &lt;address&gt;</c>, <c>ret</c>) and,
/// for each, how many words it occupies once assembled, whether it carries a trailing literal operand word, and
/// a stable numeric <see cref="CvmInstructionShape.Id"/>. This is the SHAPE of each instruction only --
/// never a real numeric opcode. A real opcode depends on which node(s) the CVM's primitives are
/// actually compiled into (originally just node 607; Stefan has since said the full instruction set is
/// spread across node 607 plus 606/608/507/506/508/407, each primitive living in exactly one of them,
/// distinguished by opcode-value ranges like 0xA??? / 0xB??? whose exact assignment he'll provide
/// separately) -- so resolving a mnemonic to its real opcode is entirely the linker's job, once that
/// per-node/range mapping exists. This project deliberately never needs to know any node's F18 source
/// to assemble a program: <see cref="CvmAssembler"/> treats every mnemonic as an external symbol,
/// portable across however many nodes and whatever ranges end up implementing it.
///
/// <c>call</c> is a different shape of instruction from the other three: its opcode word is not a tag
/// dispatching to some node's primitive routine at all -- the whole word, restricted to the range
/// 0x0000-0x7FFF (bit 15 always clear), directly IS the callee's word address. This is what
/// <see cref="CvmInstructionShape.EncodesAddressDirectly"/> marks, and it is why <c>call</c> needs no
/// per-node opcode-range assignment the way <c>nop</c>/<c>pushlit</c>/<c>push</c>/<c>pop</c> do: bit 15
/// alone is enough for the CVM interpreter to tell "this word is a call to the address it contains"
/// apart from "this word is a tagged instruction" (bit 15 set), regardless of which node(s) end up
/// implementing the tagged side.
///
/// This table is the single source of truth shared by <see cref="CvmAssembler"/> here and by the IDE
/// project's own disassembler (Ga144.Evb.Ide.Services.CvmAssemblyLanguage, which today only knows how
/// to pair each mnemonic with node 607's live F18 symbol -- extending it to the other 6 nodes, and to
/// <c>call</c> at all (it has no F18 symbol to resolve against; see that file's own remarks), is
/// separate, later work). Adding a new CVM opcode is a one-line change here; the IDE project references
/// this project specifically so both sides of the toolchain can never drift apart on what the
/// instruction set is.
/// </summary>
public static class CvmInstructionSet
{
  public const string NopMnemonic = "nop";
  public const string PushLitMnemonic = "pushlit";
  public const string PushMnemonic = "push";
  public const string PopMnemonic = "pop";
  public const string CallMnemonic = "call";
  public const string RetMnemonic = "ret";

  /// <summary>
  /// The widest word address <c>call</c> can directly encode into its own opcode word: 0x7FFF, i.e.
  /// 15 bits. Bit 15 (0x8000) must stay clear on a <c>call</c> word -- that is the only thing that
  /// tells a linked program's interpreter "this word is a call to the address it contains" apart from
  /// "this word is a tagged instruction dispatch," so a <c>call</c> target that doesn't fit in 15 bits
  /// is a hard assemble/link error, never silently masked.
  /// </summary>
  public const int CallAddressMask = 0x7FFF;

  /// <summary>
  /// The shape of one CVM instruction: a stable numeric <see cref="Id"/>, how many words it
  /// assembles to (its own opcode word included), whether the last of those words is a literal
  /// operand supplied by the programmer, and whether its own opcode word directly encodes an address
  /// rather than a tagged primitive dispatch (see <see cref="EncodesAddressDirectly"/>).
  /// </summary>
  /// <param name="EncodesAddressDirectly">
  /// True only for <c>call</c> today. When true, the instruction's single opcode word IS the
  /// (eventually resolved) target address -- restricted to <see cref="CallAddressMask"/> -- rather
  /// than a <c>0x8000 | Id</c> tag placeholder resolved later via a <see cref="CvmRelocationType.CvmOpcode"/>
  /// relocation; instead it takes a plain <see cref="CvmRelocationType.AbsoluteAddress"/> relocation
  /// against its operand, the same as a <c>.word</c> or <c>pushlit</c> label/import operand, just
  /// narrower and sharing the one word with the instruction itself rather than following it. <see cref="Id"/>
  /// is still assigned for such a shape (IDs are still append-only and unique across the whole table)
  /// but is not itself encoded into the word, since there is no tag to decode it from.
  /// </param>
  public sealed record CvmInstructionShape(int Id, string Mnemonic, int WordLength, bool HasOperand, bool EncodesAddressDirectly = false);

  /// <summary>
  /// Every known CVM instruction, in mnemonic order. Extend this list as more opcodes are defined,
  /// giving each new entry the next unused <see cref="CvmInstructionShape.Id"/> -- IDs are
  /// append-only and must never be renumbered or reused, since <see cref="CvmAssembler"/> bakes a
  /// given mnemonic's ID into every object file it has ever produced (see
  /// <see cref="CvmAssembler"/>'s own remarks on why). Nothing else in this project (or in the IDE's
  /// disassembler) needs to change to pick up a new entry, beyond the IDE also being able to resolve
  /// the new mnemonic's real opcode(s).
  /// </summary>
  public static readonly IReadOnlyList<CvmInstructionShape> Instructions =
  [
    new(Id: 0, NopMnemonic, 1, false),
    new(Id: 1, PushLitMnemonic, 2, true),
    new(Id: 2, PushMnemonic, 1, false),
    new(Id: 3, PopMnemonic, 1, false),
    new(Id: 4, CallMnemonic, 1, true, EncodesAddressDirectly: true),
    new(Id: 5, RetMnemonic, 1, false),
  ];

  private static readonly IReadOnlyDictionary<string, CvmInstructionShape> ByMnemonic =
      Instructions.ToDictionary(instruction => instruction.Mnemonic, StringComparer.OrdinalIgnoreCase);

  /// <summary>Looks up a mnemonic's shape case-insensitively. Null when it isn't a known CVM instruction (it may still be a valid label/import reference -- that's the caller's concern, not this lookup's).</summary>
  public static CvmInstructionShape? TryGetShape(string mnemonic) =>
      ByMnemonic.TryGetValue(mnemonic, out CvmInstructionShape? shape) ? shape : null;

  /// <summary>Looks up an instruction by its stable ID -- the inverse of encoding a <c>0x8000 | Id</c> placeholder, useful for a tool that wants to describe an unlinked object file's raw words without consulting its relocation table.</summary>
  public static CvmInstructionShape? TryGetShapeById(int id) =>
      Instructions.FirstOrDefault(shape => shape.Id == id);
}