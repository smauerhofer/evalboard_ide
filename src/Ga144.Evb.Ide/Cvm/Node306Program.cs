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
/// <item><c>1101_111o_oogg_gfff</c> -- BINARY floating-point operation: operates on two registers,
/// <c>fff</c> (first operand, also where the result is stored) and <c>ggg</c> (second operand), selected
/// independently -- NOT <c>fff</c>/<c>fff+1</c> as an earlier revision of this source implied.</item>
/// <item><c>1101_1101_????_?fff</c> -- constant lookup: loads one of <see cref="Node306Program"/>'s own
/// four pre-defined 32-bit constants (see <c>fpr/const</c> below) into register <c>fff</c>, selected by
/// some offset the source does not spell out precisely (the trailing table says "offset in ?????"
/// verbatim -- reproduced as Stefan wrote it, not filled in).</item>
/// <item><c>1101_1100_????_?fff</c> -- UNARY floating-point operation (the cascade's final fallthrough):
/// operates on one register, <c>fff</c>, result in <c>fff</c>. <c>'fpop</c>/<c>'fpush</c> (stack transfer
/// to/from a register) share this same tag range per the trailing table, distinguished from every other
/// unary op only by which compiled address each resolves to -- the same "shared tag, distinguished by
/// live-compiled function address" scheme every other node-resolved family in this mesh already uses.
/// </item>
/// </list>
///
/// <b>UPDATED 2026-09-16: Stefan pasted a fully rewritten <c>fpr/main</c> BINARY branch</b> (the unary
/// and constant-lookup branches, and <c>fpr/instr</c> itself, are byte-for-byte unchanged from the
/// 2026-09-15 source), replacing the earlier generic "<c>fpr/instr a &gt;r ...</c>" call with an inline
/// decode that finally spells out the exact bit layout the header's own comment
/// ("1101_11??_????_????") only hinted at before: <c>1101_111o_oogg_gfff</c> -- a fixed 7-bit prefix
/// (bits 15-9, "1101111"), then <c>ooo</c> (bits 8-6, the operation: 0=add/1=sub/2=min/3=max/4=mul/5=div,
/// 6/7 unused, per the header's own binary-operation table -- the SAME 6-operation family this project's
/// own 17-node, 32-bit floating-point pipeline pasted this same session implements. Node 306 is now
/// CONFIRMED (2026-09-16, per Stefan) as the BOOT-RELAY parent feeding this pipeline's own entry point
/// (node 305) into the rest of the mesh -- see <see cref="CvmNodeMesh"/>'s own remarks -- but per
/// Stefan's own separate clarification the same day ("the boot stream need not reflect the processing
/// stream"), that boot-relay link does NOT by itself confirm the original speculation here, that this
/// node is where the pipeline's own RUNTIME results actually land; that remains a distinct, still-open
/// question this project has not asserted an answer to. <c>ggg</c> (bits 5-3, the second operand register), and
/// <c>fff</c> (bits 2-0, the first operand / result register). Traced against the new body
/// (<c>drop dup 7 and dup &gt;r a! / over 2/ 2/ 2/ 7 and &gt;r / over 2/ 2/ 2/ 2/ 2/ 2/ 7 and / !b / @+ @
/// !b !b / r&gt; a! / @+ @ !b !b / r&gt; a! / @b @b !+ ! ;</c>): the low 3 bits (<c>7 and</c>) give
/// <c>fff</c>, shifting right 3 more bits (<c>2/ 2/ 2/</c>) then masking to 3 bits gives <c>ggg</c>, and
/// shifting right 6 more bits total gives <c>ooo</c> -- exactly matching the header's own bit positions.
/// <b>RESOLVED 2026-09-16 (same day, later message): the stack-depth discrepancy previously flagged
/// here was Stefan's own typo, now corrected by him directly ("you were right. my comments were
/// wrong").</b> The inline stack comments after each extraction step were originally pasted as
/// <c>( x f / f )</c> and <c>( x f o / f g )</c> (each showing an extra, spurious <c>f</c>) and have been
/// corrected to <c>( x / f )</c> and <c>( x o / f g )</c>, now matching a literal token-by-token trace of
/// <c>drop dup 7 and dup &gt;r a!</c> exactly: it leaves only <c>x</c> on the data stack (both copies of
/// the masked <c>fff</c> value are consumed, one by <c>&gt;r</c> and one by <c>a!</c>), so <c>over</c>
/// duplicating <c>x</c> next is now fully accounted for. This confirms, rather than merely leaves
/// unaffected, the header-derived field-layout facts this class's own field-layout constants are built
/// from (see <c>Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointBinaryOperationFieldBitMask</c> and
/// siblings) -- no toolchain code needed to change, since those constants were always derived from the
/// header's own bit pattern, never from this stack-comment trace.
///
/// <c>fpr/const</c> holds four constants as raw hi/lo word pairs, per the source's own inline comments:
/// ln2, 1/ln2, pi/2, and 2/pi -- likely selected by the constant-lookup opcode's own 5-bit offset field
/// as index 0-3 (matching array order), though this specific offset-to-index mapping is not spelled out
/// in the source and is not guessed at here.
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
/// <b>WIRED, 2026-09-15/16, per Stefan's own direct instruction to add these instructions to the CVM
/// language.</b> <c>'fpop</c> is wired as CVM mnemonic <c>fpop</c>
/// (<c>CvmOperandEncoding.NodeResolvedEmbeddedValue</c>, tag <c>0xDC00</c>, a 5-bit function field / 3-bit
/// register field -- see <c>Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointPopMnemonic</c>'s own
/// remarks for the full field-layout derivation off <c>fpr/instr</c>'s own body, UNCHANGED by the
/// 2026-09-16 binary-branch rewrite above), on the SAME strength as <c>'arinc2</c>/<c>'ardec2</c>'s own
/// wiring. <b><c>'fpush</c> is DELIBERATELY NOT WIRED</b> -- RESOLVED 2026-09-16, per Stefan directly:
/// this was originally flagged as a naming COLLISION against the pre-existing CVM mnemonic <c>fpush</c>
/// (node 506's own frame-pointer push, <c>PushFrameMnemonic</c>), but Stefan's own clarification makes
/// that moot -- node 306's own <c>'fpush</c> "is inside the FP pipeline and not accessible for the CVM"
/// at all (unlike <c>'fpop</c>, which IS CVM-facing), so there is nothing of node 306's to wire under any
/// name, colliding or not. <c>PushFrameMnemonic</c> (node 506) is completely unaffected.
///
/// <b>2026-09-16: the BINARY and CONSTANT-lookup categories are now fully confirmed and wired,</b> per
/// Stefan's own updated header comment on this source (the "index operation mnemonic" table and
/// <c>fpr/const</c>'s own inline "mnemonic fXXX" comments, both reproduced verbatim above) plus his own
/// direct follow-up messages: "node 306 is the node providing opcodes to the CVM" (both categories are
/// fully self-describing -- no live node compile needed to resolve them, unlike <c>'fpop</c> just above)
/// and the exact assembler syntax, "mnemonic f g", e.g. "fadd 3 2" means <c>fr[3] = fr[3] + fr[2]</c>.
/// Binary ops (tag <c>0xDE00</c>, <c>ooo</c> at bits 8-6, <c>ggg</c> at bits 5-3, <c>fff</c> at bits 2-0)
/// are wired as CVM mnemonics <c>fadd</c>/<c>fsub</c>/<c>fmin</c>/<c>fmax</c>/<c>fmul</c>/<c>fdiv</c>
/// (indices 0-5; 6-7 remain unnamed, per Stefan's own "-" table entries) via the brand-new
/// <c>CvmOperandEncoding.EmbeddedUnsignedValuePair</c> shape -- the first shape in this toolchain to pack
/// TWO independent embedded register operands into one self-describing word -- see
/// <c>Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointAddMnemonic</c>'s own remarks for the full
/// per-mnemonic tag derivation. Constant-lookup ops (tag <c>0xDD00</c>, 5-bit offset at bits 7-3,
/// <c>fff</c> at bits 2-0) are wired as <c>fln2</c>/<c>filn2</c>/<c>fpi2</c>/<c>f2pi</c> (offsets 0-3,
/// matching <c>fpr/const</c>'s own array order) via the EXISTING <c>EmbeddedUnsignedValue</c> shape --
/// no new toolchain infrastructure was needed for these four, just one <c>Instructions</c> row each with
/// the offset baked into its own tag.
/// </summary>
internal static class Node306Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point register node (NEW ROLE as of 2026-09-15; this coordinate previously held the address-register node, now at 308 -- see this class's own remarks).</summary>
  public const int Coordinate = 306;

  /// <summary>
  /// Node 306's full resident F18 source. Originally pasted 2026-09-15 ("node 306 and 308 have swapped
  /// roles"); UPDATED 2026-09-16 with a fully rewritten <c>fpr/main</c> BINARY branch (the unary and
  /// constant-lookup EXECUTABLE branches, and every other word in this source, are byte-for-byte
  /// unchanged) -- see this class's own remarks above for the full derivation of the now-confirmed
  /// <c>1101_111o_oogg_gfff</c> bit layout. RE-SYNCED 2026-09-16 (same day, later message) with Stefan's
  /// own updated HEADER COMMENT (the "index operation mnemonic" table, the assembler-syntax example, and
  /// <c>fpr/const</c>'s own inline "mnemonic fXXX" comments) -- no executable word changed, this only
  /// added the mnemonic names Stefan placed directly into the source's own comments. RE-SYNCED AGAIN
  /// 2026-09-16 (same day, a further message: "you were right. my comments were wrong") with Stefan's own
  /// CORRECTED inline stack comments in the binary branch (<c>( x f / f )</c> -&gt; <c>( x / f )</c> and
  /// <c>( x f o / f g )</c> -&gt; <c>( x o / f g )</c>) -- again no executable word changed, only his own
  /// typo fixed; see this class's own remarks above for why this now matches a literal trace exactly.
  /// </summary>
  public const string Source = """
      ( CVM2 node 306. VM 32 bit floatingpoint register node, 1101_11??_????_???? )
      ( contains 8 floatingpoint register )
      ( low word in x, high word in x+1 )
      (
        out: ohlhl
        in: hl

      binary operation ooo:
      index operation mnemonic
      0 add fadd
      1 sub fsub
      2 min fmin
      3 max fmax
      4 mul fmul
      5 div fdiv
      6 -
      7 -

      the assembler instruction is:
      mnemonic f g
      so e.g. "fadd 3 2" means fr[3] = fr[3] + fr[2]

      )
      # 307 import
      # 0x10 org
      entry fpr/main
      # 0 /a
      # right /b

      : fpr/const
      [ // memory order hi, lo
        0x3F31 , 0x7218 , // ln2, mnemonic fln2
        0x3FB8 , 0xAA3B , // 1/ln2, mnemonic filn2
        0x3FC9 , 0x0FDB , // pi/2, mnemonic fpi2
        0x3F22 , 0xF983 , // 2/pi, mnemonic f2pi
      ]

      : 'fpop ( -) leap then
      : fpr/@next ( ) A[ k/pop ]] lit !b A[ !p ]] lit !b @b !+ ;
      : 'fpush ( -) @+ @+ leap then
      : fpr/!next ( w-) A[ @p k/push ]] lit !b !b ;


      : fpr/instr ( xy-ia) drop dup 0x07 and 2* a! 2/ 2/ 2/ 0x1f and ;
      : fpr/leave right b! A[ k/leave ; ]] lit !b
      : fpr/main  A[ !p 2* !p ]] lit !b @b @b left b!
        -if // 1101_111o_oogg_gfff
          // binary
          ( x y )
          drop dup 7 and dup >r a!
          ( x / f )
          over 2/ 2/ 2/ 7 and >r
          ( x / f g )
          over 2/ 2/ 2/ 2/ 2/ 2/ 7 and
          ( x o / f g )
          !b           // o
          @+ @ !b !b   // h l
          r> a!
          @+ @ !b !b   // h l
          r> a!
          @b @b !+ ! ;
        then // 1101_110?_????_????
        2* -if // 1101_1101_????_????
          // constant
          fpr/instr a >r a! @+ @ r> a! !+ ! ;
        then // 1101_1100_????_????
        fpr/instr >r ;

      (
      opcode 1101_111o_oogg_gfff binary floatingpoint operation. first operand is fff. second operant is ggg. result in fff. operation in ooo
      opcode 1101_1101_????_?fff lookup constant. result in fff. offset in ?????.

      'fpop 1101_1100_????_?fff pop 32-bit floatingpoint from stack {low word first} into fp register fff {3 bits}
      'fpush 1101_1100_????_?fff push 32-bit floatingpoint to stack {low word last} from fp register fff {3 bits}


      )
      """;
}