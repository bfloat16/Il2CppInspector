using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppArrayType
{
    public Pointer<Il2CppType> ElementType;
    public byte Rank;
    public byte NumSizes;
    public byte NumLowerBound;

    public PrimitivePointer<int> Sizes;
    public PrimitivePointer<int> LoBounds;
}
