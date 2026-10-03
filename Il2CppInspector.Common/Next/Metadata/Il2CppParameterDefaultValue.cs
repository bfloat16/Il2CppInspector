namespace Il2CppInspector.Next.Metadata;

using VersionedSerialization.Attributes;

[VersionedStruct]
public partial record struct Il2CppParameterDefaultValue
{
    public ParameterIndex ParameterIndex { get; internal set; }
    public TypeIndex TypeIndex { get; internal set; }
    public DefaultValueDataIndex DataIndex { get; internal set; }
}
