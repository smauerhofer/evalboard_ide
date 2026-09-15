namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 308's resident F18 source -- CVM2's 32-bit address-register node, reached from node 307's own
/// LEFT port ("1101_10??_????_????", <c># left /b</c> on this node's own side). <b>RENUMBERED
/// 2026-09-15</b> from what this project had been calling "node 306" -- Stefan: "node 306 and 308 have
/// swapped roles," re-pasting this exact address-register source (byte-for-byte the same six ops --
/// <c>arinc</c>/<c>ardec</c>/<c>arld</c>/<c>arst</c>/<c>lda</c>/<c>sta</c>, plus two brand new ones,
/// <c>arinc2</c>/<c>ardec2</c>) under the "node 308" header and the LEFT port binding, alongside node
/// 307's own dispatch cascade swapping which bit pattern relays which direction (see
/// <see cref="Node307Program"/>'s own "RIGHT/LEFT SWAPPED" remarks). Everything this project's own
/// history had documented about "node 306" the address-register node up through 2026-09-11 (see
/// `claude/cvm-node306-307-address-registers.md`) now describes THIS class instead -- physical node 308,
/// not 306. Node 306 itself now names an unrelated, brand-new floating-point register node (see
/// <see cref="Node306Program"/>, a full rewrite of that class).
///
/// <b>Displaces this node's own PRIOR role.</b> Before this message, physical node 308 was documented as
/// CVM2's "VM 32 arithmetic" node (<c>dpop</c>/<c>dpush</c>/<c>dinc</c>/<c>ddec</c>/<c>dadd</c>/<c>dor</c>,
/// added 2026-09-09 from Stefan's own <c>workspace.yaml</c> export). Stefan's new source for node 308
/// contains NONE of that -- it is the address-register mesh, not the "d" register family.
///
/// <b>REMOVED OUTRIGHT, 2026-09-15 (same day), per Stefan's own direct instruction: "remove the old
/// dpop/dpush/dinc/ddec/dadd/dor family completely."</b> This resolves the orphaning flag this remark
/// used to carry (whether the family still existed under a different coordinate, or was never real) --
/// it doesn't matter now, since Stefan asked for it gone rather than relocated. Their mnemonic
/// constants, tag/field-layout constants, <c>Instructions</c> rows, and
/// <c>CvmAssemblyLanguage.NodeSymbolByMnemonic</c>/<c>NodeResolvedEmbeddedValueFieldLayoutByMnemonic</c>
/// wiring are all deleted -- see <c>Ga144.Cvm.Toolchain.CvmInstructionSet</c>'s own removal note above
/// <c>FloatingPointFunctionFieldBitMask</c> for the full accounting. Unlike the 2026-09-09 CVM1-opcode
/// purge (which keeps a retired Id permanently unused but on record), this was a clean removal at
/// Stefan's own request; Ids 123-128 are still never reused, but nothing describes what they used to be
/// beyond that file's own removal note and this project's own history in
/// `claude/cvm-node306-307-address-registers.md`.
///
/// <b>Register count, org, and today's registers-depend-on-org history.</b> Per
/// `claude/cvm-node306-307-address-registers.md`'s own 2026-09-11 addendum, capacity is
/// <c>floor(org / 2)</c>, capped at 8 by <c>ar/main</c>'s own 3-bit register-select field. This revision's
/// own <c># 0x0c org</c> (12) gives <c>floor(12/2) = 6</c> -- which, for the first time, actually AGREES
/// with the pasted header's own "contains 6 32-bit address register" and the opcode table's own "0..5"
/// (the immediately prior revision, at <c># 0x0e org</c> = 14, disagreed with its own "6"/"0..5"
/// comments, which called for 7 -- see this project's own history for that still-unresolved flag).
///
/// <b>Two new ops, <c>arinc2</c>/<c>ardec2</c> -- "I gave up the 7th register for 2 new opcodes."</b>
/// Stefan traded the 7th register slot (the extra capacity <c># 0x0e org</c> would have given) for two
/// new opcodes instead, landing on <c># 0x0c org</c> (6 registers) plus <c>arinc2</c>/<c>ardec2</c>
/// (increment/decrement a register by 2 instead of 1, per the trailing opcode table). <b>FLAGGED, not
/// silently resolved or invented:</b> both new words are defined using two tokens, <c>leap</c> and
/// <c>then</c>, that do not appear anywhere else in this project's own F18 vocabulary (not a real F18
/// primitive this project has encountered before, not previously defined on any node, and not imported
/// from node 307) -- <c>: 'arinc2 leap</c> (no closing <c>;</c>, so per this source's own established
/// "falls through to the next word" idiom it flows directly into <c>'arinc</c>'s own body) followed by
/// <c>: 'arinc then @ ar/inc !+ ...</c>, where <c>then</c> appears with no preceding <c>-if</c>/<c>-until</c>
/// opener anywhere in scope. The exact same shape recurs on the new floating-point node's own
/// <c>'fpop</c>/<c>'fpush</c> (see <see cref="Node306Program"/>'s own remarks), which suggests it is a
/// deliberate, real construct in Stefan's own toolchain rather than a one-off typo -- but its meaning is
/// not established from context here.
///
/// <b>Wired into the CVM assembler anyway, per the mnemonic's own encoding shape, not its F18 body.</b>
/// <c>arinc2</c>/<c>ardec2</c> share <c>arinc</c>/<c>ardec</c>'s own tag (<c>0xD800</c>) and register-field
/// layout exactly (<c>CvmOperandEncoding.NodeResolvedEmbeddedValue</c>, same 6-bit function-field/3-bit
/// register-field split) -- assembling/disassembling a CVM opcode word never needs to know what an F18
/// body actually does, only where its own compiled address ends up, so the <c>leap</c>/<c>then</c>
/// uncertainty above does not block wiring these two in mechanically. If <c>leap</c> turns out not to be
/// a real word in Stefan's own toolchain, node 308's own live compile will fail outright, and
/// <see cref="Ga144.Evb.Ide.Services.CvmAssemblyLanguage.DiagnoseUnresolvedWiredMnemonic"/>'s own
/// mechanism will correctly report "node 308 did not compile this run" for EVERY one of node 308's eight
/// ops (not just these two) the next time any of them is assembled -- an accurate, loud diagnostic, not a
/// silent wrong answer. See <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet.ArithmeticIncrementAddressRegisterByTwoMnemonic"/>'s
/// own remarks for the C# side of this wiring.
/// </summary>
internal static class Node308Program
{
  /// <summary>The node this program is always deployed to -- CVM2's address-register node (register count is <c>org</c>-dependent; RENUMBERED here from 306, see this class's own remarks).</summary>
  public const int Coordinate = 308;

