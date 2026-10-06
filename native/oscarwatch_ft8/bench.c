/* FT4 decoder bench: timing per stage and decode rate against synthetic slots.
   Built only with -DOW_FT8_BUILD_BENCH=ON. Noise follows the managed tests:
   the SNR is the signal power against noise in 2500 Hz. */

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "oscarwatch_ft8.h"
#include "ow_profile.h"

#include <ft8/constants.h>
#include <ft8/encode.h>
#include <ft8/message.h>

#define RATE 12000
#define SLOT_SAMPLES 90000
#define LEAD_SAMPLES 6000
#define NSPSYM 576

static int g_use_drift_api;
static float g_drift_max = 16.0f;
static int g_drift_steps = 8;
static float g_drift_f0 = 1500.0f;
static float g_birdie_amp = 0.15f;

static uint64_t rng_state;
static void rng_seed(uint64_t s) { rng_state = s * 0x9E3779B97F4A7C15ull + 1; }
static double rng_uniform(void)
{
    rng_state ^= rng_state << 13;
    rng_state ^= rng_state >> 7;
    rng_state ^= rng_state << 17;
    return ((rng_state >> 11) + 0.5) * (1.0 / 9007199254740992.0);
}
static double rng_gauss(void)
{
    double u1 = rng_uniform(), u2 = rng_uniform();
    return sqrt(-2.0 * log(u1)) * cos(2.0 * M_PI * u2);
}

static bool no_lookup(ftx_callsign_hash_type_t t, uint32_t h, char* c)
{
    (void)t;
    (void)h;
    c[0] = '\0';
    return false;
}
static void no_save(const char* c, uint32_t h)
{
    (void)c;
    (void)h;
}
static ftx_callsign_hash_interface_t bench_hash = { no_lookup, no_save };

/* FT4 GFSK burst with a linear frequency slide, added into out at start. */
static int add_ft4(float* out, int n_out, const char* text, float f0, float slope_hz_s, float amp, int start)
{
    ftx_message_t msg;
    if (ftx_message_encode(&msg, &bench_hash, text) != FTX_MESSAGE_RC_OK)
        return -1;
    uint8_t tones[FT4_NN];
    ft4_encode(msg.payload, tones);

    static float pulse[3 * NSPSYM];
    static int pulse_ready;
    if (!pulse_ready)
    {
        for (int i = 0; i < 3 * NSPSYM; ++i)
        {
            float t = i / (float)NSPSYM - 1.5f;
            float a1 = 5.336446f * (t + 0.5f), a2 = 5.336446f * (t - 0.5f);
            pulse[i] = (erff(a1) - erff(a2)) / 2;
        }
        pulse_ready = 1;
    }

    int n_wave = FT4_NN * NSPSYM;
    float* dphi = calloc((size_t)(n_wave + 2 * NSPSYM), sizeof(float));
    float peak = 2 * (float)M_PI / NSPSYM;
    for (int i = 0; i < FT4_NN; ++i)
        for (int j = 0; j < 3 * NSPSYM; ++j)
            dphi[j + i * NSPSYM] += peak * tones[i] * pulse[j];
    for (int j = 0; j < 2 * NSPSYM; ++j)
    {
        dphi[j] += peak * pulse[j + NSPSYM] * tones[0];
        dphi[j + FT4_NN * NSPSYM] += peak * pulse[j] * tones[FT4_NN - 1];
    }

    double phi = 0;
    int ramp = NSPSYM / 8;
    for (int k = 0; k < n_wave; ++k)
    {
        int n = start + k;
        double t = k / (double)RATE;
        double f = f0 + slope_hz_s * (t - 0.5 * n_wave / (double)RATE);
        float env = 1.0f;
        if (k < ramp)
            env = (1 - cosf((float)M_PI * k / ramp)) / 2;
        else if (k >= n_wave - ramp)
            env = (1 - cosf((float)M_PI * (n_wave - 1 - k) / ramp)) / 2;
        if (n >= 0 && n < n_out)
            out[n] += amp * env * (float)sin(phi);
        phi += dphi[k + NSPSYM] + 2 * M_PI * f / RATE;
    }
    free(dphi);
    return 0;
}

