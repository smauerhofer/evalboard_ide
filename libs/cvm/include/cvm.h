#ifndef CVM_H
#define CVM_H

/* Program entry and exit, defined in asm/startup.asm: __start sets sp = 0xFFFF and fp = 0, calls main() and then
 * falls into __exit, which halts. (Declared with these exact names, since a declaration makes the compiler import
 * the symbol, and the linker must find it.) */
extern void __start();
extern void __exit();

extern void *heap_start;

/*
 * CVM runtime library -- the arithmetic helpers the C compiler calls for every operator that has no
 * CVM opcode: '*', '/', '%' and shifts by a count that is not a compile-time constant.
 *
 * You normally never call these yourself: the compiler emits "call __mul" etc. on its own and records the
 * name as an import, so any program that uses one of these operators must link against the cvm library.
 *
 * All of them are __fastcall functions: the first argument arrives in r0, the second in r1, the third (only
 * __udivmod) in r2, and the one-word result is returned in r0. There is no stack frame and nothing for the
 * caller to drop. They are hand-written CVM assembler in asm/cvmmath.asm (nothing is left to implement in
 * src/cvm.c), so they can be called from C like any other function.
 */

/* a * b, modulo 2^16 (same bit pattern for signed and unsigned operands). */
extern __fastcall int __mul(int a, int b);

/* a / b and a % b, C semantics: truncation toward zero, the remainder takes the sign of the dividend.
 * Division by zero does not hang; __divu returns 0xFFFF and __modu returns a, the signed versions give an
 * unspecified value. */
extern __fastcall int __divs(int a, int b);
extern __fastcall int __mods(int a, int b);
extern __fastcall unsigned int __divu(unsigned int a, unsigned int b);
extern __fastcall unsigned int __modu(unsigned int a, unsigned int b);

/* Shifts by a run-time count n. The result type follows a, never n.
 * __shl / __shru: a count outside 0..15 gives 0.
 * __shrs (arithmetic): a count of 15 or more (or negative) gives 0 or -1, the sign of a. */
extern __fastcall int __shl(int a, int n);
extern __fastcall int __shrs(int a, int n);
extern __fastcall unsigned int __shru(unsigned int a, unsigned int n);

/*
 * Exact ordered comparisons.
 *
 * The compiler calls these for every '<', '<=', '>' and '>=' (signed or unsigned by the operand types;
 * pointers compare their address word as unsigned), so a program that uses any of those operators must link
 * against the cvm library. Only a comparison with a literal 0 is done inline. '==' and '!=' are always
 * inline and exact.
 *
 * An older compiler lowered '<' etc. to "subtract, then test the sign", which is only right while the two
 * operands are less than 0x8000 apart (unsigned 0 < 0xFFFF and signed -32768 < 32767 came out false). These
 * functions are exact over the full 16-bit range. They return 1 (true) or 0 (false); you can also call them
 * yourself.
 *
 *   signed:    __lts a<b   __les a<=b   __gts a>b   __ges a>=b
 *   unsigned:  __ltu a<b   __leu a<=b   __gtu a>b   __geu a>=b
 *   three-way: __cmps / __cmpu return -1 if a<b, 0 if a==b, 1 if a>b (for sort callbacks)
 */
extern __fastcall int __lts(int a, int b);
extern __fastcall int __les(int a, int b);
extern __fastcall int __gts(int a, int b);
extern __fastcall int __ges(int a, int b);
extern __fastcall int __ltu(unsigned int a, unsigned int b);
extern __fastcall int __leu(unsigned int a, unsigned int b);
extern __fastcall int __gtu(unsigned int a, unsigned int b);
extern __fastcall int __geu(unsigned int a, unsigned int b);
extern __fastcall int __cmps(int a, int b);
extern __fastcall int __cmpu(unsigned int a, unsigned int b);

/* Unsigned division: returns the quotient, or the remainder when wantRemainder is non-zero. Division by
 * zero gives quotient 0xFFFF and remainder n. */
extern __fastcall unsigned int __udivmod(unsigned int n, unsigned int d, int wantRemainder);

