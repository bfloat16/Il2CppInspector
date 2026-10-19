using System.Buffers.Binary;

namespace Il2CppInspector
{
    /// <summary>
    /// Detects the 未定事件簿 (TOT) 6.1.0 encrypted metadata container before the stock Unity reader runs.
    /// The file's 43-slot header is packer-permuted, so detection uses the outer-layer key-schedule gate in
    /// the trailing 0x4000-byte container instead: u16(tail + 0xC8) == 0xFC2E and u16(tail + 0xCA) == 0x2CFE.
    /// These two little-endian words are plaintext in the shipped file and are exactly the values the
    /// libunity KDF checks before deriving the 0xB00 round-key blob (report section 3.1).
    /// </summary>
    public static class TotMetadataDetector
    {
        internal const int TailSize = 0x4000;
        internal const int GateOffset = 0xC8;
        internal const ushort GateA = 0xFC2E;
        internal const ushort GateB = 0x2CFE;

        internal static bool IsTot(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < TailSize)
            {
                return false;
            }

            var tail = bytes.Slice(bytes.Length - TailSize);
            return BinaryPrimitives.ReadUInt16LittleEndian(tail.Slice(GateOffset, 2)) == GateA && BinaryPrimitives.ReadUInt16LittleEndian(tail.Slice(GateOffset + 2, 2)) == GateB;
        }

        public static bool IsTot(string metadataPath)
        {
            if (!File.Exists(metadataPath))
            {
                return false;
            }

            using var stream = File.OpenRead(metadataPath);
            var length = stream.Length;
            if (length < TailSize)
            {
                return false;
            }

            Span<byte> gate = stackalloc byte[4];
            stream.Position = length - TailSize + GateOffset;
            return stream.Read(gate) == gate.Length && BinaryPrimitives.ReadUInt16LittleEndian(gate[..2]) == GateA && BinaryPrimitives.ReadUInt16LittleEndian(gate[2..]) == GateB;
        }
    }
}
