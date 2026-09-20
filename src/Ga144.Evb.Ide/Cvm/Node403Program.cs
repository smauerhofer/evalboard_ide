namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 403's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the seventh of 17 new
/// nodes in this batch. Per its own header: "CVM2 node 403. VM 32 bit floatingpoint stage 3a node. sign
/// &amp; type handling" -- a stage "3a", parallel to node 303's own stage 3 on the 300-row. This confirms
/// the two-branch architecture <see cref="Node303Program"/>'s own remarks already guessed at: node 303
/// splits its input into a "control" branch (opcode/type/sign, to node 403 and onward) and a "data"
/// branch (exponent/mantissa, to node 302 and onward); this node is the first step of that control
/// branch, and per its own header it "controls node 402" (the first node in this batch with a real
/// <c># 402 import</c> directive, and the first with a genuine import at all).
///
/// <b>Likely reconnects this whole new pipeline back to node 306's own previously-unwired binary
/// floating-point dispatch (offered as a hypothesis, not confirmed).</b> <c>fp3a/main</c> reads an opcode
/// word <c>o</c> and, without any table-index arithmetic, feeds it straight into <c>ex</c> against the
/// jump table defined at the bottom of this source (org 0, one entry per operation: add/sub/min/max/mul/
/// div/nop/swap). Since the table's entries are NOT uniform length (the compare-requiring ones are 2
/// words, the rest 3), a simple "base + small-integer-index" scheme could not address them -- so <c>o</c>
/// is most likely already the literal compiled address of one table entry, arriving that way from
/// wherever it originated. That would make <c>o</c> exactly the kind of live-compiled function address
/// <c>Ga144.Cvm.Toolchain.CvmInstructionSet.NodeResolvedEmbeddedValue</c>-shaped mnemonics already resolve
/// elsewhere in this project (see node 306's own binary-op dispatch category, left unwired specifically
/// for lack of a named operation to resolve an address for) -- suggesting this entire 17-node batch may
/// be the actual implementation Stefan is now adding behind those named operations (add/sub/min/max/mul/
/// div). Not confirmed by Stefan in these terms; offered only because it may matter once this batch is
/// wired into the CVM instruction set.
///
/// <b>The "remote control" idiom.</b> <c>fp3a/swap</c>/<c>fp3a/nop</c>/<c>fp3a/mul</c>/<c>fp3a/div</c>/
/// (and the tails of <c>fp3a/add</c>/<c>fp3a/sub</c>/<c>fp3a/min</c>/<c>fp3a/max</c>) each use the
/// already-established "<c>A[ word ; ]] lit !</c>" idiom (take the compiled address of an imported node
/// 402 word, e.g. <c>fp4a/swap</c>, as a literal) and WRITE that address over a port (<c>!</c>, through
/// register <c>a</c>) rather than calling it locally -- i.e. this node tells node 402 which of ITS OWN
/// operations to run by sending it that operation's own address, a remote-dispatch pattern this project
/// has not seen this explicitly before (though the same "address as literal, sent onward" shape has been
/// used for local jump tables elsewhere). This send happens through register <c>a</c> bound to
/// <c>right</c> (reassigned by <c>fp3a/main</c>, via <c>right a!</c>, immediately before it dispatches
/// into the jump table) -- NOT through <c>a</c>'s own directive default of <c>down</c>. <c>down</c> is
/// used separately, explicitly re-bound inside <c>fp3a/send</c>/<c>fp3a/xsend</c>, to relay the (possibly
/// reordered) <c>t1 t2 s1 s2</c> metadata onward to a DIFFERENT node than the one being controlled --
/// consistent with node 402 sitting on this node's own right (matching node 303's own "right = lower
/// column" pattern, not node 302's or node 304's), and the metadata continuing on down the pipeline
/// (perhaps to node 503, not yet pasted) rather than back the way <c>ottssc</c> arrived.
///
/// <b>The fallthrough idiom (missing <c>;</c>, flowing into the next colon definition) recurs SEVEN
/// times in this one source alone</b> (<c>fp3a/swap</c>&#8594;<c>fp3a/xsend</c>,
/// <c>fp3a/nop</c>&#8594;<c>fp3a/send</c>, <c>fp3a/min</c>&#8594;<c>fp3a/min1</c>,
/// <c>fp3a/sub</c>&#8594;<c>fp3a/add</c>) -- already flagged as unexplained syntax in earlier nodes of
/// this batch, but the <c>fp3a/sub</c>/<c>fp3a/add</c> pair makes its PURPOSE unambiguous for the first
/// time: <c>fp3a/sub</c> merely inverts one operand's effective sign (<c>0x8000 xor</c>) and then falls
/// into <c>fp3a/add</c>'s own body to do the actual work -- the standard "subtract is add-the-negation"
/// trick. This is strong evidence the missing-semicolon idiom is a deliberate, working code-reuse
/// mechanism in Stefan's own F18 dialect, not a paste artifact -- even though what makes it legal (rather
/// than a nested/duplicate colon-definition error) is still not understood at the toolchain level.
///
/// <b>FLAGGED, not resolved:</b> port words are yet AGAIN inconsistent with prior nodes in this batch --
/// <c># down /a</c> and <c># up /b</c> here, a third distinct up/down pairing (see
/// <see cref="Node404Program"/>'s own remarks for the second) -- reinforcing that these words are
/// per-node port aliases, not global directions. This node also uses a THIRD word, <c>right</c>, never
/// declared in its own header directives at all (only reassigned into register <c>a</c> at runtime,
/// inside <c>fp3a/main</c>) -- the first node in this batch to use a port beyond the two its own header
/// declares. <c>fp3a/main</c> also forwards the raw opcode <c>o</c> onward via <c>a</c> (still at its
/// directive default, <c>down</c>) before reassigning <c>a</c> to <c>right</c> and dispatching via
/// <c>ex</c> -- i.e. node 403 both relays the opcode further down the pipeline AND acts on it locally;
/// where that relayed copy goes/what reads it is not stated here.
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into the CVM instruction set.</b> This node DOES have a real <c># import</c> (of node 402), unlike
/// every sibling so far -- once node 402's own source is in hand, these two may be ready to add to the
/// mesh/boot order together, parent-before-child per this project's usual convention. Held for now since
/// node 402 hasn't been pasted yet.
///
/// <b>UPDATED, 2026-09-20.</b> Two changes from a side-by-side comparison against what was on file:
/// <list type="bullet">
/// <item>Header gained a "c: magnitude compare result" block documenting the single-word <c>c</c> channel
/// (<c>0x8000</c> = f1 &gt; f2, <c>0</c> = f1 &lt;= f2) -- the same addition made to
/// <see cref="Node302Program"/>/<see cref="Node303Program"/>'s own headers, no code change.</item>
/// <item><c>fp3a/add</c>'s own "normalize so |1| &gt;= |2|" block now dispatches through
/// <see cref="Node402Program"/>'s new <c>fp4a/swapc</c> (<c>A[ fp4a/swapc ]] lit !</c>) instead of the
/// plain <c>fp4a/swap</c> it used before -- this is the confirmation for node 402's own otherwise-
/// unexplained new word: it exists specifically to be called from here.</item>
/// </list>
/// </summary>
internal static class Node403Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 3a (sign/type handling, controls node 402), added 2026-09-16.</summary>
  public const int Coordinate = 403;

  /// <summary>
  /// Node 403's full resident F18 source, verbatim from Stefan's 2026-09-16 paste, updated 2026-09-20
  /// with the header's "c" documentation and the <c>fp4a/swapc</c> dispatch change (see this class's own
  /// remarks above).
  /// </summary>
  public const string Source = """
      ( CVM2 node 403. VM 32 bit floatingpoint stage 3a node. sign & type handling )
      (
        normalizes operand order, effective signs and controls node 402.

        in:  ottssc
        out: ottss

        For add/sub on output:
            |operand1| >= |operand2|
            s1,s2 are effective signs

        c: magnitude compare result
          0x8000 : f1 >  f2
          0      : f1 <= f2
      )

      # 402 import

      entry fp3a/main
      # down /a
      # up /b

      # 8 org

      : fp3a/swap ( t1 t2 s1 s2 )
        A[ fp4a/swap ; ]] lit !
      : fp3a/xsend ( t1 t2 s1 s2 - )
        down a!
        >r >r
        !           // t2
        !           // t1
        r> r> !     // s2
        !           // s1
      ;

      : fp3a/minsel ( t1 t2 s1 s2 comp - t1 t2 s1 s2 sel )
        >r
        over over xor inv
        r> and
        over xor
      ;

      : fp3a/nop ( t1 t2 s1 s2 )
        A[ fp4a/nop ; ]] lit !
      : fp3a/send ( t1 t2 s1 s2 - )
        down a!
        >r >r >r
        !           // t1
        r> !        // t2
        r> !        // s1
        r> !        // s2
      ;

      : fp3a/mul ( t1 t2 s1 s2 )
        A[ fp4a/mul ; ]] lit !
        fp3a/send
      ;

      : fp3a/div ( t1 t2 s1 s2 )
        A[ fp4a/div ; ]] lit !
        fp3a/send
      ;

      : fp3a/min ( t1 t2 s1 s2 comp )
        fp3a/minsel
      : fp3a/min1
        if
          drop fp3a/swap ;
        then
        drop fp3a/nop ;

      : fp3a/max ( t1 t2 s1 s2 comp )
        fp3a/minsel
        0x8000 xor
        fp3a/min1 ;


      : fp3a/sub ( t1 t2 s1 s2 comp )
        >r
        0x8000 xor       // invert effective s2
        r>

      : fp3a/add ( t1 t2 s1 s2 comp )

        // normalize so |1| >= |2|
        0x8000 xor       // 8000 => swap, 0 => keep order
        dup >r
        if
          A[ fp4a/swapc ]] lit !
        then
        drop

        ( t1 t2 s1 s2 / swap )

        // effective signs determine add/subtract
        over over xor
        if
          drop
          A[ fp4a/sub ; ]] lit !
        else
          drop
          A[ fp4a/add ; ]] lit !
        then

        // metadata must follow the numerical operand order
        r> if
          drop fp3a/xsend ;
        then
        drop fp3a/send ;

      : fp3a/main
        @b dup ! >r // o
        @b          // t1
        @b          // t2
        @b          // s1
        @b          // s2
        @b right a! // c
        ( t1 t2 s1 s2 c )
        ex   // call jump table
        fp3a/main ;


      # 0 org // jump table

      fp3a/add ; // add, requires compare
      fp3a/sub ; // sub, requires compare
      fp3a/min ; // min, requires compare
      fp3a/max ; // max, requires compare
      drop fp3a/mul ;
      drop fp3a/div ;
      drop fp3a/nop ;
      drop fp3a/swap ;
      """;
}