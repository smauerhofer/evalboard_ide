# CVM assembler — current language and instruction-set description (as of 2026-09-11)

This is a snapshot reference, not a changelog: it describes the CVM assembly language and instruction
set as they stand today, pulling together the live (non-retired) state scattered across
`claude/cvm-toolchain-design.md` (the full design record, with its complete revision history) and
`claude/cvm-node306-307-address-registers.md` (node 306/307's own history, including today's
org-dependent register-count discovery). Where a bit pattern, tag, or field width is stated below, it
is copied verbatim from one of those two documents' own confirmed text, not re-derived here. Where
something is still open or unconfirmed, this document says so rather than guessing — see "Open
questions and flagged inconsistencies" at the end.

For the object/library/image file formats, the linker, and the full dated history of every change
summarized here, see `claude/cvm-toolchain-design.md` itself; this document only covers the assembly
language and its instruction set.

## The CVM word

A CVM instruction or data word is 16 bits (`Ga144.Cvm.Toolchain.CvmWordCodec.WordMask = 0xFFFF`). This
is independent of the F18 mesh's own 18-bit serial wire word — the CVM's own address space and every
opcode described below live entirely inside those 16 bits.

## Assembler mechanics

- Two-pass: pass 1 computes section layout (every mnemonic's word length is fixed by the mnemonic
  alone), pass 2 emits words and relocations.
- `.section CODE`/`.section DATA`, `.export <name>`, `.import <name>`, labels, and `.word <values>` work
  as in any simple assembler. A non-numeric operand must be a same-file label or an `.import`ed name —
  an unresolved bare name is a hard assemble error, never a silent external reference.
- The assembler (`gaasm`) never bakes in a live node's real numeric opcode for a tagged/node-resolved
  mnemonic — it emits a placeholder plus a relocation, resolved later by `galink` against a primitive
  table exported from a live compile of the CVM2 node mesh. The IDE's own CVM Debugger has a second,
  separate assembler (`Ga144.Evb.Ide.Services.CvmAssemblyLanguage`) that resolves the same mnemonics
  immediately, against whatever CVM2 project is currently open, rather than through a separate link
  step.

## Encoding shapes

Every mnemonic in `CvmInstructionSet.Instructions` uses one of five shapes:

| Shape | Word layout | Resolved | Examples |
|---|---|---|---|
| `None` (tagged, node-resolved) | fixed tag \| live node address, no assembled operand | at link/live-compile time | `nop`, `push`, `pop`, `ret`, ALU ops, `leave`, node 508's comparison ops |
| `TrailingWord` | opcode word, then a second word carrying the operand (literal or resolved label address) | operand word resolved same as `.word`/`pushlit` | `pushlit`, `lcall`, `ljmp`, `gld`, `gst`, `jump` |
| `EmbeddedAddress` | the whole word IS the target address (bit 15 clear) | plain `AbsoluteAddress` relocation, 15-bit range | `call` |
| `EmbeddedSignedValue` | fixed tag \| signed value, fully known from the literal alone, no relocation | assemble time | `br`, `cbr`, `lit` |
| `EmbeddedUnsignedValue` | fixed tag \| unsigned value, fully known from the literal alone, no relocation | assemble time | `enter`, `stl`, `stp`, `ldl`, `ldp`, `ldg`, `stg` |
| `NodeResolvedEmbeddedValue` | live node address \| an embedded per-instance operand (typically a register index) | BOTH a live compile (for the address) and the literal operand at assemble time | node 306's `arinc`/`ardec`/`arld`/`arst`/`lda`/`sta`; node 308's `dpop`/`dpush`/`dinc`/`ddec`/`dadd`/`dor`; node 511's `rld`/`rst`/`rpop`/`rpush` |

A `None`-shaped or `TrailingWord`-shaped mnemonic's placeholder word is `0x8000 | Id`, where `Id` is a
small, stable, append-only integer assigned once per mnemonic and never reused even after retirement —
so a raw, unlinked hex dump can already identify which instruction a word is meant to become, before any
relocation is applied.

## Assembly language, current form

