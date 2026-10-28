using System.Runtime.InteropServices;
using Il2CppInspector.Next;
using Il2CppInspector.Next.BinaryMetadata;

namespace Il2CppInspector.Plugin.LAD
{
    /// <summary>
    /// Decodes the build's fixed ARM64 registrations with stock v29 binary records.
    /// </summary>
    internal static class LadBinary
    {
        internal static Il2CppInspector Load(IFileFormatStream image, LadMetadata adapter, EventHandler<string> status)
        {
            if (image.Arch != "ARM64" || image.Bits != 64)
                throw new NotSupportedException("LAD_CN_Android_6.0.0 currently supports the ARM64 build only.");

            var binary = new Il2CppBinaryARM64(image, status);
            Initialize(binary, adapter, image);
            adapter.ReadBinary(binary);
            return new Il2CppInspector(binary, adapter.Metadata);
        }

        private static void Initialize(Il2CppBinary binary, LadMetadata adapter, IFileFormatStream image)
        {
            binary.Metadata = adapter.Metadata;
            image.Version = MetadataVersions.V290;

            var cr = image.ImageBase + LadBuild.CodeRegistrationRva;
            var mr = image.ImageBase + LadBuild.MetadataRegistrationRva;
            binary.CodeRegistrationPointer = cr;
            binary.MetadataRegistrationPointer = mr;
            binary.RegistrationFunctionPointer = image.ImageBase + LadBuild.RegistrationRva;

            // MetadataRegistration at 0x9C080D8 is stock v24.5 with zeroed metadataUsages.
            // Generic classes require plugin type resolution.
            binary.MetadataRegistration = image.ReadMappedVersionedObject<Il2CppMetadataRegistration>(mr);

            // CR is the modern registration layout without custom-attribute generators:
            // +0x00 reverse wrappers, +0x10 generic methods, +0x28 invokers, +0x38 unresolved calls,
            // +0x48 interop, +0x58 Windows factories, +0x68 modules (0x24F244C / 0x24F13A4).
            // Module records also retain the four v29 tail pointers (+0x68..+0x80).
            binary.CodeRegistration = image.ReadMappedVersionedObject<Il2CppCodeRegistration>(cr);
            if (
                binary.MetadataRegistration.FieldOffsetsCount != adapter.Metadata.Types.Length
                || binary.MetadataRegistration.TypeDefinitionsSizesCount != adapter.Metadata.Types.Length
                || binary.CodeRegistration.CodeGenModulesCount != adapter.Metadata.Images.Length
            )
                throw new InvalidDataException("LAD binary registrations do not match the metadata tables.");

            // MR.types: 125923 stock Il2CppType pointers at 0x9B00868.
            var typeRefPointers = image.ReadMappedUWordArray(binary.MetadataRegistration.Types, checked((int)binary.MetadataRegistration.TypesCount));
            binary.TypeReferenceIndicesByAddress = typeRefPointers
                .Zip(Enumerable.Range(0, typeRefPointers.Length), (a, i) => new { a, i })
                .ToDictionary(x => x.a, x => x.i);
            binary.TypeReferences = [.. typeRefPointers.Select(image.ReadMappedVersionedObject<Il2CppType>)];
            binary.GenericInstances = image.ReadMappedVersionedObjectPointerArray<Il2CppGenericInst>(
                binary.MetadataRegistration.GenericInsts,
                checked((int)binary.MetadataRegistration.GenericInstsCount)
            );
            // 0x2472B40 confirms the stock {methodDefinitionIndex, classIndexIndex, methodIndexIndex} order.
            binary.MethodSpecs = image.ReadMappedVersionedObjectArray<Il2CppMethodSpec>(
                binary.MetadataRegistration.MethodSpecs,
                binary.MetadataRegistration.MethodSpecsCount
            );

            // MR.fieldOffsets: stock per-type pointer array at 0xA065D50.
            binary.FieldOffsetPointers = image.ReadMappedWordArray(
                binary.MetadataRegistration.FieldOffsets,
                checked((int)binary.MetadataRegistration.FieldOffsetsCount)
            );

            binary.TypeDefinitionSizes = image.ReadMappedVersionedObjectPointerArray<Il2CppTypeDefinitionSizes>(
                binary.MetadataRegistration.TypeDefinitionsSizes,
                checked((int)binary.MetadataRegistration.TypeDefinitionsSizesCount)
            );
            ReadModules(binary);

            binary.CustomAttributeGenerators = [];
            binary.MethodInvokePointers = image.ReadMappedUWordArray(
                binary.CodeRegistration.InvokerPointers,
                checked((int)binary.CodeRegistration.InvokerPointersCount)
            );
            ReadGenericMethodPointers(binary);
            // The runtime headers derive from the v24.5 headers.
            image.Version = MetadataVersions.V245;
        }

