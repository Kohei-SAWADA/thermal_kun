/*
 * Read-only implementation of the AppleSMC user-client wire protocol.
 * Protocol layout and platform sensor-key research reference:
 * https://github.com/exelban/stats (MIT, Copyright 2019 Serhiy Mytrovtsiy).
 * See THIRD_PARTY_NOTICES.md for the license notice.
 */
#include "SMCBridge.h"
#include <CoreFoundation/CoreFoundation.h>
#include <IOKit/IOKitLib.h>
#include <mach/mach.h>
#include <mach/mach_host.h>
#include <math.h>
#include <stddef.h>
#include <stdlib.h>
#include <string.h>
#include <sys/sysctl.h>

typedef struct { uint8_t major, minor, build, reserved; uint16_t release; } SMCVersion;
typedef struct { uint16_t version, length; uint32_t cpu, gpu, memory; } SMCPowerLimits;
typedef struct { uint32_t size, type; uint8_t attributes; } SMCKeyInfo;
typedef struct {
    uint32_t key;
    SMCVersion version;
    SMCPowerLimits powerLimits;
    SMCKeyInfo keyInfo;
    uint8_t result, status, command;
    uint32_t index;
    uint8_t bytes[32];
} SMCMessage;

_Static_assert(sizeof(SMCMessage) == 80, "AppleSMC message ABI size");
_Static_assert(offsetof(SMCMessage, bytes) == 48, "AppleSMC bytes ABI offset");

struct TKSMCContext {
    io_connect_t connection;
    struct { uint32_t key; SMCKeyInfo info; } cache[128];
    unsigned cacheCount;
};

static uint32_t fourcc(const char *key) {
    return ((uint32_t)(uint8_t)key[0] << 24) | ((uint32_t)(uint8_t)key[1] << 16) |
        ((uint32_t)(uint8_t)key[2] << 8) | (uint32_t)(uint8_t)key[3];
}

static int32_t transact(TKSMCContext *context, SMCMessage *input, SMCMessage *output) {
    size_t outputSize = sizeof(*output);
    memset(output, 0, sizeof(*output));
    kern_return_t result = IOConnectCallStructMethod(context->connection, 2, input,
        sizeof(*input), output, &outputSize);
    if (result != kIOReturnSuccess) return result;
    if (outputSize != sizeof(*output)) return kIOReturnBadMessageID;
    if (output->result != 0) return kIOReturnNotFound;
    return kIOReturnSuccess;
}

TKSMCContext *TKSMCOpen(int32_t *result) {
    io_service_t service = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("AppleSMC"));
    if (!service) { if (result) *result = kIOReturnNotFound; return NULL; }
    TKSMCContext *context = calloc(1, sizeof(*context));
    if (!context) { IOObjectRelease(service); if (result) *result = kIOReturnNoMemory; return NULL; }
    kern_return_t code = IOServiceOpen(service, mach_task_self(), 0, &context->connection);
    IOObjectRelease(service);
    if (result) *result = code;
    if (code != kIOReturnSuccess) { free(context); return NULL; }
    return context;
}

void TKSMCClose(TKSMCContext *context) {
    if (!context) return;
    if (context->connection) IOServiceClose(context->connection);
    free(context);
}

