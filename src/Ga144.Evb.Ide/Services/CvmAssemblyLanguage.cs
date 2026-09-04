using System.Globalization;
using Ga144.Cvm.Toolchain;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Cvm;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// The CVM's own small assembly language: Stefan's mnemonics (<c>nop</c>, <c>pushlit &lt;data&gt;</c>,
/// <c>push</c>, <c>pop</c>, <c>ret</c>, <c>halt</c> -- CVM2's node 507 "local execute" primitives)
/// layered on top of a tagged wire-level opcode convention (opcode = tag | wordAddress) -- see
/// <see cref="Node508TagBits"/>/<see cref="Node507Cvm2LocalExecuteTagBits"/>'s own remarks. (<c>call</c>,
/// <c>br</c>, <c>ifbr</c>, <c>slit</c>, and node 606's frame-pointer ops (<c>enter</c>, <c>adjust</c>,
/// <c>stl</c>, <c>stp</c>, <c>ldl</c>, <c>ldp</c>, <c>lal</c>, <c>lap</c>) are the exceptions -- see this
/// class's own remarks on why they aren't part of this tagged-opcode layer.)
///
/// <b>CVM2 (2026-09-01).</b> Stefan is rewriting the whole CVM around new, differently-numbered nodes
/// and a more sophisticated inter-node communication scheme; CVM1's nodes are not used in CVM2 at all.
/// <c>nop</c>/<c>pushlit</c>/<c>push</c>/<c>pop</c>/<c>ret</c>/<c>halt</c> are the six CVM1 mnemonics
/// CVM2's own node 507 (the ENTIRE CPU) happens to still implement, under matching tick-labels ('nop,
/// 'plit, 'push, 'pop, 'ret, 'halt) -- per Stefan's own "only update existing opcodes where possible"
/// rule, these six were repointed to node 507's implementation
/// (<see cref="Node507Cvm2LocalExecuteTagBits"/>) rather than added as new entries. CVM2 node 507's other
/// four tick-labeled words ('tjmp, 'jump, 'xs, 'xp) have no existing CVM1 mnemonic and are deliberately
/// NOT wired into this file yet.
///
/// <b>Node 507, not 508 -- corrected 2026-09-01.</b> These six primitives were briefly pointed at node
/// 508 in this project's own session history, under the mistaken belief that 508 was CVM2's CPU node.
/// It is not; 507 is, and 508 is explicitly unused for now (Stefan: "node 508 must be ignored for
/// now") -- see <see cref="Node508Program"/>'s own remarks. All six entries below now resolve against
/// <see cref="Node507Program.Coordinate"/>.
///
/// <b>CVM1 leftover NODES removed (2026-09-01, per Stefan's own request).</b> <c>Node606Program.cs</c>
/// (plus its <c>.f18</c> mirror file) is DELETED -- it is not part of CVM2's mesh
/// (708/707/607/507, plus 407/506 -- see below) at all, so keeping a dedicated resident-source file for
/// it served no purpose once CVM2 replaced CVM1 wholesale. Its own mnemonic -- node 606's <c>leave</c>
/// (now repointed, see below) -- STAYS in <see cref="CvmInstructionSet"/>'s own opcode table (per "do
/// not remove any opcodes"). Node 507's own eleven CVM1-era ALU-op mnemonics (usl/ssr/usr/add/sub/and/
/// xor/or/inv/inc/dec) are permanently orphaned the same way: node 507's REAL CVM2 source (the CPU,
/// above) does not define them either, and the physical coordinate 507 is now something else entirely.
///
/// <b><c>Node407Program.cs</c> is back -- a BRAND NEW, unrelated CVM2 file (2026-09-02).</b> The
/// original CVM1 node 407 (register-w/port ops xpt/out/in/ldhi/ldlo/sthi/stlo) was deleted 2026-09-01;
/// those seven mnemonics stay permanently orphaned, with NO <see cref="NodeSymbolByMnemonic"/> entry.
/// The coordinate 407 was then reused for a completely different CVM2 node -- Stefan's long-call/
/// long-jump helper, reached from node 507's own <c>m/main</c> dispatch once the memory layout grew
/// past <c>call</c>'s own 15-bit reach -- and that new node DOES have two entries below,
/// <see cref="CvmInstructionSet.LongCallMnemonic"/>/<see cref="CvmInstructionSet.LongJumpMnemonic"/>,
/// pointed at <see cref="Node407Program.Coordinate"/> with the new <see cref="Node407LongCallTagBits"/>
/// tag -- see that constant's own remarks for the full derivation.
///
/// <b><c>Node506Program.cs</c> is ALSO back -- another BRAND NEW, unrelated CVM2 file (2026-09-02).</b>
/// The original CVM1 node 506 (register-d/extended-precision ops zext/addc/ldd/std/xd/mul2d/div2d/sext/
/// umuld) was deleted 2026-09-01 right alongside 606 above; those nine mnemonics stay permanently
/// orphaned, with NO <see cref="NodeSymbolByMnemonic"/> entry. The coordinate 506 was then reused for
/// CVM2's stack-frame node (enter/leave/load-local/load-parameter/store-local/store-parameter), reached
/// from node 507's own <c>m/main</c> dispatch via its RIGHT port (<c>r---</c>). Per Stefan (2026-09-02,
/// "give me now enter and leave mnemonics"), only <c>enter</c> (repointed from CVM1's node 606 --
/// <see cref="CvmInstructionSet.Node506EnterTag"/>) and <c>leave</c> (repointed here, below, from CVM1's
/// node 606 to node 506's own <c>'leave</c> -- see <see cref="Node506LeaveTagBits"/>'s own remarks) are
/// wired in so far; node 506's own load-local/load-parameter/store-local/store-parameter, and its own
/// further relay to node 505 ("call node 505" in its own dispatch cascade), are NOT wired in yet -- see
/// <see cref="Node506Program"/>'s own remarks for the full derivation and the still-open items.
///
/// <b>What's still orphaned, not removed.</b> Node 508's OLD CVM1 27 comparison/arithmetic mnemonics
/// (<c>eq</c> through <c>bitcnt</c>) keep their <see cref="NodeSymbolByMnemonic"/> entries, still
/// pointing at <see cref="Node508Program.Coordinate"/> and <see cref="Node508TagBits"/>, per "do not
/// remove any opcodes" -- they simply never resolve, since node 508 currently has no real source at
/// all (see <see cref="Node508Program"/>'s own remarks: not defined yet, deliberately excluded from
/// CVM2's active mesh).
///
/// This is deliberately a SEPARATE naming layer from any node's own F18 source symbols ('nop, 'plit,
/// 'push, 'pop, 'ret, 'halt, plus 'tjmp/'jump/'xs/'xp, on CVM2's node 507 -- see
/// <see cref="Node507Program"/>'s own remarks) -- those tick-names are each node's own interpreter
/// labels and won't change; the mnemonics here are what a person reads and writes, and the two are free
/// to diverge (as pushlit already has from 'plit).
///
/// The mnemonic/word-length/operand-arity SHAPE of each instruction lives in the standalone
/// Ga144.Cvm.Toolchain project's <see cref="CvmInstructionSet"/> (shared with the freestanding
/// gaasm/galib/galink command-line tools, so both sides of the toolchain agree on what the
/// instruction set even is); this file's own job is pairing each of those shapes with the SPECIFIC
/// node that implements it and that node's own F18 symbol, which only makes sense against a live IDE
/// compile and has no business in that shared, IDE-independent project. A mnemonic is no longer
/// assumed to live on any one fixed node -- <see cref="NodeSymbolByMnemonic"/> below records, per
/// mnemonic, which node's compile to resolve it against, so <c>nop</c>/<c>pushlit</c>/<c>push</c>/
/// <c>pop</c>/<c>ret</c>/<c>halt</c> resolve against CVM2's node 507 (see the CVM2 remarks above), and
/// CVM1's OLD node 508 27 comparison/arithmetic ops (<c>eq</c> through <c>bitcnt</c>) stay pointed at
/// node 508, permanently orphaned as described above.
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
///
/// Both directions -- <see cref="BuildDecodeTable"/> for disassembly and <see cref="BuildEncodeTable"/>/
/// <see cref="Assemble"/> for assembly -- are built from the single <see cref="Instructions"/> table,
/// so they can never drift apart: adding a new TAGGED opcode to <see cref="CvmInstructionSet"/> plus
/// one line here (the node and F18 symbol it resolves to) is the only change either direction needs.
/// Each is also resolved against WHICHEVER of that mnemonic's own node happens to be present in the
/// caller's <c>compiledRam</c> -- while the standalone (no-chip-connected) path compiles only the
/// specific nodes <see cref="ViewModels.CvmDebuggerViewModel"/> asks for (just node 507 today -- see
/// <see cref="ViewModels.CvmDebuggerViewModel"/>'s own remarks).
/// </summary>
internal static class CvmAssemblyLanguage
{
  public const string NopMnemonic = CvmInstructionSet.NopMnemonic;
  public const string PushLitMnemonic = CvmInstructionSet.PushLitMnemonic;
  public const string PushMnemonic = CvmInstructionSet.PushMnemonic;
  public const string PopMnemonic = CvmInstructionSet.PopMnemonic;
  public const string RetMnemonic = CvmInstructionSet.RetMnemonic;

