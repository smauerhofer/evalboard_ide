// selftest3.c -- third hardware self-test for the CVM C compiler and runtime library: completes the coverage of
// asm/cvmmath.asm, asm/cvmlong.asm and asm/cvmfloat.asm that selftest.c and selftest2.c leave out. Every routine of the
// cvm library is called here directly (the declarations in cvm.h), with edge cases: the full 16-bit and 32-bit
// comparison range, division by zero, shift counts at and beyond the word size, float rounding, saturation, NaN,
// infinity and signed zero.
//
// Like the other two tests, every check stores the VALUE it computed in a static (page 2, so it shows up as a
// WRITE 2:xxxx in the hardware trace) and counts a mismatch in "failed". When main() returns, "failed" should be 0 and
// "done" 0x600D. Results are appended in call order: check() fills res[], checkl()/checkul() fill lres[] (two words
// each, low word first). Floats are checked by their IEEE bit pattern (checkul on fbits(f)), so no float literal has
// to be parsed to know what is expected. Static layout (page 2, in declaration order):
//   res[112]   2:0000..006F   16-bit results (104 are used, the unused tail stays 0)
//   failed     2:0070
//   done       2:0071
//   lres[96]   2:0072..0131   32-bit results (78 are used), two words each, low word first
//   ri, li     2:0132, 2:0133 next free slot of res[] / lres[] (end values 104 and 78)
// The expected values are the second argument of every check.
#include "cvm.h"

static int res[112];
static int failed = 0;
static int done = 0;
static long lres[96];
static int ri = 0;
static int li = 0;

void check(int got, int expected)
{
  res[ri] = got;
  ri = ri + 1;
  if (got != expected) failed = failed + 1;
}

void checkl(long got, long expected)
{
  lres[li] = got;
  li = li + 1;
  if (got != expected) failed = failed + 1;
}

void checkul(unsigned long got, unsigned long expected)
{
  lres[li] = got;
  li = li + 1;
  if (got != expected) failed = failed + 1;
}

// float <-> its bit pattern
unsigned long fbits(float f)
{
  unsigned long *p = (unsigned long *)&f;
  return *p;
}

float mkf(unsigned long bits)
{
  float f;
  unsigned long *p = (unsigned long *)&f;
  *p = bits;
  return f;
}

// 16-bit comparison routines -> bit mask: 1 lt, 2 le, 4 gt, 8 ge
int sflags(int a, int b)
{
  return __lts(a, b) | (__les(a, b) << 1) | (__gts(a, b) << 2) | (__ges(a, b) << 3);
}

int uflags(unsigned int a, unsigned int b)
{
  return __ltu(a, b) | (__leu(a, b) << 1) | (__gtu(a, b) << 2) | (__geu(a, b) << 3);
}

int lsflags(long a, long b)
{
  return __llts(a, b) | (__lles(a, b) << 1) | (__lgts(a, b) << 2) | (__lges(a, b) << 3);
}

int luflags(unsigned long a, unsigned long b)
{
  return __lltu(a, b) | (__lleu(a, b) << 1) | (__lgtu(a, b) << 2) | (__lgeu(a, b) << 3);
}

// float comparison routines -> bit mask: 1 lt, 2 le, 4 gt, 8 ge, 16 eq, 32 ne
int fflags(float a, float b)
{
  return __flt(a, b) | (__fle(a, b) << 1) | (__fgt(a, b) << 2) | (__fge(a, b) << 3) | (__feq(a, b) << 4) | (__fne(a, b) << 5);
}

#define LT 3
#define GT 12
#define EQ 10

