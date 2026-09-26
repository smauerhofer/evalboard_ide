using System.Text;

namespace Ga144.C.Toolchain;

/// <summary>
/// Walks a <see cref="CTranslationUnit"/> and emits CVM assembly text (the same <c>.casm</c> syntax
/// <c>CvmAssembler</c> accepts) implementing it. There is no separate type-checking pass and no
/// annotated tree: <see cref="EmitExpr"/> computes (and checks) each expression's <see cref="CType"/>
/// in the same walk that emits its code.
///
/// <para><b>CVM C ABI v1 -- read this before touching any codegen method below.</b> None of the
/// following is confirmed against real CVM hardware or a full node-506/606/607 simulation; it is this
/// compiler's own necessary, documented hypothesis about how the primitives it emits actually behave at
/// runtime, chosen as the most standard interpretation for a stack machine of this shape. If any part of
/// it turns out wrong once CVM2's frame/interpreter nodes are simulated in full, the fix is localized to
/// the handful of methods named in each bullet:</para>
/// <list type="bullet">
/// <item><description><c>pushlit N</c> pushes literal N as a new stack top (stack depth +1).</description></item>
/// <item><description><c>pop</c> pops the stack top into register r (stack -1). <c>push</c> pushes r
/// back onto the stack non-destructively, leaving r unchanged (stack +1) -- so "pop; push; push"
/// duplicates the top of stack ([...,v] -&gt; pop: r:=v,[...] -&gt; push: [...,v] -&gt; push: [...,v,v]).
/// See <see cref="EmitDup"/>.</description></item>
/// <item><description>Every binary ALU/comparison op (add, sub, and, xor, or, usl, ssr, usr, eq, ne, lt,
/// gt, le, ge, ult, ugt, ule, uge) consumes r (set by an immediately preceding <c>pop</c>) and the
/// current stack top, and replaces the stack top with the result -- so "EmitExpr(lhs); EmitExpr(rhs);
/// pop; &lt;op&gt;" computes "lhs OP rhs" with the correct operand order and leaves exactly one result
/// word. See <see cref="EmitBinaryOperation"/>.</description></item>
/// <item><description>Every unary op (inv, negate, eq0) acts on r alone: "pop; &lt;op&gt;; push" pops the
/// operand into r, transforms r in place, and pushes the result back (net: pop 1, push 1). See
/// <see cref="EmitExpr"/>'s <see cref="CUnaryExpr"/> case.</description></item>
/// <item><description><c>xt</c>/<c>ldt</c>/<c>stt</c> are this ABI's generic pointer/memory primitives:
/// <c>xt</c> pops an address off the stack into a "t" register; <c>ldt</c> pushes the value stored at
/// address t without consuming t; <c>stt</c> pops a value off the stack and stores it at address t.
/// IMPORTANT: "t" has no save/restore of its own, so this compiler never assumes it survives arbitrary
/// code emitted between resolving an Indirect lvalue's address and using it -- every Indirect lvalue's
/// address is cached into its own dedicated local slot the moment it's computed, and <c>xt</c> is
/// re-issued from that cached slot immediately before every single load or store through it, however
/// many other things (including further "xt"s of their own) ran in between. See
/// <see cref="CLvalue"/>/<see cref="ResolveLvalue"/>/<see cref="CacheIndirectAddress"/>/
/// <see cref="EmitLoad"/>/<see cref="EmitStore"/>.</description></item>
/// <item><description><c>ldl</c>/<c>ldp</c> load a local's/parameter's value into register r WITHOUT
/// touching the stack; <c>stl</c>/<c>stp</c> store r INTO a local/parameter, also without touching the
/// stack -- CONFIRMED against real hardware 2026-09-09 from Stefan's own opcode bit patterns (<c>ldl</c>
/// <c>1001_111?_????_????</c>, <c>ldp</c> <c>1001_110?_????_????</c>, <c>stl</c>
/// <c>1001_101?_????_????</c>, <c>stp</c> <c>1001_100?_????_????</c>, each a 9-bit frame-relative
/// offset): "locals and parameters are stored on the stack relative to the frame pointer and are NOT
/// accessed with push and pop." This OVERTURNS this class's former, never-confirmed and now-known-wrong
/// assumption that these four worked directly against the data stack like <c>xt</c>/<c>ldt</c>/<c>stt</c>
/// do. They behave like the generic ALU/unary ops instead: every load site must follow with an explicit
/// <c>push</c> to move the loaded value from r onto the stack, and every store site must precede with an
/// explicit <c>pop</c> to move the value-to-store from the stack into r first -- see
/// <see cref="EmitLoad"/>/<see cref="EmitStore"/>/<see cref="EmitName"/>/
/// <see cref="CacheIndirectAddress"/>/<see cref="EmitSwitch"/> and the register-parameter spill in
/// <see cref="EmitFunction"/>, all fixed together for this. <c>lal</c>/<c>lap</c> (load ADDRESS of a
/// local/parameter, a different pair of mnemonics) are RETIRED as of 2026-09-09 ("'lal' &amp; 'lap' are
/// removed. use ''f' or 'fpush' from node 506 and add the offset to calculate the address of a local or
/// parameter." -- quoted verbatim as originally given; node 506's own push word was renamed from
/// <c>'fpush</c> to <c>'pushf</c> on 2026-09-16, see below) -- this compiler no longer emits them at all.
/// In their place, computing the address of a local or parameter now emits <c>pushf</c> (push node 506's
/// own frame pointer <c>f</c> directly onto the data stack) followed by
/// <c>pushlit &lt;offset&gt;; pop; sub</c> for a LOCAL (address = f - offset,
/// matching node 506's own <c>f/main</c> dispatch, which negates a local's offset with <c>inv</c> before
/// adding) or <c>pushlit &lt;offset&gt;; pop; add</c> for a PARAMETER (address = f + offset, no
/// negation) -- see <see cref="EmitAddressOf"/>'s <c>CNameExpr</c> case and <see cref="EmitName"/>'s
/// array-decay branches, both updated together for this. This has NOT been confirmed against real
/// hardware (no fresh self-check has been run since the change), so treat it with the same caution as
/// every other "not yet confirmed" item in this list.</description></item>
/// <item><description><c>cbr &lt;label&gt;</c> (formerly named <c>ifbr</c> -- Stefan, 2026-09-09: "ifbr"
/// no longer exist and should be replaced with "cbr". it is basically the same. opcode cbr
/// 1010_11??_????_???? conditional branch to offset if r == 0. the offset is signed 10-bit number.)
/// branches when the condition (loaded into r by an immediately preceding <c>pop</c>) is ZERO (false).
/// CONFIRMED against real hardware, and the OPPOSITE polarity from this class's own former, never-
/// confirmed assumption (the old "ifbr" placeholder was assumed to branch on NONZERO/true) -- every
/// control-flow codegen method below was originally built around that wrong polarity and has been
/// re-derived for this one instead. The net effect actually simplifies most call sites: "branch away
/// from a block when its condition is false" (if/while/for/ternary's else-branch, &amp;&amp;'s short
/// circuit) now needs no extra op at all, since <c>cbr</c> already tests exactly that; "branch when the
/// condition is true" (do-while's loop-back, a switch case match, ||'s short circuit) now needs an
/// explicit <c>eq0</c> first to invert the sense before branching. See <see cref="EmitStatement"/>'s
/// <c>CIfStmt</c>/<c>CWhileStmt</c>/<c>CDoWhileStmt</c>/<c>CForStmt</c> cases, <see cref="EmitLogical"/>,
/// <see cref="EmitConditional"/>, and <see cref="EmitSwitch"/>, all re-derived together for
/// this.</description></item>
/// <item><description><b>Uniform codegen invariant:</b> every non-void expression's codegen leaves
/// EXACTLY one word on the data stack representing its value; a void-typed expression (a call to a void
/// function) leaves nothing. An expression used as a statement emits the expression, then a bare
/// <c>pop</c> to discard the leftover value when its type isn't void. This single invariant is what lets
/// every expression case in <see cref="EmitExpr"/> compose with every other one with no "is this value
/// needed" threading anywhere in the compiler, at the modest cost of an occasional extra
/// <c>pop</c>/dup.</description></item>
/// <item><description><b>Function calling convention:</b> the caller evaluates and pushes each argument
/// left-to-right, then <c>call</c>s; the callee's <c>enter &lt;n&gt;</c> reserves n local slots and is
/// assumed to make the caller's pushed arguments addressable via <c>ldp</c>/<c>stp</c> (or, for a
/// parameter's ADDRESS rather than its value, the <c>pushf</c>-based sequence described above, now that
/// <c>lap</c> is retired) (parameter offset 0 = the first declared parameter); <c>return expr;</c> evaluates expr (leaving its
/// value on the stack) and branches to the function's epilogue label; the epilogue's <c>leave</c> is
/// assumed to deallocate all locals AND the caller's pushed arguments together, leaving only the single
/// return value (if any) on top -- a Pascal-style callee-cleanup convention -- followed by <c>ret</c>.
/// See <see cref="EmitFunction"/>.</description></item>
/// <item><description><b>RENUMBERED 2026-09-15: every "node 306" below is physical node 308.</b> Stefan:
/// "node 306 and 308 have swapped roles" -- the 32-bit address-register node this whole ABI v2 section
/// describes has not changed shape or mnemonics (still <c>arld</c>/<c>lda</c>/<c>sta</c>/etc., still
/// wired the same way in <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet"/>/
/// <see cref="Ga144.Evb.Ide.Services.CvmAssemblyLanguage"/>), only its coordinate moved, from 306 to 308
/// -- see <see cref="Ga144.Evb.Ide.Cvm.Node308Program"/>'s own remarks. This codegen never hardcodes the
/// coordinate itself (it emits mnemonics, resolved against whichever node currently owns them), so no
/// code change was needed here, only this flag; the "node 306" wording throughout the remarks below is
/// left as Stefan originally wrote/was quoted saying it, describing the mechanism, not literally today's
/// coordinate.</description></item>
/// <item><description><b>ABI v2 addition (2026-09-06): address-register pointer parameters.</b> Per
/// Stefan's own node 306 source ("In 306 there are 4 32-bit address register to access the whole memory
/// range... for the ABI the address register are volatile and are not saved on the stack when a
/// function is entered. they are also used for the first 4 pointers as arguments to a function"): among
/// a function's PARAMETERS, in declaration order, the first four whose TYPE is a pointer (an array
/// parameter already decays to pointer by the time <see cref="CParser"/> hands it here -- see
/// <see cref="CParameter"/>) are passed via node 306's address registers 0-3 instead of the ordinary
/// push-onto-the-stack convention above; every OTHER parameter (a 5th-or-later pointer, or any
/// non-pointer parameter, wherever it falls among the four) keeps ABI v1's stack convention completely
/// unchanged, just renumbered to skip the register-passed ones (see
/// <see cref="ComputeParameterRegisterSlots"/>). This is a pure CALLING-CONVENTION change, nothing else:
/// pointer TYPE and SIZE are UNCHANGED (still exactly one CVM word each -- this compiler does not yet
/// support the "whole memory range"/far/32-bit addressing node 306's own registers make possible; every
/// pointer this compiler emits still addresses only the current node's own local page, with an implicit
/// page word of 0 -- a known, deliberate gap, not yet resolved with Stefan), and pointer DEREFERENCE
/// (<c>*p</c>, <c>p[i]</c>) still goes through the existing <c>xt</c>/<c>ldt</c>/<c>stt</c> mechanism
/// above, completely untouched.
///
/// Mechanically: at a CALL SITE (<see cref="EmitCall"/>), a register-eligible argument is still
/// evaluated in its normal left-to-right position (leaving one stack word, per the uniform invariant
/// below) but is then immediately popped into r, paired with a literal page word of 0, and loaded into
/// its assigned address register via <c>lda</c> -- never left on the stack for the callee. At the
/// CALLEE'S OWN PROLOGUE (<see cref="EmitFunction"/>), right after <c>enter &lt;n&gt;</c>, each
/// register-eligible parameter is immediately spilled OUT of its address register (<c>sta</c>) into an
/// ordinary LOCAL slot (not a Parameter-kind frame offset -- it never arrived via the stack, so it is
/// allocated and referenced exactly like any other <see cref="CLvalueKind.Local"/> variable for the rest
/// of the function body, needing no new <see cref="CLvalueKind"/> or <see cref="CVarKind"/> at all).
/// Address registers are NEVER trusted to persist beyond that one transient moment (call-site transmit,
/// or prologue spill) -- exactly the same discipline this ABI already applies to the "t" register above,
/// for the same reason (volatile, no save/restore of its own, and here explicitly confirmed by Stefan:
/// "not saved on the stack when a function is entered"). See <see cref="ComputeParameterRegisterSlots"/>,
/// <see cref="EmitFunction"/>, and <see cref="EmitCall"/> for the exact instruction sequences.
/// </description></item>
/// <item><description><c>*</c>/<c>/</c>/<c>%</c> have no hardware primitive, so they compile to calls
/// into runtime helper routines Stefan implements by hand in the "cvm" assembly library:
/// <c>__mul</c>, <c>__divs</c>/<c>__divu</c> (signed/unsigned divide), <c>__mods</c>/<c>__modu</c>
/// (signed/unsigned modulo). Calling convention for these leaf helpers: caller pushes lhs then rhs
/// (left-to-right), <c>call</c>s, callee consumes both top-of-stack words and pushes exactly one result
/// word, then <c>ret</c>s -- no frame needed since they're pure leaves. See
/// <see cref="EmitLibraryBinary"/>.</description></item>
/// <item><description><c>&lt;&lt;</c>/<c>&gt;&gt;</c> need no library call: node 507's <c>usl</c>/
/// <c>ssr</c>/<c>usr</c> are binary (value, count), so they use the ordinary pop+op pattern
/// directly.</description></item>
/// <item><description>Every scalar and pointer type this compiler supports is exactly one CVM word
/// (see <see cref="CType.SizeInWords"/>'s own remarks) -- so pointer arithmetic and array indexing never
/// need a scaling multiplication; <c>arr[i]</c> is simply "base address + i" via a plain
/// <c>add</c>.</description></item>
/// <item><description><b>Optimizer addition (2026-09-10): constant-indexed local array elements skip
/// address computation entirely.</b> <c>arr[K]</c> for a compile-time-constant <c>K</c> on a genuinely
/// LOCAL array is just the local at slot <c>arr's own base slot + K</c> -- loadable/storable with a plain
/// <c>ldl</c>/<c>stl</c>, no runtime address, no node-306 indirection, no <see
/// cref="CvmPeepholeOptimizer"/> toggle needed (it relies on nothing but this compiler's own local-slot
/// layout and the already-confirmed <c>ldl</c>/<c>stl</c> addressing every other local already uses, so it
/// is unconditional). See <see cref="TryGetConstantLocalArrayElementSlot"/>'s own remarks for the full
/// reasoning and scope.</description></item>
/// <item><description><b>Optimizer addition (2026-09-10): global-scalar access uses <c>gld</c>/<c>gst</c>
/// directly, no address-register dance.</b> A plain module-scope or <c>static</c> global used to be
/// treated exactly like a runtime pointer -- push its label, cache the "address" into a spilled local
/// slot, then indirect through node 306's <c>arst</c>/<c>lda</c>/<c>sta</c> on every access -- even
/// though the label is already a fixed, compile-time-constant reference the assembler/linker resolve via
/// a relocation. It now goes straight through <c>gld</c> (fetch: r := global) / <c>gst</c> (assign:
/// global := r), named and behaving exactly like an ordinary "load"/"store" -- see <see
/// cref="EmitGlobalFetch"/>/<see cref="EmitGlobalAssign"/>'s own remarks for the real hardware bug this
/// direction was hiding (node 508's own <c>g/@</c>/<c>g/!</c> primitives really were swapped) and
/// Stefan's 2026-09-10 hardware trace that caught and corrected it. Unconditional, like the array
/// optimization above: no case this recognizes (a compile-time-known global label) can ever need the
/// general address-computation
/// path.</description></item>
/// <item><description><c>switch</c>/<c>case</c> does NOT use a real <c>tjmp</c> (table jump) instruction:
/// its LOCATION is now confirmed (node 507, "'tjmp is in node 507", 2026-09-09 -- the same
/// "1000_1???" local-execute tag family as <c>nop</c>/<c>push</c>/<c>pop</c>/<c>ret</c>/<c>halt</c>, per
/// <see cref="CvmInstructionSet.TableJumpMnemonic"/>'s own remarks), but its calling convention -- how a
/// case table is laid out in memory, how the selector is bounds-checked and turned into an index, and
/// whether/how out-of-range selectors fall through to a default -- is still not confirmed anywhere in the
/// project's documentation, and this compiler follows the project's own established practice of never
/// guessing an unconfirmed hardware/table convention. Instead, a switch still compiles to a
/// straightforward, always-correct chain of equality comparisons against the selector (evaluated once
/// into a compiler-allocated temporary local). This is flagged here as a future optimization pending
/// Stefan supplying <c>tjmp</c>'s real calling convention -- see <see cref="EmitStatement"/>'s
/// <see cref="CSwitchStmt"/> case.</description></item>
/// <item><description>
/// <b>Float ABI (added 2026-09-26) -- basic <c>float</c> support, per Stefan's own dictation
/// (<c>claude/cvm-abi.md</c> section 2.3).</b> Everything above this bullet assumes every value is
/// exactly one CVM word; <c>float</c> (see <see cref="CType.Float"/>) is the first exception, and it does
/// NOT go through the ordinary one-word data-stack machinery at all -- it is evaluated entirely in node
/// 305's own 8-register floating-point file (<c>fr[0..7]</c>) via <c>fadd</c>/<c>fsub</c>/<c>fmul</c>/
/// <c>fdiv</c>/<c>fneg</c>/<c>fabs</c>/<c>fmin</c>/<c>fmax</c>/<c>fmove</c>/<c>fpop</c>/<c>fpush</c>, and
/// only ever touches the ordinary stack/memory to load/store a <c>float</c> variable's two words or to
/// marshal a stack-passed <c>float</c> argument.
/// <list type="bullet">
/// <item><description><b>Scope (a deliberate SCOPE decision for this first pass, not a hardware
/// hypothesis):</b> a plain <c>float</c> scalar only -- local, parameter, global, literal, <c>+</c> <c>-</c>
/// <c>*</c> <c>/</c> unary <c>+</c>/<c>-</c>, assignment (<c>=</c> <c>+=</c> <c>-=</c> <c>*=</c> <c>/=</c>),
/// <c>return</c>, and calls (as an argument or return value). NOT supported, rejected with a diagnostic:
/// <c>float *</c>/<c>float[]</c> (see <see cref="CType.Float"/>'s own remarks -- rejected in
/// <see cref="CParser"/>), comparisons (<c>==</c> <c>!=</c> <c>&lt;</c> <c>&gt;</c> <c>&lt;=</c>
/// <c>&gt;=</c>), <c>++</c>/<c>--</c>, any int/<c>float</c> conversion (implicit or an explicit cast), and
/// a <c>float</c> literal used directly where an <c>int</c> is expected or vice versa -- Stefan: "only add
/// functions that are directly supported by the FP subprocessor... later we will add more functions in a
/// float-library." Three built-ins expose the three hardware ops with no C operator of their own --
/// <c>fabsf</c>/<c>fminf</c>/<c>fmaxf</c> compile directly to <c>fabs</c>/<c>fmin</c>/<c>fmax</c>, never a
/// real call (see <see cref="TryGetFloatBuiltin"/>).</description></item>
/// <item><description><b>Codegen model: a depth-counter register allocator, not a stack machine.</b>
/// <see cref="EmitFloatExprInto"/> evaluates a <c>float</c> expression into a chosen destination register
/// <c>fr[destReg]</c>, recursively: a binary op evaluates its left operand into <c>fr[destReg]</c> and its
/// right operand into <c>fr[destReg+1]</c> (never lower than <c>destReg</c>, so an already-computed OUTER
/// operand sitting in a lower-numbered register is never disturbed), then combines them in place
/// (<c>fadd destReg destReg+1</c>, etc., per node 305/306's own <c>fr[f] := fr[f] OP fr[g]</c> semantics --
/// see <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointAddMnemonic"/>'s own remarks). Every
/// register below the CURRENT <c>destReg</c> is, by construction, still needed by an enclosing operation --
/// this is exactly what <see cref="_liveFloatRegisterCount"/> tracks, and it is what
/// <see cref="EmitCall"/> consults to decide how many registers a call needs to protect. Nesting deeper
/// than <see cref="FloatRegisterCount"/> (8) registers is a compiler error ("expression too complex"),
/// not silently wrong code.</description></item>
/// <item><description><b>Loading/storing a <c>float</c> variable's two words -- word order, DERIVED
/// from node 305's own F18 source (<c>fr/push</c>/<c>fr/pop</c>), not yet independently confirmed on real
/// hardware.</b> <c>fr/push (g f)</c> is <c>a! @+ !b @ !b</c> -- it sends the LOW word first, then the
/// HIGH word second; <c>fr/pop (g f)</c> is <c>a! @b !+ @b !</c> -- the FIRST word it reads is stored into
/// the LOW slot, the SECOND into the HIGH slot. Combined with Stefan's own general rule ("low word at
/// memory[adr], high word at memory[adr+1]"), this compiler always composes/decomposes a register the
/// same way: to LOAD <c>fr[d]</c> from two known words, push the LOW word then the HIGH word (in that
/// order) then <c>fpop d</c>; to STORE <c>fr[d]</c> out, <c>fpush d</c> then pop twice -- the first pop is
/// the HIGH word, the second is the LOW word. See <see cref="EmitFloatLoad"/>/<see cref="EmitFloatStore"/>/
/// <see cref="EmitFloatLiteralInto"/>. A bare <c>fpush f</c> ... (nothing else touching the stack)
/// ... <c>fpop f</c> pair (used ONLY for caller-save/restore, see below) is trusted to round-trip
/// <c>fr[f]</c>'s exact value -- this is the more likely intended reading of "the caller must save the
/// register... and restore them," but note it is NOT mechanically re-derivable from the same F18 trace
/// above without also assuming something about how <c>@b</c>/<c>!b</c> interact with whatever real stack
/// backs them; flagged for Stefan's confirmation.</description></item>
/// <item><description><b>A <c>float</c> LOCAL's two frame slots are HIGH-then-LOW, the opposite order
/// from a <c>float</c> PARAMETER or a <c>float</c> GLOBAL's two words.</b> A local's own address is
/// <c>f - offset</c> (see this class's own <c>pushf</c>/<c>lal</c>/<c>lap</c> remarks above) -- ADDRESS
/// DECREASES as the frame-slot offset increases -- so honoring "low word at the lower address" means the
/// LOW word must live at the HIGHER slot number (<c>baseSlot+1</c>) and the HIGH word at the base slot
/// itself. A PARAMETER's address is <c>f + offset</c> (increases with offset) and a GLOBAL's second word
/// is a second, immediately-following data-section label (see below) -- both the natural, unsurprising
/// order (LOW at the base, HIGH at base+1). See <see cref="EmitFloatLoad"/>/<see
/// cref="EmitFloatStore"/>.</description></item>
/// <item><description><b>A <c>float</c> GLOBAL is two contiguous data words under two labels, not one.</b>
/// <c>gld</c>/<c>gst</c> (see this class's own "global-scalar access" bullet above) take a single label
/// with no address arithmetic, so a <c>float</c> global's high word gets its OWN label,
/// <c>&lt;label&gt;__hi</c>, emitted immediately after the low word's own <c>.word</c> line with nothing
/// else emitted in between (see <see cref="EmitFloatGlobalStorage"/>) -- this compiler controls DATA-
/// section layout entirely, so the two words ARE genuinely contiguous (<c>label__hi</c>'s address really
/// is <c>label</c>'s address + 1), exactly matching Stefan's memory-layout rule; only the SYMBOLIC name
/// for the second word is a compiler-internal convention (a hand-written <c>.casm</c> routine expecting
/// to reach it via "<c>label+1</c>" address arithmetic -- which this assembler does not support for any
/// operand, not just this one -- would not find a symbol for it under that name). A future
/// pointer-to-float/float-array feature will need to revisit this.</description></item>
/// <item><description><b>Return-value convention -- HYPOTHESIS, not stated by Stefan and NOT yet
/// confirmed.</b> This compiler places a <c>float</c>-returning function's return value in <c>fr[0]</c>
/// unconditionally, regardless of calling convention, right before branching to the epilogue -- the
/// simplest, most standard choice for a single dedicated "the" float register, mirroring how a plain
/// (non-<c>__fastcall</c>) int/pointer return already uses one fixed, implicit slot (the stack top). See
/// <see cref="EmitStatement"/>'s <c>CReturnStmt</c> case and <see cref="EmitCall"/>'s own return-value
/// retrieval.</description></item>
/// <item><description><b>Parameter passing, per Stefan verbatim:</b> "for fastcall functions the
/// parameter are passed by register fr[0] for the first float parameter, fr[1] for the second parameter,
/// ... for normal functions float parameter are put on the stack as a 32-bit value little endian word."
/// <see cref="ComputeParameterFloatRegisterSlots"/> assigns fr-registers to a <c>__fastcall</c>
/// function's <c>float</c> parameters, counting ONLY among its own <c>float</c> parameters (a completely
/// separate, independent count from the pointer/ar-register assignment above -- node 305's register file
/// has nothing to do with node 306's). A register-assigned <c>float</c> parameter is spilled into an
/// ordinary local slot pair immediately in the prologue, exactly like a register-assigned pointer
/// parameter already is (see this class's own ABI v2 remarks) -- fr registers are never trusted to
/// survive past that one moment. A stack-passed <c>float</c> parameter's word OFFSET is generalized from
/// the pre-existing per-PARAMETER count to a per-WORD count (<see cref="CType.SizeInWords"/>), since it is
/// no longer true that every stack-passed parameter is exactly one word.</description></item>
/// <item><description><b>Caller-save/restore, per Stefan verbatim:</b> "within a function all fr register
/// can be used freely, that means, calling a function may use some fr register. so calling a function
/// that use fr register, the caller must save the fr register in use to the stack and restore them after
/// the call. for functions that do not use fr register, there is no need to save/restore any fr-register.
/// so the compiler must know if a function directly or indirectly uses any fr-register."
/// <see cref="ComputeFunctionUsesFloatRegisters"/> computes exactly that (a whole-translation-unit,
/// fixed-point call-graph analysis -- DIRECT use is any <c>float</c> parameter/return/local/literal
/// anywhere in a function's own body; INDIRECT use is calling, however deeply, a function that does; a
/// call to a function whose body isn't visible in this file -- only declared, or in another translation
/// unit -- is conservatively treated as "may use fr registers," since nothing here can see its actual
/// body). <see cref="EmitCall"/> consults it for EVERY call (not just one inside a <c>float</c>
/// expression): whenever <see cref="_liveFloatRegisterCount"/> is nonzero and the callee may use fr
/// registers, it wraps the call with <c>fpush 0..fpush (n-1)</c> before and <c>fpop (n-1)..fpop 0</c>
/// after (ascending save, descending restore -- ordinary LIFO stack discipline), retrieving a
/// <c>float</c> return value out of <c>fr[0]</c> into its own destination register BEFORE the restore
/// pops run (since <c>fr[0]</c> is always among the registers being restored whenever the destination is
/// anything other than <c>fr[0]</c> itself).</description></item>
/// </list>
/// </description></item>
/// <item><description>
/// <b>Basic <c>struct</c> support (added 2026-09-26), per Stefan's own "add 'struct' to the C language"
/// instruction.</b> See <see cref="CType.StructOf"/>'s own remarks for the full scope. Unlike
/// <c>float</c>, a struct needs no new expression-evaluation model at all -- every member this compiler
/// supports (scalar, pointer, float, array, or a nested struct accessed no further than its own address)
/// is reached purely through the EXISTING lvalue/address machinery above: a struct local/global/static
/// already gets exactly <see cref="CType.SizeInWords"/> consecutive slots or data words for free (<see
/// cref="DeclareLocal"/>/<see cref="EmitGlobal"/> already generalize over any size, not just 1), and
/// "." / "->" member access (<see cref="CMemberAccessExpr"/>) is just one more address-plus-known-offset
/// computation feeding the SAME <see cref="CacheIndirectAddress"/>/<see cref="EmitDereferenceLoad"/>/
/// <see cref="EmitDereferenceStore"/> sequence <c>*ptr</c> and <c>arr[i]</c> already use -- see <see
/// cref="EmitAddressOfMember"/>. The one part of a struct that DOES still need its own explicit guard is
/// reading/assigning an entire multi-word struct value as if it were the usual single stack word (<see
/// cref="EmitAssign"/>/<see cref="EmitAssignForEffect"/>'s own struct checks, and <see
/// cref="ResolveLvalue"/>'s/<see cref="EmitExpr"/>'s own <see cref="CMemberAccessExpr"/>/<see
/// cref="CIndexExpr"/> cases). A struct is never passed/returned BY VALUE in a function signature (<see
/// cref="CParser"/> rejects that at parse time, so <see cref="CCodeGenerator"/> never even sees one
/// there) -- that is the one struct-by-value scope limit that remains.
/// </description></item>
/// <item><description>
/// <b>Multi-word pointer arithmetic/indexing (added 2026-09-26, the same day, per Stefan's own follow-up
/// request to lift the struct/<c>float</c> pointer scope limits above).</b> Every pointer/array/index
/// computation in this class used to assume every element was exactly one word (no scaling
/// multiplication, ever) -- now <see cref="EmitScaleByElementSize"/>/<see
/// cref="EmitDescaleByElementSize"/> multiply/divide by the pointee's own <see cref="CType.SizeInWords"/>
/// via the same hand-written "__mul"/"__divs" runtime routines "*"/"/" already use, wired into <see
/// cref="EmitAddressOfIndex"/>, <see cref="EmitBinaryOperation"/>'s pointer +/- cases (which is also how
/// <see cref="EmitIncrementOrDecrement"/> automatically got correctly-scaled <c>p++</c>/<c>p--</c>, with
/// no changes of its own), and <see cref="TryGetConstantLocalArrayElementSlot"/>'s constant-index fast
/// path. This is what makes an array of <c>float</c>/<c>struct</c>, a <c>float</c>/array/struct member of
/// a <c>struct</c>, a pointer to <c>float</c>/<c>struct</c>, and pointer arithmetic/indexing on any of
/// those all correct rather than just "no longer rejected." The float ABI itself gained a matching <see
/// cref="CFloatLvalueKind.Indirect"/> lvalue kind (<see cref="CacheFloatIndirectAddress"/>/<see
/// cref="EmitFloatLoadIndirect"/>/<see cref="EmitFloatStoreIndirect"/>) for a dereferenced <c>float
/// *</c>/a <c>float</c> array element/a <c>float</c> struct member, and <see cref="LooksLikeFloatExpr"/>
/// gained a side-effect-free <see cref="TryGetStaticType"/> helper so it (and <see cref="EmitExpr"/>'s own
/// dereference/index/member-access cases) can recognize these as 'float'-typed BEFORE emitting any code.
/// <see cref="ComputeFunctionUsesFloatRegisters"/>'s "does this function use fr registers" scan also
/// needed a safety fix the same day -- see <see cref="ExprUsesFloatDirectly"/>'s own remarks.
///
/// <b>Known limitation, discovered while implementing this (not something this pass attempts to fix):</b>
/// this class's own confirmed rule that "a LOCAL's address is <c>f - offset</c>, DECREASING as the
/// frame-slot offset increases" (see the <c>pushf</c>/<c>EmitLocalOrParameterAddress</c> remarks above)
/// means a multi-word LOCAL aggregate's own words do NOT sit at ascending addresses as its slot number
/// increases, the opposite of every other storage kind (global, heap, a pointer received as a parameter).
/// <see cref="EmitFloatLoadLocal"/>/<see cref="EmitFloatStoreLocal"/> already special-case this for a
/// PLAIN <c>float</c> local (storing it HIGH-then-LOW in slot order specifically so its actual memory
/// order comes out low-then-high), and this pass's own <see cref="EmitAddressOf"/> fix for "&amp;" of a
/// local <c>float</c> does the matching offset-by-one adjustment -- but <see
/// cref="TryGetConstantLocalArrayElementSlot"/>'s general formula and <see cref="EmitAddressOfMember"/>'s
/// offset-add do NOT apply an equivalent correction for a LOCAL array with more than one word per element
/// or a LOCAL struct with more than one word total. This is consistent with (not a regression from) the
/// SAME formula's pre-existing behavior for a plain LOCAL array indexed by a NON-constant expression (its
/// general/runtime address path already composed addresses in the opposite direction from the
/// constant-index fast path, for any element size, before today) -- a real, apparently long-standing gap
/// in this compiler's local-frame addressing model, orthogonal to today's struct/float/scaling work and
/// deliberately NOT investigated or fixed here. It does not affect GLOBAL or heap/parameter-passed
/// pointers (the realistic case for struct-pointer arithmetic -- e.g. the <c>libc</c> <c>heap.c</c>
/// free-list allocator that originally motivated <c>struct</c> support manages heap memory, not a local
/// frame), only a LOCAL array/struct/float variable's own multi-word internal layout.
/// </description></item>
/// </list>
/// </summary>
public sealed class CCodeGenerator
{
  private enum CVarKind
  {
    Local,
    Parameter,
    Global,
  }

