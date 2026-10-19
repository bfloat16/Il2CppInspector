using System.Text.RegularExpressions;

namespace Il2CppInspector
{
    internal static class Bh3RuntimeHeaders
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
            return "// Honkai Impact 3rd 9.1.0 Windows MORAX runtime layout. Runtime pointers/tokens are encoded.\n" + stock;
        }

        // D §A.5 / §B.2. The vtable is a separate 16-byte-per-slot allocation whose pointer lives at
        // +0x00, so the header offsets below are absolute from the class pointer.
        private const string ClassPrefix = """
            const void* vtable; // separate allocation: methodPointer[count], then MethodInfo*[count]
            uint64_t unknown_08;
            uint64_t unknown_10;
            uint64_t unknown_18;
            const char* name;
            PropertyInfo* properties;
            void* static_fields;
            const void* typeDefinition; // raw encrypted 82-byte metadata record, not the host's normalized struct
            uint64_t unknown_40;
            uint64_t unknown_48;
            FieldInfo* fields;
            const Il2CppImage* image;
            Il2CppGenericClass* generic_class;
            uint64_t unknown_68;
            const Il2CppRuntimeInterfaceOffsetPair* interfaceOffsets; // 16 bytes per pair
            EventInfo* events;
            const MethodInfo** methods; // lazily resolved method pointer array
            Il2CppType byval_arg;
            Il2CppType this_arg;
            uint32_t unknown_A8;
            uint32_t poolOffset_AC; // class-pool relative offset
            uint32_t parent_B0; // class-pool relative offset
            uint32_t flags;
            uint32_t token;
            uint32_t elementType_BC; // class-pool relative offset
            uint16_t unknown_C0;
            uint16_t unknown_C2;
            uint16_t instance_size; // B §B.2 (D §5.3 labelled this element_size)
            int16_t thread_static_fields_offset; // -1 when unassigned
            uint16_t static_fields_size;
            uint16_t method_count;
            uint16_t interface_offsets_count;
            uint16_t vtable_count;
            uint8_t unknown_D0;
            uint8_t unknown_D1;
            uint8_t unknown_D2;
            uint8_t init_flags;
            uint8_t runtime_flags;
            uint8_t runtime_flags2;
            uint8_t type_flags;
            uint8_t unknown_D7;
            """;

        private static readonly (string Name, string Body)[] Records =
        [
            (
                "Il2CppType",
                """
                uint64_t data;
                uint16_t attrs;
                uint8_t type;
                uint8_t bits; // only bit 0x40 (byref) occurs in this build
                uint32_t reserved_0C;
                """
            ),
            (
                "Il2CppGenericClass",
                """
                int32_t typeDefinitionIndex;
                int32_t classInstIndex; // original startup record
                Il2CppGenericContext context;
                Il2CppClass* cached_class;
                """
            ),
            (
                "FieldInfo",
                """
                const Il2CppType* type; // encoded pointer: subtract 0x527998825445EFF7 before dereferencing
                Il2CppClass* parent; // encoded pointer: XOR 0x1E6E8A9871AE6091 before dereferencing
                const char* name; // encoded pointer: subtract 0x51508F1E7B6A0466 before dereferencing
                uint32_t offset; // XOR 0x577E5060; the decoded high byte selects the storage region
                uint32_t token; // subtract the runtime tag 0x5B7A007D
                """
            ),
            (
                "PropertyInfo",
                """
                const MethodInfo* set; // encoded pointer: subtract 0x7D6A0AE85B474814 before dereferencing
                const char* name; // encoded pointer: XOR 0x2D37876C318539CF before dereferencing
                Il2CppClass* parent; // encoded pointer: subtract 0x7BA57DCD02DB8BA6 before dereferencing
                const MethodInfo* get; // encoded pointer: XOR 0x2FF7343D533EA9BB before dereferencing
                uint32_t attrs; // subtract 0x46694636
                uint32_t token; // subtract the runtime tag 0x0C5486EF
                """
            ),
            (
                "EventInfo",
                """
                const MethodInfo* add; // encoded pointer: XOR 0x5C1DA6200B92C940 before dereferencing
                const MethodInfo* raise; // encoded pointer: XOR 0x2B71B1555D2C853F before dereferencing
                const char* name; // encoded pointer: subtract 0x26DE1BD645D9BE76 before dereferencing
                const MethodInfo* remove; // encoded pointer: XOR 0x5EE5696B1A14F8E5 before dereferencing
                Il2CppClass* parent; // encoded pointer: subtract 0x2C0981AF492EF3AD before dereferencing
                const Il2CppType* eventType; // encoded pointer: subtract 0x72AEA5942BD1D3C9 before dereferencing
                uint32_t token; // subtract the runtime tag 0x1B9C4DC9
                uint32_t reserved_34;
                """
            ),
            (
                "MethodInfo",
                """
                Il2CppMethodPointer methodPointer;
                Il2CppClass* klass;
                int32_t methodIndex;
                uint32_t reserved_14;
                InvokerMethod invoker_method;
                // sub_180616E00 writes decoded values from the shuffled metadata record.
                uint32_t genericContainerIndex;
                uint32_t reserved_24;
                uint32_t token;
                uint16_t iflags;
                uint16_t slot;
                uint16_t flags;
                uint8_t parameters_count;
                uint8_t runtime_flags;
                uint32_t reserved_34;
                uint64_t unknown_38;
                """
            ),
            (
                "Il2CppAssemblyName",
                """
                uint64_t encodedName; // pointer XOR 0x7D61CD0A593D1BC7
                uint32_t encodedMajor;
                uint32_t encodedMinor;
                uint32_t encodedHashAlg;
                uint32_t encodedFlags;
                uint64_t encodedCulture;
                uint32_t encodedUnknown_18;
                uint32_t encodedRevision;
                uint32_t encodedBuild;
                uint32_t encodedUnknown_24;
                uint8_t public_key_token[8];
                """
            ),
            (
                "Il2CppAssembly",
                """
                uint64_t encodedImage; // decode pointer by subtracting 0x43CC7AAC6622F48B (points back at the Il2CppImage)
                uint32_t encodedUnknown_08;
                uint32_t encodedUnknown_0C;
                uint64_t encodedUnknown_10;
                uint64_t encodedName; // pointer XOR 0x4B89E97D65638F66
                uint32_t encodedMajor;
                uint32_t encodedMinor;
                uint32_t encodedBuild;
                uint32_t encodedRevision;
                uint64_t encodedCulture; // pointer XOR 0x24845DEF1B976E19
                uint32_t encodedHashAlg;
                uint32_t encodedUnknown_3C;
                uint32_t encodedFlags;
                uint32_t encodedUnknown_44;
                uint64_t unknown_48;
                """
            ),
            (
                "Il2CppImage",
                """
                uint64_t encodedName; // pointer XOR 0x7D61CD0A593D1BC7
                uint32_t encodedUnknown_08;
                uint32_t encodedUnknown_0C;
                uint64_t encodedAssembly; // pointer XOR 0x257FB0AD1563A853
                uint64_t encodedUnknown_18;
                uint32_t encodedUnknown_20;
                uint32_t encodedUnknown_24;
                uint64_t encodedUnknown_28;
                uint32_t encodedUnknown_30;
                uint32_t encodedUnknown_34;
                uint32_t encodedUnknown_38;
                uint32_t encodedUnknown_3C;
                uint64_t encodedNameNoExt; // decode pointer by subtracting 0x172ADC487FDA4B76 (compared with the module name)
                uint64_t encodedCodeGenModule; // pointer XOR 0x70BFF49F45FAD23F
                uint64_t encodedInvokerIndices; // decode pointer by subtracting 0x6375017220BCE993, u16 indices
                """
            ),
            (
                "Il2CppTokenIndexMethodTuple",
                """
                uint32_t token; // 0x06xxxxxx method token
                uint32_t reserved_04;
                uint64_t pointer; // plaintext value-type method override address
                """
            ),
            ("Il2CppClass", ClassPrefix),
            ("Il2CppClass_0", ClassPrefix),
            (
                "Il2CppCodeRegistration",
                """
                uint64_t unknown_00; // u32 startupCctorTypeIndicesCount(44) and u32 count(6005)
                const Il2CppMethodPointer* genericMethodPointers;
                const void* unknown_10;
                uint32_t encodedInvokerPointersCount; // XOR 0x0F26DB88; 32067 in this build
                uint32_t encodedUnknown_1C; // XOR 0x4DE37C52; empty table
                uint32_t encodedUnknown_20; // subtract 0x639887D0; 1896 records of 0x38 bytes
                uint32_t reserved_24;
                const Il2CppCodeGenModule** codeGenModules;
                uint32_t encodedCodeGenModulesCount; // XOR 0x4ED23D5E; 212 in this build
                uint32_t unknown_34;
                const void* unknown_38; // pairs with encodedUnknown_1C; empty
                const void* unknown_40;
                uint32_t encodedGenericMethodPointersCount; // XOR 0x6B05DA47
                uint32_t reserved_4C;
                const InvokerMethod* invokerPointers;
                uint32_t encodedCustomAttributeCount; // subtract 0x06D60DEA
                uint32_t reserved_5C;
                const void* unknown_60;
                const void* startupCctorTypeIndices; // u32 type indices, count 44
                const void* unknown_70;
                uint32_t unknown_78;
                uint32_t reserved_7C;
                const Il2CppMethodPointer* genericAdjustorThunks;
                const void* moduleIndexedPointers; // indexed by the module ordinal
                const Il2CppMethodPointer* customAttributeGenerators;
                """
            ),
            (
                "Il2CppMetadataRegistration",
                """
                uint64_t unknown_00;
                uint32_t unknown_08;
                uint32_t encodedUnknown_0C; // XOR 0x65F3E4B0; element count for a pool allocation
                uint64_t unknown_10;
                uint64_t unknown_18;
                uint64_t unknown_20;
                uint64_t unknown_28;
                uint64_t unknown_30;
                uint32_t unknown_38;
                uint32_t encodedGenericInstsCount; // XOR 0x4D2509F2; 39751 in this build
                uint32_t encodedTypesCount; // subtract 0x15796DC9; 469900 in this build
                uint32_t unknown_44;
                uint64_t unknown_48;
                uint64_t unknown_50;
                void** globalStaticStorageSlot; // +0x58; tag 4 storage pointer slot
                uint64_t unknown_60;
                uint64_t unknown_68;
                const Il2CppType* types;
                const Il2CppGenericInst* genericInsts;
                uint64_t unknown_80;
                uint64_t unknown_88;
                void* globalStaticStorage; // +0x90; tag 2 storage
                const uint32_t* unknown_98; // monotonic u32 table; semantics unconfirmed
                uint64_t unknown_A0;
                """
            ),
            (
                "Il2CppCodeGenModule",
                """
                uint32_t sentinel_00; // 0x7800BACC with a small per-module perturbation
                uint32_t reserved_04;
                uint64_t reserved_08;
                uint64_t reserved_10;
                uint32_t reserved_18;
                uint32_t reserved_1C;
                uint32_t segmentMarker_20; // 0x0B1CDEAD with a small per-module perturbation
                uint32_t reserved_24;
                const char* moduleName;
                const void* unknown_30;
                uint32_t unknown_38;
                uint32_t encodedTupleCount; // XOR 0x763AC9A5
                uint64_t reserved_40;
                const Il2CppTokenIndexMethodTuple* methodPointers; // token-keyed, not a flat array
                const Il2CppMethodPointer* methodPointerArray;
                uint32_t encodedUnknown_58; // XOR 0x7A6F2562
                uint32_t encodedModuleIndex; // XOR 0x23A44D5F
                """
            ),
        ];
    }
}
