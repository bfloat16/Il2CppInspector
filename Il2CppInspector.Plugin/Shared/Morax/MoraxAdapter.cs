using System.Collections.Immutable;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;
using Il2CppInspector.Plugins;

namespace Il2CppInspector;

/// <summary>Registration, layout groups and binary attachment shared by the MORAX metadata adapters.</summary>
internal abstract class MoraxAdapter : GameMetadataAdapter
{
    internal readonly IFileFormatStream Image;
    private readonly Section[] executableSections;
    internal ulong RegistrationAddress { get; private set; }
    internal ulong CodeRegistrationAddress { get; private set; }
    internal ulong MetadataRegistrationAddress { get; private set; }
    internal ulong UsageAddress { get; private set; }
    internal bool EnableEnumSharing { get; private set; }

    // Constructed generic type ordinal -> layout group.
    internal Dictionary<int, int> GenericLayoutGroups { get; } = [];

    protected MoraxAdapter(IFileFormatStream image, GamePlugin plugin)
        : base(plugin)
    {
        if (image is not PEReader || image.Bits != 64 || image.Arch != "x64")
            throw new NotSupportedException($"{plugin.GameId} supports Windows x64 PE images only.");
        Image = image;
        executableSections = image.GetSections().Where(s => s.IsExec).ToArray();
    }

    /// <summary>Locates the registration block and returns the embedded header address.</summary>
    private protected ulong DiscoverRegistration(Func<ulong[], bool> accept, bool readEnumSharing = true, bool executableOnly = true)
    {
        var targets = MoraxReader.DiscoverRegistration(Image, accept, executableOnly);
        RegistrationAddress = targets[0];
        CodeRegistrationAddress = targets[1];
        MetadataRegistrationAddress = targets[2];
        UsageAddress = targets[3];
        if (readEnumSharing)
            EnableEnumSharing = Image.ReadMappedBytes(targets[4], 1)[0] != 0;
        return targets[5];
    }

    internal bool IsCode(ulong va) => va != 0 && executableSections.Any(s => va >= s.VirtualStart && va <= s.VirtualEnd);

    internal abstract int LayoutAlignment(int group);

    internal abstract int DefinitionLayoutGroup(int definition);

    // RVA data is managed storage, even when its marshal representation has a different size.
    public override int FieldRvaSize(Il2CppType type, Il2CppBinary binary) =>
        type.Type switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN or Il2CppTypeEnum.IL2CPP_TYPE_I1 or Il2CppTypeEnum.IL2CPP_TYPE_U1 => 1,
            Il2CppTypeEnum.IL2CPP_TYPE_CHAR or Il2CppTypeEnum.IL2CPP_TYPE_I2 or Il2CppTypeEnum.IL2CPP_TYPE_U2 => 2,
            Il2CppTypeEnum.IL2CPP_TYPE_I4 or Il2CppTypeEnum.IL2CPP_TYPE_U4 or Il2CppTypeEnum.IL2CPP_TYPE_R4 => 4,
            Il2CppTypeEnum.IL2CPP_TYPE_I8 or Il2CppTypeEnum.IL2CPP_TYPE_U8 or Il2CppTypeEnum.IL2CPP_TYPE_R8 => 8,
            Il2CppTypeEnum.IL2CPP_TYPE_I or Il2CppTypeEnum.IL2CPP_TYPE_U => 8,
            Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE => Math.Max(0, checked((int)binary.TypeDefinitionSizes[type.Data.KlassIndex].InstanceSize - 16)),
            _ => 0,
        };

    /// <summary>Installs the normalized tables; callers then set the build-specific registration records.</summary>
    private protected void Attach(
        Il2CppBinary binary,
        Metadata metadata,
        ImmutableArray<Il2CppType> types,
        Dictionary<ulong, int> typeAddresses,
        ImmutableArray<Il2CppGenericInst> instances,
        ImmutableArray<Il2CppMethodSpec> specs,
        ImmutableArray<uint> fieldOffsets,
        ImmutableArray<Il2CppTypeDefinitionSizes> sizes
    )
    {
        binary.Metadata = metadata;
        binary.Image.Version = metadata.Version;
        binary.CodeRegistrationPointer = CodeRegistrationAddress;
        binary.MetadataRegistrationPointer = MetadataRegistrationAddress;
        binary.RegistrationFunctionPointer = RegistrationAddress;
        binary.Modules = [];
        binary.TypeReferences = types;
        binary.TypeReferenceIndicesByAddress = typeAddresses;
        binary.GenericInstances = instances;
        binary.MethodSpecs = specs;
        binary.FieldOffsets = fieldOffsets;
        binary.TypeDefinitionSizes = sizes;
    }
}