```
; a line comment (// also works)
.section CODE
.export main
.import someExternal

main:
  nop
  pushlit 0x1234        ; literal, 0x-hex or decimal, must fit in 16 bits
  pushlit loop           ; or a label/import name
  pop
  push
  call loop               ; call a label/import, or a literal 0x0000-0x7FFF
  call 0x0100
loop:
  nop
  ret
  halt
  br -3                   ; signed offset, -0x400..0x3FF (11 bits); target = (this word's address + 1) + offset
  cbr 5                   ; signed offset, -0x200..0x1FF (10 bits); branches if r == 0
  lit -100                ; load a literal signed value into r, -0x100..0xFF (9 bits)

  ; node 507's ALU ops -- act on r and/or the data stack, no operand
  usl
  ssr
  usr
  add
  sub
  and
  xor
  or
  inv
  inc
  dec
  tjmp                    ; computed jump: pop a signed offset off the data stack, add to the PC
  jump                    ; fetch a trailing word, load it into the PC (TrailingWord, 2 words)
  xs                      ; exchange s and r
  xp                      ; exchange p and r

  ; node 506's frame-pointer family (unsigned value, no live node compile needed for the operand)
  enter 3                 ; enter stack frame, reserve locals
  stl 0                   ; store to local at frame-relative offset 0
  stp 1                   ; store to parameter at frame-relative offset 1
  ldl 0                   ; load local at frame-relative offset 0
  ldp 1                   ; load parameter at frame-relative offset 1
  fpush                   ; push frame pointer f onto the data stack
  f                       ; move f into register r
  leave                   ; node 506's own 'leave

  ; node 508's comparison/arithmetic family -- no operand, node-resolved
  eq
  eq0
  false
  true
  ne
  ne0
  gt
  gt0
  ge
  ge0
  le
  le0
  lt
  lt0
  mul2
  udiv2
  div2
  abs
  bitcnt
  ugt                     ; unsigned comparisons -- node 408, not node 508
  ule
  ult
  uge
  neg                     ; unary minus -- node 509

  ; node 508's global-scalar access
  gld <label>             ; TrailingWord: r := global (fetch)
  gst <label>             ; TrailingWord: global := r (store)
  ldg 100                 ; EmbeddedUnsignedValue, 9-bit unsigned offset (0-511): r := global
  stg 100                 ; EmbeddedUnsignedValue, 9-bit unsigned offset (0-511): global := r

  ; node 405's carry/multiword-arithmetic family
  tgc
  adc
  ldc
  sec
  clc
  stc
  sbc
  rol
  ror

  ; node 308's 32-bit arithmetic register family (register operand embedded in the opcode)
  dpop 0
  dpush 0
  dinc 0
  ddec 0
  dadd 0
  dor 0

  ; node 511's register file (register operand embedded in the opcode)
  rld 0
  rst 0
  rpop 0
  rpush 0

  ; node 306's address-register family (register operand embedded in the opcode -- see its own
  ; section below for the register-count caveat)
  arinc 0
  ardec 0
  arld 0
  arst 0
  lda 0
  sta 0

  ; node 510's extended-precision family
  addc
  xst
  xld
  xmul2
  xdiv2
  xumul

.section DATA
table: .word 1, 2, 3
```

Not shown above (naming collision still open, see the end of this document): node 505's own `'fx`.

## Branch target addressing (confirmed against real hardware)

A `br`/`cbr` word's target address is `(this word's own address + 1) + offset` — relative to the word
immediately *after* the branch's own opcode word, not the branch word's own address. `cbr` branches
when r == 0 (confirmed 2026-09-09; this is the OPPOSITE of an earlier, unconfirmed hypothesis the C
compiler briefly carried).

## `call`, `br`, `cbr`, `lit`: the three fully self-contained shapes

- **`call`**: the whole word IS the target address (`CallAddressMask = 0x7FFF`, bit 15 clear). A
  literal target above `0x7FFF` is a hard assemble error. **Known gap, not yet fixed**: a `call` to a
  label/import whose final address is only resolved at link time is not yet checked against this
  15-bit limit — see `cvm-toolchain-design.md`'s own "Known, deliberate gap" note.
- **`br`**: tag `0x8000` (`BranchTag`), occupying `0x8000-0x87FF`; offset is signed 11-bit
  (`-0x400..0x3FF`).
- **`cbr`**: tag `0xAC00` (`ConditionalBranchTag`, mask `0xFC00`), occupying `0xAC00-0xAFFF`; offset is
  signed 10-bit (`-0x200..0x1FF`).
- **`lit`**: tag `0xB200` (`LitTag`, mask `0xFE00`), occupying `0xB200-0xB3FF`; value is signed 9-bit
  (`-0x100..0xFF`). This is node 509's own opcode; `slit` (the earlier, wider 12-bit form) is retired
  and no longer assembles at all.

## Node 507: core dispatch and ALU

Node 507 hosts the CVM's core stack/control dispatch (`nop`/`pushlit`/`push`/`pop`/`ret`/`halt`, plus
`tjmp`/`jump`/`xs`/`xp`), all sharing one "local execute" tag family (`0x8800 | address`), resolved
against a live compile — no assembled operand of their own (`pushlit`/`jump` carry their own operand as
a trailing word instead). Node 507 also hosts the ALU:

