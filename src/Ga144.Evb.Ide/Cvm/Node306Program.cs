namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 306's resident F18 source -- CVM2's floating-point instruction decoder,
/// <c>1101_11oo_oogg_gfff</c>: decodes the 12-mnemonic floating-point opcode family (fadd/fsub/fmin/fmax/
/// fmul/fdiv/fmove/fconst/fneg/fabs/fpop/fpush, operating on node 305's register file) and dispatches into
/// node 305. Supplied by Stefan 2026-09-26 as part of the complete FP subprocessor rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES a structurally similar prior revision, with one real protocol difference.</b> A revision of
/// this same "instruction decoder" design was already stored here (dated 2026-09-21). Diffing the two
/// revealed one genuine change, reproduced verbatim below: <c>fp/binary</c>'s body was simplified from
/// routing through <c>fp/2pa</c> to injecting straight into node 305's <c>fr/binary</c> (<c>A[ fr/binary ]]
/// lit ! ! ! !</c>). A few additional trailing-whitespace-only differences were also found and are not
/// functionally significant.
///
/// Imports node 307 (for the <c>k/pop</c>/<c>k/push</c>/<c>k/leave</c>-style relay helpers <c>fp/@next</c>/
/// <c>fp/!next</c>/<c>fp/leave</c> build on) and node 305 (for <c>fr/*</c> and the <c>f.*</c> opcode
/// constants). <c>fp/main</c> decodes the opcode's <c>f</c>/<c>g</c>/<c>o</c> fields and dispatches via
/// <c>ex</c> into the <c># 0x0 org</c> jump table. <c>fp/binary</c> is shared by the six binary ops;
/// <c>fp/move</c>/<c>fp/const</c>/<c>fp/neg</c>/<c>fp/abs</c> inject directly into node 305's matching
/// word via <c>fp/2par</c>; <c>fp/pop</c>/<c>fp/push</c> route through node 305's <c>fr/pop</c>/
/// <c>fr/push</c> and this node's own instruction-stream helpers.
///
/// Added 2026-09-26.
/// </summary>
internal static class Node306Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point instruction decoder (1101_11oo_oogg_gfff), dispatching into node 305's register file. Replaced 2026-09-26.</summary>
  public const int Coordinate = 306;

  /// <summary>
  /// Node 306's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the 2026-09-21 revision previously stored here; see this class's own remarks for the one real difference found.
  /// </summary>
  public const string Source = """
      ( CVM2 node 306. VM 32 bit floatingpoint instruction decoder node, 1101_11??_????_????

      node 305 contains the floatingpoint register

      binary operation ooo:
      index operation mnemonic
      0 add fadd
      1 sub fsub
      2 min fmin
      3 max fmax
      4 mul fmul
      5 div fdiv
      6 move fmove
      7 const fconst
      8 neg fneg
      9 abs fabs
      10 pop fpop
      11 push fpush

      the assembler instruction is:
      mnemonic f g
      so e.g. "fadd 3 2" means fr[3] = fr[3] + fr[2]


      opcode 1101_11oo_oogg_gfff binary floatingpoint operation. first operand is fff. second operant is ggg. result in fff. operation in oooo

      fadd a b ; fr[a] = fr[a] + fr[b]
      fsub a b ; fr[a] = fr[a] - fr[b]
      fmin a b ; fr[a] = min{fr[a], fr[b]}
      fmax a b ; fr[a] = max{fr[a], fr[b]}
      fmul a b ; fr[a] = fr[a] * fr[b]
      fdiv a b ; fr[a] = fr[a] / fr[b]
      fmove a b ; fr[a] = fr[b]
      fconst a b ; fr[a] = const[b]
      fneg a b ; fr[a] = neg{fr[b]}
      fabs a b ; fr[a] = abs{fr[b]}
      fpop a ; ggg is not used {only 1 argument}
      fpush a ; ggg is not used {only 1 argument}

      # 0 const f.add
      # 1 const f.sub
      # 2 const f.min
      # 3 const f.max
      # 4 const f.mul
      # 5 const f.div
      const imported from node 305

      )


      # 307 import
      # 305 import

      # left /a
      # right /b
      entry fp/main

      # 0x1e org

      : fp/2pa ( a b - ) A[ @p @p ]] lit ! ! ! ;
      : fp/2par ( f g call ) >r fp/2pa r> ! ;

      : fp/@next ( - w ) A[ k/pop ]] lit !b A[ !p ]] lit !b @b ;

      : fp/po ( f g call - ) A[ fr/pop ]] lit ! fp/@next ! fp/@next ! ;



      : fp/binary ( f g o - ) A[ fr/binary ]] lit ! ! ! !

      : fp/leave A[ k/leave ; ]] lit !b
      : fp/main
        A[ !p drop ]] lit !b @b
        ( opcode 1101_11oo_oogg_gfff )
        dup 7 and 2*
        ( op f )
        over 2/ 2/ 2/
        dup 7 and 2*
        ( f op g )
        over 2/ 2/ 2/
        ( f op g op )
        0x0f and 2* >r
        ( f op g / o )
        >r drop r>
        ( f g / o )
        ex 
        fp/leave 
      ;

      # 0x0 org

      : fp/add ( f g ) f.add fp/binary ;
      : fp/sub ( f g ) f.sub fp/binary ;
      : fp/min ( f g ) f.min fp/binary ;
      : fp/max ( f g ) f.max fp/binary ;
      : fp/mul ( f g ) f.mul fp/binary ;
      : fp/div ( f g ) f.div fp/binary ;
      : fp/move ( f g ) A[ fr/move ]] lit fp/2par ;
      : fp/const ( f g ) A[ fr/const ]] lit fp/2par ;
      : fp/neg ( f g ) A[ fr/neg ]] lit fp/2par ;
      : fp/abs ( f g ) A[ fr/abs ]] lit fp/2par ;
      : fp/pop ( f g ) fp/2pa fp/po ;
      : fp/push ( f g )
        fp/2pa A[ fr/push ]] lit !
        @ @ 
        leap then
      : fp/!next ( w - ) A[ @p k/push ]] lit !b !b ;
      """;
}
