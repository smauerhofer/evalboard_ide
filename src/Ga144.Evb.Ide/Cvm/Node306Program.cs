namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 306's resident F18 source -- the CVM_pipeline's 16-bit unary and binary register operations. REPLACED
/// 2026-10-04 with Stefan's delivered source (the earlier content was the 2026-09-30 empty "stub of the VM"). The
/// instruction table rows <c>1100|wwww|wwww|xxxx</c> (unary 16-bit operation) and <c>1101|wwww|yyyy|xxxx</c> (binary
/// 16-bit operation) execute a word of this node: <c>w</c> is the word's address HERE (8 bits for unary, 4 bits for
/// binary -- so 'or 'and 'xor 'sub 'add 'packbytes must stay at addresses 0..15), <c>x</c>/<c>y</c> are the register
/// operands. See <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet.PackBytesMnemonic"/> for the mnemonics and the
/// assembler syntax (<c>inc r3</c>, <c>add r1 r2</c>). This file is a reference copy: the toolchain compiles whatever
/// source is saved for node 306 in the project.
/// </summary>
internal static class Node306Program
{
  /// <summary>The node this program is always deployed to -- CVM2's 16-bit unary/binary operation node.</summary>
  public const int Coordinate = 306;

  /// <summary>Node 306's full resident F18 source, verbatim from Stefan's 2026-10-04 delivery.</summary>
  public const string Source = """
      ( CVM_pipeline node 306.

      16-bit unary & binary operation.

      )

      // entry x2/main
      # --l- /p
      # up /b
      # right /a

      # 0 org

      : 'or ( w1 w2 - w' ) >r inv r> inv and inv ;
      : 'and ( w1 w2 - w' ) and ;
      : 'xor ( w1 w2 - w' ) xor ;
      : 'sub ( w1 w2 - w' ) 'neg
      : 'add ( w1 w2 - w' ) + ;
      : 'packbytes ( lo hi - w )
        >r 'lowbyte r>
      : x2/pack ( lo8 hi - w )
        2* 2* 2* 2* 2* 2* 2* 2*
        xor
      ;

      : 'div2 ( w - w' )
        2* 2* 2/ 2/ 2/
      ;


      : 'inv ( w - w' )
        inv
      ;

      : 'neg ( w - w' )
        inv
        'inc
      ;

      : 'inc ( w - w' )
        1 'add
      ;

      : 'dec ( w - w' )
        -1 'add
      ;

      : 'mul2 ( w - w' )
        2*
      ;

      : 'udiv2 ( w - w' )
        2/
      ;

      : 'mask15 ( w - w ) // removes bit 15
        0x7fff and
      ;

      : 'inv15 ( w - w ) // inverts bit 15
        0x8000 xor
      ;

      : 'abs ( w - magnitude )
        dup 2* 2* . -if
          drop 'neg ;
        then
        drop
      ;

      : 'bool ( w - f ) // normalize to 0 or 1
        if
          drop 1 ;
        then
      ;

      : 'clearlow ( w - w' ) // clear lowest set bit
        dup -1 . + and
      ;

      : 'lowbit ( w - mask ) // isolate lowest set bit
        dup inv 1 . + and
      ;

      : 'bitcount ( w - 0 cnt )
        dup >r dup dup xor a! . ahead
        begin
          @+ drop
          r> and dup >r 
          [ swap ]
          then
        next
        a
        right a!
      ;

      : 'highbyte ( w - byte )
        2/ 2/ 2/ 2/ 2/ 2/ 2/ 2/
      : 'lowbyte ( w - byte )
        0xff and
      ;

      : 'byteswap ( w - w' )
        dup >r 'highbyte r> x2/pack
      ;


      : x2/pass4 leap
      : x2/pass2 then leap
      : x2/pass1 then @b ! ;

      : x2/binary16 ( addr - )
        >r @b x2/operand ;

      : x2/unary16 ( addr - )
        >r
      : x2/operand
        @b
        ex
        0xffff and
        !
      ;
      """;
}