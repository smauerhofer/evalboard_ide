namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 506's resident F18 source -- CVM2's stack-frame node, supplied verbatim by Stefan on
/// 2026-09-02: "i have rewritten node 506. it handles stack frames only." This is a BRAND NEW file --
/// CVM1 also had a node 506 (register-d/extended-precision ops zext/addc/ldd/std/xd/mul2d/div2d/sext/
/// umuld), deleted on 2026-09-01 along with 606/506/407 per <see cref="Services.CvmAssemblyLanguage"/>'s
/// own remarks on the CVM1 leftover NODE removal; this node 506 is unrelated CVM2 content that happens
/// to reuse the same coordinate, not a revival of the old one. It plays roughly the SAME conceptual
/// role CVM1's node 606 played (frame-pointer/local-variable management -- enter/leave/load-local/
/// load-parameter/store-local/store-parameter), just redesigned for CVM2's own relay-based dispatch
/// scheme and a wider 9-bit offset field, rather than 606's 8-bit one.
///
/// <b>Why a separate node.</b> Same reasoning as <see cref="Node407Program"/> (node 507's own RAM is
/// completely full): frame-management primitives need their own resident node with its own fresh
/// 64-word budget. Verified via a standalone harness compile importing <see cref="Node507Program"/>'s
/// own exports (<c>F18CompilerOptions.ForRam(506)</c>): 0 errors, 60/64 words used, entry point
/// <c>f/main</c> at 0x01B, every symbol resolves (<c>par</c> 0x0000, <c>f/next</c> 0x0002, <c>f/pop</c>
/// 0x0005, <c>f/r@</c> 0x0009, <c>f/r!</c> 0x000B, <c>f/push</c> 0x000D, <c>f/stack@</c> 0x000F,
/// <c>f/stack!</c> 0x0014, <c>f/leave</c> 0x0019, <c>f/main</c> 0x001B, <c>'leave</c> 0x0038) -- these
/// addresses are unchanged by the 2026-09-04 bug fix below (removing the stray <c>;</c> only changed
/// which NOP padding filled an already-one-word-long packed instruction, not the total word count).
///
/// <b>Reached from node 507 via the RIGHT port (<c>r---</c>), not <c>-d--</c>.</b> Node 507's own
/// <c>m/main</c> dispatch cascade tests "100?" next as "1001" -&gt; <c>r---</c> (right) vs "1000" ->
/// local-execute/branch-relative (see <see cref="Node507Program"/>'s own remarks) -- and this source's
/// own header, <c>( CVM2 node 506. frame, 1001_????_????_???? )</c>, states exactly that "1001" prefix,
/// so node 506 sits at node 507's right, reached the same "multiport call temporarily fetches from that
/// port" way <see cref="Node407Program"/> is reached via <c>-d--</c>.
///
/// <b>Same relay/receive idiom as node 407.</b> <c>f/main</c> opens with
/// <c>A[ 2* !p !p ]] lit !b @b @b &gt;r</c> -- the identical <c>x</c>/<c>y</c> stack-comment convention
/// traced for node 407 (<see cref="Node407Program"/>'s own remarks): <c>y</c> (the progressively-shifted
/// remaining-bits value) is what gets tested bit by bit below; <c>x</c> (the ORIGINAL fetched CVM opcode
/// word, parked on the native return stack via <c>&gt;r</c>) is what any downstream <c>ex</c> would jump
/// to, and what <c>f/main</c>'s own load/store branches recover via <c>r&gt;</c> to extract the embedded
/// offset from directly (see below) rather than jumping to it.
///
/// <b><c>f/main</c>'s own dispatch cascade and its CVM-level opcode encoding.</b> Three more bits are
/// tested past the "1001" prefix (bits 11, 10, 9 of the ORIGINAL opcode word):
/// <list type="bullet">
/// <item><c>1001_111?_????_????</c> -- load local into r. <c>r&gt; par inv f/stack@</c>: <c>par</c>
/// (<c>0x1ff and</c>) masks the ORIGINAL opcode word's own low 9 bits out as the offset, <c>inv</c>
/// negates it (locals sit BELOW the frame pointer) before <c>f/stack@</c> reads it. Self-describing --
/// the whole word is tag bits 15-9 (<c>1001111</c>) OR'd with a 9-bit value in bits 8-0, no live-node
/// symbol needed to decode it, exactly like CVM1's old node 606 enter/adjust/stl/stp/ldl/ldp/lal/lap
/// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/>) -- just a 9-bit field
/// instead of 606's 8-bit one, and (unlike 606's ops, which were plain unsigned counts/offsets) the
/// VALUE here decodes into a signed local-vs-parameter direction via <c>inv</c>, so this may end up
/// needing its own new embedded-value shape rather than reusing EmbeddedUnsignedValue as-is -- not yet
/// settled.</item>
/// <item><c>1001_110?_????_????</c> -- load parameter into r. <c>r&gt; par f/stack@</c>: same offset
/// extraction, no <c>inv</c> (parameters sit ABOVE the frame pointer).</item>
/// <item><c>1001_101?_????_????</c> -- store local from r. <c>r&gt; par inv f/stack!</c>.</item>
/// <item><c>1001_100?_????_????</c> -- store parameter from r. <c>r&gt; par f/stack!</c>.</item>
/// <item><c>1001_01??_????_????</c> -- <c>r&gt; --l- ;</c>, a further multiport hand-off to node 506's
/// OWN left neighbour -- per the source's own comment, "call node 505." Node 505's source has not been
/// supplied yet; this whole 6-bit-tag sub-family (<c>1001_01??</c>, i.e. 0x9400-0x97FF) is reserved for
/// whatever node 505 itself defines, the same way node 506 was handed the whole "1001" nibble by node
/// 507. Not wired into anything yet -- flagged, not guessed at.</item>
/// <item><c>1001_001?_????_????</c> -- enter stack frame. <c>a A[ @p m/push ]] lit !b !b ;</c> saves the
/// caller's frame pointer, then reads the stack pointer and computes the new frame pointer from
/// <c>r&gt; par inv +</c> -- the 9-bit offset (locals count) again taken directly from the original
/// opcode word's own low bits, self-describing like the load/store ops above.</item>
/// <item><c>1001_000?_????_????</c> -- <c>ex</c> (no preceding <c>r&gt;</c>, matching
/// <see cref="Node407Program"/>'s own confirmed-on-hardware <c>b/main</c> tail exactly): jumps to
/// whatever address is already in R, i.e. <c>x</c> itself (the original opcode word, parked earlier and
/// never popped along any of the branches above) -- the SAME "ex reached once the cascade consumes a
/// fixed prefix" pattern <see cref="Node407Program"/> uses for <c>'lcall</c>/<c>'ljmp</c>.
/// This is where <c>'leave</c> (defined separately below <c>f/main</c>) is actually reached from: TAGGED
/// (needs live-node-symbol resolution, not self-describing), tag bits 15-9 = <c>1001000</c>, i.e.
/// <c>0x9000 | (address of 'leave on node 506)</c> -- <c>0x9038</c> against this exact compile (entry
/// above). Exactly the same "tag | local address" scheme node 507's own local-execute uses with 0x8800
/// and node 407's <c>'lcall</c>/<c>'ljmp</c> use with 0xC000.</item>
/// </list>
///
/// <b>Opcode-space collision with <c>br</c>/<c>ifbr</c> -- KNOWN, ACCEPTED, deliberately left
/// unresolved.</b> The ENTIRE "1001_????_????_????" range (0x9000-0x9FFF) this source claims is already
/// fully owned by <c>br</c> (<see cref="CvmInstructionSet.BranchTag"/>, 0x9000, top 5 bits <c>10010</c>,
/// i.e. every <c>1001_0xxx</c> word) and <c>ifbr</c> (<see cref="CvmInstructionSet.ConditionalBranchTag"/>,
/// 0x9800, top 5 bits <c>10011</c>, every <c>1001_1xxx</c> word) -- together already covering the exact
/// same full nibble, confirmed working on real hardware (the <c>br 1</c> test --
/// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>'s own remarks). Per Stefan
/// (2026-09-02): <c>br</c>/<c>ifbr</c> will eventually move to a new tag range ("i will specified the
/// range later. for now it is not yet defined"), but per Stefan's later explicit instruction (2026-09-04,
/// "ignore the ranges of br/ifbr. ignore the overlapping ranges. give me now enter and leave mnemonics.")
/// <c>enter</c>/<c>leave</c> ARE wired in now anyway (<see cref="CvmInstructionSet.Instructions"/>'s
/// <c>enter</c> entry, tag <see cref="CvmInstructionSet.Node506EnterTag"/> 0x9200; and
/// <see cref="Services.CvmAssemblyLanguage.NodeSymbolByMnemonic"/>'s <c>leave</c> entry, tag
/// <see cref="Services.CvmAssemblyLanguage.Node506LeaveTagBits"/> 0x9000), ENCODING correctly despite the
/// collision -- only DISASSEMBLY of an <c>enter</c>/<c>leave</c> word is currently wrong (reported as
/// <c>br</c> instead), since <see cref="CvmInstructionSet.TryDescribeSelfDecodingWord"/> checks <c>br</c>
/// first for the whole 0x9000-0x97FF range. This node's own load-local/load-parameter/store-local/
/// store-parameter mnemonics and its own relay to node 505 remain UNWIRED (out of the narrower scope
/// Stefan asked for: "give me now enter and leave mnemonics").
///
/// <b>Bug found and fixed (2026-09-04): a stray <c>;</c> inside <c>enter</c>'s own remote
/// read-stack-pointer step corrupted <c>'leave</c>'s compiled encoding.</b> An earlier revision of this
/// source read <c>A[ dup !p ; ]] lit !b @b</c> for that step (three native opcodes packed into one word:
/// <c>dup</c>, <c>!p</c>, then a bare <c>;</c>) and <c>'leave</c>'s own body separately read
/// <c>A[ @p m/pop ]] lit !b a !b A[ !p ]] lit !b @b a! ;</c> (an extra, unneeded <c>@p</c>+send of node
/// 506's OWN CURRENT, not-yet-restored frame pointer feeding 507's <c>m/pop</c> bogus input). Stefan
/// diagnosed and fixed BOTH on real hardware: the packed <c>;</c> is dropped (<c>A[ dup !p ]] lit !b @b</c>,
/// now the same "no bare ';' inside the remote step" shape throughout), and <c>'leave</c> itself is
/// simplified to <c>A[ m/pop ]] lit !b A[ !p ]] lit !b @b a! ;</c> -- pop remotely on 507, transmit the
/// popped value back, store it into this node's own frame-pointer register, matching <c>enter</c>'s own
/// clean "remote op, transmit back, store locally" shape. Independently confirmed in this session, BEFORE
/// Stefan's own report came in, via a standalone compiler-harness bisection against this exact source:
/// with the stray <c>;</c> present, node 506's own compiled RAM never actually contained a valid "call
/// m/pop" instruction at the second word of <c>'leave</c>'s body -- the toolchain's real compiler (not a
/// hand-decode) produced an unrelated packed-opcode word there instead of
/// <see cref="Compiler.F18InstructionSet.TryEncodePackedControl"/>'s own expected call encoding, even
/// though neither word individually looks wrong and an isolated compile of <c>'leave</c>'s body ALONE
/// (outside the full node) encoded correctly -- the corruption only appeared with the complete, real
/// <c>f/main</c> preceding it. Removing the stray <c>;</c> (Stefan's exact fix, reproduced verbatim
/// below) was independently re-verified against the SAME harness to restore the correct call-to-<c>m/pop</c>
/// encoding at that word, with every symbol address unchanged (the stray <c>;</c> only changed which NOP
/// padding filled that one already-one-word-long packed instruction, not the total word count) -- so this
/// was a genuine compile-time correctness bug, not merely a runtime one, and the fix is confirmed correct
/// two independent ways.
/// </summary>
internal static class Node506Program
{
  /// <summary>The node this program is always deployed to -- CVM2's stack-frame node.</summary>
  public const int Coordinate = 506;