- Binary ops (`usl`/`ssr`/`usr`/`add`/`sub`/`and`/`xor`/`or`): tag `0xC800 | address`, range
  `0xC800-0xCFFF`.
- Unary ops (`inv`/`inc`/`dec`): tag `0xC000 | address`, range `0xC000-0xC7FF`.

`tjmp` is a genuine computed jump (pops a signed offset off the data stack, adds it to the PC) — its
own calling convention (case-table layout for `switch`/`case`) is still unconfirmed, so the C compiler
does not use it yet.

## Node 506: frame pointer and locals

Node 506 currently hosts the confirmed frame-pointer/local-variable family:

| Mnemonic | Tag | Shape |
|---|---|---|
| `enter` | `0x9200` | `EmbeddedUnsignedValue` |
| `stl` | `0x9A00` | `EmbeddedUnsignedValue` |
| `stp` | `0x9800` | `EmbeddedUnsignedValue` |
| `ldl` | `0x9E00` | `EmbeddedUnsignedValue` |
| `ldp` | `0x9C00` | `EmbeddedUnsignedValue` |
| `f` | (node-resolved, `leave`'s own tag family) | `None` |
| `fpush` | (node-resolved, `leave`'s own tag family) | `None` |
| `leave` | (node-resolved) | `None` |

A local's own address is `f - offset`; a parameter's own address is `f + offset` — computed today with
`fpush` followed by ordinary `pushlit`/`pop`/`add`/`sub`, since the earlier `lal`/`lap` (load address of
local/parameter) mnemonics are retired.

## Node 508: comparisons, arithmetic, and global access

Node 508 currently hosts (tag family `0xE800 | address`, `None`-shaped, node-resolved): `eq`, `eq0`,
`false`, `true`, `ne`, `ne0`, `gt`, `gt0`, `ge`, `ge0`, `le`, `le0`, `lt`, `lt0`, `mul2`, `udiv2`,
`div2`, `abs`, `bitcnt`. (`ugt`/`ule`/`ult`/`uge` were moved off node 508 onto node 408's own live
compile; `negate`/`xt`/`ldt`/`stt` were deleted outright in the 2026-09-09 CVM1-opcode purge, replaced
by node 509's `neg` for unary minus and node 306's `arst`/`lda`/`sta` for pointer dereference.)

Global-scalar access, direction hardware-confirmed 2026-09-10 via a real bus trace (`gst` STORES,
`gld` LOADS — ordinary convention):

- `gld <label>` / `gst <label>`: `TrailingWord`, resolved against a live compile, any address.
- `ldg <offset>` / `stg <offset>`: `EmbeddedUnsignedValue`, 9-bit unsigned offset (0-511), tags
  `0xA800` (`ldg`, mask `0xFE00`) and `0xAA00` (`stg`, mask `0xFE00`) — a shorter, fixed-tag alternative
  to `gld`/`gst` for a small enough offset. Not yet confirmed on real hardware as of this writing (the
  bus trace confirmed `gld`/`gst`'s direction, not `ldg`/`stg` specifically).

## Node 306: address registers (org-dependent count — revised 2026-09-11)

Node 306 holds a bank of 32-bit address registers, each a word pair (address word, page word), letting
a loaded register address any word across the whole GA144 memory space rather than just node 306's own
local RAM.

**The register count is NOT a fixed hardware constant.** As of Stefan's 2026-09-11 clarification,
capacity depends on where node 306's own code starts (`# <hex> org`): register storage occupies word
addresses `[0, org)` below the code, and each register is a word pair, so capacity is `floor(org / 2)`,
capped at 8 by `ar/main`'s own 3-bit register-select field (`dup 0x07 and`). Earlier documentation in
this project stating a fixed count of "four" or "six" was describing whatever `org` happened to be in
effect at the time it was written, not a hardware limit.

Six ops, all `NodeResolvedEmbeddedValue`-shaped (a live-compiled function address OR'd with an embedded
register-index operand), sharing tag `0xD800` (`0xD800-0xDBFF`):

| Mnemonic | Direction (confirmed 2026-09-11) |
|---|---|
| `arinc <reg>` | increment the register |
| `ardec <reg>` | decrement the register |
| `arld <reg>` | SETS the register: register := (address = r, page = top of stack) |
| `arst <reg>` | READS the register back out: r := register's address, stack top := register's page |
| `lda <reg>` | dereferences THROUGH the register: r := memory[register's address] |
| `sta <reg>` | dereferences THROUGH the register: memory[register's address] := r |

`arld`/`arst` act on the register itself; `lda`/`sta` use it as a pointer into memory — two different
pairs of operations despite the similar naming.

**Open, unresolved as of today**: the pasted source's own comments still disagree with the org-based
formula for the `org = 0x0e` case currently in the reference copy (header says "6", opcode table says
"0..5", the formula says "7") — see "Open questions" below. Separately, the assembler's own
register-operand range check (`Node306RegisterFieldBitMask = 0x0007`) still unconditionally accepts any
operand 0-7 regardless of the node's current `org`, so an operand valid for the 3-bit field but too
large for the current `org` would assemble without error and then alias into node 306's own live code
at runtime — flagged, not yet addressed.

## Node 308: 32-bit arithmetic registers

Four 32-bit registers on node 308 (imported by node 307), `NodeResolvedEmbeddedValue`-shaped with a
2-bit register index at bits 1-0 and a 6-bit function field at bits 7-2, no base-address bias (a
different field layout from node 306's or node 511's own): `dpop`, `dpush`, `dinc`, `ddec`, `dadd`,
`dor`.

## Node 405: carry/multiword arithmetic

Reached from node 406's `1110_1???` relay branch; holds a single carry bit. Nine `None`-shaped,
node-resolved ops: `tgc`, `adc`, `ldc`, `sec`, `clc`, `stc`, `sbc`, `rol`, `ror`.

## Node 505: frame2

Fills node 506's own `1001_01??` relay branch. Only `'fx` is currently wired as a CVM mnemonic — see
"Open questions" below for why node 505's own `'f` is deliberately NOT wired.

## Node 510: extended precision

`addc` (repointed here from node 506's now-deleted old register-d family) plus `xst`, `xld`, `xmul2`,
`xdiv2`, `xumul` — all `None`-shaped, node-resolved.

## Node 511: register file

`rld`, `rst`, `rpop`, `rpush` — `NodeResolvedEmbeddedValue`-shaped, 5-bit register index / 5-bit
function field with a `-0x20` bias, `# 0x20 org`.

## Retired mnemonics (no longer assemble, kept only as permanently-unused Ids)

`slit` (replaced by `lit`), `lal`/`lap` (replaced by `fpush` + offset arithmetic), `adjust` (deleted
outright, "no longer exists"), node 508's old `negate`/`xt`/`ldt`/`stt`, node 506's old register-d
family except `addc` (`zext`/`ldd`/`std`/`xd`/`mul2d`/`div2d`/`sext`/`umuld`), node 306's old
self-describing `ldar`/`star`/`inca`/`deca` (and node 306's old, differently-tagged `lda`/`sta`), and
node 407's entire register-w/port family (`xpt`/`out`/`in`/`ldhi`/`ldlo`/`sthi`/`stlo`, deleted
outright alongside the four above in the 2026-09-09 "CVM1-opcode purge"). None of these mnemonics
should be reintroduced under a different meaning — their Ids are permanently retired.

## Open questions and flagged inconsistencies (not resolved, not guessed at)

1. **Node 306's register-count comments still disagree with its own org-based formula.** For the
   reference copy's current `org = 0x0e` (14), `floor(14/2) = 7`, but the pasted header still says "6"
   and the opcode table still says "0..5". Needs Stefan to say which figure is authoritative.
2. **Node 306's assembler-side register-operand range check is static, not org-aware.** An operand
   in-range for the 3-bit field but too large for the node's current `org` assembles without error
   today. Whether to make this check dynamic (derived from a live compile) is undecided.
3. **Node 505's own `'f` vs. node 506's `'f`.** Node 505's trailing comment names two different
   behaviors ("store" and "exchange") both attached to the name `'f`, while its second word definition
   is actually named `'fx`. Only `'fx` is wired; node 505's own `'f` is left unwired pending
   confirmation.
4. **Node 509's `lit` sign-extension branch looks like dead code.** Its body tests bit 9 of a value
   already masked to 9 bits, so that branch can never fire — likely a leftover from a wider, prior
   revision of the same word. Not changed pending confirmation.
5. **Node 307's `k/main` final dispatch branch has no visible closing code or `;`** in the source as
   currently pasted. Stefan has confirmed directly, twice, that node 307 compiles fine in his own live
   project — this is noted here only because the reference copy reproduces the source verbatim,
   unresolved-looking ending included.
6. **`call` to an unresolved cross-file label is not checked against the 15-bit address limit** at link
   time — only a literal `call` target is checked at assemble time.
7. **`ldg`/`stg`'s direction is inferred from `gld`/`gst`'s own hardware-confirmed direction**, not
   independently confirmed by their own hardware run.
