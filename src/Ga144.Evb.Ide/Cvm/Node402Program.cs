namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 402's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the eighth of 17 new nodes
/// in this batch. Per its own header: "CVM2 node 402. VM 32 bit floatingpoint stage 4a node. exponent
/// handling" -- continuing the control branch <see cref="Node403Program"/> began (403 controls 402, and
/// this node in turn imports from and controls node 401, via the identical
/// "<c>A[ fp5a/word ; ]] lit !</c>" remote-dispatch idiom node 403 used for IT).
///
/// <b>FLAGGED: the header's own prose is inconsistent with its own field list.</b> Point 1 says "read
/// sign &amp; types from node 302," but the <c>in:</c> lines that follow describe EXPONENT data
/// (<c>ee0</c>, <c>eecd</c>), not sign/type -- likely stale prose copied from elsewhere and not updated;
/// reproduced exactly as pasted, not corrected.
///
/// <b>What the body appears to do (worked out from the two input shapes and the branch structure, not
/// spelled out this plainly by Stefan):</b> the two <c>in:</c> lines describe TWO possible shapes of the
/// same incoming message from node 302 -- <c>ee0</c> (exponent pair plus a literal 0) when no exponent-
/// vs-mantissa compare is needed, or <c>eecd</c> (exponent pair, a nonzero compare-request flag, and the
/// exponent delta) when it is. <c>fp4a/main</c> reads the shared <c>ee</c> prefix, then reads and tests
/// the third word as the compare flag; when it's the literal 0 from the <c>ee0</c> shape, the single-armed
/// `if` skips the whole compare block and (per this dialect's own apparent "if doesn't consume the tested
/// value" behavior, already inferred in <see cref="Node301Program"/>'s own remarks) that same 0 is left
/// on the stack, conveniently doubling as an already-correct "not greater" comparison result. When the
/// flag IS set, the node reads the exponent delta, and picks between it and a mantissa-comparison value
/// read from node 401 (`left`, per <c># left /a`) depending on whether the exponent delta is zero --
/// exactly the "compare exponent first, mantissa only as a tie-breaker" shape node 302's own pipeline
/// stage already established. <c>fp4a/gt</c> then converts whichever value won into the same 0/<c>0x8000</c>
/// flag convention used throughout this pipeline.
///
/// <b>FLAGGED, not resolved: <c>fp4a/swap</c>/<c>fp4a/nop</c>/<c>fp4a/add</c>/<c>fp4a/sub</c>/
/// <c>fp4a/mul</c>/<c>fp4a/div</c> are defined here (mirroring node 403's own identical six, this time
/// forwarding to node 401's <c>fp5a/*</c> exports) but are never called anywhere in this pasted
/// <c>fp4a/main</c>.</b> Given node 403's own header ("wait for instructions on right") clearly implies
/// this node is meant to receive and act on the operation address node 403 sends it, either the dispatch
/// happens through a mechanism this source doesn't show (a receive-and-<c>ex</c> step this paste omits,
/// possibly the very thing <c>r---</c> below stands in for), or something else. Not guessed at further.
///
/// <b>FLAGGED, not resolved: <c>r---</c>.</b> Appears alone, mid-<c>fp4a/main</c>, right after a stack
/// comment showing three live values (<c>e1 e2 comp</c>) and right before the two port writes that only
/// account for TWO of them (matching the header's own "out: ee" -- just e1/e2). Not a token this project
/// has seen anywhere before (its shape doesn't resemble any other F18 word seen so far, including the
/// dash-suffixed relay directives like <c>---u</c> this project HAS seen, though the resemblance is
/// noted). Whether it disposes of `comp` some other way, is where the missing instruction-dispatch from
/// the paragraph above actually happens, or is an incomplete/stubbed line, is not stated by Stefan and is
/// not guessed at here.
///
/// <b>Other observations, flagged rather than resolved:</b> the bare <c>..</c> token appears again
/// (after <c>left a!</c>, near the very top of <c>fp4a/main</c>) -- its fifth appearance across this
/// batch so far. <c>fp4a/nop</c>'s own body has a trailing <c>over</c> that its five siblings
/// (<c>fp4a/swap</c>/<c>add</c>/<c>sub</c>/<c>mul</c>/<c>div</c>) don't share -- reproduced as pasted, not
/// resolved. <c>fp4a/gt</c> restates the same three-way sign comparison as node 302's own <c>fp4/gt</c>,
/// but structured with a proper `-if`/`else`/`if`/`then`/`then` nesting instead of two sibling `if`
/// blocks -- a different but equivalent shape, not treated as an inconsistency.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set</b> -- same reasoning as its siblings, and this node's own missing
/// dispatch mechanism (above) is reason enough on its own to hold off.
/// </summary>
internal static class Node402Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 4a (exponent handling, controls node 401), added 2026-09-16.</summary>
  public const int Coordinate = 402;

  /// <summary>
  /// Node 402's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The unresolved
  /// <c>r---</c> and <c>..</c> tokens noted in this class's own remarks above are reproduced exactly as
  /// pasted, not corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 402. VM 32 bit floatingpoint stage 4a node. exponent handling )
      (
      1. read sign & types from node 302
      2. wait for instructions on right

        in: ee0     {from node 302}
        in: eecd    {from node 302}
        out: ee     {to node 502}
      )
      # 401 import

      entry fp4a/main
      # left /a
      # up /b

      # 0 org

      : fp4a/gt ( d-c )
        -if                    // d < 0
          drop 0
        else                   // d >= 0
          if                   // d > 0
            drop 0x8000
          then                 // d = 0 leaves zero
        then
      ;

      : fp4a/swap A[ fp5a/swap ; ]] lit ! ;
      : fp4a/nop A[ fp5a/nop ; ]] lit ! over ;

      : fp4a/add A[ fp5a/add ; ]] lit ! ;
      : fp4a/sub A[ fp5a/sub ; ]] lit ! ;
      : fp4a/mul A[ fp5a/mul ; ]] lit ! ;
      : fp4a/div A[ fp5a/div ; ]] lit ! ;

      : fp4a/main
        @b @b
        left a! ..

        @b if                  // compare required
          drop                  // discard compare-request flag

          @b                    // exponent delta

          if                    // exponent delta != 0
            @ drop              // discard mantissa comparison
          else
            drop @              // exponent equal: use mantissa comparison
          then

          fp4a/gt               // -> 0 or 0x8000
        then

        down a!
        ( e1 e2 comp )

        r---

        down b!
        !b !b
        fp4a/main ;
      """;
}
