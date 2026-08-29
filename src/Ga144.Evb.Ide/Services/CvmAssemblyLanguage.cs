using System.Globalization;
using Ga144.Cvm.Toolchain;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Cvm;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// The CVM's own small assembly language: Stefan's mnemonics (<c>nop</c>, <c>pushlit &lt;data&gt;</c>,
/// <c>push</c>, <c>pop</c>, <c>ret</c>, node 507's eleven ALU ops, node 606's own <c>leave</c>, and
/// node 508's 27 comparison/arithmetic ops) layered on top of the wire-level opcode convention
/// (opcode = tag | wordAddress) that <see cref="CvmMemoryProtocol"/> already established for node 607
/// -- node 507's ALU ops, node 606's <c>leave</c>, and node 508's own ops each carry a DIFFERENT tag of
/// their own, not node 607's 0x8000 (see
/// <see cref="Node507UnaryTagBits"/>/<see cref="Node507BinaryTagBits"/>/<see cref="Node606TagBits"/>/
/// <see cref="Node508TagBits"/>'s own remarks). (<c>call</c>, <c>br</c>, <c>ifbr</c>, <c>slit</c>, and
/// node 606's OTHER eight frame-pointer ops (<c>enter</c>, <c>adjust</c>, <c>stl</c>, <c>stp</c>,
/// <c>ldl</c>, <c>ldp</c>, <c>lal</c>, <c>lap</c>) are the exceptions -- see this class's own remarks
/// on why they aren't part of this tagged-opcode layer.)
///
/// This is deliberately a SEPARATE naming layer from any node's own F18 source symbols ('nop, 'plit,
/// 'pop, 'push on node 607; 'usl, 'ssr, 'usr, '+, '-, 'and, 'xor, 'or, 'inv, 'inc, 'dec on node 507;
/// 'leave on node 606; 'eq, 'eq0, 'false, 'true, 'ne, 'ne0, 'ugt, 'gt, 'gt0, 'ge, 'ge0, 'ule, 'le,
/// 'le0, 'lt, 'lt0, 'ult, 'uge, 'mul2, 'udiv2, 'div2, 'abs, 'negate, 'xt, 'ldt, 'stt, 'bitcnt on node
/// 508) -- those tick-names are each node's own interpreter labels and won't change; the mnemonics
/// here are what a person reads and writes, and the two are free to diverge (as pushlit already has
/// from 'plit).
///
/// The mnemonic/word-length/operand-arity SHAPE of each instruction now lives in the standalone
/// Ga144.Cvm.Toolchain project's <see cref="CvmInstructionSet"/> (shared with the freestanding
/// gaasm/galib/galink command-line tools, so both sides of the toolchain agree on what the
/// instruction set even is); this file's own job is pairing each of those shapes with the SPECIFIC
/// node that implements it and that node's own F18 symbol, which only makes sense against a live IDE
/// compile and has no business in that shared, IDE-independent project. A mnemonic is no longer
/// assumed to live on node 607 -- <see cref="NodeSymbolByMnemonic"/> below records, per mnemonic,
/// which node's compile to resolve it against, so <c>nop</c>/<c>pushlit</c>/<c>push</c>/<c>pop</c>/
/// <c>ret</c> resolve against node 607, <c>usl</c>/<c>ssr</c>/<c>usr</c>/<c>add</c>/<c>sub</c>/
/// <c>and</c>/<c>xor</c>/<c>or</c>/<c>inv</c>/<c>inc</c>/<c>dec</c> resolve against node 507,
/// <c>leave</c> resolves against node 606, and node 508's 27 comparison/arithmetic ops (<c>eq</c>
/// through <c>bitcnt</c>, see <see cref="Node508TagBits"/>'s own remarks for the full list) resolve
/// against node 508.
///
/// Shapes whose <see cref="CvmInstructionSet.CvmInstructionShape.Encoding"/> is anything other than
/// <see cref="CvmInstructionSet.CvmOperandEncoding.None"/>/<see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/>
/// are deliberately left out of that pairing: <c>call</c>
/// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/>), <c>br</c>/<c>ifbr</c>/
/// <c>slit</c> (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>), and node 606's
/// eight ops (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/>) have no F18
/// symbol at all to resolve, since none of their opcode words are a tagged dispatch to a named
/// primitive routine -- each one's whole word is fully determined by its own operand alone. Because of
/// that, none of them need a live compile to recognize: <see cref="CvmDebugSession.DisassemblePage0"/>
/// checks for them directly via <see cref="CvmInstructionSet.TryDescribeSelfDecodingWord"/> BEFORE ever
/// consulting this file's own symbol-driven decode table, so they already show up correctly in the
/// memory inspector. <see cref="Assemble"/> mirrors that same dual dispatch on the OTHER direction --
/// hand-typed CVM asm source that uses <c>call</c>/<c>br</c>/<c>ifbr</c>/<c>slit</c>/node 606's ops is
/// encoded directly from <see cref="CvmInstructionSet"/> and the operand alone, bypassing this file's
/// own <see cref="Instructions"/>/<see cref="NodeSymbolByMnemonic"/> pairing entirely (see
/// <see cref="Assemble"/>'s own remarks) -- so <see cref="Instructions"/> itself still omits all of
/// them, since they would have nothing to pair them with, without that meaning they can't be assembled.
/// Extending this file to the remaining primitive nodes (608/707/407/506) for the TAGGED mnemonics
/// they might one day expose remains separate, later work -- 607, 507, 606 (for <c>leave</c>
/// specifically -- its other eight ops are self-describing, not tagged), and now 508 (all 27 of its
/// ops are tagged, none self-describing) are simply the nodes that have tagged mnemonics of their own
/// today.
///
/// Both directions -- <see cref="BuildDecodeTable"/> for disassembly and <see cref="BuildEncodeTable"/>/
/// <see cref="Assemble"/> for assembly -- are built from the single <see cref="Instructions"/> table,
/// so they can never drift apart: adding a new TAGGED opcode to <see cref="CvmInstructionSet"/> plus
/// one line here (the node and F18 symbol it resolves to) is the only change either direction needs.
/// Each is also resolved against WHICHEVER of that mnemonic's own node happens to be present in the
/// caller's <c>compiledRam</c> -- a live chip session's <c>compiledRam</c> already has every node in
/// the boot tree (607, 507, and the rest), while the standalone (no-chip-connected) path compiles only
/// the specific nodes <see cref="ViewModels.CvmDebuggerViewModel"/> asks for
/// (<see cref="ViewModels.CvmDebuggerViewModel"/>'s own remarks cover which).
/// </summary>
internal static class CvmAssemblyLanguage
{
  public const string NopMnemonic = CvmInstructionSet.NopMnemonic;
  public const string PushLitMnemonic = CvmInstructionSet.PushLitMnemonic;
  public const string PushMnemonic = CvmInstructionSet.PushMnemonic;
  public const string PopMnemonic = CvmInstructionSet.PopMnemonic;
  public const string RetMnemonic = CvmInstructionSet.RetMnemonic;

