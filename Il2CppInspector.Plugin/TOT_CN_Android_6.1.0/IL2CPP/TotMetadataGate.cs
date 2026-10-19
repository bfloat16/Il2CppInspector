using System.Buffers.Binary;

namespace Il2CppInspector
{
    internal static class TotMetadataGate
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

            var tail = bytes[^TailSize..];
            return BinaryPrimitives.ReadUInt16LittleEndian(tail[GateOffset..]) == GateA && BinaryPrimitives.ReadUInt16LittleEndian(tail[(GateOffset + 2)..]) == GateB;
        }
    }
}
