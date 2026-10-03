namespace Il2CppInspector.Tests.Plugin.ZZZ.Reflection
{
    internal static class ZzzReflectionTests
    {
        internal static void Run(ZzzTestContext context)
        {
            var input = context.Input;
            var model = context.Model;
            var arrays = input.TypeReferences.Select((type, index) => (type, index)).Where(pair => pair.type.Type == Il2CppTypeEnum.IL2CPP_TYPE_ARRAY).ToArray();
            Check(arrays.Length == 82 && arrays.Select(pair => pair.type.Data.Value).Distinct().Count() == 46, "All 82 multidimensional-array references use the 46 registered descriptors");
            foreach (var (rawArray, referenceIndex) in arrays)
            {
                var descriptor = input.BinaryImage.ReadMappedVersionedObject<Il2CppArrayType>(rawArray.Data.ArrayType);
                var array = model.TypesByReferenceIndex[referenceIndex];
                if (rawArray.ByRef)
                    array = array.ElementType;
                var elementIndex = input.Binary.TypeReferenceIndicesByAddress[descriptor.ElementType.PointerValue];
                if (!array.IsArray || array.GetArrayRank() != descriptor.Rank || array.ElementType != model.TypesByReferenceIndex[elementIndex])
                    throw new InvalidOperationException("Multidimensional-array rank or element differs from its descriptor");
            }
            Pass("The stock array reader preserves every ZZZ descriptor's rank and element type");
            Check(
                input.InterfaceOffsets.Length == 42192
                    && input.TypeDefinitions.Sum(t => (int)t.InterfaceOffsetsCount) == 102219
                    && input.InterfaceOffsets.All(p => p.InterfaceTypeIndex >= 0 && p.InterfaceTypeIndex < input.TypeReferences.Length && p.Offset >= 0),
                "All 42,192 six-byte interface-offset pairs are restored with valid type references"
            );
            Check(
                model.TypesByFullName["System.Boolean"].Sizes.NativeSize == 4
                    && model.TypesByFullName["System.Boolean"].Sizes.InstanceSize == 17
                    && model.TypesByFullName["System.Object"].Sizes.NativeSize == -1,
                "Marshal sizes come from the native-size table and retain the unavailable sentinel"
            );
            Check(
                model.TypesByFullName["System.AppDomain"].Sizes.ThreadStaticFieldsSize == 32 && model.TypesByFullName["System.NumberFormatter"].Sizes.ThreadStaticFieldsSize == 16,
                "Thread-static sizes are decoded from descriptor +2 rather than hardcoded to zero"
            );
            Check(
                input.VTableMethodIndices.Count(v => v >> 29 == 3) == 1543683 && input.VTableMethodIndices.Count(v => v >> 29 == 6) == 19957 && input.VTableMethodIndices.Count(v => v == 0) == 4126,
                "Vtable values use ordinary MethodDef/MethodRef encoding with explicit empty slots"
            );
            Check(
                input.TypeReferences.All(t => t.NumModifiers == 0 && !t.Pinned && !t.ValueType) && input.TypeReferences.Count(t => t.ByRef) == 101995,
                "Only the 101,995 observed byref flags are mapped; unobserved modifier/pinned bits are not guessed"
            );
            var versions = input.Assemblies.ToDictionary(a => input.Strings[a.Aname.NameIndex], a => new Version(a.Aname.Major, a.Aname.Minor, a.Aname.Build, a.Aname.Revision));
            Check(
                versions["ICSharpCode.SharpZipLib"] == new Version(0, 86, 0, 518) && versions["System.Memory"] == new Version(4, 0, 1, 1) && versions["Newtonsoft.Json"] == new Version(9, 0, 0, 0),
                "Assembly versions match the initializer and assembly-name formatter instead of placeholders"
            );
            Check(
                input.Assemblies.All(a => a.Aname.HashLen == 0 && a.Aname.PublicKeyIndex == -1) && input.AssemblyPublicKeys.Count == 0,
                "All 170 runtime hash lengths decode to zero; the runtime adapter supplies no full public key"
            );
            Check(
                input.Binary.MethodInvokerIndices.Values.SelectMany(indices => indices).All(index => index >= -1 && index < input.MethodInvokePointers.Length),
                "Module-index-selected method invokers stay inside the registered table or use the -1 sentinel"
            );
            var resolvedGenericMethods = (System.Collections.IDictionary)
                typeof(TypeModel)
                    .GetProperty("ResolvedGenericMethods", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    .GetValue(model);
            Check(resolvedGenericMethods.Count == 0, "Generic methods stay lazy during DLL and C# export");
            var vector = model.TypesByFullName["UnityEngine.Vector3"];
            var converter = model.TypesByDefinitionIndex[3];
            Check(
                converter.DeclaredFields.Where(f => f.IsStatic && !f.IsLiteral).Select(f => f.Offset).Order().SequenceEqual(new long[] { 0x210, 0x990, 0x998 })
                    && converter.DeclaredFields.Where(f => f.IsStatic && !f.IsLiteral).Select(f => f.ZzzStorageTag).Order().SequenceEqual(new[] { 2, 4, 4 }),
                "Mono.DataConverter retains global buffer offsets and their tag-2/tag-4 identities"
            );
            var adapter = input.Metadata.GameAdapter;
            var rawOffsets = (uint[])
                adapter
                    .GetType()
                    .GetProperty("RawFieldOffsets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    .GetValue(adapter);
            var staticTypes = 0;
            for (var ti = 0; ti < input.TypeDefinitions.Length; ti++)
            {
                var td = input.TypeDefinitions[ti];
                var tagged = Enumerable.Range(td.FieldIndex, td.FieldCount).Where(f => rawOffsets[f] >> 24 is 2 or 4).ToArray();
                if (tagged.Length == 0)
                    continue;
                staticTypes++;
                if (tagged.Any(f => input.FieldOffsets[f] != (rawOffsets[f] & 0xFFFFFF)))
                    throw new InvalidOperationException("Global static offset was rebased away from its runtime buffer");
            }
            Check(staticTypes == 13509, "All 13,509 types with tagged statics retain offsets relative to their selected global buffers");
            Check(
                vector.DeclaredFields.Where(f => f.IsStatic && !f.IsLiteral).Min(f => f.Offset) == 0x2910 && vector.DeclaredFields.Where(f => f.IsStatic && !f.IsLiteral).Max(f => f.Offset) == 0x297C,
                "Vector3 global static offsets retain their runtime values"
            );
            Check(
                vector.Sizes.InstanceSize == 28 && vector.GetField("x").Offset == 0 && vector.GetField("y").Offset == 4 && vector.GetField("z").Offset == 8,
                "Vector3 size and instance offsets match the binary"
            );
            var list = model.TypesByFullName["System.Collections.Generic.List`1"];
            Check(list.ImplementedInterfaces.Count() == 8 && list.GenericTypeParameters.Single().Name == "T", "List<T> has eight interfaces and named generic argument");
            var objectType = model.TypesByFullName["System.Object"];
            var reflectionList = model.TypesByDefinitionIndex[311].MakeGenericType(model.TypesByFullName["System.Reflection.MethodInfo"]);
            Check(
                reflectionList.Sizes.InstanceSize - 16 == 24
                    && reflectionList.GetField("_items").Offset == 0
                    && reflectionList.GetField("_item").Offset == 8
                    && reflectionList.GetField("_count").Offset == 16,
                "ListBuilder<MethodInfo> shares its 24-byte layout and keeps typed fields"
            );
            var pooled = model.TypesByDefinitionIndex[5975].MakeGenericType(list.MakeGenericType(objectType));
            Check(
                pooled.Sizes.InstanceSize - 16 == 16 && pooled.GetField("m_ToReturn").Offset == 0 && pooled.GetField("m_Pool").Offset == 8,
                "PooledObject<List<object>> has the IDA-confirmed 16-byte return layout"
            );
            var iterator = model.TypesByDefinitionIndex[1556].MakeGenericType(model.TypesByFullName["System.TypeIdentifier"]);
            Check(
                iterator.Sizes.InstanceSize - 16 == 24 && iterator.GetField("current").Offset == 16 && iterator.GetField("current").FieldType == model.TypesByFullName["System.TypeIdentifier"],
                "Shared enumerator offsets preserve the actual TypeIdentifier field type"
            );
            var policy = model.TypesByFullName["Mono.Security.Interface.MonoSslPolicyErrors"];
            var nullablePolicy = model.TypesByFullName["System.Nullable`1"].MakeGenericType(policy);
            Check(
                nullablePolicy.Sizes.InstanceSize - 16 == 8 && nullablePolicy.GetField("value").Offset == 0 && nullablePolicy.GetField("has_value").Offset == 4,
                "Nullable<enum> uses the runtime's shared enum layout"
            );
            var cameraData = model.TypesByFullName["PipelineCamera.WorldBasicCameraData"];
            var blend = model.TypesByDefinitionIndex[37196].MakeGenericType(cameraData);
            Check(blend.IsEnum && blend.GetEnumUnderlyingType().FullName == "System.Byte", "A constructed camera nested enum remains a byte enum");
            Check(
                blend.Sizes.InstanceSize == 17 && blend.Sizes.NativeSize == 1 && model.TypesByDefinitionIndex[37196].MakeGenericType(vector).Sizes.NativeSize == 1,
                "Nested generic enums have byte storage through both recorded and fresh MakeGenericType paths"
            );
            Check(
                model
                    .TypesByReferenceIndex.Where(t => t is { IsEnum: true, IsGenericType: true, IsGenericTypeDefinition: false })
                    .Distinct()
                    .All(t => t.Sizes.InstanceSize == t.GetEnumUnderlyingType().Sizes.InstanceSize && t.Sizes.NativeSize == t.GetEnumUnderlyingType().Sizes.NativeSize),
                "Every recorded generic enum uses its underlying scalar size consistently"
            );
            var cameraItem = model.TypesByDefinitionIndex[37197].MakeGenericType(cameraData);
            var cameraTuple = model.TypesByDefinitionIndex[119].MakeGenericType(cameraItem, blend);
            Check(cameraTuple.Sizes.InstanceSize - 16 == 48 && cameraTuple.GetField("Item2").Offset == 40, "Camera tuple return storage is 48 bytes with its enum at offset 40");
            var spec = input.GenericMethodPointers.Keys.First();
            var usage = new MetadataUsage(MetadataUsageType.MethodRef, input.MethodSpecs.IndexOf(spec));
            var genericMethod = model.GetMetadataUsageMethod(usage);
            Check(model.GetMetadataUsageMethod(usage) == genericMethod && genericMethod.VirtualAddress.HasValue, "Generic use resolves and reuses one constructed member");
            var freshSpec = input.MethodSpecs.First(s => s.MethodIndexIndex >= 0 && !resolvedGenericMethods.Contains(s));
            var freshDefinition = model.MethodsByDefinitionIndex[freshSpec.MethodDefinitionIndex];
            var freshName =
                freshDefinition.DeclaringType.Namespace + (string.IsNullOrEmpty(freshDefinition.DeclaringType.Namespace) ? "" : ".") + freshDefinition.DeclaringType.Name + "." + freshDefinition.Name;
            var freshArguments = model.ResolveGenericArguments(input.GenericInstances[freshSpec.MethodIndexIndex]);
            Check(model.GetGenericMethod(freshName, freshArguments).GetGenericArguments().SequenceEqual(freshArguments), "An uncached named generic query resolves the supplied method arguments");
            var genericName =
                genericMethod.DeclaringType.Namespace + (string.IsNullOrEmpty(genericMethod.DeclaringType.Namespace) ? "" : ".") + genericMethod.DeclaringType.Name + "." + genericMethod.Name;
            Check(
                model.GetGenericMethod(genericName, genericMethod.GetGenericArguments()) == genericMethod,
                "Public generic lookup resolves method specs without requiring a previous metadata-usage query"
            );
        }
    }
}
