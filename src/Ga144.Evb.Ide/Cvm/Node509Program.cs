namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 509's resident F18 source -- CVM2's unary-arithmetic node, originally supplied by Stefan on
/// 2026-09-05 ("here is node 509"), then CORRECTED the same day once a debugger session on real
/// hardware turned up a bug in the original <c>u/main</c> body: "this is the fixed code for node 509."
/// This is a BRAND NEW file -- CVM1 had no node 509 at all, so unlike 407/506/508 there is no coordinate
/// reuse here, no old orphaned mnemonics of ITS OWN to worry about (though it does REPOINT several
/// mnemonics orphaned by other nodes -- see below).
///
/// <b>The 2026-09-05 fix, confirmed on real hardware.</b> Stefan's own debugger session assembled
/// <c>lit 0x55 / inc / push</c> against this exact node and captured both the disassembly and the raw
/// transaction log:
/// <code>
/// 0:0000    B455    lit 85
/// 0:0001    B026
/// 0:0002    8830    push
///
/// [READ ] 0:0000 -&gt; B455  (raw [0x00000, 0x00000])
/// [READ ] 0:0001 -&gt; B026  (raw [0x00000, 0x00001])
/// [READ ] 0:0002 -&gt; 8830  (raw [0x00000, 0x00002])
/// [WRITE] 1:FFFE &lt;- 0056  (raw [0x3FFFE, 0x30001, 0x00056])
/// </code>
/// -- i.e. <c>lit 0x55</c> (85 decimal) loaded correctly, <c>inc</c> incremented it to 86 (0x56), and
/// <c>push</c> wrote that exact result to the stack. Re-verified independently in this session via a
/// standalone harness compile of the corrected source below (importing <see cref="Node508Program"/>'s
/// own exports) and the real <c>CvmAssemblyLanguage</c> assembler: assembling the identical
/// <c>lit 0x55 / inc / push</c> program against that compile produces the EXACT SAME three words
/// (<c>0xB455</c>, <c>0xB026</c>, <c>0x8830</c>) and disassembles back to <c>lit 85</c> / <c>inc</c> /
/// <c>push</c>, byte for byte matching Stefan's own hardware log -- this fix is confirmed two
/// independent ways, not just asserted.
///
/// <b>What was actually wrong, and what changed.</b> The ORIGINAL <c>u/main</c> read:
/// <code>
/// : u/main # u/leave lit &gt;r A[ 2* !p !p ]] lit !b @b @b &gt;r
///   -if // 1011_1???_????_????
///     r&gt; --l- ;
///   then // 1011_0???_????_????
///   2* -if // 1011_01??_????_????
///     r&gt; 0x3fc00 and 5 for 2/ 2/ unext u/r! ;
///   then // 1011_00??_????_????
///   u/r@ ex 0xffff and u/r! ;
/// </code>
/// Two separate bugs are visible in hindsight: (1) the opening line pushed the cascade's own test value
/// onto the return stack with a trailing <c>&gt;r</c>, then each branch immediately popped it back with
/// its own <c>r&gt;</c> -- a pointless (and, worse, WRONG-width) round trip; and (2) the "load literal"
/// branch tried to mask/shift the raw value out with <c>0x3fc00 and 5 for 2/ 2/ unext</c> -- 0x3fc00 is
/// a 19-bit mask, far wider than the word itself, and the shift-loop doesn't correctly recover a signed
/// 10-bit field either way. The FIXED version (below) instead: (a) drops the trailing <c>&gt;r</c> after
/// the initial <c>A[ !p 2* !p ]] lit !b @b @b</c> take-over step entirely, leaving the cascade's test
/// value on the DATA stack for each branch to consume directly (and each branch that used to open with
/// <c>r&gt;</c> now just uses the value already there, or <c>drop</c>s it when it isn't needed); (b) the
/// literal-load branch now correctly masks with <c>0x03ff and</c> (10 bits, matching the value field
/// width exactly), then sign-extends by hand: <c>dup 0x0200 and if drop 0xfc00 xor u/r! ; then drop
/// u/r! ;</c> -- test the field's own sign bit (0x0200), and if set, XOR in the upper 6 bits
/// (<c>0xfc00</c>) to sign-extend a negative 10-bit two's-complement value out to the register's own
/// full width, otherwise store the positive value as-is; (c) the final "unary" branch now opens with
/// <c>drop &gt;r</c> -- discarding the leftover cascade-test remainder and re-parking the ORIGINAL
/// opcode word (x) into R itself, immediately before <c>ex</c>, rather than relying on a value parked
/// there from much earlier in the word's own lifetime. Also note: the opening take-over step's own
/// packed instruction changed from <c>2* !p !p</c> to <c>!p 2* !p</c> (op order swapped) -- part of the
/// same fix, not explained further here beyond noting it changed.
///
/// <b>A stray typo in Stefan's own trailing comment block, corrected.</b> An earlier revision of this
/// source's own trailing comment block read <c>opcode 1011_101?_????_????</c> for the literal-load
/// form, which didn't match the ACTUAL dispatch cascade a few lines above it (<c>2* -if //
/// 1011_01??_????_????</c>) or the compiled/hardware-confirmed encoding above (<c>0xB455</c> for
/// <c>lit 0x55</c>, tag 0xB400 -- exactly <see cref="CvmInstructionSet.LitTag"/>'s own value). Flagged
/// here previously; Stefan has since confirmed it was a plain typo and supplied the corrected comment
/// text (<c>1011_01??_????_????</c>, matching the cascade and the confirmed encoding), reproduced in the
/// source below. No behavior changed -- comment text only.
///
/// <b>Why a separate node.</b> Same reasoning as every other CVM2 satellite node (407/506/508): a fresh
/// 64-word budget for a handful of new primitives. Re-verified via a standalone harness compile of the
/// CORRECTED source, importing <see cref="Node508Program"/>'s own exports (<c>g/r@</c>, <c>g/r!</c>,
/// <c>g/pop</c>, <c>g/push</c>, <c>g/leave</c>): 0 errors, 58/64 words used (up from 55, per the
/// two-more-opcodes addition just below), entry point <c>u/main</c> at 0x00E (unchanged), every symbol
/// resolves at its own address (<c>u/r@</c> 0x0000, <c>u/r!</c> 0x0004, <c>u/pop</c> 0x0006, <c>u/push</c>
/// 0x000A, <c>u/leave</c> 0x000C, <c>u/main</c> 0x000E, <c>'abs</c> 0x0023, <c>'neg</c> 0x0025, <c>'inc</c>
/// 0x0026, <c>'dec</c> 0x0028, <c>'inv</c> 0x002A, <c>'mul2</c> 0x002B, <c>'div2</c> 0x002C, <c>'udiv2</c>
/// 0x002E, <c>'bitcnt</c> 0x002F, <c>'parity</c> 0x0037, <c>'odd</c> 0x0038) -- <c>'inc</c>'s own address,
/// 0x0026, is exactly what makes the confirmed <c>0xB026</c> opcode word above correct (0xB000 | 0x026).
///
/// <b>Two more ops added 2026-09-05: <c>'parity</c> and <c>'odd</c>.</b> Per Stefan's own follow-up ("I
/// added 2 new opcodes to node 509. add them also to the language"). Both are wired into
/// <see cref="CvmInstructionSet"/>/<see cref="Services.CvmAssemblyLanguage"/> exactly like the original
/// nine tick-prefixed words (tag 0xB000, resolved only against this node's own live compile) -- see
/// <see cref="CvmInstructionSet.ParityMnemonic"/>'s own remarks. They share the SAME cross-definition
/// fall-through idiom as <c>'abs</c>/<c>'neg</c>/<c>'inc</c>/<c>'dec</c> above: <c>: 'parity 'bitcnt</c>
/// has no own trailing <c>;</c>, so its one-word body (a call to <c>'bitcnt</c>) falls straight through
/// into <c>'odd</c>'s own body (<c>1 and ;</c>) -- confirmed via a standalone compile, where <c>'parity</c>
/// (0x0037) and <c>'odd</c> (0x0038) land at adjacent addresses with no intervening return opcode between
/// them. Entered directly, <c>'parity</c> therefore computes <c>'bitcnt</c> then ANDs the result with 1
/// (the parity bit -- 1 if r holds an odd number of set bits, 0 otherwise, matching the trailing comment's
/// own description), while <c>'odd</c> entered directly at its own address just does the AND-1 test alone
/// (1 if r itself is odd, 0 otherwise) -- each still has its own distinct, independently-reachable
/// address, exactly like the four-way <c>'abs</c>/<c>'neg</c>/<c>'inc</c>/<c>'dec</c> overlap.
///
/// <b>Reached from node 508 (NOT node 507 directly) via the RIGHT port, confirmed symmetrically both
/// sides.</b> This source's own header, <c>( CVM2 node 509. unary arithmetic, 1011_????_????_???? )</c>,
/// states the exact same leading bit pattern as the FIRST test in <see cref="Node508Program"/>'s own
/// <c>g/main</c> dispatch cascade -- <c>-if // 1011_????_????_???? r&gt;r--- ;</c> ("extended
/// arithmetic," per that class's own remarks, previously flagged as relayed onward to an as-yet-
/// unsupplied neighbour) -- node 509 is that neighbour, making CVM2's mesh three hops deep for the
/// first time: 507 -&gt; 508 -&gt; 509, none of them a direct child of 507. This source's own
/// <c># right /b</c> directive binds its own port B to "right," matching node 508's own <c>r---</c>
/// hand-off exactly -- <see cref="Models.KrakenConfiguration.PortAddress"/>'s own geographic-adjacency
/// table independently computes the SAME local port name, "right" (0x1D5), on BOTH sides of the
/// 508&lt;-&gt;509 link, the same symmetric-local-name pattern already confirmed for 407&lt;-&gt;507
/// ("down"), 506&lt;-&gt;507 ("right"), and 508&lt;-&gt;507 ("left"). Unaffected by this fix.
///
/// <b>Imports node 508, not 507.</b> <c># 508 import</c> brings <c>g/r@</c>, <c>g/r!</c>, <c>g/pop</c>,
/// <c>g/push</c>, and <c>g/leave</c> into scope by name -- node 509 never talks to 507 directly at all;
/// every remote op it streams goes to its own immediate neighbour, 508, which is itself just another
/// hop relaying further requests on to 507 as needed. Unaffected by this fix.
///
/// <b>Register/stack helpers.</b> <c>u/r@</c>/<c>u/r!</c>/<c>u/pop</c>/<c>u/push</c>/<c>u/leave</c> are
/// structurally identical, word for word, to every other satellite node's own register helpers (see
/// <see cref="Node508Program"/>'s own <c>g/r@</c>/<c>g/r!</c>/<c>g/pop</c>/<c>g/push</c>/<c>g/leave</c>),
/// just embedding a call to node 508's own imported words (<c>g/r@</c>, <c>g/r!</c>, etc.) instead of
/// node 507's <c>m/*</c> family. Unaffected by this fix.
///
/// <b><c>u/main</c>'s own dispatch cascade, as fixed.</b> Opens with the same "prepare return address,
/// push take over code" idiom every other satellite node's own main dispatch uses (though, per the fix
/// above, WITHOUT a trailing <c>&gt;r</c> this time -- the take-over result stays on the data stack),
/// then tests the header's own <c>1011</c> prefix bit by bit:
/// <list type="bullet">
/// <item><c>1011_1???_????_????</c> -- relayed onward via the LEFT port (<c>--l- ;</c>, no longer
/// preceded by a pointless <c>r&gt;</c>): structurally the SAME "further hand-off to an as-yet-unsupplied
/// neighbour node" idiom every other satellite node's own main dispatch uses for its own still-open
/// relay branches -- not yet answered by anything in CVM2's mesh, left exactly as open as those, rather
/// than guessed at further.</item>
/// <item><c>1011_01??_????_????</c> -- "load literal -512..511", <c>// mnemonic lit</c>: a 10-bit
/// embedded SIGNED value baked directly into the opcode word, now correctly masked and sign-extended
/// (<c>drop 0x03ff and dup 0x0200 and if drop 0xfc00 xor u/r! ; then drop u/r! ;</c>) -- see "What was
/// actually wrong, and what changed" above. Per Stefan's own explicit follow-up ("add this range to the
/// cvm language ... mnemonic lit"), wired into <see cref="CvmInstructionSet"/> as <c>lit</c>,
/// self-describing exactly like <c>br</c>/<c>ifbr</c>/<c>slit</c> (tag 0xB400, a 6-bit tag OR'd with
/// this same 10-bit signed field, UNCHANGED by this fix) -- see
/// <see cref="CvmInstructionSet.LitTag"/>'s own remarks for the full derivation. <c>lit</c> needs no
/// live node/linker resolution at all, unlike every one of node 509's own eleven TAGGED words below.</item>
/// <item><c>1011_00??_????_????</c> -- falls through to <c>drop &gt;r u/r@ ex 0xffff and u/r! ;</c>
/// (the added <c>drop &gt;r</c> is the third part of the fix above): discard the leftover cascade-test
/// remainder, re-park the original opcode word (x) into R, fetch the value relayed in from the caller,
/// <c>ex</c> (jump to whatever address is now in R), then store the result back once whichever named
/// word below returns. This is where every one of node 509's own eleven tick-prefixed words is actually
/// reached from.</item>
/// </list>
///
/// <b>The eleven named words and two separate cross-definition fall-throughs.</b> <c>'abs</c>,
/// <c>'neg</c>, <c>'inc</c>, and <c>'dec</c> share one continuous compile-time control span: <c>'abs</c>
/// opens with a sign test (<c>2* 2* 2/ 2/ -if</c>) whose TRUE branch falls straight through <c>'neg</c>'s
/// own body (<c>inv</c>) into <c>'inc</c>'s own body (<c>1 . + ;</c>) -- i.e. NOT(x)+1, the standard
/// two's-complement negate, exactly what <c>'neg</c> itself computes when entered directly at its own
/// address -- while the FALSE branch of that same <c>-if</c> (x already non-negative) skips past all of
/// <c>'inc</c>/<c>'dec</c>'s own bodies to the <c>then</c> just after <c>'dec</c>'s own <c>;</c>, leaving
/// <c>'abs</c>'s own result unchanged on the stack. This is the exact same "still-open compile-time
/// control marker carries straight through one colon-definition falling into the next" idiom
/// <see cref="Node507Program"/>'s own remarks already document for <c>m/call</c>/<c>m/main</c> -- F18
/// colon-definitions are address labels, not scopes, so nothing unusual is happening here beyond that
/// established pattern; each of the four names still has its own distinct, independently-reachable
/// address regardless of how their bodies happen to overlap in memory. A second, simpler instance of the
/// same idiom spans <c>'parity</c>/<c>'odd</c> (added 2026-09-05): <c>: 'parity 'bitcnt</c> has no own
/// trailing <c>;</c>, so its one-word body (a call to <c>'bitcnt</c>) falls straight through into
/// <c>'odd</c>'s own body (<c>1 and ;</c>) -- entered directly, <c>'parity</c> computes bitcnt-then-AND-1
/// (the parity bit), while <c>'odd</c> entered directly at its own address just does the AND-1 test alone;
/// again, two distinct addresses despite the shared tail. <c>'inv</c>/<c>'mul2</c>/<c>'div2</c>/
/// <c>'udiv2</c>/<c>'bitcnt</c> are each simple, self-contained bodies with no such overlap. Unaffected by
/// the 2026-09-05 bug fix above (only the addresses these words land at moved, per that word-count
/// change).
///
/// <b>CVM-level opcode tag -- unaffected by the bug fix above, re-confirmed against the corrected
/// compile.</b> <c>u/main</c>'s own cascade still consumes exactly "1011_00" (6 fixed bits) before
/// falling to <c>ex</c>, so every one of node 509's eleven named words' own CVM opcode word still has its
/// top 6 bits "101100" -- tag 0xB000 OR'd with the word's own local address on node 509, the SAME
/// "tag | local address" scheme node 507's own local execute (0x8800), node 407's <c>'lcall</c>/
/// <c>'ljmp</c> (0xC000), node 506's <c>'leave</c> (0x9000), and node 508's <c>'ldg</c>/<c>'stg</c>
/// (0xA000) all already use -- see <see cref="Services.CvmAssemblyLanguage.Node509UnaryArithmeticTagBits"/>'s
/// own remarks. No known collision with any existing tag range. Directly confirmed against Stefan's own
/// hardware log above: <c>'inc</c>'s address (0x0026) OR'd with 0xB000 is exactly <c>0xB026</c>, the
/// word the debugger actually read back.
///
/// <b>Wired into <see cref="CvmInstructionSet"/>/<see cref="Services.CvmAssemblyLanguage"/>
/// (2026-09-05).</b> EIGHT of node 509's eleven tick-prefixed words match an EXISTING, previously-orphaned
/// mnemonic exactly (<c>'inv</c>/<c>'inc</c>/<c>'dec</c> from node 507's old ALU-op family,
/// <c>'abs</c>/<c>'mul2</c>/<c>'div2</c>/<c>'udiv2</c>/<c>'bitcnt</c> from node 508's old 27-op family)
/// and, per "only update existing opcodes where possible," REPOINT those existing mnemonics rather than
/// adding duplicates. <c>'neg</c>, <c>'parity</c>, and <c>'odd</c> are the three genuinely new mnemonics
/// -- <c>'neg</c> does NOT repoint the existing, separately-orphaned <c>negate</c> mnemonic (different
/// spelling, taken literally per Stefan's own tick-naming rule), and <c>'parity</c>/<c>'odd</c> have no
/// existing orphaned counterpart of either name at all. See <see cref="CvmInstructionSet.NegMnemonic"/>'s
/// and <see cref="CvmInstructionSet.ParityMnemonic"/>'s own remarks for the full accounting. <c>lit</c>
/// (the embedded-literal form above) was added the same day as node 509's twelfth mnemonic overall,
/// self-describing rather than tagged -- see <see cref="CvmInstructionSet.LitMnemonic"/>'s own remarks.
/// None of this wiring needs to track address changes by hand:
/// <see cref="Services.CvmAssemblyLanguage.BuildDecodeTable"/>/<see cref="Services.CvmAssemblyLanguage.BuildEncodeTable"/>
/// always resolve a TAGGED mnemonic against whichever address its own F18 symbol currently has in a live
/// compile, so every address shift above (from the bug fix, and now from the two new words) is picked up
/// automatically, and <c>lit</c>'s own tag/field shape never moves at all.
/// </summary>
internal static class Node509Program
{
  /// <summary>The node this program is always deployed to -- CVM2's unary-arithmetic node.</summary>
  public const int Coordinate = 509;

