namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 306's resident F18 source -- CVM2's 4x 32-bit address-register node, reached from node 307's
/// own RIGHT port ("1101_10??_????_????", <c># right /b</c>), holding four 32-bit registers as word
/// pairs (address word, page word) in its own local RAM, letting a fully-loaded register address any
/// word across the whole GA144 memory space, not just this node's own RAM. Imports node 307
/// (<c>k/r@</c>/<c>k/r!</c>/<c>k/pop</c>/<c>k/push</c>/<c>k/leave</c>).
///
/// <b>RE-SYNCED 2026-09-09</b> against Stefan's own <c>workspace.yaml</c> project export, as part of
/// the opcode/assembler-vs-node reconciliation audit: this node's mnemonics changed from an EARLIER,
/// self-describing <c>ldar</c>/<c>star</c>/<c>inca</c>/<c>deca</c>/<c>lda</c>/<c>sta</c> scheme (fixed
/// 4-bit-tag/2-bit-register-index words needing no live compile) to the current <c>'lda</c>/<c>'sta</c>/
/// <c>'arinc</c>/<c>'ardec</c>/<c>'arld</c>/<c>'arst</c> -- six ordinary TICK-PREFIXED words, resolved
/// like every other tagged mnemonic in this mesh (<see cref="CvmInstructionSet.CvmOperandEncoding.None"/>,
/// node-resolved against a live compile, no embedded operand of their own). <c>ar/main</c>'s own
/// dispatch now masks the incoming call byte to 7 bits (<c>dup 0x07 and 2* a!</c>, selecting one of the
/// four registers by its own low bits) before jumping to the called word's compiled address, the same
/// "call word, address in opcode" idiom node 507/508/509/etc. all use elsewhere in this mesh -- NOT the
/// old self-describing shape. Per Stefan's own trailing comment block, all six now share the flat
/// <c>1101_10??_????_????</c> range (the earlier per-op sub-splits -- <c>1101_1011</c> for <c>ldar</c>,
/// etc. -- no longer apply): <c>lda</c>/<c>sta</c> load/store r via the address register named in the
/// low opcode bits; <c>arinc</c>/<c>ardec</c> increment/decrement it; <c>arld</c>/<c>arst</c> load/store
/// the register's own 32-bit value itself (address in r, page on the data stack).
/// </summary>
internal static class Node306Program
{
  /// <summary>The node this program is always deployed to -- CVM2's 4x 32-bit address-register node.</summary>
  public const int Coordinate = 306;

  /// <summary>
  /// Node 306's full resident F18 source. <b>RE-SYNCED 2026-09-09</b> against Stefan's own
  /// <c>workspace.yaml</c> project export (the "CVM2" project's Host chip) as part of the opcode/
  /// assembler-vs-node reconciliation audit -- the PRIOR "SYNCED, 2026-09-08" copy here had itself
  /// drifted from Stefan's live source (mislabeled header comment, a differently-shaped <c>ar/main</c>
  /// prelude). This copy is verbatim from that export. The six opcodes (<c>ldar</c>/<c>star</c>/
  /// <c>inca</c>/<c>deca</c>/<c>lda</c>/<c>sta</c>) and their bit patterns, described in the class
  /// remarks above, are UNCHANGED by this re-sync -- only the body of <c>ar/main</c>'s own dispatch
  /// prelude and the source's own header line differ from the prior copy. Any other specific claim
  /// above (a compiled address, a verification result) predates this sync and should be treated as
  /// stale until re-confirmed against a fresh compile.
  /// </summary>
  public const string Source = """
      ( CVM2 node 306. VM 32 bit addres pointer, 1101_10??_????_???? )
      ( contains 4 32-bit address register )
      ( address word in x, page word in x+1 )
      # 307 import
      # 0x08 org
      entry ar/main
      # 0 /a
      # right /b

      : ar/inc 1 . + ;
      : ar/dec -1 . + ;
      : ar/reg 0x06 and a! ;

      : ar/r@ ( -w) A[ k/r@ ]] lit !b A[ !p ]] lit !b @b ;
      : ar/r! ( w) A[ @p k/r! ]] lit !b !b ;
      : ar/pop ( -w) A[ k/pop ]] lit !b A[ !p ]] lit !b @b ;
      : ar/push ( w) A[ @p k/push ]] lit !b !b ;

      : ar/leave A[ k/leave ; ]] lit !b
      : ar/main  A[ 2* !p !p ]] lit !b @b

        @b
        // set register in a
        dup 0x07 and 2* a!
        // call word
        2/ 2/ 2/ 0x3f and ex ar/leave ;

      : 'lda A[ k/@ ]] lit !b @+ !b @ !b ;
      : 'sta A[ k/! ]] lit !b @+ !b @ !b ;
      : 'arinc @ ar/inc !+ 0x10000 and # ar/leave until @ ar/inc ! ;
      : 'ardec @ ar/dec !+ # ar/leave -until @ ar/dec ! ;
      : 'arld ar/r@ !+ ar/pop ! ;
      : 'arst @+ ar/r! @ ar/push ;


      (
      opcode lda   1101_10??_????_?aaa load r from address in address register
      opcode sta   1101_10??_????_?aaa store r to address in address register
      opcode arinc 1101_10??_????_?aaa increment address register
      opcode ardec 1101_10??_????_?aaa decrement address register
      opcode arld  1101_1000_1???_?aa0 load address register, address in r, page in stack
      opcode arst  1101_1000_0???_?aa0 store address register, address in r, page in stack
      )
      """;
}