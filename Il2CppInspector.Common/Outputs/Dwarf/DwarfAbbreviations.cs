namespace Il2CppInspector.Outputs.Dwarf;

internal enum DwarfAbbrev : byte
{
    Unit = 1,
    Function,
    UntypedFunction,
    Parameter,
    BaseType,
    Void,
    Pointer,
    Structure,
    Union,
    Declaration,
    Member,
    Bitfield,
    Array,
    Subrange,
    Enumeration,
    SignedEnumerator,
    UnsignedEnumerator,
    Subroutine,
    Typedef,
    Const,
    Variable,
    Volatile,
}

internal static class DwarfAbbreviations
{
    // DWARF 4, 32-bit format: string, section and reference offsets are four bytes.
    private const uint Name = 0x03,
        LinkageName = 0x6E,
        Type = 0x49,
        ByteSize = 0x0B;
    private const uint Strp = 0x0E,
        Ref4 = 0x13,
        Udata = 0x0F,
        Present = 0x19,
        SecOffset = 0x17,
        Addr = 0x01,
        Data2 = 0x05,
        Data8 = 0x07;

    public static DwarfData Build()
    {
        var data = new DwarfData();
        // A zero low_pc makes the DWARF 4 range-list entries absolute addresses.
        Add(DwarfAbbrev.Unit, 0x11, true, (0x25, Strp), (0x13, Data2), (Name, Strp), (0x1B, Strp), (0x10, SecOffset), (0x11, Addr), (0x55, SecOffset));
        Add(DwarfAbbrev.Function, 0x2E, true, (Name, Strp), (LinkageName, Strp), (0x11, Addr), (0x12, Data8), (0x27, Present), (Type, Ref4));
        Add(DwarfAbbrev.UntypedFunction, 0x2E, false, (Name, Strp), (LinkageName, Strp), (0x11, Addr), (0x12, Data8));
        Add(DwarfAbbrev.Parameter, 0x05, false, (Name, Strp), (Type, Ref4));
        Add(DwarfAbbrev.BaseType, 0x24, false, (Name, Strp), (ByteSize, Udata), (0x3E, 0x0B));
        Add(DwarfAbbrev.Void, 0x3B, false, (Name, Strp));
        Add(DwarfAbbrev.Pointer, 0x0F, false, (ByteSize, Udata), (Type, Ref4));
        Add(DwarfAbbrev.Structure, 0x13, true, (Name, Strp), (ByteSize, Udata));
        Add(DwarfAbbrev.Union, 0x17, true, (Name, Strp), (ByteSize, Udata));
        Add(DwarfAbbrev.Declaration, 0x13, false, (Name, Strp), (0x3C, Present));
        Add(DwarfAbbrev.Member, 0x0D, false, (Name, Strp), (Type, Ref4), (0x38, Udata));
        Add(DwarfAbbrev.Bitfield, 0x0D, false, (Name, Strp), (Type, Ref4), (0x0D, Udata), (0x6B, Udata));
        Add(DwarfAbbrev.Array, 0x01, true, (Type, Ref4), (ByteSize, Udata));
        Add(DwarfAbbrev.Subrange, 0x21, false, (0x37, Udata));
        Add(DwarfAbbrev.Enumeration, 0x04, true, (Name, Strp), (ByteSize, Udata), (Type, Ref4));
        Add(DwarfAbbrev.SignedEnumerator, 0x28, false, (Name, Strp), (0x1C, 0x0D));
        Add(DwarfAbbrev.UnsignedEnumerator, 0x28, false, (Name, Strp), (0x1C, Udata));
        Add(DwarfAbbrev.Subroutine, 0x15, true, (Type, Ref4), (0x27, Present));
        Add(DwarfAbbrev.Typedef, 0x16, false, (Name, Strp), (Type, Ref4));
        Add(DwarfAbbrev.Const, 0x26, false, (Type, Ref4));
        Add(DwarfAbbrev.Variable, 0x34, false, (Name, Strp), (LinkageName, Strp), (Type, Ref4), (0x3F, Present), (0x02, 0x18));
        Add(DwarfAbbrev.Volatile, 0x35, false, (Type, Ref4));
        data.Uleb(0);
        return data;

        void Add(DwarfAbbrev code, uint tag, bool children, params (uint Attribute, uint Form)[] attributes)
        {
            data.Uleb((byte)code);
            data.Uleb(tag);
            data.Writer.Write(children ? (byte)1 : (byte)0);
            foreach (var (attribute, form) in attributes)
            {
                data.Uleb(attribute);
                data.Uleb(form);
            }
            data.Uleb(0);
            data.Uleb(0);
        }
    }
}
