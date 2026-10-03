using System.Reflection;
using VersionedSerialization;
using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.Metadata;

using StringIndex = int;
using VTableIndex = int;

[VersionedStruct]
public partial record struct Il2CppTypeDefinition
{
    public static readonly TypeIndex InvalidTypeIndex = -1;

    public StringIndex NameIndex { get; internal set; }
    public StringIndex NamespaceIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.0")]
    public int CustomAttributeIndex { get; internal set; }

    public TypeIndex ByValTypeIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.5")]
    public TypeIndex ByRefTypeIndex { get; internal set; }

    public TypeIndex DeclaringTypeIndex { get; internal set; }
    public TypeIndex ParentIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "31.0")]
    public TypeIndex ElementTypeIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.1")]
    public int RgctxStartIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.1")]
    public int RgctxCount { get; internal set; }

    public GenericContainerIndex GenericContainerIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "22.0")]
    public int ReversePInvokeWrapperIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "22.0")]
    public int MarshalingFunctionsIndex { get; internal set; }

    [VersionCondition(GreaterThanOrEqual = "21.0", LessThanOrEqual = "22.0")]
    public int CcwFunctionIndex { get; internal set; }

    [VersionCondition(GreaterThanOrEqual = "21.0", LessThanOrEqual = "22.0")]
    public int GuidIndex { get; internal set; }

    public TypeAttributes Flags { get; internal set; }

    public FieldIndex FieldIndex { get; internal set; }
    public MethodIndex MethodIndex { get; internal set; }
    public EventIndex EventIndex { get; internal set; }
    public PropertyIndex PropertyIndex { get; internal set; }
    public NestedTypeIndex NestedTypeIndex { get; internal set; }
    public InterfacesIndex InterfacesIndex { get; internal set; }
    public VTableIndex VTableIndex { get; internal set; }
    public InterfacesIndex InterfaceOffsetsStart { get; internal set; }

    public ushort MethodCount { get; internal set; }
    public ushort PropertyCount { get; internal set; }
    public ushort FieldCount { get; internal set; }
    public ushort EventCount { get; internal set; }
    public ushort NestedTypeCount { get; internal set; }
    public ushort VTableCount { get; internal set; }
    public ushort InterfacesCount { get; internal set; }
    public ushort InterfaceOffsetsCount { get; internal set; }

    public Il2CppTypeDefinitionBitfield Bitfield { get; internal set; }

    [VersionCondition(GreaterThanOrEqual = "19.0")]
    public uint Token { get; internal set; }

    public readonly bool IsValid => NameIndex != 0;

    public int GetEnumElementTypeIndex(StructVersion version) => version >= MetadataVersions.V350 ? ParentIndex : ElementTypeIndex;
}
