#ifndef STDLIB_H
#define STDLIB_H

#include <stddef.h>

extern __fastcall void *malloc(size_t size);
extern __fastcall void *calloc(size_t nmemb, size_t size);
extern __fastcall void *realloc(void *ptr, size_t size);
extern __fastcall void free(void *ptr);


#endif
