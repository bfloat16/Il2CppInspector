using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppGenericContext
{
    public Pointer<Il2CppGenericInst> ClassInst;
    public Pointer<Il2CppGenericInst> MethodInst;
}
