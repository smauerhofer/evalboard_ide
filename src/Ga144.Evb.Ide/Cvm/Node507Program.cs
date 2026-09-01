namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 507's resident F18 source -- CVM2's entire CPU. Supplied verbatim by Stefan on 2026-09-01,
/// correcting an earlier mix-up in this project: the exact same source had been placed on
/// <see cref="Node508Program"/> under the mistaken belief that 508 was the CPU node. It is not --
/// 507 is. <see cref="Node508Program"/> is now a placeholder; see its own remarks.
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
/// <b>m/main's dispatch cascade -- DERIVED, NOT YET CONFIRMED WITH STEFAN.</b> The source's own
/// header comment marks its own top-bit dispatch pattern as still undetermined ("<c>????_????_????_????</c>").
/// Reading the cascade itself: "11??" tests true -&gt; hand off to the DOWN port (<c>-d--</c>) --
/// per the inline comment this reaches a node not yet loaded (read as a future ALU/offload node,
/// possibly node 508 -- see <see cref="Node508Program"/>'s own remarks -- but this is a guess, not
/// confirmed); "101?" -&gt; LEFT port (<c>--l-</c>); "100?" (unconditional at that point) -&gt; RIGHT
/// port (<c>r---</c>) -- this is node 607, CVM2's on-chip SRAM-request router; within the remaining
/// "1000_????" quarter, "1000_1???" is explicitly commented "local execute" (<c>drop &gt;r ;</c> --
/// jump directly to an address in this node's own RAM), and "1000_0???" falls through to "branch
/// relative" instead. "1000_1???_????_????" as a top-5-bit pattern is 0x8800, i.e. opcode = 0x8800 |
/// wordAddress -- corroborated by <c>'halt</c>'s own body literally writing the constant 0x8800 to
/// port b. This has been checked by standalone compile only (all ten tick-labeled symbols resolve
/// to real addresses), never against real hardware or confirmed with Stefan -- treat as a strong
/// hypothesis, not a settled fact.
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
/// <b><c>'nop</c>'s empty body.</b> <c>: 'nop ( rs-rs) ;</c> compiles to ZERO words. Standalone, its
/// own symbol address lands one past the last actually-written word, landing on the compiler's
/// default-fill pattern (four packed native F18 nop opcodes) rather than on code this source itself
/// wrote. Possibly intentional (relying on unwritten RAM already reading as native nops) or possibly
/// missing an explicit return/branch-back -- never resolved with Stefan, still open.
///
/// <b>Verification.</b> Compiled standalone against this project's real <c>Compiler/F18Compiler.cs</c>
/// via <c>F18CompilerOptions.ForRam(507)</c>. See this class's own remarks for the exact word
/// count/entry point/symbol table this revision compiled to.
/// </summary>
internal static class Node507Program
{
  /// <summary>The node this program is always deployed to -- CVM2's entire CPU.</summary>
  public const int Coordinate = 507;

  /// <summary>
  /// Node 507's full resident F18 source, exactly as supplied by Stefan on 2026-09-01. See the
  /// class remarks for the dispatch logic, the ten tick-labeled opcodes, the <c>'nop</c> empty-body
  /// finding, and the compile verification this source was checked against.
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
      : m/0@ ( rsa-rsw) 0 m/@ ;
      : m/next ( rs-rsx) a dup 1 . + a! dup dup xor m/@ ;
      : 'plit ( rs-rs) m/next
      : m/push ( rsw-rs) >r -1 . + r> over
      : m/1! ( rswa-rs) 1
      : m/! ( rswab-rs) inv !b inv !b !b ;
      : m/0! ( rswa-rs) 0 m/!
      : m/branch ( rso-rs) a . + a! ;
      : m/call ( rsxy-rs) ahead drop >r a m/push r> a!
      : m/main ( rs-rs) m/next dup 2* 2* -until // 0???_????_????_????
        ( rs)
        # m/main >r
        ( rsxy) 2* -if  // 11??_????_????_????
          -d-- ;
        then // 10??_????_????_????
        ( rsxy) 2* -if  // 101?_????_????_????
          --l- ;
        then // 100?_????_????_????
          r--- ;
        ( rsxy) 2* -if  // 1001_????_????_????
        then // 1000_????_????_????
        ( rsxy) 2* -if  // 1000_1???_????_????
          // local execute
          drop >r ;
        then // 1000_0???_????_????
        // branch relative
        ( rsxy) 2* 2/ 2/ 2/ 2/ 2/ 2/ 2/ a . + a! drop ;
      : 'push .loc ( rs-rs) over m/push ;
      : 'ret ( rs-rs) m/pop a! ;
      : 'tjmp ( rso-rs) a . + a!
      : 'jump ( rs-rs) m/next a! ;
      : 'pop ( rs-rs) m/pop over ;
      : 'xs ( rs-rs) over ;
      : 'xp ( rs-rs) >r a over a! r> ;
      : 'halt ( rs) dup xor dup inv !b !b 0x8800 !b ;
      : 'nop ( rs-rs) ;
      (
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