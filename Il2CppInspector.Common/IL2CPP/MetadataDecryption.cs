using System.Buffers.Binary;

namespace Il2CppInspector;

internal static class MetadataDecryption
{
    private const int HeaderSize = 0x100;
    private const uint Magic = 0xFAB11BAF;
    private const int ExpectedVersion = 29;

    private static readonly string[] SectionOrder =
    [
        "stringLiteral",
        "stringLiteralData",
        "string",
        "events",
        "properties",
        "methods",
        "parameterDefaultValues",
        "fieldDefaultValues",
        "fieldAndParamDVData",
        "fieldMarshaledSizes",
        "parameters",
        "fields",
        "genericParameters",
        "genericParameterConstraints",
        "genericContainers",
        "nestedTypes",
        "interfaces",
        "vtableMethods",
        "interfaceOffsets",
        "typeDefinitions",
        "images",
        "assemblies",
        "fieldRefs",
        "referencedAssemblies",
        "attributeData",
        "attributeDataRange",
        "unresolvedVCallParamTypes",
        "unresolvedVCallParamRanges",
        "windowsRuntimeTypeNames",
        "windowsRuntimeStrings",
        "exportedTypeDefinitions",
    ];

    private readonly record struct HeaderEntry(int Offset, string Name, uint Key, bool IsAdd);

    private static readonly HeaderEntry[] HeaderMap =
    [
        new(0x08, "fieldAndParamDVData", 0x7F5E, false),
        new(0x10, "fieldMarshaledSizes", 0x170F2, false),
        new(0x18, "exportedTypeDefinitions", 0x29C0D, true),
        new(0x20, "typeDefinitions", 0x3B90C, false),
        new(0x28, "fields", 0xB75D6, false),
        new(0x30, "unresolvedVCallParamRanges", 0x99DB9, false),
        new(0x38, "string", 0x34B0E, false),
        new(0x40, "interfaces", 0xF0292, false),
        new(0x48, "events", 0xDD414, false),
        new(0x50, "properties", 0xC34DB, false),
        new(0x58, "images", 0x91A68, false),
        new(0x60, "methods", 0xF87B, false),
        new(0x68, "attributeData", 0x8C869, false),
        new(0x78, "referencedAssemblies", 0x8A623, false),
        new(0x80, "parameterDefaultValues", 0x17C39, false),
        new(0x88, "fieldDefaultValues", 0xE9F85, false),
        new(0x90, "vtableMethods", 0x2873D, false),
        new(0x98, "fieldRefs", 0x577F0, false),
        new(0xA0, "parameters", 0xCDBD1, false),
        new(0xA8, "genericParameters", 0x76CA9, false),
        new(0xB0, "genericParameterConstraints", 0xE9F1B, false),
        new(0xB8, "assemblies", 0xDFB98, false),
        new(0xC0, "interfaceOffsets", 0x8D964, false),
        new(0xC8, "genericContainers", 0xE9334, false),
        new(0xD0, "attributeDataRange", 0x27DF6, false),
        new(0xD8, "nestedTypes", 0x78247, false),
        new(0xE0, "stringLiteral", 0xD5B2F, false),
        new(0xE8, "unresolvedVCallParamTypes", 0x90E0F, false),
        new(0xF0, "stringLiteralData", 0x2C515, false),
        new(0xF8, "windowsRuntimeTypeNames", 0xE29FC, false),
    ];

