namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 305's resident F18 source -- CVM2's floating-point register node: 8 32-bit floating-point
/// registers plus 8 32-bit floating-point constants (ln 2, 1/ln 2, pi/2, 2/pi, 1.0, 0.5, 2.0, sqrt(2)).
/// Supplied by Stefan 2026-09-26 as part of the complete FP subprocessor rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES a structurally similar prior revision, with real protocol differences.</b> A revision of
/// this same "register file" design was already stored here (dated 2026-09-21). Diffing the two revealed
/// two genuine (not merely cosmetic) changes, both reproduced verbatim below: (1) a new
/// <c># 0x0 org</c> block zero-initializing all 16 register words, present here but absent from the prior
/// revision; (2) <c>fr/binary</c>'s low/high send order and its final double-write were changed.
///
/// <c>fr/binary</c> drives the entire downstream pipeline (nodes 205 through 105) for a binary op: it sends
/// the operation's resident-word address plus both operands' <c>l,h</c> words down to node 205 and reads
/// the packed <c>h,l</c> result back from node 304. <c>fr/const</c>/<c>fr/move</c>/<c>fr/abs</c>/
/// <c>fr/neg</c>/<c>fr/push</c>/<c>fr/pop</c> implement the remaining single/no-operand opcodes locally,
/// without involving the pipeline.
///
/// Added 2026-09-26.
/// </summary>
internal static class Node305Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point register file (8 registers + 8 constants), driving the 205-105 pipeline for binary ops. Replaced 2026-09-26.</summary>
  public const int Coordinate = 305;

  /// <summary>
  /// Node 305's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the 2026-09-21 revision previously stored here; see this class's own remarks for the two real differences found.
  /// </summary>
  public const string Source = """
      ( CVM2 node 305. VM 32 bit floatingpoint register node

      - contains 8 32 bit floating pointer register
      - contains 8 32 bit floating pointer constants

      low word at address x, high word at address x+1


      binary:
        out:     o l1 h1 l2 h2      { to node 205 }
        in:      h l                { from node 304 }
      )

      # 0 const f.add
      # 1 const f.sub
      # 2 const f.min
      # 3 const f.max
      # 4 const f.mul
      # 5 const f.div

      # left /b
      # --l- /p

      # 0x0 org
      [
        0 , 0 , 0 , 0 , 0 , 0 , 0 , 0 , 
        0 , 0 , 0 , 0 , 0 , 0 , 0 , 0 , 
      ]

      # 0x10 org

      : fr/cbase .loc
      [ // memory order lo, hi
        0x7218 , 0x3F31 , // 0: ln(2)       = 0.69314718
        0xAA3B , 0x3FB8 , // 1: 1/ln(2)     = 1.44269504
        0x0FDB , 0x3FC9 , // 2: pi/2        = 1.57079633
        0xF983 , 0x3F22 , // 3: 2/pi        = 0.63661977

        0x0000 , 0x3F80 , // 4: 1.0
        0x0000 , 0x3F00 , // 5: 0.5
        0x0000 , 0x4000 , // 6: 2.0
        0x04F3 , 0x3FB5 , // 7: sqrt(2)     = 1.41421356
      ]

      # 0x20 org

      : fr/binary
        // read input
        @b                         // o
        @b                         // g
        @b                         // f
        down b!
        // send op + 2 parameter
        ( o g f )
        dup a!                     // set f
        >r >r !b                   // o
        @+ !b @+ !b                 // lh
        r> a!                      // set g
        @+ !b @+ !b                 // lh
        // receive result
        right b!
        r> a!                      // set f
        @b
        @b
        !+
        !+                         // hl
        left b!
      ;

      : fr/const ( g f ) over # fr/cbase lit . + a! 
      : fr/mov ( g l h ) @+ @ >r >r a! r> !+ r> ! ;
      : fr/move ( g f ) over a! fr/mov ;


      : fr/abs ( g f )
        fr/move
        @ 0x7fff and !
      ;

      : fr/neg ( g f )
        fr/move
        @ 0x8000 xor !
      ;

      : fr/push ( g f ) a! @+ !b @ !b ;
      : fr/pop ( g f ) a! @b !+ @b ! ;
      """;
}