  private sealed record CVarSymbol(CVarKind Kind, int Index, CType Type, string? GlobalLabel = null);

  private sealed class CFunctionSignature
  {
    public required CType ReturnType { get; init; }

    public required IReadOnlyList<CType> ParameterTypes { get; init; }

    /// <summary>ABI v2: one entry per parameter, parallel to <see cref="ParameterTypes"/> -- the
    /// address-register index (0-3) a register-eligible pointer parameter is passed in, or null for
    /// every other parameter (which keeps ABI v1's stack convention). See
    /// <see cref="ComputeParameterRegisterSlots"/> and this class's own ABI doc comment.</summary>
    public required IReadOnlyList<int?> ParameterRegisterSlots { get; init; }

    /// <summary>Float ABI (added 2026-09-26): one entry per parameter, parallel to
    /// <see cref="ParameterTypes"/> -- the fr-register index (0-7) a <c>float</c> parameter of a
    /// <c>__fastcall</c> function is passed in, or null for every other parameter (a non-<c>float</c>
    /// parameter, or a <c>float</c> parameter of a NON-<c>__fastcall</c> function, which is always
    /// stack-passed instead -- Stefan verbatim: "for normal functions float parameter are put on the
    /// stack"). A SEPARATE, independent count from <see cref="ParameterRegisterSlots"/>'s own pointer
    /// count -- node 305's floating-point register file is a completely separate 8-register file from
    /// node 306's address registers. See <see cref="ComputeParameterFloatRegisterSlots"/> and this
    /// class's own doc comment.</summary>
    public required IReadOnlyList<int?> ParameterFloatRegisterSlots { get; init; }

    public bool IsDefined { get; set; }
  }

  private sealed class CGlobalSymbol
  {
    public required CType Type { get; init; }

    public required string Label { get; init; }

    public required bool IsStatic { get; init; }
  }

  private enum CLvalueKind
  {
    Local,
    Parameter,
    Global,
    Indirect,
  }

  /// <summary>
  /// The resolved location of an assignable expression. Local/Parameter carry no stack side-effect --
  /// <see cref="Index"/> is just the frame offset for <see cref="EmitLoad"/>/<see cref="EmitStore"/> to
  /// use directly (<c>ldl</c>/<c>stl</c>/<c>ldp</c>/<c>stp</c> take the offset right in the opcode
  /// word). Indirect (<c>*ptr</c> or a non-constant-indexed array element) means <see
  /// cref="ResolveLvalue"/> has already computed the address ONCE and cached it into the local slot
  /// number <see cref="Index"/> refers to (via <see cref="CacheIndirectAddress"/>) -- NOT left sitting
  /// only in the CVM's "r" register, which has no save/restore and does not survive arbitrary code
  /// running in between (see this class's own doc comment). <see cref="EmitLoad"/>/<see
  /// cref="EmitStore"/> each reload that cached address and re-run the full node-306 dereference
  /// sequence (<see cref="EmitDereferenceLoad"/>/<see cref="EmitDereferenceStore"/>) immediately before
  /// using it, so they stay correct no matter what runs between resolving the lvalue and using it.
  ///
  /// Global (added 2026-09-10) is a plain module-scope or <c>static</c> global's own scalar/pointer
  /// storage, addressed directly by its own compile-time-constant assembly <see cref="Label"/> via
  /// <c>gld</c>/<c>gst</c> (see <see cref="EmitGlobalFetch"/>/<see cref="EmitGlobalAssign"/>'s own
  /// remarks) -- unlike Indirect, there is nothing to cache into a local slot at all, because the
  /// label itself (not a runtime-computed address) is already a fixed, permanently-valid reference the
  /// assembler/linker resolve once at link time; <see cref="EmitLoad"/>/<see cref="EmitStore"/> just
  /// re-emit <c>gld</c>/<c>gst</c> against that same label text every time.
  /// </summary>
  private sealed record CLvalue(CLvalueKind Kind, int Index, CType Type, string? Label = null);

  private sealed class CFunctionContext
  {
    public required string Name { get; init; }

    public required CType ReturnType { get; init; }

    public required string EpilogueLabel { get; init; }

    public int NextLocalSlot { get; set; }

    public readonly List<Dictionary<string, CVarSymbol>> Scopes = [];

    public readonly Stack<string> BreakLabels = new();

    public readonly Stack<string> ContinueLabels = new();
  }

  private readonly Dictionary<string, CFunctionSignature> _functions = new(StringComparer.Ordinal);
  private readonly Dictionary<string, CGlobalSymbol> _globals = new(StringComparer.Ordinal);
  private readonly SortedSet<string> _imports = new(StringComparer.Ordinal);
  private readonly Dictionary<string, string> _stringPool = new(StringComparer.Ordinal);
  private int _stringLabelCounter;
  private int _staticLocalCounter;
  private int _labelCounter;
  private readonly List<string> _codeLines = [];
  private readonly List<string> _dataLines = [];
  private readonly List<string> _diagnostics = [];
  private bool _hasError;
  private CFunctionContext? _currentFunction;
  private Dictionary<CStmt, string>? _switchLabelsByNode;

  /// <summary>Float ABI (added 2026-09-26): node 305's own fr-register count -- see this class's own doc
  /// comment's "Float ABI" section.</summary>
  private const int FloatRegisterCount = 8;

  /// <summary>Float ABI: DirectlyOrIndirectlyUsesFloatRegisters per DEFINED function name, computed once
  /// by <see cref="ComputeFunctionUsesFloatRegisters"/> right after <see cref="CollectSignatures"/> runs.
  /// Consulted by <see cref="EmitCall"/> via <see cref="CalleeUsesFloatRegisters"/> (which treats a name
  /// missing here -- an external/only-declared function -- as "true," conservatively, since its body is
  /// not visible to this translation unit).</summary>
  private readonly Dictionary<string, bool> _functionUsesFr = new(StringComparer.Ordinal);

  /// <summary>Float ABI: how many of node 305's fr registers (0.._liveFloatRegisterCount-1) currently
  /// hold a value some ENCLOSING, still-in-progress evaluation needs to survive -- see
  /// <see cref="EmitFloatExprInto"/>'s own remarks. Zero outside of any <c>float</c>-expression
  /// evaluation or <c>float</c>-argument marshalling. <see cref="EmitCall"/> reads this to decide how
  /// many registers a call needs to save/restore around itself.</summary>
  private int _liveFloatRegisterCount;

  /// <summary>Optimizer step 2 of 2, added 2026-09-10 -- see <see cref="CvmPeepholeOptimizer"/>'s own
  /// remarks. Off by default: unlike <see cref="CConstantFolder"/> (pure AST arithmetic, the same
  /// already-used evaluator as this class's own <see cref="TryEvaluateConstantLong"/>), two of this
  /// pass's rules lean on an assumption this codebase has flagged but not yet confirmed against real
  /// hardware (see <see cref="EmitFunction"/>'s own ABI v2 prologue remarks on "stl"/"sta" and register
  /// r) -- so it stays opt-in per project (<c>Ga144.Evb.Ide.Models.CProjectMetadata.EnablePeepholeOptimization</c>)
  /// until Stefan has had a chance to verify it.</summary>
  private readonly bool _enablePeepholeOptimization;

  /// <summary>
  /// Added 2026-09-26, for the C Debugger's own "disassembled code mixed with the corresponding C code
  /// as comments" runtime display (see <see cref="SourceCommentLabels"/>'s own remarks). The top-level
  /// file this generator is compiling, and that file's own raw source text split into lines -- both
  /// null for a caller that doesn't want source-comment labels at all (every pre-existing call site,
  /// via <see cref="CCodeGenerator(bool)"/>'s single-argument overload, still compiles and behaves
  /// exactly as before this feature existed). Only a statement whose OWN <see cref="CSourceLocation.FileName"/>
  /// matches <see cref="_sourceFileName"/> exactly gets a label -- one expanded from a macro defined in
  /// an `#include`d header, for instance, has a DIFFERENT FileName, and deliberately gets no label at
  /// all rather than one pointing at the wrong file's line N (this compiler has no multi-file source
  /// map to draw on here, so "skip it" is the only correct choice, not "guess").
  /// </summary>
  private readonly string? _sourceFileName;
  private readonly IReadOnlyList<string>? _sourceLines;

  /// <summary>Every synthetic per-statement label this generator emitted (see <see cref="EmitSourceLineComment"/>),
  /// mapped to that statement's own trimmed source line text -- e.g. "__srcline_3" -&gt; "a = a + 1;".
  /// Empty when this generator was constructed with no <see cref="_sourceLines"/> to draw from. Each
  /// label is a plain, NEVER-exported (<see cref="EmitLabel"/>, not ".export") local label -- assembled
  /// with <c>Ga144.Cvm.Toolchain.CvmSymbolBinding.Local</c> binding, so it costs nothing at runtime (a
  /// label consumes no word of its own) and is deliberately NOT required to be globally unique across a
  /// whole link (see <c>Ga144.Cvm.Toolchain.CvmImageSymbol.DefiningObjectName</c>'s own remarks for how
  /// a consumer like the C Debugger resolves one of these back to a final address without ambiguity once
  /// several objects, each with their own "__srcline_0", are linked together).</summary>
  public IReadOnlyDictionary<string, string> SourceCommentLabels => _sourceCommentLabels;

  private readonly Dictionary<string, string> _sourceCommentLabels = new(StringComparer.Ordinal);
  private int _sourceCommentCounter;

  public CCodeGenerator(bool enablePeepholeOptimization = false, string? sourceFileName = null, IReadOnlyList<string>? sourceLines = null)
  {
    _enablePeepholeOptimization = enablePeepholeOptimization;
    _sourceFileName = sourceFileName;
    _sourceLines = sourceLines;
  }

  /// <summary>
  /// Emits a synthetic, never-exported label (recorded in <see cref="_sourceCommentLabels"/>) right
  /// before a statement's own generated code, naming the exact C source line it came from -- purely
  /// additive metadata: the label itself is zero words wide, so a compile with nowhere to feed the
  /// result (<see cref="_sourceLines"/> null, or a location outside <see cref="_sourceFileName"/>) can
  /// just skip it with no effect on the generated program either way. Called once per statement from
  /// <see cref="EmitStatement"/>'s own top-level dispatch -- never for a <see cref="CCompoundStmt"/>
  /// itself (its own "location" is just its opening brace, not a statement with code of its own) or a
  /// <see cref="CEmptyStmt"/> (a bare ";", nothing to label).
  /// </summary>
  private void EmitSourceLineComment(CSourceLocation location)
  {
    if (_sourceLines is null || !string.Equals(location.FileName, _sourceFileName, StringComparison.Ordinal))
    {
      return;
    }

    int lineIndex = location.Line - 1;
    if (lineIndex < 0 || lineIndex >= _sourceLines.Count)
    {
      return;
    }

    string text = _sourceLines[lineIndex].Trim();
    if (text.Length == 0)
    {
      return;
    }

    string label = $"__srcline_{_sourceCommentCounter++}";
    EmitLabel(label);
    _sourceCommentLabels[label] = text;
  }

  /// <summary>Generates CVM assembly text for <paramref name="unit"/>. Returns null (with at least one
  /// diagnostic) if any error was reported anywhere during generation; warnings alone still return the
  /// generated text.</summary>
  public (string? Assembly, IReadOnlyList<string> Diagnostics) Generate(CTranslationUnit unit)
  {
    CollectSignatures(unit);

    foreach (CGlobalVarDecl global in unit.Globals)
    {
      EmitGlobal(global);
    }

    foreach (CFunctionDecl function in unit.Functions)
    {
      if (function.Body is not null)
      {
        EmitFunction(function);
      }
    }

    EmitStringPool();

    if (_hasError)
    {
      return (null, _diagnostics);
    }

    var text = new StringBuilder();
    text.AppendLine("; Generated by the GA144 C compiler -- do not edit by hand.");

    foreach (string import in _imports)
    {
      text.AppendLine($".import {import}");
    }

    text.AppendLine(".section DATA");
    foreach (string line in _dataLines)
    {
      text.AppendLine(line);
    }

    // Optimizer step 2 of 2 (see CvmPeepholeOptimizer's own remarks) -- runs over the already-fully-
    // emitted CODE lines, after every "enter N" placeholder above has already been patched to its real
    // value (EmitFunction patches _codeLines by INDEX while a function's body is still being emitted;
    // by the time Generate reaches this point every function is done, so there is no patch left for this
    // pass to disturb). DATA-section lines are never touched -- there is nothing peephole-optimizable in
    // a plain ".word" declaration.
    IReadOnlyList<string> codeLines = _enablePeepholeOptimization ? CvmPeepholeOptimizer.Optimize(_codeLines) : _codeLines;

    text.AppendLine(".section CODE");
    foreach (string line in codeLines)
    {
      text.AppendLine(line);
    }

    return (text.ToString(), _diagnostics);
  }

  private void Error(CSourceLocation location, string message)
  {
    _diagnostics.Add(location.FormatDiagnostic("error", message));
    _hasError = true;
  }

  private string NewLabel(string hint) => $"__L{_labelCounter++}_{hint}";

  /// <summary>
  /// The CVM C ABI's external-symbol naming convention (per Stefan, 2026-09-07: "an external symbol
  /// should prepend an addition '_' character to the label"): every assembly-level symbol that IS
  /// directly a user-written C identifier -- a function's own name, or a global variable's own name --
  /// gets one additional leading underscore prepended on top of whatever the person wrote. A C
  /// function named <c>start</c> becomes the CVM label/export <c>_start</c>; a C function literally
  /// named <c>_start</c> becomes <c>__start</c>. This mirrors the classic C toolchain convention of
  /// prefixing every C-linkage symbol, so a compiled C name can never collide with a hand-written CVM
  /// assembly label that doesn't carry the prefix.
  ///
  /// Applied at every point a function's or global's OWN name becomes literal assembly text: its own
  /// label/".export" line, a ".import" for one only declared (not defined) in this file, a "call"
  /// target, and a "pushlit" reference to a global's address.
  ///
  /// <b>Deliberately NOT applied to:</b>
  /// <list type="bullet">
  /// <item><description>this compiler's own internal, already-mangled labels -- loop/branch labels
  /// (<see cref="NewLabel"/>), the string-literal pool's "__strN" (<see cref="InternStringLiteral"/>),
  /// a "static" local's "__static_&lt;function&gt;_&lt;name&gt;_&lt;n&gt;" (see
  /// <see cref="DeclareLocal"/>) -- none of which is a direct re-emission of a user identifier, so none
  /// needs (or gets) this prefix;</description></item>
  /// <item><description>the hand-written CVM runtime library routine names ("__mul"/"__divs"/
  /// "__divu"/"__mods"/"__modu", see <see cref="EmitLibraryBinary"/>) -- these are this compiler's own
  /// fixed, reserved call targets, not something the person wrote in their C source, so they are
  /// emitted exactly as chosen. <b>Flagged:</b> Stefan has not said whether these should ALSO gain an
  /// extra leading underscore (i.e. call sites emitting "___mul" to match a "cvm" library that defines
  /// it under that name) -- left unprefixed until confirmed, since changing this later is a one-line
  /// fix localized to <see cref="EmitLibraryBinary"/>'s three call sites.</description></item>
  /// </list>
  /// </summary>
  private static string MangleExternalSymbol(string name) => "_" + name;

  private void EmitCode(string line) => _codeLines.Add("  " + line);

  private void EmitLabel(string label) => _codeLines.Add($"{label}:");

  // ---------------------------------------------------------------------------------------------
  // Signature collection -- a first pass over the whole translation unit so a function or global may
  // be referenced before its own declaration appears (ordinary C forward-reference rules), without
  // this compiler needing a separate header/prototype mechanism.
  // ---------------------------------------------------------------------------------------------

