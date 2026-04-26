using System.Reflection;
using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.Metadata;

using GenericParameterConstraintIndex = short;
using StringIndex = int;

[VersionedStruct]
public partial record struct Il2CppGenericParameter
{
    public GenericContainerIndex OwnerIndex { get; private set; }
    public StringIndex NameIndex { get; private set; }
    public GenericParameterConstraintIndex ConstraintsStart { get; private set; }
    public short ConstraintsCount { get; private set; }
    public ushort Num { get; private set; }
    public ushort Flags { get; private set; }

    public readonly GenericParameterAttributes Attributes => (GenericParameterAttributes)Flags;
}
