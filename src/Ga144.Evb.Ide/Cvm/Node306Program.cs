namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 306's resident F18 source. <b>BRAND NEW ROLE, 2026-09-15</b> -- Stefan: "node 306 and 308 have
/// swapped roles," pasting an entirely new node under this coordinate: an 8x 32-bit floating-point
/// register node, reached from node 307's own RIGHT port ("1101_11??_????_????", <c># right /b</c> on
/// this node's own side -- see <see cref="Node307Program"/>'s own "RIGHT/LEFT SWAPPED" remarks). This
/// class's ENTIRE PRIOR HISTORY (2026-09-06 through 2026-09-11: the 32-bit address-register node, its
/// register-count corrections, the <c>arld</c>/<c>arst</c>/<c>lda</c>/<c>sta</c> direction confirmation,
/// the "arld 1 silently becomes nop" incident, and the org-dependent-capacity discovery -- see
/// `claude/cvm-node306-307-address-registers.md`) now belongs to physical node 308 instead -- see
/// <see cref="Node308Program"/>, which now carries that source and that history forward. Nothing below
/// this point describes an address register of any kind.
///
/// <b>Floating-point register file.</b> Per this source's own header and trailing remarks, node 306 (in
/// its new role) holds eight 32-bit floating-point registers, each a word pair (low word in <c>x</c>,
/// high word in <c>x+1</c>, per the header's own third line -- note this is LOW-then-HIGH, the OPPOSITE
/// word order from node 308's own address registers, which store address-then-page). The source's own
/// leading comment block also documents port usage beyond the ordinary <c>/a</c>/<c>/b</c> pair this
/// mesh's other nodes use (<c>out: ohlhl</c>, <c>in: hl</c>) -- their exact meaning is not otherwise
/// explained in the source and is not guessed at here.
///
/// <b>Three-way dispatch, unlike node 308's flat one.</b> <c>fpr/main</c> reads two words via <c>@b</c>
/// (the same "relay two bits per level" idiom used throughout this mesh), THEN diverts port B to the
/// LEFT (<c>left b!</c>) before dispatching, and diverts it back to RIGHT (<c>right b!</c>) inside
/// <c>fpr/leave</c> -- a port-redirection idiom not seen on any other node documented in this project so
/// far; its purpose is not explained in the source and is not guessed at here. The dispatch itself splits
/// three ways per the trailing opcode-table comment:
/// <list type="bullet">
/// <item><c>1101_111?_????_?fff</c> -- BINARY floating-point operation: operates on two registers,
/// <c>fff</c> and <c>fff+1</c>, result in <c>fff</c>.</item>
/// <item><c>1101_1101_????_?fff</c> -- constant lookup: loads one of <see cref="Node306Program"/>'s own
/// four pre-defined 32-bit constants (see <c>fpr/const</c> below) into register <c>fff</c>, selected by
/// some offset the source does not spell out precisely (the trailing table says "offset in ?" verbatim
/// -- reproduced as Stefan wrote it, not filled in).</item>
/// <item><c>1101_1100_????_?fff</c> -- UNARY floating-point operation (the cascade's final fallthrough):
/// operates on one register, <c>fff</c>, result in <c>fff</c>. <c>'fpop</c>/<c>'fpush</c> (stack transfer
/// to/from a register) share this same tag range per the trailing table, distinguished from every other
/// unary op only by which compiled address each resolves to -- the same "shared tag, distinguished by
/// live-compiled function address" scheme every other node-resolved family in this mesh already uses.
/// </item>
/// </list>
///
/// <c>fpr/const</c> holds four constants as raw hi/lo word pairs, per the source's own inline comments:
/// ln2, 1/ln2, pi/2, and 2/pi.
///
/// <b>FLAGGED, not silently resolved or invented: <c>'fpop</c>/<c>'fpush</c>'s own bodies use the same
/// unexplained <c>leap</c>/<c>then</c> shape node 308's new <c>'arinc2</c>/<c>'ardec2</c> also use (see
/// <see cref="Node308Program"/>'s own remarks).</b> <c>: 'fpop ( -) leap then</c> and
/// <c>: 'fpush ( -) @+ @+ leap then</c> both end in <c>leap then</c>, with no closing <c>;</c> for either
/// (so, per this source's own "falls through" idiom, <c>'fpop</c> flows into <c>'fpush</c>, which flows
/// into <c>fpr/instr</c>). <c>leap</c> is not a previously-seen F18 primitive anywhere in this project,
/// not defined earlier in this same source, and not imported from node 307; <c>then</c> here has no
/// preceding <c>-if</c>/<c>-until</c> opener. The fact that the identical shape appears independently on
/// two different new node sources pasted the same day suggests it is a real, deliberate construct in
/// Stefan's own toolchain (perhaps a genuine, newly-introduced F18 primitive this project has not
/// encountered before) rather than a coincidence or a copy-paste artifact -- but its meaning cannot be
/// inferred from context, so it is reproduced completely verbatim.
///
/// <b>PARTIALLY WIRED, 2026-09-15 (same day), per Stefan's own direct instruction to add these
/// instructions to the CVM language.</b> <c>'fpop</c> is wired as CVM mnemonic <c>fpop</c>
/// (<c>CvmOperandEncoding.NodeResolvedEmbeddedValue</c>, tag <c>0xDC00</c>, a 5-bit function field / 3-bit
/// register field -- see <c>Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointPopMnemonic</c>'s own
/// remarks for the full field-layout derivation off <c>fpr/instr</c>'s own body), on the SAME strength
/// as <c>'arinc2</c>/<c>'ardec2</c>'s own wiring -- the assembler only needs to know where a mnemonic's
/// compiled address ends up, not what its F18 body does, so the <c>leap</c>/<c>then</c> uncertainty above
/// does not block this. <b><c>'fpush</c> is FLAGGED, NOT WIRED</b>: it collides with the ALREADY-EXISTING
/// CVM mnemonic <c>fpush</c> (node 506's own frame-pointer push, <c>PushFrameMnemonic</c>) -- a
/// completely different opcode under the same name. This project's own established precedent for exactly
/// this situation (node 505's own <c>'f</c> vs node 506's <c>'f</c>) is to leave the colliding mnemonic
/// unwired rather than silently invent a disambiguated name on Stefan's behalf; <c>'fpush</c> needs a
/// name from him (renaming this node's own version, or node 506's) before it can be added. The BINARY
/// and CONSTANT-lookup dispatch categories remain entirely unwired and un-nameable: the source's own
/// trailing opcode table describes their encoding shape but names no specific operation for either, so
/// nothing can be wired without Stefan supplying actual op names -- not a design-question blocker like
/// before, just nothing to hang a mnemonic on yet.
/// </summary>
internal static class Node306Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point register node (NEW ROLE as of 2026-09-15; this coordinate previously held the address-register node, now at 308 -- see this class's own remarks).</summary>
  public const int Coordinate = 306;

  /// <summary>
  /// Node 306's full resident F18 source, verbatim from Stefan's 2026-09-15 paste ("node 306 and 308
  /// have swapped roles"). Brand new to this project -- no earlier revision of this content existed
  /// under any coordinate.
  /// </summary>
  public const string Source = """
      ( CVM2 node 306. VM 32 bit floatingpoint register node, 1101_11??_????_???? )
      ( contains 8 floatingpoint register )
      ( low word in x, high word in x+1 )
      (
        out: ohlhl
        in: hl
      )
      # 307 import
      # 0x10 org
      entry fpr/main
      # 0 /a
      # right /b

      : fpr/const
      [ // memory order hi, lo
        0x3F31 , 0x7218 , // ln2
        0x3FB8 , 0xAA3B , // 1/ln2
        0x3FC9 , 0x0FDB , // pi/2
        0x3F22 , 0xF983 , // 2/pi
      ]

      : 'fpop ( -) leap then
      : fpr/@next ( ) A[ k/pop ]] lit !b A[ !p ]] lit !b @b !+ ;
      : 'fpush ( -) @+ @+ leap then
      : fpr/!next ( w-) A[ @p k/push ]] lit !b !b ;


      : fpr/instr ( xy-ia) drop dup 0x07 and 2* a! 2/ 2/ 2/ 0x1f and ;
      : fpr/leave right b! A[ k/leave ; ]] lit !b
      : fpr/main  A[ !p 2* !p ]] lit !b @b @b left b!
        -if // 1101_111?_????_????
          // binary
          fpr/instr a >r !b @+ @+ !b !b @+ @+ !b !b r> a! @b @b !+ ! ;
        then // 1101_110?_????_????
        2* -if // 1101_1101_????_????
          // constant
          fpr/instr a >r a! @+ @ r> a! !+ ! ;
        then // 1101_1100_????_????
        fpr/instr >r ;

      (
      opcode 1101_111?_????_?fff binary floatingpoint operation. first operand is fff. second operant is fff+1. result in fff
      opcode 1101_1100_????_?fff unary floatingpoint operation. operand is fff. result in fff
      opcode 1101_1101_????_?fff lookup constant. result in fff. offset in ?.

      'fpop 1101_1100_????_?fff pop 32-bit floatingpoint from stack {low word first} into fp register fff {3 bits}
      'fpush 1101_1100_????_?fff push 32-bit floatingpoint to stack {low word last} from fp register fff {3 bits}


      )
      """;
}