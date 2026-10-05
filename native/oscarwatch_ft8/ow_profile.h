#ifndef OW_PROFILE_H
#define OW_PROFILE_H

/* Per-stage wall-clock for the bench build only (OW_FT8_PROFILE). The shipped
   library compiles every macro here to nothing. */

enum
{
    OW_PROF_MONITOR,
    OW_PROF_CANDIDATES,
    OW_PROF_LDPC,
    OW_PROF_AP,
    OW_PROF_SUBTRACT,
    OW_PROF_RECURSE,
    OW_PROF_COUNT
};

#ifdef OW_FT8_PROFILE

#ifdef _WIN32
#include <windows.h>
static double ow_prof_now(void)
{
    LARGE_INTEGER f, c;
    QueryPerformanceFrequency(&f);
    QueryPerformanceCounter(&c);
    return (double)c.QuadPart / (double)f.QuadPart;
}
#else
#include <time.h>
static double ow_prof_now(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (double)ts.tv_sec + 1e-9 * (double)ts.tv_nsec;
}
#endif

extern double g_ow_prof[OW_PROF_COUNT];
#define OW_PROF_BEGIN(v) double v = ow_prof_now()
#define OW_PROF_END(slot, v) (g_ow_prof[slot] += ow_prof_now() - (v))

#else

#define OW_PROF_BEGIN(v) ((void)0)
#define OW_PROF_END(slot, v) ((void)0)

#endif

#endif /* OW_PROFILE_H */
