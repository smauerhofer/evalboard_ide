using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// The canonical, single-source-of-truth list of node coordinates that make up CVM2's own interpreter
/// mesh for a pure-software compile -- no serial port, no connected chip. Moved here 2026-09-08 out of
/// <c>CvmDebuggerViewModel</c>'s own former private <c>StandaloneCvmNodeCoordinates</c> field, so this
/// one list backs BOTH the CVM Debugger's standalone Assemble/disassemble path
/// (<c>CvmDebuggerViewModel.CompileStandaloneCvmNodes</c>) and the newer build-time
/// <see cref="CvmPrimitiveTableExporter"/> -- Stefan's own instruction for this area ("the exact
/// instruction set is still a moving target -- use what is available now in CVM2, and make changes when
/// new instructions become available") means a node WILL be added, renamed, or repointed again; before
/// this move, doing that meant remembering to touch two separate private lists in two separate view
/// models, with nothing to catch a missed one except a confusing "undefined opcode" symptom turning up
/// in only one of the two tools. Now there is exactly one list to update.
///
/// CVM2 (2026-09-01): trimmed to just the CPU node. CVM1's own five nodes originally listed here
/// (607, 507, 606, 506, 407) are ALL orphaned now -- none of them are part of CVM2's actual mesh
/// (708/707/607/507, "the nodes from CVM1 ... will not be used in CVM2"; note 507's OWN CVM1-era
/// mnemonics are orphaned too, even though the coordinate 507 itself is very much back in active use
/// as CVM2's real CPU -- see CvmAssemblyLanguage's own remarks), and
/// CvmAssemblyLanguage.NodeSymbolByMnemonic's own entries for their mnemonics (the ALU ops, leave,
/// node 506's ops, node 407's OLD register-w/port ops) stay in the table per "don't remove any
/// opcodes" but simply never resolve once their node isn't compiled here -- exactly the same
/// graceful-omission behavior CvmAssemblyLanguage already has for a node missing from compiledRam
/// altogether, so dropping them from this list changes nothing about whether those mnemonics still
/// exist, only whether a pure-software compile wastes a compile on nodes CVM2 doesn't use. This also
/// fixes a real bug: compiling all six unconditionally meant ANY one of them failing to compile in
/// Stefan's own live project would abort Assemble entirely with no obvious connection to what was
/// actually typed. See CvmDebuggerViewModel.CompileStandaloneCvmNodes' own remarks for the
/// belt-and-braces fix to that same failure mode (continue past one bad node rather than aborting on it) --
/// <see cref="CvmPrimitiveTableExporter"/> mirrors that same graceful-omission behavior.
///
/// Node 407 added back 2026-09-02: NOT a revival of the orphaned CVM1 node -- CVM2 reuses the same
/// coordinate for an unrelated long-call/long-jump helper (lcall/ljmp), reached from node 507's own
/// m/main dispatch. Needed here for lcall/ljmp to resolve at all in this standalone path -- otherwise
/// CvmAssemblyLanguage's own "undefined opcode -> nop" substitution would silently turn every lcall/
/// ljmp into a nop, exactly as it already does for a mnemonic whose node is missing from compiledRam
/// for any other reason. As with node 507, this compiles from THIS CHIP'S OWN LIVE PROJECT DATA for
/// coordinate 407, not from Cvm.Node407Program's reference source -- that reference copy has no effect
/// until it's also saved into the live project via the Node Editor.
///
/// Node 506 added back 2026-09-04, same reasoning: CVM2 reuses the coordinate for the stack-frame node
/// (enter/leave/...), reached from node 507's own m/main dispatch via its RIGHT port. Only 'leave (a
/// TAGGED mnemonic, resolved via NodeSymbolByMnemonic against a live compile) actually needs this --
/// enter is self-describing (CvmInstructionSet.Node506EnterTag) and would resolve with or without node
/// 506 being compiled here -- but including it costs nothing and keeps 'leave from silently degrading
/// to the same "undefined opcode -> nop" substitution 407 hit before it was added. Same live-project-
/// data caveat as node 407: Cvm.Node506Program's reference source has no effect here until it is also
/// saved into node 506's own Node Editor tab for this chip.
///
/// Node 508 and node 509 added 2026-09-05, plugging a real gap this list had missed until now:
/// 'gld/'gst (node 508, renamed 2026-09-09 from 'ldg'/'stg) and
/// 'inv/'inc/'dec/'neg/'abs/'mul2/'div2/'udiv2/'bitcnt (node 509) are ALL
/// TAGGED mnemonics, so without their own node compiled here they silently degraded to the same
/// "undefined opcode -> nop" substitution 407/506 would have hit before THEY were added -- in
/// practice this showed up as a disassembled word (e.g. node 509's own 0xB026, 'inc) rendering with
/// no mnemonic at all in the memory inspector's no-session view, even though the exact same word
/// disassembled correctly during a live hardware session (Ga144CvmHardwareInstaller's own
/// compiledRam always covers every node in CvmBootStreamBuilder.BuildLoadOrder, 508/509 included).
/// 508 is listed first since 509 imports 508's own exports ('# 508 import') -- CompileNode resolves
/// that import itself from the chip's own live node graph regardless of this list's order, but the
/// order here is kept parent-before-child for readability, matching CvmBootStreamBuilder's own
/// "compile order" (507, 407, 506, 508, 509). Same live-project-data caveat as 407/506: each
/// NodeXxxProgram.Source reference copy has no effect here until it is also saved into that node's
/// own Node Editor tab for this chip.
///
/// Node 406 added 2026-09-05, the SAME missed-gap bug as 508/509 above, reported directly by Stefan:
/// assembling "addi 0x0a" against a live session (whose compiledRam already covered node 406 via
/// CvmBootStreamBuilder.BuildLoadOrder) correctly emitted 0xE424/0x000A, but the memory inspector's
/// own no-session disassembly showed no mnemonic at all for 0xE424 -- the same "opcode's own node
/// isn't in THIS list" gap 508/509 hit before they were added here, not a CvmAssemblyLanguage/
/// CvmInstructionSet bug (their round-trip already verified clean against a compile that DOES
/// include node 406 -- see CvmInstructionSet.AddConstantMnemonic's own remarks). Listed last since it
/// imports node 407's own exports ('# 407 import'), same "parent before child, CompileNode resolves
/// the import regardless of list order" note as 508/509 above.
///
/// Node 408 added 2026-09-06, pre-emptively (before the same gap could bite): a comparison node,
/// node 407's OWN second leaf, hanging off its previously-unanswered LEFT-port branch (see
/// Cvm.Node407Program's/Cvm.Node408Program's own remarks) exactly the way 406 hangs off 407's RIGHT
/// port -- same "imports node 407, must be in this list or its own tagged mnemonics silently degrade"
/// reasoning as node 406 just above, so added alongside it rather than waiting for a bug report.
///
/// Nodes 306, 307, 308, 405, 505, 510, 511 added 2026-09-09 as part of the opcode/assembler-vs-node
/// reconciliation audit against Stefan's own <c>workspace.yaml</c> project export -- the SAME missed-
/// gap bug as 406/408/508/509 above, just for seven nodes at once: every one of these defines its own
/// TAGGED mnemonics (306's lda/sta/arinc/ardec/arld/arst, 308's dpop/dpush/dinc/ddec/dadd/dor, 405's
/// tgc/adc/ldc/sec/clc/stc/sbc/rol/ror, 505's fx, 510's addc/xst/xld/xmul2/xdiv2/xumul, 511's
/// rld/rst/rpop/rpush) that would silently degrade to "undefined opcode -> nop" without their own node
/// compiled here, exactly like every node added above it. Order is parent-before-child, following each
/// node's own "# N import" directive: 505 imports 506; 510 imports 509; 511 imports 510; 406 imports
/// 407 (unchanged, listed above); 405 imports 406; 307 imports 407; 308 imports 307; 306 imports 307.
/// CompileNode resolves each import from the chip's own live node graph regardless of this list's
/// order, but the order here is kept parent-before-child for readability, same as every prior addition.
/// </summary>
public static class CvmNodeMesh
{
  public static readonly IReadOnlyList<int> StandaloneCoordinates =
  [
    CvmMemoryProtocol.NopSourceNodeCoordinate, // 507, CVM2's entire CPU (corrected 2026-09-01 from 508).
    Node407Program.Coordinate, // 407, CVM2's long-call/long-jump helper (added 2026-09-02).
    Node506Program.Coordinate, // 506, CVM2's stack-frame node (added 2026-09-04).
    Node505Program.Coordinate, // 505, CVM2's frame2/more-frame-operations node (added 2026-09-09).
    Node508Program.Coordinate, // 508, CVM2's gld/gst node (added 2026-09-05, renamed 2026-09-09).
    Node509Program.Coordinate, // 509, CVM2's unary-arithmetic node (added 2026-09-05).
    Node510Program.Coordinate, // 510, CVM2's extended-arithmetic (double-register) node (added 2026-09-09).
    Node511Program.Coordinate, // 511, CVM2's 32-register register-file node (added 2026-09-09).
    Node406Program.Coordinate, // 406, CVM2's binary-arithmetic node (added 2026-09-05).
    Node405Program.Coordinate, // 405, CVM2's multiword-arithmetic (carry-flag) node (added 2026-09-09).
    Node408Program.Coordinate, // 408, CVM2's comparison node (added 2026-09-06).
    Node307Program.Coordinate, // 307, CVM2's "VM ternary main" relay node (added 2026-09-09).
    Node308Program.Coordinate, // 308, CVM2's 4x 32-bit "VM 32 arithmetic" register node (added 2026-09-09).
    Node306Program.Coordinate, // 306, CVM2's 4x 32-bit address-register node (added 2026-09-09).
  ];
}