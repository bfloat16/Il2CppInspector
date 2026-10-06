using System.Buffers.Binary;
using Il2CppInspector.Cpp;

namespace Il2CppInspector.Outputs.Dwarf;

internal sealed class DwarfTypes(DwarfData info, DwarfData strings, DwarfData fixups)
{
    private sealed record Entry(DwarfAbbrev Kind, string Name = null, int Size = 0, uint Referent = 0, uint[] Parameters = null, CppComplexType Complex = null);

    private readonly Dictionary<CppComplexType, uint> complexTypes = [];
    private readonly NativeDebugTypeAliases debugAliases = new();
    private readonly Dictionary<(string Name, int Size), uint> primitives = [];
    private readonly Dictionary<(uint Referent, int Size), uint> pointers = [];
    private readonly Dictionary<(uint Element, int Count), uint> arrays = [];
    private readonly Dictionary<(string Name, uint Type), uint> aliases = [];
    private readonly Dictionary<uint, uint> constants = [];
    private readonly Dictionary<uint, uint> volatiles = [];
    private readonly Dictionary<string, uint> procedures = [];
    private readonly Dictionary<string, uint> opaque = [];
    private readonly Dictionary<string, ulong> names = [];
    private readonly List<Entry> entries = [null];
    private readonly List<bool> written = [true];
    private readonly List<bool> emitted = [true];
    private readonly List<bool> inProgress = [false];
    private readonly List<ulong> offsets = [0];
    private readonly Queue<uint> pending = new();
    private readonly List<uint> unitTypes = [];
    private readonly byte[] patchBuffer = new byte[1024 * 1024];
    private ulong unitStart;
    public int Count { get; private set; }

    // A type and its dependency graph must stay in the same compilation unit.
    public Action BeforeTopLevel { get; set; }

    public void BeginUnit(ulong start)
    {
        if (pending.Count != 0)
            throw new InvalidOperationException("Unresolved types in the previous DWARF compilation unit.");
        foreach (var id in unitTypes)
        {
            written[(int)id] = false;
            offsets[(int)id] = 0;
        }
        unitTypes.Clear();
        unitStart = start;
    }

    public void Name(string name, bool cache = true)
    {
        name ??= "";
        if (!names.TryGetValue(name, out var offset))
        {
            offset = strings.String(name);
            if (cache)
                names.Add(name, offset);
        }
        info.Writer.Write(checked((uint)offset));
    }

    public void Reference(uint id)
    {
        fixups.Writer.Write(info.Position);
        info.Writer.Write(id);
        if (!written[(int)id])
            pending.Enqueue(id);
    }

    private uint Add(Entry entry)
    {
        var id = checked((uint)entries.Count);
        entries.Add(entry);
        written.Add(false);
        emitted.Add(false);
        inProgress.Add(false);
        offsets.Add(0);
        return id;
    }

    public uint Resolve(CppType type)
    {
        switch (type)
        {
            case CppConstType constant:
                return Constant(Resolve(constant.ElementType));
            case CppVolatileType volatileType:
                var volatileId = Resolve(volatileType.ElementType);
                if (!volatiles.TryGetValue(volatileId, out var modifier))
                    volatiles.Add(volatileId, modifier = Add(new(DwarfAbbrev.Volatile, Referent: volatileId)));
                return modifier;
            case CppAlias alias:
                var aliasKey = (alias.Name, Resolve(debugAliases.AliasElement(alias)));
                if (!aliases.TryGetValue(aliasKey, out var aliasId))
                    aliases.Add(aliasKey, aliasId = Add(new(DwarfAbbrev.Typedef, alias.Name, Referent: aliasKey.Item2)));
                return aliasId;
            case CppPointerType pointer:
                return Pointer(Resolve(pointer.ElementType), pointer.SizeBytes);
            case CppArrayType array:
                var arrayKey = (Resolve(array.ElementType), array.Length);
                if (!arrays.TryGetValue(arrayKey, out var arrayId))
                    arrays.Add(arrayKey, arrayId = Add(new(DwarfAbbrev.Array, Size: array.SizeBytes, Referent: arrayKey.Item1, Parameters: [checked((uint)array.Length)])));
                return arrayId;
            case CppFnPtrType function:
                var result = Resolve(function.ReturnType);
                var arguments = function.Arguments.Select(a => Resolve(a.Type)).ToArray();
                var key = result + ":" + string.Join(',', arguments);
                if (!procedures.TryGetValue(key, out var procedure))
                    procedures.Add(key, procedure = Add(new(DwarfAbbrev.Subroutine, Referent: result, Parameters: arguments)));
                return Pointer(procedure, function.SizeBytes);
            case CppComplexType complex:
                if (!complexTypes.TryGetValue(complex, out var complexId))
                    complexTypes.Add(complex, complexId = Add(new(DwarfAbbrev.Structure, Complex: complex)));
                return complexId;
            case CppForwardDefinitionType forward:
                if (!opaque.TryGetValue(forward.Name, out var opaqueId))
                    opaque.Add(forward.Name, opaqueId = Add(new(DwarfAbbrev.Declaration, forward.Name)));
                return opaqueId;
            default:
                var primitiveKey = (type.Name, type.SizeBytes);
                if (!primitives.TryGetValue(primitiveKey, out var primitiveId))
                    primitives.Add(primitiveKey, primitiveId = Add(new(type.Name == "void" ? DwarfAbbrev.Void : DwarfAbbrev.BaseType, type.Name, type.SizeBytes)));
                return primitiveId;
        }
    }

