namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 708's resident F18 source -- the real, unmirrored PC async serial interface node, CVM2's
/// tail end of the chain (508 -&gt; 607 -&gt; 707 -&gt; 708). Supplied verbatim by Stefan on 2026-09-01,
/// replacing this file's PREVIOUS content (a <c>'wr</c>/<c>'rd</c>/<c>'cx</c> tick-labeled draft
/// that this project had treated as already-correct CVM2 content since it predated the CVM2
/// announcement -- that assumption was wrong; this is the real source).
///
/// <b>Naming break from the earlier draft.</b> This source's three request words are named
/// <c>/wr</c>, <c>/rd</c>, <c>/cx</c> -- a leading SLASH, not the leading TICK (<c>'wr</c>/<c>'rd</c>/
/// <c>'cx</c>) the previous draft used and <see cref="Node707Program"/>'s own source still imports
/// by those tick names via <c>A[ 'wr ; ]]</c>/<c>A[ 'cx ; ]]</c>/<c>A[ 'rd ; ]]</c> lit-packing, nor
/// does this source export anything named <c>'left</c> at all (<see cref="Node707Program"/> also
/// resolves <c>A[ 'left ]]</c> against 708's exports before ever reading a command word). Importing
/// this corrected 708 without also updating 707 to reference <c>/wr</c>/<c>/rd</c>/<c>/cx</c> (and
/// whatever replaces <c>'left</c>, if anything -- this source has no obviously equivalent export)
/// will fail node 707's own RAM compile with undefined-name errors. 707 has NOT been updated to
/// match -- see <see cref="CvmBootStreamBuilder"/>'s own remarks for the exact compile failure this
/// produces until 707's own source is corrected too.
///
/// <b>Words.</b> <c>obit</c>/<c>oword</c>/<c>obyt</c> are the low-level async-serial bit-banging
/// primitives (one bit, one 18-bit word, one byte respectively) that everything else is built from;
/// <c>obyt</c>'s own body opens with three bare <c>then</c>s and no matching <c>if</c> -- not a
/// stray typo but this project's own established idiom (see <see cref="Node507Program"/>/
/// <see cref="Node607Program"/>'s remarks on fall-through) of using a control-flow word as inline
/// F18-word-slot padding/alignment, taken as-is from Stefan's own source rather than reinterpreted.
/// <c>/start</c> is this node's <c>entry</c> point: waits for a stimulus from the PC (via <c>io b!</c>
/// then <c>18ibits</c>, the ROM-resident bit-receive routine) signaling the PC is ready to play the
/// SRAM, then hands control left (<c>--l-</c>, toward 707). <c>/wr</c> (write 3 words, read 0),
/// <c>/rd</c> (write 2 words, read 1), <c>/cx</c> (write 4 words, read 1) match the shapes already
/// documented in this project's AN003 reference and <see cref="Services.CvmMemoryProtocol"/>'s own
/// remarks. <c>/rd</c> has no closing <c>;</c> of its own -- it falls straight through into a new
/// word, <c>recv</c> (the shared single-word-receive tail both <c>/rd</c> and <c>/cx</c> use), the
/// same fall-through idiom <c>obyt</c>'s own padding and this project's other CVM2 nodes rely on
/// throughout. Per Stefan's own trailing comment: the delay value computed by whichever operation
/// ran last is kept on top of the stack (T) so it can be reused by the next write; any read
/// generates a fresh delay instead.
///
/// <b>Verification.</b> Compiled against this project's real <c>Compiler/F18Compiler.cs</c>, paired
/// with its own real factory ROM (<see cref="Node708Rom"/>, providing <c>18ibits</c>/<c>delay</c> as
/// predefined symbols, the same same-node ROM-then-RAM pairing <c>F18NodeCompilationService</c>
/// uses): ROM <c>Success = true</c>, 60/64 words, the same two expected informational <c>.loc</c>
/// diagnostics as before; RAM <c>Success = true</c>, ZERO diagnostics, 41/64 words used, entry
/// point <c>/start</c> at word address 0x011. Full symbol table: <c>obit</c> 0x000, <c>oword</c>
/// 0x002, <c>obyt</c> 0x006, <c>/start</c> 0x011, <c>/wr</c> 0x016, <c>/rd</c> 0x01C, <c>recv</c>
/// 0x01F, <c>/cx</c> 0x022. Also compiled together with the UNCHANGED <see cref="Node707Program"/>
/// to confirm the naming-break claim above rather than just assert it: that combined compile fails
/// with <c>Success = false</c> -- <c>F18C019</c> at each of <c>'left</c>/<c>'wr</c>/<c>'cx</c>/
/// <c>'rd</c> ("is not a primitive F18A opcode and cannot appear inside A[ ... ]]"), plus one
/// cascading <c>F18I001</c> stack-underflow and one cascading <c>F18M005</c> alignment error that
/// follow from the first missing name. 707 needs its own matching update before this pairing will
/// compile again.
/// </summary>
internal static class Node708Program
{
  /// <summary>The node this program is always deployed to -- the real, unmirrored PC async serial interface node.</summary>
  public const int Coordinate = 708;

  /// <summary>
  /// Node 708's full resident F18 source, exactly as supplied by Stefan on 2026-09-01. See the class
  /// remarks for the word-by-word breakdown, the naming break from the earlier draft this replaces,
  /// and the compile verification this source was checked against.
  /// </summary>
  public const string Source = """
      ( CVM2 node 708. PC async interface )
      # 0 org
      # left /a
      entry /start
      : obit ( dwn-dwx) !b over >r delay ;
      : oword ( dw-d) leap drop leap drop leap drop drop ;
      : obyt ( dw-dwx) then then then 3 obit drop
          7 for dup 1 and 3 xor obit drop 2/ next
          2 obit ;
      : /start ( -d) io b! 18ibits drop drop --l- ;
      : /wr ( d-d) @ >r @ >r @ oword r> oword r> oword --l- ;
      : /rd ( d-d) @ >r @ oword r> oword : recv 18ibits drop ! --l- ;
      : /cx ( d-d) @ >r @ >r @ >r @ oword r> oword r> oword r> oword recv ;
      (
      d is the delay is kept in T, so it can be used for every write. Any read will generate a new delay.
      /start waits from a stimulus from the PC, signaling that the PC is ready to play the SRAM.
      /wr write to PC, write 3 words, read 0 words
      /rd read from PC, write 2 words, read 1 word
      /cx compare and exchange with PC, write 4 words, read 1 word
      )
      """;
}