  // Node 508's own tag for its OLD CVM1 27 comparison/arithmetic ops (eq through bitcnt): the
  // "register t" opcode class CVM1's old node 507 used to forward wholesale to node 508, confirmed in
  // this project's own cvm-toolchain-design.md as 0xE800-0xEFFF. Kept, unreferenced by anything except
  // those 27 now-permanently-orphaned NodeSymbolByMnemonic entries below (node 508 has no real CVM2
  // source at all -- see Node508Program's own remarks -- but the entries themselves stay per "do not
  // remove any opcodes" -- see this class's own remarks). Node 606/506/407's own former tag constants
  // (0xA000 node 606, 0xE000 node 506, 0xF000 node 407) were removed on 2026-09-01 along with those
  // three nodes' own Node*Program.cs/​.f18 files -- see this class's own remarks on the CVM1 leftover
  // NODE removal (not an opcode removal).
  private const int Node508TagBits = 0xE800;

  // Node 507's CVM2 tag for its six "local execute" primitives (nop, plit/pushlit, push, pop, ret,
  // halt -- the six of CVM2 node 507's ten tick-labeled opcodes that match an existing CVM1 mnemonic,
  // per Stefan's own "only update existing opcodes where possible" rule; 'tjmp/'jump/'xs/'xp are new
  // and deliberately NOT wired in here yet). DERIVED, NOT YET CONFIRMED WITH STEFAN: node 507's own
  // m/main dispatch (Node507Program.Source) tests the fetched word's top bits in a cascade whose own
  // inline comments spell out the cumulative prefix at each step -- "11??" down-port, "101?" left-port,
  // "100?" (unconditional) right-port, then within the remaining "1000_????" quarter, "1000_1???" is
  // explicitly commented "local execute" (drop >r ; -- jump directly to the address in the low bits)
  // and "1000_0???" falls through to "branch relative" instead. "1000_1???_????_????" as a top-5-bit
  // pattern is 0x8800, i.e. opcode = 0x8800 | wordAddress -- corroborated by 'halt's own body literally
  // writing the same 0x8800 constant to port b (dup xor dup inv !b !b 0x8800 !b ;), suggesting 0x8800
  // is a meaningful constant elsewhere in this exact system, not a coincidence. This has NOT been
  // verified against real hardware or confirmed with Stefan -- only compiled standalone via a harness
  // (0 errors, all ten tick-labeled symbols resolve to real addresses). Treat as a strong hypothesis,
  // not a settled fact, until Stefan confirms it. Renamed from Node508Cvm2LocalExecuteTagBits on
  // 2026-09-01 when it turned out node 507, not 508, is CVM2's real CPU -- see Node507Program's own
  // remarks and this class's own remarks above.
  private const int Node507Cvm2LocalExecuteTagBits = 0x8800;