void test16()
{
  // __mul, __divs, __mods, __divu, __modu, __udivmod
  check(__mul(0x8000, 2), 0);
  check(__mul(-3, 7), -21);
  check(__mul(255, 257), -1);              // 0xFFFF
  check(__mul(0, 1234), 0);
  check(__divs(1000, -7), -142);
  check(__mods(1000, -7), 6);
  check(__divs(-1000, -7), 142);
  check(__mods(-1000, -7), -6);
  check(__divs(7, 7), 1);
  check(__divs(3, 7), 0);
  check(__mods(3, 7), 3);
  check(__divu(5, 0), -1);                 // division by zero: 0xFFFF
  check(__modu(5, 0), 5);                  // division by zero: the dividend
  check(__divu(0xFFFF, 1), -1);
  check(__modu(0xFFFF, 0x1000), 0x0FFF);
  check(__divu(0x8000, 3), 10922);         // 2AAA
  check(__modu(0x8000, 3), 2);
  check(__udivmod(1000, 7, 0), 142);
  check(__udivmod(1000, 7, 1), 6);
  check(__udivmod(0xFFFF, 0xFFFF, 0), 1);
  check(__udivmod(0xFFFF, 0xFFFF, 1), 0);
  check(__udivmod(0, 5, 0), 0);
  check(__udivmod(5, 0, 0), -1);
  check(__udivmod(5, 0, 1), 5);
  check(__udivmod(0x8000, 0x7FFF, 0), 1);
  check(__udivmod(0x8000, 0x7FFF, 1), 1);

  // shifts: counts 0, 15 and 16 or more
  check(__shl(0x1234, 0), 0x1234);
  check(__shl(1, 15), 0x8000 - 0x10000);   // 8000
  check(__shl(0xFFFF, 16), 0);
  check(__shru(0x8000, 15), 1);
  check(__shru(0xFFFF, 16), 0);
  check(__shru(0xFFFF, 0), -1);
  check(__shrs(0x8000 - 0x10000, 15), -1);
  check(__shrs(0x4000, 15), 0);
  check(__shrs(-2, 1), -1);
  check(__shrs(-32768, 16), -1);           // count >= 15: sign fill
  check(__shrs(32767, 40), 0);
  check(__shrs(-5, 0), -5);

  // comparisons over the full range (never the "subtract and test the sign" shortcut)
  check(sflags(-5, 3), LT);
  check(sflags(3, -5), GT);
  check(sflags(7, 7), EQ);
  check(sflags(-32768, 32767), LT);        // the old lowering got this one wrong
  check(sflags(32767, -32768), GT);
  check(sflags(-32768, -32768), EQ);
  check(uflags(0, 0xFFFF), LT);
  check(uflags(0xFFFF, 0), GT);
  check(uflags(0x8000, 0x7FFF), GT);       // a signed compare would say less
  check(uflags(0x7FFF, 0x8000), LT);
  check(uflags(0x1234, 0x1234), EQ);
  check(__cmps(-1, 1), -1);
  check(__cmps(5, 5), 0);
  check(__cmps(7, 3), 1);
  check(__cmps(-32768, 32767), -1);
  check(__cmpu(1, 0xFFFF), -1);
  check(__cmpu(0xFFFF, 1), 1);
  check(__cmpu(4, 4), 0);
  check(__cmpu(0x7FFF, 0x8000), -1);
}

