namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// The CVM Debugger's own default test program: exercises every one of the CVM's 42 opcodes that
/// this project can currently deliver a result for AND verify from the transaction log alone,
/// replacing the earlier 3-instruction smoke test (5 'nop, 'plit/pop/push round trip, one 'call,
/// one 'br) that only ever touched 5 of the CVM's 72 opcodes.
///
/// <b>Coverage: 42 of 72 opcodes, every one with a log-checkable expected value.</b> Node 607's own
/// five primitives (nop, pushlit, pop, push, ret) plus call/br/ifbr/slit; node 507's eleven ALU ops
/// (usl, ssr, usr, add, sub, and, xor, or, inv, inc, dec); node 506's nine register-d/
/// extended-precision ops (zext, addc, ldd, std, xd, mul2d, div2d, sext, umuld); node 407's five
/// register-w/port ops that need no live F18A port on the far side (xpt, ldhi, ldlo, sthi, stlo);
/// and eight of node 606's nine frame-pointer ops (enter, stl, stp, ldl, ldp, lal, lap, leave --
/// see the exclusion note on 'adjust below). Every instruction that WRITES a value is immediately
/// followed by a comment stating the expected hex value the transaction log's own
/// "WRITE ... &lt;- XXXX" line should show, computed and cross-checked with a Python simulation of
/// each node's own F18 source before this program was written -- see this class's own git history /
/// the session notes for the derivation of each one (register-r operand order for the binary ALU
/// ops in particular is the OPPOSITE of the naive first guess: for usl/ssr/usr/sub the value shifted
/// or subtracted is the POPPED external operand, and r supplies the count/subtrahend -- "slit X;
/// push; slit Y; op" computes X-op-Y with X popped from external memory and Y left in r).
/// <b>Confirmed against a real run (2026-08-30), two corrections to that original derivation:</b>
/// usl/ssr/usr actually shift by r+1, not r -- node 507's own binary-op dispatch drives the shift
/// with F18's standard <c>for</c>/<c>next</c> loop, which runs its body (popped-count + 1) times,
/// the classic colorForth/F18 off-by-one every <c>for</c> loop has (0 still loops once); and node
/// 407's <c>ldlo</c> returns only the LOW 16 bits of its own locally-tracked 18-bit port value
/// (masked), not the raw unmasked 18-bit value an earlier draft of this program assumed. The
/// comments below already reflect both corrected values; every other opcode's expected value was
/// confirmed exact on that same run with no correction needed.
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
/// reply that will never come. Node 407's other five ops (xpt, ldhi, ldlo, sthi, stlo) never touch a
/// real port and are exercised normally.</item>
/// <item><b>Node 606's 'adjust is excluded for a different, more serious reason: it was tried, and
/// it broke real hardware.</b> An earlier version of this program included 'adjust (right before the
/// call into the frame-pointer block below), on the assumption -- per Node606.f18's own header,
/// which says adjust's dispatch was "given...with no [confirmed]" cascade, same caveat as la/ld/st
/// -- that at worst its exact effect on p was merely unconfirmed, not unsafe. A real run on
/// 2026-08-30 showed otherwise: immediately after 'adjust's own opcode fetch, the transaction log
/// showed the CVM cluster's own boot handshake (two page-1 reads at address 0, exactly what
/// Ga144CvmHardwareInstaller's automatic test expects right after waking node 708's 'start) followed
/// by page-0 fetching restarting from address 0 -- i.e. 'adjust forced a full, uncommanded cluster
/// reset. The resumed second pass then ran with a corrupted external-stack pointer (WRITE addresses
/// wrapped from page 1 address 0x0000 down through 0x3FFFF instead of continuing the first pass's
/// own downward sequence) and the run eventually timed out. This is NOT the same class of risk as
/// 'br'/'ifbr' below (which just fall into an existing, harmless jump-table branch) -- 'adjust
/// visibly corrupts control flow across the whole cluster, so it stays out of this program entirely
/// until it can be investigated further, the same treatment as 'in'/'out' above.</item>
/// </list>
///
/// <b>Two exploratory instructions, deliberately NOT asserted as "known correct":</b>
/// 'br and 'ifbr (at the very end, after the frame-pointer block returns) are included only as an
/// observation opportunity, not a real branch test: Node607.f18's own dispatch table does not
/// actually implement a signed-offset branch for either tag yet (per this project's own
/// CvmMemoryProtocol.cs remarks) -- today they fall into the same "100?" jump-table branch as
/// ret/xs/xp/tjmp/pc and would be misdecoded (harmlessly, unlike 'adjust above -- nothing past them
/// in this program depends on their outcome, since they are the last two real instructions here).
/// This program places both words purely so Stefan can see, from the log, what node 607's existing
/// dispatch actually does with them on real hardware -- it does not assume or check for a
/// particular outcome.
///
/// The frame-pointer block (word address 0x88, called once from the main flow via 'call) is
/// self-checking for node 606's local-vs-parameter offset-sign convention: Node606.f18's own header
/// says which of stl/stp maps to a negative-vs-positive frame offset is itself "inferred, not
/// given", so rather than asserting an absolute address this program compares stl's/ldl's own
/// address against lal's freshly computed one (and, separately, stp's/ldp's against lap's) -- those
/// two MUST always agree with each other regardless of which sign convention turns out to be
/// correct, so the check is meaningful without this program taking a position on the unconfirmed
/// convention itself.
///
/// This is the single source of truth for that program's text:
/// <see cref="Services.CvmMemoryProtocol.TryBuildDebuggerTestProgram"/> assembles this exact text
/// (via <see cref="Services.CvmAssemblyLanguage.ParseSource"/> then
/// <see cref="Services.CvmAssemblyLanguage.Assemble"/>) to build the word list
/// <see cref="ViewModels.CvmDebuggerViewModel.StartAsync"/> loads by default, and
/// <see cref="ViewModels.CvmDebuggerViewModel.DefaultAssemblyCode"/> is this same text again, so
/// Start and an unedited click of Assemble always produce byte-identical simulated-SRAM contents.
/// 157 words total once assembled.
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
      "slit 100          ; r = 0x0064\n" +
      "push              ; WRITE <- 0064\n" +
      "slit -1           ; r = 0xFFFF (slit's negative range)\n" +
      "push              ; WRITE <- FFFF\n" +
      "slit 2047         ; r = 0x07FF (slit's positive extreme)\n" +
      "push              ; WRITE <- 07FF\n" +
      "slit -2048        ; r = 0xF800 (slit's negative extreme)\n" +
      "push              ; WRITE <- F800\n" +
      "slit 15           ; r = 0x000F\n" +
      "inv               ; r = ~0x000F = 0xFFF0\n" +
      "push              ; WRITE <- FFF0\n" +
      "slit 41           ; r = 0x0029\n" +
      "inc               ; r = 0x002A\n" +
      "push              ; WRITE <- 002A\n" +
      "slit 41           ; r = 0x0029\n" +
      "dec               ; r = 0x0028\n" +
      "push              ; WRITE <- 0028\n" +
      "slit 0x0012       ; X=0x0012 (shiftee)\n" +
      "push              ; WRITE <- 0012\n" +
      "slit 8            ; Y=8 (shift count, becomes r) -- node 507's 'for'/'next' loop runs (r+1) times, the standard F18 for-loop off-by-one, so this actually shifts by 9, not 8\n" +
      "usl               ; pop X(0012) READ <- 0012; r = 0012<<(8+1) = 2400\n" +
      "push              ; WRITE <- 2400\n" +
      "slit -100         ; X=0xFF9C\n" +
      "push              ; WRITE <- FF9C\n" +
      "slit 3            ; Y=3 (count) -- actually shifts by (3+1)=4, same 'for'/'next' off-by-one as usl above\n" +
      "ssr               ; pop X(FF9C) READ <- FF9C; r = signed(FF9C)>>(3+1) = FFF9\n" +
      "push              ; WRITE <- FFF9\n" +
      "slit -100         ; X=0xFF9C\n" +
      "push              ; WRITE <- FF9C\n" +
      "slit 3            ; Y=3 (count) -- actually shifts by (3+1)=4, same 'for'/'next' off-by-one as usl above\n" +
      "usr               ; pop X(FF9C) READ <- FF9C; r = unsigned(FF9C)>>(3+1) = 0FF9\n" +
      "push              ; WRITE <- 0FF9\n" +
      "slit 100          ; X=100\n" +
      "push              ; WRITE <- 0064\n" +
      "slit 200          ; Y=200\n" +
      "add               ; pop X(0064) READ <- 0064; r = 0064+00C8 = 012C\n" +
      "push              ; WRITE <- 012C\n" +
      "slit 500          ; X=500\n" +
      "push              ; WRITE <- 01F4\n" +
      "slit 200          ; Y=200\n" +
      "sub               ; pop X(01F4) READ <- 01F4; r = X-Y = 01F4-00C8 = 012C\n" +
      "push              ; WRITE <- 012C\n" +
      "slit 0x0FF        ; X=0x0FF\n" +
      "push              ; WRITE <- 00FF\n" +
      "slit 0x0F0        ; Y=0x0F0\n" +
      "and               ; pop X(00FF) READ <- 00FF; r = 00FF & 00F0 = 00F0\n" +
      "push              ; WRITE <- 00F0\n" +
      "slit 0x0FF        ; X=0x0FF\n" +
      "push              ; WRITE <- 00FF\n" +
      "slit 0x0F0        ; Y=0x0F0\n" +
      "xor               ; pop X(00FF) READ <- 00FF; r = 00FF ^ 00F0 = 000F\n" +
      "push              ; WRITE <- 000F\n" +
      "slit 0x00F        ; X=0x00F\n" +
      "push              ; WRITE <- 000F\n" +
      "slit 0x0F0        ; Y=0x0F0\n" +
      "or                ; pop X(000F) READ <- 000F; r = 000F | 00F0 = 00FF\n" +
      "push              ; WRITE <- 00FF\n" +
      "slit 0x0AB        ; r=0x00AB (a recognizable, non-zero marker)\n" +
      "std               ; d := 0x00AB\n" +
      "zext              ; d := 0 (zero-extend clears d)\n" +
      "ldd               ; r := d = 0\n" +
      "push              ; WRITE <- 0000 (confirms zext cleared a previously non-zero d)\n" +
      "slit 0x0AB        ; r=0x00AB\n" +
      "std               ; d := 0x00AB (also verifies std leaves r unchanged)\n" +
      "push              ; WRITE <- 00AB (r unchanged by std)\n" +
      "ldd               ; r := d -- round trip, expect 00AB back\n" +
      "push              ; WRITE <- 00AB (confirms std+ldd round-trip)\n" +
      "slit 0x0CD        ; r=0x00CD (d is still 0x00AB from the block above)\n" +
      "xd                ; exchange d(00AB) and r(00CD): r:=00AB, d:=00CD\n" +
      "push              ; WRITE <- 00AB (r took d's old value)\n" +
      "ldd               ; r := d = 00CD\n" +
      "push              ; WRITE <- 00CD (confirms xd truly swapped both registers)\n" +
      "slit -1           ; r=0xFFFF\n" +
      "sext              ; d := 0xFFFF (r's sign bit is 1)\n" +
      "ldd               ; r := d\n" +
      "push              ; WRITE <- FFFF (confirms sext: negative r -> d=FFFF)\n" +
      "slit 5            ; r=0x0005\n" +
      "sext              ; d := 0x0000 (r's sign bit is 0)\n" +
      "ldd               ; r := d\n" +
      "push              ; WRITE <- 0000 (confirms sext: non-negative r -> d=0000)\n" +
      "slit 1            ; r=1\n" +
      "std               ; d := 1\n" +
      "slit -1           ; r=0xFFFF (this will be the popped addend X)\n" +
      "push              ; WRITE <- FFFF\n" +
      "slit 1            ; r=1 (restore r=1 for addc's own r operand)\n" +
      "addc              ; pop X(FFFF) READ <- FFFF; total = d(1)+X(FFFF)+r(1) = 0x10001; r := 0x10001 RAW (506's own r! does not mask to 16 bits -- see note below), d := carry = 1\n" +
      "push              ; WRITE <- 10001 (5 hex digits -- raw/unmasked r; see the note above 'addc)\n" +
      "ldd               ; r := d -- expect the captured carry\n" +
      "push              ; WRITE <- 0001 (confirms addc's carry landed correctly in d)\n" +
      "slit 1            ; r=1\n" +
      "std               ; d := 1\n" +
      "slit -1           ; r=0xFFFF (the value whose (r,d) pair mul2d will shift left)\n" +
      "mul2d             ; shift (r=FFFF,d=1) left 1 bit: new_r = FFFF, new_d(carry) = 1 (old bit15 of r)\n" +
      "push              ; WRITE <- FFFF\n" +
      "ldd               ; r := d\n" +
      "push              ; WRITE <- 0001 (confirms mul2d's carry-out)\n" +
      "slit 1            ; r=1\n" +
      "std               ; d := 1\n" +
      "slit 1            ; r=1 (the (r,d) pair div2d will shift right)\n" +
      "div2d             ; shift (r=1,d=1) right 1 bit: new_r = 8000, new_d(carry) = 1 (old bit0 of r)\n" +
      "push              ; WRITE <- 8000\n" +
      "ldd               ; r := d\n" +
      "push              ; WRITE <- 0001 (confirms div2d's carry-out)\n" +
      "slit 2000         ; X=2000 (0x07D0)\n" +
      "push              ; WRITE <- 07D0\n" +
      "slit 2000         ; r=2000 (0x07D0)\n" +
      "umuld             ; pop X(07D0) READ <- 07D0; product = 2000*2000 = 4,000,000 = 0x3D0900; r := low16 = 0900, d := high = 003D\n" +
      "push              ; WRITE <- 0900\n" +
      "ldd               ; r := d\n" +
      "push              ; WRITE <- 003D (confirms umuld's high half)\n" +
      "slit 0            ; r=0 (so the first xpt's WRITE below shows A's pristine boot value cleanly)\n" +
      "xpt               ; swap A(0x00175, boot 'left' port addr) and r(0): r:=00175, A:=00000\n" +
      "push              ; WRITE <- 00175 (confirms A's boot-time port address)\n" +
      "xpt               ; swap back: r:=00000 (old A), A:=00175 (restored)\n" +
      "push              ; WRITE <- 00000\n" +
      "slit 2            ; r=2 (0b10) -- the 2-bit 'hi' value to install\n" +
      "sthi              ; 407's local stack holds the boot seed 0; builds (0 & 0xFFFF) xor (r<<16) = 0x20000, left on 407's OWN stack (not r)\n" +
      "ldlo              ; loads the LOW 16 bits of that 407-local 18-bit value into r: r := 0x20000 & 0xFFFF = 0x0000 -- confirmed on real hardware 2026-08-30 (an earlier draft of this comment wrongly claimed ldlo returns the raw, unmasked 18-bit value; it does not)\n" +
      "push              ; WRITE <- 0000 (confirms sthi placed the 2 bits above bit 15, out of ldlo's own low-16 reach)\n" +
      "slit 0x234        ; r=0x0234 -- the 16-bit 'lo' value to install\n" +
      "stlo              ; combines (0x20000 & 0x30000) xor r(0234) = 0x20234, left on 407's OWN stack\n" +
      "ldlo              ; loads the LOW 16 bits again: r := 0x20234 & 0xFFFF = 0x0234\n" +
      "push              ; WRITE <- 0234 (confirms stlo merged the new low 16 bits, keeping the earlier hi 2 bits -- ldhi below reads those back out)\n" +
      "ldhi              ; extracts the hi 2 bits back out of 0x20234: r := (0x20234>>16)&3 = 2\n" +
      "push              ; WRITE <- 0002 (confirms ldhi's own extraction)\n" +
      "call 136          ; jump to the frame-pointer subroutine below -- FRAME_TEST's own word address, computed by counting the words above (pushlit is the only 2-word instruction here); 136 (0x88)\n" +
      "nop               ; execution resumes here once FRAME_TEST's own 'ret' returns\n" +
      "nop\n" +
      "nop\n" +
      "enter 4           ; reserve a 4-word frame; WRITE (old f, expected 0000) at the address 'leave' will read back; new f := that write address\n" +
      "slit 0x0AB        ; r=0x00AB\n" +
      "stl 1             ; WRITE r(00AB) to local #1 (frame-relative offset 1)\n" +
      "slit 0x0CD        ; r=0x00CD\n" +
      "stp 2             ; WRITE r(00CD) to parameter #2 (frame-relative offset 2)\n" +
      "ldl 1             ; READ local #1 back into r -- expect the SAME address and value 00AB as the stl above\n" +
      "push              ; WRITE <- 00AB (round-trips ldl's own fetch)\n" +
      "ldp 2             ; READ parameter #2 back into r -- expect the SAME address and value 00CD as the stp above\n" +
      "push              ; WRITE <- 00CD (round-trips ldp's own fetch)\n" +
      "lal 1             ; r := local #1's own address -- NO memory access; compare this pushed address against the stl/ldl transactions' own address above\n" +
      "push              ; WRITE <- local #1's address, i.e. exactly the stl/ldl address above\n" +
      "lap 2             ; r := parameter #2's own address -- NO memory access; compare against the stp/ldp transactions' own address above\n" +
      "push              ; WRITE <- parameter #2's address, i.e. exactly the stp/ldp address above\n" +
      "leave             ; READ back the frame's saved old f -- expect value 0000, restoring f to its pre-enter value\n" +
      "ret               ; pop the return address 'call FRAME_TEST' pushed and resume the main flow\n" +
      "nop               ; buffer before the exploratory block\n" +
      "nop\n" +
      "br 1              ; EXPLORATORY -- Node607.f18 has no real branch decode for this tag yet; observe, don't assume, what the log shows\n" +
      "nop\n" +
      "ifbr 1            ; EXPLORATORY -- same caveat as br above\n" +
      "nop\n" +
      "";
}