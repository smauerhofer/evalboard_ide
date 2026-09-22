using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Compiles the CVM cluster's resident node programs and produces one <see cref="CvmBootDescriptor"/>
/// per node.
///
/// <b>CVM2 (2026-09-01).</b> Stefan is rewriting the whole CVM around new, differently-numbered nodes
/// and a more sophisticated inter-node communication scheme; CVM1's nine-node branching tree (607 as
/// CPU, with 507/606/608 and 507's own three children 506/508/407) is retired -- "the nodes from CVM1
/// ... will not be used in CVM2". CVM2's own topology BRANCHES AT 507 (added 2026-09-04, nodes 506 and
/// 508 -- see below): 708 (the real, unmirrored async serial PC link, unchanged in role from CVM1) -&gt;
/// 707 (a permanent runtime relay between 607 and 708) -&gt; 607 (the on-chip SRAM-request router) -&gt;
/// 507 (the entire CPU, see <see cref="Node507Program"/>), which itself has THREE sibling leaf children
/// reached from its own <c>m/main</c> dispatch: 407 (the long-call/long-jump helper, added 2026-09-02,
/// reached via 507's DOWN port -- see <see cref="Node407Program"/>), 506 (the stack-frame node, added
/// 2026-09-04, reached via 507's RIGHT port -- see <see cref="Node506Program"/>), and 508 (the
/// globals-access node, also added 2026-09-04, reached via 507's LEFT port -- see
/// <see cref="Node508Program"/>). CVM2's mesh goes a level deeper for the first time as of 2026-09-05:
/// node 509 (the unary-arithmetic node, see <see cref="Node509Program"/>) hangs off node 508's OWN
/// dispatch, not 507's directly, reached via 508's RIGHT port -- 507 -&gt; 508 -&gt; 509. Later the same
/// day, a SECOND such three-hop branch was added: node 406 (the binary-arithmetic node, see
/// <see cref="Node406Program"/>) hangs off node 407's OWN dispatch, reached via 407's RIGHT port -- 507
/// -&gt; 407 -&gt; 406, mirroring 507 -&gt; 508 -&gt; 509 on the OTHER leaf of 507's own three-way split.
/// The next day (2026-09-06), node 407 grew a SECOND child of its own: node 408 (the comparison node,
/// see <see cref="Node408Program"/>) hangs off node 407's OWN dispatch too, reached via 407's LEFT port
/// (a previously-unanswered branch of that same cascade) -- 507 -&gt; 407 -&gt; 408, a sibling of 406 under
/// 407 rather than a further hop past it. Later the SAME day, node 407 grew a THIRD child, but this one
/// FURTHER OUT rather than a sibling: node 407's own long-FLAGGED "1101" branch (see
/// <see cref="Node407Program"/>'s own remarks) is filled by node 307, "VM ternary main" (see
/// <see cref="Node307Program"/>), which itself has its own further RIGHT-port child, node 306, the 4x
/// 32-bit-address-register node (see <see cref="Node306Program"/>) -- 507 -&gt; 407 -&gt; 307 -&gt; 306, CVM2's
/// first FIVE-hop-deep branch. On 2026-09-07, the 508-&gt;509 branch was extended two hops further: node
/// 510, "extended arithmetic" (see <see cref="Node510Program"/>), fills node 509's own previously-open
/// LEFT-port branch (508 -&gt; 509 -&gt; 510), and node 511, the 32-register register-file node (see
/// <see cref="Node511Program"/>), hangs off node 510's own RIGHT port one hop further out still (509 -&gt;
/// 510 -&gt; 511) -- CVM2's mesh is now SIX relay hops deep on this branch, its deepest so far. Node 511's
/// own four opcodes ('rld/'rst/'rpop/'rpush, supporting register-based parameter passing) needed a
/// genuinely new operand encoding, <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/>
/// -- see that enum member's own remarks. CVM2's mesh is FOURTEEN nodes now
/// (708/707/607/507/407/506/508/509/406/408/307/306/510/511). "More nodes will be added later" (Stefan's
/// own words) -- this builder's job is to stay easy to extend as that happens, not to assume fourteen is
/// final.
///
/// <b>RENUMBERED 2026-09-15: node 306 and node 308 have swapped roles.</b> Every mention of "node 306"
/// above, from this class-level history through <see cref="BuildLoadOrder"/>'s own doc comment, describes
/// the 32-bit address-register node reached via 307's own port -- that node is now physical node 308 (see
/// <see cref="Node308Program"/>), not 306. Physical node 306 now names an unrelated, brand-new
/// floating-point register node (see <see cref="Node306Program"/>, a full rewrite), a sibling of node 308
/// under node 307. This builder compiles AND loads it (see <see cref="BuildDescriptors"/>'s own
/// <c>result306</c> and <see cref="BuildLoadOrder"/>'s own step, both added the same day 'fpop was wired)
/// -- but only 'fpop itself is wired as a CVM mnemonic; the node's own three-way (binary/constant/unary)
/// dispatch still does not fit any existing operand-encoding shape beyond that one op, so wiring the rest
/// in remains an open architecture question, not guessed at here. The mesh shape itself is unchanged by
/// this renumbering (still 507 -&gt; 407 -&gt; 307 -&gt; [address-register node], still CVM2's first
/// five-hop-deep branch) -- only the coordinate label on the leaf moved, from 306 to 308.
///
/// <b>Node 507 (CPU), not 508 -- corrected 2026-09-01.</b> This project's own session briefly placed
/// CVM2's CPU source on node 508 under a mistaken attribution; Stefan corrected it directly: the CPU
/// is node 507. All of this builder's own compiling/loading of "the CPU node" targets
/// <see cref="Node507Program"/>. Node 508 itself was left as an ignored placeholder until 2026-09-04,
/// when Stefan supplied its real role and source -- the globals-access node, a third sibling leaf of
/// 507 alongside 407 and 506 -- so <see cref="Node508Program"/> IS now compiled and loaded below, just
/// never as the CPU.
///
/// <b>Node 607's CVM2 source (2026-09-01).</b> <see cref="Node607Program"/> now carries Stefan's own
/// CVM2 source -- the on-chip SRAM-request router between 507 and 707 -- replacing the earlier
/// placeholder gap left by a prior context-limit compaction that lost the first copy pasted into this
/// session.
///
/// <b>Nodes 707 and 708's REAL CVM2 source (2026-09-01).</b> This project's earlier copies of 707 and
/// 708 -- carried since 2026-08-27 and wrongly treated as already-correct CVM2 content -- have both
/// now been replaced with Stefan's real source for each. The real 708 names its three request words
/// <c>/wr</c>/<c>/rd</c>/<c>/cx</c> (a leading slash, not a tick) and exports nothing named <c>'left</c>
/// at all; the real 707 imports 708 by those same slash names (<c>A[ /wr ; ]]</c> etc.) with a plain
/// three-way dispatch (write / compare-exchange / read, no "mark" branch, no leading <c>'left</c>
/// read) -- see <see cref="Node707Program"/>/<see cref="Node708Program"/>'s own remarks. The two were
/// compiled together to confirm the match, not just assert it: <c>Success = true</c> for both, with
/// only the same two harmless <c>'warm'</c>/<c>'cold'</c> shadowing warnings 707's own import of 708
/// has always produced.
///
/// <b>Compile order.</b> None of CVM2's four nodes import another CVM2 node's exports BY NAME except
/// 707, which does <c># 708 import</c> to reach 708's exports by name via a multiport call. 507 talks
/// to 607, and 607 talks to 707, purely through raw port I/O (<c>@</c>/<c>!</c>/<c>@b</c>/<c>!b</c>)
/// -- no named symbol resolution needed, so both compile completely standalone. So: 708 (ROM then
/// RAM) first, then 707 (importing 708's combined ROM+RAM exports); 607 and 507 are independent
/// standalone compiles with no ordering constraint relative to anything else.
///
/// <b>Load order is not compile order.</b> CVM2's mesh DOES branch, right at 507 -- exactly the DB013
/// 6.1.2.4 "Root Node Programming" concern <see cref="CvmBootLoadStep"/>'s own remarks describe (a
/// branch node needing a temporary relay role while EACH child loads in turn), now genuinely in play
/// with THREE leaf siblings hanging off 507 (407, 506, and, as of 2026-09-04, 508) plus, as of
/// 2026-09-05, a FOURTH level on TWO separate branches: node 509 hangs off 508's own dispatch rather
/// than 507's, and node 406 hangs off 407's own dispatch rather than 507's. As of 2026-09-06, 407 gained
/// a SECOND child of its own, node 408, a sibling of 406 under 407 (both must load before 407's own
/// step). Load order: 406 and 408 (via 407 acting as relay -- 406 added 2026-09-05, 408 added
/// 2026-09-06 -- both must load before 407's OWN step, since 407 must still be a passive relay while
/// either loads through it), then 407 (reached via 507 acting as relay, added 2026-09-02), then 506
/// (also via 507 acting as relay, added 2026-09-04), then 509 (via 508 acting as relay, added
/// 2026-09-05 -- 509 must load before 508's OWN step, for the same reason 406/408 must load before
/// 407's), then 508 (via 507 acting as relay, added 2026-09-04 -- 507 relays ALL THREE of its direct
/// children in turn, re-pointing its own B port at whichever child is loading next), then 507 itself
/// (via 607), then 607 (via 707), then 707 (via 708), then 708 itself last, direct, no relay -- the same
/// tail Stefan already confirmed for CVM1 (607 via 707, 707 via 708, 708 last).
/// <see cref="Services.Ga144CvmHardwareInstaller.OpenAndBootMesh"/>'s own
/// <c>parentOf</c>/<c>AncestorChain</c>/<c>focused</c>-set relay logic needed NO code changes to support
/// any of this, including 509's, 406's, and 408's own extra hops: <c>AncestorChain</c> already walks
/// <c>parentOf</c> recursively however deep the chain goes, so 509's own ancestor chain
/// (708/707/607/507/508, four relay hops deep) and 406's/408's own ancestor chains (both
/// 708/707/607/507/407, also four relay hops deep) all fall out of the exact same generic loop that
/// already handled 407/506/508's own three-hop chains -- each load step unconditionally re-points its
/// via-node's B port at that step's own target, so loading 406 and 408 (both through 407, which is
/// itself still just relaying, in either order relative to each other), then 407, then 506, then 509
/// (through 508, which is itself still just relaying), then 508 back-to-back all through the same tree
/// already works correctly -- 407 gets "focused" only once (during whichever of the 406/408 steps runs
/// first, the first time it appears in ANY load step's own ancestor chain), 507 gets "focused" only once
/// (during that same first step too, since both 406's and 408's own ancestor chains reach all the way to
/// 507), 508 gets "focused" only once (during the 509 step), and every later step simply re-points, no
/// re-focus needed. This load order is inferred from the same confirmed CVM1 reasoning applied to CVM2's
/// branching case -- the ORIGINAL 407 step (without 406/408 as further hops) IS confirmed on real
/// hardware (see <see cref="Node407Program"/>'s own remarks), but the NEW 406, 408, 506, 508, and 509
/// steps are not yet real-hardware-tested.
///
/// <b>This builder's own compiles are reference-only, never what real hardware installs
/// (2026-09-01).</b> <see cref="BuildDescriptors"/>/<see cref="BuildLoadPlan"/> below compile the
/// fixed <c>NodeXxxProgram.Source</c> strings baked into this assembly, and neither is called by the
/// shipped app's real install/dry-run paths any more -- both <see cref="Services.Ga144CvmHardwareInstaller"/>
/// and <see cref="ViewModels.ChipViewModel"/>'s "Compile CVM Test" compile each node's OWN CURRENT
/// PROJECT source first, per Stefan's explicit instruction ("take the nodes code in the project and
/// not the NodeXxxProgram code, which should only be used if no code in the project is defined"), and
/// reach for <see cref="ReferenceSourceFor"/> below only as a fallback when a node's project source is
/// still blank. <see cref="BuildDescriptors"/>/<see cref="BuildLoadPlan"/> remain here only as this
/// session's own throwaway-harness/reference-verification tool for the reference sources themselves.
/// </summary>
public static class CvmBootStreamBuilder
{
  public static IReadOnlyList<CvmBootDescriptor> BuildDescriptors()
  {
    var compiler = new F18Compiler();

    // 708 needs no cross-node import (nothing in its source imports another CVM node's exports), but
    // unlike every other node it is not an ordinary internal F18A node: it is the real, unmirrored
    // async serial boot node, so its RAM compile needs ITS OWN real factory ROM's exports (18ibits,
    // delay) in scope -- the same same-node ROM-then-RAM pairing F18NodeCompilationService uses, not a
    // cross-node ImportResolver. CVM2 (2026-09-01): this is now Stefan's own real 708 source -- see
    // Node708Program's own remarks -- which exports /wr//rd//cx (no leading tick) and no 'left at all.
    F18CompileResult rom708 = CompileNode708Rom(compiler);
    ThrowIfFailed(rom708);

    F18CompileResult result708 = Compile(compiler, Node708Program.Source, ImportingRom(Node708Program.Coordinate, rom708));
    ThrowIfFailed(result708);

    // 707's '# 708 import' is an ordinary cross-node RAM import, EXCEPT that 708's own exports span
    // both its custom ROM and its RAM (unlike 607/507/407/506/508/509 below, none of which layers real
    // custom ROM under their RAM). Reproduces F18NodeCompilationService.ResolveRamImport's exact combine-then-
    // import sequence for that one case: merge 708's ROM exports with its RAM exports, then hand the
    // merged set to 707 as its resolved import. CVM2 (2026-09-01): this is now Stefan's own real 707
    // source -- see Node707Program's own remarks -- matched to the real 708 above (imports /wr//rd//cx
    // by name, three-way dispatch, no 'left read).
    F18CompileResult result707 = Compile(compiler, Node707Program.Source, ImportingCombinedRam(Node707Program.Coordinate, rom708, result708));
    ThrowIfFailed(result707);

    // CVM2 (2026-09-01): 607 is now the on-chip SRAM-request router, reached from 507 above and
    // reaching 707 below purely via raw port I/O -- no '# NNN import' directive of its own (unlike
    // CVM1's 507/606/608, which all imported 607 by name). Standalone compile, same shape as 507
    // below. Verified via a standalone harness compile of this exact source (0 diagnostics, 18/64
    // words used, entry point 'main' at 0x000) -- see Node607Program's own remarks.
    F18CompileResult result607 = Compile(compiler, Node607Program.Source, F18CompilerOptions.ForRam(Node607Program.Coordinate));
    ThrowIfFailed(result607);

    // CVM2 (2026-09-01): 507 is the entire CPU -- corrected from an earlier, mistaken attribution to
    // node 508 in this project's own session (see Node507Program/Node508Program's own remarks).
    // Standalone compile -- confirmed via a standalone harness compile of this exact source (0
    // errors, UsedWordCount 61, EntryPoint 0x01C at m/main, as of the 2026-09-02 '# m/main lit >r'
    // fix that Stefan confirmed working on real hardware) -- see Node507Program's own remarks.
    F18CompileResult result507 = Compile(compiler, Node507Program.Source, F18CompilerOptions.ForRam(Node507Program.Coordinate));
    ThrowIfFailed(result507);

    // CVM2 (2026-09-02): node 407, the long-call/long-jump helper -- reached from 507's own m/main
    // dispatch (down port, per Node507Program's own remarks) once a fetched opcode word's top bits read
    // "11??". Imports 507 by name ('# 507 import', m/pop/m/push/m/next/a/a!), so must compile AFTER
    // result507 above. See Node407Program's own remarks for the full source and the confirmed physical
    // link (Stefan: "the port between 407 and 507 is still 'down'").
    F18CompileResult result407 = Compile(compiler, Node407Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node407Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node507Program.Coordinate
          ? F18ImportResolution.FromExports(result507.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result407);

    // CVM2 (2026-09-05): node 406, the binary-arithmetic node -- reached from node 407's own n/main
    // dispatch via ITS RIGHT port, NOT a sibling of 407/506/508 but a further hop past 407 (407 -> 406),
    // CVM2's SECOND three-hop branch off 507 (mirroring 507->508->509 with 507->407->406). Imports 407
    // by name ('# 407 import', n/r@/n/r!/n/pop/n/push/n/next/n/leave), so must compile AFTER result407
    // above, not result507. See Node406Program's and Node407Program's own remarks for the full source
    // and the import-name-mismatch fix that unblocked this compile.
    F18CompileResult result406 = Compile(compiler, Node406Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node406Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node407Program.Coordinate
          ? F18ImportResolution.FromExports(result407.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result406);

    // ADDED 2026-09-22, closing a real pre-existing gap: node 405, the multiword-arithmetic
    // (carry-flag) node -- reached from node 406's own y/main dispatch (its previously-open
    // "1110_1???" branch, relaying LEFT -- see Node406Program's and Node405Program's own remarks).
    // Imports 406 by name ('# 406 import', y/r@/y/r!/y/pop/y/leave), so must compile AFTER result406
    // above. Node 405 has had a real resident source and a place in CvmNodeMesh.StandaloneCoordinates
    // since 2026-09-09, but was never compiled here, listed in BuildLoadOrder, or given a
    // ReferenceSourceFor fallback until BuildLoadOrder's own 2026-09-22 rework (see that method's own
    // remarks) needed a real via-edge, and therefore a real compiled descriptor, for it.
    F18CompileResult result405 = Compile(compiler, Node405Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node405Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node406Program.Coordinate
          ? F18ImportResolution.FromExports(result406.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result405);

    // CVM2 (2026-09-06): node 408, the comparison node -- reached from node 407's own n/main dispatch via
    // ITS LEFT port, the SAME kind of further hop past 407 as node 406 is (407 -> 408), CVM2's own
    // fourth-relay-hop sibling of 406 rather than a further link past it. Imports 407 by name
    // ('# 407 import', n/r@/n/r!/n/pop/n/push/n/leave), so must compile AFTER result407 above, same as
    // result406. See Node408Program's and Node407Program's own remarks for the full source and the
    // previously-unanswered "1111"/LEFT branch this fills.
    F18CompileResult result408 = Compile(compiler, Node408Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node408Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node407Program.Coordinate
          ? F18ImportResolution.FromExports(result407.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result408);

    // CVM2 (2026-09-06): node 307, "VM ternary main" -- fills node 407's own long-FLAGGED "1101"
    // dispatch branch (see Node407Program's own remarks). Imports 407 by name ('# 407 import',
    // n/@/n/!/n/r@/n/r!/n/pop/n/push/n/next/n/leave), so must compile AFTER result407 above.
    //
    // NOT EXPECTED TO SUCCEED YET: Node307Program.Source, as supplied, is very likely incomplete -- its
    // own k/main definition has no closing ';' and its final dispatch branch has no code at all (see
    // Node307Program's own remarks, reproduced verbatim there rather than silently completed). This call
    // is expected to throw via ThrowIfFailed below until Stefan confirms/completes that source. Left in,
    // rather than special-cased around, per this method's own established "every node here gets
    // compiled and checked, loudly, or not at all" pattern -- the SAME discipline every earlier node's
    // own compile step already follows.
    F18CompileResult result307 = Compile(compiler, Node307Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node307Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node407Program.Coordinate
          ? F18ImportResolution.FromExports(result407.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result307);

    // RENUMBERED 2026-09-15: the address-register mesh this comment used to describe under "node 306"
    // now lives on physical node 308 -- Stefan: "node 306 and 308 have swapped roles." This step now
    // compiles Node308Program.Source (not Node306Program.Source) accordingly; the variable is renamed
    // result308 to match. Node 306 itself now names an unrelated, brand-new floating-point register node
    // (see Node306Program's own remarks) -- see result306's own compile step further below, added the
    // same day 'fpop was wired.
    //
    // CVM2 (2026-09-06): the address-register node -- reached from node 307's own k/main dispatch via
    // its LEFT port as of 2026-09-15 (was RIGHT, under the "node 306" name, through 2026-09-11 -- see
    // Node307Program's own "RIGHT/LEFT SWAPPED" remarks), one hop further out than 307 itself
    // (507 -> 407 -> 307 -> 308). Imports 307 by name ('# 307 import', k/r@/k/r!/k/pop/k/push/k/leave),
    // so must compile AFTER result307 above -- and therefore inherits the same "not expected to succeed
    // yet" caveat noted there, since a failed result307 leaves nothing valid in result307.Exports for
    // this import to resolve. See Node308Program's own remarks for the full source.
    //
    // STALE COMMENT, CORRECTED 2026-09-09: this used to describe this node's ORIGINAL six mnemonics
    // (ldar/star/inca/deca/lda/sta) as a self-describing family needing no live compile at all -- that
    // was true of the 2026-09-06 source, but this node's source was rewritten the same day (second pass
    // of the opcode/assembler-vs-node reconciliation audit) around a different, tick-prefixed,
    // NODE-RESOLVED family instead (arinc/ardec/arld/arst/lda/sta, plus arinc2/ardec2 added 2026-09-15 --
    // see Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own NodeSymbolByMnemonic and
    // NodeResolvedEmbeddedValueFieldLayoutByMnemonic for the wiring). Unlike the old family, ALL EIGHT of
    // these current mnemonics DO depend on this compile step succeeding -- each one's real opcode word is
    // resolved against result308's own F18 symbols, exactly like every other tagged/node-resolved
    // mnemonic elsewhere in this file. The OLD family's own mnemonic and tag constants
    // (CvmInstructionSet.LoadAddressRegisterMnemonic and its siblings) were deleted outright 2026-09-09
    // (CVM1-opcode purge) and no longer exist.
    F18CompileResult result308 = Compile(compiler, Node308Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node308Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node307Program.Coordinate
          ? F18ImportResolution.FromExports(result307.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result308);

    // CVM2 (2026-09-21): node 305, now the floating-point REGISTER FILE (see Node305Program's own remarks
    // for the full derivation) -- moved here, ahead of result306 below, because node 306's own rework gave
    // it a SECOND import, '# 305 import' (fr/binary/fr/move/fr/const/fr/neg/fr/abs/fr/pop/fr/push, plus
    // the six f.add.."f.div" constants), which result306's own ImportResolver now resolves against this
    // result. Node 305 itself has no imports of its own (no '# NNN import' in its new source), so, unlike
    // every chained import step elsewhere in this method, it has no ordering constraint of its own beyond
    // "before result306". This is a SECOND, UNRELATED role for coordinate 305 in this file -- it no longer
    // has anything to do with the old 17-node floating-point pipeline's own stage 1 (see the "new 17-node
    // pipeline" comment block further below, updated to flag that node 305 has been carved out of it).
    F18CompileResult result305 = Compile(compiler, Node305Program.Source, F18CompilerOptions.ForRam(Node305Program.Coordinate));
    ThrowIfFailed(result305);

    // CVM2 (2026-09-15, REWORKED 2026-09-21): node 306, the floating-point INSTRUCTION DECODER -- reached
    // from node 307's own k/main dispatch via its RIGHT port (Node307Program's own "RIGHT/LEFT SWAPPED"
    // remarks), a SIBLING of node 308 above. Imports 307 by name ('# 307 import', k/pop/k/push/k/leave),
    // so must compile AFTER result307 above, same as result308 -- and now ALSO imports 305 by name
    // ('# 305 import', the register-file words listed on result305's own compile step just above), so
    // must compile after result305 too. Every mnemonic node 306 provides (fadd/fsub/fmin/fmax/fmul/fdiv/
    // fmove/fconst/fneg/fabs/fpop/fpush) is fully self-describing at the CVM-opcode level regardless
    // (CvmOperandEncoding.EmbeddedUnsignedValuePair, see CvmInstructionSet.FloatingPointAddMnemonic's own
    // remarks) and does not itself depend on this compile succeeding -- only BuildDescriptors' own "every
    // node compiles cleanly" invariant does.
    F18CompileResult result306 = Compile(compiler, Node306Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node306Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate switch
      {
        int coordinate when coordinate == Node307Program.Coordinate => F18ImportResolution.FromExports(result307.Exports),
        int coordinate when coordinate == Node305Program.Coordinate => F18ImportResolution.FromExports(result305.Exports),
        _ => F18ImportResolution.Failure($"node {importedCoordinate} not available"),
      },
    });
    ThrowIfFailed(result306);

    // CVM2 (2026-09-04): node 506, the stack-frame node (enter/leave/...) -- reached from 507's own
    // m/main dispatch via its RIGHT port, a SIBLING of 407 (both are leaves hanging directly off 507,
    // not a further link in the chain past 407 -- confirmed independently by
    // Models.KrakenConfiguration.PortAddress, which computes "right" on BOTH sides of 507<->506, same
    // as node 407's own remarks confirmed "down" on both sides of 507<->407). Imports 507 by name
    // ('# 507 import', m/pop/m/push/m/next/a/a!), so must compile AFTER result507 above, same as 407.
    // See Node506Program's own remarks for the full source.
    F18CompileResult result506 = Compile(compiler, Node506Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node506Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node507Program.Coordinate
          ? F18ImportResolution.FromExports(result507.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result506);

    // ADDED 2026-09-22, closing a real pre-existing gap: node 505, the "frame2, more frame
    // operations" node -- fills node 506's own previously-unwired "1001_01??" relay branch (see
    // Node506Program's and Node505Program's own remarks). Imports 506 by name ('# 506 import',
    // f/r@/f/r!/f/pop/f/push/f/next/f/leave), so must compile AFTER result506 above. Same
    // previously-flagged, now-closed gap as node 405 above: a real resident source and a place in
    // CvmNodeMesh.StandaloneCoordinates since 2026-09-09, but never compiled here, listed in
    // BuildLoadOrder, or given a ReferenceSourceFor fallback until BuildLoadOrder's 2026-09-22 rework
    // needed a real via-edge for it too.
    F18CompileResult result505 = Compile(compiler, Node505Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node505Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node506Program.Coordinate
          ? F18ImportResolution.FromExports(result506.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result505);

    // CVM2 (2026-09-04): node 508, the globals-access node (load/store global, 'gld/'gst -- renamed
    // 2026-09-09 from 'ldg'/'stg, see CvmInstructionSet.LoadGlobalMnemonic's own remarks) -- reached
    // from 507's own m/main dispatch via its LEFT port, a THIRD sibling of 407 and 506 (all three hang
    // directly off 507's own dispatch, none is a further link past another -- confirmed independently
    // by Models.KrakenConfiguration.PortAddress, which computes "left" on BOTH sides of 507<->508, same
    // as node 407's own "down" and node 506's own "right"). Imports 507 by name ('# 507 import',
    // m/pop/m/push/m/next/m/2@/m/2!), so must compile AFTER result507 above, same as 407 and 506.
    // See Node508Program's own remarks for the full source.
    F18CompileResult result508 = Compile(compiler, Node508Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node508Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node507Program.Coordinate
          ? F18ImportResolution.FromExports(result507.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result508);

    // CVM2 (2026-09-05): node 509, the unary-arithmetic node -- reached from node 508's own g/main
    // dispatch via its RIGHT port, NOT a sibling of 407/506/508 but a further hop past 508 (508 -> 509,
    // confirmed independently by Models.KrakenConfiguration.PortAddress, which computes "right" on BOTH
    // sides of 508<->509). Imports 508 by name ('# 508 import', g/r@/g/r!/g/pop/g/push/g/leave), so must
    // compile AFTER result508 above, not result507. See Node509Program's own remarks for the full source.
    F18CompileResult result509 = Compile(compiler, Node509Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node509Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node508Program.Coordinate
          ? F18ImportResolution.FromExports(result508.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result509);

    // CVM2 (2026-09-07): node 510, "extended arithmetic" -- reached from node 509's own u/main
    // dispatch via ITS LEFT port, NOT a sibling of 406/408/307/509/506 but a further hop past 509
    // (508 -> 509 -> 510), CVM2's mesh going five relay hops deep on this branch for the first time
    // (708/707/607/507/508/509/510). Imports 509 by name ('# 509 import',
    // u/r@/u/r!/u/pop/u/push/u/leave), so must compile AFTER result509 above. Supplied by Stefan
    // together with node 511 ("here are nodes 510 and 511"). See Node510Program's own remarks for the
    // full source, the confirmed relay chain (node 509's own previously-open LEFT branch), and the
    // FLAGGED opening-idiom shape (matching node 509's OWN pre-fix pattern, not its corrected one).
    F18CompileResult result510 = Compile(compiler, Node510Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node510Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node509Program.Coordinate
          ? F18ImportResolution.FromExports(result509.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result510);

    // CVM2 (2026-09-07): node 511, the 32-register register-file node -- reached from node 510's own
    // x/main dispatch via ITS RIGHT port, one hop further out than 510 itself (509 -> 510 -> 511),
    // CVM2's mesh now SIX relay hops deep on this branch (708/707/607/507/508/509/510/511). Imports
    // 510 by name ('# 510 import', x/r@/x/r!/x/pop/x/push/x/leave), so must compile AFTER result510
    // above. Its own four new CVM opcodes ('rld/'rst/'rpop/'rpush, register load/store/pop/push,
    // supporting register-based parameter passing per Stefan's own description) are wired into
    // CvmInstructionSet/CvmAssemblyLanguage directly via the new NodeResolvedEmbeddedValue encoding
    // (see LoadRegisterFileMnemonic's and CvmOperandEncoding.NodeResolvedEmbeddedValue's own remarks),
    // not through this node's own live compile succeeding here -- see Node511Program's own remarks for
    // the full source and the bit-format derivation ((functionAddress - 0x20) &lt;&lt; 5 | registerIndex).
    F18CompileResult result511 = Compile(compiler, Node511Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node511Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node510Program.Coordinate
          ? F18ImportResolution.FromExports(result510.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result511);

    // ----------------------------------------------------------------------------------------------
    // What remains of the old 16-node (WAS 17, see below), 32-bit floating-point pipeline (added
    // 2026-09-16) -- an entirely separate mesh branch from the CVM instruction-dispatch tree above:
    // none of these nodes import, or are imported by, any node compiled above. See CvmNodeMesh's own
    // remarks for the full pipeline shape (originally 301-305 unpack/rearrange/split, fanning out at
    // 303 into three control chains -- "3a" 403->402->401, "3b" 503->502->501, "3c" 603->602->601 --
    // converging back at 604/504/404).
    //
    // CORRECTED, 2026-09-21: the comment this replaces claimed these 17 nodes were "deliberately left
    // OUT of BuildLoadOrder below until Stefan confirms that link" -- that is no longer true of
    // BuildLoadOrder's actual content (see its own doc comment and steps further below: Stefan DID
    // confirm the full leaf-first chain, "306->305->304->303->302->301" etc., on 2026-09-16, and it has
    // been present there since). That claim was stale here even before today's edit; corrected now
    // rather than repeated, since this block is being touched anyway.
    //
    // FLAGGED, 2026-09-21, NOT RESOLVED: node 305 -- this pipeline's own former stage 1 -- was reassigned
    // to an unrelated floating-point-register-file role as part of Stefan's FP-engine rework (see
    // result305's own compile step, now far above this one, and Node305Program's own remarks). Its
    // compile step was REMOVED from this block for that reason (it still compiles, just earlier, for the
    // unrelated reason of feeding node 306's import). BuildLoadOrder's own "(304, 305)"/"(305, 306)" load
    // steps are a physical mesh-relay/deployment concern, not a data-flow one, and its own leaf-first
    // topology (305 sits one hop further from the boot-injection point than 306, unchanged by either
    // node's functional role) still holds regardless -- so those two steps were left as they are, not
    // touched here. What is still genuinely open is FUNCTIONAL, not load-order: whether these remaining
    // 16 nodes (301-304, 401-404, 501-504, 601-604) are now dead code, whether one of them takes over
    // node 305's former stage-1 role, or whether the entire branch is meant to be retired. Not decided
    // here either way -- these 16 are still compiled below (for standalone tooling/disassembly) exactly
    // as before.
    //
    // Compile order: within each control chain, the IMPORTED node compiles first, exactly like every
    // "# N import" chain above (401 before 402 before 403, matching 403's own "# 402 import" and 402's
    // own "# 401 import"; same pattern for 501/502/503 and 601/602/603). The row-300 nodes (301-304) and
    // the no-import siblings (404, 504, 604) have no ordering constraint of their own.
    // ----------------------------------------------------------------------------------------------

    F18CompileResult result304 = Compile(compiler, Node304Program.Source, F18CompilerOptions.ForRam(Node304Program.Coordinate));
    ThrowIfFailed(result304);

    F18CompileResult result303 = Compile(compiler, Node303Program.Source, F18CompilerOptions.ForRam(Node303Program.Coordinate));
    ThrowIfFailed(result303);

    F18CompileResult result302 = Compile(compiler, Node302Program.Source, F18CompilerOptions.ForRam(Node302Program.Coordinate));
    ThrowIfFailed(result302);

    F18CompileResult result301 = Compile(compiler, Node301Program.Source, F18CompilerOptions.ForRam(Node301Program.Coordinate));
    ThrowIfFailed(result301);

    F18CompileResult result401 = Compile(compiler, Node401Program.Source, F18CompilerOptions.ForRam(Node401Program.Coordinate));
    ThrowIfFailed(result401);

    F18CompileResult result402 = Compile(compiler, Node402Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node402Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node401Program.Coordinate
          ? F18ImportResolution.FromExports(result401.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result402);

    F18CompileResult result403 = Compile(compiler, Node403Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node403Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node402Program.Coordinate
          ? F18ImportResolution.FromExports(result402.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result403);

    F18CompileResult result404 = Compile(compiler, Node404Program.Source, F18CompilerOptions.ForRam(Node404Program.Coordinate));
    ThrowIfFailed(result404);

    F18CompileResult result501 = Compile(compiler, Node501Program.Source, F18CompilerOptions.ForRam(Node501Program.Coordinate));
    ThrowIfFailed(result501);

    F18CompileResult result502 = Compile(compiler, Node502Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node502Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node501Program.Coordinate
          ? F18ImportResolution.FromExports(result501.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result502);

    F18CompileResult result503 = Compile(compiler, Node503Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node503Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node502Program.Coordinate
          ? F18ImportResolution.FromExports(result502.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result503);

    F18CompileResult result504 = Compile(compiler, Node504Program.Source, F18CompilerOptions.ForRam(Node504Program.Coordinate));
    ThrowIfFailed(result504);

    F18CompileResult result601 = Compile(compiler, Node601Program.Source, F18CompilerOptions.ForRam(Node601Program.Coordinate));
    ThrowIfFailed(result601);

    F18CompileResult result602 = Compile(compiler, Node602Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node602Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node601Program.Coordinate
          ? F18ImportResolution.FromExports(result601.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result602);

    F18CompileResult result603 = Compile(compiler, Node603Program.Source, new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Ram,
      NodeCoordinate = Node603Program.Coordinate,
      MemoryBaseAddress = 0x000,
      MemoryWordCount = 64,
      IncludeCommonRomWords = true,
      ImportResolver = importedCoordinate => importedCoordinate == Node602Program.Coordinate
          ? F18ImportResolution.FromExports(result602.Exports)
          : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
    });
    ThrowIfFailed(result603);

    F18CompileResult result604 = Compile(compiler, Node604Program.Source, F18CompilerOptions.ForRam(Node604Program.Coordinate));
    ThrowIfFailed(result604);

    return
    [
      CvmBootDescriptor.FromCompileResult(result407),
      CvmBootDescriptor.FromCompileResult(result406),
      CvmBootDescriptor.FromCompileResult(result405),
      CvmBootDescriptor.FromCompileResult(result408),
      CvmBootDescriptor.FromCompileResult(result307),
      CvmBootDescriptor.FromCompileResult(result308),
      CvmBootDescriptor.FromCompileResult(result306),
      CvmBootDescriptor.FromCompileResult(result506),
      CvmBootDescriptor.FromCompileResult(result505),
      CvmBootDescriptor.FromCompileResult(result511),
      CvmBootDescriptor.FromCompileResult(result510),
      CvmBootDescriptor.FromCompileResult(result509),
      CvmBootDescriptor.FromCompileResult(result508),
      CvmBootDescriptor.FromCompileResult(result507),
      CvmBootDescriptor.FromCompileResult(result607),
      CvmBootDescriptor.FromCompileResult(result707),
      CvmBootDescriptor.FromCompileResult(result708),
      CvmBootDescriptor.FromCompileResult(result305),
      CvmBootDescriptor.FromCompileResult(result304),
      CvmBootDescriptor.FromCompileResult(result303),
      CvmBootDescriptor.FromCompileResult(result302),
      CvmBootDescriptor.FromCompileResult(result301),
      CvmBootDescriptor.FromCompileResult(result401),
      CvmBootDescriptor.FromCompileResult(result402),
      CvmBootDescriptor.FromCompileResult(result403),
      CvmBootDescriptor.FromCompileResult(result404),
      CvmBootDescriptor.FromCompileResult(result501),
      CvmBootDescriptor.FromCompileResult(result502),
      CvmBootDescriptor.FromCompileResult(result503),
      CvmBootDescriptor.FromCompileResult(result504),
      CvmBootDescriptor.FromCompileResult(result601),
      CvmBootDescriptor.FromCompileResult(result602),
      CvmBootDescriptor.FromCompileResult(result603),
      CvmBootDescriptor.FromCompileResult(result604),
    ];
  }

  /// <summary>
  /// <b>REWORKED 2026-09-22, per Stefan directly: "stop this static madness. i want full dynamic
  /// bootstream and post-mortem analysis, based on all the nodes that have been configured by the
  /// checkbox, thus belong to the current project. no more static lists."</b> This is no longer a fixed
  /// field -- there is no hand-maintained roster left in this class at all. "Configured," for this
  /// purpose, is exactly <see cref="ViewModels.NodeViewModel.IsConfigured"/>'s own predicate, reproduced
  /// here directly against the model rather than the view model (<c>node.Enabled ||
  /// !string.IsNullOrWhiteSpace(node.SourceCode)</c>) -- the SAME "configured/yellow" checkbox state the
  /// GA144 window's own node grid already shows Stefan, now read live off <paramref name="chip"/> instead
  /// of cross-checked against a separately-maintained C# array that this session's own back-and-forth
  /// (405/505, then 201-205/102-105) proved painful to keep in sync by hand. <see cref="Node708Program"/>
  /// is always included regardless of its own checkbox state: it is the one node this whole mesh cannot
  /// exist without (the real, physical, unmirrored serial boot entry point -- there is no "boot stream"
  /// at all without a root to hang it from), so an unchecked 708 would otherwise make
  /// <see cref="ComputeLoadOrder"/> throw "not configured" for the one node that can never sensibly be
  /// left out; <see cref="Services.Ga144CvmHardwareInstaller.OpenAndBootMesh"/>'s own per-node source
  /// check still catches a genuinely blank node 708 (or any other configured-but-empty node) before
  /// anything is written to hardware, exactly as it already did.
  ///
  /// <see cref="CvmNodeMesh.StandaloneCoordinates"/> is UNCHANGED and still exists, but is no longer
  /// consulted anywhere in this class -- it now serves only its own, separate original purpose (the CVM
  /// Debugger's standalone/no-hardware Assemble-and-disassemble path,
  /// <see cref="ViewModels.CvmDebuggerViewModel.CompileStandaloneCvmNodes"/>, and the build-time
  /// <see cref="CvmPrimitiveTableExporter"/>), which has nothing to do with a live chip's own boot order
  /// or post-mortem capture and was not part of what Stefan asked to change here.
  /// </summary>
  public static IReadOnlyList<int> GetConfiguredCoordinates(Ga144ChipConfiguration chip)
  {
    ArgumentNullException.ThrowIfNull(chip);

    var coordinates = chip.Nodes
        .Where(node => node.Enabled || !string.IsNullOrWhiteSpace(node.SourceCode))
        .Select(node => node.Coordinate)
        .ToList();

    if (!coordinates.Contains(Node708Program.Coordinate))
    {
      coordinates.Add(Node708Program.Coordinate);
    }

    return coordinates;
  }

  /// <summary>
  /// This project's own node-coordinate convention is <c>row*100 + column</c> (row 0-7, column 0-17 --
  /// e.g. node 708 is row 7, column 8), the GA144's real physical grid, 4-connected, no diagonal
  /// adjacency. Returns <paramref name="coordinate"/>'s up-to-four physical neighbors that are
  /// themselves present in <paramref name="configured"/>, in ascending numeric order -- the fixed
  /// tie-break order <see cref="ComputeLoadOrder"/>'s own fill relies on for determinism (see that
  /// method's, and <see cref="BuildLoadOrder"/>'s own remarks).
  /// </summary>
  private static IEnumerable<int> GetPhysicalNeighbors(int coordinate, IReadOnlySet<int> configured)
  {
    int row = coordinate / 100;
    int column = coordinate % 100;

    int[] candidates =
    [
      (row - 1) * 100 + column, // one row down
      (row + 1) * 100 + column, // one row up
      row * 100 + (column - 1), // one column left
      row * 100 + (column + 1), // one column right
    ];

    return candidates.Where(configured.Contains).OrderBy(candidate => candidate);
  }

  /// <summary>
  /// Computes a leaves-first/root-last <see cref="CvmBootLoadStep"/> sequence by "filling" outward
  /// from <paramref name="rootCoordinate"/> over the physical grid (<see cref="GetPhysicalNeighbors"/>),
  /// breadth-first, restricted to <paramref name="configuredCoordinates"/> -- see
  /// <see cref="BuildLoadOrder"/>'s own remarks for the full rationale, the tie-break rule, and the
  /// specific, verified deviations this produces from the previous hand-written order. Throws if
  /// <paramref name="rootCoordinate"/> is itself not configured, or if any configured coordinate is not
  /// reachable from it -- this is the runtime enforcement of "all other configured nodes must be
  /// reachable from node 708."
  /// </summary>
  private static IReadOnlyList<CvmBootLoadStep> ComputeLoadOrder(
      IReadOnlyList<int> configuredCoordinates, int rootCoordinate)
  {
    var configured = new HashSet<int>(configuredCoordinates);
    if (!configured.Contains(rootCoordinate))
    {
      throw new InvalidOperationException(
          $"Node {rootCoordinate:000} (the intended boot root) is not itself in the configured node list.");
    }

    // Breadth-first fill from the root: each node's via-parent is whichever already-visited
    // configured node reaches it FIRST. Ties (a node with more than one still-unvisited configured
    // neighbor available at the same fill distance) are broken by GetPhysicalNeighbors' own
    // smallest-coordinate-first order, so the result is fully deterministic and reproducible.
    var viaByCoordinate = new Dictionary<int, int?> { [rootCoordinate] = null };
    var queue = new Queue<int>();
    queue.Enqueue(rootCoordinate);

    while (queue.Count > 0)
    {
      int current = queue.Dequeue();
      foreach (int neighbor in GetPhysicalNeighbors(current, configured))
      {
        if (!viaByCoordinate.ContainsKey(neighbor))
        {
          viaByCoordinate[neighbor] = current;
          queue.Enqueue(neighbor);
        }
      }
    }

    List<int> unreached = [.. configuredCoordinates.Where(coordinate => !viaByCoordinate.ContainsKey(coordinate))];
    if (unreached.Count > 0)
    {
      throw new InvalidOperationException(
          $"The following configured node(s) are not reachable from node {rootCoordinate:000} over " +
          "the physical grid, so no boot load order can be built for them: " +
          string.Join(", ", unreached.Select(coordinate => coordinate.ToString("000"))) + ".");
    }

    // Group children by their own via-parent (siblings sorted ascending, same determinism rule as the
    // fill above), then walk the resulting tree in POST-ORDER -- every child before its own
    // via-parent -- so the root, node 708, always comes out last with a null via, matching
    // CvmBootLoadStep's own leaves-first/root-last contract.
    var childrenByParent = new Dictionary<int, List<int>>();
    foreach (KeyValuePair<int, int?> pair in viaByCoordinate)
    {
      if (pair.Value is int parent)
      {
        if (!childrenByParent.TryGetValue(parent, out List<int>? siblings))
        {
          siblings = [];
          childrenByParent[parent] = siblings;
        }

        siblings.Add(pair.Key);
      }
    }

    foreach (List<int> siblings in childrenByParent.Values)
    {
      siblings.Sort();
    }

    var steps = new List<CvmBootLoadStep>();

    void VisitPostOrder(int node)
    {
      if (childrenByParent.TryGetValue(node, out List<int>? children))
      {
        foreach (int child in children)
        {
          VisitPostOrder(child);
        }
      }

      steps.Add(new CvmBootLoadStep(node, viaByCoordinate[node]));
    }

    VisitPostOrder(rootCoordinate);
    return steps;
  }

  /// <summary>
  /// <b>REWORKED AGAIN, 2026-09-22, later the same day, per Stefan directly: "stop this static madness.
  /// i want full dynamic bootstream and post-mortem analysis, based on all the nodes that have been
  /// configured by the checkbox, thus belong to the current project. no more static lists."</b> This
  /// method's signature changed to take <paramref name="chip"/>: the coordinate roster the fill below
  /// runs over is no longer any field on this class at all (the fixed <c>AllConfiguredCoordinates</c>
  /// this remarks block used to describe is gone outright) -- it is
  /// <see cref="GetConfiguredCoordinates"/>, computed FRESH from <paramref name="chip"/>'s own live
  /// per-node <c>Enabled</c>/<c>SourceCode</c> state every time this method is called. Whichever nodes
  /// Stefan has actually ticked (or given source to) in the Node Editor for THIS chip, right now, are
  /// exactly what the boot stream includes -- nothing more needs editing in this file the next time a
  /// node is added or removed from the project; see <see cref="GetConfiguredCoordinates"/>'s own remarks
  /// for the exact predicate and the one hardcoded exception (708 itself, unconditionally, since a boot
  /// stream cannot exist without its own root). Every mention of <c>AllConfiguredCoordinates</c> in the
  /// history below now means "whatever <see cref="GetConfiguredCoordinates"/> returned for the chip in
  /// question at the time" -- the FILL ALGORITHM ITSELF (breadth-first from 708, physical 4-connected
  /// grid adjacency, smallest-coordinate-first tie-break, post-order output) is COMPLETELY UNCHANGED by
  /// this rework, only where the coordinate list comes from.
  ///
  /// <b>REWORKED 2026-09-22, per Stefan directly: "in CvmBootStreamBuilder the boot stream is
  /// constructed now statically in 'BuildLoadOrder'. Can we change the 'BuildLoadOrder' so that it will
  /// use all configured nodes for the boot stream dynamically? the last node must be node 708. all
  /// other configured nodes must be reachable from node 708," followed, after this session verified by
  /// simulation that a plain physical-adjacency graph scan does NOT reproduce every previously-confirmed
  /// edge, by: "simply use a fill algorithm originating from node 708."</b> This method no longer
  /// returns a hand-written array -- it computes the load order at runtime with
  /// <see cref="ComputeLoadOrder"/>, a breadth-first "fill" outward from node 708 over the GA144's own
  /// physical 4-connected grid (<see cref="GetPhysicalNeighbors"/>), restricted to
  /// <see cref="GetConfiguredCoordinates"/>'s own result for <paramref name="chip"/> (every node this
  /// chip's project currently has ticked/enabled or given source to). Each node's <c>ViaNodeCoordinate</c>
  /// is whichever already-visited configured node the fill reached it FROM first; ties are broken
  /// smallest-coordinate-first, so the result is fully deterministic and reproducible from one run to
  /// the next. The fill enforces the stated invariant itself: if any configured coordinate is not
  /// reached from 708, <see cref="ComputeLoadOrder"/> throws rather than silently omitting it. The final
  /// sequence is the fill tree's own POST-ORDER walk (children before their own via-parent, 708 itself
  /// last with a null via), matching <see cref="CvmBootLoadStep"/>'s own leaves-first/root-last contract.
  ///
  /// <b>This closes a real, previously-flagged gap: nodes 405 and 505 are now part of the boot stream
  /// for the first time.</b> Both have real resident sources (<see cref="Node405Program"/>,
  /// <see cref="Node505Program"/>) and have been part of <see cref="CvmNodeMesh.StandaloneCoordinates"/>
  /// since 2026-09-09, but neither was ever compiled by <see cref="BuildDescriptors"/>, listed in this
  /// method's old hardcoded array, or given a <see cref="ReferenceSourceFor"/> fallback -- all three
  /// gaps are closed together in this same change (see <see cref="BuildDescriptors"/>'s own
  /// <c>result405</c>/<c>result505</c> steps and <see cref="ReferenceSourceFor"/>'s own new entries),
  /// since the fill now needs a real via-edge, a real compiled descriptor, and a real reference-source
  /// fallback for both, exactly like every other node here.
  ///
  /// <b>KNOWN, VERIFIED DEVIATION from the previous hand-written order -- flagged here rather than
  /// silently absorbed, because it changes which physical port five already-hardware-adjacent nodes are
  /// reached through.</b> Node 405 sits, on the physical grid, directly between node 406 (part of the
  /// main CVM dispatch tree) and node 404 (the FP pipeline's own "stage 8 rounding" node); node 505
  /// likewise sits directly between node 506 (the stack-frame node) and node 504 (the FP pipeline's
  /// "stage 7 classify" node) -- the exact "shortcut between two previously-separate mesh branches" this
  /// session's own working notes flagged before this method was reworked. Both 406 and 506 are reached
  /// by the fill far sooner than the FP pipeline's own long 407-&gt;307-&gt;306-&gt;305-&gt;304-&gt;404 (or
  /// -&gt;504) chain reaches the same neighborhood, so the fill -- exactly like a real flood fill would --
  /// ends up reaching 404 and 504 THROUGH the newly-added 405/505 shortcut rather than through their
  /// previously-confirmed FP-pipeline parents. Verified by simulating this exact algorithm against the
  /// coordinate roster this project's chip had at the time (what <see cref="GetConfiguredCoordinates"/>
  /// now computes live): of the 33 non-root edges, 28 come out IDENTICAL to the
  /// previous hand-written array (including the entire 407/307/306/308 branch, the 508/509/510/511
  /// branch, and the 301-&gt;302-&gt;303-&gt;304-&gt;305-&gt;306 pipeline spine itself), and exactly FIVE
  /// change:
  /// <list type="bullet">
  /// <item>404 now loads via 405 (previously via 304)</item>
  /// <item>504 now loads via 505 (previously via 404)</item>
  /// <item>601 now loads via 501 (previously via 602)</item>
  /// <item>602 now loads via 502 (previously via 603)</item>
  /// <item>603 now loads via 503 (previously via 604)</item>
  /// </list>
  /// Every one of these five follows directly or indirectly from the same two new shortcuts -- no OTHER
  /// previously-confirmed relay assignment changes. <b>None of these five new via-edges, nor 405's/505's
  /// own two new edges, has been confirmed on real hardware</b> -- Stefan's own hardware confirmations to
  /// date only ever covered the previous, hand-written edges. This is exactly what Stefan's own
  /// instruction asked for, so it is implemented as asked rather than worked around -- but it is called
  /// out here plainly, the same way this method's own history below flags every other not-yet-hardware-
  /// tested edge, so a real-hardware re-verification pass covers these five specifically (and 405's/505's
  /// own loads) before relying on them.
  ///
  /// <b>EXTENDED, same day, per Stefan directly: "CVM also includes nodes 201..205 and 102..105. I want
  /// that these nodes are also included in the boot stream."</b> These nine were added to
  /// <see cref="CvmNodeMesh.StandaloneCoordinates"/> (see that class's own remarks on this addition --
  /// unlike every other coordinate there, none of these nine has a <c>NodeXxxProgram.cs</c> reference
  /// source; they rely entirely on their own live project source) -- and, as of the SAME day's later
  /// "stop this static madness" rework below, this addition is moot as far as THIS class is concerned:
  /// <see cref="GetConfiguredCoordinates"/> no longer consults
  /// <see cref="CvmNodeMesh.StandaloneCoordinates"/> at all, so whether these nine load is governed purely
  /// by their own live "configured" checkbox state in the project, exactly like every other node. Re-
  /// verified at the time by simulating the fill again against the resulting 43-coordinate roster (the
  /// full <see cref="CvmNodeMesh.StandaloneCoordinates"/> set, all configured): all nine attach as one
  /// single new leaf cluster hanging off node 305 (one hop
  /// further out -- 305 -&gt; 205, then 205 -&gt; 204 -&gt; 203 -&gt; 202 -&gt; 201 on one branch and
  /// 205 -&gt; 105 -&gt; 104 -&gt; 103 -&gt; 102 on the other), and changes NONE of the previously-computed
  /// edges among the other 34 nodes -- this new cluster only ever touches the rest of the mesh at node
  /// 305, so it cannot create the kind of cross-branch shortcut 405/505 did above. None of these nine
  /// nodes' own loads has been hardware-tested.
  ///
  /// <b>Everything below this point is the ORIGINAL doc comment for the hand-written array this method
  /// used to return.</b> It is preserved verbatim, unedited, because it remains the authoritative
  /// historical record of which physical port each of these edges was confirmed (or not yet confirmed)
  /// to use, and that hardware-fact history did not change just because the array itself is now computed
  /// rather than typed out by hand -- the 28 edges the fill reproduces exactly still carry whatever
  /// confirmation status is described for them below; only the five edges (plus 405's/505's own two)
  /// flagged immediately above are new territory this history does not (and could not) already cover.
  ///
  /// CVM2's boot LOAD order: leaves-first/root-last, with a branch at 507. Originally just
  /// 507 -&gt; 607 -&gt; 707 -&gt; 708 (2026-09-01, inferred from the same reasoning Stefan confirmed for
  /// CVM1's branching tree applied to CVM2's then-simpler non-branching case). Extended 2026-09-02 with
  /// node 407 -- the long-call/long-jump helper -- as a new leaf reached VIA 507
  /// (<c>new CvmBootLoadStep(407, 507)</c>): 407 is one hop further out than 507 (host&lt;-&gt;708&lt;-&gt;
  /// 707&lt;-&gt;607&lt;-&gt;507&lt;-&gt;407), so it must load FIRST, before 507 itself starts running its
  /// own compiled program and stops passively relaying. Extended again 2026-09-04 with node 506 -- the
  /// stack-frame node -- and node 508 -- the globals-access node -- as a SECOND and THIRD leaf ALSO
  /// reached via 507 (<c>new CvmBootLoadStep(506, 507)</c>, <c>new CvmBootLoadStep(508, 507)</c>),
  /// siblings of 407 rather than a further link past it: all three hang directly off 507's own
  /// <c>m/main</c> dispatch (407 via 507's down port, 506 via 507's right port, 508 via 507's left
  /// port), so all three must load before 507 itself runs. Each via-node's local port name toward its
  /// target (407: "down" both sides, confirmed by Stefan -- see Node407Program's own remarks; 506:
  /// "right" both sides, matching node 507's own dispatch cascade's <c>r---</c> for the "1001" prefix --
  /// see Node506Program's own remarks; 508: "left" both sides, matching node 507's own dispatch
  /// cascade's <c>--l-</c> for the "101?" prefix -- see Node508Program's own remarks) is, in every case,
  /// independently confirmed by <see cref="Models.KrakenConfiguration.PortAddress"/>'s own
  /// geographic-adjacency table, and resolved generically from <c>parentOf</c> by
  /// <see cref="Services.Ga144CvmHardwareInstaller"/> -- which needed NO code changes to support 506 or
  /// 508 as further children of the same relay parent (507): its own <c>focused</c>-set logic re-points
  /// 507's B port at whichever child is currently loading, unconditionally, on every step, and only
  /// "focuses" 507 itself once, the first time it appears in any load step's ancestor chain (i.e. during
  /// the 407 step, so the subsequent 506 and 508 steps just re-point, no re-focus needed).
  ///
  /// <b>Extended again 2026-09-05 with node 509 -- the unary-arithmetic node -- reached via 508, NOT
  /// 507 directly (<c>new CvmBootLoadStep(509, 508)</c>).</b> Unlike 407/506/508, 509 is not a sibling
  /// of theirs: it hangs off node 508's OWN <c>g/main</c> dispatch (508's right port), one hop further
  /// out than 508 itself, making CVM2's mesh four relay hops deep for the first time
  /// (708&lt;-&gt;707&lt;-&gt;607&lt;-&gt;507&lt;-&gt;508&lt;-&gt;509). It must therefore load BEFORE
  /// 508's own step, exactly the same "leaf loads before its immediate relay parent" rule 407/506/508
  /// already follow relative to 507, just one level deeper -- placed right after 506 and before 508 in
  /// the list below. 508's own local port name toward 509 ("right" both sides, matching node 508's own
  /// dispatch cascade's <c>r---</c> for the "1011" prefix -- see Node509Program's own remarks) is
  /// likewise confirmed by <see cref="Models.KrakenConfiguration.PortAddress"/>. No code changes were
  /// needed in <see cref="Services.Ga144CvmHardwareInstaller"/> for this extra level either --
  /// <c>AncestorChain</c> already recurses through <c>parentOf</c> however deep the chain runs, so 509's
  /// own four-hop chain (707/607/507/508) just falls out of the same loop, with 508 itself getting
  /// "focused" for the first time during the 509 step rather than during its own step.
  ///
  /// <b>Extended again, same day, with node 406 -- the binary-arithmetic node -- reached via 407, NOT
  /// 507 directly (<c>new CvmBootLoadStep(406, 407)</c>).</b> The SECOND four-relay-hops-deep branch:
  /// node 406 hangs off node 407's OWN <c>n/main</c> dispatch (407's right port), one hop further out
  /// than 407 itself, exactly mirroring 509's own relationship to 508 above. It must therefore load
  /// BEFORE 407's own step, placed FIRST in the list below (407's own step, in turn, must still precede
  /// 506/508's own steps and 507's own step, unchanged). 407's own local port name toward 406 ("right"
  /// both sides, matching node 407's own dispatch cascade's <c>r---</c> for the "1110" prefix -- see
  /// Node406Program's own remarks) is likewise confirmed by
  /// <see cref="Models.KrakenConfiguration.PortAddress"/>. No code changes were needed in
  /// <see cref="Services.Ga144CvmHardwareInstaller"/> for this second extra level either -- 406's own
  /// four-hop chain (708/707/607/507/407) falls out of the same generic <c>AncestorChain</c> loop as
  /// 509's, with 407 itself getting "focused" for the first time during the 406 step rather than during
  /// its own step, and 507 getting "focused" for the first time during the 406 step too, since 406's own
  /// ancestor chain reaches all the way back to 507.
  ///
  /// <b>Extended again, 2026-09-06, with node 408 -- the comparison node -- reached via 407, NOT 507
  /// directly (<c>new CvmBootLoadStep(408, 407)</c>).</b> A SIBLING of 406 under 407 (both are a further
  /// hop past 407, not past each other): node 408 hangs off node 407's OWN <c>n/main</c> dispatch (407's
  /// LEFT port, previously unanswered -- see Node407Program's own remarks), the SAME relationship 406 has
  /// to 407 via its own RIGHT port. It must therefore load BEFORE 407's own step too, placed right after
  /// 406 in the list below (relative order between 406 and 408 does not matter -- both are independent
  /// children of the same still-passively-relaying 407 -- only that both precede 407's own step). 407's
  /// own local port name toward 408 ("left" both sides, matching node 407's own dispatch cascade's
  /// <c>--l-</c> for the "1111" prefix -- see Node408Program's own remarks) is likewise expected to match
  /// <see cref="Models.KrakenConfiguration.PortAddress"/>'s own geographic-adjacency table, the same way
  /// every other link here has. No code changes were needed in
  /// <see cref="Services.Ga144CvmHardwareInstaller"/> for this either -- 408's own four-hop chain
  /// (708/707/607/507/407), identical in shape to 406's own, falls out of the same generic
  /// <c>AncestorChain</c> loop, with 407 and 507 both already getting "focused" during whichever of the
  /// 406/408 steps happens to run first.
  ///
  /// <b>Extended again, same day, with node 307 -- "VM ternary main" -- reached via 407, NOT 507
  /// directly (<c>new CvmBootLoadStep(307, 407)</c>), and node 306 -- the 4x 32-bit-address-register
  /// node -- reached via 307 (<c>new CvmBootLoadStep(306, 307)</c>), one hop further still.</b> Node 307
  /// fills node 407's own long-FLAGGED "1101" branch (see Node407Program's own remarks), making CVM2's
  /// mesh FIVE relay hops deep for the first time on this branch (708/707/607/507/407/307/306). Node 307
  /// must therefore load BEFORE 407's own step, alongside 406 and 408 (relative order among the three
  /// does not matter -- all are independent children of the still-passively-relaying 407); node 306 must
  /// load before node 307's OWN step, the same "leaf loads before its immediate relay parent" rule 509
  /// already follows relative to 508. No code changes were needed in
  /// <see cref="Services.Ga144CvmHardwareInstaller"/> for either extra hop -- 306's own five-hop ancestor
  /// chain (708/707/607/507/407/307) falls out of the same generic <c>AncestorChain</c> recursion as
  /// every earlier addition, with 307 and 407 both getting "focused" for the first time during the 306
  /// step if it happens to run before 307's/407's own steps (their relative order among 406/408/307 is
  /// otherwise unconstrained).
  ///
  /// <b>BUG, introduced 2026-09-06, FOUND AND FIXED 2026-09-08: the array below had this backwards.</b>
  /// It listed <c>new CvmBootLoadStep(307, 407)</c> BEFORE <c>new CvmBootLoadStep(306, 307)</c> --
  /// exactly the one place in this whole list that violated the "child loads before its relay parent's
  /// own step" rule every other branch here follows (406/408 before 407; 511/510/509 before 508). Since
  /// a node's own load step ends with <see cref="Services.Ga144CvmHardwareInstaller"/>'s own
  /// <c>BuildProgramLeaf</c> appending a bare jump straight into that node's real compiled entry point,
  /// running 307's own step before 306's meant node 307 had already jumped into <c>k/main</c> -- no
  /// longer a passive ROM-level relay -- by the time the installer tried to route 306's program "via
  /// 307". The frames meant for 306 landed on a node that was instead sitting at <c>k/main</c>'s own
  /// <c>@b</c> read, waiting on a real command from 407: at best silently dropped, at worst consumed as
  /// a bogus dispatch command, leaving 307 in an undefined state for good. Confirmed via Stefan's own
  /// before/after <c>CvmBootStreamBuilder.cs</c> bisection: identical file except for this node 306/307
  /// addition, working before, a total "0 transactions" hardware timeout after. Fixed by swapping the
  /// two lines so 306 loads BEFORE 307's own step, matching what this very doc comment already said the
  /// intended order was.
  ///
  /// <b>Extended again, 2026-09-07, with node 510 -- "extended arithmetic" -- reached via 509, NOT 508
  /// directly (<c>new CvmBootLoadStep(510, 509)</c>), and node 511 -- the 32-register register-file
  /// node -- reached via 510 (<c>new CvmBootLoadStep(511, 510)</c>), one hop further still.</b> Node 510
  /// fills node 509's own previously-open LEFT-port branch (see Node509Program's own remarks), making
  /// CVM2's mesh SIX relay hops deep for the first time on this branch
  /// (708/707/607/507/508/509/510/511). Node 510 must therefore load BEFORE 509's own step, the same
  /// "leaf loads before its immediate relay parent" rule 509 already follows relative to 508; node 511
  /// must load before node 510's OWN step, one level deeper still. No code changes were needed in
  /// <see cref="Services.Ga144CvmHardwareInstaller"/> for either extra hop -- 511's own six-hop ancestor
  /// chain (708/707/607/507/508/509/510) falls out of the same generic <c>AncestorChain</c> recursion as
  /// every earlier addition, with 510 and 509 both getting "focused" for the first time during the 511
  /// step if it happens to run before 510's/509's own steps.
  ///
  /// <b>CONFIRMED ON REAL HARDWARE (2026-09-02) for the ORIGINAL 407 step (without 406/408/307/306/510/511
  /// as further hops).</b> The load order through node 407 was installed and run on a real EVB: a test program's
  /// <c>lcall</c>/<c>'ret</c> round-tripped correctly through node 407 (see Node407Program's own remarks
  /// for the transaction log), which could only happen if every hop's relay/focus/port-write sequence,
  /// all the way out to 407, was correct. <b>The NEW 406, 408, 506, 508, 509, 307, 306, 510, and 511
  /// steps are NOT yet real-hardware-tested</b> -- all nine follow the same generic relay mechanism the
  /// 407 step already validated, but none has itself been confirmed by a transaction log the way 407
  /// was, and node 307's own source is not even confirmed to COMPILE yet (see Node307Program's own
  /// remarks) -- this load-order entry describes the intended MESH SHAPE regardless, which is
  /// independent of whether any particular node's current source happens to compile today. The 306/307
  /// pair specifically went from "reordering bug silently breaking the whole mesh" (see the remarks
  /// above) to "reordering bug fixed, still awaiting its own first real-hardware run" on 2026-09-08 --
  /// not yet promoted to "confirmed" until Stefan reports a clean install/test after this fix.
  ///
  /// <b>RENUMBERED 2026-09-15: "node 306" throughout the history above is physical node 308.</b> Stefan:
  /// "node 306 and 308 have swapped roles" -- the 32-bit address-register node this whole load-order
  /// story is about (the 306/307 ordering bug, its fix, its real-hardware status) has NOT moved in the
  /// mesh -- it is still one relay hop past node 307, loading before 307's own step for the exact same
  /// reason described above -- only its coordinate label has changed, from 306 to 308. The step below
  /// reads <c>new CvmBootLoadStep(308, 307)</c> accordingly. Physical node 408 (the pre-existing
  /// "comparison node" step directly above it, unrelated to this renumbering) keeps its own separate
  /// coordinate; the two are not to be confused despite the similar digits. Physical node 306 now names
  /// an unrelated floating-point register node (see <see cref="Node306Program"/>), a SIBLING of node 308
  /// under node 307 (both are one relay hop past 307, reached via its RIGHT and LEFT ports respectively --
  /// relative order between the two does not matter, only that both precede node 307's own step).
  ///
  /// <b>Node 306 ADDED to this load order 2026-09-15, the same day 'fpop was wired.</b> It was left out
  /// above (through the swap) because it had no wired mnemonic depending on a live compile of it; now
  /// that <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointPopMnemonic"/> does, it needs a
  /// real compiled program on real hardware too, same as every other node in this list. UPDATED
  /// 2026-09-16: node 306's binary and constant-lookup categories are now ALSO wired (<c>fadd</c>/
  /// <c>fsub</c>/<c>fmin</c>/<c>fmax</c>/<c>fmul</c>/<c>fdiv</c>/<c>fln2</c>/<c>filn2</c>/<c>fpi2</c>/
  /// <c>f2pi</c>, both fully self-describing -- see <see cref="Cvm.Node306Program"/>'s own remarks) --
  /// unlike 'fpop, neither needs a live compile of node 306 to resolve, but node 306 still needs to be
  /// present and loaded on real hardware for these opcodes to mean anything once executed.
  ///
  /// <b>The ENTIRE 17-node floating-point pipeline's own internal relay topology is now CONFIRMED,
  /// 2026-09-16, per Stefan directly, verbatim:</b>
  /// <code>
  ///   306-&gt;305-&gt;304-&gt;303-&gt;302-&gt;301
  ///   304-&gt;404-&gt;403-&gt;402-&gt;401
  ///   404-&gt;504-&gt;503-&gt;502-&gt;501
  ///   504-&gt;604-&gt;603-&gt;602-&gt;601
  /// </code>
  /// Read as "left relays to right" (the SAME direction as every <c>CvmBootLoadStep(child, via)</c> pair
  /// elsewhere in this method, e.g. <c>(406, 407)</c> for "406 via 407"), this is a branching tree, not
  /// four independent chains: 304 has TWO children -- 303 (heading a simple 302-&gt;301 tail) AND 404;
  /// 404 in turn has two children -- 403 (heading a simple 402-&gt;401 tail) AND 504; 504 in turn has two
  /// children -- 503 (heading a simple 502-&gt;501 tail) AND 604; 604 has one child, 603 (heading a simple
  /// 602-&gt;601 tail). This is BOOT-TIME PORT-RELAY ADJACENCY only -- a separate fact from the COMPILE-
  /// order "# N import" chains <see cref="BuildDescriptors"/> already uses (401 imports nothing extra
  /// beyond 402's own import of it, etc.), which remain unaffected.
  ///
  /// <b>IMPORTANT, per Stefan directly (2026-09-16), on what this topology does NOT establish:</b> "the
  /// order in which nodes interact is independent of the order they are loaded in the boot stream, because
  /// once a node is loaded and started it is waiting for a stimulus, but it receives this stimulus only
  /// after all nodes have been booted. so the boot stream need not reflect the processing stream." This
  /// confirmed relay chain answers ONLY "which port does node X's boot frame travel through," not "which
  /// node computes first" or "which node sends its result to which next" once the pipeline is actually
  /// running. The earlier, separate, still-speculative per-node "stage 1 unpack ... stage 8 round" /
  /// "fanning out at 303 into three parallel control chains" narrative in <see cref="Cvm.CvmNodeMesh"/>'s
  /// own remarks was a claim about that different, RUNTIME processing order -- it is neither confirmed nor
  /// refuted by the boot-relay topology above, and this docstring no longer claims otherwise.
  ///
  /// Per <see cref="Cvm.CvmBootLoadStep"/>'s own remarks ("leaves first / root last", a post-order walk:
  /// every child finishes loading and is left running its own real program before its parent gives up its
  /// relay role), the 17 steps below are ordered so each subtree fully completes before the node relaying
  /// it takes on its own program -- 301 first (the deepest leaf), 305 last (306's own only child, so 306's
  /// own pre-existing step below can stay exactly where it already was, now correctly preceded by this
  /// entire subtree instead of by nothing). This load ORDER is purely administrative (per Stefan's own
  /// note above), unrelated to the pipeline's own eventual runtime processing order.
  /// </summary>
  public static IReadOnlyList<CvmBootLoadStep> BuildLoadOrder(Ga144ChipConfiguration chip) =>
      ComputeLoadOrder(GetConfiguredCoordinates(chip), rootCoordinate: Node708Program.Coordinate);

  /// <summary>
  /// The fixed reference source for one CVM2 node, keyed by coordinate -- the SAME strings
  /// <see cref="BuildDescriptors"/> itself compiles, exposed here so real hardware/dry-run callers
  /// (<see cref="Services.Ga144CvmHardwareInstaller"/>, <see cref="ViewModels.ChipViewModel"/>) can use
  /// them as a FALLBACK, never as the primary source. Per Stefan (2026-09-01): "I hope for the CVM2
  /// boot stream you take the nodes code in the project and not the NodeXxxProgram code, which should
  /// only be used if no code in the project is defined" -- so a caller must always try
  /// <c>chip.GetNode(coordinate).SourceCode</c> first and reach for this only when that is blank.
  /// Returns null for any coordinate with no CVM2 reference source of its own.
  /// </summary>
  public static string? ReferenceSourceFor(int coordinate) => coordinate switch
  {
    Node407Program.Coordinate => Node407Program.Source,
    Node406Program.Coordinate => Node406Program.Source,
    Node405Program.Coordinate => Node405Program.Source,
    Node408Program.Coordinate => Node408Program.Source,
    Node307Program.Coordinate => Node307Program.Source,
    Node306Program.Coordinate => Node306Program.Source,
    Node308Program.Coordinate => Node308Program.Source,
    Node506Program.Coordinate => Node506Program.Source,
    Node505Program.Coordinate => Node505Program.Source,
    Node508Program.Coordinate => Node508Program.Source,
    Node509Program.Coordinate => Node509Program.Source,
    Node510Program.Coordinate => Node510Program.Source,
    Node511Program.Coordinate => Node511Program.Source,
    Node507Program.Coordinate => Node507Program.Source,
    Node607Program.Coordinate => Node607Program.Source,
    Node707Program.Coordinate => Node707Program.Source,
    Node708Program.Coordinate => Node708Program.Source,
    Node305Program.Coordinate => Node305Program.Source,
    Node304Program.Coordinate => Node304Program.Source,
    Node303Program.Coordinate => Node303Program.Source,
    Node302Program.Coordinate => Node302Program.Source,
    Node301Program.Coordinate => Node301Program.Source,
    Node401Program.Coordinate => Node401Program.Source,
    Node402Program.Coordinate => Node402Program.Source,
    Node403Program.Coordinate => Node403Program.Source,
    Node404Program.Coordinate => Node404Program.Source,
    Node501Program.Coordinate => Node501Program.Source,
    Node502Program.Coordinate => Node502Program.Source,
    Node503Program.Coordinate => Node503Program.Source,
    Node504Program.Coordinate => Node504Program.Source,
    Node601Program.Coordinate => Node601Program.Source,
    Node602Program.Coordinate => Node602Program.Source,
    Node603Program.Coordinate => Node603Program.Source,
    Node604Program.Coordinate => Node604Program.Source,
    _ => null,
  };

  /// <summary>
  /// Pairs <see cref="BuildLoadOrder"/>'s sequence with each step's compiled <see cref="CvmBootDescriptor"/>
  /// from <see cref="BuildDescriptors"/>. Previously every step resolved to a real descriptor (all ten
  /// CVM2 nodes compiled); as of 2026-09-06's node 307/306 addition, <see cref="BuildDescriptors"/> is
  /// expected to THROW before returning anything at all, since node 307's own source is not yet
  /// confirmed to compile (see that method's own remarks and <see cref="Node307Program"/>'s) -- so this
  /// method currently propagates that same exception rather than returning a plan with a null
  /// descriptor for any step.
  ///
  /// <b>REWORKED 2026-09-22:</b> this method now takes <paramref name="chip"/> and threads it straight
  /// through to <see cref="BuildLoadOrder"/>, for the same "no more static lists" reason described on
  /// <see cref="GetConfiguredCoordinates"/>'s and <see cref="BuildLoadOrder"/>'s own remarks. This method
  /// has no callers outside this class today (it is this session's own throwaway-harness/reference-
  /// verification tool, per <see cref="BuildDescriptors"/>'s own remarks), so the signature change is
  /// free to make without any other call site to update.
  /// </summary>
  public static IReadOnlyList<(CvmBootLoadStep Step, CvmBootDescriptor? Descriptor)> BuildLoadPlan(Ga144ChipConfiguration chip)
  {
    Dictionary<int, CvmBootDescriptor> descriptorsByCoordinate =
        BuildDescriptors().ToDictionary(descriptor => descriptor.NodeCoordinate);

    return BuildLoadOrder(chip)
        .Select(step => (
            step,
            descriptorsByCoordinate.TryGetValue(step.NodeCoordinate, out CvmBootDescriptor? descriptor)
                ? descriptor
                : null))
        .ToList();
  }

  private static F18CompileResult Compile(F18Compiler compiler, string source, F18CompilerOptions options) =>
      compiler.Compile(source, options);

  // Compiles node 708's real factory ROM (Node708Rom -- this project's own byte-for-byte copy
  // of data/ga144-rom.yaml's node 708 entry, "macro rom_async_boot"), including the same
  // predefined 'await' symbol injection F18NodeCompilationService.CompileRom uses for every
  // node's ROM compile.
  private static F18CompileResult CompileNode708Rom(F18Compiler compiler)
  {
    var predefinedSymbols = new Dictionary<string, F18ExportedSymbol>(StringComparer.OrdinalIgnoreCase)
    {
      ["await"] = new F18ExportedSymbol(
          "await",
          F18AwaitAddresses.ForNode(Node708Program.Coordinate),
          F18ExportKind.Word,
          Node708Program.Coordinate,
          F18MemorySpace.Rom),
    };

    var options = new F18CompilerOptions
    {
      MemorySpace = F18MemorySpace.Rom,
      NodeCoordinate = Node708Program.Coordinate,
      MemoryBaseAddress = 0x080,
      MemoryWordCount = 64,
      IncludeCommonRomWords = false,
      PredefinedSymbols = predefinedSymbols,
      MacroResolver = Node708Rom.ResolveSystemMacro,
      MacroLookupScope = F18MacroLookupScope.SystemOnly,
    };

    return compiler.Compile(Node708Rom.RomAsyncBootSource, options);
  }

  // Pairs a node's RAM compile with its OWN already-compiled ROM's exports -- the same-node
  // ROM-then-RAM pattern F18NodeCompilationService.CompileRam uses -- as opposed to a cross-node
  // import. Node 708 needs this one: it has no '# NNN import' directive of its own, but its RAM
  // source calls words (18ibits, delay) that live in its own real ROM, not in the compiler's built-in
  // common ROM words. (607 and 507 layer no real custom ROM under their RAM, so neither needs this.)
  private static F18CompilerOptions ImportingRom(int coordinate, F18CompileResult ownRom) => new()
  {
    MemorySpace = F18MemorySpace.Ram,
    NodeCoordinate = coordinate,
    MemoryBaseAddress = 0x000,
    MemoryWordCount = 64,
    IncludeCommonRomWords = true,
    PredefinedConstants = ownRom.Constants,
    PredefinedSymbols = ownRom.Symbols,
  };

  // 707's '# 708 import' is an ordinary cross-node RAM import, EXCEPT that 708's own exports span
  // both its custom ROM and its RAM (unlike 607/507 above, neither of which layers real custom ROM
  // under their RAM). Reproduces F18NodeCompilationService.ResolveRamImport's exact combine-then-import
  // sequence for that one case: merge 708's ROM exports with its RAM exports (CombineExports,
  // mirroring that service's private TryCombineExports), then hand the merged set to 707 as its
  // resolved import.
  private static F18CompilerOptions ImportingCombinedRam(int coordinate, F18CompileResult rom, F18CompileResult ram) => new()
  {
    MemorySpace = F18MemorySpace.Ram,
    NodeCoordinate = coordinate,
    MemoryBaseAddress = 0x000,
    MemoryWordCount = 64,
    IncludeCommonRomWords = true,
    ImportResolver = importedCoordinate => importedCoordinate == ram.NodeCoordinate
        ? CombineExports(ram.NodeCoordinate, rom.Exports, ram.Exports)
        : F18ImportResolution.Failure($"node {importedCoordinate} not available"),
  };

  // Merges a node's ROM export set and RAM export set into one, RAM names taking precedence on a
  // name that (unexpectedly) appears in both -- same rule and same failure-on-genuine-conflict
  // behavior as F18NodeCompilationService's private TryCombineExports, which this reproduces for
  // node 708's combined (ROM + RAM) export set.
  private static F18ImportResolution CombineExports(int coordinate, F18ExportSet rom, F18ExportSet ram)
  {
    var constants = new Dictionary<string, int>(rom.Constants, StringComparer.OrdinalIgnoreCase);
    var symbols = new Dictionary<string, F18ExportedSymbol>(rom.Symbols, StringComparer.OrdinalIgnoreCase);

    foreach (KeyValuePair<string, int> pair in ram.Constants)
    {
      if (constants.ContainsKey(pair.Key) || symbols.ContainsKey(pair.Key))
      {
        return F18ImportResolution.Failure($"Node {coordinate:000} exports '{pair.Key}' from both ROM and RAM.");
      }

      constants[pair.Key] = pair.Value;
    }

    foreach (KeyValuePair<string, F18ExportedSymbol> pair in ram.Symbols)
    {
      if (constants.ContainsKey(pair.Key) || symbols.ContainsKey(pair.Key))
      {
        return F18ImportResolution.Failure($"Node {coordinate:000} exports '{pair.Key}' from both ROM and RAM.");
      }

      symbols[pair.Key] = pair.Value;
    }

    return F18ImportResolution.FromExports(new F18ExportSet
    {
      NodeCoordinate = coordinate,
      Constants = constants,
      Symbols = symbols
    });
  }

  private static void ThrowIfFailed(F18CompileResult result)
  {
    if (result.Success)
    {
      return;
    }

    string diagnostics = string.Join(
        Environment.NewLine,
        result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    throw new InvalidOperationException(
        $"Node {result.NodeCoordinate:000} failed to compile while building the CVM boot stream:{Environment.NewLine}{diagnostics}");
  }
}