using System.Text.RegularExpressions;

namespace Il2CppInspector;

internal static class GenshinRuntimeHeaders
{
    internal static string Apply(string stock)
    {
        foreach (var (name, body) in Records)
        {
            var pattern = $@"typedef struct {name}\s*\{{.*?\}}\s*{name};";
            if (!Regex.IsMatch(stock, pattern, RegexOptions.Singleline))
                throw new InvalidOperationException($"Missing runtime header record {name}.");
            stock = Regex.Replace(stock, pattern, _ => $"typedef struct {name}\n{{\n{body.ReplaceLineEndings("\n")}\n}} {name};", RegexOptions.Singleline);
        }
        return "// Genshin 7.1.0 Windows MORAX physical runtime records. Encoded members require decoding.\n" + stock;
    }

    private const string ClassPrefix = """
        const Il2CppImage* image;
        void* gc_desc;
        Il2CppType this_arg;
        uint64_t unknown_20;
        const char* name;
        FieldInfo* fields;
        Il2CppClass** nestedTypes;
        Il2CppClass** implementedInterfaces;
        PropertyInfo* properties;
        uint64_t unknown_50;
        const MethodInfo** methods;
        const void* typeDefinition; // encrypted physical 70-byte record
        Il2CppType byval_arg;
        void* static_fields;
        Il2CppGenericClass* generic_class;
        const Il2CppRGCTXData* rgctx_data;
        EventInfo* events;
        uint32_t unknown_98;
        uint32_t unknown_9C;
        uint32_t element_class; // class pool offset
        uint32_t castClass; // class pool offset
        uint32_t declaringType; // class pool offset
        uint32_t flags;
        uint32_t parent; // class pool offset
        int16_t thread_static_fields_offset;
        int16_t native_size; // unsigned 0xFFFF is unavailable
        uint16_t element_size;
        uint16_t static_fields_size;
        uint16_t instance_size;
        uint16_t vtable_count;
        uint16_t method_count;
        uint16_t interface_offsets_count;
        uint8_t thread_static_fields_size;
        uint8_t unknown_C5;
        uint8_t unknown_C6;
        uint8_t cctor_finished;
        uint8_t init_flags;
        uint8_t runtime_flags;
        uint8_t type_flags;
        uint8_t unknown_CB;
        uint32_t unknown_CC;
        """;

    private static readonly (string Name, string Body)[] Records =
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
            Il2CppClass* parent; // XOR 0x55D980367BE5F6CA
            const char* name; // encoded pointer
            const Il2CppType* type; // encoded pointer
            uint32_t offset; // encoded, includes storage tag
            uint32_t unknown_1C;
            """
        ),
        (
            "PropertyInfo",
            """
            const char* name; // subtract 0x7EE8B0E037DD8EF7
            const MethodInfo* get; // XOR 0x30EA9C9D1A323195
            const MethodInfo* set; // XOR 0x62DC8A0B7235EC6A
            Il2CppClass* parent; // XOR 0x732414304DD79276
            uint32_t attrs; // XOR 0x7F39AD03
            uint32_t reserved_24;
            """
        ),
        (
            "EventInfo",
            """
            const MethodInfo* raise; // XOR 0x21F6DD132B878346
            const MethodInfo* add; // XOR 0x65223019099E6FE2
            const Il2CppType* eventType; // XOR 0x50FC4D975196ACF0
            Il2CppClass* parent; // XOR 0x6645EE586A446E1A
            const MethodInfo* remove; // XOR 0x2B3209C955C03447
            const char* name; // subtract 0x28A3A1625E3045E2
            """
        ),
        (
            "MethodInfo",
            """
            Il2CppClass* klass;
            Il2CppMethodPointer methodPointer;
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
            InvokerMethod invoker_method;
            uint16_t flags;
            uint16_t slot;
            uint16_t iflags;
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
            const InvokerMethod* invokerPointers;
            const Il2CppInteropData* interopData;
            const Il2CppMethodPointer* genericMethodPointers;
            const Il2CppMethodPointer* unresolvedVirtualCallPointers;
            const Il2CppMethodPointer* methodPointers;
            const Il2CppMethodPointer* customAttributeGenerators;
            uint64_t encodedUnknown_38;
            uint32_t encodedRuntimeGeneratorCount;
            uint32_t unknown_44;
            const int32_t* startupCctorTypeIndices;
            const Il2CppMethodPointer* genericAdjustorThunks;
            uint32_t encodedGenericMethodPointersCount;
            uint32_t unknown_5C;
            uint32_t unknown_60;
            uint32_t encodedInteropDataCount;
            const Il2CppMethodPointer* runtimeGenerators;
            uint64_t unknown_70;
            const Il2CppMethodPointer* valueTypeMethodPointers;
            uint32_t encodedStartupCctorCount;
            uint32_t unknown_84;
            uint64_t unknown_88;
            uint32_t encodedInvokerPointersCount;
            uint32_t unknown_94;
            uint64_t unknown_98;
            uint32_t encodedCustomAttributeCount;
            uint32_t unknown_A4;
            """
        ),
        (
            "Il2CppMetadataRegistration",
            """
            void** globalStaticStorageSlot;
            uint64_t unknown_08;
            uint64_t unknown_10;
            const uint16_t* nativeSizes;
            uint64_t unknown_20;
            const Il2CppGenericInst* genericInsts;
            uint32_t encodedTypesCount;
            uint32_t unknown_34;
            uint64_t unknown_38;
            uint64_t unknown_40;
            uint32_t unknown_48;
            uint32_t encodedGenericInstsCount;
            const Il2CppType* types;
            void* globalStaticStorage;
            uint64_t unknown_60;
            uint64_t unknown_68;
            uint64_t unknown_70;
            uint64_t unknown_78;
            uint64_t unknown_80;
            const Il2CppArrayType* arrayTypes;
            uint64_t unknown_90;
            uint32_t encodedGlobalStaticStorageSize;
            uint32_t unknown_9C;
            uint64_t usageRegistration[13];
            """
        ),
    ];
}
