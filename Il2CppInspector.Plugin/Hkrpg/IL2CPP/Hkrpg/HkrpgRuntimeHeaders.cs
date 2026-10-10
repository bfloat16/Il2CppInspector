using System.Text.RegularExpressions;

namespace Il2CppInspector;

internal static class HkrpgRuntimeHeaders
{
    internal static string Apply(string stock)
    {
        foreach (var name in new[] { "Il2CppType", "Il2CppGenericClass", "Il2CppClass", "Il2CppClass_0", "Il2CppClass_1", "Il2CppClass_Merged", "MethodInfo", "FieldInfo", "PropertyInfo", "EventInfo", "Il2CppImage", "Il2CppAssembly", "Il2CppAssemblyName", "Il2CppCodeRegistration", "Il2CppMetadataRegistration" })
        {
            var pattern = $@"typedef struct (?:__attribute__\(\(aligned\([0-9]+\)\)\)\s+)?{name}\s*\{{.*?\}}\s*{name};";
            if (!Regex.IsMatch(stock, pattern, RegexOptions.Singleline))
                throw new InvalidOperationException($"Missing runtime header record {name}.");
            var replacement = Records.TryGetValue(name, out var body)
                ? $"typedef struct {name}\n{{\n{body.ReplaceLineEndings("\n")}\n}} {name};"
                : $"typedef struct {name} {name};";
            stock = Regex.Replace(stock, pattern, _ => replacement, RegexOptions.Singleline);
        }
        return "// HSR CN Windows 4.6.51 physical runtime records. Encoded members require decoding.\n"
            + "// Class records expose the verified 0xC0 prefix; variable vtable tails are not described.\n"
            + "// Unknown members retain their physical offsets; unrecovered records remain opaque.\n" + stock;
    }

    // Class construction: RVA 0x208E1787; field initialization: RVA 0x3F0E560.
    private const string ClassPrefix = """
        const Il2CppImage* image;
        uint64_t unknown_08;
        uint64_t unknown_10;
        FieldInfo* fields;
        const char* name;
        PropertyInfo* properties;
        Il2CppType byval_arg;
        uint64_t unknown_38;
        uint64_t unknown_40;
        uint64_t unknown_48;
        uint64_t unknown_50;
        uint64_t unknown_58;
        const void* typeDefinition; // encrypted physical 70-byte record
        uint64_t unknown_68;
        uint64_t unknown_70;
        Il2CppType this_arg;
        EventInfo* events;
        void* static_fields;
        uint32_t element_class; // class pool offset
        uint32_t parent; // class pool offset
        uint32_t declaringType; // class pool offset
        uint32_t flags;
        uint32_t castClass; // class pool offset
        int16_t thread_static_fields_offset;
        uint16_t native_size; // 0xFFFF means unavailable
        uint16_t unknown_A8;
        uint16_t static_fields_size;
        uint16_t instance_size;
        uint16_t method_count;
        uint16_t vtable_count;
        uint16_t interface_offsets_count;
        uint8_t unknown_B4;
        uint8_t thread_static_fields_size;
        uint8_t unknown_B6;
        uint8_t unknown_B7;
        uint8_t layout_flags; // log2(alignment) in bits 4..6
        uint8_t unknown_B9;
        uint8_t unknown_BA;
        uint8_t unknown_BB;
        uint32_t unknown_BC;
        """;

    private static readonly Dictionary<string, string> Records = new()
    {
        ["Il2CppType"] = """
            uint32_t data; // index, interpreted according to type; not a native pointer
            uint16_t attrs;
            uint8_t type;
            uint8_t bits; // byref uses bit 0x40
            """,
        ["Il2CppClass_0"] = ClassPrefix,
        ["Il2CppClass"] = ClassPrefix,
        ["Il2CppClass_Merged"] = ClassPrefix,
        ["FieldInfo"] = """
            Il2CppClass* parent; // add 0x85825031D00B8DF7 to decode
            const Il2CppType* type; // encoded pointer
            const char* name; // subtract 0x72916D972C0FE1BF to decode
            uint32_t offset; // subtract 0x1ECDD046; high byte is the storage tag
            uint32_t unknown_1C;
            """,
        ["Il2CppImage"] = """
            uint32_t typeCount; // XOR 0x1324FE97
            uint32_t unknown_04;
            int32_t typeStart; // XOR 0x47B42FAD
            uint32_t unknown_0C;
            const Il2CppAssembly* assembly; // XOR 0x1FCCB337131D032E
            uint32_t unknown_18;
            uint32_t unknown_1C;
            uint64_t unknown_20;
            const char* nameNoExt; // subtract 0x559CF1BE51BF2ED6
            int32_t customAttributeStart; // XOR 0x43633AF4
            uint32_t unknown_34;
            const char* name; // XOR 0x41D95E3D67AE73E0
            uint32_t unknown_40;
            uint32_t customAttributeCount; // XOR 0x7C06D18C
            """,
        ["Il2CppCodeRegistration"] = """
            uint64_t unknown_00;
            uint64_t unknown_08;
            uint64_t unknown_10;
            uint64_t unknown_18;
            uint64_t unknown_20;
            uint64_t unknown_28;
            const Il2CppMethodPointer* genericMethodPointersFallback;
            uint64_t unknown_38;
            const Il2CppMethodPointer* methodPointers;
            uint64_t unknown_48;
            uint64_t unknown_50;
            uint64_t unknown_58;
            uint64_t unknown_60;
            const InvokerMethod* invokerPointers;
            uint64_t unknown_70;
            const Il2CppMethodPointer* customAttributeGenerators;
            uint64_t unknown_80;
            uint64_t unknown_88;
            const Il2CppMethodPointer* genericMethodPointers;
            uint64_t unknown_98;
            uint64_t unknown_A0;
            uint32_t invokerPointersCount; // XOR 0x4695BA9B
            uint32_t unknown_AC;
            """,
        ["Il2CppMetadataRegistration"] = """
            uint64_t unknown_00;
            uint64_t unknown_08;
            uint64_t unknown_10;
            uint64_t unknown_18;
            uint64_t unknown_20;
            uint64_t unknown_28;
            uint64_t unknown_30;
            const Il2CppGenericInst* genericInsts;
            const uint16_t* nativeSizes; // indexed by layout group, 0xFFFF means unavailable
            uint64_t unknown_48;
            uint64_t unknown_50;
            uint64_t unknown_58;
            uint64_t unknown_60;
            uint64_t unknown_68;
            const Il2CppArrayType* arrayTypes;
            uint64_t unknown_78;
            const Il2CppType* types; // contiguous 8-byte records
            """,
    };
}
