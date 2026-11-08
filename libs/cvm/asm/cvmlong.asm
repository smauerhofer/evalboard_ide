; cvmlong.asm -- CVM runtime routines for the C types "long" and "unsigned long" (32-bit), written directly in
; CVM assembler. Everything the C compiler calls for a long operator that has no inline CVM opcode lives here
; (declared in include/cvm.h): multiply, divide/modulo, shifts by a run-time count and the exact ordered
; comparisons.
;
;   __fastcall long          __lmul (long a, long b)                  a * b modulo 2^32 (signed and unsigned alike)
;   __fastcall long          __ldivs, __lmods (long a, long b)        C semantics: truncate toward zero, the remainder
;                                                                      has the sign of the dividend
;   __fastcall unsigned long __ldivu, __lmodu (unsigned long a, unsigned long b)
;   __fastcall long          __lshl (long a, int n)                   n outside 0..31 gives 0
;   __fastcall unsigned long __lshru (unsigned long a, int n)         n outside 0..31 gives 0
;   __fastcall long          __lshrs (long a, int n)                  n outside 0..31 gives 0 / -1 (the sign of a)
;   __fastcall int           __llts __lles __lgts __lges (long a, long b)                       signed   -> 1 / 0
;   __fastcall int           __lltu __lleu __lgtu __lgeu (unsigned long a, unsigned long b)     unsigned -> 1 / 0
;   __fastcall int           __lcmps (long a, long b), __lcmpu (unsigned long a, unsigned long b)  -> -1 / 0 / 1
;
; Calling convention (claude/cvm-abi.md section 3.4, __fastcall): the first long argument arrives in d4
; (r8 = low word, r9 = high word), the second in d5 (r10 = low, r11 = high), an int argument (the shift count)
; in r0. A long result is returned in d0 (r0 = low word, r1 = high word), a comparison result in r0. No stack
; frame is used. Every register is call-volatile, so r0-r15 are used freely. Only opcodes already verified on
; hardware (the 16-bit ones) are used -- no 32-bit "d" opcode at all.
;
; Carry / borrow of a 16-bit add or subtract, from the operands a, b and the result r (bit 15 of the value):
;   carry  of a + b = (a & b) | ((a | b) & ~r)
;   borrow of a - b = (~a & b) | (~(a ^ b) & r)
; Unsigned 16-bit "a < b" tests use the "top bits equal" trick of cvmmath.asm: if the top bits of a and b are
; equal, the sign of a - b is exact; otherwise the operand whose top bit is set is the larger one.

.import __mul
.import __udm_core

; ---------------------------------------------------------------------------------------------------
; __lmul -- a * b mod 2^32 = al*bl (full 32 bits) + ((al*bh + ah*bl) mod 2^16) << 16.
; The two cross products come from the 16-bit __mul; al*bl is a shift-and-add over the bits of bl into the
; 32-bit accumulator r1:r0 with the multiplicand x = r3:r2 doubling every pass.
.export __lmul

__lmul:
  mov r0, r8
  mov r1, r11
  call __mul
  mov r5, r0
  mov r0, r9
  mov r1, r10
  call __mul
  add r5, r0
  mov r2, r8
  rlit r3, 0
  mov r4, r10
  rlit r0, 0
  rlit r1, 0
  rlit r12, 1

__lmul_loop:
  if r4 ==0 then __lmul_done
  mov r6, r4
  and r6, r12
  if r6 ==0 then __lmul_shift
  mov r6, r0
  and r6, r2
  mov r13, r0
  or r13, r2
  add r0, r2
  mov r14, r0
  inv r14
  and r13, r14
  or r6, r13
  add r1, r3
  if r6 >=0 then __lmul_shift
  inc r1

__lmul_shift:
  mul2 r3
  if r2 >=0 then __lmul_nohi
  inc r3

__lmul_nohi:
  mul2 r2
  udiv2 r4
  br __lmul_loop

__lmul_done:
  add r1, r5
  ret

