using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.Metadata;

[VersionedStruct]
public partial record struct Il2CppCustomAttributeTypeRange
{
    [VersionCondition(GreaterThanOrEqual = "24.1")]
    public uint Token { get; internal set; }

    public int Start { get; internal set; }
    public int Count { get; internal set; }
}
