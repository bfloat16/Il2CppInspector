using VersionedSerialization;

namespace Il2CppInspector.Tests.Serialization;

internal static class ReaderTests
{
    internal static void Run()
    {
        byte[] data = [1, 0, 0, 0, 2, 0, 0, 0];
        var reader = Reader.LittleEndian(data);
        var values = reader.ReadVersionedObjectArray<VersionedValue>(2, new StructVersion(2));
        Check(
            values[0].Value == 1 && values[1].Value == 2 && values.All(static v => v.LegacyValue == 73),
            "Versioned value arrays initialize excluded fields before reading serialized members"
        );

        reader = Reader.LittleEndian(data);
        var objects = reader.ReadVersionedObjectArray<VersionedReference>(2, new StructVersion(2));
        Check(
            objects[0].Value == 1
                && objects[1].Value == 2
                && objects.All(static v => v.LegacyValue == 73)
                && !ReferenceEquals(objects[0], objects[1]),
            "Versioned reference arrays construct independent objects before reading serialized members"
        );

        byte[] legacy = [1, 0, 0, 0, 3, 0, 0, 0, 2, 0, 0, 0, 4, 0, 0, 0];
        reader = Reader.LittleEndian(legacy);
        values = reader.ReadVersionedObjectArray<VersionedValue>(2, new StructVersion(1));
        Check(
            values[0].Value == 1 && values[0].LegacyValue == 3 && values[1].Value == 2 && values[1].LegacyValue == 4,
            "Versioned arrays preserve record boundaries when legacy fields are present"
        );
    }

    private struct VersionedValue() : IReadable
    {
        internal int Value;
        internal int LegacyValue = 73;

        public static int Size(in StructVersion version = default, in ReaderConfig config = default) => version.Major == 1 ? 8 : 4;

        public void Read<TReader>(ref Reader<TReader> reader, in StructVersion version = default)
            where TReader : IReader, allows ref struct
        {
            Value = reader.ReadPrimitive<int>();
            if (version.Major == 1)
                LegacyValue = reader.ReadPrimitive<int>();
        }
    }

    private sealed class VersionedReference : IReadable
    {
        internal int Value;
        internal int LegacyValue = 73;

        public static int Size(in StructVersion version = default, in ReaderConfig config = default) => version.Major == 1 ? 8 : 4;

        public void Read<TReader>(ref Reader<TReader> reader, in StructVersion version = default)
            where TReader : IReader, allows ref struct
        {
            Value = reader.ReadPrimitive<int>();
            if (version.Major == 1)
                LegacyValue = reader.ReadPrimitive<int>();
        }
    }
}
