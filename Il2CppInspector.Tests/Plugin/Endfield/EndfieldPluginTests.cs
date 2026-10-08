using System.Buffers.Binary;
using Il2CppInspector.Plugin.Endfield;

namespace Il2CppInspector.Tests.Plugin.Endfield
{
    internal static class EndfieldPluginTests
    {
        internal static void Run()
        {
            var plugin = GamePlugins.Get("endfield_cn");
            Check(plugin.GameId == "Endfield_CN" && plugin.GetType().Assembly.GetName().Name == "Il2CppInspector.Plugin.Endfield", "Endfield_CN is discovered and selected case-insensitively");
            Common.Plugins.GamePluginsTests.Run(["Endfield", "Endfield_", "Endfield_CN_bad", "Endfield_CN_1.0", "Endfield_CN_1.0_extra"]);

            var data = CreateMetadata();
            var original = data.ToArray();
            Check(plugin.Matches(data) && GamePlugins.Select(null, data) == plugin, "Endfield v29 metadata is automatically detected");
            var fixedData = EndfieldMetadata.Fix(data);
            Check(data.SequenceEqual(original), "Endfield repair leaves input bytes intact");
            Check(fixedData.Length == data.Length - 8 && fixedData.AsSpan(0x100).SequenceEqual(data.AsSpan(0x108)), "Endfield repair removes eight header bytes and preserves the entire payload");

            using var stream = new MemoryStream(fixedData);
            var metadata = Metadata.FromStream(stream);
            Check(metadata.Header.Version == 29 && metadata.Header.StringLiteralOffset == 0x100, "Repaired Endfield header is accepted by the stock reader");
            Check(metadata.StringLiterals.SequenceEqual(["test"]) && metadata.Strings[0] == "abc", "Stock reader decodes repaired Endfield strings and literal records");
            Check(!metadata.HasGameAdapter && metadata.GamePlugin == null, "Endfield repair uses stock analysis and exports");
            Check(GamePlugins.Select(null, fixedData) == null && !plugin.Matches(fixedData), "Repaired stock metadata is not repaired again");
            Check(Rejects(fixedData), "Endfield repair rejects already repaired metadata");

            VerifySections();
            VerifyTypeDefinitions();
            Check(!plugin.Matches([]) && !plugin.Matches(data.AsSpan(0, 0x107)) && Rejects(data[..0x107]), "Truncated Endfield headers are rejected");
            var invalid = data.ToArray();
            WriteInt(invalid, 0, 0);
            Check(!plugin.Matches(invalid) && Rejects(invalid), "Encrypted or invalid metadata magic is rejected");
            invalid = data.ToArray();
            WriteInt(invalid, 4, 27);
            Check(!plugin.Matches(invalid) && Rejects(invalid), "Unsupported Endfield metadata versions are rejected");

            foreach (var (offset, size) in new[] { (-1, 0), (0, 4), (0x100, 4), (data.Length + 1, 0), (0x108, -1), (0x108, int.MaxValue), (int.MaxValue, int.MaxValue) })
            {
                invalid = data.ToArray();
                WriteSection(invalid, 3, offset, size);
                Check(Rejects(invalid), $"Invalid Endfield section range ({offset}, {size}) is rejected");
            }
        }

