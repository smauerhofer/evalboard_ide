namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 306's resident F18 source -- CVM2's address-register node. Stefan supplied the first revision on
/// 2026-09-06 alongside the accompanying revision of node 307 ("I change node 307 and added node
/// 306... In 306 there are 4 32-bit address register to access the whole memory range."), then a
/// follow-up revision the same day ("here are the nodes without the typos you mentioned") that fixes the
/// two things flagged against that first revision -- see below. Brand new file; no earlier revision of
/// node 306 existed in this project before the first message.
///
/// <b>Four 32-bit address registers, held as word-pairs in this node's own RAM.</b> Per Stefan's own
/// narration and this source's own header comment ("<c>contains 4 32-bit address register</c>",
/// "<c>address word in x, page word in x+1</c>"), each register's 32-bit value is a pair of CVM words --
/// a 16-bit address word and a 16-bit page word -- letting a fully-loaded register address ANY word in
/// the GA144's whole memory space (across pages/nodes), not just this node's own local RAM. Register
/// SELECTION (which of the four) is a 2-bit index encoded directly into the low bits of node 306's own
/// six opcodes below (the "aa" field in each one's bit pattern) -- see <c>ar/reg</c>'s own remarks.
///
/// <b>Reached from node 307's own <c>k/main</c>, RIGHT port -- "1101_10??_????_????".</b> This source's
/// own header states that exact bit range; node 307's own dispatch cascade (see
/// <see cref="Node307Program"/>'s own remarks) relays "1101_10??" via its RIGHT port (<c>r---</c>) once
/// its own outer test ("1101_1???") is taken and its own inner test ("1101_11??" vs "1101_10??") falls
/// to the "else" side. <c># right /b</c> below binds this node's own port B as "right", the same
/// physical-adjacency convention already confirmed elsewhere in this mesh (node 306 sits at row 3,
/// column 06 -- one column over from node 307's row 3, column 07; <see cref="Models.KrakenConfiguration"/>'s
/// own SRAM master path, <c>SramMasterPath106</c>: <c>...406, 306, 206...</c>, independently places 306
/// between 406 and 206, consistent with this being a normal row-3 mesh node).
///
/// <b>Imports node 307.</b> <c># 307 import</c> brings <c>k/r@</c>/<c>k/r!</c>/<c>k/pop</c>/<c>k/push</c>/
/// <c>k/leave</c> into scope -- <c>ar/r@</c>/<c>ar/r!</c>/<c>ar/pop</c>/<c>ar/push</c>/<c>ar/leave</c>
/// here are the same one-hop-further relay idiom used at every other link in this mesh, each embedding a
/// call to the corresponding <c>k/</c> import.
///
/// <b><c>ar/reg</c> -- the register-select helper every one of the six opcodes below calls first.</b>
/// <c>: ar/reg 0x06 and a! ;</c> masks the still-shifted dispatch value down to bits 2-1 (values
/// 0x0/0x2/0x4/0x6 -- i.e. register index 0-3, each doubled) and loads the result into the F18 pointer
/// register <c>a</c>, which the six opcode bodies then use with <c>@</c>/<c>!</c>/<c>@+</c>/<c>!+</c> to
/// address the selected register's own 2-word slot in this node's local RAM (slot N at word offset
/// <c>2*N</c>, matching the doubled mask exactly). This is also why the CVM-level opcode encoding packs
/// the register index at bits 2-1 rather than bits 1-0 -- see
/// <see cref="CvmInstructionSet.LoadAddressRegisterMnemonic"/>'s own remarks for the assembler/
/// disassembler side of this.
///
/// <b><c>ar/inc</c>/<c>ar/dec</c>.</b> <c>: ar/inc 1 . + ;</c> / <c>: ar/dec -1 . + ;</c> -- read here
/// only at the level Stefan's own trailing opcode-table comments describe ("increment/decrement address
/// register"); the exact role of the bare <c>.</c> token in each body is NOT analyzed further here (it
/// does not match any token this project's other confirmed CVM2 sources use), consistent with only
/// wiring up what is independently confirmed rather than guessing at unconfirmed low-level F18 semantics.
/// Unchanged by Stefan's 2026-09-06 follow-up.
///
/// <b><c>ar/main</c>'s own dispatch cascade -- consumes three more bits within the already-fixed
/// "1101_10" prefix.</b> Reads two words via <c>@b</c> (port B, the "right" link to 307), the same
/// idiom used throughout this mesh:
/// <list type="bullet">
/// <item><c>1101_101?_????_????</c> (outer <c>-if</c> taken): further split by one bit --
/// <c>1101_1011</c> is <c>ldar</c> ("load r using address register"); else <c>1101_1010</c> is
/// <c>star</c> ("store r using address register").</item>
/// <item><c>1101_100?_????_????</c> (outer <c>-if</c> not taken): further split by one bit --
/// <c>1101_1001</c> splits AGAIN: <c>1101_1001_1???</c> is <c>inca</c> ("increment address"); else
/// <c>1101_1001_0???</c> is <c>deca</c> ("decrement address"). Else (the outer branch's own "then"
/// fall-through) <c>1101_1000</c> splits once more: <c>1101_1000_1???</c> is <c>lda</c> ("load address
/// register, address in r, page in stack"); else the final fall-through is <c>1101_1000_0???</c>, which
/// is <c>sta</c> ("store address register, address in r, page in stack") -- both this bit pattern and
/// the mnemonic now agree, per Stefan's 2026-09-06 follow-up (see below).</item>
/// </list>
///
/// <b>Fixed by Stefan's 2026-09-06 follow-up ("without the typos"), no longer flagged:</b> the first
/// revision's final branch carried an inline comment reading "<c>// lda load address register, address
/// in r, page in stack</c>" -- identical, word for word, to the comment on the genuine <c>lda</c> branch
/// just above it, even though the two branches' own CODE differs (roughly opposite data directions) and
/// the source's own TRAILING opcode-table comment block unambiguously assigned that bit pattern to
/// <c>sta</c>. This revision's inline comment now correctly reads "<c>// sta store address register,
/// address in r, page in stack</c>", agreeing with the trailing table -- see
/// <see cref="CvmInstructionSet.StoreAddressRegisterValueMnemonic"/> (tag <c>0xD800</c>), which was
/// unaffected by the inline-comment slip either way and needed no change. The recurring "1100" vs "1101"
/// leading-nibble slip on this node's own two <c>then</c> comments (<c>inca</c>/<c>deca</c>'s split and
/// the final branch) is also fixed in this revision -- both now correctly read "1101".
///
/// <b>Six new CVM mnemonics -- self-describing, straight from Stefan's own trailing bit-pattern table,
/// not tick-prefixed in the source at all.</b> Unlike every earlier tagged mnemonic in this project
/// (which all begin with a literal <c>'</c> in their own F18 source, per Stefan's own naming rule), NONE
/// of <c>ldar</c>/<c>star</c>/<c>inca</c>/<c>deca</c>/<c>lda</c>/<c>sta</c> appears as a tick-prefixed
/// word anywhere in this source -- Stefan instead named and bit-pattern-specified all six directly in
/// the trailing comment block ("<c>opcode ldar 1101_1011_????_?aa0 load r from address in address
/// register</c>", etc.). Given that explicit, unambiguous naming, these are wired into
/// <see cref="CvmInstructionSet"/> as self-describing <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/>
/// mnemonics (a fixed tag plus a 2-bit register-index operand), the SAME shape node 606's own eight
/// frame-pointer ops use, needing no live compile/node resolution at all -- see
/// <see cref="CvmInstructionSet.LoadAddressRegisterMnemonic"/>'s own remarks for the full derivation,
/// INCLUDING a flagged collision with <c>slit</c>'s own already-active tag range (0xD000-0xDFFF fully
/// contains this family's own 0xD800-0xDBFF). That collision is unaffected by this revision.
///
/// <b>Not verified against a live compile.</b> No compiler is available in this environment to confirm
/// <c>ar/main</c>'s own body actually compiles as pasted (particularly given the unanalyzed <c>.</c>
/// token in <c>ar/inc</c>/<c>ar/dec</c> and the <c>until</c>/<c>-until</c> loop forms those two bodies
/// use) -- reproduced verbatim, not adjusted to compile.
/// </summary>
internal static class Node306Program
{
  /// <summary>The node this program is always deployed to -- CVM2's 4x 32-bit address-register node.</summary>
  public const int Coordinate = 306;