void testlong()
{
  // __lmul, __ldivs, __lmods, __ldivu, __lmodu
  checkl(__lmul(-3, 7), -21);
  checkul(__lmul(0x10000, 0x10000), 0);
  checkul(__lmul(65535, 65537), 0xFFFFFFFF);
  checkl(__lmul(123456, 0), 0);
  checkl(__ldivs(-7, 2), -3);
  checkl(__ldivs(7, -2), -3);
  checkl(__ldivs(-7, -2), 3);
  checkl(__lmods(-7, 2), -1);
  checkl(__lmods(7, -2), 1);
  checkl(__lmods(-7, -2), -1);
  checkl(__ldivs(-2147483647 - 1, 2), -1073741824);
  checkul(__ldivu(0xFFFFFFFF, 0x10000), 0xFFFF);
  checkul(__lmodu(0xFFFFFFFF, 0x10000), 0xFFFF);
  checkul(__ldivu(0xFFFFFFFF, 0xFFFFFFFF), 1);
  checkul(__ldivu(1, 0xFFFFFFFF), 0);
  checkul(__lmodu(1, 0xFFFFFFFF), 1);
  checkul(__ldivu(5, 0), 0xFFFFFFFF);      // division by zero
  checkul(__lmodu(5, 0), 5);
  checkul(__ldivu(0x80000000, 3), 0x2AAAAAAA);
  checkul(__lmodu(0x80000000, 3), 2);
  checkul(__ldivu(100000, 100), 1000);

  // shifts: counts 0, 8, 16, 31, 32 and beyond
  checkul(__lshl(0x12345678, 0), 0x12345678);
  checkul(__lshl(0x12345678, 8), 0x34567800);
  checkul(__lshl(0x12345678, 16), 0x56780000);
  checkul(__lshl(1, 31), 0x80000000);
  checkul(__lshl(0xFFFFFFFF, 32), 0);
  checkul(__lshru(0x12345678, 0), 0x12345678);
  checkul(__lshru(0x12345678, 8), 0x00123456);
  checkul(__lshru(0x12345678, 16), 0x00001234);
  checkul(__lshru(0x80000000, 31), 1);
  checkul(__lshru(0xFFFFFFFF, 40), 0);
  checkl(__lshrs(0x12345678, 8), 0x00123456);
  checkl(__lshrs(-1000000, 0), -1000000);
  checkl(__lshrs(-1000000, 16), -16);
  checkl(__lshrs(-2147483647 - 1, 31), -1);
  checkl(__lshrs(-5, 40), -1);             // count >= 32: sign fill
  checkl(__lshrs(0x7FFFFFFF, 40), 0);

  // comparisons over the full 32-bit range
  check(lsflags(-1, 1), LT);
  check(lsflags(1, -1), GT);
  check(lsflags(100000, 100000), EQ);
  check(lsflags(0x7FFFFFFF, -2147483647 - 1), GT);
  check(lsflags(-2147483647 - 1, 0x7FFFFFFF), LT);
  check(lsflags(0x00010002, 0x00010003), LT);   // equal high words: the low words decide, unsigned
  check(lsflags(0x00018000, 0x00017FFF), GT);
  check(luflags(0xFFFFFFFF, 1), GT);       // a signed compare would say less
  check(luflags(1, 0xFFFFFFFF), LT);
  check(luflags(0x80000000, 0x7FFFFFFF), GT);
  check(luflags(0x12345678, 0x12345678), EQ);
  check(luflags(0x00010000, 0x0001FFFF), LT);
  check(__lcmps(-5, 3), -1);
  check(__lcmps(3, 3), 0);
  check(__lcmps(0x7FFFFFFF, -2147483647 - 1), 1);
  check(__lcmps(0x00010002, 0x00010003), -1);
  check(__lcmps(0x00010003, 0x00010002), 1);
  check(__lcmpu(0x7FFFFFFF, 0x80000000), -1);
  check(__lcmpu(0xFFFFFFFF, 1), 1);
  check(__lcmpu(7, 7), 0);
}

