namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 405's resident F18 source -- CVM2's "multiword arithmetic" (carry-flag) node, reached from node
/// 406's own previously-open "1110_1???" ("more arithmetic") relay branch (<see cref="Node406Program"/>'s
/// own <c>y/main</c>, corrected 2026-09-09 to relay LEFT, <c>--l-</c>, into this node -- see that class's
/// own remarks). Imports node 406 (<c>y/r@</c>/<c>y/r!</c>/<c>y/pop</c>/<c>y/leave</c>). BRAND NEW to this
/// toolchain: added 2026-09-09 as part of the opcode/assembler-vs-node reconciliation audit against
/// Stefan's <c>workspace.yaml</c> project export -- no earlier revision of this file existed here.
///
/// Register <c>a</c> holds a single carry bit (0 or 1), per the header's own second line. <c>mw/main</c>
/// does NOT use a bit-cascade dispatch at all: it reads one word via <c>@b</c>, holds it in the native
/// return register via <c>&gt;r</c>, then falls straight to <c>ex</c> (jump to whatever is now in R) --
/// the same "tag | local address, no further bit tests" idiom node 407's own <c>'lcall</c>/<c>'ljmp</c>
/// fall-through and node 510's own extended-arithmetic tail already use. All nine tick-prefixed words
/// (<c>'tgc</c>/<c>'adc</c>/<c>'ldc</c>/<c>'sec</c>/<c>'clc</c>/<c>'stc</c>/<c>'sbc</c>/<c>'rol</c>/
/// <c>'ror</c>) are therefore <see cref="CvmInstructionSet.CvmOperandEncoding.None"/>-shaped, node-
/// resolved against a live compile, sharing node 406's own "1110_1???" tag OR'd with each op's own address
/// on this node.
/// </summary>
internal static class Node405Program
{
  /// <summary>The node this program is always deployed to -- CVM2's "multiword arithmetic" (carry-flag) node.</summary>
  public const int Coordinate = 405;

  /// <summary>
  /// Node 405's full resident F18 source, verbatim from Stefan's own <c>workspace.yaml</c> project export
  /// (the "CVM2" project's Host chip), added 2026-09-09 as part of the opcode/assembler-vs-node
  /// reconciliation audit.
  /// </summary>
  public const string Source = """
      ( CVM2 node 405. multiword arithmetic, 1110_1???_????_???? )
      ( A: carry bit 0 or 1 for multiword arithmetic )
      # 406 import
      # 0 org
      entry mw/main
      # 0 /a
      # left /b
      : mw/r@ ( -w) A[ y/r@ ]] lit !b A[ !p ]] lit !b @b ;
      : mw/r! ( w) A[ @p y/r! ]] lit !b !b ;
      : mw/pop ( -w) A[ y/pop ]] lit !b A[ !p ]] lit !b @b ;
      : mw/leave A[ y/leave ; ]] lit !b
      : mw/main  A[ drop !p ]] lit !b @b >r mw/r@ ex mw/leave ;

      : 'tgc ( ) a 1 xor a! ;
      : 'adc ( w-) mw/pop + a . +
      : mw/! ( w-) dup 0xffff mw/r! 0x10000 and
      : 'ldc ( w-) if
      : 'sec ( w-) dup xor a! @+ drop ;
      then
      : 'clc ( w-) dup xor a! ;
      : 'stc ( ) a mw/r! ;
      : 'sbc ( w-) 'tgc inv 'adc ;
      : 'rol ( w-) 2* a xor mw/! ;
      : 'ror ( w-) a if drop 0x10000 xor dup then drop dup 1 and 'stc 2/ mw/r! ;

      (
      'tgc toggle carry. invert carry.
      'ldc load carry. set carry if r != 0 else clear carry
      'clc clear carry
      'sec set carry
      'stc store carry into r
      'adc add with carry. carry,r = r + pop + carry
      'sbc subtract with carry. carry,r = pop - r + !carry
      'rol rotate left. carry, r = r << 1, carry
      'ror rotate right. r, carry = carry, r >> 1

      )
      """;
}
