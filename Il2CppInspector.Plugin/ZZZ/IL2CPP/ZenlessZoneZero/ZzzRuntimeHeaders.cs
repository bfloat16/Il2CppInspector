using System.Text.RegularExpressions;

namespace Il2CppInspector
{
    internal static class ZzzRuntimeHeaders
    {
        internal static string Apply(string stock)
        {
            foreach (var (name, body) in Records)
            {
                var pattern = $@"typedef struct {name}\s*\{{.*?\}}\s*{name};";
                if (!Regex.IsMatch(stock, pattern, RegexOptions.Singleline))
                {
                    throw new InvalidOperationException($"Missing runtime header record {name}.");
                }

                // Keep emitted records independent of source-file line endings.
                var record = body.ReplaceLineEndings("\n");
                stock = Regex.Replace(stock, pattern, _ => $"typedef struct {name}\n{{\n{record}\n}} {name};", RegexOptions.Singleline);
            }
            return "// ZenlessZoneZero 3.2.0 Windows MORAX runtime layout. Runtime pointers/tokens can be encoded.\n" + stock;
        }

        private const string ClassPrefix = """
            const Il2CppImage* image;
            void* gc_desc;
            uint64_t cctor_thread;
            Il2CppClass** type_hierarchy;
            void* static_fields;
            const char* name;
            Il2CppClass** nestedTypes;
            FieldInfo* fields;
            Il2CppClass** implemented_interfaces;
            const Il2CppRGCTXData* rgctx_data;
            Il2CppRuntimeInterfaceOffsetPair* interface_offsets;
            const void* type_definition;
            void* generic_class;
            const MethodInfo** methods;
            PropertyInfo* properties;
            EventInfo* events;
            Il2CppType byval_arg;
            Il2CppType this_arg;
            uint32_t parent_offset;
            uint32_t type_token;
            uint32_t element_or_cast_offset;
            uint32_t type_attributes;
            uint32_t declaring_type_offset;
            uint32_t alternate_type_offset;
            uint16_t alignment_index;
            uint16_t element_size;
            uint16_t instance_size;
            uint16_t static_fields_size;
            int16_t native_size; // marshal size; -1 means unavailable
            uint16_t method_count;
            uint16_t vtable_count;
            uint16_t interface_offsets_count;
            uint8_t type_hierarchy_depth;
            uint8_t thread_static_fields_size;
            uint8_t cctor_started;
            uint8_t cctor_finished;
            uint8_t runtime_flags;
            uint8_t type_flags;
            uint8_t init_flags;
            uint8_t unknown_CF;
            """;