        private static void VerifyTypeDefinitions()
        {
            const int typesOffset = 0x118;
            const int imagesOffset = typesOffset + 2 * 92;
            const int exportedOffset = imagesOffset + 40;
            var data = new byte[exportedOffset + 4];
            CreateMetadata().CopyTo(data, 0);
            WriteSection(data, 19, typesOffset, 2 * 92);
            WriteSection(data, 20, imagesOffset, 40);
            WriteSection(data, 30, exportedOffset, 4);
            for (var i = 0; i < 2; i++)
            {
                var offset = typesOffset + i * 92;
                WriteInt(data, offset + 64, 0x12345678 + i);
                WriteInt(data, offset + 68, 3 | (2 << 16));
                WriteInt(data, offset + 72, 7 | (1 << 16));
                WriteInt(data, offset + 76, 6 | (5 << 16));
                WriteInt(data, offset + 80, 4 | (8 << 16));
                WriteInt(data, offset + 84, 0xC00);
                WriteInt(data, offset + 88, 0x02000001 + i);
            }
            WriteInt(data, imagesOffset + 12, 2);
            WriteInt(data, imagesOffset + 28, 1);
            WriteInt(data, exportedOffset, 1);
            var repaired = EndfieldMetadata.Fix(data);
            var normalized = EndfieldMetadata.NormalizeTypeDefinitions(repaired);
            Check(normalized.Length == repaired.Length - 8, "Endfield custom type records shrink from 92 to 88 bytes");
            Check(normalized.AsSpan(normalized.Length - 44).SequenceEqual(repaired.AsSpan(repaired.Length - 44)), "Endfield type normalization preserves following tables");
            Check(
                ReadInt(normalized, 8 + 20 * 8) == imagesOffset - 16 && ReadInt(normalized, 8 + 30 * 8) == exportedOffset - 16,
                "Endfield type normalization adjusts image and exported-type offsets"
            );
            Check(ReferenceEquals(normalized, EndfieldMetadata.NormalizeTypeDefinitions(normalized)), "Stock-sized Endfield type records are not normalized twice");
            using var stream = new MemoryStream(normalized);
            var metadata = Metadata.FromStream(stream);
            Check(metadata.Types.Length == 2 && metadata.Images[0].TypeCount == 2, "Endfield type and image counts agree after normalization");
            for (var i = 0; i < 2; i++)
            {
                var type = metadata.Types[i];
                Check(
                    type.MethodCount == 3
                        && type.PropertyCount == 2
                        && type.FieldCount == 7
                        && type.EventCount == 1
                        && type.NestedTypeCount == 6
                        && type.VTableCount == 5
                        && type.InterfacesCount == 4
                        && type.InterfaceOffsetsCount == 8
                        && type.Token == 0x02000001 + i,
                    "Endfield type normalization preserves member counts and tokens: " + i
                );
            }

            WriteInt(repaired, imagesOffset - 8 + 12, 3);
            var rejected = false;
            try
            {
                EndfieldMetadata.NormalizeTypeDefinitions(repaired);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Check(rejected, "Endfield type normalization rejects inconsistent image ranges");
        }

        private static void VerifySections()
        {
            var data = new byte[0x208];
            WriteInt(data, 0, unchecked((int)0xFAB11BAF));
            WriteInt(data, 4, 29);
            for (var i = 0; i < 31; i++)
                WriteSection(data, i, 0x108 + i * 4, 4);
            WriteSection(data, 7, 0, 0);
            WriteSection(data, 28, int.MaxValue, -1);
            WriteSection(data, 29, -1, int.MaxValue);
            var fixedData = EndfieldMetadata.Fix(data);
            for (var i = 0; i < 31; i++)
            {
                var expectedOffset = i switch
                {
                    7 or 29 => 0,
                    28 => 0x17c,
                    _ => 0x100 + i * 4,
                };
                var expectedSize = i switch
                {
                    7 or 29 => 0,
                    28 => 0x84,
                    _ => 4,
                };
                Check(ReadInt(fixedData, 8 + i * 8) == expectedOffset && ReadInt(fixedData, 12 + i * 8) == expectedSize, $"Endfield section {i} is repaired correctly");
            }
        }

        private static byte[] CreateMetadata()
        {
            var data = new byte[0x118];
            WriteInt(data, 0, unchecked((int)0xFAB11BAF));
            WriteInt(data, 4, 29);
            WriteSection(data, 0, 0x108, 8);
            WriteSection(data, 1, 0x110, 4);
            WriteSection(data, 2, 0x114, 4);
            WriteSection(data, 30, 0x118, 0);
            data.AsSpan(0x100, 8).Fill(0xCC);
            WriteInt(data, 0x108, 4);
            "testabc\0"u8.CopyTo(data.AsSpan(0x110));
            return data;
        }

        private static bool Rejects(byte[] data)
        {
            try
            {
                EndfieldMetadata.Fix(data);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        private static void WriteSection(byte[] data, int index, int offset, int size)
        {
            WriteInt(data, 8 + index * 8, offset);
            WriteInt(data, 12 + index * 8, size);
        }

        private static void WriteInt(byte[] data, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);

        private static int ReadInt(byte[] data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
    }
}