  private void CollectSignatures(CTranslationUnit unit)
  {
    foreach (CFunctionDecl function in unit.Functions)
    {
      if (_functions.TryGetValue(function.Name, out CFunctionSignature? existing))
      {
        if (existing.IsDefined && function.Body is not null)
        {
          Error(function.Location, $"function \"{function.Name}\" is already defined");
          continue;
        }

        if (function.Body is not null)
        {
          existing.IsDefined = true;
        }

        continue;
      }

      IReadOnlyList<CType> parameterTypes = function.Parameters.Select(p => p.Type).ToList();
      _functions[function.Name] = new CFunctionSignature
      {
        ReturnType = function.ReturnType,
        ParameterTypes = parameterTypes,
        ParameterRegisterSlots = ComputeParameterRegisterSlots(parameterTypes),
        ParameterFloatRegisterSlots = ComputeParameterFloatRegisterSlots(parameterTypes, function.IsFastcall),
        IsDefined = function.Body is not null,
      };
    }

    foreach (CGlobalVarDecl global in unit.Globals)
    {
      if (_globals.ContainsKey(global.Name))
      {
        Error(global.Location, $"global \"{global.Name}\" is already declared");
        continue;
      }

      _globals[global.Name] = new CGlobalSymbol { Type = global.Type, Label = global.Name, IsStatic = global.IsStatic };
    }

    // Float ABI (added 2026-09-26): a whole-translation-unit fixed-point pass over every DEFINED
    // function's own body, computing which functions use node 305's fr registers -- directly (a float
    // parameter/return/local/literal/expression of their own) or indirectly (calling, however deeply
    // nested, a function that does). See ComputeFunctionUsesFloatRegisters' own remarks; this MUST run
    // after every function's CFunctionSignature above is populated (a call site needs ParameterTypes to
    // even recognize a float argument) but before any function body is compiled (EmitCall consults the
    // result for every call it emits, including a call to a function defined LATER in this file).
    ComputeFunctionUsesFloatRegisters(unit);
  }

  /// <summary>How many of a function's own pointer-typed parameters get an address register (ABI v2) --
  /// node 306 has exactly four (see <see cref="CvmInstructionSet"/>'s own <c>ldar</c>/<c>star</c>/
  /// <c>inca</c>/<c>deca</c>/<c>lda</c>/<c>sta</c> remarks).</summary>
  private const int MaxAddressRegisterParameters = 4;

  /// <summary>
  /// ABI v2: among <paramref name="parameterTypes"/>, in order, assigns the first
  /// <see cref="MaxAddressRegisterParameters"/> POINTER-typed ones an address-register index (0, 1, 2,
  /// 3, in the order their own pointer encountered) -- every other parameter (a later pointer, or any
  /// non-pointer parameter, wherever it falls) gets null, keeping ABI v1's stack convention. Computed
  /// once per signature, from parameter TYPES alone, so a call site can rely on it even for a function
  /// only declared (not yet defined) -- see this class's own ABI doc comment.
  /// </summary>
  private static IReadOnlyList<int?> ComputeParameterRegisterSlots(IReadOnlyList<CType> parameterTypes)
  {
    var slots = new int?[parameterTypes.Count];
    int pointerCount = 0;
    for (int i = 0; i < parameterTypes.Count; i++)
    {
      if (!parameterTypes[i].IsPointer)
      {
        continue;
      }

      if (pointerCount < MaxAddressRegisterParameters)
      {
        slots[i] = pointerCount;
      }

      pointerCount++;
    }

    return slots;
  }

  /// <summary>
  /// Float ABI (added 2026-09-26): among <paramref name="parameterTypes"/>, in order, assigns EVERY
  /// <c>float</c>-typed one an fr-register index (0, 1, 2, ... in the order its own <c>float</c>
  /// parameter is encountered) -- but ONLY when <paramref name="isFastcall"/> is true. Per Stefan
  /// verbatim: "for fastcall functions the parameter are passed by register fr[0] for the first float
  /// parameter, fr[1] for the second parameter, ... for normal functions float parameter are put on the
  /// stack" -- so a NON-<c>__fastcall</c> function gets an all-null result here regardless of how many
  /// <c>float</c> parameters it declares (every one of them is stack-passed, two words each, see
  /// <see cref="EmitFunction"/>'s own stack-parameter-offset accounting). <see cref="CParser"/> already
  /// rejects, at parse time, a <c>__fastcall</c> function declaring more than
  /// <c>MaxFastcallFloatParameters</c> (8, node 305's own register count) <c>float</c> parameters, so this
  /// method never needs to reject an overflow itself.</summary>
  private static IReadOnlyList<int?> ComputeParameterFloatRegisterSlots(IReadOnlyList<CType> parameterTypes, bool isFastcall)
  {
    var slots = new int?[parameterTypes.Count];
    if (!isFastcall)
    {
      return slots;
    }

    int floatCount = 0;
    for (int i = 0; i < parameterTypes.Count; i++)
    {
      if (parameterTypes[i].IsFloat)
      {
        slots[i] = floatCount;
        floatCount++;
      }
    }

    return slots;
  }

  // ---------------------------------------------------------------------------------------------
  // Float ABI: whole-translation-unit "does this function use fr registers, directly or indirectly"
  // analysis -- see this class's own doc comment's "Float ABI" section, "Caller-save/restore" bullet.
  // ---------------------------------------------------------------------------------------------

  private void ComputeFunctionUsesFloatRegisters(CTranslationUnit unit)
  {
    List<CFunctionDecl> defined = unit.Functions.Where(f => f.Body is not null).ToList();

    foreach (CFunctionDecl function in defined)
    {
      _functionUsesFr[function.Name] = function.ReturnType.IsFloat
          || function.Parameters.Any(p => p.Type.IsFloat)
          || StatementUsesFloatDirectly(function.Body!);
    }

    // Fixed-point iteration over the (possibly recursive/mutually-recursive) call graph: a function
    // becomes "uses fr registers" as soon as ANY call it makes (at any nesting depth) is to a function
    // that does -- including a function not defined in this file at all, treated conservatively as "may
    // use fr registers" since its body isn't visible here. Bounded: at most defined.Count passes can
    // ever flip a false to true.
    bool changed = true;
    while (changed)
    {
      changed = false;
      foreach (CFunctionDecl function in defined)
      {
        if (_functionUsesFr[function.Name])
        {
          continue;
        }

        if (StatementCallsFloatUsingFunction(function.Body!))
        {
          _functionUsesFr[function.Name] = true;
          changed = true;
        }
      }
    }
  }

  /// <summary>Float ABI: consulted by <see cref="EmitCall"/> for every call site -- a callee not defined
  /// in this translation unit (only declared, or genuinely external) is conservatively assumed to
  /// possibly use fr registers, since this compiler cannot see its body.</summary>
  private bool CalleeUsesFloatRegisters(string calleeName) => !_functionUsesFr.TryGetValue(calleeName, out bool uses) || uses;

  /// <summary>Float ABI: true if <paramref name="stmt"/> (a function's own body, not descending into a
  /// nested function -- there are none in C) declares a <c>float</c> local anywhere, or contains a
  /// <see cref="CFloatLiteralExpr"/> anywhere in any expression. Originally (2026-09-26) deliberately did
  /// NOT need full symbol resolution, on the reasoning that referencing an already-<c>float</c>-typed
  /// global or parameter always flowed through a <c>float</c> local, literal, or (already separately
  /// checked by the caller) this function's own parameter/return type -- true for every program "basic"
  /// <c>float</c> support could compile AT THE TIME, since no pointer/array of <c>float</c> existed yet.
  /// <b>That stopped being true the same day, once multi-word pointer arithmetic/indexing legalized
  /// <c>float *</c>/an array or struct member of <c>float</c></b>: <c>void setpx(float *fp) { *fp =
  /// 3.14f; }</c> is a genuine 'float'-register-using function (its body's OWN 'float' literal still gets
  /// caught, but not every such function needs one -- e.g. "void copy(float *dst, float *src) { *dst =
  /// *src; }" has no literal at all) whose SIGNATURE doesn't say so either (its parameters are `float *`,
  /// not `float` -- <c>function.Parameters.Any(p =&gt; p.Type.IsFloat)</c> in <see
  /// cref="ComputeFunctionUsesFloatRegisters"/> is false). Missing this would be a genuine SAFETY bug, not
  /// just a missed optimization: <see cref="EmitCall"/> would then skip a save/restore a caller actually
  /// needed, silently corrupting live float register state across the call. See <see
  /// cref="ExprUsesFloatDirectly"/>'s own remarks for the (deliberately conservative, not precise) fix.
  /// </summary>
  private static bool StatementUsesFloatDirectly(CStmt stmt)
  {
    switch (stmt)
    {
      case CCompoundStmt compound:
        return compound.Statements.Any(StatementUsesFloatDirectly);
      case CLocalVarDecl localDecl:
        return localDecl.Type.IsFloat || (localDecl.Initializer is not null && ExprUsesFloatDirectly(localDecl.Initializer));
      case CExprStmt exprStmt:
        return ExprUsesFloatDirectly(exprStmt.Expression);
      case CIfStmt ifStmt:
        return ExprUsesFloatDirectly(ifStmt.Condition) || StatementUsesFloatDirectly(ifStmt.Then) || (ifStmt.Else is not null && StatementUsesFloatDirectly(ifStmt.Else));
      case CWhileStmt whileStmt:
        return ExprUsesFloatDirectly(whileStmt.Condition) || StatementUsesFloatDirectly(whileStmt.Body);
      case CDoWhileStmt doWhileStmt:
        return StatementUsesFloatDirectly(doWhileStmt.Body) || ExprUsesFloatDirectly(doWhileStmt.Condition);
      case CForStmt forStmt:
        return (forStmt.Init is not null && StatementUsesFloatDirectly(forStmt.Init))
            || (forStmt.Condition is not null && ExprUsesFloatDirectly(forStmt.Condition))
            || (forStmt.Update is not null && ExprUsesFloatDirectly(forStmt.Update))
            || StatementUsesFloatDirectly(forStmt.Body);
      case CReturnStmt { Value: { } value }:
        return ExprUsesFloatDirectly(value);
      case CLabelStmt labelStmt:
        return StatementUsesFloatDirectly(labelStmt.Inner);
      case CCaseLabelStmt caseLabelStmt:
        return ExprUsesFloatDirectly(caseLabelStmt.Value) || StatementUsesFloatDirectly(caseLabelStmt.Inner);
      case CDefaultLabelStmt defaultLabelStmt:
        return StatementUsesFloatDirectly(defaultLabelStmt.Inner);
      case CSwitchStmt switchStmt:
        return ExprUsesFloatDirectly(switchStmt.Selector) || StatementUsesFloatDirectly(switchStmt.Body);
      default:
        return false;
    }
  }

  /// <summary>See <see cref="StatementUsesFloatDirectly"/>'s own remarks for why the
  /// <see cref="CUnaryExpr"/>{Dereference}/<see cref="CIndexExpr"/>/<see cref="CMemberAccessExpr"/> cases
  /// below were added 2026-09-26 (the same day as basic <c>float</c> support itself, once a
  /// pointer/array/struct member could first be 'float'-typed): this whole analysis runs before any
  /// per-function type/symbol resolution is available (see this class's own doc comment on why the float
  /// ABI's "does this function use fr registers" scan is context-free), so there is no cheap way to know
  /// HERE whether a given dereference/index/member-access is actually 'float'-typed the way <see
  /// cref="TryGetStaticType"/> can once real per-function symbol tables exist. Rather than risk the
  /// SAFETY bug documented above, these three node shapes are treated as "uses float" UNCONDITIONALLY --
  /// deliberately conservative, matching the SAME conservative choice this class already makes for a
  /// callee whose body isn't visible (<see cref="CalleeUsesFloatRegisters"/>): a function that merely
  /// dereferences/indexes/member-accesses through a pointer for entirely non-'float' reasons now also
  /// gets an fpush/fpop save-restore its callers didn't strictly need, a missed optimization, never a
  /// missed SAVE.</summary>
  private static bool ExprUsesFloatDirectly(CExpr expr)
  {
    switch (expr)
    {
      case CFloatLiteralExpr:
        return true;
      case CUnaryExpr { Op: CUnaryOp.Dereference }:
      case CIndexExpr:
      case CMemberAccessExpr:
        return true;
      case CUnaryExpr unary:
        return ExprUsesFloatDirectly(unary.Operand);
      case CBinaryExpr binary:
        return ExprUsesFloatDirectly(binary.Left) || ExprUsesFloatDirectly(binary.Right);
      case CAssignExpr assign:
        return ExprUsesFloatDirectly(assign.Target) || ExprUsesFloatDirectly(assign.Value);
      case CCompoundAssignExpr compoundAssign:
        return ExprUsesFloatDirectly(compoundAssign.Target) || ExprUsesFloatDirectly(compoundAssign.Value);
      case CConditionalExpr conditional:
        return ExprUsesFloatDirectly(conditional.Condition) || ExprUsesFloatDirectly(conditional.WhenTrue) || ExprUsesFloatDirectly(conditional.WhenFalse);
      case CCallExpr call:
        return call.Arguments.Any(ExprUsesFloatDirectly);
      case CCastExpr cast:
        return cast.TargetType.IsFloat || ExprUsesFloatDirectly(cast.Operand);
      case CSizeOfExprExpr sizeOfExpr:
        return ExprUsesFloatDirectly(sizeOfExpr.Operand);
      case CCommaExpr comma:
        return ExprUsesFloatDirectly(comma.Left) || ExprUsesFloatDirectly(comma.Right);
      default:
        return false;
    }
  }

  /// <summary>Float ABI: true if <paramref name="stmt"/> contains a call (at any nesting depth, including
  /// inside another call's own arguments) to a function this compiler already knows uses fr registers
  /// (per <see cref="_functionUsesFr"/> so far) or cannot see the body of at all. Walks the same shape as
  /// <see cref="StatementUsesFloatDirectly"/>/<see cref="ExprUsesFloatDirectly"/> but collects
  /// <see cref="CCallExpr"/> nodes instead.</summary>
  private bool StatementCallsFloatUsingFunction(CStmt stmt) => EnumerateCalls(stmt).Any(call => CalleeUsesFloatRegisters(call.FunctionName));

  private static IEnumerable<CCallExpr> EnumerateCalls(CStmt stmt)
  {
    switch (stmt)
    {
      case CCompoundStmt compound:
        return compound.Statements.SelectMany(EnumerateCalls);
      case CLocalVarDecl { Initializer: { } init }:
        return EnumerateCalls(init);
      case CExprStmt exprStmt:
        return EnumerateCalls(exprStmt.Expression);
      case CIfStmt ifStmt:
        return EnumerateCalls(ifStmt.Condition).Concat(EnumerateCalls(ifStmt.Then)).Concat(ifStmt.Else is not null ? EnumerateCalls(ifStmt.Else) : []);
      case CWhileStmt whileStmt:
        return EnumerateCalls(whileStmt.Condition).Concat(EnumerateCalls(whileStmt.Body));
      case CDoWhileStmt doWhileStmt:
        return EnumerateCalls(doWhileStmt.Body).Concat(EnumerateCalls(doWhileStmt.Condition));
      case CForStmt forStmt:
        return (forStmt.Init is not null ? EnumerateCalls(forStmt.Init) : [])
            .Concat(forStmt.Condition is not null ? EnumerateCalls(forStmt.Condition) : [])
            .Concat(forStmt.Update is not null ? EnumerateCalls(forStmt.Update) : [])
            .Concat(EnumerateCalls(forStmt.Body));
      case CReturnStmt { Value: { } value }:
        return EnumerateCalls(value);
      case CLabelStmt labelStmt:
        return EnumerateCalls(labelStmt.Inner);
      case CCaseLabelStmt caseLabelStmt:
        return EnumerateCalls(caseLabelStmt.Value).Concat(EnumerateCalls(caseLabelStmt.Inner));
      case CDefaultLabelStmt defaultLabelStmt:
        return EnumerateCalls(defaultLabelStmt.Inner);
      case CSwitchStmt switchStmt:
        return EnumerateCalls(switchStmt.Selector).Concat(EnumerateCalls(switchStmt.Body));
      default:
        return [];
    }
  }

