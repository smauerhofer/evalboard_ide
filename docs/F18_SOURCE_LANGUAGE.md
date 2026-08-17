# Textual F18A node source language

The IDE compiler uses ordinary text rather than color semantics. It compiles one node at a time. Project source targets the node's 64-word RAM (`0x000..0x03f`); the system-wide ROM source library targets the 64-word ROM address range (`0x080..0x0bf`) and is compiled before RAM.

## Module directives

`org`, `const`/`constant`, `equ`, and `import` are immediate directives that consume their argument from the compile-time data stack. Push the value first with `#`, then name the directive:

```forth
# 0x00 org
entry start
# 0x20000 const mask
# 63 constant count
# 0xBE equ sget
# 106 import
```

`#` pushes a value onto the compile-time stack. `org` sets the location counter to the popped value. `const`/`constant` and `equ` pop a value and bind it to the name that follows. `import` pops a node coordinate and brings that node's exported symbols into scope.

`org` is checked against the active compilation space: RAM accepts `0x000` through `0x03f`, while ROM accepts `0x080` through `0x0bf` (an address may also land in either range's mirror, `0x040..0x07f` or `0x0c0..0x0ff`, which wraps to the same physical cells).

`entry name` selects the startup entry-point symbol. It takes its target as an ordinary following token, not from the stack, and may only appear outside a definition. For RAM, without `entry`, the first `:` definition is used.

`label name` assigns the current location counter to `name`, the same way a bare `:` would but without compiling a return. It also takes its target as a following token.

## Definitions

```forth
: start
    io b!
    begin
        @b !b
    again
;
```

`:` opens a definition and must be followed by a word name. Referencing that name from another definition compiles a call; forward references are supported.

`;` compiles a return opcode (`ret`). It does not end or close the definition -- compilation simply continues with whatever follows. A definition may contain any number of `;`, and later code can fall straight through from one `:` into the next without an intervening `;` at all. `exit` compiles the same return opcode as `;` and is used the same way inside a definition's body.

`call name`, `jump name`, and `recurse` are also available for explicit control transfers.

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

`data 0x12345` is also accepted at module level as an alternate spelling. Inside a definition, `data` is the GreenArrays I/O address constant and therefore compiles as a literal.

## Primitive F18A opcodes

```text
ex  unext  @p  @+  @b  @  !p  !+  !b  !
+*  2*  2/  inv  +  and  xor  drop  dup  r>  over  a  .  >r  b!  a!
```

`nop` aliases `.`. The F18A opcode at `0x13` is written `inv`. The F18A opcode at `0x16` is exclusive OR and is written `xor`. The F18 stack-transfer opcodes are written `>r` and `r>`; the spellings `push`, `pop`, and `or` are rejected with diagnostics.

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

`A[ ... ]]` assembles one F18A instruction word from up to four primitive opcodes. Inside a definition, the assembled word is compiled as a literal. At module level it is left on the compile-time data stack, so `A[ ... ]] ,` emits it as a raw word into the active RAM or ROM image.

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

A quoted word may also end with a single resolved word reference -- local or imported from another node -- instead of, or after, primitive opcodes. This compiles that reference as a packed call occupying the rest of the word. The assembled word is data as far as this node is concerned: the point is to ship it to another node over a port (via `!b`/`!p`) so that node executes it. An ordinary bare reference to an imported name still pushes its address as a literal, since this node can never itself execute a call into another node's address space -- but inside `A[ ... ]]` the goal is to construct bits for the other node to run, so embedding its call target is exactly what's needed. The word reference must be the last thing before `]]`; like any other packed control transfer, it consumes the remainder of the word:

```forth
# 106 import
: relay-fetch A[ @p+ x@ ]] !b !b . ;   \ ships a call to node 106's 'x@'
```

## ROM source library

Each coordinate has one system-wide ROM source stored in `ga144-rom.yaml`. The node editor exposes it in the **ROM source** tab and its compiled 18-bit words in **System ROM image**. This is separate from project RAM source and is shared by Host and Target chips and by all projects.

Compilation order is always ROM then RAM. Constants, labels, and words exported by the selected node's ROM source are automatically in scope when its RAM source is compiled. System macros may be expanded in ROM or RAM; project user macros are RAM-only.

A module-level node import pops a coordinate from the compile-time stack:

```forth
# 708 import
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