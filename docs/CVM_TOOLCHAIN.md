# CVM toolchain: assembler, librarian, linker

This is the design for a real toolchain around the CVM assembly language (`nop`, `pushlit <data>`,
`push`, `pop`, and whatever gets added later), sized for being fed by a future C compiler backend as
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
loop:
  nop

.section DATA
table: .word 1, 2, 3      ; raw data words -- each may also be numeric or a label/import name
```

All four built-in instructions are always available without `.import` -- the assembler never bakes
in a numeric opcode for them (it has no notion of node 607's F18 source at all); every instruction
word is emitted as a placeholder with a relocation against an external symbol named after the
mnemonic, for the linker to resolve later. Every non-numeric operand (a `pushlit` or `.word` operand
that isn't a literal) must be either a label defined in the same file or a name declared with
`.import` -- an operand that's neither is a hard assemble error, not a silent external.

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
  resolved address into the word as-is; `CvmOpcode` writes `0x8000 | resolvedAddress` -- the CVM's
  own opcode convention, used for every built-in instruction.

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

**Opcode binding is fully resolved at link time.** `gaasm` never knows node 607's real numeric
opcodes; `galink` will need a small "primitive table" file the IDE exports after compiling node 607,
mapping each built-in mnemonic to the numeric opcode this particular build's interpreter actually
uses for it. A stale primitive table (node 607 recompiled since it was exported) would silently link
a program against the wrong addresses, so the plan is for the IDE to stamp a build identifier into
that file and for `galink` to at least warn when it looks stale.

Given one or more `.gaobj` files (plus optional `-l <archive.galib>` search paths), a memory layout
(which section starts at which page:address -- `CODE` at `0:0000` by default), and the primitive
table, `galink` will resolve every symbol (a genuinely undefined one is a link error), apply every
relocation now that final addresses are known, and write a single flat `.gaimg`:

```
galink <input1.gaobj> [input2.gaobj ...] --primitives <node607.gaprim> -o <output.gaimg>
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
