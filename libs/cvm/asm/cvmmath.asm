; cvmmath.asm -- CVM runtime math routines written directly in CVM assembler.
;
; Everything the C compiler calls for an operator that has no CVM opcode lives here (declared in
; include/cvm.h): the multiply, the divide/modulo family, the shifts by a run-time count and the exact
; ordered comparisons.
;
;   __fastcall int          __mul (int a, int b)                         a * b modulo 2^16
;   __fastcall unsigned int __udivmod(unsigned n, unsigned d, int wantRemainder)   quotient, or remainder if non-zero
;   __fastcall unsigned int __divu, __modu (unsigned a, unsigned b)
;   __fastcall int          __divs, __mods (int a, int b)    C semantics: truncate toward zero, the remainder
;                                                            has the sign of the dividend
;   __fastcall int          __shl, __shrs (int a, int n);   unsigned int __shru (unsigned a, unsigned n)
;   __fastcall int          __lts __les __gts __ges (int a, int b)               signed   <  <=  >  >=   -> 1 / 0
;   __fastcall int          __ltu __leu __gtu __geu (unsigned a, unsigned b)     unsigned <  <=  >  >=   -> 1 / 0
;   __fastcall int          __cmps (int a, int b), __cmpu (unsigned a, unsigned b)       -> -1 / 0 / 1
;
; Calling convention: every routine here is __fastcall (claude/cvm-abi.md section 3.4). The first argument
; arrives in r0, the second in r1, the third (only __udivmod) in r2; there is no stack frame (no link/unlink),
; the caller has nothing to drop, and the 1-word result is returned in r0. Every register is call-volatile,
; so r0-r7 are used freely. Only opcodes already verified on hardware are used.
;
; Comparison trick used throughout: "sub, then test the sign" is only exact when the operands are less
; than 0x8000 apart (the 16-bit sub wraps). Two values whose top bits are EQUAL are always that close
; (unsigned and signed alike), so the difference is tested only then. When the top bits DIFFER the answer
; is decided by one top bit: unsigned a < b iff b has its top bit set; signed a < b iff a has it set.

; ---------------------------------------------------------------------------------------------------
; __mul -- shift-and-add. x = a, y = b, p = 0; while (y != 0) { if (y & 1) p += x; x <<= 1; y >>= 1; }
; __fastcall: a arrives in r0, b in r1 (no stack frame, no link/unlink); the product is returned in r0.
; The loop runs once per bit of y, so first make y the operand that is obviously the shorter one: if y has a
; non-zero high byte and x has none, swap them (5 * 1000 then takes 3 passes, not 10).
; Registers: r0 x (multiplicand), r1 y (multiplier), r2 product, r3 scratch, r4 constant 1.
.export __mul

__mul:
  mov r3, r1
  highbyte r3
  if r3 ==0 then __mul_ordered
  mov r3, r0
  highbyte r3
  if r3 !=0 then __mul_ordered
  mov r3, r0
  mov r0, r1
  mov r1, r3

__mul_ordered:
  rlit r2, 0
  rlit r4, 1

__mul_loop:
  mov r3, r1
  and r3, r4
  if r3 ==0 then __mul_skip
  add r2, r0

__mul_skip:
  mul2 r0
  udiv2 r1
  if r1 !=0 then __mul_loop

  mov r0, r2
  ret

; ---------------------------------------------------------------------------------------------------
; Unsigned division core (internal, register convention, not exported).
;   in : r0 = n (dividend), r1 = d (divisor)
;   out: r0 = quotient, r2 = remainder          clobbers r3, r4
;   d == 0 gives quotient 0xFFFF and remainder n (it never hangs).
; d >= 0x8000: the quotient is 0 or 1, one compare/subtract decides it.
; d <  0x8000: restoring division, 16 passes. The partial remainder r2 stays below d, so 2*r2+1 < 2*d fits
; in 16 bits and r2' - d lies in (-0x8000, 0x8000): its sign is exact. The quotient bits are shifted into
; the vacated low end of r0 while the dividend bits are shifted out of its top.
;   mul2 r2; if (n top bit) r2++; mul2 n; if (r2 - d >= 0) { r2 -= d; n++ }
; A dividend that is smaller than d (and below 0x8000) returns at once.

; Exported only so cvmlong.asm can reuse it for 32-bit division of 16-bit-sized operands.
.export __udm_core

__udm_core:
  if r1 ==0 then __udm_zero
  if r1 <0 then __udm_big
  if r0 <0 then __udm_start
  mov r3, r0
  sub r3, r1
  if r3 >=0 then __udm_start

