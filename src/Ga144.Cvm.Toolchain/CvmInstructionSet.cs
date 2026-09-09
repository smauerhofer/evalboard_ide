namespace Ga144.Cvm.Toolchain;

/// <summary>
/// The CVM assembly language's own instruction set -- Stefan's mnemonics (<c>nop</c>,
/// <c>pushlit &lt;data&gt;</c>, <c>push</c>, <c>pop</c>, <c>call &lt;address&gt;</c>, <c>ret</c>,
/// <c>br &lt;offset&gt;</c>, <c>cbr &lt;offset&gt;</c>, plus node 507's ALU
/// ops -- <c>usl</c>, <c>ssr</c>, <c>usr</c>, <c>add</c>, <c>sub</c>, <c>and</c>, <c>xor</c>, <c>or</c>
/// (binary: register r and the top of the CVM data stack), and <c>inv</c>, <c>inc</c>, <c>dec</c>
/// (unary: register r alone), plus node 606's frame-pointer-management ops -- <c>enter &lt;locals&gt;</c>,
/// <c>adjust &lt;offset&gt;</c>, <c>stl &lt;offset&gt;</c>, <c>stp &lt;offset&gt;</c>,
/// <c>ldl &lt;offset&gt;</c>, <c>ldp &lt;offset&gt;</c> (each self-describing, an 8-bit tag OR'd with an
/// 8-bit unsigned value -- <c>lal</c>/<c>lap</c>, the family's other two, were RETIRED 2026-09-09 in
/// favor of node 506's own <c>f</c>/<c>fpush</c> plus explicit offset arithmetic, see
/// <see cref="LoadAddressOfLocalTag"/>'s own remarks), plus
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
/// <c>cbr</c> (a fixed 6-bit tag OR'd with a 10-bit signed value), its own DIFFERENT tag
/// and field width again -- see <see cref="LitTag"/>'s own remarks. <c>lit</c> is now the CVM's only
/// self-describing literal-load mnemonic: the older, wider <c>slit</c> (12-bit value, node 507's own
/// "1101" dispatch class) was RETIRED 2026-09-09 in its favor -- "'slit' is replaced by 'lit'. remove it
/// from the language." -- see <see cref="SlitTag"/>'s own remarks), plus node 509's tenth and eleventh
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
/// plus a full opcode/assembler-vs-node reconciliation audit (2026-09-09, per Stefan's own request:
/// "there is a mismatch of the opcodes in the assembler and the opcodes provided by the nodes in the
/// CVM2. remove all opcodes not present in any node and add all opcodes defined in the nodes, but not yet
/// available in the assembler") that read every CVM2 node's own current F18 source end to end and
/// reconciled it against this table: node 507's remaining three tick-labeled opcodes, <c>jump</c>/
/// <c>xs</c>/<c>xp</c>, were ADDED (see <see cref="JumpMnemonic"/>'s own remarks); and every mnemonic the
/// audit found with no backing F18 symbol on any node in the current mesh AND no active dependent
/// elsewhere in this toolchain was RETIRED from <see cref="Instructions"/> (kept as a constant, per "do
/// not remove any opcodes") -- which turned out to be only node 511's own four register-file ops
/// (<c>rld</c>/<c>rst</c>/<c>rpop</c>/<c>rpush</c>), whose F18 symbols disappeared from node 511's own
/// source in its 2026-09-08 re-sync (its <c>r/main</c> now inlines all four operations directly rather
/// than naming them -- see <see cref="LoadRegisterFileMnemonic"/>'s own remarks; FLAGGED -- Stefan should
/// confirm whether this was intentional before these four are permanently abandoned rather than
/// un-retired). Every OTHER orphaned mnemonic this audit found turned out to have an active dependent
/// elsewhere in the codebase and was deliberately left in place rather than removed: the remaining eight
/// of node 508's old 27-op family (<c>ugt</c>/<c>ule</c>/<c>ult</c>/<c>uge</c>/<c>negate</c>/<c>xt</c>/
/// <c>ldt</c>/<c>stt</c>) are still actively emitted by <see cref="Ga144.C.Toolchain.CCodeGenerator"/>'s
/// own codegen (unsigned comparisons, unary minus, pointer dereference -- see
/// <see cref="UnsignedGreaterThanMnemonic"/>'s own remarks), and all nine of node 506's old register-d
/// family (<c>zext</c> through <c>umuld</c>) plus five of node 407's old seven register-w/port ops
/// (<c>xpt</c>/<c>ldhi</c>/<c>ldlo</c>/<c>sthi</c>/<c>stlo</c>) are still assembled by
/// <see cref="Ga144.Evb.Ide.Cvm.CvmDebuggerDefaultProgram"/>'s own hand-written default test program (see
/// <see cref="ZeroExtendMnemonic"/>'s own remarks). Every already-wired
/// tagged mnemonic (<c>tjmp</c>, node 406's/408's/509's repointed families, node 306's six self-describing
/// ops) was re-verified against each node's own current source during this same audit and found already
/// consistent -- see Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own <c>NodeSymbolByMnemonic</c> for the
/// live wiring this audit checked. Nodes 606/607/608/707/708 were confirmed OUT OF SCOPE for this audit --
/// they are a separate SRAM-request/test-cluster subsystem with no entries in <c>NodeSymbolByMnemonic</c>
/// at all (node 607's own remarks say so explicitly), not part of the CVM2 opcode-implementing mesh
/// (306/307/406/407/408/506/507/508/509/510/511) this table's tagged mnemonics resolve against.
/// For each instruction: how many words it occupies once assembled, how its
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
  public const string ConditionalBranchMnemonic = "cbr";

  /// <summary>RETIRED 2026-09-09 ("'slit' is replaced by 'lit'. remove it from the language.") -- kept, per "do not remove any opcodes," but no longer referenced by <see cref="Instructions"/>. See <see cref="SlitTag"/>'s own remarks; use <see cref="LitMnemonic"/> instead.</summary>
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
  // like call/br/cbr/slit -- NOT like the tagged nop/push/pop/ret/ALU family above): a fixed 8-bit tag
  // (bits 15-8, pattern 1010_1nnn) OR'd with an UNSIGNED 8-bit offset/count (bits 7-0, 0x00-0xFF). This
  // is a genuinely different shape from br/cbr's EmbeddedSignedValue -- the table gives every one
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
  /// <summary>RETIRED 2026-09-09 -- see <see cref="CvmInstructionSet.LoadAddressOfLocalTag"/>'s own remarks. Use <see cref="FrameToRegisterMnemonic"/>/<see cref="PushFrameMnemonic"/> plus offset arithmetic instead.</summary>
  public const string LoadAddressOfLocalMnemonic = "lal";

  /// <summary>RETIRED 2026-09-09 alongside <see cref="LoadAddressOfLocalMnemonic"/> -- see <see cref="CvmInstructionSet.LoadAddressOfLocalTag"/>'s own remarks.</summary>
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
  // remove any opcodes" -- see each superseded constant's own remarks). adjust remains permanently
  // orphaned -- node 506's own source has never named an equivalent. lal/lap, the other two long-orphaned
  // node-606 leftovers, were RETIRED outright 2026-09-09 rather than left orphaned -- see
  // LoadAddressOfLocalTag's own remarks.

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
  // -- per Stefan (2026-09-02): "ignore the ranges of br/cbr. ignore the overlapping ranges. give me
  // now enter and leave mnemonics." br/cbr have not been moved yet, so a word like 0x9038 currently
  // decodes as "br" (TryDescribeSelfDecodingWord checks self-describing shapes first) even though it is
  // also a valid 'leave opcode on node 506 -- this ambiguity is accepted for now, not a bug to silently
  // work around, and is expected to resolve once br/cbr's own new tag range is chosen.
  public const string LeaveMnemonic = "leave";

  // 'f/'fpush, added 2026-09-09 alongside the new node 506 source that also removes lal/lap (see
  // LoadAddressOfLocalTag's own remarks): "'lal' & 'lap' are removed. use ''f' or 'fpush' from node 506
  // and add the offset to calculate the address of a local or parameter. i will provide a new 506."
  // Shaped exactly like 'leave/'halt above -- a single bare TAGGED opcode word (CvmOperandEncoding.None),
  // reached the SAME way 'leave is (node 506's own f/main dispatch falling to "ex" once the fetched
  // word's top 7 bits read "1001_000", tag 0x9000 | address on node 506 -- see
  // Ga144.Evb.Ide.Services.CvmAssemblyLanguage.Node506LeaveTagBits's own remarks). 'f moves the frame
  // pointer f into register r; 'fpush pushes f onto the CVM data stack directly. Neither takes an
  // assembled operand -- computing "address of local/parameter N" is now the CALLER's job (push f via
  // fpush, push the offset via pushlit, then sub/add -- locals subtract, parameters add, matching node
  // 506's own f/main "inv" convention), not a single self-describing instruction the way lal/lap used to
  // be. See Ga144.C.Toolchain.CCodeGenerator's own remarks for the replacement codegen sequence.
  public const string FrameToRegisterMnemonic = "f";
  public const string PushFrameMnemonic = "fpush";

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

  // 'tjmp, node 507's own table-jump primitive -- always present in node 507's source ("'tjmp .loc
  // (rso-rs) a . + a!", identical body to node 507's own internal m/branch), but left unwired as a CVM
  // mnemonic until Stefan confirmed its location 2026-09-09 ("'tjmp is in node 507"). Shaped exactly like
  // 'nop/'push/'pop/'ret/'halt above -- a single bare TAGGED opcode word (CvmOperandEncoding.None),
  // resolved against node 507's own SAME "1000_1???" local-execute tag family
  // (Node507Cvm2LocalExecuteTagBits, 0x8800) those five already use. Per the source's own trailing
  // comment ("table jump to address in table with offset"): pops a signed offset already on the CVM data
  // stack and adds it directly to the program counter -- a computed jump, unlike br/cbr's own offset
  // embedded in the opcode word itself. NOT YET consumed by the C compiler's switch/case codegen --
  // wiring the mnemonic here only means gaasm/the disassembler now recognize it; see
  // Ga144.C.Toolchain.CCodeGenerator's own remarks on why switch still compiles to an if-chain pending a
  // confirmed calling convention (how the case table itself is laid out, bounds-checked, and indexed)
  // rather than just the opcode's own existence.
  public const string TableJumpMnemonic = "tjmp";

  // jump/xs/xp, node 507's own remaining three tick-labeled opcodes -- always present in node 507's
  // source ("'jump .loc (rs-rs) m/next a! ;", "'xs .loc (rs-rs) over ;", "'xp .loc (rs-rs) >r a over a!
  // r> ;") but left unwired pending this 2026-09-09 opcode/assembler-vs-node reconciliation audit ("there
  // is a mismatch of the opcodes in the assembler and the opcodes provided by the nodes in the CVM2 ...
  // add all opcodes defined in the nodes, but not yet available in the assembler"). Per the source's own
  // trailing comment: 'jump ("jump to address in next word") fetches a trailing word via m/next and
  // loads it directly into A (the CVM program counter) -- shaped EXACTLY like pushlit/lcall/ljmp/ldg/stg
  // (CvmOperandEncoding.TrailingWord, one tagged opcode word then one trailing operand word), resolved
  // against node 507's own SAME "1000_1???" local-execute tag family (Node507Cvm2LocalExecuteTagBits,
  // 0x8800) tjmp/nop/push/pop/ret/halt already use. 'xs ("exchange s and r") and 'xp ("exchange p and r")
  // take no assembled operand at all -- shaped exactly like nop/push/pop/ret/halt/tjmp
  // (CvmOperandEncoding.None), same tag family. None of the three has been confirmed on real hardware.
  public const string JumpMnemonic = "jump";
  public const string ExchangeSMnemonic = "xs";
  public const string ExchangePMnemonic = "xp";

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
  //
  // FLAGGED 2026-09-09 (opcode/assembler-vs-node reconciliation audit, per Stefan's own "remove all
  // opcodes not present in any node"): these same eight are the ONLY orphaned mnemonics in this whole
  // table that this audit found still ACTIVELY EMITTED by Ga144.C.Toolchain.CCodeGenerator's own codegen
  // -- "ugt"/"ule"/"ult"/"uge" for every unsigned comparison, "negate" for unary minus, and "xt"/"ldt"/
  // "stt" for every pointer dereference (load/store through an lvalue address). Deleting their Instructions
  // rows per the letter of "remove all opcodes not present in any node" would silently break C compilation
  // for any program using an unsigned comparison, a unary minus, or a pointer dereference -- effectively
  // all of them. Kept exactly as before this audit rather than removed; this is flagged for Stefan rather
  // than resolved unilaterally, since the real fix is presumably a future node implementing this
  // comparison-tail/pointer-dereference family (or the C compiler switching to some other already-wired
  // primitive), neither of which this audit can decide on its own.
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
  //
  // FLAGGED 2026-09-09 (opcode/assembler-vs-node reconciliation audit, per Stefan's own "remove all
  // opcodes not present in any node"): CVM1's old node 506 (this family's own implementing node) was
  // deleted 2026-09-01 and its coordinate reused for CVM2's stack-frame node, which never defined any of
  // these nine F18 symbols -- Services.CvmAssemblyLanguage's own NodeSymbolByMnemonic never had entries
  // for them either. That would make all nine a clean removal under the letter of "remove all opcodes not
  // present in any node" -- except this exact nine-mnemonic sequence is still assembled, word for word, by
  // Cvm.CvmDebuggerDefaultProgram's own hand-written default test program (confirmed against real hardware
  // per that class's own remarks, though that confirmation predates the CVM1->CVM2 rewrite and so verified
  // CVM1's old node 506, not anything a current CVM2 board runs). Deleting these rows would break that
  // program's own assembly without making anything true about the current mesh either way, so they are
  // kept exactly as before this audit -- see UnsignedGreaterThanMnemonic's own remarks for node 508's
  // parallel case.
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
  //
  // FLAGGED 2026-09-09 (opcode/assembler-vs-node reconciliation audit), same situation as node 506's
  // register-d family just above: CVM1's old node 407 (this family's own implementing node) was deleted
  // 2026-09-01 and its coordinate reused for CVM2's long-call/long-jump/long-branch helper, which never
  // defined any of these seven F18 symbols, and NodeSymbolByMnemonic never had entries for them either --
  // but five of the seven (xpt/ldhi/ldlo/sthi/stlo; 'in'/'out' are deliberately excluded by Stefan's own
  // choice, see Cvm.CvmDebuggerDefaultProgram's own remarks) are still assembled by that same
  // hand-written default test program, confirmed against real (pre-CVM2) hardware. Kept exactly as before
  // this audit for the same reason as ZeroExtendMnemonic's own family -- see that constant's remarks.
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
  // RESOLVED 2026-09-09 (was FLAGGED): this whole family sits at 0xD800-0xDBFF, which used to be
  // squarely inside SlitTag's own then-active 0xD000-0xDFFF range (Stefan's own "1101 xxxx xxxx xxxx
  // ... slit" table), so TryDescribeSelfDecodingWord checking slit BEFORE the generic
  // EmbeddedUnsignedValue loop these six mnemonics rely on used to completely shadow all six in
  // disassembly (a word like 0xD800 ("sta 0") showed as "slit -2048" in the memory inspector/debugger,
  // never as "sta 0"). Stefan retired slit outright the same day ("'slit' is replaced by 'lit'. remove
  // it from the language." -- see SlitTag's own remarks), which removed the shadowing check entirely --
  // these six now decode correctly through the generic loop with nothing left in 0xD000-0xDFFF to
  // collide with. Assembling FROM a mnemonic (ldar/star/inca/deca/lda/sta by name) was always unaffected
  // by this -- only disassembly was ever ambiguous.
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
  //
  // RETIRED 2026-09-09 (opcode/assembler-vs-node audit, per Stefan's own "remove all opcodes not present
  // in any node") -- FLAGGED, not a routine retirement like the ones above. Node 511's own source was
  // re-synced verbatim to Stefan's current live project source on 2026-09-08 (see Node511Program's own
  // remarks), and its r/main dispatch no longer defines four separate tick-prefixed words for these
  // operations at all: the load/store/pop/push bodies are now inlined directly into r/main's own 2-bit
  // dispatch cascade, selected by tag bits rather than by resolving a named F18 symbol's address, so there
  // is nothing left for LoadRegisterFileMnemonic/StoreRegisterFileMnemonic/PopRegisterFileMnemonic/
  // PushRegisterFileMnemonic to resolve against under Stefan's own tick-naming rule. Whether this is a
  // deliberate redesign (32-register parameter passing folded into a different encoding) or an artifact of
  // the resync losing a since-reverted structure is NOT established either way -- reproduced and retired
  // exactly per the letter of the current instruction, not silently kept on the strength of the 2026-09-07
  // history above. All four are removed from Instructions below, mnemonic constants kept per "do not
  // remove any opcodes"; Stefan should confirm before these are un-retired.
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
  // exactly like br/cbr/slit -- see LitTag's own remarks for the full bit derivation.
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
  //   1001 1xxx xxxx xxxx   -0x400..0x3FF   ifbr (conditional branch, signed offset -- OLD, UNCONFIRMED
  //                                          placeholder mnemonic, RENAMED AND RE-ENCODED, see below)
  // NEW (2026-09-09) table -- br only, per Stefan's own words above:
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
  // CONFIRMED, RENAMED 2026-09-09 (later the same day): Stefan retired the old "ifbr" placeholder
  // mnemonic and its guessed 0x9800 tag outright -- "\"ifbr\" no longer exist and should be replaced
  // with \"cbr\". it is basically the same. opcode cbr 1010_11??_????_???? conditional branch to offset
  // if r == 0. the offset is signed 10-bit number." This is a real, hardware-confirmed bit pattern, not
  // a guess: a fixed 6-bit tag (bits 15-10, binary 101011) OR'd with a 10-bit two's-complement signed
  // offset (bits 9-0); -0x200..0x1FF is exactly a 10-bit signed value's own range, confirming the field
  // width. Semantically identical to old "ifbr" (a conditional branch on register r), just a new name,
  // a new tag, AND a narrower offset field (10 bits, not 11) -- so cbr does NOT share BranchTag/
  // BranchTagMask/BranchOffsetBitMask with br any more; see ConditionalBranchTag/
  // ConditionalBranchTagMask/ConditionalBranchOffsetBitMask below, its own dedicated set.
  //
  // Collision-wise this is a full swap, not just a fix: cbr's new tag range (0xAC00-0xAFFF) no longer
  // overlaps Node506StoreParameterTag/Node506StoreLocalTag/Node506LoadParameterTag/Node506LoadLocalTag
  // (0x9800-0x9FFF) at all -- that long-standing, previously "unresolved, needs Stefan's own ifbr bit
  // pattern" collision is GONE. 0xAC00-0xAFFF also used to fully contain node 606's own OLD
  // LoadAddressOfLocalTag (0xAE00, mnemonic lal)/LoadAddressOfParameterTag (0xAF00, mnemonic lap) range,
  // trading one collision for another -- RESOLVED FOR GOOD 2026-09-09, later the same day: Stefan retired
  // lal/lap outright ("'lal' & 'lap' are removed. use ''f' or 'fpush' from node 506 and add the offset to
  // calculate the address of a local or parameter.") rather than giving them their own confirmed tag, so
  // there is no longer anything in Instructions for cbr's range to collide with at all -- see
  // LoadAddressOfLocalTag's own remarks.

  /// <summary>
  /// The fixed high-bit pattern (bits 15-11) of a <c>br</c> word: binary 10000. CHANGED 2026-09-09 from
  /// binary 10010 (0x9000) -- see this file's own remarks just above for Stefan's correction, the exact
  /// wording that prompted it, and the independent node-507-dispatch derivation that agrees with it.
  /// Used ONLY for <c>br</c> now -- <c>cbr</c> has its own, narrower <see cref="ConditionalBranchTag"/>
  /// (the two mnemonics no longer share a tag/offset width; see this file's own remarks above).
  /// </summary>
  public const int BranchTag = 0x8000;

  /// <summary>
  /// The fixed high-bit pattern (bits 15-10) of a <c>cbr</c> word: binary 101011 (0xAC00). CONFIRMED
  /// AND RENAMED 2026-09-09, replacing the old, unconfirmed "ifbr" placeholder (which guessed 0x9800,
  /// a 5-bit tag) -- see this file's own remarks just above for Stefan's exact wording. Do NOT reuse
  /// the old 0x9800 value: that guess is retired along with the "ifbr" name.
  /// RESOLVED 2026-09-09 (same day): this range (0xAC00-0xAFFF) briefly, fully contained
  /// <see cref="LoadAddressOfLocalTag"/> (<c>lal</c>, 0xAE00) and <see cref="LoadAddressOfParameterTag"/>
  /// (<c>lap</c>, 0xAF00) -- both then still wired in <see cref="Instructions"/>, both permanently-orphaned
  /// CVM1 node-606 leftovers with no node-506 replacement ever named. Stefan retired both outright the
  /// same day rather than giving them a real tag ("'lal' & 'lap' are removed. use ''f' or 'fpush' from
  /// node 506 and add the offset to calculate the address of a local or parameter.") -- see
  /// <see cref="Instructions"/>'s own remarks on the retired Ids. With lal/lap gone entirely, cbr's range
  /// no longer collides with anything.
  /// </summary>
  public const int ConditionalBranchTag = 0xAC00;

  /// <summary>Isolates a word's top 5 bits, for testing against <see cref="BranchTag"/> only -- <c>cbr</c> uses the narrower <see cref="ConditionalBranchTagMask"/> instead (see <see cref="ConditionalBranchTag"/>'s own remarks on why the two no longer share a width).</summary>
  public const int BranchTagMask = 0xF800;

  /// <summary>Isolates a word's low 11 bits -- the raw (not yet sign-extended) <c>br</c> offset field. <c>cbr</c> uses the narrower <see cref="ConditionalBranchOffsetBitMask"/> instead.</summary>
  public const int BranchOffsetBitMask = 0x7FF;

  /// <summary>The most negative offset an 11-bit two's-complement field (<c>br</c>'s own) can hold: -0x400 (-1024).</summary>
  public const int BranchOffsetMinValue = -0x400;

  /// <summary>The largest offset an 11-bit two's-complement field (<c>br</c>'s own) can hold: 0x3FF (1023).</summary>
  public const int BranchOffsetMaxValue = 0x3FF;

  /// <summary>Isolates a word's top 6 bits, for testing against <see cref="ConditionalBranchTag"/>. Added 2026-09-09 alongside <c>cbr</c>'s real bit pattern -- <c>cbr</c>'s tag is one bit wider than <c>br</c>'s, so it cannot reuse <see cref="BranchTagMask"/>.</summary>
  public const int ConditionalBranchTagMask = 0xFC00;

  /// <summary>Isolates a word's low 10 bits -- the raw (not yet sign-extended) <c>cbr</c> offset field. Added 2026-09-09 alongside <c>cbr</c>'s real bit pattern.</summary>
  public const int ConditionalBranchOffsetBitMask = 0x3FF;

  /// <summary>The most negative offset a 10-bit two's-complement field (<c>cbr</c>'s own) can hold: -0x200 (-512). Added 2026-09-09.</summary>
  public const int ConditionalBranchOffsetMinValue = -0x200;

  /// <summary>The largest offset a 10-bit two's-complement field (<c>cbr</c>'s own) can hold: 0x1FF (511). Added 2026-09-09.</summary>
  public const int ConditionalBranchOffsetMaxValue = 0x1FF;

  // slit's own encoding, straight from Stefan's bit-pattern table:
  //   1101 xxxx xxxx xxxx   -0x800..0x7FF   slit (literal, signed value)
  // -- a fixed 4-bit tag (bits 15-12) OR'd with a 12-bit two's-complement signed value (bits 11-0).
  // -0x800..0x7FF is exactly a 12-bit signed value's own range, confirming the field width. Unlike
  // br/cbr's offset, slit's value isn't an address computation at all: per Stefan, executing a slit
  // word loads its signed value directly into the F18 interpreter's own R register (node 607's own
  // runtime behavior, not something this toolchain project implements or needs to know how to do).
  //
  // RETIRED 2026-09-09, per Stefan: "'slit' is replaced by 'lit'. remove it from the language." Unlike
  // the ifbr -> cbr rename (a real replacement tag), slit's role is simply absorbed into the ALREADY-
  // EXISTING, already-confirmed node-509 lit (see LitTag's own remarks) -- a narrower 10-bit field, not
  // a like-for-like swap. Removed from Instructions and from TryDescribeSelfDecodingWord's own decode
  // checks (see that method's own remarks); this constant, its mask/value-range siblings just below, and
  // SlitMnemonic/DecodeSlitValue are all KEPT, per "do not remove any opcodes" -- Id 8 (formerly
  // SlitMnemonic) is retired and must never be reused for a different instruction (see Instructions'
  // own remarks).

  /// <summary>The fixed high-bit pattern (bits 15-12) of a <c>slit</c> word: binary 1101. RETIRED 2026-09-09 -- see this file's own remarks just above.</summary>
  public const int SlitTag = 0xD000;

  /// <summary>Isolates a word's top 4 bits, for testing against <see cref="SlitTag"/>. RETIRED alongside <see cref="SlitTag"/>.</summary>
  public const int SlitTagMask = 0xF000;

  /// <summary>Isolates a word's low 12 bits -- the raw (not yet sign-extended) <c>slit</c> value field. RETIRED alongside <see cref="SlitTag"/>.</summary>
  public const int SlitValueBitMask = 0xFFF;

  /// <summary>The most negative value a 12-bit two's-complement field can hold: -0x800 (-2048). RETIRED alongside <see cref="SlitTag"/>.</summary>
  public const int SlitValueMinValue = -0x800;

  /// <summary>The largest value a 12-bit two's-complement field can hold: 0x7FF (2047). RETIRED alongside <see cref="SlitTag"/>.</summary>
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
  // -- a fixed 8-bit tag (bits 15-8) OR'd with an UNSIGNED 8-bit value (bits 7-0). Unlike br/cbr/slit,
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

  /// <summary>
  /// The fixed high-bit pattern (bits 15-8) of a <c>lal</c> word: binary 1010_1110. RETIRED 2026-09-09,
  /// per Stefan: "'lal' & 'lap' are removed. use ''f' or 'fpush' from node 506 and add the offset to
  /// calculate the address of a local or parameter." Unlike the CVM1 node-606 leftovers this pair was
  /// always orphaned alongside (adjust, which stays orphaned with no replacement named), Stefan gave an
  /// explicit replacement mechanism this time -- node 506's own new <see cref="FrameToRegisterMnemonic"/>
  /// (<c>f</c>) / <see cref="PushFrameMnemonic"/> (<c>fpush</c>) plus ordinary <c>sub</c>/<c>add</c>
  /// arithmetic against the frame pointer -- so this is a real retirement, not a continued orphaning.
  /// Kept, per "do not remove any opcodes," but no longer referenced by <see cref="Instructions"/>; Id 26
  /// is retired and must never be reused for a different instruction (see <see cref="Instructions"/>'s
  /// own remarks). See <see cref="Ga144.C.Toolchain.CCodeGenerator"/>'s own remarks for the replacement
  /// codegen sequence.
  /// </summary>
  public const int LoadAddressOfLocalTag = 0xAE00;

  /// <summary>The fixed high-bit pattern (bits 15-8) of a <c>lap</c> word: binary 1010_1111. RETIRED 2026-09-09 alongside <see cref="LoadAddressOfLocalTag"/> -- see that constant's own remarks; Id 27 is retired.</summary>
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
  // enter is repointed here ("only update existing opcodes where possible"); adjust remains UNTOUCHED
  // and still points at node 606's old 8-bit tag above -- node 506's own source has never named an
  // "adjust" equivalent, so it stays permanently orphaned. lal/lap, which used to sit alongside adjust in
  // this same "orphaned" bucket, were retired outright 2026-09-09 instead (see LoadAddressOfLocalTag's
  // own remarks) -- a real removal, not a continued orphaning. This also settles the
  // "may need its own new embedded-value shape" question Node506Program's own remarks once raised: the
  // VALUE field itself is a plain unsigned 9-bit offset for every one of these ops (load-local's/
  // store-local's own sign flip happens entirely inside node 506's own dispatch, via "inv", never at the
  // CVM opcode encoding level) -- EmbeddedUnsignedValue, unchanged, is the right shape after all.
  //
  // KNOWN, DELIBERATE collision with br/ifbr (2026-09-02, extended 2026-09-06): Node506EnterTag used to
  // fall inside BranchTag's own range (OLD 0x9000-0x97FF, EmbeddedSignedValue) -- per Stefan: "ignore
  // the ranges of br/ifbr. ignore the overlapping ranges. give me now enter and leave mnemonics." Not
  // resolved at the time, accepted for a while. The four tags just below (stp/stl/ldp/ldl,
  // 0x9800/0x9A00/0x9C00/0x9E00) fell the SAME way inside the old "ifbr" placeholder's range
  // (0x9800-0x9FFF) instead -- the same kind of collision, just against ifbr rather than br.
  //
  // RESOLVED 2026-09-09 (in two steps, same day): first br's own tag moved to 0x8000 (see BranchTag's
  // own remarks -- Stefan: "'br' is wrongly encoded"), so Node506EnterTag (0x9200, unchanged -- it
  // comes from node 506's own real F18 dispatch, never a CVM assembler choice) no longer falls inside
  // br's range at all; enter's collision with br is gone. Then Stefan retired the old "ifbr" guess
  // entirely and gave its real replacement, cbr, a confirmed tag of its own (0xAC00 -- see
  // ConditionalBranchTag's own remarks) -- since that new range doesn't touch 0x9800-0x9FFF either, the
  // four tags just below (stp/stl/ldp/ldl) no longer collide with cbr/ifbr at all. (cbr's new range
  // briefly traded this collision for a different one against lal/lap, resolved for good later the same
  // day when Stefan retired lal/lap outright -- see ConditionalBranchTag's own remarks; it never involved
  // these four.)

  /// <summary>
  /// The fixed high-bit pattern (bits 15-9) of CVM2 node 506's <c>enter</c> word: binary 1001_001,
  /// i.e. 0x9200 with the low 9 bits (the offset) zeroed. See this file's own remarks just above on
  /// the 7-bit-tag/9-bit-value split and the now-fully-resolved (2026-09-09) br/ifbr-turned-cbr
  /// collision history.
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
  // shape as br/cbr/slit (EmbeddedSignedValue, self-describing, no live node/linker involvement at
  // all), just its own tag and its own narrower 10-bit field. This is the "1011_01??_????_????" branch
  // of node 509's own u/main dispatch cascade (see Node509Program's own remarks) that was previously
  // left unwired for lack of a name -- now named, it is wired the same way br/cbr/slit are: a direct
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
    /// low bits (<c>br</c>, <c>cbr</c>, <c>slit</c> -- each with its own tag and field width; see
    /// <see cref="CvmInstructionShape.ValueBitMask"/>'s own remarks). Fully self-describing and known
    /// at assemble time from a literal operand alone -- unlike the tagged mnemonics, it involves no
    /// node, no linker, and (for now, see <see cref="CvmAssembler"/>'s own remarks) no label/import
    /// operand either.
    ///
    /// For <c>br</c>/<c>cbr</c> specifically, this has been confirmed against real hardware (a
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
    /// tag/value split isn't fixed width across all of them -- <c>br</c> reserves 5 bits for its tag and
    /// packs an 11-bit signed offset into the rest (<see cref="BranchOffsetBitMask"/>), <c>cbr</c>
    /// reserves 6 bits for its own (confirmed 2026-09-09, one bit wider than br's) and packs a 10-bit
    /// signed offset into the rest (<see cref="ConditionalBranchOffsetBitMask"/>) -- the two no longer
    /// share a width, see <see cref="CvmInstructionSet.ConditionalBranchTag"/>'s own remarks --
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
  /// <c>br</c>, <c>cbr</c>, <c>slit</c>) needs its own encode/decode logic in
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
    new(Id: 7, ConditionalBranchMnemonic, 1, CvmOperandEncoding.EmbeddedSignedValue, Tag: ConditionalBranchTag, ValueBitMask: ConditionalBranchOffsetBitMask),
    // Id 8 (was SlitMnemonic, "slit") RETIRED 2026-09-09 -- see SlitTag's own remarks. Never reuse Id 8.
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
    // Ids 26/27 (were LoadAddressOfLocalMnemonic "lal" / LoadAddressOfParameterMnemonic "lap") RETIRED
    // 2026-09-09 -- see LoadAddressOfLocalTag's own remarks. Never reuse Ids 26/27.
    new(Id: 28, LeaveMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 29, EqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 30, EqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 31, FalseMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 32, TrueMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 33, NotEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 34, NotEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    // Id 35 (UnsignedGreaterThanMnemonic "ugt") -- FLAGGED, NOT retired 2026-09-09 despite no node in
    // the current CVM2 mesh defining a matching F18 symbol: Ga144.C.Toolchain.CCodeGenerator's own
    // unsigned-comparison codegen (ComputeCommonType(...).IsUnsigned ? "ugt" : "gt") actively emits this
    // mnemonic for every unsigned ">" comparison in C source. Kept, permanently orphaned, exactly as
    // before this audit -- see UnsignedGreaterThanMnemonic's own remarks.
    new(Id: 35, UnsignedGreaterThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 36, GreaterThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 37, GreaterThanZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 38, GreaterOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 39, GreaterOrEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    // Id 40 (UnsignedLessOrEqualMnemonic "ule") -- FLAGGED, NOT retired, same reason as Id 35 above:
    // CCodeGenerator emits "ule" for every unsigned "<=" comparison.
    new(Id: 40, UnsignedLessOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 41, LessOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 42, LessOrEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 43, LessThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 44, LessThanZeroMnemonic, 1, CvmOperandEncoding.None),
    // Ids 45/46 (UnsignedLessThanMnemonic "ult" / UnsignedGreaterOrEqualMnemonic "uge") -- FLAGGED, NOT
    // retired, same reason as Id 35/40 above: CCodeGenerator emits "ult"/"uge" for every unsigned "<"/">="
    // comparison.
    new(Id: 45, UnsignedLessThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 46, UnsignedGreaterOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 47, MultiplyByTwoMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 48, UnsignedDivideByTwoMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 49, DivideByTwoMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 50, AbsoluteValueMnemonic, 1, CvmOperandEncoding.None),
    // Id 51 (NegateMnemonic "negate") -- FLAGGED, NOT retired 2026-09-09: CCodeGenerator emits "negate"
    // for unary minus (do not confuse with NegMnemonic "neg", node 509's own genuinely different, still-
    // live mnemonic).
    new(Id: 51, NegateMnemonic, 1, CvmOperandEncoding.None),
    // Ids 52-54 (ExchangeTMnemonic "xt" / LoadTMnemonic "ldt" / StoreTMnemonic "stt") -- FLAGGED, NOT
    // retired 2026-09-09: CCodeGenerator emits "xt"/"ldt"/"stt" for every pointer dereference (load/store
    // through an lvalue address) in C source -- see UnsignedGreaterThanMnemonic's own remarks for the
    // full flag on this whole group.
    new(Id: 52, ExchangeTMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 53, LoadTMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 54, StoreTMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 55, BitCountMnemonic, 1, CvmOperandEncoding.None),
    // Ids 56-64 (ZeroExtendMnemonic "zext" through UnsignedMultiplyDoubleMnemonic "umuld") -- FLAGGED,
    // NOT retired 2026-09-09 despite CVM1's old node 506 (this family's own implementing node, deleted
    // 2026-09-01, coordinate reused for CVM2's stack-frame node) never having a CVM2 replacement: this
    // exact nine-mnemonic sequence is still assembled, word for word, by
    // Cvm.CvmDebuggerDefaultProgram's own "default test program" (zext/addc/ldd/std/xd/mul2d/div2d/sext/
    // umuld), confirmed against REAL HARDWARE per that class's own remarks -- but that confirmation
    // predates the CVM1->CVM2 rewrite, so it verified CVM1's old node 506, not anything a CVM2 board now
    // runs. Retiring these would break that program's own assembly outright without making anything true
    // about the CURRENT CVM2 mesh either way, so they are left exactly as before this audit; see
    // UnsignedGreaterThanMnemonic's own remarks for this audit's parallel flag on node 508's family.
    new(Id: 56, ZeroExtendMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 57, AddWithCarryMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 58, LoadDMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 59, StoreDMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 60, ExchangeDMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 61, MultiplyByTwoDoubleMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 62, DivideByTwoDoubleMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 63, SignExtendMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 64, UnsignedMultiplyDoubleMnemonic, 1, CvmOperandEncoding.None),
    // Ids 65-71 (ExchangePortMnemonic "xpt" through StoreLowMnemonic "stlo") -- FLAGGED, NOT retired,
    // same reason: five of these seven (xpt/ldhi/ldlo/sthi/stlo -- 'in'/'out' are deliberately excluded
    // by Stefan's own choice, see that same class's remarks) are likewise still assembled by
    // Cvm.CvmDebuggerDefaultProgram and were likewise confirmed only against CVM1's old node 407.
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
    // Ids 107-110 (were LoadRegisterFileMnemonic "rld" / StoreRegisterFileMnemonic "rst" /
    // PopRegisterFileMnemonic "rpop" / PushRegisterFileMnemonic "rpush") RETIRED 2026-09-09 -- FLAGGED,
    // not a routine case, see LoadRegisterFileMnemonic's own remarks: node 511's own re-synced source no
    // longer names these four operations as separate F18 symbols at all. Never reuse Ids 107-110 without
    // first confirming with Stefan that this retirement (rather than a repoint to node 511's new inline
    // dispatch shape) is what he actually wants.

    // tjmp (node 507's own table-jump primitive, confirmed located 2026-09-09 -- see
    // TableJumpMnemonic's own remarks) and node 506's new f/fpush (2026-09-09, replacing the retired
    // lal/lap -- see FrameToRegisterMnemonic's own remarks). All three are tagged/node-resolved, exactly
    // like leave/halt above (CvmOperandEncoding.None, no Tag/ValueBitMask here -- resolved only against
    // a live compile via Ga144.Evb.Ide.Services.CvmAssemblyLanguage.NodeSymbolByMnemonic).
    new(Id: 111, TableJumpMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 112, FrameToRegisterMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 113, PushFrameMnemonic, 1, CvmOperandEncoding.None),

    // jump/xs/xp, node 507's own last three tick-labeled opcodes, added 2026-09-09 by the opcode/
    // assembler-vs-node reconciliation audit -- see JumpMnemonic's own remarks. jump is shaped exactly
    // like pushlit/lcall/ljmp/ldg/stg (CvmOperandEncoding.TrailingWord, 2 words); xs/xp take no operand
    // at all, exactly like nop/push/pop/ret/halt/tjmp (CvmOperandEncoding.None, 1 word). All three are
    // tagged/node-resolved against node 507's own live compile, no Tag/ValueBitMask here.
    new(Id: 114, JumpMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 115, ExchangeSMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 116, ExchangePMnemonic, 1, CvmOperandEncoding.None),
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
  /// Extracts a <c>br</c> word's signed offset field, sign-extending its low 11 bits. This only recovers
  /// the raw offset, not an absolute target address -- doing that also needs the branch word's OWN
  /// address, since real hardware resolves the target as <c>(this word's own address + 1) + offset</c>
  /// (confirmed against a real <c>br 1</c> run -- see
  /// <see cref="CvmOperandEncoding.EmbeddedSignedValue"/>'s own remarks), not as an offset from the
  /// word's own address. <c>br</c> only -- see <see cref="DecodeConditionalBranchOffset"/> for <c>cbr</c>,
  /// which packs its offset into 10 bits, not 11 (see <see cref="ConditionalBranchTag"/>'s own remarks).
  /// </summary>
  public static int DecodeBranchOffset(int word) => DecodeSignedField(word, BranchOffsetBitMask);

  /// <summary>
  /// Extracts a <c>cbr</c> word's signed offset field, sign-extending its low 10 bits. Added 2026-09-09
  /// alongside <c>cbr</c>'s real, confirmed bit pattern, which turned out one bit narrower than <c>br</c>'s
  /// -- see <see cref="DecodeBranchOffset"/>'s own remarks for the target-resolution formula, which
  /// applies identically here (this only recovers the raw offset, not the resolved target).
  /// </summary>
  public static int DecodeConditionalBranchOffset(int word) => DecodeSignedField(word, ConditionalBranchOffsetBitMask);

  /// <summary>Extracts a <c>slit</c> word's signed value field, sign-extending its low 12 bits. Unlike <see cref="DecodeBranchOffset"/>, this IS the whole answer -- a <c>slit</c> value isn't relative to anything. RETIRED alongside <c>slit</c> itself (see <see cref="SlitTag"/>'s own remarks) -- no longer called from <see cref="TryDescribeSelfDecodingWord"/>, kept only for decoding an already-shipped word that used the old encoding.</summary>
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
  /// <c>br</c>/<c>cbr</c> word's RESOLVED absolute target -- <c>(wordAddress + 1) + offset</c>, per
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
  /// also print <c>br</c>/<c>cbr</c>'s RESOLVED absolute target next to their raw offset -- per
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

    if ((word & BranchTagMask) == BranchTag)
    {
      return $"{BranchMnemonic} {FormatOperand(DecodeBranchOffset(word))}{DescribeBranchTarget(wordAddress, DecodeBranchOffset(word))}";
    }

    // cbr, CONFIRMED 2026-09-09 (replacing the old, unconfirmed "ifbr" placeholder guess -- see
    // ConditionalBranchTag's own remarks for Stefan's exact bit pattern). Checked here, right after br
    // and well before the generic EmbeddedUnsignedValue loop below. Originally this ordering mattered
    // because cbr's tag (0xAC00, mask 0xFC00) briefly, fully contained the unconfirmed, orphaned lal/lap
    // tags (0xAE00/0xAF00) that loop would otherwise have matched -- lal/lap were retired outright the
    // same day (see ConditionalBranchTag's own remarks), so that collision no longer exists at all, but
    // cbr is kept here rather than moved back into the generic loop since a self-describing branch shape
    // reads more clearly grouped with br above it.
    if ((word & ConditionalBranchTagMask) == ConditionalBranchTag)
    {
      return $"{ConditionalBranchMnemonic} {FormatOperand(DecodeConditionalBranchOffset(word))}{DescribeBranchTarget(wordAddress, DecodeConditionalBranchOffset(word))}";
    }

    if ((word & LitTagMask) == LitTag)
    {
      return $"{LitMnemonic} {FormatOperand(DecodeLitValue(word))}";
    }

    // Every EmbeddedUnsignedValue shape, self-describing the same way: a fixed tag OR'd with an
    // unsigned value in the low ValueBitMask bits. Matched generically against every such shape in
    // Instructions rather than one if-check per mnemonic, so a new one added there later needs no
    // change here. IMPORTANT (2026-09-02): this mask/width is now taken from EACH shape's OWN
    // ValueBitMask (derived as ~shape.ValueBitMask), not a single hardcoded width -- node 606's one
    // remaining EmbeddedUnsignedValue op (adjust, still 8-bit tag/8-bit value, Node606TagMask/
    // Node606ValueBitMask -- its former siblings stl/stp/ldl/ldp were repointed to node 506 and lal/lap
    // were retired outright, see LoadAddressOfLocalTag's own remarks) and node 506's enter (7-bit
    // tag/9-bit value, Node506EnterTag/Node506FrameValueBitMask) genuinely differ in width, so the OLD
    // single-hardcoded-mask version of this loop (word & Node606TagMask for every shape) would have
    // decoded enter's own 9-bit value one bit short. Node606TagMask/DecodeNode606Value themselves are
    // unchanged and still correct for node 606's own remaining op.
    //
    // RESOLVED 2026-09-09: this loop used to be shadowed twice over for words in this range -- cbr's own
    // check above used to shadow lal/lap (0xAE00/0xAF00), and the (since-removed) SlitTag check used to
    // shadow node 306's six address-register ops (0xD800-0xDBFF, inside slit's old 0xD000-0xDFFF range).
    // Both are gone now: lal/lap were retired outright the same day cbr got its real tag (see
    // ConditionalBranchTag's own remarks), and slit itself was retired later the same day, in favor of
    // the already-existing node-509 lit (see SlitTag's own remarks) -- removing its dedicated check
    // entirely, so node 306's six ops now fall through correctly to this loop with nothing left to
    // shadow them.
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