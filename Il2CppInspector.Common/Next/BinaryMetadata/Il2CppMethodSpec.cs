using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.BinaryMetadata;

using GenericInstIndex = int;
using MethodIndex = int;

[VersionedStruct]
public partial record struct Il2CppMethodSpec
{
    public MethodIndex MethodDefinitionIndex;
    public GenericInstIndex ClassIndexIndex;
    public GenericInstIndex MethodIndexIndex;
}
