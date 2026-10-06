using Il2CppInspector.Cpp;

namespace Il2CppInspector.Outputs.Pdb;

internal sealed class PdbTypes
{
    public List<CvTypeRecord> Records { get; } = [];
    private readonly Dictionary<CppComplexType, uint> forward = [];
    private readonly NativeDebugTypeAliases debugAliases = new();
    private readonly Dictionary<CppComplexType, uint> complete = [];
    private readonly Dictionary<CppComplexType, string> names = [];
    private readonly Dictionary<uint, uint> pointers = [];
    private readonly Dictionary<uint, uint> constants = [];
    private readonly Dictionary<uint, uint> volatiles = [];
    private readonly Dictionary<(uint Element, int Size), uint> arrays = [];
    private readonly Dictionary<string, uint> procedures = [];
    private readonly Dictionary<string, uint> arguments = [];
    private readonly Queue<CppComplexType> pending = new();
    private readonly HashSet<CppComplexType> building = [];

    private uint Add(ushort kind, CvWriter body, string name = null, bool isForward = false)
    {
        var index = checked(0x1000 + (uint)Records.Count);
        Records.Add(new(body.Record(kind, true), name, isForward));
        return index;
    }

    public uint Resolve(CppType type)
    {
        while (type is CppAlias alias)
            type = debugAliases.AliasElement(alias);
        return type switch
        {
            CppConstType constant => Constant(Referent(constant.ElementType)),
            CppVolatileType volatileType => Volatile(Referent(volatileType.ElementType)),
            CppPointerType pointer => Pointer(Referent(pointer.ElementType)),
            CppArrayType array => Array(Resolve(array.ElementType), array.SizeBytes),
            CppFnPtrType function => Pointer(Procedure(function)),
            CppComplexType complex => Definition(complex),
            CppForwardDefinitionType opaque => Opaque(opaque.Name),
            _ => Primitive(type),
        };
    }

    private readonly Dictionary<string, uint> opaqueTypes = [];

    private uint Opaque(string name)
    {
        if (opaqueTypes.TryGetValue(name, out var index))
            return index;
        using var body = AggregateHeader(false, 0, 0x280, 0, 0, name);
        index = Add(0x1505, body, name, true);
        opaqueTypes.Add(name, index);
        return index;
    }

    private uint Referent(CppType type)
    {
        while (type is CppAlias alias)
            type = debugAliases.AliasElement(alias);
        return type is CppComplexType and not CppEnumType ? Forward((CppComplexType)type) : Resolve(type);
    }

    private uint Pointer(uint referent)
    {
        if (pointers.TryGetValue(referent, out var index))
            return index;
        using var body = new CvWriter();
        body.U32(referent);
        body.U32(0x1000C);
        index = Add(0x1002, body);
        pointers.Add(referent, index);
        return index;
    }

    private uint Constant(uint referent)
    {
        if (constants.TryGetValue(referent, out var index))
            return index;
        using var body = new CvWriter();
        body.U32(referent);
        body.U16(1); // LF_MODIFIER, const
        index = Add(0x1001, body);
        constants.Add(referent, index);
        return index;
    }

    private uint Volatile(uint referent)
    {
        if (volatiles.TryGetValue(referent, out var index))
            return index;
        using var body = new CvWriter();
        body.U32(referent);
        body.U16(2);
        index = Add(0x1001, body);
        volatiles.Add(referent, index);
        return index;
    }

    private uint Array(uint element, int size)
    {
        if (arrays.TryGetValue((element, size), out var index))
            return index;
        using var body = new CvWriter();
        body.U32(element);
        body.U32(0x23);
        body.Numeric(checked((ulong)size));
        body.String("");
        index = Add(0x1503, body);
        arrays.Add((element, size), index);
        return index;
    }

    public uint Procedure(CppFnPtrType signature)
    {
        var returnType = Resolve(signature.ReturnType);
        var parameters = signature.Arguments.Select(p => Resolve(p.Type)).ToArray();
        var argumentKey = string.Join(',', parameters);
        var key = returnType + ":" + argumentKey;
        if (procedures.TryGetValue(key, out var index))
            return index;
        if (!arguments.TryGetValue(argumentKey, out var argumentList))
        {
            using var list = new CvWriter();
            list.U32((uint)parameters.Length);
            foreach (var parameter in parameters)
                list.U32(parameter);
            argumentList = Add(0x1201, list);
            arguments.Add(argumentKey, argumentList);
        }
        using var body = new CvWriter();
        body.U32(returnType);
        body.U8(0);
        body.U8(0);
        body.U16(checked((ushort)parameters.Length));
        body.U32(argumentList);
        index = Add(0x1008, body);
        procedures.Add(key, index);
        return index;
    }

    private string Name(CppComplexType type)
    {
        if (!names.TryGetValue(type, out var name))
            names.Add(type, name = string.IsNullOrEmpty(type.Name) ? $"__PdbAnonymous_{names.Count}" : type.Name);
        return name;
    }

    private uint Forward(CppComplexType type)
    {
        if (forward.TryGetValue(type, out var index))
            return index;
        var name = Name(type);
        using var body = AggregateHeader(type.ComplexValueType == ComplexValueType.Union, 0, 0x280, 0, 0, name);
        index = Add(type.ComplexValueType == ComplexValueType.Union ? (ushort)0x1506 : (ushort)0x1505, body, name, true);
        forward.Add(type, index);
        pending.Enqueue(type);
        return index;
    }

