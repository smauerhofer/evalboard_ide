namespace Ga144.Cvm.Toolchain;

/// <summary>
/// The CVM assembly language's own instruction set -- Stefan's mnemonics (<c>nop</c>,
/// <c>pushlit &lt;data&gt;</c>, <c>push</c>, <c>pop</c>, <c>call &lt;address&gt;</c>, <c>ret</c>,
/// <c>br &lt;offset&gt;</c>, <c>ifbr &lt;offset&gt;</c>, <c>slit &lt;value&gt;</c>, plus node 507's ALU
/// ops -- <c>usl</c>, <c>ssr</c>, <c>usr</c>, <c>add</c>, <c>sub</c>, <c>and</c>, <c>xor</c>, <c>or</c>
/// (binary: register r and the top of the CVM data stack), and <c>inv</c>, <c>inc</c>, <c>dec</c>
/// (unary: register r alone), plus node 606's frame-pointer-management ops -- <c>enter &lt;locals&gt;</c>,
/// <c>adjust &lt;offset&gt;</c>, <c>stl &lt;offset&gt;</c>, <c>stp &lt;offset&gt;</c>,
/// <c>ldl &lt;offset&gt;</c>, <c>ldp &lt;offset&gt;</c>, <c>lal &lt;offset&gt;</c>,
/// <c>lap &lt;offset&gt;</c> (each self-describing, an 8-bit tag OR'd with an 8-bit unsigned value), plus
/// node 606's ninth mnemonic <c>leave</c> and tenth mnemonic <c>halt</c> (both tagged mnemonics like
/// nop/push/pop/ret, NOT self-describing -- see <see cref="LeaveMnemonic"/>'s and
/// <see cref="HaltMnemonic"/>'s own remarks)), plus node 508's 27 comparison/arithmetic ops --
/// <c>eq</c>, <c>eq0</c>, <c>false</c>, <c>true</c>, <c>ne</c>, <c>ne0</c>, <c>ugt</c>, <c>gt</c>,
/// <c>gt0</c>, <c>ge</c>, <c>ge0</c>, <c>ule</c>, <c>le</c>, <c>le0</c>, <c>lt</c>, <c>lt0</c>,
/// <c>ult</c>, <c>uge</c>, <c>mul2</c>, <c>udiv2</c>, <c>div2</c>, <c>abs</c>, <c>negate</c>, <c>xt</c>,
/// <c>ldt</c>, <c>stt</c>, <c>bitcnt</c> (tagged mnemonics exactly like <c>leave</c>, resolved against
/// node 508's own live compile -- see <see cref="EqualMnemonic"/>'s own remarks)), plus node 506's nine
/// register-d/extended-precision ops -- <c>zext</c>, <c>addc</c>, <c>ldd</c>, <c>std</c>, <c>xd</c>,
/// <c>mul2d</c>, <c>div2d</c>, <c>sext</c>, <c>umuld</c> (tagged mnemonics exactly like <c>leave</c> and
/// node 508's 27 ops, resolved against node 506's own live compile -- see
/// <see cref="ZeroExtendMnemonic"/>'s own remarks)), plus node 407's seven register-w/port ops --
/// <c>xpt</c>, <c>out</c>, <c>in</c>, <c>ldhi</c>, <c>ldlo</c>, <c>sthi</c>, <c>stlo</c> (tagged
/// mnemonics exactly like node 506's and 508's ops, resolved against node 407's own live compile --
/// see <see cref="ExchangePortMnemonic"/>'s own remarks)), plus CVM2's <c>lcall</c>/<c>ljmp</c> (long
/// call/long jump, added 2026-09-02 -- shaped exactly like <c>pushlit</c>, resolved against node 407's
/// own live compile too, but a DIFFERENT tag -- see <see cref="LongCallMnemonic"/>'s own remarks)), plus
/// CVM2's <c>ldg</c>/<c>stg</c> (load global/store global, added 2026-09-04 -- shaped exactly like
/// <c>lcall</c>/<c>ljmp</c>, resolved against node 508's own live compile, its own DIFFERENT tag again --
/// see <see cref="LoadGlobalMnemonic"/>'s own remarks)), plus node 509's nine unary-arithmetic ops --
/// <c>abs</c>, <c>neg</c>, <c>inc</c>, <c>dec</c>, <c>inv</c>, <c>mul2</c>, <c>div2</c>, <c>udiv2</c>,
/// <c>bitcnt</c> (added 2026-09-05: eight of these REPOINT existing orphaned mnemonics -- <c>inv</c>/
/// <c>inc</c>/<c>dec</c> from node 507's old ALU-op family and <c>abs</c>/<c>mul2</c>/<c>div2</c>/
/// <c>udiv2</c>/<c>bitcnt</c> from node 508's old 27-op family -- to node 509's own live compile,
/// per "only update existing opcodes where possible"; only <c>neg</c> is a genuinely new mnemonic
/// (node 509's own <c>'neg</c> has no existing same-named counterpart -- <c>negate</c>, Id 51, stays a
/// separate, still-orphaned mnemonic). Tagged exactly like <c>leave</c>/node 508's/node 506's/node
/// 407's own ops, a DIFFERENT tag again -- see <see cref="NegMnemonic"/>'s own remarks), plus node
/// 509's tenth mnemonic <c>lit</c> (added 2026-09-05, per Stefan's own explicit follow-up: "add this
/// range to the cvm language ... mnemonic lit") -- self-describing, shaped exactly like <c>br</c>/
/// <c>ifbr</c>/<c>slit</c> (a fixed 6-bit tag OR'd with a 10-bit signed value), its own DIFFERENT tag
/// and field width again -- see <see cref="LitTag"/>'s own remarks), plus node 509's tenth and eleventh
/// tagged ops, <c>parity</c> and <c>odd</c> (added 2026-09-05, per Stefan's own follow-up: "I added 2
/// new opcodes to node 509. add them also to the language") -- both genuinely new (no existing orphaned
/// mnemonic of either name to repoint), tagged exactly like the original nine (see
/// <see cref="ParityMnemonic"/>'s own remarks), plus node 509's twelfth tagged op, <c>not</c> (added
/// 2026-09-05, per Stefan's own follow-up: "I added 'not' to node 509. please add it to assembler and
/// disassembler") -- also genuinely new, tagged the same way (see <see cref="NotMnemonic"/>'s own
/// remarks), plus node 406's twelve binary-arithmetic ops (added 2026-09-05, per Stefan's own node 406
/// source, "add these opcodes to assembler and disassembler and include this node in the boot stream") --
/// <c>add</c>/<c>sub</c>/<c>and</c>/<c>xor</c>/<c>or</c>/<c>usl</c>/<c>ssr</c>/<c>usr</c> REPOINT eight
/// of node 507's old, permanently-orphaned ALU-op mnemonics (per "only update existing opcodes where
/// possible"), while <c>rsb</c>/<c>rsl</c>/<c>rsr</c>/<c>rur</c> are genuinely new; EACH of these twelve
/// also gets a second, "i"-suffixed CVM mnemonic (<c>addi</c>/<c>subi</c>/<c>rsbi</c>/<c>andi</c>/
/// <c>xori</c>/<c>ori</c>/<c>rsli</c>/<c>usli</c>/<c>rsri</c>/<c>ssri</c>/<c>ruri</c>/<c>usri</c>) for
/// its own "constant in the next trailing word" form, per Stefan's own explicit naming rule ("for opcode
/// with constant parameter, add a i to the mnemonic. so 'add' becomes 'addi'") -- see
/// <see cref="ReverseSubtractMnemonic"/>'s and <see cref="AddConstantMnemonic"/>'s own remarks for the
/// full derivation, plus node 408's fourteen comparison ops (added 2026-09-06, per Stefan's own node 408
/// source, "add node 408 to the assembler, disassembler and boot stream", extended the same day with
/// <c>ge</c>/<c>gt</c>/<c>le</c>, "add more opcode to assembler and disassembler") -- <c>true</c>/
/// <c>false</c>/<c>eq</c>/<c>ne0</c>/<c>ne</c>/<c>eq0</c>/<c>lt0</c>/<c>ge0</c>/<c>le0</c>/<c>gt0</c>/
/// <c>lt</c>/<c>ge</c>/<c>gt</c>/<c>le</c> ALL FOURTEEN repoint existing, previously-orphaned mnemonics
/// from node 508's old CVM1 27-comparison-op family (per "only update existing opcodes where possible")
/// -- unlike every earlier node added this way, node 408 needed NO genuinely new mnemonic constants or
/// <see cref="Instructions"/> entries at all, either time, since every name it needed already existed in
/// this table -- see the IDE project's own
/// Services.CvmAssemblyLanguage.Node408UnaryComparisonTagBits/Node408BinaryComparisonTagBits for the tag
/// derivation), plus node 407's own long-branch op (added 2026-09-06, per Stefan's own follow-up node 407
/// source, "more opcodes to node 407 added", then narrowed the same day, "I remove clbr and cljmp from
/// node 407. remove it also from the CVM assembler and disassembler" -- see <see
/// cref="LongBranchMnemonic"/>'s own remarks for the full history) -- <c>lbr</c> -- a genuinely NEW
/// mnemonic, shaped exactly like <c>lcall</c>/<c>ljmp</c> (same node, same tag, just a different address),
/// and
/// for each, how
/// many words it occupies once assembled, how its
/// operand (if any) is encoded, and a stable numeric <see cref="CvmInstructionShape.Id"/>. This is the
/// SHAPE of each instruction only -- for the tagged-dispatch mnemonics
/// (<see cref="CvmOperandEncoding.None"/>/<see cref="CvmOperandEncoding.TrailingWord"/>: <c>nop</c>,
/// <c>pushlit</c>, <c>push</c>, <c>pop</c>, <c>ret</c>, and all eleven ALU ops above -- none of the ALU
/// ops takes an assembled operand, unary or binary alike, since their values come from register r and/or
/// the CVM data stack rather than the instruction word itself), never a real numeric opcode: a real
/// opcode there depends on which node(s) the CVM's primitives are actually compiled into (node 607 for
/// the first five; node 507, reached from 607's dispatch, for the eleven ALU ops -- each primitive
/// living in exactly one node, distinguished by opcode-value ranges) -- so resolving one of those
/// mnemonics to its real opcode is entirely the linker's job, once that per-node/range mapping exists.
/// This project deliberately never needs to know any node's F18 source to assemble a program:
/// <see cref="CvmAssembler"/> treats every tagged-dispatch mnemonic as an external symbol, portable
/// across however many nodes and whatever ranges end up implementing it.
/// The two self-describing encodings (<see cref="CvmOperandEncoding.EmbeddedAddress"/>,
/// <see cref="CvmOperandEncoding.EmbeddedSignedValue"/>) need no such resolution at all -- their whole
/// opcode word is fully known the moment the operand is, with no node/linker involvement.
///
/// This table is the single source of truth shared by <see cref="CvmAssembler"/> here and by the IDE
/// project's own disassembler (Ga144.Evb.Ide.Services.CvmAssemblyLanguage, which pairs each TAGGED
/// mnemonic with its own node's live F18 symbol -- 607, 507, 606, 508, 506, 407, and now 509 all have at
/// least one; 608/707 remain separate, later work; the self-describing mnemonics need no such pairing
/// and are recognized directly by <see cref="TryDescribeSelfDecodingWord"/> instead). Adding a new CVM
/// opcode is a one-line change here; the IDE project references this project specifically so both
/// sides of the toolchain can never drift apart on what the instruction set is.
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
  public const string SlitMnemonic = "slit";

  // Node 507's ALU ops, added per Stefan's node 507 source: eight binary ops (register r combined with
  // the top of the CVM data stack) and three unary ops (register r alone). None of the eleven takes an
  // assembled operand -- like nop/push/pop/ret, each is a single bare tagged opcode word; the operands
  // they act on already live in r and/or on the CVM data stack by the time the opcode runs.
  public const string UnsignedShiftLeftMnemonic = "usl";
  public const string SignedShiftRightMnemonic = "ssr";
  public const string UnsignedShiftRightMnemonic = "usr";
  public const string AddMnemonic = "add";
  public const string SubtractMnemonic = "sub";
  public const string AndMnemonic = "and";
  public const string XorMnemonic = "xor";
  public const string OrMnemonic = "or";
  public const string InvertMnemonic = "inv";
  public const string IncrementMnemonic = "inc";
  public const string DecrementMnemonic = "dec";

  // Node 606's frame-pointer-management ops, added per Stefan's node 606 source and its accompanying
  // bit-pattern table. Each is a single self-describing word (no node/linker resolution needed at all,
  // like call/br/ifbr/slit -- NOT like the tagged nop/push/pop/ret/ALU family above): a fixed 8-bit tag
  // (bits 15-8, pattern 1010_1nnn) OR'd with an UNSIGNED 8-bit offset/count (bits 7-0, 0x00-0xFF). This
  // is a genuinely different shape from br/ifbr/slit's EmbeddedSignedValue -- the table gives every one
  // of these an unsigned 0..0xFF range, never a signed one, so they use the new
  // CvmOperandEncoding.EmbeddedUnsignedValue instead. la/ld/st are node 606's own shared internal words
  // (each reached twice, once via "noff" for the local/negative-offset variant and once via "off" for
  // the parameter/positive-offset variant); the CVM mnemonic table gives the four resulting pairs their
  // own distinct names (stl/stp, ldl/ldp, lal/lap) rather than exposing "off"/"noff" as a separate CVM
  // concept.
  public const string EnterMnemonic = "enter";
  public const string AdjustMnemonic = "adjust";
  public const string StoreLocalMnemonic = "stl";
  public const string StoreParameterMnemonic = "stp";
  public const string LoadLocalMnemonic = "ldl";
  public const string LoadParameterMnemonic = "ldp";
  public const string LoadAddressOfLocalMnemonic = "lal";
  public const string LoadAddressOfParameterMnemonic = "lap";

  // stl/stp/ldl/ldp REPOINTED to CVM2's node 506 (2026-09-06), the same way 'leave already was
  // (2026-09-02, see the comment block just below) and 'enter (see Node506EnterTag's own remarks):
  // Stefan's revised node 506 source names these four branches of its own f/main dispatch explicitly
  // ("opcode stl 1001_101?...", "opcode stp 1001_100?...", "opcode ldl 1001_111?...", "opcode ldp
  // 1001_110?...") for the first time -- previously these same four branches existed in node 506's
  // source but carried no name, so they stayed unwired (see Cvm.Node506Program's own remarks). Still
  // self-describing (EmbeddedUnsignedValue, unchanged), but now under node 506's own 7-bit-tag/9-bit-value
  // split (Node506StoreLocalTag/Node506StoreParameterTag/Node506LoadLocalTag/Node506LoadParameterTag,
  // Node506FrameValueBitMask) rather than CVM1's old node-606 8-bit-tag/8-bit-value one
  // (StoreLocalTag/StoreParameterTag/LoadLocalTag/LoadParameterTag, kept but superseded, per "do not
  // remove any opcodes" -- see each superseded constant's own remarks). adjust/lal/lap remain
  // permanently orphaned -- node 506's own source has never named an equivalent for any of the three.

  // 'leave was originally node 606's ninth mnemonic (CVM1), shaped completely differently from the
  // eight self-describing ones just above: a TAGGED mnemonic, exactly like nop/pushlit/push/pop/ret on
  // node 607 -- a single bare opcode word (CvmOperandEncoding.None) whose real numeric value depends on
  // where 'leave ends up in its own node's compiled RAM, resolved only against a live compile (see the
  // IDE-side Ga144.Evb.Ide.Services.CvmAssemblyLanguage.NodeSymbolByMnemonic, not this project, which
  // never knows any node's F18 source).
  //
  // REPOINTED to CVM2's node 506 (2026-09-02), per "only update existing opcodes where possible":
  // node 506's own new stack-frame source (Cvm.Node506Program) defines its own 'leave, reached via its
  // own f/main dispatch cascade falling to "ex" once the fetched word's top 7 bits read "1001_000"
  // (tag 0x9000 | address on node 506) -- see that class's own remarks for the full derivation. This
  // tag is a KNOWN, DELIBERATE collision with the still-live BranchTag (also 0x9000, EmbeddedSignedValue)
  // -- per Stefan (2026-09-02): "ignore the ranges of br/ifbr. ignore the overlapping ranges. give me
  // now enter and leave mnemonics." br/ifbr have not been moved yet, so a word like 0x9038 currently
  // decodes as "br" (TryDescribeSelfDecodingWord checks self-describing shapes first) even though it is
  // also a valid 'leave opcode on node 506 -- this ambiguity is accepted for now, not a bug to silently
  // work around, and is expected to resolve once br/ifbr's own new tag range is chosen.
  public const string LeaveMnemonic = "leave";

  // 'halt, added by Stefan to node 606 ("@b // wait for a word that will never come" -- his own comment:
  // "'halt halts the CVM. only a reset of the chip can break this halt."), is the second named word
  // reached the same way as 'leave just above: same TAGGED shape (CvmOperandEncoding.None), same "1010
  // 0xxx xxxx xxxx" opcode class, resolved only against a live compile of node 606's own source, never
  // self-describing. It exists specifically to give a hand-written program an explicit, deliberate way
  // to stop node 607 dead (parked in an infinite @b wait) instead of running off the end of its own
  // linear layout into zero-filled RAM, which self-describes as "call 0" and silently restarts execution
  // from address 0 -- see CvmDebuggerDefaultProgram's own remarks for a real instance of that fall-
  // through hazard.
  public const string HaltMnemonic = "halt";

  // Node 508's comparison/arithmetic ops, added per Stefan's node 508 source and his own naming rule
  // for that message ("all words that begin with a ' are an opcode for the CVM with the mnemonic using
  // the same name without the leading '"). Every one of these 27 is shaped exactly like 'leave above
  // (and like node 607's own nop/push/pop/pushlit/ret, and node 507's eleven ALU ops) -- a single bare
  // TAGGED opcode word (CvmOperandEncoding.None), never self-describing: node 508's own 'main' receives
  // a dispatch address directly over the port and jumps straight to it ("A[ drop !p a !p ]] lit !b @b
  // >r @b ex"), so each named word's real opcode is simply node 508's own confirmed opcode-class tag
  // (0xE800-0xEFFF, "register t", per this project's own cvm-toolchain-design.md) OR'd with wherever
  // that word lands in node 508's own compiled RAM -- resolved only against a live compile, exactly
  // like 'leave, never self-describing like enter/adjust/stl/stp/ldl/ldp/lal/lap. None of the 27 takes
  // an assembled operand: every comparison/arithmetic op here acts on register r (already on the CVM
  // data stack by the time 'main dispatches to it) and, where relevant, a second value 507/607 relay
  // over the port -- never on a literal baked into the instruction word itself.
  //
  // FOURTEEN of these 27 (eq/eq0/false/true/ne/ne0/gt0/ge0/le0/lt/lt0/ge/gt/le) were REPOINTED to CVM2's
  // own node 408 (2026-09-06, the last three -- ge/gt/le -- added the same day in a follow-up "add more
  // opcode" revision), per "only update existing opcodes where possible" -- see the class-level remarks
  // above and the IDE project's own Services.CvmAssemblyLanguage for the full derivation. Five more --
  // mul2/udiv2/div2/abs/bitcnt -- were separately repointed to node 509; the remaining EIGHT -- ugt/ule/
  // ult/uge/negate/xt/ldt/stt -- remain permanently orphaned against node 508, which never defined any of
  // these 27 old F18 symbols in its own real CVM2 source.
  public const string EqualMnemonic = "eq";
  public const string EqualToZeroMnemonic = "eq0";
  public const string FalseMnemonic = "false";
  public const string TrueMnemonic = "true";
  public const string NotEqualMnemonic = "ne";
  public const string NotEqualToZeroMnemonic = "ne0";
  public const string UnsignedGreaterThanMnemonic = "ugt";
  public const string GreaterThanMnemonic = "gt";
  public const string GreaterThanZeroMnemonic = "gt0";
  public const string GreaterOrEqualMnemonic = "ge";
  public const string GreaterOrEqualToZeroMnemonic = "ge0";
  public const string UnsignedLessOrEqualMnemonic = "ule";
  public const string LessOrEqualMnemonic = "le";
  public const string LessOrEqualToZeroMnemonic = "le0";
  public const string LessThanMnemonic = "lt";
  public const string LessThanZeroMnemonic = "lt0";
  public const string UnsignedLessThanMnemonic = "ult";
  public const string UnsignedGreaterOrEqualMnemonic = "uge";
  public const string MultiplyByTwoMnemonic = "mul2";
  public const string UnsignedDivideByTwoMnemonic = "udiv2";
  public const string DivideByTwoMnemonic = "div2";
  public const string AbsoluteValueMnemonic = "abs";
  public const string NegateMnemonic = "negate";
  public const string ExchangeTMnemonic = "xt";
  public const string LoadTMnemonic = "ldt";
  public const string StoreTMnemonic = "stt";
  public const string BitCountMnemonic = "bitcnt";

  // Node 506's register-d/extended-precision ops, added per Stefan's node 506 source and the same
  // naming rule he gave for node 508 ("every word that begins with a ' is an opcode for the CVM with the
  // mnemonic using the same name without the leading '"). Every one of these nine is shaped exactly like
  // node 508's 27 ops (and 'leave, and node 607's own nop/push/pop/pushlit/ret) -- a single bare TAGGED
  // opcode word (CvmOperandEncoding.None), never self-describing: node 506's own 'main' receives a
  // dispatch address directly over the port and jumps straight to it ("A[ drop !p ]] lit !b @b >r ex"),
  // so each named word's real opcode is simply node 506's own confirmed opcode-class tag (0xE000-0xE7FF,
  // "register d", per this project's own cvm-toolchain-design.md) OR'd with wherever that word lands in
  // node 506's own compiled RAM, resolved only against a live compile, exactly like node 508's ops. None
  // of the nine takes an assembled operand: each acts on this node's own register d and/or node 507's
  // register r (already relayed across the port by the time 'main dispatches to it), never on a literal
  // baked into the instruction word itself.
  public const string ZeroExtendMnemonic = "zext";
  public const string AddWithCarryMnemonic = "addc";
  public const string LoadDMnemonic = "ldd";
  public const string StoreDMnemonic = "std";
  public const string ExchangeDMnemonic = "xd";
  public const string MultiplyByTwoDoubleMnemonic = "mul2d";
  public const string DivideByTwoDoubleMnemonic = "div2d";
  public const string SignExtendMnemonic = "sext";
  public const string UnsignedMultiplyDoubleMnemonic = "umuld";

  // Node 407's register-w/port ops, added per Stefan's node 407 source and the same naming rule he
  // gave for nodes 508/506 ("every word that begins with a ' is an opcode for the CVM with the
  // mnemonic using the same name without the leading '"). Every one of these seven is shaped exactly
  // like node 506's and 508's ops -- a single bare TAGGED opcode word (CvmOperandEncoding.None), never
  // self-describing: node 407's own 'main' receives a dispatch address directly over the port and jumps
  // straight to it ("A[ drop !p ]] lit !b @b >r ex"), so each named word's real opcode is simply node
  // 407's own confirmed opcode-class tag (0xF000-0xFFFF, "register w", per this project's own
  // cvm-toolchain-design.md) OR'd with wherever that word lands in node 407's own compiled RAM,
  // resolved only against a live compile, exactly like node 506's and 508's ops. None of the seven takes
  // an assembled operand: 'xpt/'out/'in act on node 407's own A (which holds a live port address on
  // this node, not a data value) and the plain F18A '@'/'!' opcodes; 'ldhi/'ldlo/'sthi/'stlo move an
  // 18-bit port value's two halves to and from node 507's register r, all via values already on the
  // stack or already relayed over the port by the time 'main dispatches to it -- never a literal baked
  // into the instruction word itself.
  public const string ExchangePortMnemonic = "xpt";
  public const string PortWriteMnemonic = "out";
  public const string PortReadMnemonic = "in";
  public const string LoadHighMnemonic = "ldhi";
  public const string LoadLowMnemonic = "ldlo";
  public const string StoreHighMnemonic = "sthi";
  public const string StoreLowMnemonic = "stlo";

  // CVM2's long call/long jump, added per Stefan's node 407 source (2026-09-02) and the memory-layout
  // change on node 507 that motivated it: page 0 now spans the FULL 0x0000-0xFFFF, so a function above
  // 0x7FFF no longer fits in call's own 15-bit EmbeddedAddress word (CallAddressMask). Both are shaped
  // EXACTLY like pushlit -- a single tagged opcode word (resolved against a live node's own compiled
  // symbol, never self-describing) followed by one trailing operand word -- so no new
  // CvmOperandEncoding case was needed, just two more TrailingWord entries pointed at a different node.
  // Per Stefan's own explanation of node 407's n/main dispatch cascade (renamed from b/main 2026-09-05 --
  // see Cvm.Node407Program's own remarks) ("the sequence 'ex ;' will call 'lcall and 'ljmp because their
  // address is already in R") and the x/y relay protocol between node 507 and node 407 (confirmed
  // correct by Stefan, 2026-09-02): node 507's m/main hands off to node 407 once the fetched CVM opcode
  // word's top bits read "11??", and node 407's own n/main cascade consumes two more bits before
  // falling to "ex" for the "1100" case -- so a CVM opcode word reaching 'lcall or
  // 'ljmp always has its top 4 bits "1100" (0xC000), the same "tag | local address" scheme node 507's
  // own local-execute already uses with 0x8800 (Node507Cvm2LocalExecuteTagBits, in the IDE project's own
  // Services.CvmAssemblyLanguage). The actual far-call/far-jump TARGET address is carried separately, in
  // the trailing word, read by 'lcall/'ljmp themselves via node 507's own m/next once running on node
  // 407 -- see Cvm.Node407Program's own remarks for the full derivation. lcall pushes a return address
  // (like call/m/call); ljmp does not (like a plain jump). CONFIRMED ON REAL HARDWARE (2026-09-02):
  // lcall's own opcode/operand pair (0xC01B 0x0007 in Stefan's test program), the return-address push,
  // the jump, and the matching 'ret pop/return all round-tripped correctly on a real EVB -- see
  // Cvm.Node407Program's own remarks for the transaction log.
  public const string LongCallMnemonic = "lcall";
  public const string LongJumpMnemonic = "ljmp";

  // Node 407's long-branch op, added 2026-09-06 per Stefan's own follow-up node 407 source ("more opcodes
  // to node 407 added. use them in assembler and disassembler"), which also renamed the node's own header
  // comment ("VM secondary main", was "VM extending") and hoisted the shared "fetch the trailing operand
  // word" step (A[ m/next ]] lit !b) out of 'lcall/'ljmp's own bodies and into n/main's own dispatch tail
  // itself (still falling to "ex" for the "1100" prefix, so the CVM opcode tag itself, LongCallTagBits
  // below, is UNCHANGED). Originally added as THREE mnemonics -- clbr/lbr/cljmp -- but Stefan removed the
  // other two from node 407's own source the same day ("I remove clbr and cljmp from node 407. remove it
  // also from the CVM assembler and disassembler"), before either was ever confirmed on real hardware, so
  // their constants and Instructions entries (formerly Id 98/Id 100) are deleted outright here rather than
  // kept-but-superseded -- unlike every other "do not remove any opcodes" case elsewhere in this file, no
  // .gaobj could have been assembled against those two Ids since they never shipped past the same day they
  // were added, so there is nothing to preserve backward compatibility with. Ids 98 and 100 are retired and
  // must never be reused for a different instruction (see Instructions below). lbr itself is unaffected --
  // still shaped exactly like lcall/ljmp (CvmOperandEncoding.TrailingWord: one tagged opcode word, resolved
  // against node 407's own live compile, same tag as lcall/ljmp since it shares node 407's SAME "1100"
  // dispatch branch, just a different address within node 407's own RAM; then one trailing operand word).
  // Per this source's own trailing comment: lbr (long branch) adds a trailing signed offset to the CVM's
  // own program counter via node 507's own m/branch, unconditionally. NOT YET CONFIRMED ON REAL HARDWARE
  // (2026-09-06) -- see Cvm.Node407Program's own remarks for the full derivation.
  public const string LongBranchMnemonic = "lbr";

  // Node 306's four 32-bit address-register ops, added per Stefan's node 306 source (2026-09-06, "I
  // change node 307 and added node 306... In 306 there are 4 32-bit address register to access the
  // whole memory range."), reached from node 407's OWN "1101_????_????_????" sub-branch (previously
  // FLAGGED as unresolved -- see Cvm.Node407Program's own remarks -- now filled by node 307, "VM
  // ternary main", which itself further relays "1101_10??_????_????" specifically to node 306, "# right
  // /b" -- see Cvm.Node307Program's/Cvm.Node306Program's own remarks). Unlike every tagged mnemonic
  // above (lcall/ljmp/lbr/ldg/stg and the rest), these SIX are self-describing, straight from Stefan's
  // own trailing bit-pattern table on node 306's source -- no node/linker resolution needed at all, the
  // same CvmOperandEncoding.EmbeddedUnsignedValue shape node 606's eight frame-pointer ops use, just
  // with a genuinely new wrinkle: the packed value is a 2-bit ADDRESS-REGISTER INDEX (0-3) that sits at
  // bits 2-1 of the word, not bit 0 upward -- bit 0 is architecturally fixed at 0 (Stefan's own pattern
  // ends every one of these six in "?aa0"), and the "?" nibble/bit above the tag is genuinely
  // don't-care (node 306's own ar/main dispatch, ": ar/reg 0x06 and a! ;", never tests it), assembled as
  // 0 here for a clean, predictable canonical encoding. This needed one new field on
  // CvmInstructionShape, ValueBitShift (default 0, so every existing EmbeddedUnsignedValue/
  // EmbeddedSignedValue shape is completely unaffected) -- see that field's own remarks, and
  // CvmAssembler.EmitEmbeddedUnsignedValue's/CvmAssemblyLanguage.EncodeSelfDescribingWord's own remarks
  // for where the shift is actually applied.
  //
  // ldar (1101_1011_????_?aa0) loads r from the word at the address held in address register aa;
  // star (1101_1010_????_?aa0) stores r to that same address -- the actual pointer DEREFERENCE
  // primitives, per the source's own trailing comments ("load/store r using address register"). inca/
  // deca (1101_1001_1???_?aa0 / 1101_1001_0???_?aa0) increment/decrement address register aa's own
  // address by one word. lda/sta (1101_1000_1???_?aa0 / 1101_1000_0???_?aa0) load/store address
  // register aa's own full 32-bit value from/to {register r (address word), the CVM data stack top
  // (page word)} -- per the source's own trailing comments ("load/store address register, address in
  // r, page in stack"); these are how a 32-bit address gets INTO or back OUT OF a register in the first
  // place (e.g. for the C compiler's own ABI v2 use of address registers to pass pointer arguments --
  // see Ga144.C.Toolchain.CCodeGenerator's own ABI doc comment), as opposed to ldar/star which use an
  // already-loaded register to access memory. NOT YET CONFIRMED ON REAL HARDWARE (2026-09-06).
  //
  // FLAGGED, not silently worked around: this whole family sits at 0xD800-0xDBFF, squarely inside
  // SlitTag's own already-active 0xD000-0xDFFF range (Stefan's own "1101 xxxx xxxx xxxx ... slit"
  // table). TryDescribeSelfDecodingWord checks slit BEFORE the generic EmbeddedUnsignedValue loop these
  // six mnemonics rely on (see that method's own remarks), so EVERY one of these six -- being pure
  // self-describing words with no node/symbol to fall back on -- is currently COMPLETELY SHADOWED in
  // disassembly: a word like 0xD800 ("sta 0") will show as "slit -2048" in the memory
  // inspector/debugger, never as "sta 0", however this file's own Instructions table is ordered.
  // Assembling FROM a mnemonic (ldar/star/inca/deca/lda/sta by name) is completely unaffected and still
  // produces the exact bit pattern Stefan specified -- only decoding an already-assembled WORD back to
  // a mnemonic is ambiguous. This is the exact same shape of issue already accepted for enter/br
  // (Node506EnterTag's own remarks, "ignore the overlapping ranges") -- implemented here exactly as
  // specified rather than silently narrowed or moved, flagged for Stefan to resolve (whether slit's own
  // range is meant to exclude 0xD800-0xDBFF now that node 306 claims it, or whether this collision is
  // accepted the same way as enter/br's).
  public const string LoadAddressRegisterMnemonic = "ldar";
  public const string StoreAddressRegisterMnemonic = "star";
  public const string IncrementAddressRegisterMnemonic = "inca";
  public const string DecrementAddressRegisterMnemonic = "deca";
  public const string LoadAddressRegisterValueMnemonic = "lda";
  public const string StoreAddressRegisterValueMnemonic = "sta";

  /// <summary>Fixed high-bit pattern for <see cref="LoadAddressRegisterMnemonic"/> (<c>ldar</c>), register index 0: <c>1101_1011_0000_0000</c>. See that constant's own remarks.</summary>
  public const int Node306LoadAddressRegisterTag = 0xDB00;

  /// <summary>Fixed high-bit pattern for <see cref="StoreAddressRegisterMnemonic"/> (<c>star</c>), register index 0: <c>1101_1010_0000_0000</c>. See <see cref="LoadAddressRegisterMnemonic"/>'s own remarks.</summary>
  public const int Node306StoreAddressRegisterTag = 0xDA00;

  /// <summary>Fixed high-bit pattern for <see cref="IncrementAddressRegisterMnemonic"/> (<c>inca</c>), register index 0: <c>1101_1001_1000_0000</c>. See <see cref="LoadAddressRegisterMnemonic"/>'s own remarks.</summary>
  public const int Node306IncrementAddressRegisterTag = 0xD980;

  /// <summary>Fixed high-bit pattern for <see cref="DecrementAddressRegisterMnemonic"/> (<c>deca</c>), register index 0: <c>1101_1001_0000_0000</c>. See <see cref="LoadAddressRegisterMnemonic"/>'s own remarks.</summary>
  public const int Node306DecrementAddressRegisterTag = 0xD900;

  /// <summary>Fixed high-bit pattern for <see cref="LoadAddressRegisterValueMnemonic"/> (<c>lda</c>), register index 0: <c>1101_1000_1000_0000</c>. See <see cref="LoadAddressRegisterMnemonic"/>'s own remarks.</summary>
  public const int Node306LoadAddressRegisterValueTag = 0xD880;

  /// <summary>Fixed high-bit pattern for <see cref="StoreAddressRegisterValueMnemonic"/> (<c>sta</c>), register index 0: <c>1101_1000_0000_0000</c>. See <see cref="LoadAddressRegisterMnemonic"/>'s own remarks.</summary>
  public const int Node306StoreAddressRegisterValueTag = 0xD800;

  /// <summary>Isolates the 2-bit address-register index field (bits 2-1) shared by all six of node 306's ops -- see <see cref="LoadAddressRegisterMnemonic"/>'s own remarks. Combine with <see cref="Node306AddressRegisterIndexShift"/> when packing/unpacking (the index itself is 0-3, not 0/2/4/6).</summary>
  public const int Node306AddressRegisterIndexBitMask = 0x0006;

  /// <summary>How far left an address-register index (0-3) is shifted before OR-ing into <see cref="Node306AddressRegisterIndexBitMask"/>'s bits -- 1, since those bits sit at positions 2-1, not 1-0 (bit 0 is architecturally fixed at 0). See <see cref="CvmInstructionShape.ValueBitShift"/>'s own remarks.</summary>
  public const int Node306AddressRegisterIndexShift = 1;

  // Node 511's four register-file ops (2026-09-07, "here are nodes 510 and 511 ... they support 32
  // register that can be used for parameter passing to functions") -- unlike every mnemonic above,
  // these need BOTH a live compile of a specific node (like the None/TrailingWord "tagged" mnemonics
  // resolved in Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own NodeSymbolByMnemonic) AND an embedded
  // operand packed into the very same word (like an EmbeddedUnsignedValue mnemonic) -- something no
  // earlier CvmOperandEncoding case does both of at once. See CvmOperandEncoding.NodeResolvedEmbeddedValue's
  // own remarks for why a new case was added rather than reusing None/TrailingWord/EmbeddedUnsignedValue,
  // and Node511Program's own remarks for the full bit-by-bit derivation straight from its own r/main body.
  //
  // Node 511's own header ("register file, 1011_11??_????_????") fixes the top 6 bits; node 511's own
  // r/main splits the remaining 10 bits into a 5-bit "which function" field (bits 9-5, biased by +0x20 --
  // "the address of the function is encoded in the next 5 bits with an offset of 0x20") and a 5-bit
  // register-index field (bits 4-0, unbiased, 0-31 -- "32 register"). The FIXED 6-bit tag prefix itself
  // (Node511Tag) lives with the rest of this file's other live-node-resolved tag constants, in
  // Ga144.Evb.Ide.Services.CvmAssemblyLanguage, not here -- see that file's own remarks -- since (like
  // every other live-node-resolved tag) it is only ever combined with a RESOLVED address there, never
  // used standalone by anything self-describing. The two field layouts below (function-select field and
  // register field), by contrast, describe the WORD FORMAT itself, not a live-resolution detail, so they
  // stay here alongside every other field-layout constant in this file.
  public const string LoadRegisterFileMnemonic = "rld";
  public const string StoreRegisterFileMnemonic = "rst";
  public const string PopRegisterFileMnemonic = "rpop";
  public const string PushRegisterFileMnemonic = "rpush";

  /// <summary>Isolates node 511's 5-bit "which function" field (bits 9-5) -- the resolved target address (0x20-0x3F) minus <see cref="Node511FunctionFieldBaseAddress"/>, shifted left by <see cref="Node511FunctionFieldShift"/>. See <see cref="LoadRegisterFileMnemonic"/>'s own remarks.</summary>
  public const int Node511FunctionFieldBitMask = 0x03E0;

  /// <summary>How far left node 511's resolved (address - <see cref="Node511FunctionFieldBaseAddress"/>) is shifted before OR-ing into <see cref="Node511FunctionFieldBitMask"/>'s bits -- 5, since the 5-bit register field occupies bits 4-0 below it. See <see cref="LoadRegisterFileMnemonic"/>'s own remarks.</summary>
  public const int Node511FunctionFieldShift = 5;

  /// <summary>Node 511's own "# 0x20 org" -- every one of its compiled word addresses starts at 0x20, so the function-select field is the RESOLVED address minus this, never the raw address itself. See <see cref="LoadRegisterFileMnemonic"/>'s own remarks.</summary>
  public const int Node511FunctionFieldBaseAddress = 0x20;

  /// <summary>Isolates node 511's 5-bit register-index field (bits 4-0, unshifted, 0-31) -- see <see cref="LoadRegisterFileMnemonic"/>'s own remarks.</summary>
  public const int Node511RegisterFieldBitMask = 0x001F;

  // CVM2's load global/store global, added per Stefan's node 508 source (2026-09-04, "here is node
  // 508. it handles access to globals.") and his own tick-naming rule ("every word that begins with a
  // ' is an opcode for the CVM with the mnemonic using the same name without the leading '"): node 508
  // defines ': 'ldg g/next g/@ ;' and ': 'stg g/next g/! ;', each first fetching a trailing offset word
  // via g/next (structurally the SAME m/next-relay g/next itself performs) before doing the actual
  // global read/write -- exactly the "tagged opcode word, then one trailing operand word" shape
  // pushlit/lcall/ljmp already use, so no new CvmOperandEncoding case was needed here either, just two
  // more TrailingWord entries pointed at node 508. Per node 508's own g/main dispatch cascade (see
  // Cvm.Node508Program's own remarks for the full derivation): node 507 hands off to node 508 once a
  // fetched CVM opcode word's top bits read "101?" (the LEFT port), and node 508's own cascade consumes
  // three more bits before falling through to its own remote-fetch-then-"ex" tail for the "1010_0"
  // case (5 bits total: "10100") -- so 'ldg's/'stg's own CVM opcode word always has its top 5 bits
  // "10100" (0xA000), the same "tag | local address" scheme Node407LongCallTagBits/
  // Node506LeaveTagBits/Node507Cvm2LocalExecuteTagBits already use (in the IDE project's own
  // Services.CvmAssemblyLanguage), just a 5-bit tag/11-bit address split this time. The actual
  // global-offset operand itself is carried separately, in the trailing word, read by 'ldg/'stg
  // themselves via g/next once running on node 508 -- see Cvm.Node508Program's own remarks. Node 508's
  // own g/main also answers FOUR further, narrower opcode forms with an offset embedded directly in the
  // opcode word (9 bits, no trailing word) rather than via 'ldg/'stg's trailing-word form: global fetch,
  // global store, branch, and conditional branch (the latter two added 2026-09-06, using node 507's own
  // m/branch export -- see Cvm.Node507Program's own remarks -- for the actual jump; the offset width
  // shrank from 10 to 9 bits that same revision to make room for the extra dispatch bit distinguishing
  // the branch pair from the fetch/store pair). None of these four are wired in here, since Stefan's own
  // source gives none of them a tick-prefixed name to hang a CVM mnemonic off of (only 'ldg/'stg qualify
  // under his own naming rule) -- see Cvm.Node508Program's own remarks, including a flagged, not-fixed
  // apparent stack-depth bug spotted in the new "branch" form's own sign-extension idiom. 'ldg's/'stg's
  // own addresses on node 508 moved (0x002C/0x002E -> 0x003A/0x003C) when g/main grew to fit the two new
  // forms, but since both are resolved dynamically against a live compile (never a hardcoded address),
  // this required no change to LoadGlobalMnemonic/StoreGlobalMnemonic or their own tag derivation above.
  // NOT YET CONFIRMED ON REAL HARDWARE (2026-09-04) -- derived the same way lcall/ljmp's own tag was
  // before its own hardware confirmation, but node 508's load has not itself been installed and run yet.
  public const string LoadGlobalMnemonic = "ldg";
  public const string StoreGlobalMnemonic = "stg";

  // Node 509's nine unary-arithmetic ops, added per Stefan's node 509 source (2026-09-05, "here is node
  // 509"). Node 509 is reached from node 508's own g/main dispatch (NOT from node 507 directly) once a
  // fetched CVM opcode word's top bits read "1011" -- node 508's own remarks document the "extended
  // arithmetic, relayed onward via the RIGHT port" branch as an as-yet-unsupplied neighbour; node 509 is
  // that neighbour. Every one of these nine is shaped exactly like node 508's/506's/407's own ops -- a
  // single bare TAGGED opcode word (CvmOperandEncoding.None), never self-describing: node 509's own
  // u/main receives a dispatch address directly over the port (relayed further from node 508, itself
  // relayed from node 507) and jumps straight to it via "ex" once its own cascade consumes "1011_00"
  // (see Services.CvmAssemblyLanguage.Node509UnaryArithmeticTagBits's own remarks for the full
  // derivation), so each named word's real opcode is node 509's own confirmed tag (0xB000) OR'd with
  // wherever that word lands in node 509's own compiled RAM, resolved only against a live compile. None
  // of the nine takes an assembled operand: each acts on register r alone (already relayed across two
  // hops -- 507 to 508 to 509 -- by the time u/main dispatches to it).
  //
  // EIGHT of these nine REPOINT an existing, previously-orphaned mnemonic rather than adding a new one,
  // per "only update existing opcodes where possible": InvertMnemonic/IncrementMnemonic/
  // DecrementMnemonic (node 507's own old ALU-op family, three of its eleven unary/binary ops) and
  // AbsoluteValueMnemonic/MultiplyByTwoMnemonic/DivideByTwoMnemonic/UnsignedDivideByTwoMnemonic/
  // BitCountMnemonic (five of node 508's own old 27-op family) all already existed in Instructions below
  // with no live node to resolve against; node 509's own tick-prefixed words ('inv, 'inc, 'dec, 'abs,
  // 'mul2, 'div2, 'udiv2, 'bitcnt) match those exact mnemonic strings, so they are repointed here rather
  // than duplicated. Only NegMnemonic below is a genuinely NEW entry: node 509's own word is named
  // 'neg, not 'negate, so it does NOT repoint the existing (still separately orphaned) NegateMnemonic
  // ("negate", Id 51) -- taken literally, per Stefan's own tick-naming rule, rather than assumed to be a
  // renaming of it.
  public const string NegMnemonic = "neg";

  // CVM2 node 509's own literal-load mnemonic, added 2026-09-05 per Stefan's own explicit follow-up
  // ("add this range to the cvm language ... mnemonic lit") naming the "1011_01??_????_????" branch of
  // node 509's own u/main dispatch that was previously left unwired for lack of a name (see
  // Node509Program's own remarks). Self-describing (CvmOperandEncoding.EmbeddedSignedValue), shaped
  // exactly like br/ifbr/slit -- see LitTag's own remarks for the full bit derivation.
  public const string LitMnemonic = "lit";

  // Node 509's tenth and eleventh unary-arithmetic ops, added 2026-09-05 per Stefan's own follow-up
  // ("I added 2 new opcodes to node 509. add them also to the language") and his own tick-naming rule,
  // exactly like the original nine. Both are genuinely NEW mnemonics -- no existing orphaned "parity" or
  // "odd" mnemonic anywhere in this table to repoint -- shaped exactly like the original nine (a single
  // bare TAGGED opcode word, CvmOperandEncoding.None, resolved only against node 509's own live compile,
  // tag 0xB000 | local address, same as InvertMnemonic/IncrementMnemonic/etc. above). Per Node509Program's
  // own remarks, 'parity and 'odd share the SAME cross-definition fall-through idiom already used by
  // 'abs/'neg/'inc/'dec: "'parity" has no own trailing ";" -- its one-word body (a call to 'bitcnt) falls
  // straight through into 'odd's own body ("1 and ;"), so invoking 'parity computes bitcnt-then-AND-1
  // (the parity bit), while 'odd entered directly at its own address just does the AND-1 test alone. Each
  // still has its own distinct, independently-reachable address, exactly like the four-way overlap above.
  public const string ParityMnemonic = "parity";
  public const string OddMnemonic = "odd";

  // Node 509's twelfth op, added 2026-09-05 per Stefan's own follow-up ("I added 'not' to node 509.
  // please add it to assembler and disassembler") -- genuinely new, no existing orphaned "not" mnemonic
  // to repoint. Shaped exactly like the other eleven (a single bare TAGGED opcode word,
  // CvmOperandEncoding.None, resolved only against node 509's own live compile, tag 0xB000 | local
  // address). Note (not this toolchain's concern to resolve, just to flag): this same source revision
  // also inserted a new "ahead" right after 'neg's own "inv", closed by a SECOND "then" added at the very
  // end of 'not's own definition ("if dup xor ; then then") -- per the F18 compiler's own LIFO forward-
  // branch stack (CompileForwardIf/CompileAhead push a handle, CompileThen pops the most recently pushed
  // one), that new "ahead" is resolved by the FIRST "then" after it (the one already there, right after
  // 'dec's own ";"), while the ORIGINAL "-if" opened by 'abs is now what the NEW second "then" resolves,
  // pushed down one level by the new "ahead". Every one of 'abs/'neg/'inc/'dec/'inv/... own addresses is
  // confirmed UNCHANGED by this (the new "ahead" packed into 'neg's own existing word, per
  // F18CompilerOptions.PackControlTransfers's default packing behavior) -- see Node509Program's own
  // remarks for the full derivation.
  public const string NotMnemonic = "not";

  // Node 406's twelve binary-arithmetic ops, added 2026-09-05 per Stefan's node 406 source ("i provide
  // you with node 406. it contains binary operations. add these opcodes to assembler and disassembler
  // and include this node in the boot stream"). Node 406 is reached from node 407's own n/main dispatch
  // (NOT node 507 directly) via its RIGHT port -- a SECOND three-hop branch off 507, mirroring
  // 507->508->509 with 507->407->406. EIGHT of these twelve REPOINT existing, previously-orphaned
  // mnemonics from node 507's old CVM1-era ALU-op family, per "only update existing opcodes where
  // possible": AddMnemonic/SubtractMnemonic/AndMnemonic/XorMnemonic/OrMnemonic/
  // UnsignedShiftLeftMnemonic/SignedShiftRightMnemonic/UnsignedShiftRightMnemonic (add/sub/and/xor/or/
  // usl/ssr/usr) already existed in Instructions below with no live node to resolve against; node 406's
  // own tick-prefixed words ('add, 'sub, 'and, 'xor, 'or, 'usl, 'ssr, 'usr) match those exact mnemonic
  // strings, so they are repointed here rather than duplicated -- see
  // Services.CvmAssemblyLanguage.NodeSymbolByMnemonic for the actual repointing. FOUR are genuinely NEW
  // mnemonics: node 406's own 'rsb/'rsl/'rsr/'rur ("reverse subtract"/"reverse shift left"/"reverse
  // shift right"/"reverse unsigned shift right") have no existing same-named counterpart anywhere in
  // this table.
  //
  // Each of the twelve exists in TWO forms on node 406 itself, both resolving to the SAME F18 symbol/
  // address but reached via a DIFFERENT tag and taking a DIFFERENT CVM operand shape, per node 406's own
  // y/main dispatch cascade (see Cvm.Node406Program's own remarks for the full derivation): a
  // "parameter on the stack" form (CvmOperandEncoding.None, tag 0xE000, Node406BinaryStackTagBits) and a
  // "constant in the next word" form (CvmOperandEncoding.TrailingWord, tag 0xE400,
  // Node406BinaryConstantTagBits) -- shaped exactly like pushlit/lcall/ljmp/ldg/stg's own trailing-word
  // convention. Per Stefan's own explicit naming instruction ("for opcode with constant parameter, add a
  // i to the mnemonic. so 'add' becomes 'addi'"), the constant-in-next-word form of each of the twelve
  // gets its own, separate, "i"-suffixed CVM mnemonic (addi/subi/rsbi/andi/xori/ori/rsli/usli/rsri/ssri/
  // ruri/usri) -- a completely distinct entry in this table from its stack-parameter counterpart, even
  // though both ultimately resolve to the same node-406 F18 symbol.
  public const string ReverseSubtractMnemonic = "rsb";
  public const string ReverseShiftLeftMnemonic = "rsl";
  public const string ReverseShiftRightMnemonic = "rsr";
  public const string ReverseUnsignedShiftRightMnemonic = "rur";

  // The "i suffix" constant-in-next-word forms of all twelve of node 406's binary ops -- see the remarks
  // just above for the naming rule and the shared-symbol/different-tag relationship to their
  // stack-parameter counterparts.
  public const string AddConstantMnemonic = "addi";
  public const string SubtractConstantMnemonic = "subi";
  public const string ReverseSubtractConstantMnemonic = "rsbi";
  public const string AndConstantMnemonic = "andi";
  public const string XorConstantMnemonic = "xori";
  public const string OrConstantMnemonic = "ori";
  public const string ReverseShiftLeftConstantMnemonic = "rsli";
  public const string UnsignedShiftLeftConstantMnemonic = "usli";
  public const string ReverseShiftRightConstantMnemonic = "rsri";
  public const string SignedShiftRightConstantMnemonic = "ssri";
  public const string ReverseUnsignedShiftRightConstantMnemonic = "ruri";
  public const string UnsignedShiftRightConstantMnemonic = "usri";

  /// <summary>
  /// The widest word address <c>call</c> can directly encode into its own opcode word: 0x7FFF, i.e.
  /// 15 bits. Bit 15 (0x8000) must stay clear on a <c>call</c> word -- that is the only thing that
  /// tells a linked program's interpreter "this word is a call to the address it contains" apart from
  /// "this word is a tagged instruction dispatch," so a <c>call</c> target that doesn't fit in 15 bits
  /// is a hard assemble/link error, never silently masked.
  /// </summary>
  public const int CallAddressMask = 0x7FFF;

  // br's own encoding -- MOVED 2026-09-09, per Stefan: "the assembler is not up to date. 'br' is
  // wrongly encoded ... opcode br 1000_0??? ???? ???? branch relative signed 11-bit offset in opcode."
  // OLD (2026-09-02) table, now WRONG for br, kept here only for the paper trail:
  //   1001 0xxx xxxx xxxx   -0x400..0x3FF   br   (branch, signed offset)
  //   1001 1xxx xxxx xxxx   -0x400..0x3FF   ifbr (conditional branch, signed offset)
  // NEW (2026-09-09) table -- br ONLY, per Stefan's own words above:
  //   1000 0xxx xxxx xxxx   -0x400..0x3FF   br   (branch, signed offset)
  // -- still a fixed 5-bit tag (bits 15-11) OR'd with an 11-bit two's-complement signed offset (bits
  // 10-0); -0x400..0x3FF is exactly an 11-bit signed value's own range, confirming the field width is
  // unchanged -- only the tag's own bit pattern moved, one nibble down (1001_0 -> 1000_0).
  //
  // This exact "1000_0" pattern for br was ALREADY independently derived, before Stefan's correction,
  // in Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own Node507Cvm2LocalExecuteTagBits remarks: node
  // 507's own m/main dispatch cascade (its real F18 source) reads "1000_0???" as "branch relative" and
  // "1000_1???" as "local execute" (jump to the address in the low bits) -- see that constant's own
  // remarks, there marked "DERIVED, NOT YET CONFIRMED WITH STEFAN" at the time. Stefan's message here
  // independently confirms exactly that derivation for br, which is why BranchTag below moves to
  // 0x8000 with high confidence.
  //
  // ifbr's own tag is DELIBERATELY NOT CHANGED here, and must NOT be assumed to be 0x8800 (the
  // neighboring "1000_1" slot) -- that slot is ALREADY node 507's own real, hardware-corroborated
  // Node507Cvm2LocalExecuteTagBits (nop/pushlit/push/pop/ret/halt; see that constant's own remarks,
  // including 'halt's body literally writing the 0x8800 constant to hardware). Moving ifbr there would
  // trade one silent collision (with Node506StoreParameterTag, below) for a worse one, against a tag
  // that is actually confirmed rather than merely orphaned. Stefan's message named only br's new
  // pattern; ifbr STAYS at its OLD 0x9800 below, still colliding with Node506StoreParameterTag exactly
  // as before -- unresolved, needs Stefan's own ifbr bit pattern before it can move anywhere.

  /// <summary>
  /// The fixed high-bit pattern (bits 15-11) of a <c>br</c> word: binary 10000. CHANGED 2026-09-09 from
  /// binary 10010 (0x9000) -- see this file's own remarks just above for Stefan's correction, the exact
  /// wording that prompted it, and the independent node-507-dispatch derivation that agrees with it.
  /// </summary>
  public const int BranchTag = 0x8000;

  /// <summary>
  /// The fixed high-bit pattern (bits 15-11) of an <c>ifbr</c> word: binary 10011 (0x9800).
  /// DELIBERATELY UNCHANGED as of 2026-09-09 even though <see cref="BranchTag"/> moved -- see this
  /// file's own remarks just above. Do NOT set this to 0x8800: that is
  /// Ga144.Evb.Ide.Services.CvmAssemblyLanguage.Node507Cvm2LocalExecuteTagBits, a different, already
  /// hardware-corroborated opcode (node 507's own "local execute" dispatch, not a free slot). This
  /// constant still collides with <see cref="Node506StoreParameterTag"/> exactly as it did before
  /// 2026-09-09 -- see that constant's own remarks -- until Stefan gives ifbr's own correct new tag.
  /// </summary>
  public const int ConditionalBranchTag = 0x9800;

  /// <summary>Isolates a word's top 5 bits, for testing against <see cref="BranchTag"/>/<see cref="ConditionalBranchTag"/>.</summary>
  public const int BranchTagMask = 0xF800;

  /// <summary>Isolates a word's low 11 bits -- the raw (not yet sign-extended) branch offset field.</summary>
  public const int BranchOffsetBitMask = 0x7FF;

  /// <summary>The most negative offset an 11-bit two's-complement field can hold: -0x400 (-1024).</summary>
  public const int BranchOffsetMinValue = -0x400;

  /// <summary>The largest offset an 11-bit two's-complement field can hold: 0x3FF (1023).</summary>
  public const int BranchOffsetMaxValue = 0x3FF;

  // slit's own encoding, straight from Stefan's bit-pattern table:
  //   1101 xxxx xxxx xxxx   -0x800..0x7FF   slit (literal, signed value)
  // -- a fixed 4-bit tag (bits 15-12) OR'd with a 12-bit two's-complement signed value (bits 11-0).
  // -0x800..0x7FF is exactly a 12-bit signed value's own range, confirming the field width. Unlike
  // br/ifbr's offset, slit's value isn't an address computation at all: per Stefan, executing a slit
  // word loads its signed value directly into the F18 interpreter's own R register (node 607's own
  // runtime behavior, not something this toolchain project implements or needs to know how to do).

  /// <summary>The fixed high-bit pattern (bits 15-12) of a <c>slit</c> word: binary 1101.</summary>
  public const int SlitTag = 0xD000;

  /// <summary>Isolates a word's top 4 bits, for testing against <see cref="SlitTag"/>.</summary>
  public const int SlitTagMask = 0xF000;

  /// <summary>Isolates a word's low 12 bits -- the raw (not yet sign-extended) <c>slit</c> value field.</summary>
  public const int SlitValueBitMask = 0xFFF;

  /// <summary>The most negative value a 12-bit two's-complement field can hold: -0x800 (-2048).</summary>
  public const int SlitValueMinValue = -0x800;

  /// <summary>The largest value a 12-bit two's-complement field can hold: 0x7FF (2047).</summary>
  public const int SlitValueMaxValue = 0x7FF;

  // Node 606's eight frame-pointer-management ops, straight from Stefan's bit-pattern table:
  //   1010 1000 xxxx xxxx   0..0xFF   enter <locals>
  //   1010 1001 xxxx xxxx   0..0xFF   adjust <offset>
  //   1010 1010 xxxx xxxx   0..0xFF   stl <offset>
  //   1010 1011 xxxx xxxx   0..0xFF   stp <offset>
  //   1010 1100 xxxx xxxx   0..0xFF   ldl <offset>
  //   1010 1101 xxxx xxxx   0..0xFF   ldp <offset>
  //   1010 1110 xxxx xxxx   0..0xFF   lal <offset>
  //   1010 1111 xxxx xxxx   0..0xFF   lap <offset>
  // -- a fixed 8-bit tag (bits 15-8) OR'd with an UNSIGNED 8-bit value (bits 7-0). Unlike br/ifbr/slit,
  // the table gives these an unsigned range, not a signed one, so 0xFF is the largest value, never
  // sign-extended back to -1.

  /// <summary>
  /// The fixed high-bit pattern (bits 15-8) of CVM1's OLD node-606 <c>enter</c> word: binary 1010_1000.
  /// SUPERSEDED (2026-09-02) -- <c>enter</c> itself now uses <see cref="Node506EnterTag"/> instead (see
  /// that constant's own remarks); this constant is kept per "do not remove any opcodes" but is no
  /// longer referenced by <see cref="Instructions"/>.
  /// </summary>
  public const int EnterTag = 0xA800;

  /// <summary>The fixed high-bit pattern (bits 15-8) of an <c>adjust</c> word: binary 1010_1001.</summary>
  public const int AdjustTag = 0xA900;

  /// <summary>
  /// The fixed high-bit pattern (bits 15-8) of CVM1's OLD node-606 <c>stl</c> word: binary 1010_1010.
  /// SUPERSEDED (2026-09-06) -- <c>stl</c> itself now uses <see cref="Node506StoreLocalTag"/> instead
  /// (see that constant's own remarks); kept per "do not remove any opcodes" but no longer referenced by
  /// <see cref="Instructions"/>.
  /// </summary>
  public const int StoreLocalTag = 0xAA00;

  /// <summary>
  /// The fixed high-bit pattern (bits 15-8) of CVM1's OLD node-606 <c>stp</c> word: binary 1010_1011.
  /// SUPERSEDED (2026-09-06) -- <c>stp</c> itself now uses <see cref="Node506StoreParameterTag"/>
  /// instead (see that constant's own remarks); kept per "do not remove any opcodes" but no longer
  /// referenced by <see cref="Instructions"/>.
  /// </summary>
  public const int StoreParameterTag = 0xAB00;

  /// <summary>
  /// The fixed high-bit pattern (bits 15-8) of CVM1's OLD node-606 <c>ldl</c> word: binary 1010_1100.
  /// SUPERSEDED (2026-09-06) -- <c>ldl</c> itself now uses <see cref="Node506LoadLocalTag"/> instead
  /// (see that constant's own remarks); kept per "do not remove any opcodes" but no longer referenced by
  /// <see cref="Instructions"/>.
  /// </summary>
  public const int LoadLocalTag = 0xAC00;

  /// <summary>
  /// The fixed high-bit pattern (bits 15-8) of CVM1's OLD node-606 <c>ldp</c> word: binary 1010_1101.
  /// SUPERSEDED (2026-09-06) -- <c>ldp</c> itself now uses <see cref="Node506LoadParameterTag"/> instead
  /// (see that constant's own remarks); kept per "do not remove any opcodes" but no longer referenced by
  /// <see cref="Instructions"/>.
  /// </summary>
  public const int LoadParameterTag = 0xAD00;

  /// <summary>The fixed high-bit pattern (bits 15-8) of a <c>lal</c> word: binary 1010_1110.</summary>
  public const int LoadAddressOfLocalTag = 0xAE00;

  /// <summary>The fixed high-bit pattern (bits 15-8) of a <c>lap</c> word: binary 1010_1111.</summary>
  public const int LoadAddressOfParameterTag = 0xAF00;

  /// <summary>Isolates a word's top 8 bits, for testing against any of node 606's eight tags above.</summary>
  public const int Node606TagMask = 0xFF00;

  /// <summary>Isolates a word's low 8 bits -- the unsigned value field shared by all eight of node 606's ops.</summary>
  public const int Node606ValueBitMask = 0xFF;

  // CVM2's node 506 (2026-09-02) redefines enter/leave (and, since 2026-09-06, load-local/load-parameter/
  // store-local/store-parameter too) with a DIFFERENT bit layout than CVM1's node 606: a 7-bit tag (bits
  // 15-9) OR'd with a 9-bit UNSIGNED offset (bits 8-0), rather than 606's 8-bit tag/8-bit value split --
  // per Node506Program's own remarks, derived directly from its f/main dispatch cascade: "1001_001?"
  // (7 bits fixed: 1001001) is enter, with "the offset is 9 bit" per the source's own trailing comment.
  // enter is repointed here ("only update existing opcodes where possible"); adjust/lal/lap remain
  // UNTOUCHED and still point at node 606's old 8-bit tags above -- node 506's own source has never named
  // an "adjust"/"lal"/"lap" equivalent, so those three stay permanently orphaned. This also settles the
  // "may need its own new embedded-value shape" question Node506Program's own remarks once raised: the
  // VALUE field itself is a plain unsigned 9-bit offset for every one of these ops (load-local's/
  // store-local's own sign flip happens entirely inside node 506's own dispatch, via "inv", never at the
  // CVM opcode encoding level) -- EmbeddedUnsignedValue, unchanged, is the right shape after all.
  //
  // KNOWN, DELIBERATE collision with br/ifbr (2026-09-02, extended 2026-09-06): Node506EnterTag used to
  // fall inside BranchTag's own range (OLD 0x9000-0x97FF, EmbeddedSignedValue) -- per Stefan: "ignore
  // the ranges of br/ifbr. ignore the overlapping ranges. give me now enter and leave mnemonics." Not
  // resolved at the time, accepted for a while. The four tags just below (stp/stl/ldp/ldl,
  // 0x9800/0x9A00/0x9C00/0x9E00) fell the SAME way inside ConditionalBranchTag's own range
  // (0x9800-0x9FFF) instead -- the same kind of collision, just against ifbr rather than br.
  //
  // PARTIALLY RESOLVED 2026-09-09: br's own tag moved to 0x8000 (see BranchTag's own remarks -- Stefan:
  // "'br' is wrongly encoded"), so Node506EnterTag (0x9200, unchanged -- it comes from node 506's own
  // real F18 dispatch, never a CVM assembler choice) no longer falls inside br's range at all; enter's
  // collision is gone. ifbr's own tag is DELIBERATELY UNCHANGED (still 0x9800 -- see
  // ConditionalBranchTag's own remarks; 0x8800, the naive "same move, one bit further" guess, is already
  // a different real, confirmed opcode and must not be reused), so the four tags just below (stp/stl/
  // ldp/ldl) STILL collide with ifbr exactly as before -- unresolved, needs Stefan's own ifbr bit
  // pattern.

  /// <summary>
  /// The fixed high-bit pattern (bits 15-9) of CVM2 node 506's <c>enter</c> word: binary 1001_001,
  /// i.e. 0x9200 with the low 9 bits (the offset) zeroed. See this file's own remarks just above on
  /// the 7-bit-tag/9-bit-value split and the accepted br/ifbr collision.
  /// </summary>
  public const int Node506EnterTag = 0x9200;

  /// <summary>
  /// The fixed high-bit pattern (bits 15-9) of CVM2 node 506's <c>stp</c> (store parameter) word: binary
  /// 1001_100, i.e. 0x9800 with the low 9 bits (the offset) zeroed. Added 2026-09-06 when Stefan's
  /// revised node 506 source named this branch explicitly ("opcode stp 1001_100?_????_???? store r into
  /// parameter") -- previously this same <c>1001_100?</c> branch existed in the source but carried no
  /// name, so it stayed unwired (see <see cref="StoreParameterTag"/>'s own remarks for the superseded
  /// CVM1-node-606 constant this replaces). Falls inside <see cref="ConditionalBranchTag"/>'s own range --
  /// see this file's own remarks just above.
  /// </summary>
  public const int Node506StoreParameterTag = 0x9800;

  /// <summary>
  /// The fixed high-bit pattern (bits 15-9) of CVM2 node 506's <c>stl</c> (store local) word: binary
  /// 1001_101, i.e. 0x9A00 with the low 9 bits (the offset) zeroed. Added 2026-09-06 alongside
  /// <see cref="Node506StoreParameterTag"/> -- see that constant's own remarks.
  /// </summary>
  public const int Node506StoreLocalTag = 0x9A00;

  /// <summary>
  /// The fixed high-bit pattern (bits 15-9) of CVM2 node 506's <c>ldp</c> (load parameter) word: binary
  /// 1001_110, i.e. 0x9C00 with the low 9 bits (the offset) zeroed. Added 2026-09-06 alongside
  /// <see cref="Node506StoreParameterTag"/> -- see that constant's own remarks.
  /// </summary>
  public const int Node506LoadParameterTag = 0x9C00;

  /// <summary>
  /// The fixed high-bit pattern (bits 15-9) of CVM2 node 506's <c>ldl</c> (load local) word: binary
  /// 1001_111, i.e. 0x9E00 with the low 9 bits (the offset) zeroed. Added 2026-09-06 alongside
  /// <see cref="Node506StoreParameterTag"/> -- see that constant's own remarks.
  /// </summary>
  public const int Node506LoadLocalTag = 0x9E00;

  /// <summary>Isolates a word's low 9 bits -- CVM2 node 506's own unsigned offset field, shared by <c>enter</c> and its four load-local/load-parameter/store-local/store-parameter siblings.</summary>
  public const int Node506FrameValueBitMask = 0x1FF;

  // CVM2 node 509's own literal-load form, added 2026-09-05 per Stefan's node 509 source
  // (Cvm.Node509Program) and his own follow-up naming it: "add this range to the cvm language ...
  // mnemonic lit". Straight from that source's own bit-pattern comment:
  //   1011 01xx xxxx xxxx   -0x200..0x1FF   lit (literal, signed value)
  // -- a fixed 6-bit tag (bits 15-10) OR'd with a 10-bit two's-complement signed value (bits 9-0).
  // -0x200..0x1FF is exactly a 10-bit signed value's own range, confirming the field width -- the same
  // shape as br/ifbr/slit (EmbeddedSignedValue, self-describing, no live node/linker involvement at
  // all), just its own tag and its own narrower 10-bit field. This is the "1011_01??_????_????" branch
  // of node 509's own u/main dispatch cascade (see Node509Program's own remarks) that was previously
  // left unwired for lack of a name -- now named, it is wired the same way br/ifbr/slit are: a direct
  // check in TryDescribeSelfDecodingWord below, not the generic EmbeddedUnsignedValue loop (which is
  // node 606/509's own TAGGED-dispatch family's mechanism, a different thing). Distinct from the
  // existing <c>slit</c> (0xD000, 12-bit field) -- despite the conceptual similarity (both load a
  // literal signed value directly into a register), Stefan named this one separately, so it is wired as
  // its own mnemonic rather than folded into slit.

  /// <summary>The fixed high-bit pattern (bits 15-10) of a <c>lit</c> word: binary 1011_01.</summary>
  public const int LitTag = 0xB400;

  /// <summary>Isolates a word's top 6 bits, for testing against <see cref="LitTag"/>.</summary>
  public const int LitTagMask = 0xFC00;

  /// <summary>Isolates a word's low 10 bits -- the raw (not yet sign-extended) <c>lit</c> value field.</summary>
  public const int LitValueBitMask = 0x3FF;

  /// <summary>The most negative value a 10-bit two's-complement field can hold: -0x200 (-512).</summary>
  public const int LitValueMinValue = -0x200;

  /// <summary>The largest value a 10-bit two's-complement field can hold: 0x1FF (511).</summary>
  public const int LitValueMaxValue = 0x1FF;

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
    /// high bits) OR'd with a signed value packed into <see cref="CvmInstructionShape.ValueBitMask"/>'s
    /// low bits (<c>br</c>, <c>ifbr</c>, <c>slit</c> -- each with its own tag and field width; see
    /// <see cref="CvmInstructionShape.ValueBitMask"/>'s own remarks). Fully self-describing and known
    /// at assemble time from a literal operand alone -- unlike the tagged mnemonics, it involves no
    /// node, no linker, and (for now, see <see cref="CvmAssembler"/>'s own remarks) no label/import
    /// operand either.
    ///
    /// For <c>br</c>/<c>ifbr</c> specifically, this has been confirmed against real hardware (a
    /// <c>br 1</c> placed right where a call/ret round trip resumes, at address 2, jumped straight to
    /// address 4, skipping address 3 entirely): the target address is
    /// <c>(this instruction's own address + 1) + offset</c> -- i.e. relative to the address of the
    /// word immediately AFTER the branch's own opcode word, not relative to the branch word's own
    /// address. This is exactly the fact a future label operand would need (see
    /// <see cref="CvmAssembler"/>'s own remarks) to turn "jump to that label" into the right literal
    /// offset; it just isn't wired up yet. <c>slit</c> has no such "relative to" question at all --
    /// its value isn't an address, just a literal loaded directly into a register (see
    /// <see cref="SlitTag"/>'s own remarks).
    /// </summary>
    EmbeddedSignedValue,

    /// <summary>
    /// Like <see cref="EmbeddedSignedValue"/> -- a fixed <see cref="CvmInstructionShape.Tag"/> OR'd with
    /// a value packed into <see cref="CvmInstructionShape.ValueBitMask"/>'s low bits, fully self-
    /// describing and known at assemble time from a literal operand alone -- except the packed value is
    /// UNSIGNED (node 606's eight frame-pointer-management ops: <c>enter</c>, <c>adjust</c>, <c>stl</c>,
    /// <c>stp</c>, <c>ldl</c>, <c>ldp</c>, <c>lal</c>, <c>lap</c>, each an 8-bit tag OR'd with an 8-bit
    /// unsigned offset/count, 0x00-0xFF -- see <see cref="Node606TagMask"/>'s own remarks). Like
    /// <see cref="EmbeddedSignedValue"/>, no label/import operand is supported (yet).
    ///
    /// Also covers node 306's six address-register ops (<see cref="LoadAddressRegisterMnemonic"/>'s own
    /// remarks), which need one refinement: the packed field isn't at bit 0 upward like every earlier
    /// EmbeddedUnsignedValue mnemonic, so <see cref="CvmInstructionShape.ValueBitShift"/> exists to left-
    /// shift the operand before OR-ing it into <see cref="CvmInstructionShape.ValueBitMask"/>'s bits (and
    /// right-shift it back out when decoding) -- 0 for every OLDER EmbeddedUnsignedValue mnemonic (node
    /// 606's eight ops, unaffected), 1 for node 306's six (their 2-bit register index sits at bits 2-1,
    /// with bit 0 architecturally fixed at 0).
    /// </summary>
    EmbeddedUnsignedValue,

    /// <summary>
    /// Node 511's four register-file ops (<c>rld</c>/<c>rst</c>/<c>rpop</c>/<c>rpush</c>, 2026-09-07)
    /// only -- the first mnemonics in this project needing BOTH of the two things every earlier encoding
    /// only ever needed one of: a live compile of a specific node's F18 source to resolve WHICH function
    /// is being invoked (like <see cref="None"/>/<see cref="TrailingWord"/>), AND an embedded operand
    /// packed into the very same word (like <see cref="EmbeddedUnsignedValue"/>). Node 511's own word
    /// format is <c>Node511Tag | ((resolvedAddress - Node511FunctionFieldBaseAddress) &lt;&lt;
    /// Node511FunctionFieldShift) | (registerIndex &amp; Node511RegisterFieldBitMask)</c> -- see
    /// <see cref="LoadRegisterFileMnemonic"/>'s own remarks for the field-layout constants and the full
    /// derivation, and <see cref="Ga144.Evb.Ide.Services.CvmAssemblyLanguage"/>'s own remarks for exactly
    /// where the "live compile" half and the "embedded operand" half are each actually combined (its
    /// <c>BuildEncodeTable</c>/<c>BuildDecodeTable</c>/<c>Assemble</c>/<c>DisassemblePage0</c>, not here
    /// -- unlike <see cref="EmbeddedSignedValue"/>/<see cref="EmbeddedUnsignedValue"/>, this encoding is
    /// NOT self-describing and is deliberately excluded from <see cref="TryDescribeSelfDecodingWord"/>).
    /// <see cref="Ga144.Cvm.Toolchain.CvmAssembler"/> does not (yet) support this encoding at all -- it
    /// errors out rather than silently dropping the embedded register operand, since its own
    /// relocation-based resolution of tagged mnemonics has no way to carry a second, per-instance operand
    /// value; only <see cref="Ga144.Evb.Ide.Services.CvmAssemblyLanguage"/>'s own immediately-resolving
    /// assembler supports it today.
    /// </summary>
    NodeResolvedEmbeddedValue,
  }

  /// <summary>
  /// The shape of one CVM instruction: a stable numeric <see cref="Id"/>, how many words it assembles
  /// to (its own opcode word included), and how its operand (if any) is encoded (see
  /// <see cref="CvmOperandEncoding"/>). <see cref="Tag"/> and <see cref="ValueBitMask"/> are only
  /// meaningful for <see cref="CvmOperandEncoding.EmbeddedSignedValue"/>/<see cref="CvmOperandEncoding.EmbeddedUnsignedValue"/>
  /// shapes -- every other encoding ignores them (default 0).
  /// </summary>
  public sealed record CvmInstructionShape(int Id, string Mnemonic, int WordLength, CvmOperandEncoding Encoding, int Tag = 0, int ValueBitMask = 0, int ValueBitShift = 0)
  {
    /// <summary>True for every encoding except <see cref="CvmOperandEncoding.None"/> -- whether the assembler requires exactly one operand argument for this mnemonic.</summary>
    public bool HasOperand => Encoding != CvmOperandEncoding.None;

    /// <summary>
    /// For an <see cref="CvmOperandEncoding.EmbeddedSignedValue"/>/<see cref="CvmOperandEncoding.EmbeddedUnsignedValue"/>
    /// shape: which low bits of the word hold its value field, distinct per mnemonic family since the
    /// tag/value split isn't fixed width across all of them -- <c>br</c>/<c>ifbr</c> reserve 5 bits for
    /// their tag and pack an 11-bit signed offset into the rest (<see cref="BranchOffsetBitMask"/>),
    /// <c>slit</c> reserves only 4 bits for its tag and packs a 12-bit signed value into the rest
    /// (<see cref="SlitValueBitMask"/>), and node 606's eight ops each reserve 8 bits for their own tag
    /// and pack an 8-bit UNSIGNED value into the rest (<see cref="Node606ValueBitMask"/>). <see cref="Tag"/>
    /// is expected to already be aligned to whatever's outside this mask (i.e.
    /// <c>Tag &amp; ValueBitMask == 0</c>), so a decoder can always recover the tag bits alone via
    /// <c>word &amp; ~ValueBitMask</c> without a separately stored mask per shape.
    /// </summary>
    public int ValueBitMask { get; init; } = ValueBitMask;

    /// <summary>
    /// How far left the operand is shifted before OR-ing it into <see cref="ValueBitMask"/>'s bits (and
    /// shifted back right when decoding) -- 0 for every EmbeddedSignedValue/EmbeddedUnsignedValue shape
    /// except node 306's six address-register ops, whose 2-bit register-index operand sits at bits 2-1
    /// rather than bit 0 upward (see <see cref="LoadAddressRegisterMnemonic"/>'s own remarks). Default 0
    /// leaves every earlier shape's packing arithmetic (a plain <c>Tag | (value &amp; ValueBitMask)</c>,
    /// no shift) completely unchanged.
    /// </summary>
    public int ValueBitShift { get; init; } = ValueBitShift;
  }

  /// <summary>
  /// Every known CVM instruction, in mnemonic order. Extend this list as more opcodes are defined,
  /// giving each new entry the next unused <see cref="CvmInstructionShape.Id"/> -- IDs are
  /// append-only and must never be renumbered or reused, since <see cref="CvmAssembler"/> bakes a
  /// given tagged mnemonic's ID into every object file it has ever produced (see
  /// <see cref="CvmAssembler"/>'s own remarks on why). Nothing else in this project (or in the IDE's
  /// disassembler) needs to change to pick up a new tagged-dispatch entry, beyond the IDE also being
  /// able to resolve the new mnemonic's real opcode(s); a new self-describing entry (like <c>call</c>,
  /// <c>br</c>, <c>ifbr</c>, <c>slit</c>) needs its own encode/decode logic in
  /// <see cref="CvmAssembler"/> and <see cref="TryDescribeSelfDecodingWord"/> instead, since there's no
  /// live compile involved.
  /// </summary>
  public static readonly IReadOnlyList<CvmInstructionShape> Instructions =
  [
    new(Id: 0, NopMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 1, PushLitMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 2, PushMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 3, PopMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 4, CallMnemonic, 1, CvmOperandEncoding.EmbeddedAddress),
    new(Id: 5, RetMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 6, BranchMnemonic, 1, CvmOperandEncoding.EmbeddedSignedValue, Tag: BranchTag, ValueBitMask: BranchOffsetBitMask),
    new(Id: 7, ConditionalBranchMnemonic, 1, CvmOperandEncoding.EmbeddedSignedValue, Tag: ConditionalBranchTag, ValueBitMask: BranchOffsetBitMask),
    new(Id: 8, SlitMnemonic, 1, CvmOperandEncoding.EmbeddedSignedValue, Tag: SlitTag, ValueBitMask: SlitValueBitMask),
    new(Id: 9, UnsignedShiftLeftMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 10, SignedShiftRightMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 11, UnsignedShiftRightMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 12, AddMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 13, SubtractMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 14, AndMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 15, XorMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 16, OrMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 17, InvertMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 18, IncrementMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 19, DecrementMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 20, EnterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506EnterTag, ValueBitMask: Node506FrameValueBitMask),
    new(Id: 21, AdjustMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: AdjustTag, ValueBitMask: Node606ValueBitMask),
    new(Id: 22, StoreLocalMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506StoreLocalTag, ValueBitMask: Node506FrameValueBitMask),
    new(Id: 23, StoreParameterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506StoreParameterTag, ValueBitMask: Node506FrameValueBitMask),
    new(Id: 24, LoadLocalMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506LoadLocalTag, ValueBitMask: Node506FrameValueBitMask),
    new(Id: 25, LoadParameterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506LoadParameterTag, ValueBitMask: Node506FrameValueBitMask),
    new(Id: 26, LoadAddressOfLocalMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: LoadAddressOfLocalTag, ValueBitMask: Node606ValueBitMask),
    new(Id: 27, LoadAddressOfParameterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: LoadAddressOfParameterTag, ValueBitMask: Node606ValueBitMask),
    new(Id: 28, LeaveMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 29, EqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 30, EqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 31, FalseMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 32, TrueMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 33, NotEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 34, NotEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 35, UnsignedGreaterThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 36, GreaterThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 37, GreaterThanZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 38, GreaterOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 39, GreaterOrEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 40, UnsignedLessOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 41, LessOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 42, LessOrEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 43, LessThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 44, LessThanZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 45, UnsignedLessThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 46, UnsignedGreaterOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 47, MultiplyByTwoMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 48, UnsignedDivideByTwoMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 49, DivideByTwoMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 50, AbsoluteValueMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 51, NegateMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 52, ExchangeTMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 53, LoadTMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 54, StoreTMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 55, BitCountMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 56, ZeroExtendMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 57, AddWithCarryMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 58, LoadDMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 59, StoreDMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 60, ExchangeDMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 61, MultiplyByTwoDoubleMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 62, DivideByTwoDoubleMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 63, SignExtendMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 64, UnsignedMultiplyDoubleMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 65, ExchangePortMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 66, PortWriteMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 67, PortReadMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 68, LoadHighMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 69, LoadLowMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 70, StoreHighMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 71, StoreLowMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 72, HaltMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 73, LongCallMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 74, LongJumpMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 75, LoadGlobalMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 76, StoreGlobalMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 77, NegMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 78, LitMnemonic, 1, CvmOperandEncoding.EmbeddedSignedValue, Tag: LitTag, ValueBitMask: LitValueBitMask),
    new(Id: 79, ParityMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 80, OddMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 81, NotMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 82, ReverseSubtractMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 83, ReverseShiftLeftMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 84, ReverseShiftRightMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 85, ReverseUnsignedShiftRightMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 86, AddConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 87, SubtractConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 88, ReverseSubtractConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 89, AndConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 90, XorConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 91, OrConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 92, ReverseShiftLeftConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 93, UnsignedShiftLeftConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 94, ReverseShiftRightConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 95, SignedShiftRightConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 96, ReverseUnsignedShiftRightConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 97, UnsignedShiftRightConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // Id 98 (clbr) and Id 100 (cljmp) are retired -- removed 2026-09-06 per Stefan ("I remove clbr and
    // cljmp from node 407. remove it also from the CVM assembler and disassembler"), before either was
    // confirmed on real hardware. Never reuse either Id for a different instruction.
    new(Id: 99, LongBranchMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // Node 306's six address-register ops -- self-describing (EmbeddedUnsignedValue, WordLength 1, no
    // node/linker resolution), a 2-bit register-index operand at bits 2-1 (ValueBitShift: 1) -- see
    // LoadAddressRegisterMnemonic's own remarks, including the FLAGGED collision with SlitTag's own
    // 0xD000-0xDFFF range.
    new(Id: 101, LoadAddressRegisterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node306LoadAddressRegisterTag, ValueBitMask: Node306AddressRegisterIndexBitMask, ValueBitShift: Node306AddressRegisterIndexShift),
    new(Id: 102, StoreAddressRegisterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node306StoreAddressRegisterTag, ValueBitMask: Node306AddressRegisterIndexBitMask, ValueBitShift: Node306AddressRegisterIndexShift),
    new(Id: 103, IncrementAddressRegisterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node306IncrementAddressRegisterTag, ValueBitMask: Node306AddressRegisterIndexBitMask, ValueBitShift: Node306AddressRegisterIndexShift),
    new(Id: 104, DecrementAddressRegisterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node306DecrementAddressRegisterTag, ValueBitMask: Node306AddressRegisterIndexBitMask, ValueBitShift: Node306AddressRegisterIndexShift),
    new(Id: 105, LoadAddressRegisterValueMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node306LoadAddressRegisterValueTag, ValueBitMask: Node306AddressRegisterIndexBitMask, ValueBitShift: Node306AddressRegisterIndexShift),
    new(Id: 106, StoreAddressRegisterValueMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node306StoreAddressRegisterValueTag, ValueBitMask: Node306AddressRegisterIndexBitMask, ValueBitShift: Node306AddressRegisterIndexShift),
    // Node 511's four register-file ops -- CvmOperandEncoding.NodeResolvedEmbeddedValue (needs a live
    // compile of node 511 AND an embedded register operand; see that encoding's own remarks). Tag,
    // ValueBitMask, and ValueBitShift are all left at their defaults here (0) -- unlike node 306's
    // self-describing six, this shape's actual tag/field-layout combination only ever happens in
    // Ga144.Evb.Ide.Services.CvmAssemblyLanguage against a live compile, never from this table alone.
    new(Id: 107, LoadRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
    new(Id: 108, StoreRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
    new(Id: 109, PopRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
    new(Id: 110, PushRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
  ];

  private static readonly IReadOnlyDictionary<string, CvmInstructionShape> ByMnemonic =
      Instructions.ToDictionary(instruction => instruction.Mnemonic, StringComparer.OrdinalIgnoreCase);

  /// <summary>Looks up a mnemonic's shape case-insensitively. Null when it isn't a known CVM instruction (it may still be a valid label/import reference -- that's the caller's concern, not this lookup's).</summary>
  public static CvmInstructionShape? TryGetShape(string mnemonic) =>
      ByMnemonic.TryGetValue(mnemonic, out CvmInstructionShape? shape) ? shape : null;

  /// <summary>Looks up an instruction by its stable ID -- the inverse of encoding a <c>0x8000 | Id</c> placeholder, useful for a tool that wants to describe an unlinked object file's raw words without consulting its relocation table.</summary>
  public static CvmInstructionShape? TryGetShapeById(int id) =>
      Instructions.FirstOrDefault(shape => shape.Id == id);

  /// <summary>Sign-extends the low bits of <paramref name="word"/> selected by <paramref name="valueBitMask"/> -- the shared arithmetic behind <see cref="DecodeBranchOffset"/> and <see cref="DecodeSlitValue"/>.</summary>
  private static int DecodeSignedField(int word, int valueBitMask)
  {
    int raw = word & valueBitMask;
    int maxPositive = valueBitMask >> 1;
    return raw > maxPositive ? raw - (valueBitMask + 1) : raw;
  }

  /// <summary>
  /// Extracts a branch/conditional-branch word's signed offset field, sign-extending its low 11 bits.
  /// This only recovers the raw offset, not an absolute target address -- doing that also needs the
  /// branch word's OWN address, since real hardware resolves the target as
  /// <c>(this word's own address + 1) + offset</c> (confirmed against a real <c>br 1</c> run -- see
  /// <see cref="CvmOperandEncoding.EmbeddedSignedValue"/>'s own remarks), not as an offset from the
  /// word's own address.
  /// </summary>
  public static int DecodeBranchOffset(int word) => DecodeSignedField(word, BranchOffsetBitMask);

  /// <summary>Extracts a <c>slit</c> word's signed value field, sign-extending its low 12 bits. Unlike <see cref="DecodeBranchOffset"/>, this IS the whole answer -- a <c>slit</c> value isn't relative to anything.</summary>
  public static int DecodeSlitValue(int word) => DecodeSignedField(word, SlitValueBitMask);

  /// <summary>Extracts a <c>lit</c> word's signed value field, sign-extending its low 10 bits. Like <see cref="DecodeSlitValue"/> (not <see cref="DecodeBranchOffset"/>), this IS the whole answer.</summary>
  public static int DecodeLitValue(int word) => DecodeSignedField(word, LitValueBitMask);

  /// <summary>Extracts one of node 606's eight ops' unsigned value field -- its low 8 bits, taken as-is (never sign-extended, unlike <see cref="DecodeBranchOffset"/>/<see cref="DecodeSlitValue"/>).</summary>
  public static int DecodeNode606Value(int word) => word & Node606ValueBitMask;

  /// <summary>
  /// The one shared operand rendering for every disassembled instruction that carries a numeric value,
  /// tagged or self-describing alike -- fixed 2026-09-05 per Stefan ("i would prefer a unified
  /// presentation") after the memory inspector showed <c>lit</c> in decimal only, <c>call</c> in hex
  /// only, and a tagged trailing-word mnemonic (<c>andi</c>) in hex only, three different conventions
  /// side by side in the same listing. Always renders as <c>0x{4-digit hex} ({decimal})</c>: the hex
  /// half is <paramref name="value"/>'s raw 16-bit two's-complement bit pattern (matching
  /// <see cref="CvmWordCodec.WordMask"/>, so a negative <paramref name="value"/> -- e.g. a backward
  /// <c>br</c> offset or a negative <c>lit</c>/<c>slit</c> -- wraps to its own high hex digits exactly
  /// as it would sit in the word, rather than printing a minus sign into the hex half), the decimal half
  /// is <paramref name="value"/> itself, signed exactly as each caller's own <c>Decode*</c> method
  /// already returns it. Every self-describing case in <see cref="TryDescribeSelfDecodingWord"/> and the
  /// tagged-mnemonic trailing-word case in Ga144.Evb.Ide.Services.CvmAssemblyLanguage.DisassemblePage0
  /// route through this one method now, so the listing can never again show two different operand
  /// conventions in the same column.
  /// </summary>
  public static string FormatOperand(int value) => $"0x{value & 0xFFFF:X4} ({value})";

  /// <summary>
  /// ADDED 2026-09-09, alongside <see cref="TryDescribeSelfDecodingWord"/>'s new <c>wordAddress</c>
  /// parameter (see that method's own remarks for why): renders <c>" -&gt; 0x{4-digit hex}"</c> for a
  /// <c>br</c>/<c>ifbr</c> word's RESOLVED absolute target -- <c>(wordAddress + 1) + offset</c>, per
  /// <see cref="DecodeBranchOffset"/>'s own remarks -- or an empty string when <paramref
  /// name="wordAddress"/> is null (the caller didn't have one to give, so there's nothing to resolve).
  /// Deliberately NOT run through <see cref="FormatOperand"/>: that method's decimal half assumes a
  /// SIGNED 16-bit value (right for an offset, wrong for an address -- a target at or past 0x8000 would
  /// misprint as negative), and CVM addresses can in principle exceed 16 bits entirely (the simulated
  /// SRAM is a 20-bit, 1 Mword space -- see <see cref="Ga144.Evb.Ide.Services.CvmSimulatedSram"/>'s own
  /// remarks -- even though a normal page-0 program never reaches that far), so this always prints a
  /// plain hex address, at least 4 digits, never a parenthesized decimal.
  /// </summary>
  private static string DescribeBranchTarget(int? wordAddress, int offset) =>
      wordAddress is int address ? $" -> 0x{address + 1 + offset:X4}" : string.Empty;

  /// <summary>
  /// Decodes a single already-fetched CVM word using ONLY the two self-describing encodings
  /// (<see cref="CvmOperandEncoding.EmbeddedAddress"/>, <see cref="CvmOperandEncoding.EmbeddedSignedValue"/>)
  /// -- the ones fully determined by the word's own bit pattern, needing no live F18 compile at all to
  /// recognize (unlike the tagged/<c>0x8000 | address</c> family, whose real opcode values only exist
  /// once resolved against a specific compile -- see the IDE's own
  /// Ga144.Evb.Ide.Services.CvmAssemblyLanguage.BuildDecodeTable for that half). Returns null when the
  /// word matches none of those patterns, letting the caller fall back to that live, compile-specific
  /// table next.
  ///
  /// <paramref name="wordAddress"/> (ADDED 2026-09-09) is this word's own flat address, needed ONLY to
  /// also print <c>br</c>/<c>ifbr</c>'s RESOLVED absolute target next to their raw offset -- per
  /// <see cref="DecodeBranchOffset"/>'s own remarks, the offset alone is not a target address; it must
  /// be combined with the word's own address to become one. Added per Stefan asking why the linker
  /// placed "__exit" at "location 0x200" (it doesn't -- see <see cref="CvmLinker"/>'s own entry-layout
  /// remarks: __exit correctly resolves to address 1; "0x0200 (512)" in a disassembly of __exit's own
  /// <c>br</c> word is that word's raw OFFSET field, not __exit's own address or its target's), which
  /// is exactly the confusion this optional target annotation exists to prevent from recurring. Pass
  /// null (the default) to keep the OLD offset-only rendering -- e.g. for a word decoded with no
  /// meaningful address of its own (an unlinked object's raw word, say).
  /// </summary>
  public static string? TryDescribeSelfDecodingWord(int word, int? wordAddress = null)
  {
    if (word <= CallAddressMask)
    {
      return $"{CallMnemonic} {FormatOperand(word)}";
    }

    int branchTag = word & BranchTagMask;
    if (branchTag == BranchTag)
    {
      return $"{BranchMnemonic} {FormatOperand(DecodeBranchOffset(word))}{DescribeBranchTarget(wordAddress, DecodeBranchOffset(word))}";
    }

    if (branchTag == ConditionalBranchTag)
    {
      return $"{ConditionalBranchMnemonic} {FormatOperand(DecodeBranchOffset(word))}{DescribeBranchTarget(wordAddress, DecodeBranchOffset(word))}";
    }

    if ((word & SlitTagMask) == SlitTag)
    {
      return $"{SlitMnemonic} {FormatOperand(DecodeSlitValue(word))}";
    }

    if ((word & LitTagMask) == LitTag)
    {
      return $"{LitMnemonic} {FormatOperand(DecodeLitValue(word))}";
    }

    // Every EmbeddedUnsignedValue shape, self-describing the same way: a fixed tag OR'd with an
    // unsigned value in the low ValueBitMask bits. Matched generically against every such shape in
    // Instructions rather than one if-check per mnemonic, so a new one added there later needs no
    // change here. IMPORTANT (2026-09-02): this mask/width is now taken from EACH shape's OWN
    // ValueBitMask (derived as ~shape.ValueBitMask), not a single hardcoded width -- node 606's eight
    // ops (adjust/stl/stp/ldl/ldp/lal/lap, still 8-bit tag/8-bit value, Node606TagMask/
    // Node606ValueBitMask) and node 506's enter (7-bit tag/9-bit value, Node506EnterTag/
    // Node506FrameValueBitMask) now genuinely differ in width, so the OLD single-hardcoded-mask version
    // of this loop (word & Node606TagMask for every shape) would have decoded enter's own 9-bit value
    // one bit short. Node606TagMask/DecodeNode606Value themselves are unchanged and still correct for
    // node 606's own seven still-8-bit ops.
    //
    // NOTE (STALE as of 2026-09-09, kept for the paper trail): this loop used to be unreachable for
    // "enter" specifically -- br's own self-describing check above (branchTag == BranchTag) matched
    // FIRST for every word in 0x9000-0x97FF, which was Node506EnterTag's entire range too (0x9200's own
    // top 5 bits equalled br's OLD tag). Per Stefan (2026-09-02, "ignore the ranges of br/ifbr... give
    // me now enter and leave mnemonics") this collision was accepted for a while: Assemble() always
    // emitted the correct 0x9200|offset word for "enter" (encoding dispatches by mnemonic string, never
    // through this method), but disassembling that same word back used to report "br <offset>" instead.
    // PARTIALLY RESOLVED 2026-09-09: br's own tag moved to 0x8000 (see BranchTag's own remarks, "'br' is
    // wrongly encoded"), so branchTag no longer equals BranchTag for 0x9200 -- this loop should now
    // actually be reached for "enter", not yet re-confirmed against a live disassembly. ifbr's own tag
    // is DELIBERATELY UNCHANGED (still 0x9800 -- see ConditionalBranchTag's own remarks), so
    // 0x9800/0x9A00/0x9C00/0x9E00 (stp/stl/ldp/ldl) still match branchTag == ConditionalBranchTag first,
    // exactly as before -- still unreachable here, still unresolved.
    //
    // SAME SITUATION, WORSE, for node 306's six ops (ldar/star/inca/deca/lda/sta, 0xD800-0xDBFF): this
    // loop is currently UNREACHABLE for all six, since the SlitTag check just above matches FIRST for
    // every word in 0xD000-0xDFFF, which fully contains 0xD800-0xDBFF. Unlike enter (which at least
    // shares its collision with only one other mnemonic, br), node 306's ops have no live node/symbol to
    // fall back on at all -- see LoadAddressRegisterMnemonic's own remarks for the full flag. Assembling
    // ldar/star/inca/deca/lda/sta by name is unaffected; disassembling any word in their range currently
    // always reports "slit <value>" instead.
    foreach (CvmInstructionShape shape in Instructions)
    {
      if (shape.Encoding != CvmOperandEncoding.EmbeddedUnsignedValue)
      {
        continue;
      }

      int tagMask = ~shape.ValueBitMask & 0xFFFF;
      if ((word & tagMask) == shape.Tag)
      {
        return $"{shape.Mnemonic} {FormatOperand((word & shape.ValueBitMask) >> shape.ValueBitShift)}";
      }
    }

    return null;
  }
}