namespace Il2CppInspector;

internal sealed partial class Bh3Morax
{
    public override (ulong Address, object Value) DecodeDefault(int typeIndex, int dataIndex, Il2CppBinary binary)
    {
        if (dataIndex < 0)
        {
            return (0, null);
        }

        var offset = checked(Base(0x070) + dataIndex);
        return ((ulong)offset, MoraxReader.DecodeConstant(Metadata, offset, binary.TypeReferences[typeIndex].Type));
    }
}
