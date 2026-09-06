namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 406's resident F18 source -- CVM2's binary-arithmetic node, originally supplied by Stefan
/// (2026-09-05, "i provide you with node 406. it contains binary operations. add these opcodes to
/// assembler and disassembler and include this node in the boot stream"), then CORRECTED the same day
/// once real hardware turned up a bug in the original <c>y/main</c> body: "i fixed node 406. now it
/// works with hardware." A BRAND NEW coordinate, no CVM1 namesake at all.
///
/// <b>The 2026-09-05 fix, confirmed on real hardware.</b> The ORIGINAL <c>y/main</c> opened with:
/// <code>
/// : y/main # y/leave lit &gt;r A[ 2* !p !p ]] lit !b @b @b &gt;r
/// </code>
/// The FIXED version (below) instead reads:
/// <code>
/// : y/main # y/leave lit &gt;r A[ !p 2* !p ]] lit !b @b &gt;r @b
/// </code>
/// Two things changed together: (1) the packed take-over instruction's own op order swapped from
/// <c>2* !p !p</c> to <c>!p 2* !p</c> -- the SAME op-order swap Stefan's own earlier, separately
/// hardware-confirmed fix to <see cref="Node509Program"/>'s own <c>u/main</c> made to its own take-over
/// step (see that class's own remarks: "not explained further here beyond noting it changed" -- the same
/// honest scope applies here, since neither fix's own deeper rationale has been spelled out); and (2) the
/// receive sequence changed from <c>@b @b &gt;r</c> to <c>@b &gt;r @b</c> -- WHICH of the two relayed
/// words (received one at a time via <c>@b</c>) ends up on the return stack versus the data stack
/// swapped: the ORIGINAL sequence read both words onto the data stack THEN moved the SECOND one to the
/// return stack (leaving the FIRST on the data stack); the FIXED sequence moves the FIRST word to the
/// return stack immediately, THEN reads the second (leaving it on the data stack). Since <see cref="Node406Program"/>
/// (unlike <see cref="Node509Program"/>'s own unary ops, which only ever needed one relayed value) needs
/// BOTH relayed words for its own binary ops -- one held in R for <c>y/r@</c>/<c>y/r!</c> to read/write
/// (the "x" operand) and one left on the data stack for <c>y/pop</c> to consume directly, or skipped past
/// by <c>y/next</c>'s own trailing-word path (the "y" operand/parameter) -- getting the two swapped is
/// exactly the kind of bug that only shows up once a program actually exercises a binary op with a real
/// value pair, not in a standalone compile (which only ever confirms addresses resolve, never runtime
/// data flow -- see this class's own Verification paragraph below). Re-verified in this session via a
/// standalone harness compile of the corrected source: 0 errors, addresses UNCHANGED from before the fix
/// (62/64 words, <c>y/main</c> still at 0x0014, every op still at the same address) -- the fix is purely
/// a runtime data-flow correction, not a structural change, exactly mirroring how <see cref="Node509Program"/>'s
/// own op-order fix left its own addresses untouched too.
///
/// <b>Why a separate node, and why reached via node 407's RIGHT port.</b> This node fills node 407's own
/// previously-unsupplied "1110_????_????_????" further-relay branch (see <see cref="Node407Program"/>'s
/// own remarks on its <c>n/main</c> dispatch cascade: "111?" -&gt; "1111" hands off LEFT, else "1110"
/// hands off RIGHT) -- exactly the same shape as node 509 filling node 508's own previously-unsupplied
/// "1011" branch, one level deeper in CVM2's mesh. <c># 407 import</c> brings node 407's own exported
/// symbols (<c>n/r@</c>, <c>n/r!</c>, <c>n/pop</c>, <c>n/push</c>, <c>n/next</c>, <c>n/leave</c>) into
/// scope here by name. Node 406's own source, AS ORIGINALLY SUPPLIED, already referenced these exact
/// <c>n/</c>-prefixed names -- it needed no changes at all once node 407 itself was corrected to export
/// them under that prefix (see <see cref="Node407Program"/>'s own remarks on the <c>b/</c>-&gt;<c>n/</c>
/// rename and the new <c>n/next</c> export, Stefan's own fix for a blocking import-name mismatch this
/// node's original source exposed).
///
/// <b><c>y/main</c>'s own dispatch cascade.</b> Reads two words via <c>@b</c> (from port B, the link
/// back to node 407) into a register-r-held first word, per-word bit-testing further within the
/// already-consumed "1110" prefix:
/// <list type="bullet">
/// <item><description><c>-if</c> true (bit reads "1110_1???") -&gt; <c>r&gt; r--- ;</c>, a further RIGHT-port
/// relay ("// more arithmetic") to some as-yet-unsupplied neighbour node -- left open, not guessed at,
/// exactly like node 509's own still-open "1011_1???" LEFT branch and node 407's own still-open "1111"/
/// "1101" branches.</description></item>
/// <item><description><c>-if</c> false (bit reads "1110_0???") falls through, past the first
/// <c>then</c>, into the SECOND test.</description></item>
/// </list>
///
/// <b>The two-form dispatch, and how it converges via <c>ahead</c>/<c>[ swap ]</c>/<c>then</c> --
/// traced precisely, the same rigor node 509's own <c>begin [ swap ]</c> mechanism was given.</b>
/// <code>
///   2* -if // 1110_01??_????_????
///     y/next ahead [ swap ]
///   then // 1110_00??_????_????
///   y/pop then y/r@ ex 0xffff and y/r! ;
/// </code>
/// Compile-time control-flow markers live on <see cref="Compiler.F18CompileTimeInterpreter"/>'s own
/// data stack (<c>PushControlValue</c>/<c>TryPopControlValue</c> are thin wrappers over
/// <c>TryPushData</c>/<c>TryPopData</c> -- the SAME stack <c>swap</c> operates on), resolved LIFO: a
/// <c>then</c> always resolves the MOST RECENTLY pushed still-open marker. Walking this exact sequence:
/// the second <c>-if</c> pushes a forward-branch marker (call it M2, resolved when its own condition is
/// false); its TRUE body is <c>y/next ahead [ swap ]</c> -- <c>y/next</c> fetches the trailing constant
/// word (the "parameter in next word" form's own operand), then <c>ahead</c> pushes a SECOND, unconditional
/// forward-branch marker (M3) on top of M2, and <c>[ swap ]</c> immediately swaps them, putting M2 back on
/// top -- structurally the identical "push a marker, then reorder the stack so a nested/later
/// construct's own marker resolves out of its natural LIFO order" trick <see cref="Node509Program"/>'s
/// own <c>'neg</c>/<c>'not</c> use (there with <c>begin</c>/backward branches; here with <c>ahead</c>/
/// forward branches -- the same underlying stack-reordering idea applied to the other branch direction).
/// The FIRST <c>then</c> that follows now pops M2 (back on top after the swap) and resolves IT at the
/// current position -- landing exactly at the start of the "parameter on stack" form, matching its own
/// comment ("1110_00??_????_????"). Execution then falls into <c>y/pop</c> (fetching the stack-supplied
/// operand). The SECOND <c>then</c> immediately after pops M3 -- the <c>ahead</c>'s own still-open
/// marker, dormant since the swap -- and resolves it right there, immediately AFTER <c>y/pop</c> and
/// before the shared tail. So at RUNTIME: the "constant in next word" path executes <c>y/next</c>, then
/// its own <c>ahead</c> jumps forward UNCONDITIONALLY, skipping over <c>y/pop</c> entirely, landing on
/// the exact same shared tail (<c>y/r@ ex 0xffff and y/r! ;</c>) the "parameter on stack" path reaches by
/// simply falling through <c>y/pop</c> -- both forms converge on one shared body rather than duplicating
/// it, exactly the same convergence goal node 509's own fall-through idioms serve, achieved here with a
/// different compile-time mechanism (<c>ahead</c>/forward branches, not <c>begin</c>/backward ones)
/// because the two paths here are alternative ENTRY points into shared code rather than one body falling
/// through into the next.
///
/// <b>The shared tail, <c>y/r@ ex 0xffff and y/r! ;</c>.</b> <c>y/r@</c> reads node 407's own shared
/// register (holding the OTHER operand, "x", already relayed there before y/main was entered); <c>ex</c>
/// is GA144's native "execute" opcode -- per <see cref="Node407Program"/>'s own remarks ("the sequence
/// 'ex ;' will call 'lcall and 'ljmp because their address is already in R"), this behaves as a genuine
/// subroutine CALL to whatever address is already sitting in register R (loaded there by the earlier
/// <c>@b &gt;r @b</c> receive at the top of <c>y/main</c> -- see the class remarks above on the
/// 2026-09-05 fix to this exact sequence -- the same "address relayed over the port, then
/// ex" idiom nodes 407/508/509 already use for their own dispatch) -- so <c>ex</c> jumps into whichever
/// of the twelve named binary ops below this CVM opcode's own tag selected, passing the two operands (x
/// from <c>y/r@</c>, y from the stack) exactly as each op's own stack comment declares (<c>( xy-z)</c>);
/// the op's own trailing <c>;</c> returns back to right after <c>ex</c>, where the 18-bit result is
/// masked to 16 bits (<c>0xffff and</c>) and written back to the same shared register (<c>y/r!</c>) for
/// the relay chain to pick up.
///
/// <b>The twelve named binary ops -- EIGHT repoint pre-existing, previously-orphaned CVM ALU mnemonics;
/// FOUR are genuinely new.</b> Per "only update existing opcodes where possible": <c>'add</c>/<c>'sub</c>/
/// <c>'and</c>/<c>'xor</c>/<c>'or</c>/<c>'usl</c>/<c>'ssr</c>/<c>'usr</c> match node 507's own old,
/// permanently-orphaned CVM1 ALU-op family (<see cref="CvmInstructionSet.AddMnemonic"/> etc.) exactly by
/// name, so they REPOINT those existing mnemonics to node 406's own live compile rather than adding
/// duplicates (see <see cref="Services.CvmAssemblyLanguage"/>'s own <c>NodeSymbolByMnemonic</c>
/// wiring). <c>'rsb</c>/<c>'rsl</c>/<c>'rsr</c>/<c>'rur</c> ("reverse subtract"/"reverse shift left"/
/// "reverse shift right"/"reverse unsigned shift right") have no existing same-named mnemonic anywhere
/// in the table and are genuinely NEW entries (<see cref="CvmInstructionSet.ReverseSubtractMnemonic"/>
/// etc.).
///
/// <b>The "i suffix" convention -- per Stefan's own explicit follow-up ("for opcode with constant
/// parameter, add a i to the mnemonic. so 'add' becomes 'addi'").</b> Each of the twelve ops above exists
/// in TWO forms on this node -- the "parameter on stack" form (reached via <c>y/pop</c>, tag 0xE000) and
/// the "parameter in next word" form (reached via <c>y/next</c>, tag 0xE400) -- both resolving to the
/// SAME F18 symbol/address here, differing only in which CVM opcode tag and operand shape selects them.
/// The stack form keeps the plain mnemonic (<c>add</c>, <c>sub</c>, ...); the next-word form gets the
/// SAME name with an "i" appended (<c>addi</c>, <c>subi</c>, <c>rsbi</c>, <c>andi</c>, <c>xori</c>,
/// <c>ori</c>, <c>rsli</c>, <c>usli</c>, <c>rsri</c>, <c>ssri</c>, <c>ruri</c>, <c>usri</c>) -- twelve
/// more, genuinely new, CVM mnemonics, shaped exactly like <c>pushlit</c>/<c>lcall</c>/<c>ljmp</c>/
/// <c>ldg</c>/<c>stg</c> (<see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/>: one tagged
/// opcode word, one trailing literal operand word).
///
/// <b>The tag derivation, 0xE000/0xE400 -- straight from this source's own trailing comment.</b>
/// <c>1110_01??_????_????</c> is the "constant in next word" form (six bits fixed: <c>111001</c>, i.e.
/// 0xE400) and <c>1110_00??_????_????</c> is the "parameter on stack" form (six bits fixed:
/// <c>111000</c>, i.e. 0xE000) -- a 6-bit-tag/10-bit-address split, matching node 509's own
/// <c>Node509UnaryArithmeticTagBits</c> scheme's bit width exactly (see that constant's own remarks in
/// <see cref="Services.CvmAssemblyLanguage"/> for that precedent). Node 406's own RAM
/// is only 64 words, so this range (0xE000-0xE03F / 0xE400-0xE43F) has no LIVE collision with any other
/// wired tag (<c>Node508TagBits</c>, 0xE800, is the nearest neighbour, non-overlapping). NOTE (not this
/// toolchain's concern to resolve, just to flag): <see cref="CvmInstructionSet"/>'s own COMMENTS describe
/// an unused, purely-historical "register d" range at 0xE000-0xE7FF, associated with node 506's own
/// fully-orphaned, never-wired CVM1 register-d family -- this overlaps node 406's new tags only in
/// COMMENT TEXT, never in any live wiring (that CVM1 register-d family has no
/// <see cref="Services.CvmAssemblyLanguage.NodeSymbolByMnemonic"/> entry at all any more). This tag
/// derivation is now CONFIRMED ON REAL HARDWARE (2026-09-05) -- the tag/opcode split itself needed no
/// change; only the receive sequence feeding <c>y/main</c> did (see the class remarks above on the
/// 2026-09-05 fix).
///
/// <b>Verification.</b> Compiled standalone against this project's real <c>Compiler/F18Compiler.cs</c>,
/// importing <see cref="Node407Program"/>'s own (renamed, <c>n/</c>-prefixed) exports: 0 errors, 62/64
/// words used, entry point <c>y/main</c> at 0x0014. Every symbol resolves: <c>y/limit</c>=0x0000,
/// <c>y/r@</c>=0x0002, <c>y/r!</c>=0x0006, <c>y/pop</c>=0x0008, <c>y/push</c>=0x000C,
/// <c>y/next</c>=0x000E, <c>y/leave</c>=0x0012, <c>y/main</c>=0x0014, <c>'add</c>=0x0024,
/// <c>'sub</c>=0x0025, <c>'rsb</c>=0x0026, <c>'and</c>=0x0029, <c>'xor</c>=0x002A, <c>'or</c>=0x002B,
/// <c>'rsl</c>=0x002D, <c>'usl</c>=0x002E, <c>'rsr</c>=0x0033, <c>'ssr</c>=0x0034, <c>'rur</c>=0x0039,
/// <c>'usr</c>=0x003A. These addresses are IDENTICAL both before and after the 2026-09-05 hardware fix --
/// that fix only swapped the op order and the x/y stack placement inside <c>y/main</c>'s receive sequence,
/// it did not add, remove, or reorder any word, so nothing downstream (the assembler's tag wiring in
/// <see cref="Services.CvmAssemblyLanguage"/>, the boot stream in <see cref="CvmBootStreamBuilder"/>, or
/// the debugger's <c>StandaloneCvmNodeCoordinates</c> list) needed any change to pick up the corrected
/// source. The source now compiled here is Stefan's HARDWARE-CONFIRMED, corrected version (see the class
/// remarks above); the earlier import-name mismatch found before node 407 was corrected is unrelated and
/// already fixed -- see <see cref="Node407Program"/>'s own remarks for that.
/// </summary>
internal static class Node406Program
{
  /// <summary>The node this program is always deployed to -- CVM2's binary-arithmetic node.</summary>
  public const int Coordinate = 406;