  /// <summary>
  /// Node 308's full resident F18 source, verbatim from Stefan's 2026-09-15 paste ("node 306 and 308
  /// have swapped roles") -- identical to the address-register source this project had previously kept
  /// as <c>Node306Program.Source</c> (2026-09-11, <c># 0x0e org</c> revision), except: the header comment
  /// is unchanged ("VM 32 bit addres pointer" / "contains 6 32-bit address register" -- both spelled
  /// exactly as Stefan wrote them, including "addres"), <c># 0x0c org</c> replaces <c># 0x0e org</c>, and
  /// <c># left /b</c> replaces <c># right /b</c>; two new ops, <c>'arinc2</c>/<c>'ardec2</c>, are added
  /// right before <c>'arinc</c>/<c>'ardec</c> -- see this class's own remarks for why their bodies
  /// (<c>leap</c>/<c>then</c>) are flagged rather than wired in.
  /// </summary>
  public const string Source = """
      ( CVM2 node 306. VM 32 bit addres pointer, 1101_10??_????_???? )
      ( contains 6 32-bit address register )
      ( address word in x, page word in x+1 )
      # 307 import
      # 0x0c org
      entry ar/main
      # 0 /a
      # left /b

      : ar/inc 1 . + ;
      : ar/dec -1 . + ;
      // : ar/reg 0x06 and a! ;

      : ar/r@ ( -w) A[ k/r@ ]] lit !b A[ !p ]] lit !b @b ;
      : ar/r! ( w) A[ @p k/r! ]] lit !b !b ;
      : ar/pop ( -w) A[ k/pop ]] lit !b A[ !p ]] lit !b @b ;
      : ar/push ( w) A[ @p k/push ]] lit !b !b ;

      : ar/leave A[ k/leave ; ]] lit !b
      : ar/main  A[ !p drop ]] lit !b @b
        // set register in a
        dup 0x07 and 2* a!
        // call word
        2/ 2/ 2/ 0x3f and >r ex ar/leave ;

      : 'lda A[ k/@ ]] lit
      : ar/adr ( ) A[ @p @p ]] lit !b @+ !b @ !b !b ;
      : 'sta A[ k/! ]] lit ar/adr ;
      : 'arinc2 leap
      : 'arinc then @ ar/inc !+ 0x10000 and # ar/leave until @ ar/inc ! ;
      : 'ardec2 leap
      : 'ardec then @ ar/dec !+ # ar/leave -until @ ar/dec ! ;
      : 'arld ar/r@ !+ ar/pop ! ;
      : 'arst @+ ar/r! @ ar/push ;


      (
      opcode lda   1101_10??_????_?aaa load r from address in address register 0..5. register encoded in aaa
      opcode sta   1101_10??_????_?aaa store r to address in address register 0..5. register encoded in aaa
      opcode arinc 1101_10??_????_?aaa increment address register 0..5. register encoded in aaa
      opcode ardec 1101_10??_????_?aaa decrement address register 0..5. register encoded in aaa
      opcode arinc2 1101_10??_????_?aaa increment address register 0..5 by 2. register encoded in aaa
      opcode ardec2 1101_10??_????_?aaa decrement address register 0..5 by 2. register encoded in aaa
      opcode arld  1101_1000_1???_?aa0 load address register 0..5, address in r, page in stack. register encoded in aaa
      opcode arst  1101_1000_0???_?aa0 store address register 0..5, address in r, page in stack. register encoded in aaa
      )
      """;
}