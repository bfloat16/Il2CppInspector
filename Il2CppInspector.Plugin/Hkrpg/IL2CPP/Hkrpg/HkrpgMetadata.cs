using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector;

internal sealed partial class HkrpgMorax
{
    private Il2CppTypeDefinition[] definitions;
    private Il2CppMethodDefinition[] methods;
    private Il2CppFieldDefinition[] fields;

    private void ReadMetadata()
    {
        Metadata.Images = Records(startup, checked((int)header.ImagesOffset), checked((int)header.ImageCount), 40, HkrpgRecords.Image);
        var count = checked((int)Metadata.Images.Max(i => (uint)(int)i.TypeStart + i.TypeCount));
        if (count != 84933)
            throw new InvalidDataException("HSR 4.6.51 image type ranges do not match the supported build.");
        definitions = Records(global, Base(header.TypesOffset), count, 70, HkrpgRecords.Type).ToArray();
        methods = Records(global, Base(header.MethodsOffset), checked((int)header.MethodCount), 26, HkrpgRecords.Method).ToArray();
        fields = new Il2CppFieldDefinition[definitions.Max(t => t.FieldCount == 0 ? 0 : checked((int)t.FieldIndex + t.FieldCount))];
        CheckRange(global, Base(header.FieldsOffset), checked(fields.Length * 8));
        foreach (var t in definitions)
        {
            for (var local = 0; local < t.FieldCount; local++)
            {
                var fi = checked((int)t.FieldIndex + local);
                fields[fi] = HkrpgRecords.Field(global.AsSpan(Base(header.FieldsOffset) + fi * 8, 8), fi, t.FieldIndex, local);
            }
        }
        for (var ti = 0; ti < count; ti++)
        {
            var t = definitions[ti];
            if (t.MethodCount > 0 && ((int)t.MethodIndex < 0 || (int)t.MethodIndex > methods.Length - t.MethodCount))
                throw new InvalidDataException($"HSR type {ti} has invalid method range.");
            for (var local = 0; local < t.MethodCount; local++)
                methods[t.MethodIndex + local].DeclaringType = ti;
            Intern(t.NameIndex);
            Intern(t.NamespaceIndex);
        }
        if (methods.Any(m => m.DeclaringType < 0))
            throw new InvalidDataException("HSR method table contains definitions without an owner.");
        Metadata.Properties = Records(global, Base(header.PropertiesOffset), definitions.Max(t => t.PropertyCount == 0 ? 0 : checked((int)t.PropertyIndex + t.PropertyCount)), 10, HkrpgRecords.Property);
        Metadata.Events = Records(global, Base(header.EventsOffset), definitions.Max(t => t.EventCount == 0 ? 0 : checked((int)t.EventIndex + t.EventCount)), 14, HkrpgRecords.Event);
        Metadata.Params = Records(global, Base(header.ParametersOffset), methods.Max(m => m.ParameterCount == 0 ? 0 : checked((int)m.ParameterStart + m.ParameterCount)), 8, HkrpgRecords.Parameter);
        Metadata.NestedTypeIndices = Records(global, Base(header.NestedTypesOffset), definitions.Max(t => t.NestedTypeCount == 0 ? 0 : (int)t.NestedTypeIndex + t.NestedTypeCount), 4, (d, _) => I32(d, 0));
        Metadata.InterfaceUsageIndices = Records(global, Base(header.InterfacesOffset), definitions.Max(t => t.InterfacesCount == 0 ? 0 : (int)t.InterfacesIndex + t.InterfacesCount), 4, (d, _) => new TypeIndex(I32(d, 0)));
        for (var ti = 0; ti < count; ti++)
        {
            var t = definitions[ti];
            for (var n = 0; n < t.NestedTypeCount; n++)
            {
                var child = Metadata.NestedTypeIndices[t.NestedTypeIndex + n];
                if ((uint)child >= count)
                    throw new InvalidDataException("HSR nested type index out of range.");
                // Resolve the parent's byval type after the binary type table has been decoded.
                declaringDefinitions[child] = ti;
            }
        }
        Metadata.Fields = ImmutableCollectionsMarshal.AsImmutableArray(fields);
        Metadata.FieldDefaultValues = Records(global, Base(header.FieldDefaultsOffset), checked((int)header.FieldDefaultCount), 12,
            (d, _) => new Il2CppFieldDefaultValue { TypeIndex = I32(d, 0), DataIndex = I32(d, 4), FieldIndex = I32(d, 8) });
        Metadata.ParameterDefaultValues = Records(global, Base(header.ParameterDefaultsOffset), checked((int)header.ParameterDefaultCount), 12,
            (d, _) => new Il2CppParameterDefaultValue { TypeIndex = I32(d, 0), ParameterIndex = I32(d, 4), DataIndex = I32(d, 8) });
        Metadata.Assemblies = BuildAssemblies();
        Metadata.AttributeTypeRanges = [];
        Metadata.AttributeTypeIndices = [];
        Metadata.AttributeDataRanges = [];
        Metadata.InterfaceOffsets = [];
        Metadata.VTableMethodIndices = [];
        Metadata.MetadataUsageLists = [];
        Metadata.MetadataUsagePairs = [];
        Metadata.TypeInlineArrays = [];
        foreach (var field in fields) Intern(field.NameIndex);
        foreach (var method in methods) Intern(method.NameIndex);
        foreach (var parameter in Metadata.Params) Intern(parameter.NameIndex);
        foreach (var property in Metadata.Properties) Intern(property.NameIndex);
        foreach (var ev in Metadata.Events) Intern(ev.NameIndex);
        status?.Invoke(this, $"HSR: {Metadata.Images.Length} images, {definitions.Length} types, {methods.Length} methods, {fields.Length} fields.");
    }