  // Node 607's own tag: "0x8000 | wordAddress" (CvmMemoryProtocol's documented convention for its five
  // tagged primitives).
  private const int Node607TagBits = 0x8000;

  // Node 507's OWN dispatch convention is NOT node 607's flat 0x8000 tag -- per Stefan, and per 507's
  // own 'main' bit-test comments (Node507.f18): the 0xC000-0xFFFF class as a whole is what node 607's
  // own exec hands off to 507 (its first branch, "2* -if ---u ; then", tests exactly that top bit
  // pair); WITHIN that class, 507's own further bit tests split unary ALU ops (tag pattern
  // 1100_0???_????_???? = 0xC000) from binary ALU ops (1100_1???_????_???? = 0xC800) before finally
  // jumping to the specific word via the address bits in the low 11 bits. Using 607's flat 0x8000 tag
  // for these (an earlier bug) put every ALU op's opcode in totally the wrong range -- and, since a
  // node-507 word address and a node-607 word address can coincide numerically, silently collided some
  // ALU ops with unrelated node-607 primitives that happened to share the same low bits (confirmed:
  // "'-"/sub on 507 and a same-address word on 607 both encoded as 0x803B under the old, wrong tag).
  private const int Node507UnaryTagBits = 0xC000;
  private const int Node507BinaryTagBits = 0xC800;

  // Node 606's own tag for its "call word in node 606, address in opcode" family (the OTHER opcode
  // class in Stefan's node-606 table, distinct from enter/adjust/stl/stp/ldl/ldp/lal/lap's own
  // self-describing 0xA800-0xAFFF tags in CvmInstructionSet -- those never need this constant at all,
  // since they carry their own fixed value and need no node/symbol pairing). Bit pattern
  // "1010 0xxx xxxx xxxx", but per Node606.f18's own 'main' dispatch ("@b xff and >r"), the dispatch
  // byte is always masked down to 8 bits before use, so in practice only 0xA000-0xA0FF is ever
  // produced by this node's own code -- same address-field width as node 607's own 0x8000|address
  // family. 'leave is the first (and, as of this revision, only) named word reached this way.
  private const int Node606TagBits = 0xA000;

