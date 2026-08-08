# Textual F18A node source language

The IDE compiler uses ordinary text rather than color semantics. It compiles one node at a time. Project source targets the node's 64-word RAM (`0x000..0x03f`); the system-wide ROM source library targets the 64-word ROM address range (`0x080..0x0bf`) and is compiled before RAM.

## Module directives

```forth
org 0x00             // Prefix form
0x20 org             // arrayForth-compatible postfix form
entry start
const mask = 0x20000
constant count 63
```

`org` is checked against the active compilation space: RAM accepts `0x000` through `0x03f`, while ROM accepts `0x080` through `0x0bf`. `entry` selects the startup entry-point symbol. For RAM, without `entry`, the first `:` definition is used.

## Definitions

```forth
: start
    io b!
    begin
        @b !b
    again
;
```

`:` must be followed by a word name. `;` compiles the F18A return opcode and terminates the definition. A word name used in another definition compiles a call. Forward references are supported.

`exit` compiles a return without ending the textual definition. `call name`, `jump name`, and `recurse` are also available.

## Numbers and literals

C-style integer syntax is accepted:

```text
123       -1        0x34       0b1010       0o77       0x1_5555
```

A number inside a definition compiles an `@p` literal and its 18-bit data word. Negative values use 18-bit two's-complement representation. The valid source range is `-0x20000` through `0x3ffff`.

Raw words can be emitted into the active RAM or ROM compilation space at module level with any of these forms:

```forth
.word 0x12345
word 0x12345
0x12345 ,
```

For compatibility, `data 0x12345` is also accepted at module level. Inside a definition, `data` is the GreenArrays I/O address constant and therefore compiles as a literal.

## Primitive F18A opcodes

```text
ex  unext  @p  @+  @b  @  !p  !+  !b  !
+*  2*  2/  inv  +  and  xor  drop  dup  r>  over  a  .  >r  b!  a!
```

`nop` aliases `.`. The F18A opcode at `0x13` inverts all bits and is written `inv` in this textual syntax, with `not` accepted as an alias; the former `-` spelling has been removed because it was easily confused with subtraction (the F18A has no subtract opcode; negation is done via `inv` then `1+`, and a leading `-` denotes a negative numeric literal). The F18A opcode at `0x16` is exclusive OR and is written `xor` in this textual syntax. The F18 stack-transfer opcodes are written `>r` and `r>`; legacy `push`, `pop`, and `or` spellings are rejected with diagnostics.

## Control extensions

```forth
begin ... again
begin ... until
begin ... -until
if ... then
-if ... then
if ... else ... then
ahead ... then
leap ... then       // GreenArrays alias for ahead
63 for ... next
begin ... while ... repeat
begin ... -while ... repeat
```

The compiler aligns branch destinations to instruction-word boundaries and emits control transfers in slot 0. This is conservative and correct, though not yet as dense as the optimizing arrayForth compiler.

## Quoted instruction words

`A[ ... ]]` assembles one F18A instruction word. Inside a definition, the assembled word is compiled as a literal. At module level it is left on the compile-time data stack, so `A[ ... ]] ,` emits it as a raw word into the active RAM or ROM image.

```forth
: send-oldest
    A[ !b @p @ ]]
    !
;
```

Known GreenArrays validation vectors:

```text
A[ !b @p @ ]]  = 0x09d0a
A[ !b !+ !b ]] = 0x09822
```


## ROM source library

Each coordinate has one system-wide ROM source stored in `ga144-rom.yaml`. The node editor exposes it in the **ROM source** tab and its compiled 18-bit words in **System ROM image**. This is separate from project RAM source and is shared by Host and Target chips and by all projects.

Compilation order is always ROM then RAM. Constants, labels, and words exported by the selected node's ROM source are automatically in scope when its RAM source is compiled. System macros may be expanded in ROM or RAM; project user macros are RAM-only.

A module-level node import uses a coordinate on the compile-time stack:

```forth
708 import
```

In ROM compilation this imports the other node's ROM exports. In RAM compilation it imports the other node's ROM and RAM exports. The current node may not import itself because its own ROM dictionary is already supplied automatically to RAM.

The GreenArrays chip data book notes that the standard GA144 ROM varies by node type and that the original routine source was distributed separately in arrayForth source blocks. The IDE therefore does not invent undocumented node-ROM routine addresses: populate the system ROM source with the source/dictionary appropriate to the chip revision you are targeting. `warm` and `cold` remain available as the common documented compatibility entries described below.

## GreenArrays constants

Single-port constants:

```text
right  down  left  up  io  data  ldata
```

Multiport constants:

```text
lu du dl dlu ru rl rlu rd rdu rdl rdlu
---u --l- --lu -d-- -d-u -dl- -dlu r--- r--u r-l- r-lu rd-- rd-u rdl-
```

Other constants:

```text
ram rom eam io-reset word-mask
```

`warm` and `cold` are callable ROM words at `0x0a9` and `0x0aa`. The wider set of GA144 ROM routines is node-ROM-dependent and is intentionally not assigned universal addresses by this compiler yet.

## Comments

All of these forms are accepted:

```forth
( parenthesized comment )
// line comment
\ line comment
```

## Current scope

The compiler assembles both system ROM and project RAM, resolves local definitions and labels, supports compile-time FORTH interpretation, system/user textual macros with the scope rules above, and resolves explicit cross-node imports. It intentionally does not ship guessed node-ROM-specific vendor dictionaries or reproduce arrayForth's full optimizing slot-placement policy.