        private static readonly (string Name, string Body)[] Records =
        [
            (
                "Il2CppType",
                """
                uint64_t data;
                uint16_t attrs;
                uint8_t type;
                uint8_t bits;
                uint32_t reserved_0C;
                """
            ),
            (
                "FieldInfo",
                """
                const Il2CppType* type;
                Il2CppClass* parent;
                const char* name;
                uint32_t offset;
                uint32_t token;
                """
            ),
            (
                "PropertyInfo",
                """
                Il2CppClass* parent;
                const MethodInfo* get;
                const char* name;
                const MethodInfo* set;
                uint32_t token;
                uint32_t attrs;
                """
            ),
            (
                "EventInfo",
                """
                const MethodInfo* raise;
                const MethodInfo* remove;
                const MethodInfo* add;
                Il2CppClass* parent;
                const Il2CppType* eventType;
                const char* name;
                uint32_t token;
                uint32_t reserved_34;
                """
            ),
            (
                "MethodInfo",
                """
                Il2CppMethodPointer methodPointer;
                Il2CppClass* klass;
                int32_t methodDefinitionIndex;
                uint32_t reserved_14;
                InvokerMethod invoker_method;
                void* genericMethod;
                uint32_t token;
                uint32_t flags_packed;
                uint16_t slot;
                uint8_t parameters_count;
                uint8_t runtime_flags;
                uint32_t reserved_34;
                void* metadata_cache;
                """
            ),
            (
                "Il2CppAssemblyName",
                """
                uint64_t encodedName; // pointer + 0x6B416E753612B870
                uint32_t encodedMajor;
                uint32_t encodedMinor;
                uint32_t encodedHashLen; // XOR 0x794417C5; not a public-key index
                uint32_t unknown_14;
                uint64_t encodedCulture; // pointer + 0x6E997F9D5F5E2320
                uint32_t encodedHashAlg;
                uint32_t encodedBuild;
                uint32_t encodedRevision;
                uint32_t encodedFlags;
                uint8_t public_key_token[8];
                // The runtime name has no full public-key pointer.
                """
            ),
            (
                "Il2CppAssembly",
                """
                uint32_t encodedReferencedAssemblyStart;
                uint32_t unknown_04;
                Il2CppAssemblyName aname;
                uint32_t encodedToken;
                uint32_t unknown_44;
                uint64_t encodedImage; // pointer XOR 0x376189071FD94C63
                uint32_t encodedReferencedAssemblyCount;
                uint32_t unknown_54;
                """
            ),
            (
                "Il2CppImage",
                """
                uint32_t encodedExportedTypeStart;
                uint32_t encodedTypeCount;
                uint64_t encodedNameToClassHashTable; // pointer + 0x714C33FB6D25A4B9
                uint32_t encodedToken;
                uint32_t unknown_14;
                uint64_t encodedAssembly; // pointer XOR 0x14B579F87911C4DB
                uint32_t encodedExportedTypeCount;
                uint32_t encodedCustomAttributeStart;
                uint32_t encodedCustomAttributeCount;
                uint32_t encodedTypeStart;
                uint64_t encodedNameNoExt; // pointer XOR 0x262B1F6A0295FAE0
                uint32_t encodedEntryPointIndex;
                uint32_t unknown_3C;
                uint64_t encodedCodeGenModule; // pointer XOR 0x637B365B57864070
                uint64_t encodedName; // pointer XOR 0x5782FC8C3DEE4137
                uint64_t encodedInvokerIndices; // pointer + 0x1A43C26E6342C2DB
                """
            ),
            (
                "Il2CppTokenIndexMethodTuple",
                """
                uint32_t token;
                int32_t index;
                int32_t methodInfoUsageIndex; // slot in UsageRegistration+0x10, not a MethodInfo pointer
                int32_t methodSpecIndex; // used when matching duplicate-token generic wrappers
                """
            ),
            ("Il2CppClass", ClassPrefix + "\nIl2CppMethodPointer vtable[0]; // runtime_flags bit 0x80 adds const MethodInfo* method[vtable_count] after these pointers"),
            ("Il2CppClass_0", ClassPrefix),
            (
                "Il2CppCodeRegistration",
                """
                const Il2CppMethodPointer* genericAdjustorThunks;
                const int32_t* startupCctorTypeIndices;
                const uint64_t* runtimeRecords_10; // two words per active 16-byte record; contents unconfirmed
                uint32_t encodedCustomAttributeCount;
                uint32_t reserved_1C;
                const Il2CppCodeGenModule** codeGenModules;
                uint32_t encodedCodeGenModulesCount;
                uint32_t encodedUnresolvedVirtualCallCount;
                uint64_t unknown_30;
                const InvokerMethod* invokerPointers;
                const Il2CppMethodPointer* reversePInvokeWrappers;
                const uint32_t* internalCallHashes; // keys copied via qword_184C24BC8; 5168 in this build
                const Il2CppMethodPointer* customAttributeGenerators;
                uint32_t encodedStartupCctorCount;
                uint32_t encodedGenericMethodPointersCount;
                uint32_t encodedRuntimeRecordsCount; // decoded by subtracting 0x51557C49
                uint32_t encodedInteropDataCount;
                const Il2CppMethodPointer* unresolvedVirtualCallPointers;
                const Il2CppMethodPointer* genericMethodPointers;
                Il2CppMethodPointer* internalCallCache; // filled via qword_184C24BC0 during startup
                uint32_t encodedInvokerPointersCount;
                uint32_t unknown_84;
                const Il2CppInteropData* interopData;
                """
            ),
            (
                "Il2CppMetadataRegistration",
                """
                uint64_t unknown_00;
                uint32_t encodedUnknown_08;
                uint32_t unknown_0C;
                uint64_t unknown_10;
                uint32_t unknown_18;
                uint32_t encodedTypesCount;
                uint64_t unknown_20;
                uint32_t encodedUnknown_28;
                uint32_t encodedUnknown_2C;
                const Il2CppType* types;
                uint32_t encodedUnknown_38;
                uint32_t encodedGenericInstsCount;
                uint64_t unknown_40;
                const Il2CppGenericInst* genericInsts;
                uint32_t encodedUnknown_50;
                uint32_t encodedUnknown_54;
                uint64_t unknown_58;
                void* globalStaticStorage;
                const Il2CppArrayType* arrayTypes; // 46 contiguous descriptors; Il2CppType.data points directly into this table
                uint32_t encodedUnknown_70;
                uint32_t unknown_74;
                void** globalStaticStorageSlot; // tag 4; allocation result stored here at 0x180271237
                uint32_t encodedUnknown_80;
                uint32_t encodedGlobalStaticStorageSize; // bytes at +84, decoded by subtracting 0x60903198
                uint32_t encodedUnknown_88;
                uint32_t unknown_8C;
                const uint16_t* nativeSizes; // indexed by layout group; 0xFFFF sentinel
                """
            ),
            (
                "Il2CppCodeGenModule",
                """
                uint64_t reserved_00;
                const char* moduleName;
                uint32_t encodedReversePInvokeWrapperCount;
                uint32_t encodedAdjustorThunksCount;
                uint32_t encodedRgctxsCount; // XOR 0x1ABD7223; checked against all token ranges
                uint32_t reserved_1C;
                const Il2CppTokenIndexMethodTuple* reversePInvokeWrapperIndices; // ZZZ tuple is 16 bytes
                uint32_t encodedModuleIndex; // subtract 0x2A6FF452; selects metadata invoker slice
                uint32_t reserved_2C;
                const Il2CppTokenRangePair* rgctxRanges;
                uint32_t encodedRgctxRangesCount;
                uint32_t encodedMethodPointerCount;
                const Il2CppTokenAdjustorThunkPair* adjustorThunks;
                const Il2CppMethodPointer* methodPointers;
                const Il2CppRGCTXDefinition* rgctxs;
                // Verified prefix ends at +58; total module size is not established.
                """
            ),
        ];
    }
}
