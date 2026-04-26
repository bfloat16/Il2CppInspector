using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppRange
{
    public int Start;
    public int Length;
}
