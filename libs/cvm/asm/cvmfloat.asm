; cvmfloat.asm -- CVM runtime routines for the C type "float" (IEEE 754 binary32) that have no coprocessor opcode,
; written directly in CVM assembler: conversions between float and long / unsigned long, and the six
; comparison operators. (Addition, subtraction, multiplication and division are the coprocessor opcodes
; fadd / fsub / fmul / fdiv; the compiler emits those inline.) Declared in include/cvm.h.
;
;   __fastcall float         __ltof  (long a)              long          -> float, rounded to nearest, ties to even
;   __fastcall float         __ultof (unsigned long a)     unsigned long -> float, rounded to nearest, ties to even
;   __fastcall long          __ftol  (float a)             float -> long, truncated toward zero; |a| >= 2^31
;                                                          saturates to 0x7FFFFFFF / 0x80000000; NaN gives 0
;   __fastcall unsigned long __ftoul (float a)             float -> unsigned long, truncated toward zero;
;                                                          a >= 2^32 saturates to 0xFFFFFFFF, NaN gives 0;
;                                                          a negative value wraps (two's complement)
;   __fastcall int           __flt __fle __fgt __fge __feq __fne (float a, float b)     IEEE comparison -> 1 / 0
;
; Comparison semantics are IEEE 754: if either operand is a NaN every one of the six is 0 except __fne, which is 1;
; +0.0 and -0.0 compare equal.
;
; Calling convention (claude/cvm-abi.md section 3.4, __fastcall): the first wide argument arrives in d4
; (r8 = low word, r9 = high word), the second in d5 (r10 = low, r11 = high). A float / long result is returned in
; d0 (r0 = low word, r1 = high word), a comparison result in r0. No stack frame is used and every register is
; call-volatile, so r0-r15 are used freely. Only opcodes already verified on hardware (the 16-bit ones) are used --
; no 32-bit "d" opcode and no coprocessor opcode at all -- plus the long shifts / comparison of cvmlong.asm.
;
; IEEE binary32 layout: bit 31 sign, bits 30..23 exponent (bias 127), bits 22..0 fraction. In the high word
; (r9) that is: bit 15 sign, bits 14..7 exponent, bits 6..0 the top seven fraction bits.

.import __lshl
.import __lshru
.import __lcmps

; ---------------------------------------------------------------------------------------------------
; __ultof / __ltof -- 32-bit integer to float.
; Entry state of the common core __f_from_mag: r9:r8 = magnitude (unsigned), r7 = sign bit (0 or 0x8000).
; The core normalises the magnitude so that bit 31 is set after n left shifts (so its highest set bit was bit
; 31 - n), keeps the top 24 bits as the significand and rounds on the 8 bits that fall off (to nearest, ties to
; even). The result bits are  ((157 - n) << 23) + significand24  (+ 1 when rounding up): the significand's
; implicit leading one adds to the exponent field, so a carry out of the significand falls into the exponent
; for free.

.export __ultof

__ultof:
  rlit r7, 0
  br __f_from_mag

.export __ltof

__ltof:
  rlit r7, 0
  if r9 >=0 then __f_from_mag
  rlit r7, 0x8000
  inv r9
  neg r8
  if r8 !=0 then __f_from_mag
  inc r9

__f_from_mag:
  rlit r6, 0
  if r9 !=0 then __ff_hi
  if r8 ==0 then __ff_zero
  mov r9, r8
  rlit r8, 0
  rlit r6, 16

__ff_hi:
  mov r2, r9
  highbyte r2
  if r2 !=0 then __ff_bits
  lowbyte r9
  byteswap r9
  mov r2, r8
  highbyte r2
  or r9, r2
  lowbyte r8
  byteswap r8
  rlit r2, 8
  add r6, r2

__ff_bits:
  if r9 <0 then __ff_norm
  mul2 r9
  if r8 >=0 then __ff_nc
  inc r9

__ff_nc:
  mul2 r8
  inc r6
  br __ff_bits

__ff_norm:
  ; r9:r8 now has bit 31 set. significand low 16 bits -> r0, high byte (with the implicit one) -> r1
  mov r0, r8
  highbyte r0
  mov r2, r9
  lowbyte r2
  byteswap r2
  or r0, r2
  mov r1, r9
  highbyte r1
  ; exponent field part: (157 - n) << 7, formed as ((157 - n) << 8) >> 1 (157 - n < 256)
  rlit r2, 157
  sub r2, r6
  byteswap r2
  udiv2 r2
  add r1, r2
  ; round to nearest, ties to even, on the dropped byte r8 & 0xFF
  mov r2, r8
  lowbyte r2
  rlit r3, 0x80
  sub r2, r3
  if r2 <0 then __ff_done
  if r2 >0 then __ff_up
  mov r2, r8
  rlit r3, 0x100
  and r2, r3
  if r2 ==0 then __ff_done

__ff_up:
  inc r0
  if r0 !=0 then __ff_done
  inc r1

__ff_done:
  or r1, r7
  ret

__ff_zero:
  rlit r0, 0
  rlit r1, 0
  ret

; ---------------------------------------------------------------------------------------------------
; __f_to_mag (internal) -- float in r9:r8 to the magnitude of its integer part.
; Returns r1:r0 = |a| truncated, as an unsigned 32-bit value (0xFFFFFFFF when |a| >= 2^32), r7 = the sign bit
; (0 or 0x8000; cleared for a NaN, whose magnitude is 0). Uses r2-r5, and the shifts of cvmlong.asm.
; With e = exponent - 127 the value is significand24 * 2^(e - 23): e < 0 gives 0, e >= 32 saturates, otherwise the
; 24-bit significand is shifted left by e - 23 (e >= 23) or right by 23 - e.

__f_to_mag:
  mov r7, r9
  rlit r2, 0x8000
  and r7, r2
  mov r4, r9
  mul2 r4
  highbyte r4
  rlit r2, 127
  mov r3, r4
  sub r3, r2
  if r3 <0 then __fm_zero
  rlit r2, 32
  mov r5, r3
  sub r5, r2
  if r5 >=0 then __fm_big
  rlit r2, 0x7F
  and r9, r2
  rlit r2, 0x80
  or r9, r2
  rlit r2, 23
  mov r0, r3
  sub r0, r2
  if r0 <0 then __fm_right
  call __lshl
  ret

__fm_right:
  neg r0
  call __lshru
  ret

__fm_big:
  rlit r2, 255
  sub r4, r2
  if r4 !=0 then __fm_sat
  mov r5, r9
  rlit r2, 0x7F
  and r5, r2
  or r5, r8
  if r5 ==0 then __fm_sat
  rlit r7, 0

__fm_zero:
  rlit r0, 0
  rlit r1, 0
  ret

__fm_sat:
  rlit r0, -1
  rlit r1, -1
  ret

; __fastcall long __ftol(float a)
.export __ftol

__ftol:
  call __f_to_mag
  if r7 !=0 then __ft_neg
  if r1 >=0 then __ft_ret
  rlit r0, -1
  rlit r1, 0x7FFF

__ft_ret:
  ret

__ft_neg:
  if r1 <0 then __ft_min
  inv r1
  neg r0
  if r0 !=0 then __ft_ret
  inc r1
  ret

__ft_min:
  rlit r0, 0
  rlit r1, 0x8000
  ret

; __fastcall unsigned long __ftoul(float a)
.export __ftoul

__ftoul:
  call __f_to_mag
  if r7 ==0 then __ft_ret
  br __ft_neg

; ---------------------------------------------------------------------------------------------------
; Comparisons. __f_nan (internal): r2 = high word, r3 = low word of a float -> r2 != 0 when it is a NaN
; (|x| > 0x7F800000 as an unsigned 32-bit value). Uses r4, r5.

__f_nan:
  mask15 r2
  rlit r4, 0x7F80
  mov r5, r2
  sub r5, r4
  if r5 >0 then __f_nan_yes
  if r5 <0 then __f_nan_no
  mov r2, r3
  ret

__f_nan_yes:
  rlit r2, 1
  ret

__f_nan_no:
  rlit r2, 0
  ret

; __f_cmp (internal): a = r9:r8, b = r11:r10 -> r0 = -1 / 0 / 1, or 2 when either operand is a NaN. A float's
; sign-magnitude bits become a signed 32-bit key (a negative value's magnitude, negated; -0.0 becomes 0), and the
; keys are compared with the exact signed long comparison of cvmlong.asm.

__f_cmp:
  mov r2, r9
  mov r3, r8
  call __f_nan
  if r2 !=0 then __f_unordered
  mov r2, r11
  mov r3, r10
  call __f_nan
  if r2 !=0 then __f_unordered
  mov r6, r9
  mask15 r9
  if r6 >=0 then __f_key_b
  inv r9
  neg r8
  if r8 !=0 then __f_key_b
  inc r9

__f_key_b:
  mov r7, r11
  mask15 r11
  if r7 >=0 then __f_keys
  inv r11
  neg r10
  if r10 !=0 then __f_keys
  inc r11

__f_keys:
  call __lcmps
  ret

__f_unordered:
  rlit r0, 2
  ret

; __fastcall int __flt(float a, float b): a < b
.export __flt

__flt:
  call __f_cmp
  cond r0, <0
  rpop r0
  ret

; __fastcall int __fle(float a, float b): a <= b
.export __fle

__fle:
  call __f_cmp
  cond r0, <=0
  rpop r0
  ret

; __fastcall int __fgt(float a, float b): a > b, as b < a (an unordered pair gives 2, which is neither < 0 nor <= 0)
.export __fgt

__fgt:
  mov r2, r8
  mov r8, r10
  mov r10, r2
  mov r2, r9
  mov r9, r11
  mov r11, r2
  br __flt

; __fastcall int __fge(float a, float b): a >= b, as b <= a
.export __fge

__fge:
  mov r2, r8
  mov r8, r10
  mov r10, r2
  mov r2, r9
  mov r9, r11
  mov r11, r2
  br __fle

; __fastcall int __feq(float a, float b): a == b
.export __feq

__feq:
  call __f_cmp
  cond r0, ==0
  rpop r0
  ret

; __fastcall int __fne(float a, float b): a != b (true when unordered)
.export __fne

__fne:
  call __f_cmp
  cond r0, !=0
  rpop r0
  ret