    public static bool TryDecrypt(Metadata metadata)
    {
        var fileSize = (int)metadata.Length;
        if (fileSize < HeaderSize)
            return false;

        var buffer = metadata.GetBuffer();
        var data = buffer.AsSpan(0, fileSize);

        // If magic is already valid, this is normal metadata — skip decryption
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) == Magic)
            return false;

        // Try decoding obfuscated section offsets and validate
        var decoded = new Dictionary<string, int>(SectionOrder.Length);
        foreach (var entry in HeaderMap)
        {
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(data[entry.Offset..]);
            var offset = (int)(entry.IsAdd ? raw + entry.Key : raw ^ entry.Key);
            if (offset < 0 || offset > fileSize)
                return false;
            decoded[entry.Name] = offset;
        }
        decoded["windowsRuntimeStrings"] = fileSize;

        // Sort by offset to compute section sizes
        var sorted = decoded.OrderBy(kv => kv.Value).ToList();
        var sizes = new Dictionary<string, int>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            var nextOffset = i + 1 < sorted.Count ? sorted[i + 1].Value : fileSize;
            sizes[sorted[i].Key] = nextOffset - sorted[i].Value;
        }

        // Extract blobs from the original obfuscated data
        var blobs = new Dictionary<string, byte[]>(decoded.Count);
        foreach (var (name, offset) in decoded)
            blobs[name] = data.Slice(offset, sizes[name]).ToArray();

        // Decrypt per-record XOR
        DecryptMethods(blobs["methods"]);
        DecryptProperties(blobs["properties"]);
        DecryptEvents(blobs["events"]);
        DecryptStringLiterals(blobs["stringLiteral"]);
        DecryptParameterDefaultValues(blobs["parameterDefaultValues"]);
        FixImageTokens(blobs["images"]);

        // Reassemble as clean v29 metadata
        var offsets = new Dictionary<string, int>(SectionOrder.Length);
        var currentOffset = HeaderSize;
        foreach (var name in SectionOrder)
        {
            offsets[name] = currentOffset;
            currentOffset += (blobs.GetValueOrDefault(name) ?? []).Length;
        }

        var result = new byte[currentOffset];
        BinaryPrimitives.WriteUInt32LittleEndian(result, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), ExpectedVersion);

        int h = 8;
        foreach (var name in SectionOrder)
        {
            var blob = blobs.GetValueOrDefault(name) ?? [];
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(h), offsets[name]);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(h + 4), blob.Length);
            h += 8;
            blob.CopyTo(result, offsets[name]);
        }

        // Replace metadata stream contents
        metadata.SetLength(0);
        metadata.Position = 0;
        metadata.Write(result, 0, result.Length);
        metadata.Position = 0;

        return true;
    }

    private static void Xor32(Span<byte> blob, int offset, uint key)
    {
        var val = BinaryPrimitives.ReadUInt32LittleEndian(blob[offset..]);
        BinaryPrimitives.WriteUInt32LittleEndian(blob[offset..], val ^ key);
    }

    private static void Xor16(Span<byte> blob, int offset, ushort key)
    {
        var val = BinaryPrimitives.ReadUInt16LittleEndian(blob[offset..]);
        BinaryPrimitives.WriteUInt16LittleEndian(blob[offset..], (ushort)(val ^ key));
    }

    private static void DecryptMethods(byte[] blob)
    {
        const int structSize = 0x20;
        const uint key32 = 0x686;
        const ushort key16 = 0x686;
        int count = blob.Length / structSize;
        var span = blob.AsSpan();

        for (int i = 0; i < count; i++)
        {
            int b = i * structSize;
            Xor32(span, b + 0x00, key32);
            Xor32(span, b + 0x04, key32);
            Xor32(span, b + 0x08, key32);
            Xor32(span, b + 0x0C, key32);
            Xor32(span, b + 0x10, key32);
            Xor32(span, b + 0x14, key32);
            Xor16(span, b + 0x18, key16);
            Xor16(span, b + 0x1C, key16);
        }
    }

    private static void DecryptProperties(byte[] blob)
    {
        const int structSize = 0x14;
        const uint key = 0x2479;
        int count = blob.Length / structSize;
        var span = blob.AsSpan();

        for (int i = 0; i < count; i++)
        {
            int b = i * structSize;
            Xor32(span, b + 0x00, key);
            Xor32(span, b + 0x04, key);
            Xor32(span, b + 0x08, key);
            Xor32(span, b + 0x0C, key);
            Xor32(span, b + 0x10, key);
        }
    }

    private static void DecryptEvents(byte[] blob)
    {
        const int structSize = 0x18;
        const uint key = 0xF52;
        int count = blob.Length / structSize;
        var span = blob.AsSpan();

        for (int i = 0; i < count; i++)
        {
            int b = i * structSize;
            Xor32(span, b + 0x00, key);
            Xor32(span, b + 0x04, key);
            Xor32(span, b + 0x08, key);
            Xor32(span, b + 0x0C, key);
            Xor32(span, b + 0x10, key);
            Xor32(span, b + 0x14, key);
        }
    }

    private static void DecryptStringLiterals(byte[] blob)
    {
        const int structSize = 0x08;
        const uint key = 0x1BF52;
        int count = blob.Length / structSize;
        var span = blob.AsSpan();

        for (int i = 0; i < count; i++)
        {
            int b = i * structSize;
            Xor32(span, b + 0x00, key);
            Xor32(span, b + 0x04, key);
        }
    }

    private static void DecryptParameterDefaultValues(byte[] blob)
    {
        const int structSize = 0x0C;
        const uint key = 0x1C13;
        int count = blob.Length / structSize;
        var span = blob.AsSpan();

        for (int i = 0; i < count; i++)
        {
            int b = i * structSize;
            Xor32(span, b + 0x00, key);
            Xor32(span, b + 0x04, key);
            Xor32(span, b + 0x08, key);
        }
    }

    private static void FixImageTokens(byte[] blob)
    {
        const int structSize = 0x28;
        const int tokenFieldOffset = 0x1C;
        int count = blob.Length / structSize;
        var span = blob.AsSpan();

        for (int i = 0; i < count; i++)
        {
            int off = i * structSize + tokenFieldOffset;
            if (BinaryPrimitives.ReadUInt32LittleEndian(span[off..]) == 0)
                BinaryPrimitives.WriteUInt32LittleEndian(span[off..], 1);
        }
    }
}
