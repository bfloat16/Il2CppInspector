namespace Il2CppInspector;

internal static class ZzzRuntimeHeaders
{
    private const string ClassPrefix = """
        const Il2CppImage* image;
        void* gc_desc;
        Il2CppRuntimeInterfaceOffsetPair* interfaceOffsets;
        const Il2CppRGCTXData* rgctx_data;
        Il2CppGenericClass* generic_class;
        const MethodInfo** methods;
        PropertyInfo* properties;
        Il2CppClass** nestedTypes;
        void* static_fields;
        const char* name;
        const Il2CppTypeDefinition* typeDefinition;
        uint64_t reserved_58;
        FieldInfo* fields;
        Il2CppClass** implementedInterfaces;
        uint64_t reserved_70;
        EventInfo* events;
        Il2CppType byval_arg;
        Il2CppType this_arg;
        uint32_t parent; // class-pool relative offset; add the pool base to decode
        uint32_t token;
        uint32_t element_class; // class-pool relative offset
        uint32_t flags;
        uint32_t declaringType; // class-pool relative offset
        uint32_t castClass; // class-pool relative offset
        uint16_t element_size;
        uint16_t instance_size;
        int16_t thread_static_fields_offset; // thread-static storage slot; -1 means unassigned
        uint16_t static_fields_size;
        int16_t native_size; // marshal size; -1 means unavailable
        uint16_t interface_offsets_count;
        uint16_t method_count;
        uint16_t vtable_count;
        uint8_t thread_static_fields_size;
        uint8_t reserved_C9;
        uint8_t reserved_CA;
        uint8_t reserved_CB;
        uint8_t runtime_flags;
        uint8_t type_flags;
        uint8_t init_flags;
        uint8_t unknown_CF;
        """;