  /// <summary>
  /// Node 406's full resident F18 source, as supplied by Stefan on 2026-09-05, unmodified. See the class
  /// remarks for the <c>y/main</c> dispatch cascade, the <c>ahead</c>/<c>[ swap ]</c>/<c>then</c>
  /// deferred-resolution mechanism, the twelve named ops (eight repointed, four new), the "i suffix"
  /// convention, and the tag derivation.
  /// </summary>
  public const string Source = """
      ( CVM2 node 406. binary arithmetic, 1110_????_????_???? )
      # 407 import
      # 0 org
      entry y/main
      # 0 /a
      # right /b
      : y/limit 0x000f and ;
      : y/r@ ( -w) A[ n/r@ ]] lit !b A[ !p ]] lit !b @b ;
      : y/r! ( w) A[ @p n/r! ]] lit !b !b ;
      : y/pop ( -w) A[ n/pop ]] lit !b A[ !p ]] lit !b @b ;
      : y/push ( w) A[ @p n/push ]] lit !b !b ;
      : y/next ( -w) A[ n/next ]] lit !b A[ !p ]] lit !b @b ;
      : y/leave A[ n/leave ; ]] lit !b
      : y/main # y/leave lit >r A[ !p 2* !p ]] lit !b @b >r @b
        -if // 1110_1???_????_????
          // more arithmetic
          r> r--- ;
        then // 1110_0???_????_????
        2* -if // 1110_01??_????_????
          // binary operator with constant in next word
          y/next ahead [ swap ]
        then // 1110_00??_????_????
        // binary operator with parameter on stack
        y/pop then y/r@ ex 0xffff and y/r! ;
      : 'add ( xy-z) + ;
      : 'sub ( xy-z) over
      : 'rsb ( xy-z) inv . + 1 . + ;
      : 'and ( xy-z) and ;
      : 'xor ( xy-z) xor ;
      : 'or ( xy-z) inv over inv and inv ;
      : 'rsl ( xy-z) over
      : 'usl ( xy-z) y/limit if for 2* unext ; then begin begin drop ;
      : 'rsr ( xy-z) over
      : 'ssr ( xy-z) 2* 2* 2/ 2/ y/limit until for 2/ unext ;
      : 'rur ( xy-z) over
      : 'usr ( xy-z) y/limit until for 2/ unext ;
      (
        1110_01??_????_???? binary operation with r and next word as parameter
        1110_00??_????_???? binary operation with r and parameter on stack
        'add add r with parameter
        'sub subtract r with parameter
        'rsb reverse subtract. subtract r from parameter
        'and bitwise and.
        'xor bitwise exclusive or.
        'or bitwise or.
        'rsl reverse unsigned shift left r by parameter bits
        'usl unsigned shift left parameter by r bits
        'rsr reverse signed shift right r by parameter bits
        'ssr signed shift right parameter by r bits
        'rur reverse unsigned shift right r by parameter bits
        'usr unsigned shift right parameter by r bits
        parameter is either a word constant or popped from the stack.
      )
      """;
}