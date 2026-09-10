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
/// <c>br</c>, <c>cbr</c>, and node 606's frame-pointer ops (<c>enter</c>, <c>adjust</c>,
/// <c>stl</c>, <c>stp</c>, <c>ldl</c>, <c>ldp</c>) are the exceptions -- see this
/// class's own remarks on why they aren't part of this tagged-opcode layer. <c>slit</c> and
/// <c>lal</c>/<c>lap</c> were RETIRED 2026-09-09, and their own mnemonic/tag constants later deleted
/// outright too -- see <see cref="CvmInstructionSet"/>'s own remarks on the 2026-09-09 CVM1-opcode
/// purge.)
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
/// 10-bit width. <b>Flagged, not silently fixed:</b> this same re-sync surfaced an apparent naming/body
/// cross-wire in Stefan's own current source -- <c>'gld</c> calls <c>g/@</c>, whose own body performs
/// what reads as a remote STORE (<c>m/2!</c>), while <c>'gst</c> calls <c>g/!</c>, whose own body performs
/// what reads as a remote FETCH (<c>m/2@</c>). See <see cref="Node508Program"/>'s own remarks; reproduced
/// verbatim here too, since this class always resolves <c>'gld</c>/<c>'gst</c> dynamically against a live
/// compile of that source, never a hardcoded address or body.
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
/// <c>slit</c> gone, that collision is resolved too. <see cref="Assemble"/> mirrors that same dual
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

  // Node 308's dpop/dpush/dinc/ddec/dadd/dor tag (2026-09-09, opcode/assembler-vs-node reconciliation
  // audit), the SAME NodeResolvedEmbeddedValue shape as node 511's rld/rst/rpop/rpush above but with a
  // different field layout -- see CvmInstructionSet.Node308FunctionFieldBitMask's own remarks for the
  // bit-by-bit derivation off node 308's own d/main body. Reached from node 307's own LEFT relay
  // ("1101_11??_????_????" per node 307's own dispatch comment, matching node 308's own header exactly),
  // fixing node 308's own top 6 bits at "1101_11" -- 0xDC00.
  private const int Node308Tag = 0xDC00;

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
        [RetMnemonic] = (Node507Program.Coordinate, "'ret", Node507Cvm2LocalExecuteTagBits),
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
        [CvmInstructionSet.LongCallMnemonic] = (Node407Program.Coordinate, "'lcall", Node407LongCallTagBits),
        [CvmInstructionSet.LongJumpMnemonic] = (Node407Program.Coordinate, "'ljmp", Node407LongCallTagBits),

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
        // remarks.
        [CvmInstructionSet.FrameToRegisterMnemonic] = (Node506Program.Coordinate, "'f", Node506LeaveTagBits),
        [CvmInstructionSet.PushFrameMnemonic] = (Node506Program.Coordinate, "'fpush", Node506LeaveTagBits),
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
        [CvmInstructionSet.LoadRegisterFileMnemonic] = (Node511Program.Coordinate, "'rld", Node511Tag),
        [CvmInstructionSet.StoreRegisterFileMnemonic] = (Node511Program.Coordinate, "'rst", Node511Tag),
        [CvmInstructionSet.PopRegisterFileMnemonic] = (Node511Program.Coordinate, "'rpop", Node511Tag),
        [CvmInstructionSet.PushRegisterFileMnemonic] = (Node511Program.Coordinate, "'rpush", Node511Tag),

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
        // register selector, instead of shifting it up into the function-select field. Fixed by routing
        // all six through `NodeResolvedFixedRegisterShiftByMnemonic` (see that dictionary's own remarks,
        // just below `NodeResolvedEmbeddedValueFieldLayoutByMnemonic`) in `BuildDecodeTable`/
        // `BuildEncodeTable`'s own tails, which shifts the resolved address left by 3 and ORs in a FIXED
        // register index of 0 -- register 0 always, since neither `CvmAssembler` (the real command-line/
        // C-compiler-facing assembler) nor the `.gaprim` primitive-table format has any way to carry a
        // per-call register operand for a plain `None`-shaped mnemonic; see that dictionary's own remarks
        // for the FLAGGED gap this leaves (registers 1-3 unreachable from compiled code today).
        [CvmInstructionSet.ArithmeticIncrementAddressRegisterMnemonic] = (Node306Program.Coordinate, "'arinc", 0xD800),
        [CvmInstructionSet.ArithmeticDecrementAddressRegisterMnemonic] = (Node306Program.Coordinate, "'ardec", 0xD800),
        [CvmInstructionSet.ArithmeticLoadAddressRegisterMnemonic] = (Node306Program.Coordinate, "'arld", 0xD800),
        [CvmInstructionSet.ArithmeticStoreAddressRegisterMnemonic] = (Node306Program.Coordinate, "'arst", 0xD800),
        [CvmInstructionSet.LoadAddressRegisterValueMnemonic] = (Node306Program.Coordinate, "'lda", 0xD800),
        [CvmInstructionSet.StoreAddressRegisterValueMnemonic] = (Node306Program.Coordinate, "'sta", 0xD800),

        // Node 308's six ops (2026-09-09) -- BRAND NEW, NodeResolvedEmbeddedValue (see
        // NodeResolvedEmbeddedValueFieldLayoutByMnemonic below for the field layout, different from node
        // 511's). Tag Node308Tag (0xDC00) -- see that constant's own remarks.
        [CvmInstructionSet.DoublePopMnemonic] = (Node308Program.Coordinate, "'dpop", Node308Tag),
        [CvmInstructionSet.DoublePushMnemonic] = (Node308Program.Coordinate, "'dpush", Node308Tag),
        [CvmInstructionSet.DoubleIncrementMnemonic] = (Node308Program.Coordinate, "'dinc", Node308Tag),
        [CvmInstructionSet.DoubleDecrementMnemonic] = (Node308Program.Coordinate, "'ddec", Node308Tag),
        [CvmInstructionSet.DoubleAddMnemonic] = (Node308Program.Coordinate, "'dadd", Node308Tag),
        [CvmInstructionSet.DoubleOrMnemonic] = (Node308Program.Coordinate, "'dor", Node308Tag),

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

        // Node 510's own "extended arithmetic" ops (2026-09-09) -- tagged/node-resolved, sharing node
        // 510's own "1011_10??" local-execute tag (0xB800) OR'd with each op's own address on node 510.
        // 'addc REPOINTS the existing AddWithCarryMnemonic ("addc") -- FLAGGED, see
        // ExtendedStoreMnemonic's own remarks in CvmInstructionSet for the CvmDebuggerDefaultProgram
        // implication of this repoint.
        [CvmInstructionSet.AddWithCarryMnemonic] = (Node510Program.Coordinate, "'addc", 0xB800),
        [CvmInstructionSet.ExtendedStoreMnemonic] = (Node510Program.Coordinate, "'xst", 0xB800),
        [CvmInstructionSet.ExtendedLoadMnemonic] = (Node510Program.Coordinate, "'xld", 0xB800),
        [CvmInstructionSet.ExtendedMultiplyByTwoMnemonic] = (Node510Program.Coordinate, "'xmul2", 0xB800),
        [CvmInstructionSet.ExtendedDivideByTwoMnemonic] = (Node510Program.Coordinate, "'xdiv2", 0xB800),
        [CvmInstructionSet.ExtendedUnsignedMultiplyMnemonic] = (Node510Program.Coordinate, "'xumul", 0xB800),
      };

  /// <summary>
  /// Per-mnemonic field layout for <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/>
  /// mnemonics only (node 511's <c>rld</c>/<c>rst</c>/<c>rpop</c>/<c>rpush</c> and, since 2026-09-09, node
  /// 308's <c>dpop</c>/<c>dpush</c>/<c>dinc</c>/<c>ddec</c>/<c>dadd</c>/<c>dor</c>): unlike every other
  /// entry in <see cref="NodeSymbolByMnemonic"/>, this shape needs to know exactly WHERE within the
  /// resolved word its own "which function" field and its own embedded register-index operand each sit --
  /// and, per <see cref="CvmInstructionSet.Node308FunctionFieldBitMask"/>'s own remarks, node 308's own
  /// layout is genuinely different from node 511's (a 6-bit/2-bit split with no base-address bias, vs.
  /// node 511's 5-bit/5-bit split biased by <see cref="CvmInstructionSet.Node511FunctionFieldBaseAddress"/>)
  /// -- so this is a separate per-mnemonic lookup rather than a single shared set of constants
  /// <see cref="BuildDecodeTable"/>/<see cref="BuildEncodeTable"/> could hardcode once.
  /// </summary>
  private static readonly IReadOnlyDictionary<string, (int FunctionFieldBitMask, int FunctionFieldShift, int FunctionFieldBaseAddress, int RegisterFieldBitMask)> NodeResolvedEmbeddedValueFieldLayoutByMnemonic =
      new Dictionary<string, (int, int, int, int)>(StringComparer.OrdinalIgnoreCase)
      {
        [CvmInstructionSet.LoadRegisterFileMnemonic] = (CvmInstructionSet.Node511FunctionFieldBitMask, CvmInstructionSet.Node511FunctionFieldShift, CvmInstructionSet.Node511FunctionFieldBaseAddress, CvmInstructionSet.Node511RegisterFieldBitMask),
        [CvmInstructionSet.StoreRegisterFileMnemonic] = (CvmInstructionSet.Node511FunctionFieldBitMask, CvmInstructionSet.Node511FunctionFieldShift, CvmInstructionSet.Node511FunctionFieldBaseAddress, CvmInstructionSet.Node511RegisterFieldBitMask),
        [CvmInstructionSet.PopRegisterFileMnemonic] = (CvmInstructionSet.Node511FunctionFieldBitMask, CvmInstructionSet.Node511FunctionFieldShift, CvmInstructionSet.Node511FunctionFieldBaseAddress, CvmInstructionSet.Node511RegisterFieldBitMask),
        [CvmInstructionSet.PushRegisterFileMnemonic] = (CvmInstructionSet.Node511FunctionFieldBitMask, CvmInstructionSet.Node511FunctionFieldShift, CvmInstructionSet.Node511FunctionFieldBaseAddress, CvmInstructionSet.Node511RegisterFieldBitMask),
        [CvmInstructionSet.DoublePopMnemonic] = (CvmInstructionSet.Node308FunctionFieldBitMask, CvmInstructionSet.Node308FunctionFieldShift, CvmInstructionSet.Node308FunctionFieldBaseAddress, CvmInstructionSet.Node308RegisterFieldBitMask),
        [CvmInstructionSet.DoublePushMnemonic] = (CvmInstructionSet.Node308FunctionFieldBitMask, CvmInstructionSet.Node308FunctionFieldShift, CvmInstructionSet.Node308FunctionFieldBaseAddress, CvmInstructionSet.Node308RegisterFieldBitMask),
        [CvmInstructionSet.DoubleIncrementMnemonic] = (CvmInstructionSet.Node308FunctionFieldBitMask, CvmInstructionSet.Node308FunctionFieldShift, CvmInstructionSet.Node308FunctionFieldBaseAddress, CvmInstructionSet.Node308RegisterFieldBitMask),
        [CvmInstructionSet.DoubleDecrementMnemonic] = (CvmInstructionSet.Node308FunctionFieldBitMask, CvmInstructionSet.Node308FunctionFieldShift, CvmInstructionSet.Node308FunctionFieldBaseAddress, CvmInstructionSet.Node308RegisterFieldBitMask),
        [CvmInstructionSet.DoubleAddMnemonic] = (CvmInstructionSet.Node308FunctionFieldBitMask, CvmInstructionSet.Node308FunctionFieldShift, CvmInstructionSet.Node308FunctionFieldBaseAddress, CvmInstructionSet.Node308RegisterFieldBitMask),
        [CvmInstructionSet.DoubleOrMnemonic] = (CvmInstructionSet.Node308FunctionFieldBitMask, CvmInstructionSet.Node308FunctionFieldShift, CvmInstructionSet.Node308FunctionFieldBaseAddress, CvmInstructionSet.Node308RegisterFieldBitMask),
      };

  /// <summary>
  /// ADDED 2026-09-09: node 306's six address-register ops (<c>arinc</c>/<c>ardec</c>/<c>arld</c>/
  /// <c>arst</c>/<c>lda</c>/<c>sta</c>) need the SAME "shift the resolved word address up to make room
  /// for an embedded field" correction <see cref="NodeResolvedEmbeddedValueFieldLayoutByMnemonic"/>
  /// applies for node 511/308 -- node 306's own <c>ar/main</c> pulls a 3-bit register-select field out
  /// of the low bits before shifting the rest down to get its actual jump target (see
  /// <c>NodeSymbolByMnemonic</c>'s own remarks on these six for the exact bit derivation) -- but unlike
  /// node 511/308, none of these six is wired as <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/>:
  /// each is an ordinary <see cref="CvmInstructionSet.CvmOperandEncoding.None"/> mnemonic that resolves
  /// to exactly ONE opcode word, with its embedded register field FIXED at 0 rather than exposed as an
  /// assembler operand. That's a deliberate simplification, not an oversight: <c>CvmAssembler</c> (the
  /// real command-line assembler the C compiler's own output actually goes through -- see its own
  /// remarks on why node 511's <c>rld</c>/<c>rst</c>/<c>rpop</c>/<c>rpush</c> were NEVER assemblable
  /// there) and the <c>.gaprim</c> primitive-table format both only know how to carry a single, whole
  /// opcode word per mnemonic name -- neither has any notion of a per-call embedded operand the way this
  /// file's own <see cref="Assemble"/>/<see cref="DisassemblePage0"/> (used only by the CVM Debugger's
  /// own hand-typed assembly panel) do. Wiring node 306 as <c>NodeResolvedEmbeddedValue</c> would
  /// therefore have made it resolve correctly only inside the debugger's own panel and silently fall
  /// back to node 306's dead OLD self-describing shape (or simply fail to assemble) everywhere a real
  /// <c>.casm</c> file is involved, including every C program <see cref="CCodeGenerator"/> compiles.
  ///
  /// <b>FLAGGED for Stefan:</b> fixing the register field at 0 means only ONE of node 306's four address
  /// registers is reachable from compiled/assembled code today; registers 1-3 are real, addressable
  /// hardware this scheme cannot reach until <c>CvmAssembler</c>/<c>.gaprim</c> grow a real
  /// register-operand mechanism (mnemonic + immediate register index, the primitive table keyed by both
  /// rather than by mnemonic name alone). Not attempted here, since inventing that mechanism now -- for a
  /// need (more than one live pointer at a time) nothing in this compiler currently has -- would be
  /// exactly the kind of unconfirmed design guess this project avoids.
  /// </summary>
  private static readonly IReadOnlyDictionary<string, int> NodeResolvedFixedRegisterShiftByMnemonic =
      new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
      {
        [CvmInstructionSet.ArithmeticIncrementAddressRegisterMnemonic] = 3,
        [CvmInstructionSet.ArithmeticDecrementAddressRegisterMnemonic] = 3,
        [CvmInstructionSet.ArithmeticLoadAddressRegisterMnemonic] = 3,
        [CvmInstructionSet.ArithmeticStoreAddressRegisterMnemonic] = 3,
        [CvmInstructionSet.LoadAddressRegisterValueMnemonic] = 3,
        [CvmInstructionSet.StoreAddressRegisterValueMnemonic] = 3,
      };

  /// <summary>
  /// Every known CVM asm mnemonic THAT RESOLVES TO SOME NODE'S F18 SYMBOL, which node and symbol that
  /// is, and how many words (its own opcode word included) it occupies once assembled. <c>pushlit</c>
  /// is the only such instruction with a trailing operand word today -- extend
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
          .Where(shape => shape.Encoding is CvmInstructionSet.CvmOperandEncoding.None or CvmInstructionSet.CvmOperandEncoding.TrailingWord or CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue)
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
  /// <see cref="Assemble"/>'s own label pass -- never both.
  /// </summary>
  public sealed record CvmAsmInstruction(string Mnemonic, int? Operand, string? Label = null, string? OperandLabel = null);

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
        if (!NodeResolvedEmbeddedValueFieldLayoutByMnemonic.TryGetValue(mnemonic, out (int FunctionFieldBitMask, int FunctionFieldShift, int FunctionFieldBaseAddress, int RegisterFieldBitMask) layout))
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
          table[baseOpcode | register] = (mnemonic, wordLength, register);
        }

        continue;
      }

      // Node 306's six ops only -- see NodeResolvedFixedRegisterShiftByMnemonic's own remarks: the
      // resolved word address is the FUNCTION field, not the whole free-bits value, so it must be
      // shifted up before OR-ing with the tag (the register field this vacates is always 0).
      if (NodeResolvedFixedRegisterShiftByMnemonic.TryGetValue(mnemonic, out int fixedShift))
      {
        table[tag | (resolvedAddress << fixedShift)] = (mnemonic, wordLength, null);
        continue;
      }

      table[tag | resolvedAddress] = (mnemonic, wordLength, null);
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
  public static IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask)> BuildEncodeTable(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    var table = new Dictionary<string, (int, int, bool, bool, int)>(StringComparer.OrdinalIgnoreCase);
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
        if (!NodeResolvedEmbeddedValueFieldLayoutByMnemonic.TryGetValue(mnemonic, out (int FunctionFieldBitMask, int FunctionFieldShift, int FunctionFieldBaseAddress, int RegisterFieldBitMask) layout))
        {
          continue;
        }

        int functionField = resolvedAddress - layout.FunctionFieldBaseAddress;
        if (functionField < 0 || functionField > (layout.FunctionFieldBitMask >> layout.FunctionFieldShift))
        {
          continue;
        }

        int baseOpcode = tag | (functionField << layout.FunctionFieldShift);
        table[mnemonic] = (baseOpcode, wordLength, true, true, layout.RegisterFieldBitMask);
        continue;
      }

      // Node 306's six ops only -- see NodeResolvedFixedRegisterShiftByMnemonic's own remarks (same
      // shift-before-OR correction as BuildDecodeTable's own tail, just above).
      if (NodeResolvedFixedRegisterShiftByMnemonic.TryGetValue(mnemonic, out int fixedShift))
      {
        table[mnemonic] = (tag | (resolvedAddress << fixedShift), wordLength, hasOperand, false, 0);
        continue;
      }

      table[mnemonic] = (tag | resolvedAddress, wordLength, hasOperand, false, 0);
    }

    return table;
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
  /// check below applies unchanged either way. <c>call</c> and every tagged mnemonic (<c>pushlit</c>
  /// -- the now-retired <c>slit</c> used to belong here too) resolve a label to its own ABSOLUTE word address; <c>br</c>/<c>cbr</c> resolve
  /// to a signed RELATIVE offset instead, since that is what their own opcode word actually encodes
  /// (see <see cref="ResolveOperandLabel"/>'s own remarks); node 606's eight
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/> ops don't accept a label
  /// operand at all, since their value is a frame-relative slot index/count, never an address.
  /// </summary>
  public static (List<int>? Words, string? Error) Assemble(
      IReadOnlyList<CvmAsmInstruction> instructions,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask)> encodeTable = BuildEncodeTable(compiledRam);

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

      if (!encodeTable.TryGetValue(instruction.Mnemonic, out (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask) entry))
      {
        if (selfDescribingShape is not null)
        {
          // A genuine CVM opcode (CvmInstructionSet knows its shape) that just has no live node to
          // answer it right now -- per Stefan, substitute node 507's own current 'nop opcode rather
          // than failing the whole assemble. See this method's own remarks.
          if (!encodeTable.TryGetValue(NopMnemonic, out (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask) nopEntry))
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

      if (entry.OperandIsEmbedded)
      {
        // Node 511's four ops only (see BuildEncodeTable's own remarks): the operand is a register index
        // packed directly into entry.Opcode's own low bits, never a separate trailing word.
        if (instruction.Operand!.Value < 0 || instruction.Operand!.Value > entry.EmbeddedValueMask)
        {
          return (null, $"line {line + 1}: {instruction.Operand!.Value} does not fit in \"{instruction.Mnemonic}\"'s embedded register operand (0..{entry.EmbeddedValueMask}).");
        }

        words.Add(entry.Opcode | (instruction.Operand!.Value & entry.EmbeddedValueMask));
        continue;
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
      IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask)> encodeTable)
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
  /// <c>cbr</c>/node 606's ops -- the now-retired <c>slit</c> used to belong here too) is always its own
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
      IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask)> encodeTable)
  {
    CvmInstructionSet.CvmInstructionShape? selfDescribingShape = CvmInstructionSet.TryGetShape(mnemonic);
    if (selfDescribingShape is { Encoding: CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress or CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue or CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue })
    {
      return selfDescribingShape.WordLength;
    }

    return encodeTable.TryGetValue(mnemonic, out (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask) entry) ? entry.WordLength : 1;
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
      int maxRegister = NodeResolvedEmbeddedValueFieldLayoutByMnemonic.TryGetValue(instruction.Mnemonic, out (int FunctionFieldBitMask, int FunctionFieldShift, int FunctionFieldBaseAddress, int RegisterFieldBitMask) layout)
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
  /// <c>call</c>/<c>br</c>/<c>cbr</c> (<see cref="CvmInstructionSet.TryDescribeSelfDecodingWord"/>) --
  /// the now-retired <c>slit</c> used to belong here too -- that need no compile/symbol at all. This MUST be a stateful scan starting at 0, never an
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

      // "call", "br", and "cbr" have no F18 symbol to resolve -- each one's whole word is
      // fully determined by its own bit pattern and operand alone (CvmInstructionSet.
      // CvmOperandEncoding.EmbeddedAddress / EmbeddedSignedValue), independent of node 607's live
      // compile, so all three are checked before consulting the (symbol-driven) decode table at all.
      // (the now-retired "slit" used to be a fourth self-describing mnemonic here.)
      // wordAddress passed through 2026-09-09 so br/cbr's own listing line can also show the
      // RESOLVED absolute target next to the raw offset -- see TryDescribeSelfDecodingWord's own
      // remarks (Stefan asked why the linker put "__exit" at "location 0x200"; it doesn't -- that
      // was always just the raw offset field, easy to misread as an address in this exact listing).
      string? selfDescribing = CvmInstructionSet.TryDescribeSelfDecodingWord(word, wordAddress: address);
      if (selfDescribing is not null)
      {
        notes[address] = selfDescribing;
        address += 1;
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
  /// Encodes one <c>call</c>/<c>br</c>/<c>cbr</c>/node-606 word directly from
  /// <paramref name="shape"/> and its literal operand (the now-retired <c>slit</c> used to belong here
  /// too) -- the same arithmetic
  /// <see cref="CvmAssembler.EmitEmbeddedSignedValue"/>/<see cref="CvmAssembler.EmitEmbeddedUnsignedValue"/>
  /// use for <c>br</c>/<c>cbr</c> and node 606's eight ops respectively (mask-derived
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
      // Node 606's eight ops (ValueBitShift 0) and node 306's six address-register ops (ValueBitShift 1,
      // a 2-bit register index at bits 2-1 rather than bit 0 upward -- see
      // CvmInstructionSet.CvmInstructionShape.ValueBitShift's own remarks): unsigned
      // 0..(ValueBitMask >> ValueBitShift), never a negative half -- unlike the signed case just below,
      // so no min/max split is needed here. This mirrors CvmAssembler.EmitEmbeddedUnsignedValue exactly
      // (kept as a small duplicate here per this method's own remarks).
      int unsignedMaxValue = shape.ValueBitMask >> shape.ValueBitShift;
      if (value < 0 || value > unsignedMaxValue)
      {
        return (null, $"line {lineNumber}: {value} does not fit in \"{shape.Mnemonic}\"'s unsigned value (0..{unsignedMaxValue}).");
      }

      return (shape.Tag | ((value << shape.ValueBitShift) & shape.ValueBitMask), null);
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