    public void Include(IEnumerable<CppType> types)
    {
        foreach (var type in types)
            Resolve(type);
        while (pending.TryDequeue(out var type))
            Definition(type);
    }

    private uint Definition(CppComplexType type)
    {
        if (complete.TryGetValue(type, out var index))
            return index;
        var forwardIndex = type is CppEnumType ? 0 : Forward(type);
        if (type.SizeBytes == 0)
            return forwardIndex;
        if (!building.Add(type))
            throw new InvalidDataException($"Cyclic by-value C++ layout: {type.Name}");
        try
        {
            var fields = type.Fields.Values.SelectMany(f => f).ToArray();
            var entries = new List<byte[]>(fields.Length);
            foreach (var field in fields)
            {
                using var member = new CvWriter();
                if (field is CppEnumField value)
                {
                    member.U16(0x1502);
                    member.U16(3);
                    var number = Convert.ToDecimal(value.Value);
                    if (number < 0)
                    {
                        member.U16(0x8009);
                        member.U64(unchecked((ulong)Convert.ToInt64(value.Value)));
                    }
                    else
                        member.Numeric(Convert.ToUInt64(value.Value));
                    member.String(value.CName);
                }
                else
                {
                    var fieldType = Resolve(debugAliases.FieldType(type, field.Type, field.IsConst));
                    var memberOffset = field.OffsetBytes;
                    if (field.BitfieldSize > 0)
                    {
                        // LLVM records the storage offset separately from the bit offset.
                        var storageBits = Math.Max(8, field.Type.Size);
                        var bitOffset = field.Offset % storageBits;
                        memberOffset = (field.Offset - bitOffset) / 8;
                        using var bitfield = new CvWriter();
                        bitfield.U32(fieldType);
                        bitfield.U8(checked((byte)field.BitfieldSize));
                        bitfield.U8(checked((byte)bitOffset));
                        fieldType = Add(0x1205, bitfield);
                    }
                    member.U16(0x150D);
                    member.U16(3);
                    member.U32(fieldType);
                    member.Numeric(checked((ulong)memberOffset));
                    member.String(field.Name);
                }
                member.Align(true);
                entries.Add(member.ToArray());
            }
            var fieldList = Fields(entries);
            var name = Name(type);
            if (type is CppEnumType enumeration)
            {
                using var body = new CvWriter();
                body.U16(checked((ushort)fields.Length));
                body.U16(0x200);
                body.U32(Resolve(enumeration.UnderlyingType));
                body.U32(fieldList);
                body.String(name);
                body.String(".?AW4" + name + "@@");
                index = Add(0x1507, body, name);
            }
            else
            {
                using var body = AggregateHeader(type.ComplexValueType == ComplexValueType.Union, checked((ushort)fields.Length), 0x200, fieldList, checked((ulong)type.SizeBytes), name);
                index = Add(type.ComplexValueType == ComplexValueType.Union ? (ushort)0x1506 : (ushort)0x1505, body, name);
            }
            complete.Add(type, index);
            return index;
        }
        finally
        {
            building.Remove(type);
            if (complete.ContainsKey(type))
                type.ReleaseTransientFields();
        }
    }

    private uint Fields(List<byte[]> entries)
    {
        var chunks = new List<byte[]>();
        using (var current = new MemoryStream())
        {
            // Accumulate bounded fragments before assigning continuation indices.
            foreach (var entry in entries)
            {
                if (entry.Length > 0xFF00)
                    throw new InvalidDataException("A CodeView member name exceeds the record limit.");
                if (current.Length + entry.Length > 0xFF00)
                {
                    chunks.Add(current.ToArray());
                    current.SetLength(0);
                    current.Position = 0;
                }
                current.Write(entry);
            }
            chunks.Add(current.ToArray());
        }
        uint continuation = 0;
        for (var i = chunks.Count - 1; i >= 0; i--)
        {
            using var body = new CvWriter();
            body.Bytes(chunks[i]);
            if (continuation != 0)
            {
                body.U16(0x1404);
                body.U16(0);
                body.U32(continuation);
            }
            continuation = Add(0x1203, body);
        }
        return continuation;
    }

    private static CvWriter AggregateHeader(bool union, ushort count, ushort properties, uint fields, ulong size, string name)
    {
        var body = new CvWriter();
        body.U16(count);
        body.U16(properties);
        body.U32(fields);
        if (!union)
        {
            body.U32(0);
            body.U32(0);
        }
        body.Numeric(size);
        body.String(name);
        body.String((union ? ".?AT" : ".?AU") + name + "@@");
        return body;
    }

    private static uint Primitive(CppType type) =>
        type.Name switch
        {
            "void" => 3,
            "bool" => 0x30,
            "char" => 0x70,
            "int8_t" or "signed char" => 0x68,
            "uint8_t" or "unsigned char" => 0x69,
            "int16_t" or "short" => 0x72,
            "uint16_t" or "unsigned short" or "wchar_t" => 0x73,
            "int32_t" or "int" or "long" => 0x74,
            "uint32_t" or "unsigned int" or "unsigned long" => 0x75,
            "int64_t" or "intptr_t" or "long long" => 0x76,
            "uint64_t" or "uintptr_t" or "size_t" or "unsigned long long" => 0x77,
            "float" => 0x40,
            "double" => 0x41,
            _ => throw new InvalidDataException($"Unsupported native primitive type: {type.Name}"),
        };
}
