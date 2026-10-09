using Il2CppInspector.Next;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector.Tests.Plugin.Genshin;

internal static class GenshinTests
{
    internal static void Run(string[] args)
    {
        using var binaryFile = File.OpenRead(Path.Combine(args[1], "YuanShen.exe"));
        var input = global::Il2CppInspector
            .Plugins.GamePlugins.Get("Genshin_CN_Windows_7.1.0")
            .Load(
                binaryFile,
                File.ReadAllBytes(Path.Combine(args[1], "global-metadata.dat")),
                File.ReadAllBytes(Path.Combine(args[1], "startup-metadata.dat")),
                new LoadOptions { Game = "Genshin_CN_Windows_7.1.0" },
                (_, s) => Console.WriteLine(s)
            );
        Check(input.TypeDefinitions.Length == 88902 && input.Images.Length == 75, "Real image ranges exclude both physical sentinels");
        Check(input.Methods.Length == 733442 && input.Fields.Length == 440172 && input.Params.Length == 548400, "Shuffled member records retain the physical table boundaries");
        Check(input.TypeReferences.Length == 683568 && input.MethodSpecs.Length == 570843, "Type references and MethodSpecs retain their native indices");
        Check(input.CustomAttributeGenerators.Length == 72760 && input.MethodInvokePointers.Length == 79765, "Custom attribute generators and method invokers use separate registration fields");
        var adapter = input.Metadata.GameAdapter;
        Check(input.Images.Sum(i => (long)i.ExportedTypeCount) == 1351, "Facade image ranges retain the exported type count");
        Check(
            adapter.AdditionalUsages.Count(u => u.Section == "genshinEmptyArrays") == 2564 && adapter.AdditionalUsages.Count(u => u.Section == "genshinNativeStrings") == 10,
            "Special caches retain empty-array and native-string semantics"
        );
        Console.WriteLine($"Normalized usages: {input.MetadataUsages.Count}; generic bodies: {input.GenericMethodPointers.Count}");
        string lastDetail = null;
        var model = new TypeModel(
            input,
            p =>
            {
                if (p.Detail != lastDetail)
                    Console.WriteLine(lastDetail = p.Detail);
            }
        );
        Check(model.TypesByFullName["System.ValueType"].BaseType == model.TypesByFullName["System.Object"], "ParentIndex resolves through Il2CppType references");
        Check(model.TypesByFullName["System.Enum"].BaseType == model.TypesByFullName["System.ValueType"], "Framework inheritance retains enum/value-type semantics");
        Check(model.TypesByFullName["System.Int32"].IsValueType && !model.TypesByFullName["System.String"].IsEnum, "Primitive type identities are intact");
        Check(model.TypesByFullName["System.DayOfWeek"].GetEnumUnderlyingType() == model.TypesByFullName["System.Int32"], "Enum ElementTypeIndex resolves the expected scalar type");
        var objectDef = model.TypesByFullName["System.Object"].Definition;
        Check(!input.TypeReferences[objectDef.ByValTypeIndex].ByRef && input.TypeReferences[objectDef.ByRefTypeIndex].ByRef, "ByVal and ByRef retain their distinct fields");
        Check(model.GenericMethods.Count == 570843, "All generic method contexts retain the correct arity");
        Check(model.AttributesByIndices.Count == 74864, "Image-scoped tokens resolve all custom attributes");
        Check(model.Assemblies.Single(a => a.ShortName == "mscorlib.dll").FullName.Contains("Version=4.0.0.0"), "Assembly version components decode independently");
        Check(input.StringLiterals[7] == "navMeshData" && input.StringLiterals[10] == "http://www.w3.org/XML/1998/namespace", "Multi-block string literal keystream advances by block ordinal");
        Check(model.TypesByFullName["System.Math"].DeclaredFields.Single(f => f.Name == "PI").DefaultValue is double pi && pi == Math.PI, "Known scalar default uses the restored blob format");
        Check(
            adapter.AdditionalUsages.Where(u => u.Section == "genshinNativeStrings").All(u => u.Length == u.Value.Length)
                && adapter.AdditionalUsages.Where(u => u.Section == "genshinEmptyArrays").All(u => u.Length == 0 && u.Subtype == 0),
            "Special cache lengths and values survive normalization"
        );
        var list = model.TypesByFullName["System.Collections.Generic.List`1"];
        Check(
            list.DeclaredProperties.Single(p => p.Name == "Count").GetMethod.Name == "get_Count" && list.DeclaredProperties.Single(p => p.Name == "Capacity").SetMethod.Name == "set_Capacity",
            "Known properties bind the actual getter and setter instead of a valid constructor index"
        );
        var eventInfo = model.TypesByFullName["System.AppDomain"].DeclaredEvents.Single(e => e.Name == "AssemblyLoad");
        Check(
            eventInfo.AddMethod.Name == "add_AssemblyLoad" && eventInfo.RemoveMethod.Name == "remove_AssemblyLoad" && eventInfo.RaiseMethod == null,
            "Known event retains add/remove and the absent raise method"
        );
        var method = model.TypesByFullName["System.Object"].DeclaredMethods.First(m => m.Name == "ToString");
        Check(
            input.GetMethodPointer(default, method.Definition)?.Start > 0 && input.GetInvokerIndex(default, method.Definition) >= 0,
            "Global method and Invoker lookups work for a representative framework method"
        );
        var app = new AppModel(model, false).Build();
        var headers = app.RuntimeCppTypes;
        Check(
            headers.GetComplexType("Il2CppClass_0").SizeBytes == 0xD0 && headers.GetComplexType("Il2CppClass_0").Flattened["static_fields"].OffsetBytes == 0x78,
            "Runtime class prefix and static storage match native offsets"
        );
        Check(
            headers.GetComplexType("MethodInfo").SizeBytes == 0x38 && headers.GetComplexType("MethodInfo").Flattened["flags"].OffsetBytes == 0x28,
            "MethodInfo preserves its shuffled runtime layout"
        );
        ValidateCacheJson(app);
        if (args.Length > 2 && !args[2].StartsWith("--"))
        {
            Directory.CreateDirectory(args[2]);
            File.WriteAllText(
                Path.Combine(args[2], "runtime-check.cpp"),
                "#include <stdint.h>\n#include <stddef.h>\n"
                    + app.UnityHeaders.GetTypeHeaderText(64)
                    + "\nstatic_assert(sizeof(Il2CppClass) == 0xD0);\nstatic_assert(offsetof(Il2CppClass, static_fields) == 0x78);\n"
                    + "static_assert(offsetof(Il2CppClass, byval_arg) == 0x68);\nstatic_assert(offsetof(Il2CppClass, this_arg) == 0x10);\n"
                    + "static_assert(sizeof(MethodInfo) == 0x38);\nstatic_assert(offsetof(MethodInfo, flags) == 0x28);\nstatic_assert(offsetof(MethodInfo, slot) == 0x2A);\n"
                    + "static_assert(sizeof(PropertyInfo) == 0x28);\nstatic_assert(sizeof(EventInfo) == 0x30);\nstatic_assert(sizeof(FieldInfo) == 0x20);\n"
                    + "static_assert(sizeof(Il2CppType) == 0x10);\nstatic_assert(offsetof(Il2CppCodeRegistration, methodPointers) == 0x28);\n"
                    + "static_assert(offsetof(Il2CppCodeRegistration, customAttributeGenerators) == 0x30);\nstatic_assert(offsetof(Il2CppMetadataRegistration, types) == 0x50);\n"
            );
            if (args.Contains("--write-header"))
                input.Metadata.GamePlugin.WriteHeader(app, Path.Combine(args[2], "il2cpp.h"), true);
            if (args.Contains("--write-json"))
                input.Metadata.GamePlugin.WriteJson(app, Path.Combine(args[2], "metadata.json"), false);
            if (args.Contains("--write-dll"))
                input.Metadata.GamePlugin.WriteAssemblies(model, Path.Combine(args[2], "DummyDll"), false, (_, s) => Console.WriteLine(s));
        }
        Console.WriteLine("Genshin tests passed");
    }