/* Noise sigma giving snr_db for a unit-amplitude sine (power 0.5). */
static float noise_sigma(float snr_db)
{
    double snr = pow(10.0, snr_db / 10.0);
    return (float)sqrt(0.5 * RATE / (snr * 5000.0));
}

static void add_noise(float* x, int n, float sigma)
{
    for (int i = 0; i < n; ++i)
        x[i] += sigma * (float)rng_gauss();
}

static int run_decode(const float* x, int n, int deep, const char* hints, float hint_hz, ow_ft8_decode_t* out, double* ms)
{
    double t0 = ow_prof_now();
    int r;
#ifndef OW_BENCH_NO_DRIFT
    if (g_use_drift_api)
        r = ow_ft8_decode_pcm_drift(x, n, RATE, 1, 200.f, 2800.f, out, OW_FT8_MAX_DECODES, deep,
            hints, hint_hz, hints ? 200.f : 0.f, g_drift_max, g_drift_steps, 0.0f, 0.0f);
    else
#endif
    if (hints)
        r = ow_ft8_decode_pcm_ap(x, n, RATE, 1, 200.f, 2800.f, out, OW_FT8_MAX_DECODES, deep, hints, hint_hz, 200.f);
    else
        r = ow_ft8_decode_pcm(x, n, RATE, 1, 200.f, 2800.f, out, OW_FT8_MAX_DECODES, deep);
    *ms += 1000.0 * (ow_prof_now() - t0);
    return r;
}

static void print_profile(int runs)
{
    static const char* names[OW_PROF_COUNT] = { "monitor", "candidates", "ldpc", "ap", "subtract", "recurse(total)" };
    printf("  per decode:");
    for (int i = 0; i < OW_PROF_COUNT; ++i)
        printf(" %s %.1f ms", names[i], 1000.0 * g_ow_prof[i] / runs);
    printf("\n");
    memset(g_ow_prof, 0, sizeof(g_ow_prof));
}

static void bench_sensitivity(int seeds, int deep)
{
    const char* text = "MM9SQL G4ABC RR73";
    const float snrs[] = { -14, -16, -17, -18, -19, -20 };
    float* x = malloc(SLOT_SAMPLES * sizeof(float));
    printf("sensitivity (%s, %d seeds)\n", deep ? "deep" : "fast", seeds);
    for (size_t s = 0; s < sizeof(snrs) / sizeof(snrs[0]); ++s)
    {
        int hits = 0, falses = 0;
        double ms = 0;
        for (int seed = 1; seed <= seeds; ++seed)
        {
            memset(x, 0, SLOT_SAMPLES * sizeof(float));
            add_ft4(x, SLOT_SAMPLES, text, 1500.f, 0.f, 1.f, LEAD_SAMPLES);
            rng_seed((uint64_t)seed * 1000 + s);
            add_noise(x, SLOT_SAMPLES, noise_sigma(snrs[s]));
            ow_ft8_decode_t out[OW_FT8_MAX_DECODES];
            int n = run_decode(x, SLOT_SAMPLES, deep, NULL, 0, out, &ms);
            for (int i = 0; i < n; ++i)
            {
                if (strcmp(out[i].text, text) == 0)
                    ++hits;
                else
                    ++falses;
            }
        }
        printf("  %5.0f dB: %3d/%d decoded, %d false, %.1f ms\n", snrs[s], hits, seeds, falses, ms / seeds);
    }
    print_profile(seeds * (int)(sizeof(snrs) / sizeof(snrs[0])));
    free(x);
}

