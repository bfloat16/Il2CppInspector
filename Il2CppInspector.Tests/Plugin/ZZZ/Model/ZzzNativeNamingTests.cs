namespace Il2CppInspector.Tests.Plugin.ZZZ.Model;

internal static class ZzzNativeNamingTests
{
    internal static void Run(ZzzTestContext context, string output, string[] args)
    {
        var app = new AppModel(context.Model, false).Build(new UnityVersion("2019.4.40f1"));
        var native = app.GameNativeModel;
        Check(native.Name(context.Vector) == "Vector3", "ZZZ uses the metadata type name for Vector3");
        Check(native.Name(context.Model.TypesByFullName["System.DayOfWeek"]) == "DayOfWeek__Enum", "ZZZ uses the stock enum naming convention");
        var list = context.List.MakeGenericType(context.Vector);
        Check(native.Name(list).Contains("List_1_UnityEngine_Vector3"), "Constructed type names preserve the generic type and argument names");
        Check(native.ArrayName(context.Vector.MakeArrayType()) == native.Name(context.Vector) + "__Array", "Arrays use the stock element-name suffix");
        var sameNames = context.Model.TypesByDefinitionIndex.Where(t => t is { IsGenericType: false }).GroupBy(t => t.Name).First(g => g.Count() > 1).ToArray();
        Check(sameNames.Select(native.Name).Distinct().Count() == sameNames.Length, "Types with the same metadata name are disambiguated");
        Check(
            app.RuntimeCppTypes.GetComplexType("FieldInfo")["type"].Type is CppPointerType
                && app.RuntimeCppTypes.GetComplexType("MethodInfo").Flattened["genericContainerIndex"].OffsetBytes == 0x20
                && app.RuntimeCppTypes.GetComplexType("Il2CppClass_0")["thread_static_fields_offset"].OffsetBytes == 0xB8,
            "Runtime declarations retain stock field semantics with the verified ZZZ offsets"
        );

        Directory.CreateDirectory(output);
        if (args.Contains("--write-header"))
        {
            var header = Path.Combine(output, "il2cpp.h");
            new CppScaffolding(app, useBetterArraySize: true).WriteTypes(header);
            Check(!File.ReadLines(header).Any(line => line.Contains("Zzz_", StringComparison.Ordinal)), "Streamed headers add no ZZZ prefix");
            File.WriteAllText(
                Path.Combine(output, "native-names-check.cpp"),
                "#include <stdint.h>\n#include <stddef.h>\n#include \"il2cpp.h\"\nusing namespace app;\n"
                    + "static_assert(sizeof(Vector3) == 12);\nstatic_assert(__builtin_offsetof(Vector3, z) == 8);\n"
                    + $"static_assert(sizeof({native.Name(context.ObjectType)}) == {context.ObjectType.Sizes.InstanceSize});\n"
                    + "static_assert(sizeof(StateEvent) == 25 && __alignof(StateEvent) == 1);\n"
                    + "static_assert(sizeof(DeltaStateEvent) == 29);\n"
                    + "static_assert(__builtin_offsetof(InputSystem_StateEventBuffer, data) == 25);\n"
                    + "static_assert(sizeof(PrimitiveValue) == 16 && __builtin_offsetof(PrimitiveValue, m_LongValue) == 4);\n"
                    + "static_assert(__is_same(decltype(RuntimeTypeHandle::value), void *));\n"
                    + "static_assert(__builtin_offsetof(Il2CppClass_0, thread_static_fields_offset) == 0xB8);\n"
                    + "static_assert(__builtin_offsetof(MethodInfo, flags) == 0x2C && __builtin_offsetof(MethodInfo, iflags) == 0x2E);\n"
                    + "static_assert(__is_same(decltype(FieldInfo::type), const Il2CppType *));\n"
            );
        }
    }
}
