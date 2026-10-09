using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Utils;

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
        object value = null;
        if ((uint)type.Type >= 2 && (uint)type.Type <= 14)
        {
            Metadata.Position = offset;
            value = BlobReader.GetConstantValueFromBlob(null, type.Type, Metadata);
        }
        return ((ulong)offset, value);
    }

    public override int FieldRvaSize(Il2CppType type, Il2CppBinary binary) =>
        type.Type switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN or Il2CppTypeEnum.IL2CPP_TYPE_I1 or Il2CppTypeEnum.IL2CPP_TYPE_U1 => 1,
            Il2CppTypeEnum.IL2CPP_TYPE_CHAR or Il2CppTypeEnum.IL2CPP_TYPE_I2 or Il2CppTypeEnum.IL2CPP_TYPE_U2 => 2,
            Il2CppTypeEnum.IL2CPP_TYPE_I4 or Il2CppTypeEnum.IL2CPP_TYPE_U4 or Il2CppTypeEnum.IL2CPP_TYPE_R4 => 4,
            Il2CppTypeEnum.IL2CPP_TYPE_I8 or Il2CppTypeEnum.IL2CPP_TYPE_U8 or Il2CppTypeEnum.IL2CPP_TYPE_R8 => 8,
            Il2CppTypeEnum.IL2CPP_TYPE_I or Il2CppTypeEnum.IL2CPP_TYPE_U => 8,
            Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE => checked((int)binary.TypeDefinitionSizes[type.Data.KlassIndex].InstanceSize - 16),
            _ => 0,
        };
}
