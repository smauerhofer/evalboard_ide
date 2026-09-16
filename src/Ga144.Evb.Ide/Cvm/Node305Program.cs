namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 305's resident F18 source, verbatim from Stefan's 2026-09-16 paste -- the first of 17 new nodes
/// he is adding to this project (301-304, 401-404, 501-504, 601-604, plus this one). Per this source's
/// own header: "CVM2 node 305. VM 32 bit floatingpoint stage 1 node. unpack and pack" -- the first stage
/// of what is evidently a multi-stage 32-bit IEEE-754 floating-point pipeline (later-pasted nodes in this
/// same batch may be stage 2, 3, etc.; not guessed at here).
///
/// <b>Architecturally DIFFERENT from every node in this mesh so far: no tag-dispatch, no import.</b>
/// Every prior non-CPU node (306/307/308, 405-408, 505, 508-511, etc.) is reached by relaying a CVM word
/// down a bit-pattern dispatch tree, and imports its parent's own <c>k/pop</c>/<c>k/push</c>/<c>k/leave</c>
/// exports (via a <c># NNN import</c> directive) to read/write the shared virtual stack. Node 305's source
/// has <b>no <c># import</c> directive at all</b>, and its body never calls <c>k/pop</c>/<c>k/push</c>/
/// <c>k/leave</c> -- it only reads/writes its own ports directly (<c>@b</c>/<c>!b</c>/<c>@</c>/<c>!</c>)
/// and a register/variable named <c>up</c> that is not defined anywhere in this source. Per its own
/// header comment, it is driven purely by a port-level protocol: <c>in: ohlhl</c> (an opcode/command word,
/// then two floats' worth of high/low word pairs), <c>out: otsehltsehl</c> (an opcode word, then two
/// decomposed floats, each as type/sign/exponent/mantissa-high/mantissa-low), <c>reply: sehl</c> (a single
/// recomposed float, as sign/exponent/mantissa-high/mantissa-low). This means node 305 is NOT part of the
/// existing CVM tag-dispatch tree (507-407-307-306/308-etc.) in any way this project currently
/// understands -- which parent node (if any) talks to it, over which physical port, and what triggers
/// each of <c>fp1/decompose</c> (called twice) vs. <c>fp1/compose</c> is not stated by Stefan and is not
/// guessed at here.
///
/// <b>FLAGGED, not silently resolved: several details in this source are unclear and reproduced exactly
/// as pasted, not corrected.</b>
/// <list type="bullet">
/// <item><c>fp1/fold</c>'s own definition (<c>: fp1/fold ( lh-lhf) over over 0x7f and</c>) has no closing
/// <c>;</c> -- it flows directly into the next colon definition, <c>fp1/or</c>. This is the same
/// no-closing-semicolon "falls through into the next word" idiom already flagged (and left unexplained)
/// on node 306's own <c>'fpop</c>/<c>'fpush</c> and node 308's own <c>'arinc2</c>/<c>'ardec2</c> -- see
/// <see cref="Node306Program"/>'s and <see cref="Node308Program"/>'s own remarks.</item>
/// <item><c>fp1/or</c> is never called anywhere in this source. Its own body
/// (<c>&gt;r inv r&gt; inv and inv ;</c>) computes a bitwise OR via De Morgan's law, but nothing in
/// <c>fp1/decompose</c> or <c>fp1/compose</c> invokes it. Possibly dead code, possibly meant to be called
/// somewhere this paste omitted -- not guessed at here.</item>
/// <item>The line <c>dup 2/ 2/ 2/ 2/ 2/ 2/ 2/ 0xff and ..</c> (inside <c>fp1/decompose</c>) ends in a
/// bare <c>..</c> token. This is not a previously-seen F18 word anywhere in this project (single <c>.</c>
/// is not either). Reproduced exactly as pasted.</item>
/// <item><c>fp1/compose</c> begins with <c>up a!</c> -- <c>up</c> is not a register, port, or word
/// defined anywhere else in this source, nor previously seen anywhere else in this project. It may be a
/// global/shared variable this node expects to be defined elsewhere (imported implicitly some way this
/// project's toolchain does not currently model), or a typo -- not guessed at here.</item>
/// <item>The trailing comment block after <c>fp1/decompose</c>'s first branch reads
/// <c>0x7f and 0x80 xor ! ! ;</c> immediately followed on the SAME logical branch by
/// <c>then</c> -- reproduced exactly as indented/laid out in the paste; the branch structure (which
/// <c>if</c>/<c>then</c> pairs with which) was not independently re-verified beyond reproducing the text
/// verbatim.</item>
/// </list>
///
/// <b>NOT YET added to <see cref="CvmNodeMesh"/> or <see cref="CvmBootStreamBuilder"/>, and NOT wired
/// into <c>Ga144.Cvm.Toolchain.CvmInstructionSet</c>/<c>Services.CvmAssemblyLanguage</c>.</b> Doing either
/// requires knowing this node's place in the physical mesh (which coordinate reaches it, over which
/// port) and, for instruction wiring, an actual dispatched, named opcode -- neither of which this source
/// states. This is being held pending the rest of the 17-node batch Stefan is pasting, in case a later
/// node in the same batch supplies the missing context (e.g. a stage-2/3 node that imports from or
/// dispatches into this one).
/// </summary>
internal static class Node305Program
{
  /// <summary>The node this program is always deployed to -- CVM2's new 32-bit floating-point pipeline, stage 1 (unpack/pack), added 2026-09-16.</summary>
  public const int Coordinate = 305;

