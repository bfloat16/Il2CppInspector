namespace Il2CppInspector.Next.Metadata;

using VersionedSerialization.Attributes;
using AssemblyIndex = int;
using CustomAttributeIndex = int;
using StringIndex = int;

[VersionedStruct]
public partial record struct Il2CppImageDefinition
{
    public StringIndex NameIndex { get; internal set; }
    public AssemblyIndex AssemblyIndex { get; internal set; }

    public TypeDefinitionIndex TypeStart { get; internal set; }
    public uint TypeCount { get; internal set; }

    [VersionCondition(GreaterThanOrEqual = "24.0")]
    public TypeDefinitionIndex ExportedTypeStart { get; internal set; }

    [VersionCondition(GreaterThanOrEqual = "24.0")]
    public uint ExportedTypeCount { get; internal set; }

    public MethodIndex EntryPointIndex { get; internal set; }

    [VersionCondition(GreaterThanOrEqual = "19.0")]
    public uint Token { get; internal set; }

    [VersionCondition(GreaterThanOrEqual = "24.1")]
    public CustomAttributeIndex CustomAttributeStart { get; internal set; }

    [VersionCondition(GreaterThanOrEqual = "24.1")]
    public uint CustomAttributeCount { get; internal set; }

    public readonly bool IsValid => NameIndex != 0;
}
