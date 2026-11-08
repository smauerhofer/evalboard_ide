// selftest4.c -- fourth hardware self-test for the CVM C compiler: "struct". It exercises everything the compiler supports
// for structs: layout and sizeof (word-granular, no padding), member access with "." and "->", members of every kind
// (int, unsigned char, long, float, pointer, array, nested struct), structs as locals, globals, array elements and
// through pointers, pointer arithmetic over struct arrays, self-referential structs (linked list, tree), and structs
// holding arrays of structs. The raw memory layout is checked by reading a struct back as plain words.
//
// Not covered because this compiler does not support them (it reports an error): whole-struct assignment, passing or
// returning a struct by value, struct initialiser lists, union, pointer difference (p - q) and anonymous / typedef'd
// struct definitions.
//
// Like the other self-tests, every check stores the VALUE it computed in a static (page 2, so it shows up as a
// WRITE 2:xxxx in the hardware trace) and counts a mismatch in "failed". When main() returns, "failed" should be 0 and
// "done" 0x600D. Results are appended in call order: check() fills res[], checkl()/checkul() fill lres[] (two words
// each, low word first). Floats are checked by their IEEE bit pattern. Static layout (page 2, in declaration order):
//   res[128]     2:0000..007F   16-bit results (122 are used, the tail stays 0)
//   failed       2:0080
//   done         2:0081
//   lres[16]     2:0082..00A1   32-bit results, two words each, low word first (6 are used)
//   ri, li       2:00A2, 2:00A3 next free slot of res[] / lres[] (end values 122 and 6)
//   gp 2:00A4 (2 words), garr 2:00A6 (10), gbig 2:00B0 (14), pool 2:00BE (18), gwrap 2:00D0 (7), gtree 2:00D7 (25),
//   nodesUsed 2:00F0, then the linker's heap_start marker 2:00F1 (value 000200F3).
#include "cvm.h"

struct Pt
{
  int x;
  int y;
};

struct Big
{
  int id;
  unsigned char tag;
  long total;
  float ratio;
  struct Pt pos;
  int hist[4];
  struct Pt *link;
};

struct Node
{
  int val;
  struct Node *next;
};

struct Wrap
{
  struct Pt pts[3];
  int n;
};

struct Tree
{
  int val;
  struct Tree *left;
  struct Tree *right;
};

static int res[128];
static int failed = 0;
static int done = 0;
static long lres[16];
static int ri = 0;
static int li = 0;

static struct Pt gp;
static struct Pt garr[5];
static struct Big gbig;
static struct Node pool[6];
static struct Wrap gwrap;
static struct Tree gtree[5];
static int nodesUsed = 0;

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

unsigned long fbits(float f)
{
  unsigned long *p = (unsigned long *)&f;
  return *p;
}

// --- helpers that take a struct pointer
int sumPt(struct Pt *p)
{
  return p->x + p->y;
}

void setPt(struct Pt *p, int x, int y)
{
  p->x = x;
  p->y = y;
}

void swapPt(struct Pt *p)
{
  int t = p->x;
  p->x = p->y;
  p->y = t;
}

int sumArr(struct Pt *a, int n)
{
  int s = 0;
  int i;
  for (i = 0; i < n; i++)
  {
    s = s + a[i].x * 10 + a[i].y;
  }

  return s;
}

int sumArrPtr(struct Pt *a, int n)
{
  int s = 0;
  while (n > 0)
  {
    s = s + a->x + a->y * 2;
    a++;
    n--;
  }

  return s;
}

struct Node *newNode(int val)
{
  struct Node *n = &pool[nodesUsed];
  nodesUsed = nodesUsed + 1;
  n->val = val;
  n->next = 0;
  return n;
}

int listSum(struct Node *n)
{
  int s = 0;
  while (n != 0)
  {
    s = s + n->val;
    n = n->next;
  }

  return s;
}

struct Node *listReverse(struct Node *head)
{
  struct Node *prev = 0;
  while (head != 0)
  {
    struct Node *next = head->next;
    head->next = prev;
    prev = head;
    head = next;
  }

  return prev;
}

