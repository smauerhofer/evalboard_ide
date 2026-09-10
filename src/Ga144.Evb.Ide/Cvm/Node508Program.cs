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
/// <b>Revised 2026-09-06: the embedded-offset dispatch grew one level deeper.</b> Stefan's follow-up
/// message that supplied this revision ("here is node 508, where some changes happened") retracted an
/// earlier, mistaken paste that actually reproduced node 408's own (unrelated) source unchanged; THIS is
/// the real, different update. The register/stack helpers (<c>g/r@</c>/<c>g/r!</c>/<c>g/pop</c>/
/// <c>g/push</c>/<c>g/next</c>/<c>g/@</c>/<c>g/!</c>/<c>g/leave</c>) and <c>'ldg</c>/<c>'stg</c>'s own
/// bodies are byte-for-byte UNCHANGED; what changed is <c>g/main</c>'s own dispatch cascade under the
/// <c>1010_1???_????_????</c> prefix -- see the cascade paragraph below for the full shape. Re-verified
/// via a standalone harness compile importing <see cref="Node507Program"/>'s own exports (now including
/// <c>m/branch</c>, newly used here -- see below): 0 errors, 62/64 words used, entry point <c>g/main</c>
/// still at 0x018 (every helper word's address is UNCHANGED: <c>g/r@</c> 0x0000, <c>g/r!</c> 0x0002,
/// <c>g/pop</c> 0x0004, <c>g/push</c> 0x0008, <c>g/next</c> 0x000A, <c>g/@</c> 0x000E, <c>g/!</c> 0x0012,
/// <c>g/leave</c> 0x0016, <c>g/main</c> 0x0018), but <c>'ldg</c>/<c>'stg</c> both moved FORWARD, from
/// 0x002C/0x002E to <c>0x003A</c>/<c>0x003C</c>, since <c>g/main</c>'s own body grew to make room for the
/// two new branch forms. Also re-verified against the full ten-node CVM2 boot mesh
/// (<see cref="CvmBootStreamBuilder.BuildDescriptors"/>/<c>BuildLoadOrder</c>/<c>BuildLoadPlan</c>): every
/// node still compiles with 0 errors and every load step still resolves, including node 509
/// (<see cref="Node509Program"/>), which imports this node by name -- unaffected, since it only uses
/// <c>g/r@</c>/<c>g/r!</c>/<c>g/pop</c>/<c>g/push</c>/<c>g/leave</c>, none of which moved. Re-verified
/// through <see cref="Services.CvmAssemblyLanguage.BuildEncodeTable"/>/<c>BuildDecodeTable</c> too:
/// <c>ldg</c>/<c>stg</c> still resolve and round-trip correctly at their new addresses (<c>0xA03A</c>/
/// <c>0xA03C</c>) -- <see cref="Services.CvmAssemblyLanguage"/> needed NO source changes at all for this,
/// since it always resolves <c>'ldg</c>/<c>'stg</c>'s address against a live compile of this class's own
/// <see cref="Source"/>, never a hardcoded number.
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
/// either of them. Unchanged by the 2026-09-06 revision.
///
/// <b>Imports node 507.</b> <c># 507 import</c> brings <c>m/pop</c>, <c>m/push</c>, <c>m/next</c>,
/// <c>m/2@</c>, and <c>m/2!</c> into scope by name -- the last two are node 507's own page-2 read/write
/// primitives (added 2026-09-02, per that class's own remarks), used here for the actual global
/// load/store rather than the page-1 stack access <see cref="Node506Program"/> uses via <c>m/1@</c>/
/// <c>m/1!</c>. The 2026-09-06 revision adds a SIXTH import to this list, purely by use (the <c># 507
/// import</c> directive itself is unchanged text) -- <c>m/branch</c>, node 507's own <c>( rso-rs) a . +
/// a! ;</c> primitive, ALREADY exported by <see cref="Node507Program"/> but previously used only by node
/// 407's own <c>'lbr</c>/<c>'clbr</c> (added the same day); this is the first time node 508 itself calls
/// it, from its own new "branch" form below.
///
/// <b>Register/stack helpers and the shared "remote op / transmit back / return control" idiom.</b>
/// <c>g/r@</c>/<c>g/r!</c>/<c>g/pop</c>/<c>g/push</c>/<c>g/next</c>/<c>g/leave</c> are structurally
/// identical, word for word, to <see cref="Node407Program"/>'s own <c>n/r@</c>/<c>n/r!</c>/<c>n/pop</c>/
/// <c>n/push</c>/<c>n/leave</c> (renamed from <c>b/</c> 2026-09-05, see that class's own remarks) and
/// <see cref="Node506Program"/>'s own <c>f/r@</c>/<c>f/r!</c>/
/// <c>f/pop</c>/<c>f/push</c> (<c>f/next</c> too, via <c>A[ m/next ]] lit !b ahead</c>): each uses the
/// <c>A[ ... ]] lit !b</c> idiom to assemble a raw instruction word, compile it as a literal, and stream
/// it out over port B (bound to "left" here). <c>g/@</c>/<c>g/!</c> are new -- global fetch/store by
/// address -- and mirror <see cref="Node506Program"/>'s own <c>f/stack@</c>/<c>f/stack!</c> shape
/// exactly, just against <c>m/2@</c>/<c>m/2!</c> (page 2, globals) instead of <c>m/1@</c>/<c>m/1!</c>
/// (page 1, stack). All of this is UNCHANGED by the 2026-09-06 revision.
///
/// <b><c>g/main</c>'s own dispatch cascade and its CVM-level opcode encoding (revised 2026-09-06).</b>
/// Opens with the same "prepare return address, push take over code" idiom <see cref="Node407Program"/>'s
/// <c>n/main</c> and <see cref="Node506Program"/>'s <c>f/main</c> both use (<c># g/leave lit &gt;r</c>
/// then <c>A[ 2* !p !p ]] lit !b @b @b &gt;r</c>), then tests the header's own <c>101?</c> prefix bit by
/// bit. The TOP-LEVEL split is unchanged:
/// <list type="bullet">
/// <item><c>1011_????_????_????</c> -- extended arithmetic, relayed onward via the RIGHT port
/// (<c>r---</c>): node 509's own unary-arithmetic node hangs directly off this port (see
/// <see cref="Node509Program"/>'s own remarks). Unchanged, no revision here.</item>
/// <item><c>1010_????_????_????</c> -- everything below this node's own local dispatch (the
/// <c>1010_0???</c> fall-through and the now-deeper <c>1010_1???</c> cascade).</item>
/// </list>
/// What's NEW is entirely inside the OLD single <c>1010_1???</c> "globals" branch, which is now split ONE
/// LEVEL DEEPER, into a "branch" group and a "globals" group, each then split once more:
/// <list type="bullet">
/// <item><c>1010_11??_????_????</c> -- the NEW branch group, itself split by one more bit:
/// <list type="bullet">
/// <item><c>1010_111?_????_????</c> -- conditional branch: <c>g/r@ if r&gt; g/leave then</c>. Per the
/// source's own trailing comment ("conditional branch to offset if r == 0"): <c>g/r@</c> remotely fetches
/// node 507's own r register; if it is NONZERO, the <c>if</c>-body runs (<c>r&gt;</c> discards the
/// pending shifted offset, then <c>g/leave</c> transmits a bare remote return and -- since <c>g/leave</c>
/// itself has no trailing <c>;</c> -- falls straight through into <c>g/main</c> again, i.e. "return
/// without branching"). If r IS zero, the <c>if</c>-body is skipped entirely and control falls through
/// <c>then</c> directly into the NEXT item below (the unconditional "branch" body) -- the same
/// "IF...THEN, false-clause always runs next" fall-through idiom already confirmed elsewhere this
/// session (e.g. node 408's <c>'eq</c>/<c>'ne0</c>), and it reads CONSISTENTLY with the source's own
/// stated meaning here (branch only when r == 0), the same way <see cref="Node407Program"/>'s own
/// <c>'clbr</c> does -- no polarity concern to flag for this one.</item>
/// <item><c>1010_110?_????_????</c> -- branch (unconditional, reached directly OR via the
/// conditional-branch's own zero-flag fall-through above): <c>r&gt; 0x1ff and dup 0x100 and if drop
/// 0xfe00 xor dup then drop A[ @p m/branch ]] lit !p !p ;</c>. Masks the pending shifted value to 9 bits,
/// then sign-extends it if bit 8 is set (mirroring node 509's own already-confirmed 10-bit sign-extension
/// idiom for <c>'lit</c> -- <c>dup 0x0200 and if drop 0xfc00 xor ... ; then drop ... ;</c>, see
/// <see cref="Node509Program"/>'s own remarks), then streams a remote call to node 507's own
/// <c>m/branch</c> (adds the offset to node 507's own program counter). <b>Flagged, not fixed: an
/// apparent stack-depth bug in this specific line.</b> Hand-tracing the token-by-token data-stack effect
/// (dup/literal push = +1 each, and/xor/if/drop = -1 each) shows BOTH the taken and not-taken paths of
/// the inner <c>if...then</c> converge on exactly ONE item still pending immediately after <c>then</c> --
/// but the very next token, <c>drop</c>, discards it before <c>A[ @p m/branch ]] lit !p !p</c> ever runs.
/// That leaves nothing for the SECOND <c>!p</c> to transmit as <c>m/branch</c>'s own offset operand (the
/// first <c>!p</c> only sends the freshly-built remote opcode word itself) -- as written, this appears to
/// under-flow rather than actually deliver the computed offset. Node 509's own analogous idiom (above)
/// has NO equivalent trailing <c>drop</c> -- each of ITS branches ends by consuming the value directly
/// (<c>u/r! ;</c>) instead of leaving it to a shared post-<c>then</c> cleanup step. Per "never guess at
/// unspecified design decisions, flag explicitly rather than silently fix": this is reproduced completely
/// verbatim below; only Stefan can say whether that trailing <c>drop</c> is a genuine mistake or whether
/// something about the real hardware's actual <c>and</c>/<c>xor</c>/<c>if</c> timing makes it correct
/// anyway.</item>
/// </list>
/// </item>
/// <item><c>1010_10??_????_????</c> -- the "globals" group (what the ENTIRE <c>1010_1???</c> branch used
/// to be, one bit shallower, before this revision), itself split by one more bit, with the SAME two
/// bodies as before just under a narrower prefix and a narrower mask:
/// <list type="bullet">
/// <item><c>1010_101?_????_????</c> -- global fetch to r (source's own comment: "load r form global",
/// a plain typo for "from"): <c>r&gt; 0x1ff and g/@ ;</c>. Previously <c>1010_11??</c> with a 10-bit mask
/// (<c>0x3ff</c>); now <c>1010_101?</c> with a 9-bit mask (<c>0x1ff</c>) -- one fewer embedded offset
/// bit, traded for the new branch group's own extra prefix bit.</item>
/// <item><c>1010_100?_????_????</c> -- global store from r: <c>r&gt; 0x1ff and g/! ;</c>. Previously
/// <c>1010_10??</c> (10-bit mask); now <c>1010_100?</c> (9-bit mask), same trade as above.</item>
/// </list>
/// </item>
/// <item><c>1010_0???_????_????</c> -- UNCHANGED: falls through to <c>A[ m/next ]] lit !b A[ !p ]] lit
/// !b @b ex ;</c>, the same remote-fetch-and-store sequence <c>g/next</c> itself performs, immediately
/// followed by <c>ex</c> -- this is the tail <c>'ldg</c>/<c>'stg</c> both jump into (see below), the same
/// "ex reached once the cascade consumes a fixed prefix" pattern <see cref="Node407Program"/>/
/// <see cref="Node506Program"/> both use for <c>'lcall</c>/<c>'ljmp</c>/<c>'leave</c>.</item>
/// </list>
///
/// <b><c>'ldg</c>/<c>'stg</c> -- UNCHANGED bodies, moved addresses.</b> <c>: 'ldg g/next g/@ ;</c> and
/// <c>: 'stg g/next g/! ;</c> are byte-for-byte the same definitions as before 2026-09-06; only their own
/// compiled addresses moved (0x002C/0x002E -&gt; 0x003A/0x003C) because <c>g/main</c>'s own body, which
/// precedes them in the source, grew to make room for the new branch/conditional-branch forms. Per the
/// source's own trailing comment, "'ldg load global to r. offset in the next word" / "'stg store r to
/// global. offset in the next word": each first calls <c>g/next</c> (an ordinary LOCAL call here, not
/// the <c>A[...]]</c> remote-embed form -- the offset word itself lives in node 507's shared program
/// memory, fetched the same way <see cref="Node407Program"/>'s own <c>'lcall</c>/<c>'ljmp</c> fetch their
/// trailing address via <c>m/next</c>), then <c>g/@</c>/<c>g/!</c> to actually perform the global
/// access. This is the same <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/> shape
/// <c>pushlit</c>/<c>'lcall</c>/<c>'ljmp</c> already use -- a full-width offset in the CVM word
/// immediately following the opcode -- rather than the 9-bit embedded-offset shape the fetch/store
/// branches above use directly.
///
/// <b>Wired into <see cref="CvmInstructionSet"/>/<see cref="Services.CvmAssemblyLanguage"/>: only
/// <c>'ldg</c>/<c>'stg</c>, exactly as before.</b> Per Stefan's own tick-naming rule ("every word that
/// begins with a <c>'</c> is an opcode for the CVM with the mnemonic using the same name without the
/// leading <c>'</c>"), and per "only update existing opcodes where possible" / never guess at an
/// unspecified design decision: NONE of the four embedded-offset forms above (fetch, store, branch,
/// conditional branch) has a tick-prefixed name in Stefan's own source, so NONE of them gets a CVM
/// mnemonic here -- the same choice already made for this node's own OLD two embedded forms (fetch/
/// store), and for node 406's/408's own unnamed "reserved" branches, and for node 407's own still-open
/// "1101" branch. <c>'ldg</c>/<c>'stg</c> resolve dynamically against THIS class's own <see cref="Source"/>
/// (see <see cref="Services.CvmAssemblyLanguage.Node508LoadStoreGlobalTagBits"/>'s own remarks) -- since
/// that resolution is always against a live compile, never a hardcoded address, this revision needed NO
/// changes there at all despite <c>'ldg</c>/<c>'stg</c>'s own addresses moving; only THIS file's <see cref="Source"/>
/// and doc comments needed updating, plus the now-stale-numbers passage in
/// <see cref="Services.CvmAssemblyLanguage"/>'s own class remarks and <see cref="CvmInstructionSet.LoadGlobalMnemonic"/>'s
/// own remarks (both updated the same day to describe four embedded forms instead of two, and 9 bits
/// instead of 10, rather than re-deriving anything about <c>'ldg</c>/<c>'stg</c> themselves).
///
/// <b>No known opcode-space collision against this node's own LIVE-WIRED tag.</b> Unlike
/// <see cref="Node506Program"/>'s own former, now-resolved collision (<c>ldl</c>/<c>ldp</c>/<c>stl</c>/
/// <c>stp</c> vs. the old "ifbr" placeholder -- see that class's own remarks), this node's actual wired
/// range (<c>0xA000-0xA03F</c> for <c>'ldg</c>/<c>'stg</c>, node 508's own RAM being only 64 words) does
/// not overlap <see cref="CvmInstructionSet.BranchTag"/> (0x8000 as of 2026-09-09, OLD 0x9000 before
/// that, <c>1000_0xxx</c> now) or <see cref="CvmInstructionSet.ConditionalBranchTag"/> (<c>cbr</c>,
/// renamed and RE-TAGGED 2026-09-09 from the old "ifbr" guess to a confirmed 0xAC00-0xAFFF, <c>1010_11xx</c>
/// -- see that constant's own remarks) at all -- no collision to flag here. NOTE, however: the wider
/// theoretical <c>101?_????_????_????</c> range this class's own remarks describe this node as
/// "claiming" (0xA000-0xBFFF, never all actually wired) now DOES contain cbr's real 0xAC00-0xAFFF tag --
/// harmless today since node 508 never wires anything past 0xA03F, but worth remembering if this node's
/// own live range is ever widened. Unchanged by the 2026-09-06 revision (the range claimed at the CVM
/// opcode level, <c>0xA000-0xA03F</c> for <c>'ldg</c>/<c>'stg</c>, is exactly as narrow as before -- node
/// 508's own RAM is still only 64 words).
/// </summary>
internal static class Node508Program
{
  /// <summary>The node this program is always deployed to -- CVM2's globals-access node.</summary>
  public const int Coordinate = 508;