; ---------------------------------------------------------------------------------------------------
; Unsigned 32-bit division core (internal, register convention).
;   in : r9:r8 = n (dividend), r11:r10 = d (divisor)
;   out: r1:r0 = quotient, r3:r2 = remainder          clobbers r4-r6, r12-r14
;   d == 0 gives quotient 0xFFFFFFFF and remainder n (it never hangs).
; Operands that both fit in 16 bits are handed to the 16-bit core of cvmmath.asm. Otherwise: restoring
; division, 32 passes; the remainder R = r3:r2 is shifted left with the next dividend bit and may overflow
; 32 bits (r6 remembers the bit shifted out), in which case R - d is taken unconditionally.

__ludm32:
  mov r4, r10
  or r4, r11
  if r4 ==0 then __ludm32_zero
  mov r4, r11
  or r4, r9
  if r4 !=0 then __ludm32_full
  mov r0, r8
  mov r1, r10
  call __udm_core
  rlit r1, 0
  rlit r3, 0
  ret

__ludm32_full:
  rlit r2, 0
  rlit r3, 0
  rlit r12, 32

__ludm32_loop:
  rlit r6, 0
  if r3 >=0 then __l32_a
  rlit r6, 1

__l32_a:
  mul2 r3
  if r2 >=0 then __l32_b
  inc r3

__l32_b:
  mul2 r2
  if r9 >=0 then __l32_c
  inc r2

__l32_c:
  mul2 r9
  if r8 >=0 then __l32_d
  inc r9

__l32_d:
  mul2 r8
  if r6 !=0 then __ludm32_sub
  mov r5, r3
  xor r5, r11
  if r5 ==0 then __l32_cmplo
  if r5 >=0 then __l32_hisame
  if r3 <0 then __ludm32_sub
  br __ludm32_next

__l32_hisame:
  mov r5, r3
  sub r5, r11
  if r5 >0 then __ludm32_sub
  br __ludm32_next

__l32_cmplo:
  mov r5, r2
  xor r5, r10
  if r5 ==0 then __ludm32_sub
  if r5 >=0 then __l32_losame
  if r2 <0 then __ludm32_sub
  br __ludm32_next

__l32_losame:
  mov r5, r2
  sub r5, r10
  if r5 >0 then __ludm32_sub
  br __ludm32_next

__ludm32_sub:
  mov r5, r2
  inv r5
  and r5, r10
  mov r13, r2
  xor r13, r10
  inv r13
  mov r14, r2
  sub r14, r10
  and r13, r14
  or r5, r13
  mov r2, r14
  sub r3, r11
  if r5 >=0 then __l32_nob
  dec r3

__l32_nob:
  inc r8

__ludm32_next:
  dec r12
  if r12 !=0 then __ludm32_loop
  mov r0, r8
  mov r1, r9
  ret

__ludm32_zero:
  mov r2, r8
  mov r3, r9
  rlit r0, -1
  rlit r1, -1
  ret

; __fastcall unsigned long __ldivu(unsigned long a, unsigned long b): the core's quotient is already in r1:r0
.export __ldivu

__ldivu:
  br __ludm32

; __fastcall unsigned long __lmodu(unsigned long a, unsigned long b)
.export __lmodu

__lmodu:
  call __ludm32
  mov r0, r2
  mov r1, r3
  ret

; __fastcall long __ldivs(long a, long b): divide the magnitudes, negate the quotient when the signs differ.
; r15 = sign word (a.hi xor b.hi).
.export __ldivs

__ldivs:
  mov r15, r9
  xor r15, r11
  if r9 >=0 then __lds_a
  inv r9
  neg r8
  if r8 !=0 then __lds_a
  inc r9

__lds_a:
  if r11 >=0 then __lds_b
  inv r11
  neg r10
  if r10 !=0 then __lds_b
  inc r11

__lds_b:
  call __ludm32
  if r15 >=0 then __lds_done
  inv r1
  neg r0
  if r0 !=0 then __lds_done
  inc r1

__lds_done:
  ret

; __fastcall long __lmods(long a, long b): remainder of the magnitudes, negated when the dividend is negative.
.export __lmods