static void bench_busy(int seeds, int deep)
{
    static const struct
    {
        const char* text;
        float hz;
        float snr;
        int delay;
    } sigs[] = {
        { "CQ K1ABC FN42", 600.f, -4.f, 0 },
        { "W9XYZ K1ABC -11", 1250.f, 2.f, 1200 },
        { "CQ JA1ABC PM95", 1262.f, -10.f, 600 },
        { "CQ DL1ABC JO62", 1600.f, -9.f, 2400 },
        { "F5ABC EA4XYZ R-08", 1950.f, -12.f, 300 },
        { "G4ABC MM9SQL RR73", 2300.f, -14.f, 1800 },
    };
    const int nsig = (int)(sizeof(sigs) / sizeof(sigs[0]));
    float* x = malloc(SLOT_SAMPLES * sizeof(float));
    int found[16] = { 0 };
    int falses = 0;
    double ms = 0;
    float sigma = noise_sigma(0.f);
    for (int seed = 1; seed <= seeds; ++seed)
    {
        memset(x, 0, SLOT_SAMPLES * sizeof(float));
        for (int i = 0; i < nsig; ++i)
            add_ft4(x, SLOT_SAMPLES, sigs[i].text, sigs[i].hz, 0.f, powf(10.f, sigs[i].snr / 20.f), LEAD_SAMPLES + sigs[i].delay);
        rng_seed((uint64_t)seed * 7919);
        add_noise(x, SLOT_SAMPLES, sigma);
        ow_ft8_decode_t out[OW_FT8_MAX_DECODES];
        int n = run_decode(x, SLOT_SAMPLES, deep, NULL, 0, out, &ms);
        for (int i = 0; i < n; ++i)
        {
            int match = 0;
            for (int j = 0; j < nsig; ++j)
                if (strcmp(out[i].text, sigs[j].text) == 0)
                {
                    ++found[j];
                    match = 1;
                }
            if (!match)
                ++falses;
        }
    }
    printf("busy band (%s, %d seeds), %.1f ms per decode, %d false\n", deep ? "deep" : "fast", seeds, ms / seeds, falses);
    for (int j = 0; j < nsig; ++j)
        printf("  %-20s %6.0f Hz %5.0f dB: %d/%d\n", sigs[j].text, sigs[j].hz, sigs[j].snr, found[j], seeds);
    print_profile(seeds);
    free(x);
}

/* Many stations at once, so the candidate budget runs out before every burst gets a look. */
static void bench_crowd(int seeds, int deep)
{
    enum { kStations = 18 };
    static const char* calls[kStations] = { "K1AA", "K2BB", "K3CC", "K4DD", "K5EE", "K6FF", "K7GG", "K8HH", "K9II",
        "W1JJ", "W2KK", "W3LL", "W4MM", "W5NN", "W6OO", "W7PP", "W8QQ", "W9RR" };
    char texts[kStations][32];
    for (int i = 0; i < kStations; ++i)
        snprintf(texts[i], sizeof(texts[i]), "CQ %s FN42", calls[i]);
    float* x = malloc(SLOT_SAMPLES * sizeof(float));
    int found = 0, falses = 0;
    double ms = 0;
    for (int seed = 1; seed <= seeds; ++seed)
    {
        memset(x, 0, SLOT_SAMPLES * sizeof(float));
        rng_seed((uint64_t)seed * 104729);
        for (int i = 0; i < kStations; ++i)
        {
            float hz = 300.f + 140.f * (float)i;
            float snr = -6.f - (float)(i % 5) * 2.f;
            add_ft4(x, SLOT_SAMPLES, texts[i], hz, 0.f, powf(10.f, snr / 20.f), LEAD_SAMPLES + (int)(rng_uniform() * 6000.0));
        }
        add_noise(x, SLOT_SAMPLES, noise_sigma(0.f));
        ow_ft8_decode_t out[OW_FT8_MAX_DECODES];
        int n = run_decode(x, SLOT_SAMPLES, deep, NULL, 0, out, &ms);
        for (int i = 0; i < n; ++i)
        {
            int match = 0;
            for (int j = 0; j < kStations; ++j)
                match |= strcmp(out[i].text, texts[j]) == 0;
            if (match)
                ++found;
            else
                ++falses;
        }
    }
    printf("crowded band (%s, %d seeds): %d/%d decoded, %d false, %.1f ms\n",
        deep ? "deep" : "fast", seeds, found, seeds * kStations, falses, ms / seeds);
    print_profile(seeds);
    free(x);
}

/* Transponder-like noise: passband sloping 12 dB across the band, plus steady
   birdies, one inside a signal's tone group and one beside another. */
