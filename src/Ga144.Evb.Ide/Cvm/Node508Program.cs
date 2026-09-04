namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 508's resident F18 source -- CVM2's globals-access node, supplied verbatim by Stefan on
/// 2026-09-04: "here is node 508. it handles access to globals." This corrects the earlier placeholder
/// state of this file (Stefan, 2026-09-01: "node 508 must be ignored for now" -- a leftover from a
/// mistaken early assumption that 508, not 507, was CVM2's CPU node; see <see cref="Node507Program"/>'s
/// own remarks). It is also unrelated to CVM1's own node 508 (the "register-t/comparison" servant node
/// to CVM1's old 507 ALU, whose stale <c>.f18</c> mirror was deleted, never kept in sync, back on
/// 2026-09-01) -- this is brand new CVM2 content that happens to reuse the same coordinate.
///
/// <b>Why a separate node.</b> Same reasoning as <see cref="Node407Program"/> and
/// <see cref="Node506Program"/> (node 507's own RAM is completely full): globals-access primitives need
/// their own resident node with its own fresh 64-word budget. Verified via a standalone harness compile
/// importing <see cref="Node507Program"/>'s own exports (<c>m/pop</c>, <c>m/push</c>, <c>m/next</c>,
/// <c>m/2@</c>, <c>m/2!</c>) with a <c>F18CompilerOptions</c> shaped the same way
/// <see cref="Node407Program"/>/<see cref="Node506Program"/>'s own compiles are: 0 errors, 48/64 words
/// used, entry point <c>g/main</c> at 0x018, every symbol resolves (<c>g/r@</c> 0x0000, <c>g/r!</c>
/// 0x0002, <c>g/pop</c> 0x0004, <c>g/push</c> 0x0008, <c>g/next</c> 0x000A, <c>g/@</c> 0x000E, <c>g/!</c>
/// 0x0012, <c>g/leave</c> 0x0016, <c>g/main</c> 0x0018, <c>'ldg</c> 0x002C, <c>'stg</c> 0x002E).
///
/// <b>Reached from node 507 via the LEFT port (<c>--l-</c>), confirmed symmetrically both sides.</b>
/// This source's own header, <c>( CVM2 node 508. globals, 101?_????_????_???? )</c>, states the exact
/// same leading bit pattern as the THIRD test in <see cref="Node507Program"/>'s own <c>m/main</c>
/// dispatch cascade -- <c>2* -if // 101?_????_????_???? --l- ;</c> -- which hands off to node 508's
/// local LEFT port. This source's own <c># left /b</c> directive binds its own port B to "left" too,
/// matching that expectation exactly (not a coincidence -- <see cref="Models.KrakenConfiguration.PortAddress"/>'s
/// own geographic-adjacency table independently computes the SAME local port name, "left" (0x175), on
/// BOTH sides of the 507&lt;-&gt;508 link, the same symmetric-local-name pattern already confirmed for
/// 407&lt;-&gt;507 ("down", both sides) and 506&lt;-&gt;507 ("right", both sides)). So 508 is now a THIRD
/// sibling leaf hanging directly off 507's own dispatch, alongside 407 and 506, not a further link past
/// either of them.
///
/// <b>Imports node 507.</b> <c># 507 import</c> brings <c>m/pop</c>, <c>m/push</c>, <c>m/next</c>,
/// <c>m/2@</c>, and <c>m/2!</c> into scope by name -- the last two are node 507's own page-2 read/write
/// primitives (added 2026-09-02, per that class's own remarks), used here for the actual global
/// load/store rather than the page-1 stack access <see cref="Node506Program"/> uses via <c>m/1@</c>/
/// <c>m/1!</c>.
///
/// <b>Register/stack helpers and the shared "remote op / transmit back / return control" idiom.</b>
/// <c>g/r@</c>/<c>g/r!</c>/<c>g/pop</c>/<c>g/push</c>/<c>g/next</c>/<c>g/leave</c> are structurally
/// identical, word for word, to <see cref="Node407Program"/>'s own <c>b/r@</c>/<c>b/r!</c>/<c>b/pop</c>/
/// <c>b/push</c>/<c>b/leave</c> and <see cref="Node506Program"/>'s own <c>f/r@</c>/<c>f/r!</c>/
/// <c>f/pop</c>/<c>f/push</c> (<c>f/next</c> too, via <c>A[ m/next ]] lit !b ahead</c>): each uses the
/// <c>A[ ... ]] lit !b</c> idiom to assemble a raw instruction word, compile it as a literal, and stream
/// it out over port B (bound to "left" here). <c>g/@</c>/<c>g/!</c> are new -- global fetch/store by
/// address -- and mirror <see cref="Node506Program"/>'s own <c>f/stack@</c>/<c>f/stack!</c> shape
/// exactly, just against <c>m/2@</c>/<c>m/2!</c> (page 2, globals) instead of <c>m/1@</c>/<c>m/1!</c>
/// (page 1, stack).
///
/// <b><c>g/main</c>'s own dispatch cascade and its CVM-level opcode encoding.</b> Opens with the same
/// "prepare return address, push take over code" idiom <see cref="Node407Program"/>'s <c>b/main</c> and
/// <see cref="Node506Program"/>'s <c>f/main</c> both use (<c># g/leave lit &gt;r</c> then
/// <c>A[ 2* !p !p ]] lit !b @b @b &gt;r</c>), then tests the header's own <c>101?</c> prefix bit by bit:
/// <list type="bullet">
/// <item><c>1011_????_????_????</c> -- extended arithmetic, relayed onward via the RIGHT port
/// (<c>r---</c>): structurally the SAME "further hand-off to an as-yet-unsupplied neighbour node" idiom
/// <see cref="Node407Program"/>'s own <c>b/main</c> uses for its <c>--l-</c>/<c>r---</c>/<c>---u</c>
/// branches -- not the link back to 507, and not yet answered by anything in CVM2's mesh (no node
/// occupying 508's own right-hand neighbour position has been supplied). Left exactly as open as
/// <see cref="Node407Program"/> leaves its own equivalent branches, rather than guessed at further.</item>
/// <item><c>1010_11??_????_????</c> -- global fetch to r, a 10-bit embedded offset (<c>0x3ff and</c>
/// matches the "the offset is 10 bit" comment below exactly): <c>r&gt; 0x3ff and g/@ ;</c>.</item>
/// <item><c>1010_10??_????_????</c> -- global store from r, same 10-bit offset: <c>r&gt; 0x3ff and
/// g/! ;</c>.</item>
/// <item><c>1010_0???_????_????</c> -- falls through to <c>A[ m/next ]] lit !b A[ !p ]] lit !b @b ex ;</c>,
/// the SAME remote-fetch-and-store sequence <c>g/next</c> itself performs, immediately followed by
/// <c>ex</c> (jump to whatever address is already in R, i.e. x itself -- the same "ex reached once the
/// cascade consumes a fixed prefix" pattern <see cref="Node407Program"/>/<see cref="Node506Program"/>
/// both use for <c>'lcall</c>/<c>'ljmp</c>/<c>'leave</c>). Unlike those two nodes' own final branches
/// (which are bare <c>ex ;</c>, nothing more), this one performs an extra remote round-trip first --
/// the exact purpose of that extra step relative to <c>'ldg</c>/<c>'stg</c>'s own separate, later use of
/// <c>g/next</c> is not fully worked out here and is flagged as open rather than guessed at further.</item>
/// </list>
///
/// <b><c>'ldg</c>/<c>'stg</c>.</b> <c>: 'ldg g/next g/@ ;</c> and <c>: 'stg g/next g/! ;</c> -- per the
/// source's own trailing comment, "'ldg load global to r. offset in the next word" / "'stg store r to
/// global. offset in the next word": each first calls <c>g/next</c> (an ordinary LOCAL call here, not
/// the <c>A[...]]</c> remote-embed form -- the offset word itself lives in node 507's shared program
/// memory, fetched the same way <see cref="Node407Program"/>'s own <c>'lcall</c>/<c>'ljmp</c> fetch their
/// trailing address via <c>m/next</c>), then <c>g/@</c>/<c>g/!</c> to actually perform the global
/// access. This is the same <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/> shape
/// <c>pushlit</c>/<c>'lcall</c>/<c>'ljmp</c> already use -- a full-width offset in the CVM word
/// immediately following the opcode -- rather than the 10-bit embedded-offset shape the fetch/store
/// branches above use directly.
///
/// <b>Derived CVM-level opcode shapes -- NOT yet wired into <see cref="CvmInstructionSet"/>/
/// <see cref="Services.CvmAssemblyLanguage"/>.</b> Following exactly the same "tag | embedded/local
/// value" scheme already confirmed for node 507's own local-execute (0x8800), node 407's <c>'lcall</c>/
/// <c>'ljmp</c> (0xC000 | address-on-407), and node 506's <c>enter</c>/<c>leave</c> (0x9200 with a 9-bit
/// embedded value, 0x9000 | address-on-506): the embedded global-fetch/store forms above would encode as
/// <c>0xAC00 | (10-bit offset)</c> (fetch) and <c>0xA800 | (10-bit offset)</c> (store), and <c>'ldg</c>/
/// <c>'stg</c> would encode as <c>0xA000 | (address of 'ldg/'stg on node 508)</c> --
/// <c>0xA02C</c>/<c>0xA02E</c> against this exact compile (entry above). None of this is wired into the
/// toolchain's own opcode tables yet -- Stefan's request that introduced this source asked only that
/// node 508 be included in the CVM2 boot mesh (see <see cref="CvmBootStreamBuilder"/>), the same
/// narrower scope <see cref="Node407Program"/> and <see cref="Node506Program"/> were each first
/// introduced under before their own opcodes were wired in on a later, separate request.
///
/// <b>No known opcode-space collision.</b> Unlike <see cref="Node506Program"/>'s own accepted, deliberate
/// collision with <c>br</c>/<c>ifbr</c> (both squarely inside the <c>1001_????_????_????</c> range), the
/// <c>101?_????_????_????</c> range this node claims does not overlap <see cref="CvmInstructionSet.BranchTag"/>
/// (0x9000, <c>1001_0xxx</c>) or <see cref="CvmInstructionSet.ConditionalBranchTag"/> (0x9800,
/// <c>1001_1xxx</c>) at all -- no collision to flag here.
/// </summary>
internal static class Node508Program
{
  /// <summary>The node this program is always deployed to -- CVM2's globals-access node.</summary>
  public const int Coordinate = 508;

