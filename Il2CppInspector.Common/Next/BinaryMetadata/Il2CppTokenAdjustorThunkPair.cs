using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppTokenAdjustorThunkPair
{
    [NativeInteger]
    public uint Token;

    public Il2CppMethodPointer AdjustorThunk;
}