int treeSum(struct Tree *t)
{
  if (t == 0) return 0;
  return t->val + treeSum(t->left) + treeSum(t->right);
}

int treeDepth(struct Tree *t)
{
  if (t == 0) return 0;
  int l = treeDepth(t->left);
  int r = treeDepth(t->right);
  if (l > r) return l + 1;
  return r + 1;
}

// --- 1. layout, sizeof and raw memory
void testLayout()
{
  check(sizeof(struct Pt), 2);
  check(sizeof(struct Big), 14);           // 1 + 1 + 2 + 2 + 2 + 4 + 2
  check(sizeof(struct Node), 3);           // int + pointer (2 words)
  check(sizeof(struct Wrap), 7);
  check(sizeof(struct Tree), 5);
  check(sizeof(garr), 10);
  check(sizeof(garr) / sizeof(garr[0]), 5);
  check(sizeof(gbig.hist), 4);
  check(sizeof(gbig.pos), 2);
  check(sizeof(pool[0]), 3);

  // no padding: the members sit back to back, in declaration order; read the struct back as raw words
  struct Pt p;
  p.x = 0x1111;
  p.y = 0x2222;
  int *raw = (int *)&p;
  check(raw[0], 0x1111);
  check(raw[1], 0x2222);

  struct Big *b = &gbig;
  b->id = 0x0101;
  b->tag = 0x02;
  b->total = 0x00040003;                   // words 2 and 3: 0003, 0004
  b->pos.x = 0x0A0A;                       // words 6 and 7 (after the 2-word float)
  b->pos.y = 0x0B0B;
  b->hist[0] = 0x0C0C;
  b->hist[3] = 0x0D0D;
  int *w = (int *)b;
  check(w[0], 0x0101);
  check(w[1], 0x0002);
  check(w[2], 0x0003);
  check(w[3], 0x0004);
  check(w[6], 0x0A0A);
  check(w[7], 0x0B0B);
  check(w[8], 0x0C0C);
  check(w[11], 0x0D0D);

  // member addresses (a pointer cast to int keeps the word address)
  check((int)&b->tag - (int)b, 1);
  check((int)&b->total - (int)b, 2);
  check((int)&b->ratio - (int)b, 4);
  check((int)&b->pos - (int)b, 6);
  check((int)&b->hist - (int)b, 8);
  check((int)&b->link - (int)b, 12);
  check((int)&garr[3] - (int)&garr[0], 6);     // an array of structs steps by the struct size
}

// --- 2. member access through "." and "->", local and global
void testMembers()
{
  struct Pt l;
  l.x = 3;
  l.y = 4;
  check(l.x * 10 + l.y, 34);
  check(sumPt(&l), 7);
  swapPt(&l);
  check(l.x * 10 + l.y, 43);

  gp.x = 100;
  gp.y = -7;
  check(gp.x + gp.y, 93);
  setPt(&gp, 5, 6);
  check(gp.x * 10 + gp.y, 56);

  struct Pt *pp = &l;
  pp->x = 11;
  l.y = 12;
  check(pp->x + pp->y, 23);
  check((*pp).y, 12);

  // member compound assignment, increment and decrement
  l.x += 5;
  l.y -= 2;
  l.x++;
  --l.y;
  pp->x *= 2;
  check(l.x, 34);                          // ((11 + 5) + 1) * 2
  check(l.y, 9);                           // 12 - 2 - 1

  // two independent structs of the same type
  struct Pt a;
  struct Pt b;
  setPt(&a, 1, 2);
  setPt(&b, 30, 40);
  check(sumPt(&a) * 100 + sumPt(&b), 370);
  a.x = b.y;
  b.x = a.y;
  check(a.x * 100 + b.x, 4002);
}

