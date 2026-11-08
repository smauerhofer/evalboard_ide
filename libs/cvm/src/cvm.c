#include "cvm.h"

/*
 * CVM runtime library. See cvm.h for the contract.
 *
 * Every routine the compiler calls for '*', '/', '%', a shift by a run-time count and the ordered
 * comparisons ('<', '<=', '>', '>=') is hand-written CVM assembler in asm/cvmmath.asm. Nothing is left to
 * implement in C here; this file only keeps the library's C side (the declarations in cvm.h) compiled.
 */
