namespace Il2CppInspector.Next.Metadata;

using VersionedSerialization.Attributes;
using StringIndex = int;

[VersionedStruct]
public partial record struct Il2CppParameterDefinition
{
    public StringIndex NameIndex { get; internal set; }
    public uint Token { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.0")]
    public int CustomAttributeIndex { get; internal set; }

    public TypeIndex TypeIndex { get; internal set; }

    public readonly bool IsValid => NameIndex != 0;
}
