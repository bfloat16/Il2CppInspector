using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppTypeDefinitionSizes
{
    public uint InstanceSize;
    public int NativeSize;
    public uint StaticFieldsSize;
    public uint ThreadStaticFieldsSize;
}
