using Il2CppInspector.Next.BinaryMetadata;

namespace Il2CppInspector;

internal static class HsrDefaults
{
    internal static (ulong Address, object Value) Decode(HsrMetadata metadata, int typeIndex, int dataIndex, Il2CppBinary binary)
    {
        if (dataIndex < 0)
            return (0, null);
        var o = checked(metadata.Base(metadata.Header.DefaultDataOffset) + dataIndex);
        var kind = binary.TypeReferences[typeIndex].Type;
        if (kind == Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
        {
            var definition = metadata.Definitions[binary.TypeReferences[typeIndex].Data.KlassIndex];
            if (definition.Bitfield.EnumType)
                kind = binary.TypeReferences[definition.ElementTypeIndex].Type;
        }
        return ((ulong)o, MoraxReader.DecodeConstant(metadata.Model, o, kind));
    }
}