        private static void ReadModules(Il2CppBinary binary)
        {
            var image = binary.Image;
            binary.Modules = [];
            var moduleCount = checked((int)binary.CodeRegistration.CodeGenModulesCount);
            var modulePtrs = image.ReadMappedUWordArray(binary.CodeRegistration.CodeGenModules, moduleCount);
            for (var i = 0; i < moduleCount; i++)
            {
                var moduleVa = modulePtrs[i];
                // 0x88-byte v29 Il2CppCodeGenModule at 0x9C0DA70; +0x68..+0x80 are zero in this build.
                var module = image.ReadMappedVersionedObject<Il2CppCodeGenModule>(moduleVa);
                var name = image.ReadMappedNullTerminatedString(module.ModuleName);
                binary.Modules.Add(name, module);
                binary.CodeGenModulePointers.Add(name, moduleVa);

                var pointers =
                    module.MethodPointers != 0 && image.TryMapVATR(module.MethodPointers, out _)
                        ? image.ReadMappedUWordArray(module.MethodPointers, checked((int)module.MethodPointerCount))
                        : new ulong[module.MethodPointerCount];
                binary.ModuleMethodPointers.Add(module, pointers);

                var invokerIndices =
                    module.InvokerIndices != 0 && image.TryMapVATR(module.InvokerIndices, out _)
                        ? [.. image.ReadMappedPrimitiveArray<int>(module.InvokerIndices, checked((int)module.MethodPointerCount))]
                        : new int[module.MethodPointerCount];
                binary.MethodInvokerIndices.Add(module, ImmutableCollectionsMarshal.AsImmutableArray(invokerIndices));
            }
        }

        private static void ReadGenericMethodPointers(Il2CppBinary binary)
        {
            var image = binary.Image;
            var count = checked((int)binary.CodeRegistration.GenericMethodPointersCount);
            var pointers = image.ReadMappedUWordArray(binary.CodeRegistration.GenericMethodPointers, count);

            // 0x2474390 walks 16-byte {spec, method, invoker, adjustor} records; 0x24F244C
            // resolves method/adjustor/invoker through CR+0x18 / +0x20 / +0x30 respectively.
            var table = image.ReadMappedVersionedObjectArray<Il2CppGenericMethodFunctionsDefinitions>(
                binary.MetadataRegistration.GenericMethodTable,
                binary.MetadataRegistration.GenericMethodTableCount
            );
            foreach (var entry in table)
            {
                if (
                    (uint)entry.GenericMethodIndex >= binary.MethodSpecs.Length
                    || (uint)entry.Indices.MethodIndex >= pointers.Length
                    || entry.Indices.InvokerIndex < -1
                    || entry.Indices.InvokerIndex >= binary.MethodInvokePointers.Length
                )
                    throw new InvalidDataException("LAD generic method table contains an invalid index.");
                var spec = binary.MethodSpecs[entry.GenericMethodIndex];
                binary.GenericMethodPointers.Add(spec, pointers[entry.Indices.MethodIndex]);
                binary.GenericMethodInvokerIndices.Add(spec, entry.Indices.InvokerIndex);
            }
        }
    }
}
