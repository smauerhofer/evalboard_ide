using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ga144.Cvm.Toolchain;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Cvm;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// <b>2026-10-04: THIS CLASS NO LONGER CONTAINS AN ASSEMBLER.</b> The remarks below were written when it did; every
/// mention of "Assemble"/"ParseSource" in them (now <see cref="AssembleProgram"/>) describes the RETIRED
/// immediately-resolving assembler, which was merged into <see cref="CvmAssembler"/> per Stefan ("it is time to
/// unify the 2 assembler into 1"). What stays here: the live-node-compile opcode tables
/// (<see cref="BuildEncodeTable"/>, <see cref="BuildIfConditionEncodeTable"/>, <see cref="BuildDecodeTable"/>), the
/// disassembler, the unresolved-mnemonic diagnosis, and <see cref="AssembleProgram"/>/<see cref="AssembleAndLoadProgram"/>,
/// the thin glue that assembles with <see cref="CvmAssembler"/>, links against the primitive table those tables
/// produce, and loads the image at address 0.
///
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
/// <c>next32</c>/<c>pop16</c>/<c>pop32</c>/<c>rjmp</c>/<c>rcall</c>/<c>sbr</c>/<c>cbr</c> (RENAMED the
/// same day to <c>if</c>, see <see cref="CvmInstructionSet.IfMnemonic"/>'s own remarks) -- every one
/// of these is ALSO self-describing (<see cref="CvmInstructionSet.CvmOperandEncoding.FixedOpcode"/>/
/// <see cref="CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTrailingWord"/>/
/// <see cref="CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords"/>/
/// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/>/
/// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>/
/// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord"/>), so
/// none needs an F18 symbol either -- but <c>if</c> specifically can only be DECODED this way, not
/// yet ASSEMBLED (see <see cref="AssembleProgram"/>'s own remarks on why it is explicitly rejected rather
/// than guessed at).
///
/// <see cref="AssembleProgram"/> mirrors that same dual
/// dispatch on the OTHER direction --
/// hand-typed CVM asm source that uses <c>call</c>/<c>br</c>/<c>if</c>/node 606's or node
/// 306's ops is encoded directly from <see cref="CvmInstructionSet"/> and the operand alone, bypassing
/// this file's own <see cref="Instructions"/>/<see cref="NodeSymbolByMnemonic"/> pairing entirely (see
/// <see cref="AssembleProgram"/>'s own remarks) -- so <see cref="Instructions"/> itself still omits all of
/// them, since they would have nothing to pair them with, without that meaning they can't be assembled.
///
/// Both directions -- <see cref="BuildDecodeTable"/> for disassembly and <see cref="BuildEncodeTable"/>/
/// <see cref="AssembleProgram"/> for assembly -- are built from the single <see cref="Instructions"/> table,
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

  // ---- REVISED 2026-10-04: node 507's header table now reads (verbatim, Stefan) -------------------------
  //
  //   0000|0000|0000|0000| 508  | nop
  //   0000|wwww|wwss|xxxx| 508  | special 508 with optional register
  //   0001|00ww|wwww|iiii| 508  | special 508 direct with unsigned immediate value 0..15
  //   0010|wwww|wwss|xxxx| 508  | special 508 with optional register with focus
  //   01oo|oooo|oooo|oooo| 508  | br {short branch, relative}
  //   1000|0000|cccc|xxxx| 506  | if {conditional long branch, relative}
  //   1001|0000|cccc|xxxx| 506  | cond { condition calculation and push result on stack }
  //   1001|1iii|iiii|iiii| 506  | slit { push 11-bit signed literal on stack }
  //   1010|0www|wwee|xxxx| 506  | special 506 with 16-bit register read for output
  //   1010|1www|wwee|xxxx| 506  | special 506 with 16-bit register write from input
  //   1011|wwww|wwww|weee| 506  | special 506 without register
  //   1100|wwww|wwee|xxxx| 506  | unary 16-bit operation
  //   1101|pppp|yyyy|xxxx| 506  | binary 16-bit operation
  //   1110|wwww|wwee|xxxx| 506  | unary 32-bit operation
  //   1111|0ppp|yyyy|xxxx| 506  | binary 32-bit integer operation
  //   1111|1ppp|yyyy|xxxx| 506  | binary 32-bit float operation
  //
  // with "s: size 0..3 representing actual size 1..4", "w: word address", "i: immediate unsigned value",
  // "x: parameter1". For the node-509 special words that means
  //
  //     opcode = focus(0x2000) | (address of the 'name label in node 509 << 6) | ((words - 1) << 4) | x
  //
  // (the older paragraphs below describe the PREVIOUS layout -- address << 7, 3 size bits -- and are kept as
  // history; the constants right below are authoritative). The direct row 0001|00ww|wwww|iiii has NO
  // mnemonics yet: which node-509 words use it, and how their size is signalled, has not been specified.
  // The special-506 / 16-bit / 32-bit rows still have no mnemonics either (their node-505 source has not
  // been provided).
  //
  // ---- ADDED 2026-10-03 (CVM REDESIGN): the node-509 special-word table ---------------------------------
  // Per Stefan directly: "I redesigned the CVM a little. there are now more CVM words available, thanks
  // to the table in node 509." Node 507's header now carries this bit-pattern table:
  //
  //   0000|0000|0000|0000| nop
  //   000w|wwww|wsss|xxxx| special 508 with optional register
  //   001w|wwww|wsss|xxxx| special 508 with optional register with focus
  //   01oo|oooo|oooo|oooo| br {short branch, relative}
  //   100.|....|cccc|xxxx| if {conditional long branch, relative}
  //   101.|00ww|wwww|xxxx| special 506 with optional register
  //   110 |    |    |    | 16-bit operation
  //   111 |    |    |    | 32-bit operation
  //
  // and node 508's own x6/unpack decodes the two "special 508" rows exactly as that table reads:
  //   dup 0xf and            -> x, the 4-bit register (bits 3-0, sent to node 407 for "with focus")
  //   over 2/ 2/ 2/ 2/       -> op >> 4
  //   dup 7 and dup !        -> "send size-1": s = bits 6-4, sent as the word count minus one
  //   2/ 2/ 2/ 0x3f and !    -> "send handler offset": w = bits 12-7, the 6-bit address in node 509
  // after which node 509's own x6a/main (@b >r @b a! begin @+ !b unext) streams s+1 words starting at
  // node-509 address w (the "'name" entries of its table) onward. So every "special 508" opcode is
  //
  //     tag | (address of its 'name label in node 509 << 7) | ((words in the entry - 1) << 4) | x
  //
  // with focus bit 13 (0x2000) set when node 508 must first send r/focus_nw/x to node 407, clear when not.
  //
  // CORRECTED 2026-10-03, per Stefan directly: "the offsets are the labels defined in the node 509. there
  // is a comment in the next line of a comment that specify the size of the instruction, which must also
  // be encoded, and the focus bit 13." BOTH the size field "sss" and the focus bit are therefore READ
  // FROM THE COMMENT in node 509's source, on the line right after each 'name label line:
  //
  //     : 'call // call to address in next word
  //       // 3 with focus
  //
  // "// <N> with focus" / "// <N> without focus" -- N is the number of words in the entry (encoded as
  // N - 1 in bits 6-4), "with focus" sets bit 13. (An earlier version of this wiring hardcoded focus per
  // mnemonic and derived N from the distance to the next label; that is gone -- the comment is the single
  // source of truth, exactly as Stefan described it.) See TryReadNode509SpecialComment. A label without
  // that comment line, or a comment that does not parse, makes the word fail to resolve with a precise
  // diagnosis -- never a guessed size or focus.
  //
  // The tag stored per mnemonic in NodeSymbolByMnemonic is therefore just the base 0x0000
  // (Node509SpecialBaseTagBits); the focus bit (Node509SpecialFocusBit, 0x2000) is OR'd in per word.
  private const int Node509SpecialBaseTagBits = 0x0000;
  private const int Node509SpecialFocusBit = 0x2000;
  // CHANGED 2026-10-04, per Stefan: "the number of size bits is reduced from 3 to 2 because the max size of a
  // command in node 509 is 4." The size field "ss" is now bits 5-4 (mask 0x0030, N - 1 in 0..3 = N 1..4) and
  // the 6-bit address field "wwwwww" moved down one bit with it, to bits 11-6 (mask 0x0FC0, shift 6). Bit 12
  // is no longer part of the address: it is free, and "0001" in bits 15-12 now selects the new "special 508
  // direct" row (see the table above). Focus stays bit 13. Earlier values: shift 7, mask 0x1F80, size mask 0x70.
  private const int Node509SpecialAddressShift = 6;
  private const int Node509SpecialAddressFieldBitMask = 0x0FC0;
  private const int Node509SpecialSizeFieldShift = 4;
  private const int Node509SpecialSizeFieldBitMask = 0x0030;

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

  // Node 306's 16-bit register operations (2026-10-04, Stefan's table rows "1100|wwww|wwww|xxxx| unary 16-bit operation
  // {w = address in node 306}" and "1101|wwww|yyyy|xxxx| binary 16-bit operation {w = address in node 306}"). Unary:
  // opcode = 0xC000 | w << 4 | x (w = 8-bit node-306 address, x = register). Binary: 0xD000 | w << 8 | y << 4 | x (w = 4-bit
  // address, so these words must sit at node-306 addresses 0..15 -- 'or 'and 'xor 'sub 'add 'packbytes do today).
  // See CvmInstructionSet.PackBytesMnemonic's remarks.
  private const int Node306UnaryOperationTagBits = 0xC000;
  private const int Node306BinaryOperationTagBits = 0xD000;
  private const int Node306UnaryFunctionFieldBitMask = 0x0FF0;
  private const int Node306UnaryFunctionFieldShift = 4;
  private const int Node306BinaryFunctionFieldBitMask = 0x0F00;
  private const int Node306BinaryFunctionFieldShift = 8;

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
        // PushMnemonic ("push") -- RETIRED 2026-10-02, SAME REASON as RetMnemonic's/LongCallMnemonic's
        // own OLD entries elsewhere in this dictionary (a Dictionary literal cannot hold two entries
        // for the same key): per Stefan, "there is a new word 'push'. it is also a special word (node
        // 507 'push). it pushed the next word onto the stack." That is a brand new, unrelated shape
        // (new Id 197, CvmOperandEncoding.TrailingWord, node 507, tag Node506BitPatternTableTagBits --
        // see CvmInstructionSet.Instructions' own remarks on Id 197) replacing this CVM2-era one (same
        // node, but the OLD "1000_1???" local-execute tag family and CvmOperandEncoding.None, no
        // operand) -- kept here as a comment, per "do not remove any opcodes", as the historical
        // record; the live entry is just below, alongside lcall/ljmp/ret/link/unlink's own.
        // [PushMnemonic] = (Node507Program.Coordinate, "'push", Node507Cvm2LocalExecuteTagBits),
        // [PopMnemonic] = (Node507Program.Coordinate, "'pop", Node507Cvm2LocalExecuteTagBits),
        // (CVM2's OLD inert "pop" entry -- commented out 2026-10-03 for the same Dictionary-key reason as
        // RetMnemonic/PushMnemonic: the redesigned VM's own "pop" is a live entry further below, resolved
        // against node 509's special table.)
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
        // [RetMnemonic] = (Node507Program.Coordinate, "'ret", Node506BitPatternTableTagBits),
        // SUPERSEDED 2026-10-03 (CVM redesign): node 507's 'ret/'link/'unlink/'lcall/'ljmp/'push moved out
        // of node 507 (its source keeps them only inside a /* ... */ comment) into node 509's special-word
        // table -- see the live [RetMnemonic]/[LinkMnemonic]/[UnlinkMnemonic] entries in the node-509 block
        // below, and the node-509 special-word remarks at Node509SpecialBaseTagBits.
        // LinkMnemonic ("link")/UnlinkMnemonic ("unlink") -- ADDED 2026-10-01, alongside ret in node
        // 506's bit-pattern-table family (see Node506BitPatternTableTagBits' own remarks and
        // CvmInstructionSet.Instructions' own remarks on Ids 185/186). Brand new mnemonic names, no
        // OLD CVM1/CVM2 entry to comment out first (unlike ret just above).
        //
        // CORRECTED 2026-10-02, same as RetMnemonic just above and for the same reason: NodeCoordinate
        // moves from node 506 to node 507; Node506BitPatternTableTagBits' own tag is unchanged.
        // [CvmInstructionSet.LinkMnemonic] = (Node507Program.Coordinate, "'link", Node506BitPatternTableTagBits),
        // [CvmInstructionSet.UnlinkMnemonic] = (Node507Program.Coordinate, "'unlink", Node506BitPatternTableTagBits),
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
        // [CvmInstructionSet.LongCallMnemonic] = (Node507Program.Coordinate, "'lcall", Node506BitPatternTableTagBits),
        // [CvmInstructionSet.LongJumpMnemonic] = (Node507Program.Coordinate, "'ljmp", Node506BitPatternTableTagBits),
        // (lcall/ljmp RETIRED 2026-10-03 -- replaced by call/jmp, see the node-509 block below and
        // CvmInstructionSet.Instructions' own remarks on Ids 195-199.)
        // NEW 2026-10-02, same day: 'push joins lcall/ljmp/ret/link/unlink in node 507's own "special"
        // family (Id 197 -- see CvmInstructionSet.Instructions' own remarks on that Id for Stefan's
        // exact wording and the node-507 source confirming the shape). Same tag, same node, same
        // shift (0 -- see NodeResolvedAddressShiftByMnemonic's own remarks) as the other five;
        // CvmOperandEncoding.TrailingWord (like link/lcall/ljmp) means it takes a real trailing
        // operand word: the literal value 'push writes onto the return stack. Supersedes the OLD
        // CVM2-era [PushMnemonic] entry just above (commented out there, per "do not remove any
        // opcodes").
        // [PushMnemonic] = (Node507Program.Coordinate, "'push", Node506BitPatternTableTagBits),
        // (RETIRED 2026-10-03: the old "push the next word" is gone; the redesigned VM's "push" pushes a
        // REGISTER and lives in the node-509 block below -- CvmInstructionSet.Instructions' Ids 197/202.)
        //
        // ==== ADDED 2026-10-03 (CVM REDESIGN): node 509's special-word table ================================
        // Every entry below resolves against node 509's OWN compile (Node509Program.Coordinate) to its own
        // tick-labeled word, and carries Node509SpecialBaseTagBits (0x0000). The FOCUS bit (0x2000) and the
        // SIZE field (bits 5-4) are NOT in the tag: both are read per word from the "// N with focus" /
        // "// N without focus" comment on the line under that label in Stefan's node 509 source (see
        // TryReadNode509SpecialComment). ADDRESS SHIFT (6), the size field and the focus bit are applied
        // by BuildDecodeTable/BuildEncodeTable through TryResolveNode509SpecialOpcode.
        //
        // NOTE: Node509Program.cs is still the OLD (pre-redesign) node 509 source -- per Stefan's standing
        // instruction ("do not sync the source yet (first finish the CVM before updating any NodeXXX...
        // file)"), none of the Node###Program.cs files were touched. Until Node509Program is synced to
        // the redesigned source, none of these labels exist in its compile, so every mnemonic below fails
        // to resolve with DiagnoseUnresolvedWiredMnemonic's own loud "wired but unresolved" diagnosis
        // rather than silently assembling to a wrong word. Expected, prepared-but-not-yet-functional
        // wiring, same state ret/link/unlink/lcall/ljmp/push were in before node 507 was caught up.
        [RetMnemonic] = (Node509Program.Coordinate, "'ret", Node509SpecialBaseTagBits),
        [CvmInstructionSet.CallMnemonic] = (Node509Program.Coordinate, "'call", Node509SpecialBaseTagBits),
        [CvmInstructionSet.JmpMnemonic] = (Node509Program.Coordinate, "'jmp", Node509SpecialBaseTagBits),
        [CvmInstructionSet.LinkMnemonic] = (Node509Program.Coordinate, "'link", Node509SpecialBaseTagBits),
        [CvmInstructionSet.UnlinkMnemonic] = (Node509Program.Coordinate, "'unlink", Node509SpecialBaseTagBits),
        // 2026-10-03 (node 509 follow-up): register words carry an 'r' or 'd' prefix -- 'rpop/'rpush/
        // 'rpopi/'rpushi (16-bit register), 'dpop/'dpush/'dpopi/'dpushi (32-bit double register) -- and
        // 'push now means "push the next 16-bit word" (plus new 'push2, next 32-bit value).
        [CvmInstructionSet.RegisterPopMnemonic] = (Node509Program.Coordinate, "'rpop", Node509SpecialBaseTagBits),
        [CvmInstructionSet.DoublePopMnemonic] = (Node509Program.Coordinate, "'dpop", Node509SpecialBaseTagBits),
        [PushMnemonic] = (Node509Program.Coordinate, "'push", Node509SpecialBaseTagBits),
        [CvmInstructionSet.Push2Mnemonic] = (Node509Program.Coordinate, "'push2", Node509SpecialBaseTagBits),
        [CvmInstructionSet.RegisterPushMnemonic] = (Node509Program.Coordinate, "'rpush", Node509SpecialBaseTagBits),
        [CvmInstructionSet.DoublePushMnemonic] = (Node509Program.Coordinate, "'dpush", Node509SpecialBaseTagBits),
        [CvmInstructionSet.RegisterPopIndirectMnemonic] = (Node509Program.Coordinate, "'rpopi", Node509SpecialBaseTagBits),
        [CvmInstructionSet.DoublePopIndirectMnemonic] = (Node509Program.Coordinate, "'dpopi", Node509SpecialBaseTagBits),
        [CvmInstructionSet.RegisterPushIndirectMnemonic] = (Node509Program.Coordinate, "'rpushi", Node509SpecialBaseTagBits),
        [CvmInstructionSet.DoublePushIndirectMnemonic] = (Node509Program.Coordinate, "'dpushi", Node509SpecialBaseTagBits),
        [CvmInstructionSet.RegisterIncrementMnemonic] = (Node509Program.Coordinate, "'rinc", Node509SpecialBaseTagBits),
        [CvmInstructionSet.RegisterDecrementMnemonic] = (Node509Program.Coordinate, "'rdec", Node509SpecialBaseTagBits),
        [CvmInstructionSet.RegisterAddMnemonic] = (Node509Program.Coordinate, "'radd", Node509SpecialBaseTagBits),
        // 'halt (2026-10-03): "stop execution until reset or interrupt" -- table entry 2 words, WITHOUT focus.
        [CvmInstructionSet.HaltMnemonic] = (Node509Program.Coordinate, "'halt", Node509SpecialBaseTagBits),
        // 2026-10-04: node 509's second batch of words (see CvmInstructionSet.DoubleFetchMnemonic's remarks) --
        // all "with focus"; size and focus come from the comment under each label like every other word.
        [CvmInstructionSet.DoubleFetchMnemonic] = (Node509Program.Coordinate, "'dfetch", Node509SpecialBaseTagBits),
        [CvmInstructionSet.DoubleStoreMnemonic] = (Node509Program.Coordinate, "'dstore", Node509SpecialBaseTagBits),
        [CvmInstructionSet.RegisterLiteralMnemonic] = (Node509Program.Coordinate, "'rlit", Node509SpecialBaseTagBits),
        [CvmInstructionSet.DoubleLiteralMnemonic] = (Node509Program.Coordinate, "'dlit", Node509SpecialBaseTagBits),
        [CvmInstructionSet.RegisterJumpMnemonic] = (Node509Program.Coordinate, "'rjmp", Node509SpecialBaseTagBits),
        [CvmInstructionSet.RegisterCallMnemonic] = (Node509Program.Coordinate, "'rcall", Node509SpecialBaseTagBits),
        [CvmInstructionSet.DropMnemonic] = (Node509Program.Coordinate, "'drop", Node509SpecialBaseTagBits),
        [CvmInstructionSet.DupMnemonic] = (Node509Program.Coordinate, "'dup", Node509SpecialBaseTagBits),

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
        [CvmInstructionSet.InvertMnemonic] = (Node306Program.Coordinate, "'inv", Node306UnaryOperationTagBits),
        [CvmInstructionSet.IncrementMnemonic] = (Node306Program.Coordinate, "'inc", Node306UnaryOperationTagBits),
        [CvmInstructionSet.DecrementMnemonic] = (Node306Program.Coordinate, "'dec", Node306UnaryOperationTagBits),
        [CvmInstructionSet.NegMnemonic] = (Node306Program.Coordinate, "'neg", Node306UnaryOperationTagBits),
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
        [CvmInstructionSet.AddMnemonic] = (Node306Program.Coordinate, "'add", Node306BinaryOperationTagBits),
        [CvmInstructionSet.AddConstantMnemonic] = (Node406Program.Coordinate, "'add", Node406BinaryConstantTagBits),
        [CvmInstructionSet.SubtractMnemonic] = (Node306Program.Coordinate, "'sub", Node306BinaryOperationTagBits),
        [CvmInstructionSet.SubtractConstantMnemonic] = (Node406Program.Coordinate, "'sub", Node406BinaryConstantTagBits),
        [CvmInstructionSet.ReverseSubtractMnemonic] = (Node406Program.Coordinate, "'rsb", Node406BinaryStackTagBits),
        [CvmInstructionSet.ReverseSubtractConstantMnemonic] = (Node406Program.Coordinate, "'rsb", Node406BinaryConstantTagBits),
        [CvmInstructionSet.AndMnemonic] = (Node306Program.Coordinate, "'and", Node306BinaryOperationTagBits),
        [CvmInstructionSet.AndConstantMnemonic] = (Node406Program.Coordinate, "'and", Node406BinaryConstantTagBits),
        [CvmInstructionSet.XorMnemonic] = (Node306Program.Coordinate, "'xor", Node306BinaryOperationTagBits),
        [CvmInstructionSet.XorConstantMnemonic] = (Node406Program.Coordinate, "'xor", Node406BinaryConstantTagBits),
        [CvmInstructionSet.OrMnemonic] = (Node306Program.Coordinate, "'or", Node306BinaryOperationTagBits),
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
        [CvmInstructionSet.MultiplyByTwoMnemonic] = (Node306Program.Coordinate, "'mul2", Node306UnaryOperationTagBits),
        [CvmInstructionSet.UnsignedDivideByTwoMnemonic] = (Node306Program.Coordinate, "'udiv2", Node306UnaryOperationTagBits),
        [CvmInstructionSet.DivideByTwoMnemonic] = (Node306Program.Coordinate, "'div2", Node306UnaryOperationTagBits),
        [CvmInstructionSet.AbsoluteValueMnemonic] = (Node306Program.Coordinate, "'abs", Node306UnaryOperationTagBits),
        [CvmInstructionSet.BitCountMnemonic] = (Node509Program.Coordinate, "'bitcnt", Node509UnaryArithmeticTagBits),
        // ---- node 306's 16-bit register operations (2026-10-04): the entries above for add/sub/and/xor/or (binary) and
        // inv/inc/dec/neg/mul2/udiv2/div2/abs (unary) were REPOINTED here from their retired node-406/509 homes; these
        // are the genuinely new words. "bitcount" is the new 'bitcount word -- NOT the retired "bitcnt" just above.
        [CvmInstructionSet.PackBytesMnemonic] = (Node306Program.Coordinate, "'packbytes", Node306BinaryOperationTagBits),
        [CvmInstructionSet.Mask15Mnemonic] = (Node306Program.Coordinate, "'mask15", Node306UnaryOperationTagBits),
        [CvmInstructionSet.Invert15Mnemonic] = (Node306Program.Coordinate, "'inv15", Node306UnaryOperationTagBits),
        [CvmInstructionSet.BoolMnemonic] = (Node306Program.Coordinate, "'bool", Node306UnaryOperationTagBits),
        [CvmInstructionSet.ClearLowestBitMnemonic] = (Node306Program.Coordinate, "'clearlow", Node306UnaryOperationTagBits),
        [CvmInstructionSet.LowestBitMnemonic] = (Node306Program.Coordinate, "'lowbit", Node306UnaryOperationTagBits),
        [CvmInstructionSet.BitCountOperationMnemonic] = (Node306Program.Coordinate, "'bitcount", Node306UnaryOperationTagBits),
        [CvmInstructionSet.HighByteMnemonic] = (Node306Program.Coordinate, "'highbyte", Node306UnaryOperationTagBits),
        [CvmInstructionSet.LowByteMnemonic] = (Node306Program.Coordinate, "'lowbyte", Node306UnaryOperationTagBits),
        [CvmInstructionSet.ByteSwapMnemonic] = (Node306Program.Coordinate, "'byteswap", Node306UnaryOperationTagBits),
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
        // RETIRED 2026-10-03 (node-509 redesign), per Stefan: "node 510 is no longer part of the CVM." The seven
        // node-510 register-access entries below are commented out, not deleted ("do not remove any
        // opcodes"). Besides being dead, two of them (rpop/rpush) COLLIDED with the live node-509 words
        // 'rpop/'rpush defined above: an index-initializer entry later in the literal silently replaces an
        // earlier one, so "rpush" resolved to node 510's "reg/pu" and assembling it failed with "node 510's
        // source does not define reg/pu". The register words are now node 509's 'rpop/'rpush/'dpop/'dpush/
        // 'rpopi/... (see the node-509 block above); rld/rst/rclr/rpo2/rpu2 have no node-509 equivalent.
        // [CvmInstructionSet.LoadRegisterFileMnemonic] = (Node510Program.Coordinate, "reg/ld", Node510RegisterAccessTag),
        // [CvmInstructionSet.StoreRegisterFileMnemonic] = (Node510Program.Coordinate, "reg/st", Node510RegisterAccessTag),
        // [CvmInstructionSet.PopRegisterFileMnemonic] = (Node510Program.Coordinate, "reg/po", Node510RegisterAccessTag),
        // [CvmInstructionSet.PushRegisterFileMnemonic] = (Node510Program.Coordinate, "reg/pu", Node510RegisterAccessTag),
        // Three genuinely new ops from the same family (2026-09-27) -- see
        // CvmInstructionSet.RegisterClearMnemonic's own remarks. "rlit" is deliberately not wired here --
        // see CvmInstructionSet.RegisterSetMnemonic's own remarks for why.
        // [CvmInstructionSet.RegisterClearMnemonic] = (Node510Program.Coordinate, "reg/clr", Node510RegisterAccessTag),
        // [CvmInstructionSet.RegisterPopPairMnemonic] = (Node510Program.Coordinate, "reg/po2", Node510RegisterAccessTag),
        // [CvmInstructionSet.RegisterPushPairMnemonic] = (Node510Program.Coordinate, "reg/pu2", Node510RegisterAccessTag),

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
        // RETIRED 2026-10-03: node 510 is no longer part of the CVM, so its seven register-access layouts
        // below are commented out (kept per "do not remove any opcodes"). The node-509 register words need no
        // entry here -- they are encoded by TryResolveNode509SpecialOpcode.
        // REPOINTED 2026-09-27 from node 511's OLD 5-bit/5-bit layout (Node511FunctionField*/
        // Node511RegisterFieldBitMask, kept for historical reference in CvmInstructionSet) to the new
        // "extended register access" node's 6-bit/3-bit one -- the FIRST family in this dictionary whose
        // own register field needs a real RegisterFieldShift (4), since it no longer sits at bit 0.
        // [CvmInstructionSet.LoadRegisterFileMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        // [CvmInstructionSet.StoreRegisterFileMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        // [CvmInstructionSet.PopRegisterFileMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        // [CvmInstructionSet.PushRegisterFileMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        // [CvmInstructionSet.RegisterClearMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        // [CvmInstructionSet.RegisterPopPairMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
        // [CvmInstructionSet.RegisterPushPairMnemonic] = (CvmInstructionSet.RegisterAccessFunctionFieldBitMask, CvmInstructionSet.RegisterAccessFunctionFieldShift, CvmInstructionSet.RegisterAccessFunctionFieldBaseAddress, CvmInstructionSet.RegisterAccessRegisterFieldBitMask, CvmInstructionSet.RegisterAccessRegisterFieldShift),
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
        // Node 306's 16-bit register operations (2026-10-04): the function field is the node-306 word address (8 bits at
        // bits 11-4 for unary, 4 bits at bits 11-8 for binary); the register field(s) are x (bits 3-0) and, for binary, y (bits 7-4).
        [CvmInstructionSet.InvertMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.IncrementMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.DecrementMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.NegMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.MultiplyByTwoMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.UnsignedDivideByTwoMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.DivideByTwoMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.AbsoluteValueMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.Mask15Mnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.Invert15Mnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.BoolMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.ClearLowestBitMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.LowestBitMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.BitCountOperationMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.HighByteMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.LowByteMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.ByteSwapMnemonic] = (Node306UnaryFunctionFieldBitMask, Node306UnaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask, 0),
        [CvmInstructionSet.AddMnemonic] = (Node306BinaryFunctionFieldBitMask, Node306BinaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask | CvmInstructionSet.Operation16SecondRegisterBitMask, 0),
        [CvmInstructionSet.SubtractMnemonic] = (Node306BinaryFunctionFieldBitMask, Node306BinaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask | CvmInstructionSet.Operation16SecondRegisterBitMask, 0),
        [CvmInstructionSet.AndMnemonic] = (Node306BinaryFunctionFieldBitMask, Node306BinaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask | CvmInstructionSet.Operation16SecondRegisterBitMask, 0),
        [CvmInstructionSet.XorMnemonic] = (Node306BinaryFunctionFieldBitMask, Node306BinaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask | CvmInstructionSet.Operation16SecondRegisterBitMask, 0),
        [CvmInstructionSet.OrMnemonic] = (Node306BinaryFunctionFieldBitMask, Node306BinaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask | CvmInstructionSet.Operation16SecondRegisterBitMask, 0),
        [CvmInstructionSet.PackBytesMnemonic] = (Node306BinaryFunctionFieldBitMask, Node306BinaryFunctionFieldShift, 0, CvmInstructionSet.Operation16FirstRegisterBitMask | CvmInstructionSet.Operation16SecondRegisterBitMask, 0),
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
        // ---- REDESIGNED 2026-10-03 (CVM redesign) --------------------------------------------------
        // The node-507 "special" family (ret/link/unlink at 0, lcall/ljmp at 0, push at 0 -- all shift 0,
        // added 2026-10-01/02, see the comments retained just below) is SUPERSEDED by node 509's special-
        // word table, whose address field ("wwwwww") sits at bits 11-6: shift 6
        // (Node509SpecialAddressShift; was 7 until 2026-10-04). The mnemonics keep their entries -- the same mnemonic
        // strings, a different node/tag/shift (see NodeSymbolByMnemonic's own node-509 block). The
        // old entries are kept as comments, per "do not remove any opcodes":
        //   [RetMnemonic] = 0, [LinkMnemonic] = 0, [UnlinkMnemonic] = 0,
        //   [LongCallMnemonic] = 0, [LongJumpMnemonic] = 0, [PushMnemonic] = 0,
        // NOTE: BuildDecodeTable/BuildEncodeTable do NOT read this dictionary for the node-509 special
        // family any more -- they route those mnemonics through TryResolveNode509SpecialOpcode, which
        // applies Node509SpecialAddressShift itself and also fills in the size field this dictionary has
        // no way to express. The entries below are listed for the audit trail (and so a future reader
        // grepping for a mnemonic here still finds it), not because anything consults them.
        [CvmInstructionSet.RetMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.CallMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.JmpMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.LinkMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.UnlinkMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.RegisterPopMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.DoublePopMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.PushMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.Push2Mnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.RegisterPushMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.DoublePushMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.RegisterPopIndirectMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.DoublePopIndirectMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.RegisterPushIndirectMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.DoublePushIndirectMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.RegisterIncrementMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.RegisterDecrementMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.RegisterAddMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.HaltMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.DoubleFetchMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.DoubleStoreMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.RegisterLiteralMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.DoubleLiteralMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.RegisterJumpMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.RegisterCallMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.DropMnemonic] = Node509SpecialAddressShift,
        [CvmInstructionSet.DupMnemonic] = Node509SpecialAddressShift,
      };

  /// <summary>
  /// ADDED 2026-10-03 (CVM redesign). True when a <see cref="NodeSymbolByMnemonic"/> entry belongs to node
  /// 509's special-word table: resolved against node 509 AND carrying the base tag
  /// <see cref="Node509SpecialBaseTagBits"/> (0x0000 -- the focus bit is added per word from the comment
  /// under the label, see <see cref="TryReadNode509SpecialComment"/>). Node 509's other (retired, inert)
  /// mnemonic entries use <see cref="Node509UnaryArithmeticTagBits"/> and so never match.
  /// </summary>
  private static bool IsNode509SpecialWord(int nodeCoordinate, int tag) =>
      nodeCoordinate == Node509Program.Coordinate && tag == Node509SpecialBaseTagBits;

  /// <summary>
  /// ADDED 2026-10-03 (CVM redesign), per Stefan: <i>"there is a comment in the next line [of the label]
  /// that specify the size of the instruction, which must also be encoded, and the focus bit 13."</i>
  /// Finds the label line <c>: 'name ...</c> for <paramref name="symbolName"/> in node 509's (macro-
  /// expanded) source and parses the first non-blank line after it, which must read
  /// <c>// N with focus</c> or <c>// N without focus</c>: <paramref name="words"/> = N (the entry's length
  /// in words, encoded as N - 1 in bits 6-4) and <paramref name="focus"/> = whether bit 13 is set.
  /// <c>/* ... */</c> block comments are blanked out first (newlines kept), so a retired block of old code
  /// in the source can never be mistaken for the table. Returns false with a <paramref name="reason"/>
  /// when the label line or its comment cannot be found/parsed -- never a guessed size or focus.
  /// </summary>
  private static bool TryReadNode509SpecialComment(string expandedSource, string symbolName, out int words, out bool focus, out string? reason)
  {
    words = 0;
    focus = false;
    reason = null;

    if (string.IsNullOrEmpty(expandedSource))
    {
      reason = "node 509's compile did not keep its expanded source text, so the \"// N with(out) focus\" comment under the label cannot be read.";
      return false;
    }

    // Blank out /* ... */ blocks (keep newlines, so line structure survives); leave // comments alone,
    // since those are exactly what is being read -- a "/*" inside a // comment is not a block start.
    StringBuilder cleaned = new(expandedSource.Length);
    int index = 0;
    while (index < expandedSource.Length)
    {
      char c = expandedSource[index];
      if (c == '/' && index + 1 < expandedSource.Length && expandedSource[index + 1] == '/')
      {
        while (index < expandedSource.Length && expandedSource[index] != '\n')
        {
          cleaned.Append(expandedSource[index]);
          index++;
        }

        continue;
      }

      if (c == '/' && index + 1 < expandedSource.Length && expandedSource[index + 1] == '*')
      {
        int end = expandedSource.IndexOf("*/", index + 2, StringComparison.Ordinal);
        int stop = end < 0 ? expandedSource.Length : end + 2;
        for (int k = index; k < stop; k++)
        {
          cleaned.Append(expandedSource[k] == '\n' ? '\n' : ' ');
        }

        index = stop;
        continue;
      }

      cleaned.Append(c);
      index++;
    }

    string[] lines = cleaned.ToString().Split('\n');
    Regex labelLine = new($@"^\s*:\s*{Regex.Escape(symbolName)}(\s|$)", RegexOptions.CultureInvariant);
    Regex commentLine = new(@"^\s*//\s*(\d+)\s+(with|without)\s+focus\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
    {
      if (!labelLine.IsMatch(lines[lineIndex]))
      {
        continue;
      }

      int next = lineIndex + 1;
      while (next < lines.Length && lines[next].Trim().Length == 0)
      {
        next++;
      }

      if (next >= lines.Length)
      {
        reason = $"\"{symbolName}\" is the last line of node 509's source -- there is no \"// N with(out) focus\" comment line under it.";
        return false;
      }

      Match match = commentLine.Match(lines[next]);
      if (!match.Success)
      {
        reason = $"the line under \"{symbolName}\" in node 509's source reads \"{lines[next].Trim()}\", expected a comment \"// N with focus\" or \"// N without focus\" (N = number of words in the entry).";
        return false;
      }

      words = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
      focus = string.Equals(match.Groups[2].Value, "with", StringComparison.OrdinalIgnoreCase);
      return true;
    }

    reason = $"no label line \": {symbolName}\" was found in node 509's expanded source.";
    return false;
  }

  /// <summary>
  /// ADDED 2026-10-03 (CVM redesign). Computes the opcode word (register field still 0) of one node-509
  /// special word: <c>focus(0x2000) | (address &lt;&lt; 6) | ((words - 1) &lt;&lt; 4)</c>, where
  /// <c>address</c> is the word address of the entry's <c>'name</c> label in node 509's compile and
  /// <c>words</c> and <c>focus</c> come from the <c>// N with(out) focus</c> comment under the label
  /// (<see cref="TryReadNode509SpecialComment"/>) -- the single source of truth for both.
  ///
  /// Returns false, with a human-readable <paramref name="reason"/>, when the word cannot be represented:
  /// the address does not fit the 6-bit field (node 509's table must live in its first 64 words), the
  /// comment is missing or unreadable, or N is outside 1..4 (the 2-bit size field holds N - 1). Never
  /// guesses -- an unresolvable entry is omitted, same graceful convention every other mnemonic that
  /// fails to resolve already follows (DiagnoseUnresolvedWiredMnemonic then says why).
  /// </summary>
  private static bool TryResolveNode509SpecialOpcode(int tag, F18CompileResult compile, F18ExportedSymbol symbol, out int opcode, out string? reason)
  {
    opcode = 0;
    reason = null;

    int address = symbol.Value & CvmWordCodec.WordMask;
    int maxAddress = Node509SpecialAddressFieldBitMask >> Node509SpecialAddressShift;
    if (address < 0 || address > maxAddress)
    {
      reason = $"\"{symbol.Name}\" resolved to node 509 address 0x{address:X}, outside the 6-bit word-address field of the special-word encoding (0x0-0x{maxAddress:X}) -- node 509's table must sit in its first {maxAddress + 1} words.";
      return false;
    }

    if (!TryReadNode509SpecialComment(compile.ExpandedSource, symbol.Name, out int words, out bool focus, out string? commentReason))
    {
      reason = commentReason;
      return false;
    }

    int maxWords = (Node509SpecialSizeFieldBitMask >> Node509SpecialSizeFieldShift) + 1;
    if (words < 1 || words > maxWords)
    {
      reason = $"the comment under \"{symbol.Name}\" in node 509's source says {words} word(s), but the 2-bit size field only holds 1..{maxWords}.";
      return false;
    }

    opcode = tag | (focus ? Node509SpecialFocusBit : 0) | (address << Node509SpecialAddressShift) | ((words - 1) << Node509SpecialSizeFieldShift);
    return true;
  }

  /// <summary>
  /// ADDED 2026-10-02, for <c>if</c> (CvmInstructionSet.IfMnemonic, renamed from <c>cbr</c> the same
  /// day): node 406's own ten named condition words, each its own tick-prefixed F18 symbol, by the
  /// name Stefan's own confirmed assembler syntax ("if &lt;reg&gt; &lt;cond&gt; &lt;target&gt;") uses
  /// for them. Unlike <see cref="NodeSymbolByMnemonic"/> (one node+symbol pair per MNEMONIC), this is
  /// one node+symbol pair per NAMED OPERAND VALUE of a single mnemonic -- a genuinely different
  /// resolution shape this file had no precedent for before <c>if</c>, since every earlier tagged
  /// mnemonic resolved its own address as a whole, never per a user-chosen operand token. "cond" is
  /// resolved exactly the same way any other node-resolved field here is (a live F18 symbol address,
  /// looked up against <paramref name="compiledRam"/> inside <see cref="AssembleProgram"/>'s own dedicated
  /// <c>if</c> handling -- see its own remarks) -- this dictionary only supplies the name-to-symbol
  /// mapping, not the resolution itself. Node 406's own source, as pasted, declares each both under an
  /// internal <c>x1/...</c> name and a public tick-prefixed one (<c>'==0</c> etc.) via a <c>.loc</c>
  /// directive this project does not yet fully understand (flagged, not guessed at) -- the public tick-
  /// prefixed name is used here, exactly the convention every other <see cref="NodeSymbolByMnemonic"/>
  /// entry already follows (<c>'ret</c>, <c>'link</c>, ... never the internal <c>m/...</c>/<c>x1/...</c>
  /// name). Node 406's own live source does not actually define these symbols yet as of this entry's own
  /// addition (<see cref="Node406Program"/> is still 2026-09-30's older "read register" stub, not yet
  /// synced to the condition-word content Stefan has since pasted in chat, per this project's own
  /// standing "do not sync the source yet" instruction) -- so, exactly like <c>'push</c>/<c>'lcall</c>/
  /// <c>'ljmp</c> against node 507 before THEIR own node was finally caught up, resolving any of these
  /// ten names today will fail with a specific, loud diagnosis (see <see cref="AssembleProgram"/>'s own <c>if</c>
  /// handling) rather than silently producing a wrong address -- this dictionary is prepared wiring for
  /// whenever node 406 catches up, not a claim that it works today.
  /// </summary>
  private static readonly IReadOnlyDictionary<string, string> IfConditionSymbolByName =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["==0"] = "'==0",
        ["!=0"] = "'!=0",
        ["<0"] = "'<0",
        ["<=0"] = "'<=0",
        [">0"] = "'>0",
        [">=0"] = "'>=0",
        ["true"] = "'true",
        ["false"] = "'false",
        ["even"] = "'even",
        ["odd"] = "'odd",
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
  /// <see cref="BuildEncodeTable"/>/<see cref="AssembleProgram"/>/<see cref="DisassemblePage0"/>'s own remarks
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

      // ADDED 2026-10-03 (CVM redesign): node 509's special-word table has its own opcode formula (tag |
      // address << 6 | size << 4 | register) -- see TryResolveNode509SpecialOpcode. A word that takes a
      // register operand ("x", bits 3-0) gets 16 decode entries, one per register value, exactly like
      // node 511's old register-file ops did below; a word without one decodes only with x = 0 (a
      // non-zero x on e.g. "ret" is not what this assembler emits, so it is not claimed here).
      if (IsNode509SpecialWord(nodeCoordinate, tag))
      {
        if (!TryResolveNode509SpecialOpcode(tag, compile, symbol, out int specialOpcode, out _))
        {
          continue;
        }

        if (encoding == CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue)
        {
          for (int register = 0; register <= CvmInstructionSet.SpecialRegisterFieldBitMask; register++)
          {
            table[specialOpcode | register] = (mnemonic, wordLength, register);
          }
        }
        else
        {
          table[specialOpcode] = (mnemonic, wordLength, null);
        }

        continue;
      }

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
  /// The encode direction, for <see cref="AssembleProgram"/>: resolves <see cref="Instructions"/> against
  /// THIS run's own compiles -- each mnemonic against its own node
  /// (<see cref="NodeSymbolByMnemonic"/>) -- and returns a map from mnemonic (case-insensitive) to its
  /// opcode word, word length, whether it takes an operand, and (node 511's four ops only) whether that
  /// operand gets EMBEDDED into the same opcode word rather than appended as a separate trailing word,
  /// plus the bit mask it's embedded under.
  ///
  /// For <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/> mnemonics
  /// specifically, <c>Opcode</c> here is only the BASE word (tag | function-select field) -- the
  /// register operand is still missing and gets OR'd in by <see cref="AssembleProgram"/> itself once it knows
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

      // ADDED 2026-10-03 (CVM redesign): node 509's special-word table -- see BuildDecodeTable's own
      // remarks on the same branch, and TryResolveNode509SpecialOpcode for the formula. For a register-
      // taking word the entry's Opcode is the BASE word (register 0); the register ("x", bits 3-0, mask
      // CvmInstructionSet.SpecialRegisterFieldBitMask, shift 0) is OR'd in by Assemble once it knows the
      // operand -- the same embedded-operand mechanism node 511's old ops used.
      if (IsNode509SpecialWord(nodeCoordinate, tag))
      {
        if (!TryResolveNode509SpecialOpcode(tag, compile, symbol, out int specialOpcode, out _))
        {
          continue;
        }

        table[mnemonic] = encoding == CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue
            ? (specialOpcode, wordLength, true, true, CvmInstructionSet.SpecialRegisterFieldBitMask, 0)
            : (specialOpcode, wordLength, hasOperand, false, 0, 0);
        continue;
      }

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
  /// ADDED 2026-10-02, per Stefan directly asking this file's own "if"-vs-<see cref="CvmAssembler"/>
  /// gap to be closed by inventing the missing mechanism (see <see cref="CvmInstructionSet.IfPrimitiveNamePrefix"/>/
  /// <see cref="CvmInstructionSet.IfConditionPrimitiveKeyByName"/>'s own remarks for the full design).
  /// <see cref="BuildEncodeTable"/> resolves one node+symbol pair per MNEMONIC -- exactly what
  /// <see cref="Ga144.Evb.Ide.Services.CvmPrimitiveTableExporter"/> needs to feed <see cref="Ga144.Cvm.Toolchain.CvmPrimitiveTable"/>
  /// for every OTHER node-wired mnemonic (<c>ret</c>/<c>link</c>/<c>unlink</c>/<c>lcall</c>/<c>ljmp</c>/
  /// <c>push</c>). <c>if</c> needs a DIFFERENT shape entirely -- one node+symbol pair per NAMED
  /// CONDITION VALUE (<see cref="IfConditionSymbolByName"/>'s own ten entries), which is exactly why
  /// <c>if</c>'s own <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord"/>
  /// shape is structurally excluded from <see cref="BuildEncodeTable"/>'s own <see cref="Instructions"/>
  /// iteration (see that list's own remarks) -- this method is the parallel, condition-keyed sibling
  /// <see cref="Ga144.Evb.Ide.Services.CvmPrimitiveTableExporter"/> also reads, so <see cref="Ga144.Cvm.Toolchain.CvmAssembler"/>'s
  /// own, separate linker-based assembler can resolve <c>if</c> too, through the exact same
  /// <see cref="Ga144.Cvm.Toolchain.CvmRelocationType.CvmOpcode"/> relocation mechanism it already
  /// trusts for everything else -- see that assembler's own dedicated <c>if</c> case.
  ///
  /// Each entry's KEY is <see cref="CvmInstructionSet.IfPrimitiveNamePrefix"/> plus that condition's own
  /// <see cref="CvmInstructionSet.IfConditionPrimitiveKeyByName"/> value (e.g. <c>"if.eq0"</c> for
  /// <c>"==0"</c>) -- never the raw condition name itself (see that dictionary's own remarks for why:
  /// four of the ten names contain a bare <c>=</c>, which would corrupt <see cref="Ga144.Cvm.Toolchain.CvmPrimitiveTable"/>'s
  /// own hand-written <c>.gaprim</c> text format). Each entry's VALUE is the complete BASE word --
  /// <c>if</c>'s own tag (<c>0xA000</c>) OR'd with that condition's own node-406-resolved address,
  /// already shifted into bits 7-4 -- with the register field left at 0 for
  /// <see cref="Ga144.Cvm.Toolchain.CvmRelocation.EmbeddedValue"/> to OR in at link time, exactly
  /// mirroring how node 308's address-register family's own base word is produced by
  /// <see cref="BuildEncodeTable"/>'s own <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/>
  /// branch just above. A condition whose symbol isn't defined in node 406's CURRENT compile (still
  /// true as of this method's own addition -- <see cref="Node406Program"/> is 2026-09-30's older
  /// stub, per standing instruction) or whose resolved address doesn't fit this shape's own 4-bit
  /// "cond" field is simply omitted, the same graceful-omission convention
  /// <see cref="BuildEncodeTable"/>/<see cref="BuildDecodeTable"/> already use for every other
  /// mnemonic that fails to resolve this run.
  /// </summary>
  public static IReadOnlyDictionary<string, int> BuildIfConditionEncodeTable(IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    var table = new Dictionary<string, int>(StringComparer.Ordinal);
    if (CvmInstructionSet.TryGetShape(CvmInstructionSet.IfMnemonic) is not { } ifShape ||
        !compiledRam.TryGetValue(Node406Program.Coordinate, out F18CompileResult? compile))
    {
      return table;
    }

    CvmInstructionSet.CvmInstructionShape? condShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.CondMnemonic);
    int maxCond = ifShape.ValueBitMask >> ifShape.ValueBitShift;
    foreach ((string conditionName, string symbolName) in IfConditionSymbolByName)
    {
      if (!compile.Symbols.TryGetValue(symbolName, out F18ExportedSymbol? symbol))
      {
        continue;
      }

      int resolvedCond = symbol.Value & CvmWordCodec.WordMask;
      if (resolvedCond < 0 || resolvedCond > maxCond)
      {
        continue;
      }

      if (!CvmInstructionSet.IfConditionPrimitiveKeyByName.TryGetValue(conditionName, out string? key))
      {
        continue;
      }

      table[CvmInstructionSet.IfPrimitiveNamePrefix + key] = ifShape.Tag | ((resolvedCond << ifShape.ValueBitShift) & ifShape.ValueBitMask);

      // ADDED 2026-10-03: "cond" (CvmInstructionSet.CondMnemonic) shares if's own ten node-406 condition words
      // and the same cccc/xxxx field layout, so its ten synthetic keys ("cond.eq0", ...) are computed from the
      // very same resolvedCond, just with cond's own tag (0x9000).
      if (condShape is not null)
      {
        table[CvmInstructionSet.CondPrimitiveNamePrefix + key] = condShape.Tag | ((resolvedCond << condShape.ValueBitShift) & condShape.ValueBitMask);
      }
    }

    return table;
  }

  /// <summary>
  /// CORRECTED 2026-09-11: Stefan reported "arld 1" silently assembling as a bare, operand-dropped
  /// <c>nop</c> even though node 306 is a real, wired opcode family (see <see cref="NodeSymbolByMnemonic"/>'s
  /// own node 306 entries) -- and firmly rejected the theory this session first reached for (node 307's
  /// own compile failing), so the real defect was in THIS file, not the node source. It was: <see cref="AssembleProgram"/>'s
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
  /// method is <see cref="AssembleProgram"/>'s new first check on that fallback path: it returns a precise,
  /// actionable reason when the mnemonic IS wired (so <see cref="AssembleProgram"/> now fails loudly instead of
  /// nop-substituting), or null when it genuinely has no wiring at all (so <see cref="AssembleProgram"/>'s
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

    // ADDED 2026-10-03 (CVM redesign): node 509's special-word table can fail AFTER its label resolved --
    // the address may not fit the 6-bit field, or the comment under the label (size / focus) may be
    // missing, unparsable or out of range. TryResolveNode509SpecialOpcode already says exactly why.
    if (IsNode509SpecialWord(wiring.NodeCoordinate, wiring.Tag) &&
        !TryResolveNode509SpecialOpcode(wiring.Tag, compile, symbol, out _, out string? specialReason))
    {
      return $"is implemented on node {wiring.NodeCoordinate:000}'s special-word table, but cannot be encoded: {specialReason}";
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

  /// <summary>Display name the debugger's one assembled program carries into <see cref="CvmLinker"/> (only ever seen in link diagnostics).</summary>
  private const string DebuggerProgramObjectName = "program";

  /// <summary>Longest list of assemble/link errors <see cref="AssembleProgram"/> reports in one message; the rest are summarised as "and N more".</summary>
  private const int MaxReportedErrors = 8;

  /// <summary>
  /// <b>THE CVM Debugger's assemble step, 2026-10-04 -- there is no second assembler any more.</b> Per Stefan
  /// ("it is time to unify the 2 assembler into 1. keep the syntax with the spaces and forget the syntax with
  /// commas"), the IDE's own immediately-resolving assembler (the former <c>ParseSource</c>/<c>Assemble</c> pair and
  /// all their encoders, ~1,700 lines that duplicated <see cref="CvmAssembler"/>) was deleted. What remains of this
  /// class around it is the half the toolchain cannot have: the live, node-compile-driven opcode tables
  /// (<see cref="BuildEncodeTable"/>/<see cref="BuildIfConditionEncodeTable"/>), the decoder and the disassembler.
  ///
  /// The pipeline: (1) <see cref="CvmAssembler.Assemble"/> turns <paramref name="sourceText"/> into one relocatable
  /// object (operands separated by spaces and/or commas);
  /// (2) <see cref="CvmPrimitiveTableExporter.BuildPrimitiveTable"/> turns <paramref name="compiledRam"/> into the
  /// primitive table -- the SAME table a Build hands <c>galink</c>; (3) <see cref="CvmLinker.Link"/> binds every
  /// opcode relocation against it with <see cref="CvmLinkOptions.ApplyEntryLayout"/> off, so CODE starts at address 0
  /// with no synthesized entry vector (the debugger's CPU simply fetches from 0). A linker "undefined reference" to a
  /// mnemonic that IS a CVM opcode is turned back into the precise <see cref="DiagnoseUnresolvedWiredMnemonic"/>
  /// explanation (node did not compile / symbol missing / address does not fit the special-word field), so the old
  /// helpful messages survive.
  ///
  /// <c>Labels</c> maps every label the program defines (local and exported, never primitive names) to its final
  /// address, case-insensitively, for the memory inspector's "Label" column. Behaviour that differs from the retired
  /// assembler: label names are case-sensitive in the source (mnemonics and condition names are not); an opcode that
  /// has no live node behind it is a link error rather than a silent <c>nop</c>; <c>slit &lt;label&gt;</c> (a label's
  /// address as a literal) is no longer accepted -- <c>slit</c> takes a number.
  /// </summary>
  public static (List<int>? Words, IReadOnlyDictionary<string, int>? Labels, string? Error) AssembleProgram(
      string sourceText,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    (CvmObjectFile? objectFile, IReadOnlyList<string> assembleErrors) = CvmAssembler.Assemble(sourceText);
    if (objectFile is null)
    {
      return (null, null, JoinErrors(assembleErrors));
    }

    CvmPrimitiveTable primitives = CvmPrimitiveTableExporter.BuildPrimitiveTable(compiledRam);
    CvmLinker.Result linked = CvmLinker.Link(
        [new CvmLinkObjectInput(DebuggerProgramObjectName, objectFile)],
        [],
        primitives,
        new CvmLinkOptions { ApplyEntryLayout = false });
    if (!linked.Success || linked.Image is null)
    {
      return (null, null, JoinErrors([.. linked.Messages.Select(message => ExplainLinkMessage(message, compiledRam))]));
    }

    var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (CvmImageSymbol symbol in linked.Image.Symbols)
    {
      // DefiningObjectName is null for a symbol derived from a primitive-table entry (nop, call, ...) --
      // those are opcodes, not program labels.
      if (symbol.DefiningObjectName is not null)
      {
        labels[symbol.Name] = symbol.Address;
      }
    }

    return (new List<int>(linked.Image.Words), labels, null);
  }

  private static string JoinErrors(IReadOnlyList<string> errors)
  {
    if (errors.Count <= MaxReportedErrors)
    {
      return string.Join("\n", errors);
    }

    return string.Join("\n", errors.Take(MaxReportedErrors)) + $"\n... and {errors.Count - MaxReportedErrors} more error(s).";
  }

  /// <summary>
  /// Rewrites the linker's generic <c>undefined reference to "X" (...)</c> into the debugger's own, more specific
  /// wording when "X" is a CVM mnemonic (resolved through <see cref="DiagnoseUnresolvedWiredMnemonic"/>) or one of
  /// the synthetic <c>if.&lt;key&gt;</c>/<c>cond.&lt;key&gt;</c> condition keys; any other message (a genuinely
  /// undefined label, a duplicate definition, ...) passes through unchanged.
  /// </summary>
  private static string ExplainLinkMessage(string message, IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    Match match = UndefinedReferencePattern.Match(message);
    if (!match.Success)
    {
      return message;
    }

    string name = match.Groups["name"].Value;
    foreach (string prefix in new[] { CvmInstructionSet.IfPrimitiveNamePrefix, CvmInstructionSet.CondPrimitiveNamePrefix })
    {
      if (name.StartsWith(prefix, StringComparison.Ordinal))
      {
        string keyword = prefix.TrimEnd('.');
        return $"\"{keyword} {name[prefix.Length..]}\": node {Node406Program.Coordinate:000} did not compile this run, or its current source does not define the symbol that condition resolves to -- fix/save it in the Node Editor, then re-assemble.";
      }
    }

    string? diagnosis = DiagnoseUnresolvedWiredMnemonic(name, compiledRam);
    if (diagnosis is not null)
    {
      return $"\"{name}\" {diagnosis}";
    }

    if (CvmInstructionSet.TryGetShape(name) is not null)
    {
      return $"\"{name}\" is a known CVM mnemonic, but no live node currently implements it, so it has no opcode to link against.";
    }

    return message;
  }

  private static readonly Regex UndefinedReferencePattern = new("^undefined reference to \"(?<name>[^\"]+)\"", RegexOptions.Compiled);

  /// <summary>
  /// Shared by <see cref="CvmDebugSession.AssembleAndLoadProgram"/> (a live session's own port-backed
  /// simulated SRAM) and <see cref="ViewModels.CvmDebuggerViewModel"/>'s standalone Assembly Code path
  /// (a plain in-memory <see cref="CvmSimulatedSram"/> that exists whether or not a chip is connected):
  /// assembles and links <paramref name="sourceText"/> against <paramref name="compiledRam"/>
  /// (<see cref="AssembleProgram"/>) and, on success, overwrites <paramref name="sram"/>'s page 0 with the
  /// result starting at address 0, zero-filling any leftover tail from <paramref name="previousProgram"/> if it
  /// was longer, so no stale opcode lingers past the new program's end. Returns the new word list (the caller's
  /// own job to remember as its "currently loaded program") and never touches <paramref name="sram"/> at all on
  /// an assemble/link failure. <c>Labels</c> is <see cref="AssembleProgram"/>'s own label map.
  /// </summary>
  public static (List<int>? Words, IReadOnlyDictionary<string, int>? Labels, string? Error) AssembleAndLoadProgram(
      string sourceText,
      CvmSimulatedSram sram,
      IReadOnlyList<int> previousProgram,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    (List<int>? words, IReadOnlyDictionary<string, int>? labels, string? error) = AssembleProgram(sourceText, compiledRam);
    if (words is null)
    {
      return (null, null, error);
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
  /// <c>cbr</c> is deliberately NOT assemblable yet (see <see cref="AssembleProgram"/>'s own remarks) but DOES
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

    // ADDED 2026-10-03: maps an "if" word's condition field (an address in node 406) back to its name
    // ("==0", "even", ...) so the listing reads "if r0 ==0 then +2 -> 0x0016". Null (so the listing falls
    // back to "cond#N") when node 406 did not compile this run or no condition word sits at that address.
    string? ifConditionName(int conditionAddress)
    {
      if (!compiledRam.TryGetValue(Node406Program.Coordinate, out F18CompileResult? node406Compile))
      {
        return null;
      }

      foreach ((string conditionName, string conditionSymbol) in IfConditionSymbolByName)
      {
        if (node406Compile.Symbols.TryGetValue(conditionSymbol, out F18ExportedSymbol? conditionExport) &&
            (conditionExport.Value & CvmWordCodec.WordMask) == conditionAddress)
        {
          return conditionName;
        }
      }

      return null;
    }

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
      string? selfDescribing = CvmInstructionSet.TryDescribeSelfDecodingWord(word, out int selfDescribingWordLength, wordAddress: address, nextWord: nextWord, nextWord2: nextWord2, ifConditionName: ifConditionName);
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
          string registerNote = $"{instruction.Mnemonic} {CvmInstructionSet.FormatRegisterOperand(instruction.Mnemonic, embeddedOperand)}";

          // 2026-10-04: rlit (2 words) / dlit (3 words) carry their literal in the following word(s) --
          // rlit one 16-bit word; dlit LOW word then HIGH word, shown as one 32-bit number.
          if (instruction.WordLength == 2 && address + 1 < endAddressExclusive)
          {
            registerNote += " " + CvmInstructionSet.FormatOperand(sram.Read(CvmMemoryProtocol.CombineAddress(0, address + 1)));
          }
          else if (instruction.WordLength == 3 && address + 2 < endAddressExclusive)
          {
            uint wideLiteral = ((uint)(sram.Read(CvmMemoryProtocol.CombineAddress(0, address + 2)) & 0xFFFF) << 16) | (uint)(sram.Read(CvmMemoryProtocol.CombineAddress(0, address + 1)) & 0xFFFF);
            registerNote += $" 0x{wideLiteral:X8} ({wideLiteral})";
          }

          notes[address] = registerNote;
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

}