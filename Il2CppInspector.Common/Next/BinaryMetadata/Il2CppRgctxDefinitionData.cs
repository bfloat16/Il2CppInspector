using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.BinaryMetadata;

using MethodIndex = int;
using TypeIndex = int;

[VersionedStruct]
public partial record struct Il2CppRgctxDefinitionData
{
    public int Value;

    public readonly MethodIndex MethodIndex => Value;
    public readonly TypeIndex TypeIndex => Value;
}
