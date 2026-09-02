namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 507's resident F18 source -- CVM2's entire CPU. Originally supplied by Stefan on
/// 2026-09-01, correcting an earlier mix-up in this project: the exact same source had been placed
/// on <see cref="Node508Program"/> under the mistaken belief that 508 was the CPU node. It is not --
/// 507 is. <see cref="Node508Program"/> is now a placeholder; see its own remarks.
///
/// <b>2026-09-02 fix, confirmed working on real hardware.</b> Adding
/// <see cref="Compiler.F18Compiler"/>'s new compile-time-stack-must-be-empty check (per Stefan:
/// "please add a check that after compilation of a node the compiler stack must be empty") caught a
/// real bug in <c>m/main</c>'s dispatch loop: the original <c>m/main</c> body read <c># m/main >r</c>
/// -- pushing <c>m/main</c>'s own address onto the COMPILE-TIME stack via <c>#</c>, but never
/// consuming it with <c>lit</c>/<c>literal</c> to actually compile it as an object-code literal, so
/// the runtime <c>>r</c> right after it operated on whatever was already on the CVM's real return
/// stack instead -- the address was silently dropped, never reaching the compiled program at all.
/// Stefan supplied the fix directly: <c># m/main lit >r</c>. Stefan confirmed on real hardware that
/// <c>'nop</c> now works with this fix in place -- the first real-silicon confirmation any part of
/// this source has had; every note below marked "never confirmed with Stefan"/"never against real
/// hardware" predates this and should be read as still open beyond that one confirmed point.
///
/// <b>2026-09-02 fix #2, confirmed working on real hardware (per Stefan: "call and ret are working
/// now. 'ahead' was wrong, 'begin' was right").</b> <c>m/call</c>'s body -- deliberately left
/// unterminated so it falls straight through into <c>m/main</c> right after it, the same
/// cross-definition fall-through idiom documented throughout this file -- opened with <c>ahead</c>
/// (an unconditional forward branch that wants a matching <c>then</c>), but the compile-time control
/// word it was actually meant to pair with is <c>m/main</c>'s own <c>-until</c> a few words later, on
/// the OTHER side of the <c>m/call</c>/<c>m/main</c> boundary -- exactly the kind of cross-definition
/// span this source already relies on elsewhere, since F18 colon-definitions are just address labels,
/// not scopes, so a still-open compile-time control marker carries straight through one falling into
/// the next. <c>ahead</c>/<c>then</c> is the wrong pairing for that: <c>begin</c>/<c>-until</c> is.
/// <b>Per <see cref="Compiler.F18Compiler"/>'s own "Unified control stack" remarks, this project's
/// compiler keeps NO separate control-flow stack at all -- <c>ahead</c>/<c>if</c>/<c>begin</c>/<c>for</c>
/// push and <c>then</c>/<c>until</c>/<c>again</c>/<c>next</c> pop the SAME
/// <c>Interpreter.DataStack</c> the compile-time-stack-empty check above (F18I011) already watches,
/// exactly like real F18 hardware (DB013 5.3.x) does -- a forward opener like <c>ahead</c> pushes an
/// encoded PATCH HANDLE for a later <c>then</c> to resolve; a backward opener like <c>begin</c> pushes
/// a raw DESTINATION address for a later <c>until</c>/<c>-until</c>/<c>again</c> to branch to; both are
/// "just integers" to the stack.</b> That is exactly why this particular mismatch slipped past F18I011:
/// <c>ahead</c> pushed one value (its own patch handle) and <c>-until</c> popped one value right back
/// off, so the stack was empty and balanced by the time compilation finished -- <c>-until</c> just
/// happened to consume <c>ahead</c>'s patch handle as if it were a plain backward-branch address
/// (silently wrong, not out of range), while <c>ahead</c>'s own forward branch was left completely
/// unpatched since the <c>then</c> that should have resolved it never ran. A COUNT-based check like
/// F18I011 cannot see a KIND mismatch like this one; only real hardware surfaced it, as wrong
/// <c>call</c>/<c>ret</c> behavior. Stefan's fix: <c>ahead</c> -&gt; <c>begin</c> in <c>m/call</c>.
/// Alongside it, <c>m/main</c>'s <c>dup 2* 2*</c> picked up a trailing <c>..</c> (the same word as
/// <c>align</c> -- see <see cref="Compiler.F18Compiler"/>'s own handling of both) right before
/// <c>-until</c>, forcing that loop's branch target onto a word boundary. Both changes confirmed
/// together on real hardware: <c>call</c> and <c>ret</c> now work.
/// Re-verified standalone: 60/64 words used (was 61 before this fix), entry point <c>m/main</c> now at
/// 0x01B (was 0x01C) -- both shifts are solely from <c>begin</c>/<c>..</c> compiling different word
/// counts than <c>ahead</c> did, not from any other change.
///
/// <b>Also changed in the 2026-09-02 revision (functionally equivalent, not new bugs).</b>
/// <c>m/next</c>'s body was refactored from <c>a dup 1 . + a! dup dup xor m/@ ;</c> (computing 0 via
/// <c>dup dup xor</c>, i.e. any value XORed with itself) to just <c>a dup 1 . + a!</c>, deliberately
/// left unterminated so it falls straight through into the newly-relocated <c>m/0@ ( rsa-rsw) 0 m/@
/// ;</c> immediately after it -- the same "control-flow word as inline padding/fall-through" idiom
/// already documented on <see cref="Node607Program"/>/<see cref="Node708Program"/>'s own remarks,
/// just applied here for the first time. <c>0</c> replaces <c>dup dup xor</c> as a plainer way to
/// push the same value, and <c>m/0@</c> moved to sit directly after <c>m/next</c> so the fall-through
/// lands on it. Six more tick-labeled opcodes ('ret/'tjmp/'jump/'pop/'xs/'xp/'halt/'nop -- all but
/// 'push, which already had one) picked up their own <c>.loc</c> marker, purely a compiler
/// address/symbol diagnostic with no effect on the compiled program. Re-verified standalone: same
/// ten tick-labeled symbols all still resolve, entry point shifted by one word (<c>m/main</c> now at
/// 0x01C, was 0x01D) purely because of the one extra <c>lit</c> instruction's own word.
///
/// <b>Registers.</b> Per the source's own header comment: T is the CVM's stack pointer s, A is the
/// CVM program counter p, S is the CVM's other register r. <c># 1 org</c> starts compiling at word
/// address 1 (not 0); <c># 0 /a</c> initializes A (p) to 0; <c># up /b</c> points this node's B
/// register at its "up" port -- the SRAM-facing port used by <c>m/@</c>/<c>m/!</c> below; <c>[ 0
/// 0xffff 2 ] /stack</c> preloads the native F18 data stack with r=0, s=0xffff before <c>m/main</c>
/// ever runs.
///
/// <b>Memory access.</b> <c>m/@</c> (<c>!b !b @b</c> -- two plain writes then one read) and
/// <c>m/!</c> (<c>inv !b inv !b !b</c> -- two INVERTED writes then one plain write, zero reads)
/// are, respectively, the CVM's own external-memory read and write primitives -- confirmed a
/// byte-for-byte structural match for this project's AN003 (SRAM Control Cluster) reference
/// convention and for <see cref="Services.CvmMemoryProtocol"/>'s own existing wire-level shape, so
/// that class needed no protocol changes for CVM2, only updating which node/tag answers it (now
/// this node, 507, not 508).
///
/// <b>m/main's dispatch cascade -- DERIVED, NOT YET CONFIRMED WITH STEFAN BEYOND 'nop.</b> The
/// source's own header comment marks its own top-bit dispatch pattern as still undetermined
/// ("<c>????_????_????_????</c>"). Reading the cascade itself: "11??" tests true -&gt; hand off to
/// the DOWN port (<c>-d--</c>) -- per the inline comment this reaches a node not yet loaded (read as
/// a future ALU/offload node, possibly node 508 -- see <see cref="Node508Program"/>'s own remarks --
/// but this is a guess, not confirmed); "101?" -&gt; LEFT port (<c>--l-</c>); "100?" (unconditional
/// at that point) -&gt; RIGHT port (<c>r---</c>) -- this is node 607, CVM2's on-chip SRAM-request
/// router; within the remaining "1000_????" quarter, "1000_1???" is explicitly commented "local
/// execute" (<c>drop &gt;r ;</c> -- jump directly to an address in this node's own RAM), and
/// "1000_0???" falls through to "branch relative" instead. "1000_1???_????_????" as a top-5-bit
/// pattern is 0x8800, i.e. opcode = 0x8800 | wordAddress -- corroborated by <c>'halt</c>'s own body
/// literally writing the constant 0x8800 to port b. Confirmed on real hardware only for the path
/// 'nop actually exercises; the rest of the cascade (down/left/right dispatch, local execute,
/// branch-relative) is still only a standalone-compile-checked hypothesis, not yet exercised or
/// confirmed on silicon.
///
/// <b>Ten tick-labeled opcodes.</b> <c>'plit</c>, <c>'push</c>, <c>'ret</c>, <c>'tjmp</c>,
/// <c>'jump</c>, <c>'pop</c>, <c>'xs</c>, <c>'xp</c>, <c>'halt</c>, <c>'nop</c>. Six match existing
/// CVM1 mnemonics and are repointed to this node by
/// <see cref="Services.CvmAssemblyLanguage"/>'s own <c>NodeSymbolByMnemonic</c> table (<c>nop</c>
/// -&gt; <c>'nop</c>, <c>pushlit</c> -&gt; <c>'plit</c>, <c>push</c> -&gt; <c>'push</c>, <c>pop</c>
/// -&gt; <c>'pop</c>, <c>ret</c> -&gt; <c>'ret</c>, <c>halt</c> -&gt; <c>'halt</c>); the remaining
/// four (<c>'tjmp</c>, <c>'jump</c>, <c>'xs</c>, <c>'xp</c>) have no existing CVM1 mnemonic and are
/// deliberately NOT wired into that table yet.
///
/// <b><c>'nop</c>'s empty body.</b> <c>: 'nop .loc ( rs-rs) ;</c> compiles to ZERO words. Standalone,
/// its own symbol address lands one past the last actually-written word, landing on the compiler's
/// default-fill pattern (four packed native F18 nop opcodes) rather than on code this source itself
/// wrote. Confirmed working as intended on real hardware (Stefan, 2026-09-02): unwritten RAM already
/// reading as native nops was indeed the intent, not a missing return/branch-back.
///
/// <b>Verification.</b> Compiled standalone against this project's real <c>Compiler/F18Compiler.cs</c>
/// via <c>F18CompilerOptions.ForRam(507)</c>, including the new compile-time-stack-empty check: 60/64
/// words used, 0 errors, entry point <c>m/main</c> at 0x01B. See this class's own remarks for the
/// history behind these numbers.
/// </summary>
internal static class Node507Program
{
  /// <summary>The node this program is always deployed to -- CVM2's entire CPU.</summary>
  public const int Coordinate = 507;

