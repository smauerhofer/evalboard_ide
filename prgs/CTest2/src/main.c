// selftest2.c -- second hardware self-test for the CVM C compiler and runtime library: the routines selftest.c leaves out.
//
// Like selftest.c, every check stores the VALUE it computed in a static result variable (page 2, so it shows up as a
// WRITE 2:xxxx in the hardware trace) and counts a mismatch in "failed". When main() returns, "failed" should be 0 and
// "done" 0x600D. Static layout (page 2, in declaration order):
//   res[0..23]   2:0000..0017   16-bit results
//   failed       2:0018
//   done         2:0019
//   lres[0..15]  2:001A..0039   long / unsigned long results, two words each, low word first
//   fres[0..7]   2:003A..0049   float results, two words each, low word first
// Expected values (hex) are listed next to each check.
#include "cvm.h"

static int res[24];
static int failed = 0;
static int done = 0;
static long lres[16];
static float fres[8];

void check(int i, int got, int expected)
{
  res[i] = got;
  if (got != expected) failed = failed + 1;
}

void checkl(int i, long got, long expected)
{
  lres[i] = got;
  if (got != expected) failed = failed + 1;
}

void checkul(int i, unsigned long got, unsigned long expected)
{
  lres[i] = got;
  if (got != expected) failed = failed + 1;
}

void checkf(int i, float got, float expected)
{
  fres[i] = got;
  if (got != expected) failed = failed + 1;
}

void main()
{
  // --- 16-bit shifts by a run-time count (__shl, __shru, __shrs)
  int one = 1;
  int n5 = 5;
  int n16 = 16;
  int n20 = 20;
  int n4 = 4;
  unsigned int top = 0x8000;
  int m256 = -256;
  int m1 = -1;
  check(0, one << n5, 32);                 // 0020
  check(1, top >> n4, 0x0800);             // 0800
  check(2, m256 >> n4, -16);               // FFF0
  check(3, one << n16, 0);                 // 0000: count >= 16
  check(4, m1 >> n20, -1);                 // FFFF: arithmetic shift keeps the sign

  // --- 16-bit unsigned division/modulo, signed division of negative numbers (__divu, __modu, __divs, __mods)
  unsigned int u60k = 60000;
  unsigned int seven = 7;
  int m1000 = -1000;
  int sv = 7;
  check(5, u60k / seven, 8571);            // 217B
  check(6, u60k % seven, 3);               // 0003
  check(7, m1000 / sv, -142);              // FF72
  check(8, m1000 % sv, -6);                // FFFA

  // --- 16-bit bit operations and unsigned compares
  int ba = 0x1234;
  int bb = 0x0FF0;
  check(9, (ba & bb) | (ba ^ bb), 0x1FF4); // 1FF4
  check(10, ~ba, 0xEDCB);                  // EDCB
  unsigned int ua = 40000;
  unsigned int ub = 50000;
  check(11, ua < ub, 1);                   // 0001 (a signed compare would say 0)
  check(12, ub >= ua, 1);                  // 0001
  check(13, ua > ub, 0);                   // 0000

  // --- long shifts by a run-time count (__lshl, __lshru, __lshrs)
  long la = 0x12345;
  int n12 = 12;
  int n25 = 25;
  int n31 = 31;
  int n32 = 32;
  checkl(0, la << n4, 0x123450);           // 00123450
  checkl(1, la << n20, 0x34500000);        // 34500000: count >= 16
  unsigned long ul = 0xF0000000;
  checkul(2, ul >> n12, 0x000F0000);       // 000F0000
  unsigned long ulmsb = 0x80000000;
  checkul(3, ulmsb >> n31, 1);             // 00000001
  long lneg = -1000000;
  checkl(4, lneg >> n4, -62500);           // FFFF0BDC
  checkl(5, lneg >> n25, -1);              // FFFFFFFF: sign fill
  checkl(6, la << n32, 0);                 // 00000000: count >= 32

  // --- unsigned long division / modulo (__ldivu, __lmodu), signed long division of a negative (__ldivs, __lmods)
  unsigned long u4g = 4000000000;
  unsigned long ul7 = 7;
  checkul(7, u4g / ul7, 571428571);        // 220F4EDB
  checkul(8, u4g % ul7, 3);                // 00000003
  long l7 = 7;
  checkl(9, lneg / l7, -142857);           // FFFDD1F7
  checkl(10, lneg % l7, -1);               // FFFFFFFF

  // --- long bit operations and negation
  unsigned long lx = 0xF0F0F0F0;
  unsigned long ly = 0x0FF00FF0;
  checkul(11, (lx & ly) | (lx ^ ly), 0xFFF0FFF0);  // FFF0FFF0
  checkul(12, ~lx, 0x0F0F0F0F);                    // 0F0F0F0F
  long l123456 = 123456;
  checkl(13, -l123456, -123456);                   // FFFE1DC0

  // --- long compares: unsigned (__lgtu, __lltu), signed with a negative (__llts)
  unsigned long one32 = 1;
  check(14, u4g > one32, 1);               // 0001 (a signed compare would say 0)
  check(15, one32 < u4g, 1);               // 0001
  check(16, lneg < l7, 1);                 // 0001
  check(17, l7 <= lneg, 0);                // 0000

  // --- float subtraction and division (fsub, fdiv)
  float f55 = 5.5;
  float f225 = 2.25;
  float f7 = 7.0;
  float f2 = 2.0;
  float f10 = 10.0;
  float f4 = 4.0;
  float f1 = 1.0;
  float f8 = 8.0;
  checkf(0, f55 - f225, 3.25);             // 40500000
  checkf(1, f7 / f2, 3.5);                 // 40600000
  checkf(2, f10 / f4, 2.5);                // 40200000
  checkf(3, f1 / f8, 0.125);               // 3E000000

  // --- long <-> float conversions (__ultof, __ftoul, __ftol)
  unsigned long u2e9 = 2000000000;
  float fu = u2e9;
  checkf(4, fu * f2, 4000000000.0);        // 4F6E6B28: unsigned long -> float is exact here
  float f3e9 = 3000000000.0;
  checkul(14, (unsigned long)f3e9, 3000000000);    // B2D05E00
  float fneg = -12345.75;
  checkl(15, (long)fneg, -12345);                  // FFFFCFC7 (truncation toward zero)
  unsigned long umax = 0xFFFFFFFF;
  float fm = umax;
  float p32 = 65536.0;
  checkf(5, fm, p32 * p32);                // 4F800000: 2^32 (round to nearest)

  // --- float compares with negative values (__flt, __fge)
  float fm2 = -2.0;
  float fm1 = -1.0;
  check(18, fm2 < fm1, 1);                 // 0001
  check(19, fm1 >= fm2, 1);                // 0001
  check(20, fm2 > f1, 0);                  // 0000

  done = 0x600D;
}
