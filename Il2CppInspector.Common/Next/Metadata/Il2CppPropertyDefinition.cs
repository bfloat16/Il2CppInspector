using System.Reflection;
using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.Metadata;

using StringIndex = int;

[VersionedStruct]
public partial record struct Il2CppPropertyDefinition
{
    public StringIndex NameIndex { get; internal set; }
    public MethodIndex Get { get; internal set; }
    public MethodIndex Set { get; internal set; }
    public PropertyAttributes Attrs { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.0")]
    public int CustomAttributeIndex { get; internal set; }

    [VersionCondition(GreaterThanOrEqual = "19.0")]
    public uint Token { get; internal set; }

    public readonly bool IsValid => NameIndex != 0;
}