  // CVM2's long call/long jump tag (2026-09-02), per Stefan's node 407 source and his own explanation
  // of node 407's b/main dispatch cascade and the x/y relay protocol handing off from node 507's own
  // m/main (confirmed correct by Stefan -- see Cvm.Node407Program's own remarks for the full
  // derivation): node 507 hands off to node 407 once a fetched CVM opcode word's top bits read "11??",
  // and node 407's own cascade consumes two more bits before falling to "ex" (which jumps to whatever
  // address is already in R -- 'lcall's or 'ljmp's own address on node 407, loaded there by whoever
  // dispatches in) for the "1100" case -- so 'lcall/'ljmp's own CVM opcode word always has its top 4
  // bits "1100", i.e. tag 0xC000 OR'd with the local address on node 407, the same "tag | local
  // address" scheme Node507Cvm2LocalExecuteTagBits already uses (just a different tag/node pair). The
  // far call/jump TARGET address itself is not in this tag word at all -- it's the TrailingWord operand
  // (CvmInstructionSet.CvmOperandEncoding.TrailingWord), read by 'lcall/'ljmp via node 507's own m/next
  // once running on node 407.
  private const int Node407LongCallTagBits = 0xC000;

  // CVM2's node 506 'leave tag (2026-09-02), per Stefan's node 506 source (Cvm.Node506Program): its own
  // f/main dispatch cascade falls to "ex" (jump to whatever address is in R) once the fetched CVM
  // opcode word's top 7 bits read "1001_000" -- the same "tag | local address" scheme
  // Node407LongCallTagBits/Node507Cvm2LocalExecuteTagBits already use, just with a 7-bit tag/9-bit
  // address split instead of a 4-bit or 5-bit one. As a plain 16-bit tag word (address bits zeroed)
  // this is 0x9000 -- see CvmInstructionSet.Node506EnterTag's own remarks for the full bit derivation.
  //
  // KNOWN, DELIBERATE collision with br (2026-09-02): 0x9000 is also CvmInstructionSet.BranchTag's own
  // value, and BranchTag's mask covers this tag's entire range (0x9000-0x97FF). Per Stefan: "ignore the
  // ranges of br/ifbr. ignore the overlapping ranges. give me now enter and leave mnemonics." Not
  // resolved -- accepted for now, same as CvmInstructionSet.Node506EnterTag's own collision with br.
  // Unlike enter (self-describing, so br's OWN check silently wins during disassembly -- see
  // CvmInstructionSet.TryDescribeSelfDecodingWord's own remarks), leave is a TAGGED mnemonic resolved
  // only through THIS file's own BuildDecodeTable/BuildEncodeTable against a live compile, which never
  // consults br/ifbr at all -- so leave's own encode/decode through this file is unaffected by the
  // collision; it only matters if the very same 0x9038-shaped word is ever fetched as a plain word and
  // run through CvmInstructionSet.TryDescribeSelfDecodingWord first (which will report "br" instead).
  private const int Node506LeaveTagBits = 0x9000;

