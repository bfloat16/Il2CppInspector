using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.Metadata;

[VersionedStruct]
public partial record struct Il2CppCustomAttributeDataRange
{
    public uint Token { get; private set; }
    public uint StartOffset { get; private set; }
}