__lmods:
  mov r15, r9
  if r9 >=0 then __lms_a
  inv r9
  neg r8
  if r8 !=0 then __lms_a
  inc r9

__lms_a:
  if r11 >=0 then __lms_b
  inv r11
  neg r10
  if r10 !=0 then __lms_b
  inc r11

__lms_b:
  call __ludm32
  mov r0, r2
  mov r1, r3
  if r15 >=0 then __lms_done
  inv r1
  neg r0
  if r0 !=0 then __lms_done
  inc r1

__lms_done:
  ret

; ---------------------------------------------------------------------------------------------------
; Shifts by a run-time count n (r0). A count outside 0..31 (n & 0xFFE0 != 0, which includes every negative n)
; gives 0 for __lshl / __lshru and the sign fill (0 or -1 in both words) for __lshrs. Otherwise: bit 4 of n
; shifts by 16 (a word move), bit 3 by 8 (byte moves through lowbyte / highbyte / byteswap), bits 2..0 are a
; loop of single-bit shifts. Value in r9:r8, count copied to r2, scratch r3.

; __fastcall long __lshl(long a, int n)
.export __lshl

__lshl:
  mov r2, r0
  rlit r3, 0xFFE0
  and r3, r2
  if r3 !=0 then __lshl_zero
  rlit r3, 16
  and r3, r2
  if r3 ==0 then __lshl_8
  mov r9, r8
  rlit r8, 0

__lshl_8:
  rlit r3, 8
  and r3, r2
  if r3 ==0 then __lshl_small
  lowbyte r9
  byteswap r9
  mov r3, r8
  highbyte r3
  or r9, r3
  lowbyte r8
  byteswap r8

__lshl_small:
  rlit r3, 7
  and r2, r3
  if r2 ==0 then __lshl_done

__lshl_loop:
  mul2 r9
  if r8 >=0 then __lshl_nc
  inc r9

__lshl_nc:
  mul2 r8
  dec r2
  if r2 !=0 then __lshl_loop

__lshl_done:
  mov r0, r8
  mov r1, r9
  ret

__lshl_zero:
  rlit r0, 0
  rlit r1, 0
  ret

; __fastcall unsigned long __lshru(unsigned long a, int n)
.export __lshru

__lshru:
  mov r2, r0
  rlit r3, 0xFFE0
  and r3, r2
  if r3 !=0 then __lshru_zero
  rlit r3, 16
  and r3, r2
  if r3 ==0 then __lshru_8
  mov r8, r9
  rlit r9, 0

__lshru_8:
  rlit r3, 8
  and r3, r2
  if r3 ==0 then __lshru_small
  mov r3, r9
  lowbyte r3
  byteswap r3
  highbyte r8
  or r8, r3
  highbyte r9

__lshru_small:
  rlit r3, 7
  and r2, r3
  if r2 ==0 then __lshru_done
  rlit r13, 1
  rlit r14, 0x8000

__lshru_loop:
  mov r3, r9
  and r3, r13
  udiv2 r8
  if r3 ==0 then __lshru_nc
  or r8, r14

__lshru_nc:
  udiv2 r9
  dec r2
  if r2 !=0 then __lshru_loop

__lshru_done:
  mov r0, r8
  mov r1, r9
  ret

__lshru_zero:
  rlit r0, 0
  rlit r1, 0
  ret

; __fastcall long __lshrs(long a, int n): arithmetic shift right (the sign is shifted in).
.export __lshrs

__lshrs:
  mov r2, r0
  rlit r3, 0xFFE0
  and r3, r2
  if r3 !=0 then __lshrs_fill
  rlit r3, 16
  and r3, r2
  if r3 ==0 then __lshrs_8
  mov r8, r9
  rlit r9, 0
  if r8 >=0 then __lshrs_8
  rlit r9, -1

__lshrs_8:
  rlit r3, 8
  and r3, r2
  if r3 ==0 then __lshrs_small
  mov r3, r9
  lowbyte r3
  byteswap r3
  highbyte r8
  or r8, r3
  if r9 <0 then __lshrs_neg8
  highbyte r9
  br __lshrs_small

