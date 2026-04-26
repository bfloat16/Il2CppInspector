using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppWindowsRuntimeFactoryTableEntry
{
    public Pointer<Il2CppType> Type;
    public Il2CppMethodPointer CreateFactoryFunction;
}