/*
 * long / unsigned long (32 bit). The compiler does + - & | ^ ~ unary-minus ++ -- == != and the int<->long
 * conversions inline with the 32-bit "d" opcodes; everything below is a call into asm/cvmlong.asm. They are
 * __fastcall as well: the first long argument arrives in d4, the second in d5, an int shift count in r0; a
 * long result is returned in d0 (low word r0, high word r1), a comparison result in r0.
 *
 *   __lmul                      a * b modulo 2^32 (signed and unsigned alike)
 *   __ldivs  __lmods            signed / and % (truncate toward zero, remainder takes the dividend's sign)
 *   __ldivu  __lmodu            unsigned / and %. Division by zero gives quotient 0xFFFFFFFF, remainder a.
 *   __lshl  __lshru  __lshrs    shifts by a run-time count; a count outside 0..31 gives 0 (and the sign
 *                               fill 0 / -1 for the arithmetic shift __lshrs)
 *   __llts __lles __lgts __lges   signed   a < b, a <= b, a > b, a >= b  -> 1 / 0
 *   __lltu __lleu __lgtu __lgeu   unsigned a < b, a <= b, a > b, a >= b  -> 1 / 0
 *   __lcmps __lcmpu             three-way: -1 if a < b, 0 if a == b, 1 if a > b
 */
extern __fastcall long __lmul(long a, long b);
extern __fastcall long __ldivs(long a, long b);
extern __fastcall long __lmods(long a, long b);
extern __fastcall unsigned long __ldivu(unsigned long a, unsigned long b);
extern __fastcall unsigned long __lmodu(unsigned long a, unsigned long b);
extern __fastcall long __lshl(long a, int n);
extern __fastcall long __lshrs(long a, int n);
extern __fastcall unsigned long __lshru(unsigned long a, int n);
extern __fastcall int __llts(long a, long b);
extern __fastcall int __lles(long a, long b);
extern __fastcall int __lgts(long a, long b);
extern __fastcall int __lges(long a, long b);
extern __fastcall int __lltu(unsigned long a, unsigned long b);
extern __fastcall int __lleu(unsigned long a, unsigned long b);
extern __fastcall int __lgtu(unsigned long a, unsigned long b);
extern __fastcall int __lgeu(unsigned long a, unsigned long b);
extern __fastcall int __lcmps(long a, long b);
extern __fastcall int __lcmpu(unsigned long a, unsigned long b);

/*
 * float (IEEE 754 binary32). The compiler does + - * / (the coprocessor opcodes fadd, fsub, fmul, fdiv), unary -,
 * and the truth test inline; everything below is a call into asm/cvmfloat.asm. All are __fastcall: the first
 * wide argument arrives in d4 (r8/r9), the second in d5 (r10/r11); a float or long result comes back in d0
 * (r0/r1), a comparison result in r0.
 *
 *   __ltof  __ultof             long / unsigned long -> float (round to nearest, ties to even). The
 *                               compiler converts an int or unsigned int by widening it to long first.
 *   __ftol  __ftoul             float -> long / unsigned long, truncated toward zero. |a| >= 2^31 (__ftol) or
 *                               a >= 2^32 (__ftoul) saturates; NaN gives 0; (int)f is the low word of __ftol.
 *   __flt __fle __fgt __fge     IEEE ordered comparisons -> 1 / 0 (0 when either operand is a NaN)
 *   __feq __fne                 IEEE == and != (__fne is 1 when either operand is a NaN; +0.0 == -0.0)
 */
extern __fastcall float __ltof(long a);
extern __fastcall float __ultof(unsigned long a);
extern __fastcall long __ftol(float a);
extern __fastcall unsigned long __ftoul(float a);
extern __fastcall int __flt(float a, float b);
extern __fastcall int __fle(float a, float b);
extern __fastcall int __fgt(float a, float b);
extern __fastcall int __fge(float a, float b);
extern __fastcall int __feq(float a, float b);
extern __fastcall int __fne(float a, float b);

#endif
