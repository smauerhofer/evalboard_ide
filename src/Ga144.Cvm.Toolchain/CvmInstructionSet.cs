namespace Ga144.Cvm.Toolchain;

/// <summary>
/// The CVM assembly language's own instruction set -- Stefan's mnemonics (<c>nop</c>,
/// <c>pushlit &lt;data&gt;</c>, <c>push</c>, <c>pop</c>, <c>call &lt;address&gt;</c>, <c>ret</c>,
/// <c>br &lt;offset&gt;</c>, <c>cbr &lt;offset&gt;</c>, plus node 507's ALU
/// ops -- <c>usl</c>, <c>ssr</c>, <c>usr</c>, <c>add</c>, <c>sub</c>, <c>and</c>, <c>xor</c>, <c>or</c>
/// (binary: register r and the top of the CVM data stack), and <c>inv</c>, <c>inc</c>, <c>dec</c>
/// (unary: register r alone), plus node 606's frame-pointer-management ops -- <c>enter &lt;locals&gt;</c>,
/// <c>stl &lt;offset&gt;</c>, <c>stp &lt;offset&gt;</c>,
/// <c>ldl &lt;offset&gt;</c>, <c>ldp &lt;offset&gt;</c> (each self-describing, an 8-bit tag OR'd with an
/// 8-bit unsigned value -- <c>lal</c>/<c>lap</c>, the family's other two, were RETIRED 2026-09-09 in
/// favor of node 506's own <c>f</c>/<c>fpush</c> plus explicit offset arithmetic -- their own mnemonic
/// and tag constants were later deleted outright, see this file's own remarks below on the 2026-09-09
/// CVM1-opcode purge; <c>adjust</c>, the family's ninth member, was DELETED OUTRIGHT 2026-09-10 per
/// Stefan's own instruction, "'adjust' no longer exists. you can remove it." -- see this file's own
/// remarks below on that removal), plus
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
/// node 508's 27 ops, resolved against node 506's own live compile -- eight of these nine, all but
/// <c>addc</c>, were later deleted outright, see this file's own remarks below on the 2026-09-09
/// CVM1-opcode purge)), plus node 407's seven register-w/port ops --
/// <c>xpt</c>, <c>out</c>, <c>in</c>, <c>ldhi</c>, <c>ldlo</c>, <c>sthi</c>, <c>stlo</c> (tagged
/// mnemonics exactly like node 506's and 508's ops, resolved against node 407's own live compile --
/// all seven were later deleted outright, see this file's own remarks below on the 2026-09-09
/// CVM1-opcode purge)), plus CVM2's <c>lcall</c>/<c>ljmp</c> (long
/// call/long jump, added 2026-09-02 -- shaped exactly like <c>pushlit</c>, resolved against node 407's
/// own live compile too, but a DIFFERENT tag -- see <see cref="LongCallMnemonic"/>'s own remarks)), plus
/// CVM2's <c>gld</c>/<c>gst</c> (load global/store global, added 2026-09-04, renamed 2026-09-09 from
/// <c>ldg</c>/<c>stg</c> -- shaped exactly like <c>lcall</c>/<c>ljmp</c>, resolved against node 508's own
/// live compile, its own DIFFERENT tag again -- see <see cref="LoadGlobalMnemonic"/>'s own remarks)),
/// plus node 509's nine unary-arithmetic ops --
/// <c>abs</c>, <c>neg</c>, <c>inc</c>, <c>dec</c>, <c>inv</c>, <c>mul2</c>, <c>div2</c>, <c>udiv2</c>,
/// <c>bitcnt</c> (added 2026-09-05: eight of these REPOINT existing orphaned mnemonics -- <c>inv</c>/
/// <c>inc</c>/<c>dec</c> from node 507's old ALU-op family and <c>abs</c>/<c>mul2</c>/<c>div2</c>/
/// <c>udiv2</c>/<c>bitcnt</c> from node 508's old 27-op family -- to node 509's own live compile,
/// per "only update existing opcodes where possible"; only <c>neg</c> is a genuinely new mnemonic
/// (node 509's own <c>'neg</c> has no existing same-named counterpart -- <c>negate</c>, Id 51, stayed a
/// separate, orphaned mnemonic for a while longer, until it too was deleted outright, see this file's
/// own remarks below on the 2026-09-09 CVM1-opcode purge). Tagged exactly like <c>leave</c>/node 508's/node 506's/node
/// 407's own ops, a DIFFERENT tag again -- see <see cref="NegMnemonic"/>'s own remarks), plus node
/// 509's tenth mnemonic <c>lit</c> (added 2026-09-05, per Stefan's own explicit follow-up: "add this
/// range to the cvm language ... mnemonic lit") -- self-describing, shaped exactly like <c>br</c>/
/// <c>cbr</c> (a fixed 6-bit tag OR'd with a 10-bit signed value), its own DIFFERENT tag
/// and field width again -- see <see cref="LitTag"/>'s own remarks. <c>lit</c> is now the CVM's only
/// self-describing literal-load mnemonic: the older, wider <c>slit</c> (12-bit value, node 507's own
/// "1101" dispatch class) was RETIRED 2026-09-09 in its favor -- "'slit' is replaced by 'lit'. remove it
/// from the language." -- its own mnemonic/tag constants and decode helper were later deleted outright
/// too, see this file's own remarks below on the 2026-09-09 CVM1-opcode purge), plus node 509's tenth and eleventh
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
/// <c>ldt</c>/<c>stt</c>) were at the time still actively emitted by
/// <see cref="Ga144.C.Toolchain.CCodeGenerator"/>'s own codegen (unsigned comparisons, unary minus,
/// pointer dereference -- see <see cref="UnsignedGreaterThanMnemonic"/>'s own remarks), and all nine of
/// node 506's old register-d family (<c>zext</c> through <c>umuld</c>) plus five of node 407's old seven
/// register-w/port ops (<c>xpt</c>/<c>ldhi</c>/<c>ldlo</c>/<c>sthi</c>/<c>stlo</c>) were still assembled
/// by <see cref="Ga144.Evb.Ide.Cvm.CvmDebuggerDefaultProgram"/>'s own hand-written default test program.
/// <b>SUPERSEDED 2026-09-09, later the same day -- see this file's own remarks immediately below on the
/// CVM1-opcode purge:</b> both of those dependents were rewritten and every one of these now-truly-
/// orphaned mnemonics (all but <c>ugt</c>/<c>ule</c>/<c>ult</c>/<c>uge</c>, which node 408 backs for
/// real, and <c>addc</c>, repointed to node 510) was deleted outright.
///
/// <b>CVM1-opcode purge (2026-09-09, later the same day as the reconciliation audit above), per
/// Stefan's own explicit follow-up ("how can I make you forget all CVM1 opcodes and use CVM2 opcodes
/// only?"): every mnemonic the audit above found orphaned SOLELY because of an in-toolchain dependent
/// (not a live CVM2 node) has since had that dependent rewritten and been deleted outright.</b>
/// <see cref="Ga144.C.Toolchain.CCodeGenerator"/>'s own codegen was rewritten to stop emitting
/// negate/xt/ldt/stt: unary minus now emits node 509's already-live <c>neg</c> instead, and every
/// pointer dereference now uses node 306's own address-register mechanism (<c>arld</c>/<c>lda</c>/
/// <c>sta</c> -- corrected 2026-09-11 from an earlier "arst" that had arld's and arst's own directions
/// reversed, see <see cref="ArithmeticLoadAddressRegisterMnemonic"/>'s own remarks -- still always
/// emitted against address register 0 today, though node 306 genuinely has six, see
/// <see cref="ArithmeticStoreAddressRegisterMnemonic"/>'s/
/// <see cref="LoadAddressRegisterValueMnemonic"/>'s own remarks) instead of the old, node-less
/// xt/ldt/stt sequence. <see cref="Ga144.Evb.Ide.Cvm.CvmDebuggerDefaultProgram"/>'s own hand-written
/// default test program was likewise rewritten to drop its register-d block (zext/addc/ldd/std/xd/
/// mul2d/div2d/sext/umuld) and its register-w/port block (xpt/ldhi/ldlo/sthi/stlo) entirely -- addc's
/// own OLD test specifically, since <c>addc</c> itself is NOT deleted here (it stays live, repointed to
/// node 510, see <see cref="AddWithCarryMnemonic"/>'s own remarks); its outdated test values simply no
/// longer belong in a program that no longer exercises node 506's old register-d block at all. With
/// neither dependent left, NINETEEN mnemonics -- <c>negate</c> / <c>xt</c> / <c>ldt</c> / <c>stt</c>
/// (node 508, 4, formerly Ids 51-54), <c>zext</c> / <c>ldd</c> / <c>std</c> / <c>xd</c> / <c>mul2d</c> /
/// <c>div2d</c> / <c>sext</c> / <c>umuld</c> (node 506's OLD register-d family minus <c>addc</c>, 8,
/// formerly Ids 56, 58-64), and <c>xpt</c> / <c>out</c> / <c>in</c> / <c>ldhi</c> / <c>ldlo</c> /
/// <c>sthi</c> / <c>stlo</c> (node 407's OLD register-w/port family, all 7, formerly Ids 65-71) -- are
/// deleted from this file outright: both the mnemonic constant
/// AND the <see cref="Instructions"/> entry are gone, not merely commented out. Their retired Ids (51-54,
/// 56, 58-71 excluding 57) are still never to be reused (see <see cref="Instructions"/>'s own remarks at
/// each gap), but unlike every earlier retirement in this file, the mnemonic constants themselves are
/// gone too, since Stefan's own instruction this round was explicitly to "drop the retired historical
/// constants" rather than leave them as permanent dead weight. <c>ugt</c>/<c>ule</c>/<c>ult</c>/<c>uge</c>
/// (repointed to node 408) and <c>addc</c> (repointed to node 510) are NOT part of this purge -- all
/// four have a real, live CVM2 node behind them today; only the truly node-less remainder was removed.
/// This same instruction also reaches backwards to constants that were ALREADY retired-but-kept before
/// this round even started: <c>SlitMnemonic</c> and its <c>SlitTag</c>/<c>SlitTagMask</c>/
/// <c>SlitValueBitMask</c>/<c>SlitValueMinValue</c>/<c>SlitValueMaxValue</c> siblings plus the
/// <c>DecodeSlitValue</c> method, <c>LoadAddressOfLocalMnemonic</c>/<c>LoadAddressOfParameterMnemonic</c>
/// plus <c>LoadAddressOfLocalTag</c>/<c>LoadAddressOfParameterTag</c>, and node 306's OLD self-describing
/// family (<c>LoadAddressRegisterMnemonic</c>/<c>StoreAddressRegisterMnemonic</c>/
/// <c>IncrementAddressRegisterMnemonic</c>/<c>DecrementAddressRegisterMnemonic</c>, i.e. <c>ldar</c>/
/// <c>star</c>/<c>inca</c>/<c>deca</c>, plus their six <c>Node306*Tag</c> constants and the
/// <c>Node306AddressRegisterIndexBitMask</c>/<c>Node306AddressRegisterIndexShift</c> pair) are ALL
/// deleted outright too. This is a deliberate, one-time departure from this file's own long-standing "do
/// not remove any opcodes" convention -- every OTHER retirement in this file (Ids 8, 26/27, 98, 100,
/// 101-106 among them) still follows that convention exactly (<see cref="Instructions"/> row commented
/// out, Id never reused, constant kept) and nothing about that convention changes going forward; only
/// these specific, already-dead, already-orphaned-across-multiple-audits entries were swept out, on
/// Stefan's own explicit one-time instruction.
///
/// <b>"adjust" deleted outright (2026-09-10), a second, later one-time departure from "do not remove any
/// opcodes."</b> Node 606's <c>adjust</c> (formerly Id 21, <c>AdjustMnemonic</c>/<c>AdjustTag</c>) had
/// been carried since the CVM1-opcode purge above as "permanently orphaned" -- node 506's own rewritten
/// source never named a replacement for it, unlike its seven siblings (<c>enter</c>/<c>stl</c>/<c>stp</c>/
/// <c>ldl</c>/<c>ldp</c> were repointed to node 506; <c>lal</c>/<c>lap</c> were retired outright the same
/// day) -- and <see cref="Ga144.Evb.Ide.Cvm.CvmDebuggerDefaultProgram"/>'s own remarks record a real
/// hardware run where trying it corrupted the whole cluster's control flow. Per Stefan (2026-09-10): "
/// 'adjust' no longer exists. you can remove it." -- so unlike the ordinary "kept but superseded"
/// treatment given to node 606's OTHER superseded tags (<c>EnterTag</c>/<c>StoreLocalTag</c>/
/// <c>StoreParameterTag</c>/<c>LoadLocalTag</c>/<c>LoadParameterTag</c>, still kept per "do not remove any
/// opcodes" since nobody has said those no longer exist), <c>AdjustMnemonic</c> and <c>AdjustTag</c> are
/// deleted outright, the same treatment the CVM1-opcode purge above gave truly-dead mnemonics. Former Id
/// 21 is still never to be reused (see <see cref="Instructions"/>'s own remarks at that gap).
/// <c>Node606TagMask</c>/<c>Node606ValueBitMask</c>/<see cref="DecodeNode606Value"/> are kept -- they were
/// never adjust-specific to begin with (node 606's whole eight-op family shared them before five were
/// repointed to node 506 and two were retired outright), so removing adjust leaves them merely unreferenced
/// by <see cref="Instructions"/> today, the same "kept but currently unreferenced" state <c>EnterTag</c>'s
/// own family has been in since 2026-09-02/06.
///
/// Every already-wired
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

  // SlitMnemonic ("slit") -- RETIRED 2026-09-09 ("'slit' is replaced by 'lit'. remove it from the
  // language.") and DELETED OUTRIGHT (along with SlitTag/SlitTagMask/SlitValueBitMask/
  // SlitValueMinValue/SlitValueMaxValue and the DecodeSlitValue method) later the same day, per Stefan's
  // own instruction to drop already-retired historical constants entirely rather than keep them as dead
  // weight -- see this file's own remarks above on the 2026-09-09 CVM1-opcode purge. Id 8 is still never
  // to be reused. Use LitMnemonic ("lit") instead.

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
  // AdjustMnemonic ("adjust") -- DELETED OUTRIGHT 2026-09-10, per Stefan: "'adjust' no longer exists. you
  // can remove it." Unlike its long-orphaned status before this (node 506's own source never named a
  // replacement, and a real hardware run showed executing it corrupts the whole cluster's control flow --
  // see Ga144.Evb.Ide.Cvm.CvmDebuggerDefaultProgram's own remarks), this was not a mere continued
  // orphaning: Stefan's own words say the opcode itself no longer exists. Former Id 21 is still never to
  // be reused; AdjustTag (formerly 0xA900) is deleted alongside it -- see this file's own class-level
  // remarks above and Instructions' own remarks at that gap.
  public const string StoreLocalMnemonic = "stl";
  public const string StoreParameterMnemonic = "stp";
  public const string LoadLocalMnemonic = "ldl";
  public const string LoadParameterMnemonic = "ldp";
  // LoadAddressOfLocalMnemonic ("lal") / LoadAddressOfParameterMnemonic ("lap") -- RETIRED 2026-09-09
  // ("'lal' & 'lap' are removed. use ''f' or 'fpush' from node 506 and add the offset to calculate the
  // address of a local or parameter.") and DELETED OUTRIGHT (along with LoadAddressOfLocalTag/
  // LoadAddressOfParameterTag) later the same day -- see this file's own remarks above on the
  // 2026-09-09 CVM1-opcode purge. Ids 26/27 are still never to be reused. Use FrameToRegisterMnemonic
  // ("f") / PushFrameMnemonic ("fpush") plus offset arithmetic instead.

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
  // remove any opcodes" -- see each superseded constant's own remarks). adjust, which used to remain
  // permanently orphaned here (node 506's own source never named an equivalent), was DELETED OUTRIGHT
  // 2026-09-10 instead, per Stefan's own instruction that it no longer exists -- see AdjustMnemonic's
  // own former remarks, now removed, and this file's own class-level remarks above. lal/lap, the other
  // two long-orphaned node-606 leftovers, were RETIRED outright 2026-09-09 rather than left orphaned, and
  // their own mnemonic/tag constants were later deleted outright too -- see this file's own remarks above
  // on the 2026-09-09 CVM1-opcode purge.

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

  // 'f/'fpush, added 2026-09-09 alongside the new node 506 source that also removes lal/lap (see this
  // file's own remarks above on the 2026-09-09 CVM1-opcode purge): "'lal' & 'lap' are removed. use ''f' or 'fpush' from node 506
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
  // RESOLVED 2026-09-09, in part (second pass of the opcode/assembler-vs-node reconciliation audit,
  // against Stefan's own workspace.yaml project export -- this is the EXACT gap that prompted the whole
  // audit: "there are 'ugt' opcodes in the nodes"). FOUR of these eight -- ugt/ule/ult/uge -- are no
  // longer merely kept-alive orphans: node 408's own current source defines a real c/u helper plus
  // matching 'ugt/'ule/'ult/'uge words (see Cvm.Node408Program's own remarks), so all four now REPOINT to
  // node 408's own live compile, exactly like the other ten comparison ops already do -- see
  // Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own NodeSymbolByMnemonic for the wiring. The remaining
  // FOUR -- negate/xt/ldt/stt -- stayed permanently orphaned against every node in the CVM2 mesh at the
  // time, still actively emitted by Ga144.C.Toolchain.CCodeGenerator's own codegen ("negate" for unary
  // minus, "xt"/"ldt"/"stt" for every pointer dereference), so deleting their Instructions rows then
  // would have silently broken C compilation for any program using a unary minus or a pointer
  // dereference -- kept at the time, flagged rather than resolved unilaterally.
  //
  // RESOLVED FOR GOOD 2026-09-09 (later the same day, "make me forget all CVM1 opcodes and use CVM2
  // opcodes only"): CCodeGenerator's own codegen was rewritten to stop emitting all four -- unary minus
  // now emits node 509's already-live 'neg, and every pointer dereference now uses node 306's own
  // address-register mechanism ('arst/'lda/'sta, fixed to address register 0 -- see
  // ArithmeticStoreAddressRegisterMnemonic's/LoadAddressRegisterValueMnemonic's own remarks) -- so
  // negate/xt/ldt/stt were finally deleted outright, mnemonic constants and Instructions rows both; see
  // this file's own remarks above on the CVM1-opcode purge for the full accounting. Their formerly-kept
  // Ids (51-54) are still never to be reused.
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
  // NegateMnemonic ("negate") / ExchangeTMnemonic ("xt") / LoadTMnemonic ("ldt") / StoreTMnemonic
  // ("stt") -- DELETED OUTRIGHT 2026-09-09 (formerly Ids 51-54): once CCodeGenerator's own codegen was
  // rewritten to stop emitting them (unary minus now emits node 509's neg; every pointer dereference now
  // uses node 306's arst/lda/sta instead), no live CVM2 node and no in-toolchain dependent backed any of
  // these four any longer -- see this file's own remarks above on the 2026-09-09 CVM1-opcode purge.
  // Ids 51-54 are still never to be reused.
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
  // program's own assembly without making anything true about the current mesh either way, so they were
  // kept at the time -- see UnsignedGreaterThanMnemonic's own remarks for node 508's parallel case.
  //
  // RESOLVED FOR GOOD 2026-09-09 (later the same day): CvmDebuggerDefaultProgram's own smoke test was
  // rewritten to drop this entire register-d block, so eight of these nine (all but addc, separately
  // repointed to node 510) were finally deleted outright -- see this file's own remarks above on the
  // CVM1-opcode purge.
  // ZeroExtendMnemonic ("zext") / LoadDMnemonic ("ldd") / StoreDMnemonic ("std") / ExchangeDMnemonic
  // ("xd") / MultiplyByTwoDoubleMnemonic ("mul2d") / DivideByTwoDoubleMnemonic ("div2d") /
  // SignExtendMnemonic ("sext") / UnsignedMultiplyDoubleMnemonic ("umuld") -- DELETED OUTRIGHT
  // 2026-09-09 (formerly Ids 56, 58-64): once CvmDebuggerDefaultProgram's own smoke test (their last
  // dependent) was rewritten to drop this register-d block entirely, no live CVM2 node and no
  // in-toolchain dependent backed any of these eight any longer -- see this file's own remarks above on
  // the 2026-09-09 CVM1-opcode purge. Ids 56, 58-64 are still never to be reused. AddWithCarryMnemonic
  // ("addc", Id 57) is the ONE mnemonic from this old family that survives -- it was separately
  // REPOINTED (not retired) to node 510's own live 'addc, see the remarks on ExtendedStoreMnemonic below.
  public const string AddWithCarryMnemonic = "addc";

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
  // hand-written default test program, confirmed against real (pre-CVM2) hardware. Kept at the time for
  // the same reason as node 506's register-d family above.
  //
  // RESOLVED FOR GOOD 2026-09-09 (later the same day): CvmDebuggerDefaultProgram's own smoke test was
  // rewritten to drop this entire register-w/port block, so all seven (including 'in'/'out', which had
  // no dependent at all -- they were never assembled anywhere, only excluded from testing for hang-risk
  // reasons) were finally deleted outright -- see this file's own remarks above on the CVM1-opcode purge.
  // ExchangePortMnemonic ("xpt") / PortWriteMnemonic ("out") / PortReadMnemonic ("in") / LoadHighMnemonic
  // ("ldhi") / LoadLowMnemonic ("ldlo") / StoreHighMnemonic ("sthi") / StoreLowMnemonic ("stlo") --
  // DELETED OUTRIGHT (formerly Ids 65-71); those Ids are still never to be reused.

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

  // Node 306's four 32-bit address-register ops, ORIGINALLY added per Stefan's node 306 source
  // (2026-09-06) as a self-describing (EmbeddedUnsignedValue) family: ldar/star/inca/deca/lda/sta, a
  // fixed tag OR'd with a 2-bit address-register index. RETIRED 2026-09-09 (opcode/assembler-vs-node
  // reconciliation audit against Stefan's own workspace.yaml project export): node 306's OWN current
  // source no longer defines ANY of these six self-describing words at all -- it was rewritten entirely
  // around a different, TICK-PREFIXED/node-resolved family instead (see LoadAddressRegisterValueMnemonic's
  // own remarks just below for the replacement). ldar/star/inca/deca have no surviving counterpart under
  // any name on node 306 any more (their own former roles are now covered by the NEW lda/sta/arinc/
  // ardec below, which are DIFFERENT F18 symbols with a DIFFERENT encoding shape, not merely repointed) --
  // their Instructions rows (formerly Ids 101-104) were removed at the time; never reuse Ids 101-104 for
  // a different instruction.
  //
  // DELETED OUTRIGHT 2026-09-09 (later the same day, CVM1-opcode purge -- see this file's own remarks
  // above): LoadAddressRegisterMnemonic ("ldar") / StoreAddressRegisterMnemonic ("star") /
  // IncrementAddressRegisterMnemonic ("inca") / DecrementAddressRegisterMnemonic ("deca"), and their six
  // Node306LoadAddressRegisterTag/Node306StoreAddressRegisterTag/Node306IncrementAddressRegisterTag/
  // Node306DecrementAddressRegisterTag/Node306LoadAddressRegisterValueTag/Node306StoreAddressRegisterValueTag
  // tag constants (0xDB00/0xDA00/0xD980/0xD900/0xD880/0xD800) plus the Node306AddressRegisterIndexBitMask
  // (0x0006)/Node306AddressRegisterIndexShift (1) pair that packed/unpacked their shared 2-bit register
  // field, are all gone -- these were already-retired historical constants kept only per "do not remove
  // any opcodes," and Stefan's own instruction this round was to drop that whole category entirely. Ids
  // 101-104 are still never to be reused.

  // lda/sta -- RE-TASKED 2026-09-09 (same audit). The mnemonic STRINGS survive (node 306's own new source
  // still defines tick-prefixed words named 'lda and 'sta), but their MEANING and ENCODING both changed
  // completely: the OLD lda/sta (formerly Ids 105/106, self-describing EmbeddedUnsignedValue, "load/store
  // address register's own 32-bit value, address in r, page on the stack") are RETIRED outright -- that
  // exact role is now covered by the genuinely NEW mnemonics arld/arst below, under different names. The
  // NEW lda/sta (see the Instructions entries for these constants, Ids 121/122) are ordinary tick-
  // prefixed, node-resolved (CvmOperandEncoding.None) opcodes instead -- "load/store r from/to the address
  // held in address register aa," i.e. exactly the role the OLD ldar/star mnemonics used to play, just
  // under new names and a completely different word format (no embedded register-index bits of their
  // own at the CVM-opcode level any more; node 306's own ar/main dispatch resolves the register index
  // from the CALL BYTE it receives over the port, not from bits baked into a self-describing opcode
  // word). This is a genuine re-tasking, not a routine repoint (the encoding shape itself changed), so it
  // is called out explicitly rather than silently treated like every other same-shape repoint elsewhere
  // in this file. See Cvm.Node306Program's own remarks for the full node-side derivation and the
  // <c>ar/main</c> dispatch that resolves all six of node 306's current ops (lda/sta/arinc/ardec/arld/
  // arst) to their own compiled addresses.
  public const string LoadAddressRegisterValueMnemonic = "lda";
  public const string StoreAddressRegisterValueMnemonic = "sta";

  // arinc/ardec/arld/arst -- the other four of node 306's current six ops, alongside the re-tasked
  // lda/sta just above. arinc/ardec take over what ldar's/star's... no -- what inca's/deca's old NAMES
  // used to mean (increment/decrement the address register), just renamed and re-encoded exactly like
  // lda/sta. arld/arst take over what the OLD lda/sta used to mean (load/store the address register's
  // own full 32-bit value). DIRECTION CONFIRMED 2026-09-11 by Stefan directly ("arld loads an address
  // into an address register. lda loads r using the address of an address register."): arld is the SET
  // direction -- register := (address = r, page = the stack's own top word), exactly the "address in r,
  // page in stack" shape the trailing opcode table gives it -- and arst is arld's complement, reading a
  // register's own stored (address, page) value back OUT into (r, stack). (lda/sta, by contrast, dereference
  // THROUGH a register as a pointer -- lda: r := memory[register]; sta: memory[register] := r -- a
  // completely different pair of operations from arld/arst, despite the superficially similar names.)
  // See Ga144.C.Toolchain.CCodeGenerator's own EmitDereferenceLoad/EmitFunction/EmitCall remarks for the
  // 2026-09-11 fix this confirmation required (two call sites had arld and arst's roles reversed).
  // All six of node 306's current ops (lda/sta/arinc/ardec/arld/arst) share the
  // flat "1101_10??_????_????" range per node 306's own trailing comment block -- node 306's own ar/main
  // dispatch masks the incoming call byte to select which of the six compiled words to jump to, so
  // (unlike the OLD self-describing family) none of these six carries its own distinguishing tag bits at
  // the CVM-opcode level at all; the same "tag | resolved local address" scheme node 507/508/509/etc.
  // already use elsewhere applies here too, worked out in Ga144.Evb.Ide.Services.CvmAssemblyLanguage.
  //
  // RE-TASKED AGAIN 2026-09-11, per Stefan's own follow-up ("there is more than 1 address register.
  // change the assembler to reflect that"), pasting node 306/307's own resident source directly: node
  // 306 has SIX 32-bit address registers (0..5), not the single hardwired one every consumer of these
  // six mnemonics used to assume, and ar/main's own dispatch prelude ("dup 0x07 and 2* a!" .. "2/ 2/ 2/
  // 0x3f and ex") already carried the real 3-bit register-select field in the call byte's own low bits
  // all along -- it was this toolchain's OWN encoding (CvmOperandEncoding.None, "fixed to address
  // register 0", see the retirement note this replaces just below) that never gave the assembler a way
  // to express anything but register 0. All six are RE-TASKED from CvmOperandEncoding.None to
  // CvmOperandEncoding.NodeResolvedEmbeddedValue -- the exact shape node 308's/511's own register-indexed
  // ops already use -- with the register-index operand's own bit width recorded on each shape's own
  // ValueBitMask (see Node306RegisterFieldBitMask's own remarks just below), so every one of these six
  // now takes a real assembler operand (a register index 0..5, e.g. "arld 2") instead of implicitly
  // meaning "register 0" every time. See Ga144.Cvm.Toolchain.CvmAssembler's own remarks for the new
  // embedded-register-operand mechanism (CvmRelocation.EmbeddedValue) this correction motivated, and
  // Ga144.C.Toolchain.CCodeGenerator's own EmitDereferenceLoad/EmitDereferenceStore remarks for why the C
  // compiler's own codegen still only ever emits register 0 today despite the assembler now being able
  // to express all six.
  public const string ArithmeticIncrementAddressRegisterMnemonic = "arinc";
  public const string ArithmeticDecrementAddressRegisterMnemonic = "ardec";
  public const string ArithmeticLoadAddressRegisterMnemonic = "arld";
  public const string ArithmeticStoreAddressRegisterMnemonic = "arst";

  // Node306LoadAddressRegisterTag/Node306StoreAddressRegisterTag/Node306IncrementAddressRegisterTag/
  // Node306DecrementAddressRegisterTag/Node306LoadAddressRegisterValueTag/Node306StoreAddressRegisterValueTag
  // and Node306AddressRegisterIndexBitMask/Node306AddressRegisterIndexShift -- DELETED OUTRIGHT
  // 2026-09-09 alongside ldar/star/inca/deca above; see this file's own remarks above (both on the
  // CVM1-opcode purge and on that constant's own retirement note) for the full accounting.

  /// <summary>Isolates node 306's 6-bit "which function" field (bits 8-3) of a resolved lda/sta/arinc/
  /// ardec/arld/arst opcode word -- the resolved address (0-63, node 306's own "# 0x08 org" needing no
  /// bias, exactly like node 308's own layout) shifted left by <see cref="Node306FunctionFieldShift"/>.
  /// Derived directly from ar/main's own dispatch tail, "2/ 2/ 2/ 0x3f and ex": shifting the incoming
  /// call byte right by 3 and masking to 6 bits recovers the function address, so encoding is the
  /// inverse -- shift the resolved address left by 3 before OR-ing it in. See
  /// <see cref="ArithmeticStoreAddressRegisterMnemonic"/>'s own remarks for the correction this
  /// accompanies.</summary>
  public const int Node306FunctionFieldBitMask = 0x01F8;

  /// <summary>How far left node 306's resolved function address is shifted before OR-ing into <see cref="Node306FunctionFieldBitMask"/>'s bits -- 3, since the 3-bit register field occupies bits 2-0 below it. See <see cref="ArithmeticStoreAddressRegisterMnemonic"/>'s own remarks.</summary>
  public const int Node306FunctionFieldShift = 3;

  /// <summary>Node 306's own function field needs no base-address subtraction (always 0), exactly like <see cref="Node308FunctionFieldBaseAddress"/> -- its "# 0x08 org" range already starts inside the 0-63 window this field covers. See <see cref="ArithmeticStoreAddressRegisterMnemonic"/>'s own remarks.</summary>
  public const int Node306FunctionFieldBaseAddress = 0;

  /// <summary>Isolates node 306's 3-bit register-index field (bits 2-0, unshifted) -- straight off ar/main's own "dup 0x07 and", confirming SIX live registers (0-5; values 6-7 are unassigned, not a hardware register). Doubles as this family's <see cref="CvmInstructionShape.ValueBitMask"/> in <see cref="Instructions"/> below, so <see cref="CvmAssembler"/>'s own operand-range check reads the valid register range straight off the shape without a separate lookup. See <see cref="ArithmeticStoreAddressRegisterMnemonic"/>'s own remarks.</summary>
  public const int Node306RegisterFieldBitMask = 0x0007;

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
  // UN-RETIRED 2026-09-09 (opcode/assembler-vs-node reconciliation audit against Stefan's own
  // workspace.yaml project export). A PRIOR pass through this same audit had retired these four, based on
  // an intermediate re-sync of node 511's source (2026-09-08) that inlined the load/store/pop/push bodies
  // directly into r/main's own dispatch cascade with no separate named F18 symbol for any of them. The
  // CURRENT, authoritative workspace.yaml export (Stefan's own explicit ruling: "workspace.yaml" wins
  // whenever it disagrees with a checked-in reference file) restores node 511's ORIGINAL, simpler
  // direct-field-extraction r/main shape (see Cvm.Node511Program's own remarks), where 'rld/'rst/'rpop/
  // 'rpush ARE, once again, four separately named, separately addressed F18 words. These are therefore
  // real, live, currently-defined opcodes on node 511 -- not orphaned, not retired -- and are restored to
  // Instructions below under their ORIGINAL Ids (107-110), never renumbered.
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

  // Node 308's dpop/dpush/dinc/ddec/dadd/dor (added 2026-09-09) share node 511's NodeResolvedEmbeddedValue
  // shape (a live compile resolves which function is invoked, a register index is embedded in the same
  // word) but with a DIFFERENT field layout, straight from Cvm.Node308Program's own d/main body:
  // "dup 0x03 and 2* a!" isolates a 2-bit register index (bits 1-0) and doubles it before loading it as a
  // RAM address (node 308's own 32-bit "d" registers are stored as word pairs, so each register occupies
  // two consecutive words); "2/ 2/ 0x3f and ex" then shifts the SAME original word right by 2 (discarding
  // the register field) and masks to 6 bits, giving the resolved function address DIRECTLY -- unlike node
  // 511's own "# 0x20 org" bias, node 308's own compiled addresses need no base-address subtraction at
  // all (its function field's own 0-63 range already matches "# 0x08 org" starting inside it).

  /// <summary>Isolates node 308's 6-bit "which function" field (bits 7-2) -- unlike <see cref="Node511FunctionFieldBitMask"/>, this is the resolved address directly, no base-address subtraction. See <see cref="DoublePopMnemonic"/>'s own remarks.</summary>
  public const int Node308FunctionFieldBitMask = 0x00FC;

  /// <summary>How far left node 308's resolved function address is shifted before OR-ing into <see cref="Node308FunctionFieldBitMask"/>'s bits -- 2, since the 2-bit register field occupies bits 1-0 below it. See <see cref="DoublePopMnemonic"/>'s own remarks.</summary>
  public const int Node308FunctionFieldShift = 2;

  /// <summary>Node 308's own function field needs no base-address subtraction (always 0) -- unlike <see cref="Node511FunctionFieldBaseAddress"/>, its 0-63 range already starts where "# 0x08 org" does. See <see cref="DoublePopMnemonic"/>'s own remarks.</summary>
  public const int Node308FunctionFieldBaseAddress = 0;

  /// <summary>Isolates node 308's 2-bit register-index field (bits 1-0, unshifted, 0-3) -- see <see cref="DoublePopMnemonic"/>'s own remarks.</summary>
  public const int Node308RegisterFieldBitMask = 0x0003;

  // CVM2's load global/store global, added per Stefan's node 508 source (2026-09-04, "here is node
  // 508. it handles access to globals.") and his own tick-naming rule ("every word that begins with a
  // ' is an opcode for the CVM with the mnemonic using the same name without the leading '"). Node 508
  // defines ': 'gld g/next g/@ ;' and ': 'gst g/next g/! ;', each first fetching a trailing offset word
  // via g/next (structurally the SAME m/next-relay g/next itself performs) before doing the actual
  // global read/write -- exactly the "tagged opcode word, then one trailing operand word" shape
  // pushlit/lcall/ljmp already use, so no new CvmOperandEncoding case was needed here either, just two
  // more TrailingWord entries pointed at node 508. Per node 508's own g/main dispatch cascade (see
  // Cvm.Node508Program's own remarks for the full derivation): node 507 hands off to node 508 once a
  // fetched CVM opcode word's top bits read "101?" (the LEFT port), and node 508's own cascade consumes
  // three more bits before falling through to its own remote-fetch-then-"ex" tail for the "1010_0"
  // case (5 bits total: "10100") -- so 'gld's/'gst's own CVM opcode word always has its top 5 bits
  // "10100" (0xA000), the same "tag | local address" scheme Node407LongCallTagBits/
  // Node506LeaveTagBits/Node507Cvm2LocalExecuteTagBits already use (in the IDE project's own
  // Services.CvmAssemblyLanguage), just a 5-bit tag/11-bit address split this time. The actual
  // global-offset operand itself is carried separately, in the trailing word, read by 'gld/'gst
  // themselves via g/next once running on node 508 -- see Cvm.Node508Program's own remarks. Node 508's
  // own g/main also answers TWO further, narrower opcode forms with an offset embedded directly in the
  // opcode word (9 bits, no trailing word) rather than via 'gld/'gst's trailing-word form: global fetch
  // and global store by embedded 9-bit offset (the CVM-level "conditional branch to offset if r == 0",
  // 1010_11??, is a THIRD, separate top-level opcode -- see ConditionalBranchTag's own remarks; node
  // 508's own dispatch implements its bit-test/sign-extend/relay behavior directly rather than hanging a
  // tick-prefixed node-508 word off of it, so it was never a candidate for a NodeResolved entry here to
  // begin with).
  //
  // WIRED, 2026-09-10 -- see LoadGlobalEmbeddedMnemonic/StoreGlobalEmbeddedMnemonic below. These two
  // embedded-offset forms were originally left out (this paragraph used to say so) because Stefan's
  // source gave neither a tick-prefixed F18 word name to hang a mnemonic off of, only a plain "opcode
  // ldg .../opcode stg ..." comment -- unlike every other mnemonic in this table, which is always named
  // after an actual tick-prefixed or otherwise-callable F18 word. Stefan asked for them directly by
  // those exact names ("i am missing these 2 opcodes in the assembler") and confirmed the relationship
  // to the trailing-word forms above ("ldg is a shorter version of gld", "stg is a shorter version of
  // gst") -- same direction as gld/gst (ldg: r := global, stg: global := r, both now
  // hardware-confirmed -- see LoadGlobalMnemonic/StoreGlobalMnemonic's own remarks), just a fixed,
  // self-describing 9-bit-embedded-offset encoding instead of a trailing word, for when the offset is
  // small enough to fit (0-511) and the extra word isn't worth spending. Unlike gld/gst, these do NOT
  // need a NodeResolved/live-compile entry at all -- ldg/stg's own tag and 9-bit field are fully fixed
  // and self-describing from Stefan's own bit pattern comment alone, exactly like cbr/lit/node 506's
  // enter-family ops, so they use the plain EmbeddedUnsignedValue encoding those already use.
  //
  // RENAMED 2026-09-09 from LdgMnemonic="ldg"/StgMnemonic="stg" to "gld"/"gst": re-synced against
  // Stefan's LATEST workspace.yaml upload (the one that also surfaced node 408's missing 'ugt' family --
  // see CvmInstructionSet's own class remarks), which shows node 508's own tick-prefixed words are
  // actually named 'gld'/'gst, not 'ldg'/'stg as an EARLIER sync of this file had it (and as this
  // constant's own name, still LoadGlobalMnemonic/StoreGlobalMnemonic, continues to describe -- only the
  // STRING VALUE changed, per Stefan's own tick-naming rule which derives the mnemonic from the node's
  // own word name verbatim). Ids (75/76) and CvmOperandEncoding (TrailingWord) are unchanged -- this is
  // a same-Id string correction, not a retirement; nothing here was ever assembled against real hardware
  // under the old "ldg"/"stg" spelling (see the NOT-YET-CONFIRMED note below, unchanged since 2026-09-04),
  // so no append-only concern applies. See Cvm.Node508Program's own remarks for a further apparent bug
  // this same re-sync surfaced -- 'gld's own body calls g/@, but g/@'s own definition performs what reads
  // as a REMOTE STORE (m/2!), while 'gst calls g/!, whose own definition performs what reads as a REMOTE
  // FETCH (m/2@) -- and for how that was RESOLVED, not just flagged, below.
  //
  // MIS-RESOLVED, then CORRECTED, both 2026-09-10 (prompted by Stefan's own C compiler question about
  // whether an optimizer could route a global scalar access through these instead of the general
  // address-register dance). First pass: asked directly whether 'gld/'gst behave as named or are
  // swapped, Stefan answered "gld loads a global from r" / "gst stores a global into r" -- this was read
  // as CONFIRMING the backwards direction (global := r for 'gld, r := global for 'gst) was intentional,
  // not a bug, and Ga144.C.Toolchain.CCodeGenerator's own EmitGlobalFetch/EmitGlobalAssign were wired
  // that way. THAT READING WAS WRONG. Stefan then ran an actual test against the real node/simulation --
  // "lit 1 / gst 2 / gld 2 / gst 4 / nop" -- and the bus trace shows a WRITE of 1 to global page 2,
  // address 2 on "gst 2", then a READ of that same address (returning the 1 just written) on the
  // following "gld 2". That is: gst genuinely STORES (global := r) and gld genuinely LOADS (r := global)
  // -- exactly what their names ordinarily mean, no reversal. The cross-wire flagged above was a REAL bug
  // in node 508's F18 source (g/@'s and g/!'s own bodies really were swapped relative to their names),
  // not a naming-convention quirk -- confirmed by Stefan's own words: "you were right: g/@ and g/! were
  // swapped."
  //
  // FIXED by Stefan directly (2026-09-10, "here is the fixed node 508"): g/@'s and g/!'s own bodies are
  // now swapped (each ends in the remote op its name says it should), and 'gld/'gst are declared via
  // ".loc" directly against g/@/g/! rather than as separate g/next-wrapped words -- see
  // Cvm.Node508Program's own remarks and its updated Source for the full fix, reproduced there verbatim.
  // EmitGlobalFetch/EmitGlobalAssign were corrected the same day to emit gld/gst in this now
  // hardware-confirmed direction; see c-compiler-design.md's "Global-scalar addressing" section for the
  // full story, including the trace.
  //
  // Direction is now HARDWARE-CONFIRMED (2026-09-10, via the trace above) -- the one respect in which
  // this differs from lcall/ljmp's own still-open status: those are confirmed only by derivation, these
  // by an actual run. The 2026-09-04 "not yet confirmed" note is otherwise superseded.
  public const string LoadGlobalMnemonic = "gld";
  public const string StoreGlobalMnemonic = "gst";

  // Node 508's own embedded-9-bit-offset global fetch/store, added 2026-09-10 per Stefan's own bit
  // pattern comment (reproduced verbatim in Cvm.Node508Program's own Source):
  //   opcode ldg 1010_100?_????_???? load global from r. the offset is unsigned 9 bit.
  //   opcode stg 1010_101?_????_???? store global into r. the offset is unsigned 9 bit.
  // Confirmed by Stefan to share LoadGlobalMnemonic/StoreGlobalMnemonic's own (hardware-confirmed)
  // direction, just a shorter encoding: "ldg is a shorter version of gld" (r := global), "stg is a
  // shorter version of gst" (global := r). Distinct C# constant names from LoadGlobalMnemonic/
  // StoreGlobalMnemonic are needed since those already hold the STRING values "gld"/"gst" (the
  // TrailingWord forms above) -- "ldg"/"stg" were freed up for reuse by the 2026-09-09 rename recorded
  // above, and Stefan's new request reclaims them for this genuinely different, narrower encoding.
  //
  // Bit derivation: 7-bit fixed tag (bits 15-9) OR'd with a 9-bit UNSIGNED offset (bits 8-0), exactly
  // like node 506's enter/ldl/stl/ldp/stp family (see Node506EnterTag/Node506FrameValueBitMask's own
  // remarks) -- same EmbeddedUnsignedValue shape, ValueBitShift left at 0. ldg's tag "1010100" as a
  // 16-bit word with the value field zeroed is 0xA800; stg's tag "1010101" is 0xAA00; both share the
  // same 7-bit tag mask 0xFE00 and 9-bit value mask 0x1FF (matching g/main's own "r> 0x1ff and g/!"/
  // "r> 0x1ff and g/@" masking in Cvm.Node508Program.Source). Fully self-describing and known at
  // assemble time from a literal operand alone, exactly like cbr/lit -- no NodeResolved/live-compile
  // entry needed, unlike gld/gst above.
  //
  // FLAGGED at the time this was added, then MOOTED the same day: ldg's own range (0xA800-0xA9FF) used
  // to fully CONTAIN AdjustTag's range (0xA900-0xA9FF, node 606's old "adjust", formerly Id 21) -- any
  // ldg with an offset of 0x100-0x1FF produced a word bit-for-bit identical to some adjust instruction.
  // Per Stefan (2026-09-10, same day): "'adjust' no longer exists. you can remove it." -- adjust's own
  // mnemonic/tag/Instructions entry are deleted outright (see AdjustMnemonic's own former remarks, now
  // removed, and Instructions' own retired-Ids list), so this overlap no longer exists at all: ldg's
  // full 0xA800-0xA9FF range is exclusively ldg's now. stg's own range (0xAA00-0xABFF) never had an
  // overlap with anything wired.
  //
  // NOT YET CONFIRMED ON REAL HARDWARE -- Stefan's hardware trace confirmed gld/gst's own (TrailingWord)
  // direction, not this embedded-offset pair specifically; no run has exercised ldg/stg yet.
  public const int LoadGlobalEmbeddedTag = 0xA800;
  public const int StoreGlobalEmbeddedTag = 0xAA00;
  public const int GlobalEmbeddedTagMask = 0xFE00;
  public const int GlobalEmbeddedOffsetBitMask = 0x1FF;
  public const string LoadGlobalEmbeddedMnemonic = "ldg";
  public const string StoreGlobalEmbeddedMnemonic = "stg";

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
  // 'neg, not 'negate, so it did NOT repoint the old, separately orphaned "negate" mnemonic (Id 51) --
  // taken literally, per Stefan's own tick-naming rule, rather than assumed to be a renaming of it.
  // "negate" was itself deleted outright later (2026-09-09) once CCodeGenerator stopped emitting it in
  // favor of this same 'neg -- see this file's own remarks above on the CVM1-opcode purge.
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

  // Node 308's six 4x-32-bit "VM 32 arithmetic" register ops, added 2026-09-09 (opcode/assembler-vs-node
  // reconciliation audit against Stefan's own workspace.yaml project export) -- BRAND NEW to this
  // toolchain, no earlier revision of node 308 existed here at all. Shaped exactly like node 511's own
  // rld/rst/rpop/rpush (CvmOperandEncoding.NodeResolvedEmbeddedValue): a live compile of node 308 resolves
  // WHICH function is being invoked, and a 2-bit register index (0-3) is embedded directly in the same
  // opcode word (node 308's own d/main masks the incoming call byte's low 2 bits into the register index,
  // per Cvm.Node308Program's own remarks). All six share node 308's own "1101_11??_????_??aa" range.
  public const string DoublePopMnemonic = "dpop";
  public const string DoublePushMnemonic = "dpush";
  public const string DoubleIncrementMnemonic = "dinc";
  public const string DoubleDecrementMnemonic = "ddec";
  public const string DoubleAddMnemonic = "dadd";
  public const string DoubleOrMnemonic = "dor";

  // Node 405's nine "multiword arithmetic" (carry-flag) ops, added 2026-09-09 (same audit) -- BRAND NEW,
  // no earlier revision of node 405 existed here. Shaped like every other simple tagged/node-resolved
  // family (CvmOperandEncoding.None): node 405's own mw/main has no bit cascade of its own at all, just a
  // single dispatch word read then an immediate "ex" (see Cvm.Node405Program's own remarks), so each of
  // these nine is a single bare opcode word resolved only against node 405's own live compile, sharing
  // node 406's own "1110_1???" tag OR'd with each op's own address on node 405.
  public const string ToggleCarryMnemonic = "tgc";
  public const string AddWithCarryFlagMnemonic = "adc";
  public const string LoadCarryMnemonic = "ldc";
  public const string SetCarryMnemonic = "sec";
  public const string ClearCarryMnemonic = "clc";
  public const string StoreCarryMnemonic = "stc";
  public const string SubtractWithCarryMnemonic = "sbc";
  public const string RotateLeftMnemonic = "rol";
  public const string RotateRightMnemonic = "ror";

  // Node 505's "frame2" op, added 2026-09-09 (same audit) -- BRAND NEW, no earlier revision of node 505
  // existed here. Only 'fx is wired (CvmOperandEncoding.None, node-resolved, node 505's own f2/main also
  // has no bit cascade -- a single dispatch word then "ex", same shape as node 405's mw/main above).
  // Node 505's OTHER word, also spelled 'f in its own source, is DELIBERATELY left unwired: it collides
  // with node 506's own already-wired FrameToRegisterMnemonic ("f") under a different F18 symbol on a
  // different node -- see Cvm.Node505Program's own remarks (FLAGGED, likely a copy-paste comment typo on
  // node 505's own trailing doc block, not silently resolved either way).
  public const string FrameExchangeMnemonic = "fx";

  // Node 510's own "extended arithmetic" (double-register) ops, added 2026-09-09 (same audit): the PRIOR
  // sync of node 510 here (2026-09-08) described it as defining no opcodes of its own at all; the current
  // workspace.yaml export shows it defines six. Shaped like every other simple tagged/node-resolved
  // family (CvmOperandEncoding.None), resolved against node 510's own live compile, node 510's own
  // "1011_10??" local-execute tag OR'd with each op's own address -- see Cvm.Node510Program's own
  // remarks for the double-word (32-bit, register x + register a) shift/multiply mechanics.
  //
  // FLAGGED: 'addc REPOINTS the existing AddWithCarryMnemonic ("addc", Id 57) rather than adding a new
  // mnemonic, per "only update existing opcodes where possible" -- node 510's own source defines a
  // tick-prefixed word with that exact name. Unlike every earlier repoint in this file, though, the OLD
  // "addc" mnemonic was not merely dead: at the time, it was one of the nine still actively assembled by
  // Cvm.CvmDebuggerDefaultProgram's own hand-written default test program -- that program's own "addc"
  // would resolve against node 510's live compile instead of whatever CVM1's old node 506 used to
  // produce, a real behavioral change for that legacy test, not a no-op repoint. Repointed anyway, since
  // Stefan's own tick-naming rule leaves no alternative name for node 510's own live 'addc word.
  //
  // RESOLVED 2026-09-09 (later the same day, CVM1-opcode purge -- see this file's own remarks above):
  // CvmDebuggerDefaultProgram's own smoke test dropped its entire register-d block, addc's own old test
  // included, rather than carry forward expected values confirmed against a node addc no longer runs on
  // -- so the flag above is resolved by removal, not by a fresh hardware run.
  public const string ExtendedStoreMnemonic = "xst";
  public const string ExtendedLoadMnemonic = "xld";
  public const string ExtendedMultiplyByTwoMnemonic = "xmul2";
  public const string ExtendedDivideByTwoMnemonic = "xdiv2";
  public const string ExtendedUnsignedMultiplyMnemonic = "xumul";

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
  // there is no longer anything in Instructions for cbr's range to collide with at all -- see this
  // file's own remarks above on the 2026-09-09 CVM1-opcode purge (LoadAddressOfLocalTag/
  // LoadAddressOfParameterTag no longer exist as constants either).

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
  /// RESOLVED 2026-09-09 (same day): this range (0xAC00-0xAFFF) briefly, fully contained the OLD
  /// LoadAddressOfLocalTag (<c>lal</c>, 0xAE00) and LoadAddressOfParameterTag (<c>lap</c>, 0xAF00) --
  /// both constants since deleted outright, see this file's own remarks above on the 2026-09-09
  /// CVM1-opcode purge -- both then still wired in <see cref="Instructions"/>, both permanently-orphaned
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
  // checks (see that method's own remarks); at the time this constant, its mask/value-range siblings,
  // SlitMnemonic, and the DecodeSlitValue method were all kept per "do not remove any opcodes" -- Id 8
  // (formerly SlitMnemonic) is retired and must never be reused for a different instruction (see
  // Instructions' own remarks).
  //
  // DELETED OUTRIGHT 2026-09-09 (later the same day, CVM1-opcode purge -- see this file's own remarks
  // above): SlitTag (was 0xD000), SlitTagMask (0xF000), SlitValueBitMask (0xFFF), SlitValueMinValue
  // (-0x800), SlitValueMaxValue (0x7FF), and the DecodeSlitValue method are all gone, alongside
  // SlitMnemonic above -- Stefan's own instruction this round was to drop this whole already-retired
  // category entirely rather than keep it as dead weight. Id 8 is still never to be reused.

  // Node 606's eight frame-pointer-management ops, straight from Stefan's bit-pattern table:
  //   1010 1000 xxxx xxxx   0..0xFF   enter <locals>
  //   1010 1001 xxxx xxxx   0..0xFF   adjust <offset>  -- DELETED OUTRIGHT 2026-09-10, "no longer exists"
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

  // AdjustTag (was 0xA900, binary 1010_1001) -- DELETED OUTRIGHT 2026-09-10 alongside AdjustMnemonic,
  // per Stefan: "'adjust' no longer exists. you can remove it." -- see AdjustMnemonic's own former
  // remarks and this file's own class-level remarks above. Former Id 21 is still never to be reused.

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

  // LoadAddressOfLocalTag (was 0xAE00, lal) / LoadAddressOfParameterTag (was 0xAF00, lap) -- RETIRED
  // 2026-09-09 per Stefan: "'lal' & 'lap' are removed. use ''f' or 'fpush' from node 506 and add the
  // offset to calculate the address of a local or parameter." Unlike the CVM1 node-606 leftovers this
  // pair was always orphaned alongside (adjust, deleted outright 2026-09-10 -- see AdjustMnemonic's own
  // former remarks and this file's own class-level remarks above), Stefan
  // gave an explicit replacement mechanism this time -- node 506's own FrameToRegisterMnemonic (f) /
  // PushFrameMnemonic (fpush) plus ordinary sub/add arithmetic against the frame pointer -- so this was
  // a real retirement, not a continued orphaning. DELETED OUTRIGHT later the same day (2026-09-09
  // CVM1-opcode purge -- see this file's own remarks above), alongside LoadAddressOfLocalMnemonic/
  // LoadAddressOfParameterMnemonic. Ids 26/27 are still never to be reused. See
  // Ga144.C.Toolchain.CCodeGenerator's own remarks for the replacement codegen sequence.

  /// <summary>Isolates a word's top 8 bits, for testing against any of node 606's eight tags above.</summary>
  public const int Node606TagMask = 0xFF00;

  /// <summary>Isolates a word's low 8 bits -- the unsigned value field shared by all eight of node 606's ops.</summary>
  public const int Node606ValueBitMask = 0xFF;

  // CVM2's node 506 (2026-09-02) redefines enter/leave (and, since 2026-09-06, load-local/load-parameter/
  // store-local/store-parameter too) with a DIFFERENT bit layout than CVM1's node 606: a 7-bit tag (bits
  // 15-9) OR'd with a 9-bit UNSIGNED offset (bits 8-0), rather than 606's 8-bit tag/8-bit value split --
  // per Node506Program's own remarks, derived directly from its f/main dispatch cascade: "1001_001?"
  // (7 bits fixed: 1001001) is enter, with "the offset is 9 bit" per the source's own trailing comment.
  // enter is repointed here ("only update existing opcodes where possible"); adjust used to remain
  // UNTOUCHED, still pointing at node 606's old 8-bit tag above (node 506's own source never named an
  // "adjust" equivalent, so it stayed permanently orphaned), until it was DELETED OUTRIGHT 2026-09-10
  // per Stefan's own instruction that it no longer exists -- see AdjustMnemonic's own former remarks and
  // this file's own class-level remarks above. lal/lap, which used to sit alongside adjust in
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
  // mnemonic lit". ORIGINALLY (through the 2026-09-08 sync) a fixed 6-bit tag (bits 15-10) OR'd with a
  // 10-bit two's-complement signed value (bits 9-0), range -0x200..0x1FF, tag 0xB400.
  //
  // CHANGED 2026-09-09 (opcode/assembler-vs-node reconciliation audit against Stefan's own workspace.yaml
  // project export): node 509's own u/main dispatch moved lit down one more dispatch level (freeing up
  // "1011_01??" for a second left-port relay -- see Node509Program's own remarks) and narrowed its own
  // field width in the process. Straight from the export's own bit-pattern comment and dispatch cascade:
  //   1011 0010 xxx xxxxxxxx   (in practice, 1011_001? consumed, the trailing "?" folded into the value)
  //   -0x100..0xFF   lit (literal, signed value)
  // -- a fixed 7-bit tag (bits 15-9, binary 1011001, i.e. 0xB200) OR'd with a 9-bit two's-complement
  // signed value (bits 8-0). -0x100..0xFF is exactly a 9-bit signed value's own range, matching the
  // source's own updated comment ("load literal -256..255") exactly. Still the same self-describing
  // shape as br/cbr/slit (EmbeddedSignedValue, no live node/linker involvement at all), just a narrower
  // tag/value split -- see TryDescribeSelfDecodingWord below, unchanged in mechanism.
  //
  // FLAGGED, not silently corrected: node 509's own body for this branch --
  // "drop 0x01ff and dup 0x0200 and if drop 0xfe00 xor u/r! ; then drop u/r! ;" -- masks the value to 9
  // bits (0x01ff, bits 8-0) but then tests bit 9 (0x0200) for the sign, a bit the mask just zeroed; the
  // sign-extend XOR mask (0xfe00) likewise assumes an 8-bit-tag/9-bit-sign-at-bit-9 split rather than
  // this 7-bit-tag/9-bit-sign-at-bit-8 one. Taken literally, this would mean the "if" branch (negative
  // sign-extension) can never actually trigger at runtime -- effectively making the node's own real
  // hardware behavior an UNSIGNED 0..511 load, not the signed -256..255 the header/trailing comment both
  // promise. This looks like a leftover from the previous (10-bit-field) revision's own 0x0200/0xfc00
  // constants not being updated to 0x0100/0xff00 when the field narrowed to 9 bits, but it is reproduced
  // in Node509Program's own Source verbatim rather than silently "fixed" -- this toolchain's own
  // encode/decode below follows the WORD FORMAT the tag/dispatch and header comment describe (7-bit
  // tag, 9-bit signed field), which is what an assembler/disassembler needs regardless of whether node
  // 509's own runtime sign-extension arithmetic works as intended; Stefan should confirm which is
  // correct (the header comment, or the literal body) before this is relied on for a negative literal
  // on real hardware.
  public const int LitTag = 0xB200;

  /// <summary>Isolates a word's top 7 bits, for testing against <see cref="LitTag"/>. CHANGED 2026-09-09 (was 6 bits/0xFC00) -- see <see cref="LitTag"/>'s own remarks.</summary>
  public const int LitTagMask = 0xFE00;

  /// <summary>Isolates a word's low 9 bits -- the raw (not yet sign-extended) <c>lit</c> value field. CHANGED 2026-09-09 (was 10 bits/0x3FF) -- see <see cref="LitTag"/>'s own remarks.</summary>
  public const int LitValueBitMask = 0x1FF;

  /// <summary>The most negative value a 9-bit two's-complement field can hold: -0x100 (-256). CHANGED 2026-09-09 (was -0x200) -- see <see cref="LitTag"/>'s own remarks.</summary>
  public const int LitValueMinValue = -0x100;

  /// <summary>The largest value a 9-bit two's-complement field can hold: 0xFF (255). CHANGED 2026-09-09 (was 0x1FF) -- see <see cref="LitTag"/>'s own remarks.</summary>
  public const int LitValueMaxValue = 0xFF;

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
    /// its value isn't an address, just a literal loaded directly into a register. <c>slit</c> itself
    /// was later retired in favor of <c>lit</c> and its own constants deleted outright -- see this
    /// file's own remarks on the 2026-09-09 CVM1-opcode purge.
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
    /// Also covers node 306's OLD, now-deleted six address-register ops (see this file's own remarks on
    /// the 2026-09-09 CVM1-opcode purge), which needed one refinement: the packed field isn't at bit 0 upward like every earlier
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
    /// <b>Also node 306's six address-register ops (<c>arinc</c>/<c>ardec</c>/<c>arld</c>/<c>arst</c>/
    /// <c>lda</c>/<c>sta</c>), RE-TASKED to this shape 2026-09-11 once Stefan's own node 306/307 source
    /// confirmed a real 3-bit register-select field (0..5, six live registers) in ar/main's own dispatch
    /// -- see <see cref="ArithmeticStoreAddressRegisterMnemonic"/>'s own remarks. Node 306's own field
    /// layout (<see cref="Node306FunctionFieldBitMask"/>/<see cref="Node306FunctionFieldShift"/>/
    /// <see cref="Node306FunctionFieldBaseAddress"/>/<see cref="Node306RegisterFieldBitMask"/>) is
    /// structurally identical to node 308's (a plain 0-based function field, no bias), just a
    /// 6-bit/3-bit split instead of node 308's 6-bit/2-bit one.</b>
    ///
    /// <see cref="Ga144.Cvm.Toolchain.CvmAssembler"/> CORRECTED 2026-09-11 to support this encoding for
    /// real (see that class's own remarks and <see cref="CvmRelocation.EmbeddedValue"/>'s): the
    /// register-index operand is parsed and range-checked against the shape's own
    /// <see cref="CvmInstructionShape.ValueBitMask"/>, then carried on the SAME <see cref="CvmRelocationType.CvmOpcode"/>
    /// relocation the generic tagged-mnemonic path already emits, for the linker to OR into the resolved
    /// word once it knows the live-compiled base address -- no second relocation, no new relocation
    /// type needed. <see cref="Ga144.Evb.Ide.Services.CvmAssemblyLanguage"/>'s own immediately-resolving
    /// assembler (the CVM Debugger's own Assembly Code editor) continues to support it too, unchanged.
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
    /// <c>slit</c> (RETIRED and later deleted outright -- see this file's own remarks on the
    /// 2026-09-09 CVM1-opcode purge) reserved only 4 bits for its tag and packed a 12-bit signed value
    /// into the rest, and node 606's eight ops each reserve 8 bits for their own tag
    /// and pack an 8-bit UNSIGNED value into the rest (<see cref="Node606ValueBitMask"/>). <see cref="Tag"/>
    /// is expected to already be aligned to whatever's outside this mask (i.e.
    /// <c>Tag &amp; ValueBitMask == 0</c>), so a decoder can always recover the tag bits alone via
    /// <c>word &amp; ~ValueBitMask</c> without a separately stored mask per shape.
    /// </summary>
    public int ValueBitMask { get; init; } = ValueBitMask;

    /// <summary>
    /// How far left the operand is shifted before OR-ing it into <see cref="ValueBitMask"/>'s bits (and
    /// shifted back right when decoding) -- 0 for every EmbeddedSignedValue/EmbeddedUnsignedValue shape
    /// except node 306's OLD, now-deleted six address-register ops, whose 2-bit register-index operand
    /// sat at bits 2-1 rather than bit 0 upward (see this file's own remarks on the 2026-09-09
    /// CVM1-opcode purge). Default 0
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
    // Id 8 (was SlitMnemonic, "slit") RETIRED 2026-09-09, and its own mnemonic/tag constants and decode
    // helper deleted outright later the same day -- see this file's own remarks above on the
    // 2026-09-09 CVM1-opcode purge. Never reuse Id 8.
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
    // Id 21 (was AdjustMnemonic "adjust") is retired -- DELETED OUTRIGHT 2026-09-10 per Stefan: "'adjust'
    // no longer exists. you can remove it." See AdjustMnemonic's own former remarks (now removed) and
    // this file's own class-level remarks above for the full history, including the real hardware run
    // that showed executing it corrupted the whole cluster's control flow. Never reuse Id 21.
    new(Id: 22, StoreLocalMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506StoreLocalTag, ValueBitMask: Node506FrameValueBitMask),
    new(Id: 23, StoreParameterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506StoreParameterTag, ValueBitMask: Node506FrameValueBitMask),
    new(Id: 24, LoadLocalMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506LoadLocalTag, ValueBitMask: Node506FrameValueBitMask),
    new(Id: 25, LoadParameterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506LoadParameterTag, ValueBitMask: Node506FrameValueBitMask),
    // Ids 26/27 (were LoadAddressOfLocalMnemonic "lal" / LoadAddressOfParameterMnemonic "lap") RETIRED
    // 2026-09-09, their mnemonic/tag constants later deleted outright too -- see this file's own remarks
    // above on the 2026-09-09 CVM1-opcode purge. Never reuse Ids 26/27.
    new(Id: 28, LeaveMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 29, EqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 30, EqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 31, FalseMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 32, TrueMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 33, NotEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 34, NotEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    // Id 35 (UnsignedGreaterThanMnemonic "ugt") -- RESOLVED 2026-09-09 (second pass): node 408's own
    // current source defines a real 'ugt word (via its c/u helper) -- see UnsignedGreaterThanMnemonic's
    // own remarks. No longer merely kept for CCodeGenerator's sake; now genuinely live.
    new(Id: 35, UnsignedGreaterThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 36, GreaterThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 37, GreaterThanZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 38, GreaterOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 39, GreaterOrEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    // Id 40 (UnsignedLessOrEqualMnemonic "ule") -- RESOLVED 2026-09-09, same as Id 35 above: node 408
    // defines a real 'ule word.
    new(Id: 40, UnsignedLessOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 41, LessOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 42, LessOrEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 43, LessThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 44, LessThanZeroMnemonic, 1, CvmOperandEncoding.None),
    // Ids 45/46 (UnsignedLessThanMnemonic "ult" / UnsignedGreaterOrEqualMnemonic "uge") -- RESOLVED
    // 2026-09-09, same as Id 35/40 above: node 408 defines real 'ult/'uge words.
    new(Id: 45, UnsignedLessThanMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 46, UnsignedGreaterOrEqualMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 47, MultiplyByTwoMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 48, UnsignedDivideByTwoMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 49, DivideByTwoMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 50, AbsoluteValueMnemonic, 1, CvmOperandEncoding.None),
    // Id 51 (was NegateMnemonic "negate") -- FLAGGED at the time, NOT retired: CCodeGenerator emitted
    // "negate" for unary minus (do not confuse with NegMnemonic "neg", node 509's own genuinely
    // different, still-live mnemonic). DELETED OUTRIGHT 2026-09-09 (CVM1-opcode purge, later the same
    // day) once CCodeGenerator was rewritten to emit "neg" instead -- see this file's own remarks above.
    // Never reuse Id 51.
    // Ids 52-54 (were ExchangeTMnemonic "xt" / LoadTMnemonic "ldt" / StoreTMnemonic "stt") -- FLAGGED at
    // the time, NOT retired: CCodeGenerator emitted "xt"/"ldt"/"stt" for every pointer dereference
    // (load/store through an lvalue address) in C source. DELETED OUTRIGHT 2026-09-09 (same purge) once
    // CCodeGenerator was rewritten to use node 306's arst/lda/sta instead -- see this file's own remarks
    // above. Never reuse Ids 52-54.
    new(Id: 55, BitCountMnemonic, 1, CvmOperandEncoding.None),
    // Ids 56, 58-64 (were ZeroExtendMnemonic "zext" through UnsignedMultiplyDoubleMnemonic "umuld",
    // minus Id 57/addc) -- FLAGGED at the time, NOT retired, despite CVM1's old node 506 (this family's
    // own implementing node, deleted 2026-09-01, coordinate reused for CVM2's stack-frame node) never
    // having a CVM2 replacement: this exact nine-mnemonic sequence was still assembled, word for word,
    // by Cvm.CvmDebuggerDefaultProgram's own "default test program" (zext/addc/ldd/std/xd/mul2d/div2d/
    // sext/umuld), confirmed against REAL HARDWARE per that class's own remarks -- but that confirmation
    // predated the CVM1->CVM2 rewrite, so it verified CVM1's old node 506, not anything a CVM2 board
    // actually runs. DELETED OUTRIGHT 2026-09-09 (same purge) once CvmDebuggerDefaultProgram's own smoke
    // test was rewritten to drop this whole block -- see this file's own remarks above. Never reuse Ids
    // 56, 58-64. Id 57 (addc) is the sole survivor of this family -- see its own remarks just below.
    new(Id: 57, AddWithCarryMnemonic, 1, CvmOperandEncoding.None),
    // Ids 65-71 (were ExchangePortMnemonic "xpt" through StoreLowMnemonic "stlo") -- FLAGGED at the
    // time, NOT retired, same reason: five of these seven (xpt/ldhi/ldlo/sthi/stlo -- 'in'/'out' were
    // deliberately excluded from testing by Stefan's own choice, though never assembled anywhere else
    // either) were likewise still assembled by Cvm.CvmDebuggerDefaultProgram and were likewise confirmed
    // only against CVM1's old node 407. DELETED OUTRIGHT 2026-09-09 (same purge) once
    // CvmDebuggerDefaultProgram's own smoke test was rewritten to drop this whole block -- see this
    // file's own remarks above. Never reuse Ids 65-71.
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
    // Ids 101-106 (were ldar/star/inca/deca/lda/sta, self-describing EmbeddedUnsignedValue) RETIRED
    // 2026-09-09 -- node 306's own current source no longer defines this family at all; their mnemonic
    // and tag constants were later deleted outright too, see this file's own remarks above on the
    // 2026-09-09 CVM1-opcode purge. Never reuse Ids 101-106.

    // Ids 107-110 (LoadRegisterFileMnemonic "rld" / StoreRegisterFileMnemonic "rst" /
    // PopRegisterFileMnemonic "rpop" / PushRegisterFileMnemonic "rpush") -- UN-RETIRED 2026-09-09 (see
    // LoadRegisterFileMnemonic's own remarks): workspace.yaml's own authoritative node 511 source
    // restores these as four separately-named, separately-addressed F18 words. NodeResolvedEmbeddedValue,
    // exactly like node 308's dpop/dpush/dinc/ddec/dadd/dor below -- a live compile of node 511 resolves
    // which function is invoked, and a 5-bit register index is embedded in the same opcode word (see
    // Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own Node511* field-layout wiring). Restored under
    // their ORIGINAL Ids, never renumbered.
    new(Id: 107, LoadRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
    new(Id: 108, StoreRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
    new(Id: 109, PopRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
    new(Id: 110, PushRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),

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

    // Node 306's CURRENT six ops (2026-09-09, second pass; RE-TASKED again 2026-09-11 to a real
    // register operand -- see ArithmeticStoreAddressRegisterMnemonic's own remarks) -- tick-prefixed,
    // node-resolved, with a 3-bit embedded register-index operand (CvmOperandEncoding.NodeResolvedEmbeddedValue,
    // ValueBitMask = Node306RegisterFieldBitMask), replacing the retired self-describing family above
    // (Ids 101-106).
    new(Id: 117, ArithmeticIncrementAddressRegisterMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: Node306RegisterFieldBitMask),
    new(Id: 118, ArithmeticDecrementAddressRegisterMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: Node306RegisterFieldBitMask),
    new(Id: 119, ArithmeticLoadAddressRegisterMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: Node306RegisterFieldBitMask),
    new(Id: 120, ArithmeticStoreAddressRegisterMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: Node306RegisterFieldBitMask),
    new(Id: 121, LoadAddressRegisterValueMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: Node306RegisterFieldBitMask),
    new(Id: 122, StoreAddressRegisterValueMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: Node306RegisterFieldBitMask),

    // Node 308's six ops (2026-09-09) -- NodeResolvedEmbeddedValue, 2-bit register index embedded, same
    // shape as node 511's rld/rst/rpop/rpush above. See DoublePopMnemonic's own remarks.
    new(Id: 123, DoublePopMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
    new(Id: 124, DoublePushMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
    new(Id: 125, DoubleIncrementMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
    new(Id: 126, DoubleDecrementMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
    new(Id: 127, DoubleAddMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),
    new(Id: 128, DoubleOrMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue),

    // Node 405's nine ops (2026-09-09) -- tagged/node-resolved, CvmOperandEncoding.None. See
    // ToggleCarryMnemonic's own remarks.
    new(Id: 129, ToggleCarryMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 130, AddWithCarryFlagMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 131, LoadCarryMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 132, SetCarryMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 133, ClearCarryMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 134, StoreCarryMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 135, SubtractWithCarryMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 136, RotateLeftMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 137, RotateRightMnemonic, 1, CvmOperandEncoding.None),

    // Node 505's 'fx (2026-09-09) -- tagged/node-resolved, CvmOperandEncoding.None. See
    // FrameExchangeMnemonic's own remarks (including the deliberately-unwired 'f collision with node
    // 506's own FrameToRegisterMnemonic).
    new(Id: 138, FrameExchangeMnemonic, 1, CvmOperandEncoding.None),

    // Node 510's five genuinely new ops (2026-09-09) -- tagged/node-resolved, CvmOperandEncoding.None.
    // 'addc itself REPOINTS the existing AddWithCarryMnemonic (Id 57) rather than adding a new Id -- see
    // ExtendedStoreMnemonic's own remarks for the (since-resolved) CvmDebuggerDefaultProgram implication.
    new(Id: 139, ExtendedStoreMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 140, ExtendedLoadMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 141, ExtendedMultiplyByTwoMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 142, ExtendedDivideByTwoMnemonic, 1, CvmOperandEncoding.None),
    new(Id: 143, ExtendedUnsignedMultiplyMnemonic, 1, CvmOperandEncoding.None),

    // Node 508's embedded-9-bit-offset global fetch/store (2026-09-10) -- see
    // LoadGlobalEmbeddedMnemonic/StoreGlobalEmbeddedMnemonic's own remarks for the bit derivation and
    // the relationship to gld/gst (Id 75/76) above. A tag-range overlap with AdjustTag was flagged
    // (accepted) when this was added, then mooted the same day once adjust itself was deleted outright
    // -- see those same remarks and this file's own class-level remarks on the 2026-09-10 removal.
    new(Id: 144, LoadGlobalEmbeddedMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: LoadGlobalEmbeddedTag, ValueBitMask: GlobalEmbeddedOffsetBitMask),
    new(Id: 145, StoreGlobalEmbeddedMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: StoreGlobalEmbeddedTag, ValueBitMask: GlobalEmbeddedOffsetBitMask),
  ];

  private static readonly IReadOnlyDictionary<string, CvmInstructionShape> ByMnemonic =
      Instructions.ToDictionary(instruction => instruction.Mnemonic, StringComparer.OrdinalIgnoreCase);

  /// <summary>Looks up a mnemonic's shape case-insensitively. Null when it isn't a known CVM instruction (it may still be a valid label/import reference -- that's the caller's concern, not this lookup's).</summary>
  public static CvmInstructionShape? TryGetShape(string mnemonic) =>
      ByMnemonic.TryGetValue(mnemonic, out CvmInstructionShape? shape) ? shape : null;

  /// <summary>Looks up an instruction by its stable ID -- the inverse of encoding a <c>0x8000 | Id</c> placeholder, useful for a tool that wants to describe an unlinked object file's raw words without consulting its relocation table.</summary>
  public static CvmInstructionShape? TryGetShapeById(int id) =>
      Instructions.FirstOrDefault(shape => shape.Id == id);

  /// <summary>Sign-extends the low bits of <paramref name="word"/> selected by <paramref name="valueBitMask"/> -- the shared arithmetic behind <see cref="DecodeBranchOffset"/> and <see cref="DecodeLitValue"/> (formerly also <c>DecodeSlitValue</c>, deleted alongside <c>slit</c> itself -- see this file's own remarks on the 2026-09-09 CVM1-opcode purge).</summary>
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

  // DecodeSlitValue -- extracted a slit word's signed value field (its low 12 bits). RETIRED alongside
  // slit itself and later DELETED OUTRIGHT (2026-09-09, CVM1-opcode purge -- see this file's own
  // remarks above) rather than kept for decoding an already-shipped word, per Stefan's own instruction
  // to drop already-retired historical constants (and their supporting code) entirely.

  /// <summary>Extracts a <c>lit</c> word's signed value field, sign-extending its low 10 bits. This IS the whole answer -- unlike <see cref="DecodeBranchOffset"/>, a <c>lit</c> value isn't relative to anything.</summary>
  public static int DecodeLitValue(int word) => DecodeSignedField(word, LitValueBitMask);

  /// <summary>Extracts one of node 606's eight ops' unsigned value field -- its low 8 bits, taken as-is (never sign-extended, unlike <see cref="DecodeBranchOffset"/>).</summary>
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
    // ValueBitMask (derived as ~shape.ValueBitMask), not a single hardcoded width -- this was originally
    // proven necessary by node 606's own adjust (8-bit tag/8-bit value, Node606TagMask/
    // Node606ValueBitMask) genuinely differing in width from node 506's enter (7-bit tag/9-bit value,
    // Node506EnterTag/Node506FrameValueBitMask): the OLD single-hardcoded-mask version of this loop
    // (word & Node606TagMask for every shape) would have decoded enter's own 9-bit value one bit short.
    // adjust itself was DELETED OUTRIGHT 2026-09-10 (see AdjustMnemonic's own former remarks and this
    // file's own class-level remarks above) -- every EmbeddedUnsignedValue op live in Instructions today
    // (enter/stl/stp/ldl/ldp, and 2026-09-10's own ldg/stg) happens to share the same 9-bit width again,
    // but the per-shape ValueBitMask lookup stays generic rather than being narrowed back to a single
    // hardcoded width, since a future op could easily reintroduce a different one. Node606TagMask/
    // Node606ValueBitMask/DecodeNode606Value themselves are unchanged and simply unreferenced by
    // Instructions now, the same state EnterTag's own superseded-tag family has been in since 2026-09-02.
    //
    // RESOLVED 2026-09-09: this loop used to be shadowed twice over for words in this range -- cbr's own
    // check above used to shadow lal/lap (0xAE00/0xAF00), and the (since-removed) SlitTag check used to
    // shadow node 306's six address-register ops (0xD800-0xDBFF, inside slit's old 0xD000-0xDFFF range).
    // Both are gone now: lal/lap were retired outright the same day cbr got its real tag (see
    // ConditionalBranchTag's own remarks), and slit itself was retired later the same day, in favor of
    // the already-existing node-509 lit (its own SlitTag check, and later its whole constant family, was
    // removed -- see this file's own remarks above on the 2026-09-09 CVM1-opcode purge) -- removing its
    // dedicated check entirely, so node 306's six ops now fall through correctly to this loop with
    // nothing left to shadow them.
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