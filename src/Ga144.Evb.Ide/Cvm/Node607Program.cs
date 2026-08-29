namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 607's resident F18 source -- the CVM test-cluster CPU node (test-mirror of real design
/// node 207). Holds P (the CVM program counter, kept in this node's own A register) and S (the
/// CVM data-stack top, kept directly on this node's native F18 data stack): the node that
/// fetches, decodes, and executes every CVM instruction.
///
/// <b>Test-mirror mapping.</b> Node 607 is real design node 207, geometrically reflected into
/// this project's test topology (row' = 8-row, column unchanged) so the whole CVM cluster sits
/// mirrored next to the existing Kraken/SRAM test infrastructure:
/// <code>
///   real design            test mirror          holds
///   107 (SRAM interface) -&gt; 707                  bridges to PC via 708
///   207 (p, s)            -&gt; 607 (this node)      P (in A reg), S (native stack)
///   307 (r)               -&gt; 507                  register r
///   206 (f)               -&gt; 606                  register f (frame)
///   208 (g)               -&gt; 608                  register g (globals)
///   306 (d)               -&gt; 506                  register d
///   308 (t)               -&gt; 508                  register t
///   407 (w)               -&gt; 407 (self-maps)      register w
/// </code>
/// In the TEST setup only (not real hardware) node 707 has no local storage of its own -- it is a
/// stateless interface that forwards every request on to the PC over the serial link through node
/// 708, which stands in for the external SRAM the real node 107 would talk to.
///
/// <b>Local port directions on 607</b> (row 6 is even, column 07 is odd, per this project's
/// <c>KrakenTopology.PortAddress</c> mirroring rules):
/// <code>
///   down  (-d--, 0x115) -&gt; 707  memory/PC interface, mirrors 107
///   up    (---u, 0x145) -&gt; 507  register r, mirrors 307
///   left  (--l-, 0x175) -&gt; 608  register g / globals, mirrors 208
///   right (r---, 0x1D5) -&gt; 606  register f / frame, mirrors 206
/// </code>
/// Per Stefan's instruction, every occurrence of the named multiport call <c>-d--</c> that
/// targets node 507 has been replaced with <c>---u</c> (down -&gt; up), since 507 (register r AND,
/// per Stefan's own confirmed node 607/507 source, the entire 0xC000-0xFFFF opcode class -- short
/// literals, the ALU, and register w/t/d forwarding) is reached via the up port from 607, not via
/// down. This corrects an earlier version of this file, which kept <c>exec</c>'s own first-branch
/// <c>-d--</c> unchanged on the mistaken assumption that it was a distinct "memory-class opcode"
/// dispatch legitimately aimed at 707: Stefan's own inline comment on that exact branch confirms
/// it is the SAME "hand off to the second CVM node" dispatch <c>/r@</c>/<c>/r!</c> already use,
/// not a separate memory path, so it needed the same substitution.
///
/// <b>This revision, taken directly from what's actually running on real hardware</b> (confirmed:
/// a hand-assembled "slit 100 / inc / push" sequence read back exactly the expected opcodes --
/// 0xD064 / 0xC03C / 0x801A -- and produced a real external-memory write of 0x0065 (101 =
/// 100+1), round-tripping through node 507's short-literal and unary-ALU dispatch). Several real
/// corrections came with it, beyond the -d--/---u substitution above:
/// <list type="bullet">
/// <item><c>/cx?</c> (compare-and-exchange) has been removed entirely, matching node 507 dropping
/// <c>'cx?</c>/<c>s/cx?</c> in the same generation -- AN003's cx? request shape is no longer part
/// of this system's memory protocol.</item>
/// <item><c>/next</c> -- the CVM's own "fetch the next instruction word" primitive -- is
/// corrected to actually read from PAGE 0 (this node's own code space) at address P, rather than
/// walking into <c>/pop</c>'s own page-1 (parameter/return area) fetch chain as an earlier
/// version did (reading a program's own CODE from the wrong page).</item>
/// <item>The interpreter's own fetch/decode/execute loop -- previously named <c>main</c> -- is
/// renamed to <c>'nop</c>, and is now ALSO this node's <c>entry</c> point: jumping to the top of
/// the loop and letting it fetch the next instruction, with nothing else done first, already has
/// zero side effects beyond "keep running" -- exactly what a CVM <c>nop</c> opcode needs, so the
/// loop word doubles as <c>nop</c>'s own target rather than needing a separate, genuinely-empty
/// word.</item>
/// <item>A new module-level <c>[ 0xffff 1 ] /stack</c> directive preloads this node's native F18
/// data stack (which IS the CVM's own S) with a single value, 0xFFFF, before <c>'nop</c>'s fetch
/// loop ever runs.</item>
/// </list>
///
/// <b>Verification.</b> Compiled with zero diagnostics against this project's real
/// <c>Compiler/F18Compiler.cs</c> (via <c>F18CompilerOptions.ForRam(607)</c>): <c>Success =
/// true</c>, all 64/64 RAM words used, entry point <c>'nop</c> at word address 0x03B -- which is
/// also why <c>'nop</c>'s own opcode reads back as 0x803B on real hardware. The eleven
/// CVM-instruction opcodes below (the original nine from cvm_2.txt, plus <c>'lit</c> and
/// register-w/t/d forwarding, since <c>exec</c> now documents all of its own branches) resolve to
/// exactly the addresses this compiler assigns their implementing words -- <c>'plit</c> compiles
/// to word address 0x00E and its opcode is 0x800E, <c>'push</c> compiles to 0x01A and its opcode
/// is 0x801A (confirmed against a real hardware read-back), and so on. This is exactly how
/// <c>exec</c>'s final branch turns an opcode into a jump: the opcode's low bits already are the
/// target word's address.
/// </summary>
internal static class Node607Program
{
  /// <summary>The node this program is always deployed to -- test-mirror of real design node 207.</summary>
  public const int Coordinate = 607;

