using System.Text.RegularExpressions;
using Il2CppInspector.Cpp.UnityHeaders;

namespace Il2CppInspector.Plugin.LAD;

internal static class LadRuntimeHeaders
{
    internal static UnityHeaders GetHeaders() => UnityHeaders.ForPlugin(new UnityVersion("2019.4.24"), new UnityVersion("2019.4.34f1"), Transform);

    private static string Transform(string text)
    {
        // 0x2471EF0: fields +0x78, rgctx +0xB8, generic container +0xD8, instance_size +0xE0,
        // flags +0xFC, method_count +0x104, vtable +0x120 (16-byte slots).
        text = Regex.Replace(
            text,
            @"typedef struct Il2CppClass(?:_[01])?\s*\{.*?\} Il2CppClass(?:_[01])?;",
            static match =>
                match
                    .Value.Replace("    Il2CppClass* klass;\n", "")
                    .Replace("    void *unity_user_data;\n", "")
                    .Replace("    void * unity_user_data;\n", "")
                    .Replace("    __attribute__((aligned(8))) size_t cctor_thread;\n", "")
                    .Replace("    GenericContainerIndex genericContainerIndex;\n", "    const Il2CppGenericContainer* genericContainer;\n"),
            RegexOptions.Singleline
        );

        // 0x24EE764: virtualMethodPointer +0x08, invoker +0x10, name +0x18, parameters +0x30.
        text = text.Replace(
            "    Il2CppMethodPointer methodPointer;\n    InvokerMethod",
            "    Il2CppMethodPointer methodPointer;\n    Il2CppMethodPointer virtualMethodPointer;\n    InvokerMethod"
        );
        text = text.Replace("    const ParameterInfo* parameters;", "    const Il2CppType** parameters;");
        // 0x20CA13C stores the invocation result through its fifth argument.
        text = text.Replace(
            "typedef void* (*InvokerMethod)(Il2CppMethodPointer, const MethodInfo*, void*, void**);",
            "typedef void (*InvokerMethod)(Il2CppMethodPointer, const MethodInfo*, void*, void**, void*);"
        );

        // 0x2472398 rewrites the compact type's definition index to a metadata pointer at startup.
        text = text.Replace(
            "    TypeDefinitionIndex typeDefinitionIndex;\n    Il2CppGenericContext context;",
            "    const Il2CppType* type;\n    Il2CppGenericContext context;"
        );
        text = text.Replace("    TypeIndex byrefTypeIndex;\n", "");
        // 0x2471EF0 constructs this_arg with byref in bit 29; modern types reserve bit 31 for valuetype.
        text = text.Replace("    unsigned int num_mods : 6;", "    unsigned int num_mods : 5;");
        text = text.Replace(
            "    unsigned int pinned : 1;\n} Il2CppType;",
            "    unsigned int pinned : 1;\n    unsigned int valuetype : 1;\n} Il2CppType;"
        );
        // CR+0x38 is unresolvedVirtualCallCount (0x247295C); attributes use serialized blobs.
        text = text.Replace(
            "    CustomAttributeIndex customAttributeCount;\n    const CustomAttributesCacheGenerator* customAttributeGenerators;\n",
            ""
        );
        // The module at 0x9C0DA70 and 64 adjacent module pairs establish the 0x88-byte record size.
        // These four v29 tail pointers are all zero in the shipped build.
        text = text.Replace(
            "typedef struct Il2CppCodeGenModule\n",
            "typedef struct Il2CppMetadataRegistration Il2CppMetadataRegistration;\n"
                + "typedef struct Il2CppCodeRegistration Il2CppCodeRegistration;\n"
                + "typedef struct Il2CppCodeGenModule\n"
        );
        text = text.Replace(
            "    const Il2CppDebuggerMetadataRegistration *debuggerMetadata;\n} Il2CppCodeGenModule;",
            "    const Il2CppDebuggerMetadataRegistration *debuggerMetadata;\n"
                + "    Il2CppMethodPointer moduleInitializer;\n"
                + "    TypeDefinitionIndex* staticConstructorTypeIndices;\n"
                + "    const Il2CppMetadataRegistration* metadataRegistration;\n"
                + "    const Il2CppCodeRegistration* codeRegistration;\n} Il2CppCodeGenModule;"
        );
        return text;
    }
}
