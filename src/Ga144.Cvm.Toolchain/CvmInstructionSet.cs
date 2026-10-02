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
/// favor of node 506's own <c>f</c>/<c>pushf</c> (renamed 2026-09-16 from <c>fpush</c>) plus explicit offset arithmetic -- their own mnemonic
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

  // IfMnemonic ("if") -- ADDED 2026-10-02, per Stefan directly: "'cbr' has been renamed to 'if'." This
  // is a RENAME of the "CVM_pipeline" table's own Id 193 row (see this file's own class-level remarks
  // right after UnlinkMnemonic, and Instructions' own remarks at Id 193) -- same Id, same tag (0xA000),
  // same bit layout (cccc at bits 7-4, xxxx at bits 3-0), only the mnemonic string changes. The OLD
  // ConditionalBranchMnemonic ("cbr") constant just above is left exactly as it was, still carrying
  // CVM2's OLD, fully-retired cbr (former Id 7, EmbeddedSignedValue) per "do not remove any opcodes" --
  // it is simply no longer referenced by Id 193's own row, which now uses this constant instead.
  public const string IfMnemonic = "if";

  // LinkMnemonic ("link")/UnlinkMnemonic ("unlink") -- ADDED 2026-10-01, the new VM's fourth/fifth
  // opcodes (right after ret, same node-506 bit-pattern-table/dispatch family) -- see this file's own
  // Instructions remarks on Ids 185/186 for the full derivation. Brand new names, no CVM1/CVM2
  // namesake to collide or coincide with (unlike RetMnemonic's own "ret").
  public const string LinkMnemonic = "link";
  public const string UnlinkMnemonic = "unlink";

  // ---- 2026-10-02: "CVM_pipeline" bit-pattern table, pasted whole by Stefan alongside a 23-node F18
  // source listing (nodes 205-209/305-309/405-409/505-509/607/707/708). Per Stefan's own governing
  // instruction once asked how far to take this: "do not sync the source yet (first finish the CVM
  // before updating any NodeXXX... file) but rebuild the instruction set as far it is possible." So
  // this section (and the Instructions rows/encodings it feeds, further below) rebuilds ONLY
  // CvmInstructionSet/CvmAssemblyLanguage/CvmAssembler against that table -- no Node###Program.cs file
  // (existing or one of the three still missing -- 206, 209, 309) is touched here.
  //
  // The table itself (reproduced verbatim, Stefan's own text):
  //   1111|11
  //   5432|1098|7654|3210|
  //   ----+----+----+----+----------------------------
  //   0000|0000|0000|0000| nop
  //   000a|aaaa|aaaa|aaaa| scall
  //   0010|0000|00ww|wwww| special {ink, unlink, ret}
  //   0100|    |    |    | next16
  //   0101|    |    |    | next32
  //   0110|    |    |    | pop16
  //   0111|    |    |    | pop32
  //   1000|....|....|xxxx| rjmp {jump to address in register r}
  //   1001|....|....|xxxx| rcall {call to address in register r}
  //   1010|....|cccc|xxxx| cbr {conditional long branch, relative}
  //   1011|oooo|oooo|oooo| sbr {short branch, relative}
  //
  //   11  |    |    |    | other
  //   ----+----+----+----+----------------------------------------
  //   1100|0www|wwww|weee| implied
  //   1100|1   |iiii|xxxx| immediate
  //   1101|0   |    |xxxx| unary {16 bit}
  //   1101|1   |yyyy|xxxx| binary {16 bit}
  //   1110|0   |    |xxxx| unary {32 bit}
  //   1110|1   |yyyy|xxxx| binary {32 bit}
  //   1111|0   |    |xxxx| unary {32 bit float}
  //   1111|1   |yyyy|xxxx| binary {32 bit float}
  //
  //   a: absolute address
  //   b: immediate instruction
  //   c: binary instruction address {address in corresponing node}
  //   d: unary instruction address {address in corresponing node}
  //   e: node identifier
  //   f: instruction address {address in corresponing node}
  //   i: immediate unsigned value
  //   o: signed offset
  //   cond: condition
  //   x: parameter1 binary: x = x op y
  //   y: parameter2
  //   .: unused bit
  //
  // Cross-checked against the LIVE (not merely commented) F18 source Stefan pasted alongside this
  // table: node 505's d1/main dispatch cascade branches on exactly these same bit prefixes, in this
  // same order (11*->other/stub, 1011*->d/branch, 1010*->d/conditional, 1001*->d/rcall, 1000*->
  // d/rjump, 0111*->d/pop2, 0110*->d/pop1, 0101*->d/next2, 0100*->d/next1, 001*->d/special, 000*->
  // d/call) -- strong, independent confirmation that nop/scall/special/next16/next32/pop16/pop32/
  // rjmp/rcall/cbr/sbr are correctly identified and correctly ordered. Node 506 (the table's own
  // "home") still only has this table as a COMMENT, not live dispatch code -- its real d/main is
  // still the pre-existing trivial stub -- but it DOES have live word-level helpers (d/call, d/next1,
  // d/next2, d/pop1, d/pop2, d/special, d/conditional, d/rcall/d/rjump, d/branch, 'ret/'link/'unlink,
  // etc.) that corroborate several rows' own field widths (see each new mnemonic's own remarks below
  // for exactly which).
  //
  // EVERY new mnemonic below is Id 187 or higher: 184-186 (ret/link/unlink) were already assigned the
  // 2026-10-01 night before this table arrived, under the OLD (now superseded) assumption that their
  // shared tag was 0xFC00 with the resolved node address shifted left by 3 -- see
  // Node506BitPatternTableTagBits' and NodeResolvedAddressShiftByMnemonic's own remarks (IDE project)
  // for the correction this table's own "special" row forces (tag 0x2000, UNshifted 6-bit field).
  // ret/link/unlink's OWN Instructions rows (184-186, just below) do not change shape or Id at all --
  // only their shared tag/shift, which (per this file's own long-standing convention) lives entirely
  // on the IDE side, not here.
  public const string Next16Mnemonic = "next16";
  public const string Next32Mnemonic = "next32";
  public const string Pop16Mnemonic = "pop16";
  public const string Pop32Mnemonic = "pop32";
  public const string RegisterJumpMnemonic = "rjmp";
  public const string RegisterCallMnemonic = "rcall";
  public const string ShortBranchMnemonic = "sbr";
  // ConditionalBranchMnemonic ("cbr") REUSES the existing constant declared above (right after
  // BranchMnemonic) -- that constant already carries CVM2's OLD, fully-retired cbr (former Id 7,
  // EmbeddedSignedValue, tag 0xA800); this table's own cbr (new Id below) was a DIFFERENT shape in a
  // DIFFERENT era, pure naming coincidence, exactly like RetMnemonic's own two unrelated "ret"s -- see
  // this file's own class-level remarks on mnemonic STRINGS being freely reused across eras while only
  // the numeric Id stays append-only.
  //
  // RENAMED 2026-10-02, per Stefan directly: "'cbr' has been renamed to 'if' / CVM syntax is / 'if
  // <reg> <cond>' / cond is defined in node 406. the offset is taken as the value for c." Id 193's own
  // row below now uses IfMnemonic ("if") instead -- same Id, same tag (0xA000), same bit layout. Three
  // things this rename settles, two of them FLAGGED-and-now-CONFIRMED and one newly confirmed as a
  // deliberate design constraint:
  //
  // (1) CONFIRMED -- "c" (cccc, bits 7-4) is a NODE-406-RESOLVED reference, not a plain numeric
  // condition code, exactly as this remark originally flagged as the open, unconfirmed possibility.
  // Node 406 defines exactly ten named conditions ("==0"/"!=0"/"<0"/"<=0"/">0"/">="/"true"/"false"/
  // "even"/"odd"), each its own tick-prefixed F18 word; its own x1/cond dispatcher reads the operand's
  // register-index nibble, focuses that register, pushes "c" to the return stack via ">r", reads the
  // focused register's value, then hits its own ";" -- which pops "c" (never popped back off before
  // that ";") instead of a real return address, i.e. a COMPUTED JUMP straight into whichever named
  // condition routine is compiled at that node-406-local address. That routine's own ";" then pops what
  // is now exposed underneath -- the true original return address -- and returns normally. This is the
  // exact same resolve-an-address-and-transfer-control pattern this table's own ret/link/lcall/ljmp/
  // push already use against node 507 (see this file's own remarks on those, right after this block),
  // just one level more indirect (a computed jump via the return stack, not a direct call/jump).
  //
  // (2) CONFIRMED by Stefan directly (2026-10-02), then SUPERSEDED by Stefan himself the same day once
  // the complete operand syntax (see (4) below) made the question concrete: his FIRST answer, while "if"
  // still only had two named operands, was that the branch's own polarity is the OPPOSITE of node 406's
  // own table-text wording ("return true if r == 0" for '==0, etc.) -- "the evaluation of the flag is
  // reversed, so my example ['if r0 ==0' branches when r0 != 0] is correct." Once Stefan spelled out the
  // real, complete syntax with a branch-target operand ("if r0 ==0 loop_label"), he REVERSED this answer
  // again, in his own words: "yes, the correct syntax is 'if r0 ==0 loop_label', so I will have to
  // reverse the flag evaluation, so it is still understandable for humans. then 'if r0 ==0 loop_label'
  // means 'if r0 is 0 the goto loop_label'." So the FINAL, standing answer is the intuitive one: "if r0
  // ==0 loop_label" branches (goes) to loop_label WHEN r0 IS 0, not the inverse his first answer
  // described. This is Stefan's own change to the underlying hardware/firmware flag evaluation on node
  // 406's side (something he is doing, not something this toolchain's bit encoding controls) -- nothing
  // in this file encodes polarity directly either way, it is a runtime/node-406 behavior, not a bit the
  // assembler packs -- but any future disassembly description text must describe "if r0 ==0 loop_label"
  // as branching to loop_label when r0 == 0, matching this final answer, not the earlier-confirmed-then-
  // superseded one.
  //
  // (3) CONFIRMED by Stefan directly: the register field (xxxx, bits 3-0, 4 bits) genuinely limits "if"
  // to registers r0-r15 only, out of node 407's full 20-register file (r0-r19, fp=18/sp=19) -- not a
  // misreading. Stefan's own words: "with 4 bits you an only access 16 register, so only r0 to r15 are
  // accessible." He also volunteered, unprompted, that node 407 additionally exposes ten 32-bit DOUBLE
  // registers d0-d9 (d0 = r1:r0, d1 = r3:r2, ... pairing consecutive 16-bit registers; d9 is special,
  // holding fp/sp) -- context on the register file generally, not something "if" reaches via this 4-bit
  // field either (a double register would need its own, wider addressing, which "if" does not have).
  //
  // (4) CONFIRMED by Stefan directly, resolving what was previously this remark's own "STILL OPEN" item:
  // the complete hand-typed assembler syntax is "if <reg> <cond> <target>" -- three space-separated
  // operands, not two. Stefan's own words: "yes, the correct syntax is 'if r0 ==0 loop_label' ... that
  // makes 'if' a word than has 3 parameter: register, condition and (target address or label)." The
  // third operand (a literal branch-target address or a label name, mirroring br/sbr's own existing
  // label-operand convention) resolves to this shape's own trailing word -- see
  // EmbeddedUnsignedValuePairWithTrailingWord's own remarks, and Ga144.Evb.Ide.Services.
  // CvmAssemblyLanguage.EncodeIfInstruction's own remarks for the real encoder this unblocked, including
  // the one remaining inferred-not-confirmed detail (the trailing word's own relative-offset base point).
  //
  // ---- end CVM_pipeline bit-pattern table mnemonics; see Instructions' own remarks further below for
  // the new Ids (187-194) and ValueBitMask/Tag details, and TryDescribeSelfDecodingWord's own remarks
  // for how next32/if's extra trailing word(s) are disassembled. ----

  // ---- 2026-10-02: CVM_pipeline table's "other" row family (implied/immediate/unary/binary) -- tag
  // RANGES ONLY, RESERVED, NOT IMPLEMENTED. FLAGGED, not confirmed: no concrete F18 mnemonic or node
  // target is defined anywhere in the material Stefan pasted for any of the eight rows below, and the
  // table's own bit layout for several of them is itself incomplete -- "implied"'s own w/e split
  // (8-bit w at bits 10-3, a REAL 3-bit node-identifier e at bits 2-0, not fixed to 0 the way every
  // other None-shaped mnemonic in this file, ret included, has always treated a trailing "eee") is
  // confidently readable off the table text, but "immediate"'s own "iiii" field width/position and
  // "unary"'s own instruction-selector field (legend: "d: unary instruction address") are left BLANK
  // in the table itself ("1101|0   |    |xxxx|" -- the middle nibble is empty, not even dots), so
  // there is nothing yet to encode beyond the fixed high bits. These eight constants exist purely so
  // the high-bit tag ranges this table reserves are on record (and so nothing else in this file is
  // ever accidentally assigned a tag that collides with one) -- none has an Instructions row, none has
  // a mnemonic constant, and none is referenced by CvmAssembler/CvmAssemblyLanguage/
  // TryDescribeSelfDecodingWord. Add the real rows once Stefan defines a concrete mnemonic and (where
  // needed) node target for each.
  public const int ReservedImpliedTag = 0xC000;
  public const int ReservedImmediateTag = 0xC800;
  public const int ReservedUnary16Tag = 0xD000;
  public const int ReservedBinary16Tag = 0xD800;
  public const int ReservedUnary32Tag = 0xE000;
  public const int ReservedBinary32Tag = 0xE800;
  public const int ReservedUnary32FloatTag = 0xF000;
  public const int ReservedBinary32FloatTag = 0xF800;
  // NOTE: ReservedUnary32FloatTag (0xF000) is bit-for-bit identical to LongCallTag (lcall's own tag,
  // unchanged -- see that constant's own remarks for the full flag on this collision). Left exactly as
  // computed from the table, not nudged aside, since nothing today defines a concrete "unary {32-bit
  // float}" mnemonic to actually emit 0xF000 and collide with lcall in practice.

  // ---- 2026-09-30 (same day as the reset), the new VM's own "call" family: scall/lcall/call ----------
  // Per Stefan directly, right after giving the new VM's own instruction-word bit-pattern spec: "the next
  // instruction i want to define is 'call'. there is a short variant 'scall' and a long variant 'lcall'.
  // 'call' should try to fit the address into a 'scall' and use 'lcall' if the address does not fit. if
  // an address is unknown at compile time, e.g. an external symbol, 'call' will always use 'lcall'."
  //
  // ShortCallMnemonic ("scall") is a genuinely NEW mnemonic name -- spec row "00aa|aaaa|aaaa|aaaa": the
  // instruction's one and only word directly IS the target address, no tag bits at all beyond the two
  // fixed leading zeros. Structurally IDENTICAL to CVM2's OLD call (CvmOperandEncoding.EmbeddedAddress),
  // just a NARROWER field -- 14 bits, not 15 -- so this is also the first shape to actually exercise
  // EmbeddedAddress's field width read generically off the shape's own ValueBitMask (ShortCallAddressMask
  // below) rather than the single hardcoded CallAddressMask constant every consumer used to reference
  // directly -- see EmbeddedAddress's own remarks for the full generalization, and CallAddressMask's own
  // remarks: that constant is untouched, kept purely for the historical CVM2 record, no longer referenced
  // by anything live.
  public const string ShortCallMnemonic = "scall";

  // LongCallMnemonic ("lcall") REUSES CVM2's own old mnemonic STRING (defined further below, alongside
  // ljmp, in CVM2's own long-call/long-jump remarks) but is a completely different SHAPE under the new
  // VM -- spec row "1111|00..|....|....|": a fixed, universally-known tag word (LongCallTag, 0xF000) that
  // needs no live node compile to resolve at all (unlike CVM2's OLD lcall, which shared this exact
  // mnemonic string but was node-resolved, CvmOperandEncoding.TrailingWord, against node 407's own live
  // compile), followed by one full trailing word holding the actual target address. Neither existing
  // encoding covered "a fixed, self-describing tag word THEN a real trailing operand" -- FixedOpcode has
  // no operand at all (nop), TrailingWord's tag is never self-describing on its own -- so
  // CvmOperandEncoding.FixedOpcodeWithTrailingWord (see its own remarks) was added specifically for it.
  // The spec's own low 10 bits on lcall's first word ("..........") are marked don't-care/reserved, not
  // given any meaning -- this toolchain always emits and matches them as 0 (LongCallTag itself already
  // has them clear), since nothing today defines any other use for them. FLAGGED, not confirmed: this is
  // an assumption about unspecified bits, exactly like every other still-open bit in this spec.
  //
  // NOTE, flagged rather than silently resolved: scall's own word 0x0000 (address 0) is bit-for-bit
  // identical to nop's own fixed 0x0000 -- the spec gives nop and "short call to address 0" the exact same
  // bit pattern. TryDescribeSelfDecodingWord checks every FixedOpcode shape (nop) before the generalized
  // EmbeddedAddress check, so a disassembled 0x0000 always reads "nop", never "scall 0x0000" -- consistent
  // with nop being checked first specifically for this reason (see FixedOpcode's own remarks), but this
  // does mean "scall 0" assembles fine yet will always disassemble back as "nop", never round-tripping to
  // "scall 0x0000". Left exactly as specified until Stefan says otherwise -- he already flagged this same
  // overlap himself as unconfirmed when the bit-pattern spec first arrived.
  //
  // "call" itself is a PURE ASSEMBLER-LEVEL PSEUDO-MNEMONIC, exactly like "literal" (see
  // Ga144.Evb.Ide.Services.CvmAssemblyLanguage.LiteralPseudoMnemonic's own remarks for that precedent) --
  // it has no CvmInstructionSet.Instructions entry, no Id, no shape of its own at all: both
  // Ga144.Cvm.Toolchain.CvmAssembler and Ga144.Evb.Ide.Services.CvmAssemblyLanguage intercept it by name
  // and lower it to a real "scall"/"lcall" line before any shape lookup happens, mirroring exactly how
  // "literal" lowers itself to "lit"/"litr". A literal numeric address picks whichever of scall/lcall
  // actually fits (scall when it's in ShortCallAddressMask's own 0..0x3FFF range, lcall otherwise); a
  // label or (CvmAssembler-only) ".import"ed external symbol ALWAYS lowers to lcall, unconditionally, per
  // Stefan's own words above -- FLAGGED, a deliberate design choice rather than something he spelled out
  // in full: both assemblers here are two-pass (see CvmAssembler's own class-level remarks), fixing every
  // instruction's word length during pass 1 BEFORE most labels' own final addresses are known, so there is
  // no way to shrink a label-targeted "call" down to scall without a further, iterative relaxation pass
  // neither assembler implements -- exactly the same restriction "literal" already imposes on itself for a
  // label operand (see EncodeLiteralPseudoMnemonic's own remarks), just applied to "call" instead of
  // refusing the label outright: rather than reject a label operand (which "call" needs to support for its
  // primary, ordinary use -- calling a named subroutine), it conservatively always takes the safe, always-
  // correct lcall for a label, never attempting to guess whether the label's eventual address would have
  // fit scall. This means "call" to an already-defined nearby label will sometimes emit an lcall where a
  // human hand-writing "scall" directly could have gotten away with the shorter form -- a known, accepted
  // conservatism, not a bug, until/unless Stefan asks for a real relaxation pass instead.

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
  // ("f") / PushFrameMnemonic ("pushf", renamed 2026-09-16 from "fpush") plus offset arithmetic instead.

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
  // pointer f into register r; 'fpush (RENAMED 'pushf, see immediately below) pushes f onto the CVM data
  // stack directly. Neither takes an assembled operand -- computing "address of local/parameter N" is
  // now the CALLER's job (push f via pushf, push the offset via pushlit, then sub/add -- locals subtract,
  // parameters add, matching node 506's own f/main "inv" convention), not a single self-describing
  // instruction the way lal/lap used to be. See Ga144.C.Toolchain.CCodeGenerator's own remarks for the
  // replacement codegen sequence.
  public const string FrameToRegisterMnemonic = "f";

  // RENAMED 2026-09-16, per Stefan directly: node 506's own F18 word (and this CVM mnemonic) was
  // "'fpush"/"fpush" from 2026-09-09 until this rename. Stefan renamed node 506's own source word to
  // "'pushf" specifically to free the name "fpush" for node 306's own, unrelated floating-point push
  // (see FloatingPointPushMnemonic below) -- resolving the naming collision this project had previously
  // left flagged rather than resolved unilaterally (see FloatingPointPopMnemonic's own remarks for that
  // history). The C# constant name is kept as PushFrameMnemonic (still accurately describes what it
  // does -- push node 506's own frame pointer) even though its own STRING value changed; every call site
  // (Ga144.C.Toolchain.CCodeGenerator.EmitLocalOrParameterAddress, Ga144.Evb.Ide.Services.
  // CvmAssemblyLanguage.NodeSymbolByMnemonic) goes through this constant, EXCEPT CCodeGenerator's own
  // EmitCode("fpush") call, which had to be fixed by hand since it hardcoded the mnemonic as a raw string
  // literal instead of referencing this constant -- see that method's own remarks.
  public const string PushFrameMnemonic = "pushf";

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

  // REDEFINED 2026-09-30, for the new VM's own "call" family (see this file's own class-level remarks
  // right after ConditionalBranchMnemonic, above): LongCallMnemonic's own STRING ("lcall") survives, but
  // everything about its SHAPE just above -- node 407, the 0xC000 "tag | local address" scheme, the
  // node-507/node-407 relay protocol, the real-hardware confirmation -- describes CVM2's OLD lcall only,
  // fully retired along with every other CVM2 opcode in this reset (see Instructions' own remarks at the
  // top of its list). The new VM's own lcall shares nothing with it beyond the name: no node, no relay, no
  // 0xC000 tag -- see ShortCallMnemonic's own remarks (above) and LongCallTag's own remarks (below) for
  // the new, unrelated shape this mnemonic string now means. Kept here, unedited otherwise, purely as the
  // historical record of what "lcall" used to be, per this file's own "do not remove any opcodes"/"do not
  // rewrite history" convention.

  /// <summary>
  /// The new VM's own scall (2026-09-30): the widest word address it can directly encode into its own
  /// opcode word -- per spec row "00aa|aaaa|aaaa|aaaa|" (see this file's own class-
  /// level remarks on the new VM's "call" family, right after <see cref="ConditionalBranchMnemonic"/>, for
  /// Stefan's exact wording and the full derivation). Structurally the same idea as CVM2's OLD
  /// <see cref="CallAddressMask"/> (also <see cref="CvmOperandEncoding.EmbeddedAddress"/>, also "the whole
  /// word IS the address"), just one bit narrower -- <see cref="CallAddressMask"/> itself is untouched,
  /// kept purely for the historical CVM2 record; this is scall's own, independent mask, carried on its own
  /// <see cref="Instructions"/> row via <see cref="CvmInstructionShape.ValueBitMask"/> rather than a second
  /// hardcoded constant baked into every EmbeddedAddress consumer the way <see cref="CallAddressMask"/>
  /// used to be -- see <see cref="CvmOperandEncoding.EmbeddedAddress"/>'s own remarks for that
  /// generalization.
  ///
  /// NARROWED 2026-10-02, from 0x3FFF (14 bits) to 0x1FFF (13 bits): the "CVM_pipeline" bit-pattern
  /// table Stefan pasted that day (see this file's own class-level remarks right after
  /// <see cref="UnlinkMnemonic"/>) gives scall's row as "000a|aaaa|aaaa|aaaa|" -- THREE fixed leading
  /// zero bits, not two, leaving a 13-bit address field. Independently corroborated by node 505's own
  /// live d1/main dispatch, which only strips a leading "000" prefix (not "00") before falling to
  /// d/call. This is a narrowing, not a reinterpretation -- every previously-valid scall address still
  /// assembles and disassembles identically, since 13 bits was always a subset of the old 14-bit range.
  /// </summary>
  public const int ShortCallAddressMask = 0x1FFF;

  /// <summary>
  /// The narrowest word address <c>scall</c> can actually be used for: 1, not 0. CONFIRMED 2026-09-30
  /// (same day scall/lcall/call were first wired up), per Stefan directly: "scall 0 does not exist. 'nop'
  /// has precedence." -- the word <c>scall 0</c> would produce, 0x0000, is bit-for-bit identical to
  /// <c>nop</c>'s own fixed word, and nop wins that overlap (<see cref="TryDescribeSelfDecodingWord"/>
  /// already checked every <see cref="CvmOperandEncoding.FixedOpcode"/> shape before the generalized
  /// <see cref="CvmOperandEncoding.EmbeddedAddress"/> loop for exactly this reason, before Stefan even
  /// confirmed it -- see <see cref="CvmOperandEncoding.EmbeddedAddress"/>'s own remarks). Rather than let
  /// an assembled <c>scall 0</c> silently become indistinguishable from <c>nop</c>, both
  /// <see cref="Ga144.Cvm.Toolchain.CvmAssembler"/> and <see cref="Ga144.Evb.Ide.Services.CvmAssemblyLanguage"/>
  /// now reject a literal <c>scall 0</c> outright as a hard assemble error, and the <c>call</c> pseudo-
  /// mnemonic treats address 0 as "does not fit scall" (exactly like a too-large address), always lowering
  /// to <c>lcall</c> instead -- <c>lcall</c> itself has no such restriction: its own tag word (0xF000) does
  /// not overlap 0x0000 at all, so <c>lcall 0</c> is a perfectly ordinary, valid way to call address 0.
  /// <see cref="ShortCallAddressMask"/> itself was separately narrowed to 0x1FFF 2026-10-02 (see that
  /// constant's own remarks) -- this is a separate, narrower lower bound alongside it, not a replacement
  /// for it, and is unaffected by that narrowing.
  /// </summary>
  public const int ShortCallMinimumAddress = 1;

  /// <summary>
  /// The new VM's own lcall (2026-09-30): the fixed, universally-known high-bit pattern (bits 15-10) of
  /// its own tag word, binary 111100 -- spec row "1111|00..|....|....|" (see this file's own class-level
  /// remarks on the new VM's "call" family, right after <see cref="ConditionalBranchMnemonic"/>, for
  /// Stefan's exact wording and the full derivation). The spec's own low 10 bits are marked don't-
  /// care/reserved; this toolchain always emits and matches them as 0 (already true of 0xF000 itself), so
  /// no separate mask constant is needed the way <see cref="BranchTagMask"/>/<see cref="ConditionalBranchTagMask"/>
  /// exist alongside their own tags -- <see cref="CvmOperandEncoding.FixedOpcodeWithTrailingWord"/> always
  /// matches this tag with a plain exact <c>word == LongCallTag</c> check, never a masked one.
  ///
  /// RESOLVED 2026-10-02: the "CVM_pipeline" bit-pattern table Stefan pasted that day (see this
  /// file's own class-level remarks right after <see cref="UnlinkMnemonic"/>) has NO row for lcall at
  /// all, and this constant's own 0xF000 sits squarely inside that table's "unary {32-bit float}"
  /// range (0xF000-0xF7FF) -- flagged at the time as an open question ("does lcall survive this
  /// table, and under what tag?"). Stefan answered the SAME day: "ljmp and lcall must encode in a
  /// similar way like 'ret and their address also taken from labels 'ljmp and 'lcall in node 507.
  /// they are now special opcodes." So lcall DOES survive, but under the "special" tag (0x2000, the
  /// same one ret/link/unlink/ljmp all share -- see <see cref="Instructions"/>' own remarks on Ids
  /// 195/196), not this one -- the collision this constant's own remarks used to flag no longer has
  /// anything live to collide. This constant itself is now referenced by NOTHING live at all (Id
  /// 183, the only row that ever used it, is retired just below in <see cref="Instructions"/>) --
  /// kept, unedited in VALUE, purely as that retired row's own historical record, per this file's
  /// "do not remove any opcodes" convention.
  /// </summary>
  public const int LongCallTag = 0xF000;

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
  // ValueBitMask (see AddressRegisterRegisterFieldBitMask's own remarks just below, renamed 2026-09-15
  // from Node306RegisterFieldBitMask), so every one of these six
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

  // arinc2/ardec2 -- NEW 2026-09-15, alongside the physical renumbering below ("node 306 and 308 have
  // swapped roles" -- see Cvm.Node308Program's own remarks). Stefan: "I gave up the 7th register for 2
  // new opcodes: arinc2 and ardec2" -- increment/decrement an address register by 2 instead of 1, per
  // the trailing opcode table in Cvm.Node308Program's own Source. Share arinc/ardec's own tag and field
  // layout exactly (same CvmOperandEncoding.NodeResolvedEmbeddedValue shape, same
  // AddressRegisterFunctionField*/AddressRegisterRegisterFieldBitMask below) -- wired in mechanically on
  // the strength of that shared, already-confirmed encoding shape, even though the two new F18 words'
  // own bodies use a "leap"/"then" construct not otherwise seen in this project and not understood here.
  // See Cvm.Node308Program's own remarks for the full flag and why the encoding-level wiring doesn't
  // depend on resolving that uncertainty.
  public const string ArithmeticIncrementAddressRegisterByTwoMnemonic = "arinc2";
  public const string ArithmeticDecrementAddressRegisterByTwoMnemonic = "ardec2";

  // RENUMBERED 2026-09-15: this whole address-register family (arinc/ardec/arld/arst/lda/sta, plus the
  // new arinc2/ardec2 above) moved from physical node 306 to physical node 308 -- Stefan: "node 306 and
  // 308 have swapped roles." The four field-layout constants just below were named "Node306*" up through
  // 2026-09-11 and are RENAMED here to "AddressRegisterFunctionField*"/"AddressRegisterRegisterFieldBitMask"
  // (dropping the node number entirely) rather than becoming "Node308*" -- that name was previously
  // taken by this node's OWN prior, unrelated "VM 32 arithmetic" family (dpop/dpush/dinc/ddec/dadd/dor),
  // since REMOVED OUTRIGHT the same day per Stefan's own direct instruction (see the removal note above
  // FloatingPointFunctionFieldBitMask), and node-number-based naming has now proven unstable across this
  // project's own history (306 was corrected from "four" to "six" registers, then renumbered to 308
  // outright) -- a function-based name survives the next renumbering, if there is one. The VALUES are
  // completely unchanged from the "Node306*" constants they replace.
  //
  // Node306LoadAddressRegisterTag/Node306StoreAddressRegisterTag/Node306IncrementAddressRegisterTag/
  // Node306DecrementAddressRegisterTag/Node306LoadAddressRegisterValueTag/Node306StoreAddressRegisterValueTag
  // and Node306AddressRegisterIndexBitMask/Node306AddressRegisterIndexShift -- DELETED OUTRIGHT
  // 2026-09-09 alongside ldar/star/inca/deca above; see this file's own remarks above (both on the
  // CVM1-opcode purge and on that constant's own retirement note) for the full accounting.

  /// <summary>Isolates the address-register family's 6-bit "which function" field (bits 8-3) of a
  /// resolved lda/sta/arinc/ardec/arinc2/ardec2/arld/arst opcode word -- the resolved address (0-63,
  /// this node's own "# 0x0c org" needing no bias) shifted left by
  /// <see cref="AddressRegisterFunctionFieldShift"/>. Derived directly from ar/main's own dispatch tail,
  /// "2/ 2/ 2/ 0x3f and ex": shifting the incoming call byte right by 3 and masking to 6 bits recovers
  /// the function address, so encoding is the inverse -- shift the resolved address left by 3 before
  /// OR-ing it in. RENAMED 2026-09-15 from <c>Node306FunctionFieldBitMask</c> (same value) -- see this
  /// family's own renumbering remarks just above. See <see cref="ArithmeticStoreAddressRegisterMnemonic"/>'s
  /// own remarks for the original correction this accompanies.</summary>
  public const int AddressRegisterFunctionFieldBitMask = 0x01F8;

  /// <summary>How far left the address-register family's resolved function address is shifted before
  /// OR-ing into <see cref="AddressRegisterFunctionFieldBitMask"/>'s bits -- 3, since the 3-bit register
  /// field occupies bits 2-0 below it. RENAMED 2026-09-15 from <c>Node306FunctionFieldShift</c> (same
  /// value). See <see cref="ArithmeticStoreAddressRegisterMnemonic"/>'s own remarks.</summary>
  public const int AddressRegisterFunctionFieldShift = 3;

  /// <summary>The address-register family's own function field needs no base-address subtraction
  /// (always 0) -- its "# 0x0c org" range already starts inside the 0-63 window this field covers.
  /// RENAMED 2026-09-15 from <c>Node306FunctionFieldBaseAddress</c> (same value). See
  /// <see cref="ArithmeticStoreAddressRegisterMnemonic"/>'s own remarks.</summary>
  public const int AddressRegisterFunctionFieldBaseAddress = 0;

  /// <summary>Isolates the address-register family's 3-bit register-index field (bits 2-0, unshifted) --
  /// straight off ar/main's own "dup 0x07 and". Doubles as this family's
  /// <see cref="CvmInstructionShape.ValueBitMask"/> in <see cref="Instructions"/> below, so
  /// <see cref="CvmAssembler"/>'s own operand-range check reads the valid register range straight off
  /// the shape without a separate lookup -- note this mask alone (0-7) does NOT reflect how many
  /// registers actually exist for a given <c>org</c> (currently 6, per
  /// `claude/cvm-node306-307-address-registers.md`'s own org-dependent-capacity addendum); the assembler
  /// does not currently validate the operand against the live <c>org</c>, only against this fixed 3-bit
  /// field width -- see that same addendum's own open question. RENAMED 2026-09-15 from
  /// <c>Node306RegisterFieldBitMask</c> (same value). See <see cref="ArithmeticStoreAddressRegisterMnemonic"/>'s
  /// own remarks.</summary>
  public const int AddressRegisterRegisterFieldBitMask = 0x0007;

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
  //
  // REPOINTED, 2026-09-27 -- Stefan redesigned nodes 510/511 wholesale: physical node 511 is now pure
  // passive 64-word register STORAGE with no logic of its own at all ("register storage, storage for 64
  // register"), and physical node 510 ("extended register access") absorbs everything r/main used to do,
  // directly, one hop closer to node 509 (imports node 509, not node 510) -- see Cvm.Node510Program's and
  // Cvm.Node511Program's own remarks for the new source. Node 510's own new reg/main once again names
  // rld/rst/rpop/rpush (per "only update existing opcodes where possible") -- Ids 107-110 and these four
  // mnemonic strings are UNCHANGED, only WHICH node/symbol/field-layout they resolve against changes (see
  // RegisterAccessTag/RegisterAccessRegisterFieldBitMask/RegisterAccessRegisterFieldShift below, and
  // Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own NodeSymbolByMnemonic/
  // NodeResolvedEmbeddedValueFieldLayoutByMnemonic entries). The OLD node-511 field layout just below
  // (Node511FunctionFieldBitMask/Shift/BaseAddress, Node511RegisterFieldBitMask) is now SUPERSEDED, not
  // deleted -- kept per this file's own "do not remove any opcodes" convention, describing the PRIOR
  // (2026-09-07 through 2026-09-27) revision of this opcode family for historical reference.
  public const string LoadRegisterFileMnemonic = "rld";
  public const string StoreRegisterFileMnemonic = "rst";
  public const string PopRegisterFileMnemonic = "rpop";
  public const string PushRegisterFileMnemonic = "rpush";

  /// <summary>SUPERSEDED 2026-09-27 (see <see cref="LoadRegisterFileMnemonic"/>'s own remarks) -- isolates node 511's OLD 5-bit "which function" field (bits 9-5) -- the resolved target address (0x20-0x3F) minus <see cref="Node511FunctionFieldBaseAddress"/>, shifted left by <see cref="Node511FunctionFieldShift"/>. Kept for historical reference; the current layout is <see cref="RegisterAccessFunctionFieldBitMask"/>.</summary>
  public const int Node511FunctionFieldBitMask = 0x03E0;

  /// <summary>SUPERSEDED 2026-09-27 -- how far left node 511's OLD resolved (address - <see cref="Node511FunctionFieldBaseAddress"/>) was shifted before OR-ing into <see cref="Node511FunctionFieldBitMask"/>'s bits -- 5, since the OLD 5-bit register field occupied bits 4-0 below it. Kept for historical reference; see <see cref="RegisterAccessFunctionFieldShift"/> for the current value.</summary>
  public const int Node511FunctionFieldShift = 5;

  /// <summary>SUPERSEDED 2026-09-27 -- node 511's OLD "# 0x20 org": every one of its compiled word addresses started at 0x20, so the function-select field was the RESOLVED address minus this. Kept for historical reference; see <see cref="RegisterAccessFunctionFieldBaseAddress"/> for the current value (0, unconfirmed -- see Cvm.Node510Program's own remarks).</summary>
  public const int Node511FunctionFieldBaseAddress = 0x20;

  /// <summary>SUPERSEDED 2026-09-27 -- isolates node 511's OLD 5-bit register-index field (bits 4-0, unshifted, 0-31). Kept for historical reference; see <see cref="RegisterAccessRegisterFieldBitMask"/> for the current (6-bit, 0-63) value.</summary>
  public const int Node511RegisterFieldBitMask = 0x001F;

  // The new "extended register access" family (2026-09-27, replacing node 511's old direct field-
  // extraction scheme above) -- Stefan's own header: "handle reg[x] access. 0 <= x <= 63", bit layout
  // "1011_1?xx_xxxx_?ooo" (ooo: 3-bit operation code, xxxxxx: 6-bit register selection, the two "?" bits
  // unused/don't-care). This is now ONE hop off node 509 (not two, as the old node 509 -> 510 -> 511
  // chain was) -- physical node 510 does everything itself and merely forwards the actual storage
  // read/write to physical node 511's own passive RAM. See Cvm.Node510Program's own remarks for the full
  // derivation and, importantly, for what is FLAGGED as unconfirmed rather than guessed here: how the
  // 3-bit "ooo" field actually indexes into the 8-entry jump table (each entry may be more than one word),
  // and where register r is loaded with the jump target before reg/main's own final "ex".
  //
  // Register field: bits 9-4 (6 bits, 0-63) -- WIDER than the old node-511 layout (5 bits, 0-31) and, for
  // the first time in this family, NOT at bits 4-0 -- shifted up by 4 to make room for the 3-bit op field
  // below it. This is why Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own
  // NodeResolvedEmbeddedValueFieldLayoutByMnemonic dictionary gained a RegisterFieldShift member
  // (2026-09-27) alongside its existing RegisterFieldBitMask -- every earlier NodeResolvedEmbeddedValue
  // family (node 511's old layout, node 308's address-register family) happened to keep its own register
  // field at bit 0, so no shift was ever needed before now.
  public const int RegisterAccessRegisterFieldBitMask = 0x003F;

  /// <summary>How far left the register operand is shifted before OR-ing it into the opcode word -- 4, since the 3-bit op-select field (see <see cref="RegisterAccessFunctionFieldBitMask"/>) occupies bits 2-0 below it. See <see cref="RegisterAccessRegisterFieldBitMask"/>'s own remarks.</summary>
  public const int RegisterAccessRegisterFieldShift = 4;

  /// <summary>Isolates the 3-bit "ooo" op-select field (bits 2-0, unshifted, 0-7) -- per Stefan's own table: 0=clear (rclr), 1=set (rlit, NOT yet wired -- see <see cref="LoadRegisterFileMnemonic"/>'s remarks two blocks up for why), 2=pop2 (rpop2), 3=push2 (rpush2), 4=load (rld), 5=store (rst), 6=pop (rpop), 7=push (rpush).</summary>
  public const int RegisterAccessFunctionFieldBitMask = 0x0007;

  /// <summary>No shift -- the op-select field sits at bits 2-0, the bottom of the word. See <see cref="RegisterAccessFunctionFieldBitMask"/>'s own remarks.</summary>
  public const int RegisterAccessFunctionFieldShift = 0;

  /// <summary>NOT CONFIRMED against a live compile -- assumes node 510's own 8-entry jump table (<c># 0 org</c>, "reg/clr ; reg/lit ; reg/po2 ; reg/pu2 ; reg/ld ; reg/st ; reg/po ; reg/pu ;") places one entry per word starting at address 0, so the op-select field needs no bias at all. Each entry as pasted compiles a CALL-then-RETURN pair, which strongly suggests 2 words per entry, not 1 -- see Cvm.Node510Program's own remarks for why this is flagged rather than guessed, and Stefan should confirm before this base address (or a required x2 op-index scaling this file does not currently apply) is trusted.</summary>
  public const int RegisterAccessFunctionFieldBaseAddress = 0;

  // The tag shared by every "extended register access" opcode -- the fixed top 5 bits of
  // "1011_1?xx_xxxx_?ooo", i.e. 0xB800 (mask 0xF800). This is the SAME top-level bit range the OLD node
  // 510 ("extended arithmetic", 0xB800-0xBBFF) and OLD node 511 ("register file", 0xBC00-0xBFFF) used to
  // split between them -- the new single node now answers the whole combined range directly, one hop
  // closer to node 509. Like every other live-node-resolved tag, this lives in
  // Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own NodeSymbolByMnemonic (as Node510RegisterAccessTag),
  // not here -- ValueBitMask/ValueBitShift above are the only pieces of this shape's layout this file
  // itself needs, for CvmAssembler's own offline register-range validation and embedding.

  // Three genuinely new mnemonics from this same family (op codes 0, 2, 3 -- see
  // RegisterAccessFunctionFieldBitMask's own remarks for the full 0-7 table). Each is a plain
  // register-index-only op (WordLength 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, no trailing word)
  // -- confirmed from each one's own F18 body (reg/clr: "dup xor reg/wr", no word fetch; reg/pu2: "reg/rd
  // reg/rd reg/push reg/push", no word fetch either) -- unlike op code 1 (rlit), see below.
  public const string RegisterClearMnemonic = "rclr";
  public const string RegisterPopPairMnemonic = "rpop2";
  public const string RegisterPushPairMnemonic = "rpush2";

  // FLAGGED, NOT WIRED: op code 1, "rlit" ("set", "reg[x] = next_word" per Stefan's own table). Node
  // 510's own "reg/lit" is defined as a plain word-reference with no trailing ";" (falls through into
  // reg/next), and reg/next's own body chains reg/get -> u/next -> node 508's g/next -> node 507's own
  // m/next -- the CVM-level "fetch the NEXT WORD from the instruction stream" primitive every other
  // TrailingWord mnemonic in this file (pushlit, gld/gst, litr, ...) also goes through. This strongly
  // suggests "rlit" is a TWO-WORD instruction (opcode word carrying the register index, PLUS a trailing
  // literal word) -- a shape this file has never needed before: every existing
  // CvmOperandEncoding.NodeResolvedEmbeddedValue mnemonic (node 511's old family above, node 308's
  // address-register family) is WordLength 1, register-only, no trailing word at all. Rather than invent
  // a new combined encoding (register embedded in the opcode word AND a trailing operand word) on a
  // guess, "rlit" is deliberately left OUT of Instructions below -- Stefan should confirm the exact word
  // count/operand shape before this is wired in. The mnemonic string itself is recorded here so the name
  // is reserved and this gap is easy to find later.
  public const string RegisterSetMnemonic = "rlit";

  // REMOVED OUTRIGHT, 2026-09-15, per Stefan's own direct instruction ("remove the old dpop/dpush/
  // dinc/ddec/dadd/dor family completely"). This used to be "Node308FunctionFieldBitMask"/"Shift"/
  // "BaseAddress"/"RegisterFieldBitMask" (values 0x00FC/2/0/0x0003), the field layout for physical node
  // 308's OWN PRIOR role -- the "VM 32 arithmetic" dpop/dpush/dinc/ddec/dadd/dor family, added
  // 2026-09-09, orphaned by the same day's "node 306 and 308 have swapped roles" renumbering (node
  // 308's CURRENT source is the address-register mesh, see AddressRegisterFunctionFieldBitMask's own
  // remarks, and never defined this family in the first place). UNLIKE the 2026-09-09 CVM1-opcode purge
  // precedent (which permanently retires an Id rather than ever reusing it for a different meaning),
  // Stefan asked for this family removed completely rather than merely orphaned-and-flagged -- so these
  // four constants, the six mnemonic constants below, and their six Instructions rows are deleted
  // outright. See the Instructions table's own remark at Ids 123-128 (their numbers are still never
  // reused, exactly like every other retired range in this file) and
  // claude/cvm-node306-307-address-registers.md's own final addendum for the full history of how this
  // family became orphaned before being removed here.

  // Node 306/305's floating-point engine REWORKED, 2026-09-21, per Stefan directly ("i want to to a
  // rework of the FP engine. it does not function well in the current state."): node 306 is now a pure
  // INSTRUCTION DECODER (see Cvm.Node306Program's own remarks) that dispatches into node 305, which now
  // "contains the floatingpoint register" (the register file moved from 306 to 305 -- the two have
  // swapped roles again, same as the 2026-09-15 306/308 swap before it). The old three-way split (a
  // UNARY tag 0xDC00-0xDCFF for 'fpop/'fpush, a CONSTANT-lookup tag 0xDD00-0xDDFF, and a BINARY tag
  // 0xDE00-0xDFFF for a 3-bit op field) is GONE, replaced by ONE unified family, confirmed directly from
  // node 306's own header table and its own "opcode 1101_11oo_oogg_gfff" spec: a single fixed tag
  // (0xDC00, the "1101_11" 6-bit prefix) covers all twelve operations, with a WIDER 4-bit operation
  // field (oooo, not the old 3-bit ooo) selecting which of add/sub/min/max/mul/div/move/const/neg/abs/
  // pop/push runs -- see FloatingPointAddMnemonic's own remarks for the full mnemonic list and the
  // per-mnemonic tag derivation. The numeric tag value 0xDC00 is NOT reused under its OLD meaning here
  // (node 306's now-retired unary-only 'fpop/'fpush tag) -- exactly the same "tag bits stay, meaning
  // moves" precedent as the 2026-09-15 removal note above Node511RegisterFieldBitMask. Every mnemonic
  // this old scheme wired (fpop/fpush/fadd/fsub/fmin/fmax/fmul/fdiv/fln2/filn2/fpi2/f2pi) and its
  // constants (FloatingPointFunctionFieldBitMask/Shift/BaseAddress, FloatingPointBinaryOperationTag/
  // FieldBitMask/FieldShift, FloatingPointConstantLookupTag/OffsetFieldBitMask/Shift, and the four
  // FloatingPointXxxFunctionFieldBaseAddress constants) is retired outright along with it -- fln2/filn2/
  // fpi2/f2pi have NO replacement mnemonic (node 306's new "fconst f g" is a single generic op, not four
  // named constants), and fpop/fpush keep their names but move from the old NodeResolvedEmbeddedValue
  // (tied to a live-compiled function address on 306) to the new, fully self-describing scheme below.
  //
  // New field layout, straight from node 306's own header comment and opcode spec ("opcode
  // 1101_11oo_oogg_gfff binary floatingpoint operation. first operand is fff. second operant is ggg.
  // result in fff. operation in oooo"): fff (bits 2-0, register operand AND result) and ggg (bits 5-3,
  // second/read-only register operand) keep the exact same bit positions and widths the old binary-op
  // family already used -- only the operation field widened, from 3 bits (bits 8-6) to 4 bits (bits 9-6),
  // to fit all twelve operations instead of six.
  public const int FloatingPointRegisterFieldBitMask = 0x0007;

  /// <summary>Isolates node 306's 3-bit second-operand register field, <c>ggg</c> (bits 5-3, unshifted, 0-7). See the remarks above <see cref="FloatingPointRegisterFieldBitMask"/>.</summary>
  public const int FloatingPointSecondOperandRegisterFieldBitMask = 0x0038;

  /// <summary>How far left the second-operand register index is shifted before OR-ing into <see cref="FloatingPointSecondOperandRegisterFieldBitMask"/>'s bits -- 3, since the 3-bit <c>fff</c> field occupies bits 2-0 below it. See the remarks above <see cref="FloatingPointRegisterFieldBitMask"/>.</summary>
  public const int FloatingPointSecondOperandRegisterFieldShift = 3;

  /// <summary>
  /// The fixed high-bit pattern (bits 15-10, "110111") of node 306's UNIFIED floating-point operation
  /// word family, with every variable field (oooo/ggg/fff) zeroed: binary 1101_1100_0000_0000. See the
  /// remarks above <see cref="FloatingPointRegisterFieldBitMask"/> for why this reuses the numeric value
  /// the old, now-retired unary-only tag also happened to use.
  /// </summary>
  public const int FloatingPointOperationTag = 0xDC00;

  /// <summary>Isolates node 306's 4-bit operation field (bits 9-6: 0=add, 1=sub, 2=min, 3=max, 4=mul, 5=div, 6=move, 7=const, 8=neg, 9=abs, 10=pop, 11=push; 12-15 unused) of a <see cref="FloatingPointOperationTag"/> word. See the remarks above <see cref="FloatingPointRegisterFieldBitMask"/>.</summary>
  public const int FloatingPointOperationFieldBitMask = 0x03C0;

  /// <summary>How far left the operation code is shifted before OR-ing into <see cref="FloatingPointOperationFieldBitMask"/>'s bits -- 6, since the 3-bit <c>ggg</c> field (bits 5-3) and 3-bit <c>fff</c> field (bits 2-0) both sit below it. See the remarks above <see cref="FloatingPointRegisterFieldBitMask"/>.</summary>
  public const int FloatingPointOperationFieldShift = 6;

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

  // Node 508's own literal-load family, added 2026-09-27 per Stefan: "add litr, litm and lit2 to the
  // language" -- three new tick-prefixed words ('litr/'litm/'lit2) on node 508, right alongside 'gld/
  // 'gst (see Node508Program's own remarks for the full source and the fall-through mechanism between
  // 'litm and 'litr). Unlike lit (node 509, self-describing, 9-bit embedded signed value, no live
  // compile at all -- see LitMnemonic's own remarks), all three of these are dynamically-resolved-
  // address opcodes exactly like gld/gst: TrailingWord/TwoTrailingWords shaped, tag | node 508's own
  // live-compiled address for the word, resolved the same way (Node508LoadStoreGlobalTagBits, 0xA000 --
  // see that constant's own remarks in Ga144.Evb.Ide.Services.CvmAssemblyLanguage) rather than a fixed,
  // self-describing tag of their own.
  //
  // litr <value>: TrailingWord (one operand word) -- stores the trailing word directly into register r
  // (node 508's own 'litr: g/next then g/r!). The wider, unrestricted counterpart to lit for when a
  // value doesn't fit lit's narrow -256..255 signed range -- see the new "literal" pseudo-mnemonic in
  // Ga144.Evb.Ide.Services.CvmAssemblyLanguage/Ga144.Cvm.Toolchain.CvmAssembler (added the same day),
  // which picks lit or litr automatically so a hand-written .cvmasm source (or a future C-compiler call
  // site) never has to make that choice itself.
  //
  // litm <high> <low>: TwoTrailingWords (two operand words) -- pushes the first trailing word onto the
  // data stack, then stores the second into register r. OPTIMIZED 2026-09-27, same day as the addition
  // (Stefan: "i could optimize 2 words in node 508"): node 508's own 'litm is now a plain CALL to a
  // shared internal word, g/lit1 (fetch-and-push, not a CVM mnemonic of its own), with NO closing ";" of
  // its own -- the call's return lands exactly at 'litr's own entry (since 'litm has nothing else
  // compiled after the call), so calling 'litm still ends up pushing the first word then falling through
  // into 'litr's own "store to r" body, same net effect as the original inline version, just via a real
  // call to shared code instead of a duplicated inline copy -- see Node508Program's own remarks for the
  // full trace and for how "leap"/"then" (the mechanism 'lit2 uses to share the same g/lit1) were
  // finally confirmed to mean "compile a CALL to wherever the matching then resolves to".
  //
  // lit2 <high> <low>: TwoTrailingWords (two operand words) -- pushes BOTH trailing words onto the data
  // stack, in order. OPTIMIZED 2026-09-27 alongside litm: node 508's own 'lit2 is now just a single
  // "leap" (a CALL, per DB013 5.3.2.1) to the same shared g/lit1 -- because the call's own natural return
  // address happens to equal g/lit1's own entry address (nothing is compiled between the leap and its
  // matching then), returning from the first fetch-and-push re-enters g/lit1 a second time before the
  // genuine caller's own return address is reached, so g/lit1's body runs exactly twice per 'lit2 call.
  // See Node508Program's own remarks for the full instruction-by-instruction trace.
  public const string LitrMnemonic = "litr";
  public const string LitmMnemonic = "litm";
  public const string Lit2Mnemonic = "lit2";

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

  // REMOVED OUTRIGHT, 2026-09-15: node 308's old dpop/dpush/dinc/ddec/dadd/dor mnemonic constants
  // (DoublePopMnemonic "dpop", DoublePushMnemonic "dpush", DoubleIncrementMnemonic "dinc",
  // DoubleDecrementMnemonic "ddec", DoubleAddMnemonic "dadd", DoubleOrMnemonic "dor") -- see the removal
  // note above FloatingPointRegisterFieldBitMask for why (Stefan's own direct instruction), and the
  // Instructions table's own remark at Ids 123-128 for the retired-and-never-reused Id range.

  /// <summary>
  /// Node 306/305's twelve unified floating-point operation mnemonics -- REWORKED 2026-09-21, per
  /// Stefan's own new node 306 header table ("index operation mnemonic"), replacing the old six-mnemonic
  /// binary-only family plus the separately-schemed <c>'fpop</c>/<c>'fpush</c>/<c>fln2</c>/<c>filn2</c>/
  /// <c>fpi2</c>/<c>f2pi</c> (see the removal note above <see cref="FloatingPointRegisterFieldBitMask"/>
  /// for the full history and why the old scheme is gone rather than extended). Node 306 is now a pure
  /// decoder: it imports node 305 (which now holds the actual floating-point register file -- the two
  /// swapped roles) and node 307 (the CVM relay, unchanged), and dispatches every one of these twelve
  /// mnemonics to one of node 305's own exported functions (<c>fr/binary</c>/<c>fr/move</c>/
  /// <c>fr/const</c>/<c>fr/neg</c>/<c>fr/abs</c>/<c>fr/pop</c>/<c>fr/push</c>) -- none of that matters at
  /// the CVM-opcode level, though, since all twelve are genuinely self-describing.
  ///
  /// Bit layout, confirmed directly from node 306's own header (<c>1101_11oo_oogg_gfff</c>): fixed prefix
  /// bits 15-10 = 110111 (<see cref="FloatingPointOperationTag"/>, 0xDC00), <c>oooo</c> (bits 9-6,
  /// <see cref="FloatingPointOperationFieldBitMask"/>/<see cref="FloatingPointOperationFieldShift"/>,
  /// FOUR bits now, not three) selects the operation, <c>ggg</c> (bits 5-3,
  /// <see cref="FloatingPointSecondOperandRegisterFieldBitMask"/>) is the second operand register,
  /// <c>fff</c> (bits 2-0, <see cref="FloatingPointRegisterFieldBitMask"/>) is the first operand register
  /// (also where the result is written). Assembler syntax, per Stefan verbatim: "mnemonic f g", e.g.
  /// "fadd 3 2" means <c>fr[3] = fr[3] + fr[2]</c> -- uniform across TEN of the twelve mnemonics
  /// (everything except <c>fpop</c>/<c>fpush</c>, see below), fully self-describing
  /// (<see cref="CvmOperandEncoding.EmbeddedUnsignedValuePair"/>, no live node compile needed to assemble
  /// or disassemble): per-mnemonic tag = <see cref="FloatingPointOperationTag"/> | (oooo &lt;&lt;
  /// <see cref="FloatingPointOperationFieldShift"/>): add=0, sub=1, min=2, max=3, mul=4, div=5, move=6,
  /// const=7, neg=8, abs=9, pop=10, push=11 (12-15 unused, no mnemonic).
  ///
  /// <b>CORRECTED, 2026-09-21, per Stefan directly ("fpush and fpop only have 1 parameter, the other
  /// opcodes have 2 parameter"): <c>fpop</c>/<c>fpush</c> are assembler syntax "mnemonic f" -- ONE
  /// argument, not two.</b> node 306's own source still decodes a full <c>(f g)</c> pair out of every
  /// opcode word unconditionally (<c>fp/main</c> makes no distinction by operation before calling
  /// <c>ex</c>), and <c>fr/pop</c>/<c>fr/push</c> on node 305 likewise still receive a <c>g</c> value on
  /// their own stack and simply never use it -- but at the CVM-opcode level, where <c>ggg</c> is written
  /// is these two mnemonics' own business, and Stefan's correction is that the assembler must not ask for
  /// a second operand at all; the emitted word's <c>ggg</c> bits are just always zero. See
  /// <see cref="FloatingPointPopTag"/>/<see cref="FloatingPointPushTag"/>'s own remarks and these two
  /// mnemonics' own <see cref="CvmInstructionShape"/> rows (plain <see cref="CvmOperandEncoding.EmbeddedUnsignedValue"/>,
  /// not the Pair encoding every other one of the twelve uses).
  /// </summary>
  public const string FloatingPointAddMnemonic = "fadd";
  public const string FloatingPointSubtractMnemonic = "fsub";
  public const string FloatingPointMinimumMnemonic = "fmin";
  public const string FloatingPointMaximumMnemonic = "fmax";
  public const string FloatingPointMultiplyMnemonic = "fmul";
  public const string FloatingPointDivideMnemonic = "fdiv";
  public const string FloatingPointMoveMnemonic = "fmove";
  public const string FloatingPointConstantMnemonic = "fconst";
  public const string FloatingPointNegateMnemonic = "fneg";
  public const string FloatingPointAbsoluteMnemonic = "fabs";
  public const string FloatingPointPopMnemonic = "fpop";
  public const string FloatingPointPushMnemonic = "fpush";

  /// <summary>Per-mnemonic tag for <see cref="FloatingPointAddMnemonic"/> -- operation index 0 (<see cref="FloatingPointOperationTag"/> | (0 &lt;&lt; <see cref="FloatingPointOperationFieldShift"/>)).</summary>
  public const int FloatingPointAddTag = FloatingPointOperationTag | (0 << FloatingPointOperationFieldShift);
  /// <summary>Per-mnemonic tag for <see cref="FloatingPointSubtractMnemonic"/> -- operation index 1.</summary>
  public const int FloatingPointSubtractTag = FloatingPointOperationTag | (1 << FloatingPointOperationFieldShift);
  /// <summary>Per-mnemonic tag for <see cref="FloatingPointMinimumMnemonic"/> -- operation index 2.</summary>
  public const int FloatingPointMinimumTag = FloatingPointOperationTag | (2 << FloatingPointOperationFieldShift);
  /// <summary>Per-mnemonic tag for <see cref="FloatingPointMaximumMnemonic"/> -- operation index 3.</summary>
  public const int FloatingPointMaximumTag = FloatingPointOperationTag | (3 << FloatingPointOperationFieldShift);
  /// <summary>Per-mnemonic tag for <see cref="FloatingPointMultiplyMnemonic"/> -- operation index 4.</summary>
  public const int FloatingPointMultiplyTag = FloatingPointOperationTag | (4 << FloatingPointOperationFieldShift);
  /// <summary>Per-mnemonic tag for <see cref="FloatingPointDivideMnemonic"/> -- operation index 5.</summary>
  public const int FloatingPointDivideTag = FloatingPointOperationTag | (5 << FloatingPointOperationFieldShift);
  /// <summary>Per-mnemonic tag for <see cref="FloatingPointMoveMnemonic"/> -- operation index 6.</summary>
  public const int FloatingPointMoveTag = FloatingPointOperationTag | (6 << FloatingPointOperationFieldShift);
  /// <summary>Per-mnemonic tag for <see cref="FloatingPointConstantMnemonic"/> -- operation index 7.</summary>
  public const int FloatingPointConstantTag = FloatingPointOperationTag | (7 << FloatingPointOperationFieldShift);
  /// <summary>Per-mnemonic tag for <see cref="FloatingPointNegateMnemonic"/> -- operation index 8.</summary>
  public const int FloatingPointNegateTag = FloatingPointOperationTag | (8 << FloatingPointOperationFieldShift);
  /// <summary>Per-mnemonic tag for <see cref="FloatingPointAbsoluteMnemonic"/> -- operation index 9.</summary>
  public const int FloatingPointAbsoluteTag = FloatingPointOperationTag | (9 << FloatingPointOperationFieldShift);
  /// <summary>Per-mnemonic tag for <see cref="FloatingPointPopMnemonic"/> -- operation index 10. UNLIKE the old <c>'fpop</c>, no longer node-resolved -- see the removal note above <see cref="FloatingPointRegisterFieldBitMask"/>. CORRECTED 2026-09-21: assembler syntax is "fpop f" -- ONE argument, not two (see the class-level remarks above <see cref="FloatingPointAddMnemonic"/>) -- so this mnemonic's own <see cref="CvmInstructionShape"/> row uses plain <see cref="CvmOperandEncoding.EmbeddedUnsignedValue"/>, not the Pair encoding the other ten share.</summary>
  public const int FloatingPointPopTag = FloatingPointOperationTag | (10 << FloatingPointOperationFieldShift);
  /// <summary>Per-mnemonic tag for <see cref="FloatingPointPushMnemonic"/> -- operation index 11. UNLIKE the old <c>'fpush</c>, no longer node-resolved -- see the removal note above <see cref="FloatingPointRegisterFieldBitMask"/>. CORRECTED 2026-09-21: assembler syntax is "fpush f" -- ONE argument, not two (see the class-level remarks above <see cref="FloatingPointAddMnemonic"/>) -- so this mnemonic's own <see cref="CvmInstructionShape"/> row uses plain <see cref="CvmOperandEncoding.EmbeddedUnsignedValue"/>, not the Pair encoding the other ten share.</summary>
  public const int FloatingPointPushTag = FloatingPointOperationTag | (11 << FloatingPointOperationFieldShift);

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

  // Node 409's four "bit operation" mnemonics (bclr/bset/binv/bcopy), added 2026-09-27 per Stefan's own
  // complete node 409 F18 source ("CVM2 node 409. bit operations"). Bit layout, confirmed directly from
  // Stefan's own header comment (1111_1bbb_baaa_a0oo): fixed prefix bits 15-11 = 11111
  // (BitOperationTag, 0xF800), bbbb (bits 10-7, BitPositionSecondFieldBitMask/Shift) is the second bit
  // position, aaaa (bits 6-3, BitPositionFieldBitMask/Shift) is the first bit position, bit 2 is a fixed
  // 0, and oo (bits 1-0) selects the operation: 0=bclr, 1=bset, 2=binv, 3=bcopy. Fully self-describing
  // (CvmOperandEncoding.EmbeddedUnsignedValue for bclr/bset/binv, one operand; EmbeddedUnsignedValuePair
  // for bcopy, two operands), no live node compile needed to assemble or disassemble -- mirroring node
  // 306's own split between EmbeddedUnsignedValue (fpop/fpush, one operand) and EmbeddedUnsignedValuePair
  // (the other ten, two operands); see FloatingPointAddMnemonic's own remarks for that precedent.
  //
  // FLAGGED, not silently resolved: Stefan's own header comment lists "ooo: operation, opcode" with
  // slots for values 0 through 7 (implying a 3-bit field), and node 409's own bit/main dispatcher reads
  // exactly 3 bits ("dup 7 and >r") -- but the bit-layout diagram immediately below that same comment
  // hardwires bit 2 to a fixed 0 within this tag family, leaving only 2 bits (bits 1-0) actually
  // variable under a 1111_1??? tag word, so only operations 0-3 (bclr/bset/binv/bcopy) are reachable this
  // way; slots 4-7 are listed but architecturally unreachable under this tag as written. Reproduced
  // verbatim rather than "corrected" either way, per this project's standing convention for an apparent
  // self-contradiction in Stefan's own source (see e.g. Node505Program's own remarks on its 'f/'fx
  // collision).
  //
  // FLAGGED, also not silently resolved: Stefan's own source imports node 407 ("# 407 import"), but node
  // 409 (row 4, col 9) is not node 407's (row 4, col 7) physical grid neighbor under this project's own
  // row*100+column adjacency convention (CvmBootStreamBuilder.GetPhysicalNeighbors) -- node 409's actual
  // physical neighbor at column 8 would be node 408. Reproduced verbatim in Node409Program's own Source
  // string and wired into CvmBootStreamBuilder exactly as written (importing from node 407), per the same
  // "reproduce, flag, don't guess" convention.
  public const string BitClearMnemonic = "bclr";
  public const string BitSetMnemonic = "bset";
  public const string BitInvertMnemonic = "binv";
  public const string BitCopyMnemonic = "bcopy";

  /// <summary>Fixed prefix for node 409's four bit-operation opcodes -- bits 15-11 = 11111, i.e. 0xF800. See the remarks above <see cref="BitClearMnemonic"/> for the full bit-layout derivation.</summary>
  public const int BitOperationTag = 0xF800;

  /// <summary>Isolates node 409's first bit-position operand field, <c>aaaa</c> (bits 6-3, 0-15) -- the sole operand of <c>bclr</c>/<c>bset</c>/<c>binv</c>, and the first operand of <c>bcopy</c>. See the remarks above <see cref="BitClearMnemonic"/>.</summary>
  public const int BitPositionFieldBitMask = 0x0078;

  /// <summary>How far left the first bit-position operand is shifted before OR-ing into <see cref="BitPositionFieldBitMask"/>'s bits -- 3, since bit 2 (the operation field's own fixed-0 bit) and the 2-bit <c>oo</c> field sit below it. See the remarks above <see cref="BitClearMnemonic"/>.</summary>
  public const int BitPositionFieldShift = 3;

  /// <summary>Isolates node 409's second bit-position operand field, <c>bbbb</c> (bits 10-7, 0-15) -- <c>bcopy</c>'s own second operand only; unused by <c>bclr</c>/<c>bset</c>/<c>binv</c>. See the remarks above <see cref="BitClearMnemonic"/>.</summary>
  public const int BitPositionSecondFieldBitMask = 0x0780;

  /// <summary>How far left the second bit-position operand is shifted before OR-ing into <see cref="BitPositionSecondFieldBitMask"/>'s bits -- 7, since the 4-bit <c>aaaa</c> field (bits 6-3), the fixed-0 bit 2, and the 2-bit <c>oo</c> field all sit below it. See the remarks above <see cref="BitClearMnemonic"/>.</summary>
  public const int BitPositionSecondFieldShift = 7;

  /// <summary>Per-mnemonic tag for <see cref="BitClearMnemonic"/> -- operation index 0 (<see cref="BitOperationTag"/> | 0).</summary>
  public const int BitClearTag = BitOperationTag | 0;
  /// <summary>Per-mnemonic tag for <see cref="BitSetMnemonic"/> -- operation index 1.</summary>
  public const int BitSetTag = BitOperationTag | 1;
  /// <summary>Per-mnemonic tag for <see cref="BitInvertMnemonic"/> -- operation index 2.</summary>
  public const int BitInvertTag = BitOperationTag | 2;
  /// <summary>Per-mnemonic tag for <see cref="BitCopyMnemonic"/> -- operation index 3. UNLIKE bclr/bset/binv, takes TWO operands ("bcopy a b") -- see the remarks above <see cref="BitClearMnemonic"/>.</summary>
  public const int BitCopyTag = BitOperationTag | 3;

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
  // PushFrameMnemonic (RENAMED 2026-09-16 from "fpush" to "pushf" -- see that constant's own remarks)
  // plus ordinary sub/add arithmetic against the frame pointer -- so this was
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
    /// Like <see cref="TrailingWord"/>, except TWO operand words follow the tagged opcode word instead
    /// of one (<c>litm</c>, <c>lit2</c> -- added 2026-09-27, node 508, alongside <c>litr</c> which stays
    /// a plain single-<see cref="TrailingWord"/> mnemonic; see <see cref="LitrMnemonic"/>'s own remarks
    /// for all three). <see cref="CvmInstructionShape.WordLength"/> is 3 for this shape (tag word + two
    /// operand words) -- <see cref="Ga144.Evb.Ide.Services.CvmAssemblyLanguage"/>'s own
    /// <c>Assemble</c>/<c>DisassemblePage0</c> and <see cref="Ga144.Cvm.Toolchain.CvmAssembler"/>'s own
    /// pass-1 arg-count check and pass-2 emission were each extended with one new branch for this shape
    /// alongside their existing <see cref="TrailingWord"/> handling -- see those classes' own remarks.
    /// The first operand word is resolved/emitted exactly like <see cref="TrailingWord"/>'s single
    /// operand; the second follows immediately after it, same rules (literal, label, or import).
    /// </summary>
    TwoTrailingWords,

    /// <summary>
    /// The instruction's one and only word directly IS the (eventually resolved) target address, with
    /// no tag at all -- CVM2's OLD <c>call</c>, restricted to <see cref="CallAddressMask"/> so bit 15
    /// stayed clear, now retired alongside every other CVM2 opcode (see <see cref="Instructions"/>'s own
    /// remarks at the top of its list).
    ///
    /// GENERALIZED 2026-09-30, for the new VM's own <c>scall</c> (see this file's own class-level remarks
    /// on the new VM's "call" family, right after <see cref="ConditionalBranchMnemonic"/>): the field
    /// width is no longer a single hardcoded constant every consumer referenced directly -- it is read
    /// generically off each shape's own <see cref="CvmInstructionShape.ValueBitMask"/> instead (0x3FFF,
    /// <see cref="ShortCallAddressMask"/>, for scall; CVM2's OLD call would have used
    /// <see cref="CallAddressMask"/> here had this generalization existed at the time), the same way
    /// <see cref="EmbeddedUnsignedValue"/> already reads its own width per-shape rather than assuming one
    /// fixed value across every mnemonic using it. <see cref="CvmInstructionShape.Tag"/> is expected to
    /// already be aligned to whatever's outside the mask, exactly like <see cref="EmbeddedSignedValue"/>'s
    /// own convention -- scall's own Tag is 0, since its top two bits are simply required to be clear, not
    /// carrying any distinct tag value of their own.
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
    /// <b>Also the address-register family's eight ops (<c>arinc</c>/<c>ardec</c>/<c>arinc2</c>/
    /// <c>ardec2</c>/<c>arld</c>/<c>arst</c>/<c>lda</c>/<c>sta</c>), RE-TASKED to this shape 2026-09-11
    /// once Stefan's own node 306/307 source confirmed a real 3-bit register-select field in ar/main's
    /// own dispatch -- see <see cref="ArithmeticStoreAddressRegisterMnemonic"/>'s own remarks. RENUMBERED
    /// 2026-09-15 from physical node 306 to physical node 308 (see <see cref="Cvm.Node308Program"/>'s own
    /// remarks, "node 306 and 308 have swapped roles") -- the field-layout constants below were renamed
    /// from "Node306*" to "AddressRegisterFunctionField*"/"AddressRegisterRegisterFieldBitMask"
    /// accordingly, values unchanged: <see cref="AddressRegisterFunctionFieldBitMask"/>/
    /// <see cref="AddressRegisterFunctionFieldShift"/>/<see cref="AddressRegisterFunctionFieldBaseAddress"/>/
    /// <see cref="AddressRegisterRegisterFieldBitMask"/>, structurally identical to the PRIOR, now
    /// superseded, node-308 "d" register family's own layout (a plain 0-based function field, no bias),
    /// just a 6-bit/3-bit split instead of that family's 6-bit/2-bit one.</b>
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

    /// <summary>
    /// Node 306/305's twelve unified floating-point operations (<c>fadd</c>/<c>fsub</c>/<c>fmin</c>/
    /// <c>fmax</c>/<c>fmul</c>/<c>fdiv</c>/<c>fmove</c>/<c>fconst</c>/<c>fneg</c>/<c>fabs</c>/<c>fpop</c>/
    /// <c>fpush</c>, added for the original six 2026-09-16, widened to all twelve in the 2026-09-21
    /// rework -- see <see cref="FloatingPointAddMnemonic"/>'s own remarks) -- the first mnemonics in this
    /// project needing TWO independently-packed embedded operands in one word, both self-describing (no
    /// live node compile involved at all, unlike <see cref="NodeResolvedEmbeddedValue"/>): a fixed
    /// <see cref="CvmInstructionShape.Tag"/> (one per mnemonic, baking in node 306's 4-bit operation
    /// code) OR'd with a first unsigned value in <see cref="CvmInstructionShape.ValueBitMask"/> (register
    /// <c>fff</c> -- the first operand, and where the result is written) and a second unsigned value in
    /// <see cref="CvmInstructionShape.SecondValueBitMask"/> (register <c>ggg</c> -- the second operand,
    /// read-only). Assembler syntax is "mnemonic f g" (space-separated, exactly two positional
    /// arguments), per Stefan verbatim -- e.g. "fadd 3 2" means <c>fr[3] = fr[3] + fr[2]</c>. Like
    /// <see cref="EmbeddedSignedValue"/>/<see cref="EmbeddedUnsignedValue"/>, fully known at assemble
    /// time from two literal operands alone, with no label/import operand support (yet), and IS checked
    /// by <see cref="TryDescribeSelfDecodingWord"/>.
    /// </summary>
    EmbeddedUnsignedValuePair,

    /// <summary>
    /// ADDED 2026-09-30, for the new VM's reset (<c>nop</c>, Id 0, the one surviving instruction --
    /// see <see cref="Instructions"/>'s own remarks at the top of its list). The instruction's one and
    /// only word is a fixed, universally-known literal <see cref="CvmInstructionShape.Tag"/> -- nothing
    /// else. This looks similar to <see cref="None"/> (no assembled operand) and to
    /// <see cref="EmbeddedSignedValue"/>/<see cref="EmbeddedUnsignedValue"/> (self-describing, a literal
    /// <see cref="CvmInstructionShape.Tag"/>) but is neither: unlike <see cref="None"/>, NO live node
    /// compile is ever consulted to resolve it -- the opcode word is already fully known the moment the
    /// mnemonic is; and unlike <see cref="EmbeddedSignedValue"/>/<see cref="EmbeddedUnsignedValue"/>,
    /// there is no embedded value field at all (<see cref="CvmInstructionShape.ValueBitMask"/> is
    /// meaningless here, always 0) -- the whole word IS the tag, with nothing else packed into it, so no
    /// operand is ever assembled (<see cref="CvmInstructionShape.HasOperand"/> is false for this
    /// encoding, exactly like <see cref="None"/>). <see cref="CvmAssembler"/> emits
    /// <see cref="CvmInstructionShape.Tag"/> directly with no relocation entry at all -- the same "fully
    /// known from the mnemonic alone" treatment <see cref="EmbeddedAddress"/>/<see cref="EmbeddedSignedValue"/>
    /// give their own literal operand, just with no operand to combine it with here.
    /// <see cref="TryDescribeSelfDecodingWord"/> matches it generically (an exact <c>word == shape.Tag</c>
    /// check over every <see cref="FixedOpcode"/> shape in <see cref="Instructions"/>), checked FIRST,
    /// before the hardcoded <c>call</c>/<c>br</c>/<c>cbr</c>/<c>lit</c> checks below it -- important for
    /// <c>nop</c> specifically, since its own word, 0x0000, would otherwise also satisfy <c>call</c>'s own
    /// "word &lt;= <see cref="CallAddressMask"/>" range check (that hardcoded check itself is commented
    /// out as part of this same reset -- see <see cref="TryDescribeSelfDecodingWord"/>'s own remarks --
    /// but the ordering is kept defensively correct regardless).
    /// </summary>
    FixedOpcode,

    /// <summary>
    /// ADDED 2026-09-30, for the new VM's own <c>lcall</c> (see this file's own class-level remarks on
    /// the new VM's "call" family, right after <see cref="ConditionalBranchMnemonic"/>). Two words: the
    /// first is a fixed, universally-known literal <see cref="CvmInstructionShape.Tag"/> -- like
    /// <see cref="FixedOpcode"/>, no live node compile is ever consulted to resolve it, the tag word is
    /// already fully known the moment the mnemonic is -- but unlike <see cref="FixedOpcode"/> (which takes
    /// no operand at all), a real operand follows in a second, trailing word: the actual target address,
    /// resolved exactly like a <see cref="TrailingWord"/> mnemonic's own trailing operand (a literal,
    /// label, or import all resolve the same way -- see <see cref="Ga144.Cvm.Toolchain.CvmAssembler"/>'s
    /// own <c>EmitOperandWord</c>), just with no node/linker involvement for the LEADING tag word the way
    /// <see cref="TrailingWord"/>'s own tag always needs. <see cref="CvmInstructionShape.HasOperand"/> is
    /// therefore true for this encoding (it is neither <see cref="None"/> nor <see cref="FixedOpcode"/>),
    /// same as every other operand-taking encoding. <see cref="TryDescribeSelfDecodingWord"/> matches the
    /// leading tag word generically (an exact <c>word == shape.Tag</c> check over every
    /// <see cref="FixedOpcodeWithTrailingWord"/> shape in <see cref="Instructions"/>, mirroring
    /// <see cref="FixedOpcode"/>'s own loop) but, unlike every other case there, needs a SECOND word (the
    /// already-fetched trailing operand, if the caller has it) to describe the full instruction -- see that
    /// method's own <c>nextWord</c>/<c>wordLength</c> parameters, added alongside this encoding.
    /// </summary>
    FixedOpcodeWithTrailingWord,

    /// <summary>
    /// ADDED 2026-10-02, for the "CVM_pipeline" bit-pattern table's own <c>next32</c> (see this file's
    /// own class-level remarks right after <see cref="UnlinkMnemonic"/>, table row "0101|....|....|
    /// ....| next32"). Exactly like <see cref="FixedOpcodeWithTrailingWord"/> -- a fixed, universally-
    /// known literal <see cref="CvmInstructionShape.Tag"/> word needing no node/linker involvement at
    /// all -- except TWO trailing operand words follow it instead of one, mirroring how
    /// <see cref="TwoTrailingWords"/> already extends <see cref="TrailingWord"/> for a node-resolved
    /// tag. <c>next32</c> itself fetches a 32-bit (two-word) literal, per its own name and its sibling
    /// row <c>next16</c> (<see cref="FixedOpcodeWithTrailingWord"/>, one word) immediately above it in
    /// the table -- both corroborated by node 506's own live d/next1 (one word) and d/next2 (two
    /// words) helpers, which node 505's own d1/main dispatches to for exactly these two table rows.
    /// <see cref="CvmInstructionShape.WordLength"/> is 3 for this shape (tag word + two operand words).
    /// <see cref="TryDescribeSelfDecodingWord"/> needs a SECOND already-fetched word (beyond
    /// <see cref="FixedOpcodeWithTrailingWord"/>'s own single <c>nextWord</c>) to fully describe an
    /// instruction using this shape -- see that method's own <c>nextWord2</c> parameter, added
    /// alongside this encoding.
    /// </summary>
    FixedOpcodeWithTwoTrailingWords,

    /// <summary>
    /// ADDED 2026-10-02, for the "CVM_pipeline" bit-pattern table's own <c>cbr</c>, RENAMED the same day
    /// to <c>if</c> (see <see cref="IfMnemonic"/>'s own remarks -- this file's class-level remarks right
    /// after <see cref="UnlinkMnemonic"/>, table row "1010|....|cccc|xxxx| cbr {conditional long branch,
    /// relative}"). The instruction's FIRST word is a fixed <see cref="CvmInstructionShape.Tag"/> OR'd
    /// with TWO independently-packed embedded unsigned fields (like <see cref="EmbeddedUnsignedValuePair"/>'s
    /// own first word: a "cond" field in <see cref="CvmInstructionShape.ValueBitMask"/>/
    /// <see cref="CvmInstructionShape.ValueBitShift"/>, a register field in
    /// <see cref="CvmInstructionShape.SecondValueBitMask"/>/<see cref="CvmInstructionShape.SecondValueBitShift"/>)
    /// -- but UNLIKE <see cref="EmbeddedUnsignedValuePair"/>, a real trailing operand word follows: the
    /// branch's own signed relative offset, resolved exactly like a <see cref="TrailingWord"/> mnemonic's
    /// trailing operand (a literal or label both resolve the same way), needed because this is explicitly
    /// a "long" branch (<see cref="ShortBranchMnemonic"/>'s own row, by contrast, embeds its offset
    /// directly in the one word it has, with no trailing word at all -- that is what makes it "short").
    /// <see cref="CvmInstructionShape.WordLength"/> is 2 for this shape.
    ///
    /// CONFIRMED 2026-10-02 -- see <see cref="IfMnemonic"/>'s own remarks (this file's class-level
    /// remarks right after <see cref="UnlinkMnemonic"/>) for the full account: the "cond" field is a
    /// node-406-resolved reference (a computed-jump target into one of node 406's ten named condition
    /// routines), not a plain numeric condition code, per the table's own legend ("c: binary instruction
    /// address") -- resolving what was FLAGGED here as the open question. The register field's hard
    /// 4-bit/r0-r15 limit is also confirmed, not flagged. The branch's own polarity went through TWO
    /// confirmed answers the same day, the second superseding the first (see <see cref="IfMnemonic"/>'s
    /// own remarks, item (2), for the full back-and-forth): the FINAL, standing answer is that "if r0
    /// ==0 loop_label" branches to loop_label WHEN r0 IS 0 -- the intuitive reading -- achieved by
    /// Stefan's own change to node 406's underlying flag evaluation, not by anything this shape's bit
    /// layout encodes. The complete hand-typed operand syntax (how the branch target reaches this
    /// shape's own trailing word) is ALSO now confirmed -- "if &lt;reg&gt; &lt;cond&gt; &lt;target&gt;",
    /// three operands -- see <see cref="IfMnemonic"/>'s own remarks, item (4), and Ga144.Evb.Ide.Services.
    /// CvmAssemblyLanguage.EncodeIfInstruction's own remarks for the real encoder this unblocked.
    /// </summary>
    EmbeddedUnsignedValuePairWithTrailingWord,
  }

  /// <summary>
  /// The shape of one CVM instruction: a stable numeric <see cref="Id"/>, how many words it assembles
  /// to (its own opcode word included), and how its operand (if any) is encoded (see
  /// <see cref="CvmOperandEncoding"/>). <see cref="Tag"/> and <see cref="ValueBitMask"/> are only
  /// meaningful for <see cref="CvmOperandEncoding.EmbeddedSignedValue"/>/<see cref="CvmOperandEncoding.EmbeddedUnsignedValue"/>
  /// shapes -- every other encoding ignores them (default 0).
  /// </summary>
  public sealed record CvmInstructionShape(int Id, string Mnemonic, int WordLength, CvmOperandEncoding Encoding, int Tag = 0, int ValueBitMask = 0, int ValueBitShift = 0, int SecondValueBitMask = 0, int SecondValueBitShift = 0)
  {
    /// <summary>True for every encoding except <see cref="CvmOperandEncoding.None"/> and
    /// <see cref="CvmOperandEncoding.FixedOpcode"/> (ADDED 2026-09-30 -- see that case's own remarks) --
    /// whether the assembler requires exactly one operand argument for this mnemonic.</summary>
    public bool HasOperand => Encoding != CvmOperandEncoding.None && Encoding != CvmOperandEncoding.FixedOpcode;

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

    /// <summary>
    /// For an <see cref="CvmOperandEncoding.EmbeddedUnsignedValuePair"/> shape ONLY (node 306's six
    /// binary floating-point ops): which bits of the word hold the SECOND embedded operand (register
    /// <c>ggg</c>), distinct from <see cref="ValueBitMask"/>'s first operand (register <c>fff</c>). Zero
    /// (the default) for every other shape, which have no second operand at all.
    /// </summary>
    public int SecondValueBitMask { get; init; } = SecondValueBitMask;

    /// <summary>
    /// How far left the second operand is shifted before OR-ing it into <see cref="SecondValueBitMask"/>'s
    /// bits (and shifted back right when decoding). Zero (the default) for every other shape.
    /// </summary>
    public int SecondValueBitShift { get; init; } = SecondValueBitShift;
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
    // ---- 2026-09-30: NEW VIRTUAL MACHINE, full reset -----------------------------------------
    // Per Stefan directly: "there is a new virtual machine. all opcodes are invalid. except that
    // 'nop' has the opcode '0'." Every row below this point (all of CVM2's opcodes, down to the
    // very last one) is RETIRED as of this reset -- commented out in place, never deleted, exactly
    // this file's own long-standing "do not remove any opcodes" convention, so every Id below is
    // still never to be reused. This is a WHOLESALE reset, not a further reconciliation pass: this
    // new VM shares no opcode with CVM2 except the coincidence that 'nop' happens to be named the
    // same thing again -- nothing below should be read as "still valid, just unwired."
    //
    // Id 0 (nop) is the ONE survivor, but its own SHAPE changed too, not just its status: under
    // CVM2 it was a TAGGED mnemonic (CvmOperandEncoding.None, resolved only against a live node's
    // own compile, exactly like push/pop/ret/leave -- see this file's own class-level remarks
    // above). Stefan's own words this time -- "'nop' has the opcode '0'" -- describe a FIXED,
    // universally-known literal opcode, not "whatever address nop happens to land at in some future
    // node's compiled RAM": no live node, no linker, no relocation entry at all. That is a genuinely
    // new shape this table has never needed before, so a new encoding was added for it --
    // CvmOperandEncoding.FixedOpcode, see that enum case's own remarks -- rather than stretching
    // None (which has always meant "node-resolved" everywhere else in this file) to also mean
    // "already fully known." nop keeps Id 0 -- the same Id it has always had -- since this is a
    // reshaping of what nop IS, not a retirement-and-replacement the way every other entry in this
    // file has ever been renumbered/repointed.
    new(Id: 0, NopMnemonic, 1, CvmOperandEncoding.FixedOpcode, Tag: 0x0000),
    // ADDED 2026-09-30 (same day as the reset), the new VM's own scall/lcall -- see this file's own
    // class-level remarks on the new VM's "call" family, right after ConditionalBranchMnemonic, for the
    // full derivation and Stefan's exact wording. IDs 182/183 are the first ever assigned to the NEW VM
    // (every prior Id, 1-181, belongs to the now-fully-retired CVM2 opcode set below) -- append-only,
    // same as always. "call" itself has no row here at all: it is a pure assembler-level pseudo-mnemonic
    // that lowers to one of these two, exactly like "literal" lowers to lit/litr (see ShortCallMnemonic's
    // own remarks).
    new(Id: 182, ShortCallMnemonic, 1, CvmOperandEncoding.EmbeddedAddress, Tag: 0x0000, ValueBitMask: ShortCallAddressMask),
    // Id 183 (lcall, FixedOpcodeWithTrailingWord/LongCallTag) -- RETIRED 2026-10-02, per Stefan
    // directly: "ljmp and lcall must encode in a similar way like 'ret and their address also taken
    // from labels 'ljmp and 'lcall in node 507. they are now special opcodes." This SHAPE (a fixed,
    // self-describing 0xF000 tag word with no live node involved, followed by a full trailing
    // address word) is fully superseded by the new Id 195 row further below -- a different shape
    // (node-507-resolved, tag 0x2000, not a fixed self-describing 0xF000) but, after a same-day
    // correction (see Id 195's own remarks for the full story), the SAME CATEGORY as before:
    // CvmOperandEncoding.TrailingWord, still 2 words, still a real address operand -- "call" still
    // lowers to it with no loss of capability, just through a live node-507 compile now instead of a
    // fixed constant. Commented out rather than deleted, per this file's own "do not remove any
    // opcodes" convention -- Id 183 itself is never to be reused. LongCallTag (0xF000) is now
    // referenced by nothing live at all (see its own remarks) -- kept purely as this row's
    // historical record.
    // new(Id: 183, LongCallMnemonic, 2, CvmOperandEncoding.FixedOpcodeWithTrailingWord, Tag: LongCallTag),
    // ADDED 2026-09-30 (same day, third new-VM opcode): Stefan placed a label 'ret in the new VM's node
    // 506 (the bit-pattern-table/dispatch node -- see Cvm.Node506Program's own remarks) for the "ret"
    // opcode, spec row "1111|11ff|ffff|feee| implied" (e = 0, always; f = the 7-bit address of 'ret in
    // node 506). Shaped exactly like every other node-resolved, tagged, single-bare-opcode-word mnemonic
    // this file has ever had (CvmOperandEncoding.None, no operand, real numeric value depends entirely on
    // where 'ret lands in node 506's own compiled RAM) -- see this file's own class-level remarks on that
    // shape. Reuses the EXISTING RetMnemonic ("ret") constant declared at the very top of this file rather
    // than adding a new name: Stefan's own node source names this word 'ret, and his own naming rule
    // (mnemonic = tick name minus the tick) gives that same string -- pure naming coincidence with CVM2's
    // OLD node-507 "ret" (former Id 5, fully retired below along with every other CVM2 opcode), not a
    // continuation of it; the two share nothing else (different node, different tag, different era).
    //
    // FLAGGED: unlike scall/lcall and every prior None-shaped mnemonic in this file's history (which all
    // OR their resolved node address in unshifted, at bit 0), this spec's own address field "f" sits at
    // bits 9-3, not bits 6-0 -- the bottom 3 bits ("eee") are fixed at 0, "implied" per Stefan's own
    // wording, not a free field. The real opcode is therefore Tag(0xFC00, bits 15-10 all 1) | (resolvedAddress << 3),
    // not the plain "tag | resolvedAddress" every earlier None-shaped mnemonic used -- this needed a new,
    // dedicated address-shift lookup on the IDE side (see
    // Ga144.Evb.Ide.Services.CvmAssemblyLanguage.NodeResolvedAddressShiftByMnemonic) rather than a change
    // here: this project's own Instructions table still has no Tag/shift of its own for a None-shaped row
    // (never has -- see this file's own class-level remarks on why the real tag/node pairing lives entirely
    // on the IDE side), so nothing about that split changes for this entry.
    new(Id: 184, RetMnemonic, 1, CvmOperandEncoding.None),
    // ADDED 2026-10-01 (same node-506 bit-pattern-table/dispatch family as ret, just above): Stefan
    // placed labels 'link and 'unlink in the new VM's node 506, each tagged "like the opcode for
    // ret" -- i.e. the SAME spec row shape ("1111|11ff|ffff|feee| implied", e = 0 always, f = the
    // 7-bit address of the corresponding word in node 506) -- so link/unlink share ret's own tag
    // (0xFC00) and its own bits-9-3 address shift; only which word in node 506 each one resolves
    // against differs. Because that tag/shift pairing is no longer ret-specific, the IDE-side
    // constant/dictionary entries backing it were generalized accordingly rather than duplicated --
    // see Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own remarks on Node506RetTagBits (now
    // Node506BitPatternTableTagBits) and NodeResolvedAddressShiftByMnemonic.
    //
    // "link" (Id 185): per Stefan, "the offset for 'link' is in the word after the opcode... 'link'
    // is encoded in 2 words: the first is the opcode, the second is the offset, which is added to
    // the stack pointer." That is CvmOperandEncoding.TrailingWord -- a node-resolved, tagged opcode
    // word (word 1) immediately followed by one plain operand word (word 2, the literal offset) --
    // structurally identical to pushlit/gld/gst/litr's own shape, just resolved against node 506
    // instead. The offset itself is an ordinary trailing word (CvmAssembler's existing generic
    // TrailingWord path emits it, via EmitOperandWord, exactly as it does for every other
    // TrailingWord mnemonic -- no new assembler code needed); Stefan's own wording doesn't restrict
    // it to a narrower range or forbid a label/import operand, so none is imposed here.
    new(Id: 185, LinkMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // "unlink" (Id 186): per Stefan, "'unlink' has no parameter and is encode[d] in 1 word like
    // 'ret'." Same shape as ret exactly -- CvmOperandEncoding.None, no operand -- just a different
    // node-506 word to resolve against.
    new(Id: 186, UnlinkMnemonic, 1, CvmOperandEncoding.None),
    //
    // CORRECTED 2026-10-02, per the "CVM_pipeline" bit-pattern table Stefan pasted that day (see this
    // file's own class-level remarks right after UnlinkMnemonic): ret/link/unlink's shared tag/shift,
    // assumed above (and on the IDE side) to be 0xFC00 with the resolved node address shifted left by
    // 3, is SUPERSEDED. The table's own "special" row -- "0010|0000|00ww|wwww| special {ink, unlink,
    // ret}" -- gives a tag of 0x2000 (bits 15-12 = 0010, bits 11-6 fixed at 0) with an UNshifted 6-bit
    // "ww wwww" field at bits 5-0 (mask 0x003F), not a 7-bit field at bits 9-3. This table also
    // explicitly NAMES this row's own family after these three mnemonics ("special {ink, unlink,
    // ret}" -- "ink" almost certainly a transcription of "link"), independently confirming they belong
    // together, exactly as Stefan's own prior wording ("their opcode is like the opcode for 'ret'")
    // already said. Neither row 184/185/186's own Id, mnemonic, WordLength, nor Encoding changes at
    // all -- only the tag/shift pairing, which (per this file's own long-standing convention) has
    // never lived here for a None/TrailingWord-shaped row; see
    // Ga144.Evb.Ide.Services.CvmAssemblyLanguage.Node506BitPatternTableTagBits/
    // NodeResolvedAddressShiftByMnemonic's own remarks for the actual correction. The original comment
    // block above (just above Id 184) is left exactly as written, as the historical record of the
    // first, now-superseded assumption -- not rewritten -- per this file's own "do not rewrite
    // history" convention.
    //
    // ADDED 2026-10-02, Ids 187-194: the rest of the "CVM_pipeline" table's own low-level/short-form
    // row family (next16/next32/pop16/pop32/rjmp/rcall/cbr/sbr) -- see this file's own class-level
    // remarks right after UnlinkMnemonic for the table text, the derivation, and which parts are
    // cross-checked against node 505's live dispatch vs. inferred/flagged. The four "other" row
    // families below this table (implied/immediate/unary/binary, each split further into {16-bit,
    // 32-bit, 32-bit float} for the latter three) are deliberately NOT given Instructions rows here:
    // no concrete F18 mnemonic or node target is defined anywhere in the material Stefan pasted for
    // any of them, several of the table's own field columns for them are blank/unspecified (not even
    // "don't care" dots, just empty), and "implied" specifically has a real, non-zero node-identifier
    // field (bits 2-0) that every earlier None-shaped mnemonic in this file (ret included) has always
    // treated as fixed at 0 -- a genuinely new shape this file has no precedent for. Reserved tag
    // constants for all eight (so future rows can slot in without hunting for free bit ranges) are
    // declared just below this list, flagged the same way, with no Instructions entries of their own.
    //
    // next16 (Id 187): table row "0100|    |    |    | next16" -- bits 15-12 fixed "0100" (0x4000),
    // the rest of the word entirely blank/unspecified in the table (not even dots), with the actual
    // operand carried in ONE trailing word instead -- exactly CvmOperandEncoding.FixedOpcodeWithTrailingWord's
    // own shape (see LongCallMnemonic's own row, Id 183, for the precedent; unlike lcall, next16 needs
    // no node/linker involvement for ITS trailing operand either -- a plain literal/label). Cross-
    // checked against node 505's own live d1/main dispatch, which strips exactly the "0100" prefix
    // before falling to d/next1 -- node 506's own live d/next1 helper fetches exactly one trailing
    // word (via node 507's own m/next relay), confirming the one-trailing-word shape.
    new(Id: 187, Next16Mnemonic, 2, CvmOperandEncoding.FixedOpcodeWithTrailingWord, Tag: 0x4000),
    // next32 (Id 188): table row "0101|    |    |    | next32" -- bits 15-12 fixed "0101" (0x5000),
    // same blank remainder, but TWO trailing words (per its own "32" name, alongside next16's "16"
    // immediately above it in the table) -- CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords, added
    // alongside this row (see that enum case's own remarks). Cross-checked the same way as next16:
    // node 505's d1/main strips "0101" to fall to d/next2, and node 506's own live d/next2 helper
    // fetches two trailing words (via m/next called twice), confirming the two-trailing-word shape.
    new(Id: 188, Next32Mnemonic, 3, CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords, Tag: 0x5000),
    // pop16 (Id 189): table row "0110|    |    |    | pop16" -- bits 15-12 fixed "0110" (0x6000), and
    // UNLIKE next16/next32, no trailing word at all here -- "pop" (as opposed to "next", which reads a
    // literal operand FOLLOWING the opcode) discards data already on a stack, needing nothing more
    // than the fixed opcode word itself: CvmOperandEncoding.FixedOpcode, same shape as nop. Cross-
    // checked against node 505's own live d1/main, which strips "0110" to fall to d/pop1 -- node 506's
    // own live d/pop1 helper takes no trailing-word argument, confirming no operand is assembled.
    new(Id: 189, Pop16Mnemonic, 1, CvmOperandEncoding.FixedOpcode, Tag: 0x6000),
    // pop32 (Id 190): table row "0111|    |    |    | pop32" -- bits 15-12 fixed "0111" (0x7000), same
    // reasoning as pop16 (no trailing word, CvmOperandEncoding.FixedOpcode). Cross-checked against
    // node 505's own live d1/main falling to d/pop2 for the "0111" prefix; node 506's own live d/pop2
    // helper likewise takes no trailing-word argument.
    new(Id: 190, Pop32Mnemonic, 1, CvmOperandEncoding.FixedOpcode, Tag: 0x7000),
    // rjmp (Id 191): table row "1000|....|....|xxxx| rjmp {jump to address in register r}" -- bits
    // 15-12 fixed "1000" (0x8000), bits 11-4 unused/don't-care ("...."), bits 3-0 a 4-bit register
    // index ("x", per the table's own legend: "x: parameter1"). CvmOperandEncoding.EmbeddedUnsignedValue,
    // fully self-describing (no node/linker involvement at all -- the register index is a plain
    // literal operand, like node 511's own register-file ops). Cross-checked against node 505's own
    // live d1/main, which strips "1000" to fall to d/rjump; node 506's own live d/rjump/d/rreg helpers
    // corroborate a register-index operand (as opposed to an address), matching "jump to address in
    // register r" verbatim.
    new(Id: 191, RegisterJumpMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: 0x8000, ValueBitMask: 0x000F),
    // rcall (Id 192): table row "1001|....|....|xxxx| rcall {call to address in register r}" -- same
    // shape as rjmp, tag 0x9000 instead. Cross-checked against node 505's own live d1/main falling to
    // d/rcall for the "1001" prefix.
    new(Id: 192, RegisterCallMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: 0x9000, ValueBitMask: 0x000F),
    // if (Id 193) -- RENAMED 2026-10-02 from "cbr" to "if", per Stefan directly (see IfMnemonic's own
    // remarks, and this file's class-level remarks right after UnlinkMnemonic, for the full account of
    // what's now confirmed: node-406 resolution of the "cond" field, the branch's own final polarity, the
    // register field's hard r0-r15 limit, and the complete 3-operand hand-typed syntax). Same Id, same
    // bit layout as the original table row: "1010|....|cccc|xxxx| cbr {conditional long branch,
    // relative}" -- bits 15-12 fixed "1010" (0xA000), bits 11-8 unused/don't-care, bits 7-4 a 4-bit
    // "cond" field (mask 0x00F0, shift 4, node-406-resolved per IfMnemonic's own remarks), bits 3-0 a
    // 4-bit register field (mask 0x000F, shift 0, r0-r15 only), PLUS one trailing signed-offset word
    // (CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord -- see that enum case's own remarks
    // for the full derivation). Cross-checked (bit layout and prefix only) against node 505's own live
    // d1/main falling to d/conditional for the "1010" prefix. Now assemblable from hand-typed source --
    // see Ga144.Evb.Ide.Services.CvmAssemblyLanguage.EncodeIfInstruction's own remarks for the real
    // encoder, including the one remaining inferred-not-confirmed detail (the trailing word's own
    // relative-offset base point).
    new(Id: 193, IfMnemonic, 2, CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord, Tag: 0xA000, ValueBitMask: 0x00F0, ValueBitShift: 4, SecondValueBitMask: 0x000F),
    // sbr (Id 194): table row "1011|oooo|oooo|oooo| sbr {short branch, relative}" -- bits 15-12 fixed
    // "1011" (0xB000), bits 11-0 a 12-bit SIGNED relative offset ("o", per the table's own legend:
    // "o: signed offset") embedded directly in the one word -- no trailing word at all, which is
    // exactly what makes this branch "short" as opposed to cbr's own "long" one just above it in the
    // table. CvmOperandEncoding.EmbeddedSignedValue, the same shape CVM2's OLD br/cbr used to use (see
    // BranchTag's own remarks) -- fully self-describing, no node/linker involvement. Cross-checked
    // against node 505's own live d1/main falling to d/branch for the "1011" prefix.
    new(Id: 194, ShortBranchMnemonic, 1, CvmOperandEncoding.EmbeddedSignedValue, Tag: 0xB000, ValueBitMask: 0x0FFF),
    //
    // ADDED 2026-10-02, Ids 195/196 -- per Stefan directly (a direct statement, not derived from the
    // table text the way Ids 187-194 above were): "the address of 'ret, 'link and 'unlink label must
    // be taken from node 507. ljmp and lcall must encode in a similar way like 'ret and their address
    // also taken from labels 'ljmp and 'lcall in node 507. they are now special opcodes." Two things
    // follow from this:
    //
    // (1) ret/link/unlink (Ids 184-186, just above -- unchanged here) are CORRECTED to resolve
    // against node 507, not node 506 -- node 506 is still the table's own home (the "special" tag,
    // 0x2000, still comes from node 506's own bit-pattern-table comment block), but the actual
    // LABEL ADDRESSES these three resolve against live in node 507 instead. Per this file's own
    // standing convention, a None/TrailingWord-shaped row carries no Tag/node of its own here at all
    // -- that correction lives entirely on the IDE side; see
    // Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own NodeSymbolByMnemonic remarks for the actual
    // fix (RetMnemonic/LinkMnemonic/UnlinkMnemonic's own NodeCoordinate changed from
    // Node506Program.Coordinate to Node507Program.Coordinate).
    //
    // (2) lcall/ljmp JOIN this same "special" family (tag 0x2000) as two brand new rows. This
    // SUPERSEDES lcall's OLD shape (Id 183, just above, now retired).
    //
    // SHAPE CORRECTED the same day, within hours: Stefan's own first wording ("encode in a similar
    // way like 'ret") was initially taken to mean CvmOperandEncoding.None (no operand at all, exactly
    // like ret/unlink) -- WRONG, per Stefan's own direct follow-up once this file's first attempt was
    // shown to him: "the lcall and ljmp still have the address of the destination in the next word,
    // so they still have 1 argument. fix that." Confirmed against the live node 507 source he then
    // pasted:
    //   : 'lcall ( - )        : 'ljmp ( - )             : 'ret ( - )
    //     m/next                m/next a! ;               m/pop a! ;
    //   : m/call ( adr - )
    //     a m/push
    //   : m/jump ( adr - )
    //     a! ;
    // 'lcall and 'ljmp BOTH call m/next FIRST (node 507's own "fetch the next word from memory and
    // advance the instruction pointer" primitive -- the exact same primitive 'link already uses for
    // its own trailing offset word) before doing anything else with the fetched value (adr) -- 'ret,
    // by contrast, calls m/pop (pop the RETURN STACK, no memory fetch at all). So lcall/ljmp are
    // CvmOperandEncoding.TrailingWord (2 words -- a node-507-resolved tag word, then one trailing
    // operand word carrying the real far-call/far-jump target), shaped exactly like "link", NOT like
    // "ret"/"unlink" -- "like 'ret" described only the SHARED TAG FAMILY (the "special" row, 0x2000),
    // not literally ret's own own zero-operand shape. This also un-flags the "call" pseudo-mnemonic
    // gap this file's own first attempt opened (see Id 183's own retirement remarks, just above) --
    // "call" CAN still lower to lcall with a real address operand; see
    // Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own EncodeCallPseudoMnemonic remarks (and
    // Ga144.Cvm.Toolchain.CvmAssembler's own pass-2 "call" case remarks) for how that lowering now
    // resolves lcall's own tag through a live node-507 compile instead of a fixed, self-describing
    // constant the way the OLD, retired Id 183 shape used to.
    //
    // d/main's own "special" dispatch in node 506 (not yet live -- see this file's own class-level
    // remarks) independently corroborates the node-507 resolution for this whole family: node 505's
    // own live d1/main source, pasted alongside this correction, falls to "d/save" for the "001*"
    // prefix with the comment "// the encode address is from node 507" right there in Stefan's own
    // F18 source.
    //
    // ljmp (LongJumpMnemonic, "ljmp") reuses CVM2's own old mnemonic STRING (Id 74 and, briefly, a
    // node-407 entry further below -- both long retired) -- pure naming coincidence, same convention
    // as ret's own two unrelated "ret"s. Neither lcall nor ljmp has ever had a "new VM"/CVM_pipeline
    // row before this one; these are genuinely new Ids, not a repoint of an existing live row.
    new(Id: 195, LongCallMnemonic, 2, CvmOperandEncoding.TrailingWord),
    new(Id: 196, LongJumpMnemonic, 2, CvmOperandEncoding.TrailingWord),
    //
    // ADDED 2026-10-02, same day, Id 197: Stefan's own direct follow-up, after the lcall/ljmp
    // correction just above: "there is a new word 'push'. it is also a special word (node 507
    // 'push). it pushed the next word onto the stack." PushMnemonic ("push") is an EXISTING constant
    // (reused, not new -- CVM1's OLD "push", former Id 2, CvmOperandEncoding.None, retired below along
    // with every other CVM1 opcode; pure naming coincidence, same convention as ret's own two
    // unrelated "ret"s and ljmp's own two unrelated "ljmp"s).
    //
    // Confirmed against node 507's own live source Stefan pasted alongside this: 'push falls straight
    // into m/next (node 507's own "fetch the next word from memory, advance the instruction pointer"
    // primitive -- the exact same one 'link/'lcall/'ljmp already use for their own trailing word)
    // before falling into m/push (w - ), which sends an r/push command to the register file and then
    // writes the fetched word to the return-stack's own memory page via m/write:
    //   : 'push ( - )
    //     m/next
    //   : m/push ( w - )
    //     A[ r/push ]] lit m/rsend
    //     1 @b
    //   : m/write ( w p a - )
    //     A[ mem/wrup ]] lit m/usend
    //     !b !b !b ;
    // So 'push takes a real trailing operand word -- the literal value it pushes -- exactly the same
    // CvmOperandEncoding.TrailingWord shape as link/lcall/ljmp (a node-507-resolved tag word, then one
    // plain trailing word), joining them in the same "special" family (tag 0x2000, per the
    // "CVM_pipeline" table's own "special" row -- that row's own text, "special {ink, unlink, ret}",
    // was never a claim the family had only three members; it is simply the three Stefan happened to
    // name when he first introduced the row). Per this file's own standing convention, the tag/node
    // pairing for a None/TrailingWord-shaped row lives entirely on the IDE side, never here -- see
    // Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own NodeSymbolByMnemonic remarks for the actual
    // node-507/tag/shift wiring (and for the OLD CVM2-era "push" entry this supersedes, kept as a
    // comment per "do not remove any opcodes").
    new(Id: 197, PushMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // ---------------------------------------------------------------------------------------------
    // new(Id: 1, PushLitMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 2, PushMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 3, PopMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 4, CallMnemonic, 1, CvmOperandEncoding.EmbeddedAddress),
    // new(Id: 5, RetMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 6, BranchMnemonic, 1, CvmOperandEncoding.EmbeddedSignedValue, Tag: BranchTag, ValueBitMask: BranchOffsetBitMask),
    // new(Id: 7, ConditionalBranchMnemonic, 1, CvmOperandEncoding.EmbeddedSignedValue, Tag: ConditionalBranchTag, ValueBitMask: ConditionalBranchOffsetBitMask),
    // Id 8 (was SlitMnemonic, "slit") RETIRED 2026-09-09, and its own mnemonic/tag constants and decode
    // helper deleted outright later the same day -- see this file's own remarks above on the
    // 2026-09-09 CVM1-opcode purge. Never reuse Id 8.
    // new(Id: 9, UnsignedShiftLeftMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 10, SignedShiftRightMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 11, UnsignedShiftRightMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 12, AddMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 13, SubtractMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 14, AndMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 15, XorMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 16, OrMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 17, InvertMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 18, IncrementMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 19, DecrementMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 20, EnterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506EnterTag, ValueBitMask: Node506FrameValueBitMask),
    // Id 21 (was AdjustMnemonic "adjust") is retired -- DELETED OUTRIGHT 2026-09-10 per Stefan: "'adjust'
    // no longer exists. you can remove it." See AdjustMnemonic's own former remarks (now removed) and
    // this file's own class-level remarks above for the full history, including the real hardware run
    // that showed executing it corrupted the whole cluster's control flow. Never reuse Id 21.
    // new(Id: 22, StoreLocalMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506StoreLocalTag, ValueBitMask: Node506FrameValueBitMask),
    // new(Id: 23, StoreParameterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506StoreParameterTag, ValueBitMask: Node506FrameValueBitMask),
    // new(Id: 24, LoadLocalMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506LoadLocalTag, ValueBitMask: Node506FrameValueBitMask),
    // new(Id: 25, LoadParameterMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: Node506LoadParameterTag, ValueBitMask: Node506FrameValueBitMask),
    // Ids 26/27 (were LoadAddressOfLocalMnemonic "lal" / LoadAddressOfParameterMnemonic "lap") RETIRED
    // 2026-09-09, their mnemonic/tag constants later deleted outright too -- see this file's own remarks
    // above on the 2026-09-09 CVM1-opcode purge. Never reuse Ids 26/27.
    // new(Id: 28, LeaveMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 29, EqualMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 30, EqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 31, FalseMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 32, TrueMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 33, NotEqualMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 34, NotEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    // Id 35 (UnsignedGreaterThanMnemonic "ugt") -- RESOLVED 2026-09-09 (second pass): node 408's own
    // current source defines a real 'ugt word (via its c/u helper) -- see UnsignedGreaterThanMnemonic's
    // own remarks. No longer merely kept for CCodeGenerator's sake; now genuinely live.
    // new(Id: 35, UnsignedGreaterThanMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 36, GreaterThanMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 37, GreaterThanZeroMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 38, GreaterOrEqualMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 39, GreaterOrEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    // Id 40 (UnsignedLessOrEqualMnemonic "ule") -- RESOLVED 2026-09-09, same as Id 35 above: node 408
    // defines a real 'ule word.
    // new(Id: 40, UnsignedLessOrEqualMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 41, LessOrEqualMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 42, LessOrEqualToZeroMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 43, LessThanMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 44, LessThanZeroMnemonic, 1, CvmOperandEncoding.None),
    // Ids 45/46 (UnsignedLessThanMnemonic "ult" / UnsignedGreaterOrEqualMnemonic "uge") -- RESOLVED
    // 2026-09-09, same as Id 35/40 above: node 408 defines real 'ult/'uge words.
    // new(Id: 45, UnsignedLessThanMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 46, UnsignedGreaterOrEqualMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 47, MultiplyByTwoMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 48, UnsignedDivideByTwoMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 49, DivideByTwoMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 50, AbsoluteValueMnemonic, 1, CvmOperandEncoding.None),
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
    // new(Id: 55, BitCountMnemonic, 1, CvmOperandEncoding.None),
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
    // RETIRED 2026-09-27 (kept, per this file's own "do not remove any opcodes" convention): node 510's
    // wholesale redesign ("extended register access", replacing "extended arithmetic") no longer defines
    // an 'addc word anywhere -- AddWithCarryMnemonic ("addc", Id 57) has no live node behind it at all any
    // more, for the first time since it was repointed here from node 506's old register-d family
    // (2026-09-09) and then again to node 510 (also 2026-09-09). Id 57 is still never to be reused.
    // new(Id: 57, AddWithCarryMnemonic, 1, CvmOperandEncoding.None),
    // Ids 65-71 (were ExchangePortMnemonic "xpt" through StoreLowMnemonic "stlo") -- FLAGGED at the
    // time, NOT retired, same reason: five of these seven (xpt/ldhi/ldlo/sthi/stlo -- 'in'/'out' were
    // deliberately excluded from testing by Stefan's own choice, though never assembled anywhere else
    // either) were likewise still assembled by Cvm.CvmDebuggerDefaultProgram and were likewise confirmed
    // only against CVM1's old node 407. DELETED OUTRIGHT 2026-09-09 (same purge) once
    // CvmDebuggerDefaultProgram's own smoke test was rewritten to drop this whole block -- see this
    // file's own remarks above. Never reuse Ids 65-71.
    // new(Id: 72, HaltMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 73, LongCallMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 74, LongJumpMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 75, LoadGlobalMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 76, StoreGlobalMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 77, NegMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 78, LitMnemonic, 1, CvmOperandEncoding.EmbeddedSignedValue, Tag: LitTag, ValueBitMask: LitValueBitMask),
    // new(Id: 79, ParityMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 80, OddMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 81, NotMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 82, ReverseSubtractMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 83, ReverseShiftLeftMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 84, ReverseShiftRightMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 85, ReverseUnsignedShiftRightMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 86, AddConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 87, SubtractConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 88, ReverseSubtractConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 89, AndConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 90, XorConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 91, OrConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 92, ReverseShiftLeftConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 93, UnsignedShiftLeftConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 94, ReverseShiftRightConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 95, SignedShiftRightConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 96, ReverseUnsignedShiftRightConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 97, UnsignedShiftRightConstantMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // Id 98 (clbr) and Id 100 (cljmp) are retired -- removed 2026-09-06 per Stefan ("I remove clbr and
    // cljmp from node 407. remove it also from the CVM assembler and disassembler"), before either was
    // confirmed on real hardware. Never reuse either Id for a different instruction.
    // new(Id: 99, LongBranchMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // Ids 101-106 (were ldar/star/inca/deca/lda/sta, self-describing EmbeddedUnsignedValue) RETIRED
    // 2026-09-09 -- node 306's own current source no longer defines this family at all; their mnemonic
    // and tag constants were later deleted outright too, see this file's own remarks above on the
    // 2026-09-09 CVM1-opcode purge. Never reuse Ids 101-106.

    // Ids 107-110 (LoadRegisterFileMnemonic "rld" / StoreRegisterFileMnemonic "rst" /
    // PopRegisterFileMnemonic "rpop" / PushRegisterFileMnemonic "rpush") -- UN-RETIRED 2026-09-09 (see
    // LoadRegisterFileMnemonic's own remarks): workspace.yaml's own authoritative node 511 source
    // restores these as four separately-named, separately-addressed F18 words. NodeResolvedEmbeddedValue,
    // the same shape the address-register family and node 306's own 'fpop use elsewhere in this table --
    // a live compile of node 511 resolves which function is invoked, and a 5-bit register index is
    // embedded in the same opcode word (see
    // Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own Node511* field-layout wiring). Restored under
    // their ORIGINAL Ids, never renumbered.
    // REPOINTED 2026-09-27 from node 511's old 5-bit/5-bit layout to the new "extended register access"
    // node's 6-bit/3-bit one (ValueBitMask/ValueBitShift added so CvmAssembler's own offline register-
    // range validation and embedded-value shifting are correct for the new, wider, shifted field) -- see
    // LoadRegisterFileMnemonic's own remarks.
    // new(Id: 107, LoadRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: RegisterAccessRegisterFieldBitMask, ValueBitShift: RegisterAccessRegisterFieldShift),
    // new(Id: 108, StoreRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: RegisterAccessRegisterFieldBitMask, ValueBitShift: RegisterAccessRegisterFieldShift),
    // new(Id: 109, PopRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: RegisterAccessRegisterFieldBitMask, ValueBitShift: RegisterAccessRegisterFieldShift),
    // new(Id: 110, PushRegisterFileMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: RegisterAccessRegisterFieldBitMask, ValueBitShift: RegisterAccessRegisterFieldShift),

    // tjmp (node 507's own table-jump primitive, confirmed located 2026-09-09 -- see
    // TableJumpMnemonic's own remarks) and node 506's new f/fpush (2026-09-09, replacing the retired
    // lal/lap -- see FrameToRegisterMnemonic's own remarks). All three are tagged/node-resolved, exactly
    // like leave/halt above (CvmOperandEncoding.None, no Tag/ValueBitMask here -- resolved only against
    // a live compile via Ga144.Evb.Ide.Services.CvmAssemblyLanguage.NodeSymbolByMnemonic).
    // new(Id: 111, TableJumpMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 112, FrameToRegisterMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 113, PushFrameMnemonic, 1, CvmOperandEncoding.None),

    // jump/xs/xp, node 507's own last three tick-labeled opcodes, added 2026-09-09 by the opcode/
    // assembler-vs-node reconciliation audit -- see JumpMnemonic's own remarks. jump is shaped exactly
    // like pushlit/lcall/ljmp/ldg/stg (CvmOperandEncoding.TrailingWord, 2 words); xs/xp take no operand
    // at all, exactly like nop/push/pop/ret/halt/tjmp (CvmOperandEncoding.None, 1 word). All three are
    // tagged/node-resolved against node 507's own live compile, no Tag/ValueBitMask here.
    // new(Id: 114, JumpMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 115, ExchangeSMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 116, ExchangePMnemonic, 1, CvmOperandEncoding.None),

    // The address-register family's CURRENT ops (2026-09-09, second pass; RE-TASKED again 2026-09-11 to
    // a real register operand -- see ArithmeticStoreAddressRegisterMnemonic's own remarks; RENUMBERED
    // 2026-09-15 from physical node 306 to physical node 308, see Cvm.Node308Program's own remarks) --
    // tick-prefixed, node-resolved, with a 3-bit embedded register-index operand
    // (CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask = AddressRegisterRegisterFieldBitMask),
    // replacing the retired self-describing family above (Ids 101-106).
    // new(Id: 117, ArithmeticIncrementAddressRegisterMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: AddressRegisterRegisterFieldBitMask),
    // new(Id: 118, ArithmeticDecrementAddressRegisterMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: AddressRegisterRegisterFieldBitMask),
    // new(Id: 119, ArithmeticLoadAddressRegisterMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: AddressRegisterRegisterFieldBitMask),
    // new(Id: 120, ArithmeticStoreAddressRegisterMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: AddressRegisterRegisterFieldBitMask),
    // new(Id: 121, LoadAddressRegisterValueMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: AddressRegisterRegisterFieldBitMask),
    // new(Id: 122, StoreAddressRegisterValueMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: AddressRegisterRegisterFieldBitMask),

    // Ids 123-128 (were dpop/dpush/dinc/ddec/dadd/dor, node 308's OLD "VM 32 arithmetic" family) REMOVED
    // OUTRIGHT 2026-09-15 per Stefan's own direct instruction -- see the removal note above
    // FloatingPointRegisterFieldBitMask for the full history. Never reuse Ids 123-128.

    // Node 405's nine ops (2026-09-09) -- tagged/node-resolved, CvmOperandEncoding.None. See
    // ToggleCarryMnemonic's own remarks.
    // new(Id: 129, ToggleCarryMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 130, AddWithCarryFlagMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 131, LoadCarryMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 132, SetCarryMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 133, ClearCarryMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 134, StoreCarryMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 135, SubtractWithCarryMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 136, RotateLeftMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 137, RotateRightMnemonic, 1, CvmOperandEncoding.None),

    // Node 505's 'fx (2026-09-09) -- tagged/node-resolved, CvmOperandEncoding.None. See
    // FrameExchangeMnemonic's own remarks (including the deliberately-unwired 'f collision with node
    // 506's own FrameToRegisterMnemonic).
    // new(Id: 138, FrameExchangeMnemonic, 1, CvmOperandEncoding.None),

    // Node 510's five genuinely new ops (2026-09-09) -- tagged/node-resolved, CvmOperandEncoding.None.
    // 'addc itself REPOINTS the existing AddWithCarryMnemonic (Id 57) rather than adding a new Id -- see
    // ExtendedStoreMnemonic's own remarks for the (since-resolved) CvmDebuggerDefaultProgram implication.
    // RETIRED 2026-09-27 (kept, per this file's own "do not remove any opcodes" convention): node 510's
    // wholesale redesign ("extended register access", replacing "extended arithmetic") no longer defines
    // 'xst/'xld/'xmul2/'xdiv2/'xumul at all -- the entire double-word (32-bit) arithmetic role node 510
    // used to play is simply gone from the new source, with no successor anywhere in the mesh. Ids
    // 139-143 are still never to be reused.
    // new(Id: 139, ExtendedStoreMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 140, ExtendedLoadMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 141, ExtendedMultiplyByTwoMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 142, ExtendedDivideByTwoMnemonic, 1, CvmOperandEncoding.None),
    // new(Id: 143, ExtendedUnsignedMultiplyMnemonic, 1, CvmOperandEncoding.None),

    // Node 508's embedded-9-bit-offset global fetch/store (2026-09-10) -- see
    // LoadGlobalEmbeddedMnemonic/StoreGlobalEmbeddedMnemonic's own remarks for the bit derivation and
    // the relationship to gld/gst (Id 75/76) above. A tag-range overlap with AdjustTag was flagged
    // (accepted) when this was added, then mooted the same day once adjust itself was deleted outright
    // -- see those same remarks and this file's own class-level remarks on the 2026-09-10 removal.
    // new(Id: 144, LoadGlobalEmbeddedMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: LoadGlobalEmbeddedTag, ValueBitMask: GlobalEmbeddedOffsetBitMask),
    // new(Id: 145, StoreGlobalEmbeddedMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: StoreGlobalEmbeddedTag, ValueBitMask: GlobalEmbeddedOffsetBitMask),

    // arinc2/ardec2 (2026-09-15) -- see ArithmeticIncrementAddressRegisterByTwoMnemonic's own remarks
    // for "I gave up the 7th register for 2 new opcodes" and the flagged, unwired-at-the-F18-level
    // "leap"/"then" body shape. Wired here purely on the strength of the shared, already-confirmed
    // encoding shape (same tag/field layout as arinc/ardec).
    // new(Id: 146, ArithmeticIncrementAddressRegisterByTwoMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: AddressRegisterRegisterFieldBitMask),
    // new(Id: 147, ArithmeticDecrementAddressRegisterByTwoMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: AddressRegisterRegisterFieldBitMask),

    // Ids 148-159 (were the OLD 'fpop/'fpush/fadd/fsub/fmin/fmax/fmul/fdiv/fln2/filn2/fpi2/f2pi family,
    // 2026-09-15/16/17) RETIRED OUTRIGHT 2026-09-21 -- REWORKED per Stefan's own direct instruction ("i
    // want to to a rework of the FP engine. it does not function well in the current state.") into the
    // unified twelve-op family just below. Never reuse Ids 148-159.

    // Node 306/305's REWORKED, unified floating-point operation family (2026-09-21) -- see
    // FloatingPointAddMnemonic's own remarks for the full bit-layout derivation (straight from Stefan's
    // own header comment/table on the new node 306). All twelve are genuinely self-describing and none
    // need a live node compile to assemble or disassemble any more (UNLIKE the OLD scheme, which resolved
    // fpop/fpush/fconst against a live compile of node 306's own then-current source) -- but they are NOT
    // all the same shape: TEN (fadd/fsub/fmin/fmax/fmul/fdiv/fmove/fconst/fneg/fabs) are
    // CvmOperandEncoding.EmbeddedUnsignedValuePair (first register in ValueBitMask/fff, second in
    // SecondValueBitMask/ggg), while fpop/fpush are plain CvmOperandEncoding.EmbeddedUnsignedValue (fff
    // only) -- CORRECTED 2026-09-21, per Stefan directly ("fpush and fpop only have 1 parameter, the
    // other opcodes have 2 parameter"; ggg is simply unused/always zero for these two, per his own node
    // 306 header: "fpop a ; ggg is not used {only 1 argument}").
    // new(Id: 160, FloatingPointAddMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValuePair, Tag: FloatingPointAddTag, ValueBitMask: FloatingPointRegisterFieldBitMask, SecondValueBitMask: FloatingPointSecondOperandRegisterFieldBitMask, SecondValueBitShift: FloatingPointSecondOperandRegisterFieldShift),
    // new(Id: 161, FloatingPointSubtractMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValuePair, Tag: FloatingPointSubtractTag, ValueBitMask: FloatingPointRegisterFieldBitMask, SecondValueBitMask: FloatingPointSecondOperandRegisterFieldBitMask, SecondValueBitShift: FloatingPointSecondOperandRegisterFieldShift),
    // new(Id: 162, FloatingPointMinimumMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValuePair, Tag: FloatingPointMinimumTag, ValueBitMask: FloatingPointRegisterFieldBitMask, SecondValueBitMask: FloatingPointSecondOperandRegisterFieldBitMask, SecondValueBitShift: FloatingPointSecondOperandRegisterFieldShift),
    // new(Id: 163, FloatingPointMaximumMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValuePair, Tag: FloatingPointMaximumTag, ValueBitMask: FloatingPointRegisterFieldBitMask, SecondValueBitMask: FloatingPointSecondOperandRegisterFieldBitMask, SecondValueBitShift: FloatingPointSecondOperandRegisterFieldShift),
    // new(Id: 164, FloatingPointMultiplyMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValuePair, Tag: FloatingPointMultiplyTag, ValueBitMask: FloatingPointRegisterFieldBitMask, SecondValueBitMask: FloatingPointSecondOperandRegisterFieldBitMask, SecondValueBitShift: FloatingPointSecondOperandRegisterFieldShift),
    // new(Id: 165, FloatingPointDivideMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValuePair, Tag: FloatingPointDivideTag, ValueBitMask: FloatingPointRegisterFieldBitMask, SecondValueBitMask: FloatingPointSecondOperandRegisterFieldBitMask, SecondValueBitShift: FloatingPointSecondOperandRegisterFieldShift),
    // new(Id: 166, FloatingPointMoveMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValuePair, Tag: FloatingPointMoveTag, ValueBitMask: FloatingPointRegisterFieldBitMask, SecondValueBitMask: FloatingPointSecondOperandRegisterFieldBitMask, SecondValueBitShift: FloatingPointSecondOperandRegisterFieldShift),
    // new(Id: 167, FloatingPointConstantMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValuePair, Tag: FloatingPointConstantTag, ValueBitMask: FloatingPointRegisterFieldBitMask, SecondValueBitMask: FloatingPointSecondOperandRegisterFieldBitMask, SecondValueBitShift: FloatingPointSecondOperandRegisterFieldShift),
    // new(Id: 168, FloatingPointNegateMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValuePair, Tag: FloatingPointNegateTag, ValueBitMask: FloatingPointRegisterFieldBitMask, SecondValueBitMask: FloatingPointSecondOperandRegisterFieldBitMask, SecondValueBitShift: FloatingPointSecondOperandRegisterFieldShift),
    // new(Id: 169, FloatingPointAbsoluteMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValuePair, Tag: FloatingPointAbsoluteTag, ValueBitMask: FloatingPointRegisterFieldBitMask, SecondValueBitMask: FloatingPointSecondOperandRegisterFieldBitMask, SecondValueBitShift: FloatingPointSecondOperandRegisterFieldShift),
    // fpop/fpush: ONE argument each ("fpop f"/"fpush f"), not the "f g" pair every other op above takes --
    // CORRECTED 2026-09-21 per Stefan directly. Plain EmbeddedUnsignedValue (fff only, no
    // SecondValueBitMask/SecondValueBitShift): the assembler now requires exactly one operand for these
    // two, and the emitted word's ggg bits (5-3) are simply always zero, matching node 306's own header
    // ("fpop a ; ggg is not used {only 1 argument}").
    // new(Id: 170, FloatingPointPopMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: FloatingPointPopTag, ValueBitMask: FloatingPointRegisterFieldBitMask),
    // new(Id: 171, FloatingPointPushMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: FloatingPointPushTag, ValueBitMask: FloatingPointRegisterFieldBitMask),

    // Node 409's four bit-operation mnemonics (2026-09-27) -- see BitClearMnemonic's own remarks for the
    // full bit-layout derivation and the two flagged opens (the ooo-vs-oo field width, and the # 407
    // import vs. node 408's physical adjacency). bclr/bset/binv take one operand (EmbeddedUnsignedValue,
    // aaaa only); bcopy takes two (EmbeddedUnsignedValuePair, aaaa first/bbbb second) -- the same
    // one-operand/two-operand split precedent as node 306's fpop/fpush vs. its other ten floating-point
    // ops.
    // new(Id: 172, BitClearMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: BitClearTag, ValueBitMask: BitPositionFieldBitMask, ValueBitShift: BitPositionFieldShift),
    // new(Id: 173, BitSetMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: BitSetTag, ValueBitMask: BitPositionFieldBitMask, ValueBitShift: BitPositionFieldShift),
    // new(Id: 174, BitInvertMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValue, Tag: BitInvertTag, ValueBitMask: BitPositionFieldBitMask, ValueBitShift: BitPositionFieldShift),
    // new(Id: 175, BitCopyMnemonic, 1, CvmOperandEncoding.EmbeddedUnsignedValuePair, Tag: BitCopyTag, ValueBitMask: BitPositionFieldBitMask, ValueBitShift: BitPositionFieldShift, SecondValueBitMask: BitPositionSecondFieldBitMask, SecondValueBitShift: BitPositionSecondFieldShift),

    // Node 508's literal-load family, added 2026-09-27 -- see LitrMnemonic's own remarks. litr is a
    // plain single-TrailingWord mnemonic (dynamically resolved against node 508's own live compile,
    // exactly like gld/gst above); litm/lit2 are the first two mnemonics in this project needing the new
    // TwoTrailingWords shape (two operand words following the tag word, WordLength 3).
    // new(Id: 176, LitrMnemonic, 2, CvmOperandEncoding.TrailingWord),
    // new(Id: 177, LitmMnemonic, 3, CvmOperandEncoding.TwoTrailingWords),
    // new(Id: 178, Lit2Mnemonic, 3, CvmOperandEncoding.TwoTrailingWords),

    // The new "extended register access" node's three genuinely new, register-only ops (2026-09-27) --
    // see RegisterClearMnemonic's own remarks. "rlit" (op code 1) is deliberately NOT here -- see
    // RegisterSetMnemonic's own remarks for why.
    // new(Id: 179, RegisterClearMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: RegisterAccessRegisterFieldBitMask, ValueBitShift: RegisterAccessRegisterFieldShift),
    // new(Id: 180, RegisterPopPairMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: RegisterAccessRegisterFieldBitMask, ValueBitShift: RegisterAccessRegisterFieldShift),
    // new(Id: 181, RegisterPushPairMnemonic, 1, CvmOperandEncoding.NodeResolvedEmbeddedValue, ValueBitMask: RegisterAccessRegisterFieldBitMask, ValueBitShift: RegisterAccessRegisterFieldShift),
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
  ///
  /// <paramref name="wordLength"/> (ADDED 2026-09-30, alongside <see cref="CvmOperandEncoding.FixedOpcodeWithTrailingWord"/>)
  /// tells the caller how many words this decode actually consumed -- always 1, EXCEPT for a
  /// <see cref="CvmOperandEncoding.FixedOpcodeWithTrailingWord"/> match (<c>lcall</c>), which is 2 (its own
  /// tag word plus the trailing address word). Every earlier self-describing shape this method recognizes
  /// is exactly one word, so this parameter is new machinery introduced specifically for lcall, not a
  /// behavior change for anything that came before it.
  ///
  /// <paramref name="nextWord"/> (ADDED 2026-09-30, same reason) is this word's own already-fetched
  /// successor, needed to print a <see cref="CvmOperandEncoding.FixedOpcodeWithTrailingWord"/> shape's
  /// real operand (lcall's own target address, or next16's own literal -- see <paramref name="nextWord2"/>
  /// for the cbr/next32 cases needing a further word still) -- this method decodes one word's own bit
  /// pattern in isolation and has no memory/SRAM access of its own, so a caller walking a real program
  /// (see the IDE's own Ga144.Evb.Ide.Services.CvmAssemblyLanguage.DisassemblePage0) must read that next
  /// word itself and pass it in. Pass null (the default) when there is no next word to give (e.g. lcall's
  /// own tag word happens to be the very last word in a truncated range) -- the mnemonic alone is still
  /// returned, just without an operand.
  ///
  /// <paramref name="nextWord2"/> (ADDED 2026-10-02, alongside <see cref="CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords"/>
  /// and <see cref="CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord"/> -- next32/cbr) is the
  /// word AFTER <paramref name="nextWord"/>, needed only for a shape using one of those two encodings
  /// (next32's own second 16 bits of its 32-bit literal; a second trailing word, were one ever added, for
  /// cbr -- today cbr itself only consumes <paramref name="nextWord"/>, so this parameter is read by the
  /// next32 loop alone for now). Same null-means-"not available" convention as <paramref name="nextWord"/>;
  /// every pre-existing call site (today, exactly one real one outside this file --
  /// Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own disassembler) needs no change at all, since this
  /// parameter defaults to null and nothing before next32/cbr ever reads it.
  /// </summary>
  public static string? TryDescribeSelfDecodingWord(int word, out int wordLength, int? wordAddress = null, int? nextWord = null, int? nextWord2 = null)
  {
    wordLength = 1;

    // ADDED 2026-09-30, checked FIRST, ahead of every hardcoded check below: the new VM's own
    // CvmOperandEncoding.FixedOpcode shapes (today, just nop, word 0x0000 -- see that enum case's own
    // remarks and Instructions' own remarks at the top of its list). An exact word match against each
    // live FixedOpcode shape's own Tag, matched generically the same way the EmbeddedUnsignedValue loop
    // further down already is, so a future FixedOpcode mnemonic needs no new check here. Checked first
    // specifically so nop's own 0x0000 can never be shadowed by call's old "word <= CallAddressMask"
    // range check just below (0 satisfies that range too) -- moot today since that check is itself
    // commented out as part of this same reset, but kept defensively correct regardless. Also checked
    // ahead of the new (2026-09-30) generalized EmbeddedAddress loop further down, for the SAME reason:
    // scall's own address 0 (word 0x0000) is bit-for-bit identical to nop's own fixed word -- see
    // ShortCallMnemonic's own remarks on this exact, flagged-not-resolved overlap.
    foreach (CvmInstructionShape fixedOpcodeShape in Instructions)
    {
      if (fixedOpcodeShape.Encoding == CvmOperandEncoding.FixedOpcode && word == fixedOpcodeShape.Tag)
      {
        return fixedOpcodeShape.Mnemonic;
      }
    }

    // ADDED 2026-09-30, for the new VM's own lcall (CvmOperandEncoding.FixedOpcodeWithTrailingWord -- see
    // that enum case's own remarks). An exact word match against each live FixedOpcodeWithTrailingWord
    // shape's own Tag, the same generic-loop-over-Instructions pattern the FixedOpcode loop just above
    // already uses -- checked right after it (grouping every EXACT-tag self-describing shape together)
    // and well before the generalized, RANGE-based EmbeddedAddress loop below: lcall's own tag, 0xF000,
    // does not overlap scall's own 0x0000-0x3FFF range at all, so this ordering doesn't change any actual
    // decode outcome today, it just reads more clearly grouped this way.
    foreach (CvmInstructionShape fixedTagWithTrailingWordShape in Instructions)
    {
      if (fixedTagWithTrailingWordShape.Encoding != CvmOperandEncoding.FixedOpcodeWithTrailingWord || word != fixedTagWithTrailingWordShape.Tag)
      {
        continue;
      }

      wordLength = 2;
      return nextWord is int lcallTarget
          ? $"{fixedTagWithTrailingWordShape.Mnemonic} {FormatOperand(lcallTarget)}"
          : fixedTagWithTrailingWordShape.Mnemonic;
    }

    // ADDED 2026-10-02, for the "CVM_pipeline" table's own next32 (CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords
    // -- see that enum case's own remarks). Same exact-tag loop pattern as the
    // FixedOpcodeWithTrailingWord loop just above (next16/lcall), checked right after it -- grouping
    // every EXACT-tag self-describing shape together, still well before the generalized,
    // RANGE-based EmbeddedAddress loop below -- except TWO already-fetched words (nextWord, then
    // nextWord2) are needed to print the full 32-bit literal, and either, both, or neither may be
    // available to the caller; this prints as much as it has rather than requiring both.
    foreach (CvmInstructionShape fixedTagWithTwoTrailingWordsShape in Instructions)
    {
      if (fixedTagWithTwoTrailingWordsShape.Encoding != CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords || word != fixedTagWithTwoTrailingWordsShape.Tag)
      {
        continue;
      }

      wordLength = 3;
      if (nextWord is int next32High && nextWord2 is int next32Low)
      {
        return $"{fixedTagWithTwoTrailingWordsShape.Mnemonic} {FormatOperand(next32High)} {FormatOperand(next32Low)}";
      }

      return nextWord is int next32PartialOperand
          ? $"{fixedTagWithTwoTrailingWordsShape.Mnemonic} {FormatOperand(next32PartialOperand)} ?"
          : fixedTagWithTwoTrailingWordsShape.Mnemonic;
    }

    // ADDED 2026-09-30, for the new VM's own scall: CVM2's OLD call used to be a single hardcoded
    // "word <= CallAddressMask" check (see the commented-out block just below) -- GENERALIZED here into a
    // per-shape loop over every live CvmOperandEncoding.EmbeddedAddress shape, the same pattern the
    // EmbeddedUnsignedValue loop further down already uses, so a shape's own Tag/ValueBitMask (not a
    // single hardcoded constant) decides what it matches -- see EmbeddedAddress's own remarks for the
    // full generalization. Checked after the two exact-tag loops above (FixedOpcode/nop,
    // FixedOpcodeWithTrailingWord/lcall) so neither can ever be shadowed by scall's own wide 0x0000-0x3FFF
    // range -- specifically nop's own 0x0000, which is bit-for-bit identical to "scall 0x0000" (see
    // ShortCallMnemonic's own remarks on this exact, flagged-not-resolved overlap).
    foreach (CvmInstructionShape embeddedAddressShape in Instructions)
    {
      if (embeddedAddressShape.Encoding != CvmOperandEncoding.EmbeddedAddress)
      {
        continue;
      }

      int embeddedAddressTagMask = ~embeddedAddressShape.ValueBitMask & 0xFFFF;
      if ((word & embeddedAddressTagMask) == embeddedAddressShape.Tag)
      {
        return $"{embeddedAddressShape.Mnemonic} {FormatOperand(word & embeddedAddressShape.ValueBitMask)}";
      }
    }

    // call/br/cbr/lit -- RETIRED 2026-09-30 along with every other CVM2 opcode (see Instructions' own
    // remarks at the top of its list: "there is a new virtual machine. all opcodes are invalid. except
    // that 'nop' has the opcode '0'."). Commented out rather than deleted, same "do not remove any
    // opcodes" convention as everywhere else in this file -- unlike every retirement before this one,
    // these four hardcoded checks do NOT consult Instructions' own retirement state at all (they always
    // fired directly off CallAddressMask/BranchTag/ConditionalBranchTag/LitTag regardless of whether
    // call/br/cbr/lit's own Instructions rows were still present), so leaving them active would have
    // kept describing words as "call"/"br"/"cbr"/"lit" even though none of those mnemonics exist in the
    // new VM any longer -- silently contradicting "all opcodes are invalid except nop." Commented out
    // together with their own Instructions rows for that reason.
    // if (word <= CallAddressMask)
    // {
    //   return $"{CallMnemonic} {FormatOperand(word)}";
    // }
    //
    // if ((word & BranchTagMask) == BranchTag)
    // {
    //   return $"{BranchMnemonic} {FormatOperand(DecodeBranchOffset(word))}{DescribeBranchTarget(wordAddress, DecodeBranchOffset(word))}";
    // }
    //
    // // cbr, CONFIRMED 2026-09-09 (replacing the old, unconfirmed "ifbr" placeholder guess -- see
    // // ConditionalBranchTag's own remarks for Stefan's exact bit pattern). Checked here, right after br
    // // and well before the generic EmbeddedUnsignedValue loop below. Originally this ordering mattered
    // // because cbr's tag (0xAC00, mask 0xFC00) briefly, fully contained the unconfirmed, orphaned lal/lap
    // // tags (0xAE00/0xAF00) that loop would otherwise have matched -- lal/lap were retired outright the
    // // same day (see ConditionalBranchTag's own remarks), so that collision no longer exists at all, but
    // // cbr is kept here rather than moved back into the generic loop since a self-describing branch shape
    // // reads more clearly grouped with br above it.
    // if ((word & ConditionalBranchTagMask) == ConditionalBranchTag)
    // {
    //   return $"{ConditionalBranchMnemonic} {FormatOperand(DecodeConditionalBranchOffset(word))}{DescribeBranchTarget(wordAddress, DecodeConditionalBranchOffset(word))}";
    // }
    //
    // if ((word & LitTagMask) == LitTag)
    // {
    //   return $"{LitMnemonic} {FormatOperand(DecodeLitValue(word))}";
    // }

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

    // Node 306/305's twelve unified floating-point ops (EmbeddedUnsignedValuePair, 2026-09-16, widened
    // from six to twelve in the 2026-09-21 rework) -- the same generic per-shape matching as the
    // EmbeddedUnsignedValue loop just above, except the tag mask must exclude BOTH embedded fields (fff
    // and ggg), and both are rendered, first operand (also the result register) before second, matching
    // Stefan's own "mnemonic f g" assembler syntax. The tag mask is derived from each shape's own
    // ValueBitMask/SecondValueBitMask, not hardcoded, so the wider 4-bit operation field the rework
    // introduced needs no change here.
    foreach (CvmInstructionShape shape in Instructions)
    {
      if (shape.Encoding != CvmOperandEncoding.EmbeddedUnsignedValuePair)
      {
        continue;
      }

      int tagMask = ~(shape.ValueBitMask | shape.SecondValueBitMask) & 0xFFFF;
      if ((word & tagMask) == shape.Tag)
      {
        int first = (word & shape.ValueBitMask) >> shape.ValueBitShift;
        int second = (word & shape.SecondValueBitMask) >> shape.SecondValueBitShift;
        return $"{shape.Mnemonic} {FormatOperand(first)} {FormatOperand(second)}";
      }
    }

    // ADDED 2026-10-02, for the "CVM_pipeline" table's own cbr, RENAMED the same day to if
    // (CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord -- see that enum case's own
    // remarks, and IfMnemonic's own remarks for what's now confirmed vs. still open). Same
    // tag-mask-excludes-both-fields matching as the EmbeddedUnsignedValuePair loop just above, except a
    // trailing offset word follows the tag word -- printed if the caller supplied one via nextWord,
    // omitted otherwise, the same "print what's available" convention FixedOpcodeWithTrailingWord's own
    // loop (lcall/next16) already uses. Operand order printed here (register, then cond) matches
    // Stefan's own confirmed "if <reg> <cond>" syntax -- NOT the cond/register order this shape's own
    // ValueBitMask/SecondValueBitMask happen to be declared in. "cond" is still printed as a raw 0-15
    // number, not yet resolved back to one of node 406's ten named conditions (==0/!=0/.../even/odd) --
    // that reverse lookup is part of the still-open work IfMnemonic's own remarks describe, not
    // implemented here.
    foreach (CvmInstructionShape shape in Instructions)
    {
      if (shape.Encoding != CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord)
      {
        continue;
      }

      int tagMask = ~(shape.ValueBitMask | shape.SecondValueBitMask) & 0xFFFF;
      if ((word & tagMask) == shape.Tag)
      {
        wordLength = 2;
        int cond = (word & shape.ValueBitMask) >> shape.ValueBitShift;
        int register = (word & shape.SecondValueBitMask) >> shape.SecondValueBitShift;
        return nextWord is int ifOffset
            ? $"{shape.Mnemonic} {FormatOperand(register)} {FormatOperand(cond)} {FormatOperand(ifOffset)}"
            : $"{shape.Mnemonic} {FormatOperand(register)} {FormatOperand(cond)}";
      }
    }

    return null;
  }
}