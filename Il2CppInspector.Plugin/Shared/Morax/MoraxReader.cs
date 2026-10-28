using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Utils;

namespace Il2CppInspector;

/// <summary>Byte readers, table framing and registration discovery shared by the MORAX plugins.</summary>
internal static class MoraxReader
{
    internal static ushort U16(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(o, 2));

    internal static short I16(ReadOnlySpan<byte> d, int o) => unchecked((short)U16(d, o));

    internal static uint U32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(o, 4));

    internal static int I32(ReadOnlySpan<byte> d, int o) => unchecked((int)U32(d, o));

    internal static ulong U64(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt64LittleEndian(d.Slice(o, 8));

    internal static int Index16(ushort v) => v == ushort.MaxValue ? -1 : v;

    internal delegate T RecordReader<T>(ReadOnlySpan<byte> d, int index);

    internal static ImmutableArray<T> Records<T>(byte[] bytes, int offset, int count, int stride, RecordReader<T> read)
    {
        CheckRange(bytes, offset, checked(count * stride));
        var result = new T[count];
        for (var i = 0; i < count; i++)
            result[i] = read(bytes.AsSpan(offset + i * stride, stride), i);
        return ImmutableCollectionsMarshal.AsImmutableArray(result);
    }

    internal static void CheckRange(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException($"MORAX table outside input: offset 0x{offset:X}, length 0x{length:X}.");
    }

    /// <summary>Strings and literals are 8-byte blocks XORed with key + block * increment.</summary>
    internal static string DecodeBlocks(byte[] data, int offset, int length, ulong key, ulong increment, Encoding encoding = null)
    {
        var rounded = checked((length + 7) & ~7);
        CheckRange(data, offset, rounded);
        Span<byte> bytes = rounded <= 512 ? stackalloc byte[rounded] : new byte[rounded];
        for (var i = 0; i < rounded; i += 8)
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(i, 8), U64(data, offset + i) ^ unchecked(key + (ulong)(i / 8) * increment));
        return (encoding ?? Encoding.UTF8).GetString(bytes[..length]);
    }

    /// <summary>
    /// Decodes the embedded header slots. Kinds: 0 = XOR, 1 = ADD, 2 = SUB, 3 = SHR 3 then XOR,
    /// 4 = SHR 4 then XOR, 5 = SHR 2 then XOR,
    /// 6 = arithmetic SHR 3 then XOR.
    /// </summary>
    internal static Dictionary<int, int> DecodeSlots(ReadOnlySpan<byte> header, (int Offset, int Kind, uint Key)[] ciphers)
    {
        var slots = new Dictionary<int, int>(ciphers.Length);
        foreach (var (offset, kind, key) in ciphers)
        {
            var raw = U32(header, offset);
            slots.Add(
                offset,
                unchecked(
                    (int)(
                        kind switch
                        {
                            0 => raw ^ key,
                            1 => raw + key,
                            2 => raw - key,
                            3 => (raw >> 3) ^ key,
                            4 => (raw >> 4) ^ key,
                            5 => (raw >> 2) ^ key,
                            6 => (uint)((int)raw >> 3) ^ key,
                            _ => throw new InvalidDataException($"Unknown MORAX header cipher kind {kind}."),
                        }
                    )
                )
            );
        }
        return slots;
    }

    /// <summary>
    /// The registration function is five consecutive <c>lea rax,[rip+target]; mov [rip+global],rax</c> pairs.
    /// Returns the function address followed by the five targets: code registration, metadata registration,
    /// metadata usages, enum sharing flag and embedded header.
    /// </summary>
    internal static ulong[] DiscoverRegistration(IFileFormatStream image, Func<ulong[], bool> accept, bool executableOnly = true)
    {
        foreach (var section in image.GetSections().Where(s => (s.IsExec || !executableOnly) && !s.IsBSS && s.ImageLength >= 70))
        {
            var bytes = image.ReadBytes(section.ImageStart, section.ImageLength);
            for (var i = 0; i <= bytes.Length - 70; i++)
            {
                var valid = true;
                for (var p = 0; p < 5 && valid; p++)
                {
                    var o = i + p * 14;
                    valid =
                        bytes[o] == 0x48
                        && bytes[o + 1] == 0x8D
                        && bytes[o + 2] == 0x05
                        && bytes[o + 7] == 0x48
                        && bytes[o + 8] == 0x89
                        && bytes[o + 9] == 0x05;
                }
                if (!valid)
                    continue;
                var start = image.MapFileOffsetToVA(section.ImageStart + (uint)i);
                var targets = new ulong[6];
                targets[0] = start;
                for (var p = 0; p < 5; p++)
                    targets[p + 1] = unchecked((ulong)((long)start + p * 14 + 7 + I32(bytes, i + p * 14 + 3)));
                if (accept(targets))
                    return targets;
            }
        }
        throw new NotSupportedException("Could not locate the MORAX registration block.");
    }

    /// <summary>Accepts a registration block whose header target starts with "MHY\0".</summary>
    internal static bool HasMhyHeader(IFileFormatStream image, ulong[] targets) =>
        image.TryMapVATR(targets[5], out _) && image.ReadMappedUInt32(targets[5]) == 0x0059484D;

    internal static bool IsMhy(ReadOnlySpan<byte> metadata) => metadata.Length >= 4 && metadata[..4].SequenceEqual("MHY\0"u8);

    /// <summary>Scalars and length-prefixed strings use the stock blob format; other defaults keep only their address.</summary>
    internal static object DecodeConstant(Metadata metadata, int offset, Il2CppTypeEnum kind)
    {
        if ((uint)kind < (uint)Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN || (uint)kind > (uint)Il2CppTypeEnum.IL2CPP_TYPE_STRING)
            return null;
        metadata.Position = offset;
        return BlobReader.GetConstantValueFromBlob(null, kind, metadata);
    }
}
