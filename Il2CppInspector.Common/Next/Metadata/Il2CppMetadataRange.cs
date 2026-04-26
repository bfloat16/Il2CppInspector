using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.Metadata;

[VersionedStruct]
public partial record struct Il2CppMetadataRange
{
    public int Start { get; private set; }
    public int Length { get; private set; }
}