  /// <summary>
  /// Node 508's full resident F18 source. <b>RE-SYNCED 2026-09-09</b> verbatim against Stefan's LATEST
  /// <c>workspace.yaml</c> upload (the same upload that surfaced node 408's missing <c>'ugt</c> family --
  /// see <see cref="CvmInstructionSet"/>'s own class remarks) -- supersedes the 2026-09-08 sync below it
  /// in git history and the 2026-09-06 narrative in the class remarks above, both of which described a
  /// DIFFERENT, deeper "branch"/"conditional branch" split (two 9-bit embedded forms) that this latest
  /// export shows never actually shipped: the real, current <c>g/main</c> keeps ONE combined
  /// <c>1010_11??</c> form instead (skip-if-r-nonzero, else branch by a 10-bit signed offset) -- exactly
  /// <see cref="CvmInstructionSet.ConditionalBranchTag"/>'s own already-wired <c>cbr</c> shape (0xAC00,
  /// mask 0xFC00, 10-bit offset), so this re-sync brings the node's own source back in step with a tag
  /// that was already correct elsewhere in the toolchain rather than changing that tag itself.
  ///
  /// <b>Naming/body cross-wire -- flagged 2026-09-06/09, MIS-RESOLVED then CORRECTED, both 2026-09-10.</b>
  /// This export also renames node 508's own two tick-prefixed words from <c>'ldg</c>/<c>'stg</c> to
  /// <c>'gld</c>/<c>'gst</c> (see <see cref="CvmInstructionSet.LoadGlobalMnemonic"/>'s own remarks for the
  /// mnemonic-string rename this triggered) -- tracing their bodies against this node's own <c>g/@</c>/
  /// <c>g/!</c> primitives looked odd from the start: <c>'gld</c> called <c>g/@</c>, whose own body ended
  /// by remotely invoking <c>m/2!</c> -- a STORE into node 507's own page-2 globals -- while <c>'gst</c>
  /// called <c>g/!</c>, whose own body ended by remotely invoking <c>m/2@</c>, a FETCH. That reads
  /// backwards from conventional Forth naming (<c>@</c> fetches, <c>!</c> stores).
  ///
  /// Asked directly, Stefan's first answer ("gld loads a global from r" / "gst stores a global into r")
  /// was read as confirming this backwards direction was intentional -- <c>'gld</c>/<c>g/@</c> genuinely
  /// performing <c>global := r</c>, <c>'gst</c>/<c>g/!</c> genuinely performing <c>r := global</c> -- and
  /// <c>Ga144.C.Toolchain.CCodeGenerator.EmitGlobalFetch</c>/<c>EmitGlobalAssign</c> were wired that way.
  /// <b>That reading was WRONG.</b> Stefan then ran an actual test against the real node/simulation --
  /// <c>lit 1; gst 2; gld 2; gst 4; nop</c> -- and the resulting bus trace shows a WRITE of <c>1</c> to
  /// global page 2, address 2 on the <c>gst 2</c> instruction, then a READ of that same address back
  /// (returning the <c>1</c> just written) on the following <c>gld 2</c> instruction. That is, <c>gst</c>
  /// genuinely STORES (<c>global := r</c>) and <c>gld</c> genuinely LOADS (<c>r := global</c>) -- exactly
  /// what their names ordinarily mean, no reversal at all. The cross-wire flagged above was a REAL bug in
  /// this node's F18 source, not a naming-convention quirk: <c>g/@</c>'s and <c>g/!</c>'s own bodies
  /// really were swapped relative to their names.
  ///
  /// <b>Fixed by Stefan directly, 2026-09-10</b> ("here is the fixed node 508"), reflected in the source
  /// immediately below: <c>g/@</c>'s and <c>g/!</c>'s own bodies are swapped (each now ends in the
  /// remote op its NAME says it should -- <c>g/@</c> in the FETCH <c>m/2@</c>, <c>g/!</c> in the STORE
  /// <c>m/2!</c>), and <c>'gld</c>/<c>'gst</c> are no longer separate compiled words that call through
  /// <c>g/next</c> first -- each is now declared via <c>.loc</c> immediately before its own primitive
  /// (<c>'gld</c> before <c>g/@</c>, <c>'gst</c> before <c>g/!</c>), aliasing it directly. Reproduced
  /// here verbatim, per this project's own practice of never second-guessing or "improving" a fix Stefan
  /// supplies himself. <see cref="Ga144.C.Toolchain.CCodeGenerator.EmitGlobalFetch"/>/<c>EmitGlobalAssign</c>
  /// were corrected the same day to emit <c>gld</c>/<c>gst</c> in their now-confirmed-correct direction;
  /// see <see cref="CvmInstructionSet.LoadGlobalMnemonic"/>'s own remarks and `c-compiler-design.md`'s
  /// "Global-scalar addressing" section for the full story, including the hardware trace.
  ///
  /// <b>The plain "opcode ldg .../opcode stg ..." comment lines below (distinct from the tick-prefixed
  /// <c>'gld</c>/<c>'gst</c> above) are now wired into the assembler too, 2026-09-10</b> -- Stefan asked
  /// for them directly ("i am missing these 2 opcodes in the assembler... they are a shorter version
  /// with a 9 bit offset") and confirmed they share <c>'gld</c>/<c>'gst</c>'s own (hardware-confirmed)
  /// direction: "ldg is a shorter version of gld", "stg is a shorter version of gst". Unlike
  /// <c>'gld</c>/<c>'gst</c>, these are a fixed, self-describing 7-bit-tag/9-bit-unsigned-offset shape
  /// (no trailing word, no live-compile resolution against this node at all) -- see
  /// <see cref="CvmInstructionSet.LoadGlobalEmbeddedMnemonic"/>/<c>StoreGlobalEmbeddedMnemonic</c>'s own
  /// remarks for the bit derivation. A tag-range overlap with node 606's <c>adjust</c> was flagged
  /// (accepted) when this was first wired in, but <c>adjust</c> itself was deleted outright the same
  /// day, 2026-09-10, per Stefan ("'adjust' no longer exists. you can remove it.") -- see
  /// <see cref="CvmInstructionSet.LoadGlobalEmbeddedMnemonic"/>'s own remarks for the current state.
  ///
  /// See the class remarks above for the register/stack helpers, the confirmed LEFT port link back to
  /// node 507, and the general dispatch shape -- all UNCHANGED by this re-sync except where called out
  /// here. Treat any specific numeric claim in the class remarks above (a compiled address, a bit width)
  /// as possibly stale until re-confirmed against a fresh compile of the source immediately below.
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
      : 'gld .loc
      : g/@ ( o-) A[ @p m/2@ ]] lit !b !b A[ over ]] lit !b ;
      : 'gst .loc
      : g/! ( o-) A[ over @p ]] lit !b !b A[ m/2! ]] lit !b ;
      : g/leave A[ ; ]] lit !b
      : g/main # g/leave lit >r A[ 2* !p !p ]] lit !b @b @b >r
        -if // 1011_????_????_????
          // extended arithmetic
          r> r--- ;
        then // 1010_????_????_????
        2* -if // 1010_1???_????_????

          2* -if // 1010_11??_????_????

            // conditional branch
            g/r@ if r> g/leave then
            // branch
            2* 2* 2* 2* -if drop 0xfc00 xor dup then drop A[ @p m/branch ]] lit !p !p ;

          then // 1010_10??_????_????

          // globals
          2* -if // 1010_101?_????_????
            // store global into r
            r> 0x1ff and g/! ;
          then // 1010_100?_????_????
          // load global from r
          r> 0x1ff and g/@ ;

        then // 1010_0???_????_????
        A[ m/next ]] lit !b A[ !p ]] lit !b @b ex ;

      (
      opcode ldg 1010_100?_????_???? load global from r. the offset is unsigned 9 bit.
      opcode stg 1010_101?_????_???? store global into r. the offset is unsigned 9 bit.
      opcode cbr 1010_11??_????_???? conditional branch to offset if r == 0. the offset is signed 10-bit number.

      'gld load global from r. address in the next word
      'gst store global into r. address in the next word


      )
      """;
}