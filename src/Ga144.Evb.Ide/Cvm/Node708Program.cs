namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 708's resident F18 source -- the CVM test-cluster's external serial
/// boot/host-link node. Unlike every other CVM node in this cluster (507,
/// 506, 508, 606, 608, which are test-mirrors of real design nodes 307, 306,
/// 308, 206, 208, and 407, which self-maps), 708 is used directly, unmirrored:
/// it is the same physical async serial boot node the real board uses to talk
/// to the PC, and this is its own resident program, not a stand-in for
/// anything else.
///
/// <b>Role.</b> Per Stefan's own description (kept verbatim in the source
/// below): <c>'start</c> waits for a signal word from the PC that the PC is
/// ready to play the SRAM, then expects further traffic from either the left
/// port (the CVM's own virtual-machine access, reaching down through 707/607
/// into the rest of the cluster) or the right port (console I/O). <c>'left</c>
/// and <c>'right</c> latch which port the following transfers use.
/// <c>'wr</c> writes three words to the PC (reads none back); <c>'rd</c>
/// writes two words then reads one word back -- both driven by <c>oword</c>,
/// which bit-bangs each word through <c>obit</c>/<c>obyt</c> at whatever rate
/// the <c>bitdelay</c> RAM variable currently holds.
///
/// <b>Boot-tree role.</b> Per the load order Stefan confirmed for this
/// cluster (leaves first, root last): "...607 (via 707), 707 (via 708) and
/// 708" -- 708 is the root of the whole CVM boot tree, loaded last, with no
/// "via" (see <see cref="CvmBootStreamBuilder"/>). It needs no cross-node
/// import: everything it calls (<c>18ibits</c>, <c>delay</c>) comes from its
/// own real factory ROM, not from another CVM node's exports.
///
/// <b>Review findings, both confirmed by Stefan.</b>
/// <list type="bullet">
/// <item>The original trailing documentation comment ended each of the
/// <c>'wr</c>/<c>'rd</c> lines with a parenthesized aside, e.g.
/// "<c>(write 3 words, read 0 words)</c>". Because this project's
/// <c>F18Tokenizer</c> comments do not nest -- a <c>(...)</c> comment ends at
/// the very next <c>)</c>, full stop -- that inner aside closed the outer
/// comment early, spilling "<c>'rd read from PC</c>" and the final stray
/// <c>)</c> out as live source and failing to compile (unknown words
/// <c>read</c>/<c>from</c>/<c>PC</c>, plus an unexpected <c>)</c>). Fixed by
/// rewording the two asides to plain comma-separated text with no inner
/// parentheses -- confirmed byte-identical in effect to what was clearly
/// intended, with zero compiler diagnostics afterward.</item>
/// <item><c>: readw ( -dwx) dup 18ibits drop over over</c> had no terminating
/// <c>;</c> and fell straight through into <c>oword</c>'s body. Nothing in
/// this file ever called <c>readw</c>, so the fall-through had no effect
/// either way; confirmed with Stefan that <c>readw</c> was not needed and
/// removed entirely.</item>
/// </list>
///
/// <b>Verification.</b> Compiled against this project's real
/// <c>Compiler/F18Compiler.cs</c> in a standalone, non-WPF <c>net10.0</c>
/// console harness that replicates <c>F18NodeCompilationService</c>'s
/// ROM-then-RAM pipeline exactly (including the predefined <c>await</c>
/// symbol injection): ROM first, using node 708's actual factory ROM as
/// recorded in <c>data/ga144-rom.yaml</c> (<c>macro rom_async_boot</c>,
/// which expands to <c>rom_relay</c> + <c>rom_warm</c> + <c>rom_async</c> +
/// <c>rom_shift</c>) -- <c>Success = true</c>, 60/64 ROM words used, with
/// only the two expected informational <c>.loc</c> diagnostics from that
/// pre-existing, unmodified ROM data. RAM then compiled with that ROM's
/// exports in scope -- <c>Success = true</c>, 47/64 RAM words used, zero
/// diagnostics, entry point <c>'start</c> at word address 0x014.
/// </summary>
internal static class Node708Program
{
  /// <summary>The node this program is always deployed to -- the real, unmirrored async serial boot node.</summary>
  public const int Coordinate = 708;

  /// <summary>
  /// Node 708's full resident F18 source, exactly as confirmed by Stefan (the trailing
  /// documentation comment reworded to avoid nested parentheses, and the unused, unterminated
  /// <c>readw</c> word removed). See the class remarks for the compile verification this source
  /// was checked against.
  /// </summary>
  public const string Source = """
      ( cvm test node 708 )
      # 0 org
      entry 'start
      : obit ( dwn-dw) !b over >r delay ;
      : oword ( dw-d)  leap drop  leap drop leap drop  drop ;
      : obyt ( dw-dwx)  then then then  3 obit drop
          7 for dup 1 and 3 xor obit  drop 2/ next
          2 obit ;
      # 0 f18var bitdelay
      : 'start io b! 18ibits drop drop !bitdelay r-l- ;
      : 'left left a! --l- ;
      : 'right right a! r--- ;
      : 'wr bitdelay @ oword bitdelay @ oword bitdelay @ oword r-l- ;
      : 'rd bitdelay @ oword bitdelay @ oword 18ibits drop ! !bitdelay r-l- ;
      (
      the variable bitdelay holds the latest bit delay value
      'start waits from a word from the PC, signaling that the PC is ready to play the SRAM. it expect input either from left or right node. left is for
      the virtual machine access to SRAM and right is for console I/O.
      'left prepare to use left port only
      'right prepare to use right port only
      'wr write to PC, write 3 words, read 0 words
      'rd read from PC, write 2 words, read 1 word
      )
      """;
}
