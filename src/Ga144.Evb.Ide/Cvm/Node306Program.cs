namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 306's resident F18 source. <b>REWORKED, 2026-09-21, per Stefan directly: "i want to to a rework
/// of the FP engine. it does not function well in the current state."</b> This replaces the ENTIRE
/// 2026-09-15/16/17/20 design (a self-contained floating-point register file with a three-way tag
/// dispatch -- BINARY/CONSTANT-lookup/UNARY, see this class's OLD remarks, now superseded) with a new
/// split: node 306 is now a pure INSTRUCTION DECODER, and the floating-point register file itself moves
/// to node 305 (<see cref="Node305Program"/>, now updated to its own new source too -- see its own
/// remarks). This is the SECOND time this coordinate's role has flipped -- the first was the 2026-09-15
/// 306/308 swap (see this class's own remarks on that event, still accurate history, just for the role
/// BEFORE this one).
///
/// <b>Unified opcode, replacing the old three separate tags.</b> Per Stefan's own new header comment and
/// trailing opcode spec ("opcode 1101_11oo_oogg_gfff binary floatingpoint operation. first operand is
/// fff. second operant is ggg. result in fff. operation in oooo"), EVERY operation now shares one fixed
/// tag (bits 15-10, "110111", 0xDC00) and a single WIDER 4-bit operation field <c>oooo</c> (bits 9-6,
/// not the old 3-bit <c>ooo</c>) selecting which of twelve operations runs, with <c>ggg</c> (bits 5-3)
/// and <c>fff</c> (bits 2-0) keeping their old bit positions as the second and first/result register
/// operands respectively:
/// <list type="bullet">
/// <item>0 add (<c>fadd</c>), 1 sub (<c>fsub</c>), 2 min (<c>fmin</c>), 3 max (<c>fmax</c>), 4 mul
/// (<c>fmul</c>), 5 div (<c>fdiv</c>) -- the original six binary ops, unchanged in meaning.</item>
/// <item>6 move (<c>fmove</c>), 7 const (<c>fconst</c>), 8 neg (<c>fneg</c>), 9 abs (<c>fabs</c>) --
/// genuinely NEW operations this rework adds; node 306 dispatches each to node 305's own
/// <c>fr/move</c>/<c>fr/const</c>/<c>fr/neg</c>/<c>fr/abs</c> via the <c>fp/2par</c> helper. Notably,
/// <c>fconst</c> REPLACES the old, separately-tagged four-mnemonic constant-lookup family
/// (<c>fln2</c>/<c>filn2</c>/<c>fpi2</c>/<c>f2pi</c>, see this class's own OLD remarks) with a single
/// generic operation -- those four named mnemonics have NO replacement; how a caller now selects WHICH
/// constant is not spelled out in the new source and is not guessed at here.</item>
/// <item>10 pop (<c>fpop</c>), 11 push (<c>fpush</c>) -- same names and same stack-transfer purpose as the
/// old unary-tag <c>'fpop</c>/<c>'fpush</c>, dispatched via <c>fp/po</c>/<c>fp/pu</c>, which round-trip
/// through node 307's own <c>k/pop</c>/<c>k/push</c>, rather than the old node-resolved,
/// live-compiled-address scheme. <b>CORRECTED, 2026-09-21, per Stefan directly ("fpush and fpop only have
/// 1 parameter, the other opcodes have 2 parameter"): unlike the other ten, these two take only ONE
/// assembler argument</b> ("fpop f"/"fpush f", not "fpop f g") -- <c>ggg</c> is simply never used for
/// these two (Stefan's own header, added the same correction: "fpop a ; ggg is not used {only 1
/// argument}"). Node 306's own F18 body below is unaffected by this -- <c>fp/main</c> still decodes a
/// full <c>(f g)</c> pair out of every opcode word unconditionally, regardless of which operation it is,
/// so <c>fp/po</c>/<c>fp/pu</c> still receive a <c>g</c> value (always 0 for these two, since the CVM
/// encoder never writes anything into <c>ggg</c> for them); the one-argument restriction is purely a
/// CVM-assembler-level fact, see
/// <c>Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointPopMnemonic</c>/<c>FloatingPointPushMnemonic</c>'s
/// own remarks for where it is actually enforced.</item>
/// </list>
/// Assembler syntax is "mnemonic f g" for TEN of the twelve (all but <c>fpop</c>/<c>fpush</c>, see
/// above), per Stefan verbatim ("fadd 3 2" means <c>fr[3] = fr[3] + fr[2]</c>) -- Stefan's own header now
/// spells out all twelve as worked examples too (<c>fadd a b ; fr[a] = fr[a] + fr[b]</c> etc.), matching
/// this file's own <c>( f g )</c> stack comments on every one of the twelve word definitions at the
/// bottom (including <c>fp/pop</c>/<c>fp/push</c> themselves, which keep that comment even though the CVM
/// mnemonic feeding them now supplies only one real argument -- the F18 body's own calling convention is
/// unchanged, only the CVM assembler's argument count is). See
/// <c>Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointAddMnemonic</c>'s own remarks for the full CVM
/// mnemonic/tag wiring (fully self-describing: <c>CvmOperandEncoding.EmbeddedUnsignedValuePair</c> for
/// ten of the twelve, plain <c>CvmOperandEncoding.EmbeddedUnsignedValue</c> for <c>fpop</c>/<c>fpush</c>,
/// needing no live node compile to resolve any of the twelve either way -- unlike the old
/// <c>'fpop</c>/<c>'fpush</c>/constant-lookup family, which did).
///
/// <b>Node 306 imports both node 307 (unchanged, the CVM relay) and node 305 (NEW import, the register
/// file)</b> -- <c>fp/main</c> reads the raw opcode word, extracts <c>f</c>/<c>op</c>/<c>g</c>, and uses
/// <c>ex</c> to tail-jump into whichever of node 305's exported words the operation selects
/// (<c>fr/binary</c> for the six binary ops via <c>fp/binary</c>; <c>fr/move</c>/<c>fr/const</c>/
/// <c>fr/neg</c>/<c>fr/abs</c> directly via <c>fp/2par</c>; <c>fr/pop</c>/<c>fr/push</c> via
/// <c>fp/po</c>/<c>fp/pu</c>, which additionally round-trip a 32-bit value to/from the CVM stack through
/// node 307's own <c>k/pop</c>/<c>k/push</c>). <c>fp/2res</c>/<c>fp/2pa</c>/<c>fp/2par</c>/<c>fp/pop2</c>/
/// <c>fp/push2</c> are small stack-shuffling helpers around the <c>A[ ... ]] lit</c> ("compile the
/// literal address of a word") idiom this mesh uses throughout to call another node's own exported words
/// by address.
///
/// <b>FLAGGED, not silently resolved or invented, exactly like the prior revision's own flagged
/// <c>leap</c>/<c>then</c> shape (see this class's own OLD remarks, and <see cref="Node308Program"/>'s):
/// <c>fp/pop2</c> and <c>fp/push2</c> both end in <c>leap then</c> with no closing <c>;</c></b> (so, per
/// this mesh's own "falls through" idiom, <c>fp/pop2</c> flows into <c>fp/@next</c>, and <c>fp/push2</c>
/// into <c>fp/!next</c>). <c>leap</c> remains an F18 primitive this project has not otherwise encountered
/// and cannot explain from context; reproduced completely verbatim.
///
/// <b>The overall FP engine "does not function well in the current state," per Stefan's own opening
/// statement introducing this rework</b> -- this class records the new source and its own opcode
/// derivation faithfully, but does not independently verify correctness of the F18 body beyond that; any
/// remaining bug in this pasted source is Stefan's own to find, not something silently patched here.
/// </summary>
internal static class Node306Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point INSTRUCTION DECODER (REWORKED role as of 2026-09-21; this coordinate previously held the floating-point REGISTER FILE itself, 2026-09-15 through 2026-09-20 -- see this class's own remarks). Node 305 now holds the register file instead.</summary>
  public const int Coordinate = 306;

  /// <summary>
  /// Node 306's full resident F18 source. REPLACED WHOLESALE, 2026-09-21, per Stefan's own paste
  /// introducing the FP engine rework, then its header comment block REPLACED AGAIN the same day when
  /// Stefan corrected fpop/fpush to one-argument mnemonics and added the full twelve-line worked-example
  /// table (the F18 code below the header is byte-for-byte identical between the two pastes). See this
  /// class's own remarks for the full derivation of the unified <c>1101_11oo_oogg_gfff</c> opcode and the
  /// register-file move to node 305. Reproduced verbatim, including its own header comment block.
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

      # 0x18 org

      # left /a
      # right /b
      entry fp/main

      : fp/2res ( - a b ) A[ !p !p ]] lit ! @ @ ;

      : fp/2pa ( a b - ) A[ @p @p ]] lit ! ! ! ;
      : fp/2par ( f g call ) >r fp/2pa r> ! ;

      : fp/pop2 ( - l h )
        leap then
      : fp/@next ( - w ) A[ k/pop ]] lit !b A[ !p ]] lit !b @b ;

      : fp/pu ( f g call - )
        >r
        fp/2par fp/2res
      : fp/push2 ( h l - )
         leap then
      : fp/!next ( w - ) A[ @p k/push ]] lit !b !b ;


      : fp/po ( f g call - )
        >r
        fp/2pa fp/pop2 r> fp/2par ;

      : fp/binary ( f g o - ) A[ fr/binary ]] lit !b !b !b

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
      : fp/pop ( f g ) A[ fr/pop ]] lit fp/po ;
      : fp/push ( f g ) A[ fr/push ]] lit fp/pu ;
      """;
}