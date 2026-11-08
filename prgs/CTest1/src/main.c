// selftest.c -- small hardware self-test for the CVM C compiler and runtime library.
//
// Every check stores the VALUE it computed in a static result variable (page 2, so it shows up as a WRITE 2:xxxx in the
// hardware trace) and counts a mismatch in "failed". When main() returns, "failed" should be 0 and "done" 0x600D.
// Static layout (page 2, in declaration order): res[0..15] at 2:0000.., then failed, done, lres[0..5] (two words each,
// low word first), fres[0..3] (two words each, low word first).
#include "cvm.h"

static int res[16];
static int failed = 0;
static int done = 0;
static long lres[6];
static float fres[4];

// --- helpers with stack parameters (the default calling convention): they read their parameters at fp+3...
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

void checkf(int i, float got, float expected)
{
  fres[i] = got;
  if (got != expected) failed = failed + 1;
}

// --- stack parameters, locals, nested and recursive calls
int sum3(int p, int q, int r)
{
  int t[3];
  t[0] = p; t[1] = q; t[2] = r;
  return t[0] + t[1] * 2 + t[2] * 3;
}

int deep(int n)
{
  int loc[4];
  loc[0] = n;
  loc[3] = n + 1;
  if (n > 0) deep(n - 1);
  return loc[0] * 10 + loc[3];
}

int manyargs(int a1, int a2, int a3, int a4, int a5, int a6)
{
  int z = a6 - a1;
  return z * 100 + a2 + a3 + a4 + a5;
}

long lmix(long u, int v, long w)
{
  long k = u + w;
  return k + sum3(v, v, v);
}

float fmix(float f, int i, float g)
{
  float h = f * g;
  return h + i;
}

void main()
{
  // 0..3: frame layout
  check(0, sum3(1, 2, 3), 14);
  check(1, deep(3), 34);
  check(2, manyargs(1, 2, 3, 4, 5, 9), 814);
  int x = 5;
  int y = sum3(x, x + 1, x + 2);
  check(3, x * 1000 + y, 5000 + 5 + 12 + 21);

  // 4..8: 16-bit runtime library (cvmmath.asm)
  int m = 123;
  int n = 1000;
  check(4, m * 5, 615);
  check(5, n / 7, 142);
  check(6, n % 7, 6);
  check(7, -5 < 3, 1);
  unsigned int u = 0xFFFF;
  check(8, u > 1, 1);

  // long (cvmlong.asm)
  long a = 100000;
  long b = 23456;
  checkl(0, a + b, 123456);
  checkl(1, a * 3, 300000);
  long mil = 1000000;
  long seven = 7;
  checkl(2, mil / seven, 142857);
  checkl(4, mil % seven, 1);
  checkl(3, lmix(a, 2, b), 123456 + 12);
  long big = 70000;
  check(9, big > a, 0);
  check(10, big < a, 1);

  // float (cvmfloat.asm and the coprocessor opcodes)
  float f = 1.5;
  float g = 2.25;
  checkf(0, f + g, 3.75);
  checkf(1, f * 4.0, 6.0);
  checkf(2, fmix(f, 3, g), 6.375);
  int k = 7;
  checkf(3, (float)k, 7.0);
  float neg = -2.75;
  check(11, (int)neg, -2);
  check(12, f < g, 1);
  check(13, g <= f, 0);
  check(14, f == 1.5, 1);

  done = 0x600D;
}