  /// <summary>
  /// Node 305's full resident F18 source, verbatim from Stefan's 2026-09-16 paste. Every quirk noted in
  /// this class's own remarks above (the missing semicolon on <c>fp1/fold</c>, the unused <c>fp1/or</c>,
  /// the bare <c>..</c> token, the undefined <c>up</c> register) is reproduced exactly as pasted, not
  /// corrected.
  /// </summary>
  public const string Source = """
      ( CVM2 node 305. VM 32 bit floatingpoint stage 1 node. unpack and pack )
      (
      receives 2 floatingpoint numbers and decomposes each into

      1. sign:
            0       => positive
            0x8000  => negative

      2. type:
            t = 0x00   finite nonzero
            t = 0x01   zero
            t = 0x02   infinity
            t = 0x04   qNaN
            t = 0x0c   sNaN       // NAN bit + signaling bit

      3. exponent:
            IEEE-754 biased exponent, 0x00 .. 0xff

      4. mantissa high:
            normal numbers have implicit leading 1 made explicit in bit 7

      5. mantissa low

        in: ohlhl
        out: otsehltsehl
        reply: sehl

      )
      # 0 org
      entry fp1/main
      # left /a
      # right /b

      : fp1/fold ( lh-lhf) over over 0x7f and
      : fp1/or ( ab-c) >r inv r> inv and inv ;

      : fp1/decompose ( )
        @b >r @b r> ( lh )

        // sign
        dup 0x8000 and >r
        ( lh )                          // R: s

        // exponent
        dup 2/ 2/ 2/ 2/ 2/ 2/ 2/ 0xff and ..
        ( lhe )

        if // exp != 0

          dup 0xff xor if // normal
            dup xor !                    // t = 0
            r> !                         // s
            !                            // e
            0x7f and 0x80 xor ! ! ;     // h l
          then

          // NaN or infinity
          drop
          ( lhe )                        // e = ff
          >r                             // R: s e
          fp1/fold

          if // NaN
            drop
            ( lh )
            dup 0x40 and 2/ 2/ 2/ 0x0c xor
            !                            // t
            r> r> ! !                    // s e

      : fp1/final ( lh-) 0x7f and ! ! ;

          then // infinity
          drop
          2 !                            // t
          r> r> ! !                     // s e
          fp1/final ;

        then // denormalized or zero

        ( lhe )                          // e = 0
        >r                               // R: s e
        fp1/fold

        if // denormalized number
          drop
          r> dup !                       // t = e = 0, retain e
          r> !                           // s
          !                              // e
          fp1/final ;
        then

        // zero
        drop
        1 !                              // t
        r> r> ! !                       // s e
        fp1/final ;

      : fp1/compose // read stehl from up and convert it to a 32-bit IEEE-754 number
        up a!
        // sign
        @
        // exponent
        @ 2* 2* 2* 2* 2* 2* 2* xor
        // mantissa high
        @ 0x7f and xor !b
        // mantissa low
        @ 0xffff and !b
      : fp1/main left a!
        @b ! // o
        fp1/decompose
        fp1/decompose
        fp1/compose ;
      """;
}