  // Node 508's own tag for its 27 comparison/arithmetic ops: the "register t" opcode class node 507's
  // own 'main' dispatch forwards wholesale to node 508 (Node507.f18's own "1110_1???_????_????" branch,
  // "--l- a leave ;"), confirmed in this project's own cvm-toolchain-design.md as 0xE800-0xEFFF. Unlike
  // node 606's 8-bit-masked 0xA000-0xA0FF, node 508's own 'main' does a direct "ex" jump to whatever
  // address it receives with no masking of its own -- the practical range actually produced is narrower
  // still (node 508's RAM is 64 words), but the tag itself is the full 0xE800 high bits, same as node
  // 507's own two ALU tags above.
  private const int Node508TagBits = 0xE800;

  // Which node implements each shared-toolchain mnemonic, that node's own F18 symbol for it, and the
  // tag bits its opcode word must carry (see Node607TagBits/Node507UnaryTagBits/Node507BinaryTagBits/
  // Node606TagBits/Node508TagBits above -- these are NOT all the same value). Every mnemonic in
  // CvmInstructionSet.Instructions must have an entry here, or BuildDecodeTable/BuildEncodeTable simply
  // won't find it in a live compile -- this is the one place that link, kept as a small, easy-to-audit
  // map rather than folded back into the shared table (which has no notion of "node", "F18 symbol", or
  // "tag" at all, on purpose: gaasm never needs any of them). Node 607's five original tagged mnemonics
  // resolve against 607's own symbols (still defined in CvmMemoryProtocol, node 607's own wire-protocol
  // convention); node 507's eleven ALU ops, node 606's own 'leave, and node 508's 27 comparison/
  // arithmetic ops resolve against each node's own tick-named words (Node507Program/Node507.f18,
  // Node606Program/Node606.f18, Node508Program/Node508.f18), using the literal F18 symbol names
  // straight from that source rather than adding node-specific constants to CvmMemoryProtocol, which is
  // documented as node 607's own convention. The eight binary ALU ops (usl/ssr/usr/add/sub/and/xor/or)
  // and three unary ones (inv/inc/dec) are split per Node507.f18's own 'main' dispatch comments -- see
  // Node507BinaryTagBits/Node507UnaryTagBits's own remarks. Node 606's own other eight ops
  // (enter/adjust/stl/stp/ldl/ldp/lal/lap) are deliberately absent from this map -- they are
  // self-describing (CvmOperandEncoding.EmbeddedUnsignedValue) and need no node/symbol pairing at all,
  // exactly like call/br/ifbr/slit; see this class's own remarks for why. All 27 of node 508's ops, by
  // contrast, share the single Node508TagBits tag -- node 508's own 'main' dispatches by a direct "ex"
  // jump to whatever address it receives, not a bit cascade like node 606's, so there is no unary/
  // binary-style split the way node 507 has.
  private static readonly IReadOnlyDictionary<string, (int NodeCoordinate, string SymbolName, int Tag)> NodeSymbolByMnemonic =
      new Dictionary<string, (int NodeCoordinate, string SymbolName, int Tag)>(StringComparer.OrdinalIgnoreCase)
      {
        [NopMnemonic] = (CvmMemoryProtocol.NopSourceNodeCoordinate, CvmMemoryProtocol.NopSymbolName, Node607TagBits),
        [PushLitMnemonic] = (CvmMemoryProtocol.NopSourceNodeCoordinate, CvmMemoryProtocol.PlitSymbolName, Node607TagBits),
        [PushMnemonic] = (CvmMemoryProtocol.NopSourceNodeCoordinate, CvmMemoryProtocol.PushSymbolName, Node607TagBits),
        [PopMnemonic] = (CvmMemoryProtocol.NopSourceNodeCoordinate, CvmMemoryProtocol.PopSymbolName, Node607TagBits),
        [RetMnemonic] = (CvmMemoryProtocol.NopSourceNodeCoordinate, CvmMemoryProtocol.RetSymbolName, Node607TagBits),
        [CvmInstructionSet.UnsignedShiftLeftMnemonic] = (Node507Program.Coordinate, "'usl", Node507BinaryTagBits),
        [CvmInstructionSet.SignedShiftRightMnemonic] = (Node507Program.Coordinate, "'ssr", Node507BinaryTagBits),
        [CvmInstructionSet.UnsignedShiftRightMnemonic] = (Node507Program.Coordinate, "'usr", Node507BinaryTagBits),
        [CvmInstructionSet.AddMnemonic] = (Node507Program.Coordinate, "'+", Node507BinaryTagBits),
        [CvmInstructionSet.SubtractMnemonic] = (Node507Program.Coordinate, "'-", Node507BinaryTagBits),
        [CvmInstructionSet.AndMnemonic] = (Node507Program.Coordinate, "'and", Node507BinaryTagBits),
        [CvmInstructionSet.XorMnemonic] = (Node507Program.Coordinate, "'xor", Node507BinaryTagBits),
        [CvmInstructionSet.OrMnemonic] = (Node507Program.Coordinate, "'or", Node507BinaryTagBits),
        [CvmInstructionSet.InvertMnemonic] = (Node507Program.Coordinate, "'inv", Node507UnaryTagBits),
        [CvmInstructionSet.IncrementMnemonic] = (Node507Program.Coordinate, "'inc", Node507UnaryTagBits),
        [CvmInstructionSet.DecrementMnemonic] = (Node507Program.Coordinate, "'dec", Node507UnaryTagBits),
        [CvmInstructionSet.LeaveMnemonic] = (Node606Program.Coordinate, "'leave", Node606TagBits),
        [CvmInstructionSet.EqualMnemonic] = (Node508Program.Coordinate, "'eq", Node508TagBits),
        [CvmInstructionSet.EqualToZeroMnemonic] = (Node508Program.Coordinate, "'eq0", Node508TagBits),
        [CvmInstructionSet.FalseMnemonic] = (Node508Program.Coordinate, "'false", Node508TagBits),
        [CvmInstructionSet.TrueMnemonic] = (Node508Program.Coordinate, "'true", Node508TagBits),
        [CvmInstructionSet.NotEqualMnemonic] = (Node508Program.Coordinate, "'ne", Node508TagBits),
        [CvmInstructionSet.NotEqualToZeroMnemonic] = (Node508Program.Coordinate, "'ne0", Node508TagBits),
        [CvmInstructionSet.UnsignedGreaterThanMnemonic] = (Node508Program.Coordinate, "'ugt", Node508TagBits),
        [CvmInstructionSet.GreaterThanMnemonic] = (Node508Program.Coordinate, "'gt", Node508TagBits),
        [CvmInstructionSet.GreaterThanZeroMnemonic] = (Node508Program.Coordinate, "'gt0", Node508TagBits),
        [CvmInstructionSet.GreaterOrEqualMnemonic] = (Node508Program.Coordinate, "'ge", Node508TagBits),
        [CvmInstructionSet.GreaterOrEqualToZeroMnemonic] = (Node508Program.Coordinate, "'ge0", Node508TagBits),
        [CvmInstructionSet.UnsignedLessOrEqualMnemonic] = (Node508Program.Coordinate, "'ule", Node508TagBits),
        [CvmInstructionSet.LessOrEqualMnemonic] = (Node508Program.Coordinate, "'le", Node508TagBits),
        [CvmInstructionSet.LessOrEqualToZeroMnemonic] = (Node508Program.Coordinate, "'le0", Node508TagBits),
        [CvmInstructionSet.LessThanMnemonic] = (Node508Program.Coordinate, "'lt", Node508TagBits),
        [CvmInstructionSet.LessThanZeroMnemonic] = (Node508Program.Coordinate, "'lt0", Node508TagBits),
        [CvmInstructionSet.UnsignedLessThanMnemonic] = (Node508Program.Coordinate, "'ult", Node508TagBits),
        [CvmInstructionSet.UnsignedGreaterOrEqualMnemonic] = (Node508Program.Coordinate, "'uge", Node508TagBits),
        [CvmInstructionSet.MultiplyByTwoMnemonic] = (Node508Program.Coordinate, "'mul2", Node508TagBits),
        [CvmInstructionSet.UnsignedDivideByTwoMnemonic] = (Node508Program.Coordinate, "'udiv2", Node508TagBits),
        [CvmInstructionSet.DivideByTwoMnemonic] = (Node508Program.Coordinate, "'div2", Node508TagBits),
        [CvmInstructionSet.AbsoluteValueMnemonic] = (Node508Program.Coordinate, "'abs", Node508TagBits),
        [CvmInstructionSet.NegateMnemonic] = (Node508Program.Coordinate, "'negate", Node508TagBits),
        [CvmInstructionSet.ExchangeTMnemonic] = (Node508Program.Coordinate, "'xt", Node508TagBits),
        [CvmInstructionSet.LoadTMnemonic] = (Node508Program.Coordinate, "'ldt", Node508TagBits),
        [CvmInstructionSet.StoreTMnemonic] = (Node508Program.Coordinate, "'stt", Node508TagBits),
        [CvmInstructionSet.BitCountMnemonic] = (Node508Program.Coordinate, "'bitcnt", Node508TagBits),
      };

