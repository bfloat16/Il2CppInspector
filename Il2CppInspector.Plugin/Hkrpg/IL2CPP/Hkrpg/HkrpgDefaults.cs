using System.Text;
using Il2CppInspector.Next.BinaryMetadata;

namespace Il2CppInspector;

internal sealed partial class HkrpgMorax
{
    public override (ulong Address, object Value) DecodeDefault(int typeIndex, int dataIndex, Il2CppBinary binary)
    {
        if (dataIndex < 0)
            return (0, null);
        var o = checked(Base(header.DefaultDataOffset) + dataIndex);
        var kind = binary.TypeReferences[typeIndex].Type;
        if (kind == Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
        {
            var definition = definitions[binary.TypeReferences[typeIndex].Data.KlassIndex];
            if (definition.Bitfield.EnumType)
                kind = binary.TypeReferences[definition.ElementTypeIndex].Type;
        }
        object value = kind switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN => global[o] != 0,
            Il2CppTypeEnum.IL2CPP_TYPE_I1 => unchecked((sbyte)global[o]),
            Il2CppTypeEnum.IL2CPP_TYPE_U1 => global[o],
            Il2CppTypeEnum.IL2CPP_TYPE_CHAR => (char)U16(global, o),
            Il2CppTypeEnum.IL2CPP_TYPE_I2 => unchecked((short)U16(global, o)),
            Il2CppTypeEnum.IL2CPP_TYPE_U2 => U16(global, o),
            Il2CppTypeEnum.IL2CPP_TYPE_I4 => I32(global, o),
            Il2CppTypeEnum.IL2CPP_TYPE_U4 => U32(global, o),
            Il2CppTypeEnum.IL2CPP_TYPE_I8 => unchecked((long)U64(global, o)),
            Il2CppTypeEnum.IL2CPP_TYPE_U8 => U64(global, o),
            Il2CppTypeEnum.IL2CPP_TYPE_R4 => BitConverter.Int32BitsToSingle(I32(global, o)),
            Il2CppTypeEnum.IL2CPP_TYPE_R8 => BitConverter.Int64BitsToDouble(unchecked((long)U64(global, o))),
            Il2CppTypeEnum.IL2CPP_TYPE_STRING => DecodeDefaultString(o),
            _ => null,
        };
        return ((ulong)o, value);
    }

    public override int FieldRvaSize(Il2CppType type, Il2CppBinary binary) => type.Type switch
    {
        Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN or Il2CppTypeEnum.IL2CPP_TYPE_I1 or Il2CppTypeEnum.IL2CPP_TYPE_U1 => 1,
        Il2CppTypeEnum.IL2CPP_TYPE_CHAR or Il2CppTypeEnum.IL2CPP_TYPE_I2 or Il2CppTypeEnum.IL2CPP_TYPE_U2 => 2,
        Il2CppTypeEnum.IL2CPP_TYPE_I4 or Il2CppTypeEnum.IL2CPP_TYPE_U4 or Il2CppTypeEnum.IL2CPP_TYPE_R4 => 4,
        Il2CppTypeEnum.IL2CPP_TYPE_I8 or Il2CppTypeEnum.IL2CPP_TYPE_U8 or Il2CppTypeEnum.IL2CPP_TYPE_R8 => 8,
        Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE => Math.Max(0, checked((int)binary.TypeDefinitionSizes[type.Data.KlassIndex].InstanceSize - 16)),
        _ => 0,
    };

    private string DecodeDefaultString(int offset)
    {
        var length = I32(global, offset);
        if (length == -1)
            return null;
        CheckRange(global, checked(offset + 4), length);
        return Encoding.UTF8.GetString(global, offset + 4, length);
    }
}
