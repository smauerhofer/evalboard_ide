namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 707's resident F18 source -- the CVM test-cluster's memory/PC interface node, mirroring
/// real design node 107 (see <see cref="Node607Program"/>'s remarks: "In the TEST setup only,
/// node 707 has no local storage of its own -- it is a stateless interface to the PC-hosted
/// SRAM simulator, reached through 708"). First supplied by Stefan on 2026-08-27, alongside the
/// updated <see cref="Node708Program"/> it imports.
///
/// <b>Position in the cluster.</b> 707 sits between 607 (reached via its own "down" port, set as
/// this source's default B register with <c># down /b</c>) and 708 (reached via its own "left"
/// port, set as the default A register with <c># left /a</c>). It imports 708's compiled RAM
/// exports directly (<c># 708 import</c>) -- <c>'left</c>, <c>'wr</c>, <c>'cx</c>, <c>'rd</c> --
/// rather than defining any memory-access protocol of its own.
///
/// <b>Role.</b> <c>main</c> is an unconditional loop (it calls itself as its own last word) that
/// reads one command word from 607 via <c>@b</c> on every pass, then dispatches on two of its
/// bits: the outer <c>-if</c>/<c>else</c> on the command word itself, the inner <c>-if</c>/<c>else</c>
/// on a second word read from 607. Per Stefan's own inline comments: <c>( ~p ~a )</c> writes a
/// word to the PC via 708's <c>'wr</c>; <c>( ~n p )</c> performs a compare-and-exchange via 708's
/// <c>'cx</c>; <c>( x ~op )</c> is a currently-stubbed "mark" branch (<c>inv if then</c> -- inverts
/// the top of stack and immediately closes the conditional with no body, so it currently has no
/// observable effect either way); <c>( p a )</c> reads a word back via 708's <c>'rd</c>. Each
/// branch first assembles the target 708 word's own address as a packed literal with the
/// <c>A[ '&lt;name&gt; ]] lit !</c> idiom already used throughout this cluster's other CVM nodes
/// (e.g. <see cref="Node606Program"/>, <see cref="Node608Program"/>) before shuttling the
/// requested number of words through it.
///
/// <b>Verification.</b> Compiled together with the new <see cref="Node708Program"/> via this
/// project's real <c>Compiler/F18Compiler.cs</c>, using <c>F18NodeCompilationService</c>'s exact
/// ROM-then-RAM pipeline and cross-node import resolution (a standalone, non-WPF <c>net10.0</c>
/// console harness that constructs real <c>Ga144ChipConfiguration</c>/<c>Ga144RomLibrary</c>
/// objects -- the same production path the node editor and "Install CVM test" both use). Both
/// nodes report <c>Success = true</c>. 707's own ROM is empty (no custom ROM entry for this
/// node in <c>data/ga144-rom.yaml</c>), which produces one expected informational diagnostic --
/// <c>warning F18C003: The source contains no word definitions.</c> -- and no error. 707's RAM
/// compiles to 21/64 words, zero errors, entry point <c>main</c> at word address 0x000 (707
/// exports nothing else: it has no other node importing FROM it).
///
/// <b>Two informational warnings, not errors -- explained, not yet flagged as needing a
/// change.</b> Importing 708 also produces:
/// <code>
/// warning F18C056: Import from node 708 replaces 'warm', already imported from node 707 (0x0A9).
/// warning F18C056: Import from node 708 replaces 'cold', already imported from node 707 (0x0AA).
/// </code>
/// Every RAM compile in this project seeds the standard, fixed-silicon <c>'warm'</c>/<c>'cold'</c>
/// addresses (0x0A9/0x0AA -- <c>F18InstructionSet.CallableRomWords</c>) before any import runs, so
/// 707 already "knows" those two names. 708 is the one CVM node with a real custom ROM of its own
/// (<c>macro rom_async_boot</c>), which happens to define actual words also named <c>warm</c> and
/// <c>cold</c> as part of its boot sequence (<c>rom_warm</c>) -- and because 708's ROM is the real,
/// unmirrored factory ROM, those words compile to the very same addresses, 0x0A9 and 0x0AA.
/// Importing 708 therefore replaces the generic placeholder with a value-identical entry (same
/// node-708 hardware entry points 707 never calls by name), so the warning is a same-value,
/// zero-effect symbol-table bookkeeping notice, not a behavior change; 707's own source never
/// references <c>'warm'</c> or <c>'cold'</c>. Flagged here per this project's standing practice
/// of surfacing anything ambiguous rather than silently accepting it -- confirm with Stefan only
/// if a future revision of 707 or 708 ever needs <c>'warm'</c>/<c>'cold'</c> to resolve to
/// something other than the standard entry points.
/// </summary>
internal static class Node707Program
{
  /// <summary>The node this program is always deployed to -- the memory/PC interface node, mirroring real node 107.</summary>
  public const int Coordinate = 707;

  /// <summary>
  /// Node 707's full resident F18 source, exactly as supplied by Stefan on 2026-08-27. See the
  /// class remarks for the dispatch logic, its import of <see cref="Node708Program"/>, and the
  /// compile verification (including the harmless <c>'warm'</c>/<c>'cold'</c> shadowing warnings)
  /// this source was checked against.
  /// </summary>
  public const string Source = """
      ( cvm test node 707 )
      # 708 import
      # 0 org
      # down /b
      # left /a
      entry main
      : main
        @b
        A[ 'left ]] lit !
        // we hase something to do
        -if
          @b -if
            // ( ~p ~a )  write word
            A[ 'wr ; ]] lit !
            @b ! ! !
          else
            // ( ~n p ) compare and exchange
            A[ 'cx ; ]] lit !
            @b @b ! ! ! ! @ !b
          then
        else
          @b -if
            // ( x ~op ) mark
            inv if
            then
          else
            // ( p a ) read
            A[ 'rd ; ]] lit !
            ! ! @ !b
          then
        then
        main ;
      """;
}
