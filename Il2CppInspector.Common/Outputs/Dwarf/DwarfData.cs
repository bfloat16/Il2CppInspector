using System.Text;

namespace Il2CppInspector.Outputs.Dwarf;

internal sealed class DwarfData : IDisposable
{
    public FileStream Stream { get; } =
        new(Path.Combine(Path.GetTempPath(), "il2cpp-dwarf-" + Guid.NewGuid().ToString("N") + ".tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.DeleteOnClose);
    public BinaryWriter Writer { get; }
    public ulong Position => checked((ulong)Stream.Position);

    public DwarfData() => Writer = new(Stream, Encoding.UTF8, true);

    public void Uleb(ulong value)
    {
        do
        {
            var next = (byte)(value & 127);
            value >>= 7;
            Writer.Write(value == 0 ? next : (byte)(next | 128));
        } while (value != 0);
    }

    public void Sleb(long value)
    {
        bool more;
        do
        {
            var next = (byte)(value & 127);
            value >>= 7;
            more = !((value == 0 && (next & 64) == 0) || (value == -1 && (next & 64) != 0));
            Writer.Write(more ? (byte)(next | 128) : next);
        } while (more);
    }

    public ulong String(string text)
    {
        var offset = Position;
        Writer.Write(Encoding.UTF8.GetBytes(text ?? ""));
        Writer.Write((byte)0);
        return offset;
    }

    public void Address(ulong address, int bits)
    {
        if (bits == 64)
            Writer.Write(address);
        else
            Writer.Write(checked((uint)address));
    }

    public void Dispose()
    {
        Writer.Dispose();
        Stream.Dispose();
    }
}
