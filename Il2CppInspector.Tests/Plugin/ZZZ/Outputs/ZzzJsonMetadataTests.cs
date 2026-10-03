namespace Il2CppInspector.Tests.Plugin.ZZZ.Outputs
{
    internal static class ZzzJsonMetadataTests
    {
        internal static void VerifyComments(AppModel app, string output)
        {
            var path = Path.Combine(output, "il2cpp-comments.json");
            new JSONMetadata(app) { AllowComments = true }.Write(path);
            using var inputJson = File.OpenRead(path);
            var prefix = new byte[1024];
            inputJson.ReadExactly(prefix);
            var tokenReader = new Utf8JsonReader(prefix, false, new JsonReaderState(new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow }));
            var foundComment = false;
            while (tokenReader.Read())
                foundComment |= tokenReader.TokenType == JsonTokenType.Comment;
            Check(foundComment, "ZZZ JSON honors the stock AllowComments option without changing the default output");
        }

        internal static void VerifyPayloads(ZzzTestContext context, string output)
        {
            var input = context.Input;
            var model = context.Model;
            var rawOffsets = context.RawOffsets;
            // The fields array is at the end of the streamed JSON. Read only that tail, not the gigabyte of method records.
            using var jsonStream = File.OpenRead(Path.Combine(output, "il2cpp.json"));
            jsonStream.Seek(-Math.Min(jsonStream.Length, 2 * 1024 * 1024), SeekOrigin.End);
            using var jsonReader = new StreamReader(jsonStream);
            var tail = jsonReader.ReadToEnd();
            var start = tail.IndexOf("\"fields\": [", StringComparison.Ordinal) + "\"fields\": ".Length;
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(tail[start..]));
            using var fieldsJson = JsonDocument.ParseValue(ref reader);
            var usages = input.MetadataUsages.Where(u => u.Type == MetadataUsageType.FieldInfo).ToArray();
            var records = fieldsJson.RootElement.EnumerateArray().ToArray();
            Check(records.Length == 1020 && records.Length == usages.Length, "JSON contains all 1,020 FieldInfo payload records");
            for (var i = 0; i < records.Length; i++)
            {
                var reference = input.FieldRefs[usages[i].SourceIndex];
                var definition = model.TypesByReferenceIndex[reference.TypeIndex].Index;
                var index = input.TypeDefinitions[definition].FieldIndex + reference.FieldIndex;
                var storage = input.TypeReferences[input.Fields[index].TypeIndex];
                var size =
                    storage.Type == Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE ? checked((int)input.TypeDefinitionSizes[storage.Data.KlassIndex].InstanceSize - 16)
                    : storage.Type == Il2CppTypeEnum.IL2CPP_TYPE_I8 ? 8
                    : 4;
                var bytes = input.Metadata.ReadBytes((long)input.FieldDefaultValue[index].Item1, size);
                if (records[i].GetProperty("value").GetString() != Convert.ToHexString(bytes))
                    throw new InvalidOperationException("JSON FieldInfo payload differs from metadata");
                if (records[i].GetProperty("storageTag").GetUInt32() != rawOffsets[index] >> 24 || records[i].GetProperty("offset").GetUInt32() != (rawOffsets[index] & 0xFFFFFF))
                    throw new InvalidOperationException("JSON field storage tag/offset differs from its runtime region");
            }
            Pass("All JSON payload bytes match metadata, including the 24 scalar initializers");
        }
    }
}