  private static IEnumerable<CCallExpr> EnumerateCalls(CExpr expr)
  {
    switch (expr)
    {
      case CCallExpr call:
        return call.Arguments.SelectMany(EnumerateCalls).Append(call);
      case CUnaryExpr unary:
        return EnumerateCalls(unary.Operand);
      case CBinaryExpr binary:
        return EnumerateCalls(binary.Left).Concat(EnumerateCalls(binary.Right));
      case CAssignExpr assign:
        return EnumerateCalls(assign.Target).Concat(EnumerateCalls(assign.Value));
      case CCompoundAssignExpr compoundAssign:
        return EnumerateCalls(compoundAssign.Target).Concat(EnumerateCalls(compoundAssign.Value));
      case CConditionalExpr conditional:
        return EnumerateCalls(conditional.Condition).Concat(EnumerateCalls(conditional.WhenTrue)).Concat(EnumerateCalls(conditional.WhenFalse));
      case CIndexExpr index:
        return EnumerateCalls(index.Base).Concat(EnumerateCalls(index.Index));
      case CMemberAccessExpr member:
        // Added 2026-09-26 alongside basic struct support's own CMemberAccessExpr node -- without this
        // case, a call nested in a member access's own base (e.g. "getFoo()->x", legal since a function
        // may return a struct pointer) would be invisible to the fixed-point "transitively calls a
        // float-using function" scan below, the same class of gap ExprUsesFloatDirectly's own remarks
        // describe for the "direct use" scan.
        return EnumerateCalls(member.Base);
      case CCastExpr cast:
        return EnumerateCalls(cast.Operand);
      case CSizeOfExprExpr sizeOfExpr:
        return EnumerateCalls(sizeOfExpr.Operand);
      case CCommaExpr comma:
        return EnumerateCalls(comma.Left).Concat(EnumerateCalls(comma.Right));
      default:
        return [];
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Globals and the string-literal pool.
  // ---------------------------------------------------------------------------------------------

  private void EmitGlobal(CGlobalVarDecl global)
  {
    string label = MangleExternalSymbol(global.Name);

    if (global.IsExtern)
    {
      // Declared here, defined elsewhere -- no storage, just a reference the linker resolves.
      _imports.Add(label);
      if (global.Type.IsFloat)
      {
        // Float ABI: a float global is TWO words under two labels -- see EmitFloatGlobalStorage's own
        // remarks and this class's own doc comment. Both need importing.
        _imports.Add(label + "__hi");
      }

      return;
    }

    if (!global.IsStatic)
    {
      _dataLines.Add($".export {label}");
      if (global.Type.IsFloat)
      {
        _dataLines.Add($".export {label}__hi");
      }
    }

    if (global.Type.IsFloat)
    {
      EmitFloatGlobalStorage(label, global.Initializer, global.Location);
      return;
    }

    List<string> words = ComputeGlobalInitialWords(global.Type, global.Initializer, global.Location);
    _dataLines.Add($"{label}: .word {string.Join(", ", words)}");
  }

  /// <summary>Float ABI: emits a <c>float</c> global's (or static local's) two-word storage as TWO
  /// separate, immediately-adjacent <c>.word</c> lines under two labels (<paramref name="label"/> for the
  /// low word, <c>&lt;label&gt;__hi</c> for the high word) -- see this class's own doc comment's "A
  /// <c>float</c> GLOBAL is two contiguous data words" remarks for why (in short: <c>gld</c>/<c>gst</c>
  /// take a single label with no address arithmetic, so the high word needs its own label; nothing else
  /// is emitted between these two lines, so the two words ARE genuinely contiguous in the final
  /// binary).</summary>
  private void EmitFloatGlobalStorage(string label, CExpr? initializer, CSourceLocation location)
  {
    (string lowWord, string highWord) = ComputeFloatInitialWords(initializer, location);
    _dataLines.Add($"{label}: .word {lowWord}");
    _dataLines.Add($"{label}__hi: .word {highWord}");
  }

  /// <summary>Float ABI: computes a <c>float</c> global/static-local's initial (low, high) words --
  /// see <see cref="EmitFloatLiteralInto"/>'s own remarks for the identical bit-splitting logic used for
  /// a <c>float</c> literal appearing inside a function body.</summary>
  private (string Low, string High) ComputeFloatInitialWords(CExpr? initializer, CSourceLocation location)
  {
    float value = 0f;
    if (initializer is not null && !TryEvaluateConstantFloat(initializer, out value))
    {
      Error(location, "a 'float' global/static variable's initializer must be a compile-time constant floating-point literal");
    }

    int bits = BitConverter.SingleToInt32Bits(value);
    return ((bits & 0xFFFF).ToString(), ((bits >> 16) & 0xFFFF).ToString());
  }

  /// <summary>Float ABI: folds a <c>float</c> global initializer to a compile-time constant value --
  /// mirrors <see cref="TryEvaluateConstantGlobalInitializer"/>'s own small scope (a literal, optionally
  /// negated) for the non-<c>float</c> case.</summary>
  private static bool TryEvaluateConstantFloat(CExpr expr, out float value)
  {
    switch (expr)
    {
      case CFloatLiteralExpr literal:
        value = literal.Value;
        return true;

      case CUnaryExpr { Op: CUnaryOp.Minus } unary when TryEvaluateConstantFloat(unary.Operand, out float v):
        value = -v;
        return true;

      case CUnaryExpr { Op: CUnaryOp.Plus } unary when TryEvaluateConstantFloat(unary.Operand, out float v):
        value = v;
        return true;

      default:
        value = 0f;
        return false;
    }
  }

  /// <summary>Computes the DATA-section words for a global (or static-local) variable's initial value:
  /// one word per <see cref="CType.SizeInWords"/> slot, each either a decimal literal or a label name
  /// the assembler will resolve. An array with no initializer is zero-filled; a scalar with no
  /// initializer is a single zero word.</summary>
  private List<string> ComputeGlobalInitialWords(CType type, CExpr? initializer, CSourceLocation location)
  {
    int size = Math.Max(type.SizeInWords, 1);

    if (initializer is null)
    {
      return Enumerable.Repeat("0", size).ToList();
    }

    if (type.IsArray && type.ElementType is { IsIntegral: true } elementType && (elementType.Equals(CType.Char) || elementType.Equals(CType.UnsignedChar))
        && initializer is CStringLiteralExpr stringLiteral)
    {
      byte[] bytes = Encoding.ASCII.GetBytes(stringLiteral.Value);
      var words = bytes.Select(b => ((int)b).ToString()).ToList();
      words.Add("0"); // NUL terminator
      while (words.Count < size)
      {
        words.Add("0");
      }

      if (words.Count > size && type.ArrayLength >= 0)
      {
        Error(location, $"string literal initializer is too long for array of {size} elements");
        words = words.Take(size).ToList();
      }

      return words;
    }

    if (!TryEvaluateConstantGlobalInitializer(initializer, out string? word))
    {
      Error(location, "a global variable's initializer must be a compile-time constant (a literal, a negated literal, an address-of a global, or a string literal)");
      return Enumerable.Repeat("0", size).ToList();
    }

    var result = new List<string> { word! };
    while (result.Count < size)
    {
      result.Add("0");
    }

    return result;
  }

  /// <summary>Folds a global initializer expression to a single assembler-level word (a decimal literal
  /// or a label name) at compile time -- the only shapes a hand-written <c>.word</c> line itself
  /// accepts. No general constant-folding is attempted; only the handful of forms real C programs
  /// commonly use for a global's initializer.</summary>
  private bool TryEvaluateConstantGlobalInitializer(CExpr expr, out string? word)
  {
    switch (expr)
    {
      case CIntLiteralExpr literal:
        word = literal.Value.ToString();
        return true;

      case CUnaryExpr { Op: CUnaryOp.Minus, Operand: CIntLiteralExpr inner }:
        word = (-inner.Value).ToString();
        return true;

      case CUnaryExpr { Op: CUnaryOp.AddressOf, Operand: CNameExpr name }:
        if (_globals.ContainsKey(name.Name))
        {
          word = MangleExternalSymbol(name.Name);
          return true;
        }

        word = null;
        return false;

      case CNameExpr arrayName when _globals.TryGetValue(arrayName.Name, out CGlobalSymbol? symbol) && symbol.Type.IsArray:
        // A bare array name decays to its own address, same as everywhere else in C.
        word = MangleExternalSymbol(arrayName.Name);
        return true;

      case CStringLiteralExpr stringLiteral:
        word = InternStringLiteral(stringLiteral.Value);
        return true;

      default:
        word = null;
        return false;
    }
  }

  /// <summary>Evaluates a constant integer expression at compile time (used for array bounds, case
  /// labels, and <c>sizeof</c>'s array-length interplay). Handles literals and the small set of integer
  /// operators macros commonly expand to; anything else is "not constant" rather than a hard error, so
  /// callers can report their own, more specific diagnostic.
  ///
  /// The binary-operator arithmetic itself is factored out into <see
  /// cref="CConstantFolder.TryEvaluateBinaryOp"/> (added 2026-09-10 alongside that class), shared with
  /// the AST-level constant-folding optimizer pass so the two can never quietly disagree about what a
  /// compile-time integer operation computes -- this method's own job is purely walking the handful of
  /// node shapes ITS OWN narrower callers need (a literal, a unary +/-/~ on one, sizeof, or a binary op
  /// whose own operands are themselves already constant), not the arithmetic underneath a binary
  /// op.</summary>
  private static bool TryEvaluateConstantLong(CExpr expr, out long value)
  {
    switch (expr)
    {
      case CIntLiteralExpr literal:
        value = literal.Value;
        return true;

      case CUnaryExpr { Op: CUnaryOp.Minus } unary when TryEvaluateConstantLong(unary.Operand, out long v):
        value = -v;
        return true;

      case CUnaryExpr { Op: CUnaryOp.Plus } unary when TryEvaluateConstantLong(unary.Operand, out long v):
        value = v;
        return true;

      case CUnaryExpr { Op: CUnaryOp.BitwiseNot } unary when TryEvaluateConstantLong(unary.Operand, out long v):
        value = ~v;
        return true;

      case CSizeOfTypeExpr sizeOfType:
        value = sizeOfType.Type.SizeInWords;
        return true;

      case CBinaryExpr binary when TryEvaluateConstantLong(binary.Left, out long l) && TryEvaluateConstantLong(binary.Right, out long r):
        return CConstantFolder.TryEvaluateBinaryOp(binary.Op, l, r, out value);

      default:
        value = 0;
        return false;
    }
  }

  /// <summary>Interns a string literal as an anonymous global <c>char[]</c>, reusing the same label for
  /// an identical literal seen more than once. Actual DATA-section emission happens once, in
  /// <see cref="EmitStringPool"/>, after every function has been compiled.</summary>
  private string InternStringLiteral(string value)
  {
    if (_stringPool.TryGetValue(value, out string? existing))
    {
      return existing;
    }

    string label = $"__str{_stringLabelCounter++}";
    _stringPool[value] = label;
    return label;
  }

  private void EmitStringPool()
  {
    foreach ((string value, string label) in _stringPool)
    {
      byte[] bytes = Encoding.ASCII.GetBytes(value);
      IEnumerable<string> words = bytes.Select(b => ((int)b).ToString()).Append("0");
      _dataLines.Add($"{label}: .word {string.Join(", ", words)}");
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Functions, scopes, and local variables.
  // ---------------------------------------------------------------------------------------------

  private void EmitFunction(CFunctionDecl function)
  {
    if (!function.IsStatic)
    {
      _codeLines.Add($".export {MangleExternalSymbol(function.Name)}");
    }

    _codeLines.Add($"{MangleExternalSymbol(function.Name)}:");

    var context = new CFunctionContext { Name = function.Name, ReturnType = function.ReturnType, EpilogueLabel = NewLabel("epilogue") };
    context.Scopes.Add(new Dictionary<string, CVarSymbol>(StringComparer.Ordinal));

    // ABI v2: CollectSignatures already ran over the whole translation unit, so this function's own
    // signature (and its ParameterRegisterSlots) is already sitting in _functions -- reuse it rather
    // than recomputing, so a call site and this function's own prologue can never disagree about which
    // parameters are register-eligible.
    CFunctionSignature signature = _functions[function.Name];
    int stackParameterIndex = 0;
    var registerParameters = new List<(int RegisterIndex, int LocalSlot)>();
    // Float ABI: parallel to registerParameters above, but for fr-register-assigned float parameters,
    // which need TWO local slots each (spilled via EmitFloatStoreLocal, not a single "stl") -- see this
    // class's own doc comment's "Parameter passing" bullet.
    var floatRegisterParameters = new List<(int RegisterIndex, int LocalBaseSlot)>();
    for (int i = 0; i < function.Parameters.Count; i++)
    {
      CParameter parameter = function.Parameters[i];
      int? registerIndex = i < signature.ParameterRegisterSlots.Count ? signature.ParameterRegisterSlots[i] : null;
      int? floatRegisterIndex = i < signature.ParameterFloatRegisterSlots.Count ? signature.ParameterFloatRegisterSlots[i] : null;
      if (registerIndex is int assignedRegister)
      {
        // Register-eligible: this parameter never arrives via the stack at all, so it gets an ordinary
        // LOCAL slot instead of a Parameter-kind frame offset -- see this class's own ABI doc comment.
        int localSlot = context.NextLocalSlot++;
        context.Scopes[0][parameter.Name] = new CVarSymbol(CVarKind.Local, localSlot, parameter.Type);
        registerParameters.Add((assignedRegister, localSlot));
      }
      else if (floatRegisterIndex is int assignedFrRegister)
      {
        // Float ABI: a fastcall float parameter arrives in fr[assignedFrRegister], never the stack --
        // gets a 2-word LOCAL slot pair instead of a Parameter-kind frame offset, exactly like a
        // register-eligible pointer parameter above.
        int localBaseSlot = context.NextLocalSlot;
        context.NextLocalSlot += 2;
        context.Scopes[0][parameter.Name] = new CVarSymbol(CVarKind.Local, localBaseSlot, parameter.Type);
        floatRegisterParameters.Add((assignedFrRegister, localBaseSlot));
      }
      else
      {
        // Stack-passed, same as ABI v1 -- but renumbered to count only the parameters that are ACTUALLY
        // pushed, since any register-eligible ones earlier in the declaration are simply absent from the
        // caller's own push sequence. Float ABI (2026-09-26): generalized from a per-PARAMETER count to a
        // per-WORD count, since a stack-passed 'float' parameter now occupies 2 words, not 1 -- see this
        // class's own doc comment.
        context.Scopes[0][parameter.Name] = new CVarSymbol(CVarKind.Parameter, stackParameterIndex, parameter.Type);
        stackParameterIndex += Math.Max(parameter.Type.SizeInWords, 1);
      }
    }

    int enterLineIndex = _codeLines.Count;
    _codeLines.Add(string.Empty); // patched below once the body's local-slot count is known.

    // ABI v2 prologue: spill every register-eligible parameter out of its (volatile, caller-set)
    // address register into its own ordinary local slot, immediately -- before the body runs and before
    // this function makes any call of its own that could clobber the register. See this class's own ABI
    // doc comment for why the value's ONLY safe lifetime inside the register is this one moment.
    foreach ((int registerIndex, int localSlot) in registerParameters)
    {
      // 2026-09-09: "stl" stores r directly (see this class's own ABI doc comment) -- no "push" needed
      // to shuttle the address through the stack first. "arst" leaves the address in r AND pushes a page
      // word onto the stack as a side effect; "stl" reads r (assumed to leave r unchanged, like "push"
      // does) so the trailing "pop" only needs to clean up that leftover page word (always 0 -- far
      // pointers are not supported yet, see this class's own ABI doc comment).
      //
      // CORRECTED 2026-09-11: this line used to emit "sta {registerIndex}" -- flagged the moment the
      // register-operand fix made it assemblable for the first time, since "sta"
      // (StoreAddressRegisterValueMnemonic) is node 306's dereference-STORE role ("store r TO the
      // address held in register aa," the same mnemonic EmitDereferenceStore uses), not "read this
      // register's own (address, page) value back out" as this comment describes. Stefan confirmed
      // directly: "arld loads an address into an address register. lda loads r using the address of an
      // address register." -- i.e. arld is the SET direction (address in, from r/stack) and lda is the
      // dereference-read direction; by elimination arst is arld's complement, reading the register's own
      // stored value back OUT into (r, stack), which is exactly what this prologue needs. Fixed to
      // "arst {registerIndex}".
      EmitCode($"arst {registerIndex}"); // r := this register's address word; stack: [..., page]
      EmitCode($"stl {localSlot}");     // localSlot := r (the address); stack unchanged
      EmitCode("pop");                  // r := the leftover page word, discarded; stack: [...]
    }

    // Float ABI (2026-09-26): spill every fr-register-assigned float parameter into its own 2-word local
    // slot pair immediately, same timing and same "never trusted to survive past this one moment"
    // discipline as the address-register spill above -- see this class's own doc comment.
    foreach ((int registerIndex, int localBaseSlot) in floatRegisterParameters)
    {
      EmitFloatStoreLocal(localBaseSlot, registerIndex);
    }

    _currentFunction = context;
    EmitStatement(function.Body!);
    _currentFunction = null;

    _codeLines.Add($"{context.EpilogueLabel}:");
    _codeLines.Add("  leave");
    _codeLines.Add("  ret");
    _codeLines[enterLineIndex] = $"  enter {context.NextLocalSlot}";
  }

  private void EnterScope() => _currentFunction!.Scopes.Add(new Dictionary<string, CVarSymbol>(StringComparer.Ordinal));

  private void ExitScope() => _currentFunction!.Scopes.RemoveAt(_currentFunction.Scopes.Count - 1);

  private CVarSymbol? LookupVariable(string name)
  {
    List<Dictionary<string, CVarSymbol>> scopes = _currentFunction!.Scopes;
    for (int i = scopes.Count - 1; i >= 0; i--)
    {
      if (scopes[i].TryGetValue(name, out CVarSymbol? symbol))
      {
        return symbol;
      }
    }

    return null;
  }

  /// <summary>Declares a local variable in the innermost active scope. A non-static array reserves
  /// <see cref="CType.SizeInWords"/> consecutive local slots (every other type reserves exactly one,
  /// since every scalar/pointer is one word); a "static" local gets the same one-per-program storage and
  /// lifetime as a global, under a mangled name only this function's scope can see, with its initializer
  /// baked into the DATA section once rather than executed as runtime code each time control reaches the
  /// declaration.</summary>
  private void DeclareLocal(CLocalVarDecl decl)
  {
    if (_currentFunction!.Scopes[^1].ContainsKey(decl.Name))
    {
      Error(decl.Location, $"\"{decl.Name}\" is already declared in this scope");
      return;
    }

    if (decl.IsStatic)
    {
      string label = $"__static_{_currentFunction!.Name}_{decl.Name}_{_staticLocalCounter++}";
      if (decl.Type.IsFloat)
      {
        // Float ABI: see EmitFloatGlobalStorage's own remarks -- a static local float needs the same
        // two-label, two-word storage a plain float global does (it is addressed the same way, via
        // CVarKind.Global + GlobalLabel, by ResolveFloatLvalue).
        EmitFloatGlobalStorage(label, decl.Initializer, decl.Location);
      }
      else
      {
        List<string> words = ComputeGlobalInitialWords(decl.Type, decl.Initializer, decl.Location);
        _dataLines.Add($"{label}: .word {string.Join(", ", words)}");
      }

      _currentFunction.Scopes[^1][decl.Name] = new CVarSymbol(CVarKind.Global, 0, decl.Type, label);
      return;
    }

    int slot = _currentFunction!.NextLocalSlot;
    _currentFunction.NextLocalSlot += Math.Max(decl.Type.SizeInWords, 1);
    _currentFunction.Scopes[^1][decl.Name] = new CVarSymbol(CVarKind.Local, slot, decl.Type);
  }

  // ---------------------------------------------------------------------------------------------
  // Lvalues: the single mechanism plain assignment, compound assignment, and pre/post increment and
  // decrement all share.
  //
  // IMPORTANT correctness note (found in review, 2026-09-06): the CVM's "t" register has no save/
  // restore of its own, so it is NOT safe to assume it survives arbitrary code emitted between
  // resolving an Indirect lvalue's address and actually loading/storing through it -- e.g. in
  // "globalA = globalB;", resolving globalA's address latches t, but evaluating the RHS globalB reads
  // a global too, which does its own "xt" and silently repoints t at globalB before the store runs.
  // The fix: an Indirect lvalue's address is computed exactly ONCE (preserving CCompoundAssignExpr's
  // own "do not re-run the target's side effects" contract) but immediately cached into a dedicated
  // local slot, not left sitting only in t. EmitLoad/EmitStore then each independently reload that
  // cached address and re-issue "xt" right before their own ldt/stt -- "ldl addressSlot; push; xt" is a
  // pure, side-effect-free re-derivation of t from the cache (2026-09-09: "ldl" itself only sets r, per
  // this class's own ABI doc comment -- the "push" is what actually puts the address back on the stack
  // for "xt" to consume), not a re-evaluation of the original target expression, so this is safe to
  // repeat as many times as needed (compound assignment loads once and stores once; increment/decrement
  // do the same) no matter what runs in between, or what "r" holds by the time it runs.
  // ---------------------------------------------------------------------------------------------

  /// <summary>Optimizer addition, 2026-09-10 -- Stefan's own observation on <c>d[0] = a;</c> (<c>d</c> a
  /// local array, <c>a</c> a local scalar): "d[0] is a local variable, a is a local variable. so a simple
  /// 'ldl d[0]; stl a' with the offset calculated for d[0] and a would have done the job." He's right, and
  /// unlike <see cref="CvmPeepholeOptimizer"/>'s rules this needs no unconfirmed hardware assumption to
  /// exploit: local-array element storage is entirely this compiler's OWN decision, not a hardware fact
  /// still awaiting confirmation. Local-slot allocation (<see cref="DeclareLocal"/>) already reserves
  /// <c>Type.SizeInWords</c> CONSECUTIVE local slots per declaration, so an array's <c>K</c>-th element
  /// (every element type here is exactly one CVM word -- see <see cref="CType"/>'s own remarks) always
  /// lives at local slot <c>arrayBaseSlot + K</c>. When <c>K</c> is itself known at compile time, that slot
  /// number is just as loadable/storable with a single <c>ldl</c>/<c>stl</c> as any plain scalar local --
  /// the exact same, already-confirmed addressing (see "ldl/ldp/stl/stp go through register r, confirmed
  /// (2026-09-09)" in the C-compiler design doc) every OTHER local already uses. No need to ever compute a
  /// runtime address, cache it, or indirect through it via node 306's address-register mechanism (<see
  /// cref="EmitDereferenceLoad"/>/<see cref="EmitDereferenceStore"/>) at all.
  ///
  /// This only ever fires for a genuinely LOCAL array (<see cref="CVarKind.Local"/>) referenced directly
  /// by its own bare name -- never a parameter (a parameter of array type has already decayed to a plain
  /// runtime pointer value by the time it reaches a <see cref="CVarSymbol"/>, so there is no fixed frame
  /// slot per element to fold into), never a global/static-local (those are addressed by a data-section
  /// label, not a frame offset -- <see cref="CVarKind.Global"/>), and never through a further expression
  /// (a dereferenced pointer, a function's return value, another index) -- anything this can't prove safe
  /// at compile time falls through unchanged to the general, always-correct address-computation path
  /// below. Because it can only ever produce EQUAL-OR-CHEAPER, equally-correct code compared to that
  /// general path for the one narrow case it recognizes, this fast path is unconditional -- unlike
  /// <see cref="CvmPeepholeOptimizer"/>, it is not gated behind a project optimizer toggle.</summary>
  private bool TryGetConstantLocalArrayElementSlot(CIndexExpr index, out int slot, out CType elementType)
  {
    slot = 0;
    elementType = CType.Int;

    if (index.Base is not CNameExpr name)
    {
      return false;
    }

    CVarSymbol? symbol = LookupVariable(name.Name);
    if (symbol is not { Kind: CVarKind.Local, Type.Kind: CTypeKind.Array })
    {
      return false;
    }

    if (symbol.Type.ArrayLength < 0)
    {
      return false;
    }

    if (!TryEvaluateConstantLong(index.Index, out long constIndex) || constIndex < 0 || constIndex >= symbol.Type.ArrayLength)
    {
      return false;
    }

    elementType = symbol.Type.ElementType!;

    // Added 2026-09-26 alongside multi-word pointer arithmetic/indexing: a local array's element used to
    // always be exactly one word (this fast path predates 'float'/'struct' array elements), so the K-th
    // element's own slot was always just "base slot + K". A wider element (float, struct, or an array
    // member) now needs K scaled by its own SizeInWords, the same generalization EmitScaleByElementSize
    // applies to the general (runtime-address) indexing path -- see that method's own remarks.
    slot = symbol.Index + (int)constIndex * Math.Max(elementType.SizeInWords, 1);
    return true;
  }

  private CLvalue ResolveLvalue(CExpr expr)
  {
    switch (expr)
    {
      case CNameExpr name:
        {
          CVarSymbol? symbol = LookupVariable(name.Name);
          if (symbol is { Kind: CVarKind.Local })
          {
            return new CLvalue(CLvalueKind.Local, symbol.Index, symbol.Type);
          }

          if (symbol is { Kind: CVarKind.Parameter })
          {
            return new CLvalue(CLvalueKind.Parameter, symbol.Index, symbol.Type);
          }

          string label = symbol?.GlobalLabel ?? MangleExternalSymbol(name.Name);
          CType type;
          if (symbol is { Kind: CVarKind.Global })
          {
            type = symbol.Type;
          }
          else if (_globals.TryGetValue(name.Name, out CGlobalSymbol? global))
          {
            type = global.Type;
          }
          else if (_functions.ContainsKey(name.Name))
          {
            Error(expr.Location, $"\"{name.Name}\" is a function and cannot be assigned to");
            type = CType.Int;
          }
          else
          {
            Error(expr.Location, $"\"{name.Name}\" is not declared");
            type = CType.Int;
          }

          if (!_globals.ContainsKey(name.Name) && symbol is not { Kind: CVarKind.Global })
          {
            _imports.Add(label);
          }

          // 2026-09-10: a global's own label is already a fixed, compile-time-constant reference (the
          // assembler/linker resolve it once, via a relocation exactly like pushlit's -- see
          // EmitGlobalFetch/EmitGlobalAssign's own remarks) -- there is nothing to compute or cache into
          // a local slot the way *ptr's or a non-constant array index's runtime address needs. This
          // used to unconditionally do "EmitCode($\"pushlit {label}\"); return CacheIndirectAddress(type);",
          // spending a whole address-computation-and-cache sequence (and burning a local slot) on
          // something already known at compile time; gld/gst make that unnecessary. See
          // "Global-scalar addressing: gld/gst replace the address-register dance (2026-09-10)" in
          // c-compiler-design.md for the full story, including the hardware trace that caught and fixed
          // a real bug in node 508's own g/@/g/! primitives underlying gld/gst.
          return new CLvalue(CLvalueKind.Global, 0, type, label);
        }

      case CUnaryExpr { Op: CUnaryOp.Dereference } deref:
        {
          CType pointerType = EmitExpr(deref.Operand);
          CType pointeeType = pointerType.IsPointer ? pointerType.ElementType! : pointerType.Decay() is { IsPointer: true } decayed ? decayed.ElementType! : CType.Int;
          if (!pointerType.IsPointer)
          {
            Error(expr.Location, $"cannot dereference a value of type \"{pointerType}\"");
          }

          return CacheIndirectAddress(pointeeType);
        }

      case CIndexExpr index:
        {
          if (TryGetConstantLocalArrayElementSlot(index, out int directSlot, out CType directElementType))
          {
            return new CLvalue(CLvalueKind.Local, directSlot, directElementType);
          }

          CType elementType = EmitAddressOfIndex(index);
          return CacheIndirectAddress(elementType);
        }

      case CMemberAccessExpr member:
        {
          CType fieldType = EmitAddressOfMember(member);
          if (fieldType.IsStruct)
          {
            // See CType.StructOf's own remarks: reading/assigning a WHOLE struct value in one expression
            // is not yet supported -- access one of its own members instead, or use '&' for its address.
            // Still returns a real (if unusable) CLvalue, same as every other error branch in this
            // method, since a CParseException-style abort mid-codegen isn't this class's error model.
            Error(expr.Location, $"\"{member.Member}\" is a struct member of type \"{fieldType}\" -- reading or assigning a whole struct value is not yet supported by this compiler");
          }

          return CacheIndirectAddress(fieldType);
        }

      default:
        Error(expr.Location, "expression is not assignable");
        EmitCode("pushlit 0");
        return CacheIndirectAddress(CType.Int);
    }
  }

  /// <summary>Given an address already sitting on top of the stack, caches a copy of it into a fresh
  /// local slot (so it can be safely re-derived later regardless of what runs in between -- see this
  /// section's own remarks), leaving the stack exactly as it was before the address was pushed.
  ///
  /// REWRITTEN 2026-09-09: this used to also "prime" node 508's pointer register "t" (via <c>xt</c>) for
  /// whatever load/store came next -- but <see cref="EmitLoad"/>/<see cref="EmitStore"/>'s own
  /// <c>default</c> case always re-derives the address from <paramref name="type"/>'s own cached slot
  /// and re-runs the full dereference sequence anyway, so that priming was already dead work even before
  /// node 508's <c>xt</c>/<c>ldt</c>/<c>stt</c> stopped resolving against any live node. Dropped outright
  /// rather than ported to node 306's replacement sequence (see <see cref="EmitDereferenceLoad"/>'s own
  /// remarks) -- there is nothing left here that needs it.
  /// </summary>
  private CLvalue CacheIndirectAddress(CType type)
  {
    int addressSlot = _currentFunction!.NextLocalSlot++;
    EmitCode("pop");                    // r := address; stack: [] (one word consumed off the incoming stack)
    EmitCode($"stl {addressSlot}");     // addressSlot := r (the address)
    return new CLvalue(CLvalueKind.Indirect, addressSlot, type);
  }

  /// <summary>Dereferences the address already sitting in register r, pushing the value read from
  /// memory at that address.
  ///
  /// ADDED 2026-09-09, replacing node 508's now-dead <c>xt</c>/<c>ldt</c> (deleted outright from
  /// <see cref="CvmInstructionSet"/> the same day, once this codegen stopped emitting them -- see
  /// that class's own remarks on the 2026-09-09 CVM1-opcode purge). The replacement uses node 306's address-register mechanism instead (see
  /// <see cref="Node306Program"/>'s own remarks for the F18-level design): <c>'arld</c> ("load an address
  /// into an address register") loads node 306's address register 0 from (address = r, page = the
  /// stack's own top word), and <c>'lda</c> ("load r using the address of an address register") then
  /// reads memory at that address back into r. Every C pointer in this compiler is a plain 16-bit CVM
  /// address (there is no notion of a non-zero page anywhere else in this ABI), so the page pushed here
  /// is always a literal 0.
  ///
  /// CORRECTED 2026-09-11 (twice): first, <c>arld</c>/<c>lda</c>/<c>sta</c> gained a real register-index
  /// OPERAND (<c>CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue</c> -- see
  /// <see cref="CvmInstructionSet.ArithmeticStoreAddressRegisterMnemonic"/>'s own remarks), since node
  /// 306 genuinely has SIX address registers (0-5), not the single hardwired one the codebase used to
  /// assume, and <see cref="Ga144.Cvm.Toolchain.CvmAssembler"/> can now express any of them. Second, this
  /// method itself used to emit <c>"arst 0"</c> for the register-setup step -- WRONG, per Stefan's own
  /// direct follow-up confirming the direction of both mnemonics: "arld loads an address into an address
  /// register. lda loads r using the address of an address register." <c>arld</c> is the SET direction
  /// (address in, from r/stack); <c>arst</c> is its complement, reading a register's own stored value
  /// back OUT (used by <see cref="EmitFunction"/>'s own ABI v2 prologue spill, not here). Fixed to
  /// <c>"arld 0"</c>. This method still always uses register 0 -- the compiler's own pointer
  /// representation is still a single 16-bit CVM address with no notion of which register or page it
  /// belongs to, so there is nothing yet for a second live register to hold; using more than one address
  /// register at once is a question for a future ABI change (e.g. a heap spanning more than one page),
  /// not something this correction decides on its own. Also NOT YET CONFIRMED ON REAL HARDWARE -- this
  /// sequence has not been run against a physical GA144 board.
  /// </summary>
  private void EmitDereferenceLoad()
  {
    EmitCode("pushlit 0");   // stack: [..., 0] (page = 0)
    EmitCode("arld 0");      // node 306 address register 0 := (address = r, page = pop 0); stack: [...]
    EmitCode("lda 0");       // r := memory[address register 0]
    EmitCode("push");        // stack: [..., value]
  }

  /// <summary>Dereferences the address already sitting in register r, storing the value already on top
  /// of the stack to memory at that address. See <see cref="EmitDereferenceLoad"/>'s own remarks for the
  /// node-306 mechanism this uses (including the 2026-09-11 register-operand and arld/arst-direction
  /// corrections) -- <c>'sta</c> ("store r to address in address register") is <c>'lda</c>'s write-side
  /// counterpart.</summary>
  private void EmitDereferenceStore()
  {
    EmitCode("pushlit 0");   // stack: [..., value, 0] (page = 0)
    EmitCode("arld 0");      // node 306 address register 0 := (address = r, page = pop 0); stack: [..., value]
    EmitCode("pop");         // r := value; stack: [...]
    EmitCode("sta 0");       // memory[address register 0] := r
  }

  /// <summary>Fetches a global's value into r, via <c>gld</c> -- named and behaving exactly like an
  /// ordinary CPU "load": r := the global at <paramref name="label"/>.
  ///
  /// CORRECTED 2026-09-10 (same day as this whole feature, superseding an earlier, WRONG resolution of
  /// the same question). This class, <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet"/>'s own
  /// <c>LoadGlobalMnemonic</c>/<c>StoreGlobalMnemonic</c> remarks, and <c>Node508Program</c>'s own
  /// remarks had all flagged an apparent naming/body cross-wire in node 508's F18 source (<c>'gld</c>
  /// calling a primitive whose body ends in a remote STORE; <c>'gst</c> calling one whose body ends in a
  /// remote FETCH). Asked directly, Stefan's FIRST answer ("gld loads a global from r" / "gst stores a
  /// global into r") was read as confirming that backwards direction was intentional -- <c>gld</c> :=
  /// store, <c>gst</c> := fetch -- and this method and <see cref="EmitGlobalAssign"/> were wired that
  /// way. That reading was WRONG: Stefan then ran an actual hardware/simulation test (<c>lit 1; gst 2;
  /// gld 2; gst 4; nop</c>) and the trace shows a WRITE to global address 2 on the <c>gst 2</c>
  /// instruction (storing r's value 1 there) and a READ of that same address back into r on the
  /// following <c>gld 2</c> -- i.e. <c>gst</c> genuinely STORES (global := r) and <c>gld</c> genuinely
  /// LOADS (r := global), exactly as their names ordinarily would suggest, with no reversal at all. The
  /// earlier "cross-wire" was a REAL bug in node 508's original F18 source (g/@'s and g/!'s own bodies
  /// really were swapped relative to their names), not a naming-convention quirk -- Stefan fixed it by
  /// swapping the two bodies and re-wiring <c>'gld</c>/<c>'gst</c> to alias <c>g/@</c>/<c>g/!</c>
  /// directly (see <c>Node508Program.Source</c>'s own updated text). Call sites should always go
  /// through this helper (and <see cref="EmitGlobalAssign"/>), never emit <c>gld</c>/<c>gst</c>
  /// directly, so a future correction (should one ever be needed again) has exactly one place to
  /// change.</summary>
  private void EmitGlobalFetch(string label) => EmitCode($"gld {label}");

  /// <summary>Stores r's value into a global, via <c>gst</c> -- named and behaving exactly like an
  /// ordinary CPU "store": the global at <paramref name="label"/> := r. See <see
  /// cref="EmitGlobalFetch"/>'s own remarks for the full history, including the earlier WRONG
  /// resolution this corrects and the hardware trace that corrected it.</summary>
  private void EmitGlobalAssign(string label) => EmitCode($"gst {label}");

  private void EmitLoad(CLvalue lvalue)
  {
    switch (lvalue.Kind)
    {
      case CLvalueKind.Local:
        EmitCode($"ldl {lvalue.Index}");
        EmitCode("push");
        break;
      case CLvalueKind.Parameter:
        EmitCode($"ldp {lvalue.Index}");
        EmitCode("push");
        break;
      case CLvalueKind.Global:
        EmitGlobalFetch(lvalue.Label!);   // r := the global's value -- no address computation at all
        EmitCode("push");
        break;
      default:
        EmitCode($"ldl {lvalue.Index}");   // r := the cached address
        EmitDereferenceLoad();
        break;
    }
  }

  private void EmitStore(CLvalue lvalue)
  {
    switch (lvalue.Kind)
    {
      case CLvalueKind.Local:
        EmitCode("pop");                   // r := value to store; stack: [...] (one word consumed)
        EmitCode($"stl {lvalue.Index}");   // the local := r
        break;
      case CLvalueKind.Parameter:
        EmitCode("pop");                   // r := value to store; stack: [...] (one word consumed)
        EmitCode($"stp {lvalue.Index}");   // the parameter := r
        break;
      case CLvalueKind.Global:
        EmitCode("pop");                   // r := value to store; stack: [...] (one word consumed)
        EmitGlobalAssign(lvalue.Label!);   // the global := r -- no address computation at all
        break;
      default:
        EmitCode($"ldl {lvalue.Index}");   // r := the cached address
        EmitDereferenceStore();
        break;
    }
  }

  /// <summary>Duplicates the current stack top: "pop" into r, then "push; push" -- the first push
  /// restores the original value, the second leaves a copy above it. Net stack effect: +1.</summary>
  private void EmitDup()
  {
    EmitCode("pop");
    EmitCode("push");
    EmitCode("push");
  }

  // ---------------------------------------------------------------------------------------------
  // Float ABI (added 2026-09-26): 'float' expressions, lvalues, and register save/restore. See this
  // class's own doc comment's "Float ABI" section for the full model -- everything below operates on
  // node 305's fr registers, not the ordinary one-word data stack.
  // ---------------------------------------------------------------------------------------------

  private enum CFloatLvalueKind
  {
    Local,
    Parameter,
    Global,

    /// <summary>A <c>float</c> reached through a computed RUNTIME address -- a dereferenced <c>float
    /// *</c>, a <c>float</c> array element, or a <c>float</c> struct member -- added 2026-09-26 alongside
    /// multi-word pointer arithmetic/indexing, once a pointer/array/struct member could first be
    /// 'float'-typed. Parallels <see cref="CLvalueKind.Indirect"/> exactly: <see cref="CFloatLvalue.Index"/>
    /// names a local slot holding the cached address (of the LOW word -- see
    /// <see cref="EmitFloatLoadIndirect"/>'s own remarks), re-derived from that slot each time it's
    /// dereferenced rather than kept live in a register across other code, the same reason
    /// <see cref="CacheIndirectAddress"/> caches into a slot instead of trusting a register to survive.
    /// </summary>
    Indirect,
  }

  /// <summary>The resolved location of an assignable <c>float</c> value. <see cref="Index"/> is the base
  /// LOCAL slot or PARAMETER word offset (the second word lives at <see cref="Index"/>+1, or -1 for Local
  /// -- see <see cref="EmitFloatLoad"/>/<see cref="EmitFloatStore"/>'s own remarks on the direction flip)
  /// for <see cref="CFloatLvalueKind.Local"/>/<see cref="CFloatLvalueKind.Parameter"/>; the cached
  /// address's own local slot for <see cref="CFloatLvalueKind.Indirect"/> (see
  /// <see cref="CacheFloatIndirectAddress"/>); unused for Global. For Global, <see cref="Label"/> is the
  /// low word's own label (the high word is <c>{Label}__hi</c>).</summary>
  private sealed record CFloatLvalue(CFloatLvalueKind Kind, int Index, string? Label = null);

  /// <summary>Resolves a <c>float</c>-context lvalue: a plain named variable (<see
  /// cref="CFloatLvalueKind.Local"/>/<see cref="CFloatLvalueKind.Parameter"/>/<see
  /// cref="CFloatLvalueKind.Global"/>), or -- added 2026-09-26 alongside multi-word pointer
  /// arithmetic/indexing -- a dereferenced <c>float *</c>, a <c>float</c> array element, or a <c>float</c>
  /// struct member (all <see cref="CFloatLvalueKind.Indirect"/>, via <see
  /// cref="CacheFloatIndirectAddress"/>). Each of the three new cases evaluates its own ADDRESS
  /// computation through the exact same (non-float) helpers the general int/pointer lvalue machinery
  /// uses (<see cref="EmitExpr"/> for the pointer operand, <see cref="EmitAddressOfIndex"/>, <see
  /// cref="EmitAddressOfMember"/>) -- an address is always a plain one-word CVM value regardless of what
  /// it points AT, so nothing float-specific is needed until the address is actually dereferenced.
  /// Anything else (an arbitrary non-lvalue expression) is a clear diagnostic, same as before.</summary>
  private CFloatLvalue ResolveFloatLvalue(CExpr expr)
  {
    switch (expr)
    {
      case CNameExpr name:
        {
          CVarSymbol? symbol = LookupVariable(name.Name);
          if (symbol is { Kind: CVarKind.Local })
          {
            return new CFloatLvalue(CFloatLvalueKind.Local, symbol.Index);
          }

          if (symbol is { Kind: CVarKind.Parameter })
          {
            return new CFloatLvalue(CFloatLvalueKind.Parameter, symbol.Index);
          }

          string label = symbol?.GlobalLabel ?? MangleExternalSymbol(name.Name);
          if (symbol is not { Kind: CVarKind.Global })
          {
            if (_globals.ContainsKey(name.Name))
            {
              // A real global defined in this file -- EmitGlobal already declared its storage.
            }
            else if (_functions.ContainsKey(name.Name))
            {
              Error(expr.Location, $"\"{name.Name}\" is a function and cannot be assigned to");
            }
            else
            {
              // Not defined in this file -- assume an external 'float' global the linker will resolve.
              // Both labels need importing -- see EmitGlobal's own remarks.
              _imports.Add(label);
              _imports.Add(label + "__hi");
            }
          }

          return new CFloatLvalue(CFloatLvalueKind.Global, 0, label);
        }

      case CUnaryExpr { Op: CUnaryOp.Dereference } deref:
        {
          CType pointerType = EmitExpr(deref.Operand);
          if (!pointerType.IsPointer || !pointerType.ElementType!.IsFloat)
          {
            Error(expr.Location, $"cannot dereference a value of type \"{pointerType}\" as 'float'");
          }

          return CacheFloatIndirectAddress();
        }

      case CIndexExpr index:
        {
          CType elementType = EmitAddressOfIndex(index);
          if (!elementType.IsFloat)
          {
            Error(expr.Location, $"indexing here yields a value of type \"{elementType}\", not 'float'");
          }

          return CacheFloatIndirectAddress();
        }

      case CMemberAccessExpr member:
        {
          CType fieldType = EmitAddressOfMember(member);
          if (!fieldType.IsFloat)
          {
            Error(expr.Location, $"\"{member.Member}\" is not a 'float' member");
          }

          return CacheFloatIndirectAddress();
        }

      default:
        Error(expr.Location, "only a plain 'float' variable, dereference, array element, or struct member is supported here");
        return new CFloatLvalue(CFloatLvalueKind.Local, 0);
    }
  }

  /// <summary>Loads a <c>float</c> lvalue's two words into <c>fr[destReg]</c> -- pushes LOW then HIGH (in
  /// that order, per this class's own doc comment's word-order remarks), then <c>fpop destReg</c>.</summary>
  private void EmitFloatLoad(CFloatLvalue lvalue, int destReg)
  {
    switch (lvalue.Kind)
    {
      case CFloatLvalueKind.Local:
        EmitFloatLoadLocal(lvalue.Index, destReg);
        break;
      case CFloatLvalueKind.Parameter:
        EmitCode($"ldp {lvalue.Index}");     // low
        EmitCode("push");
        EmitCode($"ldp {lvalue.Index + 1}"); // high
        EmitCode("push");
        EmitCode($"fpop {destReg}");
        break;
      case CFloatLvalueKind.Indirect:
        EmitFloatLoadIndirect(lvalue.Index, destReg);
        break;
      default:
        EmitGlobalFetch(lvalue.Label!);          // low
        EmitCode("push");
        EmitGlobalFetch(lvalue.Label + "__hi");  // high
        EmitCode("push");
        EmitCode($"fpop {destReg}");
        break;
    }
  }

  /// <summary>Stores <c>fr[srcReg]</c> into a <c>float</c> lvalue's two words -- <c>fpush srcReg</c>
  /// leaves [low, high(top)] on the stack (per this class's own doc comment's word-order remarks), so the
  /// FIRST word popped back off is the HIGH word, the SECOND is the LOW word.</summary>
  private void EmitFloatStore(CFloatLvalue lvalue, int srcReg)
  {
    switch (lvalue.Kind)
    {
      case CFloatLvalueKind.Local:
        EmitFloatStoreLocal(lvalue.Index, srcReg);
        break;
      case CFloatLvalueKind.Parameter:
        EmitCode($"fpush {srcReg}");
        EmitCode("pop"); EmitCode($"stp {lvalue.Index + 1}"); // high -> offset+1
        EmitCode("pop"); EmitCode($"stp {lvalue.Index}");     // low -> offset
        break;
      case CFloatLvalueKind.Indirect:
        EmitFloatStoreIndirect(lvalue.Index, srcReg);
        break;
      default:
        EmitCode($"fpush {srcReg}");
        EmitCode("pop"); EmitGlobalAssign(lvalue.Label + "__hi"); // high
        EmitCode("pop"); EmitGlobalAssign(lvalue.Label!);          // low
        break;
    }
  }

  /// <summary>Float ABI: a LOCAL float's two frame slots are HIGH-then-LOW (<paramref name="baseSlot"/>
  /// holds HIGH, <paramref name="baseSlot"/>+1 holds LOW) -- the opposite of a parameter/global -- because
  /// a local's own address is <c>f - offset</c> (DECREASES as the slot number increases), so the LOW word
  /// (which must sit at the LOWER address, per Stefan's own memory-layout rule) has to live at the HIGHER
  /// slot number. See this class's own doc comment for the full derivation. Used both by
  /// <see cref="EmitFloatLoad"/>/<see cref="EmitFloatStore"/> (a named local variable) and by
  /// <see cref="EmitFunction"/>'s own float-register-parameter prologue spill (a fastcall float parameter,
  /// which is ALSO just an ordinary local slot pair once spilled).</summary>
  private void EmitFloatLoadLocal(int baseSlot, int destReg)
  {
    EmitCode($"ldl {baseSlot + 1}"); // low
    EmitCode("push");
    EmitCode($"ldl {baseSlot}");     // high
    EmitCode("push");
    EmitCode($"fpop {destReg}");
  }

  private void EmitFloatStoreLocal(int baseSlot, int srcReg)
  {
    EmitCode($"fpush {srcReg}");
    EmitCode("pop"); EmitCode($"stl {baseSlot}");     // high -> baseSlot
    EmitCode("pop"); EmitCode($"stl {baseSlot + 1}"); // low -> baseSlot+1
  }

  /// <summary>Float ABI, added 2026-09-26: caches an address already on top of the ordinary (non-float)
  /// stack into a fresh local slot, for <see cref="CFloatLvalueKind.Indirect"/> -- a dereferenced <c>float
  /// *</c>, a <c>float</c> array element, or a <c>float</c> struct member. Mirrors
  /// <see cref="CacheIndirectAddress"/> exactly (identical single-word caching, same underlying node-306
  /// dereference sequence); kept as a separate method only because it returns a <see cref="CFloatLvalue"/>
  /// instead of a <see cref="CLvalue"/>. The cached address always points at the value's own LOW word --
  /// see <see cref="EmitFloatLoadIndirect"/>'s own remarks for why.</summary>
  private CFloatLvalue CacheFloatIndirectAddress()
  {
    int addressSlot = _currentFunction!.NextLocalSlot++;
    EmitCode("pop");                // r := address; stack: []
    EmitCode($"stl {addressSlot}"); // addressSlot := r (the address)
    return new CFloatLvalue(CFloatLvalueKind.Indirect, addressSlot);
  }

  /// <summary>Float ABI, added 2026-09-26: loads <paramref name="addressSlot"/>'s cached address into r,
  /// optionally offset by one word first (entirely via the ordinary one-word stack -- <c>ldl</c> only ever
  /// sets r, so adding 1 needs the same "push, pushlit, pop, add, pop" dance <see
  /// cref="EmitLocalOrParameterAddress"/> uses for "f +/- offset"). Stack-neutral overall (whatever was on
  /// the stack before this call is still there after it, undisturbed underneath any temporaries this
  /// pushes and pops again) -- safe to call with a not-yet-stored value already sitting on top of the
  /// stack, which is exactly what <see cref="EmitFloatStoreIndirect"/> needs.</summary>
  private void EmitLoadAddressSlotPlusOffsetIntoR(int addressSlot, int offset)
  {
    if (offset == 0)
    {
      EmitCode($"ldl {addressSlot}");
      return;
    }

    EmitCode($"ldl {addressSlot}"); // r := the cached address
    EmitCode("push");               // stack: [..., address]
    EmitCode($"pushlit {offset}");  // stack: [..., address, offset]
    EmitCode("pop");                // r := offset; stack: [..., address]
    EmitCode("add");                // stack: [..., address + offset]
    EmitCode("pop");                // r := address + offset; stack: [...] (back to where it started)
  }

  /// <summary>Float ABI, added 2026-09-26: loads the two words at a cached RUNTIME address (<see
  /// cref="CFloatLvalueKind.Indirect"/>) into <c>fr[destReg]</c>. The LOW word lives at the cached address
  /// itself and the HIGH word at address+1 -- the same "low at the lower address" layout a local/global
  /// float already uses (see <see cref="EmitFloatLoadLocal"/>'s own remarks for the local case's reversed
  /// SLOT order, which is exactly what makes its actual memory-ADDRESS order match this one), so a
  /// pointer to <c>float</c> is simply the address of its low word, with no special-casing needed anywhere
  /// pointer arithmetic itself happens. Re-derives the address from its cached slot twice (once for low,
  /// once for high) rather than trying to keep an address register live across both dereferences,
  /// mirroring <see cref="EmitLoad"/>/<see cref="EmitStore"/>'s own "default" (non-float Indirect) case,
  /// which re-derives its own single-word address the same way.</summary>
  private void EmitFloatLoadIndirect(int addressSlot, int destReg)
  {
    EmitLoadAddressSlotPlusOffsetIntoR(addressSlot, 0);
    EmitDereferenceLoad();  // stack: [low]
    EmitLoadAddressSlotPlusOffsetIntoR(addressSlot, 1);
    EmitDereferenceLoad();  // stack: [low, high]
    EmitCode($"fpop {destReg}");
  }

  /// <summary>Float ABI, added 2026-09-26: stores <c>fr[srcReg]</c>'s two words to a cached RUNTIME
  /// address (<see cref="CFloatLvalueKind.Indirect"/>) -- the write-side counterpart of <see
  /// cref="EmitFloatLoadIndirect"/>, same low-at-the-address/high-at-address+1 layout. <c>fpush srcReg</c>
  /// leaves [low, high(top)] on the stack (per this class's own doc comment's word-order remarks), so the
  /// HIGH word is stored FIRST (already on top, to address+1) and the LOW word second (to the address
  /// itself) -- matching <see cref="EmitFloatStoreLocal"/>/<see cref="EmitFloatStore"/>'s own "pop HIGH
  /// first" convention.</summary>
  private void EmitFloatStoreIndirect(int addressSlot, int srcReg)
  {
    EmitCode($"fpush {srcReg}"); // stack: [low, high]
    EmitLoadAddressSlotPlusOffsetIntoR(addressSlot, 1); // r := address+1; stack unchanged: [low, high]
    EmitDereferenceStore();                             // pops "high", stores to address+1; stack: [low]
    EmitLoadAddressSlotPlusOffsetIntoR(addressSlot, 0); // r := address; stack unchanged: [low]
    EmitDereferenceStore();                             // pops "low", stores to address; stack: []
  }

  /// <summary>Materializes an arbitrary compile-time <c>float</c> constant into <c>fr[destReg]</c>: splits
  /// its IEEE-754 bit pattern into two 16-bit words and pushes LOW then HIGH (matching
  /// <see cref="EmitFloatLoad"/>'s own convention) before <c>fpop destReg</c>. There is no hardware
  /// "load an arbitrary float immediate into a register" primitive -- only <c>fconst</c>'s 8 fixed ROM
  /// constants (see <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet.FloatingPointConstantMnemonic"/>'s
  /// own remarks) -- so every literal goes through the ordinary stack, exactly like loading a variable's
  /// value does.</summary>
  private void EmitFloatLiteralInto(float value, int destReg)
  {
    int bits = BitConverter.SingleToInt32Bits(value);
    int low = bits & 0xFFFF;
    int high = (bits >> 16) & 0xFFFF;
    EmitCode($"pushlit {low}");
    EmitCode($"pushlit {high}");
    EmitCode($"fpop {destReg}");
  }

  private static string FloatBinaryMnemonic(CBinaryOp op) => op switch
  {
    CBinaryOp.Add => "fadd",
    CBinaryOp.Subtract => "fsub",
    CBinaryOp.Multiply => "fmul",
    CBinaryOp.Divide => "fdiv",
    _ => throw new InvalidOperationException($"'{op}' has no floating-point hardware opcode"),
  };

  /// <summary>The three directly-hardware-backed <c>float</c> built-ins this compiler exposes, per
  /// Stefan's own scope instruction ("only add functions that are directly supported by the FP
  /// subprocessor should be used by the compiler") -- C has no operator for min/max/absolute-value, so
  /// these are recognized here purely by name (like a real compiler's <c>__builtin_*</c> intrinsics),
  /// never emitted as an actual <c>call</c>. Named after the standard C library's own <c>&lt;math.h&gt;</c>
  /// float-suffixed convention (<c>fabsf</c>/<c>fminf</c>/<c>fmaxf</c>) rather than the raw CVM mnemonics,
  /// so a future float-library can define real (non-built-in) versions under the SAME names for other
  /// toolchains/targets without a collision here.</summary>
  private static readonly Dictionary<string, (string Mnemonic, int Arity)> FloatBuiltins = new(StringComparer.Ordinal)
  {
    ["fabsf"] = ("fabs", 1),
    ["fminf"] = ("fmin", 2),
    ["fmaxf"] = ("fmax", 2),
  };

  private bool TryGetFloatBuiltin(CCallExpr call, out string mnemonic, out int arity)
  {
    if (FloatBuiltins.TryGetValue(call.FunctionName, out (string Mnemonic, int Arity) entry))
    {
      if (call.Arguments.Count != entry.Arity)
      {
        Error(call.Location, $"\"{call.FunctionName}\" expects {entry.Arity} argument(s), but {call.Arguments.Count} were given");
      }

      mnemonic = entry.Mnemonic;
      arity = entry.Arity;
      return true;
    }

    mnemonic = string.Empty;
    arity = 0;
    return false;
  }

  /// <summary>Float ABI: a best-effort, code-free STATIC check of whether <paramref name="expr"/> would,
  /// if evaluated, produce a <c>float</c> value -- used only to disambiguate a handful of contexts that
  /// need to know BEFORE emitting any code (a cast's operand, an assignment's target). Deliberately not a
  /// full type-checker (this compiler has none -- see this class's own doc comment); every <c>float</c>
  /// sub-expression this "basic" support actually needs to recognize eventually bottoms out in a literal,
  /// a name, a supported operator over those, a call, a cast, or -- added 2026-09-26 alongside multi-word
  /// pointer arithmetic/indexing -- a dereference/index/member-access resolved via
  /// <see cref="TryGetStaticType"/>, all covered below.</summary>
  private bool LooksLikeFloatExpr(CExpr expr) => expr switch
  {
    CFloatLiteralExpr => true,
    CNameExpr name => IsFloatNamedVariable(name),
    CUnaryExpr { Op: CUnaryOp.Plus or CUnaryOp.Minus } unary => LooksLikeFloatExpr(unary.Operand),
    CUnaryExpr { Op: CUnaryOp.Dereference } => TryGetStaticType(expr, out CType derefType) && derefType.IsFloat,
    CIndexExpr => TryGetStaticType(expr, out CType indexType) && indexType.IsFloat,
    CMemberAccessExpr => TryGetStaticType(expr, out CType memberType) && memberType.IsFloat,
    CBinaryExpr { Op: CBinaryOp.Add or CBinaryOp.Subtract or CBinaryOp.Multiply or CBinaryOp.Divide } binary =>
        LooksLikeFloatExpr(binary.Left) || LooksLikeFloatExpr(binary.Right),
    CCallExpr call => (FloatBuiltins.ContainsKey(call.FunctionName)) || (_functions.TryGetValue(call.FunctionName, out CFunctionSignature? sig) && sig.ReturnType.IsFloat),
    CCastExpr cast => cast.TargetType.IsFloat,
    CAssignExpr assign => LooksLikeFloatExpr(assign.Target),
    CCompoundAssignExpr compoundAssign => LooksLikeFloatExpr(compoundAssign.Target),
    _ => false,
  };

  /// <summary>Float ABI, added 2026-09-26 alongside multi-word pointer arithmetic/indexing: a
  /// side-effect-free, best-effort STATIC re-derivation of an expression's type, used only by
  /// <see cref="LooksLikeFloatExpr"/> to decide -- before emitting anything -- whether a
  /// dereference/index/member-access expression would produce a 'float' value now that a
  /// pointer/array/struct member can first be 'float'-typed. Mirrors <see cref="ResolveLvalue"/>/<see
  /// cref="EmitAddressOf"/>'s own type logic for exactly these three node kinds, but computes no code:
  /// the expression itself is never evaluated here (a variable/parameter/global's declared TYPE is looked
  /// up, never a runtime value), so this is safe to call speculatively even on an expression with side
  /// effects (the side effects still only actually happen once, when the caller goes on to really emit
  /// it). Not a full type-checker -- an expression shape this compiler doesn't otherwise support returns
  /// <see langword="false"/> rather than guessing.</summary>
  private bool TryGetStaticType(CExpr expr, out CType type)
  {
    switch (expr)
    {
      case CNameExpr name:
        {
          CVarSymbol? symbol = LookupVariable(name.Name);
          if (symbol is not null)
          {
            type = symbol.Type;
            return true;
          }

          if (_globals.TryGetValue(name.Name, out CGlobalSymbol? global))
          {
            type = global.Type;
            return true;
          }

          type = CType.Int;
          return false;
        }

      case CUnaryExpr { Op: CUnaryOp.Dereference } deref:
        {
          if (TryGetStaticType(deref.Operand, out CType pointerType) && pointerType.IsPointer)
          {
            type = pointerType.ElementType!;
            return true;
          }

          type = CType.Int;
          return false;
        }

      case CIndexExpr index:
        {
          if (TryGetStaticType(index.Base, out CType baseType))
          {
            CType decayed = baseType.Decay();
            if (decayed.IsPointer)
            {
              type = decayed.ElementType!;
              return true;
            }
          }

          type = CType.Int;
          return false;
        }

      case CMemberAccessExpr member:
        {
          if (!TryGetStaticType(member.Base, out CType baseType))
          {
            type = CType.Int;
            return false;
          }

          CType? structType = member.IsArrow
              ? (baseType.IsPointer && baseType.ElementType!.IsStruct ? baseType.ElementType : null)
              : (baseType.IsStruct ? baseType : null);

          CStructMember? field = structType?.Members?.FirstOrDefault(m => m.Name == member.Member);
          if (field is null)
          {
            type = CType.Int;
            return false;
          }

          type = field.Type;
          return true;
        }

      case CCastExpr cast:
        type = cast.TargetType;
        return true;

      default:
        type = CType.Int;
        return false;
    }
  }

  private bool IsFloatNamedVariable(CNameExpr name)
  {
    CVarSymbol? symbol = LookupVariable(name.Name);
    if (symbol is not null)
    {
      return symbol.Type.IsFloat;
    }

    return _globals.TryGetValue(name.Name, out CGlobalSymbol? global) && global.Type.IsFloat;
  }

  /// <summary>Saves <c>fr[from..from+count-1]</c> onto the ordinary data stack, ascending -- the first
  /// half of the caller-save/restore discipline this class's own doc comment describes. Paired with
  /// <see cref="EmitFloatRegisterRestoreRange"/>.</summary>
  private void EmitFloatRegisterSaveRange(int from, int count)
  {
    for (int r = from; r < from + count; r++)
    {
      EmitCode($"fpush {r}");
    }
  }

  /// <summary>Restores <c>fr[from..from+count-1]</c> from the ordinary data stack, DESCENDING (LIFO order
  /// matching <see cref="EmitFloatRegisterSaveRange"/>'s own ascending save order).</summary>
  private void EmitFloatRegisterRestoreRange(int from, int count)
  {
    for (int r = from + count - 1; r >= from; r--)
    {
      EmitCode($"fpop {r}");
    }
  }

  /// <summary>
  /// The float-expression evaluator's public entry point -- evaluates <paramref name="expr"/> into
  /// <c>fr[destReg]</c>. Sets <see cref="_liveFloatRegisterCount"/> to <paramref name="destReg"/> for the
  /// duration (see this class's own doc comment: every register below the CURRENT destReg is, by
  /// construction, still needed by an enclosing operation, and <see cref="EmitCall"/> reads this field to
  /// decide how many registers a call needs to protect), restoring the previous value afterward so a
  /// caller further up the recursion (or a sibling call/argument evaluation) sees its own correct value
  /// again.
  /// </summary>
  private void EmitFloatExprInto(CExpr expr, int destReg)
  {
    if (destReg >= FloatRegisterCount)
    {
      Error(expr.Location, $"this expression is too complex: it needs more than {FloatRegisterCount} 'float' registers (fr[0..{FloatRegisterCount - 1}]) to evaluate");
      EmitFloatLiteralInto(0f, FloatRegisterCount - 1);
      return;
    }

    int outerLiveCount = _liveFloatRegisterCount;
    _liveFloatRegisterCount = destReg;
    EmitFloatExprIntoCore(expr, destReg);
    _liveFloatRegisterCount = outerLiveCount;
  }

  private void EmitFloatExprIntoCore(CExpr expr, int destReg)
  {
    switch (expr)
    {
      case CFloatLiteralExpr literal:
        EmitFloatLiteralInto(literal.Value, destReg);
        return;

      case CIntLiteralExpr:
        // No implicit int->float conversion in this first pass -- see this class's own doc comment.
        Error(expr.Location, "an integer literal cannot be used directly where a 'float' is expected (implicit int-to-float conversion is not yet supported)");
        EmitFloatLiteralInto(0f, destReg);
        return;

      case CNameExpr name:
        if (!IsFloatNamedVariable(name))
        {
          Error(expr.Location, $"\"{name.Name}\" is not a 'float' value");
          EmitFloatLiteralInto(0f, destReg);
          return;
        }

        EmitFloatLoad(ResolveFloatLvalue(name), destReg);
        return;

      case CUnaryExpr { Op: CUnaryOp.Plus } unary:
        EmitFloatExprInto(unary.Operand, destReg);
        return;

      case CUnaryExpr { Op: CUnaryOp.Minus } unary:
        EmitFloatExprInto(unary.Operand, destReg);
        EmitCode($"fneg {destReg} {destReg}");
        return;

      case CUnaryExpr { Op: CUnaryOp.Dereference }:
      case CIndexExpr:
      case CMemberAccessExpr:
        // Added 2026-09-26 alongside multi-word pointer arithmetic/indexing: a dereferenced 'float *', a
        // 'float' array element, or a 'float' struct member, reached here either directly (a bare
        // "*fp;"/"arr[i];"/"s.member;" float-context expression) or as an operand nested inside a larger
        // float expression (e.g. "*fp + 1.0f") -- ResolveFloatLvalue's own new cases for these three node
        // kinds do the actual address computation and float-ness check; this is just the read side.
        EmitFloatLoad(ResolveFloatLvalue(expr), destReg);
        return;

      case CUnaryExpr:
        Error(expr.Location, "this operator is not yet supported for 'float' (only unary +/- are)");
        EmitFloatLiteralInto(0f, destReg);
        return;

      case CBinaryExpr { Op: CBinaryOp.Add or CBinaryOp.Subtract or CBinaryOp.Multiply or CBinaryOp.Divide } binary:
        {
          int rightReg = destReg + 1;
          EmitFloatExprInto(binary.Left, destReg);
          EmitFloatExprInto(binary.Right, rightReg);
          EmitCode($"{FloatBinaryMnemonic(binary.Op)} {destReg} {rightReg}");
          return;
        }

      case CBinaryExpr:
        Error(expr.Location, "this operator is not yet supported for 'float' (comparisons, bitwise, shift and modulo are not) -- only +, -, *, / are");
        EmitFloatLiteralInto(0f, destReg);
        return;

      case CAssignExpr assign:
        {
          CFloatLvalue target = ResolveFloatLvalue(assign.Target);
          EmitFloatExprInto(assign.Value, destReg);
          EmitFloatStore(target, destReg);
          return;
        }

      case CCallExpr call:
        {
          if (TryGetFloatBuiltin(call, out string builtinMnemonic, out int builtinArity))
          {
            // Arity mismatches were already reported by TryGetFloatBuiltin above -- a missing argument
            // here (wrong-arity call) falls back to a harmless 0.0 literal rather than crashing or
            // re-recursing into this same call node.
            CExpr placeholder = new CFloatLiteralExpr(call.Location, 0f);
            if (builtinArity == 1)
            {
              EmitFloatExprInto(call.Arguments.Count > 0 ? call.Arguments[0] : placeholder, destReg);
              EmitCode($"{builtinMnemonic} {destReg} {destReg}");
            }
            else
            {
              int rightReg = destReg + 1;
              EmitFloatExprInto(call.Arguments.Count > 0 ? call.Arguments[0] : placeholder, destReg);
              EmitFloatExprInto(call.Arguments.Count > 1 ? call.Arguments[1] : placeholder, rightReg);
              EmitCode($"{builtinMnemonic} {destReg} {rightReg}");
            }

            return;
          }

          CType returnType = EmitCall(call, floatResultDestRegister: destReg);
          if (!returnType.IsFloat)
          {
            Error(call.Location, $"\"{call.FunctionName}\" does not return a value of type 'float'");
          }

          return;
        }

      case CCastExpr cast when cast.TargetType.IsFloat:
        if (LooksLikeFloatExpr(cast.Operand))
        {
          EmitFloatExprInto(cast.Operand, destReg);
        }
        else
        {
          Error(cast.Location, "casting a non-'float' value to 'float' is not yet supported by this compiler");
          EmitFloatLiteralInto(0f, destReg);
        }

        return;

      default:
        Error(expr.Location, $"this expression is not yet supported in a 'float' context (\"{expr.GetType().Name}\")");
        EmitFloatLiteralInto(0f, destReg);
        return;
    }
  }

  /// <summary>Pushes the address of a local (or, since 2026-09-09, a parameter) at frame-relative
  /// <paramref name="offset"/> -- replaces the retired <c>lal</c>/<c>lap</c> mnemonics ("'lal' &amp;
  /// 'lap' are removed. use ''f' or 'fpush' from node 506 and add the offset to calculate the address of
  /// a local or parameter." -- quoted verbatim as originally given). <c>pushf</c> pushes node 506's own
  /// frame pointer <c>f</c> onto the data stack; a LOCAL's address is <c>f - offset</c> (matching node
  /// 506's own <c>f/main</c> dispatch, which negates a local's offset with <c>inv</c> before adding it to
  /// <c>f</c> for <c>ldl</c>/<c>stl</c>), while a PARAMETER's address is <c>f + offset</c> (no negation,
  /// matching <c>ldp</c>/<c>stp</c>). Not yet confirmed against real hardware -- see this class's own
  /// remarks.
  ///
  /// <b>RENAMED, 2026-09-16.</b> This method used to emit the CVM mnemonic <c>fpush</c> for node 506's own
  /// push word. Stefan required node 306's own, unrelated <c>'fpush</c> word to be wired as CVM mnemonic
  /// <c>fpush</c> too ("'fpush' from node 306 must be wired as fpush"), which collided with this one;
  /// Stefan resolved the collision by renaming node 506's own push word to <c>'pushf</c> ("i renamed
  /// 'fpush' of node 506 into 'pushf'"). This method emits the literal mnemonic string directly rather
  /// than referencing <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet.PushFrameMnemonic"/>, so the rename
  /// had to be applied here by hand, not just at the constant's definition -- see that constant's own
  /// remarks for the full rationale.</summary>
  private void EmitLocalOrParameterAddress(int offset, bool isParameter)
  {
    EmitCode("pushf");
    EmitCode($"pushlit {offset}");
    EmitCode("pop");
    EmitCode(isParameter ? "add" : "sub");
  }

  /// <summary>Computes and pushes the address of an lvalue expression, WITHOUT latching it into "t"
  /// (unlike <see cref="ResolveLvalue"/>) -- this is the address-of ("&amp;") operator's own codegen, and
  /// also <see cref="CIndexExpr"/>'s helper for computing an element's address before <c>xt</c>.</summary>
  private CType EmitAddressOf(CExpr expr)
  {
    switch (expr)
    {
      case CNameExpr name:
        {
          CVarSymbol? symbol = LookupVariable(name.Name);
          if (symbol is { Kind: CVarKind.Local })
          {
            // Float ABI (2026-09-26 follow-up, once 'float *' became legal): a LOCAL float's two frame
            // slots are stored HIGH-then-LOW (see EmitFloatLoadLocal's own remarks for why -- it's what
            // makes the actual MEMORY address order come out low-then-high, matching every other float
            // storage location), so the address a 'float *' must hold (the LOW word's own address) is
            // this local's own slot number PLUS one, not the slot number itself (which is the HIGH
            // word's address).
            int offset = symbol.Type.IsFloat ? symbol.Index + 1 : symbol.Index;
            EmitLocalOrParameterAddress(offset, isParameter: false);
            return CType.PointerTo(symbol.Type);
          }

          if (symbol is { Kind: CVarKind.Parameter })
          {
            EmitLocalOrParameterAddress(symbol.Index, isParameter: true);
            return CType.PointerTo(symbol.Type);
          }

          string label = symbol?.GlobalLabel ?? MangleExternalSymbol(name.Name);
          CType type;
          if (symbol is { Kind: CVarKind.Global })
          {
            // A static local -- its storage is already emitted under this mangled label, no import needed.
            type = symbol.Type;
          }
          else if (_globals.TryGetValue(name.Name, out CGlobalSymbol? global))
          {
            // A real global -- EmitGlobal already declared it (as storage or, for "extern", an import).
            type = global.Type;
          }
          else
          {
            Error(expr.Location, $"\"{name.Name}\" is not declared");
            type = CType.Int;
          }

          EmitCode($"pushlit {label}");
          return CType.PointerTo(type);
        }

      case CUnaryExpr { Op: CUnaryOp.Dereference } deref:
        // "&*p" is just "p" -- evaluate the pointer expression directly, no extra indirection.
        return EmitExpr(deref.Operand);

      case CIndexExpr index:
        return CType.PointerTo(EmitAddressOfIndex(index));

      case CMemberAccessExpr member:
        return CType.PointerTo(EmitAddressOfMember(member));

      default:
        Error(expr.Location, "cannot take the address of this expression");
        EmitCode("pushlit 0");
        return CType.PointerTo(CType.Int);
    }
  }

  /// <summary>
  /// Pushes the address of "Base.Member" or "Base-&gt;Member" (the base's own address plus a
  /// compile-time-known member offset -- no scaling needed, since an offset here is already expressed
  /// in whole CVM words, exactly like <see cref="EmitAddressOfIndex"/>'s own "no scaling multiplication"
  /// remark) and returns the member's own type. Added 2026-09-26 alongside basic <c>struct</c> support.
  ///
  /// <b>"-&gt;" vs ".", and how chained access (<c>a.b.c</c>, <c>p-&gt;q.r</c>, ...) falls out for
  /// free.</b> "-&gt;" evaluates <see cref="CMemberAccessExpr.Base"/> as an ordinary VALUE (it must
  /// already be a pointer to a struct); "." instead recurses into <see cref="EmitAddressOf"/> on <see
  /// cref="CMemberAccessExpr.Base"/> (it must be an addressable struct VALUE) -- and <see
  /// cref="EmitAddressOf"/>'s own <see cref="CMemberAccessExpr"/> case calls back into this method, so a
  /// chain of any length composes purely through this mutual recursion, each level adding its own
  /// member's offset onto whatever address the level below it already pushed. No separate "direct local
  /// slot" fast path exists yet for a single-level struct member access the way one does for a local
  /// array's constant-indexed element (see <see cref="TryGetConstantLocalArrayElementSlot"/>) -- every
  /// struct member access goes through <see cref="CacheIndirectAddress"/>/node 306's dereference
  /// sequence, correctly but not yet as cheaply as it could be; flagged in the design doc as a future
  /// optimization in the same spirit as that one, not attempted here.
  /// </summary>
  private CType EmitAddressOfMember(CMemberAccessExpr member)
  {
    CType structType;
    if (member.IsArrow)
    {
      CType pointerType = EmitExpr(member.Base);
      if (!pointerType.IsPointer || !pointerType.ElementType!.IsStruct)
      {
        // NOTE: EmitExpr above already left exactly one word on the stack (the uniform codegen
        // invariant -- see this class's own doc comment) -- do not push another placeholder here, or
        // every caller up the chain (CacheIndirectAddress in particular) would see an extra, unbalanced
        // word. Matches ResolveLvalue's own CUnaryExpr{Dereference} case, which has the identical shape.
        Error(member.Location, $"\"->\" requires a pointer to a struct, but this expression has type \"{pointerType}\"");
        return CType.Int;
      }

      structType = pointerType.ElementType!;
    }
    else
    {
      CType baseAddressType = EmitAddressOf(member.Base);
      if (!baseAddressType.IsPointer || !baseAddressType.ElementType!.IsStruct)
      {
        Error(member.Location, $"\".\" requires a struct value, but this expression has type \"{(baseAddressType.IsPointer ? baseAddressType.ElementType : baseAddressType)}\"");
        return CType.Int;
      }

      structType = baseAddressType.ElementType!;
    }

    CStructMember? field = structType.Members!.FirstOrDefault(m => m.Name == member.Member);
    if (field is null)
    {
      Error(member.Location, $"\"{structType}\" has no member named \"{member.Member}\"");
      return CType.Int;
    }

    if (field.Offset != 0)
    {
      EmitCode($"pushlit {field.Offset}");
      EmitCode("pop");
      EmitCode("add");
    }

    return field.Type;
  }

  /// <summary>Pushes the address of <c>Base[Index]</c> (base address + Index scaled by the element's own
  /// size -- see <see cref="EmitScaleByElementSize"/>'s own remarks for why scaling is needed at all, added
  /// 2026-09-26 alongside multi-word pointer arithmetic/indexing) and returns the element's type. When
  /// <c>Base[Index]</c> is a compile-time-constant-indexed element of a local array, shares <see
  /// cref="TryGetConstantLocalArrayElementSlot"/> with <see cref="ResolveLvalue"/> to compute the address
  /// directly from the combined slot number in one <see cref="EmitLocalOrParameterAddress"/> call, rather
  /// than evaluating the array's own base address and the index separately and adding them at
  /// runtime.</summary>
  private CType EmitAddressOfIndex(CIndexExpr index)
  {
    if (TryGetConstantLocalArrayElementSlot(index, out int directSlot, out CType directElementType))
    {
      EmitLocalOrParameterAddress(directSlot, isParameter: false);
      return directElementType;
    }

    CType baseType = EmitExpr(index.Base).Decay();
    if (!baseType.IsPointer)
    {
      Error(index.Location, $"cannot index into a value of type \"{baseType}\"");
      baseType = CType.PointerTo(CType.Int);
    }

    EmitExpr(index.Index);
    EmitScaleByElementSize(Math.Max(baseType.ElementType!.SizeInWords, 1));
    EmitCode("pop");
    EmitCode("add");
    return baseType.ElementType!;
  }

  private CType EmitAssign(CAssignExpr assign)
  {
    if (LooksLikeFloatExpr(assign.Target))
    {
      // Float ABI: reached only from the general (int/pointer) expression path, when a 'float'
      // assignment's OWN result value is needed by an enclosing expression (e.g. chained assignment) --
      // see this class's own doc comment. destReg 0: a top-level, freshly-entered float context.
      CFloatLvalue floatTarget = ResolveFloatLvalue(assign.Target);
      EmitFloatExprInto(assign.Value, 0);
      EmitFloatStore(floatTarget, 0);
      return CType.Float;
    }

    CLvalue target = ResolveLvalue(assign.Target);
    CType rhsType = EmitExpr(assign.Value);
    if (rhsType.IsFloat)
    {
      Error(assign.Location, $"cannot assign a 'float' value to a target of type \"{target.Type}\"");
    }
    else if (target.Type.IsStruct || rhsType.IsStruct)
    {
      // See CType.StructOf's own remarks: whole-struct assignment ("s1 = s2;") is not yet supported --
      // assign through its own members instead. Still falls through to EmitDup/EmitStore below, same as
      // every other error branch in this method: the diagnostic is what actually stops the build, not a
      // separate early return.
      Error(assign.Location, "assigning a whole struct value is not yet supported by this compiler (assign through its own members instead)");
    }

    EmitDup();
    EmitStore(target);
    return target.Type;
  }

  /// <summary>Assignment used purely for its side effect -- a bare <c>a = expr;</c> statement, or a
  /// plain declaration's initializer -- where nothing downstream needs the assignment's own result
  /// value (C's rule that "a = expr" is itself an expression only matters when something actually
  /// consumes that value, e.g. chained assignment or "if ((a = f()))"). Skips <see cref="EmitDup"/>
  /// entirely: <see cref="EmitStore"/> already nets to "-1 word" on its own (it pops the value it
  /// stores), which exactly cancels the "+1 word" <see cref="EmitExpr"/> just produced, so the net
  /// stack effect of the whole assignment is zero with no discard <c>pop</c> needed either. 2026-09-09:
  /// this is what makes <c>int a = 3;</c> compile to just <c>pushlit 3; pop; stl 0</c> instead of
  /// dup-ing the value and immediately throwing the extra copy away. See <see cref="EmitAssign"/> for
  /// the expression-context version that keeps the residual value.</summary>
  private void EmitAssignForEffect(CAssignExpr assign)
  {
    if (LooksLikeFloatExpr(assign.Target))
    {
      CFloatLvalue floatTarget = ResolveFloatLvalue(assign.Target);
      EmitFloatExprInto(assign.Value, 0);
      EmitFloatStore(floatTarget, 0);
      return;
    }

    CLvalue target = ResolveLvalue(assign.Target);
    CType rhsType = EmitExpr(assign.Value);
    if (rhsType.IsFloat)
    {
      Error(assign.Location, $"cannot assign a 'float' value to a target of type \"{target.Type}\"");
    }
    else if (target.Type.IsStruct || rhsType.IsStruct)
    {
      Error(assign.Location, "assigning a whole struct value is not yet supported by this compiler (assign through its own members instead)");
    }

    EmitStore(target);
  }

  /// <summary>Float ABI: only <c>+=</c>/<c>-=</c>/<c>*=</c>/<c>/=</c> are supported for a 'float' target
  /// (every one of these has a direct hardware opcode -- see this class's own doc comment); every other
  /// compound-assignment operator is rejected with a clear diagnostic.</summary>
  private static bool TryGetFloatCompoundOp(CBinaryOp op, out string mnemonic)
  {
    if (op is CBinaryOp.Add or CBinaryOp.Subtract or CBinaryOp.Multiply or CBinaryOp.Divide)
    {
      mnemonic = FloatBinaryMnemonic(op);
      return true;
    }

    mnemonic = string.Empty;
    return false;
  }

  private CType EmitCompoundAssign(CCompoundAssignExpr expr)
  {
    if (LooksLikeFloatExpr(expr.Target))
    {
      if (!TryGetFloatCompoundOp(expr.Op, out string mnemonic))
      {
        Error(expr.Location, $"'{expr.Op}=' is not supported for 'float' (only +=, -=, *=, /= are)");
        return CType.Float;
      }

      CFloatLvalue target = ResolveFloatLvalue(expr.Target);
      EmitFloatLoad(target, 0);
      EmitFloatExprInto(expr.Value, 1);
      EmitCode($"{mnemonic} 0 1");
      EmitFloatStore(target, 0);
      return CType.Float;
    }

    CLvalue intTarget = ResolveLvalue(expr.Target);
    EmitLoad(intTarget);
    CType rhsType = EmitExpr(expr.Value);
    // EmitBinaryOperation itself performs the single "pop" the ABI's binary-op pattern needs (it
    // expects both operands already sitting on the stack, lhs then rhs) -- do not pop again here.
    CType resultType = EmitBinaryOperation(expr.Op, intTarget.Type, rhsType, expr.Location);
    EmitDup();
    EmitStore(intTarget);
    return resultType;
  }

  /// <summary>The for-effect twin of <see cref="EmitCompoundAssign"/> -- see
  /// <see cref="EmitAssignForEffect"/>'s own remarks for why skipping the dup is safe whenever nothing
  /// needs the expression's own result (a bare <c>a += expr;</c> statement).</summary>
  private void EmitCompoundAssignForEffect(CCompoundAssignExpr expr)
  {
    if (LooksLikeFloatExpr(expr.Target))
    {
      if (!TryGetFloatCompoundOp(expr.Op, out string mnemonic))
      {
        Error(expr.Location, $"'{expr.Op}=' is not supported for 'float' (only +=, -=, *=, /= are)");
        return;
      }

      CFloatLvalue target = ResolveFloatLvalue(expr.Target);
      EmitFloatLoad(target, 0);
      EmitFloatExprInto(expr.Value, 1);
      EmitCode($"{mnemonic} 0 1");
      EmitFloatStore(target, 0);
      return;
    }

    CLvalue intTarget = ResolveLvalue(expr.Target);
    EmitLoad(intTarget);
    CType rhsType = EmitExpr(expr.Value);
    EmitBinaryOperation(expr.Op, intTarget.Type, rhsType, expr.Location);
    EmitStore(intTarget);
  }

  /// <summary>Pre/post increment and decrement, both built on the same lvalue primitives. Pre- dups the
  /// NEW value (computed, then duplicated, then stored -- result is the value now in memory); post- dups
  /// the OLD value immediately after loading it, before computing the new one, so the expression's own
  /// result is the value memory held before the update. Both leave exactly one word on the stack, per
  /// this class's own uniform codegen invariant.</summary>
  private CType EmitIncrementOrDecrement(CUnaryExpr expr)
  {
    if (LooksLikeFloatExpr(expr.Operand))
    {
      Error(expr.Location, "'++'/'--' on 'float' is not yet supported by this compiler");
      return CType.Float;
    }

    bool isIncrement = expr.Op is CUnaryOp.PreIncrement or CUnaryOp.PostIncrement;
    bool isPre = expr.Op is CUnaryOp.PreIncrement or CUnaryOp.PreDecrement;
    CBinaryOp op = isIncrement ? CBinaryOp.Add : CBinaryOp.Subtract;

    CLvalue target = ResolveLvalue(expr.Operand);

    // EmitBinaryOperation performs its own single "pop" (both operands are already on the stack by the
    // time it runs, lhs then rhs) -- neither branch below pops again first.
    if (isPre)
    {
      EmitLoad(target);          // stack: [old]
      EmitCode("pushlit 1");     // stack: [old, 1]
      EmitBinaryOperation(op, target.Type, CType.Int, expr.Location); // stack: [new]
      EmitDup();                 // stack: [new, new]
      EmitStore(target);         // stack: [new]  -- expression result is the NEW value.
    }
    else
    {
      EmitLoad(target);          // stack: [old]
      EmitDup();                 // stack: [old, old]
      EmitCode("pushlit 1");     // stack: [old, old, 1]
      EmitBinaryOperation(op, target.Type, CType.Int, expr.Location); // stack: [old, new]
      EmitStore(target);         // stack: [old]  -- expression result is the OLD value.
    }

    return target.Type;
  }

  /// <summary>The for-effect twin of <see cref="EmitIncrementOrDecrement"/> -- a bare <c>i++;</c> or
  /// <c>--i;</c> statement, where pre- vs. post- makes no observable difference (both just add or
  /// subtract one from memory) since nothing reads the expression's own result. See
  /// <see cref="EmitAssignForEffect"/>'s own remarks for why skipping the dup is safe here.</summary>
  private void EmitIncrementOrDecrementForEffect(CUnaryExpr expr)
  {
    if (LooksLikeFloatExpr(expr.Operand))
    {
      Error(expr.Location, "'++'/'--' on 'float' is not yet supported by this compiler");
      return;
    }

    bool isIncrement = expr.Op is CUnaryOp.PreIncrement or CUnaryOp.PostIncrement;
    CBinaryOp op = isIncrement ? CBinaryOp.Add : CBinaryOp.Subtract;

    CLvalue target = ResolveLvalue(expr.Operand);
    EmitLoad(target);          // stack: [old]
    EmitCode("pushlit 1");     // stack: [old, 1]
    EmitBinaryOperation(op, target.Type, CType.Int, expr.Location); // stack: [new]
    EmitStore(target);         // stack: []
  }

  // ---------------------------------------------------------------------------------------------
  // Binary operations.
  // ---------------------------------------------------------------------------------------------

  private static CType ComputeCommonType(CType a, CType b) => a.IsUnsigned || b.IsUnsigned ? CType.UnsignedInt : CType.Int;

  /// <summary>Multiplies the value already on top of the stack, in place, by <paramref
  /// name="elementSizeInWords"/> -- added 2026-09-26 alongside multi-word pointer arithmetic/indexing,
  /// replacing this compiler's old blanket assumption (documented on this class itself) that every
  /// pointer/array element is exactly one word, so "base address + index" never needed a scaling
  /// multiplication. Skipped entirely when the factor is 1 (the overwhelmingly common case -- a plain
  /// int/char/pointer element), both as a cheap optimization and so a program that never indexes a wider
  /// element never pulls in a spurious "__mul" import. Reuses "__mul" -- the same hand-written runtime
  /// routine and calling convention "*" itself uses (see <see cref="EmitLibraryBinary"/>'s own remarks):
  /// the value to scale is already on the stack, so only the factor needs pushing before the
  /// call.</summary>
  private void EmitScaleByElementSize(int elementSizeInWords)
  {
    if (elementSizeInWords == 1)
    {
      return;
    }

    EmitCode($"pushlit {elementSizeInWords}");
    _imports.Add("__mul");
    EmitCode("call __mul");
  }

  /// <summary>The inverse of <see cref="EmitScaleByElementSize"/>, for pointer-minus-pointer ("p2 - p1",
  /// which yields a count of ELEMENTS, not words): divides the value already on top of the stack (the raw
  /// word difference "pop; sub" just computed) by <paramref name="elementSizeInWords"/>, via "__divs"
  /// (signed -- the difference can be negative). Skipped when the factor is 1, for the same reasons <see
  /// cref="EmitScaleByElementSize"/> skips it.</summary>
  private void EmitDescaleByElementSize(int elementSizeInWords)
  {
    if (elementSizeInWords == 1)
    {
      return;
    }

    EmitCode($"pushlit {elementSizeInWords}");
    _imports.Add("__divs");
    EmitCode("call __divs");
  }

  private CType EmitBinaryOperation(CBinaryOp op, CType leftType, CType rightType, CSourceLocation location)
  {
    switch (op)
    {
      case CBinaryOp.Add:
        if (leftType.IsPointer && !rightType.IsPointer)
        {
          // "pointer + int": stack is [pointerValue, intValue] -- scale the int (already on top) by the
          // pointee's own size before the ordinary "pop; add", so a wider element (float, struct, or an
          // array/struct member) advances the pointer by the right number of WORDS. See
          // EmitScaleByElementSize's own remarks.
          EmitScaleByElementSize(Math.Max(leftType.ElementType!.SizeInWords, 1));
          EmitCode("pop");
          EmitCode("add");
          return leftType;
        }

        if (rightType.IsPointer && !leftType.IsPointer)
        {
          int elementSize = Math.Max(rightType.ElementType!.SizeInWords, 1);
          if (elementSize != 1)
          {
            // "int + pointer" (the int written first, e.g. "1 + p"): the value needing to be scaled (the
            // int) sits UNDERNEATH the pointer value already on top of the stack, and this compiler's
            // stack machine has no operation to reach or reorder a non-top word -- a deliberate, narrow
            // SCOPE limit (not a hardware hypothesis), not a case worth a stack-reordering primitive for.
            // Write "pointer + int" instead.
            Error(location, $"\"int + {rightType}\" (the pointer written second) is not yet supported by this compiler for an element wider than one word -- write \"{rightType} + int\" instead");
          }

          EmitCode("pop");
          EmitCode("add");
          return rightType;
        }

        EmitCode("pop");
        EmitCode("add");
        return ComputeCommonType(leftType, rightType);

      case CBinaryOp.Subtract:
        if (leftType.IsPointer && rightType.IsPointer)
        {
          // "pointer - pointer" yields a count of ELEMENTS: compute the raw word difference first, then
          // divide by the (common) element size -- see EmitDescaleByElementSize's own remarks.
          EmitCode("pop");
          EmitCode("sub");
          EmitDescaleByElementSize(Math.Max(leftType.ElementType!.SizeInWords, 1));
          return CType.Int;
        }

        if (leftType.IsPointer)
        {
          // "pointer - int": same reasoning as "pointer + int" above.
          EmitScaleByElementSize(Math.Max(leftType.ElementType!.SizeInWords, 1));
          EmitCode("pop");
          EmitCode("sub");
          return leftType;
        }

        EmitCode("pop");
        EmitCode("sub");
        return ComputeCommonType(leftType, rightType);

      case CBinaryOp.Multiply:
        return EmitLibraryBinary(leftType, rightType, "__mul", location);

      case CBinaryOp.Divide:
        return EmitLibraryBinary(leftType, rightType, ComputeCommonType(leftType, rightType).IsUnsigned ? "__divu" : "__divs", location);

      case CBinaryOp.Modulo:
        return EmitLibraryBinary(leftType, rightType, ComputeCommonType(leftType, rightType).IsUnsigned ? "__modu" : "__mods", location);

      case CBinaryOp.BitwiseAnd:
        EmitCode("pop");
        EmitCode("and");
        return ComputeCommonType(leftType, rightType);

      case CBinaryOp.BitwiseOr:
        EmitCode("pop");
        EmitCode("or");
        return ComputeCommonType(leftType, rightType);

      case CBinaryOp.BitwiseXor:
        EmitCode("pop");
        EmitCode("xor");
        return ComputeCommonType(leftType, rightType);

      case CBinaryOp.ShiftLeft:
        EmitCode("pop");
        EmitCode("usl");
        return leftType;

      case CBinaryOp.ShiftRight:
        EmitCode("pop");
        EmitCode(leftType.IsUnsigned ? "usr" : "ssr");
        return leftType;

      case CBinaryOp.Equal:
        EmitCode("pop");
        EmitCode("eq");
        return CType.Int;

      case CBinaryOp.NotEqual:
        EmitCode("pop");
        EmitCode("ne");
        return CType.Int;

      case CBinaryOp.Less:
        EmitCode("pop");
        EmitCode(ComputeCommonType(leftType, rightType).IsUnsigned ? "ult" : "lt");
        return CType.Int;

      case CBinaryOp.Greater:
        EmitCode("pop");
        EmitCode(ComputeCommonType(leftType, rightType).IsUnsigned ? "ugt" : "gt");
        return CType.Int;

      case CBinaryOp.LessOrEqual:
        EmitCode("pop");
        EmitCode(ComputeCommonType(leftType, rightType).IsUnsigned ? "ule" : "le");
        return CType.Int;

      case CBinaryOp.GreaterOrEqual:
        EmitCode("pop");
        EmitCode(ComputeCommonType(leftType, rightType).IsUnsigned ? "uge" : "ge");
        return CType.Int;

      default:
        Error(location, $"operator \"{op}\" is not yet supported by the code generator");
        EmitCode("pop");
        return CType.Int;
    }
  }

  /// <summary>Emits a call to one of the hand-written CVM runtime library routines that back
  /// <c>*</c>/<c>/</c>/<c>%</c> (no hardware primitive exists for any of them -- see this class's own
  /// doc comment). By the time this runs, both operands are already on the stack (pushed left-to-right
  /// by <see cref="EmitExpr"/>'s <see cref="CBinaryExpr"/> case or by
  /// <see cref="EmitCompoundAssign"/>); the callee consumes both and pushes exactly one result
  /// word.</summary>
  private CType EmitLibraryBinary(CType leftType, CType rightType, string functionName, CSourceLocation location)
  {
    if (leftType.IsPointer || rightType.IsPointer)
    {
      Error(location, $"\"{functionName}\" is not defined for a pointer operand");
    }

    _imports.Add(functionName);
    EmitCode($"call {functionName}");
    return ComputeCommonType(leftType, rightType);
  }

  /// <summary>Short-circuit "&amp;&amp;"/"||", both branching on <c>cbr</c>'s CONFIRMED
  /// branches-if-r-equals-0 polarity documented on this class. "a &amp;&amp; b": evaluate a; if a is
  /// false, short-circuit straight to a result of 0 without ever evaluating b; otherwise evaluate b and
  /// coerce it to exactly 0 or 1 (matching C's own &amp;&amp;/|| result type). "a || b" mirrors this: if
  /// a is true, short-circuit straight to 1; otherwise evaluate and coerce b the same way. 2026-09-09:
  /// since <c>cbr</c> itself already tests "r == 0", "&amp;&amp;" (short-circuits exactly on that same
  /// condition, left == 0) needs no extra op before it; "||" (short-circuits on the OPPOSITE condition,
  /// left != 0) needs an explicit <c>eq0</c> first to invert the sense so <c>cbr</c> fires at the right
  /// moment -- see this class's own ABI doc comment.</summary>
  private CType EmitLogical(CBinaryExpr expr)
  {
    bool isAnd = expr.Op == CBinaryOp.LogicalAnd;
    string shortCircuitLabel = NewLabel(isAnd ? "and_false" : "or_true");
    string doneLabel = NewLabel(isAnd ? "and_done" : "or_done");

    EmitExpr(expr.Left);
    EmitCode("pop");
    if (!isAnd)
    {
      EmitCode("eq0"); // || short-circuits when left is true (!=0); invert first so cbr's own "r==0" test fires on that.
    }

    EmitCode($"cbr {shortCircuitLabel}"); // && needs no inversion: it short-circuits exactly when left == 0, which is what cbr already tests.

    EmitExpr(expr.Right);
    EmitCode("pop");
    EmitCode("ne0");
    EmitCode("push");
    EmitCode($"br {doneLabel}");

    EmitLabel(shortCircuitLabel);
    EmitCode($"pushlit {(isAnd ? 0 : 1)}");

    EmitLabel(doneLabel);
    return CType.Int;
  }

  // ---------------------------------------------------------------------------------------------
  // Statements.
  // ---------------------------------------------------------------------------------------------

  private void EmitStatement(CStmt stmt)
  {
    // 2026-09-26: tag every "real" statement with its own source-comment label before emitting its
    // code -- see EmitSourceLineComment's own remarks. Deliberately excluded: CCompoundStmt itself
    // (its Location is just the opening brace; its own inner statements get tagged individually as
    // this method recurses into them) and CEmptyStmt (a bare ";", nothing to label).
    if (stmt is not (CCompoundStmt or CEmptyStmt))
    {
      EmitSourceLineComment(stmt.Location);
    }

    switch (stmt)
    {
      case CCompoundStmt compound:
        EnterScope();
        foreach (CStmt inner in compound.Statements)
        {
          EmitStatement(inner);
        }

        ExitScope();
        break;

      case CEmptyStmt:
        break;

      case CExprStmt exprStmt:
        // 2026-09-09: a bare assignment/compound-assignment/increment-decrement statement is the
        // single most common expression statement there is, and its own result value is NEVER read
        // (there is nowhere for a statement's "value" to go) -- so these three go through their
        // "ForEffect" twins, which skip the dup this class's own uniform stack invariant would
        // otherwise require, rather than the generic EmitExpr-then-pop path below. See
        // EmitAssignForEffect's own remarks.
        switch (exprStmt.Expression)
        {
          case CAssignExpr assign:
            EmitAssignForEffect(assign);
            break;

          case CCompoundAssignExpr compoundAssign:
            EmitCompoundAssignForEffect(compoundAssign);
            break;

          case CUnaryExpr { Op: CUnaryOp.PreIncrement or CUnaryOp.PostIncrement or CUnaryOp.PreDecrement or CUnaryOp.PostDecrement } incDec:
            EmitIncrementOrDecrementForEffect(incDec);
            break;

          default:
            {
              CType type = EmitExpr(exprStmt.Expression);
              // Float ABI: a 'float' result never left anything on the ordinary stack -- nothing to
              // discard (e.g. a bare "someFloatFunction();" statement). See this class's own doc comment.
              if (!type.IsVoid && !type.IsFloat)
              {
                EmitCode("pop");
              }

              break;
            }
        }

        break;

      case CLocalVarDecl localDecl:
        DeclareLocal(localDecl);
        if (localDecl.Initializer is not null && !localDecl.IsStatic)
        {
          // Same "ForEffect" reasoning as CExprStmt above: a declaration's initializer value is never
          // read either, so this goes straight to EmitAssignForEffect rather than round-tripping
          // through a dup it would just have to discard again.
          EmitAssignForEffect(new CAssignExpr(localDecl.Location, new CNameExpr(localDecl.Location, localDecl.Name), localDecl.Initializer));
        }

        break;

      case CIfStmt ifStmt:
        {
          string elseLabel = NewLabel("else");
          string endLabel = NewLabel("endif");
          EmitExpr(ifStmt.Condition);
          EmitCode("pop");
          // 2026-09-09: cbr branches when r == 0 (CONFIRMED, see this class's own ABI doc comment) --
          // exactly the "condition is false, skip to else" test we need, so no eq0 is needed first any
          // more (the old "ifbr" placeholder branched on nonzero, which is why this used to negate with
          // eq0 first).
          EmitCode($"cbr {elseLabel}");
          EmitStatement(ifStmt.Then);
          EmitCode($"br {endLabel}");
          EmitLabel(elseLabel);
          if (ifStmt.Else is not null)
          {
            EmitStatement(ifStmt.Else);
          }

          EmitLabel(endLabel);
          break;
        }

      case CWhileStmt whileStmt:
        {
          string startLabel = NewLabel("while");
          string endLabel = NewLabel("endwhile");
          _currentFunction!.BreakLabels.Push(endLabel);
          _currentFunction.ContinueLabels.Push(startLabel);
          EmitLabel(startLabel);
          EmitExpr(whileStmt.Condition);
          EmitCode("pop");
          EmitCode($"cbr {endLabel}"); // 2026-09-09: cbr already branches on r == 0 -- see EmitStatement's CIfStmt case remarks.
          EmitStatement(whileStmt.Body);
          EmitCode($"br {startLabel}");
          EmitLabel(endLabel);
          _currentFunction.BreakLabels.Pop();
          _currentFunction.ContinueLabels.Pop();
          break;
        }

      case CDoWhileStmt doWhileStmt:
        {
          string startLabel = NewLabel("dowhile");
          string continueLabel = NewLabel("dowhile_cont");
          string endLabel = NewLabel("enddowhile");
          _currentFunction!.BreakLabels.Push(endLabel);
          _currentFunction.ContinueLabels.Push(continueLabel);
          EmitLabel(startLabel);
          EmitStatement(doWhileStmt.Body);
          EmitLabel(continueLabel);
          EmitExpr(doWhileStmt.Condition);
          EmitCode("pop");
          // 2026-09-09: do-while needs to loop back when the condition is TRUE, the opposite of what
          // cbr tests directly (r == 0) -- so, unlike if/while/for, this needs an explicit eq0 first to
          // invert the sense before branching (see this class's own ABI doc comment). The old "ifbr"
          // placeholder branched on nonzero, so it needed no such inversion here; cbr does.
          EmitCode("eq0");
          EmitCode($"cbr {startLabel}");
          EmitLabel(endLabel);
          _currentFunction.BreakLabels.Pop();
          _currentFunction.ContinueLabels.Pop();
          break;
        }

      case CForStmt forStmt:
        {
          string startLabel = NewLabel("for");
          string continueLabel = NewLabel("for_cont");
          string endLabel = NewLabel("endfor");

          EnterScope();
          if (forStmt.Init is not null)
          {
            EmitStatement(forStmt.Init);
          }

          _currentFunction!.BreakLabels.Push(endLabel);
          _currentFunction.ContinueLabels.Push(continueLabel);
          EmitLabel(startLabel);
          if (forStmt.Condition is not null)
          {
            EmitExpr(forStmt.Condition);
            EmitCode("pop");
            EmitCode($"cbr {endLabel}"); // 2026-09-09: cbr already branches on r == 0 -- see EmitStatement's CIfStmt case remarks.
          }

          EmitStatement(forStmt.Body);
          EmitLabel(continueLabel);
          if (forStmt.Update is not null)
          {
            CType updateType = EmitExpr(forStmt.Update);
            if (!updateType.IsVoid && !updateType.IsFloat)
            {
              EmitCode("pop");
            }
          }

          EmitCode($"br {startLabel}");
          EmitLabel(endLabel);
          _currentFunction.BreakLabels.Pop();
          _currentFunction.ContinueLabels.Pop();
          ExitScope();
          break;
        }

      case CReturnStmt returnStmt:
        {
          if (returnStmt.Value is not null)
          {
            if (_currentFunction!.ReturnType.IsVoid)
            {
              Error(returnStmt.Location, $"function \"{_currentFunction.Name}\" returns void and cannot return a value");
            }

            if (_currentFunction.ReturnType.IsFloat)
            {
              // Float ABI HYPOTHESIS (see this class's own doc comment): the return value always goes in
              // fr[0], regardless of calling convention.
              EmitFloatExprInto(returnStmt.Value, 0);
            }
            else
            {
              CType valueType = EmitExpr(returnStmt.Value);
              if (valueType.IsFloat)
              {
                Error(returnStmt.Location, $"function \"{_currentFunction.Name}\" does not return 'float'");
              }
            }
          }
          else if (!_currentFunction!.ReturnType.IsVoid)
          {
            Error(returnStmt.Location, $"function \"{_currentFunction.Name}\" must return a value of type \"{_currentFunction.ReturnType}\"");
          }

          EmitCode($"br {_currentFunction!.EpilogueLabel}");
          break;
        }

      case CBreakStmt breakStmt:
        if (_currentFunction!.BreakLabels.Count == 0)
        {
          Error(breakStmt.Location, "\"break\" is not within a loop or switch");
          break;
        }

        EmitCode($"br {_currentFunction.BreakLabels.Peek()}");
        break;

      case CContinueStmt continueStmt:
        if (_currentFunction!.ContinueLabels.Count == 0)
        {
          Error(continueStmt.Location, "\"continue\" is not within a loop");
          break;
        }

        EmitCode($"br {_currentFunction.ContinueLabels.Peek()}");
        break;

      case CGotoStmt gotoStmt:
        EmitCode($"br {MangleLabel(gotoStmt.Label)}");
        break;

      case CLabelStmt labelStmt:
        EmitLabel(MangleLabel(labelStmt.Label));
        EmitStatement(labelStmt.Inner);
        break;

      case CCaseLabelStmt caseLabelStmt:
        EmitSwitchLabel(caseLabelStmt, caseLabelStmt.Inner);
        break;

      case CDefaultLabelStmt defaultLabelStmt:
        EmitSwitchLabel(defaultLabelStmt, defaultLabelStmt.Inner);
        break;

      case CSwitchStmt switchStmt:
        EmitSwitch(switchStmt);
        break;

      default:
        Error(stmt.Location, $"statement kind \"{stmt.GetType().Name}\" is not yet supported by the code generator");
        break;
    }
  }

  private void EmitSwitchLabel(CStmt labelNode, CStmt inner)
  {
    if (_switchLabelsByNode is null || !_switchLabelsByNode.TryGetValue(labelNode, out string? label))
    {
      Error(labelNode.Location, "\"case\"/\"default\" is not within a switch");
      EmitStatement(inner);
      return;
    }

    EmitLabel(label);
    EmitStatement(inner);
  }

  private string MangleLabel(string name) => $"{_currentFunction!.Name}__label_{name}";

  /// <summary>
  /// Compiles a <c>switch</c> without a real <c>tjmp</c> instruction -- see this class's own doc comment
  /// for why. The selector is evaluated exactly once into a compiler-allocated temporary local, then a
  /// chain of equality comparisons (in source order, "default" last regardless of where it was written,
  /// matching the order real hardware would want to test them in) jumps into the right position; the
  /// body then runs as ordinary statements, with each <see cref="CCaseLabelStmt"/>/
  /// <see cref="CDefaultLabelStmt"/> becoming a label the comparison chain already jumps to -- giving
  /// real C fall-through for free, since nothing here stops execution from running from one case
  /// straight into the next.
  /// </summary>
  private void EmitSwitch(CSwitchStmt switchStmt)
  {
    string endLabel = NewLabel("endswitch");
    var labels = new Dictionary<CStmt, string>(ReferenceEqualityComparer.Instance);
    var cases = new List<(long Value, string Label)>();
    string? defaultLabel = null;
    CollectSwitchLabels(switchStmt.Body, labels, cases, ref defaultLabel, switchStmt.Location);

    // Evaluate the selector exactly once and store it. 2026-09-09: "stl" stores r, not the stack top
    // (see this class's own ABI doc comment), so the selector has to be popped into r before it can be
    // stashed away, and reloaded (ldl; push) at the top of every comparison since "eq"'s own pop
    // clobbers r on each iteration.
    EmitExpr(switchStmt.Selector);        // stack: [selector]
    EmitCode("pop");                       // r := selector; stack: []
    int selectorSlot = _currentFunction!.NextLocalSlot++;
    EmitCode($"stl {selectorSlot}");      // selectorSlot := r (the selector)

    foreach ((long value, string label) in cases)
    {
      EmitCode($"ldl {selectorSlot}");   // r := selector
      EmitCode("push");                   // stack: [selector]
      EmitCode($"pushlit {value}");      // stack: [selector, value]
      EmitCode("pop");                    // r := value; stack: [selector]
      EmitCode("eq");                     // stack: [selector == value]
      EmitCode("pop");                    // r := comparison result; stack: []
      // 2026-09-09: need to branch into this case when the comparison is TRUE, the opposite of what cbr
      // tests directly (r == 0) -- so, like do-while, this needs an explicit eq0 first to invert the
      // sense before branching (see this class's own ABI doc comment).
      EmitCode("eq0");
      EmitCode($"cbr {label}");
    }

    EmitCode($"br {defaultLabel ?? endLabel}");

    // Save/restore rather than clobbering to null: a nested switch inside this one's body recursively
    // calls EmitSwitch, which would otherwise permanently wipe out THIS switch's own label table for
    // any case/default label that textually follows the nested switch.
    Dictionary<CStmt, string>? outerSwitchLabels = _switchLabelsByNode;
    _switchLabelsByNode = labels;
    _currentFunction.BreakLabels.Push(endLabel);
    EmitStatement(switchStmt.Body);
    _currentFunction.BreakLabels.Pop();
    _switchLabelsByNode = outerSwitchLabels;

    EmitLabel(endLabel);
  }

  /// <summary>Walks a switch's body collecting (constant value, label) pairs for every
  /// <see cref="CCaseLabelStmt"/> and the label for a single <see cref="CDefaultLabelStmt"/>, WITHOUT
  /// descending into a nested switch's own body (those cases belong to the inner switch, not this
  /// one).</summary>
  private void CollectSwitchLabels(CStmt node, Dictionary<CStmt, string> labels, List<(long Value, string Label)> cases, ref string? defaultLabel, CSourceLocation switchLocation)
  {
    switch (node)
    {
      case CCaseLabelStmt caseLabel:
        {
          if (!TryEvaluateConstantLong(caseLabel.Value, out long value))
          {
            Error(caseLabel.Location, "a \"case\" label must be a compile-time integer constant");
            value = 0;
          }

          if (cases.Any(c => c.Value == value))
          {
            Error(caseLabel.Location, $"duplicate \"case {value}\" label");
          }

          string label = NewLabel($"case_{value}");
          labels[caseLabel] = label;
          cases.Add((value, label));
          CollectSwitchLabels(caseLabel.Inner, labels, cases, ref defaultLabel, switchLocation);
          break;
        }

      case CDefaultLabelStmt defaultLabelStmt:
        {
          if (defaultLabel is not null)
          {
            Error(defaultLabelStmt.Location, "a \"switch\" may have only one \"default\" label");
          }

          string label = NewLabel("default");
          labels[defaultLabelStmt] = label;
          defaultLabel = label;
          CollectSwitchLabels(defaultLabelStmt.Inner, labels, cases, ref defaultLabel, switchLocation);
          break;
        }

      case CCompoundStmt compound:
        foreach (CStmt inner in compound.Statements)
        {
          CollectSwitchLabels(inner, labels, cases, ref defaultLabel, switchLocation);
        }

        break;

      case CIfStmt ifStmt:
        CollectSwitchLabels(ifStmt.Then, labels, cases, ref defaultLabel, switchLocation);
        if (ifStmt.Else is not null)
        {
          CollectSwitchLabels(ifStmt.Else, labels, cases, ref defaultLabel, switchLocation);
        }

        break;

      case CLabelStmt labelStmt:
        CollectSwitchLabels(labelStmt.Inner, labels, cases, ref defaultLabel, switchLocation);
        break;

      // A case/default label may be nested inside a loop that's itself inside the switch's body (the
      // Duff's-device shape, among others) -- descend into every loop kind's own Body so those are
      // still collected, exactly as CSwitchStmt's own doc comment (CAstStmt.cs) promises.
      case CWhileStmt whileStmt:
        CollectSwitchLabels(whileStmt.Body, labels, cases, ref defaultLabel, switchLocation);
        break;

      case CDoWhileStmt doWhileStmt:
        CollectSwitchLabels(doWhileStmt.Body, labels, cases, ref defaultLabel, switchLocation);
        break;

      case CForStmt forStmt:
        CollectSwitchLabels(forStmt.Body, labels, cases, ref defaultLabel, switchLocation);
        break;

      case CSwitchStmt:
        // A nested switch owns its own case/default labels -- do not descend into it.
        break;

      default:
        break;
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Expressions. Every case leaves exactly one stack word (this class's own uniform codegen
  // invariant), except a call to a void function, which leaves nothing.
  // ---------------------------------------------------------------------------------------------

  private CType EmitExpr(CExpr expr)
  {
    switch (expr)
    {
      case CIntLiteralExpr literal:
        EmitCode($"pushlit {literal.Value}");
        return literal.IsUnsigned ? CType.UnsignedInt : CType.Int;

      case CFloatLiteralExpr floatLiteral:
        // Float ABI: reached via the general (int/pointer) path -- e.g. a bare "3.5;" statement, or a
        // float literal used where an int was expected. Evaluate it at fr[0] anyway (so downstream code
        // is well-formed) and report its real type upward -- this class's own uniform "leave one stack
        // word" invariant does not hold for it, so nothing further should try to treat it as an int.
        EmitFloatExprInto(floatLiteral, 0);
        return CType.Float;

      case CStringLiteralExpr stringLiteral:
        EmitCode($"pushlit {InternStringLiteral(stringLiteral.Value)}");
        return CType.PointerTo(CType.Char);

      case CNameExpr name:
        return EmitName(name);

      case CUnaryExpr { Op: CUnaryOp.Plus } unary:
        return EmitExpr(unary.Operand);

      case CUnaryExpr { Op: CUnaryOp.Minus } unary:
        {
          // RENAMED 2026-09-09: node 508's own "negate" no longer resolved against any live CVM2 node,
          // and was deleted outright from CvmInstructionSet the same day once this codegen stopped
          // emitting it (see that class's own remarks on the 2026-09-09 CVM1-opcode purge) -- node 509's
          // "neg" (a genuinely different mnemonic string, live and node-resolved) computes the identical
          // two's-complement result ("inv" then "+1", see Node509Program's own remarks) and is used here
          // instead.
          CType type = EmitExpr(unary.Operand);
          EmitCode("pop");
          EmitCode("neg");
          EmitCode("push");
          return type;
        }

      case CUnaryExpr { Op: CUnaryOp.LogicalNot } unary:
        {
          EmitExpr(unary.Operand);
          EmitCode("pop");
          EmitCode("eq0");
          EmitCode("push");
          return CType.Int;
        }

      case CUnaryExpr { Op: CUnaryOp.BitwiseNot } unary:
        {
          CType type = EmitExpr(unary.Operand);
          EmitCode("pop");
          EmitCode("inv");
          EmitCode("push");
          return type;
        }

      case CUnaryExpr { Op: CUnaryOp.AddressOf } unary:
        return EmitAddressOf(unary.Operand);

      case CUnaryExpr { Op: CUnaryOp.Dereference } unary:
        {
          // Added 2026-09-26 alongside multi-word pointer arithmetic/indexing: a dereferenced 'float *'
          // reached via the general path (e.g. a bare "*fp;" statement) -- this compiler's uniform "leave
          // exactly one word" invariant does not hold for a 2-word 'float', so it must be routed to the
          // float-specific evaluator instead of ResolveLvalue/EmitLoad's ordinary single-word path. Mirrors
          // CFloatLiteralExpr's own handling at the top of this switch.
          if (LooksLikeFloatExpr(unary))
          {
            EmitFloatExprInto(unary, 0);
            return CType.Float;
          }

          CLvalue lvalue = ResolveLvalue(unary);
          EmitLoad(lvalue);
          return lvalue.Type;
        }

      case CUnaryExpr { Op: CUnaryOp.PreIncrement or CUnaryOp.PreDecrement or CUnaryOp.PostIncrement or CUnaryOp.PostDecrement } incDec:
        return EmitIncrementOrDecrement(incDec);

      case CBinaryExpr { Op: CBinaryOp.LogicalAnd or CBinaryOp.LogicalOr } logical:
        return EmitLogical(logical);

      case CBinaryExpr binary:
        {
          CType leftType = EmitExpr(binary.Left);
          CType rightType = EmitExpr(binary.Right);
          return EmitBinaryOperation(binary.Op, leftType, rightType, binary.Location);
        }

      case CAssignExpr assign:
        return EmitAssign(assign);

      case CCompoundAssignExpr compoundAssign:
        return EmitCompoundAssign(compoundAssign);

      case CConditionalExpr conditional:
        return EmitConditional(conditional);

      case CCallExpr call:
        return EmitCall(call);

      case CIndexExpr index:
        {
          // Added 2026-09-26 alongside multi-word pointer arithmetic/indexing: a 'float' array/pointer
          // element reached via the general path -- see the CUnaryExpr{Dereference} case just above for
          // why this needs the float-specific evaluator instead.
          if (LooksLikeFloatExpr(index))
          {
            EmitFloatExprInto(index, 0);
            return CType.Float;
          }

          CLvalue lvalue = ResolveLvalue(index);
          if (lvalue.Type.IsStruct)
          {
            // Added 2026-09-26 alongside array-of-struct support -- mirrors CMemberAccessExpr's own
            // guard just below: EmitLoad would read only the element's FIRST word (this compiler's
            // uniform "one word per value" invariant does not hold for a multi-word struct), so a
            // placeholder is emitted instead of a genuinely wrong partial read.
            Error(index.Location, $"indexing here yields a struct value of type \"{lvalue.Type}\" -- reading or assigning a whole struct value is not yet supported by this compiler (access one of its own members instead)");
            EmitCode("pushlit 0");
            return CType.Int;
          }

          EmitLoad(lvalue);
          return lvalue.Type;
        }

      case CMemberAccessExpr member:
        {
          // Added 2026-09-26 alongside multi-word pointer arithmetic/indexing: a 'float' struct member
          // reached via the general path -- see the CUnaryExpr{Dereference} case above for why.
          if (LooksLikeFloatExpr(member))
          {
            EmitFloatExprInto(member, 0);
            return CType.Float;
          }

          CLvalue lvalue = ResolveLvalue(member);
          if (lvalue.Type.IsStruct)
          {
            // ResolveLvalue's own CMemberAccessExpr case already reported this -- EmitLoad would read
            // only the member's FIRST word (this compiler's uniform "one word per value" invariant does
            // not hold for a multi-word struct), so a placeholder is emitted instead of a genuinely
            // wrong partial read, matching this class's own error-branch convention elsewhere (e.g. the
            // "expression kind not supported" default case just below).
            EmitCode("pushlit 0");
            return CType.Int;
          }

          EmitLoad(lvalue);
          return lvalue.Type;
        }

      case CCastExpr cast:
        {
          // Float ABI: 'float' breaks the "every supported type is one word" premise the plain
          // reinterpreting cast below relies on, and this compiler does not implement the actual
          // IEEE-754 conversion an int<->float cast would need (out of scope for this first pass -- see
          // this class's own doc comment) -- so a cast involving 'float' is either a no-op (float-to-
          // float, reached here e.g. as a bare statement) or a clear diagnostic, never a silent
          // reinterpretation of an int's bits as a float or vice versa.
          if (cast.TargetType.IsFloat)
          {
            if (!LooksLikeFloatExpr(cast.Operand))
            {
              Error(cast.Location, "casting a non-'float' value to 'float' is not yet supported by this compiler");
              EmitCode("pushlit 0");
              return CType.Float;
            }

            EmitFloatExprInto(cast.Operand, 0);
            return CType.Float;
          }

          if (LooksLikeFloatExpr(cast.Operand))
          {
            Error(cast.Location, "casting a 'float' value to a non-'float' type is not yet supported by this compiler");
            EmitCode("pushlit 0");
            return cast.TargetType;
          }

          // Every other supported type is one word, so a cast is a pure reinterpretation: emit the
          // operand and relabel its type, no runtime conversion needed.
          EmitExpr(cast.Operand);
          return cast.TargetType;
        }

      case CSizeOfTypeExpr sizeOfType:
        EmitCode($"pushlit {sizeOfType.Type.SizeInWords}");
        return CType.UnsignedInt;

      case CSizeOfExprExpr sizeOfExpr:
        {
          // Deviation from strict C semantics, documented in the design doc: the operand is evaluated at
          // runtime and discarded rather than being purely a compile-time type inspection. Safe as long
          // as the operand has no side effects.
          CType operandType = EmitExpr(sizeOfExpr.Operand);
          if (operandType.IsVoid)
          {
            // Already-invalid C ("sizeof" of a void-returning call) -- report it instead of silently
            // popping whatever value happens to be sitting under a stack slot that was never pushed.
            Error(sizeOfExpr.Location, "\"sizeof\" cannot be applied to a value of type \"void\"");
          }
          else if (!operandType.IsFloat)
          {
            // Float ABI: a 'float' operand never left anything on the ordinary stack (see this class's
            // own doc comment) -- nothing to discard here.
            EmitCode("pop");
          }

          EmitCode($"pushlit {Math.Max(operandType.SizeInWords, 1)}");
          return CType.UnsignedInt;
        }

      case CCommaExpr comma:
        {
          CType leftType = EmitExpr(comma.Left);
          if (!leftType.IsVoid && !leftType.IsFloat)
          {
            EmitCode("pop");
          }

          return EmitExpr(comma.Right);
        }

      default:
        Error(expr.Location, $"expression kind \"{expr.GetType().Name}\" is not yet supported by the code generator");
        EmitCode("pushlit 0");
        return CType.Int;
    }
  }

  private CType EmitName(CNameExpr name)
  {
    CVarSymbol? symbol = LookupVariable(name.Name);

    if (IsFloatNamedVariable(name))
    {
      // Float ABI: a 'float' name reached the general (int/pointer) expression path -- this compiler's
      // own uniform "leave exactly one word" stack invariant does not hold for a 2-word 'float', so this
      // is always a genuine usage error (comparisons, bitwise/shift ops, an unconverted mix with an
      // int-context expression) rather than something to silently mis-emit. See this class's own doc
      // comment.
      Error(name.Location, $"\"{name.Name}\" is a 'float' and can only be used in a floating-point expression");
      EmitCode("pushlit 0");
      return CType.Int;
    }

    if (symbol is { Kind: CVarKind.Local })
    {
      if (symbol.Type.IsArray)
      {
        EmitLocalOrParameterAddress(symbol.Index, isParameter: false);
        return symbol.Type.Decay();
      }

      EmitCode($"ldl {symbol.Index}");
      EmitCode("push");
      return symbol.Type;
    }

    if (symbol is { Kind: CVarKind.Parameter })
    {
      if (symbol.Type.IsArray)
      {
        EmitLocalOrParameterAddress(symbol.Index, isParameter: true);
        return symbol.Type.Decay();
      }

      EmitCode($"ldp {symbol.Index}");
      EmitCode("push");
      return symbol.Type;
    }

    string? staticLabel = symbol is { Kind: CVarKind.Global } ? symbol.GlobalLabel : null;
    if (staticLabel is not null)
    {
      if (symbol!.Type.IsArray)
      {
        EmitCode($"pushlit {staticLabel}");
        return symbol.Type.Decay();
      }

      EmitGlobalFetch(staticLabel);   // r := the global's value -- no address computation at all
      EmitCode("push");
      return symbol.Type;
    }

    if (_globals.TryGetValue(name.Name, out CGlobalSymbol? global))
    {
      string globalLabel = MangleExternalSymbol(name.Name);
      if (global.Type.IsArray)
      {
        EmitCode($"pushlit {globalLabel}");
        return global.Type.Decay();
      }

      EmitGlobalFetch(globalLabel);   // r := the global's value -- no address computation at all
      EmitCode("push");
      return global.Type;
    }

    if (_functions.ContainsKey(name.Name))
    {
      Error(name.Location, $"\"{name.Name}\" is a function -- did you mean to call it?");
      EmitCode("pushlit 0");
      return CType.Int;
    }

    // Not defined in this file -- assume an external global the linker will resolve, per this
    // compiler's "missing pieces become an .import" rule.
    string importedLabel = MangleExternalSymbol(name.Name);
    _imports.Add(importedLabel);
    EmitGlobalFetch(importedLabel);   // r := the global's value -- no address computation at all
    EmitCode("push");
    return CType.Int;
  }

  private CType EmitConditional(CConditionalExpr conditional)
  {
    string elseLabel = NewLabel("condfalse");
    string endLabel = NewLabel("condend");

    EmitExpr(conditional.Condition);
    EmitCode("pop");
    EmitCode($"cbr {elseLabel}"); // 2026-09-09: cbr already branches on r == 0 -- see EmitStatement's CIfStmt case remarks.
    CType trueType = EmitExpr(conditional.WhenTrue);
    EmitCode($"br {endLabel}");
    EmitLabel(elseLabel);
    CType falseType = EmitExpr(conditional.WhenFalse);
    EmitLabel(endLabel);

    return trueType.IsPointer ? trueType : falseType.IsPointer ? falseType : ComputeCommonType(trueType, falseType);
  }

  /// <summary>
  /// Emits a call. <paramref name="floatResultDestRegister"/> is Float ABI plumbing (added 2026-09-26,
  /// see this class's own doc comment): when the callee returns <c>float</c> and this is non-null, the
  /// return value (always in <c>fr[0]</c> -- see this class's own return-value HYPOTHESIS) is moved into
  /// that register before any save/restore pops run. Left null for a call reached from the general
  /// (int/pointer) expression path, which has no register of its own to receive a <c>float</c> result
  /// into -- see <see cref="EmitFloatExprInto"/>'s own <c>CCallExpr</c> case for the only call site that
  /// passes a real value.
  ///
  /// Float ABI: EVERY call, regardless of the callee's own return type, may need to protect currently-live
  /// fr registers (<see cref="_liveFloatRegisterCount"/>) from being clobbered by the callee -- see this
  /// class's own doc comment's "Caller-save/restore" bullet. That wrap covers the ENTIRE call, including
  /// argument marshalling (not just the bare "call" instruction), because marshalling a <c>float</c>
  /// argument itself writes into fr[0..], which could clobber an enclosing evaluation's still-live
  /// registers before the callee ever runs.
  /// </summary>
  private CType EmitCall(CCallExpr call, int? floatResultDestRegister = null)
  {
    string mangledName = MangleExternalSymbol(call.FunctionName);
    bool calleeMayUseFr = CalleeUsesFloatRegisters(call.FunctionName);
    int savedLiveFloatCount = _liveFloatRegisterCount;
    bool needsFloatSaveRestore = calleeMayUseFr && savedLiveFloatCount > 0;

    if (needsFloatSaveRestore)
    {
      EmitFloatRegisterSaveRange(0, savedLiveFloatCount);
    }

    if (!_functions.TryGetValue(call.FunctionName, out CFunctionSignature? signature))
    {
      Error(call.Location, $"\"{call.FunctionName}\" is not declared");
      _imports.Add(mangledName);
      foreach (CExpr argument in call.Arguments)
      {
        // Arguments stay on the stack for the callee, exactly like a normal call -- do not pop them.
        // (An undeclared function's parameter types are unknown, so a 'float' argument here would be
        // mishandled by the plain EmitExpr path below -- already an error case; not worth special-casing
        // further.)
        EmitExpr(argument);
      }

      EmitCode($"call {mangledName}");
      if (needsFloatSaveRestore)
      {
        EmitFloatRegisterRestoreRange(0, savedLiveFloatCount);
      }

      return CType.Int;
    }

    if (signature.ParameterTypes.Count != call.Arguments.Count)
    {
      Error(call.Location, $"\"{call.FunctionName}\" expects {signature.ParameterTypes.Count} argument(s), but {call.Arguments.Count} were given");
    }

    // ABI v2: an argument landing in one of node 306's address registers is evaluated exactly like any
    // other argument (so side effects and evaluation order never change), but its result is then popped
    // back off the stack and loaded into the assigned address register instead of being left there for
    // the callee to find with ldp/stp. The register is loaded with "arld", which per node 306's own
    // opcode table takes the address word from r and the page word from the CVM stack top -- so the
    // sequence is: pop the just-pushed address into r, push a literal page word of 0 (far/32-bit
    // pointers spanning pages are not yet supported -- see this file's own ABI v2 doc-comment remarks),
    // then "arld <register>" consumes both. This mirrors the callee-side prologue spill in EmitFunction,
    // which immediately re-spills the same register back onto the stack/into a local slot -- the
    // register itself is never assumed to survive anything but this one handoff.
    //
    // CORRECTED 2026-09-11: this line used to emit "lda {assignedRegister}" -- flagged the moment the
    // register-operand fix made it assemblable for the first time, since "lda"
    // (LoadAddressRegisterValueMnemonic) is node 306's dereference-LOAD role ("load r FROM the address
    // held in register aa"), not "SET the register from (address = r, page = stack top)" as this comment
    // describes. Stefan confirmed directly: "arld loads an address into an address register. lda loads r
    // using the address of an address register." -- i.e. arld is exactly the SET direction this call
    // site needs. Fixed to "arld <register>".
    //
    // Float ABI (2026-09-26): a 'float' argument is marshalled entirely separately from the plain
    // EmitExpr path (it needs 2 words and node 305's fr registers, not the ordinary 1-word stack) -- see
    // this class's own doc comment's "Parameter passing" bullet. argFloatFloor tracks how many low fr
    // registers are already "spoken for" by an EARLIER fastcall float argument of THIS SAME call (a
    // register-assigned argument's value must stay put in its register until the call runs; a
    // stack-passed one is pushed and its scratch register freed immediately, so it never needs to hold
    // argFloatFloor up).
    int argFloatFloor = 0;
    for (int i = 0; i < call.Arguments.Count; i++)
    {
      CType paramType = i < signature.ParameterTypes.Count ? signature.ParameterTypes[i] : CType.Int;
      if (paramType.IsFloat)
      {
        int? floatRegisterIndex = i < signature.ParameterFloatRegisterSlots.Count ? signature.ParameterFloatRegisterSlots[i] : null;
        if (floatRegisterIndex is int assignedFrRegister)
        {
          EmitFloatExprInto(call.Arguments[i], assignedFrRegister);
          argFloatFloor = Math.Max(argFloatFloor, assignedFrRegister + 1);
        }
        else
        {
          int scratch = argFloatFloor;
          EmitFloatExprInto(call.Arguments[i], scratch);
          EmitCode($"fpush {scratch}"); // stack-passed: push its two words (low, high) for the callee's ldp/stp -- the register is free again immediately.
        }

        continue;
      }

      _liveFloatRegisterCount = argFloatFloor; // protect any float argument already placed above while this (non-float) argument evaluates.
      EmitExpr(call.Arguments[i]);

      int? registerIndex = i < signature.ParameterRegisterSlots.Count ? signature.ParameterRegisterSlots[i] : null;
      if (registerIndex is int assignedRegister)
      {
        EmitCode("pop");
        EmitCode("pushlit 0");
        EmitCode($"arld {assignedRegister}");
      }
    }

    _liveFloatRegisterCount = savedLiveFloatCount;

    if (!signature.IsDefined)
    {
      _imports.Add(mangledName);
    }

    EmitCode($"call {mangledName}");

    CType returnType = signature.ReturnType;
    if (returnType.IsFloat && floatResultDestRegister is int destReg && destReg != 0)
    {
      // Retrieve the return value out of fr[0] BEFORE the restore pops below run -- fr[0] is always among
      // the registers being restored whenever destReg != 0 (0..savedLiveFloatCount-1 always includes 0).
      EmitCode($"fmove {destReg} 0");
    }

    if (needsFloatSaveRestore)
    {
      EmitFloatRegisterRestoreRange(0, savedLiveFloatCount);
    }

    return returnType;
  }
}