  // Which node implements each shared-toolchain mnemonic, that node's own F18 symbol for it, and the
  // tag bits its opcode word must carry (Node508TagBits for the OLD, permanently-orphaned CVM1
  // comparison ops; Node507Cvm2LocalExecuteTagBits for CVM2's own six repointed primitives -- these
  // are NOT the same value, and now live on two DIFFERENT physical coordinates, 508 and 507
  // respectively). Every TAGGED mnemonic in CvmInstructionSet.Instructions that still has a live node
  // to resolve against needs an entry here, or BuildDecodeTable/BuildEncodeTable simply won't find it
  // in a live compile -- this is the one place that link, kept as a small, easy-to-audit map rather
  // than folded back into the shared table (which has no notion of "node", "F18 symbol", or "tag" at
  // all, on purpose: gaasm never needs any of them). A mnemonic whose own node was one of the CVM1
  // leftovers removed 2026-09-01 (606, plus node 407's and 506's OLD register-w/port and register-d ops
  // -- see this class's own remarks) has NO entry here at all any more, not an entry pointing at a
  // deleted type -- BuildDecodeTable/BuildEncodeTable never look it up, and Instructions' own filter
  // drops it from the table entirely, the graceful-omission path this file already relies on for
  // call/br/ifbr/slit. Node 507 (CVM2's actual CPU), node 407 (CVM2's long-call/long-jump helper), node
  // 506 (CVM2's stack-frame node) -- both 407 and 506 different nodes than their deleted CVM1
  // namesakes, sharing only the coordinate -- and node 508 (permanently orphaned, not defined) are the
  // only coordinates anything here still points at.
  private static readonly IReadOnlyDictionary<string, (int NodeCoordinate, string SymbolName, int Tag)> NodeSymbolByMnemonic =
      new Dictionary<string, (int NodeCoordinate, string SymbolName, int Tag)>(StringComparer.OrdinalIgnoreCase)
      {
        // CVM2 (2026-09-01): nop/pushlit/push/pop/ret/halt resolve against node 507's own CVM2 "local
        // execute" dispatch -- see Node507Cvm2LocalExecuteTagBits's own remarks for the derivation (not
        // yet confirmed with Stefan). Corrected 2026-09-01 from node 508 (a mistaken earlier attribution
        // in this project's own session -- see this class's own remarks) to node 507, CVM2's real CPU.
        [NopMnemonic] = (Node507Program.Coordinate, "'nop", Node507Cvm2LocalExecuteTagBits),
        [PushLitMnemonic] = (Node507Program.Coordinate, "'plit", Node507Cvm2LocalExecuteTagBits),
        [PushMnemonic] = (Node507Program.Coordinate, "'push", Node507Cvm2LocalExecuteTagBits),
        [PopMnemonic] = (Node507Program.Coordinate, "'pop", Node507Cvm2LocalExecuteTagBits),
        [RetMnemonic] = (Node507Program.Coordinate, "'ret", Node507Cvm2LocalExecuteTagBits),
        [CvmInstructionSet.HaltMnemonic] = (Node507Program.Coordinate, "'halt", Node507Cvm2LocalExecuteTagBits),
        // CVM2's long call/long jump (2026-09-02) -- resolve against node 407's own live compile, tag
        // 0xC000 (Node407LongCallTagBits's own remarks). Unlike nop/pushlit/push/pop/ret/halt above,
        // these live on a DIFFERENT node than CVM2's CPU (507) -- BuildDecodeTable/BuildEncodeTable
        // already resolve each mnemonic against its own node independently, so this is just another
        // entry, not a special case.
        [CvmInstructionSet.LongCallMnemonic] = (Node407Program.Coordinate, "'lcall", Node407LongCallTagBits),
        [CvmInstructionSet.LongJumpMnemonic] = (Node407Program.Coordinate, "'ljmp", Node407LongCallTagBits),
        // CVM2's node 506 (2026-09-02) -- repointed from CVM1's node 606 ("only update existing opcodes
        // where possible"). Only 'leave so far, per Stefan's own explicit scope ("give me now enter and
        // leave mnemonics"); enter is a DIFFERENT (self-describing) shape and is wired directly in
        // CvmInstructionSet.Instructions instead (see Node506EnterTag's own remarks), not here. Node
        // 506's own load-local/load-parameter/store-local/store-parameter are not wired in yet.
        [CvmInstructionSet.LeaveMnemonic] = (Node506Program.Coordinate, "'leave", Node506LeaveTagBits),
        // CVM1's OLD 27 node-508 comparison/arithmetic ops -- permanently orphaned (node 508 has no
        // real CVM2 source at all -- not defined yet, deliberately excluded from CVM2's active mesh --
        // see Node508Program's own remarks), kept per "do not remove any opcodes." See this class's
        // own remarks.
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

  /// <summary>
  /// One parsed line of CVM assembly. <see cref="Label"/> is the name defined on this line (a bare
  /// "label:" line with nothing else has an empty <see cref="Mnemonic"/> and exists purely to mark
  /// the address of whatever comes next -- see <see cref="ParseSource"/>'s own remarks), never both
  /// this and <see cref="OperandLabel"/> at once. <see cref="Operand"/> is set when the line's
  /// operand (if any) was already a literal number; <see cref="OperandLabel"/> is set instead when
  /// it was a label reference still waiting to be resolved to a literal by
  /// <see cref="Assemble"/>'s own label pass -- never both.
  /// </summary>
  public sealed record CvmAsmInstruction(string Mnemonic, int? Operand, string? Label = null, string? OperandLabel = null);

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
  ///
  /// <b>Undefined-but-real opcodes assemble as 'nop, per Stefan (2026-09-01).</b> A mnemonic that IS a
  /// genuine, named CVM opcode (<see cref="CvmInstructionSet.TryGetShape"/> finds a shape for it -- the
  /// full 73-opcode table, not just this file's own resolvable subset) but currently has no live node
  /// to answer it -- every one of CVM1's now-orphaned mnemonics (the ALU ops including <c>inv</c>, node
  /// 606's <c>leave</c>, node 506/407's register ops, and CVM1's old node 508 comparison ops) -- is
  /// substituted with node 507's own current <c>'nop</c> opcode instead of failing the whole assemble:
  /// "these opcodes have not been defined yet and no longer have a meaning ... all undefined opcodes
  /// should generate a nop." Any operand supplied on that line is simply discarded (nop takes none).
  /// This only degrades gracefully for opcodes CvmInstructionSet actually knows about -- a genuinely
  /// unrecognized token (a typo, not a real CVM mnemonic at all) still fails the assemble below, since
  /// that is a different problem than "not implemented yet." Returns a null word list with a
  /// 1-based-line error message (never throws) when a mnemonic isn't recognized at all, node 507's own
  /// 'nop can't be resolved either (nothing to substitute with), an operand is missing where one is
  /// required or out of range, a label operand is undefined or unsupported for that mnemonic, or an
  /// operand is supplied where none is allowed. This is what
  /// <see cref="CvmDebugSession.AssembleAndLoadProgram"/> uses to turn the CVM Debugger's own
  /// Assembly Code editor into a program loaded straight into the simulated SRAM.
  ///
  /// <b>Labels (2026-09-02, per Stefan).</b> Unlike the freestanding <c>gaasm</c>/
  /// <see cref="CvmAssembler"/>, there are still no sections, imports, or an object file here -- this
  /// assembles one flat, immediately-loaded program, and every label is local to that one program --
  /// but a label name (defined with "name:", optionally sharing its line with an instruction, e.g.
  /// "loop: nop", or standing alone) IS supported as an operand wherever a literal number was already
  /// accepted, resolved by address before any mnemonic-specific encoding happens below. Resolution is
  /// a simple two-pass scheme entirely local to this one call: <see cref="CollectLabelAddresses"/>
  /// (pass 1) walks every instruction once to learn each label's address -- a forward reference (using
  /// a label before its own "name:" line, the normal case for a loop or a subroutine placed after its
  /// caller) works exactly the same as a backward one -- then this method's own loop (pass 2) resolves
  /// each operand label via <see cref="ResolveOperandLabel"/> before falling into the exact same
  /// literal-operand encoding path a hand-typed number would have used, so every existing range/arity
  /// check below applies unchanged either way. <c>call</c> and every tagged mnemonic (<c>pushlit</c>,
  /// plus <c>slit</c>) resolve a label to its own ABSOLUTE word address; <c>br</c>/<c>ifbr</c> resolve
  /// to a signed RELATIVE offset instead, since that is what their own opcode word actually encodes
  /// (see <see cref="ResolveOperandLabel"/>'s own remarks); node 606's eight
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/> ops don't accept a label
  /// operand at all, since their value is a frame-relative slot index/count, never an address.
  /// </summary>
  public static (List<int>? Words, string? Error) Assemble(
      IReadOnlyList<CvmAsmInstruction> instructions,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand)> encodeTable = BuildEncodeTable(compiledRam);

    (IReadOnlyDictionary<string, int>? labelAddresses, string? labelError) = CollectLabelAddresses(instructions, encodeTable);
    if (labelAddresses is null)
    {
      return (null, labelError);
    }

    var words = new List<int>();
    for (int line = 0; line < instructions.Count; line++)
    {
      CvmAsmInstruction instruction = instructions[line];
      if (instruction.Mnemonic.Length == 0)
      {
        continue; // a bare "label:" line -- nothing of its own to assemble.
      }

      if (instruction.OperandLabel is not null)
      {
        (int? resolvedOperand, string? resolveError) = ResolveOperandLabel(instruction, words.Count, labelAddresses, line + 1);
        if (resolveError is not null)
        {
          return (null, resolveError);
        }

        instruction = instruction with { Operand = resolvedOperand };
      }

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
        if (selfDescribingShape is not null)
        {
          // A genuine CVM opcode (CvmInstructionSet knows its shape) that just has no live node to
          // answer it right now -- per Stefan, substitute node 507's own current 'nop opcode rather
          // than failing the whole assemble. See this method's own remarks.
          if (!encodeTable.TryGetValue(NopMnemonic, out (int Opcode, int WordLength, bool HasOperand) nopEntry))
          {
            return (null, $"line {line + 1}: \"{instruction.Mnemonic}\" has no defined opcode yet, and could not be " +
                $"substituted with \"{NopMnemonic}\" because node 507's current compile doesn't define \"'nop\" either.");
          }

          words.Add(nopEntry.Opcode);
          continue;
        }

        // A mnemonic that isn't a recognized CVM opcode at all -- a typo, not "not implemented yet" --
        // still fails outright rather than silently becoming a nop.
        return (null, $"line {line + 1}: \"{instruction.Mnemonic}\" is not a known CVM asm mnemonic.");
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
  /// Pass 1 of resolving labels for <see cref="Assemble"/>: walks every instruction once, in the SAME
  /// order pass 2 (<see cref="Assemble"/>'s own loop) will emit words in, tracking the running word
  /// address so each label's address is known before any operand is resolved -- a forward reference
  /// (a "call"/"br" written before the "name:" line it names) is the normal, common case for a loop or
  /// a subroutine placed after its own caller, so labels cannot be resolved in a single pass. Word
  /// length per line comes from <see cref="GetWordLength"/>, never from actually encoding the line, so
  /// this pass needs no operand resolved yet. Returns a null map with a 1-based-line error only for a
  /// genuine duplicate label definition; an undefined or unsupported label OPERAND is a pass-2 concern
  /// instead (see <see cref="ResolveOperandLabel"/>'s own remarks), since only pass 2 knows which
  /// mnemonic is asking for it. Label names are matched case-insensitively, like every mnemonic in
  /// this file.
  /// </summary>
  private static (IReadOnlyDictionary<string, int>? Labels, string? Error) CollectLabelAddresses(
      IReadOnlyList<CvmAsmInstruction> instructions,
      IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand)> encodeTable)
  {
    var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    int address = 0;
    for (int line = 0; line < instructions.Count; line++)
    {
      CvmAsmInstruction instruction = instructions[line];
      if (instruction.Label is not null && !labels.TryAdd(instruction.Label, address))
      {
        return (null, $"line {line + 1}: label \"{instruction.Label}\" is already defined.");
      }

      if (instruction.Mnemonic.Length > 0)
      {
        address += GetWordLength(instruction.Mnemonic, encodeTable);
      }
    }

    return (labels, null);
  }

  /// <summary>
  /// How many words <paramref name="mnemonic"/> occupies once assembled -- used by
  /// <see cref="CollectLabelAddresses"/> to compute label addresses BEFORE any operand (literal or
  /// label) is resolved, since word length never depends on the operand's actual value. Mirrors
  /// exactly what <see cref="Assemble"/>'s own pass 2 will actually emit for the same mnemonic, so the
  /// two passes can never disagree on an address: a self-describing shape (<c>call</c>/<c>br</c>/
  /// <c>ifbr</c>/<c>slit</c>/node 606's ops) is always its own
  /// <see cref="CvmInstructionSet.CvmInstructionShape.WordLength"/>; a tagged mnemonic resolves through
  /// <paramref name="encodeTable"/> the same way pass 2 does; anything else -- a genuine opcode with no
  /// live node to answer it, which pass 2's own "undefined opcode -&gt; nop" substitution (see
  /// <see cref="Assemble"/>'s own remarks) always collapses to exactly one word regardless of the
  /// substituted opcode's real shape, or an outright unrecognized mnemonic pass 2 will reject outright
  /// -- is 1, a safe placeholder that never needs to be exact since pass 2 either matches it anyway or
  /// fails that very line before any address past it is ever used.
  /// </summary>
  private static int GetWordLength(
      string mnemonic,
      IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand)> encodeTable)
  {
    CvmInstructionSet.CvmInstructionShape? selfDescribingShape = CvmInstructionSet.TryGetShape(mnemonic);
    if (selfDescribingShape is { Encoding: CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress or CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue or CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue })
    {
      return selfDescribingShape.WordLength;
    }

    return encodeTable.TryGetValue(mnemonic, out (int Opcode, int WordLength, bool HasOperand) entry) ? entry.WordLength : 1;
  }

