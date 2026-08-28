# CVM toolchain: assembler, librarian, linker

This is the design for a real toolchain around the CVM assembly language (`nop`, `pushlit <data>`,
`push`, `pop`, `call <address>`, `ret`, `br <offset>`, `ifbr <offset>`, and whatever gets added later), sized for being fed by a future C compiler backend as
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
  ifbr 5                  ; conditional branch -- same offset encoding, a different tag

.section DATA
table: .word 1, 2, 3      ; raw data words -- each may also be numeric or a label/import name
```

All eight built-in instructions are always available without `.import` -- the assembler never bakes
in a numeric opcode for them (it has no notion of any node's F18 source at all); every instruction
word is emitted as a placeholder with a relocation against an external symbol named after the
mnemonic (`nop`/`pushlit`/`push`/`pop`/`ret`), against the callee itself (`call`), or -- for `br`/
`ifbr` -- with no relocation at all, since their whole word is already fully known once their
literal operand is (see below), for the linker to resolve later. Every non-numeric operand (a
`pushlit`, `call`, or `.word` operand that isn't a literal) must be either a label defined in the
same file or a name declared with `.import` -- an operand that's neither is a hard assemble error,
not a silent external. `br`/`ifbr` are stricter still: their operand must be a literal signed
offset today -- a label or import name is a hard assemble error ("does not (yet) support a
label/import operand"). This was originally deferred because what the offset is relative to (the
branch's own address vs. the next instruction's) was an open question that didn't need answering to
support the literal case correctly -- it's since been confirmed against real hardware (see "Branch
target addressing, confirmed" below) to be the next instruction's address, so a label operand is now
a known, mechanical computation; it just hasn't been wired into the assembler yet.

**The placeholder word itself is `0x8000 | Id`, not a bare `0` -- except for `call`/`br`/`ifbr`.**
Every entry in `CvmInstructionSet.Instructions` carries a small, stable, append-only numeric `Id`
(`nop`=0, `pushlit`=1, `push`=2, `pop`=3, `call`=4, `ret`=5, `br`=6, `ifbr`=7 today), assigned once and never renumbered or reused, because
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

`call`, `br`, and `ifbr` don't fit that scheme at all, by design: none of them has a tag word to
place an `Id` into, because each one's single opcode word is instead fully determined by its own
operand (`CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress`/`EmbeddedSignedOffset`, as opposed
to the tagged mnemonics' `None`/`TrailingWord`).

`call`'s single word directly IS the target address. Its placeholder is therefore a plain `0`,
resolved via an ordinary `AbsoluteAddress` relocation against its operand -- the same relocation
kind (and the same resolution logic) a `.word` or `pushlit` label/import operand gets, just
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
  `push`/`pop`/`ret`. `br`/`ifbr` never appear here at all -- their whole word is computed outright
  at assemble time from their literal operand, with nothing left for the linker to resolve.

## Library (.galib)

An archive of complete `.gaobj` members plus a fast symbol index -- the same idea as a Unix `ar`
archive's own symbol table (`ranlib`): a member's bytes are stored exactly as its own `.gaobj` file
already serializes itself (an archive is just "several object files, concatenated, plus an index,"
never a different encoding of an object file's content), and a separate index chunk maps every
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
seven -- and that opcodes belonging to different nodes are told apart by opcode-*value* ranges (e.g.
something like `0xA???` for one node's primitives, `0xB???` for another's). The exact range-to-node
assignment is still pending from Stefan ("the definite details will be provided later") and this doc
deliberately does not guess at it. Once it exists, resolving a `CvmOpcode` relocation becomes: look up
the relocation's symbol (mnemonic) in the primitive table(s) covering however many of the seven nodes
the program actually uses, find which node/range implements it, and write that node's real opcode
value into the word -- the `0x8000 | Id` placeholder already sitting there today is not itself
consulted for correctness (the relocation's `SymbolName` is authoritative), but doubles as a
human-readable hint of which instruction a not-yet-linked word represents, and as a safety check a
future `galink` could use to assert the relocation and the placeholder agree before overwriting it.

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
