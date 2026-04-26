using VersionedSerialization.Generator.Utils;

namespace VersionedSerialization.Generator.Models;

public sealed record PropertySerializationInfo(string Name, string ReadMethod, string SizeExpression, PropertyType Type, ImmutableEquatableArray<VersionCondition> VersionConditions);
