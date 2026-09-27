namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 409's resident F18 source -- CVM2's "bit operations" node, added 2026-09-27 per Stefan's own
/// complete source paste ("add the bit operation opcodes to the CVM language"). BRAND NEW to this
/// toolchain: no earlier revision of this file existed here. Declares <c># 407 import</c>, but is actually
/// reached over its own <c># right /b</c> port from <see cref="Node408Program"/>'s own RIGHT relay --
/// CONFIRMED the same day by Stefan's own follow-up node 408 source (see the Source remarks below and
/// <see cref="Node408Program"/>'s own remarks for the full derivation).
///
/// Unlike node 405/407/506/508/511's tick-prefixed (<c>'word</c>) opcode words, this node's four wired
/// mnemonics (<see cref="CvmInstructionSet.BitClearMnemonic"/>/<see cref="CvmInstructionSet.BitSetMnemonic"/>/
/// <see cref="CvmInstructionSet.BitInvertMnemonic"/>/<see cref="CvmInstructionSet.BitCopyMnemonic"/>, i.e.
/// <c>bclr</c>/<c>bset</c>/<c>binv</c>/<c>bcopy</c>) come from Stefan's own opcode-table comment block
/// below (<c>0: clear bit[a], bclr a</c> etc.), not from this source's own colorForth-style slash names
/// (<c>bit/main</c>/<c>bit/clear</c>/<c>bit/set</c>/<c>bit/inv</c>/<c>bit/copy</c>/<c>bit/mask</c>) -- the
/// same "self-describing jump-table" naming style already used for node 306's floating-point family (see
/// <see cref="CvmInstructionSet.FloatingPointAddMnemonic"/>'s own remarks), not the tick-naming rule.
///
/// Fully self-describing (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/> for
/// <c>bclr</c>/<c>bset</c>/<c>binv</c>, <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePair"/>
/// for <c>bcopy</c>) -- see <see cref="CvmInstructionSet.BitClearMnemonic"/>'s own remarks for the full
/// bit-layout derivation (<c>1111_1bbb_baaa_a0oo</c>) and the two flagged opens repeated below against
/// this node's own source.
/// </summary>
internal static class Node409Program
{
  /// <summary>The node this program is always deployed to -- CVM2's "bit operations" node.</summary>
  public const int Coordinate = 409;

  /// <summary>
  /// Node 409's full resident F18 source, verbatim from Stefan's own paste (2026-09-27), UPDATED the same
  /// day by Stefan's own immediate follow-up paste: <c>bit/main</c>'s own <c>@ ex !</c> became
  /// <c>@b ex !b</c> -- fetching the dispatch word and storing the result through this node's own declared
  /// port B (<c># right /b</c>) rather than local memory, i.e. talking back and forth over the SAME relay
  /// link <see cref="Node408Program"/>'s own new "bitop" branch uses to reach this node (see immediately
  /// below). No other content changed between the two pastes.
  ///
  /// FLAGGED, not silently corrected: this source's own <c>bit/main</c> dispatcher ANDs the incoming word
  /// with <c>7</c> (three bits) to pick which of up to eight routines to jump to, and the header comment
  /// below lists eight opcode slots (0 through 7) -- but only 0-3 are ever reachable through the wired
  /// CVM opcode words (<see cref="CvmInstructionSet.BitOperationTag"/>'s own bit 2 is always 0 for this
  /// tag family, so the AND-with-7 dispatch index this source computes for a CVM-emitted word can only
  /// ever be 0-3 in practice); see <see cref="CvmInstructionSet.BitClearMnemonic"/>'s own remarks for the
  /// full derivation. Left exactly as Stefan wrote it -- slots 4-7 may simply be reserved for a future,
  /// differently-tagged word that sets bit 2, which is this node's own business, not this toolchain's.
  ///
  /// RESOLVED 2026-09-27, same day, by Stefan's own follow-up <see cref="Node408Program"/> source: this
  /// source declares <c># 407 import</c>, but is actually reached over node 408's own new "bitop" branch
  /// (<c>1111_1???_????_????</c>, tag 0xF800 -- EXACTLY <see cref="CvmInstructionSet.BitOperationTag"/>),
  /// which relays RIGHT onto column 9 -- this node -- confirming the adjacency-based and tag-based
  /// circumstantial case this class's own remarks previously only FLAGGED as "very much looks like." This
  /// node's own header, <c># right /b</c>, names the SAME physical link "right" too, matching this
  /// project's own established "same local name on both ends" port-naming quirk (already confirmed for
  /// 407&lt;-&gt;507 and 406&lt;-&gt;407) rather than a fresh inconsistency of its own. The declared
  /// <c># 407 import</c> itself is unaffected by any of this -- <c>bit/main</c>/<c>bit/mask</c>/
  /// <c>bit/clear</c>/<c>bit/set</c>/<c>bit/inv</c>/<c>bit/copy</c> still never reference any
  /// <c>n/</c>-prefixed name -- and is reproduced and wired exactly as given (importing from node 407 in
  /// <see cref="CvmBootStreamBuilder"/>), since nothing in the new evidence says that declaration itself is
  /// wrong, only that the physical relay path run alongside it is through node 408, not node 407 directly.
  /// See <see cref="Node408Program"/>'s own remarks for the full derivation and the exact relay code.
  /// </summary>
  public const string Source = """
      ( CVM2 node 409. bit operations, 1111_1???_????_???? )
      # 407 import
      entry bit/main
      # right /b
      # 8 org
      (
        1111_1bbb_baaa_a0oo

        bit operation o on register r

        ooo: operation, opcode
        0: clear bit[a], bclr a
        1: set bit[a], bset a
        2: toggle bit[a], binv a
        3: copy bit[a] to bit[b], bcopy a b
        4:
        5:
        6:
        7:

        aaaa: bit position
        bbbb: bit position
      )

      : bit/mask ( bitpos - mask )
        0xf and >r 1 begin zif ; then 2* end

      : bit/clear ( bitpos value - value' )
        over bit/mask
        ( value  mask - value' )
      : bit/clear1  inv and ;

      : bit/set ( bitpos value - value' )
        over bit/mask
        ( value  mask - value' )
      : bit/set1  dup >r inv and r> xor ;

      : bit/inv ( bitpos value - value' )
        over bit/mask xor ;

      : bit/copy ( bitpos value - value' )
        dup >r >r bit/mask >r a 2/ 2/ 2/ 2/ bit/mask
        ( dmask / value value smask )
        r> r> and if // set
          drop r> over
          bit/set1 ;
        then // clear
        drop r> over
        bit/clear1
      ;


      : bit/main
        dup 7 and >r
        2/ 2/ 2/ dup a! 
        @b ex !b
        bit/main ;

      # 0 org
      bit/clear ;
      bit/set ;
      bit/inv ;
      bit/copy ;
      """;
}