// --- 3. members of every kind: unsigned char, long, float, pointer, array, nested struct
void testMemberKinds()
{
  struct Big *b = &gbig;
  b->id = 7;
  b->tag = 200;
  b->total = 100000;
  b->ratio = 1.5;
  b->pos.x = 8;
  b->pos.y = 9;
  b->link = &gp;

  check(b->id, 7);
  check(b->tag, 200);
  checkl(b->total, 100000);
  b->total = b->total * 3 + 5;
  checkl(b->total, 300005);
  b->total += 70000;
  checkl(b->total, 370005);
  checkul(fbits(b->ratio), 0x3FC00000);    // 1.5
  b->ratio = b->ratio * 2.0 + 0.5;
  checkul(fbits(b->ratio), 0x40600000);    // 3.5

  // nested struct, read and written through the outer one
  check(b->pos.x * 10 + b->pos.y, 89);
  b->pos.y = b->pos.x + b->pos.y;
  check(b->pos.y, 17);
  check(sumPt(&b->pos), 25);
  setPt(&b->pos, 40, 50);
  check(b->pos.x + b->pos.y, 90);

  // a pointer member, and members reached through it
  gp.x = 21;
  gp.y = 22;
  check(b->link->x + b->link->y, 43);
  b->link->y = 60;
  check(gp.y, 60);                         // the write through the member pointer reached gp
  check(b->link == &gp, 1);

  // an array member with constant and run-time indices
  int i;
  for (i = 0; i < 4; i++)
  {
    b->hist[i] = (i + 1) * 11;
  }

  check(b->hist[0], 11);
  check(b->hist[3], 44);
  int total = 0;
  for (i = 0; i < 4; i++)
  {
    total = total + b->hist[i];
  }

  check(total, 110);

  // a local struct with the same members
  struct Big loc;
  loc.id = 9;
  loc.total = -5;
  loc.pos.x = 1;
  loc.hist[2] = 77;
  loc.tag = 255;
  check(loc.id + loc.pos.x + loc.hist[2], 87);
  checkl(loc.total, -5);
  check(loc.tag, 255);
  check(b->id, 7);                         // the global one is untouched
}

// --- 4. arrays of structs: indexing, address-of, pointer arithmetic
void testArrays()
{
  int i;
  for (i = 0; i < 5; i++)
  {
    garr[i].x = i + 1;
    garr[i].y = (i + 1) * 100;
  }

  check(garr[0].x, 1);
  check(garr[4].y, 500);
  check(garr[2].x + garr[2].y, 303);
  check(sumArr(garr, 5), 10 * 15 + 1500);  // 1650
  check(sumArr(&garr[2], 3), 10 * 12 + 1200);
  check(sumArrPtr(garr, 5), 15 + 2 * 1500);

  struct Pt *p = garr;
  p++;
  check(p->x, 2);
  p = p + 2;
  check(p->y, 400);
  p--;
  check(p->x, 3);
  p += 2;
  check(p->x, 5);
  p -= 4;
  check(p->x, 1);
  check((p + 3)->y, 400);
  check(p[2].y, 300);
  check(p == garr, 1);
  check(p < &garr[1], 1);
  check(&garr[4] > &garr[3], 1);
  check(&garr[2] == p + 2, 1);

  // writing through a pointer into the middle of the array
  p = &garr[3];
  p->x = 99;
  p[1].y = 98;
  check(garr[3].x, 99);
  check(garr[4].y, 98);
  check(garr[2].x, 3);                     // the neighbours are unchanged
  check(garr[3].y, 400);

  // run-time index into an array of structs, read and written
  int k = 2;
  garr[k].y = garr[k + 1].x + garr[k - 1].x;     // 99 + 2
  check(garr[2].y, 101);
  garr[k + 2].x = garr[k].y;
  check(garr[4].x, 101);

  // a local array of structs
  struct Pt loc[4];
  for (i = 0; i < 4; i++)
  {
    setPt(&loc[i], i, 10 - i);
  }

  check(loc[0].x + loc[3].y, 7);
  check(sumArr(loc, 4), 10 * 6 + 34);
  check(sumArrPtr(&loc[1], 3), (1 + 9 * 2) + (2 + 8 * 2) + (3 + 7 * 2));

  // a bubble sort of the array by member y, swapping member by member
  garr[0].y = 50;
  garr[1].y = 10;
  garr[2].y = 40;
  garr[3].y = 20;
  garr[4].y = 30;
  int swapped = 1;
  while (swapped)
  {
    swapped = 0;
    for (i = 0; i < 4; i++)
    {
      if (garr[i].y > garr[i + 1].y)
      {
        int ty = garr[i].y;
        int tx = garr[i].x;
        garr[i].y = garr[i + 1].y;
        garr[i].x = garr[i + 1].x;
        garr[i + 1].y = ty;
        garr[i + 1].x = tx;
        swapped = 1;
      }
    }
  }

  check(garr[0].y, 10);
  check(garr[1].y, 20);
  check(garr[2].y, 30);
  check(garr[3].y, 40);
  check(garr[4].y, 50);
  check(garr[0].x, 2);                     // every x travelled with its y
  check(garr[1].x, 99);
  check(garr[2].x, 101);
  check(garr[3].x, 3);
  check(garr[4].x, 1);
}

