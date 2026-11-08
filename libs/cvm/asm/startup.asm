; startup.asm

.import main

.export __exit
.export __start


__exit:
  halt
  call __start
  br __exit


__start:
  ; Initialise the VM context: sp = 0xFFFF (the stack grows down from the top of the page), fp = 0.
  ; sp! / fp! pop the new value from the VM stack, so each value is pushed first.
  rlit r0, 0xFFFF
  rpush r0
  sp!
  rlit r0, 0
  rpush r0
  fp!
  call main
  br __exit
