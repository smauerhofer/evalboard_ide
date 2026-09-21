namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 305's resident F18 source. <b>REPLACED WHOLESALE, 2026-09-21, per Stefan's own FP-engine-rework
/// paste</b> (see <see cref="Node306Program"/>'s own remarks for the overall rework). This is a
/// COMPLETELY different design from everything this class previously held (both the original 2026-09-16
/// "decompose into sign/type/exponent/mantissa" draft and the simplified 2026-09-20 "decompose into
/// sign/exponent/mantissa" rewrite, see this class's own prior remarks, both now entirely superseded --
/// node 305 is no longer stage 1 of the old 17-node decompose/normalize/round pipeline at all). Node 305
/// is now the floating-point REGISTER FILE itself -- the role node 306 held from 2026-09-15 through
/// 2026-09-20, before ITS OWN second role flip moved it to being a pure instruction decoder that imports
/// this node (see <see cref="Node306Program"/>'s remarks on its own two role flips).
///
/// <b>This raises the same open architectural question already flagged when node 306 was updated, still
/// not resolved by Stefan</b>: what becomes of the OTHER 16 nodes (301-304, 401-404, 501-504, 601-604) of
/// the old 17-node pipeline, now that node 305 -- their own former stage 1 -- does a completely unrelated
/// job? Not decided here; not silently assumed either way.
///
/// <b>Layout: 8 registers + 8 constants, one 2-word (low, high) pair each</b>, per the header's own
/// "low word at address x, high word at address x+1". The 8 registers occupy words 0x00-0x0F (register
/// <c>r</c> at words <c>2r</c>/<c>2r+1</c> -- confirmed by node 306's own <c>fp/main</c>, which shifts the
/// raw 3-bit <c>f</c>/<c>g</c> register field left by one bit, i.e. multiplies by 2, before calling into
/// here, so every address this node's own words receive as <c>f</c>/<c>g</c> already IS the doubled word
/// address, not a raw 0-7 register index). <c>fr/cbase</c> (<c># 0x10 org</c>) then holds the 8 named
/// constants at words 0x10-0x1F: ln(2), 1/ln(2), pi/2, 2/pi, 1.0, 0.5, 2.0, sqrt(2) -- <c>fr/const</c>
/// selects one of these the same way <c>fr/move</c> selects a register, by adding <c>fr/cbase</c>'s own
/// address to the (already-doubled) selector before the shared <c>fr/mov</c> copy logic runs.
///
/// <b>Exports six constants, matching node 306's own comment-documented expectation exactly</b>: <c>#
/// 0 const f.add</c> through <c># 5 const f.div</c> (values 0-5) -- these are genuine <c>const</c>
/// directives here (unlike their appearance inside node 306's own header, which is only a descriptive
/// comment inside parentheses, not live code), matching node 306's own <c># 305 import</c> and its
/// <c>fp/add</c>/<c>fp/sub</c>/.../<c>fp/div</c> words that reference <c>f.add</c>.."f.div"</c> by name.
///
/// <b>Exports the seven words node 306 calls by address</b> (<c>fr/binary</c>/<c>fr/move</c>/
/// <c>fr/const</c>/<c>fr/neg</c>/<c>fr/abs</c>/<c>fr/pop</c>/<c>fr/push</c>), each following the uniform
/// "(g f)" or "(f g)"-style calling convention node 306's own <c>fp/2par</c>/<c>fp/po</c>/<c>fp/pu</c>
/// helpers use for every one of the twelve operations, even the ones (push/pop/abs/neg) that only need one
/// of the two register operands -- the unused one is simply left on the stack, presumably harmless given
/// this dialect's shallow, self-cleaning data stack; not spelled out as intentional by the source itself,
/// so noted here as an inference rather than confirmed fact.
///
/// <b>FLAGGED, not silently fixed, exactly like this mesh's other "falls through" idioms (see
/// <see cref="Node306Program"/>'s and <see cref="Node308Program"/>'s own remarks): <c>fr/move</c> ends
/// with no closing <c>;</c></b>, so it falls straight into <c>fr/mov</c>'s own body immediately below it
/// (the two together implement "copy the 2-word value at address <c>g</c> to address <c>f</c>";
/// <c>fr/const</c> reuses the same <c>fr/mov</c> tail after computing a constant-table address instead of
/// a register address).
///
/// <b>FLAGGED, not silently resolved: the boot configuration, <c># left /b</c> then <c># --l- /p</c>, with
/// no <c>entry</c> word at all.</b> This is the first node in this project's own sources to combine a
/// named-multiport literal with <c>/p</c> this way, but the underlying pieces are both independently
/// documented and confirmed against <c>F18Compiler</c>/<c>F18InstructionSet</c>: <c>--l-</c> is one of
/// DB013 4.2.7.3's 15 named multiport-call addresses (<c>F18InstructionSet.NamedMultiportCalls["--l-"] =
/// 0x175</c>, "listen for a streamed instruction word on the left port only"), and preceding it with
/// <c>#</c> leaves that resolved address on the compile-time stack instead of compiling a call, exactly as
/// <c>#</c> does for the <c>Constants</c>/<c>NamedMultiportCalls</c> tables generally; <c>/p</c> then pops
/// that value as node 305's literal initial P (mutually exclusive with, and here used instead of,
/// <c>entry &lt;word&gt;</c> -- confirmed from <c>F18Compiler.CompileInitialP</c>'s own remarks). So node
/// 305 never runs its own top-level loop: it boots straight into the chip's built-in "listen on the left
/// port and execute whatever instruction word arrives" mode, and every one of its exported words is reached
/// only by a caller compiling that word's literal address and sending it in over the port node 305 is
/// currently listening on (the same "compile the address, send it, the multiport-listening receiver
/// executes it as a call" idiom node 306's own remarks describe for reaching node 305 in the first place)
/// -- <c>fr/binary</c>'s own closing <c>left b!</c> (see below) exists to put the port register back the
/// way the chip's ongoing listen-and-fetch expects it once the call returns.
///
/// <b>FLAGGED: the header's own port-protocol comment does not obviously match the body's own port use,
/// and is reproduced exactly as given rather than reconciled.</b> The header says <c>out: ohlhl {to node
/// 205}</c> and <c>in: hl {from node 304}</c> -- "node 205" does not exist in this mesh (only 3xx-6xx
/// nodes are used anywhere in this project) and is almost certainly a typo, but for what is genuinely
/// unclear: it could mean "node 306" (node 305's actual right neighbor and the only node that imports this
/// one today), or "node 405" (node 305's actual DOWN neighbor, and the most likely home for the
/// not-yet-defined worker node <c>fr/binary</c> relays binary-operation operands to, per Stefan's own "the
/// remaining nodes are used only for binary function; they will be defined later" -- see this project's
/// own CVM instruction set remarks). Separately, <c>fr/binary</c>'s own body sets B to <c>right</c>
/// (node 306's direction, not node 304's) immediately before the "receive result" step the header calls
/// "in: hl {from node 304}" -- a direct mismatch between the header's stated source and the code's actual
/// port, reproduced as given and not silently corrected either way.
///
/// <b><c>fr/binary</c>'s own <c>down b!</c>, right after reading the three input words</b>: <c>down</c> is
/// this project's own built-in literal for the down-neighbor port (<c>F18InstructionSet.Constants["down"]
/// = 0x115</c>), consumed immediately by <c>b!</c> -- i.e. node 305 relays a binary operation's two
/// register operands DOWN (to the not-yet-defined worker node discussed above), then switches B to
/// <c>right</c> to read the single 2-word result back, before the closing <c>left b!</c> restores the
/// port register to what the ongoing "listen on left" boot mode expects. This matches Stefan's own stated
/// plan exactly: <c>fr/binary</c> is the relay point for the six binary operations (add/sub/min/max/
/// mul/div), and the actual arithmetic lives in a node this rework does not yet define.
///
/// <b>The overall FP engine "does not function well in the current state," per Stefan's own opening
/// statement introducing this rework</b> (see <see cref="Node306Program"/>'s identical note) -- this class
/// records the new source and the above derivation faithfully without independently proving correctness
/// beyond what is traced in these remarks; any remaining bug in this pasted source is Stefan's own to
/// find, not something silently patched here.
/// </summary>
internal static class Node305Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point REGISTER FILE (REWORKED role as of 2026-09-21; this coordinate previously held stage 1 of an older, and now apparently obsolete, 17-node floating-point decompose/normalize/round pipeline -- see this class's own remarks).</summary>
  public const int Coordinate = 305;

  /// <summary>
  /// Node 305's full resident F18 source. REPLACED WHOLESALE, 2026-09-21, per Stefan's own paste
  /// introducing the FP engine rework (see this class's own remarks for the full derivation of the
  /// register/constant layout, the exported symbols, and the flagged boot-configuration and
  /// port-protocol-comment items). Reproduced verbatim, including its own header comment block.
  /// </summary>
  public const string Source = """
      ( CVM2 node 305. VM 32 bit floatingpoint register node

      - contains 8 32 bit floating pointer register
      - contains 8 32 bit floating pointer constants

      low word at address x, high word at address x+1


      binary:
        out:     ohlhl      { to node 205 }
        in:      hl         { from node 304 }
      )

      # 0 const f.add
      # 1 const f.sub
      # 2 const f.min
      # 3 const f.max
      # 4 const f.mul
      # 5 const f.div

      # left /b
      # --l- /p

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
        @b @b @b down b!
        // send 2 parameter
        ( o g f ) dup a! >r >r !b
        @+ @ !b !b
        r> a! @+ @ !b !b
        // receive result
        right b! r> a! @b @b !+ !
        left b!

      ;

      : fr/move ( g f ) over a!
      : fr/mov @+ @ >r >r a! r> !+ r> ! ;
      : fr/const ( g f ) over # fr/cbase lit . + a! fr/mov ;
      : fr/push ( g f ) a! @+ @ !b !b ;
      : fr/abs ( g f ) a! @+ @ 0x7fff and ! ;
      : fr/neg ( g f ) a! @+ @ 0x8000 xor ! ;
      : fr/pop ( g f ) a! @b !+ @b ! ;
      """;
}