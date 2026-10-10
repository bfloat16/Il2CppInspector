using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInspector.Next;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;
using static Il2CppInspector.HkrpgRecords;

namespace Il2CppInspector;

internal sealed class HkrpgMetadata
{
    internal Metadata Model { get; }
    internal HkrpgHeader Header { get; }
    internal byte[] Global { get; }
    private readonly byte[] startup;
    private readonly EventHandler<string> status;

    internal Il2CppTypeDefinition[] Definitions => definitions;
    internal Il2CppMethodDefinition[] Methods => methods;
    internal Il2CppFieldDefinition[] Fields => fields;
    internal int AttributeRangeCount => attributeRanges.Length;

    internal HkrpgMetadata(byte[] global, byte[] startup, HkrpgHeader header, Plugins.GameMetadataAdapter adapter, EventHandler<string> status)
    {
        Header = header;
        this.startup = startup;
        this.status = status;
        Model = Metadata.CreateForPlugin(global, adapter, MetadataVersions.V245, status);
        Global = Model.GetBuffer();
        status?.Invoke(this, $"Decoding {adapter.Plugin.GameId} metadata (payload +0x{header.PayloadOffset:X})");
        ReadMetadata();
    }

    internal int Base(uint offset) => checked((int)((ulong)Header.PayloadOffset + offset));
    internal ImmutableArray<T> ReadGlobal<T>(uint offset, int count, int stride, RecordReader<T> read) =>
        Records(Global, Base(offset), count, stride, read);
    internal ImmutableArray<T> ReadStartup<T>(uint offset, int count, int stride, RecordReader<T> read) =>
        Records(startup, checked((int)offset), count, stride, read);

    private Il2CppTypeDefinition[] definitions;
    private Il2CppMethodDefinition[] methods;
    private Il2CppFieldDefinition[] fields;
    private Il2CppCustomAttributeTypeRange[] attributeRanges;

