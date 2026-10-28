namespace Il2CppInspector.Plugin.LAD
{
    /// <summary>
    /// Build constants for 恋与深空 (Love and Deepspace) 6.0.0 Android, from the shipped libil2cpp.so
    /// (build path /Users/x3mac/260710_release_cn_apk/.../X3Engine) and global-metadata.dat.
    /// </summary>
    internal static class LadBuild
    {
        internal const string GameId = "LAD_CN_Android_6.0.0";
        internal const int MetadataSize = 0x2491864;

        // Registration function at file offset 0x1C4F910 loads X0 = CodeRegistration, X1 = MetadataRegistration
        // and X2 = CodeGenOptions (0x7C8BA04); 0x24F1344 stores all three in the metadata cache.
        internal const ulong RegistrationRva = 0x24F1344;
        internal const ulong CodeRegistrationRva = 0x994EE08;
        internal const ulong MetadataRegistrationRva = 0x9C080D8;

        // Build signature from the shipped 0x100-byte header; the runtime loader has no stock sanity check.
        internal const uint SanityEncoded = 0x00144511;
        internal const uint VersionEncoded = 0x10086; // Shipped header word at +0x04.

        // Header slots hold section offsets XORed with per-slot keys (accessors 0x2471E78-0x2474600).
        // The +4 slots hold encoded sizes, which are not decoded: sizes follow from table contiguity.
        internal const int SlotDefaultValueData = 0x08;
        internal const int SlotStrings = 0x38;
        internal const int SlotFieldMarshaledSizes = 0x10;
        internal const int SlotExportedTypes = 0x18;
        internal const int SlotUnresolvedCallTypes = 0x30;
        internal const int SlotFieldRefs = 0x98;
        internal const int SlotGenericConstraints = 0xB0;
        internal const int SlotAttributeRanges = 0xD0;
        internal const int SlotTypeDefinitions = 0x20;
        internal const int SlotFields = 0x28;
        internal const int SlotInterfaces = 0x40;
        internal const int SlotEvents = 0x48;
        internal const int SlotProperties = 0x50;
        internal const int SlotImages = 0x58;
        internal const int SlotMethods = 0x60;

        // Serialized v29-style custom attributes at 0x173C710 (0x24732D4 / 0x248F0AC).
        internal const int SlotAttributeData = 0x68;
        internal const int SlotParameterDefaultValues = 0x80;
        internal const int SlotFieldDefaultValues = 0x88;
        internal const int SlotVTableMethods = 0x90;
        internal const int SlotParameters = 0xA0;
        internal const int SlotGenericParameters = 0xA8;
        internal const int SlotAssemblies = 0xB8;
        internal const int SlotInterfaceOffsets = 0xC0;
        internal const int SlotGenericContainers = 0xC8;
        internal const int SlotNestedTypes = 0xD8;
        internal const int SlotStringLiteralIndex = 0xE0;
        internal const int SlotUnresolvedCallRanges = 0xE8;
        internal const int SlotStringLiteralData = 0xF0;
        internal const int SlotWindowsRuntimeStrings = 0x78;
        internal const int SlotWindowsRuntimeTypes = 0xF8;

        // Per-slot offset XOR keys.
        internal const uint KeyStrings = 0x34B0E; // 0x2471EF0, 0x2473BAC.
        internal const uint KeyFieldMarshaledSizes = 0x170F2; // 0x2473B14.
        internal const int ExportedTypesOffsetAddend = 0x29C0D; // 0x2472E94.
        internal const uint KeyUnresolvedCallTypes = 0x99DB9; // 0x247295C.
        internal const uint KeyFieldRefs = 0x577F0; // 0x2472594.
        internal const uint KeyGenericConstraints = 0xE9F1B; // 0x2474158.
        internal const uint KeyAttributeRanges = 0x27DF6; // 0x24732D4.
        internal const uint KeyTypeDefinitions = 0x3B90C;
        internal const uint KeyFields = 0xB75D6;
        internal const uint KeyInterfaces = 0xF0292;
        internal const uint KeyEvents = 0xDD414;
        internal const uint KeyProperties = 0xC34DB;
        internal const uint KeyImages = 0x91A68;
        internal const uint KeyMethods = 0xF87B;
        internal const uint KeyAttributeData = 0x8C869;
        internal const uint KeyParameterDefaultValues = 0x17C39;
        internal const uint KeyFieldDefaultValues = 0xE9F85;
        internal const uint KeyVTableMethods = 0x2873D;
        internal const uint KeyParameters = 0xCDBD1;
        internal const uint KeyGenericParameters = 0x76CA9;
        internal const uint KeyAssemblies = 0xDFB98;
        internal const uint KeyInterfaceOffsets = 0x8D964;
        internal const uint KeyGenericContainers = 0xE9334;
        internal const uint KeyNestedTypes = 0x78247;
        internal const uint KeyStringLiteralIndex = 0xD5B2F;
        internal const uint KeyUnresolvedCallRanges = 0x90E0F;
        internal const uint KeyStringLiteralData = 0x2C515;
        internal const uint KeyDefaultValueData = 0x7F5E;
        internal const uint KeyWindowsRuntimeStrings = 0x8A623; // 0x247280C.
        internal const uint KeyWindowsRuntimeTypes = 0xE29FC; // 0x247280C.

        // Encoded counts, checked to reject other X3Engine builds: (raw ^ key) / 0x28 == images (0x2472398),
        // (raw >> 6) ^ key == assemblies (0x2472408).
        internal const uint ImageCountKey = 0x89768;
        internal const uint AssemblyCountShift = 6;
        internal const uint AssemblyCountKey = 0x161D;

        // Field-value XOR keys used inside the definition records themselves.
        internal const uint MethodKey = 0x686; // nameIndex/declaringType/returnType/parameterStart/genericContainer/token/flags/slot
        internal const uint EventKey = 0xF52; // every dword of Il2CppEventDefinition
        internal const uint PropertyKey = 0x2479; // every dword of Il2CppPropertyDefinition
        internal const uint ParameterDefaultValueKey = 0x1C13; // 0x2473988; field defaults stay plaintext.
        internal const uint StringLiteralKey = 0x1BF52; // string literal index pairs {length, dataOffset}
    }
}
