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

- Uses node 306's four 32-bit address registers (0-3).
- Among a function's parameters, in declaration order, the first four whose type is a pointer are passed
  this way instead of on the stack; every other parameter (a 5th-or-later pointer, or any non-pointer
  parameter) keeps the stack convention above, renumbered to skip the register-passed ones.
- Address registers are volatile: per Stefan's own words, "the address register are volatile and are not
  saved on the stack when a function is entered" -- a register's value is never assumed to survive past
  the one transient moment it's used for (the call-site handoff, or the callee's own prologue spill).
- Call site: the argument's value is popped into `r`, paired with a literal page word of `0`, then loaded
  into its assigned register with `lda <n>`.
- Callee prologue: each register-eligible parameter is immediately spilled out of its register
  (`sta <n>` / `push` / `stl <slot>` / `pop`) into an ordinary local slot, since the register cannot be
  trusted to still hold the value at any later point in the function body.
- Known, deliberate gap: pointers passed this way are still exactly one CVM word with an implicit page of
  `0` -- "far" (32-bit, cross-page) pointers are not yet supported, even though the register itself is a
  full 32-bit (address, page) pair capable of addressing the GA144's whole memory range.

## Open for the next round

- Which convention a given function uses when neither is forced by argument shape (i.e., is "by
  register" only ever the first-4-pointers rule already implemented, or can a function opt into "by
  stack" for everything, or vice versa)?
- Return values: is a pointer return value ever passed back via an address register, or always via the
  stack as today?
- The `0:0`/`0:1` open questions above.
