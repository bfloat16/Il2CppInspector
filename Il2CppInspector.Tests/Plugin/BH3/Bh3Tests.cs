using Il2CppInspector.Next;
using Il2CppInspector.Utils;

namespace Il2CppInspector.Tests.Plugin.BH3
{
    internal static class Bh3Tests
    {
        internal static void Run(string[] args)
        {
            using (var blob = new BinaryObjectStreamReader())
            {
                blob.WriteByte(0xFF);
                blob.Position = 0;
                Check(BlobReader.GetConstantValueFromBlob(null, Il2CppTypeEnum.IL2CPP_TYPE_I1, blob) is sbyte signed && signed == -1, "The shared blob reader retains signed I1 constants");
                blob.Position = 0;
                Check(BlobReader.GetConstantValueFromBlob(null, Il2CppTypeEnum.IL2CPP_TYPE_U1, blob) is byte unsigned && unsigned == 255, "Unsigned U1 constants retain the same raw byte");
            }
            var input =
                Inspector
                    .LoadFromFile(
                        Path.Combine(args[1], "UserAssembly.dll"),
                        Path.Combine(args[1], "global-metadata.dat"),
                        new LoadOptions { Game = "BH3_CN_Windows_9.1.0" },
                        (_, message) => Console.WriteLine(message)
                    )
                    ?.Single()
                ?? throw new InvalidDataException("BH3 input failed to load.");
            Check(input.TypeDefinitions.Length == 77799 && input.Images.Length == 212, "BH3 image ranges exclude the type sentinels");
            Check(input.Methods.Length == 561798 && input.Fields.Length == 362445, "BH3 method and field table boundaries");
            Check(input.Methods.Max(m => m.ParameterCount) == 39, "Method parameter counts retain only eight bits");
            Check(input.Params.Length == 460434, "Method parameter ranges cover the real parameter table");
            Check(input.AttributeTypeRanges.Length == 37538 && input.Metadata.AttributeTypeIndices.Length == 41122, "Attribute ranges and reference indices retain their physical table lengths");
            Check(
                input.Images.Sum(i => (long)i.CustomAttributeCount) == input.AttributeTypeRanges.Length && input.Binary.CodeRegistration.CustomAttributeCount == input.AttributeTypeRanges.Length,
                "Image ranges and code registration agree on all 37,538 attribute rows"
            );
            Check(
                input.AttributeTypeRanges.Select((range, i) => range.Start == (i == 0 ? 0 : input.AttributeTypeRanges[i - 1].Start + input.AttributeTypeRanges[i - 1].Count)).All(v => v)
                    && input.AttributeTypeRanges[^1].Start + input.AttributeTypeRanges[^1].Count == input.Metadata.AttributeTypeIndices.Length,
                "Packed attribute ranges cover all 41,122 type references without gaps"
            );
            Check(input.CustomAttributeGenerators.Length == 37538 && input.CustomAttributeGenerators.Distinct().Count() == 6930, "Attribute rows retain their shared generator functions");
            var defaults = input
                .Metadata.FieldDefaultValues.Select(d => (TypeIndex: (int)d.TypeIndex, d.DataIndex))
                .Concat(input.Metadata.ParameterDefaultValues.Select(d => (TypeIndex: (int)d.TypeIndex, d.DataIndex)));
            Check(
                defaults
                    .Where(d => d.DataIndex >= 0 && input.TypeReferences[d.TypeIndex].Type == Il2CppTypeEnum.IL2CPP_TYPE_I1)
                    .All(d => input.Metadata.GameAdapter.DecodeDefault(d.TypeIndex, d.DataIndex, input.Binary).Value is sbyte),
                "BH3 signed byte defaults pass through the shared reader"
            );
            Check(input.Assemblies[0].Aname.Major == 4 && input.Assemblies[0].Aname.Minor == 0, "mscorlib retains its standard 4.0.0.0 identity");
            Console.WriteLine("Building BH3 type model");
            string lastDetail = null;
            var model = new TypeModel(
                input,
                p =>
                {
                    if (p.Detail != lastDetail)
                        Console.WriteLine(lastDetail = p.Detail);
                }
            );
            Check(model.TypesByFullName["System.String"].BaseType == model.TypesByFullName["System.Object"], "Parent indices select Il2CppType references");
            Check(model.Assemblies.Single(a => a.ShortName == "ICSharpCode.SharpZipLib.dll").FullName.Contains("Version=0.86.0.518"), "All four shuffled assembly version components decode");
            Check(input.MethodSpecs.Length == 251755, "Generic method definitions come from the eight-byte table");
            Check(input.GenericMethodPointers.Count == 246656, "Generic method definitions retain all 246,656 compiled bodies");
            Check(model.TypesByReferenceIndex.All(t => t != null), "All 469,900 type references resolve");
            var attributeTypes = input.Metadata.AttributeTypeIndices.Select(i => model.TypesByReferenceIndex[i]).Distinct().ToArray();
            Check(
                attributeTypes.Length == 204
                    && attributeTypes.All(t =>
                    {
                        for (var b = t.BaseType; b != null; b = b.BaseType)
                            if (b.FullName == "System.Attribute")
                                return true;
                        return false;
                    }),
                "All attribute reference indices resolve to the 204 System.Attribute subclasses"
            );
            Check(model.AttributesByIndices.Count == 41122, "Standard token lookup materializes every attribute instance");
            Check(
                model.AttributesByIndices.Values.All(a => a.VirtualAddress.Start == input.CustomAttributeGenerators[a.Index]),
                "Standard reflection associates attribute instances with their row's generator"
            );
            Check(
                model.TypesByFullName["System.ObsoleteAttribute"].GetCustomAttributes("System.AttributeUsageAttribute").Any(),
                "Known framework attribute usage resolves through standard reflection"
            );
            Check(!model.TypesByFullName["System.String"].IsEnum && model.TypesByFullName["System.Int32"].IsValueType, "Runtime bitfields preserve primitive type identities");
            var enums = model.TypesByDefinitionIndex.Where(t => t.IsEnum).ToArray();
            Check(enums.Length == 11121 && enums.All(t => t.GetEnumUnderlyingType() != null), "All enums retain scalar types, including the five stripped definitions");
            Check(
                enums.Where(t => t.DeclaredFields.Any(f => f.Name == "value__")).All(t => t.GetEnumUnderlyingType() == t.DeclaredFields.Single(f => f.Name == "value__").FieldType),
                "Decoded enum element indices agree with the independent value__ field types"
            );
            var pi = model.TypesByFullName["System.Math"].DeclaredFields.Single(f => f.Name == "PI").DefaultValue;
            Check(pi is double value && value == Math.PI, "The standard blob reader decodes the known Math.PI constant");
            Check(model.TypesByFullName["System.Object"].DeclaredMethods.First(m => m.Name == "ToString").IsPublic, "Method flags retain public visibility");
            Check(input.Methods.All(m => (m.Token >> 24) == 6 && (m.Slot == ushort.MaxValue || m.Slot < 512)), "Method tokens and virtual slots retain their semantic ranges");
            Check(model.GenericMethods.Count == 251755, "All generic method contexts match their declaring type and method arity");
            Check(input.MetadataUsages.Count == 348209 && input.FieldRefs.Length == 847, "All 348,209 usage slots and 847 field references survive normalization");
            Check(input.MetadataUsages.All(u => u.Type != MetadataUsageType.FieldRva), "Runtime caches are not emitted as field RVA usages");
            Check(
                input.FieldRefs.All(field => (uint)(int)field.FieldIndex < (uint)model.TypesByReferenceIndex[field.TypeIndex].DeclaredFields.Count),
                "All FieldRefs resolve within their declaring types"
            );
            var app = new AppModel(model, false).Build();
            var headers = app.RuntimeCppTypes;
            Check(headers.GetComplexType("Il2CppClass_0").SizeBytes == 0xD8, "BH3 class headers retain the separate vtable allocation");
            Check(headers.GetComplexType("Il2CppClass_0").Flattened["static_fields"].OffsetBytes == 0x30, "Static storage uses klass+0x30");
            Check(headers.GetComplexType("Il2CppClass_0").Flattened["instance_size"].OffsetBytes == 0xC4, "Class instance size retains its runtime offset");
            Check(headers.GetComplexType("Il2CppCodeGenModule").Flattened["methodPointerArray"].OffsetBytes == 0x50, "Module pointer arrays retain their runtime offset");
            Check(
                headers.GetComplexType("MethodInfo").Flattened["flags"].OffsetBytes == 0x30 && headers.GetComplexType("MethodInfo").Flattened["slot"].OffsetBytes == 0x2E,
                "Method attributes and slots match the runtime accessor"
            );
            Check(
                headers.GetComplexType("Il2CppAssembly").Flattened["encodedFlags"].OffsetBytes == 0x40 && headers.GetComplexType("Il2CppAssembly").SizeBytes == 0x50,
                "Assembly fields preserve the initializer offsets"
            );
            Check(headers.GetComplexType("MethodInfo").Flattened["token"].OffsetBytes == 0x28, "MethodInfo token retains the runtime token offset");
            Check(
                headers.GetComplexType("Il2CppCodeRegistration").Flattened["encodedCustomAttributeCount"].OffsetBytes == 0x58
                    && headers.GetComplexType("Il2CppCodeRegistration").Flattened["customAttributeGenerators"].OffsetBytes == 0x90,
                "Runtime registration declares the recovered attribute count and generator pointer"
            );
            var native = (NativeTypeModel)app.GameNativeModel;
            var objectClass = app.CppTypeCollection.GetComplexType(native.ClassName(model.TypesByFullName["System.Object"]));
            Check(objectClass.SizeBytes == 0xD8 && objectClass.Flattened["vtable"].Type.SizeBytes == 8, "Generated class keeps a vtable pointer at offset zero");
            Check(input.TypeDefinitionSizes[77797].InstanceSize == 384, "Late definitions retain their real layout groups");
            if (args.Length > 2)
            {
                Directory.CreateDirectory(args[2]);
                File.WriteAllText(
                    Path.Combine(args[2], "runtime-check.cpp"),
                    "#include <stdint.h>\n#include <stddef.h>\n"
                        + app.UnityHeaders.GetTypeHeaderText(64)
                        + "\n"
                        + "static_assert(sizeof(Il2CppClass) == 0xD8);\n"
                        + "static_assert(offsetof(Il2CppClass, static_fields) == 0x30);\n"
                        + "static_assert(offsetof(Il2CppClass, instance_size) == 0xC4);\n"
                        + "static_assert(sizeof(MethodInfo) == 0x40);\n"
                        + "static_assert(offsetof(MethodInfo, flags) == 0x30);\n"
                        + "static_assert(offsetof(MethodInfo, slot) == 0x2E);\n"
                        + "static_assert(sizeof(Il2CppGenericClass) == 0x20);\n"
                        + "static_assert(sizeof(Il2CppCodeGenModule) == 0x60);\n"
                        + "static_assert(offsetof(Il2CppCodeGenModule, methodPointerArray) == 0x50);\n"
                        + "static_assert(offsetof(Il2CppCodeRegistration, genericMethodPointers) == 0x08);\n"
                        + "static_assert(offsetof(Il2CppCodeRegistration, encodedGenericMethodPointersCount) == 0x48);\n"
                        + "static_assert(offsetof(Il2CppCodeRegistration, genericAdjustorThunks) == 0x80);\n"
                        + "static_assert(offsetof(Il2CppCodeRegistration, encodedCustomAttributeCount) == 0x58);\n"
                        + "static_assert(offsetof(Il2CppCodeRegistration, customAttributeGenerators) == 0x90);\n"
                );
                if (args.Contains("--write-header"))
                    new CppScaffolding(app, useBetterArraySize: true).WriteTypes(Path.Combine(args[2], "il2cpp.h"));
            }
            Console.WriteLine("BH3 tests passed");
            Console.WriteLine(
                $"Generic bodies: {input.GenericMethodPointers.Count}; usages: {input.MetadataUsages.Count}; peak working set: {System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MiB"
            );
        }
    }
}
