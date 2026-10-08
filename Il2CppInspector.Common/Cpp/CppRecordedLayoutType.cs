using System.Text;

namespace Il2CppInspector.Cpp;

// Descriptors supply authoritative sizes and offsets; defer fields for large models.
internal sealed class CppRecordedLayoutType(int bytes, Action<CppComplexType> fill) : CppComplexType(ComplexValueType.Struct)
{
    // The recorded alignment is authoritative; field packing also preserves explicit unions.
    public override int Alignment => Math.Max(1, AlignmentBytes);
    internal bool ExplicitLayout { get; set; }
    public override bool CCompatibleEnumFields => false;
    private bool initialized;
    private int? releasedSize;
    public override int Size
    {
        get =>
            bytes >= 0 ? checked(bytes * 8)
            : !initialized && releasedSize.HasValue ? releasedSize.Value
            : Fields.Values.SelectMany(f => f).Select(f => f.Offset + f.Size).DefaultIfEmpty(0).Max();
        set { }
    }

    public override void ReleaseTransientFields()
    {
        if (!initialized)
            return;
        releasedSize = Size;
        base.Fields.Clear();
        initialized = false;
    }

    public override SortedDictionary<int, List<CppField>> Fields
    {
        get
        {
            if (!initialized)
            {
                initialized = true;
                try
                {
                    fill(this);
                }
                catch
                {
                    base.Fields.Clear();
                    initialized = false;
                    throw;
                }
            }
            return base.Fields;
        }
        internal set => base.Fields = value;
    }

    public override string ToString(string format = "")
    {
        if (Size == 0)
        {
            return $"struct {Name};\n";
        }

        var fields = Fields.Values.SelectMany(f => f).OrderBy(f => f.OffsetBytes).ToArray();
        var packing = ExplicitLayout ? 1 : Alignment;
        var natural = fields.Select(f => f.Type.Alignment).DefaultIfEmpty(1).Max();
        var alignment = AlignmentBytes > 0 && (ExplicitLayout || natural < AlignmentBytes) ? $" __declspec(align({AlignmentBytes}))" : "";
        var output = new StringBuilder($"#pragma pack(push, {packing})\nstruct{alignment} {Name} {{\n");
        var cursor = 0;
        var padding = 0;
        for (var i = 0; i < fields.Length; )
        {
            var field = fields[i];
            if (field.OffsetBytes > cursor)
            {
                output.AppendLine($"    uint8_t __padding_{padding++}[{field.OffsetBytes - cursor}];");
            }

            var end = field.OffsetBytes + field.SizeBytes;
            var j = i + 1;
            while (j < fields.Length && fields[j].OffsetBytes < end)
            {
                end = Math.Max(end, fields[j].OffsetBytes + fields[j].SizeBytes);
                j++;
            }
            if (j == i + 1)
            {
                output.AppendLine($"    {field.Type.ToFieldString(field.Name, format)};");
            }
            else
            {
                output.AppendLine("    union {");
                for (var n = i; n < j; n++)
                {
                    var member = fields[n];
                    var declaration = member.Type.ToFieldString(member.Name, format);
                    if (member.OffsetBytes == field.OffsetBytes)
                    {
                        output.AppendLine($"        {declaration};");
                    }
                    else
                    {
                        output.AppendLine($"        struct {{ uint8_t __padding_{padding++}[{member.OffsetBytes - field.OffsetBytes}]; {declaration}; }};");
                    }
                }
                output.AppendLine("    };");
            }
            cursor = Math.Max(cursor, end);
            i = j;
        }
        if (SizeBytes > cursor)
        {
            output.AppendLine($"    uint8_t __padding_{padding}[{SizeBytes - cursor}];");
        }

        return output.AppendLine("};\n#pragma pack(pop)").ToString();
    }
}
