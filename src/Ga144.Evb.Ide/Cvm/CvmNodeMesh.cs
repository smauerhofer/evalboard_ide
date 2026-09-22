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
///
/// <b>RENUMBERED 2026-09-15: node 306 and node 308 have swapped roles.</b> The "306's lda/sta/arinc/
/// ardec/arld/arst" and "308's dpop/dpush/dinc/ddec/dadd/dor" attributions two paragraphs up describe
/// the 2026-09-09 state and are no longer accurate: the address-register mnemonics (plus two new ones,
/// arinc2/ardec2) now live on node 308, and node 306 now names an unrelated floating-point register
/// node with one wired mnemonic of its own (<c>fpop</c>) -- see Node308Program's and Node306Program's
/// own remarks. <b>The old dpop/dpush/dinc/ddec/dadd/dor family was REMOVED OUTRIGHT the same day</b>,
/// per Stefan's own direct instruction ("remove the old dpop/dpush/dinc/ddec/dadd/dor family
/// completely") -- see CvmInstructionSet's own removal note above FloatingPointFunctionFieldBitMask;
/// both coordinates below (306 and 308) are unchanged by this swap, only which class/role each one
/// names.
///
/// <b>Nodes 301-305, 401-404, 501-504, 601-604 added 2026-09-16: the new 17-node, 32-bit
/// floating-point PIPELINE Stefan pasted node-by-node this same session.</b> This is an entirely
/// separate mesh branch from the CVM instruction-dispatch tree above -- these 17 nodes never appear in
/// any "# N import" directive belonging to 507/407/307/306/etc., and none of THEIR OWN "# N import"
/// directives names any node already in this list. Listed here in "imported node before its importer"
/// order within each control chain (401 before 402 before 403, matching 403's own "# 402 import" and
/// 402's own "# 401 import"; same pattern for 501/502/503 and 601/602/603), matching this list's own
/// established convention elsewhere -- see <see cref="CvmBootStreamBuilder.BuildDescriptors"/>'s own
/// compile steps for these seventeen. This COMPILE-order fact (which node imports which) is separate
/// from, and unaffected by, the physical relay topology described next.
///
/// <b>The pipeline's ENTIRE internal BOOT-STREAM relay topology -- the physical port-adjacency chain the
/// boot loader walks to deposit each node's program -- is now CONFIRMED, 2026-09-16, per Stefan directly,
/// verbatim:</b>
/// <code>
///   306-&gt;305-&gt;304-&gt;303-&gt;302-&gt;301
///   304-&gt;404-&gt;403-&gt;402-&gt;401
///   404-&gt;504-&gt;503-&gt;502-&gt;501
///   504-&gt;604-&gt;603-&gt;602-&gt;601
/// </code>
/// 304 relays to BOTH 303 (heading a simple 303-&gt;302-&gt;301 tail) AND 404; 404 relays to both 403
/// (heading a simple 403-&gt;402-&gt;401 tail) AND 504; 504 relays to both 503 (heading a simple
/// 503-&gt;502-&gt;501 tail) AND 604, which itself relays on to a simple 604-&gt;603-&gt;602-&gt;601 tail.
/// Node 306 (the floating-point register node, see <see cref="Node306Program"/>) is confirmed as the
/// relay feeding this whole pipeline's own entry point (node 305) into the rest of the mesh. All of this
/// has been added to <see cref="CvmBootStreamBuilder.BuildLoadOrder"/> -- see that method's own remarks
/// for the full post-order (leaves-first) derivation.
///
/// <b>IMPORTANT, per Stefan directly (2026-09-16), on what this topology does NOT tell us:</b> "the order
/// in which nodes interact is independent of the order they are loaded in the boot stream, because once a
/// node is loaded and started it is waiting for a stimulus, but it receives this stimulus only after all
/// nodes have been booted. so the boot stream need not reflect the processing stream." The confirmed
/// arrows above are BOOT-TIME port-relay adjacency only -- which physical port each node's boot frames
/// travel through to reach it -- and do NOT by themselves establish the pipeline's actual RUNTIME data-
/// flow/processing order (which node computes first, which sends its result to which next). The earlier,
/// separate, still-speculative per-node "stage 1 unpack... stage 8 round" narrative (inferred from each
/// node's own header, once phrased here as "fanning out at 303 into three parallel control chains") was a
/// claim about PROCESSING order, a genuinely different fact from this BOOT topology -- it is neither
/// confirmed nor refuted by the arrows above, and is not restated here to avoid re-conflating the two.
/// </summary>
public static class CvmNodeMesh
{
  /// <summary>
  /// <b>ADDED 2026-09-22, per Stefan directly: "CVM also includes nodes 201..205 and 102..105. I want
  /// that these nodes are also included in the boot stream."</b> Unlike every other coordinate in this
  /// list, these nine have no <c>NodeXxxProgram.cs</c> reference source anywhere in this repo -- there
  /// is nothing here for <see cref="CvmBootStreamBuilder.BuildDescriptors"/> to compile for them, and no
  /// entry for them in <see cref="CvmBootStreamBuilder.ReferenceSourceFor"/>. They rely entirely on
  /// whatever is currently saved in each node's own live project Node Editor tab -- exactly the same
  /// "live project source is the real source; a NodeXxxProgram.cs reference copy is only ever a
  /// fallback" rule <see cref="CvmBootStreamBuilder.ReferenceSourceFor"/>'s own remarks already state,
  /// just with no fallback at all here if a live source happens to be blank. This is intentional, not
  /// an oversight: this addition was originally scoped to closing the BOOT-STREAM gap specifically by
  /// getting these nine nodes into the boot roster this builder used at the time. Verified (by
  /// simulating this exact fill against the resulting 43-coordinate roster) that adding these nine
  /// changed none of the OTHER, already-verified via-edges: all nine attach as one single new leaf
  /// cluster hanging off node 305 (one hop further out: 305 -&gt; 205, then 205 -&gt; 204 -&gt;
  /// 203 -&gt; 202 -&gt; 201 on one branch and 205 -&gt; 105 -&gt; 104 -&gt; 103 -&gt; 102 on the other,
  /// following the same physical-grid adjacency <see cref="CvmBootStreamBuilder.GetPhysicalNeighbors"/>
  /// already uses everywhere else) -- none of them touch, or are touched by, any other branch of the
  /// mesh. None of these nine nodes' own loads has been hardware-tested.
  ///
  /// <b>SUPERSEDED, same day, per Stefan directly: "stop this static madness ... no more static
  /// lists."</b> This list is no longer what governs the CVM boot stream or post-mortem capture --
  /// <see cref="CvmBootStreamBuilder.GetConfiguredCoordinates"/> computes that roster live from each
  /// node's own project "configured" checkbox state instead, and does not consult
  /// <see cref="StandaloneCoordinates"/> at all. This list remains here only for its own separate,
  /// original purpose: the CVM Debugger's standalone/no-hardware Assemble-and-disassemble path
  /// (<see cref="ViewModels.CvmDebuggerViewModel.CompileStandaloneCvmNodes"/>) and the build-time
  /// <see cref="CvmPrimitiveTableExporter"/>. So these nine nodes being listed here no longer, on its
  /// own, puts them in the boot stream -- they are included whenever they are independently "configured"
  /// (enabled or given source) in the live project, exactly like every other node.
  /// </summary>
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
    Node308Program.Coordinate, // 308, CVM2's address-register node (added 2026-09-09 as "306"; RENUMBERED 2026-09-15 -- see Node308Program's own remarks).
    Node306Program.Coordinate, // 306, CVM2's floatingpoint register node (added 2026-09-15, RENUMBERED from the unrelated "VM 32 arithmetic" node this coordinate held 2026-09-09 through 2026-09-15 -- see Node306Program's own remarks). 'fpop, plus (2026-09-16) fadd/fsub/fmin/fmax/fmul/fdiv/fln2/filn2/fpi2/f2pi, are wired into the CVM instruction set; 'fpush is ALSO wired (2026-09-16, per Stefan's direct override: "fpush must be wired. it is a valid opcode."), once the naming collision with node 506's own 'fpush was resolved by Stefan renaming that one to 'pushf.