    internal static readonly (string Name, string Body)[] Records =
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
            Il2CppClass* parent; // encoded pointer: subtract 0x578EBCE24709EB71 before dereferencing
            const char* name; // encoded pointer: XOR 0x5E5A6E11763770BE before dereferencing
            const Il2CppType* type; // encoded pointer: subtract 0x3E940C193F8ED2CB before dereferencing
            uint32_t token; // XOR 0x1EB4E5CB
            int32_t offset; // XOR 0x3070E5C2; decoded high byte selects the storage region
            """
        ),
        (
            "PropertyInfo",
            """
            Il2CppClass* parent; // encoded pointer: XOR 0x3C49E114282CE226 before dereferencing
            const MethodInfo* get; // encoded pointer: XOR 0x6D9E0F1501CEEFC3 before dereferencing
            const char* name; // encoded pointer: subtract 0x4C491D782FD31107 before dereferencing
            const MethodInfo* set; // encoded pointer: subtract 0x7EECFA422F452E04 before dereferencing
            uint32_t token; // XOR 0x121B13F4
            uint32_t attrs; // XOR 0x47FAE4A0
            """
        ),
        (
            "EventInfo",
            """
            const Il2CppType* eventType; // encoded pointer: subtract 0x542669034B8E0966 before dereferencing
            const MethodInfo* remove; // encoded pointer: XOR 0x1BB3D9A4182C64F0 before dereferencing
            const MethodInfo* raise; // encoded pointer: XOR 0x67F302671A5ACE16 before dereferencing
            const char* name; // encoded pointer: subtract 0x52CA5A8B4BFCB071 before dereferencing
            Il2CppClass* parent; // encoded pointer: XOR 0x1B85E61C0964FB94 before dereferencing
            const MethodInfo* add; // encoded pointer: subtract 0x3804C6B36F62B678 before dereferencing
            uint32_t token; // subtract 0x4A6262D4
            uint32_t reserved_34;
            """
        ),
        (
            "MethodInfo",
            """
            Il2CppMethodPointer methodPointer;
            Il2CppClass* klass;
            InvokerMethod invoker_method;
            int32_t methodDefinitionIndex;
            uint32_t reserved_1C;
            // Definitions store an index; inflated methods store a pointer.
            union
            {
                const Il2CppGenericMethod* genericMethod;
                struct
                {
                    int32_t genericContainerIndex;
                    uint32_t reserved_24;
                };
            };
            uint32_t token;
            uint16_t slot;
            uint16_t flags;
            uint16_t iflags;
            uint8_t parameters_count;
            uint8_t runtime_flags;
            uint32_t reserved_34;
            uint64_t metadata_cache;
            """
        ),
        (
            "Il2CppAssemblyName",
            """
            uint64_t encodedName; // stored = pointer + 0x272D8B8035FB531C
            uint32_t encodedRevision; // stored = value + 0x5257A09B
            uint32_t encodedHashLen; // stored = value + 0x67EAB72D
            uint32_t encodedFlags; // stored = value ^ 0x0802502A (the record cipher 0x6AC84C5E is already folded in)
            uint32_t encodedBuild; // stored = value + 0x781E56B
            uint64_t encodedCulture; // stored = pointer + 0x382FB8790BDBC07D
            uint32_t encodedMinor; // stored = value + 0x2571CC77
            uint32_t encodedHashAlg; // stored = value + 0x41F10F98
            uint32_t encodedMajor; // stored = value + 0x295FB680
            uint8_t public_key_token[8];
            // The runtime name has no full public-key pointer.
            """
        ),
        (
            "Il2CppAssembly",
            """
            uint64_t encodedImage; // stored = pointer + 0x462D79A453BF294E
            Il2CppAssemblyName aname;
            uint32_t encodedReferencedAssemblyCount; // stored = value + 0x37CD075F
            uint32_t encodedToken; // stored = value ^ 0x24A12B0C
            uint64_t encodedReferencedAssemblyStart; // stored = pointer + 0x947A016
            """
        ),
        (
            "Il2CppImage",
            """
            uint32_t encodedExportedTypeStart; // stored = value + 0x19B5BF34
            uint32_t unknown_04;
            uint64_t encodedAssembly; // stored = pointer + 0x16B31C3449346EA3
            uint64_t encodedName; // stored = pointer ^ 0x79DD4F9A45B0B38A
            uint32_t encodedExportedTypeCount; // stored = value ^ 0x517A223B
            uint32_t encodedCustomAttributeCount; // stored = value + 0x6FB9DBDC
            uint32_t encodedToken; // stored = value + 0x20B51E6D
            uint32_t encodedTypeStart; // stored = value ^ 0x2B7D00EE
            uint64_t encodedNameNoExt; // stored = pointer + 0x3AB585323AB937CB
            uint64_t encodedCodeGenModule; // stored = pointer ^ 0x14CF813B702E4C5D
            uint32_t encodedEntryPointIndex; // stored = value ^ 0x4D7922D2
            uint32_t encodedTypeCount; // stored = value + 0x6BFB9982
            uint32_t encodedCustomAttributeStart; // stored = value ^ 0x31A9C215
            uint32_t unknown_44;
            uint64_t unknown_48;
            uint64_t encodedInvokerIndices; // stored = pointer ^ 0x487B16EF1A6F0B1A
            """
        ),
        (
            "Il2CppTokenIndexMethodTuple",
            """
            uint32_t token;
            int32_t index;
            int32_t methodInfoUsageIndex; // slot in UsageRegistration+0x40
            int32_t methodSpecIndex; // used when matching duplicate-token generic wrappers
            """
        ),
        (
            "Il2CppClass",
            ClassPrefix
                + "\nIl2CppMethodPointer vtable[0]; // runtime_flags bit 0x80 adds const MethodInfo* method[vtable_count] after these pointers"
        ),
        ("Il2CppClass_0", ClassPrefix),
        (
            "Il2CppCodeRegistration",
            """
            uint64_t unknown_00;
            const Il2CppMethodPointer* genericMethodPointers;
            const Il2CppMethodPointer* unresolvedVirtualCallPointers;
            const Il2CppMethodPointer* reversePInvokeWrappers;
            const Il2CppInteropData* interopData; // 0x38-byte records
            uint64_t runtimeRecords_28;
            uint64_t unknown_30;
            uint32_t encodedCustomAttributeCount;
            uint32_t encodedGenericMethodPointersCount;
            uint32_t unknown_40;
            uint32_t encodedUnresolvedVirtualCallCount;
            uint32_t encodedInteropDataCount; // subtract 0x299BB03E
            uint32_t unknown_4C;
            const Il2CppMethodPointer* genericAdjustorThunks;
            uint32_t encodedCodeGenModulesCount;
            uint32_t unknown_5C;
            uint32_t encodedInvokerPointersCount;
            uint32_t unknown_64;
            const Il2CppMethodPointer* customAttributeGenerators;
            const InvokerMethod* invokerPointers;
            const Il2CppCodeGenModule** codeGenModules;
            uint32_t unknown_80;
            uint32_t encodedRuntimeRecordCount2;
            """
        ),
        (
            "Il2CppMetadataRegistration",
            """
            uint64_t unknown_00;
            const Il2CppType* types;
            uint64_t unknown_10;
            uint32_t unknown_18;
            uint32_t unknown_1C;
            uint64_t unknown_20;
            uint32_t encodedGlobalStaticStorageSize; // bytes; XOR 0x3EB9D2CF
            uint32_t unknown_2C;
            uint64_t unknown_30;
            uint32_t unknown_38;
            uint32_t unknown_3C;
            uint32_t encodedTypesCount; // XOR 0x3745AAFC
            uint32_t unknown_44;
            const Il2CppGenericInst* genericInsts;
            uint32_t unknown_50;
            uint32_t encodedGenericInstsCount; // +0x54; subtract 0x5CC8ED8D
            uint64_t unknown_58;
            uint64_t unknown_60;
            void** globalStaticStorageSlot; // tag 4; allocation result stored here
            uint64_t unknown_70;
            uint64_t unknown_78;
            uint64_t unknown_80;
            void* globalStaticStorage; // tag 2; accessed at +0x88
            const uint16_t* nativeSizes; // indexed by layout group; 0xFFFF sentinel
            """
        ),
        (
            "Il2CppCodeGenModule",
            """
            uint32_t encodedModuleIndex; // XOR 0x1B557719; selects the metadata invoker slice
            uint32_t encodedAdjustorThunksCount; // XOR 0x4E75B561; count of adjustorThunks
            uint64_t unknown_08;
            const char* moduleName;
            uint32_t unknown_18;
            uint32_t unknown_1C;
            const Il2CppTokenAdjustorThunkPair* adjustorThunks; // zero iff the count at +0x04 is zero
            uint32_t encodedRgctxsCount; // subtract 0x58545BC3; checked against all token ranges
            uint32_t unknown_2C;
            const Il2CppTokenRangePair* rgctxRanges; // 12-byte {token, start, count} entries sorted by token
            const Il2CppMethodPointer* methodPointers;
            const Il2CppRGCTXDefinition* rgctxs;
            uint32_t encodedReversePInvokeWrapperCount; // XOR 0x460E780F
            uint32_t encodedRgctxRangesCount; // subtract 0x4B2A0B2D
            uint64_t unknown_50;
            const Il2CppTokenIndexMethodTuple* reversePInvokeWrapperIndices; // 16-byte records
            """
        ),
    ];
}