void testfloat()
{
  float nan = mkf(0x7FC00000);
  float pinf = mkf(0x7F800000);
  float ninf = mkf(0xFF800000);
  float nzero = mkf(0x80000000);
  float zero = mkf(0);
  float one = mkf(0x3F800000);
  float two = mkf(0x40000000);
  float mone = mkf(0xBF800000);
  float mtwo = mkf(0xC0000000);

  // __ltof / __ultof: exact values, rounding to nearest with ties to even, the extremes
  checkul(fbits(__ltof(0)), 0);
  checkul(fbits(__ltof(1)), 0x3F800000);
  checkul(fbits(__ltof(-1)), 0xBF800000);
  checkul(fbits(__ltof(100)), 0x42C80000);
  checkul(fbits(__ltof(16777216)), 0x4B800000);        // 2^24
  checkul(fbits(__ltof(16777217)), 0x4B800000);        // tie -> even (down)
  checkul(fbits(__ltof(16777219)), 0x4B800002);        // tie -> even (up)
  checkul(fbits(__ltof(16777218)), 0x4B800001);        // exact
  checkul(fbits(__ltof(2147483647)), 0x4F000000);      // rounds up to 2^31
  checkul(fbits(__ltof(-2147483647 - 1)), 0xCF000000); // -2^31
  checkul(fbits(__ltof(-16777217)), 0xCB800000);
  checkul(fbits(__ultof(0)), 0);
  checkul(fbits(__ultof(1)), 0x3F800000);
  checkul(fbits(__ultof(0x80000001)), 0x4F000000);
  checkul(fbits(__ultof(0xFFFFFF80)), 0x4F800000);     // tie between 2^32-256 and 2^32 -> even (2^32)
  checkul(fbits(__ultof(0xFFFFFF00)), 0x4F7FFFFF);     // 2^32 - 256, exact
  checkul(fbits(__ultof(0xFFFFFFFF)), 0x4F800000);

  // __ftol / __ftoul: truncation toward zero, saturation, NaN, negative values
  checkl(__ftol(mkf(0x3F000000)), 0);                  // 0.5
  checkl(__ftol(mkf(0xBF000000)), 0);                  // -0.5
  checkl(__ftol(mkf(0x3FC00000)), 1);                  // 1.5
  checkl(__ftol(mkf(0xBFC00000)), -1);                 // -1.5
  checkl(__ftol(mkf(0x47F12000)), 123456);             // 123456.0 exactly
  checkl(__ftol(mkf(0x47C35000)), 100000);
  checkl(__ftol(mkf(0x4F000000)), 0x7FFFFFFF);         // 2^31 saturates
  checkl(__ftol(mkf(0xCF000000)), -2147483647 - 1);    // -2^31 is exact
  checkl(__ftol(mkf(0xCF800000)), -2147483647 - 1);    // -2^32 saturates
  checkl(__ftol(mkf(0x4F7FFFFF)), 0x7FFFFFFF);
  checkl(__ftol(mkf(0x4EFFFFFF)), 2147483520);         // 2^31 - 128, the largest float below 2^31
  checkl(__ftol(pinf), 0x7FFFFFFF);
  checkl(__ftol(ninf), -2147483647 - 1);
  checkl(__ftol(nan), 0);
  checkl(__ftol(nzero), 0);
  checkul(__ftoul(one), 1);
  checkul(__ftoul(mkf(0x4F000000)), 0x80000000);       // 2^31
  checkul(__ftoul(mkf(0x4F7FFFFF)), 0xFFFFFF00);       // 2^32 - 256
  checkul(__ftoul(mkf(0x4F800000)), 0xFFFFFFFF);       // 2^32 saturates
  checkul(__ftoul(pinf), 0xFFFFFFFF);
  checkul(__ftoul(nan), 0);
  checkul(__ftoul(mkf(0x3F000000)), 0);
  checkul(__ftoul(mone), 0xFFFFFFFF);                  // a negative value wraps
  checkul(__ftoul(mtwo), 0xFFFFFFFE);

  // __flt __fle __fgt __fge __feq __fne: ordered values, signed zeros, infinities, NaN
  check(fflags(one, two), 0x23);           // lt le ne
  check(fflags(two, one), 0x2C);           // gt ge ne
  check(fflags(one, one), 0x1A);           // le ge eq
  check(fflags(mone, one), 0x23);
  check(fflags(one, mone), 0x2C);
  check(fflags(mtwo, mone), 0x23);
  check(fflags(mone, mtwo), 0x2C);
  check(fflags(zero, nzero), 0x1A);        // +0.0 == -0.0
  check(fflags(nzero, zero), 0x1A);
  check(fflags(nzero, nzero), 0x1A);
  check(fflags(ninf, pinf), 0x23);
  check(fflags(pinf, one), 0x2C);
  check(fflags(ninf, mtwo), 0x23);
  check(fflags(pinf, pinf), 0x1A);
  check(fflags(nan, one), 0x20);           // unordered: only "not equal"
  check(fflags(one, nan), 0x20);
  check(fflags(nan, nan), 0x20);
  check(fflags(nan, pinf), 0x20);
  check(fflags(mkf(0x7F800001), zero), 0x20);  // a signalling NaN pattern is a NaN too
  check(fflags(mkf(0xFFC00000), mone), 0x20);  // a negative NaN
  check(fflags(mkf(0x00800000), zero), 0x2C);  // the smallest normal number is above zero
  check(fflags(mkf(0x80800000), zero), 0x23);

  // the compiler's own float operators on top of the library
  check(one < two, 1);
  check(mone >= mtwo, 1);
  check(nan == nan, 0);
  check(nan != nan, 1);
  check(zero == nzero, 1);
}

void main()
{
  test16();
  testlong();
  testfloat();
  done = 0x600D;
}
