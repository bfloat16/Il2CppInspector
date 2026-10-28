using Il2CppInspector.Next.BinaryMetadata;

namespace Il2CppInspector;

internal sealed partial class GenshinMorax
{
    public override (ulong Address, object Value) DecodeDefault(int typeIndex, int dataIndex, Il2CppBinary binary)
    {
        if (dataIndex < 0)
            return (0, null);
        var offset = checked(Base(0x128) + dataIndex);
        var type = binary.TypeReferences[typeIndex];
        if (type.Type == Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE && Metadata.Types[type.Data.KlassIndex].Bitfield.EnumType)
            type = binary.TypeReferences[Metadata.Types[type.Data.KlassIndex].ElementTypeIndex];
        if (type.Type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
        {
            var pair = GenericClass(type.Data.Value);
            if (DecodeString((uint)Metadata.Types[pair.Definition].NameIndex) == "Nullable`1")
            {
                var inst = binary.GenericInstances[pair.Instance];
                var argument = Image.ReadMappedUInt64(inst.TypeArgv);
                type = binary.TypeReferences[binary.TypeReferenceIndicesByAddress[argument]];
            }
        }
        return ((ulong)offset, MoraxReader.DecodeConstant(Metadata, offset, type.Type));
    }
}
