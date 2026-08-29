namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 606's resident F18 source -- the CVM test-cluster frame-pointer node (test-mirror of
/// real design node 206, register f). See <see cref="Node607Program"/>'s remarks for the full
/// test-mirror mapping table and node 607's own role.
///
/// <b>Servant, not master.</b> Unlike node 607 (which reaches OUT to its neighbours via named
/// multiport calls to delegate work), node 606 is only ever reached because 607's own
/// <c>exec</c> jumps into it (the <c>r---</c> branch, for frame/global-class opcodes). Once 607
/// jumps here, 607's own P register is left pointing at that port address: every <c>@p</c>/
/// <c>!p</c> 607 executes from then on is a live handshake across the wire with whatever 606
/// does with ITS reciprocal port (B, pointed "right" back at 607 by this node's own
/// <c>/b</c> directive). So 606 does not run code for itself so much as it feeds 607 raw
/// instruction words to run, using the <c>A[ ... ]] lit !b</c> idiom repeatedly: assemble up to
/// four packed F18A opcodes (optionally ending in a CALL/JUMP to one of 607's own exported
/// words, resolved via <c># 607 import</c> below per DB002 3.1) with <c>A[ ... ]]</c>, push it
/// as a literal with <c>lit</c>, then transmit it to 607 with <c>!b</c>. <c>@b</c> (606 reading
/// back through the same port) receives whatever 607's own <c>!p</c>/<c>!b</c> produces in
/// response -- this is how 606 gets values out of 607's stack/registers despite having no direct
/// access to them.
///
/// <b>Local port directions on 606</b> (row 6 is even, column 06 is even, per this project's
/// <c>KrakenTopology.PortAddress</c> mirroring rules):
/// <code>
///   right (r---, 0x1D5) -&gt; 607  the CPU/master node that puppets 606
///   left  (--l-, 0x175) -&gt; 605  not part of this cluster
///   down  (-d--, 0x115) -&gt; 706  not part of this cluster
///   up    (---u, 0x145) -&gt; 506  not part of this cluster
/// </code>
/// -- matching this node's own "# right /b // master node" directive. A holds the frame pointer
/// f itself ("frame pointer init"); 606 addresses its own local RAM directly with A/off/noff to
/// store and retrieve frame-relative data, entirely separate from the port traffic to 607.
///
/// <b>Verification.</b> Compiled with zero errors AND zero warnings (<c>Success = true</c>)
/// against this project's real <c>Compiler/F18Compiler.cs</c>, importing node 607's exported
/// symbols via <c># 607 import</c> (<c>F18CompilerOptions.ImportResolver</c>, fed node 607's own
/// compiled <c>Exports</c> -- the same <c>F18ExportSet</c>/<c>F18ImportResolution</c> plumbing
/// <c>F18NodeCompilationService</c> already uses for real cross-node imports). 61 of 64 RAM
/// words used, entry point <c>main</c> at word address 0x003, <c>'leave</c> itself at word
/// address 0x037. (An earlier revision of this file, compiled against an earlier revision of
/// <see cref="Node607Program"/> whose own fetch/dispatch loop was named <c>main</c> rather than
/// <c>'nop</c>, got one additional informational, benign warning here -- F18C050, "'main'
/// redefines the name imported from node 607" -- since both nodes defined their own independent
/// word called <c>main</c>; that warning no longer fires now that 607's own loop is named
/// <c>'nop</c> instead, per <see cref="Node607Program"/>'s own revision history.) Adding the
/// per-word documentation comments to <see cref="Source"/> was re-verified to produce
/// byte-for-byte identical compiled output to the plain, uncommented version.
///
/// <b>Revision note.</b> This replaces an earlier revision's <c>'exit</c>/<c>'wait</c> pair
/// (which together implemented "wait for a stimulus [from 707/the PC] and [then] start from
/// location 1") with a single new word, <c>'leave</c> -- the undo side of <c>enter</c>, restoring
/// 606's frame pointer to whatever <c>enter</c> saved before (Stefan's own description: "undo
/// enter. move frame pointer to stack pointer and pop the saved frame pointer"). <c>'exit</c>/
/// <c>'wait</c> are gone entirely in this revision, not merely renamed.
///
/// <b>A note on confidence.</b> <c>la</c>/<c>ld</c>/<c>st</c>/<c>adjust</c> were given with no
/// description (Stefan's own word list left them blank), and <c>main</c>'s exact dispatch
/// cascade and <c>enter</c>/<c>cleanup</c>'s precise round-trip values were not independently
/// bit-traced against running hardware -- those sections in <see cref="Source"/> are the best
/// available reading of the code, clearly marked as inferred rather than confirmed, in the same
/// spirit as the lower-confidence notes on node 607's own <c>exec</c>. Everything else (the
/// control-flow structure -- which <c>if</c>/<c>then</c> pairs with which, verified by matching
/// all 7 pairs by nesting order -- the cross-node call targets, and every word carrying one of
/// Stefan's own descriptions) is verified directly against the compiler and the source as given.
/// </summary>
internal static class Node606Program
{
  /// <summary>The node this program is always deployed to -- test-mirror of real design node 206 (register f).</summary>
  public const int Coordinate = 606;