static void bench_passband(int seeds, int deep)
{
    static const struct
    {
        const char* text;
        float hz;
        float snr;
    } sigs[] = {
        { "CQ K1ABC FN42", 500.f, -12.f },
        { "W9XYZ K1ABC -11", 1100.f, -12.f },
        { "CQ DL1ABC JO62", 1700.f, -12.f },
        { "G4ABC MM9SQL RR73", 2300.f, -12.f },
    };
    const float birdies[] = { 1100.f + 2.f * 20.833f, 1700.f - 30.f };
    const int nsig = (int)(sizeof(sigs) / sizeof(sigs[0]));
    float* x = malloc(SLOT_SAMPLES * sizeof(float));
    int found[8] = { 0 };
    int falses = 0;
    double ms = 0;
    for (int seed = 1; seed <= seeds; ++seed)
    {
        memset(x, 0, SLOT_SAMPLES * sizeof(float));
        rng_seed((uint64_t)seed * 6151);
        /* White noise through a one-pole low-pass, mixed with white noise:
           about 12 dB more noise at the bottom of the band than the top. */
        float sigma = noise_sigma(0.f);
        float lp = 0.f;
        for (int i = 0; i < SLOT_SAMPLES; ++i)
        {
            lp = 0.85f * lp + 0.15f * (float)rng_gauss();
            x[i] = 0.7f * sigma * (float)rng_gauss() + 1.5f * sigma * lp;
        }
        for (size_t b = 0; b < sizeof(birdies) / sizeof(birdies[0]); ++b)
        {
            float w = 2.f * (float)M_PI * birdies[b] / RATE;
            for (int i = 0; i < SLOT_SAMPLES; ++i)
                x[i] += g_birdie_amp * sinf(w * (float)i);
        }
        for (int i = 0; i < nsig; ++i)
            add_ft4(x, SLOT_SAMPLES, sigs[i].text, sigs[i].hz, 0.f, powf(10.f, sigs[i].snr / 20.f), LEAD_SAMPLES);
        ow_ft8_decode_t out[OW_FT8_MAX_DECODES];
        int n = run_decode(x, SLOT_SAMPLES, deep, NULL, 0, out, &ms);
        for (int i = 0; i < n; ++i)
        {
            int match = 0;
            for (int j = 0; j < nsig; ++j)
                if (strcmp(out[i].text, sigs[j].text) == 0)
                {
                    ++found[j];
                    match = 1;
                }
            if (!match)
                ++falses;
        }
    }
    printf("passband and birdies (%s, %d seeds), %.1f ms per decode, %d false\n", deep ? "deep" : "fast", seeds, ms / seeds, falses);
    for (int j = 0; j < nsig; ++j)
        printf("  %-20s %6.0f Hz: %d/%d\n", sigs[j].text, sigs[j].hz, found[j], seeds);
    print_profile(seeds);
    free(x);
}

static void bench_drift(int seeds, int deep, float snr)
{
    const char* text = "W9XYZ K1ABC -11";
    const float slopes[] = { 0, 2, 4, 6, 8, 10, 12, 14, 16, 20, -6, -10, -14 };
    float* x = malloc(SLOT_SAMPLES * sizeof(float));
    printf("drift coverage at %.0f dB (%s, %d seeds%s)\n", snr, deep ? "deep" : "fast", seeds, g_use_drift_api ? ", native drift search" : "");
    for (size_t s = 0; s < sizeof(slopes) / sizeof(slopes[0]); ++s)
    {
        int hits = 0, falses = 0;
        double ms = 0;
        for (int seed = 1; seed <= seeds; ++seed)
        {
            memset(x, 0, SLOT_SAMPLES * sizeof(float));
            add_ft4(x, SLOT_SAMPLES, text, g_drift_f0, slopes[s], 1.f, LEAD_SAMPLES);
            rng_seed((uint64_t)seed * 31 + s);
            add_noise(x, SLOT_SAMPLES, noise_sigma(snr));
            ow_ft8_decode_t out[OW_FT8_MAX_DECODES];
            int n = run_decode(x, SLOT_SAMPLES, deep, NULL, 0, out, &ms);
            for (int i = 0; i < n; ++i)
            {
                if (strcmp(out[i].text, text) == 0)
                    ++hits;
                else
                    ++falses;
            }
        }
        printf("  %+5.0f Hz/s: %2d/%d, %d false, %.1f ms\n", slopes[s], hits, seeds, falses, ms / seeds);
    }
    print_profile(seeds * (int)(sizeof(slopes) / sizeof(slopes[0])));
    free(x);
}

