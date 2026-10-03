namespace Il2CppInspector.Tests.Plugin.ZZZ.Model
{
    internal static class ZzzAnalysisModelTests
    {
        internal static void Run(ZzzTestContext context, string[] args)
        {
            var input = context.Input;
            var model = context.Model;
            var vector = context.Vector;
            var list = context.List;
            var genericMethod = context.GenericMethod;
            var app = new AppModel(model, false).Build(new UnityVersion("2019.4.40f1"));
            Check(app.Fields.Count == 1020 && app.Fields.Values.All(f => f.Value.Length > 0), "Application model exposes all FieldInfo payloads, including managed structure bytes");
            Check(app.Methods.Count >= 1600000 && app.Methods.Values.Any(m => m.Group == "types_from_usages"), "Public application method collections contain compiled and usage-only methods");
            var allInvokers = model.MethodInvokers;
            Check(
                input.GenericMethodInvokerIndices.Values.Where(i => i >= 0).Distinct().All(i => allInvokers[i] != null),
                "Public invoker enumeration includes generic-only signatures without prewarming the generic method collection"
            );
            Check(model.GenericMethods.Count == input.MethodSpecs.Distinct().Count(), "Public generic enumeration includes every distinct spec");
            var cpp = app.CppTypeCollection;
            var value = app.Types[vector].CppValueType;
            Check(
                value.SizeBytes == 12 && value["x"].OffsetBytes == 0 && value["y"].OffsetBytes == 4 && value["z"].OffsetBytes == 8,
                "Public C++ value layouts preserve recovered Vector3 storage offsets"
            );
            var concreteList = list.MakeGenericType(vector);
            var listCpp = app.Types[concreteList].CppType;
            Check(listCpp.SizeBytes == 32 && ((CppComplexType)listCpp["fields"].Type)["_size"].OffsetBytes == 8, "Public closed generic C++ objects retain named substituted fields");
            var listClass = (CppComplexType)((CppPointerType)listCpp["klass"].Type).ElementType;
            var table = (CppComplexType)listClass["vtable"].Type;
            Check(
                listClass["vtable"].OffsetBytes == 0xD0
                    && table["methodPtr_1_Finalize"].OffsetBytes == 8
                    && table["method_1_Finalize"].OffsetBytes == concreteList.GetGenericTypeDefinition().Definition.VTableCount * 8 + 8,
                "Public C++ generic class vtables retain the conditional SoA layout"
            );
            Check(
                cpp.GetComplexType("Zzz_Type_3__GlobalStaticFields2").Fields.Values.SelectMany(f => f).Min(f => f.OffsetBytes) == 0x210,
                "Public C++ global-static regions retain the process buffer offsets"
            );
            var interfaceType = model.TypesByDefinitionIndex.First(t => t is { IsInterface: true, IsGenericType: false } && t.Definition.MethodCount > 0 && t.Definition.VTableCount == 0);
            var interfaceView = cpp.GetComplexType($"Zzz_Type_{interfaceType.Index}__InterfaceVTable");
            Check(
                interfaceView.SizeBytes == interfaceType.Definition.MethodCount * 8 && cpp.GetComplexType($"Zzz_Type_{interfaceType.Index}__Class").SizeBytes == 0xD0,
                "Interface callable views expose method slots without changing the physical interface class layout"
            );
            var globalRegion = cpp.GetComplexType("Zzz_Type_3__GlobalStaticFields2");
            var regionField = globalRegion.Fields.Values.SelectMany(f => f).First();
            var rendered =
                "#include \"il2cpp.h\"\n"
                + value.ToString().Replace($" {value.Name} {{", " AuditValue {")
                + globalRegion.ToString().Replace($" {globalRegion.Name} {{", " AuditStatic {")
                + table.ToString().Replace($" {table.Name} {{", " AuditVTable {")
                + "static_assert(sizeof(AuditValue) == 12);\nstatic_assert(__alignof(AuditValue) == 4);\n"
                + $"static_assert(__builtin_offsetof(AuditStatic, {regionField.Name}) == {regionField.OffsetBytes});\n"
                + $"static_assert(sizeof(AuditVTable) == {table.SizeBytes});\n"
                + "static_assert(__builtin_offsetof(AuditVTable, methodPtr_1_Finalize) == 8);\n";
            File.WriteAllText("output/zzz-gaps-fixed/native-model-render-check.cpp", rendered, Encoding.UTF8);
            if (args.Contains("--analysis-layouts"))
            {
                var ordered = app.DependencyOrderedCppTypes;
                Check(ordered.Count > 100000 && app.RequiredForwardDefinitions.Count > 100000, "Public dependency ordering and forward declarations include all materialized native layouts");
                Console.WriteLine($"Analysis types={app.Types.Count}, methods={app.Methods.Count}, declarations={ordered.Count}, forwards={app.RequiredForwardDefinitions.Count}");
                return;
            }
            var map = app.GetAddressMap();
            foreach (var metadataUsage in input.MetadataUsages)
            {
                if (metadataUsage.Type is MetadataUsageType.MethodDef or MetadataUsageType.MethodRef && map[metadataUsage.VirtualAddress] is not AppMethodReference)
                    throw new InvalidOperationException("MethodInfo usage missing in address map");
                if (metadataUsage.Type is MetadataUsageType.Type or MetadataUsageType.TypeInfo && map[metadataUsage.VirtualAddress] is not AppTypeReference)
                    throw new InvalidOperationException("Type usage missing in address map");
            }
            Check(
                map[genericMethod.VirtualAddress.Value.Start] is AppMethod && map[vector.DeclaredConstructors[0].VirtualAddress.Value.Start] is AppMethod,
                "Address map resolves ordinary and generic compiled methods instead of unknown functions"
            );
            Check(
                ((CppFnPtrType)map[input.Binary.RegistrationFunctionPointer]).Arguments.Count == 0 && ((CppFnPtrType)map[input.Binary.RegistrationFunctionPointer]).ReturnType.Name == "void *",
                "Address map uses the observed pointer return and zero arguments of the MORAX registration initializer"
            );
            Check(app.GetMethodGroup("types_from_methods").Any() && app.GetTypeGroup("types_from_usages").Any(), "Public application model group queries expose populated collections");
            app.Build(new UnityVersion("2019.4.39f1"));
            Check(app.Fields.Count == 1020, "Rebuilding application models resets derived caches and preserves field data");
        }
    }
}