  /// <summary>
  /// Node 306's full resident F18 source, as supplied by Stefan on 2026-09-06, in its second revision
  /// ("here are the nodes without the typos you mentioned"). See the class remarks for what this
  /// revision fixed (the duplicated "lda" inline comment on the <c>sta</c> branch, the "1100"/"1101"
  /// nibble slips) and what remains unanalyzed (the <c>ar/inc</c>/<c>ar/dec</c> bodies' bare <c>.</c>
  /// token).
  /// <b>SYNCED, 2026-09-08.</b> The source below was re-synced verbatim to Stefan's current live
  /// project source for node 306 (from his own uploaded <c>workspace.yaml</c>, used to bisect the
  /// <see cref="CvmBootStreamBuilder"/> node 306/307 load-order bug). This supersedes whatever the
  /// remarks above describe -- those record an EARLIER revision's shape (word counts, addresses,
  /// port bindings, exact wording) and have NOT been re-verified against this content. Treat any
  /// specific claim above (a compiled address, a port name, a verification result) as possibly
  /// stale until re-confirmed against a fresh compile.
  ///
  /// </summary>
  public const string Source = """
      ( CVM2 node 306. VM ternary main, 1101_10??_????_???? )
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
      : ar/main # ar/leave lit >r A[ 2* !p !p ]] lit !b @b @b >r

        -if // 1101_101?_????_????

          2* -if // 1101_1011_????_????
            // ldar load r using address register
            r> ar/reg A[ k/@ ]] lit !b @+ !b @ !b ;
          then // 1101_1010_????_????
          // star store r using address register
          r> ar/reg A[ k/! ]] lit !b @+ !b @ !b ;

        then // 1101_100?_????_????
        2* -if // 1101_1001_????_????
          2* -if // 1101_1001_1???_????
            // inca increment address
            r> ar/reg @ ar/inc !+ 0x10000 and # ar/leave until @ ar/inc ! ;

          then // 1100_1001_0???_????
          // deca decrement address
          r> ar/reg @ ar/dec !+ # ar/leave -until @ ar/dec ! ;
        then
        2* -if // 1101_1000_1???_????
          // lda load address register, address in r, page in stack
          r> ar/reg ar/r@ !+ ar/pop ! ;

        then // 1100_1000_0???_????
        // lda load address register, address in r, page in stack
        r> ar/reg @+ ar/r! @ ar/push ;

      (
      opcode ldar 1101_1011_????_?aa0 load r from address in address register
      opcode star 1101_1010_????_?aa0 store r to address in address register
      opcode inca 1101_1001_1???_?aa0 increment address register
      opcode deca 1101_1001_0???_?aa0 decrement address register
      opcode lda  1101_1000_1???_?aa0 load address register, address in r, page in stack
      opcode sta  1101_1000_0???_?aa0 store address register, address in r, page in stack
      )
      """;
}