    private uint Pointer(uint referent, int size)
    {
        if (!pointers.TryGetValue((referent, size), out var id))
            pointers.Add((referent, size), id = Add(new(DwarfAbbrev.Pointer, Size: size, Referent: referent)));
        return id;
    }

    private uint Constant(uint referent)
    {
        if (!constants.TryGetValue(referent, out var id))
            constants.Add(referent, id = Add(new(DwarfAbbrev.Const, Referent: referent)));
        return id;
    }

    public void Include(IEnumerable<CppType> types)
    {
        foreach (var type in types)
        {
            var id = Resolve(type);
            if (emitted[(int)id])
                continue;
            BeforeTopLevel?.Invoke();
            Emit(id);
            Drain();
        }
    }

    public void Drain()
    {
        // DWARF allows forward references. Emit layout dependencies first for
        // consumers that build aggregate sizes in order, but allow pointer cycles.
        while (pending.TryDequeue(out var id))
            Emit(id);
    }

    public void Emit(uint root)
    {
        if (written[(int)root])
            return;
        var stack = new Stack<uint>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var id = stack.Peek();
            if (written[(int)id])
            {
                stack.Pop();
                continue;
            }
            if (!inProgress[(int)id])
            {
                inProgress[(int)id] = true;
                foreach (var dependency in Dependencies(entries[(int)id]))
                {
                    if (inProgress[(int)dependency])
                        throw new InvalidDataException("Cyclic DWARF value-type layout dependency.");
                    if (!written[(int)dependency])
                        stack.Push(dependency);
                }
                continue;
            }
            stack.Pop();
            offsets[(int)id] = info.Position;
            WriteEntry(entries[(int)id], id);
            written[(int)id] = true;
            emitted[(int)id] = true;
            inProgress[(int)id] = false;
            unitTypes.Add(id);
            Count++;
        }
    }

    private IEnumerable<uint> Dependencies(Entry entry)
    {
        switch (entry.Kind)
        {
            case DwarfAbbrev.Typedef:
            case DwarfAbbrev.Const:
            case DwarfAbbrev.Volatile:
            case DwarfAbbrev.Array:
                yield return entry.Referent;
                break;
            case DwarfAbbrev.Pointer:
                // The pointee's layout does not affect the pointer's size.
                break;
            case DwarfAbbrev.Subroutine:
                yield return entry.Referent;
                foreach (var parameter in entry.Parameters)
                    yield return parameter;
                break;
            case DwarfAbbrev.Structure:
            case DwarfAbbrev.Union:
            case DwarfAbbrev.Enumeration:
                if (entry.Complex is CppEnumType enumeration)
                    yield return Resolve(enumeration.UnderlyingType);
                if (entry.Complex.SizeBytes == 0 && entry.Complex is not CppEnumType)
                    yield break;
                foreach (var field in entry.Complex.Fields.Values.SelectMany(f => f))
                {
                    if (field is CppEnumField)
                        continue;
                    yield return Resolve(debugAliases.FieldType(entry.Complex, field.Type, field.IsConst));
                }
                break;
        }
    }

    private void WriteEntry(Entry entry, uint id)
    {
        if (entry.Complex != null)
        {
            Aggregate(entry.Complex, id);
            return;
        }
        info.Uleb((byte)entry.Kind);
        switch (entry.Kind)
        {
            case DwarfAbbrev.BaseType:
                Name(entry.Name);
                info.Uleb(checked((ulong)entry.Size));
                info.Writer.Write(Encoding(entry.Name));
                break;
            case DwarfAbbrev.Void:
            case DwarfAbbrev.Declaration:
                Name(entry.Name);
                break;
            case DwarfAbbrev.Pointer:
                info.Uleb(checked((ulong)entry.Size));
                Reference(entry.Referent);
                break;
            case DwarfAbbrev.Array:
                Reference(entry.Referent);
                info.Uleb(checked((ulong)entry.Size));
                info.Uleb((byte)DwarfAbbrev.Subrange);
                info.Uleb(entry.Parameters[0]);
                info.Uleb(0);
                break;
            case DwarfAbbrev.Subroutine:
                Reference(entry.Referent);
                foreach (var parameter in entry.Parameters)
                {
                    info.Uleb((byte)DwarfAbbrev.Parameter);
                    Name("");
                    Reference(parameter);
                }
                info.Uleb(0);
                break;
            case DwarfAbbrev.Typedef:
                Name(entry.Name);
                Reference(entry.Referent);
                break;
            case DwarfAbbrev.Const:
            case DwarfAbbrev.Volatile:
                Reference(entry.Referent);
                break;
        }
    }

    private void Aggregate(CppComplexType type, uint id)
    {
        var name = string.IsNullOrEmpty(type.Name) ? $"__DwarfAnonymous_{id}" : type.Name;
        var size = type.SizeBytes;
        if (size == 0 && type is not CppEnumType)
        {
            info.Uleb((byte)DwarfAbbrev.Declaration);
            Name(name);
            type.ReleaseTransientFields();
            return;
        }
        try
        {
            var enumeration = type as CppEnumType;
            info.Uleb(
                (byte)(
                    enumeration != null ? DwarfAbbrev.Enumeration
                    : type.ComplexValueType == ComplexValueType.Union ? DwarfAbbrev.Union
                    : DwarfAbbrev.Structure
                )
            );
            Name(name);
            info.Uleb(checked((ulong)size));
            if (enumeration != null)
                Reference(Resolve(enumeration.UnderlyingType));
            foreach (var field in type.Fields.Values.SelectMany(f => f))
            {
                if (field is CppEnumField value)
                {
                    var underlying = enumeration.UnderlyingType;
                    while (underlying is CppAlias alias)
                        underlying = alias.ElementType;
                    var unsigned = Encoding(underlying.Name) is 7 or 8;
                    info.Uleb((byte)(unsigned ? DwarfAbbrev.UnsignedEnumerator : DwarfAbbrev.SignedEnumerator));
                    Name(value.CName);
                    if (unsigned)
                        info.Uleb(Convert.ToUInt64(value.Value));
                    else
                        info.Sleb(Convert.ToInt64(value.Value));
                    continue;
                }
                info.Uleb((byte)(field.BitfieldSize > 0 ? DwarfAbbrev.Bitfield : DwarfAbbrev.Member));
                Name(field.Name);
                var fieldType = Resolve(debugAliases.FieldType(type, field.Type, field.IsConst));
                Reference(fieldType);
                if (field.BitfieldSize > 0)
                {
                    info.Uleb(checked((ulong)field.BitfieldSize));
                    info.Uleb(checked((ulong)field.Offset));
                }
                else
                    info.Uleb(checked((ulong)field.OffsetBytes));
            }
            info.Uleb(0);
        }
        finally
        {
            type.ReleaseTransientFields();
        }
    }

    private static byte Encoding(string name) =>
        name switch
        {
            "bool" => 2,
            "float" or "double" => 4,
            "char" or "signed char" or "int8_t" => 6,
            "unsigned char" or "uint8_t" => 8,
            "uint16_t" or "uint32_t" or "uint64_t" or "uintptr_t" or "size_t" or "unsigned short" or "unsigned int" or "unsigned long" or "unsigned long long" or "wchar_t" => 7,
            "int16_t" or "int32_t" or "int64_t" or "intptr_t" or "short" or "int" or "long" or "long long" => 5,
            _ => throw new InvalidDataException($"Unsupported DWARF primitive type: {name}"),
        };

    public void CompleteUnit()
    {
        Drain();
        info.Writer.Flush();
        fixups.Writer.Flush();
        var resume = info.Stream.Position;
        var fixupsEnd = fixups.Stream.Position;
        fixups.Stream.Position = 0;
        using var references = new BinaryReader(fixups.Stream, System.Text.Encoding.UTF8, true);
        var next = Next();
        info.Stream.Position = checked((long)unitStart);
        while (next != ulong.MaxValue)
        {
            var start = info.Position;
            if (next < start || next > (ulong)resume - 4)
                throw new InvalidDataException("DWARF reference lies outside the compilation unit.");
            var count = checked((int)Math.Min(patchBuffer.Length, resume - info.Stream.Position));
            info.Stream.ReadExactly(patchBuffer.AsSpan(0, count));
            while (next < start + (ulong)count)
            {
                var index = checked((int)(next - start));
                if (index + 4 > count)
                {
                    count = index;
                    break;
                }
                var id = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(patchBuffer.AsSpan(index)));
                if (id <= 0 || id >= offsets.Count || offsets[id] <= unitStart || !written[id])
                    throw new InvalidDataException("Unresolved DWARF type reference.");
                BinaryPrimitives.WriteUInt32LittleEndian(patchBuffer.AsSpan(index), checked((uint)(offsets[id] - unitStart)));
                next = Next();
            }
            info.Stream.Position = checked((long)start);
            info.Writer.Write(patchBuffer.AsSpan(0, count));
        }
        info.Stream.Position = resume;
        fixups.Stream.SetLength(0);
        fixups.Stream.Position = 0;

        ulong Next() => references.BaseStream.Position < fixupsEnd ? references.ReadUInt64() : ulong.MaxValue;
    }

    public void CopyPatched(Stream output)
    {
        info.Writer.Flush();
        info.Stream.Position = 0;
        info.Stream.CopyTo(output);
    }
}
