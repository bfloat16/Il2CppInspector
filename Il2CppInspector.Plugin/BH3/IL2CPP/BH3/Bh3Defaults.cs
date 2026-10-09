using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Utils;

namespace Il2CppInspector
{
    internal sealed partial class Bh3Morax
    {
        public override (ulong Address, object Value) DecodeDefault(int typeIndex, int dataIndex, Il2CppBinary binary)
        {
            if (dataIndex < 0)
            {
                return (0, null);
            }

            var offset = checked(Base(0x070) + dataIndex);
            var kind = binary.TypeReferences[typeIndex].Type;
            // Scalar constants and length-prefixed UTF-8 strings have the ordinary IL2CPP blob
            // format. Opaque defaults keep their address for the standard FieldRVA exporters.
            object value = null;
            if ((uint)kind >= (uint)Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN && (uint)kind <= (uint)Il2CppTypeEnum.IL2CPP_TYPE_STRING)
            {
                Metadata.Position = offset;
                // These scalar kinds do not need the inspector context used by custom attribute arrays.
                value = BlobReader.GetConstantValueFromBlob(null, kind, Metadata);
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
                Il2CppTypeEnum.IL2CPP_TYPE_I or Il2CppTypeEnum.IL2CPP_TYPE_U => Image.Bits / 8,
                // RVA data is managed storage, even when its marshal representation has a different size.
                Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE => checked((int)binary.TypeDefinitionSizes[type.Data.KlassIndex].InstanceSize - 16),
                _ => 0,
            };
    }
}
