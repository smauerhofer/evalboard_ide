namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 707's resident F18 source -- CVM2's permanent runtime relay between node 607 (SRAM-request
/// router, reached via 707's own "down" port, its default B register) and node 708 (PC async serial
/// interface, reached via 707's own "left" port, its default A register, and imported by name via
/// <c># 708 import</c>). Supplied verbatim by Stefan on 2026-09-01, replacing this file's PREVIOUS
/// content (which this project had wrongly treated as already-correct CVM2 content since it
/// predated the CVM2 announcement -- like <see cref="Node708Program"/>, that assumption was wrong;
/// this is the real source, matched to the real 708 it imports).
///
/// <b>Break from the earlier draft.</b> The earlier draft imported 708 by TICK names (<c>'left</c>,
/// <c>'wr</c>, <c>'cx</c>, <c>'rd</c>) and had a four-way dispatch (write / compare-exchange / a
/// stubbed "mark" branch / read), reading an extra leading <c>'left</c> word before ever looking at
/// a command word. This source imports 708's real SLASH-named exports instead (<c>A[ /wr ; ]]</c>,
/// <c>A[ /cx ; ]]</c>, <c>A[ /rd ; ]]</c>) and drops the "mark" branch and the leading <c>'left</c>
/// read entirely -- consistent with <see cref="Node708Program"/>'s real source, which exports only
/// <c>/wr</c>/<c>/rd</c>/<c>/cx</c> and nothing named <c>'left</c> or "mark"-shaped at all. This is
/// now a plain THREE-way dispatch, not four.
///
/// <b>Role.</b> <c>main</c> is an unconditional loop (it calls itself as its own last word) that
/// reads one command word from 607 via <c>@b</c> on every pass, then dispatches on two of its bits
/// -- the outer <c>-if</c>/<c>else</c> on the command word itself, the inner <c>-if</c>/<c>else</c>
/// (write vs. compare-exchange) only reached when the outer test is true. Per Stefan's own inline
/// comments: <c>( ~p ~a )</c> writes a word to 708 via <c>'wr</c>; <c>( ~n p )</c> performs a
/// compare-and-exchange via <c>'cx</c>; the outer <c>else</c> -- no further bit test -- reads a word
/// back via <c>'rd</c>. Each branch first assembles the target 708 word's own address as a packed
/// literal with the <c>A[ '&lt;name&gt; ]] lit !</c> idiom already used throughout this project's CVM
/// node sources before shuttling the requested number of words through it (matching the same
/// idiom's earlier use in this file, and in the now-removed CVM1 nodes 606/608).
///
/// <b>Verification.</b> Compiled together with the corrected <see cref="Node708Program"/> via this
/// project's real <c>Compiler/F18Compiler.cs</c>, using the same ROM-then-RAM-then-import pipeline
/// <c>F18NodeCompilationService</c>/<see cref="CvmBootStreamBuilder"/> use: <c>Success = true</c>,
/// 17/64 RAM words used, entry point <c>main</c> at word address 0x000. Only the same two harmless,
/// same-value <c>'warm'</c>/<c>'cold'</c> shadowing warnings seen before (<c>F18C056</c> -- 708's own
/// real factory ROM happens to define words also named <c>warm</c>/<c>cold</c> at the same fixed
/// silicon addresses 0x0A9/0x0AA every RAM compile already seeds by default; 707's own source never
/// references either name). A single exported symbol, <c>main</c> -- no other node imports FROM 707.
/// </summary>
internal static class Node707Program
{
  /// <summary>The node this program is always deployed to -- the memory/PC interface node, mirroring real node 107.</summary>
  public const int Coordinate = 707;

  /// <summary>
  /// Node 707's full resident F18 source, exactly as supplied by Stefan on 2026-09-01, matched to
  /// the corrected <see cref="Node708Program"/> it imports. See the class remarks for the dispatch
  /// logic, the break from the earlier (wrongly-trusted) draft, and the compile verification this
  /// source was checked against.
  /// </summary>
  public const string Source = """
      ( CVM2 node 707. SRAM communication between nodes 607 and 708 )
      # 708 import
      # 0 org
      # down /b
      # left /a
      entry main
      : main
        @b
        // we hase something to do
        -if
          @b -if
            // ( ~p ~a )  write word
            A[ /wr ; ]] lit !
            @b ! ! !
          else
            // ( ~n p ) compare and exchange
            A[ /cx ; ]] lit !
            @b @b ! ! ! ! @ !b
          then
        else
          // ( p a ) read word
          A[ /rd ; ]] lit !
          @b ! ! @ !b
        then
        main ;
      """;
}