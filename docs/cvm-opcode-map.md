# CVM opcode map

Every CVM instruction currently defined in `Ga144.Cvm.Toolchain.CvmInstructionSet`, sorted by opcode.
Opcode and parameter values are shown as plain hex (no `0x` prefix).

The **Node** column names which node's F18 source actually implements/decodes that opcode at
runtime -- 607 is the "CPU" node running the CVM's own fetch/execute loop, and it forwards whole
opcode-value ranges out to 507, which in turn forwards its own sub-ranges further out to 506/508
(and, once they have mnemonics of their own, 407/608). `slit` is a case worth noting: its word is
self-describing (no live compile needed to assemble it), but it's node 507's own `main` dispatch,
not 607's, that actually recognizes the `1101` pattern and extracts the value -- so its Node is 507,
not 607.

Two different kinds of opcode appear here:

- **Self-describing** (`call`, `br`, `ifbr`, `slit`, and node 606's eight frame-pointer ops): the whole
  word is a fixed tag OR'd with the operand you write in source. These opcodes are architecturally
  fixed and will never change.
- **Tagged / node-resolved** (`nop`, `pushlit`, `push`, `pop`, `ret`, node 507's eleven ALU ops, node
  606's `leave`, node 508's 27 comparison/arithmetic ops, node 506's nine register-d ops, node 407's
  seven register-w/port ops): the low bits
  are wherever that mnemonic's word currently compiles to inside its own node's resident F18 source.
  The opcodes below are confirmed against the current compile of each node's source in this project; if
  that node's `.f18` source is edited, its mnemonics' opcodes can shift. None of these take an operand
  encoded in the word itself -- they act on register r/d/t/w and the CVM data stack.

| Opcode | Parameter | Node | Mnemonic | Description |
|---|---|---|---|---|
| 0000-7fff | 0000-7fff (target address, bit15=0) | 607 | call | call subroutine at address |
| 800c | - | 607 | pushlit | push the following trailing word (literal or resolved label/import) onto the stack |
| 8018 | - | 607 | pop | pop the top of the CVM data stack |
| 801a | - | 607 | push | push a value onto the CVM data stack |
| 802e | - | 607 | ret | return from a call |
| 803b | - | 607 | nop | no operation |
| 9000 | 000-7ff (signed offset, two's complement) | 607 | br | branch by a signed offset, relative to the word after this one |
| 9800 | 000-7ff (signed offset, two's complement) | 607 | ifbr | conditional branch by a signed offset, relative to the word after this one |
| a037 | - | 606 | leave | leave the stack frame, restoring the stack to its state before the last enter |
| a800 | 00-ff (unsigned count) | 606 | enter | enter a stack frame, reserving space for locals |
| a900 | 00-ff (unsigned offset) | 606 | adjust | adjust the current stack frame |
| aa00 | 00-ff (unsigned offset) | 606 | stl | store to a local at a frame-relative offset |
| ab00 | 00-ff (unsigned offset) | 606 | stp | store to a parameter at a frame-relative offset |
| ac00 | 00-ff (unsigned offset) | 606 | ldl | load a local at a frame-relative offset |
| ad00 | 00-ff (unsigned offset) | 606 | ldp | load a parameter at a frame-relative offset |
| ae00 | 00-ff (unsigned offset) | 606 | lal | load the address of a local at a frame-relative offset |
| af00 | 00-ff (unsigned offset) | 606 | lap | load the address of a parameter at a frame-relative offset |
| c03a | - | 507 | inv | bitwise invert register r (unary) |
| c03c | - | 507 | inc | increment register r (unary) |
| c03e | - | 507 | dec | decrement register r (unary) |
| c82f | - | 507 | usl | unsigned shift left (r shifted by the data stack top) |
| c831 | - | 507 | ssr | signed shift right |
| c833 | - | 507 | usr | unsigned shift right |
| c835 | - | 507 | add | add (r + data stack top) |
| c836 | - | 507 | and | bitwise and |
| c837 | - | 507 | xor | bitwise exclusive or |
| c838 | - | 507 | or | bitwise or |
| c83b | - | 507 | sub | subtract (r - data stack top) |
| d000 | 000-fff (signed value, two's complement) | 507 | slit | load a signed literal directly into register r |
| e00f | - | 506 | zext | zero-extend: clear register d |
| e013 | - | 506 | addc | add with carry (d + data stack top + r -> r, carry -> d) |
| e01d | - | 506 | ldd | load d: copy register d into register r |
| e01e | - | 506 | std | store d: copy register r into register d |
| e020 | - | 506 | xd | exchange registers d and r |
| e022 | - | 506 | mul2d | shift the (r,d) register pair left one bit, carry-out into d (left shift double) |
| e026 | - | 506 | div2d | shift the (r,d) register pair right one bit, carry-out into d (right shift double) |
| e02b | - | 506 | sext | sign-extend: replicate register r's sign bit across register d |
| e030 | - | 506 | umuld | unsigned multiply, double-word result (r * data stack top -> r:d) |
| e816 | - | 508 | eq | equal (signed) |
| e817 | - | 508 | eq0 | equal to zero |
| e818 | - | 508 | false | push false (0) |
| e819 | - | 508 | true | push true (-1) |
| e81b | - | 508 | ne | not equal |
| e81c | - | 508 | ne0 | not equal to zero |
| e81f | - | 508 | ugt | unsigned greater than |
| e823 | - | 508 | gt | greater than (signed) |
| e824 | - | 508 | gt0 | greater than zero |
| e825 | - | 508 | ge | greater or equal (signed) |
| e826 | - | 508 | ge0 | greater or equal to zero |
| e827 | - | 508 | ule | unsigned less or equal |
| e82b | - | 508 | le | less or equal (signed) |
| e82c | - | 508 | le0 | less or equal to zero |
| e82d | - | 508 | lt | less than (signed) |
| e82e | - | 508 | lt0 | less than zero |
| e82f | - | 508 | ult | unsigned less than |
| e831 | - | 508 | uge | unsigned greater or equal |
| e833 | - | 508 | mul2 | multiply by two |
| e834 | - | 508 | udiv2 | unsigned divide by two |
| e835 | - | 508 | div2 | divide by two (signed) |
| e837 | - | 508 | abs | absolute value |
| e839 | - | 508 | negate | negate (two's complement) |
| e83b | - | 508 | xt | exchange register t |
| e83c | - | 508 | ldt | load register t |
| e83d | - | 508 | stt | store register t |
| e83e | - | 508 | bitcnt | population count (count of set bits) |
| f00f | - | 407 | ldhi | move the high 2 bits from a value into register r |
| f012 | - | 407 | xpt | exchange the port register with register r |
| f014 | - | 407 | out | write an 18 bit value to the port |
| f015 | - | 407 | in | read an 18 bit value from the port |
| f016 | - | 407 | ldlo | move the low 16 bits from a value into register r |
| f017 | - | 407 | sthi | move register r's bits into the high 2 bits of a value |
| f01c | - | 407 | stlo | move register r's bits into the low 16 bits of a value |

72 instructions total. Source: `Ga144.Cvm.Toolchain.CvmInstructionSet.Instructions` (shapes and IDs)
paired with `Ga144.Evb.Ide.Services.CvmAssemblyLanguage.NodeSymbolByMnemonic` (which node/tag each
tagged mnemonic resolves against), cross-checked against a live compile of nodes 607, 507, 606, 508,
506, and 407 in this project's own `Compiler/F18Compiler.cs`. Node 608 has a resident F18 source but
no mnemonics registered in the CVM assembly language yet.