  /// <summary>
  /// Node 508's full resident F18 source, as supplied by Stefan on 2026-09-04. See the class remarks
  /// for the register/stack helpers, <c>g/main</c>'s dispatch cascade, its derived (but not yet wired)
  /// CVM-level opcode shapes, and the confirmed LEFT port link back to node 507.
  /// </summary>
  public const string Source = """
      ( CVM2 node 508. globals, 101?_????_????_???? )
      # 507 import
      # 0 org
      entry g/main
      # 0 /a
      # left /b
      : g/r@ ( -w) A[ over !p ]] lit !b @b ;
      : g/r! ( w) A[ @p over ]] lit !b !b ;
      : g/pop ( -w) A[ m/pop ]] lit !b A[ !p ]] lit !b @b ;
      : g/push ( w) A[ @p m/push ]] lit !b !b ;
      : g/next ( -w) A[ m/next ]] lit !b A[ !p ]] lit !b @b ;
      : g/@ ( o-) A[ @p m/2@ ]] lit !b !b A[ over ]] lit !b ;
      : g/! ( o-) A[ over @p ]] lit !b !b A[ m/2! ]] lit !b ;
      : g/leave A[ ; ]] lit !b
      : g/main # g/leave lit >r A[ 2* !p !p ]] lit !b @b @b >r
        -if // 1011_????_????_????
          // extended arithmetic
          r> r--- ;
        then // 1010_????_????_????
        2* -if // 1010_1???_????_????
          // globals
          2* -if // 1010_11??_????_????
            // global fetch to r
            r> 0x3ff and g/@ ;
          then // 1010_10??_????_????
            // global store to r
            r> 0x3ff and g/! ;
        then // 1010_0???_????_????
        A[ m/next ]] lit !b A[ !p ]] lit !b @b ex ;
      : 'ldg g/next g/@ ;
      : 'stg g/next g/! ;
      (
      opcode 1010_11??_????_???? load global into r. the offset is 10 bit.
      opcode 1010_10??_????_???? store r to global. the offset is 10 bit.
      'ldg load global to r. offset in the next word
      'stg store r to global. offset in the next word
      )
      """;
}