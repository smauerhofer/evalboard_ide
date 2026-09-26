# CVM ABI (in progress, started 2026-09-07)

This document is being defined incrementally, at Stefan's own direction ("let's define the ABI and
document it in an .md file"). It's the CVM's own architecture-level ABI -- program entry/exit and
function calling conventions -- one layer above the per-node opcode reference already covered in
`claude/cvm-toolchain-design.md`, and one layer more general than the C-compiler-specific description in
`claude/c-compiler-design.md`. Where a rule below is already implemented (the two calling conventions),
this document names and centralizes it rather than re-deriving it; where a rule is new (program
entry/exit), it's recorded here exactly as stated, ahead of any toolchain support for it -- this is a
living spec, expected to grow over further rounds of dictation, not a final document.

## 1. Program entry and exit

- A CVM program starts execution at address `0:0` and exits at address `0:1`.
- Normally, both addresses hold branch instructions -- i.e. `0:0`/`0:1` are fixed, reserved vector
  slots that branch into/out of the program's real code, rather than being the real entry/exit code
  itself.

As stated, not yet further specified -- flagged here rather than guessed at, for the next round to
settle:
- What the `0` in `0:0`/`0:1` denotes (a node coordinate, a page number, or some other unit in the CVM's
  own layered addressing) is not yet defined anywhere else in this project's documentation or code.
- What actually triggers the jump to `0:1` (a `ret` falling out of `main`? a `br 0:1` the compiler/linker
  emits automatically after `main` returns? something else?), and what default code (if any) lives at
  `0:1` before/absent a real program (an idle loop, a halt, something the host polls) are both open.
- **Not yet reflected in the toolchain**: neither `Ga144.Evb.Ide.Cvm.CvmBootStreamBuilder` (the boot
  stream) nor `Ga144.C.Toolchain.CCodeGenerator` (the C compiler) currently reserves, emits, or
  special-cases these two addresses in any way. This section records the rule ahead of any
  implementation of it.

## 2. Function calling conventions

Two calling conventions exist. Both are already implemented in `Ga144.C.Toolchain.CCodeGenerator` for
the C compiler (see `claude/c-compiler-design.md` for the original stack-only description and
`claude/cvm-node306-307-address-registers.md` for the register convention's own addition); this section
names them at the ABI level, independent of C, so other languages/tools built on the CVM can refer to
them by name without re-deriving the description from the C-compiler-specific write-ups.

### By stack

- The caller evaluates and pushes each argument left-to-right, then `call`s.
- The callee's `enter <n>` reserves `n` local slots; the caller's pushed arguments are addressed via
  `ldp`/`stp`/`lap` (parameter offset 0 = the first declared parameter).
- The callee leaves its return value (if any) on the stack at its epilogue.
- Callee-cleanup ("Pascal-style"): the epilogue's `leave` deallocates all locals AND the caller's pushed
  arguments together, leaving only the single return value (if any) on top, followed by `ret`.

### By register

- Uses the CVM's address-register node's 32-bit address registers (register count is `org`-dependent,
  not a fixed four -- see `claude/cvm-node306-307-address-registers.md`'s own addenda; currently six,
  registers 0-5). **RENUMBERED 2026-09-15**: this node was called "node 306" through 2026-09-11 and is
  physical **node 308** as of Stefan's "node 306 and 308 have swapped roles" -- see that same file's
  final addendum. Only the coordinate moved; the mnemonics and convention below are unchanged.
- Among a function's parameters, in declaration order, the first four whose type is a pointer are passed
  this way instead of on the stack; every other parameter (a 5th-or-later pointer, or any non-pointer
  parameter) keeps the stack convention above, renumbered to skip the register-passed ones.
- Address registers are volatile: per Stefan's own words, "the address register are volatile and are not
  saved on the stack when a function is entered" -- a register's value is never assumed to survive past
  the one transient moment it's used for (the call-site handoff, or the callee's own prologue spill).