  /// <summary>
  /// Every known CVM asm mnemonic THAT RESOLVES TO SOME NODE'S F18 SYMBOL, which node and symbol that
  /// is, and how many words (its own opcode word included) it occupies once assembled. <c>pushlit</c>
  /// is the only such instruction with a trailing operand word today -- extend
  /// <see cref="CvmInstructionSet"/> plus <see cref="NodeSymbolByMnemonic"/> as more tagged-dispatch
  /// opcodes are defined on any node; nothing else in this file needs to change. A shape whose
  /// <see cref="CvmInstructionSet.CvmInstructionShape.Encoding"/> is
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/> (<c>call</c>) or
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/> (<c>br</c>, <c>ifbr</c>,
  /// <c>slit</c>) has no F18 symbol by design and is filtered out here rather than added to
  /// <see cref="NodeSymbolByMnemonic"/> -- see this class's own remarks for why.
  ///
  /// A tagged (<c>None</c>/<c>TrailingWord</c>) mnemonic that ISN'T in <see cref="NodeSymbolByMnemonic"/>
  /// is also filtered out here, rather than looked up with a throwing indexer -- a genuinely new
  /// mnemonic on a node this file hasn't been taught to pair yet. A throwing indexer here made the
  /// whole class fail to load the moment ANY such gap existed (a static field initializer that throws
  /// is fatal for the rest of the process), which is exactly what happened when <c>usl</c> etc. were
  /// first added to <see cref="CvmInstructionSet"/> without a matching entry here -- filtering instead
  /// of indexing keeps a mnemonic gap on one node from taking down every other node's disassembly.
  /// </summary>
  public static readonly IReadOnlyList<(string Mnemonic, int NodeCoordinate, string SymbolName, int Tag, int WordLength, bool HasOperand)> Instructions =
      [.. CvmInstructionSet.Instructions
          .Where(shape => shape.Encoding is CvmInstructionSet.CvmOperandEncoding.None or CvmInstructionSet.CvmOperandEncoding.TrailingWord)
          .Where(shape => NodeSymbolByMnemonic.ContainsKey(shape.Mnemonic))
          .Select(shape =>
          {
            (int nodeCoordinate, string symbolName, int tag) = NodeSymbolByMnemonic[shape.Mnemonic];
            return (shape.Mnemonic, nodeCoordinate, symbolName, tag, shape.WordLength, shape.HasOperand);
          })];

  /// <summary>One parsed line of CVM assembly: a mnemonic plus its operand, when required (pushlit only, today).</summary>
  public sealed record CvmAsmInstruction(string Mnemonic, int? Operand);

  /// <summary>
  /// Resolves <see cref="Instructions"/> against THIS run's own compiles (never a frozen reference
  /// copy -- every address can move as any node's source evolves) and returns the decode direction: a
  /// map from a word's actual wire/memory VALUE to its mnemonic and word length, for
  /// <see cref="CvmDebugSession.DisassemblePage0"/> to consume. Each mnemonic is looked up against its
  /// OWN node's compile (<see cref="NodeSymbolByMnemonic"/>) -- a mnemonic whose node isn't present in
  /// <paramref name="compiledRam"/> at all, or whose F18 symbol isn't defined in that node's current
  /// source, is simply omitted.
  /// </summary>
  public static IReadOnlyDictionary<int, (string Mnemonic, int WordLength)> BuildDecodeTable(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    var table = new Dictionary<int, (string, int)>();
    foreach ((string mnemonic, int nodeCoordinate, string symbolName, int tag, int wordLength, _) in Instructions)
    {
      if (!compiledRam.TryGetValue(nodeCoordinate, out F18CompileResult? compile))
      {
        continue;
      }

      if (compile.Symbols.TryGetValue(symbolName, out F18ExportedSymbol? symbol))
      {
        // A CVM opcode is a 16-bit CVM word (CvmWordCodec.WordMask), not the wider 18-bit F18 wire
        // word the symbol's own address happens to be stored as. The tag depends on which node/opcode
        // class the mnemonic belongs to -- see NodeSymbolByMnemonic's own remarks -- never a flat
        // 0x8000 for every mnemonic.
        int opcode = tag | (symbol.Value & CvmWordCodec.WordMask);
        table[opcode] = (mnemonic, wordLength);
      }
    }

    return table;
  }

  /// <summary>
  /// The encode direction, for <see cref="Assemble"/>: resolves <see cref="Instructions"/> against
  /// THIS run's own compiles -- each mnemonic against its own node
  /// (<see cref="NodeSymbolByMnemonic"/>) -- and returns a map from mnemonic (case-insensitive) to its
  /// opcode word, word length, and whether it takes an operand.
  /// </summary>
  public static IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand)> BuildEncodeTable(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    var table = new Dictionary<string, (int, int, bool)>(StringComparer.OrdinalIgnoreCase);
    foreach ((string mnemonic, int nodeCoordinate, string symbolName, int tag, int wordLength, bool hasOperand) in Instructions)
    {
      if (!compiledRam.TryGetValue(nodeCoordinate, out F18CompileResult? compile))
      {
        continue;
      }

      if (compile.Symbols.TryGetValue(symbolName, out F18ExportedSymbol? symbol))
      {
        // A CVM opcode is a 16-bit CVM word (CvmWordCodec.WordMask), not the wider 18-bit F18 wire
        // word the symbol's own address happens to be stored as. The tag depends on which node/opcode
        // class the mnemonic belongs to -- see NodeSymbolByMnemonic's own remarks -- never a flat
        // 0x8000 for every mnemonic.
        int opcode = tag | (symbol.Value & CvmWordCodec.WordMask);
        table[mnemonic] = (opcode, wordLength, hasOperand);
      }
    }

    return table;
  }