static char* build_hints(const char* my, const char* their)
{
    char* buf = malloc(16384);
    buf[0] = '\0';
    char line[64];
    for (int snr = -30; snr <= 40; ++snr)
    {
        snprintf(line, sizeof(line), "%s %s %+03d\n", their, my, snr);
        strcat(buf, line);
        snprintf(line, sizeof(line), "%s %s R%+03d\n", their, my, snr);
        strcat(buf, line);
    }
    snprintf(line, sizeof(line), "%s %s RR73\n%s %s 73\n%s %s RRR\n", their, my, their, my, their, my);
    strcat(buf, line);
    return buf;
}

static void bench_ap(int seeds)
{
    const char* text = "MM9SQL G4ABC RR73";
    char* hints = build_hints("G4ABC", "MM9SQL");
    float* x = malloc(SLOT_SAMPLES * sizeof(float));
    const float snrs[] = { -16, -17, -18, -20, -22 };
    printf("hinted replies (%d seeds)\n", seeds);
    for (size_t s = 0; s < sizeof(snrs) / sizeof(snrs[0]); ++s)
    {
        int plain = 0, hinted = 0, crc_hinted = 0, wrong = 0;
        double ms = 0;
        for (int seed = 1; seed <= seeds; ++seed)
        {
            memset(x, 0, SLOT_SAMPLES * sizeof(float));
            add_ft4(x, SLOT_SAMPLES, text, 1500.f, 0.f, 1.f, LEAD_SAMPLES);
            rng_seed((uint64_t)seed * 4242 + s);
            add_noise(x, SLOT_SAMPLES, noise_sigma(snrs[s]));
            ow_ft8_decode_t out[OW_FT8_MAX_DECODES];
            int n = run_decode(x, SLOT_SAMPLES, 0, hints, 1500.f, out, &ms);
            for (int i = 0; i < n; ++i)
            {
                if (strcmp(out[i].text, text) == 0)
                {
                    if (out[i].ap)
                        ++hinted;
                    else
                        ++plain;
                    if (out[i].ap == 2)
                        ++crc_hinted;
                }
                else
                    ++wrong;
            }
        }
        printf("  %5.0f dB: %d plain + %d hinted (%d CRC-checked) of %d, %d wrong, %.1f ms\n",
            snrs[s], plain, hinted, crc_hinted, seeds, wrong, ms / seeds);
    }

    int noise_hits = 0;
    int noise_seeds = seeds * 4;
    double ms = 0;
    for (int seed = 1; seed <= noise_seeds; ++seed)
    {
        rng_seed((uint64_t)seed * 99991);
        memset(x, 0, SLOT_SAMPLES * sizeof(float));
        add_noise(x, SLOT_SAMPLES, noise_sigma(0.f));
        ow_ft8_decode_t out[OW_FT8_MAX_DECODES];
        noise_hits += run_decode(x, SLOT_SAMPLES, 0, hints, 1500.f, out, &ms) > 0;
    }
    printf("  noise only: %d of %d slots produced a decode\n", noise_hits, noise_seeds);
    free(x);
    free(hints);
}

#ifndef _WIN32
#include <pthread.h>

static float* g_thread_slot;
static int g_thread_expect;

static void* thread_body(void* arg)
{
    long bad = 0;
    for (int r = 0; r < 4; ++r)
    {
        ow_ft8_decode_t out[OW_FT8_MAX_DECODES];
        int n = ow_ft8_decode_pcm(g_thread_slot, SLOT_SAMPLES, RATE, 1, 200.f, 2800.f, out, OW_FT8_MAX_DECODES, 0);
        if (n != g_thread_expect)
            ++bad;
    }
    (void)arg;
    return (void*)bad;
}