- Call site: the argument's value is popped into `r`, paired with a literal page word of `0`, then loaded
  into its assigned register with `arld <n>` (**CORRECTED 2026-09-11**: originally written here as
  `lda <n>`, which is actually the register's own dereference-READ op, not the "set this register" op the
  call site needs -- see the address-register file's own addendum for Stefan's direct clarification:
  "arld loads an address into an address register. lda loads r using the address of an address
  register.").
- Callee prologue: each register-eligible parameter is immediately spilled out of its register
  (`arst <n>` / `push` / `stl <slot>` / `pop`) into an ordinary local slot, since the register cannot be
  trusted to still hold the value at any later point in the function body. (**CORRECTED 2026-09-11**,
  same addendum: originally written here as `sta <n>`, the register's own dereference-WRITE op, not the
  "read this register's own value back out" op the prologue needs.)
- Known, deliberate gap: pointers passed this way are still exactly one CVM word with an implicit page of
  `0` -- "far" (32-bit, cross-page) pointers are not yet supported, even though the register itself is a
  full 32-bit (address, page) pair capable of addressing the GA144's whole memory range.

### Float parameters and return values (added 2026-09-26)

Stefan's own dictation, verbatim, the same day basic `float` support was added to the C compiler (see
`claude/c-compiler-design.md`'s matching section):

> first we have to define the ABI for float. we have 8 float register. so for fastcall functions the
> parameter are passed by register fr[0] for the first float parameter, fr[1] for the second parameter,
> ... for normal functions float parameter are put on the stack as a 32-bit value little endian word. so
> the lower value is on the lower address. this is also valid for float in memory. low word at
> memory[adr], high word at memory[adr+1]. within a function all fr register can be used freely, that
> means, calling a function may use some fr register. so calling a function that use fr register, the
> caller must save the fr register in use to the stack and restore them after the call. for functions
> that do not use fr register, there is no need to save/restore any fr-register. so the compiler must
> know if a function directly or indirectly uses any fr-register.

As stated, this is a rule layered on top of the two conventions above, not a third calling convention on
its own -- a function's `float` parameters are assigned this way regardless of whether its pointer
parameters go "by stack" or "by register":

- **Fastcall float parameters**: node 305's 8-register float file (`fr[0]..fr[7]`) is used directly, in
  declaration order among a function's own `float` parameters only -- a separate counter from the
  pointer-register count in "By register" above (a `__fastcall` function can use both kinds of register
  passing at once, one counter per register file). At most 8 `float` parameters may be passed this way;
  `Ga144.C.Toolchain.CParser.MaxFastcallFloatParameters` enforces this at parse time.
- **Normal (non-fastcall) float parameters, and float in memory generally**: always 2 CVM words, stored
  little-endian at the word level -- the LOW word at the lower address, the HIGH word at
  `address + 1`. Per Stefan verbatim: "low word at memory[adr], high word at memory[adr+1]" -- this
  applies uniformly to a stack-passed parameter, a global, and (by extension) any other `float` value
  this compiler stores to memory.
- **fr registers are caller-saved, conditionally.** Within a function body, every fr register is free to
  use for any purpose -- there is no permanently-reserved or callee-saved fr register. Consequently, a
  function that calls another function which uses fr registers (directly or indirectly, transitively
  through its own further calls) must save every fr register it currently has a live value in, across
  that call, and restore them afterward; a call to a function that uses NO fr registers anywhere in its
  own call tree needs no save/restore at all. This makes "does function X use any fr register, directly
  or indirectly" a whole-program (or, today, whole-translation-unit) property the compiler must compute
  for every function, not a per-call-site local decision.

**Compiler-side implementation notes -- documented hypotheses, not independently confirmed against real
hardware or a full simulation, exactly like the rest of this document's "by register" section above.**
These are `Ga144.C.Toolchain.CCodeGenerator`'s own necessary choices to turn Stefan's rule into actual
code, flagged here the same way every other unconfirmed item in this document is flagged:

- **Word order for round-tripping an fr register through two known memory/stack words is DERIVED, not
  stated.** Node 305's own F18 source for `fr/push`/`fr/pop` (see `Node305Program.cs`) sends the LOW
  word first and the HIGH word second on push, and expects to read the LOW word first (into the lower
  address) and the HIGH word second (into the higher address) on pop. The compiler follows this same
  order for parameters and globals (LOW word first/lower address, HIGH word second/higher address),
  which happens to line up exactly with Stefan's stated "low word at memory[adr], high word at
  memory[adr+1]" rule -- but this alignment was derived from the F18 source, not separately confirmed by
  Stefan for this specific case.
- **A function's return value is assumed to always come back in `fr[0]`**, regardless of calling
  convention (fastcall or not) and regardless of whether `fr[0]` happens to also be that function's own
  first fastcall float parameter register. This specific rule was never stated by Stefan -- it is the
  compiler's own choice, the most natural extension of "parameters start at fr[0]" to return values, and
  should be treated as open until confirmed.
- **The bare fpush/fpop round-trip used for caller-side save/restore is assumed to exactly restore an fr
  register's value**, i.e. `fpush f` followed later by `fpop f` (with nothing else touching the CVM data
  stack in between at that same depth) leaves `fr[f]` exactly as it was before the `fpush`. This is the
  LEAST mechanically certain part of the derivation above: a literal reading of node 305's own push/pop
  F18 trace, applied naively to a single bare register with no separate high/low unpacking, could suggest
  a half-swap rather than a clean round trip. The compiler takes the more likely INTENDED reading (a
  save/restore pair is meant to be transparent) rather than the literal-trace reading, and this should be
  verified against real hardware or a full node-305 simulation before being trusted for anything beyond
  this compiler's own generated save/restore code.