    private readonly Dictionary<int, int> declaringDefinitions = [];

    private void ResolveEnumElementTypes()
    {
        for (var ti = 0; ti < definitions.Length; ti++)
        {
            var definition = definitions[ti];
            if (!definition.Bitfield.EnumType)
                continue;

            // MORAX can rename value__. Its unique instance field determines enum storage.
            var elementIndex = -1;
            for (var local = 0; local < definition.FieldCount; local++)
            {
                var field = fields[definition.FieldIndex + local];
                var type = types[field.TypeIndex];
                if (((FieldAttributes)type.Attrs & (FieldAttributes.Static | FieldAttributes.Literal)) != 0)
                    continue;
                if (elementIndex >= 0 || type.ByRef || (byte)type.Type is < (byte)Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN or > (byte)Il2CppTypeEnum.IL2CPP_TYPE_U8)
                    throw new InvalidDataException($"HSR enum {ti} has invalid underlying fields.");
                elementIndex = field.TypeIndex;
            }
            if (elementIndex < 0)
                throw new InvalidDataException($"HSR enum {ti} has no underlying instance field.");
            definitions[ti].ElementTypeIndex = elementIndex;
        }
    }

    private ImmutableArray<Il2CppAssemblyDefinition> BuildAssemblies()
    {
        // morax reconstructs assembly identities from image names, not encrypted assembly records.
        var assemblies = new Il2CppAssemblyDefinition[Metadata.Images.Length];
        for (var i = 0; i < assemblies.Length; i++)
        {
            var image = Metadata.Images[i];
            Intern(image.NameIndex);
            var nameIndex = -2 - i;
            var name = Path.GetFileNameWithoutExtension(Metadata.Strings[image.NameIndex]);
            Metadata.Strings.Add(nameIndex, name);
            assemblies[i] = new()
            {
                ImageIndex = i,
                Token = 0x20000001,
                Aname = new() { NameIndex = nameIndex, CultureIndex = -1, PublicKeyIndex = -1, Major = name == "mscorlib" ? 4 : 0 },
            };
        }
        return ImmutableCollectionsMarshal.AsImmutableArray(assemblies);
    }
}