/* More threads than pool slots, all decoding the same slot at once. */
static void bench_threads(void)
{
    enum { kThreads = 12 };
    g_thread_slot = malloc(SLOT_SAMPLES * sizeof(float));
    memset(g_thread_slot, 0, SLOT_SAMPLES * sizeof(float));
    add_ft4(g_thread_slot, SLOT_SAMPLES, "CQ K1ABC FN42", 600.f, 0.f, 1.f, LEAD_SAMPLES);
    add_ft4(g_thread_slot, SLOT_SAMPLES, "W9XYZ K1ABC -11", 1250.f, 0.f, 1.f, LEAD_SAMPLES + 1200);
    add_ft4(g_thread_slot, SLOT_SAMPLES, "CQ JA1ABC PM95", 1262.f, 0.f, 0.3f, LEAD_SAMPLES + 600);
    rng_seed(5);
    add_noise(g_thread_slot, SLOT_SAMPLES, noise_sigma(-8.f));
    ow_ft8_decode_t out[OW_FT8_MAX_DECODES];
    g_thread_expect = ow_ft8_decode_pcm(g_thread_slot, SLOT_SAMPLES, RATE, 1, 200.f, 2800.f, out, OW_FT8_MAX_DECODES, 0);

    pthread_t th[kThreads];
    for (int i = 0; i < kThreads; ++i)
        pthread_create(&th[i], NULL, thread_body, NULL);
    long bad = 0;
    for (int i = 0; i < kThreads; ++i)
    {
        void* r;
        pthread_join(th[i], &r);
        bad += (long)r;
    }
    printf("threads: %d threads x 4 decodes, %d decodes expected each, %ld mismatched\n", kThreads, g_thread_expect, bad);
    free(g_thread_slot);
}
#endif

/* Transmit encode cost at the decoder rate and at a typical soundcard rate. */
static void bench_tx(void)
{
    enum { kRuns = 50 };
    static const int rates[] = { 12000, 48000 };
    static const float slopes[] = { 0.f, 25.f };
    for (int r = 0; r < 2; ++r)
    {
        int cap = rates[r] * 8 + 256;
        float* buf = malloc((size_t)cap * sizeof(float));
        for (int s = 0; s < 2; ++s)
        {
            int count = 0;
            int rc = 0;
            double t0 = ow_prof_now();
            for (int i = 0; i < kRuns; ++i)
                rc |= ow_ft8_encode_pcm_ex("G4ABC MM9SQL -12", 1500.f, 1, slopes[s], 0.5f, buf, cap, rates[r], &count);
            double ms = 1000.0 * (ow_prof_now() - t0) / kRuns;
            printf("tx encode %5d Hz, slope %4.0f Hz/s: %.2f ms per burst (%d samples, rc %d)\n",
                rates[r], slopes[s], ms, count, rc);
        }
        free(buf);
    }
}

int main(int argc, char** argv)
{
    int seeds = 40;
    int deep = 0;
    const char* which = "all";
    for (int i = 1; i < argc; ++i)
    {
        if (strcmp(argv[i], "--deep") == 0)
            deep = 1;
        else if (strcmp(argv[i], "--drift-api") == 0)
            g_use_drift_api = 1;
        else if (strncmp(argv[i], "--drift-max=", 12) == 0)
            g_drift_max = (float)atof(argv[i] + 12);
        else if (strncmp(argv[i], "--birdie=", 9) == 0)
            g_birdie_amp = (float)atof(argv[i] + 9);
        else if (strncmp(argv[i], "--f0=", 5) == 0)
            g_drift_f0 = (float)atof(argv[i] + 5);
        else if (strncmp(argv[i], "--drift-steps=", 14) == 0)
            g_drift_steps = atoi(argv[i] + 14);
        else if (strncmp(argv[i], "--seeds=", 8) == 0)
            seeds = atoi(argv[i] + 8);
        else
            which = argv[i];
    }

    ow_ft8_remember_callsign("G4ABC");
    ow_ft8_remember_callsign("MM9SQL");

    int all = strcmp(which, "all") == 0;
    if (all || strcmp(which, "sens") == 0)
        bench_sensitivity(seeds, deep);
    if (all || strcmp(which, "busy") == 0)
        bench_busy(seeds / 2 > 0 ? seeds / 2 : 1, deep);
    if (all || strcmp(which, "passband") == 0)
        bench_passband(seeds / 2 > 0 ? seeds / 2 : 1, deep);
    if (all || strcmp(which, "crowd") == 0)
        bench_crowd(seeds / 2 > 0 ? seeds / 2 : 1, deep);
    if (all || strcmp(which, "drift") == 0)
        bench_drift(seeds / 4 > 0 ? seeds / 4 : 1, deep, -12.f);
    if (all || strcmp(which, "ap") == 0)
        bench_ap(seeds / 2 > 0 ? seeds / 2 : 1);
    if (all || strcmp(which, "tx") == 0)
        bench_tx();
#ifndef _WIN32
    if (all || strcmp(which, "threads") == 0)
        bench_threads();
#endif
    return 0;
}
