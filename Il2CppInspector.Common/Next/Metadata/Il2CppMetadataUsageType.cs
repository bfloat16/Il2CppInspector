namespace Il2CppInspector.Next.Metadata;

public enum Il2CppMetadataUsageType
{
    Invalid = 0b000,
    TypeInfo = 0b001,
    Il2CppType = 0b010,
    MethodDef = 0b011,
    FieldInfo = 0b100,
    StringLiteral = 0b101,
    MethodRef = 0b110,
    FieldRva = 0b111,
}
