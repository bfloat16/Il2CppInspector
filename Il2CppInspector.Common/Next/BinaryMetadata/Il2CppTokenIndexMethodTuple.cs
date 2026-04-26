using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppTokenIndexMethodTuple
{
    public uint Token;
    public int Index;

    public PrimitivePointer<Il2CppMethodPointer> Method; // void**

    public uint GenericMethodIndex;
}