__udm_less:
  mov r2, r0
  rlit r0, 0
  ret

__udm_start:
  rlit r2, 0
  rlit r4, 16

__udm_loop:
  mul2 r2
  if r0 >=0 then __udm_nobit
  inc r2

__udm_nobit:
  mul2 r0
  mov r3, r2
  sub r3, r1
  if r3 <0 then __udm_next
  mov r2, r3
  inc r0

__udm_next:
  dec r4
  if r4 !=0 then __udm_loop
  ret

__udm_big:
  if r0 >=0 then __udm_less
  mov r3, r0
  sub r3, r1
  if r3 <0 then __udm_less
  mov r2, r3
  rlit r0, 1
  ret

__udm_zero:
  mov r2, r0
  rlit r0, -1
  ret

; __fastcall unsigned int __udivmod(unsigned n, unsigned d, int wantRemainder)   r0 = n, r1 = d, r2 = flag
.export __udivmod

__udivmod:
  mov r5, r2
  call __udm_core
  if r5 ==0 then __udivmod_done
  mov r0, r2

__udivmod_done:
  ret

; __fastcall unsigned int __divu(unsigned a, unsigned b): the core's quotient is already in r0
.export __divu

__divu:
  br __udm_core

; __fastcall unsigned int __modu(unsigned a, unsigned b)
.export __modu

__modu:
  call __udm_core
  mov r0, r2
  ret

; __fastcall int __divs(int a, int b): divide the magnitudes (abs 0x8000 stays 0x8000 = 32768 unsigned),
; then negate the quotient when the signs differ (the top bit of a xor b).
.export __divs

__divs:
  mov r5, r0
  xor r5, r1
  abs r0
  abs r1
  call __udm_core
  if r5 >=0 then __divs_done
  neg r0

__divs_done:
  ret

; __fastcall int __mods(int a, int b): remainder of the magnitudes, negated when the dividend is negative.
.export __mods

__mods:
  mov r5, r0
  abs r0
  abs r1
  call __udm_core
  mov r0, r2
  if r5 >=0 then __mods_done
  neg r0

__mods_done:
  ret

; ---------------------------------------------------------------------------------------------------
; Shifts by a run-time count n. A count outside 0..15 (n & 0xFFF0 != 0, which includes every negative n)
; gives 0 for __shl / __shru and the sign fill (0 or -1) for __shrs. Otherwise: bit 3 of n shifts by 8 in
; one or two opcodes (lowbyte+byteswap / highbyte), bits 2..0 are a loop of single-bit shifts.
; Registers: r0 value (and result), r1 count, r2/r3 scratch.

; __fastcall int __shl(int a, int n)
.export __shl

__shl:
  mov r2, r1
  rlit r3, 0xFFF0
  and r2, r3
  if r2 !=0 then __shl_zero
  mov r2, r1
  rlit r3, 8
  and r2, r3
  if r2 ==0 then __shl_small
  lowbyte r0
  byteswap r0

__shl_small:
  rlit r3, 7
  and r1, r3
  if r1 ==0 then __shl_done

__shl_loop:
  mul2 r0
  dec r1
  if r1 !=0 then __shl_loop

__shl_done:
  ret

__shl_zero:
  rlit r0, 0
  ret

; __fastcall unsigned int __shru(unsigned a, unsigned n)
.export __shru

__shru:
  mov r2, r1
  rlit r3, 0xFFF0
  and r2, r3
  if r2 !=0 then __shru_zero
  mov r2, r1
  rlit r3, 8
  and r2, r3
  if r2 ==0 then __shru_small
  highbyte r0

__shru_small:
  rlit r3, 7
  and r1, r3
  if r1 ==0 then __shru_done

__shru_loop:
  udiv2 r0
  dec r1
  if r1 !=0 then __shru_loop

__shru_done:
  ret

__shru_zero:
  rlit r0, 0
  ret

; __fastcall int __shrs(int a, int n): arithmetic shift right. The 8-bit step keeps the sign: highbyte,
; then OR 0xFF00 back in when a was negative.
.export __shrs

__shrs:
  mov r2, r1
  rlit r3, 0xFFF0
  and r2, r3
  if r2 !=0 then __shrs_fill
  mov r2, r1
  rlit r3, 8
  and r2, r3
  if r2 ==0 then __shrs_small
  if r0 <0 then __shrs_neg8
  highbyte r0
  br __shrs_small

__shrs_neg8:
  highbyte r0
  rlit r2, 0xFF00
  or r0, r2

