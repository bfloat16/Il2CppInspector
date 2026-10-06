#include <stddef.h>
#include <stdint.h>
#if __SIZEOF_POINTER__ == 8
#define IS_64BIT
#else
#define IS_32BIT
#endif
#include "Cpp/UnityHeaders/39-6000.3.0b1.h"

struct Tail { void* pointer; uint8_t byte; };
struct Nested { uint8_t byte; Tail value; uint32_t after; };
static_assert(sizeof(Tail) == sizeof(void*) * 2);
static_assert(offsetof(Nested, value) == sizeof(void*));
static_assert(sizeof(Nested) == sizeof(void*) * 4);
static_assert(sizeof(MethodInfo) == (sizeof(void*) == 8 ? 88 : 48));
static_assert(sizeof(Il2CppTypeEnum) == 4);