    private static void ValidateCacheJson(AppModel app)
    {
        // Exercise only the changed output sections on the real model. The complete export
        // is large; invoking the section writer avoids repeating every unchanged section.
        using var bytes = new MemoryStream();
        using var writer = new System.Text.Json.Utf8JsonWriter(bytes);
        var formatter = new global::Il2CppInspector.Outputs.JSONMetadata(app);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(global::Il2CppInspector.Outputs.JSONMetadata).GetField("writer", flags).SetValue(formatter, writer);
        writer.WriteStartObject();
        typeof(global::Il2CppInspector.Outputs.JSONMetadata).GetMethod("writeAdditionalUsages", flags).Invoke(formatter, null);
        writer.WriteEndObject();
        writer.Flush();
        using var json = System.Text.Json.JsonDocument.Parse(bytes.ToArray());
        var strings = json.RootElement.GetProperty("genshinNativeStrings");
        var tick = strings.EnumerateArray().Single(s => s.GetProperty("value").GetString() == "Tick");
        Check(strings.GetArrayLength() == 10 && tick.GetProperty("length").GetInt32() == 4, "Native string JSON preserves text and explicit length without a managed type index");
        var arrays = json.RootElement.GetProperty("genshinEmptyArrays");
        Check(
            arrays.GetArrayLength() == 2564 && arrays.EnumerateArray().All(a => a.GetProperty("length").GetInt32() == 0 && a.GetProperty("subtype").GetUInt32() == 0),
            "Empty-array JSON preserves zero length and subtype"
        );
    }
}
