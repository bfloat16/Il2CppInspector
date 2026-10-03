using System.Reflection;
using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.Metadata;

using StringIndex = int;

[VersionedStruct]
public partial record struct Il2CppMethodDefinition
{
    public StringIndex NameIndex { get; internal set; }

    [VersionCondition(GreaterThanOrEqual = "16.0")]
    public TypeDefinitionIndex DeclaringType { get; internal set; }
    public TypeIndex ReturnType { get; internal set; }

    [VersionCondition(GreaterThanOrEqual = "31.0")]
    public uint ReturnParameterToken { get; internal set; }

    public ParameterIndex ParameterStart { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.0")]
    public int CustomAttributeIndex { get; internal set; }

    public GenericContainerIndex GenericContainerIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.1")]
    public int MethodIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.1")]
    public int InvokerIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.1")]
    public int ReversePInvokeWrapperIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.1")]
    public int RgctxStartIndex { get; internal set; }

    [VersionCondition(LessThanOrEqual = "24.1")]
    public int RgctxCount { get; internal set; }

    public uint Token { get; internal set; }
    public ushort Flags { get; internal set; }
    public ushort ImplFlags { get; internal set; }
    public ushort Slot { get; internal set; }
    public ushort ParameterCount { get; internal set; }

    public readonly MethodAttributes Attributes => (MethodAttributes)Flags;
    public readonly MethodImplAttributes ImplAttributes => (MethodImplAttributes)ImplFlags;

    public readonly bool IsValid => NameIndex != 0;
}
