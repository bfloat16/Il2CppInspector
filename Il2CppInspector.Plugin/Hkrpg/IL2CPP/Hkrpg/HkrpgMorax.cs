using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInspector.Next;

namespace Il2CppInspector;

// Port of CastoriceDumper/crates/morax for the CN Windows 4.6.51 beta build.
internal sealed partial class HkrpgMorax : Plugins.GameMetadataAdapter
{
    internal readonly IFileFormatStream Image;
    internal readonly Metadata Metadata;
    private readonly byte[] global;
    private readonly byte[] startup;
    private readonly HkrpgHeader header;
    private readonly Section[] sections;
    internal readonly ulong RegistrationAddress;
    internal readonly ulong CodeRegistrationAddress;
    internal readonly ulong MetadataRegistrationAddress;
    private readonly ulong usageAddress;
    private readonly EventHandler<string> status;

    internal static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    internal static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    internal static int I32(ReadOnlySpan<byte> data, int offset) => unchecked((int)U32(data, offset));
    internal static ulong U64(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
    internal static int Index16(ushort value) => value == ushort.MaxValue ? -1 : value;

    private int Base(uint offset) => checked((int)((ulong)header.PayloadOffset + offset));
    private delegate T RecordReader<T>(ReadOnlySpan<byte> data, int index);

    private static ImmutableArray<T> Records<T>(byte[] bytes, int offset, int count, int stride, RecordReader<T> read)
    {
        CheckRange(bytes, offset, checked(count * stride));
        var result = new T[count];
        for (var i = 0; i < count; i++)
            result[i] = read(bytes.AsSpan(offset + i * stride, stride), i);
        return ImmutableCollectionsMarshal.AsImmutableArray(result);
    }

    private static void CheckRange(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException($"HSR table outside input: offset 0x{offset:X}, length 0x{length:X}.");
    }

    private HkrpgMorax(IFileFormatStream image, byte[] globalData, byte[] startupData, EventHandler<string> status, Plugins.GamePlugin plugin)
        : base(plugin)
    {
        Image = image;
        global = globalData;
        startup = startupData;
        this.status = status;
        if (image is not PEReader || image.Bits != 64 || image.Arch != "x64")
            throw new NotSupportedException("HSR 4.6.51 supports Windows x64 PE images only.");
        sections = image.GetSections().Where(s => s.VirtualLength > 0).ToArray();
        var addresses = DiscoverRegistration();
        RegistrationAddress = addresses[0];
        CodeRegistrationAddress = addresses[1];
        MetadataRegistrationAddress = addresses[2];
        usageAddress = addresses[3];
        header = new HkrpgHeader(Image.ReadMappedBytes(addresses[5], 0x200), DiscoverPayloadOffset());
        // These keys are specific to the source decoder's build; reject other MORAX games early.
        if (header.ImageCount != 143 || header.AssemblyCount != 143 || header.MethodCount != 772590 || header.GenericClassCount != 441996)
            throw new NotSupportedException("This MORAX image does not match HSR CN Windows 4.6.51. Keys and layouts vary per build.");
        Metadata = Metadata.CreateForPlugin(global, this, MetadataVersions.V245, status);
        global = Metadata.GetBuffer();
        status?.Invoke(this, $"Decoding HSR 4.6.51 metadata (payload +0x{header.PayloadOffset:X})");
        ReadMetadata();
    }

    internal static Il2CppInspector Load(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status, Plugins.GamePlugin plugin)
    {
        var adapter = new HkrpgMorax(image, global, startup, status, plugin);
        var binary = new Il2CppBinaryX64(image, status);
        adapter.ReadBinary(binary);
        return new Il2CppInspector(binary, adapter.Metadata);
    }

    private ulong[] DiscoverRegistration()
    {
        foreach (var section in sections.Where(s => !s.IsBSS && s.ImageLength >= 70))
        {
            var bytes = Image.ReadBytes(section.ImageStart, section.ImageLength);
            for (var i = 0; i <= bytes.Length - 70; i++)
            {
                if (bytes[i] != 0x48 || bytes[i + 1] != 0x8D || bytes[i + 2] != 0x05)
                    continue;
                var valid = true;
                for (var p = 0; p < 5; p++)
                {
                    var o = i + p * 14;
                    if (bytes[o] != 0x48 || bytes[o + 1] != 0x8D || bytes[o + 2] != 0x05 || bytes[o + 7] != 0x48 || bytes[o + 8] != 0x89 || bytes[o + 9] != 0x05)
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid)
                    continue;
                var start = Image.MapFileOffsetToVA(section.ImageStart + (uint)i);
                var targets = new ulong[6];
                targets[0] = start;
                for (var p = 0; p < 5; p++)
                    targets[p + 1] = unchecked((ulong)((long)start + p * 14 + 7 + I32(bytes, i + p * 14 + 3)));
                if (!Image.TryMapVATR(targets[5] + 0x1FC, out _) || !Image.TryMapVATR(targets[2] + 0x80, out _) || !Image.TryMapVATR(targets[1] + 0x40, out _))
                    continue;
                var span = Image.ReadMappedUInt32(targets[5] + 0x1F8) ^ 0x1608C2C8u;
                if (span <= 1_000_000 || span > global.Length)
                    continue;
                var types = Image.ReadMappedUInt64(targets[2] + 0x80);
                var methods = Image.ReadMappedUInt64(targets[1] + 0x40);
                if (types != 0 && methods != 0 && Image.TryMapVATR(types, out _) && Image.TryMapVATR(methods, out _))
                    return targets;
            }
        }
        throw new NotSupportedException("Could not locate the HSR MORAX registration block in the original PE.");
    }

    private uint DiscoverPayloadOffset()
    {
        // Source signature: lea rcx,[rip+...]; call ...; mov rsi,rax; lea rcx,[rip+...].
        // Only accept the verified initializer sequence leading to add rsi,imm32.
        foreach (var section in sections.Where(s => !s.IsBSS && s.ImageLength >= 64))
        {
            var bytes = Image.ReadBytes(section.ImageStart, section.ImageLength);
            for (var i = 0; i <= bytes.Length - 64; i++)
            {
                if (bytes[i] != 0x48 || bytes[i + 1] != 0x8D || bytes[i + 2] != 0x0D || bytes[i + 7] != 0xE8
                    || bytes[i + 12] != 0x48 || bytes[i + 13] != 0x89 || bytes[i + 14] != 0xC6
                    || bytes[i + 15] != 0x48 || bytes[i + 16] != 0x8D || bytes[i + 17] != 0x0D)
                    continue;
                for (var j = i + 22; j <= i + 57; j++)
                    if (bytes[j] == 0x48 && bytes[j + 1] == 0x81 && bytes[j + 2] == 0xC6)
                        return U32(bytes, j + 3);
                throw new NotSupportedException("Unrecognized HSR payload initializer; could not decode add rsi,imm32.");
            }
        }
        return 0;
    }

    private void Intern(int index)
    {
        if (!Metadata.Strings.ContainsKey(index))
            Metadata.Strings.Add(index, DecodeString(unchecked((uint)index)));
    }

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private string DecodeString(uint index)
    {
        if (index == uint.MaxValue)
            return "";
        var negative = (index & 0x80000000) != 0;
        var length = (int)(negative ? (index >> 23) & 0xFF : (index >> 25) & 0x3F);
        if (length == 0)
            return "";
        var offset = index & (negative ? 0x7FFFFFu : 0x1FFFFFFu);
        var key = unchecked((ulong)offset * 0x907C49622D94D21AUL + 0x75B679DAF67C3F24UL);
        return DecodeBlocks(checked(Base(header.StringOffset) + (int)offset), length, key, 0x3E693CD23A41FDEFUL, StrictUtf8);
    }

    private string DecodeBlocks(int offset, int length, ulong key, ulong increment, Encoding encoding)
    {
        var rounded = checked((length + 7) & ~7);
        CheckRange(global, offset, rounded);
        Span<byte> bytes = rounded <= 512 ? stackalloc byte[rounded] : new byte[rounded];
        for (var i = 0; i < rounded; i += 8)
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(i, 8), U64(global, offset + i) ^ unchecked(key + (ulong)(i / 8) * increment));
        return encoding.GetString(bytes[..length]);
    }

    internal bool IsCode(ulong address) => address != 0 && sections.Any(s => s.IsExec && address >= s.VirtualStart && address <= s.VirtualEnd);
    private bool Contains(ulong address, ulong length) => address != 0 && sections.Any(s => address >= s.VirtualStart && address <= s.VirtualEnd && length <= s.VirtualEnd - address + 1);
}
