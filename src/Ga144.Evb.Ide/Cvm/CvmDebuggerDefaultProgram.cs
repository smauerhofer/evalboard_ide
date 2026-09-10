namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// The CVM Debugger's own default test program: exercises every one of the CVM's 27 opcodes that
/// this project can currently deliver a result for AND verify from the transaction log alone,
/// replacing the earlier 3-instruction smoke test (5 'nop, 'plit/pop/push round trip, one 'call,
/// one 'br) that only ever touched 5 of the CVM's 73 opcodes.
///
/// <b>Coverage: 27 of 73 opcodes, every one with a log-checkable expected value.</b> (Was 41 of 73
/// before this same day's later CVM1-opcode cleanup removed fourteen ops' worth of test coverage --
/// see the cleanup note below; the "73" denominator itself will shrink separately once the now-dead
/// mnemonics are removed from <see cref="Cvm.Toolchain.CvmInstructionSet"/>.) Node 607's own
/// five primitives (nop, pushlit, pop, push, ret) plus call/br/cbr/lit (node 509's own literal-load
/// form, which absorbed the retired slit's role 2026-09-09); node 507's eleven ALU ops
/// (usl, ssr, usr, add, sub, and, xor, or, inv, inc, dec); and seven of node 606's ten mnemonics
/// (enter, stl, stp, ldl, ldp, leave, and now halt itself -- the retired lal/lap no longer among
/// them, 2026-09-09, see the note below and the exclusion note on 'adjust below). Every instruction
/// that WRITES a value is immediately
/// followed by a comment stating the expected hex value the transaction log's own
/// "WRITE ... &lt;- XXXX" line should show, computed and cross-checked with a Python simulation of
/// each node's own F18 source before this program was written -- see this class's own git history /
/// the session notes for the derivation of each one (register-r operand order for the binary ALU
/// ops in particular is the OPPOSITE of the naive first guess: for usl/ssr/usr/sub the value shifted
/// or subtracted is the POPPED external operand, and r supplies the count/subtrahend -- "lit X;
/// push; lit Y; op" computes X-op-Y with X popped from external memory and Y left in r).
/// <b>Confirmed against a real run (2026-08-30), two corrections to that original derivation:</b>
/// usl/ssr/usr actually shift by r+1, not r -- node 507's own binary-op dispatch drives the shift
/// with F18's standard <c>for</c>/<c>next</c> loop, which runs its body (popped-count + 1) times,
/// the classic colorForth/F18 off-by-one every <c>for</c> loop has (0 still loops once); and node
/// 407's <c>ldlo</c> returns only the LOW 16 bits of its own locally-tracked 18-bit port value
/// (masked), not the raw unmasked 18-bit value an earlier draft of this program assumed. The
/// comments below already reflect both corrected values; every other opcode's expected value was
/// confirmed exact on that same run with no correction needed. On a SECOND real run (also
/// 2026-08-30), the call/ret round trip into the frame-pointer block, and every one of its own
/// enter/stl/stp/ldl/ldp/lal/lap/leave transactions, were confirmed exact -- including the
/// self-check that lal's/lap's freshly computed addresses agree with stl's/ldl's and stp's/ldp's
/// own addresses, which they did. On a THIRD real run (2026-09-01, the first to include node 606's
/// new <c>halt</c>), a further correction: <c>addc</c>'s result register r does NOT keep the raw,
/// unmasked 18-bit sum an earlier draft of this program assumed -- 'push wrote 0x0001, not
/// 0x10001, for the same X(FFFF)+r(1)+carry(1) sum whose carry bit landed correctly in d (read back
/// via 'ldd immediately afterward). r is masked to 16 bits like every other register this program
/// exercises; only <c>d</c> (via the separate <c>ldd</c> round trip) carries the overflow. Every
/// other opcode's value on this run matched exactly, including 'br 1 and 'halt itself -- see the
/// Layout note below for what changed there. (The register-d block that produced these addc/mul2d/
/// div2d/umuld/zext/ldd/std/xd/sext values, and the register-w/port block above it, have since been
/// removed from this program entirely -- see the CVM1-opcode cleanup note below. This paragraph is
/// kept as the historical record of what was confirmed on real hardware before that removal.)
///
/// <b>Three deliberate exclusions, each documented at the point it would otherwise appear:</b>
/// <list type="bullet">
/// <item>Node 508's 27 comparison/arithmetic opcodes (eq, eq0, false, true, ne, ne0, ugt, gt, gt0,
/// ge, ge0, ule, le, le0, lt, lt0, ult, uge, mul2, udiv2, div2, abs, negate, xt, ldt, stt, bitcnt)
/// are skipped entirely, by Stefan's own choice. Node508.f18's own header comments say the
/// mechanism these ops use to deliver a result back into node 507's register r is "inferred, not
/// given" -- unlike node 506, none of the 27 op bodies call `r!` or `main` to deliver a result back
/// to 507, so there is currently no confirmed way for a subsequent 'push to show one of these ops'
/// actual result on the wire. Rather than include them with an unverified expected value, Stefan
/// chose to skip them for now.</item>
/// <item>Node 407's 'in and 'out (real, blocking F18A port reads/writes through register A) are
/// never executed, by Stefan's own choice -- node 408, the node they would actually talk to, is not
/// part of this booted test cluster, so calling either risks hanging the whole session waiting for a
/// reply that will never come. Node 407's other five ops (xpt, ldhi, ldlo, sthi, stlo) used to be
/// exercised normally here too, but per the 2026-09-09 CVM1-opcode cleanup (see below) all five have
/// since been deleted outright -- no live CVM2 node backs them -- so they are gone from both this
/// program and (once that cleanup's opcode-table pass lands) <see cref="Cvm.Toolchain.CvmInstructionSet"/>
/// itself.</item>
/// <item><b>Node 606's 'adjust is excluded for a different, more serious reason: it was tried, and
/// it broke real hardware.</b> An earlier version of this program included 'adjust (right before the
/// call into the frame-pointer block), on the assumption -- per Node606.f18's own header, which says
/// adjust's dispatch was "given...with no [confirmed]" cascade, same caveat as la/ld/st -- that at
/// worst its exact effect on p was merely unconfirmed, not unsafe. A real run on 2026-08-30 showed
/// otherwise: immediately after 'adjust's own opcode fetch, the transaction log showed the CVM
/// cluster's own boot handshake (two page-1 reads at address 0, exactly what
/// Ga144CvmHardwareInstaller's automatic test expects right after waking node 708's 'start) followed
/// by page-0 fetching restarting from address 0 -- i.e. 'adjust forced a full, uncommanded cluster
/// reset. This is NOT the same class of risk as 'br'/'cbr' below (which just fall into an existing,
/// harmless jump-table branch) -- 'adjust visibly corrupts control flow across the whole cluster, so
/// it stays out of this program entirely until it can be investigated further, the same treatment as
/// 'in'/'out' above.
///
/// <b>RESOLVED, 2026-09-10 -- the "further investigation" never found a live equivalent, and Stefan has
/// since said the opcode itself is gone:</b> "'adjust' no longer exists. you can remove it." Its own
/// mnemonic/tag constants and <see cref="Cvm.Toolchain.CvmInstructionSet.Instructions"/> entry (formerly
/// Id 21) are now deleted outright -- see <see cref="Cvm.Toolchain.CvmInstructionSet"/>'s own remarks.
/// This paragraph is kept as the historical record of why it was excluded from this test program in the
/// first place, and of the real hardware behavior observed before it was removed.</item>
/// </list>
///
/// <b>CVM1-opcode cleanup (2026-09-09): node 506's nine register-d/extended-precision ops (zext,
/// addc, ldd, std, xd, mul2d, div2d, sext, umuld) and node 407's five register-w/port ops (xpt,
/// ldhi, ldlo, sthi, stlo) no longer appear in this program.</b> Per Stefan's own decision to retire
/// every CVM1-vintage mnemonic no live CVM2 node still backs, eight of those nine register-d ops
/// (zext, ldd, std, xd, mul2d, div2d, sext, umuld) are being deleted from
/// <see cref="Cvm.Toolchain.CvmInstructionSet"/> outright and will no longer assemble at all, so
/// their test lines (formerly right after the ALU-ops block above) were removed here the same way
/// lal/lap were below -- a deleted mnemonic cannot be left in a hand-written test program, unlike a
/// merely repointed one. <c>addc</c> itself was NOT deleted -- it was REPOINTED to node 510's own
/// <c>'addc</c> implementation, a physically different node than the one this program's removed test
/// block was confirmed against (2026-09-01, see above) -- so that old test's expected values (in
/// particular the carry-in-<c>d</c> behavior called out above) describe node 506's retired addc, not
/// node 510's current one. Per this project's own "don't fabricate an unverified expected value"
/// discipline (the same one already applied to node 508's 27 skipped ops), <c>addc</c>'s test is
/// omitted here too rather than guessed at, until a fresh hardware run against node 510 can supply a
/// trustworthy one. Node 407's five register-w/port ops were deleted outright the same way as the
/// eight register-d ops above (no live CVM2 node backs them either), so their test lines are gone for
/// the same reason. Removing these fourteen test lines shortened this program from 152 to 84 words,
/// which moved the frame-pointer subroutine's own word address -- the <c>call</c> instruction below
/// was updated to match; see the word-count note at the end of this summary.
///
/// <b>Two exploratory instructions, deliberately NOT asserted as "known correct":</b>
/// 'br and 'cbr are included only as an observation opportunity, not a real branch test:
/// Node607.f18's own dispatch table does not actually implement a signed-offset branch for either
/// tag yet (per this project's own CvmMemoryProtocol.cs remarks) -- today they fall into the same
/// "100?" jump-table branch as ret/xs/xp/tjmp/pc and would be misdecoded (harmlessly, unlike
/// 'adjust above). This program places both words purely so Stefan can see, from the log, what node
/// 607's existing dispatch actually does with them on real hardware -- it does not assume or check
/// for a particular outcome.
///
/// <b>Layout note, learned the hard way on a real run (2026-08-30): the frame-pointer subroutine is
/// placed AFTER 'br'/'cbr, not right after its own 'call site.</b> An earlier draft placed it
/// immediately after "call FRAME_TEST" (with only padding 'nop's in between), which seemed harmless
/// since a 'call jumps straight over that padding on the way in -- but node 607 has no unconditional
/// jump other than 'call'/'ret', so when 'ret' returned control to the padding nops, execution then
/// fell straight through, sequentially, into the SAME subroutine body a second time, un-called. That
/// second, un-called pass ran the frame-pointer block again, and its own 'ret' this time popped
/// whatever stale value happened to be sitting at that stack slot from an earlier iteration (in the
/// observed run, the literal 0x00CD test marker this program itself writes as parameter #2 in the
/// frame test) and jumped there as if it were a return address -- landing in unused, zero-filled
/// memory, which decodes as "call 0" (an all-zero word has no tag bit set, so it self-describes as a
/// call to address 0) and restarted the whole program from address 0, well before ever reaching
/// 'br'/'cbr'. Moving the subroutine to after 'br'/'cbr fixed the ORDERING problem -- both
/// exploratory instructions were now guaranteed to run before any possible fall-through -- but node
/// 607 itself still had no way to stop cleanly, so the program still ran off its own end into
/// zero-filled "call 0" memory and restarted once, at the very end, harmlessly (true of the original
/// 3-instruction default program too). <b>Stefan has since added a real 'halt opcode to node 606</b>
/// (a single blocking '@b' that never returns -- see <see cref="Node606Program"/>'s own remarks), and
/// this program now uses it: a 'halt sits right after 'cbr's padding 'nop, so the main flow stops
/// dead there instead of falling through into FRAME_TEST's body a second time. Only a chip reset can
/// move execution past it now.
///
/// The frame-pointer block (word address 0x8D, called once from the main flow via 'call) USED TO BE
/// self-checking for node 606's local-vs-parameter offset-sign convention: Node606.f18's own header
/// said which of stl/stp maps to a negative-vs-positive frame offset was itself "inferred, not
/// given", so rather than asserting an absolute address this program compared stl's/ldl's own
/// address against lal's freshly computed one (and, separately, stp's/ldp's against lap's) -- those
/// two MUST always agree with each other regardless of which sign convention turns out to be
/// correct, so the check was meaningful without this program taking a position on the unconfirmed
/// convention itself. Confirmed exact on the 2026-08-30 run: both pairs agreed. See the note below
/// on why this self-check no longer appears in the current source.
///
/// <b>Flagged, not fixed (2026-09-06): <c>ldl</c>/<c>ldp</c>/<c>stl</c>/<c>stp</c>'s own CVM opcode
/// encoding has since moved out from under this program's own hardware-confirmed run.</b> Everything
/// above (the 0x00AB/0x00CD expected DATA values, the stl/ldl-vs-lal and stp/ldp-vs-lap self-check) was
/// confirmed against CVM1's node 606 -- a physically different, now-deleted node -- using the OLD
/// 8-bit-tag/8-bit-value opcode words CVM1 assigned these four mnemonics. CVM2's own node 506 has since
/// been given a real <c>ldl</c>/<c>ldp</c>/<c>stl</c>/<c>stp</c> implementation of its own (see
/// <see cref="Node506Program"/>'s own remarks), and per "only update existing opcodes where possible"
/// these four mnemonics were REPOINTED to node 506's own, DIFFERENT 7-bit-tag/9-bit-value opcode words
/// (<see cref="CvmInstructionSet.Node506StoreLocalTag"/> and its three siblings) -- so assembling THIS
/// program's own <c>stl 1</c>/<c>ldl 1</c>/etc. lines today produces different raw opcode words than the
/// ones this program's own comments were confirmed against. That is arguably a fix, not a regression --
/// under the OLD CVM1-derived tags (0xA800-0xAFFF) these words would not even route to node 506 at all
/// under CVM2's own node-507 dispatch (that range belongs to node 508's globals instead), so a real
/// CVM2-mesh run of this exact program text would previously have sent stl/ldl/stp/ldp to the WRONG
/// node entirely. But node 506's own real dispatch differs from CVM1's node 606 in other ways too (a
/// 9-bit offset instead of 8-bit, and its own not-yet-independently-confirmed local-vs-parameter sign
/// convention -- see <see cref="Node506Program"/>'s own remarks), so the specific 0x00AB/0x00CD values
/// asserted above are NOT a reliable prediction of what a real CVM2 run through node 506 will produce --
/// only a fresh hardware run against the current CVM2 mesh could confirm that. Per Stefan's own standing
/// instruction (see <see cref="Ga144CvmHardwareInstaller.StartDebugSessionAsync"/>'s own remarks, "just
/// keep it for now"), this file's own text is left untouched here too; this paragraph only records why
/// its numbers can no longer be trusted at face value for CVM2.
///
/// <b>lal/lap retired 2026-09-09 ("'lal' &amp; 'lap' are removed. use ''f' or 'fpush' from node 506 and
/// add the offset to calculate the address of a local or parameter.").</b> The self-check described
/// above (comparing lal's/lap's freshly computed addresses against stl's/ldl's and stp's/ldp's own) can
/// no longer be reproduced as written -- lal/lap no longer assemble at all, unlike ldl/ldp/stl/stp above
/// which merely moved to a different node/tag. Per this project's own established convention, a
/// genuinely DELETED mnemonic (unlike a merely repointed one) cannot be left in a hand-written test
/// program at all, so this program's own two lal/lap lines (and their paired 'push transactions) have
/// been removed entirely rather than kept as now-unassemblable text. Computing a local's or parameter's
/// own address today instead uses <c>'fpush</c> (push node 506's own frame pointer <c>f</c> directly onto
/// the data stack) plus explicit offset arithmetic (see
/// <see cref="C.Toolchain.CCodeGenerator"/>'s own <c>EmitLocalOrParameterAddress</c> helper) -- no fresh
/// hardware run has exercised that sequence yet, so rather than fabricate an unverified expected value
/// for it here (the same discipline already applied to node 508's 27 skipped ops above), this program
/// simply omits that self-check until a real run can supply trustworthy numbers.
///
/// This is the single source of truth for that program's text:
/// <see cref="Services.CvmMemoryProtocol.TryBuildDebuggerTestProgram"/> assembles this exact text
/// (via <see cref="Services.CvmAssemblyLanguage.ParseSource"/> then
/// <see cref="Services.CvmAssemblyLanguage.Assemble"/>) to build the word list
/// <see cref="ViewModels.CvmDebuggerViewModel.StartAsync"/> loads by default, and
/// <see cref="ViewModels.CvmDebuggerViewModel.DefaultAssemblyCode"/> is this same text again, so
/// Start and an unedited click of Assemble always produce byte-identical simulated-SRAM contents.
/// 84 words total once assembled (152 before this same day's later removal of the register-d and
/// register-w/port test blocks during the CVM1-opcode cleanup above; 156 before the earlier
/// 2026-09-09 removal of the now-unassemblable lal/lap self-check lines).
/// </summary>
public static class CvmDebuggerDefaultProgram
{
  public const string Source =
      "nop               ; cold-start fetch; no side effect\n" +
      "nop\n" +
      "nop\n" +
      "pushlit 0x0100    ; push literal 0x0100 onto S (native stack, NOT external memory)\n" +
      "pop               ; treats 0x0100 as the external stack pointer p; READs page1:0100 (fresh SRAM=0) into r\n" +
      "push              ; writes r (0) back out; WRITE page1:0100 <- 0000\n" +
      "lit 100           ; r = 0x0064\n" +
      "push              ; WRITE <- 0064\n" +
      "lit -1            ; r = 0xFFFF (lit's negative range)\n" +
      "push              ; WRITE <- FFFF\n" +
      "lit 511           ; r = 0x01FF (lit's positive extreme -- narrower than the retired slit's old 12-bit range)\n" +
      "push              ; WRITE <- 01FF\n" +
      "lit -512          ; r = 0xFE00 (lit's negative extreme -- narrower than the retired slit's old 12-bit range)\n" +
      "push              ; WRITE <- FE00\n" +
      "lit 15            ; r = 0x000F\n" +
      "inv               ; r = ~0x000F = 0xFFF0\n" +
      "push              ; WRITE <- FFF0\n" +
      "lit 41            ; r = 0x0029\n" +
      "inc               ; r = 0x002A\n" +
      "push              ; WRITE <- 002A\n" +
      "lit 41            ; r = 0x0029\n" +
      "dec               ; r = 0x0028\n" +
      "push              ; WRITE <- 0028\n" +
      "lit 0x0012        ; X=0x0012 (shiftee)\n" +
      "push              ; WRITE <- 0012\n" +
      "lit 8             ; Y=8 (shift count, becomes r) -- node 507's 'for'/'next' loop runs (r+1) times, the standard F18 for-loop off-by-one, so this actually shifts by 9, not 8\n" +
      "usl               ; pop X(0012) READ <- 0012; r = 0012<<(8+1) = 2400\n" +
      "push              ; WRITE <- 2400\n" +
      "lit -100          ; X=0xFF9C\n" +
      "push              ; WRITE <- FF9C\n" +
      "lit 3             ; Y=3 (count) -- actually shifts by (3+1)=4, same 'for'/'next' off-by-one as usl above\n" +
      "ssr               ; pop X(FF9C) READ <- FF9C; r = signed(FF9C)>>(3+1) = FFF9\n" +
      "push              ; WRITE <- FFF9\n" +
      "lit -100          ; X=0xFF9C\n" +
      "push              ; WRITE <- FF9C\n" +
      "lit 3             ; Y=3 (count) -- actually shifts by (3+1)=4, same 'for'/'next' off-by-one as usl above\n" +
      "usr               ; pop X(FF9C) READ <- FF9C; r = unsigned(FF9C)>>(3+1) = 0FF9\n" +
      "push              ; WRITE <- 0FF9\n" +
      "lit 100           ; X=100\n" +
      "push              ; WRITE <- 0064\n" +
      "lit 200           ; Y=200\n" +
      "add               ; pop X(0064) READ <- 0064; r = 0064+00C8 = 012C\n" +
      "push              ; WRITE <- 012C\n" +
      "lit 500           ; X=500\n" +
      "push              ; WRITE <- 01F4\n" +
      "lit 200           ; Y=200\n" +
      "sub               ; pop X(01F4) READ <- 01F4; r = X-Y = 01F4-00C8 = 012C\n" +
      "push              ; WRITE <- 012C\n" +
      "lit 0x0FF         ; X=0x0FF\n" +
      "push              ; WRITE <- 00FF\n" +
      "lit 0x0F0         ; Y=0x0F0\n" +
      "and               ; pop X(00FF) READ <- 00FF; r = 00FF & 00F0 = 00F0\n" +
      "push              ; WRITE <- 00F0\n" +
      "lit 0x0FF         ; X=0x0FF\n" +
      "push              ; WRITE <- 00FF\n" +
      "lit 0x0F0         ; Y=0x0F0\n" +
      "xor               ; pop X(00FF) READ <- 00FF; r = 00FF ^ 00F0 = 000F\n" +
      "push              ; WRITE <- 000F\n" +
      "lit 0x00F         ; X=0x00F\n" +
      "push              ; WRITE <- 000F\n" +
      "lit 0x0F0         ; Y=0x0F0\n" +
      "or                ; pop X(000F) READ <- 000F; r = 000F | 00F0 = 00FF\n" +
      "push              ; WRITE <- 00FF\n" +
      "; node 506's register-d block (zext/addc/ldd/std/xd/mul2d/div2d/sext/umuld) and node 407's\n" +
      "; register-w/port block (xpt/sthi/ldlo/stlo/ldhi) were REMOVED here 2026-09-09 as part of the\n" +
      "; CVM1-opcode cleanup -- eight of the nine register-d mnemonics and all five register-w/port\n" +
      "; mnemonics no longer assemble at all (no live CVM2 node backs them), and addc's own test\n" +
      "; (which validated node 506's now-retired addc, not addc's current home on node 510) is omitted\n" +
      "; rather than replaced with a fabricated, unverified expected value -- see this class's own\n" +
      "; remarks for the full explanation. Removing these lines shifted the frame-pointer subroutine's\n" +
      "; word address from 141 (0x8D) to 73 (0x49); the 'call below was updated to match.\n" +
      "call 73           ; jump to the frame-pointer subroutine near the end of this program -- its own word address, computed by counting words (pushlit is the only 2-word instruction here); 73 (0x49)\n" +
      "nop               ; execution resumes here once FRAME_TEST's own 'ret' returns\n" +
      "nop\n" +
      "nop\n" +
      "br 1              ; EXPLORATORY -- Node607.f18 has no real branch decode for this tag yet; observe, don't assume, what the log shows\n" +
      "nop\n" +
      "cbr 1            ; EXPLORATORY -- same caveat as br above. This is the last instruction the main flow was ever designed to reach\n" +
      "nop\n" +
      "halt              ; stop the CPU cleanly here -- everything the main flow cares about has now been observed. Node 606's own 'halt (Stefan's addition: \"wait for a word that will never come\") parks execution in a blocking @b that never returns, so unlike every earlier revision of this program, control never falls through into FRAME_TEST's body a second time -- only a chip reset can move it now\n" +
      "enter 4           ; reserve a 4-word frame; WRITE (old f, expected 0000) at the address 'leave' will read back; new f := that write address\n" +
      "lit 0x0AB         ; r=0x00AB\n" +
      "stl 1             ; WRITE r(00AB) to local #1 (frame-relative offset 1)\n" +
      "lit 0x0CD         ; r=0x00CD\n" +
      "stp 2             ; WRITE r(00CD) to parameter #2 (frame-relative offset 2)\n" +
      "ldl 1             ; READ local #1 back into r -- expect the SAME address and value 00AB as the stl above\n" +
      "push              ; WRITE <- 00AB (round-trips ldl's own fetch)\n" +
      "ldp 2             ; READ parameter #2 back into r -- expect the SAME address and value 00CD as the stp above\n" +
      "push              ; WRITE <- 00CD (round-trips ldp's own fetch)\n" +
      "; lal/lap RETIRED 2026-09-09 -- no longer assemble at all. The self-check that used to live here\n" +
      "; (comparing lal's/lap's freshly computed addresses against the stl/ldl and stp/ldp transactions\n" +
      "; above) is omitted rather than replaced with an unverified 'fpush-based guess -- see this class's\n" +
      "; own remarks for why.\n" +
      "leave             ; READ back the frame's saved old f -- expect value 0000, restoring f to its pre-enter value\n" +
      "ret               ; pop the return address 'call FRAME_TEST' pushed and resume the main flow. This is the intended, correct 'call'/'ret' round trip -- it lands back on the padding nop right after 'call' above, which then runs br/cbr and reaches 'halt, stopping the CPU cleanly instead of falling through into THIS SAME block a second time the way every earlier revision of this program did (see the header remarks)\n" +
      "";
}