    // The new 17-node, 32-bit floating-point pipeline (added 2026-09-16) -- see this class's own remarks
    // above for the full shape and the still-open "how does this branch attach to the rest of the mesh"
    // question. Order below: imported node before its importer within each control chain, matching this
    // list's own established convention.
    Node305Program.Coordinate, // 305, stage 1 (unpack/pack).
    Node304Program.Coordinate, // 304, stage 2 (rearrange/collect).
    Node303Program.Coordinate, // 303, stage 3 (split into the 3a/3b/3c control chains).
    Node302Program.Coordinate, // 302, stage 4 (split exponent/mantissa).
    Node301Program.Coordinate, // 301, stage 5 (extend mantissa).
    Node401Program.Coordinate, // 401, stage 5a (mantissa handling, end of the "3a" control chain).
    Node402Program.Coordinate, // 402, stage 4a (exponent handling, imports/controls 401).
    Node403Program.Coordinate, // 403, stage 3a (sign & type handling, imports/controls 402).
    Node404Program.Coordinate, // 404, stage 8 (rounding, reduce to binary32).
    Node501Program.Coordinate, // 501, stage 5b (36-bit add/sub/multiply, end of the "3b" chain).
    Node502Program.Coordinate, // 502, stage 4b (exponent arithmetic, imports/controls 501).
    Node503Program.Coordinate, // 503, stage 3b (implements add/multiply, imports/controls 502).
    Node504Program.Coordinate, // 504, stage 7 (classify/finalize: pass-through/zero/infinity/qNaN).
    Node601Program.Coordinate, // 601, stage 5c (non-restoring binary division, end of the "3c" chain).
    Node602Program.Coordinate, // 602, stage 4c (normalize result, imports/controls 601).
    Node603Program.Coordinate, // 603, stage 3c (final sign/normalization control, imports/controls 602).
    Node604Program.Coordinate, // 604, stage 6 (classify operand types into a result class k).

    // ADDED 2026-09-22, per Stefan directly ("CVM also includes nodes 201..205 and 102..105. I want
    // that these nodes are also included in the boot stream.") -- see this class's own remarks above
    // for why there is no NodeXxxProgram.cs reference source for any of these nine, and the verified
    // shape they attach to the rest of the mesh in (one single new leaf cluster hanging off node 305).
    201, // CVM's own live-project source for this coordinate -- no NodeXxxProgram.cs reference exists.
    202, // ditto.
    203, // ditto.
    204, // ditto.
    205, // ditto.
    102, // ditto.
    103, // ditto.
    104, // ditto.
    105, // ditto.
  ];
}