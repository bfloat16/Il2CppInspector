namespace Il2CppInspector;

// Class constructor 0x15A4D6B60, fields 0x14051C480,
// properties 0x14051EE90, events 0x14051E9C0, registration 0x1402F8450.
internal static class GenshinRuntimeHeaders
{
    private const string ClassPrefix = """
        const Il2CppImage* image;
        void* gc_desc;
        Il2CppType byval_arg;
        Il2CppType this_arg;
        uint64_t unknown_30;
        void* static_fields; // 0x14051D5DA
        uint64_t unknown_40;
        uint64_t unknown_48;
        PropertyInfo* properties;
        const void* typeDefinition; // encrypted physical 70-byte record
        FieldInfo* fields;
        Il2CppGenericClass* generic_class;
        const char* name;
        EventInfo* events;
        Il2CppRuntimeInterfaceOffsetPair* interfaceOffsets;
        Il2CppClass** implementedInterfaces;
        uint64_t unknown_90;
        const MethodInfo** methods;
        uint32_t flags;
        uint32_t element_class; // class pool offset
        uint32_t declaringType; // class pool offset
        uint32_t castClass; // class pool offset
        uint32_t parent; // class pool offset
        uint16_t instance_size;
        uint16_t static_fields_size;
        uint16_t unknown_B8;
        int16_t thread_static_fields_offset;
        int16_t native_size; // unsigned 0xFFFF is unavailable
        uint16_t vtable_count;
        uint16_t interface_offsets_count;
        uint16_t method_count;
        uint8_t unknown_C4;
        uint8_t thread_static_fields_size;
        uint8_t unknown_C6;
        uint8_t cctor_finished;
        uint8_t type_flags;
        uint8_t init_flags;
        uint8_t runtime_flags;
        uint8_t unknown_CB;
        uint32_t unknown_CC;
        """;

    internal static readonly (string Name, string Body)[] Records =
    [
        (
            "Il2CppType",
            """
            uint64_t data;
            uint16_t attrs;
            uint8_t type;
            uint8_t bits; // byref uses bit0x40
            uint32_t reserved_0C;
            """
        ),
        (
            "FieldInfo",
            """
            const char* name; // XOR 0x577522FA3015CD48
            Il2CppClass* parent; // ADD 0x532DBBC45ECD4CD2
            const Il2CppType* type; // ADD 0x20281DEF12FE4570
            uint32_t offset; // ADD 0x1ED2584F, includes storage tag
            uint32_t unknown_1C;
            """
        ),
        (
            "PropertyInfo",
            """
            const MethodInfo* set; // XOR 0x247581C04B0D0BC7
            const char* name; // ADD 0x7AD9728F0C4EDD07
            Il2CppClass* parent; // ADD 0x21B4112C16657154
            const MethodInfo* get; // ADD 0x55152CBF1159C1A0
            uint32_t attrs; // XOR 0x6AE0F22C
            uint32_t reserved_24;
            """
        ),
        (
            "EventInfo",
            """
            const MethodInfo* raise; // XOR 0x6367B6FD2500DBEB
            const MethodInfo* add; // ADD 0x56D307905905DDB8
            const Il2CppType* eventType; // ADD 0x537375283E6FA511
            const MethodInfo* remove; // XOR 0x37070ACA34FF4911
            Il2CppClass* parent; // ADD 0x1C68FD9D6BF152A4
            const char* name; // ADD 0x76D80C2C656E0278
            """
        ),
        (
            "MethodInfo",
            """
            Il2CppClass* klass;
            Il2CppMethodPointer methodPointer;
            InvokerMethod invoker_method;
            union
            {
                int32_t methodDefinitionIndex;
                const Il2CppGenericMethod* genericMethod;
            };
            union
            {
                int32_t genericContainerIndex;
                const void* metadataDefinition;
            };
            uint16_t slot;
            uint16_t iflags;
            uint16_t flags;
            uint8_t parameters_count;
            uint8_t runtime_flags;
            const void* metadata_cache;
            """
        ),
        ("Il2CppClass_0", ClassPrefix),
        ("Il2CppClass", ClassPrefix + "\nIl2CppMethodPointer vtable[0]; // type_flags/private metadata bits select a second MethodInfo* array"),
        (
            "Il2CppCodeRegistration",
            """
            uint64_t unknown_00;
            uint64_t unknown_08;
            uint64_t unknown_10;
            uint64_t unknown_18;
            uint64_t unknown_20;
            uint64_t unknown_28;
            const Il2CppMethodPointer* genericMethodPointers;
            uint64_t unknown_38;
            const Il2CppMethodPointer* customAttributeGenerators;
            uint64_t unknown_48;
            uint32_t encodedCustomAttributeCount; // ADD 0xF8614AF1
            uint32_t unknown_54;
            const Il2CppMethodPointer* genericAdjustorThunks;
            uint32_t unknown_60;
            uint32_t encodedGenericMethodPointersCount; // ADD 0x9EA91E3B
            const InvokerMethod* invokerPointers;
            const Il2CppMethodPointer* methodPointers;
            uint64_t unknown_78;
            uint64_t unknown_80;
            const Il2CppMethodPointer* valueTypeMethodPointers;
            uint32_t encodedInvokerPointersCount; // ADD 0xF026D1B3
            uint32_t unknown_94;
            uint64_t unknown_98;
            uint64_t unknown_A0;
            """
        ),
        (
            "Il2CppMetadataRegistration",
            """
            const Il2CppType* types;
            uint32_t encodedGenericInstsCount; // ADD 0x82A2CEC7
            uint32_t unknown_0C;
            uint64_t unknown_10;
            uint64_t unknown_18;
            uint64_t unknown_20;
            uint64_t unknown_28;
            void* globalStaticStorage;
            uint64_t unknown_38;
            uint64_t unknown_40;
            uint64_t unknown_48;
            uint64_t unknown_50;
            uint32_t encodedGlobalStaticStorageSize; // ADD 0xDAA6C65B
            uint32_t unknown_5C;
            uint64_t unknown_60;
            const uint16_t* nativeSizes;
            const Il2CppGenericInst* genericInsts;
            uint64_t unknown_78;
            uint64_t unknown_80;
            uint32_t unknown_88;
            uint32_t encodedTypesCount; // ADD 0xCE67355C
            void** globalStaticStorageSlot;
            uint64_t usageRegistration[13];
            """
        ),
    ];
}