  /// <summary>
  /// Turns one instruction's <see cref="CvmAsmInstruction.OperandLabel"/> into the literal value
  /// <see cref="Assemble"/>'s own existing per-mnemonic encode logic already knows how to validate and
  /// pack, so that logic runs completely unchanged whether the source said a number or a label name.
  /// <c>call</c> (an <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/>) and every
  /// tagged mnemonic resolved via <paramref name="labelAddresses"/> alone (<c>pushlit</c>, the only one
  /// with a trailing-word operand today) resolve to the label's own ABSOLUTE word address -- so does
  /// <c>slit</c>, even though it's an <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>
  /// like <c>br</c>/<c>ifbr</c> below: loading a label's own address as a small signed literal is a
  /// legitimate use, even though that encoding's value isn't inherently an address (see its own
  /// remarks). <c>br</c>/<c>ifbr</c> resolve to a signed RELATIVE offset instead, per
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>'s own remarks confirmed
  /// against real hardware: the target is relative to the address of the word immediately AFTER the
  /// branch's own opcode word, i.e. <c>labelAddress - (instructionAddress + 1)</c>, not the branch
  /// word's own address. Node 606's eight <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/>
  /// ops reject a label operand outright with a clear error -- their value is a frame-relative slot
  /// index/count, never an address, so resolving one would just be silently wrong. Range/arity
  /// validation of the resolved value itself still happens downstream exactly as it would for a
  /// hand-typed literal -- this method only turns a name into a number, never validates its range.
  /// </summary>
  private static (int? Operand, string? Error) ResolveOperandLabel(
      CvmAsmInstruction instruction,
      int instructionAddress,
      IReadOnlyDictionary<string, int> labelAddresses,
      int lineNumber)
  {
    if (!labelAddresses.TryGetValue(instruction.OperandLabel!, out int labelAddress))
    {
      return (null, $"line {lineNumber}: \"{instruction.Mnemonic}\" references undefined label \"{instruction.OperandLabel}\".");
    }

    CvmInstructionSet.CvmInstructionShape? shape = CvmInstructionSet.TryGetShape(instruction.Mnemonic);
    if (shape is { Encoding: CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue })
    {
      return (null, $"line {lineNumber}: \"{instruction.Mnemonic}\" does not support a label operand -- its value is a frame-relative slot index/count, not an address; supply a literal 0..{shape.ValueBitMask} value instead.");
    }

    bool isRelativeBranch =
        string.Equals(instruction.Mnemonic, CvmInstructionSet.BranchMnemonic, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(instruction.Mnemonic, CvmInstructionSet.ConditionalBranchMnemonic, StringComparison.OrdinalIgnoreCase);
    return isRelativeBranch ? (labelAddress - (instructionAddress + 1), null) : (labelAddress, null);
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
  /// <see cref="CvmObjectFile"/>, deferred all the way to a not-yet-implemented linker, which has no
  /// place in this simpler, immediately-loaded assembler -- this file's own label support (see
  /// <see cref="Assemble"/>'s own remarks) resolves a label to a plain literal <c>int</c> BEFORE this
  /// method is ever called, so from here a label-derived operand and a hand-typed one are
  /// indistinguishable.
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
  /// plain decimal operand OR a label name (see below); blank lines and ";" or "//" line comments are
  /// ignored. This is purely textual -- it does not know or care whether a mnemonic actually resolves
  /// against a live node's current compile (that's <see cref="Assemble"/>'s job) or whether a label
  /// name it records here is ever actually defined anywhere (also <see cref="Assemble"/>'s job, via
  /// <see cref="CollectLabelAddresses"/>) -- this method only tells the two apart syntactically.
  ///
  /// <b>Labels.</b> A line may start with "name:" (an identifier -- a letter or underscore, then any
  /// mix of letters/digits/underscores -- immediately followed by a colon), either on its own (marking
  /// the address of whatever instruction comes next) or immediately followed by that instruction on
  /// the same line, e.g. "loop: nop". A candidate before ':' that isn't a valid identifier (starts
  /// with a digit, e.g. a stray "0x12:") is left alone and the whole line is parsed as an ordinary
  /// instruction instead, same as before labels existed. Once a line's optional label prefix is
  /// stripped, its second token -- if not "0x"-hex or plain decimal -- is recorded as a label
  /// OPERAND reference (<see cref="CvmAsmInstruction.OperandLabel"/>) when it's itself a valid
  /// identifier, rather than an immediate parse failure; whether that label actually exists, and
  /// whether the mnemonic in question even accepts a label there, is resolved later in
  /// <see cref="Assemble"/>.
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

      string? label = null;
      if (TryParseLabelPrefix(line, out string labelCandidate, out string remainder))
      {
        label = labelCandidate;
        line = remainder;
        if (line.Length == 0)
        {
          // A bare "label:" line with no instruction of its own -- see CollectLabelAddresses's own
          // remarks for how this marks the address of whatever comes next.
          instructions.Add(new CvmAsmInstruction(string.Empty, null, label));
          continue;
        }
      }

      string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length == 1)
      {
        instructions.Add(new CvmAsmInstruction(parts[0], null, label));
        continue;
      }

      if (parts.Length == 2)
      {
        if (TryParseOperand(parts[1], out int operand))
        {
          instructions.Add(new CvmAsmInstruction(parts[0], operand, label));
          continue;
        }

        if (IsValidIdentifier(parts[1]))
        {
          // Not a number -- a forward or backward reference to another line's label, resolved once
          // every label's address is known (see Assemble's own remarks).
          instructions.Add(new CvmAsmInstruction(parts[0], null, label, parts[1]));
          continue;
        }
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

  // A leading "name:" where "name" is a valid identifier (IsValidIdentifier) marks a label
  // definition -- returns the name and whatever follows the colon (trimmed, possibly empty for a
  // bare "label:" line). A colon that isn't preceded by a valid identifier (no colon at all, or the
  // text before it starts with a digit or contains a character an identifier can't) isn't a label at
  // all; the whole original line is handed back unchanged for ordinary instruction parsing.
  private static bool TryParseLabelPrefix(string line, out string label, out string remainder)
  {
    int colon = line.IndexOf(':');
    if (colon < 0)
    {
      label = string.Empty;
      remainder = line;
      return false;
    }

    string candidate = line[..colon].Trim();
    if (!IsValidIdentifier(candidate))
    {
      label = string.Empty;
      remainder = line;
      return false;
    }

    label = candidate;
    remainder = line[(colon + 1)..].Trim();
    return true;
  }

  // A label name (definition or operand reference): a letter or underscore, then any mix of
  // letters/digits/underscores -- deliberately cannot start with a digit, so it never collides with a
  // "0x..."/plain-decimal numeric operand or a stray "0x12:" that isn't meant as a label at all.
  private static bool IsValidIdentifier(string text)
  {
    if (text.Length == 0 || (!char.IsLetter(text[0]) && text[0] != '_'))
    {
      return false;
    }

    for (int i = 1; i < text.Length; i++)
    {
      if (!char.IsLetterOrDigit(text[i]) && text[i] != '_')
      {
        return false;
      }
    }

    return true;
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