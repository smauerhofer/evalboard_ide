namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 607's resident F18 source -- CVM2's on-chip SRAM-request router, sitting between node 508
/// (the entire CPU, reached via 607's own "up" port, its default A register) and node 707 (the
/// permanent runtime relay toward the PC's serial link, reached via 607's own "down" port, its
/// default B register). Supplied verbatim by Stefan on 2026-09-01, replacing this file's entire
/// prior CVM1 content (a full CVM CPU node with its own P/S registers and instruction-set
/// interpreter -- CVM2 moved that whole role onto node 508 instead; 607's job in CVM2 is routing
/// SRAM-style requests toward 707, not executing CVM opcodes at all).
///
/// <b>Role.</b> <c>main</c> is an unconditional loop (it calls itself as its own last word) that
/// fetches one command word from 508 via <c>@</c> on every pass, then dispatches on two of its
/// bits -- structurally the SAME two-level dispatch <see cref="Node707Program"/>'s own <c>main</c>
/// uses one hop further down the chain, and the comments on each branch are worded identically to
/// 707's: <c>( ~p ~a )</c> relays a write; <c>( ~n p )</c> relays a compare-and-exchange; <c>( x
/// ~op )</c> is "mark" (<c>inv if ... else ... then</c> -- unlike 707's currently-stubbed mark
/// branch, 607's has a real body: an "stimuli" case and a "set mask" case, both described below);
/// <c>( p a )</c> relays a read. Where 707 forwards each case to a named imported word (<c>'wr</c>,
/// <c>'cx</c>, <c>'rd</c>), 607 performs the actual store/fetch traffic directly with <c>@</c>/
/// <c>!</c> (via A, toward 508) and <c>@b</c>/<c>!b</c> (via B, toward 707) -- it is the node that
/// physically shuttles each word between 508's side of the chain and 707's, rather than a node
/// with its own named sub-routines to jump to.
///
/// <list type="bullet">
/// <item><b>Write</b> (<c>!b @ -if ... !b @ !b</c>): after the outer test consumes the first word,
/// the SAME word is immediately relayed on to 707 via <c>!b</c> (write it out B), then a second
/// word is fetched from 508 (<c>@</c>) and tested; on the write path, that second word is relayed
/// to 707 (<c>!b</c>), then a third word is fetched from 508 and relayed to 707 (<c>@ !b</c>) --
/// three words shuttled from 508's side to 707's side, matching the 3-word write AN003/
/// <see cref="Services.CvmMemoryProtocol"/> and node 508's own <c>m/!</c> already use.</item>
/// <item><b>Compare-and-exchange</b> (<c>!b @ !b @ !b @b !</c>): relays three words from 508 to
/// 707 (<c>!b @ !b @ !b</c>), then reads 707's own reply word back (<c>@b</c>) and writes it back
/// up to 508 (<c>!</c>) -- a 3-word-out/1-word-back shape distinct from the plain read below.</item>
/// <item><b>Mark</b> (<c>( x ~op )</c>, <c>inv if ... else ... then</c> then <c>!b @ !b</c>): tests
/// the (inverted) second word. The "stimuli" branch (<c>a @ a! dup ! a!</c>) reads and rewrites
/// through 607's own A register in place (fetch via A, store the fetched value back into A as the
/// new A, duplicate it, store through the now-updated A, restore A) before falling through to the
/// same two-word relay to 707 the "set mask" branch reaches directly (<c>@ a!</c> -- fetch a word
/// from 508 and load it straight into A). This branch has no counterpart in
/// <see cref="Node707Program"/> (whose own mark branch is still an unwired stub), so 607's exact
/// intent here (updating its own A register as a side channel, versus 707's stub) is Stefan's own
/// design and not independently re-derived.</item>
/// <item><b>Read</b> (<c>( p a )</c>, <c>&gt;r !b r&gt; !b @b !</c>): relays the two address words
/// from 508 to 707 (<c>!b</c> ... <c>!b</c>, with <c>&gt;r</c>/<c>r&gt;</c> preserving stack order
/// across the fetch in between), then reads 707's reply (<c>@b</c>) and writes it back up to 508
/// (<c>!</c>) -- the 2-word-out/1-word-back shape AN003/<see cref="Services.CvmMemoryProtocol"/>'s
/// plain read and node 508's own <c>m/@</c> already use.</item>
/// </list>
///
/// <b>Port setup.</b> <c># down /b</c> (toward 707) and <c># up /a</c> (toward 508) match CVM2's
/// linear chain (508 -&gt; 607 -&gt; 707 -&gt; 708). The source's own inline comment on the up-port
/// directive -- <c>// confine the access to node 507 only for now</c> -- names "507", not "508";
/// this is carried over verbatim from Stefan's own source and NOT altered or reinterpreted here.
/// It may be a leftover from this router's own development history (507 held CVM1's register r,
/// also reached via 607's up port) rather than a statement about CVM2's actual topology, but that
/// is a guess -- worth confirming with Stefan rather than assumed.
///
/// <b>Verification.</b> Compiled standalone against this project's real <c>Compiler/F18Compiler.cs</c>
/// via <c>F18CompilerOptions.ForRam(607)</c> (no import -- 607 does not import any other node's
/// dictionary, matching <see cref="CvmBootStreamBuilder"/>'s own treatment of it): <c>Success =
/// true</c>, zero diagnostics, 18/64 RAM words used, entry point <c>main</c> at word address
/// 0x000. Only a single F18 symbol is exported (<c>main</c> itself -- no tick-labeled words), so
/// unlike node 508, node 607 has no entries in <see cref="Services.CvmAssemblyLanguage"/>'s own
/// mnemonic table and needs none.
/// </summary>
internal static class Node607Program
{
  /// <summary>The node this program is always deployed to -- CVM2's on-chip SRAM-request router, between node 508 and node 707.</summary>
  public const int Coordinate = 607;

  /// <summary>
  /// Node 607's full resident F18 source, exactly as supplied by Stefan on 2026-09-01. See the
  /// class remarks for the dispatch logic and the compile verification this source was checked
  /// against.
  /// </summary>
  public const string Source = """
      ( CVM2 node 607. SRAM simulator using the PC )
      # 0 org
      # down /b
      # up /a // confine the access to node 507 only for now
      entry main
      : main
        @
        // we have something to do
        -if
          !b @ -if
            // ( ~p ~a )  write word
            !b @ !b
          else
            // ( ~n p ) compare and exchange
            !b @ !b @ !b @b !
          then
        else
          @ -if
            // ( x ~op )
            inv if
              // stimuli
              a @ a! dup ! a!
            else
              // set mask
              @ a!
            then
            !b @ !b
          else
            // ( p a ) read
            >r !b r> !b @b !
          then
        then
        main ;
      """;
}