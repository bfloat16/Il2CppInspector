using System.Collections.Immutable;
using Il2CppInspector.Next.BinaryMetadata;

namespace Il2CppInspector
{
    internal static class Bh3BinaryInitializer
    {
        // Mirrors ZzzBinaryInitializer.InitializeZzz. The BH3 registration structures have different
        // field offsets (D §1.1 / §1.2), and two of their tables live in global-metadata.dat rather
        // than the DLL.
        internal static void InitializeBh3(
            this Il2CppBinary binary,
            Bh3Morax adapter,
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
                // D §1.1: codeGenModules at +0x28/+0x30, invokerPointers at +0x50 with the count at
                // +0x18. The +0x1C count and its +0x38 pointer are both empty in this build.
                CodeGenModules = binary.Image.ReadMappedUInt64(cr + 0x28),
                CodeGenModulesCount = binary.Image.ReadMappedUInt32(cr + 0x30) ^ 0x4ED23D5Eu,
                InvokerPointersCount = binary.Image.ReadMappedUInt32(cr + 0x18) ^ 0x0F26DB88u,
                InvokerPointers = binary.Image.ReadMappedUInt64(cr + 0x50),
                GenericMethodPointers = binary.Image.ReadMappedUInt64(cr + 0x08),
                GenericMethodPointersCount = binary.Image.ReadMappedUInt32(cr + 0x48) ^ 0x6B05DA47u,
                GenericAdjustorThunks = binary.Image.ReadMappedUInt64(cr + 0x80),
                CustomAttributeCount = unchecked((int)(binary.Image.ReadMappedUInt32(cr + 0x58) - 0x06D60DEAu)),
                CustomAttributeGenerators = binary.Image.ReadMappedUInt64(cr + 0x90),
                UnresolvedVirtualCallCount = 0,
                UnresolvedVirtualCallPointers = binary.Image.ReadMappedUInt64(cr + 0x38),
            };
            binary.MetadataRegistration = new()
            {
                // D §1.2: the Il2CppType and Il2CppGenericInst arrays live in the DLL at MR+0x70 and
                // MR+0x78.
                TypesCount = types.Length,
                Types = binary.Image.ReadMappedUInt64(mr + 0x70),
                GenericInstsCount = instances.Length,
                GenericInsts = binary.Image.ReadMappedUInt64(mr + 0x78),
                // Generic classes live in startup metadata and method specs in global metadata,
                // so these normalized registrations have counts without DLL table pointers.
                GenericClassesCount = adapter.GenericClassCount,
                MethodSpecsCount = specs.Length,
                FieldOffsetsCount = binary.Metadata.Types.Length,
                TypeDefinitionsSizesCount = binary.Metadata.Types.Length,
            };
            if (binary.CodeRegistration.CustomAttributeCount != binary.Metadata.AttributeTypeRanges.Length)
                throw new InvalidDataException("BH3 attribute range/generator count mismatch.");
            binary.CustomAttributeGenerators = binary.Image.ReadMappedUWordArray(binary.CodeRegistration.CustomAttributeGenerators, binary.CodeRegistration.CustomAttributeCount);
            binary.MethodInvokePointers = binary.Image.ReadMappedUWordArray(binary.CodeRegistration.InvokerPointers, checked((int)binary.CodeRegistration.InvokerPointersCount));
        }
    }
}