int32_t TKSMCReadValue(TKSMCContext *context, const char *key, double *value) {
    if (!context || !key || strlen(key) != 4 || !value) return kIOReturnBadArgument;
    SMCMessage request = {0}, reply = {0};
    request.key = fourcc(key);
    unsigned index = 0;
    for (; index < context->cacheCount; ++index) {
        if (context->cache[index].key == request.key) break;
    }
    if (index < context->cacheCount) {
        request.keyInfo = context->cache[index].info;
    } else {
        request.command = 9; /* Read key metadata. */
        int32_t result = transact(context, &request, &reply);
        if (result != kIOReturnSuccess) return result;
        request.keyInfo = reply.keyInfo;
        if (!request.keyInfo.size || request.keyInfo.size > sizeof(reply.bytes)) return kIOReturnUnsupported;
        if (context->cacheCount < 128) {
            context->cache[context->cacheCount].key = request.key;
            context->cache[context->cacheCount++].info = request.keyInfo;
        }
    }
    request.command = 5; /* Read bytes. */
    int32_t result = transact(context, &request, &reply);
    if (result != kIOReturnSuccess) return result;
    uint32_t type = request.keyInfo.type;
    const uint8_t *bytes = reply.bytes;
    double decoded;
    if (type == fourcc("flt ") && request.keyInfo.size == 4) {
        float number;
        memcpy(&number, bytes, sizeof(number));
        decoded = number;
    } else if (type == fourcc("sp78") && request.keyInfo.size == 2) {
        int16_t number = (int16_t)(((uint16_t)bytes[0] << 8) | bytes[1]);
        decoded = (double)number / 256.0;
    } else if (type == fourcc("fpe2") && request.keyInfo.size == 2) {
        decoded = (double)(((uint16_t)bytes[0] << 8) | bytes[1]) / 4.0;
    } else if (type == fourcc("ui8 ") && request.keyInfo.size == 1) {
        decoded = bytes[0];
    } else if (type == fourcc("ui16") && request.keyInfo.size == 2) {
        decoded = ((uint16_t)bytes[0] << 8) | bytes[1];
    } else if (type == fourcc("ui32") && request.keyInfo.size == 4) {
        decoded = ((uint32_t)bytes[0] << 24) | ((uint32_t)bytes[1] << 16) |
            ((uint32_t)bytes[2] << 8) | bytes[3];
    } else {
        return kIOReturnUnsupported;
    }
    if (!isfinite(decoded)) return kIOReturnBadMedia;
    *value = decoded;
    return kIOReturnSuccess;
}

int32_t TKReadCPUTicks(uint64_t *user, uint64_t *system, uint64_t *idle, uint64_t *nice) {
    if (!user || !system || !idle || !nice) return KERN_INVALID_ARGUMENT;
    host_cpu_load_info_data_t info = {0};
    mach_msg_type_number_t count = HOST_CPU_LOAD_INFO_COUNT;
    host_t host = mach_host_self();
    kern_return_t result = host_statistics(host, HOST_CPU_LOAD_INFO, (host_info_t)&info, &count);
    mach_port_deallocate(mach_task_self(), host);
    if (result != KERN_SUCCESS) return result;
    *user = info.cpu_ticks[CPU_STATE_USER];
    *system = info.cpu_ticks[CPU_STATE_SYSTEM];
    *idle = info.cpu_ticks[CPU_STATE_IDLE];
    *nice = info.cpu_ticks[CPU_STATE_NICE];
    return KERN_SUCCESS;
}

int32_t TKReadMemoryUsage(TKMemoryUsage *usage) {
    if (!usage) return KERN_INVALID_ARGUMENT;
    vm_statistics64_data_t info = {0};
    mach_msg_type_number_t count = HOST_VM_INFO64_COUNT;
    host_t host = mach_host_self();
    kern_return_t result = host_statistics64(host, HOST_VM_INFO64, (host_info64_t)&info, &count);
    vm_size_t pageSize = 0;
    if (result == KERN_SUCCESS) result = host_page_size(host, &pageSize);
    mach_port_deallocate(mach_task_self(), host);
    if (result != KERN_SUCCESS) return result;
    uint64_t total = 0;
    size_t size = sizeof(total);
    if (sysctlbyname("hw.memsize", &total, &size, NULL, 0) != 0) return KERN_FAILURE;
    uint64_t appPages = (uint64_t)info.active_count + info.inactive_count + info.speculative_count;
    uint64_t cachePages = (uint64_t)info.purgeable_count + info.external_page_count;
    if (cachePages > appPages) return KERN_FAILURE;
    appPages -= cachePages;
    uint64_t used = (appPages + info.wire_count + info.compressor_page_count) * pageSize;
    if (used > total) return KERN_FAILURE;
    usage->total = total;
    usage->used = used;
    usage->compressed = (uint64_t)info.compressor_page_count * pageSize;
    return KERN_SUCCESS;
}
