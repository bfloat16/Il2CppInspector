namespace Il2CppInspector.Tests.Plugin.ZZZ.IL2CPP.ZenlessZoneZero
{
    internal static class ZzzRuntimeHeadersTests
    {
        internal static void Run(ZzzTestContext context, AppModel app)
        {
            var input = context.Input;
            var model = context.Model;
            var vector = context.Vector;
            var headers = (CppTypeCollection)
                typeof(AppModel)
                    .GetProperty("RuntimeCppTypes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    .GetValue(app);
            var arrayModel = typeof(AppModel)
                .GetProperty("GameNativeModel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .GetValue(app);
            var registerArray = arrayModel.GetType().GetMethod("ArrayName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            registerArray.Invoke(arrayModel, [vector.MakeArrayType()]);
            registerArray.Invoke(arrayModel, [vector.MakeArrayType().MakeArrayType()]);
            registerArray.Invoke(arrayModel, [model.TypesByFullName["System.Int32"].MakeArrayType(2)]);
            Check(
                app.GetVTableOffset() == 0xD0 && app.GetVTableIndexFromClassOffset(0xD8) == 1 && app.GetVTableIndexFromClassOffset(0x108) == 7,
                "ZZZ virtual-call slot lookup uses the eight-byte pointer array rather than stock AoS stride"
            );
            Check(headers.GetComplexType("Il2CppClass_0").SizeBytes == 0xD0 && headers.GetComplexType("MethodInfo").SizeBytes == 0x40, "MORAX runtime headers have IDA-confirmed class/method sizes");
            Check(
                headers.GetComplexType("Il2CppClass_0").Flattened["vtable_count"].OffsetBytes == 0xC4
                    && headers.GetComplexType("Il2CppClass_0").Flattened["native_size"].OffsetBytes == 0xC0
                    && headers.GetComplexType("Il2CppClass_0").Flattened["interface_offsets_count"].OffsetBytes == 0xC6,
                "Vtable, marshal-size and interface-offset count fields match their runtime offsets"
            );
            Check(
                headers.GetComplexType("Il2CppCodeRegistration").Flattened["reversePInvokeWrappers"].OffsetBytes == 0x40
                    && headers.GetComplexType("Il2CppMetadataRegistration").Flattened["globalStaticStorageSlot"].OffsetBytes == 0x78
                    && headers.GetComplexType("Il2CppMetadataRegistration").Flattened["encodedGlobalStaticStorageSize"].OffsetBytes == 0x84,
                "Active registration pointers and counts retain their IDA offsets"
            );
            Check(
                headers.GetComplexType("Il2CppMetadataRegistration").Flattened["nativeSizes"].OffsetBytes == 0x90
                    && headers.GetComplexType("Il2CppCodeGenModule").Flattened["rgctxRanges"].OffsetBytes == 0x30
                    && headers.GetComplexType("Il2CppCodeGenModule").Flattened["rgctxs"].OffsetBytes == 0x50
                    && headers.GetComplexType("Il2CppCodeGenModule").SizeBytes == 0x58,
                "Module and metadata declarations expose the verified prefixes without fictitious padding"
            );
            Check(
                input.Binary.Modules["mscorlib.dll"].MethodPointerCount == 13166
                    && input.Binary.Modules["mscorlib.dll"].RgctxRangesCount == 190
                    && input.Binary.Modules["mscorlib.dll"].RgctxsCount == 951
                    && input.Binary.Modules["mscorlib.dll"].AdjustorThunksCount == 859,
                "Module counts are decoded from their stored fields, including RGCTX and adjustor entries"
            );
            Check(
                input.Binary.Modules.Values.Sum(module => (long)module.RgctxsCount) == 28050 && headers.GetComplexType("Il2CppCodeGenModule").Flattened["encodedRgctxsCount"].OffsetBytes == 0x18,
                "All 170 RGCTX definition counts agree with token-range ends and total 28,050 entries"
            );
            Check(
                headers.GetComplexType("Il2CppClass_0").Flattened["cctor_thread"].OffsetBytes == 0x10
                    && headers.GetComplexType("Il2CppClass_0").Flattened["nestedTypes"].OffsetBytes == 0x30
                    && headers.GetComplexType("Il2CppClass_0").Flattened["element_size"].OffsetBytes == 0xBA
                    && headers.GetComplexType("Il2CppClass_0").Flattened["cctor_started"].OffsetBytes == 0xCA
                    && headers.GetComplexType("Il2CppClass_0").Flattened["cctor_finished"].OffsetBytes == 0xCB,
                "Class declarations expose the observed cctor synchronization and array-element fields"
            );
            Check(
                headers.GetComplexType("Il2CppMetadataRegistration").Flattened["arrayTypes"].OffsetBytes == 0x68
                    && headers.GetComplexType("Il2CppCodeRegistration").Flattened["internalCallHashes"].OffsetBytes == 0x48
                    && headers.GetComplexType("Il2CppCodeRegistration").Flattened["internalCallCache"].OffsetBytes == 0x78
                    && headers.GetComplexType("Il2CppImage").Flattened["encodedNameToClassHashTable"].OffsetBytes == 0x08,
                "Recovered array, internal-call and image-name cache fields retain their runtime offsets"
            );
            Check(
                headers.GetComplexType("Il2CppTokenIndexMethodTuple").SizeBytes == 16
                    && headers.GetComplexType("Il2CppTokenIndexMethodTuple").Flattened["methodInfoUsageIndex"].OffsetBytes == 8
                    && headers.GetComplexType("Il2CppTokenIndexMethodTuple").Flattened["methodSpecIndex"].OffsetBytes == 12,
                "Reverse P/Invoke header records use four words rather than the stock pointer tuple"
            );
            Check(
                headers.GetComplexType("FieldInfo").SizeBytes == 0x20 && headers.GetComplexType("PropertyInfo").SizeBytes == 0x28 && headers.GetComplexType("EventInfo").SizeBytes == 0x38,
                "MORAX reflection records have IDA-confirmed sizes"
            );
            Check(
                headers.GetComplexType("Il2CppAssemblyName").SizeBytes == 0x38
                    && headers.GetComplexType("Il2CppAssemblyName").Flattened["encodedHashLen"].OffsetBytes == 0x10
                    && headers.GetComplexType("Il2CppAssemblyName").Flattened["public_key_token"].OffsetBytes == 0x30
                    && headers.GetComplexType("Il2CppAssembly").SizeBytes == 0x58
                    && headers.GetComplexType("Il2CppAssembly").Flattened["encodedName"].OffsetBytes == 0x08
                    && headers.GetComplexType("Il2CppAssembly").Flattened["encodedImage"].OffsetBytes == 0x48,
                "Runtime assembly/name declarations match the initializer and MonoAssemblyName adapter"
            );
            Check(
                headers.GetComplexType("Il2CppImage").SizeBytes == 0x58
                    && headers.GetComplexType("Il2CppImage").Flattened["encodedAssembly"].OffsetBytes == 0x18
                    && headers.GetComplexType("Il2CppImage").Flattened["encodedCodeGenModule"].OffsetBytes == 0x40
                    && headers.GetComplexType("Il2CppImage").Flattened["encodedName"].OffsetBytes == 0x48
                    && headers.GetComplexType("Il2CppImage").Flattened["encodedInvokerIndices"].OffsetBytes == 0x50,
                "Runtime image declarations preserve the verified 0x58 allocation and encoded links"
            );
        }
    }
}
