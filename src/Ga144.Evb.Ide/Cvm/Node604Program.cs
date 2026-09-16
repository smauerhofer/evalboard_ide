namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 604's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the fourteenth of 17 new
/// nodes in this batch. Per its own header: "CVM2 node 604. VM 32 bit floatingpoint stage 6 node.
/// classify" -- fills in "stage 6", the gap between node 301/401/501's own stage 5/5a/5b and node 504's own
/// stage 7 that <see cref="Node504Program"/>'s own remarks first flagged. Its own <c>out: ksehl {to node
/// 504}</c> matches node 504's own <c>in: ksehl {from node 604}</c> exactly, confirming the two sources
/// describe the same real link, and its own <c>k:</c> table matches node 504's own <c>k:</c> table
/// verbatim.
///
/// <b>FLAGGED: this node's header appears to misattribute the k=4 resolution.</b> It says "4 ADD/SUB both
/// infinity; <b>404</b> resolves using sign bit14" -- but <see cref="Node504Program"/>'s own pasted source
/// resolves k=4 itself, directly in its own <c>fp7/main</c> ("bit14 of sign resolves Inf versus qNaN"),
/// with no involvement from node 404 at all (node 404's own role, per its own header, is rounding/reducing
/// to binary32 -- unrelated to this classification step). This is most likely a copy-paste slip (writing
/// "404" where "504" was meant, an easy mistake given the two numbers' visual similarity and this pipeline's
/// heavy reuse of stage headers between siblings); reproduced exactly as pasted, not corrected.
///
/// <b><c>fp6/or</c> is a fully confirmed bitwise OR, built from De Morgan's law:</b> <c>&gt;r inv r&gt; inv
/// and inv</c> computes <c>NOT(NOT(a) AND NOT(b)) = a OR b</c> exactly. Used throughout this file to
/// combine two operand-type codes (small enumerated values: 0=finite, 1=zero, 2=infinity, plus a NaN flag
/// at bit 4, per <c>fp6/type</c>'s own masking) into one, exploiting that these codes don't share bits, so
/// OR correctly detects "either operand has this property."
///
/// <b><c>fp6/type</c> and <c>fp6/add</c>/<c>fp6/sub</c> together give the CLEANEST, most exhaustively
/// self-consistent confirmation yet of the "<c>if</c> doesn't auto-consume its tested value" hypothesis
/// this project has relied on since <see cref="Node301Program"/>.</b> Every single branch in
/// <c>fp6/add</c>/<c>fp6/sub</c>'s cascading NaN/infinity/finite classification uses EXACTLY the right
/// number of trailing <c>drop</c>s to clear every value the hypothesis predicts would still be pending
/// (four drops after the NaN check, one plus one after the infinity checks, three at the final fall-
/// through) -- with no slack or shortfall anywhere in the cascade. This is offered as strong supporting
/// evidence, not final confirmation (Stefan hasn't stated the rule outright), but it is the single cleanest
/// trace of it so far.
///
/// <b><c>fp6/add</c> and <c>fp6/sub</c> are a genuinely exact instance of the missing-<c>;</c> fallthrough
/// idiom</b> (the batch's eighth sighting) -- and the cleanest example of WHY it exists: for classification
/// purposes only (not the numeric result), add and subtract are IDENTICAL, since negating an operand's
/// effective sign never changes whether it's NaN, infinity, zero, or finite. Unlike node 403's own
/// <c>fp3a/sub</c>&#8594;<c>fp3a/add</c> pair (which had a sign-flip step before falling through), there is
/// truly zero code between the two colon headers here -- both jump-table entries resolve to one identical
/// routine.
///
/// <b><c>fp6/div</c>'s denominator-remap (<c>if 3 xor then</c>) is fully traced and matches its own inline
/// comment exactly</b>: XOR-ing a nonzero denominator type with 3 swaps 1(zero)&lt;-&gt;2(infinity) while
/// leaving a set NaN bit (value 4) untouched, so "NaN bit survives" as commented, and 0(finite) is skipped
/// entirely by the <c>if</c>.
///
/// <b>The jump table (<c>fp6/add</c>/<c>sub</c>/<c>select</c>(min)/<c>select</c>(max)/<c>mul</c>/<c>div</c>/
/// <c>select</c>(nop)/<c>select</c>(swap)) uses the EXACT same 8-operation ordering as node 403's own table.</b>
/// Combined with node 403's own hypothesis that the opcode <c>o</c> arrives pre-scaled as a literal jump
/// address (not a small integer needing further scaling) and is simply relayed unchanged down this whole
/// pipeline (see <see cref="Node403Program"/>'s own remarks), this node reusing the identical ordering, with
/// <c>o</c> again fed straight into <c>ex</c> via <c>&gt;r</c> with no scaling in between, is consistent
/// with -- though still doesn't independently confirm -- <c>o</c> being one single opcode value carried
/// unchanged all the way from node 303/403 through to this node.
///
/// <b>A clean node overall: no unresolved mystery tokens (<c>.</c>/<c>..</c>/<c>r---</c>/<c>--l-</c>) appear
/// anywhere in this source</b> -- the first node in several to have none at all. Port words (<c># up /a</c>,
/// <c># left /b</c>) are also unremarkable: <c>left</c> reaches node 603 (same-row column neighbor, matching
/// this batch's established lateral-link convention) and <c>up</c> reaches node 504 (a decreasing row
/// number here, opposite of the 500-row's own "up" convention toward 600 -- consistent with, not
/// contradicting, the already-established "same word, different absolute direction per node" finding).
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired into
/// the CVM instruction set</b> -- same reasoning as its siblings; node 603 (this node's own upstream
/// neighbor) is not yet pasted.
/// </summary>
internal static class Node604Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 6 (classify operand types into a result class k), added 2026-09-16.</summary>
  public const int Coordinate = 604;

  /// <summary>
  /// Node 604's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. The header's likely
  /// "404" / "504" mix-up, noted in this class's own remarks above, is reproduced exactly as pasted, not
  /// corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 604. VM 32 bit floatingpoint stage 6 node. classify )
      (
        in:  ottsehl    {from node 603}
        out: ksehl      {to node 504}

        k:
          0 calculated result
          1 zero
          2 infinity
          3 qNaN
          4 ADD/SUB both infinity; 404 resolves using sign bit14
      )

      entry fp6/main
      # up /a
      # left /b

      # 8 org


      : fp6/or ( a b-c )
        >r inv r> inv and inv
      ;


      // map type-like value to result class
      //
      // 0 -> 0 finite
      // 1 -> 1 zero
      // 2 -> 2 infinity
      // anything with NaN bit -> 3

      : fp6/type ( t-k )
        dup 4 and
        if
          drop drop 3 ;
        then
        drop
        3 and
      ;


      // MIN/MAX/NOP/SWAP
      //
      // 403 already selected/reordered the operand.
      // t1 is therefore the selected type.

      : fp6/select ( t1 t2-k )
        drop
        fp6/type
      ;


      // MUL
      //
      // OR gives:
      //
      // 0 finite
      // 1 zero
      // 2 infinity
      // 3 zero*infinity
      //
      // NaN bit is handled by fp6/type.

      : fp6/mul ( t1 t2-k )
        fp6/or
        fp6/type
      ;


      // DIV
      //
      // swap denominator zero/infinity:
      //
      // 0 -> 0
      // 1 -> 2
      // 2 -> 1
      //
      // OR with numerator:
      //
      // 0 finite
      // 1 zero
      // 2 infinity
      // 3 invalid
      //
      // NaN bit survives.

      : fp6/div ( t1 t2-k )
        if
          3 xor
        then
        fp6/or
        fp6/type
      ;


      // ADD/SUB
      //
      // any NaN  -> 3
      // one Inf  -> 2
      // both Inf -> 4
      // otherwise 0

      : fp6/add
      : fp6/sub ( t1 t2-k )

        over over fp6/or

        // any NaN?
        dup 4 and
        if
          drop drop drop drop
          3 ;
        then
        drop

        // any infinity?
        2 and
        if
          drop

          // both infinity?
          and 2 and
          if
            drop
            4 ;
          then

          drop
          2 ;
        then

        drop drop drop
        0
      ;


      : fp6/main
        @b >r                  // o
        @b                     // t1
        @b                     // t2

        ex                     // -> k

        !                      // k
        @b !                   // s
        @b !                   // e
        @b !                   // h
        @b !                   // l

        fp6/main
      ;


      # 0 org

      fp6/add ;
      fp6/sub ;
      fp6/select ;             // min
      fp6/select ;             // max
      fp6/mul ;
      fp6/div ;
      fp6/select ;             // nop
      fp6/select ;             // swap
      """;
}
