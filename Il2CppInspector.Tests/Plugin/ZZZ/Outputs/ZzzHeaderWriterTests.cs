namespace Il2CppInspector.Tests.Plugin.ZZZ.Outputs
{
    internal static class ZzzHeaderWriterTests
    {
        internal static void Run(ZzzTestContext context, AppModel app, string output, string[] args)
        {
            var input = context.Input;
            var model = context.Model;
            var vector = context.Vector;
            var list = context.List;
            var nullablePolicy = context.NullablePolicy;
            var blend = context.Blend;

            var native = typeof(AppModel)
                .GetProperty("GameNativeModel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .GetValue(app);
            var ctype = native
                .GetType()
                .GetMethod("CType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .CreateDelegate<Func<global::Il2CppInspector.Reflection.TypeInfo, string>>(native);
            var complete = native
                .GetType()
                .GetMethod("IsSignatureTypeComplete", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .CreateDelegate<Func<global::Il2CppInspector.Reflection.TypeInfo, bool>>(native);
            var className = native
                .GetType()
                .GetMethod("ClassName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .CreateDelegate<Func<global::Il2CppInspector.Reflection.TypeInfo, string>>(native);
            var arrayName = native
                .GetType()
                .GetMethod("ArrayName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .CreateDelegate<Func<global::Il2CppInspector.Reflection.TypeInfo, string>>(native);
            var signatureType = typeof(global::Il2CppInspector.Plugin.ZZZ.ZzzPlugin).Assembly.GetType("Il2CppInspector.Outputs.ZzzJsonMetadata");
            var signatureWriter = Activator.CreateInstance(signatureType, app);
            var vectorConstructor = vector.DeclaredConstructors.Single(c => c.DeclaredParameters.Count == 3);
            var vectorSignature = ((string Name, string ReturnType, string Parameters, bool Complete))
                signatureType
                    .GetMethod("NativeSignature", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    .Invoke(signatureWriter, [vectorConstructor.Index, -1]);
            Check(
                vectorSignature.Parameters.Contains("float x") && vectorSignature.Parameters.Contains("float y") && vectorSignature.Parameters.Contains("float z") && vectorSignature.Complete,
                "Native method signatures retain the metadata parameter names instead of arg placeholders"
            );
            if (args.Contains("--write-project-pointers"))
            {
                var projectData = Path.Combine(output, "CppScaffolding", "appdata");
                var scaffolding = new CppScaffolding(app, useBetterArraySize: true);
                typeof(CppScaffolding)
                    .GetMethod("WriteGameApplicationPointers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    .Invoke(scaffolding, [projectData]);
                var functions = 0;
                var infos = 0;
                var samples = new List<string>();
                foreach (var line in File.ReadLines(Path.Combine(projectData, "il2cpp-functions.h")))
                {
                    if (line.StartsWith("DO_APP_FUNC("))
                    {
                        functions++;
                        if (samples.Count < 128)
                            samples.Add(line);
                    }
                    if (line.StartsWith("DO_APP_FUNC_METHODINFO("))
                        infos++;
                }
                Check(
                    functions == 1633672 && infos == input.MetadataUsages.Count(u => u.Type is MetadataUsageType.MethodDef or MetadataUsageType.MethodRef),
                    "C++ project pointers contain all 1,633,672 compiled methods and every MethodInfo usage"
                );
                Check(
                    File.ReadLines(Path.Combine(projectData, "il2cpp-types-ptr.h")).Count(line => line.StartsWith("DO_TYPEDEF(")) > 100000,
                    "C++ project TypeInfo pointers are restored from usages instead of the empty AppModel collection"
                );
                File.WriteAllText(
                    Path.Combine(output, "scaffolding-capability-check.cpp"),
                    "#include \"il2cpp.h\"\nnamespace app {\n"
                        + "#define DO_APP_FUNC(a, r, n, p) r (*n) p\n"
                        + string.Join("\n", samples)
                        + "\n#undef DO_APP_FUNC\n#define DO_TYPEDEF(a, n) n ## __Class ** n ## __TypeInfo\n"
                        + "#include \"CppScaffolding/appdata/il2cpp-types-ptr.h\"\n}\n",
                    Encoding.UTF8
                );
            }
            Check(
                ctype(vector.MakeArrayType()) == "struct " + arrayName(vector.MakeArrayType()) + " *"
                    && ctype(vector.MakeArrayType().MakeArrayType()).Contains("Zzz_Array_1_Zzz_Array_1_")
                    && arrayName(model.TypesByFullName["System.Int32"].MakeArrayType(2)).StartsWith("Zzz_Array_2_"),
                "Array declarations retain value elements, jagged pointer elements and multidimensional rank"
            );
            Check(
                ctype(nullablePolicy.MakeByRefType()).StartsWith("struct Zzz_Generic_") && !ctype(nullablePolicy.MakeByRefType()).Contains("* *"),
                "ByRef Nullable<enum> has one level of native indirection"
            );
            Check(ctype(model.TypesByFullName["System.IntPtr"].MakeByRefType()) == "void * *", "IntPtr ByRef retains its pointer slot indirection");
            var composite = model.TypesByDefinitionIndex[119].MakeGenericType(vector, vector);
            var unknown = model.TypesByDefinitionIndex[1556].MakeGenericType(composite);
            for (var depth = 0; complete(unknown) && depth < 8; depth++)
            {
                composite = model.TypesByDefinitionIndex[119].MakeGenericType(composite, composite);
                unknown = model.TypesByDefinitionIndex[1556].MakeGenericType(composite);
            }
            Check(
                !complete(unknown) && complete(unknown.MakeByRefType()) && !ctype(unknown.MakeByRefType()).Contains("* *"),
                "Opaque struct storage needs a layout by value and only one pointer by reference"
            );
            Check(
                className(unknown) == "Il2CppClass" && className(model.TypesByDefinitionIndex[1556].MakeGenericType(unknown)) == "Il2CppClass",
                "Unrecorded constructed types use the generic class prefix and cannot alias a definition's class name"
            );
            Check(File.ReadAllText(Path.Combine(output, "il2cpp.py")).Contains("-target x86_64-pc-windows-msvc"), "IDA Clang parser uses the PE's Windows x64 ABI");
            if (args.Contains("--write-native") || args.Contains("--write-header") || args.Contains("--write-project") || args.Contains("--finish-project"))
            {
                var headerList = list.MakeGenericType(vector);
                var listFields = ctype(headerList).Replace("struct ", "").TrimEnd(' ', '*') + "__Fields";
                var selectedArrays = new[] { vector.MakeArrayType(), vector.MakeArrayType().MakeArrayType(), model.TypesByFullName["System.Int32"].MakeArrayType(2) }.Select(arrayName).ToHashSet();
                var selectedDeclarations = new List<string>();
                var enumName = ctype(model.TypesByFullName["System.DayOfWeek"]);
                var byteEnumName = ctype(blend);
                var singleVtableType = model.TypesByFullName["System.Object"];
                var splitVtableType = model.TypesByDefinitionIndex.First(t =>
                    t != null && !t.IsGenericType && input.GetVTable(t.Definition).Any(u => u.IsValid && u.Type == MetadataUsageType.MethodRef)
                );
                var slotTypes = new[] { singleVtableType, splitVtableType, headerList };
                string NativeName(global::Il2CppInspector.Reflection.TypeInfo type) => ctype(type).Replace("struct ", "").TrimEnd(' ', '*');
                var semanticLines = new HashSet<string>();
                var semanticAssertions = new StringBuilder();
                semanticAssertions.AppendLine($"static_assert(sizeof({enumName}) == 4);");
                semanticAssertions.AppendLine($"static_assert(sizeof({byteEnumName}) == 1);");
                semanticAssertions.AppendLine($"static_assert(sizeof(struct {byteEnumName}__Boxed) == 17);");
                semanticAssertions.AppendLine($"static_assert(__builtin_offsetof(struct {byteEnumName}__Boxed, value) == 16);");
                semanticAssertions.AppendLine($"static_assert((int){enumName}::Sunday == 0 && (int){enumName}::Saturday == 6);");
                var interfaceType = model.TypesByDefinitionIndex.First(t => t is { IsInterface: true, IsGenericType: false } && t.Definition.MethodCount > 0 && t.Definition.VTableCount == 0);
                var interfaceView = $"Zzz_Type_{interfaceType.Index}__InterfaceVTable";
                semanticAssertions.AppendLine($"static_assert(sizeof({interfaceView}) == {interfaceType.Definition.MethodCount * 8});");
                semanticAssertions.AppendLine($"static_assert(sizeof(struct Zzz_Type_{interfaceType.Index}__Class) == 0xD0);");
                foreach (var slotType in slotTypes)
                {
                    var definition = slotType.Definition.IsValid ? slotType.Definition : slotType.GetGenericTypeDefinition().Definition;
                    var tableName = NativeName(slotType) + "__VTable";
                    var slots = input.GetVTable(definition);
                    var splitTable = slotType.IsGenericType && !slotType.IsGenericTypeDefinition || slots.Any(u => u.IsValid && u.Type == MetadataUsageType.MethodRef);
                    semanticAssertions.AppendLine($"static_assert(sizeof({tableName}) == {slots.Length * (splitTable ? 16 : 8)});");
                    for (var slot = 0; slot < slots.Length; slot++)
                    {
                        var entry = slots[slot];
                        var definitionIndex =
                            !entry.IsValid ? -1
                            : entry.Type == MetadataUsageType.MethodRef ? input.MethodSpecs[entry.SourceIndex].MethodDefinitionIndex
                            : entry.SourceIndex;
                        var slotName = definitionIndex < 0 ? "unknown" : model.MethodsByDefinitionIndex[definitionIndex].Name.ToCIdentifier();
                        // These samples have no keyword method names; the generated names still go through FieldName.
                        semanticLines.Add($"        Il2CppMethodPointer methodPtr_{slot}_{slotName};");
                        semanticAssertions.AppendLine($"static_assert(__builtin_offsetof({tableName}, methodPtr_{slot}_{slotName}) == {slot * 8});");
                        if (splitTable)
                            semanticAssertions.AppendLine($"static_assert(__builtin_offsetof({tableName}, method_{slot}_{slotName}) == {(slots.Length + slot) * 8});");
                    }
                }
                var inFields = false;
                foreach (var line in File.ReadLines(Path.Combine(output, "il2cpp.h")))
                {
                    if (line == $"struct {listFields} {{")
                        inFields = true;
                    if (inFields)
                        selectedDeclarations.Add(line);
                    if (inFields && line == "};")
                        inFields = false;
                    if (selectedArrays.Any(name => line.StartsWith($"struct {name} {{")))
                        selectedDeclarations.Add(line);
                    semanticLines.Remove(line);
                    if (line == "    Sunday = 0x0ULL," || line == "    Saturday = 0x6ULL,")
                        selectedDeclarations.Add(line);
                }
                Check(
                    selectedDeclarations.Any(line => line.Contains("_items;"))
                        && selectedDeclarations.Any(line => line.Contains("_size;"))
                        && selectedDeclarations.Any(line => line.Contains("_version;")),
                    "Closed List<Vector3> emits named typed fields instead of an opaque byte buffer"
                );
                Check(
                    selectedArrays.All(name => selectedDeclarations.Any(line => line.StartsWith($"struct {name} {{") && line.Contains(" vector[32];"))),
                    "Typed vector, jagged vector and rank-two array declarations are emitted"
                );
                Check(
                    semanticLines.Count == 0 && selectedDeclarations.Contains("    Sunday = 0x0ULL,") && selectedDeclarations.Contains("    Saturday = 0x6ULL,"),
                    "Native headers expose enum constants and method names for ordinary, split and generic vtable slots"
                );
                File.WriteAllText(
                    Path.Combine(output, "native-capability-check.cpp"),
                    "#include \"il2cpp.h\"\n"
                        + $"static_assert(__builtin_offsetof(struct {listFields}, _items) == {headerList.GetField("_items").Offset - 16});\n"
                        + $"static_assert(__builtin_offsetof(struct {listFields}, _size) == {headerList.GetField("_size").Offset - 16});\n"
                        + $"static_assert(__builtin_offsetof(struct {listFields}, _version) == {headerList.GetField("_version").Offset - 16});\n"
                        + $"static_assert(sizeof(struct {listFields}) == {headerList.Sizes.InstanceSize - 16});\n"
                        + $"static_assert(__builtin_offsetof(struct {arrayName(vector.MakeArrayType())}, vector) == 32);\n"
                        + $"static_assert(sizeof(((struct {arrayName(vector.MakeArrayType())} *)0)->vector[0]) == 12);\n"
                        + $"static_assert(sizeof(((struct {arrayName(vector.MakeArrayType().MakeArrayType())} *)0)->vector[0]) == 8);\n"
                        + "static_assert(sizeof(il2cpp_array_size_t) == 8);\n"
                        + semanticAssertions,
                    Encoding.UTF8
                );
                File.WriteAllText(
                    Path.Combine(output, "native-analysis-check.cpp"),
                    "#define _IDA_\n#include \"il2cpp.h\"\n" + $"static_assert(sizeof({byteEnumName}) == 1);\n" + $"static_assert({enumName}_Sunday == 0 && {enumName}_Saturday == 6);\n",
                    Encoding.UTF8
                );
            }
        }
    }
}
