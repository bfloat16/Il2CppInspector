namespace Il2CppInspector;

internal sealed partial class ZzzMorax
{
    public override (ulong Address, object Value) DecodeDefault(int typeIndex, int dataIndex, Il2CppBinary binary)
    {
        if (dataIndex < 0)
        {
            return (0, null);
        }

        var offset = checked(DefaultBlobOffset + dataIndex);
        return ((ulong)offset, MoraxReader.DecodeConstant(Metadata, offset, binary.TypeReferences[typeIndex].Type));
    }
}
