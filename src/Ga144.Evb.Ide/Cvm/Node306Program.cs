namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 306's resident F18 source. <b>BRAND NEW ROLE, 2026-09-15</b> -- Stefan: "node 306 and 308 have
/// swapped roles," pasting an entirely new node under this coordinate: a 32-bit floating-point
/// register node, reached from node 307's own RIGHT port ("1101_11??_????_????", <c># right /b</c> on
/// this node's own side -- see <see cref="Node307Program"/>'s own "RIGHT/LEFT SWAPPED" remarks). This
/// class's ENTIRE PRIOR HISTORY (2026-09-06 through 2026-09-11: the 32-bit address-register node, its
/// register-count corrections, the <c>arld</c>/<c>arst</c>/<c>lda</c>/<c>sta</c> direction confirmation,
/// the "arld 1 silently becomes nop" incident, and the org-dependent-capacity discovery -- see
/// `claude/cvm-node306-307-address-registers.md`) now belongs to physical node 308 instead -- see
/// <see cref="Node308Program"/>, which now carries that source and that history forward. Nothing below
/// this point describes an address register of any kind.
///
/// <b>CORRECTED, 2026-09-17, per Stefan directly: "there are only 4 FP register."</b> Every earlier
/// revision of this source and its own remarks (2026-09-15/16) claimed EIGHT floating-point registers --
/// that count was wrong and is superseded everywhere it appeared. Register storage is 4 registers x 2
/// words = 8 words (word addresses 0x0-0x7), and the resident code's own <c># N org</c> shrank to match:
/// <c>0x10</c> (which fit an 8-register/16-word file) is now <c>0x8</c> (fitting the smaller 4-register/
/// 8-word file exactly) -- the two facts corroborate each other. <c>fff</c> (the register-index field
/// embedded in every node 306 opcode) is still 3 bits wide (0-7) at the CVM opcode level -- unchanged,
/// since the field's own width is fixed by the opcode format, not by how many registers actually exist
/// behind it -- but only values 0-3 now name a real register; 4-7 are out of range on real hardware.
///
/// <b>Floating-point register file.</b> Per this source's own header and trailing remarks, node 306 (in
/// its new role) holds four 32-bit floating-point registers, each a word pair (low word in <c>x</c>,
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
/// four pre-defined 32-bit constants (see <c>fpr/const</c> below) into register <c>fff</c>. The 5-bit
/// offset field (the trailing table's own "offset in ?????") is RESOLVED, 2026-09-17, per Stefan
/// directly: "the offset to the constant must be encoded in the opcode. it is index*2 + fpr/const" --
/// i.e. the field's actual value is <c>fpr/const</c>'s own LIVE-COMPILED ADDRESS plus <c>index*2</c>
/// (index 0-3, matching <c>fpr/const</c>'s own array order: ln2/1-over-ln2/pi-over-2/2-over-pi, each
/// constant occupying 2 words). This means the constant-lookup mnemonics (<c>fln2</c>/<c>filn2</c>/
/// <c>fpi2</c>/<c>f2pi</c>) are NOT self-describing after all -- see
/// <c>Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointLn2Mnemonic</c>'s own remarks for the
/// resulting encoding correction (EmbeddedUnsignedValue -&gt; NodeResolvedEmbeddedValue, resolving
/// <c>fpr/const</c>'s address from a live compile of this node, exactly like <c>'fpop</c>/<c>'fpush</c>
/// just below).</item>
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
/// wiring. <b><c>'fpush</c> IS NOW WIRED, 2026-09-16, per Stefan's own direct, explicit override:</b> this
/// was originally left unwired because of a naming COLLISION against the pre-existing CVM mnemonic
/// <c>fpush</c> (node 506's own frame-pointer push, <c>PushFrameMnemonic</c>). Stefan overruled that:
/// "with fpush and fpop I can see the data coming in and out of the memory. fpush must be wired. it is a
/// valid opcode. \"'fpush\" from node 306 must be wired as fpush." -- the CVM Debugger has no way to read a
/// live node's internal registers directly, so without this node's own <c>'fpush</c> wired there was no
/// way to observe an FPU result at all, only feed inputs in via <c>'fpop</c>. The collision was resolved by
/// Stefan himself, who renamed node 506's own push word from <c>'fpush</c> to <c>'pushf</c> ("i renamed
/// 'fpush' of node 506 into 'pushf'") -- see <see cref="Node506Program"/>'s own remarks. This node's own
/// <c>'fpush</c> is now wired as CVM mnemonic <c>fpush</c>
/// (<c>CvmOperandEncoding.NodeResolvedEmbeddedValue</c>, sharing <c>'fpop</c>'s own tag
/// <c>FloatingPointUnaryTag</c>/<c>0xDC00</c> and field layout -- see
/// <c>Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointPushMnemonic</c>'s own remarks). <c>PushFrameMnemonic</c>
/// (node 506) is unaffected other than its string VALUE now being <c>"pushf"</c> instead of <c>"fpush"</c>.
///
/// <b>2026-09-16: the BINARY and CONSTANT-lookup categories are now fully confirmed and wired,</b> per
/// Stefan's own updated header comment on this source (the "index operation mnemonic" table and
/// <c>fpr/const</c>'s own inline "mnemonic fXXX" comments, both reproduced verbatim above) plus his own
/// direct follow-up messages: "node 306 is the node providing opcodes to the CVM" and the exact
/// assembler syntax, "mnemonic f g", e.g. "fadd 3 2" means <c>fr[3] = fr[3] + fr[2]</c>.
/// Binary ops (tag <c>0xDE00</c>, <c>ooo</c> at bits 8-6, <c>ggg</c> at bits 5-3, <c>fff</c> at bits 2-0)
/// are wired as CVM mnemonics <c>fadd</c>/<c>fsub</c>/<c>fmin</c>/<c>fmax</c>/<c>fmul</c>/<c>fdiv</c>
/// (indices 0-5; 6-7 remain unnamed, per Stefan's own "-" table entries) via the brand-new
/// <c>CvmOperandEncoding.EmbeddedUnsignedValuePair</c> shape -- the first shape in this toolchain to pack
/// TWO independent embedded register operands into one self-describing word (genuinely self-describing:
/// unlike the constant-lookup family below, the binary ops' own tag needs no live compile at all to
/// resolve) -- see <c>Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointAddMnemonic</c>'s own remarks for
/// the full per-mnemonic tag derivation. Constant-lookup ops (tag <c>0xDD00</c>, 5-bit offset at bits
/// 7-3, <c>fff</c> at bits 2-0) are wired as <c>fln2</c>/<c>filn2</c>/<c>fpi2</c>/<c>f2pi</c> (indices
/// 0-3, matching <c>fpr/const</c>'s own array order).
///
/// <b>CORRECTED, 2026-09-17: the constant-lookup family is NOT self-describing after all,</b> per
/// Stefan's own direct correction: "the offset to the constant must be encoded in the opcode. it is
/// index*2 + fpr/const." The 2026-09-16 wiring above (a fixed <c>EmbeddedUnsignedValue</c> tag per
/// mnemonic, baking in offset 0/1/2/3 directly) was WRONG -- the opcode's real 5-bit offset field must
/// hold <c>fpr/const</c>'s own LIVE-COMPILED ADDRESS plus <c>index*2</c> (each constant is 2 words), a
/// value only known once node 306 is actually compiled, exactly like <c>'fpop</c>/<c>'fpush</c>'s own
/// resolved function address. All four constant mnemonics now resolve the SAME F18 symbol,
/// <c>fpr/const</c>, via <c>CvmOperandEncoding.NodeResolvedEmbeddedValue</c>, each with its own
/// per-mnemonic additive offset (0, 2, 4, 6 words for <c>fln2</c>/<c>filn2</c>/<c>fpi2</c>/<c>f2pi</c>
/// respectively) folded into its own field-layout entry -- see
/// <c>Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointLn2Mnemonic</c>'s own remarks for the full
/// derivation and <c>Ga144.Evb.Ide.Services.CvmAssemblyLanguage.NodeResolvedEmbeddedValueFieldLayoutByMnemonic</c>
/// for the resolution wiring.
///
/// <b>CORRECTED AGAIN, 2026-09-20, per Stefan's own side-by-side comparison against this file and his
/// direct confirmation of both changes below.</b> Two fixes to the 2026-09-17 source above, both
/// confirmed as genuine bugfixes (not stylistic):
/// <list type="bullet">
/// <item>The BINARY branch's own inline operand extraction was missing the register-index doubling
/// every other extraction in this file already applies (<c>fpr/begin</c>'s own <c>dup 7 and 2* a!</c>):
/// <c>drop dup 7 and dup &gt;r a!</c> -&gt; <c>drop dup 7 and 2* dup &gt;r a!</c> for <c>fff</c>, and
/// <c>2/ 2/ 2/ dup 7 and &gt;r</c> -&gt; <c>2/ 2/ 2/ dup 7 and 2* &gt;r</c> for <c>ggg</c>. Without the
/// <c>2*</c>, <c>A</c> was set to the bare 3-bit register index instead of the correct WORD offset into
/// the register file (each of the four registers is a 2-word low/high pair, so register <c>i</c> lives
/// at word offset <c>2*i</c>) -- every binary op (<c>fadd</c>/<c>fsub</c>/<c>fmin</c>/<c>fmax</c>/
/// <c>fmul</c>/<c>fdiv</c>) was addressing the wrong word for both its operands.</item>
/// <item>The constant-lookup branch's trailing store reverts from <c>! !</c> back to <c>!+ !</c>: per
/// Stefan directly, "node 306 '!+ !' was a bugfix" -- the 2026-09-17 remarks above, which described the
/// change FROM <c>!+ !</c> TO <c>! !</c> as the correction, had it backwards. <c>!+ !</c> (store with
/// increment, then store) is the correct sequence; <c>! !</c> was itself the bug.</item>
/// </list>
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
  ///
  /// <b>REPLACED, 2026-09-17,</b> with Stefan's own corrected full source, per his direct message: "here
  /// is node 306. there are only 4 FP register. the offset to the constant must be encoded in the
  /// opcode. it is index*2 + fpr/const." Differences from the 2026-09-16 revision above, all reproduced
  /// exactly as pasted (none interpreted or silently "fixed" here -- see this class's own remarks for the
  /// facts this project DOES draw from them):
  /// <list type="bullet">
  /// <item>Header: "contains 8 floatingpoint register" -&gt; "contains 4 floatingpoint register";
  /// <c># 0x10 org</c> -&gt; <c># 0x8 org</c> (register storage shrank from 16 words to 8, so the
  /// resident code's own origin moved down to match -- see this class's own remarks above).</item>
  /// <item><c>fpr/const</c>, <c>'fpop</c>, <c>'fpush</c>, <c>fpr/begin</c> (see next item) each gained an
  /// explicit <c>.loc</c> marker right after their name, matching the style already used on node 506's
  /// own words -- no functional change, just Stefan syncing this source's own style.</item>
  /// <item><c>fpr/instr</c> renamed <c>fpr/begin</c>; its own stack comment shortened from <c>( xy-ia)</c>
  /// to <c>( x y - i )</c> (no longer documents the "a" register side-effect in the comment, though the
  /// word still sets it); its own body is otherwise the same computation, just reformatted onto two
  /// lines and with <c>0x07</c>/<c>0x1f</c> spelled <c>7</c>/<c>x1f</c> in the new text -- reproduced
  /// exactly as Stefan wrote it, including the "x1f" spelling (presumably <c>0x1f</c>; not silently
  /// corrected here).</item>
  /// <item><c>fpr/main</c> gained a new first line, <c># fpr/leave lit &gt;r</c>, not present in any
  /// earlier revision -- stashes <c>fpr/leave</c>'s own address on the return stack before dispatching.
  /// </item>
  /// <item><c>left b!</c> MOVED: the 2026-09-16 revision ran it unconditionally, immediately after the
  /// initial <c>@b @b</c> dispatch reads and before the <c>-if</c> branch test at all; this revision runs
  /// it only INSIDE the binary branch (right after entering <c>-if</c>), so the constant-lookup and
  /// unary (<c>'fpop</c>/<c>'fpush</c>) branches no longer redirect port B left at all.</item>
  /// <item>The binary branch's own inline stack comments dropped their leading <c>over</c>-tracking
  /// tokens (<c>( x / f g )</c>/<c>( x o / f g )</c> instead of the 2026-09-16 text's identical-looking
  /// comments -- these read the same; the executable words <c>over 2/ 2/ 2/</c> became plain <c>2/ 2/
  /// 2/</c>, since <c>over</c> is no longer needed once the extra <c>drop dup</c> pairing changed slightly
  /// -- reproduced exactly, not re-derived, since the confirmed bit-layout facts this project's own
  /// constants are built from come from the header, not this trace, per this class's own remarks above).
  /// </item>
  /// <item>The constant branch's own body changed from <c>fpr/instr a &gt;r a! @+ @ r&gt; a! !+ ! ;</c>
  /// to <c>fpr/begin a &gt;r a! @+ @ ( h l / f ) r&gt; a! ! ! ;</c> -- the trailing store sequence
  /// changed from <c>!+ !</c> (store-with-increment then store) to <c>! !</c> (two plain stores), and an
  /// inline stack comment <c>( h l / f )</c> was added. Reproduced exactly; not interpreted here.</item>
  /// </list>
  ///
  /// <b>CORRECTED AGAIN, 2026-09-20,</b> per Stefan's own confirmation of two bugfixes (see this class's
  /// own remarks above for the full derivation): the binary branch's operand extraction now doubles
  /// the masked register index (<c>2*</c>) before loading it into <c>A</c>, for BOTH <c>fff</c> and
  /// <c>ggg</c> -- matching <c>fpr/begin</c>'s own already-correct <c>dup 7 and 2* a!</c> pattern, which
  /// this inlined copy had been missing; and the constant branch's trailing store reverts from <c>! !</c>
  /// back to <c>!+ !</c>, per Stefan directly ("node 306 '!+ !' was a bugfix") -- the 2026-09-17 change
  /// away from it, described above, was itself the bug.
  /// </summary>
  public const string Source = """
      ( CVM2 node 306. VM 32 bit floatingpoint register node, 1101_11??_????_???? )
      ( contains 4 floatingpoint register )
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
      # 0x8 org
      entry fpr/main
      # 0 /a
      # right /b

      : fpr/const .loc
      [ // memory order hi, lo
        0x3F31 , 0x7218 , // ln2, mnemonic fln2
        0x3FB8 , 0xAA3B , // 1/ln2, mnemonic filn2
        0x3FC9 , 0x0FDB , // pi/2, mnemonic fpi2
        0x3F22 , 0xF983 , // 2/pi, mnemonic f2pi
      ]

      : 'fpop ( -) .loc
        leap then
      : fpr/@next ( ) A[ k/pop ]] lit !b A[ !p ]] lit !b @b !+ ;
      : 'fpush ( -) .loc
         @+ @+ leap then
      : fpr/!next ( w-) A[ @p k/push ]] lit !b !b ;

      : fpr/begin ( x y - i )
        drop dup 7 and 2* a! 2/ 2/ 2/ x1f and ;
      : fpr/leave right b! A[ k/leave ; ]] lit !b
      : fpr/main
        # fpr/leave lit >r
        A[ !p 2* !p ]] lit !b @b @b
        -if // 1101_111o_oogg_gfff
          left b!
          // binary
          ( x y )
          drop dup 7 and 2* dup >r a!
          ( x / f )
          2/ 2/ 2/ dup 7 and 2* >r
          ( x / f g )
          2/ 2/ 2/ 7 and
          ( o / f g )
          !b           // o
          @+ @ !b !b   // h l
          r> a!
          @+ @ !b !b   // h l
          r> a!
          @b @b !+ ! ;
        then // 1101_110?_????_????
        2* -if // 1101_1101_????_????
          // constant
          ( x y )
          fpr/begin a >r a!
          @+ @
          ( h l / f )
          r> a! !+ !
          ;
        then // 1101_1100_????_????
        fpr/begin >r ;

      (
      opcode 1101_111o_oogg_gfff binary floatingpoint operation. first operand is fff. second operant is ggg. result in fff. operation in ooo
      opcode 1101_1101_????_?fff lookup constant. result in fff. offset of constant in ?????.

      'fpop 1101_1100_????_?fff pop 32-bit floatingpoint from stack {low word first} into fp register fff {3 bits}
      'fpush 1101_1100_????_?fff push 32-bit floatingpoint to stack {low word last} from fp register fff {3 bits}


      )
      """;
}