  /// <summary>
  /// Node 607's full resident F18 source, fully commented per-word (using Stefan's own
  /// descriptions where given) with opcode annotations on every CVM-instruction-implementing
  /// word (<c>'plit</c>, <c>'lit</c>, <c>'pop</c>, <c>'push</c>, <c>'ret</c>, <c>'xs</c>,
  /// <c>'xp</c>, <c>'tjmp</c>, <c>'pc</c>) and a structural walkthrough of <c>exec</c>'s
  /// four-way dispatch, plus <c>'nop</c>'s own fetch/decode/execute loop. See the class remarks
  /// for the compile verification this source was checked against.
  /// </summary>
  public const string Source = """
      // ============================================================================
      // Node 607 -- CVM test-cluster CPU node (test-mirror of real design node 207)
      // ============================================================================
      //
      // Real hardware role (per cvm_2.txt): node 207 holds the CVM's two primary
      // registers -- P (the CVM program counter) and S (the CVM data-stack top) --
      // and is the node that fetches, decodes and executes every CVM instruction.
      // Node 607 is that same node, geometrically reflected into this project's
      // test topology (row' = 8-row, column unchanged) so the whole CVM cluster
      // sits mirrored next to the existing Kraken/SRAM test infrastructure:
      //
      //     real design           test mirror         holds
      //     ----------------------------------------------------------
      //     107 (SRAM interface)  707                 (bridges to 707 below)
      //     207 (p, s)             607  <-- this node  P (in A reg), S (native stack)
      //     307 (r)                 507                register r
      //     206 (f)                 606                register f (frame)
      //     208 (g)                 608                register g (globals)
      //     306 (d)                 506                register d
      //     308 (t)                 508                register t
      //     407 (w)                 407 (self-maps)    register w
      //
      // In the TEST setup only (not real hardware), node 707 has no local storage
      // of its own -- it is a stateless interface that forwards every request on to
      // the PC over the serial link through node 708, which stands in for the
      // external SRAM the real node 107 would talk to. So node 607's neighbour
      // "down" (toward 707) reaches memory/PC, not a local RAM node.
      //
      // Local port directions on 607 (row 6 is even, column 07 is odd, per this
      // project's KrakenTopology.PortAddress mirroring rules):
      //     down  (-d--, 0x115) -> 707  (memory/PC interface, mirrors 107)
      //     up    (---u, 0x145) -> 507  (register r, mirrors 307)
      //     left  (--l-, 0x175) -> 608  (register g / globals, mirrors 208)
      //     right (r---, 0x1D5) -> 606  (register f / frame, mirrors 206)
      //
      // P (the CVM's own program counter) is kept in this node's local A register.
      // S (the CVM's data-stack top) is kept directly on node 607's own native F18
      // data stack -- there is no separate S variable; the F18 hardware stack IS
      // the CVM's data stack while this node is running.
      //
      // Per Stefan's instruction, every occurrence of the named multiport call
      // '-d--' that targets node 507 has been replaced with '---u' (down -> up),
      // since 507 (register r AND, per Stefan's own confirmed node 607/507 source,
      // the entire 0xC000-0xFFFF opcode class -- short literals, the ALU, and
      // register w/t/d forwarding) is reached via the up port from 607, not via
      // down. This corrects an earlier version of this file, which kept 'exec's
      // own first-branch '-d--' unchanged on the mistaken assumption that it was a
      // distinct "memory-class opcode" dispatch legitimately aimed at 707: Stefan's
      // own inline comment on that exact branch (// 11??_????_????_????) confirms
      // it is the SAME "hand off to the second CVM node" dispatch /r@/​/r! already
      // use, not a separate memory path, so it needed the same substitution.
      //
      // This version replaces an earlier one with Stefan's own newest source,
      // taken directly from what's actually running on real hardware (confirmed:
      // a hand-assembled "slit 100 / inc / push" sequence read back exactly the
      // expected opcodes -- 0xD064 / 0xC03C / 0x801A -- and produced a real
      // external-memory write of 0x0065 (101 = 100+1), round-tripping through
      // node 507's short-literal and unary-ALU dispatch). Several real
      // corrections came with it, beyond the exec fix above:
      //
      //   - /cx? (compare-and-exchange) has been removed entirely, matching node
      //     507 dropping 'cx?/s/cx? in the same generation -- AN003's cx? request
      //     shape is no longer part of this system's memory protocol.
      //   - /next -- the CVM's own "fetch the next instruction word" primitive --
      //     is corrected to actually read from PAGE 0 (this node's own code
      //     space) at address P: "a dup 1 . + a! dup dup xor /@ ;" pushes P
      //     (`a`), advances P by one and stores the advance back into A (`dup 1 .
      //     + a!`), then builds the (page 0, address P) pair via `dup dup xor`
      //     (duplicating P and XOR-ing the duplicate with itself always yields 0,
      //     the page number) before calling /@. An earlier version of this word
      //     instead walked into /pop's own page-1 (parameter/return area) fetch
      //     chain -- reading a program's own CODE from the wrong page -- which
      //     this rewrite fixes.
      //   - The interpreter's own fetch/decode/execute loop -- previously named
      //     'main' -- is renamed to 'nop, and is now ALSO this node's `entry`
      //     point. This isn't a coincidence: jumping to the top of the loop and
      //     letting it fetch the next instruction, with nothing else done first,
      //     already has zero side effects beyond "keep running" -- exactly what a
      //     CVM `nop` opcode needs. So the loop word doubles as `nop`'s own
      //     target rather than needing a separate, genuinely-empty word: 'nop IS
      //     the interpreter, entered once at cold start and re-entered every time
      //     a fetched opcode's tag bit is clear (an untagged word -- see 'nop's
      //     own remarks below) or a `nop` opcode is executed.
      //   - A new module-level `[ 0xffff 1 ] /stack` directive preloads this
      //     node's native F18 data stack (which, as noted above, IS the CVM's own
      //     S) with a single value, 0xFFFF, before 'nop's fetch loop ever runs.
      //     `/stack`'s own compile-time convention (DB013) takes a COUNT on top
      //     (here `1`) followed by that many values beneath it (here `0xffff`
      //     alone) -- so this preloads exactly one value, 0xFFFF, as the initial
      //     data-stack content at cold boot.
      //
      // Verified: this source compiles against the real F18Compiler with 0
      // diagnostics, Success=true, using all 64/64 RAM words, entry point 'nop at
      // 0x03B -- which is also why 'nop's own opcode reads back as 0x803B on real
      // hardware. The eleven CVM-instruction opcodes below (the original nine from
      // cvm_2.txt, plus 'lit and register-w/t/d forwarding, since 'exec now
      // documents all of its own branches) resolve to exactly the addresses this
      // compiler assigns their implementing words -- 'plit compiles to word
      // address 0x00E and its opcode is 0x800E, 'push compiles to 0x01A and its
      // opcode is 0x801A (confirmed against a real hardware read-back), and so on.
      // This is exactly how 'exec's final branch (see below) turns an opcode into
      // a jump: the opcode's low bits already are the target word's address.
      // ============================================================================

      # 0 org
      entry 'nop

      //  P always starts at extended address 0: every CVM program's first
      //  instruction lives at address 0, so A (which holds P here) is initialised
      //  to 0 at cold start.
      #  0 /a

      //  B is initialised to point "down", i.e. at the neighbour that services
      //  memory access (707, which forwards to the PC over the serial link
      //  through 708). Words that use B directly to fetch/store through the
      //  memory-access neighbour (/@, /1@, /!, /1!) rely on this.
      # down /b

      // Preloads this node's own native F18 data stack -- which IS the CVM's S,
      // per the header remarks above -- with one value, 0xFFFF, before 'nop's
      // fetch loop ever runs for the first time. See the header remarks on
      // '/stack' for how the compile-time count-then-values convention here
      // (DB013) resolves to exactly one preloaded value.
      [ 0xffff 1 ] /stack

      // ----------------------------------------------------------------------
      // /r@ ( s-sw)  --  fetch r from node 507
      // ----------------------------------------------------------------------
      // Pushes a zero-valued request word (dup dup xor: duplicating s and
      // XOR-ing the two copies together always yields 0, regardless of s's
      // value, without disturbing s itself), duplicates that zero once more,
      // then hands control to node 507 via the up port (---u). By AN003's
      // protocol convention a zero request word means "read", so node 507
      // answers by leaving r's current value, w, on top of the stack when
      // control returns -- giving the (s-sw) effect: s is left untouched
      // underneath, with r's value w pushed on top of it.
      : /r@ ( s-sw) dup dup xor dup ---u ;

      // ----------------------------------------------------------------------
      // /pop ( s-sw)  --  pop value from stack
      // ----------------------------------------------------------------------
      // Falls straight through (no ';') into /1@, then into /@ -- see the note
      // on fall-through below. Combined, /pop/1@/@ read one word from
      // extended-page memory at the address formed from p, advancing p by one
      // so the caller can keep reading sequential words; 'dup >r' keeps a copy
      // of p on the return stack while 1/!b/!b/@b (borrowed from the words it
      // falls into) perform the actual page-1-relative fetch through B.
      : /pop ( s-sw) dup >r 1 . + r>

      // ----------------------------------------------------------------------
      // /1@ ( a-w)  --  fetch word from page 1
      // ----------------------------------------------------------------------
      // This word is just the literal page number 1, immediately followed
      // (fall-through, no ';') by /@'s body below -- so "1" supplies the page
      // argument that /@'s '!b !b @b' addresses against. /1@ is never entered
      // on its own from outside this file except via fall-through from /pop.
      : /1@ 1

      // ----------------------------------------------------------------------
      // /@ ( ab-w)  --  fetch word from extended address
      // ----------------------------------------------------------------------
      // Store a, store b, fetch through b (!b !b @b) -- a plain extended-address
      // fetch, entered from the top via /pop -> /1@ fall-through (page 1, the
      // parameter/return area) or directly from /next below (page 0, this
      // node's own code space). Since /cx? (compare-and-exchange) was removed,
      // this word no longer needs the shared 'then' landing point an earlier
      // version used for /cx?'s own 'ahead' jump -- it is a plain, single-entry
      // definition now.
      : /@ ( ab-w) !b !b @b ;

      // ----------------------------------------------------------------------
      // /next ( s-sx)  --  read next instruction word
      // ----------------------------------------------------------------------
      // Fetches the word at P from PAGE 0 -- this node's own code space -- then
      // advances P by one so the next call to /next continues with the
      // following word. 'a' pushes P; 'dup 1 . + a!' duplicates it and stores
      // the duplicate's successor back into A, leaving the ORIGINAL (pre-
      // advance) P on the stack; 'dup dup xor' turns that lone P into the (page
      // 0, address P) pair /@ needs (duplicating P and XOR-ing the duplicate
      // with itself always yields 0, the page number, without disturbing P
      // itself). This is what lets 'nop simply chain '/next ... exec 'nop' in a
      // loop, always reading the CODE page rather than the page-1
      // parameter/return area /pop/1@/@ read through.
      : /next ( s-sx) a dup 1 . + a! dup dup xor /@ ;

      // ----------------------------------------------------------------------
      // 'plit ( s-s)  --  push next literal onto stack
      // ----------------------------------------------------------------------
      // CVM opcode 0x800E (compiles to word address 0x00E). Simply reuses
      // /next to fetch the very next instruction word and push it as a literal
      // value onto the CVM's data stack (S, i.e. this node's own hardware
      // stack) -- the fetched word IS the literal. The leading '.loc' is a
      // debug-only, no-op compiler directive (F18Compiler.ShowLocation) that
      // reports this word's own compiled address in the build log; it emits no
      // code and has no effect on the compiled program.
      : 'plit .loc ( s-s) /next // push literal

      // ----------------------------------------------------------------------
      // /push ( pt-p)  --  push value onto stack
      // ----------------------------------------------------------------------
      // Falls through (no ';') into /1! below. Combined, /push/1! store t at
      // the extended address one below p (>r -1 . + r> over: move p to the
      // return stack, decrement it, bring it back), i.e. push t onto the CVM's
      // extended-memory return/parameter area addressed via p, leaving the
      // decremented p as the new p. Same debug-only '.loc' directive as 'plit
      // above.
      : /push .loc ( sw-s) >r -1 . + r> over

      // ----------------------------------------------------------------------
      // /1! ( wa)  --  store word in page 1
      // ----------------------------------------------------------------------
      // Supplies the page-1 argument (literal 1) to /!, falling straight
      // through (no ';') into it -- the write-side counterpart of /1@, used the
      // same way via fall-through from /push.
      : /1! 1

      // ----------------------------------------------------------------------
      // /! ( wab)  --  store word at extended address
      // ----------------------------------------------------------------------
      // Mirrors /@'s addressing but for a write: both the address components
      // are sent inverted (AN003's write convention, 'inv' applied twice) ahead
      // of the final plain '!b' that actually deposits the word through B.
      : /! ( wab) inv !b inv !b !b ;

      // ----------------------------------------------------------------------
      // 'lit ( s-s)  --  store next literal in register r
      // ----------------------------------------------------------------------
      // CVM opcode 0x8014 (compiles to word address 0x014). Despite the name,
      // this implements the CVM's "load literal into r" instruction (llit in
      // the reference table): it reuses /next to fetch the following
      // instruction word, exactly like 'plit -- the difference between "push
      // as literal onto S" and "load into r" is realised entirely by what the
      // caller (exec's dispatch) does with the fetched value afterwards.
      : 'lit ( s-s) /next

      // ----------------------------------------------------------------------
      // /r! ( sw-s)  --  store top of stack in register r
      // ----------------------------------------------------------------------
      // Pushes a non-zero request tag (3, duplicated) ahead of the value to
      // store, then hands control to node 507 via the up port (---u) --
      // substituted here from the original '-d--' since register r lives on
      // 507, reached via up, not on 707 (down). By AN003 convention a non-zero
      // leading word marks a write rather than a read.
      : /r! ( sw-s) 3 dup ( swxy) ---u ;

      // ----------------------------------------------------------------------
      // 'pop ( s-s)  --  CVM instruction: pop
      // ----------------------------------------------------------------------
      // CVM opcode 0x8018 (compiles to word address 0x018). Pops p (i.e. reads
      // and advances the parameter/return-area pointer via /pop) and stores
      // the popped value into register r via /r!. Per Stefan: pop a value from
      // stack and writes it to register r.
      : 'pop .loc ( s-s) /pop /r! ;

      // ----------------------------------------------------------------------
      // 'push ( s-s)  --  CVM instruction: push
      // ----------------------------------------------------------------------
      // CVM opcode 0x801A (compiles to word address 0x01A). Fetches r's
      // current value from 507 (/r@) and pushes it onto the extended-memory
      // parameter/return area via /push. Per Stefan: push the register r onto
      // the stack -- does not change register r.
      : 'push .loc ( s-s) /r@ /push ;

      // ----------------------------------------------------------------------
      // exec ( sxy-s)  --  interpret instruction
      // ----------------------------------------------------------------------
      // Decodes one already-fetched CVM opcode by successively testing and
      // shifting out its high bits (each '2* -if ... then' tests and consumes
      // the current top bit). Four cases, matching the opcode-table's
      // high-level grouping in cvm_2.txt:
      //
      //   1) 11??_????_????_????: hand control to node 507 via the up port
      //      (---u; see the header remarks above on why this is '---u' and not
      //      '-d--', the bug that made 'slit hang). This is the entire
      //      0xC000-0xFFFF class -- short literals such as 'slit, the ALU, and
      //      register w/t/d forwarding -- 507 completes the operation and
      //      returns.
      //   2) 101?_????_????_????: a further bit test chooses between handing
      //      control left to 608 (1011, register g / globals, --l-) or right to
      //      606 (1010, register f / frame, r---).
      //   3) 100?_????_????_????: a further bit test on the r-relative family --
      //      1001_1??? checks whether r (via /r@) is zero, discarding the three
      //      top stack items and returning if so (opcodes in this sub-family
      //      only make sense when r holds something); otherwise (1001_0???) the
      //      value is shifted down (seven 2/'s) and combined with the base
      //      address of the specialised-word jump table (via 'a . + a!') to
      //      jump directly to the matching 'xxx word below -- this is the
      //      mechanism that makes each of 'ret, 'xs, 'xp, 'tjmp, 'pc reachable
      //      by opcode value alone, exactly as confirmed by their compiled
      //      addresses matching the reference opcode table's low bits.
      //   4) 1000_????_????_????: none of the above -- the opcode is a plain
      //      local-jump-table index (this is the same tail as case 3's final
      //      branch, reached here with a already primed) -- drop the remaining
      //      tag and return to the caller (drop >r ;) so 'nop resumes with a
      //      already set to the target address.
      : exec ( sxy-s)
      	2* -if  // 11??_????_????_????
      		( sxy) ---u ;
      	then
      	// 10??_????_????_????
      	2* -if  // 101?_????_????_????
      		2* -if // 1011_????_????_????
      			( sxy) --l- ;
      		then // 1010_????_????_????
      			( sxy) r--- ;
      	then
      	( sxy) // 100?_????_????_????
      	2* -if  // 1001_????_????_????
      		2* -if // 1001_1???_????_????
      			( sxy) /r@
      			if drop drop drop ; then drop
      		then  // 1001_0???_????_????
      			( sxy) >r drop r> 2* 2/ 2/ 2/ 2/ 2/ 2/ 2/ a . + a! ;
      	then // 1000_????_????_????
      	( sxy) drop >r ( s) ;

      // ----------------------------------------------------------------------
      // 'ret ( s-s)  --  return from call
      // ----------------------------------------------------------------------
      // CVM opcode 0x802E (compiles to word address 0x02E). Pops the saved
      // return address (via /pop, which advances/reads the parameter/return
      // area) and installs it directly into A -- i.e. into P -- resuming the
      // CVM program at the popped address.
      : 'ret ( s-s) /pop a! ;

      // ----------------------------------------------------------------------
      // 'xs ( s-s')  --  exchange r with s
      // ----------------------------------------------------------------------
      // CVM opcode 0x8030 (compiles to word address 0x030). Saves r's current
      // value on the return stack (>r /r@), then stores the popped value back
      // into r via /r! -- swapping the top of the CVM's data stack (S, this
      // node's own native stack) with register r.
      : 'xs ( s-s') >r /r@ r> /r! ;

      // ----------------------------------------------------------------------
      // 'xp ( s-s)  --  exchange r with p
      // ----------------------------------------------------------------------
      // CVM opcode 0x8032 (compiles to word address 0x032). Fetches r's
      // current value into A (becoming the new P), then stores the old P
      // (still sitting where /r@ left the request pair) back into r via /r! --
      // swapping P and r.
      : 'xp ( s-s) a /r@ a! /r! ;

      // ----------------------------------------------------------------------
      // 'tjmp ( s-s)  --  table jump
      // ----------------------------------------------------------------------
      // CVM opcode 0x8034 (compiles to word address 0x034). Adds r's value to
      // the current P (a /r@ +), fetches the instruction word at that computed
      // address via /next, adds it again, and installs the result into A --
      // i.e. P jumps to base-plus-offset-plus-fetched-displacement, the
      // classic computed-table-jump idiom.
      : 'tjmp ( s-s) a /r@ + /next + a! ;

      // ----------------------------------------------------------------------
      // 'pc ( s-s)  --  move p to r
      // ----------------------------------------------------------------------
      // CVM opcode 0x8037 (compiles to word address 0x037). Reads the current
      // P out of A and stores it into register r via /r!.
      : 'pc ( s-s) a /r! ;

      // ----------------------------------------------------------------------
      // /call ( sxy-s)  --  call subroutine
      // ----------------------------------------------------------------------
      // Discards the decode tag, saves the current P on the return stack,
      // pushes it onto the extended-memory parameter/return area via /push
      // (the call's return address), then installs the target address (r>,
      // left on the stack by the caller -- see 'nop below) into A as the new
      // P. Deliberately has no ';': execution falls straight through into
      // 'nop immediately below, continuing the fetch/decode loop with P
      // already pointing at the called subroutine's first instruction.
      : /call ( sxy-s) drop >r a /push r> a!

      // ----------------------------------------------------------------------
      // 'nop ( s-s)  --  the CVM's fetch/decode/execute loop, AND its own
      // no-op opcode's target -- this node's `entry` point
      // ----------------------------------------------------------------------
      // Fetches the next instruction word (/next), duplicates it and shifts it
      // left twice -- NOT to test two separate bits. The CVM's own word is only
      // 16 bits wide, while this F18 core's words are 18 bits wide, so a
      // fetched CVM word sits low in its 18-bit register; shifting left by
      // exactly that 2-bit gap is what brings the CVM opcode's own single
      // most-significant bit (bit 15, the tag bit) up into the position where
      // -if/-until can actually test it. '# /call' overrides the branch target
      // actually taken so that, while that one bit tests false (-until) -- i.e.
      // the fetched word's own bit 15 is clear -- the loop routes the fetched
      // word through /call (i.e. treats it as a subroutine-call target address
      // rather than a tagged opcode) -- once the bit tests true, control
      // instead falls into 'exec' to decode the word as an ordinary tagged CVM
      // opcode, then loops back to 'nop.
      //
      // Naming this loop 'nop (rather than the earlier 'main) is deliberate,
      // not cosmetic: jumping to the top of this loop and letting it fetch the
      // next instruction, with nothing else done first, already has zero
      // side effects beyond "keep running" -- exactly the CVM's own `nop`
      // opcode needs. So 'nop's own compiled address IS the opcode
      // (0x8000 | address, 0x803B on this build) a hand-written `nop` in the
      // CVM assembly language resolves to, with no separate, genuinely-empty
      // word required. It is also this node's `entry` point, since cold start
      // is nothing more than "begin the fetch loop for the first time".
      : 'nop ( s-s) /next dup 2* 2* # /call -until exec 'nop ;

      (
      'plit push literal to stack
      'lit store literal to register r
      'pop pop a value from stack and writes it to register r.
      'push push the register r onto the stack. does not change register r.
      )
      """;
}