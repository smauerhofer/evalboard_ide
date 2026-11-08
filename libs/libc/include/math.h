#ifndef MATH_H
#define MATH_H


/* trigonometric */
float acosf(float x);
float asinf(float x);
float atanf(float x);
float atan2f(float y, float x);

float cosf(float x);
float sinf(float x);
float tanf(float x);


/* hyperbolic */
float acoshf(float x);
float asinhf(float x);
float atanhf(float x);

float coshf(float x);
float sinhf(float x);
float tanhf(float x);


/* exponential and logarithmic */
float expf(float x);
float exp2f(float x);
float expm1f(float x);

float logf(float x);
float log10f(float x);
float log1pf(float x);
float log2f(float x);
float logbf(float x);

int ilogbf(float x);


/* exponent manipulation */
float frexpf(float x, int *exp);
float ldexpf(float x, int exp);
float scalbnf(float x, int n);
float scalblnf(float x, long n);


/* power */
float cbrtf(float x);
float hypotf(float x, float y);
float powf(float x, float y);
float sqrtf(float x);


/* absolute value */
__optimize float fabsf(float x);


/* error and gamma */
float erff(float x);
float erfcf(float x);

float lgammaf(float x);
float tgammaf(float x);


/* rounding */
float ceilf(float x);
float floorf(float x);
float nearbyintf(float x);
float rintf(float x);
float roundf(float x);
float truncf(float x);

long lrintf(float x);
long lroundf(float x);

// long long llrintf(float x);
// long long llroundf(float x);


/* remainder */
float fmodf(float x, float y);
float remainderf(float x, float y);
float remquof(float x, float y, int *quo);


/* decomposition */
float modff(float x, float *iptr);


/* sign manipulation */
float copysignf(float x, float y);


/* neighboring values */
float nextafterf(float x, float y);


/* difference / min / max */
float fdimf(float x, float y);
float fmaxf(float x, float y);
float fminf(float x, float y);


/* fused multiply-add */
float fmaf(float x, float y, float z);


/* NaN construction */
float nanf(const char *tagp);

#endif
