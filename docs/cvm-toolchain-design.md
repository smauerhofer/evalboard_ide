# CVM toolchain: assembler, librarian, linker

This is the design for a real toolchain around the CVM assembly language (`nop`, `pushlit <data>`,
`push`, `pop`, `call <address>`, `ret`, `br <offset>`, `ifbr <offset>`, `slit <value>`, and whatever
gets added later), sized for being fed by a future C compiler backend as
well as by hand-written source. It lives outside the IDE project so it can run as ordinary
command-line tools, while still sharing its core logic (the instruction set table, the file formats)
with the IDE through one common library, so the two can never quietly drift apart on what the CVM
even is.

## Status

| Piece | State |
|---|---|
| CVM word width, instruction set table | Done (`Ga144.Cvm.Toolchain`) |
| Object file format (.gaobj) | Done |
| Assembler (labels, `.section`/`.export`/`.import`/`.word`) | Done (`gaasm`) |
| Library/archive format (.galib) + librarian | Done (`galib`) |
| Linker, primitive-table export from the IDE, image format (.gaimg) | Not started (`galink` is a stub) |
| CVM Debugger loading a `.gaimg` instead of the fixed test program | Not started |

## Project layout

```
src/
  Ga144.Cvm.Toolchain/     class library -- shared by everything below AND by the IDE
  Ga144.Cvm.Assembler/     gaasm.exe  -- source (.casm) -> object (.gaobj)
  Ga144.Cvm.Librarian/     galib.exe  -- object(s) (.gaobj) -> archive (.galib)
  Ga144.Cvm.Linker/        galink.exe -- object(s)/archive(s) -> image (.gaimg)  [stub]
  Ga144.Evb.Ide/           references Ga144.Cvm.Toolchain for the shared instruction-set table
```

All four are plain `net10.0` projects (no WPF, no third-party packages), so they build and run
anywhere .NET 10 does, independent of the IDE's own Windows/WPF requirements.

## The CVM word is 16 bits

A CVM word -- an opcode or a data value living at an address in the CVM's own memory -- is 16 bits
wide. This is a different thing from an F18 *wire* word: the serial link between the host and the
CVM moves 18-bit values 3 bytes at a time, because that's the physical F18 hardware's own native word
width, but the payload it carries for the CVM's own address space never uses more than 16 of those
bits. `Ga144.Cvm.Toolchain.CvmWordCodec` is the one place this is pinned down (`WordMask = 0xFFFF`,
2 bytes per word); everything in the toolchain and the IDE's own CVM-domain code (the simulated SRAM,
the test-program builder, the assembler/disassembler) now masks against it instead of against the
F18-native 18-bit mask.

## Assembly language

```
; a line comment (// also works)
.section CODE            ; switches which section subsequent lines assemble into (default: CODE)
.export main             ; makes a label below visible to other object files/the linker
.import someExternal     ; declares a name this file references but does not define

main:                     ; a label -- may share a line with an instruction, or stand alone
  nop
  pushlit 0x1234          ; a literal operand (0x-hex or plain decimal, must fit in 16 bits)
  pushlit loop            ; or a label/import name -- assembles to that symbol's final address
  pop
  push
  call loop               ; call a label/import -- resolves to that symbol's own address
  call 0x0100             ; or a literal address, 0x0000-0x7FFF only (bit 15 is reserved)
loop:
  nop
  ret                     ; return -- pops the address a call pushed and jumps back to it
  br -3                   ; branch by a literal signed offset, -0x400..0x3FF (11 bits)
  ifbr 5                  ; conditional branch -- same offset shape, a different tag and width
  slit -100               ; load a literal signed value into R, -0x800..0x7FF (12 bits)
  usl                     ; node 507's ALU ops -- no operand, all act on r and/or the data stack
  ssr                     ; ssr/usr/usl: signed/unsigned shift right, unsigned shift left
  usr
  add                     ; add/sub/and/xor/or: binary -- r combined with the top of the data stack
  sub
  and
  xor
  or
  inv                     ; inv/inc/dec: unary -- r alone
  inc
  dec
  enter 3                 ; node 606's frame-pointer ops -- unsigned value, 0x00..0xFF, no node needed
  adjust 2                ; adjust stack frame by an unsigned amount
  stl 0                   ; store to local at frame-relative offset 0
  stp 1                   ; store to parameter at frame-relative offset 1
  ldl 0                   ; load local at frame-relative offset 0
  ldp 1                   ; load parameter at frame-relative offset 1
  lal 5                   ; load address of local at frame-relative offset 5
  lap 0                   ; load address of parameter at frame-relative offset 0
  leave                   ; node 606's own 'leave -- tagged/node-resolved, like nop/push/pop/ret
  eq                      ; node 508's comparison/arithmetic ops -- also tagged/node-resolved,
  ne0                     ; no operand, none self-describing (unlike node 606's other eight):
  ugt                     ; eq, eq0, false, true, ne, ne0, ugt, gt, gt0, ge, ge0, ule, le, le0,
  mul2                    ; lt, lt0, ult, uge, mul2, udiv2, div2, abs, negate, xt, ldt, stt, bitcnt
  bitcnt

.section DATA
table: .word 1, 2, 3      ; raw data words -- each may also be numeric or a label/import name
```