  /// <summary>
  /// Node 606's full resident F18 source, fully commented per-word (using Stefan's own
  /// descriptions where given, clearly-flagged inferences where not) with a traced control-flow
  /// walkthrough of <c>main</c>'s command dispatch. See the class remarks for the compile
  /// verification this source was checked against, including its cross-node import of node 607's
  /// symbol table via <c># 607 import</c>.
  /// </summary>
  public const string Source = """
      // ============================================================================
      // Node 606 -- CVM test-cluster frame-pointer node (test-mirror of real design
      // node 206, register f)
      // ============================================================================
      //
      // Real hardware role (per cvm_2.txt): node 206 holds f, the CVM's frame
      // pointer, and implements every frame-relative operation the CPU (207/607)
      // needs: load the address of a local variable, load a local variable's
      // value, store a value into a local variable, and enter/exit a call frame.
      // Node 606 is that same node, test-mirrored (row' = 8-row, column unchanged)
      // -- see Node607Program.cs's remarks for the full mirror-mapping table.
      //
      // Unlike node 607 (which reaches OUT to its neighbours via named multiport
      // calls to delegate work), node 606 is a SERVANT node: it is only ever
      // reached because 607's own 'exec' jumps into it (the 'r---' branch, for
      // frame/global-class opcodes -- see Node607Program.cs's remarks on 'exec').
      // Once 607 jumps to 606 via a named multiport call, 607's own P register is
      // left pointing at that port address; from then on, every @p/!p 607 executes
      // is actually a live handshake across the physical wire with whatever 606
      // does with ITS reciprocal port (B, pointed "right" back at 607 by this
      // node's own '/b' directive below) -- so 606 does not run code FOR itself in
      // the usual sense so much as it *feeds 607 raw instruction words to run*,
      // using the very same 'A[ ... ]] lit !b' idiom repeatedly: assemble up to
      // four packed F18A opcodes (optionally ending in a CALL/JUMP to one of
      // node 607's own exported words -- resolved via '# 607 import' below, per
      // DB002 3.1, since 606 cannot execute an address in 607's own dictionary
      // itself, only ship the raw bits there) with 'A[ ... ]]', push it as a real
      // literal in 606's own stream with 'lit', then transmit it to 607 with '!b'.
      // 607, still stalled fetching via its own down-turned-into-a-port P, receives
      // and executes each word in turn. '@b' (606 reading back through the same
      // port) receives whatever 607's own !p/!b produces in response -- this is
      // how 606 gets values out of 607's stack/registers despite having no direct
      // access to them.
      //
      // Local port directions on 606 (row 6 is even, column 06 is even, per this
      // project's KrakenTopology.PortAddress mirroring rules):
      //     right (r---, 0x1D5) -> 607  (the CPU/master node that puppets 606)
      //     left  (--l-, 0x175) -> 605  (not part of this cluster)
      //     down  (-d--, 0x115) -> 706  (not part of this cluster)
      //     up    (---u, 0x145) -> 506  (not part of this cluster)
      // -- matching this word's own "# right /b // master node" directive: B is
      // pointed at 607, the node that puppets 606 and that every !b/@b in this
      // file actually talks to.
      //
      // A holds the frame pointer f itself (per this word's own "frame pointer
      // init" comment on '# 0 /a') -- 606 addresses its OWN local RAM directly
      // with A/off/noff to store/retrieve frame-relative data, entirely separate
      // from the port traffic to 607 described above.
      //
      // Verified: this source compiles against the real F18Compiler with 0 errors
      // and 0 warnings (Success=true), importing node 607's exported symbols via
      // '# 607 import' (F18CompilerOptions.ImportResolver, fed node 607's own
      // compiled Exports -- the same F18ExportSet/F18ImportResolution plumbing
      // F18NodeCompilationService already uses for real cross-node imports). 61 of
      // 64 RAM words used, entry point 'main' at word address 0x003, 'leave itself
      // at word address 0x037. (An earlier revision of this file, compiled against
      // an earlier revision of Node607.f18 whose own fetch/dispatch loop was named
      // 'main rather than 'nop, got one additional informational, benign warning
      // here -- F18C050, "'main' redefines the name imported from node 607" --
      // since both nodes defined their own independent word called 'main; that
      // warning no longer fires now that 607's own loop is named 'nop instead,
      // per Node607.f18's own revision history.)
      //
      // Revision note: this replaces an earlier revision's 'exit/'wait pair (which
      // together implemented "wait for a stimulus [from 707/the PC] and [then]
      // start from location 1") with a single new word, 'leave -- the undo side of
      // 'enter, restoring 606's frame pointer to whatever 'enter saved before
      // (Stefan's own description: "undo enter. move frame pointer to stack
      // pointer and pop the saved frame pointer"). 'exit/'wait are gone entirely
      // in this revision, not merely renamed.
      //
      // Adding the per-word documentation comments below was re-verified to
      // produce byte-for-byte identical compiled output (same Success, same
      // UsedWordCount, same symbol table, same entry point) to the plain,
      // uncommented version -- comments have no effect on the compiled image.
      //
      // A note on confidence: la/ld/st/adjust were given to me with no
      // description (Stefan's own word list left them blank), and 'main's exact
      // dispatch cascade and enter/cleanup's precise round-trip values were not
      // independently bit-traced against running hardware -- those sections below
      // are my own best reading of the code (clearly marked as such), not
      // confirmed facts, in the same spirit as the lower-confidence notes on
      // node 607's own 'exec'. Everything else (the control-flow structure itself,
      // which if/then pairs with which, the cross-node call targets, and every
      // word carrying one of Stefan's own descriptions) is verified directly
      // against the compiler and the source as given.
      // ============================================================================

      // Import node 607's exported dictionary (its ROM+RAM symbol table) so the
      // A[ ... ]] blocks below can reference 607's own words (/r@, /r!, /1@, /1!,
      // /push, /pop, 'ret) by name -- DB002 3.1 "imported from another node".
      # 607 import
      entry main

      //  frame pointer init: A holds f, the CVM's frame pointer, addressing this
      //  node's own local RAM directly.
      # 0 /a

      //  master node: B is pointed "right", at 607 -- the CPU node that puppets
      //  606 via a named multiport call, and the node every !b/@b in this file
      //  actually communicates with.
      # right /b

      # 0 org

      // ----------------------------------------------------------------------
      // noff  --  negative offset
      // ----------------------------------------------------------------------
      // Falls straight through (no ';') into 'off' below. 'inv' (one's-complement
      // negate) turns the raw magnitude already on the stack into its bitwise
      // inverse before 'off' adds it to the frame pointer -- the standard F18A
      // idiom for a negative offset where there is no native subtract: a + ~n
      // (off by one from a plain a - n, which the values fed into noff already
      // account for).
      : noff inv

      // ----------------------------------------------------------------------
      // off  --  positive offset
      // ----------------------------------------------------------------------
      // 'a' pushes the current frame pointer, '.' pads the packed word so '+'
      // lands in the next slot, '+' adds the offset already on the stack to it,
      // producing the frame-relative address that la/ld/st (below) will use.
      : off a . + ;

      // ----------------------------------------------------------------------
      // local  --  call local word
      // ----------------------------------------------------------------------
      // 'drop' discards the leftover dispatch tag main's own '# local -until'
      // idiom leaves behind, then 'ex' (F18A opcode 0x01) jumps to whatever
      // address main previously pushed onto the return stack with '>r' -- the
      // same "push an address on R, then EX to it" idiom used throughout this
      // project (see the '# name -until' pattern in node 607's own 'main') to
      // turn a computed value into a real control transfer.
      : local drop ex

      // ----------------------------------------------------------------------
      // main  --  node entry point / command dispatcher
      // ----------------------------------------------------------------------
      // Ships 607 a tiny relay instruction -- {2*, !p, !p} -- that shifts 607's
      // own top-of-stack left one bit (very likely stripping a tag bit set by
      // 607's 'exec' before it delegated here) and writes the top two items back
      // OUT across the port via two '!p's (since 607's P is still parked at the
      // port address, '!p' there doesn't touch real RAM -- it hands the value
      // back to whoever is on the other end of the wire, i.e. this node). The two
      // '@b's that follow receive those two relayed values back on 606's side.
      // 'xff and' masks the second one down to its low byte -- a command/opcode
      // tag -- '>r' parks it on the return stack, and '# local -until' overrides
      // the following '-until' test's branch target to 'local' (see above): if
      // the test fails, dispatch falls through to 'local', which 'ex's straight
      // to whatever address is on R.
      //
      // If the test succeeds instead, execution falls into the cascade below --
      // a tree of paired 'off'/'noff' (positive/negative frame-offset) branches
      // for la, ld, and st, and a further branch choosing between cleanup and
      // enter. This nesting was traced directly against the source's 7 matched
      // -if/then pairs (verified balanced), so the STRUCTURE below is exact; the
      // specific bit each '2* -if' tests was not independently re-derived against
      // running hardware, only inferred from which real word ends up in which
      // branch (see the note on confidence above the header).
      : main A[ 2* !p !p ]] lit !b @b
      	@b xff and >r # local -until
      	2* -if 2* -if 2* -if r> off
      // ----------------------------------------------------------------------
      // la ( a-)  --  load address (into r) -- name/meaning inferred, not one of
      // Stefan's given descriptions. Reached here as the innermost true-branch:
      // bit1 true, bit2 true, bit3 true selects "la, positive offset".
      // ----------------------------------------------------------------------
      // Assembles {@p, tail-jump /r!} and ships it to 607: fetch the next literal
      // then jump straight into 607's own /r! (store top of stack in register r),
      // so 607 loads whatever literal it fetches directly into r. The second
      // '!b' sends that literal: the frame-relative address 'off'/'noff' just
      // computed (this word's own input argument 'a', per its (a-) stack effect).
      // Falls through to 'main' via a tail call once done, to wait for the next
      // command.
      : la ( a-) A[ @p /r! ; ]] lit !b !b main ;
      	then r> noff la ;
      	then 2* -if r> off
      // ----------------------------------------------------------------------
      // ld  --  load (a local variable's value, into r) -- inferred, not given.
      // Reached when bit1 true, bit2 false: "ld, positive offset" on this branch.
      // ----------------------------------------------------------------------
      // Two packed words shipped to 607 in sequence: {@p, CALL /1@} (fetch the
      // address literal -- sent by the following plain '!b' -- then call 607's
      // own /1@, "fetch word from page 1", reading the value stored at that
      // frame-relative address) and {tail-jump /r!} (store the fetched value
      // into r). Falls through to 'main' via tail call.
      : ld A[ @p /1@ ]] lit !b !b A[ /r! ; ]] lit !b main ;
      	then r> noff ld ;
      	then 2* -if 2* -if r> off
      // ----------------------------------------------------------------------
      // st ( a-)  --  store (r's value into a local variable) -- inferred, not
      // given. Reached when bit1 false, bit2' true: "st, positive offset".
      // ----------------------------------------------------------------------
      // {CALL /r@} is assembled and laid down with ',' rather than shipped
      // immediately with 'lit' -- still just raw data at this point, per the
      // compiler's own A[ ... ]] semantics (it never decides what happens to the
      // word it assembles). The plain '!b' that follows sends it to 607, which
      // executes the call into its own /r@ ("fetch r from node 507"), fetching
      // r's current value. A second packed word {@p, tail-jump /1!} follows,
      // shipped with 'lit', telling 607 to fetch the next literal (the
      // frame-relative address, sent by the final plain '!b') and jump into its
      // own /1! ("store word in page 1") to write r's value there. Falls through
      // to 'main' via tail call.
      : st ( a-) A[ /r@ ]] , !b A[ @p /1! ; ]] lit @p !b !b main ;
      	then r> noff st ;
      	then 2* -if
      // ----------------------------------------------------------------------
      // cleanup  --  exit stack frame, return and cleanup (Stefan's own
      // description). Reached when bit1 false, bit2' false, bit3'' true.
      // ----------------------------------------------------------------------
      // Ships {@p, CALL /pop} to 607 (fetch a literal -- sent by the following
      // plain '!b' of 606's OWN current frame pointer -- then call 607's own
      // /pop, reading the word saved at that address: the caller's saved frame
      // pointer). Ships a second word, {!p, tail-jump 'ret}: 607 relays that
      // fetched value back out over the port (!p), then jumps into its own 'ret
      // ("return from call"), which itself pops once more and installs the
      // result into P -- resuming 607 at the CVM caller's saved return address.
      // Back on 606's side, '@b' receives the relayed saved frame pointer, 'a!'
      // restores it into A (completing "exit stack frame": 606's frame pointer
      // is back to the caller's), and 'r>' tidies 606's own return stack before
      // falling through into 'adjust' below (no ';' here).
      : cleanup A[ @p /pop ]] lit !b a !b A[ !p 'ret ]] lit !b @b a! r>
      // ----------------------------------------------------------------------
      // adjust  --  inferred, not given: adjusts 607's frame-relative bookkeeping
      // by a signed size. Shared tail for both cleanup (falls in directly, above)
      // and enter (falls in via 'inv', below).
      // ----------------------------------------------------------------------
      // Ships {@p, ., +, return} to 607: fetch the next literal (the frame size,
      // sent by the following plain '!b' of whatever is left on 606's stack --
      // the value 'r>' left behind from cleanup, or the inverted size from
      // enter), add it to whatever 607 already has on top of its own stack (most
      // likely p, the pointer /push//pop address through -- see node 607's own
      // remarks), then return. Falls through to 'main' via tail call.
      : adjust A[ @p . + ; ]] lit !b !b main ;
      // ----------------------------------------------------------------------
      // enter  --  enter stack frame (Stefan's own description). This is the
      // false-branch landing point for bit3'' (the 'then' immediately below is
      // what 'main's cascade jumps to when it does NOT select cleanup).
      // ----------------------------------------------------------------------
      // Ships {@p, CALL /push} to 607 (fetch a literal -- sent by the following
      // plain '!b' of 606's OWN current frame pointer -- then call 607's own
      // /push, "push value onto stack": saving the OLD frame pointer into 607's
      // extended parameter/return area, the classic "save caller's frame
      // pointer" prologue step). Ships a second word, {dup, !p}: 607 duplicates
      // and relays a value back across the port; '@b'/'a!' on 606's side receive
      // it and install it as 606's own (new) frame pointer -- the exact values
      // exchanged in this second round trip were not independently re-derived,
      // so treat this step's detail as inferred rather than confirmed. 'r>'
      // tidies 606's return stack, 'inv' negates the frame-size argument still
      // on the stack (so the shared 'adjust' tail below grows the frame by
      // subtracting rather than adding), and tail-calls into 'adjust'.
      : enter then A[ @p /push ]] lit !b a !b A[ dup !p ]] lit !b @b a! r> inv adjust ;

      // ----------------------------------------------------------------------
      // 'xf ( s-s)  --  exchanges frame pointer with r (Stefan's own description)
      // ----------------------------------------------------------------------
      // 'a' pushes 606's current frame pointer. First packed word {@p, CALL /r@}
      // ships to 607 (fetch a literal, sent by the following plain '!b' of the
      // just-pushed frame pointer -- unused by /r@ itself but present so the
      // packed word's '@p' has something to consume -- then call /r@ to fetch
      // r's current value onto 607's own stack). Second packed word {!p, tail-jump
      // /r!}: 607 relays r's old value back out over the port, then jumps into
      // /r! to store the earlier-sent frame-pointer value into r. Back on 606,
      // '@b' receives r's old value and 'a!' installs it as 606's new frame
      // pointer -- a genuine two-way exchange, matching Stefan's description
      // exactly.
      : 'xf ( s-s) a A[ @p /r@ ]] lit !b !b A[ !p /r! ; ]] lit !b @b a! ;

      // ----------------------------------------------------------------------
      // 'leave  --  undo enter: restore 606's frame pointer to what it was
      // before the matching 'enter (Stefan's own description: "undo enter. move
      // frame pointer to stack pointer and pop the saved frame pointer")
      // ----------------------------------------------------------------------
      // 'a' pushes 606's current frame pointer. First packed word {@p, CALL
      // /pop} ships to 607 (fetch a literal -- sent by the following plain '!b'
      // of that just-pushed frame pointer -- then call 607's own /pop, "pop a
      // value from stack": reading back the saved caller's frame pointer that
      // 'enter's own /push originally saved there -- the exact reverse of
      // 'enter's first round trip). Second packed word {!p, plain return}: 607
      // relays that popped value back out over the port, then executes a bare
      // return -- NOT a tail-jump into its own 'ret the way 'cleanup's epilogue
      // does. That's the key difference from 'cleanup: 'leave is invoked as an
      // ordinary primitive, not as part of a call/return sequence, so it only
      // needs to hand control straight back to whatever called it, never to pop
      // a separate return address off 607's own return stack. Back on 606's
      // side, '@b' receives the relayed saved frame pointer and 'a!' installs
      // it into A -- restoring 606's frame pointer to the caller's, i.e.
      // "undoing" whatever the matching 'enter did -- before tail-calling back
      // into 'main to wait for the next command.
      : 'leave A[ @p /pop ]] lit !b a !b A[ !p ; ]] lit !b @b a! main ;
      """;
}