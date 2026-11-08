; startup.asm

.import main

.export __exit
.export __start


__exit:
  halt
  call __start
  br __exit


__start:
  call main
  br __exit