__lshrs_neg8:
  highbyte r9
  rlit r3, 0xFF00
  or r9, r3

__lshrs_small:
  rlit r3, 7
  and r2, r3
  if r2 ==0 then __lshrs_done
  rlit r13, 1
  rlit r14, 0x8000

__lshrs_loop:
  mov r3, r9
  and r3, r13
  udiv2 r8
  if r3 ==0 then __lshrs_nc
  or r8, r14

__lshrs_nc:
  div2 r9
  dec r2
  if r2 !=0 then __lshrs_loop

__lshrs_done:
  mov r0, r8
  mov r1, r9
  ret

__lshrs_fill:
  rlit r0, 0
  rlit r1, 0
  if r9 >=0 then __lshrs_filled
  rlit r0, -1
  rlit r1, -1

__lshrs_filled:
  ret

; ---------------------------------------------------------------------------------------------------
; Exact three-way comparison cores (internal): r9:r8 = a, r11:r10 = b, result r0 = -1 / 0 / 1.
; The high words decide (signed or unsigned), the low words (always unsigned) break a tie.

__lcmps_core:
  mov r2, r9
  xor r2, r11
  if r2 ==0 then __lc_lo
  if r2 >=0 then __lc_hisame
  rlit r0, 1
  if r9 >=0 then __lc_ret
  rlit r0, -1

__lc_ret:
  ret

__lcmpu_core:
  mov r2, r9
  xor r2, r11
  if r2 ==0 then __lc_lo
  if r2 >=0 then __lc_hisame
  rlit r0, 1
  if r9 <0 then __lc_ret
  rlit r0, -1
  ret

__lc_hisame:
  rlit r0, 1
  mov r2, r9
  sub r2, r11
  if r2 >0 then __lc_ret
  rlit r0, -1
  ret

__lc_lo:
  mov r2, r8
  xor r2, r10
  if r2 ==0 then __lc_equal
  if r2 >=0 then __lc_losame
  rlit r0, 1
  if r8 <0 then __lc_ret
  rlit r0, -1
  ret

__lc_losame:
  rlit r0, 1
  mov r2, r8
  sub r2, r10
  if r2 >0 then __lc_ret
  rlit r0, -1
  ret

__lc_equal:
  rlit r0, 0
  ret

; __fastcall int __lcmps(long a, long b): -1 if a < b, 0 if equal, 1 if a > b
.export __lcmps

__lcmps:
  br __lcmps_core

; __fastcall int __lcmpu(unsigned long a, unsigned long b)
.export __lcmpu

__lcmpu:
  br __lcmpu_core

; __fastcall int __llts(long a, long b): signed a < b
.export __llts

__llts:
  call __lcmps_core
  cond r0, <0
  rpop r0
  ret

; __fastcall int __lles(long a, long b): signed a <= b
.export __lles

__lles:
  call __lcmps_core
  cond r0, <=0
  rpop r0
  ret

; __fastcall int __lgts(long a, long b): signed a > b
.export __lgts

__lgts:
  call __lcmps_core
  cond r0, >0
  rpop r0
  ret

; __fastcall int __lges(long a, long b): signed a >= b
.export __lges

__lges:
  call __lcmps_core
  cond r0, >=0
  rpop r0
  ret

; __fastcall int __lltu(unsigned long a, unsigned long b): unsigned a < b
.export __lltu

__lltu:
  call __lcmpu_core
  cond r0, <0
  rpop r0
  ret

; __fastcall int __lleu(unsigned long a, unsigned long b): unsigned a <= b
.export __lleu

__lleu:
  call __lcmpu_core
  cond r0, <=0
  rpop r0
  ret

; __fastcall int __lgtu(unsigned long a, unsigned long b): unsigned a > b
.export __lgtu

__lgtu:
  call __lcmpu_core
  cond r0, >0
  rpop r0
  ret

; __fastcall int __lgeu(unsigned long a, unsigned long b): unsigned a >= b
.export __lgeu

__lgeu:
  call __lcmpu_core
  cond r0, >=0
  rpop r0
  ret
