# C compiler: preprocessor, then compiler proper

This is the design for a from-scratch C toolchain that targets the CVM assembly language documented
in `cvm-toolchain-design.md` -- the compiler's backend emits `.casm` text that `gaasm` assembles, the
same way a hand-written `.casm` file does today. Per Stefan's own instruction: if something the
compiler needs is missing from the CVM assembler, it becomes either a new opcode in the assembler
(this happened once already -- see "br/ifbr label operands" below) or a call into Stefan's own hand-
written "cvm" assembly library (this happened for `*`/`/`/`%`, which have no hardware primitive -- see
"Runtime library routines the `cvm` project must supply" below). Where a C feature's correct codegen
depends on an unconfirmed hardware fact, the compiler does not guess: it either documents its own best
hypothesis explicitly (see "CVM C ABI v1" below) or leaves a clearly-flagged simpler fallback for later
resolution (see "switch/case, and why not `tjmp`" below).

## Status

| Piece | State |
|---|---|
| Lexer (tokenizer), source reader (line-splicing, `\r\n` normalization) | Done (`Ga144.C.Toolchain`) |
| Preprocessor (macros, conditional compilation, `#include`, predefined macros) | Done (`Ga144.C.Toolchain`) |
| Preprocessor CLI (`gcpp`) | Done (`Ga144.C.Preprocessor`) |
| Parser (recursive descent, full AST) | Done (`Ga144.C.Toolchain`) |
| Code generator (CVM assembly text) | Done (`Ga144.C.Toolchain`) |
| Compiler facade (`CCompiler`: preprocess + parse + codegen in one call) | Done (`Ga144.C.Toolchain`) |
| Compiler CLI (`gacc`) | Done (`Ga144.C.Compiler`) |
| `br`/`ifbr` label operands in `gaasm` (the compiler's control-flow codegen needs these) | Done (`Ga144.Cvm.Toolchain`) |
| `__fastcall`/`__lower` keywords parsed, with their compile-time parameter-count/register-count checks | Done (`Ga144.C.Toolchain`'s `CParser`) -- see `cvm-abi.md`; consumed by `CCodeGenerator` for pointer/float register parameters, not yet for `__lower` |
| Basic `float` support: fastcall `fr[0..7]` parameters, 2-word stack/memory parameters, per-function fr-register-use analysis with caller save/restore | Done, 2026-09-26 -- see "Basic `float` support and the float ABI (2026-09-26)" below; scope deliberately narrow (no pointer/array of `float`, no comparisons, no `++`/`--`, no int&#8596;float conversion) |
| Runtime library (`__mul`/`__divs`/`__divu`/`__mods`/`__modu`) in Stefan's "cvm" project | Not started -- needed before any program using `*`/`/`/`%` can link |
| Wired into the IDE's C project "Build" button | Done, 2026-09-07 -- compiles, assembles, and (Library projects) archives automatically; see "IDE integration" below. Linking a Program project is still impossible since `galink` remains a stub |
| Two-pass optimizer (AST-level constant folding + CVM assembly peephole pass), independently toggleable per project | Done, 2026-09-10 -- constant folding on by default, peephole optimization off by default; see "Optimizer: AST-level constant folding and a CVM peephole pass (2026-09-10)" below |
| Constant-indexed LOCAL array elements addressed directly (no runtime address computation) | Done, 2026-09-10 -- unconditional, not gated by a project toggle; see "Local-array element addressing: constant-indexed access skips address computation entirely (2026-09-10)" below |
| Global-scalar access uses `gld`/`gst` directly (no address-register dance) | Done, 2026-09-10 -- unconditional, not gated by a project toggle; see "Global-scalar addressing: `gld`/`gst` replace the address-register dance (2026-09-10)" below |
| Real `tjmp` (table jump) for `switch` | Not started -- `tjmp`'s LOCATION is now confirmed (node 507, 2026-09-09) but its calling convention (case-table layout, bounds checking) is not, so `switch` still compiles to an if-chain; see "switch/case, and why not `tjmp`" below |
| `ldl`/`ldp`/`stl`/`stp` corrected to be register-r-mediated, not stack-mediated | Done, 2026-09-09 -- see "`ldl`/`ldp`/`stl`/`stp` go through register r, confirmed (2026-09-09)" below |
| Statement-level assignment/compound-assignment/increment-decrement skip the dup-and-discard round trip | Done, 2026-09-09 -- see "Statement-level codegen no longer dups a value it's about to throw away (2026-09-09)" below |
| `ifbr` renamed to `cbr`, real bit pattern confirmed, branch polarity flipped | Done, 2026-09-09 -- see "`ifbr` renamed to `cbr`: real bit pattern confirmed, branch polarity flipped (2026-09-09)" below |
| `lal`/`lap` retired, replaced by node 506's `'f`/`'fpush` + offset arithmetic | Done, 2026-09-09 -- see "`tjmp` located, `slit` retired, `lal`/`lap` retired in favor of `'f`/`'fpush` (2026-09-09)" below |
| `typedef` | Done, 2026-09-11 -- scalar/pointer/array aliases only (no function/function-pointer typedefs); prompted by Stefan's own build failure compiling a `libc` `heap.c` against `stddef.h`/`stdlib.h` (both need `size_t`) -- see "`typedef` support (2026-09-11)" below |

## Project layout

```
src/
  Ga144.C.Toolchain/       class library -- lexer, preprocessor, parser, codegen; shared by both CLIs AND the IDE
  Ga144.C.Preprocessor/    gcpp.exe  -- source (.c) -> preprocessed text (.i)
  Ga144.C.Compiler/        gacc.exe  -- source (.c) -> CVM assembly text (.casm)
```

All plain `net10.0` projects (no WPF, no third-party packages), mirroring the CVM toolchain's own
`Ga144.Cvm.Toolchain` + `Ga144.Cvm.Assembler`/`Ga144.Cvm.Librarian`/`Ga144.Cvm.Linker` split: five small
single-purpose tools (preprocess, compile, assemble, archive, link) rather than one large one, so the
IDE can drive the shared engine directly instead of shelling out to any of them. `gacc`'s own
job stops at CVM assembly text -- turning that into an object file is `gaasm`'s job, a separate step.

`Ga144.C.Compiler`, `Ga144.C.Preprocessor`, and `Ga144.C.Toolchain` are now all listed in
`Ga144EvalboardIde.slnx` (added 2026-09-07, alongside wiring Build into the IDE -- see "IDE
integration" below; they had been entirely missing from the solution file before this) --
`Ga144.Evb.Ide.csproj` also now references `Ga144.C.Toolchain` directly, which it never did before
(it only referenced `Ga144.Cvm.Toolchain`), which is exactly why the "Build" button could not call
into the C compiler at all until now. `FileSystemIncludeResolver` lives in `Ga144.C.Toolchain` (moved
from `Ga144.C.Preprocessor`) so `gacc` and the IDE's own Build step can both reuse it without a
CLI-to-CLI reference.

Diagnostics use MSVC/Visual-Studio-style `file(line,col): severity: message` formatting throughout
(lexer, preprocessor, parser, codegen alike), distinct from the CVM assembler's simpler
`line {n}: {message}` format used by `gaasm`/`galib`.

## Lexer and preprocessor

Unchanged since the previous revision of this doc -- see the lexer's own doc comments
(`CLexer`/`CSourceReader`/`CToken`/`CTokenKind`/`CSourceLocation`) and the preprocessor's own
(`CPreprocessor`) for the full detail: line-splicing and `\r\n` normalization, the full macro-expansion
algorithm (object-like/function-like, `#`/`##`, argument prescan, self-recursion guarding),
`#if`/`#ifdef`/`#ifndef`/`#elif`/`#else`/`#endif` with correct short-circuit rules, `#include` (quoted
and angled, one level of macro-expanded `#include SOME_HEADER`, resolution decoupled via
`ICIncludeResolver`), `#pragma once`/`#define`/`#undef`/`#error`, and the predefined macros `__CVM__`/
`__DATE__`/`__TIME__`/`__LINE__`/`__FILE__`. Deliberately unsupported: variadic macros, trigraphs,
`#line`, non-`once` `#pragma`s, and a function-like invocation whose name crosses an expansion boundary
to find its `(`.

## Supported C subset (v1)

**Supported:** `void`/`int`/`unsigned int`/`char`/`unsigned char`, pointers to any of these, 1D
fixed-size arrays, global variables (scalar-constant or string-literal initializers only, no braced
lists), local variables anywhere in a block (C99-style), `static` locals, function
definitions/prototypes, `const`/`volatile` (parsed, ignored), `if`/`else`/`while`/`do`-`while`/`for`,
`return`/`break`/`continue`, `goto`/labels (function-scoped, mangled), `switch`/`case`/`default` (real
C shape -- fallthrough works, labels may nest inside other statements including loops), full operator
precedence and every operator over the supported types, casts (a no-op reinterpretation, since every
supported type is one word -- **except `float`, see below**), `sizeof` (both `sizeof(type)`, purely
compile-time, and `sizeof expr` -- see the deviation noted below), string literals (pooled anonymous
globals), the comma operator, `typedef` (scalar/pointer/array aliases only -- see "`typedef` support
(2026-09-11)" below), and (parsed only -- see `cvm-abi.md`) the `__fastcall`/`__lower`
function-declaration keywords.

**`float` -- basic support only, added 2026-09-26 (see "Basic `float` support and the float ABI
(2026-09-26)" below for the full detail).** A plain `float` scalar -- local, parameter, global,
literal, arithmetic (`+ - * /`), assignment/compound-assignment, function parameter and return value --
is supported, backed directly by node 305's floating-point register file. Deliberately **not**
supported, and rejected with a clear compiler error rather than silently mishandled: `float *` (pointer
to float), `float[]` (array of float), `==`/`!=`/`<`/`>`/`<=`/`>=` on `float`, `++`/`--` on `float`, and
any int&#8596;float conversion (implicit or an explicit cast). Three builtins expose hardware float
operations that have no C operator of their own: `fabsf(x)`, `fminf(a, b)`, `fmaxf(a, b)`.

**Not supported (a clear compiler error, never a silent miscompilation):** `struct`/`union`/`enum`,
`double`/`long`/`short`, multi-dimensional arrays, function pointers (including a `typedef` of
one), variadic functions, bit-fields, `.`/`->` member access (needs structs), pointer/array of `float`,
`float` comparisons, `++`/`--` on `float`, and int&#8596;float conversion (all per the `float` scope
note directly above).

**Known deviation from strict C semantics:** `sizeof expr` (as opposed to `sizeof(type)`) evaluates
the expression at runtime and discards its value, rather than being a purely compile-time type
inspection -- safe as long as the operand has no side effects; flagged in the code generator itself as
a documented gap rather than fixed, since fixing it needs a real type-checking pass this v1 doesn't
have (see "Known gaps" below).

## Parser (`CParser`) and AST (`CAstExpr`/`CAstStmt`/`CAstDecl`/`CType`)

Recursive descent over the preprocessor's own token stream (`NewLine` tokens are stripped first --
C's grammar doesn't need them) with precedence climbing for expressions, producing a plain,
un-annotated AST (`CTranslationUnit` of `CFunctionDecl`/`CGlobalVarDecl`; the code generator computes
and checks every expression's type in the same walk that emits its code, rather than a separate
annotation pass). Panic-mode error recovery resynchronizes at the next `;` or matching `}` so one
syntax error doesn't cascade into dozens.

`CType` sizes every scalar and pointer -- `char` included -- at exactly one CVM word
(`SizeInWords`), because this is a word-addressed machine with no smaller addressable unit; `char`
still exists as its own type purely to preserve and check a programmer's intent, not to save storage.
This is also what makes pointer arithmetic and array indexing multiplication-free: `arr[i]` is always
just `base address + i`, never `base address + i * elementSize`. **As of 2026-09-26, `float` is the
first exception to "every scalar is one word"** -- it is two words (see "Basic `float` support and the
float ABI (2026-09-26)" below) -- which is exactly why `float *`/`float[]` are rejected rather than
supported: pointer arithmetic and array-element addressing both assume a one-word element throughout
this compiler, and neither has been generalized for a multi-word element type yet.

A multi-declarator bug was caught and fixed during development: `int *a, b;` must make only `a` a
pointer, not `b` too -- an early draft incorrectly let a leading `*` leak from one declarator into the
next by consuming it into a shared base-type variable before the comma-separated-declarator loop.
Fixed by re-parsing each declarator's own stars fresh from the un-mutated base type.

`__fastcall`/`__lower` (2026-09-07, per `cvm-abi.md`'s ABI dictation) are parsed as declaration
specifiers alongside `static`/`extern`/`const`/`volatile`, recorded on `CFunctionDecl` as
`IsFastcall`/`IsLower` (the latter true if EITHER keyword was written, since `__fastcall` implies
`__lower`), and rejected with a specific diagnostic anywhere other than a function declaration
(`CParser.RejectCallingConventionKeywords`). A `__fastcall` function declaring more than 4 pointer
parameters, or whose non-pointer parameters together need more than 32 node-511 registers, is a
compile-time error (`CParser.MaxFastcallPointerParameters`/`MaxFastcallRegisterWords`); as of
2026-09-26, one declaring more than 8 `float` parameters is likewise a compile-time error
(`CParser.MaxFastcallFloatParameters`, a separate counter -- see `cvm-abi.md` and "Basic `float`
support" below). `__lower` itself is not yet consumed by `CCodeGenerator`.

## Code generator (`CCodeGenerator`) -- the CVM C ABI v1

This is the single largest source of risk in the whole compiler: it targets a calling convention and a
set of instruction-level runtime behaviors that are **not confirmed against real CVM hardware or a
full simulation of nodes 506/606/607** -- they are this compiler's own necessary, documented
hypothesis, chosen as the most standard interpretation for a stack machine of this shape. The full
reasoning lives in `CCodeGenerator`'s own class-level doc comment; the load-bearing points:

- **Every non-void expression leaves exactly one word on the data stack; a void-returning call leaves
  none.** This single invariant is what lets every expression's codegen compose with every other one
  with zero "is this value needed" threading anywhere in the compiler, at the cost of an occasional
  extra `pop`/dup -- **except at statement level, where the compiler now knows the value is always
  discarded and skips that cost entirely; see "Statement-level codegen no longer dups a value it's
  about to throw away (2026-09-09)" below.** **`float` (2026-09-26) is the one deliberate exception to
  this invariant** -- it is two words wide, so it is never carried through the ordinary one-word data
  stack at all; see "Basic `float` support and the float ABI (2026-09-26)" below for the
  register-based model used instead.
- `pushlit`/`pop`/`push` behave as a literal-push, a pop-into-register-r, and a non-destructive
  push-of-r, respectively (`pop; push; push` duplicates the stack top). Binary ALU/comparison ops
  (`add`/`sub`/`and`/`xor`/`or`/`usl`/`ssr`/`usr`/`eq`/`ne`/`lt`/`gt`/`le`/`ge`/`ult`/`ugt`/`ule`/`uge`)
  consume r (set by an immediately preceding `pop`) and the stack top, replacing the stack top with
  the result; unary ops (`inv`/`negate`/`eq0`) act on r alone (`pop; op; push`).
- `xt`/`ldt`/`stt` act directly against the data stack (`xt` pops an address into `t`; `ldt` pushes the
  value at `t`; `stt` pops a value and stores it at `t`) -- unchanged. `ldl`/`ldp`/`stl`/`stp`, by
  contrast, are now CONFIRMED (2026-09-09, see the dedicated section below) to be register-r-mediated
  like the ALU ops, NOT stack-mediated -- every load site follows the raw opcode with an explicit
  `push`, and every store site precedes it with an explicit `pop`. **Correctness note found in review:** the CVM's `t` register has no
  save/restore of its own, so nothing may assume it survives arbitrary code emitted between resolving
  an indirect lvalue's address (`*ptr` or a non-constant-indexed array element) and actually
  loading/storing through it -- e.g. `*p = *q;` would otherwise silently write to the wrong place,
  because evaluating the RHS pointer re-issues its own address computation and repoints the address
  register before the store runs. The fix: an indirect lvalue's address is computed exactly once but
  immediately cached into its own dedicated local slot (`CCodeGenerator.CacheIndirectAddress`); every
  subsequent load or store re-derives the address fresh from that cached slot right before its own
  dereference, no matter what ran in between. Cost:
  this burns one new, permanently-reserved local slot per TEXTUAL occurrence of an indirect lvalue (not
  per variable) -- a function with many pointer/array accesses accumulates one extra frame slot
  per access, since this v1 has no slot-reuse/deallocation mechanism at all (ordinary locals already
  don't reuse slots across disjoint blocks either). Given GA144's very small per-node RAM, a function
  that leans heavily on pointers/arrays could exhaust it sooner than expected -- see "Known
  gaps" below. **As of 2026-09-10, a constant-indexed LOCAL array element no longer takes this indirect
  path at all (see "Local-array element addressing: constant-indexed access skips address computation
  entirely (2026-09-10)" below), and neither does a plain global-scalar access, which now goes straight
  through `gld`/`gst` with no address computed or cached at all (see "Global-scalar addressing:
  `gld`/`gst` replace the address-register dance (2026-09-10)" below) -- so this cost now applies only
  to pointers and non-constant-indexed array elements.**
- `cbr <label>` (formerly `ifbr` -- see "`ifbr` renamed to `cbr`: real bit pattern confirmed, branch
  polarity flipped (2026-09-09)" below) branches when r (set by an immediately preceding `pop`) is
  ZERO -- CONFIRMED against real hardware, and the opposite of this document's/`CCodeGenerator`'s
  former, never-confirmed assumption (branch on NONZERO). Every control-flow codegen method has been
  re-derived for the real polarity; see that section for the full detail.
- **Calling convention:** the caller pushes each argument left-to-right, then `call`s; the callee's
  `enter <n>` reserves n local slots and is assumed to make the caller's pushed arguments addressable
  via `ldp`/`stp` (or, for a parameter's ADDRESS rather than its value, the `'fpush`-based sequence
  described in "`tjmp` located, `slit` retired, `lal`/`lap` retired in favor of `'f`/`'fpush`
  (2026-09-09)" below, now that `lap` is retired) (parameter offset 0 = the first declared parameter);
  `return expr;` leaves expr's value on the stack and branches to the function's epilogue, whose
  `leave` is assumed to deallocate all locals AND the caller's pushed arguments together (Pascal-style
  callee cleanup), leaving only the return value (if any), followed by `ret`. **This is the "by stack,
  default" convention only, and `cvm-abi.md` has since both revised part of it (right-to-left
  evaluation order, not left-to-right) and added a second, register-based convention for `__fastcall`
  functions, and (2026-09-26) a float-register layer on top of both -- see "Basic `float` support and
  the float ABI (2026-09-26)" below for what `CCodeGenerator` implements today; see `cvm-abi.md` for
  the current, authoritative ABI spec.**
- `*`/`/`/`%` have no hardware primitive, so they compile to `call`s to hand-written runtime helpers
  (see the next section); `<<`/`>>` need no such call, since node 507's `usl`/`ssr`/`usr` are already
  binary (value, count).

## Basic `float` support and the float ABI (2026-09-26)

Stefan's own dictation, verbatim (also recorded in `cvm-abi.md`'s new "Float parameters and return
values" section, which is the authoritative ABI text -- this section covers the C-compiler-specific
implementation of it):

> first we have to define the ABI for float. we have 8 float register. so for fastcall functions the
> parameter are passed by register fr[0] for the first float parameter, fr[1] for the second parameter,
> ... for normal functions float parameter are put on the stack as a 32-bit value little endian word. so
> the lower value is on the lower address. this is also valid for float in memory. low word at
> memory[adr], high word at memory[adr+1]. within a function all fr register can be used freely, that
> means, calling a function may use some fr register. so calling a function that use fr register, the
> caller must save the fr register in use to the stack and restore them after the call. for functions
> that do not use fr register, there is no need to save/restore any fr-register. so the compiler must
> know if a function directly or indirectly uses any fr-register.
>
> for now just add basic float support to the C compiler. only add functions that are directly
> supported by the FP subprocessor should be used by the compiler. later we will add more functions in
> a float-library, which is then used by the compiler to support full support for float.

**Why `float` needed a fundamentally different codegen strategy, not just a new `CType`.** Every other
type in this compiler is one word, which is exactly what makes the "every non-void expression leaves
exactly one word on the stack" invariant (see "CVM C ABI v1" above) work. `float` is two words and is
never carried on the ordinary data stack: instead, `CCodeGenerator.EmitFloatExprInto(expr, destReg)`
evaluates a float expression directly into a chosen fr register, recursively evaluating a binary
operator's left operand into `fr[destReg]` and its right operand into `fr[destReg + 1]` before combining
them in place with the matching hardware op (`fadd`/`fsub`/`fmul`/`fdiv`) -- a depth-counter register
allocator, not a stack machine. Nesting deeper than the 8 available registers (`FloatRegisterCount`) is
a compiler error rather than a silent register clash.

**Scope, per "for now just add basic float support" and "only add functions that are directly supported
by the FP subprocessor" -- deliberate compiler decisions, not hardware hypotheses:**
- `float` local/global/parameter/return-value/literal, `+ - * /`, plain and compound assignment.
- Three new builtins exposing hardware ops with no C operator of their own: `fabsf(x)` (node 305's
  `fabs`), `fminf(a, b)` (`fmin`), `fmaxf(a, b)` (`fmax`).
- **Rejected outright, with a clear diagnostic:** `float *`, `float[]`, `==`/`!=`/`<`/`>`/`<=`/`>=` on
  `float`, `++`/`--` on `float`, and any int&#8596;float conversion (implicit or an explicit cast) --
  none of these has hardware support judged "directly supported by the FP subprocessor" simple enough
  for this first pass, and Stefan's own instruction is to add the rest later via a float-library instead
  of guessing at conversions/comparisons now.

**Parameter passing, implementing `cvm-abi.md`'s new float-ABI section directly:**
- **`__fastcall` float parameters** go into `fr[0]`, `fr[1]`, ... in declaration order, counted only
  among that function's own `float` parameters (`CCodeGenerator.ComputeParameterFloatRegisterSlots`) --
  a separate counter from the existing pointer/address-register one, so a `__fastcall` function can use
  both register files at once. `CParser.MaxFastcallFloatParameters` (8) is enforced at parse time.
- **Normal (stack-passed) float parameters** always take 2 consecutive stack words, low word at the
  lower offset -- never fr registers, regardless of how many are free.
- **A float local's 2 words are ordered opposite to a float parameter's or global's**, to honor "low
  word at the lower address" against this compiler's own pre-existing local-addressing convention (a
  local's address is `frame pointer - offset`, so address DECREASES as slot number increases): a local
  float's LOW word lives at the HIGHER slot number, HIGH word at the base slot. Parameters (address
  increases with offset) and globals use the natural, ascending low-then-high slot/label order.
- **A float global is stored as two adjacent data-section labels**, `name` (low word) and `name__hi`
  (high word), rather than one multi-word `.word` list -- `gld`/`gst` take a single label with no
  address-plus-offset operand syntax, and this compiler fully controls DATA-section emission order, so
  the two labels are genuinely contiguous in the final binary even though they're accessed by two
  separate symbolic names rather than `label`/`label+1` arithmetic. **Flagged as an internal
  representation detail**: a hand-written `.casm` routine expecting real `label+1` address arithmetic
  would not find the second word this way.
- **A float-returning function's return value is placed in `fr[0]`** -- a compiler-chosen convention,
  not stated by Stefan; see `cvm-abi.md`'s own flag on this point.

**Caller-side save/restore, implementing "the compiler must know if a function directly or indirectly
uses any fr-register":**
- `CCodeGenerator.ComputeFunctionUsesFloatRegisters` computes, once per translation unit, whether each
  defined function directly uses `float` (a direct AST scan) or indirectly uses it (a fixed-point walk
  of the call graph, including through recursion). A function only declared (not defined) here -- an
  external/library function -- is conservatively assumed to use fr registers.
- `_liveFloatRegisterCount` tracks how many low fr registers hold values an enclosing expression still
  needs across a nested call. `EmitCall` wraps the entire call -- argument marshaling through
  return-value retrieval -- with ascending `fpush`es before and descending `fpop`s after, whenever any
  fr registers are live AND the callee may use fr registers; a float return value is retrieved via
  `fmove destReg 0` before the restoring pops run, so a non-`fr[0]` destination is never clobbered by
  its own restore.
- A call to a function proven (by the analysis above) to use no fr registers anywhere in its own call
  tree needs no save/restore at all, exactly as Stefan asked.

**Everything above is implemented in `Ga144.C.Toolchain`'s existing four files** -- `CType.cs` (the
`Float` kind, 2 words, `IsFloat`/`IsScalar`), `CAstExpr.cs` (`CFloatLiteralExpr`), `CParser.cs` (the
`float` keyword, float literals, the fastcall float-parameter-count check), and `CCodeGenerator.cs`
(everything above) -- rather than a new file, since `float` slots into this compiler's existing
type/AST/codegen structure at every point except the stack-vs-register expression model, which is
scoped to a handful of new, clearly-named methods rather than disturbing the existing one-word-per-value
codegen for every other type.

**Not yet done, per Stefan's own "later we will add more functions in a float-library":** float
conversions to/from `int`, float comparisons, pointer-to-float and array-of-float, and any float
function beyond the FP subprocessor's own direct hardware ops (`sqrt`, trig, etc.) -- all deferred to
that future float-library round, consistent with today's instruction to keep this first pass narrow.

## `ldl`/`ldp`/`stl`/`stp` go through register r, confirmed (2026-09-09)

Per Stefan's own opcode bit patterns --

| Mnemonic | Bit pattern | Meaning |
|---|---|---|
| `ldl <offset>` | `1001_111?_????_????` | load local into r (9-bit offset) |
| `ldp <offset>` | `1001_110?_????_????` | load parameter into r (9-bit offset) |
| `stl <offset>` | `1001_101?_????_????` | store r into local (9-bit offset) |
| `stp <offset>` | `1001_100?_????_????` | store r into parameter (9-bit offset) |

-- "locals and parameters are stored on the stack relative to the frame pointer and are NOT accessed
with push and pop." This overturns this document's (and `CCodeGenerator`'s) former, never-confirmed
assumption, stated above and now known wrong, that these four acted directly against the data stack the
same way `xt`/`ldt`/`stt` do. They instead behave exactly like the generic ALU/unary ops: `ldl`/`ldp`
load into register r only (the data stack is untouched), and `stl`/`stp` store r only (again, the data
stack is untouched) -- getting a value onto or off of the stack around them needs an explicit `push` or
`pop`, the same bridge the ALU ops already need.

**Every call site fixed to match, 2026-09-09** (`EmitLoad`, `EmitStore`, `EmitName`, `EmitSwitch`'s
selector caching, `CacheIndirectAddress`, and the register-eligible parameter spill in
`EmitFunction`'s prologue) -- the external contract each of those methods already presented to its own
callers (`EmitLoad`: pushes exactly one word representing the lvalue's current value; `EmitStore`:
consumes exactly one word from the stack top and writes it, net stack effect zero) did not change, so
none of `EmitAssign`/`EmitCompoundAssign`/`EmitIncrementOrDecrement`/`ResolveLvalue` needed touching --
only the raw instruction sequences inside those six sites did. One concrete, previously-latent bug this
also fixes: `EmitName`'s old `ldl 0; ldl 1; pop; add` sequence for something like `a + b` would have
silently discarded `a`'s value the instant `ldl 1` overwrote r, since neither `ldl` ever put anything on
the stack for `add` to combine -- this only worked by coincidence for programs that happened not to
read two locals back to back before combining them, which is exactly why it hadn't been noticed yet.

`lal`/`lap` (load ADDRESS of a local/parameter -- a different pair of mnemonics from the four above)
used to be exempted from this correction, their own stack-vs-register behavior left as this compiler's
unconfirmed assumption. As of 2026-09-09 that question is moot -- `lal`/`lap` are retired outright, not
merely corrected; see "`tjmp` located, `slit` retired, `lal`/`lap` retired in favor of `'f`/`'fpush`
(2026-09-09)" below for what replaced them.

## Statement-level codegen no longer dups a value it's about to throw away (2026-09-09)

Stefan, after the fix above: "it is still way too inefficient," with a compiled `void main() { int a =
3; int b = 4; int c = a + b; }` still emitting a `pop; push; push; pop` dup-and-immediately-discard
sequence around every single local store. Root cause: `EmitAssign`/`EmitCompoundAssign`/
`EmitIncrementOrDecrement` unconditionally keep a residual copy of their own result on the stack, since
C's assignment operator is itself an expression (needed for `a = b = 3;`, `if ((a = f()))`, etc.) --
but a bare `a = 3;` statement, a plain declaration's initializer, `a += 1;`, or `i++;` used on its own
line NEVER has anything downstream to read that residual value from, so keeping it (and then having to
pop it straight back off) was pure waste, not a hardware requirement.

Fixed with "ForEffect" twins of all three -- `EmitAssignForEffect`, `EmitCompoundAssignForEffect`,
`EmitIncrementOrDecrementForEffect` -- each of which skips `EmitDup` entirely. This is safe because
`EmitStore` already nets to "-1 word" on its own (it pops the value it stores via the correction
above), which exactly cancels the "+1 word" the value-producing code just pushed -- net stack effect
zero, no leftover, no separate discard `pop` needed either. `EmitStatement`'s `CExprStmt` case now
pattern-matches on the top-level expression (`CAssignExpr`/`CCompoundAssignExpr`/a pre/post increment
or decrement `CUnaryExpr`) and routes to the matching ForEffect twin, falling back to the original
generic "`EmitExpr`, then `pop` if non-void" path for anything else (a bare function call used as a
statement, for instance, where the "is the result needed" question genuinely doesn't have a cheaper
answer); `CLocalVarDecl`'s initializer goes through `EmitAssignForEffect` directly rather than building
a throwaway `CAssignExpr` just to immediately discard its result.

Concretely, `int a = 3;` now compiles to exactly `pushlit 3; pop; stl 0` -- three instructions, matching
what Stefan asked for directly (`pushlit 3`, then a `pop` to get the value into r, then `stl 0` to store
it -- the `pop` is required, since `stl` reads r, not the stack, per the correction above). Pre- and
post-increment/decrement collapse to the same for-effect sequence when used bare (`i++;` and `--i;`
both just become "load, add/subtract 1, store" -- the pre/post distinction only matters for the
expression's own result value, which a bare statement never has).

## `ifbr` renamed to `cbr`: real bit pattern confirmed, branch polarity flipped (2026-09-09)

Stefan, after the two fixes above: "\"ifbr\" no longer exist and should be replaced with \"cbr\". it is
basically the same. opcode cbr 1010_11??_????_???? conditional branch to offset if r == 0. the offset
is signed 10-bit number." Two things changed, not one:

1. **The mnemonic and its tag.** `ifbr` is retired outright; `cbr` takes over its role with a real,
   hardware-confirmed encoding -- a fixed 6-bit tag (`0xAC00`, binary `101011`), one bit wider than
   `br`'s own 5-bit tag, OR'ing a 10-bit signed offset (`-0x200..0x1FF`), one bit narrower than `br`'s
   11-bit field. `CvmInstructionSet.ConditionalBranchTag` moved from the old, never-confirmed `0x9800`
   placeholder guess to `0xAC00`; `ConditionalBranchTagMask`/`ConditionalBranchOffsetBitMask`/
   `ConditionalBranchOffsetMinValue`/`ConditionalBranchOffsetMaxValue` were added since `cbr` no longer
   shares a field width with `br`. This ALSO changes which other opcodes `cbr` collides with: the old
   collision against `stl`/`stp`/`ldl`/`ldp` (`0x9800-0x9FFF`) is gone outright, since `0xAC00-0xAFFF`
   doesn't touch that range at all -- but that same new range briefly fully contained node 606's own
   orphaned `lal`/`lap` tags (`0xAE00`/`0xAF00`) until they, too, were retired outright the same day
   (see "`tjmp` located, `slit` retired, `lal`/`lap` retired in favor of `'f`/`'fpush` (2026-09-09)"
   below), which made that particular collision moot rather than merely resolved by disassembly
   priority. See `cvm-toolchain-design.md`'s own updated "br/ifbr opcode tag" section and
   `CvmInstructionSet.ConditionalBranchTag`'s own remarks for the full derivation.

2. **The branch polarity -- the part that actually changes generated code, not just a name.** This
   document and `CCodeGenerator` had always assumed (never confirmed) that the old `ifbr` branched when
   r was NONZERO ("Every control-flow codegen method below is built around this polarity," this
   document's own former wording). Stefan's bit pattern says the opposite: `cbr` branches when r is
   ZERO. Every control-flow codegen method that emits a conditional branch was re-derived for the real
   polarity:

   | Site | Old (`ifbr`, assumed branch-on-nonzero) | New (`cbr`, confirmed branch-on-zero) |
   |---|---|---|
   | `if`/`while`/`for` (branch away from the body when false) | `pop; eq0; ifbr label` | `pop; cbr label` -- **one instruction shorter**: `cbr` already tests exactly "condition is false" |
   | ternary `?:` (branch to the false branch) | `pop; eq0; ifbr label` | `pop; cbr label` -- same simplification |
   | `&&` short-circuit (branch away on false) | `pop; eq0; ifbr label` | `pop; cbr label` -- same simplification |
   | `do`-`while` (loop back while true) | `pop; ifbr label` | `pop; eq0; cbr label` -- now needs an inversion it didn't before |
   | `switch` case match (branch into the case on equal) | `eq; pop; ifbr label` | `eq; pop; eq0; cbr label` -- same new inversion |
   | `\|\|` short-circuit (branch away on true) | `pop; ne0; ifbr label` | `pop; eq0; cbr label` -- inverted, and `ne0` is no longer the right op |

   The net effect is a small further efficiency win on top of the two fixes above: the most common
   cases (`if`, `while`, `for`, `&&`, the ternary) all get ONE INSTRUCTION SHORTER, since `cbr` already
   tests exactly the condition they need with no `eq0` in front of it; only the less common cases
   (`do`-`while`, `switch`, `||`) need an `eq0` they didn't before. All six call sites --
   `EmitStatement`'s `CIfStmt`/`CWhileStmt`/`CDoWhileStmt`/`CForStmt` cases, `EmitLogical`,
   `EmitConditional`, and `EmitSwitch` -- were updated together; `CCodeGenerator`'s own class doc
   comment records the same table.

This was caught by tracing through what the OLD `eq0`-then-`ifbr` sequences would actually do once fed
into a branch-on-zero instruction instead of a branch-on-nonzero one: every single one of them would
have branched on exactly the opposite condition from what the surrounding C code needs (e.g. `if` would
have run the `else` branch when the condition was true) -- a silent, total correctness break for every
`if`/`while`/`for`/`do`-`while`/`switch`/`&&`/`||`/`?:` in any program compiled with the old sequences
against the real, now-confirmed opcode. Caught and fixed before it reached Stefan as a bug report.

## `tjmp` located, `slit` retired, `lal`/`lap` retired in favor of `'f`/`'fpush` (2026-09-09)

Stefan, later the same day as the `cbr` fix above, verbatim, in three parts, alongside a complete new
Node 506 F18 source:

> "tjmp" is in node 507.
> "slit" is replaced by "lit". remove it from the language.
> "lal" & "lap" are removed. use "'f" or "fpush" from node 506 and add the offset to calculate the
> address of a local or parameter. i will provide a new 506.

**`tjmp`'s location is confirmed; its calling convention is not, so `switch` is unchanged.** See
"switch/case, and why not `tjmp`" below -- this message confirms only WHERE `tjmp` lives (node 507,
the same "1000_1???" local-execute tag family as `nop`/`push`/`pop`/`ret`/`halt`), not how a case table
would be laid out, bounds-checked, or indexed, so `CCodeGenerator.EmitSwitch` is unchanged: still an
if-chain, per this project's own standing discipline of never guessing an unconfirmed hardware calling
convention.

**`slit` is gone; `lit` (already-existing, unaffected in its own right) takes over its role.** `slit`
never had a call site of its own in `CCodeGenerator` (the compiler always emitted `pushlit`, a
DIFFERENT, TrailingWord-encoded mnemonic that pushes a full-width literal onto the CVM's own native
stack -- unrelated to `slit`'s role of loading a small signed value directly into the F18 interpreter's
own R register) so this retirement needed NO C-compiler codegen change at all. It is recorded here only
because "remove it from the language" is a language-wide change, and `CvmDebuggerDefaultProgram.cs`'s
own hand-written smoke test (which DID use `slit`, extensively, exercising the raw CVM opcode set
directly rather than through this compiler) needed its literal values rescaled to `lit`'s narrower
10-bit range -- see `cvm-toolchain-design.md`'s own matching section for the full detail.

**`lal`/`lap` retired outright; local/parameter ADDRESSES now come from `'fpush` plus explicit offset
arithmetic.** Unlike `slit`, `lal`/`lap` DID have call sites in `CCodeGenerator` -- `EmitAddressOf`'s
`CNameExpr` case (the `&`-operator's own codegen) and `EmitName`'s array-decay branches (an array used
by name decays to the address of its first element) both used to emit a bare `lal <index>`/`lap <index>`
for a local's/parameter's own address. Both were replaced with a single shared helper,
`EmitLocalOrParameterAddress(offset, isParameter)`:

```csharp
private void EmitLocalOrParameterAddress(int offset, bool isParameter)
{
  EmitCode("fpush");
  EmitCode($"pushlit {offset}");
  EmitCode("pop");
  EmitCode(isParameter ? "add" : "sub");
}
```

`'fpush` (node 506's own new opcode, pushing its own frame pointer `f` directly onto the CVM data
stack) supplies the frame pointer; the sign of the offset arithmetic follows directly from node 506's
own `f/main` dispatch, which negates a local's offset with `inv` before adding it to `f` (for
`ldl`/`stl`) but does NOT negate a parameter's offset (for `ldp`/`stp`) -- so symmetrically, a LOCAL's
own address is `f - offset` and a PARAMETER's own address is `f + offset`. The `sub`/`add` sequence
itself is not a new hypothesis: it is derived directly from `EmitBinaryOperation`'s own already-
confirmed `Subtract`/`Add` cases (`pop` then `sub`/`add` computes `left OP right`, with `left` already
on the stack top and `right` popped into `r`), the same pattern every other binary operator in this
compiler already uses.

**Not yet confirmed against real hardware.** No fresh self-check has been run against this exact
sequence since the change -- `CvmDebuggerDefaultProgram.cs`'s own OLD self-check (comparing `lal`'s/
`lap`'s freshly computed addresses against the `stl`/`ldl` and `stp`/`ldp` transactions' own addresses,
confirmed exact on a 2026-08-30 run) could not simply be updated in place, since `lal`/`lap` no longer
assemble at all -- the two lines were removed from that hand-written program rather than replaced with
an unverified guess at what a real `'fpush`-based self-check should expect, per this project's own
established discipline of never fabricating an expected value for a hardware-validation script (see
that file's own remarks, and `cvm-toolchain-design.md`'s matching section, for the full detail). Treat
this section's own `sub`/`add` derivation with the same caution as every other "documented hypothesis,
not yet confirmed" item in this document until a real run exercises it.

## Runtime library routines the `cvm` project must supply

Stefan's own hand-written assembly library needs to define these five leaf routines before any
program using `*`, `/`, or `%` can link:

| Routine | For | Calling convention |
|---|---|---|
| `__mul` | `*` | caller pushes lhs, then rhs; callee consumes both, pushes 1 result word, `ret`s |
| `__divs` | `/` on signed operands | same |
| `__divu` | `/` on unsigned operands | same |
| `__mods` | `%` on signed operands | same |
| `__modu` | `%` on unsigned operands | same |

All five are pure leaves (no frame needed) -- the same two-pushed-operands-in, one-result-out shape,
chosen so the compiler's own call-site codegen needs no special-casing versus an ordinary user
function call. Signed vs. unsigned is chosen by the compiler at each call site, following ordinary C
usual-arithmetic-conversion rules (unsigned wins if either operand is unsigned).

## switch/case, and why not `tjmp`

`tjmp` (table jump) exists in the CVM, intended for `case` statements -- its LOCATION is confirmed as
of 2026-09-09 (node 507, "'tjmp is in node 507," the same "1000_1???" local-execute tag family as
`nop`/`push`/`pop`/`ret`/`halt`) -- but its exact calling convention (how a case table is laid out in
memory, how the selector is bounds-checked and turned into an index, and what happens on an
out-of-range selector) is still not confirmed anywhere in this project's documentation -- unlike nodes
507/508/506/407/606's OTHER mnemonics, which all went through an explicit real-hardware or
real-compile confirmation before this compiler (or `gaasm`) relied on them. Per this project's own
established practice of never guessing an unconfirmed hardware calling convention into a permanent,
append-only opcode table, `switch` still compiles to a straightforward, always-correct chain of
equality comparisons against the selector (evaluated once into a compiler-allocated temporary local
slot) -- correct, but O(n) rather than O(1) per switch. This is a flagged future optimization, not a
limitation of what C a `switch` may contain: fallthrough works, and `case`/`default` may appear nested
inside other statements (including loops -- the Duff's-device shape), exactly like real C.

## `br`/`ifbr` label operands (2026-09-06)

The compiler's every `if`/`while`/`for`/`switch`/`&&`/`||` needs to branch to a compiler-generated
label, never a hand-computed literal offset -- something `gaasm` didn't support yet (see
`cvm-toolchain-design.md`'s own "Branch target addressing, confirmed against real hardware" section,
which had flagged the offset formula as known but not wired in). Implemented in
`CvmAssembler.EmitEmbeddedSignedValue`: `br`/`ifbr` (`ifbr` since renamed `cbr` -- see "`ifbr` renamed
to `cbr`" above; the assembler logic itself needed no change, since it already read every shape's tag
and field width generically rather than hardcoding either mnemonic's own values) now accept a label
defined earlier in the same file's same section, resolved to
`targetLabelOffset - (thisInstructionOffset + 1)` -- see `claude/cvm-br-ifbr-label-fix.md` for the full
detail. A literal offset still works exactly as before; `lit` (which absorbed the retired `slit`'s
role 2026-09-09) is unaffected either way -- it was never an address computation.

## Directory conventions (per Stefan)

A C project's preprocessed output belongs in a `pre` directory (`.i` files); the compiler's generated
CVM assembly belongs in a `tasm` directory (`.casm` files, compiler output, safe to delete/regenerate);
a project's own hand-written CVM assembly sources -- assembled directly by `gaasm`, never compiled from
C, and where a hand-written primitive or CVM feature the compiler can't yet generate belongs -- live in
`asm` (also `.casm`, but part of the project, never regenerated); assembled object files (compiler-
generated or hand-written) belong in an `obj` directory (`.gaobj` files, added 2026-09-07, entirely
generated by Build, safe to delete). `Ga144.Evb.Ide.Models.CProject` exposes
`PreprocessedDirectoryName`/`TargetAssemblyDirectoryName`/`AssemblyDirectoryName`/
`ObjectDirectoryName` and the matching path/file-listing helpers for all four, plus `LibraryOutputPath`
for a Library project's own archived `.galib` (see "IDE integration" below for the naming choice
there, which is flagged rather than confirmed).

## IDE integration: the "Build" button (2026-09-07)

The IDE's own C project window (`Ga144.Evb.Ide.Views.CProjectWindow`) previously had a "Build" button
that only showed `CProjectViewModel.BuildDescription` -- a static string describing what Build *would*
do -- in an informational MessageBox; clicking it never actually compiled anything, which is what
prompted this integration (Stefan's own build attempt just surfaced that placeholder text).
`CProjectViewModel.Build()` now does the real work end to end, using the same library APIs
`gacc`/`gaasm`/`galib` themselves are built on (`CCompiler.Compile`, `CvmAssembler.Assemble`,
`CvmLibrary.Save`) directly, rather than shelling out to any CLI:

1. Builds an include search list from the project's own `include` directory plus each DIRECTLY
   imported library's own `include` directory. **Flagged:** not transitive -- a library reference's
   own further library references are not searched, so a header inside "a library of a library" is
   not found today.
2. Compiles every `src/*.c` file (`CCompiler.Compile`), always writing the preprocessed text into
   `pre/` once preprocessing itself succeeds, and the generated assembly into `tasm/` only once the
   whole file compiles cleanly. A file that fails compiling does not stop the others -- every source
   file is always attempted, and every diagnostic from every file is collected and shown.
3. Assembles every `.casm` produced in step 2, plus every hand-written `asm/*.casm` file, into the new
   `obj/` directory as `.gaobj` files (`CvmAssembler.Assemble`) -- again, every file is always
   attempted regardless of earlier failures.
4. For a Library project, ONLY if every compile and every assemble in steps 2-3 succeeded, archives
   the resulting object files into the project's own `.galib` (`CProject.LibraryOutputPath`, written
   via a temp-file-then-rename exactly like `galib`'s own crash-safety) -- a library with some members
   silently missing would be worse than no library at all. For a Program project, Build stops after
   step 3 and reports that linking is not implemented yet -- `galink` is still a stub.

`CProject.LibraryOutputPath` names the `.galib` after the project's own root FOLDER name, not
`CProjectMetadata.Name` (a free-form display name that may contain characters invalid in a file name).
**Flagged:** Stefan has not confirmed this naming choice.

`Ga144EvalboardIde.slnx` and `Ga144.Evb.Ide.csproj` needed fixing to make any of this possible: the
solution file was entirely missing `Ga144.C.Toolchain`/`Ga144.C.Compiler`/`Ga144.C.Preprocessor`, and
the IDE project itself never referenced `Ga144.C.Toolchain` at all (only `Ga144.Cvm.Toolchain`) --
which is exactly why "Build" could never have reached the C compiler even if a Build method had
existed. Both gaps are fixed as of this same date.

## Optimizer: AST-level constant folding and a CVM peephole pass (2026-09-10)

Stefan first asked, of the small `void main() { int a = 3; int d[7]; d[0] = a; }` example walked
through earlier the same day, "can you add an optimizer step before generating the assembler code?
just reply yes or no." -- answered "Yes." -- then, "then let's build an optimizer for the C compiler."
A compiler-wide optimizer is exactly the kind of change that's expensive to redo if the scope guess is
wrong, so the shape, placement, and toggling were confirmed rather than assumed: asked whether this
should be one pass or two, where each should run, and whether it needed to be switchable, Stefan's
answer was "both as two separate passes and add option to the compiler to activate or deactivate these
optimizer steps and add the options to the C project window" -- three requirements, all present in
what shipped: (1) two independent passes, not one; (2) each independently toggleable at the compiler
level; (3) both toggles exposed as persisted, per-project options in `CProjectWindow`.

**Why two passes, not one.** The motivating `d[0] = a;` computes the array element's address as `d`'s
base plus `0 * elementSize` -- always zero for a compile-time-constant index of 0 -- and that
arithmetic is synthesized directly as raw instruction text by `CCodeGenerator`'s own
address-computation helpers (`EmitLocalOrParameterAddress`, the array-index codegen built on it),
never as a `CExpr` AST node. An AST-level pass can never see it, no matter how thorough, because there
is no AST node there to fold. A *second*, separate pass over the emitted assembly text itself is
therefore architecturally necessary, not merely a nicety -- confirming that Stefan's own "two separate
passes" answer was the right shape, not just an acceptable one.

**Pass 1 -- `CConstantFolder` (AST level, before code generation).** Walks the whole
`CTranslationUnit` bottom-up and folds any `CUnaryExpr`/`CBinaryExpr` whose operand(s) are already
integer-literal constants into a single `CIntLiteralExpr`, via a newly shared `TryEvaluateBinaryOp`
helper (`+ - * / % & | ^ << >>`, division/modulo guarded against a constant-zero divisor) that
`CCodeGenerator.TryEvaluateConstantLong` -- previously the only place this exact arithmetic was
evaluated, for array-bound and `case`-label checking -- now delegates to as well, so there is exactly
one implementation of "what does this constant expression evaluate to," not two that could drift
apart. Deliberately does **not** fold comparisons or logical operators (`< > == && ||` etc.) -- their
result's signedness depends on each operand's resolved type, which this pass runs before codegen
computes -- and does not collapse a ternary's constant condition into just one branch
(control-flow-adjacent, left for a future round). Defaults **on** (`enableConstantFolding = true`): it
only ever removes runtime arithmetic the source itself already fully determines, using the same
evaluator this compiler already trusted elsewhere before this feature existed, so the risk bar for
enabling it by default is low.

**Pass 2 -- `CvmPeepholeOptimizer` (assembly level, after code generation).** Rewrites
`CCodeGenerator`'s own emitted CODE-section lines to a fixed point (re-scanning after every match,
since removing one redundant pair can expose another), matching small runs of adjacent instructions
against six rules and deleting or shortening them:

| Pattern | Rewrite | Why it's a no-op |
|---|---|---|
| `push` ; `pop` | delete both | push then immediately discard |
| `pushlit 0` ; `pop` ; `add` | delete all three | adding zero |
| `pushlit 0` ; `pop` ; `sub` | delete all three | subtracting zero |
| `ldl N` ; `stl N` (same N) | delete both | load a local then immediately store it back unchanged |
| `ldp N` ; `stp N` (same N) | delete both | same, for a parameter |
| `stl N` ; `ldl N` (same N) | keep only `stl N` | the value just stored is still the one that would be reloaded |
| `stp N` ; `ldp N` (same N) | keep only `stp N` | same, for a parameter |

A label always occupies its own separate entry in `CCodeGenerator`'s internal line list (`EmitLabel`
never shares an entry with an instruction the way `EmitCode` never merges two instructions into one),
so two adjacent list entries are always genuinely adjacent instructions with nothing -- in particular
no branch target -- hidden between them; the peephole pass never needs to special-case labels itself.
The last two rules (forwarding a just-stored value back out of a `stl`/`stp` instead of re-loading it)
lean on the same assumption `CCodeGenerator.EmitFunction`'s own ABI v2 prologue already flags and
relies on -- "a store leaves r unchanged, like push does" -- rather than a new, separately-unverified
one; this is recorded here explicitly so the two reuses of that one open question stay visible
together. **Defaults off** (`enablePeepholeOptimization = false`): unlike constant folding, two of
these six rules are only as sound as that not-yet-hardware-confirmed assumption, so this project's own
"flag, don't guess" discipline calls for it to be opt-in, not a default, until a real run confirms it.

Traced by hand against the motivating example: `d[0] = a;`'s `pushlit 0; pop; add` (from the
zero-index address arithmetic) is removed; the genuinely non-zero `fpush; pushlit 1; pop; sub` that
computes `d`'s own base address, and the unrelated `pushlit 0; arst; ...` sequence used for a
node-306 address-register call (a page-select literal, not an "add zero" idiom, since it feeds `arst`
rather than `add`/`sub`), are both correctly left alone.

**Wiring.** `CCompiler.Compile` gained two optional, independent parameters, `enableConstantFolding`
(default `true`) and `enablePeepholeOptimization` (default `false`); `CCodeGenerator` gained a
constructor parameter of the same name and runs the peephole pass over its own `_codeLines` at the
tail of `Generate()`, after `EmitFunction`'s index-based patch-in of each function's final `enter N`
has already happened, so the peephole pass always sees final, patched instruction text and never risks
racing that patch. Both existing call sites -- `CProjectViewModel.Build()` and the standalone `gacc`
CLI (`Ga144.C.Compiler-Program.cs`) -- are unaffected, since both new parameters are optional and
trailing; `gacc` still calls `CCompiler.Compile` with its original four arguments and gets the same
defaults as before this feature existed.

**IDE integration.** `Ga144.Evb.Ide.Models.CProjectMetadata` gained two persisted YAML-backed
properties, `EnableConstantFolding` (default `true`) and `EnablePeepholeOptimization` (default
`false`), with `CProject` exposing matching pass-through properties and `CProjectViewModel` exposing
matching bindable ones (mutate `Model`, `Save()`, raise change notification -- the same pattern
`Kind`/`SetChipProject` already use). `CProjectWindow`'s build toolbar gained two checkboxes,
"Constant folding" and "Peephole optimization," bound two-way to those view-model properties, both
sharing one tooltip (`CProjectViewModel.OptimizerDescription`) explaining what each pass does;
`CProjectViewModel.Build()` now passes both project settings straight through to `CCompiler.Compile`,
so the checkboxes take effect on the very next Build. An existing project file with neither key still
loads correctly with both defaults applied -- YamlDotNet simply falls back to each property's C#
default when a key is absent, so no migration step was needed for project files written before this
feature existed.

## Local-array element addressing: constant-indexed access skips address computation entirely (2026-09-10)

Stefan, immediately after seeing the peephole optimizer's own trace against `d[0] = a;` above: "actually
there is no need to use an address register. \"d[0] = a;\" d[0] is a local variable, a is a local
variable. so a simple \"ldl d[0]; stl a\" with the offset calculated for d[0] and a would have done the
job. is there an optimizer that can catch this situation?" He's right, and this needed a different kind
of fix than either pass above -- not a third pass bolted onto the same framework.

**Why neither existing pass could catch it.** `CConstantFolder` only folds arithmetic already
represented as AST nodes -- it has no say over WHICH addressing strategy the code generator picks for
an lvalue in the first place. `CvmPeepholeOptimizer` only recognizes small, fixed adjacent-instruction
patterns in already-emitted text -- collapsing the ENTIRE `fpush`-based address computation, cache-
into-a-slot, and node-306 `arst`/`lda`/`sta` dereference dance into a single `ldl`/`stl` would mean
reconstructing, purely from instruction text, that the whole multi-instruction sequence targets a
provably-constant frame slot -- exactly the deep, semantic, cross-instruction reasoning a text-based
peephole pass is the wrong tool for.

**The actual fix: pick the cheaper addressing strategy at the source, instead of cleaning up after the
expensive one.** Local-array storage is entirely this compiler's OWN decision, not a hardware fact:
declaring `int d[7];` reserves 7 CONSECUTIVE local slots starting at `d`'s own base slot (`CType.
SizeInWords` for an array is `ArrayLength * elementSize`, and every element type here is exactly one
word -- see `DeclareLocal`), the exact same slot numbering every scalar local already uses. So `d[K]`
for a compile-time-constant `K` is simply the local at slot `d`'s base slot `+ K` -- loadable/storable
with a single `ldl`/`stl`, no different from any other local, needing no runtime address computation,
no node-306 indirection, and no cached address slot at all.

New method `CCodeGenerator.TryGetConstantLocalArrayElementSlot(CIndexExpr, out int slot, out CType
elementType)` recognizes this narrow, provably-safe case: `index.Base` is a bare name referring to a
genuinely LOCAL array, and `index.Index` evaluates to a compile-time constant (via the same
`TryEvaluateConstantLong` the constant folder itself already shares) within `[0, ArrayLength)`. It
deliberately never fires for a PARAMETER (an array parameter has already decayed to a runtime pointer
value by the time it's a `CVarSymbol`, so there's no fixed per-element frame slot to exploit), a
GLOBAL/static local (addressed by a data-section label, not a frame offset), or anything more complex
than a bare name (a dereferenced pointer, another index, a function call). `ResolveLvalue`'s
`CIndexExpr` case tries this first, returning a plain `CLvalueKind.Local` lvalue directly when it
succeeds -- covering both reads and writes, since `EmitExpr`'s own `CIndexExpr` case and every
assignment/compound-assignment/increment-decrement helper all resolve an index expression's lvalue
through this one shared method already. `EmitAddressOfIndex` (used by `&arr[K]` and any other caller
that needs the element's actual runtime address) shares the same helper to compute the address in a
single `EmitLocalOrParameterAddress` call from the combined offset, instead of evaluating the array's
base address and the constant index separately and adding them at runtime. Anything the helper can't
prove safe at compile time falls through unchanged to the existing, general address-computation path.

Traced by hand: `void main() { int a = 3; int d[7]; d[0] = a; }` now compiles `d[0] = a;` to exactly
`ldl 0; push; pop; stl 1` (`a` at slot 0, `d`'s base at slot 1, so `d[0]` is slot 1 + 0) -- with
`CvmPeepholeOptimizer` enabled, its own `push`;`pop` rule collapses this further to precisely
`ldl 0; stl 1`, matching what Stefan asked for directly. Even with the peephole pass left off, this is
already far shorter than the roughly fifteen-instruction address-computation/cache/node-306-dereference
sequence the general path emitted before this fix. **Superseded by the very next fix below**: with
`gld`/`gst` now handling `a`'s own access directly (see "Global-scalar addressing" immediately below),
this exact trace no longer applies once `a` is `static` rather than an ordinary local -- see that
section's own hand-traced example for the current output of the motivating program.

**Unconditional, not a third toggle.** Unlike `CvmPeepholeOptimizer`'s two speculative forwarding
rules, this fast path relies on nothing beyond this compiler's own local-slot layout and the
already-hardware-confirmed `ldl`/`stl` addressing every other local already uses (see "`ldl`/`ldp`/
`stl`/`stp` go through register r, confirmed (2026-09-09)" above) -- there is no scenario where taking
it produces worse or less-correct code than the general path it replaces. So it ships unconditionally:
no new `CProjectMetadata` property, no new `CProjectWindow` checkbox, unlike the two optimizer passes
above.

**Scope, for a future round.** This only covers a LOCAL array indexed by a compile-time constant. A
GLOBAL array with a constant index (`g[2] = 5;` where `g` is a global) still goes through the general
address-computation path today -- folding a constant index into a global's own data-section label
would need the assembler to support a label-plus-offset literal expression, which hasn't been confirmed
or built; see "Known gaps" below rather than attempted speculatively.

## Global-scalar addressing: `gld`/`gst` replace the address-register dance (2026-09-10)

Stefan, looking at the generated assembly for his own motivating example rewritten with a `static`
global instead of a local (`static int a = 0; void main() { a = 3; int d[7]; d[0] = a; }`, the same
program that surfaced the linker's `KeyNotFoundException` -- see `cvm-toolchain-design.md`'s own
"Linker fix" section for that unrelated bug): "this is what is generated, but there are opcode to
access globals." He's right again, and for the same underlying reason as the local-array fix above --
this compiler was reaching for its most general, most expensive addressing mechanism for something the
CVM already has a direct instruction for.

**What was wrong.** Before this fix, every scalar global access -- whether a `static` local's own
storage or an ordinary module-scope global -- was treated exactly like dereferencing an arbitrary
runtime pointer: push the label (a `pushlit`, itself a relocation resolved once at link time), pop it
into r, cache that "address" into a freshly spilled local slot, then indirect through node 306's
`arst`/`lda` (read) or `arst`/`sta` (write) every single time the global was touched. For
`static int a = 0; ... a = 3; ... d[0] = a;`, that meant burning a whole extra local slot just to cache
`_a`'s own address (which never changes -- it's a link-time constant, not a runtime-computed value) and
running six-plus instructions where two would do.

**The fix.** CVM opcodes 75/76 (`CvmInstructionSet.LoadGlobalMnemonic`/`StoreGlobalMnemonic`, assembled
as `gld`/`gst`) act on a global directly by its own label, exactly like `ldl`/`stl` act on a local by its
own frame offset -- no address computation, no cache, no node-306 indirection at all. `CLvalue` gained a
new `Global` kind (carrying the label instead of a slot number); `ResolveLvalue`'s `CNameExpr` case now
returns a `Global` lvalue directly for any global/`static`-local scalar, with no `pushlit`/no
`CacheIndirectAddress` call at all, and `EmitLoad`/`EmitStore`/`EmitName` each just emit `gld`/`gst`
against that same label text. Because a global's label is already a fixed, compile-time-constant
reference the assembler/linker resolve via a relocation (the exact same relocation kind `pushlit` itself
gets), there is nothing to cache in the first place -- unlike the indirect-pointer case, which caches a
*runtime-computed* address specifically because `t`/the address register has no save/restore across
intervening code (see the ABI section above). A global's own address never changes, so every
`EmitLoad`/`EmitStore` call just re-emits `gld`/`gst` fresh against the label, correctly, with nothing to
invalidate.

**The naming trap this fix had to resolve first -- and got wrong once before getting it right.**
`gld`/`gst` were documented, in both `CvmInstructionSet.cs` and `Node508Program.cs`'s own remarks, as a
previously-flagged, unresolved "apparent naming/body cross-wire": tracing node 508's own F18 source,
`'gld` called a primitive (`g/@`) whose body ended in a REMOTE STORE, while `'gst` called a primitive
(`g/!`) whose body ended in a REMOTE FETCH -- backwards from ordinary CPU convention, where "load" means
"into a register." Getting this backwards would not just miss an optimization the way a wrong guess on
the local-array fix would have -- it would silently turn every compiled global write into whatever
`gld`'s real behavior is and vice versa, corrupting every program that touches a global. So this was
**confirmed with Stefan directly before being wired into the compiler**, rather than guessed from the
(ambiguous-sounding) name.

**First pass -- wrong.** Asked whether `gld`/`gst` behave as named or are swapped, Stefan's answer was
"gld loads a global from r" and "gst stores a global into r." This was read as confirming the backwards
direction was intentional -- `gld` performing `global := r` (a store, despite the name) and `gst`
performing `r := global` (a fetch, despite the name) -- and `CCodeGenerator.EmitGlobalFetch`/
`EmitGlobalAssign` were wired that way, with `CvmInstructionSet.cs`/`Node508Program.cs`/
`CvmAssemblyLanguage.cs`'s own flagged remarks updated to record what looked like a resolution.

**That reading was wrong, caught by an actual hardware/simulation run.** Stefan then ran a small test
program -- `lit 1; gst 2; gld 2; gst 4; nop` -- and read back the bus trace: `gst 2` (with r = 1 from the
preceding `lit 1`) produced a WRITE of `1` to global page 2, address 2; the following `gld 2` produced a
READ of that same address, returning the `1` just written. That is, `gst` genuinely STORES
(`global := r`) and `gld` genuinely LOADS (`r := global`) -- exactly what their names ordinarily mean,
no reversal at all. Stefan's own words afterward: "you were right: g/@ and g/! were swapped" -- this was
a REAL bug in node 508's F18 source (`g/@`'s and `g/!`'s own bodies really were swapped relative to
their names), not a naming-convention quirk, and the FIRST resolution above was simply wrong.

**Fixed by Stefan directly** (a new node 508 source, "here is the fixed node 508"): `g/@`'s and `g/!`'s
own bodies are swapped so each now ends in the remote op its name says it should, and `'gld`/`'gst` are
declared via `.loc` directly against `g/@`/`g/!` rather than as separate `g/next`-wrapped words.
`Node508Program.Source` was updated to this fixed text verbatim, per this project's own practice of
never second-guessing or "improving" a fix Stefan supplies himself. `CCodeGenerator.EmitGlobalFetch`/
`EmitGlobalAssign` were corrected the same day to their now hardware-confirmed direction:
`EmitGlobalFetch(label)` emits `gld {label}` (r := global) and `EmitGlobalAssign(label)` emits
`gst {label}` (global := r) -- ordinary "load"/"store" behavior, nothing confusing left to hide behind a
wrapper, though the wrapper methods stay so every call site still goes through one place. All three
files' flagged remarks were updated again to record the correction, not just the (wrong) first
resolution.

Traced by hand against Stefan's own rewritten example: `static int a = 0; void main() { a = 3; int
d[7]; d[0] = a; }` now compiles to

```
_a: .word 0
...
_main:
  enter 7
  pushlit 3
  pop
  gst _a
  gld _a
  push
  pop
  stl 0
```

-- `a = 3;` collapses to `pushlit 3; pop; gst _a` (three instructions, versus nine before this whole
feature: `pushlit _a; pop; stl 0; pushlit 3; ldl 0; pushlit 0; arst; pop; sta`), and `d[0] = a;` (still
using the local-array fix above for `d[0]`'s own side) becomes `gld _a; push; pop; stl 0` (four
instructions, versus six before). With `CvmPeepholeOptimizer` enabled, the `push; pop` pair in the
second sequence collapses too, leaving just `gld _a; stl 0`. `enter 7` (not `8`): with no address ever
cached for `a`, `d` claims local slot 0 for itself instead of slot 1, so the frame is one word smaller
as a direct side effect, on top of the instruction-count savings.

**Unconditional, not a fourth toggle** -- same reasoning as the local-array fix: a compile-time-known
global label can never need the general address-computation path, so there is no case where taking this
fast path produces worse or less-correct code. **Direction is now HARDWARE-CONFIRMED** (2026-09-10, via
the bus trace above) -- unlike most of this compiler's other open hardware questions, this one has an
actual run behind it, not just a derivation. What remains unconfirmed is narrower: node 508's global
load/store has not been exercised on a full physical-board program run beyond this isolated test, and
`CvmInstructionSet.LoadGlobalMnemonic`'s own remarks record the full before/after of the correction --
see "Known gaps" below.

## `typedef` support (2026-09-11)

Prompted by an actual build failure: Stefan is building a `libc` for the GA144 project, and its
`heap.c` (presumably `malloc`/`free`/`calloc`/`realloc`) `#include`s `stdlib.h`, which needs `size_t`
from `stddef.h` -- and `typedef` had been a hard "not yet supported" error since v1, so every one of
`stddef.h`'s `typedef` lines failed with `error: 'typedef' is not yet supported by this compiler`.

**Scope: an alias over this compiler's own existing type system, nothing more.** `typedef int T;`,
`typedef unsigned int *PT;`, `typedef char Buf[16];`, and one typedef re-using another
(`typedef size_t my_size_t;`) all work, at file scope or inside a function body. A typedef naming a
function type or a function-pointer type (`typedef int Fn(int);`, `typedef int (*Fn)(int);`) is
rejected with a specific diagnostic instead -- consistent with this compiler's existing, deliberate
"function pointers are not yet supported" scope limit, not a new one. `typedef` combined with
`static`/`extern`, or given an initializer, is likewise rejected rather than silently accepted.

**No AST node, no codegen change.** A typedef is resolved to a concrete `CType` (there is no "named
alias" `CTypeKind` -- `size_t` and `unsigned int` become the literal same `CType` value) the moment
`CParser` sees it, and substituted immediately everywhere the name is used afterward; `CCodeGenerator`
never sees a typedef name at all, only the type it already resolved to. This is why the feature is
entirely inside `CParser.cs` -- `CType.cs`, `CAstDecl.cs`, and `CCodeGenerator.cs` are untouched.

**How the parser tells a typedef name from an ordinary identifier**, the classic "lexer hack" C parsers
need: `CParser` keeps one `Dictionary<string, CType>` (`_typedefs`) of every typedef name seen so far.
`LooksLikeTypeStart()` (decides whether a block statement or top-level item is a declaration) and
`LooksLikeTypeAt()` (the cast/`sizeof(type)` lookahead) both consult it alongside the built-in type
keywords, and `ParseDeclarationSpecifiers()` itself falls back to a dictionary lookup once none of
`void`/`char`/`int`/`unsigned`/`signed` matched. `typedef` was removed from `UnsupportedTypeKeywords`
(it stays a reserved word -- still cannot be used as an ordinary identifier) and a new `isTypedef` flag
threaded through `ParseDeclarationSpecifiers`'s four call sites, rejected outright at the two (a
parameter declaration, an abstract type for a cast/`sizeof`) where a typedef declaration itself could
never appear.

**One deliberate scope simplification, not real C semantics: `_typedefs` is one flat table for the
whole file, not a stack of per-block scopes.** A typedef declared inside a function body is never
removed once that function's own block ends -- it stays visible for the rest of the file too. This is
looser than standard C block scoping, but harmless for every program this compiler is actually meant to
compile: typedefs overwhelmingly come from headers `#include`d at file scope (exactly `stddef.h`'s own
case), used by ordinary code below them, never redeclared block-locally with a conflicting meaning.
`CCompiler` already creates one fresh `CParser` (and so one fresh, empty `_typedefs`) per file compiled,
which is the real per-translation-unit boundary that matters -- only scoping *within* a single file is
simplified away. A name typedef'd twice to the SAME resolved type (e.g. a header included more than
once without its own include guard) is accepted silently; typedef'd twice to two DIFFERENT types is a
compiler error, not a silent last-one-wins.

**Not yet checked**: whether `stdlib.h` itself declares `size_t` as `unsigned long` rather than
`unsigned int` -- `long`/`short` remain unsupported (see "Supported C subset" above), unrelated to this
change, so a header using `long` anywhere will still need rewriting to use `unsigned int` regardless of
`typedef` now working.

## CLI (`gacc`)

```
gacc <input.c> [-o <output.casm>] [--pre <output.i>] [-I <dir>]... [-D <name>[=<value>]]...
```

Compiles one C source file straight through preprocessing, parsing, and code generation to CVM
assembly text. `--pre` additionally writes the reassembled preprocessed source (matching `gcpp`'s own
output, and a project's own `pre` directory convention). Turning `.casm` into an object file is a
separate `gaasm` invocation, exactly like every other stage of this toolchain. This remains useful on
its own as a standalone diagnostic tool for one file at a time; the IDE's own Build step (see "IDE
integration" above) calls `CCompiler`/`CvmAssembler`/`CvmLibrary` directly rather than shelling out to
this CLI, mirroring how the librarian CLI itself calls `CvmLibrary` directly rather than through a
subprocess.

## Known gaps, for a future revision

- No real type-checking/semantic-analysis pass separate from code generation -- a handful of invalid-C
  edge cases (e.g. `sizeof` of a void-returning call, using a pointer operand with `*`/`/`/`%`) are
  caught inline rather than by a dedicated pass, and a few very unusual ones may not be caught with a
  precise diagnostic at all. Ordinary, valid C is unaffected.
- The whole CVM C ABI v1 section above needs Stefan's confirmation (or correction) once CVM2's
  frame/interpreter nodes are simulated in full -- the risk is localized to a small, named set of
  codegen methods (`EmitBinaryOperation`'s pop+op pattern, unary op pop+op+push, `EmitFunction`'s
  prologue/epilogue, and the address caching in `ResolveLvalue`'s Indirect case). `ldl`/`ldp`/`stl`/
  `stp`'s own register-r-mediated behavior and `cbr`'s own branch-on-zero polarity are no longer part
  of this open risk -- see "`ldl`/`ldp`/`stl`/`stp` go through register r, confirmed (2026-09-09)" and
  "`ifbr` renamed to `cbr`: real bit pattern confirmed, branch polarity flipped (2026-09-09)" above --
  and `lal`/`lap` are no longer an open question either, since they're retired outright; the new
  `'fpush`-based address computation that replaced them (see "`tjmp` located, `slit` retired, `lal`/
  `lap` retired in favor of `'f`/`'fpush` (2026-09-09)" above) is itself not yet confirmed against real
  hardware, though, so it belongs on this list until a fresh run exercises it. `gld`/`gst`'s own DIRECTION
  is hardware-confirmed via an actual bus trace (see "Global-scalar addressing" above, including the real
  bug this caught and Stefan's own fix to node 508's source), but that trace was an isolated test, not a
  full program run -- it belongs on this list only in that narrower sense.
- `switch` is an if-chain, not a real `tjmp`, pending `tjmp`'s calling convention (its location is
  confirmed as of 2026-09-09 -- see "switch/case, and why not `tjmp`" above).
- **Local-slot cost of the `t`-register fix**: every indirect-lvalue occurrence (not every variable)
  permanently reserves its own frame slot, and nothing in this v1 ever reuses or frees a local slot.
  For an ordinary function this is noise; for one that touches many pointers/array elements, on
  GA144's very small per-node RAM, `enter <n>`'s frame could grow large enough to matter. A future
  revision could reuse a small fixed pool of scratch slots for indirect addresses that don't need to
  survive arbitrary intervening code (a plain, non-compound assignment's target, for instance) while
  still giving compound-assignment/increment-decrement targets (which DO need their address to survive
  across evaluating something else) their own slot -- not attempted here, since telling those two cases
  apart safely needs more analysis than this pass currently does, and getting it wrong would reintroduce
  the exact bug the current, simpler fix eliminates. **Narrowed as of 2026-09-10, twice**: a
  constant-indexed LOCAL array element no longer pays this cost at all (see "Local-array element
  addressing: constant-indexed access skips address computation entirely (2026-09-10)" above), and
  neither does a plain global-scalar access, which needs no cached slot at all rather than merely a
  cheaper one (see "Global-scalar addressing: `gld`/`gst` replace the address-register dance
  (2026-09-10)" above) -- this now applies only to pointers and non-constant-indexed array elements.
- The IDE's Build step (2026-09-07) only searches each DIRECTLY imported library's own `include`
  directory, not a library's further library references (transitive includes) -- see "IDE integration"
  above.
- A Program project's Build compiles and assembles but cannot link -- `galink` is still a stub, so no
  final `.gaimg` is produced for a Program project yet.
- The peephole optimizer's `stl N; ldl N` / `stp N; ldp N` forwarding rules (see "Optimizer: AST-level
  constant folding and a CVM peephole pass (2026-09-10)" above) share the exact same
  not-yet-hardware-confirmed "a store leaves r unchanged" assumption as `CCodeGenerator.EmitFunction`'s
  own ABI v2 prologue -- resolving one resolves both, and until then `enablePeepholeOptimization`
  should stay an explicit, informed opt-in rather than a default.
- Constant-indexed array addressing (see "Local-array element addressing..." above) covers LOCAL arrays
  only. A GLOBAL array with a compile-time-constant index still takes the general address-computation
  path -- extending the same idea to globals would need the assembler to support a label-plus-offset
  literal expression, not yet confirmed or implemented. (A GLOBAL *scalar*, unlike a global array
  element, is already covered directly -- see "Global-scalar addressing" above.)
- **Basic `float` support (2026-09-26) has several open items of its own** -- see `cvm-abi.md`'s "Float
  parameters and return values" section for the authoritative list, summarized here: the fr[0]-return-
  value convention and the bare fpush/fpop caller-save/restore round-trip are both compiler-side
  hypotheses, not confirmed against real hardware or a full node-305 simulation; the "uses fr registers,
  directly or indirectly" analysis is per-translation-unit only, conservatively assuming any externally
  declared function may use fr registers; the two-label (`name`/`name__hi`) float-global representation
  is an internal convention, not literal `label+1` address arithmetic a hand-written `.casm` file could
  rely on; and float conversions, comparisons, `++`/`--`, and pointer/array of `float` are all deferred
  to the future float-library round Stefan described.