  /// <summary>
  /// Node 506's full resident F18 source. Originally supplied by Stefan on 2026-09-02 ("i have
  /// rewritten node 506. it handles stack frames only."); revised 2026-09-04 with the bug fix described
  /// in the class remarks above (the stray <c>;</c> inside <c>enter</c>'s remote read-stack-pointer step,
  /// and the simplified <c>'leave</c> body). See the class remarks for the register/stack helpers,
  /// <c>f/main</c>'s dispatch cascade, its CVM-level opcode encoding, the bug fix, and the accepted
  /// <c>br</c>/<c>ifbr</c> tag collision.
  /// </summary>
  public const string Source = """
      ( CVM2 node 506. frame, 1001_????_????_???? )
      ( A: register f (frame pointer)
      # 507 import
      # 0 org
      entry f/main
      # 0 /a
      # right /b
      : par 0x1ff and ;
      : f/next ( -w) A[ m/next ]] lit !b ahead ;
      : f/pop ( -w) A[ m/pop ]] lit !b then A[ !p ]] lit !b @b ;
      : f/r@ ( -w) A[ over !p ]] lit !b @b ;
      : f/r! ( w) A[ @p over ]] lit !b !b ;
      : f/push ( w) A[ @p m/push ]] lit !b !b ;
      : f/stack@ ( o-a) // load from stack
        a . +  A[ @p m/1@ ]] lit !b !b A[ over ]] lit !b ;
      : f/stack! ( o-a) // store to stack
        a . +  A[ over @p ]] lit !b !b A[ m/1! ]] lit !b ;
      : f/leave A[ ; ]] lit !b
      : f/main // node entry point
        # f/leave lit >r // prepare return address
        A[ 2* !p !p ]] lit !b @b @b >r // push take over code
        -if // 1001_1???_????_????
          2* -if // 1001_11??_????_????
            2* -if // 1001_111?_????_????
              // load local
              r> par inv f/stack@ ;

            then // 1001_110?_????_????
            // load parameter
            r> par f/stack@ ;
          then // 1001_10??_????_????
            2* -if // 1001_101?_????_????
              // store local
              r> par inv f/stack! ;
            then // 1001_100?_????_????
            // store parameter
            r> par f/stack! ;
        then // 1001_0???_????_????
        2* -if // 1001_01??_????_????
          r> --l- ;
        then // 1001_00???_????_????
        2* -if // 1001_001?_????_????
          // enter
          a A[ @p m/push ]] lit !b !b  // save frame pointer
          A[ dup !p ]] lit !b @b // read stack pointer
          r> par inv + a! ; // calculate new frame pointer
        then // 1001_000?_????_????
        ex ;
      : 'leave .loc
        A[ m/pop ]] lit !b A[ !p ]] lit !b @b a! ;
      (
      opcode 1001_111?_????_???? load local into r. the offset is 9 bit.
      opcode 1001_110?_????_???? load parameter into r. the offset is 9 bit.
      opcode 1001_101?_????_???? store local from r. the offset is 9 bit.
      opcode 1001_100?_????_???? store parameter from r. the offset is 9 bit.
      opcode 1001_001?_????_???? enter stack frame. the offset is 9 bit.
      opcode 1001_01??_????_???? call node 505
      'leave restore stack pointer and previous frame. undo enter stack frame.
      )
      """;
}