using System.Buffers.Binary;

namespace Il2CppInspector.Outputs.Pdb;

internal static class MsfWriter
{
    private const int BlockSize = 4096;

    private static bool IsFreeMap(int block) => block % BlockSize is 1 or 2;

    public static void Write(string path, IReadOnlyList<byte[]> streams)
    {
        var next = 1;
        int[] Allocate(int count)
        {
            var blocks = new int[count];
            for (var i = 0; i < count; i++)
            {
                while (IsFreeMap(next))
                    next++;
                blocks[i] = next++;
            }
            return blocks;
        }
        static int BlockCount(int size) => checked((int)(((long)size + BlockSize - 1) / BlockSize));
        var streamBlocks = streams.Select(s => Allocate(BlockCount(s.Length))).ToArray();
        using var directory = new CvWriter();
        directory.U32((uint)streams.Count);
        foreach (var stream in streams)
            directory.U32((uint)stream.Length);
        foreach (var blocks in streamBlocks)
        foreach (var block in blocks)
            directory.U32((uint)block);
        var directoryBlocks = Allocate(BlockCount(directory.Length));
        if (directoryBlocks.Length > BlockSize / 4)
            throw new NotSupportedException("The PDB directory exceeds the MSF block-map capacity.");
        var mapBlock = Allocate(1)[0];
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        file.SetLength((long)next * BlockSize);
        using var header = new CvWriter();
        header.Bytes("Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8);
        header.U32(BlockSize);
        header.U32(1);
        header.U32((uint)next);
        header.U32((uint)directory.Length);
        header.U32(0);
        header.U32((uint)mapBlock);
        file.Write(header.ToArray());
        void WriteBlocks(int[] blocks, byte[] data)
        {
            for (var i = 0; i < blocks.Length; i++)
            {
                file.Position = (long)blocks[i] * BlockSize;
                file.Write(data.AsSpan(i * BlockSize, Math.Min(BlockSize, data.Length - i * BlockSize)));
            }
        }
        for (var i = 0; i < streams.Count; i++)
            WriteBlocks(streamBlocks[i], streams[i]);
        WriteBlocks(directoryBlocks, directory.ToArray());
        var map = new byte[directoryBlocks.Length * 4];
        for (var i = 0; i < directoryBlocks.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(map.AsSpan(i * 4), (uint)directoryBlocks[i]);
        WriteBlocks([mapBlock], map);

        // All allocated pages are used. Each active FPM describes 32768 pages,
        // while reserved FPM locations recur every 4096 pages.
        var freeMap = new byte[BlockSize];
        Array.Fill(freeMap, (byte)0xFF);
        for (var block = 1; block < next; block++)
        {
            if (!IsFreeMap(block))
                continue;
            file.Position = (long)block * BlockSize;
            file.Write(freeMap);
        }
        for (var interval = 0; interval * (long)BlockSize * 8 < next; interval++)
        {
            Array.Fill(freeMap, (byte)0xFF);
            var count = (int)Math.Min(BlockSize * 8, next - interval * (long)BlockSize * 8);
            for (var bit = 0; bit < count; bit++)
                freeMap[bit / 8] &= (byte)~(1 << (bit % 8));
            file.Position = (long)(1 + interval * BlockSize) * BlockSize;
            file.Write(freeMap);
        }
    }
}