- **"Uses fr registers, directly or indirectly" is computed per translation unit, with an external
  function treated conservatively as "may use."** `CCodeGenerator.ComputeFunctionUsesFloatRegisters` scans
  every function defined in the same file/translation unit for a `float` local/parameter/literal/
  expression (direct use), then propagates that fact through the call graph to a fixed point (a function
  that calls a function that uses fr registers also counts as using them, transitively, including through
  recursion). A function only DECLARED (not defined) in this translation unit -- an external library
  function, for instance -- is conservatively assumed to use fr registers, so a call to it always gets
  the save/restore treatment unless/until real cross-translation-unit information is available.

## Open for the next round

- Which convention a given function uses when neither is forced by argument shape (i.e., is "by
  register" only ever the first-4-pointers rule already implemented, or can a function opt into "by
  stack" for everything, or vice versa)?
- Return values: is a pointer return value ever passed back via an address register, or always via the
  stack as today?
- The `0:0`/`0:1` open questions above.
- Float: the fr[0]-return-value convention, and the bare fpush/fpop caller-save/restore round-trip
  assumption (both flagged above), have not been confirmed against real hardware or a full node-305
  simulation. Also open: whether "uses fr registers, directly or indirectly" should ever be recorded
  per function signature (e.g. in a future object-file format) so a call to a function in ANOTHER
  translation unit or a prebuilt library need not be conservatively assumed to use fr registers.Count != call.Arguments.Count)
    {
      Error(call.Location, $"\"{call.FunctionName}\" expects {signature.ParameterTypes.Count} argument(s), but {call.Arguments.Count} were given");
    }

    // ABI v2: an argument landing in one of node 306's address registers is evaluated exactly like any
    // other argument (so side effects and evaluation order never change), but its result is then popped
    // back off the stack and loaded into the assigned address register instead of being left there for
    // the callee to find with ldp/stp. The register is loaded with "lda", which per node 306's own
    // opcode table takes the address word from r and the page word from the CVM stack top -- so the
    // sequence is: pop the just-pushed address into r, push a literal page word of 0 (far/32-bit
    // pointers spanning pages are not yet supported -- see this file's own ABI v2 doc-comment remarks),
    // then "lda <register>" consumes both. This mirrors the callee-side prologue spill in EmitFunction,
    // which immediately re-spills the same register back onto the stack/into a local slot -- the
    // register itself is never assumed to survive anything but this one handoff.
    for (int i = 0; i < call.Arguments.Count; i++)
    {
      EmitExpr(call.Arguments[i]);

      int? registerIndex = i < signature.ParameterRegisterSlots.Count ? signature.ParameterRegisterSlots[i] : null;
      if (registerIndex is int assignedRegister)
      {
        EmitCode("pop");
        EmitCode("pushlit 0");
        EmitCode($"lda {assignedRegister}");
      }
    }

    if (!signature.IsDefined)
    {
      _imports.Add(mangledName);
    }

    EmitCode($"call {mangledName}");
    return signature.ReturnType;
  }
}