  /// <summary>
  /// Node 507's full resident F18 source, as supplied by Stefan on 2026-09-01 and fixed by Stefan on
  /// 2026-09-02 -- twice: first <c># m/main >r</c> -&gt; <c># m/main lit >r</c> in <c>m/main</c>'s
  /// dispatch loop (confirmed on real hardware: <c>'nop</c> works), then <c>ahead</c> -&gt;
  /// <c>begin</c> in <c>m/call</c> plus a trailing <c>..</c> added to <c>m/main</c>'s <c>dup 2* 2*</c>
  /// (confirmed on real hardware: <c>call</c>/<c>ret</c> now work too). See the class remarks for the
  /// dispatch logic, the ten tick-labeled opcodes, both 2026-09-02 fixes and the refactor, and the
  /// compile verification this source was checked against.
  /// </summary>
  public const string Source = """
      ( CVM2 node 507. main CVM2 node, ????_????_????_???? )
      ( T: stack pointer s, A: program counter p, S: register r)
      # 1 org
      entry m/main
      # 0 /a // program counter
      # up /b // SRAM port
      [ 0 0xffff 2 ] /stack // r = 0, s = 0xffff
      : m/pop ( rs-rsw) dup >r 1 . + r>
      : m/1@ 1 ( rsa-rsw)
      : m/@ ( rsab-rsw) !b !b @b ;
      : m/next ( rs-rsx) a dup 1 . + a!
      : m/0@ ( rsa-rsw) 0 m/@ ;
      : 'plit ( rs-rs) m/next
      : m/push ( rsw-rs) >r -1 . + r> over
      : m/1! ( rswa-rs) 1
      : m/! ( rswab-rs) inv !b inv !b !b ;
      : m/0! ( rswa-rs) 0 m/!
      : m/branch ( rso-rs) a . + a! ;
      : m/call ( rsxy-rs)
        begin drop >r a m/push r> a!
      : m/main ( rs-rs) m/next
        ( rsx)
        dup 2* 2* ..
        -until // 0???_????_????_????
        ( rs)
        # m/main lit >r
        ( rsxy)
        2* -if  // 11??_????_????_????
          -d-- ;
        then // 10??_????_????_????
        ( rsxy)
        2* -if  // 101?_????_????_????
          --l- ;
        then // 100?_????_????_????
        ( rsxy)
        2* -if  // 1001_????_????_????
          r--- ;
        then // 1000_????_????_????
        ( rsxy)
        2* -if  // 1000_1???_????_????
          // local execute
          drop >r ;
        then // 1000_0???_????_????
        // branch relative
        ( rsxy) 2* 2/ 2/ 2/ 2/ 2/ 2/ 2/ a . + a! drop ;
      : 'push .loc ( rs-rs) over m/push ;
      : 'ret .loc ( rs-rs) m/pop a! ;
      : 'tjmp .loc ( rso-rs) a . + a!
      : 'jump .loc ( rs-rs) m/next a! ;
      : 'pop .loc ( rs-rs) m/pop over ;
      : 'xs .loc ( rs-rs) over ;
      : 'xp .loc ( rs-rs) >r a over a! r> ;
      : 'halt .loc ( rs) dup xor dup inv !b !b 0x8800 !b ;
      : 'nop .loc ( rs-rs) ;
      (
      m/call push current p on stack and jumps to function
      m/main CVM interpreter loop
      m/next read the current word and increment p
      'nop do nothing
      'tjmp table jump to address in table with offset
      'jump jump to address in next word
      'plit push literal to stack
      'ret return from subroutine
      'push push r onto stack
      'pop pop r from stack
      'xs exchange s and r
      'xp exchange p and r
      'nop no operation
      'halt execution by setting the SRAM mask to left and right.
      )
      """;
}