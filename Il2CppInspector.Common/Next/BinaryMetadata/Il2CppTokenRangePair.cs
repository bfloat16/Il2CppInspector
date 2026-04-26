using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppTokenRangePair
{
    public uint Token;
    public Il2CppRange Range;
}