__shrs_small:
  rlit r3, 7
  and r1, r3
  if r1 ==0 then __shrs_done

__shrs_loop:
  div2 r0
  dec r1
  if r1 !=0 then __shrs_loop

__shrs_done:
  ret

__shrs_fill:
  rlit r2, 0
  if r0 >=0 then __shrs_fill_done
  rlit r2, -1

__shrs_fill_done:
  mov r0, r2
  ret

; ---------------------------------------------------------------------------------------------------
; Exact ordered comparisons, result 1 or 0 in r0. r0 = a, r1 = b. If the top bits of a and b are equal
; (r2 = a xor b is non-negative) the 16-bit difference a - b is exact and its sign decides; otherwise one
; top bit decides, see the header.

; __fastcall int __lts(int a, int b): signed a < b
.export __lts

__lts:
  mov r2, r0
  xor r2, r1
  if r2 >=0 then __lts_same
  cond r0, <0
  rpop r0
  ret

__lts_same:
  sub r0, r1
  cond r0, <0
  rpop r0
  ret

; __fastcall int __les(int a, int b): signed a <= b
.export __les

__les:
  mov r2, r0
  xor r2, r1
  if r2 >=0 then __les_same
  cond r0, <0
  rpop r0
  ret

__les_same:
  sub r0, r1
  cond r0, <=0
  rpop r0
  ret

; __fastcall int __gts(int a, int b): signed a > b
.export __gts

__gts:
  mov r2, r0
  xor r2, r1
  if r2 >=0 then __gts_same
  cond r1, <0
  rpop r0
  ret

__gts_same:
  sub r0, r1
  cond r0, >0
  rpop r0
  ret

; __fastcall int __ges(int a, int b): signed a >= b
.export __ges

__ges:
  mov r2, r0
  xor r2, r1
  if r2 >=0 then __ges_same
  cond r1, <0
  rpop r0
  ret

__ges_same:
  sub r0, r1
  cond r0, >=0
  rpop r0
  ret

; __fastcall int __ltu(unsigned int a, unsigned int b): unsigned a < b
.export __ltu

__ltu:
  mov r2, r0
  xor r2, r1
  if r2 >=0 then __ltu_same
  cond r1, <0
  rpop r0
  ret

__ltu_same:
  sub r0, r1
  cond r0, <0
  rpop r0
  ret

; __fastcall int __leu(unsigned int a, unsigned int b): unsigned a <= b
.export __leu

__leu:
  mov r2, r0
  xor r2, r1
  if r2 >=0 then __leu_same
  cond r1, <0
  rpop r0
  ret

__leu_same:
  sub r0, r1
  cond r0, <=0
  rpop r0
  ret

; __fastcall int __gtu(unsigned int a, unsigned int b): unsigned a > b
.export __gtu

__gtu:
  mov r2, r0
  xor r2, r1
  if r2 >=0 then __gtu_same
  cond r0, <0
  rpop r0
  ret

__gtu_same:
  sub r0, r1
  cond r0, >0
  rpop r0
  ret

; __fastcall int __geu(unsigned int a, unsigned int b): unsigned a >= b
.export __geu

__geu:
  mov r2, r0
  xor r2, r1
  if r2 >=0 then __geu_same
  cond r0, <0
  rpop r0
  ret

__geu_same:
  sub r0, r1
  cond r0, >=0
  rpop r0
  ret

; __fastcall int __cmps(int a, int b): -1 if a < b, 0 if equal, 1 if a > b
.export __cmps

__cmps:
  rlit r3, 0
  mov r2, r0
  xor r2, r1
  if r2 ==0 then __cmps_done
  if r2 >=0 then __cmps_same
  rlit r3, 1
  if r0 >=0 then __cmps_done
  rlit r3, -1
  br __cmps_done

__cmps_same:
  rlit r3, 1
  sub r0, r1
  if r0 >0 then __cmps_done
  rlit r3, -1

__cmps_done:
  mov r0, r3
  ret

; __fastcall int __cmpu(unsigned int a, unsigned int b): -1 if a < b, 0 if equal, 1 if a > b
.export __cmpu

__cmpu:
  rlit r3, 0
  mov r2, r0
  xor r2, r1
  if r2 ==0 then __cmpu_done
  if r2 >=0 then __cmpu_same
  rlit r3, 1
  if r1 >=0 then __cmpu_done
  rlit r3, -1
  br __cmpu_done

__cmpu_same:
  rlit r3, 1
  sub r0, r1
  if r0 >0 then __cmpu_done
  rlit r3, -1

__cmpu_done:
  mov r0, r3
  ret