  /// <summary>
  /// Assembles a sequence of CVM asm instructions into opcode/operand words. Two families of
  /// mnemonic are resolved completely differently, mirroring <see cref="CvmDebugSession.DisassemblePage0"/>'s
  /// own dual dispatch: <c>call</c>/<c>br</c>/<c>ifbr</c>/<c>slit</c>
  /// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/>/<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>)
  /// are self-describing -- encoded directly from <see cref="CvmInstructionSet"/> and the operand
  /// alone, no live compile involved -- while every other mnemonic is resolved against THIS run's own
  /// compile of ITS OWN node (<see cref="NodeSymbolByMnemonic"/>) via <see cref="BuildEncodeTable"/>.
  /// Returns a null word list with a 1-based-line error message (never throws) when a mnemonic isn't
  /// recognized (or, for a tagged one, its own node's current source doesn't define its symbol), an
  /// operand is missing where one is required or out of range, or one is supplied where none is
  /// allowed. This is what
  /// <see cref="CvmDebugSession.AssembleAndLoadProgram"/> uses to turn the CVM Debugger's own
  /// Assembly Code editor into a program loaded straight into the simulated SRAM -- there are no
  /// labels or sections here (unlike the freestanding <c>gaasm</c>/<see cref="CvmAssembler"/>): every
  /// operand must already be a literal, since this assembles one flat, immediately-loaded program,
  /// never a relocatable object file bound for a linker.
  /// </summary>
  public static (List<int>? Words, string? Error) Assemble(
      IReadOnlyList<CvmAsmInstruction> instructions,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand)> encodeTable = BuildEncodeTable(compiledRam);
    var words = new List<int>();
    for (int line = 0; line < instructions.Count; line++)
    {
      CvmAsmInstruction instruction = instructions[line];
      CvmInstructionSet.CvmInstructionShape? selfDescribingShape = CvmInstructionSet.TryGetShape(instruction.Mnemonic);
      if (selfDescribingShape is { Encoding: CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress or CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue or CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue })
      {
        (int? word, string? selfDescribingError) = EncodeSelfDescribingWord(selfDescribingShape, instruction.Operand, line + 1);
        if (word is null)
        {
          return (null, selfDescribingError);
        }

        words.Add(word.Value);
        continue;
      }

      if (!encodeTable.TryGetValue(instruction.Mnemonic, out (int Opcode, int WordLength, bool HasOperand) entry))
      {
        // A mnemonic that IS a genuine tagged instruction (NodeSymbolByMnemonic knows it) just
        // couldn't be resolved against a live compile -- name its own node and symbol so the message
        // points at the right source file, rather than always blaming node 607 regardless of which
        // node actually implements the mnemonic that failed.
        string detail = NodeSymbolByMnemonic.TryGetValue(instruction.Mnemonic, out (int NodeCoordinate, string SymbolName, int Tag) pairing)
            ? $"or node {pairing.NodeCoordinate:000}'s current compile doesn't define its symbol \"{pairing.SymbolName}\""
            : "and no node's current compile defines a matching symbol";
        return (null, $"line {line + 1}: \"{instruction.Mnemonic}\" is not a known CVM asm mnemonic, {detail}.");
      }

      if (entry.HasOperand && instruction.Operand is null)
      {
        return (null, $"line {line + 1}: \"{instruction.Mnemonic}\" requires an operand, e.g. \"{instruction.Mnemonic} 0x1234\".");
      }

      if (!entry.HasOperand && instruction.Operand is not null)
      {
        return (null, $"line {line + 1}: \"{instruction.Mnemonic}\" does not take an operand.");
      }

      words.Add(entry.Opcode);
      if (entry.HasOperand)
      {
        words.Add(instruction.Operand!.Value & CvmWordCodec.WordMask);
      }
    }