All fifty-six built-in instructions are always available without `.import` -- the assembler never
bakes in a numeric opcode for them (it has no notion of any node's F18 source at all); every
instruction word is emitted as a placeholder with a relocation against an external symbol named after
the mnemonic (`nop`/`pushlit`/`push`/`pop`/`ret`, node 507's eleven ALU ops, node 606's `leave`,
and node 508's 27 comparison/arithmetic ops), against the callee itself (`call`), or -- for
`br`/`ifbr`/`slit` and node 606's other eight frame-pointer ops -- with no relocation at all, since
their whole word is already fully known once their literal operand is (see below), for the linker to
resolve later. Every non-numeric operand (a
`pushlit`, `call`, or `.word` operand that isn't a literal) must be either a label defined in the same
file or a name declared with `.import` -- an operand that's neither is a hard assemble error, not a
silent external. `br`/`ifbr`/`slit`/node 606's eight self-describing ops are stricter still: their
operand must be a literal value today -- a label or import name is a hard assemble error ("does not
(yet) support a label/import operand"). For `br`/`ifbr` this was originally deferred because what the
offset is relative to (the branch's own address vs. the next instruction's) was an open question that
didn't need answering to support the literal case correctly -- it's since been confirmed against real
hardware (see "Branch target addressing, confirmed" below) to be the next instruction's address, so a
label operand is now a known, mechanical computation; it just hasn't been wired into the assembler
yet. `slit` never had that question to begin with -- its value isn't an address computation at all
(see "slit: a plain signed literal" below), so a label/import operand there wouldn't mean anything
regardless of whether the assembler supported one. Node 606's eight self-describing ops are
frame-relative offsets/counts, not addresses either, for the same reason. `leave` takes no operand at
all -- it's shaped like `nop`/`push`/`pop`/`ret`, not like its eight self-describing siblings.

**The placeholder word itself is `0x8000 | Id`, not a bare `0` -- except for `call`/`br`/`ifbr`/
`slit`/node 606's eight self-describing ops.** Every entry in `CvmInstructionSet.Instructions` carries
a small, stable, append-only numeric `Id` (`nop`=0, `pushlit`=1, `push`=2, `pop`=3, `call`=4, `ret`=5,
`br`=6, `ifbr`=7, `slit`=8, `usl`=9, `ssr`=10, `usr`=11, `add`=12, `sub`=13, `and`=14, `xor`=15,
`or`=16, `inv`=17, `inc`=18, `dec`=19, `enter`=20, `adjust`=21, `stl`=22, `stp`=23, `ldl`=24,
`ldp`=25, `lal`=26, `lap`=27, `leave`=28, `eq`=29, `eq0`=30, `false`=31, `true`=32, `ne`=33,
`ne0`=34, `ugt`=35, `gt`=36, `gt0`=37, `ge`=38, `ge0`=39, `ule`=40, `le`=41, `le0`=42, `lt`=43,
`lt0`=44, `ult`=45, `uge`=46, `mul2`=47, `udiv2`=48, `div2`=49, `abs`=50, `negate`=51, `xt`=52,
`ldt`=53, `stt`=54, `bitcnt`=55 today), assigned once and never renumbered or reused, because
it gets baked into the raw words of every `.gaobj` ever produced. Before the linker resolves the
`CvmRelocationType.CvmOpcode` relocation sitting on that word, the word already says which
instruction it's meant to become -- so a tool that dumps an unlinked object file's raw section words
(or a person reading a hex dump) can identify every instruction without cross-referencing the
relocation table, and the relocation's `SymbolName` (the mnemonic) remains the actual authoritative
key the linker resolves by. This id-based encoding is also why the placeholder needed to become more
than a bare `0` in the first place: it was chosen once it became clear the CVM's primitives are not
all compiled into one node (see "Planned: linker and image" below) -- `0x8000 | Id` is a stable,
portable stand-in that doesn't presume any particular node or address layout, unlike the original
single-node convention of `0x8000 | address`. A `.word`/`pushlit` operand that refers to a label or
`.import`ed name (an `AbsoluteAddress` relocation) still gets a plain `0` placeholder -- only
instruction-opcode words (`CvmOpcode` relocations) use the `0x8000 | Id` scheme, since only those have
a tag to decode `Id` back out of.

`call`, `br`, `ifbr`, `slit`, and node 606's eight self-describing ops don't fit that scheme at all,
by design: none of them has a tag word to place an `Id` into, because each one's single opcode word is
instead fully determined by its own operand (`CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress`/
`EmbeddedSignedValue`/`EmbeddedUnsignedValue`, as opposed to the tagged mnemonics' `None`/
`TrailingWord`). `leave` is a `None`-shaped tagged mnemonic like `nop`/`push`/`pop`/`ret`, so it DOES
get the `0x8000 | Id` placeholder scheme -- see "Node 606's own tagged mnemonic, `leave`, confirmed"
below for why it's shaped this way rather than self-describing like its eight siblings.

`call`'s single word directly IS the target address. Its placeholder is therefore a plain `0`,
resolved via an ordinary `AbsoluteAddress` relocation against its operand -- the same relocation
kind (and the same resolution logic) a `.word` or `pushlit` label/import operand would get, just
restricted to 15 bits (`CvmInstructionSet.CallAddressMask = 0x7FFF`) so bit 15 stays clear once
linked. That's the whole point of the encoding: a linked program's interpreter can tell "this word
is a call to the address it contains" (bit 15 clear) apart from "this word is a tagged instruction
dispatch" (bit 15 set) using nothing but that one bit, regardless of which node(s) end up
implementing the tagged side. A literal `call` target out of that 15-bit range (`call 0x8000` or
higher) is a hard assemble error, not silent truncation -- ambiguity with the tag bit would
otherwise corrupt a linked program in a way nothing downstream could detect.

`br`/`ifbr`'s single word is a fixed 5-bit tag (`CvmInstructionSet.BranchTag = 0x9000` for `br`,
`ConditionalBranchTag = 0x9800` for `ifbr` -- bits 15-11, so both stay clearly outside `call`'s
0x0000-0x7FFF range and outside the tagged mnemonics' `0x8000 | Id` range) OR'd with the literal
operand's value sign-truncated into the low 11 bits (`BranchOffsetBitMask = 0x7FF`). Unlike `call`,
this needs no relocation at all: nothing about the word depends on where anything else in the
program ends up, so the assembler computes the final word outright, the moment it has the literal.
An offset outside the signed 11-bit range (`BranchOffsetMinValue = -0x400` to
`BranchOffsetMaxValue = 0x3FF`) is a hard assemble error, not silent truncation, same rationale as
`call`'s range check.

**Node 507's ALU ops (`usl`/`ssr`/`usr`/`add`/`sub`/`and`/`xor`/`or`/`inv`/`inc`/`dec`).** These are
tagged-dispatch mnemonics shaped exactly like `nop`/`pushlit`/`push`/`pop`/`ret` (`CvmOperandEncoding.None`,
a single bare `0x8000 | Id` placeholder word, resolved later by the linker) -- none of them takes an
assembled operand, because the values they act on already live in the CVM interpreter's own register r
and/or on the CVM data stack by the time the opcode runs, not in the instruction word. Per Stefan's node
507 source, eight are binary (r and the top of the data stack: `usl` unsigned shift left, `ssr` signed
shift right, `usr` unsigned shift right, `add`, `sub`, `and`, `xor`, `or`) and three are unary (r alone:
`inv` bitwise invert, `inc` increment, `dec` decrement) -- but the assembler makes no distinction between
the two arities at all, since neither shape has an operand to validate; the difference is purely in what
node 507's own compiled words do once dispatched, not in anything `gaasm` emits. (`gaasm`'s own
placeholder stays the uniform `0x8000 | Id` regardless of arity -- it is the IDE-side resolver,
`Ga144.Evb.Ide.Services.CvmAssemblyLanguage`, that must know the real per-arity tag; see "Planned:
linker and image" below for what that tag actually is, now that it's confirmed.)

**`slit`: a plain signed literal, not an address.** `slit`'s word is the same shape as `br`/`ifbr`
-- a fixed tag OR'd with a signed value in the low bits -- but with a *narrower* tag and a *wider*
value field: `CvmInstructionSet.SlitTag = 0xD000` occupies only the top 4 bits (bits 15-12), leaving
12 bits (`SlitValueBitMask = 0xFFF`) for the value, giving `slit` a wider range than `br`/`ifbr`
(`SlitValueMinValue = -0x800` to `SlitValueMaxValue = 0x7FF`, i.e. -2048..2047) at the cost of only
8 possible tags in that nibble instead of `br`/`ifbr`'s 32. Semantically `slit` isn't an address
computation at all -- per Stefan, executing it loads its signed value directly into the F18
interpreter's own R register, node 607's own runtime behavior and no concern of this toolchain's.
Because `br`/`ifbr` and `slit` share the identical "tag + signed field, no relocation" shape despite
their different bit widths, `CvmInstructionShape` carries a `ValueBitMask` field (in addition to
`Tag`) so one assembler method (`EmitEmbeddedSignedValue`) and one decoder
(`TryDescribeSelfDecodingWord`) serve all three mnemonics -- and any future one shaped the same way
-- without a per-mnemonic branch: the value's range and bit width are always derived from
`ValueBitMask` alone (`maxValue = mask >> 1`, `minValue = -(maxValue + 1)`), so adding a fourth
"tag + signed value" opcode someday is exactly the one-line `CvmInstructionSet.Instructions` entry
this doc's earlier "adding a new CVM opcode" remarks promise, nothing more.

**Node 606's eight self-describing frame-pointer ops (`enter`/`adjust`/`stl`/`stp`/`ldl`/`ldp`/`lal`/
`lap`): the same shape again, but unsigned.** Per Stefan's own bit-pattern table, each of these is a
fixed 8-bit tag (bits 15-8) OR'd with an UNSIGNED 8-bit value (bits 7-0, 0x00-0xFF) -- self-describing
and relocation-free exactly like `br`/`ifbr`/`slit`, just with an unsigned rather than signed value
field and a wider (8-bit, not 4/5-bit) tag. Because the value is never negative, `CvmOperandEncoding`
gained a sibling member, `EmbeddedUnsignedValue`, alongside `EmbeddedSignedValue` -- same
`Tag`/`ValueBitMask` shape on `CvmInstructionShape`, but `EmitEmbeddedUnsignedValue`/
`TryDescribeSelfDecodingWord`'s node-606 branch check `0 <= value <= ValueBitMask` instead of
splitting into a signed min/max. See "Node 606's own opcode range, confirmed" below for the actual
tag values.

**Node 606's own tagged mnemonic, `leave`, confirmed.** Not every node-606 mnemonic is
self-describing: `leave` (added after the eight above) belongs to a DIFFERENT row of Stefan's table --
"1010 0xxx xxxx xxxx | call word in node 606, address in opcode" -- which is shaped exactly like node
607's own `nop`/`pushlit`/`push`/`pop`/`ret` family: a single bare tagged word
(`CvmOperandEncoding.None`) whose real value depends on wherever `'leave` ends up in node 606's own
compiled RAM, resolved only against a live compile, never self-describing from a literal alone. See
"Node 606's own opcode range, confirmed" below for the tag itself and why the mask, in practice, is
narrower than the table's own bit diagram suggests.

**Node 508's 27 comparison/arithmetic ops, confirmed.** All 27 of node 508's tick-prefixed words
(`eq`/`eq0`/`false`/`true`/`ne`/`ne0`/`ugt`/`gt`/`gt0`/`ge`/`ge0`/`ule`/`le`/`le0`/`lt`/`lt0`/`ult`/
`uge`/`mul2`/`udiv2`/`div2`/`abs`/`negate`/`xt`/`ldt`/`stt`/`bitcnt`) are shaped exactly like node
606's `leave`, not like node 507's ALU ops or node 606's eight frame-pointer ops: a single bare tagged
word (`CvmOperandEncoding.None`), no assembled operand, resolved only against a live compile of
`Node508.f18`, never self-describing from a literal alone. Unlike node 606 (which masks its relayed
dispatch byte to 8 bits) or node 507 (which further splits its block by bits 13-11 into unary/binary
ALU and three other sub-blocks), node 508's own `main` performs no subdivision at all -- it jumps
directly to whatever unmasked address arrives over the port -- so all 27 ops share one single flat
tag rather than needing separate tags per sub-family. See "Node 508's own opcode range, confirmed"
below for the tag and the full mnemonic-to-address table.

**Branch target addressing, confirmed against real hardware.** A `br <offset>` (or `ifbr`) word's
target address is `(this word's own address + 1) + offset` -- relative to the address of the word
immediately *after* the branch's own opcode word, not relative to the branch word's own address.
This was confirmed by placing a `br 1` at address 2 of the CVM Debugger's test program (right where
its `call`/`ret` round trip resumes) and watching real hardware fetch address 4 next, skipping
address 3 entirely: `2 + 1 + 1 = 4`, matching the encoded offset of `1` exactly. Nothing in the
toolchain computes this yet (`br`/`ifbr` only take a literal offset today, per the previous
paragraph), but it's the fact a future label operand needs: to branch to a label, the assembler
would need to compute `offset = targetLabelOffset - (thisInstructionOffset + 1)`, which pass 2
already has every ingredient for (a label's final offset, same as any other operand kind) except
threading this instruction's own offset through to the point that emits its word.

This is a two-pass assembler: pass 1 walks the source purely to compute section layout (every
instruction's word length is fixed by its mnemonic alone, so no label's offset ever depends on any
operand's value) and catches syntax errors; pass 2 walks the same source again to actually emit words
and relocations, by which point forward references (like `pushlit loop` above, referring to a label
declared further down) already have a known offset.

## File format: GAFF (the shared container)

`.gaobj`, `.galib`, and `.gaimg` all share one on-disk envelope, `GaffDocument`:

```
offset 0   4 bytes   magic "GAFF"
offset 4   2 bytes   format version (currently 1)
offset 6   2 bytes   file kind: 1 = Object, 2 = Library, 3 = Image
offset 8   ...       a flat sequence of chunks, each:
                        4 bytes   tag (4 ASCII characters, e.g. "SECT")
                        4 bytes   payload length
                        N bytes   payload
```

RIFF-style on purpose: a reader that doesn't recognize a chunk's tag can always skip it using its
length, so a new chunk kind (a future debug/line-number chunk, say) can be added later without
breaking a tool built against an earlier version of a format.

## Object file (.gaobj)

Chunks, in the order `gaasm` writes them (a reader may encounter them in any order):

- **STRT** -- one NUL-terminated, UTF-8 string per entry, concatenated; every name elsewhere in the
  file is a byte offset into this blob.
- **SECT** -- one entry per section: `{ nameOffset: uint32, wordCount: uint32, words: wordCount * 2
  bytes }`. Only `CODE` and `DATA` exist today; any name is accepted, and the linker decides where
  each section's words ultimately land in the CVM's flat page/address space.
- **SYMT** -- one entry per symbol: `{ nameOffset: uint32, binding: byte (0=Local, 1=Global,
  2=External), sectionIndex: int32 (-1 for External), value: int32 (word offset within that
  section) }`.
- **RELO** -- one entry per relocation: `{ sectionIndex: int32, wordOffset: int32, symbolIndex:
  int32, type: byte (0 = AbsoluteAddress, 1 = CvmOpcode) }`. `AbsoluteAddress` writes the symbol's
  resolved address into the word as-is -- used for a `.word`/`pushlit` label or import operand, and
  for `call`'s own single opcode word (see "Assembly language" above); `CvmOpcode` writes
  `0x8000 | resolvedOpcode` -- the CVM's own tagged-dispatch convention, used for `nop`/`pushlit`/
  `push`/`pop`/`ret`, node 606's `leave`, and node 508's 27 comparison/arithmetic ops.
  `br`/`ifbr`/`slit`/node 606's eight self-describing ops
  never appear here at all -- their whole word is computed outright at assemble time from their
  literal operand, with nothing left for the linker to resolve.

## Library (.galib)

An archive of complete `.gaobj` members plus a fast symbol index -- the same idea as a Unix `ar`
archive's own symbol table (`ranlib`): a member's bytes are stored exactly as its own `.gaobj` file
already serializes itself (an archive is just "several object files, concatenated, plus an index,
never a different encoding of an object file's content"), and a separate index chunk maps every
member's exported (Global) symbols straight to which member defines it, so a future linker can pull
in only the object files a program actually needs without opening and parsing every member first.

Chunks, alongside the shared **STRT** string table:

- **MEMB** -- one entry per member: `{ nameOffset: uint32, dataOffset: uint32, dataLength: uint32 }`,
  where `dataOffset`/`dataLength` slice that member's raw bytes out of **BLOB**.
- **BLOB** -- every member's raw `.gaobj` bytes, concatenated back to back in member order.
- **SYMX** -- one entry per indexed Global symbol: `{ nameOffset: uint32, memberNameOffset: uint32 }`.

`CvmLibrary.BuildSymbolIndex` (run automatically by `Save`, and by `galib create`/`add` before
anything is written) parses every member as a `CvmObjectFile` and rejects the archive -- with no file
written or modified -- if two members share a name, a member's bytes aren't a valid object file, or
two members both export the same Global symbol (a linker would have no way to tell which one to pull
in). `galib add` writes to a temporary file and only replaces the real archive once that validation
and the write both succeed, so a bad `add` can never corrupt a previously-good archive.

```
galib create <archive.galib> <member1.gaobj> [member2.gaobj ...]
galib add <archive.galib> <member1.gaobj> [member2.gaobj ...]
galib list <archive.galib>
galib extract <archive.galib> <memberName> [-o <output.gaobj>]
```

`list` reparses every member to print its exported symbols; `extract` writes a member's bytes out
byte-for-byte identical to the original `.gaobj` that went in.

## Planned: linker and image (.gaimg)

**Opcode binding is fully resolved at link time.** `gaasm` never knows any node's real numeric
opcodes; `galink` will need a small "primitive table" file (or files) the IDE exports after compiling
the relevant node(s), mapping each built-in mnemonic to the numeric opcode this particular build's
interpreter actually uses for it. A stale primitive table (a node recompiled since it was exported)
would silently link a program against the wrong addresses, so the plan is for the IDE to stamp a
build identifier into that file and for `galink` to at least warn when it looks stale.

**The CVM's primitives are not all one node.** The original design assumed a single interpreter node
(607) exported every primitive; Stefan has since clarified the full instruction set is spread across
node 607 plus 606, 608, 507, 506, 508, and 407 -- each mnemonic implemented on exactly one of the
seven -- and that opcodes belonging to different nodes are told apart by opcode-*value* ranges. The
607/507 split is now confirmed (node 607's own `exec` hands off the entire `0xC000-0xFFFF` class --
bit 15 and bit 14 both set -- to node 507 as a single block, via the same up-port multiport call
`/r@`/`/r!` already use), node 606's own range is now confirmed too (its eight self-describing ops
each carry their own 8-bit tag in `0xA800-0xAFFF`, needing no live compile to recognize at all, plus a
ninth, tagged mnemonic, `leave`, at `0xA0xx` -- see "Node 606's own opcode range, confirmed" below),
and node 508's own range is now confirmed too (all 27 of its comparison/arithmetic ops share one
tag, `0xE800-0xEFFF`, exactly node 507's own already-documented "register t" forwarding class --
see "Node 508's own opcode range, confirmed" below); the remaining three nodes' own ranges (608,
407, 506) are still pending from Stefan ("the definite details will be provided later") and this
doc deliberately does not guess at them.

**Node 507's own internal split, confirmed.** Within the `0xC000-0xFFFF` block node 507 receives as a
whole, its own `main` dispatch (`Node507.f18`) further subdivides by bits 13-11:

| Bit pattern | Range | Meaning |
|---|---|---|
| `1111_????_????_????` | `0xF000-0xFFFF` | register w (forwarded to node 407) |
| `1110_1???_????_????` | `0xE800-0xEFFF` | register t (forwarded to node 508) |
| `1110_0???_????_????` | `0xE000-0xE7FF` | register d (forwarded to node 506) |
| `1101_????_????_????` | `0xD000-0xDFFF` | `slit` -- short literal (see `CvmInstructionSet.SlitTag`) |
| `1100_1???_????_????` | `0xC800-0xCFFF` | binary ALU op (`usl`/`ssr`/`usr`/`add`/`sub`/`and`/`xor`/`or`) |
| `1100_0???_????_????` | `0xC000-0xC7FF` | unary ALU op (`inv`/`inc`/`dec`) |

So an ALU op's real opcode is `(0xC800 for binary, 0xC000 for unary) | node507WordAddress`, NOT
node 607's own `0x8000 | address` convention -- using the wrong tag doesn't just point at the wrong
node, it can numerically collide with an unrelated node-607 primitive that happens to share the same
low address bits (this bit everyone got wrong the first time: `Ga144.Evb.Ide.Services.
CvmAssemblyLanguage`, the IDE's on-the-fly resolver used before a real linker exists, originally OR'd
every tagged mnemonic with the flat `0x8000`, so e.g. `sub` and node 607's own word at the same
low address briefly encoded identically). `slit`'s own `0xD000` tag (`CvmInstructionSet.SlitTag`)
sits in the same `0xC000-0xFFFF` block for the same reason: node 607's `exec` forwards it to 507 as
part of that same top-bit-pair test, and it is 507's `main`, not 607, that recognizes the `1101`
pattern as "short literal" and extracts its value.

**Node 606's own opcode range, confirmed.** Per Stefan's bit-pattern table, node 606's eight
self-describing frame-pointer ops sit in `0xA800-0xAFFF`, entirely OUTSIDE node 507's `0xC000-0xFFFF`
block -- these are not something 607's `exec` forwards to 507 at all, but their own top-level
opcode class:

| Bit pattern | Word | Meaning |
|---|---|---|
| `1010_1000_????_????` | `enter <locals>` | enter stack frame, reserve space for locals |
| `1010_1001_????_????` | `adjust <offset>` | adjust stack frame |
| `1010_1010_????_????` | `stl <offset>` | store local |
| `1010_1011_????_????` | `stp <offset>` | store parameter |
| `1010_1100_????_????` | `ldl <offset>` | load local |
| `1010_1101_????_????` | `ldp <offset>` | load parameter |
| `1010_1110_????_????` | `lal <offset>` | load address of local |
| `1010_1111_????_????` | `lap <offset>` | load address of parameter |

Unlike node 507's ALU ops, none of these needs a live compile or a node/symbol pairing at all --
each one's whole word is fully known from its own tag and literal operand alone, the same
"self-describing" shape as `call`/`br`/`ifbr`/`slit` (`CvmOperandEncoding.EmbeddedUnsignedValue`,
`CvmInstructionSet.TryDescribeSelfDecodingWord`). Cross-checked against node 606's own F18 dispatch
comments (`Node606.f18`'s `main` word): `la`/`ld`/`st` are node 606's own shared internal words,
each reached via either `off` (positive frame-pointer offset, for the four `*p` "parameter" variants)
or `noff` (negated, i.e. negative offset, for the four `*l` "local" variants) -- the CVM mnemonic
table gives each of those four pairs its own name (`stl`/`stp`, `ldl`/`ldp`, `lal`/`lap`) rather than
exposing `off`/`noff` as a separate CVM-level concept.

The table's remaining row in this same class, `1010_0xxx_xxxx_xxxx` ("call word in node 606, address
in opcode"), now has its first named member: `leave`. Unlike the eight ops above, this row is NOT
self-describing -- it's a tagged, node-resolved mnemonic (`CvmOperandEncoding.None`) exactly like node
607's own `nop`/`pushlit`/`push`/`pop`/`ret`, its opcode being `0xA000 | 'leave's own compiled word
address` in node 606's own RAM (`Ga144.Evb.Ide.Services.CvmAssemblyLanguage.Node606TagBits`).
Confirmed against a real compile of `Node606.f18`: `'leave` compiles to word address `0x037`, giving
it the opcode `0xA037`. Although the table's own bit diagram for this row shows 11 variable bits
(`0xxx xxxx xxxx`, suggesting an `0xA000-0xA7FF` range), `Node606.f18`'s own `main` dispatch masks the
relayed dispatch byte down to 8 bits before using it (`@b xff and >r`), so in practice only
`0xA000-0xA0FF` is ever produced -- the same 8-bit address-field width as node 607's own
`0x8000 | address` family, not the wider 11-bit field the bit diagram alone would suggest. `leave` is
expected to be the first of potentially several individually-named words reached this way, added one
at a time as each gets a name, the same way node 607's own tagged `nop`/`push`/`pop`/`pushlit`/`ret`
were identified one at a time from that node's own family before every one of them had a name. Node
606's own internal `local` word (`: local drop ex`) is what actually performs this dispatch on 606's
side -- it has no CVM mnemonic of its own, being the dispatch mechanism itself rather than one of the
named targets it can jump to.

**Node 508's own opcode range, confirmed.** Node 507's own dispatch table above already named this
block -- "register t (forwarded to node 508)", `0xE800-0xEFFF` -- before any of node 508's own
mnemonics had names. Node 508's own `main` (`Node508.f18`) does not subdivide that block any further
the way node 606 or node 507 does: it reads whatever address arrives over the port and jumps to it
directly and unmasked (`: main A[ drop !p a !p ]] lit !b @b >r @b ex`), so all 27 of its named words
share the one flat tag, `Node508TagBits = 0xE800` (`Ga144.Evb.Ide.Services.CvmAssemblyLanguage`) --
there is no unary/binary split like node 507's ALU ops, and no self-describing subset like node 606's
eight frame-pointer ops. Every one of these is a tagged, node-resolved mnemonic
(`CvmOperandEncoding.None`), its opcode being `0xE800 | word's own compiled address in node 508's RAM`,
resolved the same way as node 607's `nop`/`pushlit`/`push`/`pop`/`ret` and node 606's `leave`.
Confirmed against a real compile of `Node508.f18` (entry point `main` at word address `0x006`; all 64
RAM words used, `0x006-0x03E` hold code):

| Mnemonic | Word address | Opcode |
|---|---|---|
| `eq` | `0x016` | `0xE816` |
| `eq0` | `0x017` | `0xE817` |
| `false` | `0x018` | `0xE818` |
| `true` | `0x019` | `0xE819` |
| `ne` | `0x01B` | `0xE81B` |
| `ne0` | `0x01C` | `0xE81C` |
| `ugt` | `0x01F` | `0xE81F` |
| `gt` | `0x023` | `0xE823` |
| `gt0` | `0x024` | `0xE824` |
| `ge` | `0x025` | `0xE825` |
| `ge0` | `0x026` | `0xE826` |
| `ule` | `0x027` | `0xE827` |
| `le` | `0x02B` | `0xE82B` |
| `le0` | `0x02C` | `0xE82C` |
| `lt` | `0x02D` | `0xE82D` |
| `lt0` | `0x02E` | `0xE82E` |
| `ult` | `0x02F` | `0xE82F` |
| `uge` | `0x031` | `0xE831` |
| `mul2` | `0x033` | `0xE833` |
| `udiv2` | `0x034` | `0xE834` |
| `div2` | `0x035` | `0xE835` |
| `abs` | `0x037` | `0xE837` |
| `negate` | `0x039` | `0xE839` |
| `xt` | `0x03B` | `0xE83B` |
| `ldt` | `0x03C` | `0xE83C` |
| `stt` | `0x03D` | `0xE83D` |
| `bitcnt` | `0x03E` | `0xE83E` |

Per Stefan's own naming rule ("all words that begin with a `'` are an opcode for the CVM with the
mnemonic using the same name without the leading `'`"), the un-ticked words in `Node508.f18` --
`main` itself, the internal helpers `r!`/`u@-`/`/inc`/`/dec`, and the four internal comparison bases
`gt0`/`ge0`/`le0`/`lt0` that `Node508.f18` also happens to spell without a leading digit inside other
words' bodies -- are not exposed as CVM mnemonics; every tagged word above is a `'`-prefixed word in
the source (`'eq`, `'eq0`, ... `'bitcnt`) with the leading tick stripped for its CVM name, exactly as
already done for node 507's ALU ops and node 606's `leave`. This revision of `Node508.f18` also renamed
three ops from their prior names (`'2*`->`'mul2`, `'u2/`->`'udiv2`, `'2/`->`'div2`) and moved
entry `main` from `0x000` to `0x006`, per Stefan's own newest source (see `Node508.f18`'s own revision
note for the full list of changes from the prior revision).

Given one or more `.gaobj` files (plus optional `-l <archive.galib>` search paths), a memory layout
(which section starts at which page:address -- `CODE` at `0:0000` by default), and the primitive
table(s) for whichever of the seven nodes the program's primitives resolve to, `galink` will resolve
every symbol (a genuinely undefined one is a link error), apply every relocation now that final
addresses are known, and write a single flat `.gaimg`:

```
galink <input1.gaobj> [input2.gaobj ...] --primitives <node607.gaprim> [--primitives <node606.gaprim> ...] -o <output.gaimg>
```

Planned chunks: an `IMGH` header (entry point, load address, word count), a `WORD` chunk -- the final
flat word stream, exactly the `List<int>` `CvmSimulatedSram.LoadProgram` and the real-hardware
installer already know how to consume today -- and a `SYMT`/`STRT` pair carrying the final resolved
global symbol table, so the CVM Debugger can eventually label a linked user program's own addresses
(not just node 607's interpreter internals, which is all `DescribeProgramSymbol`/`DisassemblePage0`
can see today).

## Using the tools today

```
gaasm program.casm -o program.gaobj

galib create mylib.galib helperA.gaobj helperB.gaobj
galib add mylib.galib helperC.gaobj
galib list mylib.galib
galib extract mylib.galib helperB.gaobj -o helperB.gaobj
```

Both tools print a one-line summary on success (`gaasm`: word count, exported/external symbol counts,
relocation count; `galib`: member count, exported symbol count) and write errors to stderr, one per
line -- `gaasm`'s carry a 1-based source line number, `galib`'s name the offending member or symbol.