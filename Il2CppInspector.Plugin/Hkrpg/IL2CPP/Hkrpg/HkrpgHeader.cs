namespace Il2CppInspector;

using static HkrpgMorax;

internal sealed class HkrpgHeader
{
    internal uint PayloadOffset { get; }
    internal uint ImageCount { get; }
    internal uint AssemblyCount { get; }
    internal uint MethodCount { get; }
    internal uint GenericClassCount { get; }
    internal uint GenericMethodTableCount { get; }
    internal uint FieldDefaultCount { get; }
    internal uint ParameterDefaultCount { get; }
    internal uint ImagesOffset { get; }
    internal uint TypesOffset { get; }
    internal uint FieldsOffset { get; }
    internal uint FieldMapOffset { get; }
    internal uint FieldGroupsOffset { get; }
    internal uint FieldOffsetsOffset { get; }
    internal uint MethodsOffset { get; }
    internal uint InvokersOffset { get; }
    internal uint ParametersOffset { get; }
    internal uint PropertiesOffset { get; }
    internal uint EventsOffset { get; }
    internal uint NestedTypesOffset { get; }
    internal uint InterfacesOffset { get; }
    internal uint GenericClassesOffset { get; }
    internal uint GenericContainersOffset { get; }
    internal uint GenericParametersOffset { get; }
    internal uint GenericMethodTableOffset { get; }
    internal uint MethodSpecsOffset { get; }
    internal uint StringOffset { get; }
    internal uint UsageListsOffset { get; }
    internal uint UsagePairsOffset { get; }
    internal uint LiteralOffsetsOffset { get; }
    internal uint LiteralDataOffset { get; }
    internal uint FieldRefsOffset { get; }
    internal uint FieldDefaultsOffset { get; }
    internal uint ParameterDefaultsOffset { get; }
    internal uint DefaultDataOffset { get; }

    internal HkrpgHeader(ReadOnlySpan<byte> data, uint payload)
    {
        unchecked
        {
            PayloadOffset = payload;
            ImageCount = (U32(data, 0x134) ^ 0x10210728u) / 40;
            AssemblyCount = (uint)(I32(data, 0x178) >> 4) ^ 0x05CC1AE1u;
            MethodCount = (U32(data, 0x1F8) ^ 0x1608C2C8u) / 26;
            GenericClassCount = (uint)(I32(data, 0xBC) >> 3) ^ 0x079FC2ECu;
            GenericMethodTableCount = (U32(data, 0x1E4) ^ 0x720FEF70u) / 12;
            FieldDefaultCount = (U32(data, 0x1DC) ^ 0x3E0C72F0u) / 12;
            ParameterDefaultCount = (U32(data, 0x60) ^ 0x2F5D2DD0u) / 12;
            ImagesOffset = U32(data, 0x150) - 0x277D9EA2u;
            TypesOffset = U32(data, 0x84) ^ 0x68531D3Fu;
            FieldsOffset = U32(data, 0x20) - 0x4D031E77u;
            FieldMapOffset = U32(data, 0x180) - 0x1B24189Du;
            FieldGroupsOffset = U32(data, 0x9C) - 0x43813629u;
            FieldOffsetsOffset = U32(data, 0x148) ^ 0x329E1172u;
            MethodsOffset = U32(data, 0x14C) - 0x0C5FBD6Cu;
            InvokersOffset = U32(data, 0x13C) ^ 0x46C8010Fu;
            ParametersOffset = U32(data, 0x30) - 0x230A0242u;
            PropertiesOffset = U32(data, 0x40) ^ 0x3C685CDBu;
            EventsOffset = U32(data, 0x1AC) ^ 0x37080D6Eu;
            NestedTypesOffset = U32(data, 0x12C) ^ 0x26F0FC20u;
            InterfacesOffset = U32(data, 0x1C8) ^ 0x042F9275u;
            GenericClassesOffset = U32(data, 0x164) ^ 0x7F5C5934u;
            GenericContainersOffset = U32(data, 0xF0) - 0x62B76DF1u;
            GenericParametersOffset = U32(data, 0x140) - 0x25538CF7u;
            GenericMethodTableOffset = U32(data, 0xEC) ^ 0x67F701BAu;
            MethodSpecsOffset = U32(data, 0x38) - 0x08870E55u;
            StringOffset = U32(data, 0x1B4) - 0x72BC5B12u;
            UsageListsOffset = U32(data, 0x1D0) - 0x36599B5Bu;
            UsagePairsOffset = U32(data, 0x190) - 0x1286EF8Du;
            LiteralOffsetsOffset = U32(data, 0x1F0) ^ 0x56C7D20Du;
            LiteralDataOffset = U32(data, 0x08);
            FieldRefsOffset = U32(data, 0xA0) - 2048087822u;
            FieldDefaultsOffset = U32(data, 0x1FC) ^ 0x6238CDB0u;
            ParameterDefaultsOffset = U32(data, 0x4C) + 0xE82C6008u;
            DefaultDataOffset = U32(data, 0x3C) - 0x6874185Bu;
        }
    }
}