// --- 5. struct containing an array of structs
void testNested()
{
  struct Wrap w;
  int i;
  w.n = 3;
  for (i = 0; i < w.n; i++)
  {
    w.pts[i].x = i * 2;
    w.pts[i].y = i * 3 + 1;
  }

  check(w.pts[0].x + w.pts[0].y, 1);
  check(w.pts[1].x + w.pts[1].y, 6);
  check(w.pts[2].x + w.pts[2].y, 11);
  check(sumArr(w.pts, w.n), 10 * 6 + 12);

  struct Wrap *pw = &gwrap;
  pw->n = 2;
  pw->pts[1].y = 55;
  pw->pts[0].x = 44;
  setPt(&pw->pts[2], 7, 8);
  check(gwrap.pts[1].y, 55);
  check(gwrap.pts[0].x, 44);
  check(gwrap.pts[2].x * 10 + gwrap.pts[2].y, 78);
  check(pw->n, 2);
  struct Pt *q = &pw->pts[1];
  q->x = 66;
  check(pw->pts[1].x, 66);
  check(sumPt(q), 66 + 55);
  int *raw = (int *)pw;
  check(raw[2], 66);                       // pts[1].x is word 2
  check(raw[6], 2);                        // n is the last word
}

// --- 6. self-referential structs: a linked list and a binary tree
void testLinked()
{
  nodesUsed = 0;
  struct Node *head = newNode(1);
  struct Node *n2 = newNode(2);
  struct Node *n3 = newNode(3);
  struct Node *n4 = newNode(4);
  head->next = n2;
  n2->next = n3;
  n3->next = n4;

  check(listSum(head), 10);
  check(head->next->next->val, 3);
  check(head->next->next->next->val, 4);
  check(head->next->next->next->next == 0, 1);
  check(nodesUsed, 4);

  head = listReverse(head);
  check(head->val, 4);
  check(head->next->val, 3);
  check(head->next->next->next->val, 1);
  check(head->next->next->next->next == 0, 1);
  check(listSum(head), 10);

  // insert a node after the second one
  struct Node *extra = newNode(50);
  extra->next = head->next->next;
  head->next->next = extra;
  check(listSum(head), 60);
  check(head->next->next->val, 50);
  check(head->next->next->next->val, 2);

  // a tree in the global array: 1 at the root, children 2 and 3, 2 has children 4 and 5
  int i;
  for (i = 0; i < 5; i++)
  {
    gtree[i].val = i + 1;
    gtree[i].left = 0;
    gtree[i].right = 0;
  }

  gtree[0].left = &gtree[1];
  gtree[0].right = &gtree[2];
  gtree[1].left = &gtree[3];
  gtree[1].right = &gtree[4];
  check(treeSum(&gtree[0]), 15);
  check(treeSum(&gtree[1]), 11);
  check(treeSum(0), 0);
  check(treeDepth(&gtree[0]), 3);
  check(treeDepth(&gtree[2]), 1);
  check(gtree[0].left->right->val, 5);
  check(gtree[0].right->left == 0, 1);
  gtree[2].left = &gtree[0];               // a cycle is fine for pointers: follow it three links
  check(gtree[0].right->left->right->val, 3);
}

void main()
{
  testLayout();
  testMembers();
  testMemberKinds();
  testArrays();
  testNested();
  testLinked();
  done = 0x600D;
}
