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
/// and <c>'right</c> latch which port the following transfers use. <c>'wr</c>
/// writes three words to the PC (reads none back); <c>'rd</c> writes two
/// words then reads one word back; <c>'cx</c> (added 2026-08-27, replacing
/// the earlier two-op protocol) writes four words and reads one word back --
/// a compare-and-exchange with the PC. All three are driven by <c>oword</c>
/// (via the <c>send</c>/<c>send2</c> fall-through pair, and <c>recv</c> for
/// the trailing read), which bit-bangs each word through
/// <c>obit</c>/<c>obyt</c> at whatever rate the <c>bitdelay</c> RAM variable
/// currently holds.
///
/// <b>The <c>send</c>/<c>send2</c>/<c>recv</c> fall-through idiom
/// (2026-08-27 protocol update).</b> <c>: send2 ahead</c> has no terminating
/// <c>;</c>, so it falls straight through into <c>: send then bitdelay
/// oword ;</c> -- calling <c>send2</c> executes <c>send</c>'s body via the
/// unconditional forward jump <c>ahead</c> compiles, landing exactly at
/// <c>then</c>'s resolved target, the same fall-through/<c>ahead</c>...<c>then</c>
/// code-sharing technique this project's other CVM nodes already use (e.g.
/// <c>/pop</c>-&gt;<c>/1@</c>-&gt;<c>/@</c> in <see cref="Node607Program"/>).
/// <c>'wr</c> calls <c>send2</c> once (sharing <c>send</c>'s body) then
/// <c>send</c> and <c>oword</c> directly -- three sends total. <c>'rd</c>'s
/// own definition (<c>@ @ send2</c>) itself has no terminating <c>;</c> and
/// falls through into a newly named word, <c>recv</c> (<c>18ibits drop ! !bitdelay
/// r-l- ;</c>) -- so <c>recv</c> is reachable two ways: by fall-through from
/// <c>'rd</c>, and by an explicit call from <c>'cx</c>, which chains two
/// <c>send2</c>s (four total words sent, matching "write 4 words") before
/// calling <c>recv</c> by name to read the one word back.
///
/// <b>Boot-tree role.</b> Per the load order Stefan confirmed for this
/// cluster (leaves first, root last): "...607 (via 707), 707 (via 708) and
/// 708" -- 708 is the root of the whole CVM boot tree, loaded last, with no
/// "via" (see <see cref="CvmBootStreamBuilder"/>). It needs no cross-node
/// import: everything it calls (<c>18ibits</c>, <c>delay</c>) comes from its
/// own real factory ROM, not from another CVM node's exports -- and node 707
/// imports 708's compiled RAM exports (<c>'left</c>, <c>'wr</c>, <c>'cx</c>,
/// <c>'rd</c>) via its own <c># 708 import</c>, so the entry addresses below
/// are load-bearing for 707, not just documentation.
///
/// <b>Earlier review findings (2026-08-26 protocol), both confirmed by
/// Stefan -- superseded by the source below, kept here for history.</b>
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
/// that version ever called <c>readw</c>, so the fall-through had no effect
/// either way; confirmed with Stefan that <c>readw</c> was not needed and
/// removed entirely (it does not appear in the current, <c>'cx</c>-capable
/// source at all).</item>
/// </list>
///
/// <b>Verification (2026-08-27 protocol update).</b> Compiled against this
/// project's real <c>Compiler/F18Compiler.cs</c> via
/// <c>F18NodeCompilationService</c>'s exact ROM-then-RAM pipeline (a
/// standalone, non-WPF <c>net10.0</c> console harness that constructs real
/// <c>Ga144ChipConfiguration</c>/<c>Ga144RomLibrary</c> objects, driving the
/// same production service the node editor and "Install CVM test" both use).
/// ROM unchanged from before (node 708's actual factory ROM, <c>macro
/// rom_async_boot</c>) -- <c>Success = true</c>, 60/64 ROM words used, the
/// same two expected informational <c>.loc</c> diagnostics. RAM compiled
/// with that ROM's exports in scope -- <c>Success = true</c>, 50/64 RAM
/// words used, <b>zero diagnostics</b>, entry point <c>'start</c> still at
/// word address 0x014. Full symbol table: <c>obit</c> 0x000, <c>oword</c>
/// 0x002, <c>obyt</c> 0x006, <c>!bitdelay</c> 0x011, <c>bitdelay</c> 0x012,
/// <c>'start</c> 0x014, <c>'left</c> 0x01A, <c>'right</c> 0x01D,
/// <c>send2</c> 0x020, <c>send</c> 0x021, <c>'wr</c> 0x023, <c>'rd</c>
/// 0x028, <c>recv</c> 0x02A, <c>'cx</c> 0x02E. Also compiled together with
/// the new <see cref="Node707Program"/> (707 importing 708 exactly as its
/// own <c># 708 import</c> directive requires) -- both report
/// <c>Success = true</c>; see <see cref="Node707Program"/>'s remarks for the
/// two informational <c>'warm'</c>/<c>'cold'</c> shadowing warnings that
/// import produces and why they are harmless.
/// </summary>
internal static class Node708Program
{
  /// <summary>The node this program is always deployed to -- the real, unmirrored async serial boot node.</summary>
  public const int Coordinate = 708;

  /// <summary>
  /// Node 708's full resident F18 source, exactly as supplied by Stefan on 2026-08-27 (the
  /// <c>'wr</c>/<c>'rd</c> protocol reworked around <c>send</c>/<c>send2</c>/<c>recv</c>, and a
  /// new <c>'cx</c> compare-and-exchange operation added). See the class remarks for the
  /// fall-through idiom this relies on and the compile verification it was checked against.
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
      : 'start io b! 18ibits drop drop !bitdelay  r-l- ;
      : 'left left a! --l- ;
      : 'right right a! r--- ;
      : send2 ahead
      : send then bitdelay  oword ;
      : 'wr @ @ @ send2 send oword r-l- ;
      : 'rd @ @ send2 : recv 18ibits drop ! !bitdelay  r-l- ;
      : 'cx @ @ @ @ send2 send2 recv ;
      (
      the variable bitdelay holds the latest bit delay value
      'start waits from a word from the PC, signaling that the PC is ready to play the SRAM. it expect input either from left or right node. left is for
      the virtual machine access to SRAM and right is for console I/O.
      'left prepare to use left port only
      'right prepare to use right port only
      'wr write to PC, write 3 words, read 0 words
      'rd read from PC, write 2 words, read 1 word
      'cx compare and exchange with PC, write 4 words, read 1 word
      )
      """;
}