    return (words, null);
  }

  /// <summary>
  /// Shared by <see cref="CvmDebugSession.AssembleAndLoadProgram"/> (a live session's own port-backed
  /// simulated SRAM) and <see cref="ViewModels.CvmDebuggerViewModel"/>'s standalone Assembly Code path
  /// (a plain in-memory <see cref="CvmSimulatedSram"/> that exists whether or not a chip is connected):
  /// parses then assembles <paramref name="sourceText"/> against <paramref name="compiledRam"/> and, on
  /// success, overwrites <paramref name="sram"/>'s page 0 with the result starting at address 0,
  /// zero-filling any leftover tail from <paramref name="previousProgram"/> if it was longer, so no
  /// stale opcode lingers past the new program's end. Returns the new word list (the caller's own job
  /// to remember as its "currently loaded program") and never touches <paramref name="sram"/> at all on
  /// a parse/assemble failure.
  /// </summary>
  public static (List<int>? Words, string? Error) AssembleAndLoadProgram(
      string sourceText,
      CvmSimulatedSram sram,
      IReadOnlyList<int> previousProgram,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    (List<CvmAsmInstruction>? instructions, string? parseError) = ParseSource(sourceText);
    if (instructions is null)
    {
      return (null, parseError);
    }

    (List<int>? words, string? assembleError) = Assemble(instructions, compiledRam);
    if (words is null)
    {
      return (null, assembleError);
    }

    int previousLength = previousProgram.Count;
    sram.LoadProgram(words);
    if (words.Count < previousLength)
    {
      sram.LoadProgram(new int[previousLength - words.Count], words.Count);
    }

    return (words, null);
  }

  /// <summary>
  /// Shared by <see cref="CvmDebugSession.DisassemblePage0"/> and <see cref="ViewModels.CvmDebuggerViewModel"/>'s
  /// standalone path: linearly disassembles <paramref name="sram"/>'s page 0 from address 0 up to but
  /// not including <paramref name="endAddressExclusive"/>, into CVM assembly language mnemonics
  /// resolved against <paramref name="compiledRam"/>, plus direct bit-pattern rules for
  /// <c>call</c>/<c>br</c>/<c>ifbr</c>/<c>slit</c> (<see cref="CvmInstructionSet.TryDescribeSelfDecodingWord"/>)
  /// that need no compile/symbol at all. This MUST be a stateful scan starting at 0, never an
  /// independent per-word decode: pushlit is followed by a literal operand word that would otherwise be
  /// mistaken for its own opcode if a word were decoded in isolation.
  ///
  /// Returns a sparse map from flat address to a listing line: an instruction's own address gets its
  /// mnemonic, folded together with its operand when it has one (e.g. "pushlit 0x1234") so the memory
  /// inspector reads like a real disassembly rather than two disconnected rows; the operand word's own
  /// address is left out of the map entirely (no note at all), same as any other address that doesn't
  /// fall on a recognized instruction boundary -- typically because it holds an opcode this debugger
  /// doesn't know about yet.
  /// </summary>
  public static IReadOnlyDictionary<int, string> DisassemblePage0(
      CvmSimulatedSram sram,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam,
      int endAddressExclusive)
  {
    IReadOnlyDictionary<int, (string Mnemonic, int WordLength)> decodeTable = BuildDecodeTable(compiledRam);
    var notes = new Dictionary<int, string>();
    int address = 0;
    while (address < endAddressExclusive)
    {
      int word = sram.Read(CvmMemoryProtocol.CombineAddress(0, address));

      // "call", "br", "ifbr", and "slit" have no F18 symbol to resolve -- each one's whole word is
      // fully determined by its own bit pattern and operand alone (CvmInstructionSet.
      // CvmOperandEncoding.EmbeddedAddress / EmbeddedSignedValue), independent of node 607's live
      // compile, so all four are checked before consulting the (symbol-driven) decode table at all.
      string? selfDescribing = CvmInstructionSet.TryDescribeSelfDecodingWord(word);
      if (selfDescribing is not null)
      {
        notes[address] = selfDescribing;
        address += 1;
        continue;
      }

      if (decodeTable.TryGetValue(word, out (string Mnemonic, int WordLength) instruction))
      {
        int operandCount = instruction.WordLength - 1;
        if (operandCount == 1 && address + 1 < endAddressExclusive)
        {
          int operandValue = sram.Read(CvmMemoryProtocol.CombineAddress(0, address + 1));
          notes[address] = $"{instruction.Mnemonic} 0x{operandValue:X4}";
        }
        else
        {
          notes[address] = instruction.Mnemonic;
        }

        address += instruction.WordLength;
      }
      else
      {
        address += 1;
      }
    }

    return notes;
  }

  /// <summary>
  /// Encodes one <c>call</c>/<c>br</c>/<c>ifbr</c>/<c>slit</c>/node-606 word directly from
  /// <paramref name="shape"/> and its literal operand -- the same arithmetic
  /// <see cref="CvmAssembler.EmitEmbeddedSignedValue"/>/<see cref="CvmAssembler.EmitEmbeddedUnsignedValue"/>
  /// use for <c>br</c>/<c>ifbr</c>/<c>slit</c> and node 606's eight ops respectively (mask-derived
  /// min/max, tag OR'd with the value's low bits) and <see cref="CvmAssembler"/>'s own
  /// <c>EmbeddedAddress</c> case uses for <c>call</c>, kept as a small duplicate here rather than
  /// shared: that assembler resolves a label/import operand through relocations against a
  /// <see cref="CvmObjectFile"/>, which has no place in this simpler, label-free, immediately-loaded
  /// assembler.
  /// </summary>
  private static (int? Word, string? Error) EncodeSelfDescribingWord(CvmInstructionSet.CvmInstructionShape shape, int? operand, int lineNumber)
  {
    if (operand is not int value)
    {
      return (null, $"line {lineNumber}: \"{shape.Mnemonic}\" requires a literal operand, e.g. \"{shape.Mnemonic} 1\".");
    }

    if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress)
    {
      if ((uint)value > (uint)CvmInstructionSet.CallAddressMask)
      {
        return (null, $"line {lineNumber}: {value} does not fit in \"{shape.Mnemonic}\"'s 15-bit call target (0x0000-0x7FFF -- bit 15 is reserved).");
      }

      return (value, null);
    }

    if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue)
    {
      // Node 606's eight ops: unsigned 0..ValueBitMask, never a negative half -- unlike the signed
      // case just below, so no min/max split is needed here.
      if (value < 0 || value > shape.ValueBitMask)
      {
        return (null, $"line {lineNumber}: {value} does not fit in \"{shape.Mnemonic}\"'s unsigned value (0..{shape.ValueBitMask}).");
      }

      return (shape.Tag | (value & shape.ValueBitMask), null);
    }

    int maxValue = shape.ValueBitMask >> 1;
    int minValue = -(maxValue + 1);
    if (value < minValue || value > maxValue)
    {
      return (null, $"line {lineNumber}: {value} does not fit in \"{shape.Mnemonic}\"'s signed value ({minValue}..{maxValue}).");
    }

    return (shape.Tag | (value & shape.ValueBitMask), null);
  }

  /// <summary>
  /// Parses CVM assembly source text into <see cref="CvmAsmInstruction"/>s ready for
  /// <see cref="Assemble"/>: one mnemonic per line, optionally followed by a "0x"-prefixed hex or
  /// plain decimal operand; blank lines and ";" or "//" line comments are ignored. This is purely
  /// textual -- it does not know or care whether a mnemonic actually resolves against node 607's
  /// current compile, that check happens in <see cref="Assemble"/>.
  /// </summary>
  public static (List<CvmAsmInstruction>? Instructions, string? Error) ParseSource(string source)
  {
    var instructions = new List<CvmAsmInstruction>();
    string[] lines = source.Replace("\r\n", "\n").Split('\n');
    for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
    {
      string original = lines[lineNumber];
      string line = StripComment(original).Trim();
      if (line.Length == 0)
      {
        continue;
      }

      string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length == 1)
      {
        instructions.Add(new CvmAsmInstruction(parts[0], null));
        continue;
      }

      if (parts.Length == 2 && TryParseOperand(parts[1], out int operand))
      {
        instructions.Add(new CvmAsmInstruction(parts[0], operand));
        continue;
      }

      return (null, $"line {lineNumber + 1}: could not parse \"{original.Trim()}\".");
    }

    return (instructions, null);
  }

  private static string StripComment(string line)
  {
    int semicolon = line.IndexOf(';');
    int slashSlash = line.IndexOf("//", StringComparison.Ordinal);
    int cut = semicolon < 0 ? slashSlash : (slashSlash < 0 ? semicolon : Math.Min(semicolon, slashSlash));
    return cut < 0 ? line : line[..cut];
  }

  // Handles a leading '-' before EITHER a "0x"-prefixed hex magnitude or a plain decimal one -- the
  // decimal case alone would already parse via NumberStyles.Integer's own AllowLeadingSign, but hex
  // needs this to support a negative literal at all (needed for br/ifbr/slit operands, e.g. "-0x400").
  private static bool TryParseOperand(string text, out int value)
  {
    if (text.StartsWith('-') && TryParseOperand(text[1..], out int magnitude))
    {
      value = -magnitude;
      return true;
    }

    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
  }
}