  /// <summary>
  /// Node 509's full resident F18 source. Originally supplied by Stefan on 2026-09-05 ("here is node
  /// 509"); corrected the same day ("this is the fixed code for node 509") after a real-hardware
  /// debugger session found the <c>u/main</c> bugs described in the class remarks above. See the class
  /// remarks for the register/stack helpers, <c>u/main</c>'s dispatch cascade, the fix itself, the
  /// 'abs/'neg/'inc/'dec cross-definition fall-through, and the CVM-level opcode tag derivation.
  /// </summary>
  public const string Source = """
      ( CVM2 node 509. unary arithmetic, 1011_????_????_???? )
      # 508 import
      # 0 org
      entry u/main
      # 0 /a
      # right /b
      : u/r@ ( -w) A[ g/r@ ]] lit !b A[ !p ]] lit !b @b ;
      : u/r! ( w) A[ @p g/r! ]] lit !b !b ;
      : u/pop ( -w) A[ g/pop ]] lit !b A[ !p ]] lit !b @b ;
      : u/push ( w) A[ @p g/push ]] lit !b !b ;
      : u/leave A[ g/leave ; ]] lit !b
      : u/main # u/leave lit >r A[ !p 2* !p ]] lit !b @b @b
        -if // 1011_1???_????_????
          //
          --l- ;
        then // 1011_0???_????_????
        2* -if // 1011_01??_????_????
          // load literal -512..511
          // mnemonic lit
          drop 0x03ff and dup 0x0200 and if drop 0xfc00 xor u/r! ; then drop u/r! ;
        then // 1011_00??_????_????
        // unary
        drop >r u/r@ ex 0xffff and u/r! ;

      : 'abs ( x-y) 2* 2* 2/ 2/ -if
      : 'neg ( x-y) inv
      : 'inc ( x-y) 1 . + ;
      : 'dec ( x-y) -1 . + ;
      then
      : 'inv ( x-x) inv ;
      : 'mul2 ( x-y) 2* ;
      : 'div2 ( x-y) 2* 2* 2/ 2/
      : 'udiv2 ( x-y) 2/ ;
      : 'bitcnt dup dup xor >r begin if r> 'inc >r dup 'dec and [ swap ] again then r> ;
      : 'parity 'bitcnt
      : 'odd 1 and ;
      (
      opcode 1011_01??_????_???? load literal to r. the range of the literal is -0x200 to 0x1ff.
      'abs make r absolute
      'neg negate r
      'inc increment r
      'dec decrement r
      'inv invert all bits in r
      'mul2 shift left r
      'div2 signed shift right of r
      'udiv2 unsigned shift right of r
      'bitcnt count 1 bits in r
      'parity if r contains an odd number of 1 bits, then r becomes 1 otherwise r becomes 0.
      'odd r becomes 1 of r is odd, otherwise r becomes 0.
      )
      """;
}