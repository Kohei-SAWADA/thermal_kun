#ifndef THERMAL_KUN_SMC_BRIDGE_H
#define THERMAL_KUN_SMC_BRIDGE_H

#include <stdint.h>

typedef struct TKSMCContext TKSMCContext;

/* Read-only AppleSMC access. No write command is implemented or exported. */
TKSMCContext *TKSMCOpen(int32_t *result);
void TKSMCClose(TKSMCContext *context);
int32_t TKSMCReadValue(TKSMCContext *context, const char *key, double *value);

int32_t TKReadCPUTicks(uint64_t *user, uint64_t *system, uint64_t *idle, uint64_t *nice);

typedef struct {
    uint64_t total;
    uint64_t used;
    uint64_t compressed;
} TKMemoryUsage;
int32_t TKReadMemoryUsage(TKMemoryUsage *usage);

#endif
