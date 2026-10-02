using System.Globalization;
using Ga144.Cvm.Toolchain;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Cvm;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// <b>SECOND PASS, 2026-09-09.</b> Stefan supplied a NEW zip and a NEW <c>workspace.yaml</c> project
/// export mid-way through the opcode/assembler-vs-node reconciliation audit already described below
/// ("there are 'ugt' opcodes in the nodes ... use this new zip file and forget any reference to the
/// older zip file"), and then confirmed explicitly, when asked, that <c>workspace.yaml</c> -- not
/// whatever a checked-in <c>Node*Program.cs</c> reference file claimed -- is ground truth for what each
/// CVM2 node's F18 source actually is today. Re-running the audit against that authoritative export
/// found a substantially larger, restructured CVM2 mesh than the remarks below describe: three brand-new
/// nodes (308 "VM 32 arithmetic," 405 "multiword arithmetic," 505 "frame2"); node 306 completely
/// rewritten around a new tick-prefixed word family; node 408 missing an entire unsigned-comparison
/// sub-family (the exact <c>ugt</c> gap Stefan flagged); node 509's own <c>lit</c> narrowed and re-tagged;
/// node 510 revealed to define six ops the prior sync had missed entirely; and node 511's own <c>rld</c>/
/// <c>rst</c>/<c>rpop</c>/<c>rpush</c> -- retired by an EARLIER pass of this same audit, working from an
/// intermediate node 511 re-sync -- restored as live opcodes once workspace.yaml's own simpler <c>r/main</c>
/// shape is treated as ground truth. See each new/changed mnemonic's own remarks below and in
/// <see cref="CvmInstructionSet"/> for the specifics; the remarks immediately following this paragraph
/// describe the FIRST pass and are otherwise still accurate for everything they cover.
///
/// The CVM's own small assembly language: Stefan's mnemonics (<c>nop</c>, <c>pushlit &lt;data&gt;</c>,
/// <c>push</c>, <c>pop</c>, <c>ret</c>, <c>halt</c> -- CVM2's node 507 "local execute" primitives)
/// layered on top of a tagged wire-level opcode convention (opcode = tag | wordAddress) -- see
/// <see cref="Node508TagBits"/>/<see cref="Node507Cvm2LocalExecuteTagBits"/>'s own remarks. (<c>call</c>,
/// <c>br</c>, <c>cbr</c>, and node 606's frame-pointer ops (<c>enter</c>,
/// <c>stl</c>, <c>stp</c>, <c>ldl</c>, <c>ldp</c>) are the exceptions -- see this
/// class's own remarks on why they aren't part of this tagged-opcode layer. <c>slit</c> and
/// <c>lal</c>/<c>lap</c> were RETIRED 2026-09-09, and their own mnemonic/tag constants later deleted
/// outright too -- see <see cref="CvmInstructionSet"/>'s own remarks on the 2026-09-09 CVM1-opcode
/// purge; <c>adjust</c>, the family's ninth member, was deleted outright 2026-09-10 the same way, per
/// Stefan's own instruction that it no longer exists -- see <see cref="CvmInstructionSet"/>'s own
/// remarks on that removal.)
///
/// <b>CVM2 (2026-09-01).</b> Stefan is rewriting the whole CVM around new, differently-numbered nodes
/// and a more sophisticated inter-node communication scheme; CVM1's nodes are not used in CVM2 at all.
/// <c>nop</c>/<c>pushlit</c>/<c>push</c>/<c>pop</c>/<c>ret</c>/<c>halt</c> are the six CVM1 mnemonics
/// CVM2's own node 507 (the ENTIRE CPU) happens to still implement, under matching tick-labels ('nop,
/// 'plit, 'push, 'pop, 'ret, 'halt) -- per Stefan's own "only update existing opcodes where possible"
/// rule, these six were repointed to node 507's implementation
/// (<see cref="Node507Cvm2LocalExecuteTagBits"/>) rather than added as new entries. CVM2 node 507's other
/// four tick-labeled words ('tjmp, 'jump, 'xs, 'xp) had no existing CVM1 mnemonic; 'tjmp was wired in
/// 2026-09-09 (Stefan: "'tjmp is in node 507") as <see cref="CvmInstructionSet.TableJumpMnemonic"/>, the
/// same way the six repoints above are -- 'jump/'xs/'xp remain deliberately NOT wired into this file.
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
/// tag -- see that constant's own remarks for the full derivation. Extended 2026-09-06 with THREE more
/// tick-prefixed words on the SAME node -- <c>'clbr</c>/<c>'lbr</c>/<c>'cljmp</c> (long conditional
/// branch, long branch, long conditional jump) -- all reached through node 407's own SAME "1100" dispatch
/// branch as <c>'lcall</c>/<c>'ljmp</c>, so all five would have shared <see cref="Node407LongCallTagBits"/>,
/// distinguished only by their own address on node 407. Narrowed the SAME day, before either ever reached
/// real hardware: Stefan removed <c>'clbr</c>/<c>'cljmp</c> from node 407's own source ("I remove clbr and
/// cljmp from node 407. remove it also from the CVM assembler and disassembler"), so only <c>'lbr</c>
/// (long branch, unconditional) actually gets an entry below -- see
/// <see cref="CvmInstructionSet.LongBranchMnemonic"/>'s and <see cref="Node407Program"/>'s own remarks for
/// the full derivation, including how this revision also hoisted the "fetch the trailing operand word"
/// step out of <c>'lcall</c>/<c>'ljmp</c>'s own bodies and into <c>n/main</c>'s own shared dispatch tail
/// (an internal refactor that leaves the CVM-level tag/opcode scheme completely unchanged).
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
/// <b><c>Node508Program.cs</c> is ALSO back -- another BRAND NEW, unrelated CVM2 file (2026-09-04).</b>
/// Node 508 was an ignored placeholder from 2026-09-01 ("node 508 must be ignored for now") until
/// Stefan supplied its real role and source: the globals-access node, a third sibling leaf of 507
/// alongside 407 and 506, reached from node 507's own <c>m/main</c> dispatch via its LEFT port
/// (<c>--l-</c>). Per Stefan's own tick-naming rule, only <c>ldg</c>/<c>stg</c> (node 508's own
/// <c>'ldg</c>/<c>'stg</c>, the only two of its words that begin with a leading <c>'</c>) are wired in
/// below -- see <see cref="Node508LoadStoreGlobalTagBits"/>'s own remarks for the tag derivation.
/// Extended 2026-09-06 ("here is node 508, where some changes happened"): node 508's own dispatch cascade
/// grew one level deeper, splitting its old single embedded-offset "globals" pair (fetch/store, 10-bit
/// offset) into FOUR narrower embedded-offset forms (store, load, branch, conditional branch -- the
/// latter two are brand new, and the offset width shrank to 9 bits to make room for the extra prefix
/// bit) -- see <see cref="Node508Program"/>'s own remarks for the full cascade, including a flagged,
/// not-fixed apparent stack-depth bug in the new "branch" form's own sign-extension idiom. None of these
/// four forms has a tick-prefixed name to hang a mnemonic off of, so NONE of them is wired in here,
/// exactly the same choice already made for this node's OLD two embedded forms before this revision.
/// <c>'ldg</c>/<c>'stg</c>'s own addresses moved (0x002C/0x002E -&gt; 0x003A/0x003C, since <c>g/main</c>
/// grew ahead of them) but needed NO changes here at all, since <see cref="Node508LoadStoreGlobalTagBits"/>
/// below always resolves against a live compile of <see cref="Node508Program.Source"/>, never a
/// hardcoded address.
///
/// <b>RE-SYNCED 2026-09-09 against Stefan's LATEST <c>workspace.yaml</c> upload -- supersedes the
/// 2026-09-06 paragraph above.</b> The newest export shows node 508's own tick-prefixed words are
/// actually named <c>'gld</c>/<c>'gst</c>, not <c>'ldg</c>/<c>'stg</c> -- so <see cref="CvmInstructionSet.LoadGlobalMnemonic"/>/
/// <see cref="CvmInstructionSet.StoreGlobalMnemonic"/>'s own string VALUES were corrected to <c>"gld"</c>/
/// <c>"gst"</c> (their C# constant names are unchanged, since they still describe "load global"/"store
/// global" semantically), and the two entries below now resolve against the F18 symbols <c>'gld</c>/
/// <c>'gst</c> instead. Node 508's own dispatch cascade also reverted to a SHALLOWER shape than the
/// 2026-09-06 paragraph above describes: the "branch"/"conditional branch" split into two separate
/// 9-bit embedded forms never actually shipped -- the real, current source keeps ONE combined
/// <c>1010_11??</c> form (skip-if-r-nonzero, else branch by a 10-bit signed offset relayed to node 507's
/// own <c>m/branch</c>), which is exactly <see cref="CvmInstructionSet.ConditionalBranchTag"/>'s own
/// already-wired <c>cbr</c> shape (0xAC00, mask 0xFC00, <see cref="CvmInstructionSet.ConditionalBranchOffsetBitMask"/>
/// 0x3FF) -- so this re-sync needed NO change to <c>cbr</c> itself, only to <see cref="Node508Program.Source"/>
/// (see that class's own remarks), which had drifted out of step with <c>cbr</c>'s own already-confirmed
/// 10-bit width. <b>Naming/body cross-wire -- flagged, MIS-RESOLVED, then CORRECTED, all 2026-09-10:</b>
/// this same re-sync surfaced what looked like a cross-wire in Stefan's own current source -- <c>'gld</c>
/// called <c>g/@</c>, whose own body performed what reads as a remote STORE (<c>m/2!</c>), while
/// <c>'gst</c> called <c>g/!</c>, whose own body performed what reads as a remote FETCH (<c>m/2@</c>).
/// Asked directly, Stefan's first answer was read as confirming this was intentional naming (backwards
/// from the usual "load = into a register" convention) rather than a bug -- WRONGLY: he then ran an
/// actual test (<c>lit 1; gst 2; gld 2; gst 4; nop</c>) whose bus trace shows <c>gst</c> genuinely
/// WRITES r to the global and <c>gld</c> genuinely READS the global into r, exactly as their names
/// ordinarily mean. This was a REAL bug in <c>g/@</c>/<c>g/!</c>'s own bodies, which Stefan then fixed
/// directly (swapping the two bodies and re-wiring <c>'gld</c>/<c>'gst</c> as direct <c>.loc</c> aliases
/// rather than separate words). See <see cref="Node508Program"/>'s own remarks for the full history and
/// the fix; reproduced verbatim here too, since this class always resolves
/// <c>'gld</c>/<c>'gst</c> dynamically against a live compile of that source, never a hardcoded address or
/// body.
///
/// <b><c>Node509Program.cs</c> is a BRAND NEW coordinate (2026-09-05) -- no CVM1 namesake at all.</b>
/// Stefan's unary-arithmetic node, reached from node 508's own <c>g/main</c> dispatch (NOT node 507
/// directly) via its RIGHT port -- the first CVM2 node three hops out from the root
/// (507 -&gt; 508 -&gt; 509). Eight of its twelve tick-prefixed words REPOINT existing, previously-orphaned
/// mnemonics rather than adding duplicates, per "only update existing opcodes where possible": <c>inv</c>/
/// <c>inc</c>/<c>dec</c> (node 507's own old ALU-op family) and <c>abs</c>/<c>mul2</c>/<c>div2</c>/
/// <c>udiv2</c>/<c>bitcnt</c> (five of node 508's own old 27-op family) all now resolve against node
/// 509's own live compile instead, tag 0xB000 (see <see cref="Node509UnaryArithmeticTagBits"/>'s own
/// remarks). Only <c>neg</c> is genuinely new -- node 509's own word is <c>'neg</c>, not <c>'negate</c>,
/// so the existing, separately-orphaned <c>negate</c> mnemonic was left untouched at the time (it was
/// later deleted outright, 2026-09-09, once CCodeGenerator switched to emitting <c>neg</c> instead --
/// see <see cref="CvmInstructionSet"/>'s own remarks on the CVM1-opcode purge). Node 509's own
/// narrower embedded-literal opcode form (a 10-bit signed value baked directly into the opcode word) was
/// initially left unwired the same way node 508's own two embedded-offset global forms were, but Stefan
/// later named it explicitly ("add this range to the cvm language ... mnemonic lit") -- it is wired as
/// <see cref="CvmInstructionSet.LitMnemonic"/> instead, self-describing like <c>br</c>/<c>cbr</c>
/// (the now-retired, now-deleted <c>slit</c> used to be a third example -- see
/// <see cref="CvmInstructionSet"/>'s own remarks on the 2026-09-09 CVM1-opcode purge), so it needs NO entry in
/// <see cref="NodeSymbolByMnemonic"/> at all (see
/// <see cref="CvmInstructionSet.LitTag"/>'s own remarks). Two more tick-prefixed words, <c>parity</c> and
/// <c>odd</c>, were added the same way as <c>neg</c> (2026-09-05, "I added 2 new opcodes to node 509. add
/// them also to the language") -- both genuinely new, tagged exactly like the other nine, resolved
/// against node 509's own live compile. A thirteenth, <c>not</c>, followed the same day ("I added 'not'
/// to node 509. please add it to assembler and disassembler") -- also genuinely new, same tag, same live
/// compile -- see <see cref="Node509Program"/>'s own remarks for all three, including a structural note
/// on how adding <c>not</c>'s own definition also changed <c>'neg</c>'s own control flow (not this
/// class's concern, since address resolution here is always dynamic against a live compile regardless).
///
/// <b><c>Node406Program.cs</c> is a BRAND NEW coordinate (2026-09-05) -- no CVM1 namesake at all.</b>
/// Stefan's binary-arithmetic node, reached from node 407's own <c>n/main</c> dispatch (NOT node 507
/// directly) via its RIGHT port -- CVM2's SECOND three-hop branch off 507, mirroring 507-&gt;508-&gt;509
/// with 507-&gt;407-&gt;406. Node 406's own source originally referenced six names imported from node
/// 407 (<c>n/r@</c>/<c>n/r!</c>/<c>n/pop</c>/<c>n/push</c>/<c>n/next</c>/<c>n/leave</c>) that did not
/// exist on node 407 AS IT STOOD AT THE TIME (node 407 exported only five, under a <c>b/</c> prefix, with
/// no "next" helper) -- Stefan resolved this blocking mismatch directly by supplying a full replacement
/// <see cref="Node407Program"/> source renaming its whole export prefix to <c>n/</c> and adding a new
/// <c>n/next</c> -- see <see cref="Node407Program"/>'s own remarks, including a FLAGGED, not silently
/// resolved, discrepancy in that same replacement (its own "1101" relay branch reverting from
/// <c>---u</c> back to <c>-d--</c>). EIGHT of node 406's own twelve tick-prefixed binary ops REPOINT
/// node 507's old, permanently-orphaned ALU-op mnemonics (<c>add</c>/<c>sub</c>/<c>and</c>/<c>xor</c>/
/// <c>or</c>/<c>usl</c>/<c>ssr</c>/<c>usr</c>) rather than adding duplicates, per "only update existing
/// opcodes where possible" -- see <see cref="Node406BinaryStackTagBits"/>'s own remarks. The remaining
/// four (<c>rsb</c>/<c>rsl</c>/<c>rsr</c>/<c>rur</c>) are genuinely new. Each of the twelve additionally
/// gets its own "i"-suffixed sibling mnemonic for its "constant in the next trailing word" form, per
/// Stefan's own explicit naming rule ("for opcode with constant parameter, add a i to the mnemonic. so
/// 'add' becomes 'addi'") -- both forms share the SAME F18 symbol on node 406, differing only in tag
/// (<see cref="Node406BinaryStackTagBits"/> vs <see cref="Node406BinaryConstantTagBits"/>) and operand
/// shape -- see <see cref="Node406Program"/>'s own remarks for the full <c>ahead</c>/<c>[ swap ]</c>/
/// <c>then</c> dispatch-convergence derivation.
///
/// <b><c>Node408Program.cs</c> is a BRAND NEW coordinate (2026-09-06) -- no CVM1 namesake at all.</b>
/// Stefan's comparison node, reached from node 407's own <c>n/main</c> dispatch (NOT node 507 directly)
/// via its LEFT port -- the FOURTH branch of that same cascade, previously unanswered (see
/// <see cref="Node407Program"/>'s own remarks). Extended the same day with three more binary ops
/// (<c>'ge</c>/<c>'gt</c>/<c>'le</c>, "add more opcode to assembler and disassembler"). ALL FOURTEEN of
/// node 408's own tick-prefixed words (<c>'true</c>/<c>'false</c>/<c>'eq</c>/<c>'ne0</c>/<c>'ne</c>/
/// <c>'eq0</c>/<c>'ge</c>/<c>'lt0</c>/<c>'lt</c>/<c>'ge0</c>/<c>'gt</c>/<c>'le0</c>/<c>'le</c>/<c>'gt0</c>)
/// REPOINT existing, previously-orphaned mnemonics from node 508's old CVM1 27-comparison-op family, per
/// "only update existing opcodes where possible" -- no genuinely new <see cref="CvmInstructionSet"/>
/// entries were needed at all, either time, unlike node 406's or node 509's own additions. Six are BINARY
/// (<c>eq</c>/<c>ne</c>/<c>lt</c>/<c>ge</c>/<c>gt</c>/<c>le</c>, tag
/// <see cref="Node408BinaryComparisonTagBits"/>, 0xF400) and eight are UNARY (everything else, tag
/// <see cref="Node408UnaryComparisonTagBits"/>, 0xF000) -- see <see cref="Node408Program"/>'s own remarks
/// for the full derivation, including TWO FLAGGED (not silently investigated further) notes: one on
/// <c>'eq</c>'s own fall-through into <c>'ne0</c>'s body appearing to invert its stated true/false
/// polarity, and a second, more confidently derived one (cross-checked against this same file's own
/// earlier, unambiguous <c>'lt</c> definition) on <c>'ge</c>&lt;-&gt;<c>'lt</c> and
/// <c>'gt</c>&lt;-&gt;<c>'le</c> each appearing to compute the OTHER's stated meaning.
///
/// <b>What's still orphaned, not removed.</b> Node 508's OLD CVM1 27 comparison/arithmetic mnemonics
/// (<c>eq</c> through <c>bitcnt</c>) mostly keep their <see cref="NodeSymbolByMnemonic"/> entries, still
/// pointing at <see cref="Node508Program.Coordinate"/> and <see cref="Node508TagBits"/>, per "do not
/// remove any opcodes" -- they simply never resolve, since node 508's own REAL CVM2 source (the
/// globals-access node, above) does not define any of these 27 old F18 symbols either. Five of the 27
/// (<c>mul2</c>/<c>udiv2</c>/<c>div2</c>/<c>abs</c>/<c>bitcnt</c>) were repointed to node 509, and
/// fourteen more (<c>eq</c>/<c>eq0</c>/<c>false</c>/<c>true</c>/<c>ne</c>/<c>ne0</c>/<c>gt0</c>/<c>ge0</c>/
/// <c>le0</c>/<c>lt</c>/<c>lt0</c>/<c>ge</c>/<c>gt</c>/<c>le</c>) to node 408 (above, the last three added
/// 2026-09-06) -- leaving only EIGHT still orphaned against node 508: <c>ugt</c>, <c>ule</c>, <c>ult</c>,
/// <c>uge</c>, <c>negate</c>, <c>xt</c>, <c>ldt</c>, <c>stt</c>.
/// Node 507's own eleven old ALU-op mnemonics are now ALL repointed, none still orphaned: <c>inv</c>/
/// <c>inc</c>/<c>dec</c> to node 509 (above), and <c>add</c>/<c>sub</c>/<c>and</c>/<c>xor</c>/<c>or</c>/
/// <c>usl</c>/<c>ssr</c>/<c>usr</c> to node 406 (added 2026-09-05 -- see the node 406 block below).
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
/// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/>), <c>br</c>/<c>cbr</c>
/// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>), and node 606's
/// eight ops plus (since 2026-09-06) node 306's six address-register ops
/// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/>) have no F18
/// symbol at all to resolve, since none of their opcode words are a tagged dispatch to a named
/// primitive routine -- each one's whole word is fully determined by its own operand alone. Because of
/// that, none of them need a live compile to recognize: <see cref="CvmDebugSession.DisassemblePage0"/>
/// checks for them directly via <see cref="CvmInstructionSet.TryDescribeSelfDecodingWord"/> BEFORE ever
/// consulting this file's own symbol-driven decode table, so they already show up correctly in the
/// memory inspector -- node 306's OLD, now-deleted six-op self-describing family used to be fully
/// shadowed there by the now-retired, now-deleted <c>slit</c>'s own tag (see
/// <see cref="CvmInstructionSet"/>'s own remarks on the 2026-09-09 CVM1-opcode purge); with
/// <c>slit</c> gone, that collision is resolved too.
///
/// JOINED 2026-10-02 by the "CVM_pipeline" table's own <c>nop</c>/<c>scall</c>/<c>next16</c>/
/// <c>next32</c>/<c>pop16</c>/<c>pop32</c>/<c>rjmp</c>/<c>rcall</c>/<c>sbr</c>/<c>cbr</c> -- every one
/// of these is ALSO self-describing (<see cref="CvmInstructionSet.CvmOperandEncoding.FixedOpcode"/>/
/// <see cref="CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTrailingWord"/>/
/// <see cref="CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords"/>/
/// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/>/
/// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>/
/// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord"/>), so
/// none needs an F18 symbol either -- but <c>cbr</c> specifically can only be DECODED this way, not
/// yet ASSEMBLED (see <see cref="Assemble"/>'s own remarks on why it is explicitly rejected rather
/// than guessed at).
///
/// <see cref="Assemble"/> mirrors that same dual
/// dispatch on the OTHER direction --
/// hand-typed CVM asm source that uses <c>call</c>/<c>br</c>/<c>cbr</c>/node 606's or node
/// 306's ops is encoded directly from <see cref="CvmInstructionSet"/> and the operand alone, bypassing
/// this file's own <see cref="Instructions"/>/<see cref="NodeSymbolByMnemonic"/> pairing entirely (see
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
  //
  // PARTIALLY CORROBORATED 2026-09-09: Stefan's own message fixing CvmInstructionSet.BranchTag ("'br'
  // is wrongly encoded ... opcode br 1000_0??? ???? ???? branch relative signed 11-bit offset in
  // opcode") independently confirms this cascade's OTHER half -- "1000_0" really is br, exactly as
  // hypothesized here. That does not by itself confirm THIS constant's own value (0x8800, "1000_1" =
  // local execute) against real hardware -- it only raises confidence in the shared derivation. Do not
  // upgrade this to "confirmed" without Stefan saying so directly. See CvmInstructionSet.BranchTag's own
  // remarks for the other side of this correction, and CvmInstructionSet.ConditionalBranchTag's own
  // remarks for why THIS constant's value (0x8800) must never be reused for cbr.
  private const int Node507Cvm2LocalExecuteTagBits = 0x8800;

  // CVM2's long call/long jump tag (2026-09-02), per Stefan's node 407 source and his own explanation
  // of node 407's n/main dispatch cascade (renamed from b/main 2026-09-05 -- see Cvm.Node407Program's
  // own remarks) and the x/y relay protocol handing off from node 507's own m/main (confirmed correct by
  // Stefan -- see Cvm.Node407Program's own remarks for the full derivation): node 507 hands off to node
  // 407 once a fetched CVM opcode word's top bits read "11??", and node 407's own cascade consumes two
  // more bits before falling to "ex" (which jumps to whatever
  // address is already in R -- 'lcall's or 'ljmp's own address on node 407, loaded there by whoever
  // dispatches in) for the "1100" case -- so 'lcall/'ljmp's own CVM opcode word always has its top 4
  // bits "1100", i.e. tag 0xC000 OR'd with the local address on node 407, the same "tag | local
  // address" scheme Node507Cvm2LocalExecuteTagBits already uses (just a different tag/node pair). The
  // far call/jump TARGET address itself is not in this tag word at all -- it's the TrailingWord operand
  // (CvmInstructionSet.CvmOperandEncoding.TrailingWord), read by 'lcall/'ljmp via node 507's own m/next
  // once running on node 407.
  //
  // Extended 2026-09-06 to also cover 'lbr (briefly 'clbr/'lbr/'cljmp too -- Stefan's follow-up node 407
  // source added three more tick-prefixed words reached through this SAME "1100" n/main branch, the
  // branch's own condition unchanged -- only what happens once it falls to "ex" changed, see
  // Node407Program's own remarks -- then removed 'clbr/'cljmp again the same day, before either reached
  // real hardware: "I remove clbr and cljmp from node 407. remove it also from the CVM assembler and
  // disassembler"). So only 'lcall/'ljmp/'lbr now share this one tag, distinguished purely by their own
  // address on node 407, exactly the same "one tag, many named ops at different addresses" shape
  // Node509UnaryArithmeticTagBits and Node408's own tags already use.
  private const int Node407LongCallTagBits = 0xC000;

  // CVM2's node 506 'leave tag (2026-09-02), per Stefan's node 506 source (Cvm.Node506Program): its own
  // f/main dispatch cascade falls to "ex" (jump to whatever address is in R) once the fetched CVM
  // opcode word's top 7 bits read "1001_000" -- the same "tag | local address" scheme
  // Node407LongCallTagBits/Node507Cvm2LocalExecuteTagBits already use, just with a 7-bit tag/9-bit
  // address split instead of a 4-bit or 5-bit one. As a plain 16-bit tag word (address bits zeroed)
  // this is 0x9000 -- see CvmInstructionSet.Node506EnterTag's own remarks for the full bit derivation.
  //
  // FORMERLY a KNOWN, DELIBERATE collision with br (2026-09-02): 0x9000 used to also be
  // CvmInstructionSet.BranchTag's own value, and BranchTag's mask covered this tag's entire OLD range
  // (0x9000-0x97FF). Per Stefan: "ignore the ranges of br/cbr. ignore the overlapping ranges. give me
  // now enter and leave mnemonics." Not resolved at the time -- accepted for a while, same as
  // CvmInstructionSet.Node506EnterTag's own collision with br. Unlike enter (self-describing, so br's
  // OWN check silently won during disassembly -- see CvmInstructionSet.TryDescribeSelfDecodingWord's own
  // remarks), leave is a TAGGED mnemonic resolved only through THIS file's own
  // BuildDecodeTable/BuildEncodeTable against a live compile, which never consults br/cbr at all -- so
  // leave's own encode/decode through this file was never affected by the collision; it only mattered if
  // the very same 0x9038-shaped word was fetched as a plain word and run through
  // CvmInstructionSet.TryDescribeSelfDecodingWord first (which used to report "br" instead). RESOLVED
  // 2026-09-09: br moved to 0x8000 (see CvmInstructionSet.BranchTag's own remarks, "'br' is wrongly
  // encoded"), so this tag's range no longer overlaps br's at all.
  private const int Node506LeaveTagBits = 0x9000;

  // ADDED 2026-09-30 (new VM, third opcode after scall/lcall): the new VM's node 506 -- a completely
  // different, unrelated node 506 than the CVM2 stack-frame node Node506LeaveTagBits just above
  // belongs to (see Cvm.Node506Program's own remarks for the new VM's bit-pattern-table/dispatch
  // role) -- names a 'ret word for the "ret" opcode, spec row "1111|11ff|ffff|feee| implied" per
  // Stefan (e = 0, always; f = the 7-bit address of 'ret in node 506) -- see
  // CvmInstructionSet.Instructions' own remarks on RetMnemonic's new Id 184 for the full derivation.
  // Top 6 bits fixed at 1 (bits 15-10): as a plain 16-bit tag word (address bits zeroed) this was
  // 0xFC00. UNLIKE every other tag constant in this file, the resolved node address was not ORed in
  // at bit 0 -- it occupied bits 9-3, with the bottom 3 bits ("eee") forced to 0 -- so every mnemonic
  // in this family also needed an entry in NodeResolvedAddressShiftByMnemonic (below) to left-shift
  // the resolved address by 3 before combining it with this tag.
  //
  // RENAMED 2026-10-01 from "Node506RetTagBits": per Stefan, link/unlink's own opcodes are "like the
  // opcode for ret" -- the exact same tag/shift, just a different word in node 506 -- so this is the
  // whole node-506 bit-pattern-table family's shared tag, not ret's alone. See
  // CvmInstructionSet.Instructions' own remarks on Ids 185/186 for link/unlink's own derivation.
  //
  // CORRECTED 2026-10-02, per the full "CVM_pipeline" bit-pattern table Stefan pasted that day (see
  // CvmInstructionSet's own class-level remarks right after UnlinkMnemonic): the table's own "special"
  // row -- "0010|0000|00ww|wwww| special {ink, unlink, ret}" -- gives tag 0x2000 (bits 15-12 = 0010,
  // bits 11-6 fixed at 0), with an UNSHIFTED 6-bit field at bits 5-0 (mask 0x003F), not the previously
  // assumed 0xFC00/7-bit-field-at-bits-9-3/shift-3. Everything above this paragraph describes that
  // first, now-superseded assumption and is left as written (not rewritten) per this file's own "do
  // not rewrite history" convention -- see NodeResolvedAddressShiftByMnemonic's own remarks for the
  // matching shift correction.
  private const int Node506BitPatternTableTagBits = 0x2000;

  // CVM2's node 508 'gld/'gst tag (2026-09-04, renamed 2026-09-09 from 'ldg'/'stg -- see
  // CvmInstructionSet.LoadGlobalMnemonic's own remarks), per Stefan's node 508 source
  // (Cvm.Node508Program): its
  // own g/main dispatch cascade falls through to its own remote-fetch-then-"ex" tail (jump to whatever
  // address is in R) once the fetched CVM opcode word's top 5 bits read "1010_0" -- the same
  // "tag | local address" scheme Node407LongCallTagBits/Node506LeaveTagBits/
  // Node507Cvm2LocalExecuteTagBits already use, just with a 5-bit tag/11-bit address split this time.
  // As a plain 16-bit tag word (address bits zeroed) this is 0xA000 -- see
  // CvmInstructionSet.LoadGlobalMnemonic's own remarks for the full bit derivation. Unlike
  // Node506LeaveTagBits, this range (0xA000-0xA03F, node 508's own RAM is only 64 words) has no known
  // collision with br (0x8000-0x87FF as of 2026-09-09, OLD 0x9000-0x97FF before that) or cbr (0xAC00-
  // 0xAFFF as of 2026-09-09, renamed and re-confirmed from the old "ifbr" placeholder's 0x9800-0x9FFF
  // guess -- see CvmInstructionSet.ConditionalBranchTag's own remarks) or with node 606's own eight
  // orphaned frame-pointer tags (0xA800-0xAFFF; cbr's own new range used to collide with two of
  // those eight -- lal/lap, 0xAE00/0xAF00 -- but both are now retired, 2026-09-09, so that collision
  // is moot -- see ConditionalBranchTag's own remarks for that, unrelated
  // to node 508) -- see Cvm.Node508Program's own remarks. NOT YET CONFIRMED ON REAL HARDWARE
  // (2026-09-04). UNCHANGED by the 2026-09-09 re-sync to node 508's own source (see
  // Cvm.Node508Program's own remarks) -- that re-sync only restructured the SIBLING "1010_1???" branch
  // (the fetch/store embedded forms plus the combined branch/conditional-branch form, still not wired
  // here as their own mnemonics), never the "1010_0???" fall-through this tag is derived from; only
  // 'gld's/'gst's own ADDRESSES on node 508 moved (resolved dynamically below, not re-derived here).
  private const int Node508LoadStoreGlobalTagBits = 0xA000;

  // "literal" (2026-09-27, per Stefan: "change opcode 'literal' so that it uses 'lit' when the constant
  // fits and 'litr' if the constant is too big for 'lit'") -- a pure assembler-level pseudo-mnemonic, not
  // a real CVM opcode at all (it has no CvmInstructionSet.Instructions entry, no tag, no F18 symbol of
  // its own): a hand-written .cvmasm source (or the CVM Debugger's own Assembly Code editor) can write
  // "literal <value>" instead of choosing between "lit"/"litr" itself, and this file picks whichever one
  // actually fits -- see EncodeLiteralPseudoMnemonic's own remarks. Intercepted by name in Assemble/
  // GetWordLength/ParseSource, all BEFORE any CvmInstructionSet.TryGetShape/NodeSymbolByMnemonic lookup.
  private const string LiteralPseudoMnemonic = "literal";

  // CVM2's node 509 unary-arithmetic tag (2026-09-05), per Stefan's node 509 source
  // (Cvm.Node509Program): its own u/main dispatch cascade falls through to its own remote-fetch-then-
  // "ex" tail (jump to whatever address is in R) once the fetched CVM opcode word's top 6 bits read
  // "1011_00" -- the same "tag | local address" scheme Node407LongCallTagBits/Node506LeaveTagBits/
  // Node507Cvm2LocalExecuteTagBits/Node508LoadStoreGlobalTagBits already use, just with a 6-bit
  // tag/10-bit address split this time. As a plain 16-bit tag word (address bits zeroed) this is
  // 0xB000 -- see CvmInstructionSet.NegMnemonic's own remarks for the full bit derivation. Node 509's
  // own RAM is only 64 words, so this range (0xB000-0xB03F) has no known collision with br
  // (0x8000-0x87FF as of 2026-09-09, OLD 0x9000-0x97FF before that), cbr (0xAC00-0xAFFF as of
  // 2026-09-09, renamed and re-confirmed from the old "ifbr" placeholder's 0x9800-0x9FFF guess -- see
  // CvmInstructionSet.ConditionalBranchTag's own remarks), node 606's own eight orphaned frame-pointer
  // tags (0xA800-0xAFFF), or the now-retired slit's old 0xD000-0xDFFF range -- see Cvm.Node509Program's
  // own remarks. NOT YET
  // CONFIRMED ON REAL HARDWARE
  // (2026-09-05).
  private const int Node509UnaryArithmeticTagBits = 0xB000;

  // CVM2's node 406 binary-arithmetic tags (2026-09-05), per Stefan's node 406 source
  // (Cvm.Node406Program): its own y/main dispatch cascade offers TWO forms of each of its twelve named
  // ops, selected by two more bits within the already-consumed "1110" prefix node 407's own n/main
  // cascade hands off on (its own RIGHT port) -- "1110_00??_????_????" (six bits fixed: 0xE000) is the
  // "parameter already on the stack" form (CvmOperandEncoding.None), and "1110_01??_????_????" (six bits
  // fixed: 0xE400) is the "parameter in the following trailing word" form
  // (CvmOperandEncoding.TrailingWord) -- the same "tag | local address" scheme every other node's tag
  // above already uses, just the first one with TWO tags sharing the SAME symbol table (both resolve
  // against the SAME F18 address on node 406, differing only in tag and operand shape) rather than one
  // tag per node. A 6-bit-tag/10-bit-address split, matching Node509UnaryArithmeticTagBits's own bit
  // width exactly. Node 406's own RAM is only 64 words, so these two ranges (0xE000-0xE03F,
  // 0xE400-0xE43F) have no LIVE collision with any other wired tag here (Node508TagBits, 0xE800, is the
  // nearest neighbour, non-overlapping) -- see Cvm.Node406Program's own remarks on
  // CvmInstructionSet's own purely-historical "register d" 0xE000-0xE7FF comment text, which overlaps
  // only in prose, never in any live wiring. NOT YET CONFIRMED ON REAL HARDWARE (2026-09-05).
  private const int Node406BinaryStackTagBits = 0xE000;
  private const int Node406BinaryConstantTagBits = 0xE400;

  // CVM2's node 408 comparison tags (2026-09-06), per Stefan's node 408 source (Cvm.Node408Program): its
  // own c/main dispatch cascade offers TWO forms, selected by two more bits within the already-consumed
  // "1111" prefix node 407's own n/main cascade hands off on (its own LEFT port, previously unanswered
  // until now -- see Node407Program's own remarks) -- "1111_00??_????_????" (six bits fixed: 0xF000) is
  // the "unary comparison" form (no second stack pop) and "1111_01??_????_????" (six bits fixed: 0xF400)
  // is the "binary comparison" form (c/pop for a second operand) -- the same "tag | local address" scheme
  // every other node's tag above already uses, a 6-bit-tag/10-bit-address split matching
  // Node406BinaryStackTagBits/Node406BinaryConstantTagBits and Node509UnaryArithmeticTagBits's own bit
  // width exactly. UNLIKE node 406's pair, these two tags do NOT share one symbol table between them --
  // each of node 408's fourteen named ops lives at its own distinct address, reached through whichever of
  // the two tags matches its own arity (binary: 'eq/'ne/'lt; unary: everything else) -- see
  // Node408Program's own remarks for the full derivation and the binary/unary classification. The
  // remaining "1111_1???_????_????" quarter (0xF800-0xFFFF, "register file") is reserved/unimplemented,
  // matching Node406BinaryStackTagBits's own unanswered "1110_1???" sibling -- not wired in here. Node
  // 408's own RAM is only 64 words, so 0xF000-0xF03F/0xF400-0xF43F have no LIVE collision with anything
  // else wired here -- see Node408Program's own remarks on the purely-historical "register w" comment-
  // range overlap. NOT YET CONFIRMED ON REAL HARDWARE (2026-09-06).
  private const int Node408UnaryComparisonTagBits = 0xF000;
  private const int Node408BinaryComparisonTagBits = 0xF400;

  // CVM2's node 511 register-file tag (2026-09-07), per Stefan's node 510/511 source ("here are nodes
  // 510 and 511 ... they support 32 register that can be used for parameter passing to functions"):
  // reached from node 507's CPU through node 509's own "1011_1???" sibling range (node 509 itself only
  // answers "1011_00??_????_????", per Node509UnaryArithmeticTagBits's own remarks; node 510, "extended
  // arithmetic, 1011_1???_????_????", relays "1011_11??" on to node 511 and locally falls through to
  // "ex" on "1011_10??", per Node510Program's own remarks), fixing node 511's own top 6 bits at
  // "1011_11" -- 0xBC00. UNLIKE every tag above, this one is not simply OR'd with a resolved address:
  // node 511's own r/main packs TWO fields into the remaining 10 bits (see
  // CvmInstructionSet.LoadRegisterFileMnemonic's own remarks for the full bit-by-bit derivation off its
  // own body): bits 9-5 are (resolvedAddress - 0x20), which of 'rld/'rst/'rpop/'rpush is being invoked;
  // bits 4-0 are the register index (0-31) operand, supplied by whoever writes the CVM asm line, never
  // resolved from a node. CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue exists
  // specifically for this shape; BuildEncodeTable/BuildDecodeTable/Assemble/DisassemblePage0 below all
  // have one small dedicated branch each for it, since neither the ordinary "tag | resolved address"
  // formula every other tagged mnemonic in this file uses, nor EncodeSelfDescribingWord's self-
  // describing formula, covers a mnemonic needing both a live resolution AND an embedded operand at
  // once. NOT YET CONFIRMED ON REAL HARDWARE (2026-09-07). (Node 407/408's own dispatch history
  // separately mentions an UNRELATED, still-unimplemented "register file" reserved at 0xF800-0xFFFF --
  // see Node408BinaryComparisonTagBits's own remarks; that is a different, still-unbuilt quarter, not
  // this one.)
  private const int Node511Tag = 0xBC00;

  // SUPERSEDES Node511Tag (and the OLD node 510 "extended arithmetic" tag, 0xB800, formerly used for
  // 'addc/'xst/'xld/'xmul2/'xdiv2/'xumul below) as of 2026-09-27: Stefan redesigned nodes 510/511
  // wholesale -- node 511 is now pure passive 64-word register STORAGE with no opcode-relevant F18 symbols
  // of its own at all, and node 510 ("extended register access") absorbs the WHOLE combined range
  // directly, one hop closer to node 509 (imports node 509, not node 510) rather than two. Same
  // NodeResolvedEmbeddedValue shape as the old Node511Tag family -- see
  // CvmInstructionSet.RegisterAccessRegisterFieldBitMask's own remarks for the new 6-bit/3-bit field
  // layout (register now at bits 9-4, not bits 4-0 -- the first NodeResolvedEmbeddedValue family in this
  // file whose register field needs an actual shift, see NodeResolvedEmbeddedValueFieldLayoutByMnemonic's
  // own RegisterFieldShift member).
  private const int Node510RegisterAccessTag = 0xB800;

  // REMOVED OUTRIGHT, 2026-09-15: "Node308Tag" (0xDC00) used to tag node 308's OLD dpop/dpush/dinc/
  // ddec/dadd/dor family (added 2026-09-09) -- removed completely per Stefan's own direct instruction,
  // see CvmInstructionSet's own removal note above FloatingPointRegisterFieldBitMask. The numeric value
  // 0xDC00 is NOT reused under that old name below -- it went on to tag node 306's OLD unary-only
  // 'fpop/'fpush family (2026-09-15/16, itself since retired -- see the next removal note), and now tags
  // node 306/305's REWORKED, unified floating-point family instead (2026-09-21, CvmInstructionSet.FloatingPointOperationTag) --
  // under node 307's OWN pre-2026-09-15 dispatch, "1101_11??" relayed LEFT to what was then physical node
  // 308 (the first family); under the swapped, CURRENT dispatch, the SAME "1101_11??" bit pattern relays
  // RIGHT to physical node 306 (the floating-point decoder) -- see Cvm.Node307Program's own "RIGHT/LEFT
  // SWAPPED" remarks. The tag bits didn't move; which node, and which family, sat behind them did, twice.

  // REMOVED OUTRIGHT, 2026-09-21: "FloatingPointUnaryTag" (0xDC00) used to tag node 306's OLD, node-
  // resolved 'fpop/'fpush (2026-09-15/16) -- REWORKED per Stefan's own direct instruction ("i want to to
  // a rework of the FP engine. it does not function well in the current state.") into the unified,
  // fully self-describing twelve-op family below (CvmInstructionSet.FloatingPointOperationTag, same
  // numeric value 0xDC00, new meaning -- see that constant's own remarks). fpop/fpush keep their names
  // but no longer need an entry in this file at all: the new scheme resolves nothing against a live node
  // compile, so NodeSymbolByMnemonic/NodeResolvedEmbeddedValueFieldLayoutByMnemonic simply have no rows
  // for any of the twelve floating-point mnemonics any more.

  // ---- 2026-09-30: NEW VIRTUAL MACHINE, full reset -- this whole dictionary was made INERT --------
  // Per Stefan directly: "there is a new virtual machine. all opcodes are invalid. except that 'nop'
  // has the opcode '0'." At that moment CvmInstructionSet.Instructions contained exactly one live entry
  // (nop), and its own encoding is CvmOperandEncoding.FixedOpcode -- a shape that, by design, needs no
  // node/symbol resolution at all (see that encoding's own remarks). None of the four encodings this
  // file's own Instructions list (below) filters for -- None/TrailingWord/TwoTrailingWords/
  // NodeResolvedEmbeddedValue -- existed in CvmInstructionSet.Instructions any longer at that point, so
  // that filtered list came out EMPTY, and every entry below was simply never looked up by anything any
  // more (BuildDecodeTable/BuildEncodeTable both iterate that filtered list, not this dictionary
  // directly). Left in place, uncommented, per this file's own "do not remove any opcodes" convention --
  // commenting out ~280 individual dictionary entries here would be pure busywork with no behavioral
  // effect, since the filter upstream already made every one of them unreachable; this note exists so a
  // future reader doesn't mistake "still compiles" for "still wired."
  //
  // NO LONGER FULLY INERT, same day: ret (Id 184, CvmOperandEncoding.None -- see
  // CvmInstructionSet.Instructions' own remarks) is the new VM's first None/TrailingWord/
  // TwoTrailingWords/NodeResolvedEmbeddedValue-shaped mnemonic, so it DOES pass the filtered list's own
  // .Where clause and DOES need a live entry here -- see the new, live [RetMnemonic] row below (node
  // 506, Node506BitPatternTableTagBits), right where the OLD, now-commented-out CVM2 node-507 "ret"
  // row used to be. ADDED 2026-10-01: link/unlink (Ids 185/186) join ret in this same family -- see
  // their own entries just below. Every other row in this dictionary remains exactly as inert as
  // described just above.
  //
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
  // call/br/cbr (self-describing, no node needed) and for the now fully-retired slit/lal/lap (removed
  // from Instructions altogether 2026-09-09, so there is nothing left to look up at all). Node 507
  // (CVM2's actual CPU), node 407 (CVM2's long-call/long-jump helper), node
  // 506 (CVM2's stack-frame node), node 508 (CVM2's globals-access node, plus its own OLD, permanently
  // orphaned CVM1 comparison ops), node 509 (CVM2's unary-arithmetic node -- a BRAND NEW coordinate, no
  // CVM1 namesake to share or orphan), node 406 (CVM2's binary-arithmetic node -- ALSO a BRAND NEW
  // coordinate, no CVM1 namesake), and node 408 (CVM2's comparison node -- ALSO a BRAND NEW coordinate,
  // no CVM1 namesake) -- 407/506/508 all different nodes than their deleted CVM1 namesakes, sharing only
  // the coordinate -- are the only coordinates anything here still points at.
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
        // RetMnemonic ("ret") -- REPOINTED 2026-09-30 (new VM reset) from CVM2's node 507 to the new
        // VM's node 506 -- see CvmInstructionSet.Instructions' own remarks on RetMnemonic's new Id 184
        // for the full derivation. The OLD CVM2 entry is commented out just below (not merely left
        // inert like every other still-uncommented row in this dictionary) because a Dictionary literal
        // cannot hold two entries for the same key ([RetMnemonic] appearing twice would throw
        // "An item with the same key has already been added" at class-load time, taking down every
        // other mnemonic's resolution with it) -- kept as a comment, per "do not remove any opcodes",
        // as the historical record of CVM2's OLD node-507 "ret" (Node507Cvm2LocalExecuteTagBits,
        // 0x8800 | address), which shares nothing with the new entry below except the mnemonic string.
        // [RetMnemonic] = (Node507Program.Coordinate, "'ret", Node507Cvm2LocalExecuteTagBits),
        //
        // CORRECTED 2026-10-02, per Stefan directly: "the address of 'ret, 'link and 'unlink label
        // must be taken from node 507." Node506BitPatternTableTagBits' own tag (0x2000) is UNCHANGED
        // -- node 506 is still the table's own home -- but the actual LABEL this resolves against
        // (and therefore the live compile this is resolved against) moves from node 506 to node 507.
        // Likely architectural picture (not itself stated by Stefan, so flagged as inference): node
        // 506 holds the dispatch/table logic, while node 507 holds the actual addressable word for
        // each of these; node 506's own "special" dispatch presumably relays to node 507 the same
        // way CVM2's OLD call/lcall/ljmp dispatch used to relay between nodes (see
        // Node407LongCallTagBits' own remarks for that precedent). Joined by lcall/ljmp (just below)
        // under the exact same correction, per the same sentence.
        [RetMnemonic] = (Node507Program.Coordinate, "'ret", Node506BitPatternTableTagBits),
        // LinkMnemonic ("link")/UnlinkMnemonic ("unlink") -- ADDED 2026-10-01, alongside ret in node
        // 506's bit-pattern-table family (see Node506BitPatternTableTagBits' own remarks and
        // CvmInstructionSet.Instructions' own remarks on Ids 185/186). Brand new mnemonic names, no
        // OLD CVM1/CVM2 entry to comment out first (unlike ret just above).
        //
        // CORRECTED 2026-10-02, same as RetMnemonic just above and for the same reason: NodeCoordinate
        // moves from node 506 to node 507; Node506BitPatternTableTagBits' own tag is unchanged.
        [CvmInstructionSet.LinkMnemonic] = (Node507Program.Coordinate, "'link", Node506BitPatternTableTagBits),
        [CvmInstructionSet.UnlinkMnemonic] = (Node507Program.Coordinate, "'unlink", Node506BitPatternTableTagBits),
        [CvmInstructionSet.HaltMnemonic] = (Node507Program.Coordinate, "'halt", Node507Cvm2LocalExecuteTagBits),
        // tjmp (2026-09-09, "'tjmp is in node 507") -- node 507's own table-jump primitive, reached the
        // SAME "1000_1???" local-execute tag family as the six above (Node507Cvm2LocalExecuteTagBits) --
        // see CvmInstructionSet.TableJumpMnemonic's own remarks.
        [CvmInstructionSet.TableJumpMnemonic] = (Node507Program.Coordinate, "'tjmp", Node507Cvm2LocalExecuteTagBits),
        // jump/xs/xp (2026-09-09 opcode/assembler-vs-node reconciliation audit -- see
        // CvmInstructionSet.JumpMnemonic's own remarks): node 507's remaining three tick-labeled
        // opcodes, previously left deliberately unwired. Same "1000_1???" local-execute tag family as
        // the six above.
        [CvmInstructionSet.JumpMnemonic] = (Node507Program.Coordinate, "'jump", Node507Cvm2LocalExecuteTagBits),
        [CvmInstructionSet.ExchangeSMnemonic] = (Node507Program.Coordinate, "'xs", Node507Cvm2LocalExecuteTagBits),
        [CvmInstructionSet.ExchangePMnemonic] = (Node507Program.Coordinate, "'xp", Node507Cvm2LocalExecuteTagBits),
        // CVM2's long call/long jump (2026-09-02) -- resolve against node 407's own live compile, tag
        // 0xC000 (Node407LongCallTagBits's own remarks). Unlike nop/pushlit/push/pop/ret/halt above,
        // these live on a DIFFERENT node than CVM2's CPU (507) -- BuildDecodeTable/BuildEncodeTable
        // already resolve each mnemonic against its own node independently, so this is just another
        // entry, not a special case.
        //
        // RETIRED 2026-10-02, SAME REASON as RetMnemonic's own OLD CVM2 entry above (a Dictionary
        // literal cannot hold two entries for the same key): per Stefan, "ljmp and lcall must encode
        // in a similar way like 'ret and their address also taken from labels 'ljmp and 'lcall in
        // node 507. they are now special opcodes." That is a brand new, unrelated shape (the new
        // Ids 195/196, CvmOperandEncoding.None, node 507, tag Node506BitPatternTableTagBits) replacing
        // this CVM2-era one (node 407, tag 0xC000) -- kept here as a comment, per "do not remove any
        // opcodes", as the historical record; the live entries are just below.
        // [CvmInstructionSet.LongCallMnemonic] = (Node407Program.Coordinate, "'lcall", Node407LongCallTagBits),
        // [CvmInstructionSet.LongJumpMnemonic] = (Node407Program.Coordinate, "'ljmp", Node407LongCallTagBits),
        //
        // NEW 2026-10-02: lcall/ljmp join ret/link/unlink in node 507's own "special" family (Ids
        // 195/196 -- see CvmInstructionSet.Instructions' own remarks on those Ids for Stefan's exact
        // wording and the same-day shape correction). Same tag, same node, same shift (0 -- see
        // NodeResolvedAddressShiftByMnemonic's own remarks) as ret/link/unlink; CvmOperandEncoding.
        // TrailingWord (like link, NOT like ret/unlink -- confirmed against node 507's own live
        // 'lcall/'ljmp source, both of which call m/next, node 507's "fetch the next word from
        // memory" primitive, before doing anything else) means each DOES take a real trailing operand
        // word: the actual far-call/far-jump target address. "call" (EncodeCallPseudoMnemonic's own
        // remarks) lowers to lcall through this entry exactly like any other node-resolved tagged
        // mnemonic -- the live-resolved opcode word comes from BuildEncodeTable via this very entry,
        // not a fixed constant the way the OLD, retired Id 183 shape used to supply one directly.
        [CvmInstructionSet.LongCallMnemonic] = (Node507Program.Coordinate, "'lcall", Node506BitPatternTableTagBits),
        [CvmInstructionSet.LongJumpMnemonic] = (Node507Program.Coordinate, "'ljmp", Node506BitPatternTableTagBits),

        // Node 407's long-branch op (added 2026-09-06, "more opcodes to node 407 added") -- reached
        // through node 407's own SAME "1100" n/main branch as 'lcall/'ljmp above, so it shares the SAME
        // tag (Node407LongCallTagBits, 0xC000), differing only in its own address on node 407 -- see
        // CvmInstructionSet.LongBranchMnemonic's own remarks and Node407Program's own remarks for the
        // full derivation. Two sibling ops added the same day, 'clbr (conditional long branch) and 'cljmp
        // (conditional long jump), were removed again the same day, before either reached real hardware,
        // per Stefan: "I remove clbr and cljmp from node 407. remove it also from the CVM assembler and
        // disassembler" -- their entries are deleted here, not kept-and-orphaned, since
        // CvmInstructionSet.Instructions itself already retired their Ids (98/100) outright for the same
        // reason (see that table's own remarks).
        [CvmInstructionSet.LongBranchMnemonic] = (Node407Program.Coordinate, "'lbr", Node407LongCallTagBits),
        // CVM2's node 506 (2026-09-02) -- repointed from CVM1's node 606 ("only update existing opcodes
        // where possible"). Only 'leave so far, per Stefan's own explicit scope ("give me now enter and
        // leave mnemonics"); enter is a DIFFERENT (self-describing) shape and is wired directly in
        // CvmInstructionSet.Instructions instead (see Node506EnterTag's own remarks), not here. Node
        // 506's own load-local/load-parameter/store-local/store-parameter are not wired in yet.
        [CvmInstructionSet.LeaveMnemonic] = (Node506Program.Coordinate, "'leave", Node506LeaveTagBits),
        // f/fpush (2026-09-09, replacing the retired lal/lap -- "use ''f' or 'fpush' from node 506 and
        // add the offset to calculate the address of a local or parameter. i will provide a new 506.")
        // -- reached the SAME way 'leave is, sharing its tag (Node506LeaveTagBits, 0x9000 | address on
        // node 506's own f/main "ex" fall-through). See CvmInstructionSet.FrameToRegisterMnemonic's own
        // remarks. RENAMED 2026-09-16: node 506's own F18 word is now "'pushf" (was "'fpush"), per
        // Stefan's own rename resolving the CVM mnemonic collision with node 306's own 'fpush -- see
        // CvmInstructionSet.PushFrameMnemonic's own remarks.
        [CvmInstructionSet.FrameToRegisterMnemonic] = (Node506Program.Coordinate, "'f", Node506LeaveTagBits),
        [CvmInstructionSet.PushFrameMnemonic] = (Node506Program.Coordinate, "'pushf", Node506LeaveTagBits),
        // CVM2's node 508 (2026-09-04) -- the globals-access node, resolved against node 508's own live
        // compile, tag 0xA000 (Node508LoadStoreGlobalTagBits's own remarks). Only 'gld/'gst so far, per
        // Stefan's own tick-naming rule (only these two of node 508's own words begin with a leading
        // '); node 508's own two narrower embedded-offset opcode forms (9-bit offset baked directly
        // into the opcode word, no trailing word) have no tick-prefixed name to hang a mnemonic off of
        // and are NOT wired in here -- see Node508Program's own remarks. RENAMED 2026-09-09 from
        // 'ldg'/'stg to 'gld'/'gst -- re-synced against Stefan's latest workspace.yaml; see the class
        // remarks above for the full re-sync note, including a flagged (not fixed) apparent naming/body
        // cross-wire between 'gld'/'gst and node 508's own g/@/g/! primitives.
        [CvmInstructionSet.LoadGlobalMnemonic] = (Node508Program.Coordinate, "'gld", Node508LoadStoreGlobalTagBits),
        [CvmInstructionSet.StoreGlobalMnemonic] = (Node508Program.Coordinate, "'gst", Node508LoadStoreGlobalTagBits),
        // Node 508's literal-load family (2026-09-27, "add litr, litm and lit2 to the language") --
        // same node, same tag (Node508LoadStoreGlobalTagBits) as 'gld/'gst above, just three more
        // tick-prefixed words resolved against the same live compile. See Node508Program's own remarks
        // for 'litm's fall-through-into-'litr mechanism and CvmInstructionSet.LitrMnemonic's own remarks
        // for the CVM-level shape (litr: TrailingWord; litm/lit2: the new TwoTrailingWords).
        [CvmInstructionSet.LitrMnemonic] = (Node508Program.Coordinate, "'litr", Node508LoadStoreGlobalTagBits),
        [CvmInstructionSet.LitmMnemonic] = (Node508Program.Coordinate, "'litm", Node508LoadStoreGlobalTagBits),
        [CvmInstructionSet.Lit2Mnemonic] = (Node508Program.Coordinate, "'lit2", Node508LoadStoreGlobalTagBits),
        // CVM2's node 509 (2026-09-05) -- the unary-arithmetic node, resolved against node 509's own
        // live compile, tag 0xB000 (Node509UnaryArithmeticTagBits's own remarks). 'inv/'inc/'dec REPOINT
        // three of node 507's old, permanently-orphaned ALU-op mnemonics; 'neg is genuinely new (does
        // NOT repoint the separately-orphaned "negate" below -- different spelling, taken literally).
        // See Node509Program's own remarks for the full derivation.
        [CvmInstructionSet.InvertMnemonic] = (Node509Program.Coordinate, "'inv", Node509UnaryArithmeticTagBits),
        [CvmInstructionSet.IncrementMnemonic] = (Node509Program.Coordinate, "'inc", Node509UnaryArithmeticTagBits),
        [CvmInstructionSet.DecrementMnemonic] = (Node509Program.Coordinate, "'dec", Node509UnaryArithmeticTagBits),
        [CvmInstructionSet.NegMnemonic] = (Node509Program.Coordinate, "'neg", Node509UnaryArithmeticTagBits),
        // Node 509's tenth/eleventh ops (2026-09-05, "I added 2 new opcodes to node 509. add them also
        // to the language") -- both genuinely new, same tag, same live compile as the rest of node 509.
        [CvmInstructionSet.ParityMnemonic] = (Node509Program.Coordinate, "'parity", Node509UnaryArithmeticTagBits),
        [CvmInstructionSet.OddMnemonic] = (Node509Program.Coordinate, "'odd", Node509UnaryArithmeticTagBits),
        // Node 509's thirteenth op (2026-09-05, "I added 'not' to node 509. please add it to assembler
        // and disassembler") -- genuinely new, same tag, same live compile as the rest of node 509.
        [CvmInstructionSet.NotMnemonic] = (Node509Program.Coordinate, "'not", Node509UnaryArithmeticTagBits),
        // CVM2's node 406 (2026-09-05) -- the binary-arithmetic node, resolved against node 406's own
        // live compile. EIGHT of these twelve REPOINT node 507's old, permanently-orphaned ALU-op
        // mnemonics ("only update existing opcodes where possible"); rsb/rsl/rsr/rur are genuinely new.
        // Each has TWO entries here -- the plain mnemonic (parameter already on the stack,
        // Node406BinaryStackTagBits) and its own "i"-suffixed sibling (parameter in the next trailing
        // word, Node406BinaryConstantTagBits) -- both pointing at the SAME F18 symbol on node 406, per
        // Stefan's own naming rule ("for opcode with constant parameter, add a i to the mnemonic. so
        // 'add' becomes 'addi'"). See Node406Program's own remarks for the full derivation.
        [CvmInstructionSet.AddMnemonic] = (Node406Program.Coordinate, "'add", Node406BinaryStackTagBits),
        [CvmInstructionSet.AddConstantMnemonic] = (Node406Program.Coordinate, "'add", Node406BinaryConstantTagBits),
        [CvmInstructionSet.SubtractMnemonic] = (Node406Program.Coordinate, "'sub", Node406BinaryStackTagBits),
        [CvmInstructionSet.SubtractConstantMnemonic] = (Node406Program.Coordinate, "'sub", Node406BinaryConstantTagBits),
        [CvmInstructionSet.ReverseSubtractMnemonic] = (Node406Program.Coordinate, "'rsb", Node406BinaryStackTagBits),
        [CvmInstructionSet.ReverseSubtractConstantMnemonic] = (Node406Program.Coordinate, "'rsb", Node406BinaryConstantTagBits),
        [CvmInstructionSet.AndMnemonic] = (Node406Program.Coordinate, "'and", Node406BinaryStackTagBits),
        [CvmInstructionSet.AndConstantMnemonic] = (Node406Program.Coordinate, "'and", Node406BinaryConstantTagBits),
        [CvmInstructionSet.XorMnemonic] = (Node406Program.Coordinate, "'xor", Node406BinaryStackTagBits),
        [CvmInstructionSet.XorConstantMnemonic] = (Node406Program.Coordinate, "'xor", Node406BinaryConstantTagBits),
        [CvmInstructionSet.OrMnemonic] = (Node406Program.Coordinate, "'or", Node406BinaryStackTagBits),
        [CvmInstructionSet.OrConstantMnemonic] = (Node406Program.Coordinate, "'or", Node406BinaryConstantTagBits),
        [CvmInstructionSet.ReverseShiftLeftMnemonic] = (Node406Program.Coordinate, "'rsl", Node406BinaryStackTagBits),
        [CvmInstructionSet.ReverseShiftLeftConstantMnemonic] = (Node406Program.Coordinate, "'rsl", Node406BinaryConstantTagBits),
        [CvmInstructionSet.UnsignedShiftLeftMnemonic] = (Node406Program.Coordinate, "'usl", Node406BinaryStackTagBits),
        [CvmInstructionSet.UnsignedShiftLeftConstantMnemonic] = (Node406Program.Coordinate, "'usl", Node406BinaryConstantTagBits),
        [CvmInstructionSet.ReverseShiftRightMnemonic] = (Node406Program.Coordinate, "'rsr", Node406BinaryStackTagBits),
        [CvmInstructionSet.ReverseShiftRightConstantMnemonic] = (Node406Program.Coordinate, "'rsr", Node406BinaryConstantTagBits),
        [CvmInstructionSet.SignedShiftRightMnemonic] = (Node406Program.Coordinate, "'ssr", Node406BinaryStackTagBits),
        [CvmInstructionSet.SignedShiftRightConstantMnemonic] = (Node406Program.Coordinate, "'ssr", Node406BinaryConstantTagBits),
        [CvmInstructionSet.ReverseUnsignedShiftRightMnemonic] = (Node406Program.Coordinate, "'rur", Node406BinaryStackTagBits),
        [CvmInstructionSet.ReverseUnsignedShiftRightConstantMnemonic] = (Node406Program.Coordinate, "'rur", Node406BinaryConstantTagBits),
        [CvmInstructionSet.UnsignedShiftRightMnemonic] = (Node406Program.Coordinate, "'usr", Node406BinaryStackTagBits),
        [CvmInstructionSet.UnsignedShiftRightConstantMnemonic] = (Node406Program.Coordinate, "'usr", Node406BinaryConstantTagBits),
        // CVM1's OLD 27 node-508 comparison/arithmetic ops -- permanently orphaned (node 508's own REAL
        // CVM2 source, added 2026-09-04, is the globals-access node above and does not define any of
        // these 27 old F18 symbols), kept per "do not remove any opcodes." Simply omitted from
        // BuildDecodeTable/BuildEncodeTable now that node 508 has a real compile, the same graceful-
        // omission path this file already relies on for any mnemonic whose symbol isn't defined in its
        // node's current source. See this class's own remarks. FIVE of these 27 -- mul2/udiv2/div2/abs/
        // bitcnt -- are REPOINTED to node 509's own live compile below instead (2026-09-05, "only
        // update existing opcodes where possible" -- node 509's own tick-prefixed words match those
        // exact mnemonic strings); the remaining 22 stay pointed at node 508 and permanently orphaned.
        // Node 408's eleven comparison ops (2026-09-06) REPOINT all eleven of these from node 508's old,
        // permanently-orphaned CVM1 tag (Node508TagBits) to node 408's own live compile -- see
        // Node408Program's own remarks for the full derivation and the binary/unary tag split. Binary
        // ops (two stack inputs, ( xy-f)) use Node408BinaryComparisonTagBits; unary ops (one input, ( -f)
        // or ( x-f)) use Node408UnaryComparisonTagBits.
        [CvmInstructionSet.EqualMnemonic] = (Node408Program.Coordinate, "'eq", Node408BinaryComparisonTagBits),
        [CvmInstructionSet.EqualToZeroMnemonic] = (Node408Program.Coordinate, "'eq0", Node408UnaryComparisonTagBits),
        [CvmInstructionSet.FalseMnemonic] = (Node408Program.Coordinate, "'false", Node408UnaryComparisonTagBits),
        [CvmInstructionSet.TrueMnemonic] = (Node408Program.Coordinate, "'true", Node408UnaryComparisonTagBits),
        [CvmInstructionSet.NotEqualMnemonic] = (Node408Program.Coordinate, "'ne", Node408BinaryComparisonTagBits),
        [CvmInstructionSet.NotEqualToZeroMnemonic] = (Node408Program.Coordinate, "'ne0", Node408UnaryComparisonTagBits),
        // ugt/ule/ult/uge: RESOLVED 2026-09-09 (second pass, against Stefan's own workspace.yaml export
        // -- the exact gap that prompted the whole audit). Node 408's own current source defines a real
        // c/u helper plus matching 'ugt/'ule/'ult/'uge words (all four binary, ( xy-f), so they use
        // Node408BinaryComparisonTagBits like the other six binary comparison ops) -- repointed here from
        // node 508's old, permanently-orphaned CVM1 tag to node 408's own live compile, exactly like the
        // other ten comparison ops above. negate/xt/ldt/stt stayed permanently orphaned against node 508
        // for a while longer, flagged and kept per Ga144.C.Toolchain.CCodeGenerator's own continued use,
        // until the 2026-09-09 CVM1-opcode purge (later the same day) rewrote that codegen and deleted
        // all four outright -- see CvmInstructionSet's own remarks on that purge for the full accounting.
        [CvmInstructionSet.UnsignedGreaterThanMnemonic] = (Node408Program.Coordinate, "'ugt", Node408BinaryComparisonTagBits),
        [CvmInstructionSet.GreaterThanMnemonic] = (Node408Program.Coordinate, "'gt", Node408BinaryComparisonTagBits),
        [CvmInstructionSet.GreaterThanZeroMnemonic] = (Node408Program.Coordinate, "'gt0", Node408UnaryComparisonTagBits),
        [CvmInstructionSet.GreaterOrEqualMnemonic] = (Node408Program.Coordinate, "'ge", Node408BinaryComparisonTagBits),
        [CvmInstructionSet.GreaterOrEqualToZeroMnemonic] = (Node408Program.Coordinate, "'ge0", Node408UnaryComparisonTagBits),
        [CvmInstructionSet.UnsignedLessOrEqualMnemonic] = (Node408Program.Coordinate, "'ule", Node408BinaryComparisonTagBits),
        [CvmInstructionSet.LessOrEqualMnemonic] = (Node408Program.Coordinate, "'le", Node408BinaryComparisonTagBits),
        [CvmInstructionSet.LessOrEqualToZeroMnemonic] = (Node408Program.Coordinate, "'le0", Node408UnaryComparisonTagBits),
        [CvmInstructionSet.LessThanMnemonic] = (Node408Program.Coordinate, "'lt", Node408BinaryComparisonTagBits),
        [CvmInstructionSet.LessThanZeroMnemonic] = (Node408Program.Coordinate, "'lt0", Node408UnaryComparisonTagBits),
        [CvmInstructionSet.UnsignedLessThanMnemonic] = (Node408Program.Coordinate, "'ult", Node408BinaryComparisonTagBits),
        [CvmInstructionSet.UnsignedGreaterOrEqualMnemonic] = (Node408Program.Coordinate, "'uge", Node408BinaryComparisonTagBits),
        // negate/xt/ldt/stt -- WIRED to node 508 here at the time ("'negate", "'xt", "'ldt", "'stt"),
        // kept per Ga144.C.Toolchain.CCodeGenerator's own continued use. DELETED OUTRIGHT 2026-09-09
        // (CVM1-opcode purge, later the same day) once CCodeGenerator was rewritten to stop emitting all
        // four (unary minus now emits node 509's 'neg; every pointer dereference now uses node 306's own
        // 'arst/'lda/'sta) -- CvmInstructionSet.NegateMnemonic/ExchangeTMnemonic/LoadTMnemonic/
        // StoreTMnemonic no longer exist as constants, so these four entries are removed along with them.
        // See CvmInstructionSet's own remarks on the 2026-09-09 CVM1-opcode purge.
        // Repointed to node 509 (2026-09-05) -- see the node 509 block above for the full explanation.
        [CvmInstructionSet.MultiplyByTwoMnemonic] = (Node509Program.Coordinate, "'mul2", Node509UnaryArithmeticTagBits),
        [CvmInstructionSet.UnsignedDivideByTwoMnemonic] = (Node509Program.Coordinate, "'udiv2", Node509UnaryArithmeticTagBits),
        [CvmInstructionSet.DivideByTwoMnemonic] = (Node509Program.Coordinate, "'div2", Node509UnaryArithmeticTagBits),
        [CvmInstructionSet.AbsoluteValueMnemonic] = (Node509Program.Coordinate, "'abs", Node509UnaryArithmeticTagBits),
        [CvmInstructionSet.BitCountMnemonic] = (Node509Program.Coordinate, "'bitcnt", Node509UnaryArithmeticTagBits),
        // Node 511's register file (2026-09-07) -- UN-RETIRED 2026-09-09 (workspace.yaml wins, per
        // Stefan's own explicit ruling): node 511's AUTHORITATIVE current source (its own simpler,
        // direct-field-extraction r/main, restored from workspace.yaml) once again names 'rld/'rst/'rpop/
        // 'rpush as four separately-addressed F18 words -- see
        // CvmInstructionSet.LoadRegisterFileMnemonic's own remarks. A PRIOR pass of this same audit,
        // working from an intermediate re-sync of node 511's own source, had retired these four; that
        // retirement is reversed here.
        // REPOINTED 2026-09-27: node 511 is now pure passive register STORAGE with no F18 symbols of its
        // own opcode-relevant kind at all -- physical node 510's own new "extended register access" source
        // (reg/main) absorbs the whole family directly. Note these four symbols are NOT tick-prefixed in
        // Stefan's own new source (reg/ld/reg/st/reg/po/reg/pu) -- a departure from the usual "only a
        // leading ' makes a CVM opcode" naming rule, called out in Cvm.Node510Program's own remarks rather
        // than silently normalized here.
        [CvmInstructionSet.LoadRegisterFileMnemonic] = (Node510Program.Coordinate, "reg/ld", Node510RegisterAccessTag),
        [CvmInstructionSet.StoreRegisterFileMnemonic] = (Node510Program.Coordinate, "reg/st", Node510RegisterAccessTag),
        [CvmInstructionSet.PopRegisterFileMnemonic] = (Node510Program.Coordinate, "reg/po", Node510RegisterAccessTag),
        [CvmInstructionSet.PushRegisterFileMnemonic] = (Node510Program.Coordinate, "reg/pu", Node510RegisterAccessTag),
        // Three genuinely new ops from the same family (2026-09-27) -- see
        // CvmInstructionSet.RegisterClearMnemonic's own remarks. "rlit" is deliberately not wired here --
        // see CvmInstructionSet.RegisterSetMnemonic's own remarks for why.
        [CvmInstructionSet.RegisterClearMnemonic] = (Node510Program.Coordinate, "reg/clr", Node510RegisterAccessTag),
        [CvmInstructionSet.RegisterPopPairMnemonic] = (Node510Program.Coordinate, "reg/po2", Node510RegisterAccessTag),
        [CvmInstructionSet.RegisterPushPairMnemonic] = (Node510Program.Coordinate, "reg/pu2", Node510RegisterAccessTag),

        // Node 306's CURRENT six ops (2026-09-09, second pass of the opcode/assembler-vs-node
        // reconciliation audit against Stefan's own workspace.yaml export) -- tick-prefixed, node-
        // resolved (CvmOperandEncoding.None), sharing node 306's own flat "1101_10??_????_????" range.
        // Tag 0xD800 (binary 1101_1000) -- node 306's own RAM is only 64 words, so 0xD800-0xD83F has no
        // live collision with anything else wired here (the OLD self-describing family's own former
        // tags, 0xD800-0xDB00, are retired alongside Instructions' own Ids 101-106).
        //
        // CORRECTED 2026-09-09 (same day, later pass): node 306's own ar/main does NOT just mask the
        // call byte to select which compiled word to jump to -- it first pulls a 3-bit REGISTER-SELECT
        // field out of the low 3 bits (`dup 0x07 and 2* a!`) and only then shifts the remaining bits
        // right by 3 and masks to 6 bits (`2/ 2/ 2/ 0x3f and ex`) to get the actual jump target. A plain
        // `tag | resolvedAddress` (what every other None-shaped mnemonic here uses, and what this file
        // used for these six from when they were first wired until this correction) therefore encoded
        // the WRONG word: it left the resolved word address sitting in the low bits ar/main reads as the
        // register selector, instead of shifting it up into the function-select field -- see
        // NodeResolvedEmbeddedValueFieldLayoutByMnemonic's own remarks. CORRECTED 2026-09-11: these six
        // are node 306's REAL register-indexed ops, exactly the same NodeResolvedEmbeddedValue shape node
        // 308's/511's own families use just below -- node 306 genuinely has six 32-bit address registers
        // (0-5), and ar/main's own dispatch already carried a real 3-bit register-select field in its
        // incoming call byte's low bits all along (see CvmInstructionSet.ArithmeticStoreAddressRegisterMnemonic's
        // own remarks for the full correction). The stale "fixed at register 0, registers 1-3 unreachable"
        // workaround this comment used to describe (routing all six through a separate
        // NodeResolvedFixedRegisterShiftByMnemonic dictionary that hardcoded an embedded register index
        // of 0) is gone -- these now flow through the exact same generic NodeResolvedEmbeddedValue path
        // node 308/511 already use, register operand and all.
        // RENUMBERED 2026-09-15: this whole family moved from physical node 306 to physical node 308 --
        // Stefan: "node 306 and 308 have swapped roles." Coordinates below repointed from
        // Node306Program.Coordinate to Node308Program.Coordinate accordingly; the F18 symbol names and
        // shared tag (0xD800) are unchanged. arinc2/ardec2 are new (2026-09-15, "I gave up the 7th
        // register for 2 new opcodes") -- see CvmInstructionSet.ArithmeticIncrementAddressRegisterByTwoMnemonic's
        // own remarks for the flagged, unconfirmed "leap"/"then" F18 body shape this wiring does not
        // depend on.
        [CvmInstructionSet.ArithmeticIncrementAddressRegisterMnemonic] = (Node308Program.Coordinate, "'arinc", 0xD800),
        [CvmInstructionSet.ArithmeticDecrementAddressRegisterMnemonic] = (Node308Program.Coordinate, "'ardec", 0xD800),
        [CvmInstructionSet.ArithmeticIncrementAddressRegisterByTwoMnemonic] = (Node308Program.Coordinate, "'arinc2", 0xD800),
        [CvmInstructionSet.ArithmeticDecrementAddressRegisterByTwoMnemonic] = (Node308Program.Coordinate, "'ardec2", 0xD800),
        [CvmInstructionSet.ArithmeticLoadAddressRegisterMnemonic] = (Node308Program.Coordinate, "'arld", 0xD800),
        [CvmInstructionSet.ArithmeticStoreAddressRegisterMnemonic] = (Node308Program.Coordinate, "'arst", 0xD800),
        [CvmInstructionSet.LoadAddressRegisterValueMnemonic] = (Node308Program.Coordinate, "'lda", 0xD800),
        [CvmInstructionSet.StoreAddressRegisterValueMnemonic] = (Node308Program.Coordinate, "'sta", 0xD800),

        // REMOVED OUTRIGHT, 2026-09-15: node 308's old dpop/dpush/dinc/ddec/dadd/dor entries (added
        // 2026-09-09, tag Node308Tag/0xDC00) -- see CvmInstructionSet's own removal note above
        // FloatingPointRegisterFieldBitMask for why (Stefan's own direct instruction: "remove the old
        // dpop/dpush/dinc/ddec/dadd/dor family completely").

        // REMOVED OUTRIGHT, 2026-09-21: node 306's OLD 'fpop/'fpush entries (2026-09-15/16, tag
        // FloatingPointUnaryTag/0xDC00) and its four OLD constant-lookup entries (2026-09-17, tag
        // FloatingPointConstantLookupTag/0xDD00, all four resolving "fpr/const") -- REWORKED per Stefan's
        // own direct instruction into the unified, fully self-describing twelve-op family (see
        // CvmInstructionSet.FloatingPointAddMnemonic's own remarks). None of the twelve floating-point
        // mnemonics need an entry here any more: the new node 306 is a pure decoder with a fixed
        // per-operation tag, so nothing about them is resolved against a live node compile.

        // Node 405's nine ops (2026-09-09) -- BRAND NEW, tagged/node-resolved (CvmOperandEncoding.None).
        // Node 405's own mw/main has no bit cascade of its own -- a single dispatch word then an
        // immediate "ex" -- so it shares node 406's own "1110_1???" tag (0xE800) OR'd with each op's own
        // address on node 405, the same "tag | resolved local address" scheme every other simple
        // tagged/node-resolved family in this file uses.
        [CvmInstructionSet.ToggleCarryMnemonic] = (Node405Program.Coordinate, "'tgc", 0xE800),
        [CvmInstructionSet.AddWithCarryFlagMnemonic] = (Node405Program.Coordinate, "'adc", 0xE800),
        [CvmInstructionSet.LoadCarryMnemonic] = (Node405Program.Coordinate, "'ldc", 0xE800),
        [CvmInstructionSet.SetCarryMnemonic] = (Node405Program.Coordinate, "'sec", 0xE800),
        [CvmInstructionSet.ClearCarryMnemonic] = (Node405Program.Coordinate, "'clc", 0xE800),
        [CvmInstructionSet.StoreCarryMnemonic] = (Node405Program.Coordinate, "'stc", 0xE800),
        [CvmInstructionSet.SubtractWithCarryMnemonic] = (Node405Program.Coordinate, "'sbc", 0xE800),
        [CvmInstructionSet.RotateLeftMnemonic] = (Node405Program.Coordinate, "'rol", 0xE800),
        [CvmInstructionSet.RotateRightMnemonic] = (Node405Program.Coordinate, "'ror", 0xE800),

        // Node 505's 'fx (2026-09-09) -- BRAND NEW, tagged/node-resolved. Node 505's own f2/main also has
        // no bit cascade -- a single dispatch word then "ex" -- sharing node 506's own "1001_01??" tag
        // (0x9400) OR'd with 'fx's own address on node 505. Node 505's OTHER word, spelled 'f in its own
        // source, is deliberately NOT given an entry here -- see FrameExchangeMnemonic's own remarks for
        // the FLAGGED collision with FrameToRegisterMnemonic ("f") above, already wired to node 506.
        [CvmInstructionSet.FrameExchangeMnemonic] = (Node505Program.Coordinate, "'fx", 0x9400),

        // REMOVED OUTRIGHT, 2026-09-27: node 510's OLD "extended arithmetic" ops (2026-09-09,
        // 'addc/'xst/'xld/'xmul2/'xdiv2/'xumul, tag 0xB800) -- node 510's wholesale redesign into
        // "extended register access" no longer defines any of these six F18 symbols at all; see
        // CvmInstructionSet.AddWithCarryMnemonic's/ExtendedStoreMnemonic's own remarks for the retirement.
      };

  // INERT as of 2026-09-30 -- see NodeSymbolByMnemonic's own header note just above for why: no
  // NodeResolvedEmbeddedValue mnemonic survives the new VM's reset, so nothing looks this dictionary up
  // any more either. Kept, uncommented, for the same reason.
  /// <summary>
  /// Per-mnemonic field layout for <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/>
  /// mnemonics only (node 511's <c>rld</c>/<c>rst</c>/<c>rpop</c>/<c>rpush</c> and the address-register
  /// family's eight ops): unlike every other entry in <see cref="NodeSymbolByMnemonic"/>, this shape
  /// needs to know exactly WHERE within the resolved word its own "which function" field and its own
  /// embedded register-index operand each sit -- and each node's own layout genuinely differs (node
  /// 511's 5-bit/5-bit split is biased by <see cref="CvmInstructionSet.Node511FunctionFieldBaseAddress"/>;
  /// the address-register node's own 6-bit/3-bit split uses no bias at all) -- so this is a separate
  /// per-mnemonic lookup rather than a single shared set of constants <see cref="BuildDecodeTable"/>/
  /// <see cref="BuildEncodeTable"/> could hardcode once.
  /// (node 308's OLD <c>dpop</c>/<c>dpush</c>/<c>dinc</c>/<c>ddec</c>/<c>dadd</c>/<c>dor</c> family, which
  /// used to have entries here, was removed outright 2026-09-15 per Stefan's own direct instruction; node
  /// 306's OLD <c>fpop</c>/<c>fpush</c>/constant-lookup entries, added 2026-09-15/17, were REWORKED out of
  /// this dictionary entirely 2026-09-21 -- see <see cref="CvmInstructionSet.FloatingPointAddMnemonic"/>'s
  /// own remarks -- since the new floating-point family is fully self-describing and needs no live-node
  /// field layout at all.)
  /// </summary>
  private static readonly IReadOnlyDictionary<string, (int FunctionFieldBitMask, int FunctionFieldShift, int FunctionFieldBaseAddress, int RegisterFieldBitMask, int RegisterFieldShift)> NodeResolvedEmbeddedValueFieldLayoutByMnemonic =
      new Dictionary<string, (int, int, int, int, int)>(StringComparer.OrdinalIgnoreCase)
      {
        // REPOINTED 2026-09-27 from node 511's OLD 5-bit/5-bit layout (Node511FunctionField*/
        // Node511RegisterFieldBitMask, kept for historical reference in CvmInstructionSet) to the new
        // "extended register access" node's 6-bit/3-bit one -- the FIRST family in this dictionary whose
        // own register field needs a real RegisterFieldShift (4), since it no longer sits at bit 0.
        [CvmInstructionSet.LoadRegisterFileMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        [CvmInstructionSet.StoreRegisterFileMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        [CvmInstructionSet.PopRegisterFileMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        [CvmInstructionSet.PushRegisterFileMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        [CvmInstructionSet.RegisterClearMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        [CvmInstructionSet.RegisterPopPairMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        [CvmInstructionSet.RegisterPushPairMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        // REMOVED OUTRIGHT, 2026-09-15: node 308's old dpop/dpush/dinc/ddec/dadd/dor field-layout entries
        // (Node308FunctionFieldBitMask/Shift/BaseAddress/RegisterFieldBitMask) -- see CvmInstructionSet's
        // own removal note above FloatingPointRegisterFieldBitMask for why (Stefan's own direct
        // instruction).

        // REMOVED OUTRIGHT, 2026-09-21: node 306's OLD 'fpop/'fpush field-layout entries (2026-09-15/16)
        // and its four OLD constant-lookup field-layout entries (2026-09-17) -- REWORKED, along with
        // their NodeSymbolByMnemonic entries above, into the unified, fully self-describing twelve-op
        // family (see CvmInstructionSet.FloatingPointAddMnemonic's own remarks). None of the twelve
        // floating-point mnemonics need a field-layout entry here any more either: EmbeddedUnsignedValuePair
        // carries its own field layout right on each CvmInstructionShape row (ValueBitMask/SecondValueBitMask),
        // with no live-node-resolution component this dictionary exists to serve.

        // The address-register family's eight ops (arinc/ardec/arinc2/ardec2/arld/arst/lda/sta) -- ADDED
        // 2026-09-11, replacing the former NodeResolvedFixedRegisterShiftByMnemonic workaround this
        // dictionary used to leave them out of (see CvmInstructionSet.ArithmeticStoreAddressRegisterMnemonic's
        // own remarks for the full correction: this family genuinely has six 32-bit address registers,
        // 0-5, not the single hardwired one that workaround assumed). RENUMBERED 2026-09-15 from physical
        // node 306 to physical node 308 (see Cvm.Node308Program's own remarks) -- the field-layout
        // constants below were renamed from "Node306*" to "AddressRegisterFunctionField*"/
        // "AddressRegisterRegisterFieldBitMask" accordingly (values unchanged). A plain 0-based function
        // field, no bias, per ar/main's own "dup 0x07 and 2* a!" / "2/ 2/ 2/ 0x3f and ex" -- the same
        // general shape node 306's OLD 'fpop used to use too (now retired, see the removal note above).
        // arinc2/ardec2 (2026-09-15, "I gave up the 7th register for 2 new opcodes") share this exact
        // same layout.
        [CvmInstructionSet.ArithmeticIncrementAddressRegisterMnemonic] = (CvmInstructionSet.AddressRegisterFunctionFieldBitMask, CvmInstructionSet.AddressRegisterFunctionFieldShift, CvmInstructionSet.AddressRegisterFunctionFieldBaseAddress, CvmInstructionSet.AddressRegisterRegisterFieldBitMask, 0),
        [CvmInstructionSet.ArithmeticDecrementAddressRegisterMnemonic] = (CvmInstructionSet.AddressRegisterFunctionFieldBitMask, CvmInstructionSet.AddressRegisterFunctionFieldShift, CvmInstructionSet.AddressRegisterFunctionFieldBaseAddress, CvmInstructionSet.AddressRegisterRegisterFieldBitMask, 0),
        [CvmInstructionSet.ArithmeticIncrementAddressRegisterByTwoMnemonic] = (CvmInstructionSet.AddressRegisterFunctionFieldBitMask, CvmInstructionSet.AddressRegisterFunctionFieldShift, CvmInstructionSet.AddressRegisterFunctionFieldBaseAddress, CvmInstructionSet.AddressRegisterRegisterFieldBitMask, 0),
        [CvmInstructionSet.ArithmeticDecrementAddressRegisterByTwoMnemonic] = (CvmInstructionSet.AddressRegisterFunctionFieldBitMask, CvmInstructionSet.AddressRegisterFunctionFieldShift, CvmInstructionSet.AddressRegisterFunctionFieldBaseAddress, CvmInstructionSet.AddressRegisterRegisterFieldBitMask, 0),
        [CvmInstructionSet.ArithmeticLoadAddressRegisterMnemonic] = (CvmInstructionSet.AddressRegisterFunctionFieldBitMask, CvmInstructionSet.AddressRegisterFunctionFieldShift, CvmInstructionSet.AddressRegisterFunctionFieldBaseAddress, CvmInstructionSet.AddressRegisterRegisterFieldBitMask, 0),
        [CvmInstructionSet.ArithmeticStoreAddressRegisterMnemonic] = (CvmInstructionSet.AddressRegisterFunctionFieldBitMask, CvmInstructionSet.AddressRegisterFunctionFieldShift, CvmInstructionSet.AddressRegisterFunctionFieldBaseAddress, CvmInstructionSet.AddressRegisterRegisterFieldBitMask, 0),
        [CvmInstructionSet.LoadAddressRegisterValueMnemonic] = (CvmInstructionSet.AddressRegisterFunctionFieldBitMask, CvmInstructionSet.AddressRegisterFunctionFieldShift, CvmInstructionSet.AddressRegisterFunctionFieldBaseAddress, CvmInstructionSet.AddressRegisterRegisterFieldBitMask, 0),
        [CvmInstructionSet.StoreAddressRegisterValueMnemonic] = (CvmInstructionSet.AddressRegisterFunctionFieldBitMask, CvmInstructionSet.AddressRegisterFunctionFieldShift, CvmInstructionSet.AddressRegisterFunctionFieldBaseAddress, CvmInstructionSet.AddressRegisterRegisterFieldBitMask, 0),
      };

  /// <summary>
  /// ADDED 2026-09-30, for the new VM's <c>ret</c> (joined 2026-10-01 by <c>link</c>/<c>unlink</c>,
  /// the same node-506 bit-pattern-table family): per-mnemonic left-shift applied to a
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.None"/> mnemonic's resolved node address before it
  /// is OR'd into that mnemonic's tag in <see cref="BuildDecodeTable"/>/<see cref="BuildEncodeTable"/>.
  /// Every None-shaped mnemonic before <c>ret</c> (scall's own EmbeddedAddress shape aside -- scall
  /// isn't tagged/node-resolved at all) used the plain "tag | resolvedAddress" formula, address bits
  /// starting at bit 0 -- so this dictionary was never needed until now. <c>ret</c>'s own spec row
  /// ("1111|11ff|ffff|feee| implied", per Stefan) puts its 7-bit address field at bits 9-3 instead,
  /// with the bottom 3 bits ("eee") forced to 0 -- i.e. the resolved address must be shifted left by 3
  /// before it is OR'd into <see cref="Node506BitPatternTableTagBits"/>. A mnemonic absent from this
  /// dictionary gets shift 0, so every pre-existing None-shaped mnemonic's behavior is unchanged.
  /// ADDED 2026-10-01: link/unlink (Ids 185/186) join ret in node 506's bit-pattern-table family and
  /// need the same shift of 3.
  ///
  /// CORRECTED 2026-10-02, to 0 for all three: the full "CVM_pipeline" bit-pattern table (see
  /// CvmInstructionSet's own class-level remarks right after UnlinkMnemonic, and
  /// Node506BitPatternTableTagBits' own remarks just above) shows the "special" row's own address
  /// field sitting at bits 5-0, UNshifted -- the plain "tag | resolvedAddress" formula every
  /// pre-existing None-shaped mnemonic already used, before ret ever introduced this dictionary.
  /// Entries are kept here (set to 0) rather than removed outright, purely as the audit trail of which
  /// mnemonics were once thought to need a non-zero shift -- functionally identical to removing them,
  /// since a mnemonic absent from this dictionary already defaults to shift 0.
  /// </summary>
  private static readonly IReadOnlyDictionary<string, int> NodeResolvedAddressShiftByMnemonic =
      new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
      {
        [CvmInstructionSet.RetMnemonic] = 0,
        [CvmInstructionSet.LinkMnemonic] = 0,
        [CvmInstructionSet.UnlinkMnemonic] = 0,
        // ADDED 2026-10-02: lcall/ljmp (Ids 195/196) join this same "special" family -- see
        // NodeSymbolByMnemonic's own remarks on these two mnemonics for the full derivation. Listed
        // here, at shift 0, for the same audit-trail reason the other three are (a mnemonic absent
        // from this dictionary already defaults to 0, so this is not functionally required).
        [CvmInstructionSet.LongCallMnemonic] = 0,
        [CvmInstructionSet.LongJumpMnemonic] = 0,
      };

  // EMPTY as of 2026-09-30, NO LONGER EMPTY same day (see NodeSymbolByMnemonic's own header note
  // above for the ret exception) -- immediately after the full reset, CvmInstructionSet.Instructions
  // contained no None/TrailingWord/TwoTrailingWords/NodeResolvedEmbeddedValue shape at all (nop, the
  // one survivor, is CvmOperandEncoding.FixedOpcode instead -- excluded by this list's own .Where
  // filter below by design; scall/lcall, added later that same day, are EmbeddedAddress/
  // FixedOpcodeWithTrailingWord -- also excluded), so this list's own LINQ query evaluated to an empty
  // list. ret (Id 184, CvmOperandEncoding.None) is the first new-VM mnemonic to pass the filter: this
  // list now yields exactly that one row, resolved against node 506's live compile the same way every
  // pre-reset None-shaped mnemonic here used to be. BuildDecodeTable/BuildEncodeTable both iterate
  // this list, so both automatically pick ret up -- no further change was needed in either beyond the
  // address-shift lookup described on NodeResolvedAddressShiftByMnemonic.
  //
  // JOINED 2026-10-01 by link (Id 185, CvmOperandEncoding.TrailingWord) and unlink (Id 186,
  // CvmOperandEncoding.None) -- same node-506 family, same filter, same automatic pickup by
  // BuildDecodeTable/BuildEncodeTable; link's TrailingWord shape needs no special handling here
  // either, since this list (and both Build*Table methods) already treat TrailingWord as just
  // another tagged shape with a different WordLength, exactly as pushlit/gld/gst/litr already do.
  /// <summary>
  /// Every known CVM asm mnemonic THAT RESOLVES TO SOME NODE'S F18 SYMBOL, which node and symbol that
  /// is, and how many words (its own opcode word included) it occupies once assembled. <c>pushlit</c>,
  /// <c>lcall</c>/<c>ljmp</c>/<c>lbr</c>, <c>gld</c>/<c>gst</c>, and (2026-09-27) <c>litr</c> are the
  /// single-trailing-word instructions today; <c>litm</c>/<c>lit2</c> (also 2026-09-27) are the first
  /// two-trailing-word ones (<see cref="CvmInstructionSet.CvmOperandEncoding.TwoTrailingWords"/>) -- see
  /// <see cref="BuildEncodeTable"/>/<see cref="Assemble"/>/<see cref="DisassemblePage0"/>'s own remarks
  /// for where that second operand word is actually written/read. Extend
  /// <see cref="CvmInstructionSet"/> plus <see cref="NodeSymbolByMnemonic"/> as more tagged-dispatch
  /// opcodes are defined on any node; nothing else in this file needs to change. A shape whose
  /// <see cref="CvmInstructionSet.CvmInstructionShape.Encoding"/> is
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/> (<c>call</c>) or
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/> (<c>br</c>, <c>cbr</c> --
  /// the now-retired <c>slit</c> used to be a third example) has no F18 symbol by design and is
  /// filtered out here rather than added to
  /// <see cref="NodeSymbolByMnemonic"/> -- see this class's own remarks for why.
  ///
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/> (node 511's <c>rld</c>/
  /// <c>rst</c>/<c>rpop</c>/<c>rpush</c>) is included here too, alongside <c>None</c>/<c>TrailingWord</c>
  /// -- it still needs a live node/symbol resolved through <see cref="NodeSymbolByMnemonic"/> just like
  /// those two, even though its own word format also needs an embedded operand neither of them has. This
  /// tuple's own <c>Encoding</c> field is what lets <see cref="BuildEncodeTable"/>/
  /// <see cref="BuildDecodeTable"/> tell the three apart below.
  ///
  /// A tagged (<c>None</c>/<c>TrailingWord</c>/<c>NodeResolvedEmbeddedValue</c>) mnemonic that ISN'T in <see cref="NodeSymbolByMnemonic"/>
  /// is also filtered out here, rather than looked up with a throwing indexer -- a genuinely new
  /// mnemonic on a node this file hasn't been taught to pair yet. A throwing indexer here made the
  /// whole class fail to load the moment ANY such gap existed (a static field initializer that throws
  /// is fatal for the rest of the process), which is exactly what happened when <c>usl</c> etc. were
  /// first added to <see cref="CvmInstructionSet"/> without a matching entry here -- filtering instead
  /// of indexing keeps a mnemonic gap on one node from taking down every other node's disassembly.
  /// </summary>
  public static readonly IReadOnlyList<(string Mnemonic, int NodeCoordinate, string SymbolName, int Tag, int WordLength, bool HasOperand, CvmInstructionSet.CvmOperandEncoding Encoding)> Instructions =
      [.. CvmInstructionSet.Instructions
          .Where(shape => shape.Encoding is CvmInstructionSet.CvmOperandEncoding.None or CvmInstructionSet.CvmOperandEncoding.TrailingWord or CvmInstructionSet.CvmOperandEncoding.TwoTrailingWords or CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue)
          .Where(shape => NodeSymbolByMnemonic.ContainsKey(shape.Mnemonic))
          .Select(shape =>
          {
            (int nodeCoordinate, string symbolName, int tag) = NodeSymbolByMnemonic[shape.Mnemonic];
            return (shape.Mnemonic, nodeCoordinate, symbolName, tag, shape.WordLength, shape.HasOperand, shape.Encoding);
          })];

  /// <summary>
  /// One parsed line of CVM assembly. <see cref="Label"/> is the name defined on this line (a bare
  /// "label:" line with nothing else has an empty <see cref="Mnemonic"/> and exists purely to mark
  /// the address of whatever comes next -- see <see cref="ParseSource"/>'s own remarks), never both
  /// this and <see cref="OperandLabel"/> at once. <see cref="Operand"/> is set when the line's
  /// operand (if any) was already a literal number; <see cref="OperandLabel"/> is set instead when
  /// it was a label reference still waiting to be resolved to a literal by
  /// <see cref="Assemble"/>'s own label pass -- never both. <see cref="Operand2"/> (2026-09-16, node
  /// 306's six binary floating-point ops -- the first two-operand mnemonics in this file) is set only
  /// for a line with exactly two space-separated literal operands (Stefan's own "mnemonic f g" syntax,
  /// e.g. "fadd 3 2"); no label is (yet) supported in either position for a two-operand line, so
  /// there is no "OperandLabel2" counterpart.
  /// </summary>
  public sealed record CvmAsmInstruction(string Mnemonic, int? Operand, string? Label = null, string? OperandLabel = null, int? Operand2 = null);

  /// <summary>
  /// Resolves <see cref="Instructions"/> against THIS run's own compiles (never a frozen reference
  /// copy -- every address can move as any node's source evolves) and returns the decode direction: a
  /// map from a word's actual wire/memory VALUE to its mnemonic, word length, and (node 511's four ops
  /// only) the EMBEDDED register operand that word's own low bits already carry, for
  /// <see cref="CvmDebugSession.DisassemblePage0"/> to consume. Each mnemonic is looked up against its
  /// OWN node's compile (<see cref="NodeSymbolByMnemonic"/>) -- a mnemonic whose node isn't present in
  /// <paramref name="compiledRam"/> at all, or whose F18 symbol isn't defined in that node's current
  /// source, is simply omitted.
  ///
  /// <b>Node 511's four <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/>
  /// ops are the one exception to "one dictionary entry per mnemonic."</b> Every other mnemonic here
  /// resolves to exactly one WORD VALUE (tag | resolved address, with no further operand of its own), so
  /// one dictionary entry suffices. <c>rld</c>/<c>rst</c>/<c>rpop</c>/<c>rpush</c> each still resolve to
  /// one FUNCTION address, but that address only fixes bits 9-5 of the word -- bits 4-0 (the register
  /// index) vary independently, 0-31, so each of these four mnemonics needs 32 entries here, one per
  /// possible register value, each pre-computed with that register value baked in as its own
  /// <c>EmbeddedOperand</c> so <see cref="CvmDebugSession.DisassemblePage0"/> can print e.g. "rld 5"
  /// directly from a single dictionary lookup, with no trailing operand word to read (there isn't one --
  /// see <see cref="CvmInstructionSet.LoadRegisterFileMnemonic"/>'s own remarks).
  /// </summary>
  public static IReadOnlyDictionary<int, (string Mnemonic, int WordLength, int? EmbeddedOperand)> BuildDecodeTable(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    var table = new Dictionary<int, (string, int, int?)>();
    foreach ((string mnemonic, int nodeCoordinate, string symbolName, int tag, int wordLength, _, CvmInstructionSet.CvmOperandEncoding encoding) in Instructions)
    {
      if (!compiledRam.TryGetValue(nodeCoordinate, out F18CompileResult? compile))
      {
        continue;
      }

      if (!compile.Symbols.TryGetValue(symbolName, out F18ExportedSymbol? symbol))
      {
        continue;
      }

      // A CVM opcode is a 16-bit CVM word (CvmWordCodec.WordMask), not the wider 18-bit F18 wire
      // word the symbol's own address happens to be stored as. The tag depends on which node/opcode
      // class the mnemonic belongs to -- see NodeSymbolByMnemonic's own remarks -- never a flat
      // 0x8000 for every mnemonic.
      int resolvedAddress = symbol.Value & CvmWordCodec.WordMask;

      if (encoding == CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue)
      {
        // Node 511's rld/rst/rpop/rpush and (since 2026-09-09) node 308's dpop/dpush/dinc/ddec/dadd/dor --
        // see NodeResolvedEmbeddedValueFieldLayoutByMnemonic's own remarks for why each mnemonic's own
        // field layout is looked up individually rather than hardcoded. resolvedAddress here is WHICH
        // FUNCTION, not the whole opcode; it must fall inside this mnemonic's own function-field window
        // for this scheme to represent it at all. A symbol resolving outside that window (the node's own
        // source grew past its own reserved word count) is silently omitted, same as any other mnemonic
        // whose node/symbol doesn't resolve -- Stefan's own node source is never second-guessed here.
        if (!NodeResolvedEmbeddedValueFieldLayoutByMnemonic.TryGetValue(mnemonic, out (int FunctionFieldBitMask, int FunctionFieldShift, int FunctionFieldBaseAddress, int RegisterFieldBitMask, int RegisterFieldShift) layout))
        {
          continue;
        }

        int functionField = resolvedAddress - layout.FunctionFieldBaseAddress;
        if (functionField < 0 || functionField > (layout.FunctionFieldBitMask >> layout.FunctionFieldShift))
        {
          continue;
        }

        int baseOpcode = tag | (functionField << layout.FunctionFieldShift);
        for (int register = 0; register <= layout.RegisterFieldBitMask; register++)
        {
          table[baseOpcode | (register << layout.RegisterFieldShift)] = (mnemonic, wordLength, register);
        }

        continue;
      }

      // ADDED 2026-09-30, for ret alone: NodeResolvedAddressShiftByMnemonic left-shifts the resolved
      // address before it's OR'd into the tag -- see that dictionary's own remarks. Every mnemonic
      // absent from it (every None-shaped mnemonic before ret) gets shift 0, so this is a no-op for
      // them: unchanged "tag | resolvedAddress".
      int addressShift = NodeResolvedAddressShiftByMnemonic.TryGetValue(mnemonic, out int decodeShift) ? decodeShift : 0;
      table[tag | (resolvedAddress << addressShift)] = (mnemonic, wordLength, null);
    }

    return table;
  }

  /// <summary>
  /// The encode direction, for <see cref="Assemble"/>: resolves <see cref="Instructions"/> against
  /// THIS run's own compiles -- each mnemonic against its own node
  /// (<see cref="NodeSymbolByMnemonic"/>) -- and returns a map from mnemonic (case-insensitive) to its
  /// opcode word, word length, whether it takes an operand, and (node 511's four ops only) whether that
  /// operand gets EMBEDDED into the same opcode word rather than appended as a separate trailing word,
  /// plus the bit mask it's embedded under.
  ///
  /// For <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/> mnemonics
  /// specifically, <c>Opcode</c> here is only the BASE word (tag | function-select field) -- the
  /// register operand is still missing and gets OR'd in by <see cref="Assemble"/> itself once it knows
  /// the actual operand value; see this class's own remarks on <see cref="Node511Tag"/> for why this
  /// mnemonic needs a live resolution AND an embedded operand where every other tagged mnemonic here
  /// only ever needed one or the other.
  /// </summary>
  public static IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask, int EmbeddedValueShift)> BuildEncodeTable(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    var table = new Dictionary<string, (int, int, bool, bool, int, int)>(StringComparer.OrdinalIgnoreCase);
    foreach ((string mnemonic, int nodeCoordinate, string symbolName, int tag, int wordLength, bool hasOperand, CvmInstructionSet.CvmOperandEncoding encoding) in Instructions)
    {
      if (!compiledRam.TryGetValue(nodeCoordinate, out F18CompileResult? compile))
      {
        continue;
      }

      if (!compile.Symbols.TryGetValue(symbolName, out F18ExportedSymbol? symbol))
      {
        continue;
      }

      // A CVM opcode is a 16-bit CVM word (CvmWordCodec.WordMask), not the wider 18-bit F18 wire
      // word the symbol's own address happens to be stored as. The tag depends on which node/opcode
      // class the mnemonic belongs to -- see NodeSymbolByMnemonic's own remarks -- never a flat
      // 0x8000 for every mnemonic.
      int resolvedAddress = symbol.Value & CvmWordCodec.WordMask;

      if (encoding == CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue)
      {
        // See BuildDecodeTable's own remarks for the same per-mnemonic field-layout lookup and window
        // check.
        if (!NodeResolvedEmbeddedValueFieldLayoutByMnemonic.TryGetValue(mnemonic, out (int FunctionFieldBitMask, int FunctionFieldShift, int FunctionFieldBaseAddress, int RegisterFieldBitMask, int RegisterFieldShift) layout))
        {
          continue;
        }

        int functionField = resolvedAddress - layout.FunctionFieldBaseAddress;
        if (functionField < 0 || functionField > (layout.FunctionFieldBitMask >> layout.FunctionFieldShift))
        {
          continue;
        }

        int baseOpcode = tag | (functionField << layout.FunctionFieldShift);
        table[mnemonic] = (baseOpcode, wordLength, true, true, layout.RegisterFieldBitMask, layout.RegisterFieldShift);
        continue;
      }

      // ADDED 2026-09-30, for ret alone: see BuildDecodeTable's own remarks on
      // NodeResolvedAddressShiftByMnemonic -- same left-shift, same no-op for every mnemonic absent
      // from it.
      int addressShift = NodeResolvedAddressShiftByMnemonic.TryGetValue(mnemonic, out int encodeShift) ? encodeShift : 0;
      table[mnemonic] = (tag | (resolvedAddress << addressShift), wordLength, hasOperand, false, 0, 0);
    }

    return table;
  }

  /// <summary>
  /// CORRECTED 2026-09-11: Stefan reported "arld 1" silently assembling as a bare, operand-dropped
  /// <c>nop</c> even though node 306 is a real, wired opcode family (see <see cref="NodeSymbolByMnemonic"/>'s
  /// own node 306 entries) -- and firmly rejected the theory this session first reached for (node 307's
  /// own compile failing), so the real defect was in THIS file, not the node source. It was: <see cref="Assemble"/>'s
  /// own "undefined opcode -&gt; nop" fallback (this class's own remarks on that rule, "these opcodes have
  /// not been defined yet ... all undefined opcodes should generate a nop") only ever checked whether
  /// <see cref="CvmInstructionSet.TryGetShape"/> recognizes the mnemonic AT ALL -- it never distinguished
  /// a mnemonic that is genuinely, permanently unimplemented (no <see cref="NodeSymbolByMnemonic"/> entry
  /// at all -- CVM1's orphaned ALU ops, node 508's old comparison family, etc., where nop-substitution is
  /// exactly Stefan's own rule) from one that IS wired to a real node but simply failed to resolve THIS
  /// run (wrong node not compiled, symbol renamed/removed, or -- node 306/308/511's own
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/> shape only -- the
  /// resolved address falling outside that dispatch's own embeddable function-select window). The second
  /// case is a real, surprising failure a person needs to see and act on, not a deliberate design choice --
  /// silently reducing it to "nop" (worse: an operand-dropping nop, so "arld 1" and "arld" look identical
  /// once assembled) hid the actual problem behind what looked like ordinary, expected behavior. This
  /// method is <see cref="Assemble"/>'s new first check on that fallback path: it returns a precise,
  /// actionable reason when the mnemonic IS wired (so <see cref="Assemble"/> now fails loudly instead of
  /// nop-substituting), or null when it genuinely has no wiring at all (so <see cref="Assemble"/>'s
  /// existing nop-substitution keeps working exactly as before for those). Never called for a mnemonic
  /// <see cref="BuildEncodeTable"/> already resolved -- only for the ones it silently dropped.
  /// </summary>
  private static string? DiagnoseUnresolvedWiredMnemonic(string mnemonic, IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    // ADDED 2026-09-30, for the new VM's reset: NodeSymbolByMnemonic itself is now INERT (see its own
    // header note) but deliberately left un-emptied, so a mnemonic retired by this reset (e.g. "push")
    // still has a stale entry in it. Without this guard, such a mnemonic would wrongly be diagnosed as
    // "implemented on node NNN, but that node didn't compile this run" -- a real, wired-but-currently-
    // unresolved failure -- instead of the true story, "not a known CVM asm mnemonic any more." Checking
    // CvmInstructionSet.TryGetShape first (the live, authoritative Instructions table, which no longer
    // defines "push" at all) restores the correct fall-through to Assemble's own final
    // "is not a known CVM asm mnemonic" error for every retired mnemonic.
    if (CvmInstructionSet.TryGetShape(mnemonic) is null)
    {
      return null;
    }

    if (!NodeSymbolByMnemonic.TryGetValue(mnemonic, out (int NodeCoordinate, string SymbolName, int Tag) wiring))
    {
      // No live-node wiring at all -- a genuinely, permanently unimplemented mnemonic (CVM1 leftovers,
      // etc.). Stefan's own nop-substitution rule is exactly right for this case; let Assemble's existing
      // fallback handle it unchanged.
      return null;
    }

    if (!compiledRam.TryGetValue(wiring.NodeCoordinate, out F18CompileResult? compile))
    {
      return $"is implemented on node {wiring.NodeCoordinate:000}, but node {wiring.NodeCoordinate:000} did not compile (or wasn't included) this run -- fix/save it in the Node Editor, then re-assemble.";
    }

    if (!compile.Symbols.TryGetValue(wiring.SymbolName, out F18ExportedSymbol? symbol))
    {
      return $"is implemented on node {wiring.NodeCoordinate:000}, but that node's CURRENT source does not define \"{wiring.SymbolName}\" -- it may have been renamed or removed.";
    }

    if (NodeResolvedEmbeddedValueFieldLayoutByMnemonic.TryGetValue(mnemonic, out (int FunctionFieldBitMask, int FunctionFieldShift, int FunctionFieldBaseAddress, int RegisterFieldBitMask, int RegisterFieldShift) layout))
    {
      int resolvedAddress = symbol.Value & CvmWordCodec.WordMask;
      int functionField = resolvedAddress - layout.FunctionFieldBaseAddress;
      int maxFunctionField = layout.FunctionFieldBitMask >> layout.FunctionFieldShift;
      if (functionField < 0 || functionField > maxFunctionField)
      {
        return $"resolved to node {wiring.NodeCoordinate:000} address 0x{resolvedAddress:X}, which falls outside that node's own dispatch window (function-select field only reaches 0x0-0x{maxFunctionField:X} from base 0x{layout.FunctionFieldBaseAddress:X}) -- node {wiring.NodeCoordinate:000}'s source has likely grown too large, or \"{wiring.SymbolName}\" moved, for this opcode family's embedded-register-operand encoding.";
      }
    }

    // Resolved a real word/function field fine -- BuildEncodeTable should have added it. Reaching this
    // point means something unaccounted for is different between this diagnosis and BuildEncodeTable's
    // own logic; surface that plainly rather than pretending everything is fine.
    return $"resolved against node {wiring.NodeCoordinate:000}'s \"{wiring.SymbolName}\" but still could not be encoded, for a reason not covered by this diagnosis -- please report this as a toolchain bug.";
  }

  /// <summary>
  /// Assembles a sequence of CVM asm instructions into opcode/operand words. Two families of
  /// mnemonic are resolved completely differently, mirroring <see cref="CvmDebugSession.DisassemblePage0"/>'s
  /// own dual dispatch: <c>call</c>/<c>br</c>/<c>cbr</c> (the now-retired <c>slit</c> used to belong here too)
  /// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/>/<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>)
  /// are self-describing -- encoded directly from <see cref="CvmInstructionSet"/> and the operand
  /// alone, no live compile involved -- while every other mnemonic is resolved against THIS run's own
  /// compile of ITS OWN node (<see cref="NodeSymbolByMnemonic"/>) via <see cref="BuildEncodeTable"/>.
  ///
  /// <b>Undefined-but-real opcodes assemble as 'nop, per Stefan (2026-09-01) -- but ONLY when they have
  /// no live-node wiring at all.</b> A mnemonic that IS a genuine, named CVM opcode
  /// (<see cref="CvmInstructionSet.TryGetShape"/> finds a shape for it -- the full 73-opcode table, not
  /// just this file's own resolvable subset) AND has no entry at all in <see cref="NodeSymbolByMnemonic"/>
  /// -- every one of CVM1's now-orphaned mnemonics (the ALU ops including <c>inv</c>, node 606's
  /// <c>leave</c>, node 506/407's register ops, and CVM1's old node 508 comparison ops) -- is substituted
  /// with node 507's own current <c>'nop</c> opcode instead of failing the whole assemble: "these opcodes
  /// have not been defined yet and no longer have a meaning ... all undefined opcodes should generate a
  /// nop." Any operand supplied on that line is simply discarded (nop takes none).
  ///
  /// <b>CORRECTED 2026-09-11: a WIRED mnemonic that fails to resolve is a real error, never a silent
  /// nop.</b> Stefan hit exactly this gap directly: "arld 1" (node 306's <see cref="NodeSymbolByMnemonic"/>
  /// entry -- a real, wired opcode family) silently became a bare, operand-dropping <c>nop</c> when it
  /// failed to resolve, indistinguishable from a genuinely-unimplemented mnemonic hitting the SAME
  /// substitution -- see <see cref="DiagnoseUnresolvedWiredMnemonic"/>'s own remarks for the full
  /// incident and the fix: <see cref="DiagnoseUnresolvedWiredMnemonic"/> is now checked FIRST, before the
  /// nop-substitution path below, and fails the assemble outright with a specific reason (the node isn't
  /// compiled, the F18 symbol isn't defined in its current source, or -- <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/>
  /// mnemonics only -- the resolved address falls outside that node's own embeddable function-select
  /// window) whenever <see cref="NodeSymbolByMnemonic"/> has an entry for the mnemonic at all. Only a
  /// mnemonic with NO wiring whatsoever still gets the nop treatment, exactly as before.
  ///
  /// This only degrades gracefully for opcodes CvmInstructionSet actually knows about -- a genuinely
  /// unrecognized token (a typo, not a real CVM mnemonic at all) still fails the assemble below, since
  /// that is a different problem than "not implemented yet." Returns a null word list with a
  /// 1-based-line error message (never throws) when a mnemonic isn't recognized at all, a WIRED mnemonic
  /// fails to resolve (see above), node 507's own 'nop can't be resolved either for a genuinely orphaned
  /// mnemonic (nothing to substitute with), an operand is missing where one is required or out of range,
  /// a label operand is undefined or unsupported for that mnemonic, or an operand is supplied where none
  /// is allowed. This is what <see cref="CvmDebugSession.AssembleAndLoadProgram"/> uses to turn the CVM
  /// Debugger's own Assembly Code editor into a program loaded straight into the simulated SRAM.
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
  /// check below applies unchanged either way. <c>call</c> and every tagged mnemonic (<c>pushlit</c>
  /// -- the now-retired <c>slit</c> used to belong here too) resolve a label to its own ABSOLUTE word address; <c>br</c>/<c>cbr</c> resolve
  /// to a signed RELATIVE offset instead, since that is what their own opcode word actually encodes
  /// (see <see cref="ResolveOperandLabel"/>'s own remarks); node 606's eight
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/> ops don't accept a label
  /// operand at all, since their value is a frame-relative slot index/count, never an address.
  ///
  /// <c>Labels</c> (added 2026-09-26, for the CVM Debugger's own memory inspector "Label" column) is
  /// exactly the name -&gt; address map <see cref="CollectLabelAddresses"/>'s own pass 1 already computed
  /// to resolve label OPERANDS above -- simply handed back to the caller too on success, non-null (though
  /// possibly empty, for a source with no labels at all) whenever <c>Words</c> itself is non-null, and
  /// null on any failure exactly like <c>Words</c>.
  /// </summary>
  public static (List<int>? Words, IReadOnlyDictionary<string, int>? Labels, string? Error) Assemble(
      IReadOnlyList<CvmAsmInstruction> instructions,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask, int EmbeddedValueShift)> encodeTable = BuildEncodeTable(compiledRam);

    (IReadOnlyDictionary<string, int>? labelAddresses, string? labelError) = CollectLabelAddresses(instructions, encodeTable);
    if (labelAddresses is null)
    {
      return (null, null, labelError);
    }

    var words = new List<int>();
    for (int line = 0; line < instructions.Count; line++)
    {
      CvmAsmInstruction instruction = instructions[line];
      if (instruction.Mnemonic.Length == 0)
      {
        continue; // a bare "label:" line -- nothing of its own to assemble.
      }

      // "literal" (2026-09-27) does not support a label operand -- same restriction as "lit" itself (see
      // LiteralPseudoMnemonic's own remarks), and for a stronger reason here: this mnemonic's own WORD
      // COUNT depends on the operand's value (GetWordLength's own remarks), which a not-yet-resolved
      // label could only tell us AFTER every earlier label's address has already been fixed by
      // CollectLabelAddresses -- allowing one would risk every later label resolving to the wrong
      // address whenever the label's own resolved value happened to fit "lit"'s narrower range. Checked
      // before the generic OperandLabel resolution just below, so this never even reaches that step.
      if (instruction.OperandLabel is not null && string.Equals(instruction.Mnemonic, LiteralPseudoMnemonic, StringComparison.OrdinalIgnoreCase))
      {
        return (null, null, $"line {line + 1}: \"literal\" does not support a label operand -- its word count depends on the value, which must be known up front; supply a literal number instead, or use \"litr\" directly for a label's own address.");
      }

      if (instruction.OperandLabel is not null)
      {
        (int? resolvedOperand, string? resolveError) = ResolveOperandLabel(instruction, words.Count, labelAddresses, line + 1);
        if (resolveError is not null)
        {
          return (null, null, resolveError);
        }

        instruction = instruction with { Operand = resolvedOperand };
      }

      // "literal" (2026-09-27) -- intercepted here, before CvmInstructionSet.TryGetShape/encodeTable
      // lookups below, since it is a pure assembler-level pseudo-mnemonic with no CvmInstructionSet
      // shape or NodeSymbolByMnemonic entry of its own (see LiteralPseudoMnemonic's own remarks). By this
      // point instruction.Operand is guaranteed non-null-or-rejected by the label check just above and
      // EncodeLiteralPseudoMnemonic's own null check.
      if (string.Equals(instruction.Mnemonic, LiteralPseudoMnemonic, StringComparison.OrdinalIgnoreCase))
      {
        (List<int>? literalWords, string? literalError) = EncodeLiteralPseudoMnemonic(instruction, encodeTable, line + 1);
        if (literalWords is null)
        {
          return (null, null, literalError);
        }

        words.AddRange(literalWords);
        continue;
      }

      // "call" (2026-09-30, alongside the new VM's own "scall"/"lcall") -- intercepted here, right after
      // "literal", for the same fundamental reason: it is a pure assembler-level pseudo-mnemonic with no
      // CvmInstructionSet shape or NodeSymbolByMnemonic entry of its own (see
      // CvmInstructionSet.ShortCallMnemonic's own class-level remarks for the full derivation). Unlike
      // "literal", it runs AFTER the generic OperandLabel resolution just above, not before -- a label
      // operand is a perfectly normal, expected way to use "call" (calling a named subroutine is its whole
      // point), so it is resolved to a plain absolute address here exactly like "call"'s own OLD
      // EmbeddedAddress shape (and pushlit's trailing word) already resolve one, via ResolveOperandLabel's
      // own default branch. instruction.OperandLabel itself survives that resolution untouched (only
      // Operand is replaced), so EncodeCallPseudoMnemonic can still tell "this came from a label" apart
      // from "this was typed as a literal number" purely from whether OperandLabel is still set -- exactly
      // the distinction it needs to decide "always lcall" vs. "try to fit scall" (see its own remarks).
      if (string.Equals(instruction.Mnemonic, CvmInstructionSet.CallMnemonic, StringComparison.OrdinalIgnoreCase))
      {
        (List<int>? callWords, string? callError) = EncodeCallPseudoMnemonic(instruction, encodeTable, compiledRam, line + 1);
        if (callWords is null)
        {
          return (null, null, callError);
        }

        words.AddRange(callWords);
        continue;
      }

      // ADDED 2026-10-02, for the "CVM_pipeline" table's own cbr (CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord
      // -- see that enum case's own remarks, and CvmInstructionSet.ConditionalBranchMnemonic's own
      // remarks for why this shape's bit LAYOUT is confident but its field SEMANTICS are flagged).
      // Intercepted here, explicitly, rather than being let through to the generic self-describing
      // dispatch just below: this mnemonic needs THREE operands (cond, register, offset), but
      // CvmAsmInstruction (this file's own hand-typed-source grammar) supports at most two -- Operand/
      // Operand2 -- and Stefan has not specified cbr's own assembler-level syntax (e.g. whether cond
      // and register are really two separate typed numbers, or one combined token) to justify widening
      // that grammar yet. Rather than silently truncate an operand, misparse, or (worse) fall through
      // to this method's own "unimplemented opcode" nop-substitution (which would make "cbr ..." in
      // hand-typed source quietly assemble as a no-op, a far more dangerous silent failure), this fails
      // loudly and explains exactly why. Disassembly (TryDescribeSelfDecodingWord) is unaffected --
      // reading an already-encoded cbr word back out needs no grammar decision at all.
      if (string.Equals(instruction.Mnemonic, CvmInstructionSet.ConditionalBranchMnemonic, StringComparison.OrdinalIgnoreCase) &&
          CvmInstructionSet.TryGetShape(instruction.Mnemonic) is { Encoding: CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord })
      {
        return (null, null, $"line {line + 1}: \"cbr\" (the new CVM_pipeline opcode, not CVM2's old one) is not yet assemblable from hand-typed source here -- " +
            "it needs a 3-operand syntax (condition, register, offset) this grammar doesn't support yet, and its condition field's own real meaning is still unconfirmed; ask Stefan before wiring this up.");
      }

      CvmInstructionSet.CvmInstructionShape? selfDescribingShape = CvmInstructionSet.TryGetShape(instruction.Mnemonic);
      if (selfDescribingShape is { Encoding: CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress or CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue or CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue or CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePair or CvmInstructionSet.CvmOperandEncoding.FixedOpcode or CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTrailingWord or CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords })
      {
        (List<int>? selfDescribingWords, string? selfDescribingError) = EncodeSelfDescribingWord(selfDescribingShape, instruction.Operand, instruction.Operand2, line + 1);
        if (selfDescribingWords is null)
        {
          return (null, null, selfDescribingError);
        }

        words.AddRange(selfDescribingWords);
        continue;
      }

      if (!encodeTable.TryGetValue(instruction.Mnemonic, out (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask, int EmbeddedValueShift) entry))
      {
        // CORRECTED 2026-09-11 (see DiagnoseUnresolvedWiredMnemonic's own remarks for the full incident):
        // a mnemonic that IS wired to a live node (NodeSymbolByMnemonic has an entry for it) but simply
        // failed to resolve this run is a real, surprising failure -- fail loudly with a precise reason
        // instead of silently falling through to the nop-substitution meant for PERMANENTLY unimplemented
        // opcodes. Only a mnemonic with no wiring at all (DiagnoseUnresolvedWiredMnemonic returns null)
        // still gets the nop treatment below, exactly as before.
        string? wiredDiagnosis = DiagnoseUnresolvedWiredMnemonic(instruction.Mnemonic, compiledRam);
        if (wiredDiagnosis is not null)
        {
          return (null, null, $"line {line + 1}: \"{instruction.Mnemonic}\" {wiredDiagnosis}");
        }

        if (selfDescribingShape is not null)
        {
          // A genuine CVM opcode (CvmInstructionSet knows its shape) that just has no live node to
          // answer it right now -- per Stefan, substitute node 507's own current 'nop opcode rather
          // than failing the whole assemble. See this method's own remarks.
          if (!encodeTable.TryGetValue(NopMnemonic, out (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask, int EmbeddedValueShift) nopEntry))
          {
            return (null, null, $"line {line + 1}: \"{instruction.Mnemonic}\" has no defined opcode yet, and could not be " +
                $"substituted with \"{NopMnemonic}\" because node 507's current compile doesn't define \"'nop\" either.");
          }

          words.Add(nopEntry.Opcode);
          continue;
        }

        // A mnemonic that isn't a recognized CVM opcode at all -- a typo, not "not implemented yet" --
        // still fails outright rather than silently becoming a nop.
        return (null, null, $"line {line + 1}: \"{instruction.Mnemonic}\" is not a known CVM asm mnemonic.");
      }

      if (entry.HasOperand && instruction.Operand is null)
      {
        return (null, null, $"line {line + 1}: \"{instruction.Mnemonic}\" requires an operand, e.g. \"{instruction.Mnemonic} 0x1234\".");
      }

      if (!entry.HasOperand && instruction.Operand is not null)
      {
        return (null, null, $"line {line + 1}: \"{instruction.Mnemonic}\" does not take an operand.");
      }

      // litm/lit2 (2026-09-27, CvmInstructionSet.CvmOperandEncoding.TwoTrailingWords, WordLength 3) need
      // a SECOND trailing operand word too -- the checks just above already required and will validate
      // the first (instruction.Operand). Every other tagged mnemonic here still has WordLength 1 or 2, so
      // this is a no-op for them.
      if (entry.WordLength >= 3 && instruction.Operand2 is null)
      {
        return (null, null, $"line {line + 1}: \"{instruction.Mnemonic}\" requires two operands, e.g. \"{instruction.Mnemonic} 0x1234 0x5678\".");
      }

      if (entry.OperandIsEmbedded)
      {
        // Node 511's four ops only (see BuildEncodeTable's own remarks): the operand is a register index
        // packed directly into entry.Opcode's own low bits, never a separate trailing word.
        if (instruction.Operand!.Value < 0 || instruction.Operand!.Value > entry.EmbeddedValueMask)
        {
          return (null, null, $"line {line + 1}: {instruction.Operand!.Value} does not fit in \"{instruction.Mnemonic}\"'s embedded register operand (0..{entry.EmbeddedValueMask}).");
        }

        words.Add(entry.Opcode | ((instruction.Operand!.Value & entry.EmbeddedValueMask) << entry.EmbeddedValueShift));
        continue;
      }

      words.Add(entry.Opcode);
      if (entry.HasOperand)
      {
        words.Add(instruction.Operand!.Value & CvmWordCodec.WordMask);
        if (entry.WordLength >= 3)
        {
          // litm/lit2 only (see this method's own remarks just above) -- the second trailing word,
          // immediately after the first, same masking.
          words.Add(instruction.Operand2!.Value & CvmWordCodec.WordMask);
        }
      }
    }

    return (words, labelAddresses, null);
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
  ///
  /// <b>ADDED 2026-09-30 -- an optimistic, iterative sizing pass for "call" targeting a LABEL.</b> Per
  /// Stefan directly, right after "call"/"scall"/"lcall" were first wired up: "can you use a more
  /// optimistic smart iterator for sizing pass?" -- this assembler, unlike
  /// <see cref="Ga144.Cvm.Toolchain.CvmAssembler"/>, resolves every label to a real, final address itself
  /// (no separate link step, no relocations -- see this file's own remarks on why the two assemblers
  /// differ here), so it really CAN know whether a label-targeted "call" would fit scall, once the rest of
  /// the layout is known. The wrinkle is circular: "call"'s own word count affects every LATER label's
  /// address, but whether "call" fits scall depends on its OWN target label's address, which this very
  /// walk is computing. Resolved by starting OPTIMISTIC (guess every label-targeted "call" as the short
  /// form, scall, 1 word) and iterating: lay out the whole program under the current guesses, then check
  /// each optimistically-short "call" against the address that layout just gave its own target label --
  /// grow any that don't actually fit (or that resolve to address 0 -- <see cref="FitsShortCallRange"/>'s
  /// own remarks: "scall 0 does not exist"), and redo the walk. A guess only ever grows (1 -> 2), never
  /// shrinks back, because growing an EARLIER call can only push LATER addresses up, never down -- so once
  /// a call's own target address has grown past scall's range it can never come back into range on a later
  /// round. That makes this provably terminating (each round either grows at least one call or is the
  /// final, stable one; at most one call per line to grow) and gives the smallest correct sizing this
  /// assembler can produce without ever building on a wrong guess. A "call" with a LITERAL operand, or with
  /// no operand at all, is unaffected -- <see cref="GetWordLength"/> already sizes those directly, with no
  /// guessing needed, and is used as-is for every line this pass doesn't override.
  /// </summary>
  private static (IReadOnlyDictionary<string, int>? Labels, string? Error) CollectLabelAddresses(
      IReadOnlyList<CvmAsmInstruction> instructions,
      IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask, int EmbeddedValueShift)> encodeTable)
  {
    // Seed one optimistic guess (1 word, scall) per "call" line with a LABEL operand -- the only kind of
    // line whose own word count this pass ever needs to revise. A literal or missing operand is sized
    // directly by GetWordLength below, unconditionally, on every round -- it never depends on anything
    // this loop computes.
    var callWordLengthGuess = new Dictionary<int, int>();
    for (int line = 0; line < instructions.Count; line++)
    {
      if (instructions[line].OperandLabel is not null && string.Equals(instructions[line].Mnemonic, CvmInstructionSet.CallMnemonic, StringComparison.OrdinalIgnoreCase))
      {
        callWordLengthGuess[line] = 1;
      }
    }

    Dictionary<string, int> labels;
    int roundGuard = 0;
    while (true)
    {
      labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
          address += callWordLengthGuess.TryGetValue(line, out int guessedLength) ? guessedLength : GetWordLength(instruction, encodeTable);
        }
      }

      // Re-check every still-optimistic ("short") guess against the layout this round just produced --
      // an undefined label (pass 2's own concern, per this method's own remarks) also fails the fit check
      // below and simply grows to lcall here; the real "undefined label" error still comes from pass 2.
      bool grewAny = false;
      foreach (int line in callWordLengthGuess.Keys.ToList())
      {
        if (callWordLengthGuess[line] != 1)
        {
          continue;
        }

        string targetLabel = instructions[line].OperandLabel!;
        if (!labels.TryGetValue(targetLabel, out int targetAddress) || !FitsShortCallRange(targetAddress))
        {
          callWordLengthGuess[line] = 2;
          grewAny = true;
        }
      }

      if (!grewAny)
      {
        return (labels, null);
      }

      // Mathematically unreachable (each round that changes anything grows at least one line, and there
      // are at most callWordLengthGuess.Count of those to grow, ever) -- guarded anyway rather than ever
      // risking an infinite loop on a future change to this method.
      if (++roundGuard > instructions.Count + 2)
      {
        return (null, "internal error: \"call\" sizing did not converge -- please report this.");
      }
    }
  }

  /// <summary>
  /// How many words <paramref name="instruction"/>'s mnemonic occupies once assembled -- used by
  /// <see cref="CollectLabelAddresses"/> to compute label addresses BEFORE any operand (literal or
  /// label) is resolved, since word length never depends on the operand's actual value -- EXCEPT for the
  /// new <c>"literal"</c> pseudo-mnemonic below (2026-09-27), which is variable-length BY DESIGN (that's
  /// its whole point: pick the narrower encoding whenever the value allows it), so it alone needs the
  /// instruction's own already-parsed <see cref="CvmAsmInstruction.Operand"/>, not just its mnemonic
  /// name. Mirrors exactly what <see cref="Assemble"/>'s own pass 2 will actually emit for the same
  /// mnemonic, so the two passes can never disagree on an address: a self-describing shape (<c>call</c>/
  /// <c>br</c>/<c>cbr</c>/node 606's ops -- the now-retired <c>slit</c> used to belong here too) is
  /// always its own <see cref="CvmInstructionSet.CvmInstructionShape.WordLength"/>; a tagged mnemonic
  /// resolves through <paramref name="encodeTable"/> the same way pass 2 does; anything else -- a
  /// genuine opcode with no live node to answer it, which pass 2's own "undefined opcode -&gt; nop"
  /// substitution (see <see cref="Assemble"/>'s own remarks) always collapses to exactly one word
  /// regardless of the substituted opcode's real shape, or an outright unrecognized mnemonic pass 2 will
  /// reject outright -- is 1, a safe placeholder that never needs to be exact since pass 2 either matches
  /// it anyway or fails that very line before any address past it is ever used.
  ///
  /// <c>"literal"</c> itself: 1 word if <see cref="CvmAsmInstruction.Operand"/> is a plain number that
  /// fits <c>lit</c>'s signed range (pass 2 will emit <c>lit</c>), 2 otherwise (pass 2 will emit
  /// <c>litr</c>'s tag word plus its own trailing operand word) -- see
  /// <see cref="EncodeLiteralPseudoMnemonic"/>'s own remarks for the shared range check. A <c>"literal"</c>
  /// line with no resolved <see cref="CvmAsmInstruction.Operand"/> yet (a label operand, which
  /// <c>"literal"</c> does not support -- same restriction as <c>lit</c> itself, see that mnemonic's own
  /// remarks) defaults to the worst case, 2: pass 2 will reject that exact line outright before any
  /// address past it is ever used, so the guess here never needs to be exact for a SUCCESSFUL assembly,
  /// only safe for a failing one.
  ///
  /// <c>"call"</c> (2026-09-30, alongside the new VM's own <c>scall</c>/<c>lcall</c>) is also variable-
  /// length, but unlike <c>"literal"</c> it DOES support a label operand: a LITERAL operand gets the real
  /// scall/lcall fitting check right here (<see cref="FitsShortCallRange"/> -- NOT just "0x3FFF or under":
  /// Stefan later confirmed, same day, "scall 0 does not exist. 'nop' has precedence."), while a LABEL
  /// operand's own fitting decision is made by <see cref="CollectLabelAddresses"/>'s own iterative
  /// optimistic sizing pass instead (added 2026-09-30, per Stefan: "can you use a more optimistic smart
  /// iterator for sizing pass?") -- that pass calls this method only for lines it ISN'T overriding, so the
  /// "OperandLabel is not null" branch just below is a defensive fallback for a hypothetical caller that
  /// bypasses that pass, not something the real call path through <see cref="CollectLabelAddresses"/> ever
  /// actually reaches today.
  ///
  /// <c>lcall</c>'s own WordLength briefly (2026-10-02, for a few hours) read 1 here under a mistaken
  /// "encodes like 'ret, no operand" assumption -- CORRECTED THE SAME DAY once Stefan pointed at node
  /// 507's own live source ("the lcall and ljmp still have the address of the destination in the next
  /// word, so they still have 1 argument. fix that."): <c>lcall</c>/<c>ljmp</c> are
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/> (2 words), exactly like
  /// <c>link</c>, so this "2 words for the lcall case" answer is accurate again -- see
  /// <see cref="CvmInstructionSet.Instructions"/>' own remarks on Ids 195/196 for the fix.
  /// </summary>
  private static int GetWordLength(
      CvmAsmInstruction instruction,
      IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask, int EmbeddedValueShift)> encodeTable)
  {
    string mnemonic = instruction.Mnemonic;
    if (string.Equals(mnemonic, LiteralPseudoMnemonic, StringComparison.OrdinalIgnoreCase))
    {
      return instruction.Operand is int value && FitsLitRange(value) ? 1 : 2;
    }

    // "call" (2026-09-30) -- also variable-length by design, exactly like "literal" just above, but for
    // the OPPOSITE reason regarding a label operand: "literal" refuses one outright, while "call" is
    // expected to routinely take one (calling a named subroutine is its whole point). A literal operand is
    // sized directly, right here, via FitsShortCallRange. A label operand's real fitting decision is made
    // by CollectLabelAddresses's own iterative pass instead (see this method's own class-level remarks) --
    // the "always 2" answer just below is only ever reached as a defensive fallback, never on the real
    // CollectLabelAddresses call path.
    if (string.Equals(mnemonic, CvmInstructionSet.CallMnemonic, StringComparison.OrdinalIgnoreCase))
    {
      if (instruction.OperandLabel is not null)
      {
        return 2;
      }

      return instruction.Operand is int callValue && FitsShortCallRange(callValue) ? 1 : 2;
    }

    // ADDED 2026-10-02: FixedOpcodeWithTwoTrailingWords (next32) joins this generic, fixed-WordLength
    // group. EmbeddedUnsignedValuePairWithTrailingWord (cbr) is deliberately left OUT of this list --
    // Assemble's own explicit cbr interception (see its own remarks) fails that mnemonic outright
    // before word-length sizing would ever matter, so a wrong answer here could never actually ship.
    CvmInstructionSet.CvmInstructionShape? selfDescribingShape = CvmInstructionSet.TryGetShape(mnemonic);
    if (selfDescribingShape is { Encoding: CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress or CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue or CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue or CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePair or CvmInstructionSet.CvmOperandEncoding.FixedOpcode or CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTrailingWord or CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords })
    {
      return selfDescribingShape.WordLength;
    }

    return encodeTable.TryGetValue(mnemonic, out (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask, int EmbeddedValueShift) entry) ? entry.WordLength : 1;
  }

  /// <summary>
  /// Turns one instruction's <see cref="CvmAsmInstruction.OperandLabel"/> into the literal value
  /// <see cref="Assemble"/>'s own existing per-mnemonic encode logic already knows how to validate and
  /// pack, so that logic runs completely unchanged whether the source said a number or a label name.
  /// <c>call</c> (an <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/>) and every
  /// tagged mnemonic resolved via <paramref name="labelAddresses"/> alone (<c>pushlit</c>, the only one
  /// with a trailing-word operand today) resolve to the label's own ABSOLUTE word address -- so did the
  /// now-retired <c>slit</c>, even though it was an <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>
  /// like <c>br</c>/<c>cbr</c> below: loading a label's own address as a small signed literal was a
  /// legitimate use, even though that encoding's value isn't inherently an address (see its own
  /// remarks). <c>br</c>/<c>cbr</c> resolve to a signed RELATIVE offset instead, per
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
      // Node 606's eight ops take a frame-relative slot index/count; node 306's six take an address-
      // register index (0..3, ValueBitMask shifted right by ValueBitShift -- see
      // CvmInstructionSet.CvmInstructionShape.ValueBitShift's own remarks) -- neither is ever an
      // address, so a label operand is rejected outright for both the same way.
      int maxValue = shape.ValueBitMask >> shape.ValueBitShift;
      return (null, $"line {lineNumber}: \"{instruction.Mnemonic}\" does not support a label operand -- its value is not an address; supply a literal 0..{maxValue} value instead.");
    }

    if (shape is { Encoding: CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue })
    {
      // Node 511's four ops (register index 0-31) and node 308's six ops (register index 0-3) both take
      // a register index, never an address, for the same reason as node 606/node 306's ops just above --
      // the exact max value depends on which mnemonic's own field layout applies (see
      // NodeResolvedEmbeddedValueFieldLayoutByMnemonic's own remarks), so it is looked up per mnemonic
      // rather than assumed to always be node 511's own 5-bit range.
      int maxRegister = NodeResolvedEmbeddedValueFieldLayoutByMnemonic.TryGetValue(instruction.Mnemonic, out (int FunctionFieldBitMask, int FunctionFieldShift, int FunctionFieldBaseAddress, int RegisterFieldBitMask, int RegisterFieldShift) layout)
          ? layout.RegisterFieldBitMask
          : CvmInstructionSet.Node511RegisterFieldBitMask;
      return (null, $"line {lineNumber}: \"{instruction.Mnemonic}\" does not support a label operand -- its value is a register index, not an address; supply a literal 0..{maxRegister} value instead.");
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
  /// a parse/assemble failure. <c>Labels</c> is simply <see cref="Assemble"/>'s own <c>Labels</c> output
  /// passed straight through (added 2026-09-26, for the memory inspector's "Label" column).
  /// </summary>
  public static (List<int>? Words, IReadOnlyDictionary<string, int>? Labels, string? Error) AssembleAndLoadProgram(
      string sourceText,
      CvmSimulatedSram sram,
      IReadOnlyList<int> previousProgram,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    (List<CvmAsmInstruction>? instructions, string? parseError) = ParseSource(sourceText);
    if (instructions is null)
    {
      return (null, null, parseError);
    }

    (List<int>? words, IReadOnlyDictionary<string, int>? labels, string? assembleError) = Assemble(instructions, compiledRam);
    if (words is null)
    {
      return (null, null, assembleError);
    }

    int previousLength = previousProgram.Count;
    sram.LoadProgram(words);
    if (words.Count < previousLength)
    {
      sram.LoadProgram(new int[previousLength - words.Count], words.Count);
    }

    return (words, labels, null);
  }

  /// <summary>
  /// Shared by <see cref="CvmDebugSession.DisassemblePage0"/> and <see cref="ViewModels.CvmDebuggerViewModel"/>'s
  /// standalone path: linearly disassembles <paramref name="sram"/>'s page 0 from address 0 up to but
  /// not including <paramref name="endAddressExclusive"/>, into CVM assembly language mnemonics
  /// resolved against <paramref name="compiledRam"/>, plus direct bit-pattern rules for
  /// <c>call</c>/<c>br</c>/<c>cbr</c> (<see cref="CvmInstructionSet.TryDescribeSelfDecodingWord"/>) --
  /// the now-retired <c>slit</c> used to belong here too -- that need no compile/symbol at all. JOINED
  /// 2026-10-02 by the "CVM_pipeline" table's own <c>nop</c>/<c>scall</c>/<c>next16</c>/<c>next32</c>/
  /// <c>pop16</c>/<c>pop32</c>/<c>rjmp</c>/<c>rcall</c>/<c>sbr</c> -- all self-describing the same way,
  /// so <see cref="CvmInstructionSet.TryDescribeSelfDecodingWord"/> picks them up automatically with no
  /// change needed here beyond the <c>nextWord2</c> lookahead this method now also reads (for
  /// <c>next32</c>'s own second trailing word -- see that parameter's own remarks); the new table's own
  /// <c>cbr</c> is deliberately NOT assemblable yet (see <see cref="Assemble"/>'s own remarks) but DOES
  /// still disassemble correctly through this same path, reading being a strictly easier problem than
  /// writing here. This MUST be a stateful scan starting at 0, never an
  /// independent per-word decode: pushlit is followed by a literal operand word that would otherwise be
  /// mistaken for its own opcode if a word were decoded in isolation.
  ///
  /// Returns a sparse map from flat address to a listing line: an instruction's own address gets its
  /// mnemonic, folded together with its operand when it has one (e.g. "pushlit 0x1234 (4660)") so the
  /// memory inspector reads like a real disassembly rather than two disconnected rows; the operand word's
  /// own address is left out of the map entirely (no note at all), same as any other address that doesn't
  /// fall on a recognized instruction boundary -- typically because it holds an opcode this debugger
  /// doesn't know about yet. The operand itself is rendered by <see cref="CvmInstructionSet.FormatOperand"/>
  /// -- fixed 2026-09-05 per Stefan so a tagged trailing-word mnemonic (e.g. <c>andi</c>) shows the SAME
  /// "0x{hex} ({decimal})" shape <see cref="CvmInstructionSet.TryDescribeSelfDecodingWord"/> now uses for
  /// every self-describing mnemonic (<c>call</c>/<c>br</c>/<c>cbr</c>/<c>lit</c>), rather
  /// than the hex-only rendering this one line used before -- see that method's own remarks. ADDED
  /// 2026-09-09: a <c>br</c>/<c>cbr</c> line now also carries its own RESOLVED absolute target (e.g.
  /// <c>"br 0x0200 (512) -> 0x0202"</c>) -- see <see cref="CvmInstructionSet.TryDescribeSelfDecodingWord"/>'s
  /// own remarks for why (Stefan misread a raw offset as an absolute address; this prevents that).
  /// </summary>
  public static IReadOnlyDictionary<int, string> DisassemblePage0(
      CvmSimulatedSram sram,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam,
      int endAddressExclusive)
  {
    IReadOnlyDictionary<int, (string Mnemonic, int WordLength, int? EmbeddedOperand)> decodeTable = BuildDecodeTable(compiledRam);
    var notes = new Dictionary<int, string>();
    int address = 0;
    while (address < endAddressExclusive)
    {
      int word = sram.Read(CvmMemoryProtocol.CombineAddress(0, address));

      // "scall", "br", and "cbr" have no F18 symbol to resolve -- each one's whole word is
      // fully determined by its own bit pattern and operand alone (CvmInstructionSet.
      // CvmOperandEncoding.EmbeddedAddress / EmbeddedSignedValue), independent of node 607's live
      // compile, so all three are checked before consulting the (symbol-driven) decode table at all.
      // (the now-retired "slit" used to be a fourth self-describing mnemonic here; CVM2's OLD "call"
      // used to be a fifth, in scall's own place, before this same 2026-09-30 reset.)
      // wordAddress passed through 2026-09-09 so br/cbr's own listing line can also show the
      // RESOLVED absolute target next to the raw offset -- see TryDescribeSelfDecodingWord's own
      // remarks (Stefan asked why the linker put "__exit" at "location 0x200"; it doesn't -- that
      // was always just the raw offset field, easy to misread as an address in this exact listing).
      // nextWord (ADDED 2026-09-30, alongside "lcall") is a one-word lookahead, read only when there IS
      // a next word in range -- needed purely so TryDescribeSelfDecodingWord can print lcall's own real
      // operand rather than just its bare tag-word mnemonic; wordLength (also new) tells this loop
      // whether it just consumed one word (every earlier self-describing shape) or two (lcall).
      // nextWord2 (ADDED 2026-10-02, alongside "next32") is a SECOND word of lookahead, same
      // in-range-or-null convention, needed only so next32's own full 32-bit literal prints both
      // halves rather than just the first.
      int? nextWord = address + 1 < endAddressExclusive ? sram.Read(CvmMemoryProtocol.CombineAddress(0, address + 1)) : null;
      int? nextWord2 = address + 2 < endAddressExclusive ? sram.Read(CvmMemoryProtocol.CombineAddress(0, address + 2)) : null;
      string? selfDescribing = CvmInstructionSet.TryDescribeSelfDecodingWord(word, out int selfDescribingWordLength, wordAddress: address, nextWord: nextWord, nextWord2: nextWord2);
      if (selfDescribing is not null)
      {
        notes[address] = selfDescribing;
        address += selfDescribingWordLength;
        continue;
      }

      if (decodeTable.TryGetValue(word, out (string Mnemonic, int WordLength, int? EmbeddedOperand) instruction))
      {
        if (instruction.EmbeddedOperand is int embeddedOperand)
        {
          // Node 511's four ops only: the operand already lives in the word's own low bits (that's how
          // this exact dictionary entry was found at all -- see BuildDecodeTable's own remarks), never a
          // trailing word, so there's no second word to read here.
          notes[address] = $"{instruction.Mnemonic} {CvmInstructionSet.FormatOperand(embeddedOperand)}";
          address += instruction.WordLength;
          continue;
        }

        int operandCount = instruction.WordLength - 1;
        if (operandCount == 1 && address + 1 < endAddressExclusive)
        {
          int operandValue = sram.Read(CvmMemoryProtocol.CombineAddress(0, address + 1));
          notes[address] = $"{instruction.Mnemonic} {CvmInstructionSet.FormatOperand(operandValue)}";
        }
        else if (operandCount == 2 && address + 2 < endAddressExclusive)
        {
          // litm/lit2 only (2026-09-27, CvmInstructionSet.CvmOperandEncoding.TwoTrailingWords) -- two
          // trailing operand words, printed in the same "mnemonic first second" shape
          // TryDescribeSelfDecodingWord already uses for EmbeddedUnsignedValuePair (e.g. "fadd 3 2").
          int firstOperandValue = sram.Read(CvmMemoryProtocol.CombineAddress(0, address + 1));
          int secondOperandValue = sram.Read(CvmMemoryProtocol.CombineAddress(0, address + 2));
          notes[address] = $"{instruction.Mnemonic} {CvmInstructionSet.FormatOperand(firstOperandValue)} {CvmInstructionSet.FormatOperand(secondOperandValue)}";
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
  /// True when <paramref name="value"/> fits <c>lit</c>'s own signed <see cref="CvmInstructionSet.CvmInstructionShape.ValueBitMask"/>
  /// range -- shared by <see cref="GetWordLength"/> and <see cref="EncodeLiteralPseudoMnemonic"/> so the
  /// two can never disagree on which of "lit"/"litr" a given value actually needs. Computed straight off
  /// <c>lit</c>'s own <see cref="CvmInstructionSet.CvmInstructionShape"/> (never a hardcoded -256..255)
  /// so a future change to <c>lit</c>'s own bit width is picked up automatically.
  /// </summary>
  private static bool FitsLitRange(int value)
  {
    // GUARD added 2026-09-30: "lit" was retired in the new VM's reset (see CvmInstructionSet.
    // Instructions' own remarks at the top of its list) -- TryGetShape now returns null for it, where
    // this used to unconditionally assume a shape existed (the "!" null-forgiving operator would
    // otherwise crash with a NullReferenceException the moment anything called GetWordLength/
    // EncodeLiteralPseudoMnemonic on a "literal" line). Returning false routes the caller down its own
    // "too big for lit" / litr path instead, which EncodeLiteralPseudoMnemonic's own guard turns into a
    // clean, specific error rather than a crash.
    CvmInstructionSet.CvmInstructionShape? litShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.LitMnemonic);
    if (litShape is null)
    {
      return false;
    }

    int maxValue = litShape.ValueBitMask >> 1;
    int minValue = -(maxValue + 1);
    return value >= minValue && value <= maxValue;
  }

  /// <summary>
  /// True when <paramref name="value"/> fits <c>scall</c>'s actually-usable address range -- 1..
  /// <see cref="CvmInstructionSet.ShortCallAddressMask"/>, NOT 0..<see cref="CvmInstructionSet.ShortCallAddressMask"/>.
  /// Address 0 is excluded on purpose: CONFIRMED 2026-09-30 per Stefan directly ("scall 0 does not exist.
  /// 'nop' has precedence."), because the word <c>scall 0</c> would produce (0x0000) is bit-for-bit
  /// identical to <c>nop</c>'s own encoding, and <c>nop</c> wins that overlap. Shared by every "call"-family
  /// caller in this file -- <see cref="CollectLabelAddresses"/>'s iterative sizing pass, <see cref="GetWordLength"/>,
  /// and <see cref="EncodeCallPseudoMnemonic"/> -- so none of them can ever disagree on whether a given
  /// resolved address needs one word (<c>scall</c>) or two (<c>lcall</c>). Mirrors <see cref="FitsLitRange"/>'s
  /// own precedent of a small private per-file duplicate rather than sharing code with <c>CvmAssembler.cs</c>'s
  /// own copy of the same test.
  /// </summary>
  private static bool FitsShortCallRange(int value) =>
      value >= CvmInstructionSet.ShortCallMinimumAddress && value <= CvmInstructionSet.ShortCallAddressMask;

  /// <summary>
  /// Encodes the <c>"literal"</c> pseudo-mnemonic (2026-09-27, per Stefan: "change opcode 'literal' so
  /// that it uses 'lit' when the constant fits and 'litr' if the constant is too big for 'lit'") --
  /// returns either <c>lit</c>'s own one-word self-describing encoding (<see cref="FitsLitRange"/>) or
  /// <c>litr</c>'s own two-word tag-plus-trailing-operand encoding, resolved against
  /// <paramref name="encodeTable"/> exactly like any other node-508 mnemonic (see
  /// <see cref="CvmInstructionSet.LitrMnemonic"/>'s own remarks) -- never a bare <c>0x8000 | Id</c>
  /// placeholder, since this file's assembler resolves immediately rather than deferring to a linker.
  /// <see cref="Assemble"/>'s own remarks cover why a label operand is rejected before this is ever
  /// called; by the time it runs, <paramref name="instruction"/>.Operand is the only operand form left to
  /// handle.
  /// </summary>
  private static (List<int>? Words, string? Error) EncodeLiteralPseudoMnemonic(
      CvmAsmInstruction instruction,
      IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask, int EmbeddedValueShift)> encodeTable,
      int lineNumber)
  {
    if (instruction.Operand is not int value)
    {
      return (null, $"line {lineNumber}: \"literal\" requires a literal numeric operand, e.g. \"literal 1234\".");
    }

    // ADDED 2026-09-30: "lit"/"litr" were both retired in the new VM's reset (see CvmInstructionSet.
    // Instructions' own remarks at the top of its list) -- "literal" has nothing left to lower to.
    // Checked before FitsLitRange even runs (see that method's own guard, which returns false rather
    // than crashing for the same reason) so this fails with one clear, specific message instead of a
    // confusing "litr is not available right now" one two branches down, or an outright crash.
    if (CvmInstructionSet.TryGetShape(CvmInstructionSet.LitMnemonic) is null)
    {
      return (null, $"line {lineNumber}: \"literal\" is not supported -- \"lit\"/\"litr\" were both retired in the new VM's 2026-09-30 reset (see CvmInstructionSet.Instructions' own remarks); \"nop\" is the only valid opcode right now.");
    }

    if (FitsLitRange(value))
    {
      CvmInstructionSet.CvmInstructionShape litShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.LitMnemonic)!;
      return ([litShape.Tag | (value & litShape.ValueBitMask)], null);
    }

    if (!encodeTable.TryGetValue(CvmInstructionSet.LitrMnemonic, out (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask, int EmbeddedValueShift) litrEntry))
    {
      CvmInstructionSet.CvmInstructionShape shape = CvmInstructionSet.TryGetShape(CvmInstructionSet.LitMnemonic)!;
      int maxValue = shape.ValueBitMask >> 1;
      int minValue = -(maxValue + 1);
      return (null, $"line {lineNumber}: {value} does not fit in \"lit\"'s signed value ({minValue}..{maxValue}), and \"litr\" is not available right now (node 508 has no live compile defining \"'litr\").");
    }

    return ([litrEntry.Opcode, value & CvmWordCodec.WordMask], null);
  }

  /// <summary>
  /// Encodes one <c>scall</c>/<c>br</c>/<c>cbr</c>/node-606 instruction's word(s) directly from
  /// <paramref name="shape"/> and its literal operand (CVM2's OLD <c>call</c>, now retired, and the
  /// now-retired <c>slit</c> used to belong here too) -- the same arithmetic
  /// <see cref="CvmAssembler.EmitEmbeddedSignedValue"/>/<see cref="CvmAssembler.EmitEmbeddedUnsignedValue"/>
  /// use for <c>br</c>/<c>cbr</c> and node 606's eight ops respectively (mask-derived
  /// min/max, tag OR'd with the value's low bits) and <see cref="CvmAssembler"/>'s own
  /// generalized <c>EmbeddedAddress</c> case uses for <c>scall</c>, kept as a small duplicate here rather
  /// than shared: that assembler resolves a label/import operand through relocations against a
  /// <see cref="CvmObjectFile"/>, deferred all the way to a linker, which has no place in this simpler,
  /// immediately-loaded assembler -- this file's own label support (see <see cref="Assemble"/>'s own
  /// remarks) resolves a label to a plain literal <c>int</c> BEFORE this method is ever called, so from
  /// here a label-derived operand and a hand-typed one are indistinguishable.
  ///
  /// Returns a WORD LIST, not a single word (CHANGED 2026-09-30, alongside <c>lcall</c>'s own addition):
  /// every branch here still returns exactly one word except
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTrailingWord"/> (<c>lcall</c>), the
  /// first shape in this file ever needing two -- its own tag word, already fully known from
  /// <paramref name="shape"/>.Tag alone, plus a full trailing operand word carrying the actual resolved
  /// address.
  /// </summary>
  private static (List<int>? Words, string? Error) EncodeSelfDescribingWord(CvmInstructionSet.CvmInstructionShape shape, int? operand, int? operand2, int lineNumber)
  {
    if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePair)
    {
      // Node 306/305's TEN two-operand floating-point ops (2026-09-16, widened from six to twelve in the
      // 2026-09-21 rework, then narrowed back to ten the same day once Stefan corrected fpop/fpush to
      // one-operand mnemonics -- see the EmbeddedUnsignedValue branch below, which those two now use
      // instead): the ONLY two-operand shape in this file -- Stefan's own "mnemonic f g"
      // syntax, e.g. "fadd 3 2" means fr[3] = fr[3] + fr[2]. Mirrors
      // CvmAssembler.EmitEmbeddedUnsignedValuePair's own per-field validate/shift/OR pattern exactly
      // (kept as a small duplicate here per this method's own class-level remarks on why the two
      // assemblers don't share code).
      if (operand is not int first || operand2 is not int second)
      {
        return (null, $"line {lineNumber}: \"{shape.Mnemonic}\" requires exactly two literal operands, e.g. \"{shape.Mnemonic} 3 2\".");
      }

      int firstMaxValue = shape.ValueBitMask >> shape.ValueBitShift;
      if (first < 0 || first > firstMaxValue)
      {
        return (null, $"line {lineNumber}: {first} does not fit in \"{shape.Mnemonic}\"'s first (register) operand (0..{firstMaxValue}).");
      }

      int secondMaxValue = shape.SecondValueBitMask >> shape.SecondValueBitShift;
      if (second < 0 || second > secondMaxValue)
      {
        return (null, $"line {lineNumber}: {second} does not fit in \"{shape.Mnemonic}\"'s second (register) operand (0..{secondMaxValue}).");
      }

      return ([shape.Tag | ((first << shape.ValueBitShift) & shape.ValueBitMask) | ((second << shape.SecondValueBitShift) & shape.SecondValueBitMask)], null);
    }

    if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.FixedOpcode)
    {
      // ADDED 2026-09-30, for the new VM's reset (nop, the one surviving mnemonic -- see
      // CvmInstructionSet.Instructions' own remarks at the top of its list, and
      // CvmOperandEncoding.FixedOpcode's own remarks). No operand at all: the whole word is already
      // fully known from shape.Tag alone, so unlike every other branch in this method there is nothing
      // to validate against a value -- only that the caller didn't also try to give it one.
      if (operand is not null || operand2 is not null)
      {
        return (null, $"line {lineNumber}: \"{shape.Mnemonic}\" does not take an operand.");
      }

      return ([shape.Tag], null);
    }

    if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTrailingWord)
    {
      // ADDED 2026-09-30, for the new VM's own lcall -- see CvmOperandEncoding.FixedOpcodeWithTrailingWord's
      // own remarks. shape.Tag is already fully known (no node, no relocation for it, exactly like nop
      // just above), but unlike nop a real operand DOES follow: the resolved target address, already a
      // plain int by this point (a label operand is resolved to one before this method is ever called,
      // same as every other branch here).
      if (operand is not int lcallTarget)
      {
        return (null, $"line {lineNumber}: \"{shape.Mnemonic}\" requires an operand, e.g. \"{shape.Mnemonic} 0x1234\" or \"{shape.Mnemonic} loop\".");
      }

      return ([shape.Tag, lcallTarget & CvmWordCodec.WordMask], null);
    }

    if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords)
    {
      // ADDED 2026-10-02, for the "CVM_pipeline" table's own next32 -- see
      // CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords' own remarks. shape.Tag is already fully
      // known (no node, no relocation, same as next16/lcall just above), but TWO trailing operand
      // words follow instead of one. Syntax mirrors litm/lit2's own two-separate-words convention
      // (see this file's own remarks on CvmOperandEncoding.TwoTrailingWords): "next32 0x1234 0x5678",
      // not a single 32-bit value split in half -- each word resolves independently, exactly like
      // litm/lit2's own Operand/Operand2.
      if (operand is not int next32First || operand2 is not int next32Second)
      {
        return (null, $"line {lineNumber}: \"{shape.Mnemonic}\" requires two operands, e.g. \"{shape.Mnemonic} 0x1234 0x5678\".");
      }

      return ([shape.Tag, next32First & CvmWordCodec.WordMask, next32Second & CvmWordCodec.WordMask], null);
    }

    if (operand is not int value)
    {
      return (null, $"line {lineNumber}: \"{shape.Mnemonic}\" requires a literal operand, e.g. \"{shape.Mnemonic} 1\".");
    }

    if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress)
    {
      // GENERALIZED 2026-09-30, for the new VM's own scall (CVM2's OLD call used to be the only mnemonic
      // here, hardcoded to its own 15-bit CallAddressMask) -- the field width is now read straight off
      // shape.ValueBitMask (0x3FFF/14 bits for scall), mirroring CvmAssembler's own matching
      // generalization, so a future EmbeddedAddress mnemonic with yet another width needs no change here.
      if ((uint)value > (uint)shape.ValueBitMask)
      {
        return (null, $"line {lineNumber}: {value} does not fit in \"{shape.Mnemonic}\"'s {System.Numerics.BitOperations.PopCount((uint)shape.ValueBitMask)}-bit target (0x0000-0x{shape.ValueBitMask:X4}).");
      }

      // ADDED 2026-09-30, CONFIRMED per Stefan directly: "scall 0 does not exist. 'nop' has precedence."
      // A literal "scall 0" would otherwise silently encode as 0x0000 -- bit-for-bit identical to nop's own
      // encoding -- so it is rejected here as a hard error rather than emitted. Mirrors CvmAssembler.cs's
      // own equivalent check on its "call" pass-2 branch. Only "scall" itself needs this guard: a bare
      // "call 0" never reaches here as scall in the first place, since EncodeCallPseudoMnemonic's own
      // FitsShortCallRange test already routes value 0 to "lcall" before this method is ever called.
      if (string.Equals(shape.Mnemonic, CvmInstructionSet.ShortCallMnemonic, StringComparison.Ordinal) && value == 0)
      {
        return (null, $"line {lineNumber}: \"scall 0\" does not exist -- address 0 is reserved for \"nop\" (the word scall would produce, 0x0000, is identical to nop's own); use \"lcall 0\" instead.");
      }

      // DEFENSIVE, added 2026-09-30 during review: scall's own Tag happens to be 0x0000 today, so
      // "| shape.Tag" is currently a no-op -- but omitting it here would silently drop a future
      // EmbeddedAddress mnemonic's tag bits if one is ever added with a nonzero Tag (and TryDescribeSelfDecodingWord's
      // own decode side already masks against Tag, so a missing Tag here would fail to round-trip).
      return ([shape.Tag | (value & shape.ValueBitMask)], null);
    }

    if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue)
    {
      // Node 606's eight ops (ValueBitShift 0) and node 306's six address-register ops (ValueBitShift 1,
      // a 2-bit register index at bits 2-1 rather than bit 0 upward -- see
      // CvmInstructionSet.CvmInstructionShape.ValueBitShift's own remarks): unsigned
      // 0..(ValueBitMask >> ValueBitShift), never a negative half -- unlike the signed case just below,
      // so no min/max split is needed here. This mirrors CvmAssembler.EmitEmbeddedUnsignedValue exactly
      // (kept as a small duplicate here per this method's own remarks). Node 306's fpop/fpush (CORRECTED
      // 2026-09-21, per Stefan: "only 1 parameter") ALSO fall in here now -- a second operand, if the
      // caller types one, is simply never looked at by this branch (see this method's own signature,
      // which takes operand2 only for the Pair branch above).
      int unsignedMaxValue = shape.ValueBitMask >> shape.ValueBitShift;
      if (value < 0 || value > unsignedMaxValue)
      {
        return (null, $"line {lineNumber}: {value} does not fit in \"{shape.Mnemonic}\"'s unsigned value (0..{unsignedMaxValue}).");
      }

      return ([shape.Tag | ((value << shape.ValueBitShift) & shape.ValueBitMask)], null);
    }

    int maxValue = shape.ValueBitMask >> 1;
    int minValue = -(maxValue + 1);
    if (value < minValue || value > maxValue)
    {
      return (null, $"line {lineNumber}: {value} does not fit in \"{shape.Mnemonic}\"'s signed value ({minValue}..{maxValue}).");
    }

    return ([shape.Tag | (value & shape.ValueBitMask)], null);
  }

  /// <summary>
  /// Encodes the <c>"call"</c> pseudo-mnemonic (2026-09-30, alongside the new VM's own <c>scall</c>/
  /// <c>lcall</c>, per Stefan: "'call' should try to fit the address into a 'scall' and use 'lcall' if the
  /// address does not fit. if an address is unknown at compile time, e.g. an external symbol, 'call' will
  /// always use 'lcall'.") -- see <see cref="CvmInstructionSet.ShortCallMnemonic"/>'s own class-level
  /// remarks for the full derivation.
  ///
  /// <b>CORRECTED 2026-09-30 (same day), alongside <see cref="CollectLabelAddresses"/>'s own new iterative
  /// sizing pass ("can you use a more optimistic smart iterator for sizing pass?").</b> This method MUST
  /// reproduce the exact same scall-fits test <see cref="CollectLabelAddresses"/> already converged on for
  /// this exact line -- a mismatch here would mean pass 1 laid out every LATER label under one word count
  /// for this "call" while pass 2 actually emits a different one, silently shifting every one of those
  /// later labels' own real addresses out from under the layout pass 1 already committed to. Earlier
  /// (before the iterative pass existed), a label operand always meant lcall unconditionally, since sizing
  /// had to happen before any label was resolved -- now that <see cref="CollectLabelAddresses"/> resolves
  /// that exact question FOR label-targeted "call" lines before this ever runs, this method simply checks
  /// the same <see cref="FitsShortCallRange"/> test against the final resolved value, literal or label
  /// alike -- <paramref name="instruction"/>.OperandLabel no longer needs to be consulted here at all.
  ///
  /// <b>CHANGED 2026-10-02</b>, alongside lcall's own reshaping into a node-507-resolved
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/> mnemonic (see
  /// <see cref="CvmInstructionSet.Instructions"/>' own remarks on Ids 195/196): the lcall branch can no
  /// longer just read <c>shape.Tag</c> directly (lcall's old, retired shape's tag was a fixed, universally-
  /// known constant needing no live compile at all) -- it now needs <paramref name="encodeTable"/>, the
  /// SAME per-mnemonic resolved-opcode table every ordinary tagged mnemonic resolves through (built by
  /// <see cref="BuildEncodeTable"/> from <paramref name="compiledRam"/>), to find lcall's own real,
  /// node-507-resolved tag word. If lcall isn't wired to anything today (a mnemonic with no
  /// <see cref="NodeSymbolByMnemonic"/> entry at all), or node 507's current compile simply doesn't define
  /// <c>'lcall</c> this run, this fails with a specific, loud diagnosis (<see cref="DiagnoseUnresolvedWiredMnemonic"/>)
  /// rather than the generic unresolved-opcode nop-substitution <see cref="Assemble"/>'s own main loop
  /// uses elsewhere -- "call" redirects control flow, so silently turning a far call into a no-op would be
  /// a far more dangerous failure mode than it is for most other opcodes.
  /// </summary>
  private static (List<int>? Words, string? Error) EncodeCallPseudoMnemonic(
      CvmAsmInstruction instruction,
      IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask, int EmbeddedValueShift)> encodeTable,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam,
      int lineNumber)
  {
    if (instruction.Operand is not int value)
    {
      return (null, $"line {lineNumber}: \"call\" requires an operand, e.g. \"call 0x1234\" or \"call loop\".");
    }

    CvmInstructionSet.CvmInstructionShape shortCallShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.ShortCallMnemonic)!;

    if (FitsShortCallRange(value))
    {
      // DEFENSIVE, added 2026-09-30 during review: mirrors EncodeSelfDescribingWord's own equivalent
      // "| shape.Tag" -- a no-op today (scall's Tag is 0x0000) but keeps this in lockstep with that
      // method's encoding should scall's Tag ever become nonzero.
      return ([shortCallShape.Tag | (value & shortCallShape.ValueBitMask)], null);
    }

    // Too big for scall (or not a plain literal at all) -- lowers to lcall, exactly as before
    // 2026-10-02's brief, since-corrected detour (see this method's own class-level remarks): lcall
    // is node-507-resolved now, so its real tag word comes from encodeTable, the same lookup every
    // ordinary tagged mnemonic uses, not a fixed constant.
    if (!encodeTable.TryGetValue(CvmInstructionSet.LongCallMnemonic, out (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask, int EmbeddedValueShift) lcallEntry))
    {
      string? wiredDiagnosis = DiagnoseUnresolvedWiredMnemonic(CvmInstructionSet.LongCallMnemonic, compiledRam);
      return (null, wiredDiagnosis is not null
          ? $"line {lineNumber}: \"call\" cannot reach {CvmInstructionSet.FormatOperand(value)} -- it does not fit \"scall\"'s own range, and \"lcall\" {wiredDiagnosis}"
          : $"line {lineNumber}: \"call\" cannot reach {CvmInstructionSet.FormatOperand(value)} -- it does not fit \"scall\"'s own range, and \"lcall\" has no defined opcode yet.");
    }

    return ([lcallEntry.Opcode, value & CvmWordCodec.WordMask], null);
  }

  /// <summary>
  /// Parses CVM assembly source text into <see cref="CvmAsmInstruction"/>s ready for
  /// <see cref="Assemble"/>: one mnemonic per line, optionally followed by a "0x"-prefixed hex or
  /// plain decimal operand OR a label name (see below), OR (2026-09-16, node 306's floating-point ops;
  /// TEN of the twelve as of the 2026-09-21 rework, fpop/fpush excepted -- see this method's own
  /// three-token branch below) exactly TWO space-separated literal operands, e.g. "fadd 3 2" (Stefan's
  /// own "mnemonic f g" syntax) -- neither position accepts a label in that two-operand form; blank
  /// lines and ";" or "//" line comments are
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

      if (parts.Length == 3 && TryParseOperand(parts[1], out int firstOperand) && TryParseOperand(parts[2], out int secondOperand))
      {
        // Node 306's binary floating-point ops (2026-09-16; ten of the twelve as of the 2026-09-21
        // rework -- fpop/fpush take only one operand, per Stefan's own same-day correction) are the only
        // two-operand mnemonics in this file -- Stefan's own "mnemonic f g" syntax, e.g. "fadd 3 2".
        // Neither position supports a label operand (yet); whether THIS particular mnemonic actually
        // takes two operands at all is Assemble's own concern (see
        // CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePair), not this purely-syntactic
        // parse -- so "fpop 3 5" still parses fine here (Operand=3, Operand2=5); EncodeSelfDescribingWord's
        // own EmbeddedUnsignedValue branch is what silently ignores the stray second operand.
        instructions.Add(new CvmAsmInstruction(parts[0], firstOperand, label, Operand2: secondOperand));
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
  // needs this to support a negative literal at all (needed for br/cbr operands, e.g. "-0x400" --
  // the now-retired slit used to need it too).
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