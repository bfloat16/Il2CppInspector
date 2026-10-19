using System.Collections.Immutable;
using Il2CppInspector.Next.BinaryMetadata;

namespace Il2CppInspector
{
    internal static class ZzzBinaryInitializer
    {
        internal static void InitializeZzz(
            this Il2CppBinary binary,
            ZzzMorax adapter,
            ImmutableArray<Il2CppType> types,
            Dictionary<ulong, int> typeAddresses,
            ImmutableArray<Il2CppGenericInst> instances,
            ImmutableArray<Il2CppMethodSpec> specs,
            ImmutableArray<uint> offsets,
            ImmutableArray<Il2CppTypeDefinitionSizes> sizes
        )
        {
            binary.Metadata = adapter.Metadata;
            binary.Image.Version = binary.Metadata.Version;
            binary.CodeRegistrationPointer = adapter.CodeRegistrationAddress;
            binary.MetadataRegistrationPointer = adapter.MetadataRegistrationAddress;
            binary.RegistrationFunctionPointer = adapter.RegistrationAddress;
            binary.Modules = [];
            binary.TypeReferences = types;
            binary.TypeReferenceIndicesByAddress = typeAddresses;
            binary.GenericInstances = instances;
            binary.MethodSpecs = specs;
            binary.FieldOffsets = offsets;
            binary.TypeDefinitionSizes = sizes;
            var cr = binary.CodeRegistrationPointer;
            var mr = binary.MetadataRegistrationPointer;
            binary.CodeRegistration = new()
            {
                CodeGenModules = binary.Image.ReadMappedUInt64(cr + 0x20),
                CodeGenModulesCount = (uint)binary.Metadata.Images.Length,
                CustomAttributeCount = unchecked((int)(binary.Image.ReadMappedUInt32(cr + 0x18) ^ 0x42547054u)),
                CustomAttributeGenerators = binary.Image.ReadMappedUInt64(cr + 0x50),
                InvokerPointersCount = unchecked(binary.Image.ReadMappedUInt32(cr + 0x80) + 0x81CFFDF2u),
                InvokerPointers = binary.Image.ReadMappedUInt64(cr + 0x38),
                GenericMethodPointersCount = binary.Image.ReadMappedUInt32(cr + 0x5C) ^ 0x5E455406u,
                GenericMethodPointers = binary.Image.ReadMappedUInt64(cr + 0x70),
                GenericAdjustorThunks = binary.Image.ReadMappedUInt64(cr),
                UnresolvedVirtualCallCount = unchecked((int)(binary.Image.ReadMappedUInt32(cr + 0x2C) + 0xC9301B43u)),
                UnresolvedVirtualCallPointers = binary.Image.ReadMappedUInt64(cr + 0x68),
            };
            binary.MetadataRegistration = new()
            {
                TypesCount = types.Length,
                Types = binary.Image.ReadMappedUInt64(mr + 0x30),
                GenericInstsCount = instances.Length,
                GenericInsts = binary.Image.ReadMappedUInt64(mr + 0x48),
                GenericClassesCount = adapter.GenericClassCount,
                MethodSpecsCount = specs.Length,
                FieldOffsetsCount = binary.Metadata.Types.Length,
                TypeDefinitionsSizesCount = binary.Metadata.Types.Length,
            };
            binary.CustomAttributeGenerators = binary.Image.ReadMappedUWordArray(binary.CodeRegistration.CustomAttributeGenerators, binary.CodeRegistration.CustomAttributeCount);
            binary.MethodInvokePointers = binary.Image.ReadMappedUWordArray(binary.CodeRegistration.InvokerPointers, checked((int)binary.CodeRegistration.InvokerPointersCount));
            if (binary.CustomAttributeGenerators.Length != binary.Metadata.AttributeTypeRanges.Length)
            {
                throw new InvalidDataException("ZZZ attribute range/generator count mismatch.");
            }
        }
    }
}