    private void ReadMetadata()
    {
        Model.Images = ReadStartup(Header.ImagesOffset, checked((int)Header.ImageCount), 40, HkrpgRecords.Image);
        var count = checked((int)Model.Images.Max(i => (uint)(int)i.TypeStart + i.TypeCount));
        definitions = ReadGlobal(Header.TypesOffset, count, 70, HkrpgRecords.Type).ToArray();
        methods = ReadGlobal(Header.MethodsOffset, checked((int)Header.MethodCount), 26, HkrpgRecords.Method).ToArray();
        fields = new Il2CppFieldDefinition[definitions.Max(t => t.FieldCount == 0 ? 0 : checked((int)t.FieldIndex + t.FieldCount))];
        CheckRange(Global, Base(Header.FieldsOffset), checked(fields.Length * 8));
        foreach (var t in definitions)
        {
            for (var local = 0; local < t.FieldCount; local++)
            {
                var fi = checked((int)t.FieldIndex + local);
                fields[fi] = HkrpgRecords.Field(Global.AsSpan(Base(Header.FieldsOffset) + fi * 8, 8), fi, t.FieldIndex, local);
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
        Model.Properties = ReadGlobal(Header.PropertiesOffset, definitions.Max(t => t.PropertyCount == 0 ? 0 : checked((int)t.PropertyIndex + t.PropertyCount)), 10, HkrpgRecords.Property);
        Model.Events = ReadGlobal(Header.EventsOffset, definitions.Max(t => t.EventCount == 0 ? 0 : checked((int)t.EventIndex + t.EventCount)), 14, HkrpgRecords.Event);
        Model.Params = ReadGlobal(Header.ParametersOffset, methods.Max(m => m.ParameterCount == 0 ? 0 : checked((int)m.ParameterStart + m.ParameterCount)), 8, HkrpgRecords.Parameter);
        Model.NestedTypeIndices = ReadGlobal(Header.NestedTypesOffset, definitions.Max(t => t.NestedTypeCount == 0 ? 0 : (int)t.NestedTypeIndex + t.NestedTypeCount), 4, (d, _) => I32(d, 0));
        Model.InterfaceUsageIndices = ReadGlobal(Header.InterfacesOffset, definitions.Max(t => t.InterfacesCount == 0 ? 0 : (int)t.InterfacesIndex + t.InterfacesCount), 4, (d, _) => new TypeIndex(I32(d, 0)));
        for (var ti = 0; ti < count; ti++)
        {
            var t = definitions[ti];
            for (var n = 0; n < t.NestedTypeCount; n++)
            {
                var child = Model.NestedTypeIndices[t.NestedTypeIndex + n];
                if ((uint)child >= count)
                    throw new InvalidDataException("HSR nested type index out of range.");
                // Resolve the parent's byval type after the binary type table has been decoded.
                declaringDefinitions[child] = ti;
            }
        }
        Model.Fields = ImmutableCollectionsMarshal.AsImmutableArray(fields);
        Model.FieldDefaultValues = ReadGlobal(Header.FieldDefaultsOffset, checked((int)Header.FieldDefaultCount), 12,
            (d, _) => new Il2CppFieldDefaultValue { TypeIndex = I32(d, 0), DataIndex = I32(d, 4), FieldIndex = I32(d, 8) });
        Model.ParameterDefaultValues = ReadGlobal(Header.ParameterDefaultsOffset, checked((int)Header.ParameterDefaultCount), 12,
            (d, _) => new Il2CppParameterDefaultValue { TypeIndex = I32(d, 0), ParameterIndex = I32(d, 4), DataIndex = I32(d, 8) });
        Model.Assemblies = BuildAssemblies();
        ReadCustomAttributes();
        Model.AttributeDataRanges = [];
        Model.InterfaceOffsets = [];
        Model.VTableMethodIndices = [];
        Model.MetadataUsageLists = [];
        Model.MetadataUsagePairs = [];
        Model.TypeInlineArrays = [];
        foreach (var field in fields) Intern(field.NameIndex);
        foreach (var method in methods) Intern(method.NameIndex);
        foreach (var parameter in Model.Params) Intern(parameter.NameIndex);
        foreach (var property in Model.Properties) Intern(property.NameIndex);
        foreach (var ev in Model.Events) Intern(ev.NameIndex);
        status?.Invoke(this, $"HSR: {Model.Images.Length} images, {definitions.Length} types, {methods.Length} methods, {fields.Length} fields.");
    }

    private readonly Dictionary<int, int> declaringDefinitions = [];

    private void ReadCustomAttributes()
    {
        // Runtime image initialization (RVA 0x3F31FB7) supplies exact ranges, including empty images.
        var count = 0;
        foreach (var image in Model.Images)
        {
            if (image.CustomAttributeStart != count)
                throw new InvalidDataException("HSR image custom attribute ranges are not contiguous.");
            count = checked(count + (int)image.CustomAttributeCount);
        }
        attributeRanges = ReadGlobal(Header.AttributeRangesOffset, count, 8,
            (d, _) => new Il2CppCustomAttributeTypeRange { Start = (int)(U32(d, 0) & 0xFFFFFF), Count = d[3], Token = U32(d, 4) }).ToArray();
        var typeCount = 0;
        foreach (var image in Model.Images)
        {
            uint previousToken = 0;
            for (var i = image.CustomAttributeStart; i < image.CustomAttributeStart + image.CustomAttributeCount; i++)
            {
                var range = attributeRanges[i];
                var index = (int)(range.Token & 0xFFFFFF);
                var validToken = (range.Token >> 24) switch
                {
                    0x02 => index >= image.TypeStart && index < image.TypeStart + image.TypeCount,
                    0x04 => index < fields.Length,
                    0x06 => index < methods.Length,
                    0x08 => index < Model.Params.Length,
                    0x14 => index < Model.Events.Length,
                    0x17 => index < Model.Properties.Length,
                    0x20 => index == 1,
                    _ => false,
                };
                if (!validToken || range.Token <= previousToken || range.Count == 0 || range.Start != typeCount)
                    throw new InvalidDataException($"Invalid HSR custom attribute range {i}.");
                previousToken = range.Token;
                typeCount = checked(typeCount + range.Count);
            }
        }
        Model.AttributeTypeRanges = ImmutableCollectionsMarshal.AsImmutableArray(attributeRanges);
        Model.AttributeTypeIndices = ReadGlobal(Header.AttributeTypesOffset, typeCount, 4, (d, _) => I32(d, 0));
        if (Model.AttributeTypeIndices.Any(i => i < 0))
            throw new InvalidDataException("HSR custom attribute type index is negative.");
        status?.Invoke(this, $"HSR: {count} custom attribute ranges, {typeCount} attribute type references.");
    }

    internal void ResolveEnumElementTypes(IReadOnlyList<Il2CppType> types)
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
        var assemblies = new Il2CppAssemblyDefinition[Model.Images.Length];
        for (var i = 0; i < assemblies.Length; i++)
        {
            var image = Model.Images[i];
            Intern(image.NameIndex);
            var nameIndex = -2 - i;
            var name = Path.GetFileNameWithoutExtension(Model.Strings[image.NameIndex]);
            Model.Strings.Add(nameIndex, name);
            assemblies[i] = new()
            {
                ImageIndex = i,
                Token = 0x20000001,
                Aname = new() { NameIndex = nameIndex, CultureIndex = -1, PublicKeyIndex = -1, Major = name == "mscorlib" ? 4 : 0 },
            };
        }
        return ImmutableCollectionsMarshal.AsImmutableArray(assemblies);
    }

    internal void ResolveDeclaringTypes()
    {
        foreach (var (child, parent) in declaringDefinitions)
        {
            if (definitions[parent].ByValTypeIndex < 0)
                throw new InvalidDataException($"HSR declaring type {parent} has no type reference.");
            definitions[child].DeclaringTypeIndex = definitions[parent].ByValTypeIndex;
        }
    }

    internal void NormalizeMethodTokens(Il2CppImageDefinition image, int first, int last)
    {
        for (var mi = first; mi < last; mi++)
            methods[mi].Token = 0x06000000u | (uint)(mi - first + 1);
        // Preserve range indices: the generator table uses their original global order.
        for (var ai = image.CustomAttributeStart; ai < image.CustomAttributeStart + image.CustomAttributeCount; ai++)
        {
            if ((attributeRanges[ai].Token >> 24) != 0x06)
                continue;
            var mi = (int)(attributeRanges[ai].Token & 0xFFFFFF);
            if (mi < first || mi >= last)
                throw new InvalidDataException("HSR custom attribute method belongs to another image.");
            attributeRanges[ai].Token = methods[mi].Token;
        }
    }

    internal void Intern(int index)
    {
        if (!Model.Strings.ContainsKey(index))
            Model.Strings.Add(index, DecodeString(unchecked((uint)index)));
    }

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    internal string DecodeString(uint index)
    {
        if (index == uint.MaxValue)
            return "";
        var negative = (index & 0x80000000) != 0;
        var length = (int)(negative ? (index >> 23) & 0xFF : (index >> 25) & 0x3F);
        if (length == 0)
            return "";
        var offset = index & (negative ? 0x7FFFFFu : 0x1FFFFFFu);
        var key = unchecked((ulong)offset * 0x907C49622D94D21AUL + 0x75B679DAF67C3F24UL);
        return DecodeBlocks(checked(Base(Header.StringOffset) + (int)offset), length, key, 0x3E693CD23A41FDEFUL, StrictUtf8);
    }

    internal string DecodeBlocks(int offset, int length, ulong key, ulong increment, Encoding encoding)
    {
        var rounded = checked((length + 7) & ~7);
        CheckRange(Global, offset, rounded);
        Span<byte> bytes = rounded <= 512 ? stackalloc byte[rounded] : new byte[rounded];
        for (var i = 0; i < rounded; i += 8)
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(i, 8), U64(Global, offset + i) ^ unchecked(key + (ulong)(i / 8) * increment));
        return encoding.GetString(bytes[..length]);
    }
}
