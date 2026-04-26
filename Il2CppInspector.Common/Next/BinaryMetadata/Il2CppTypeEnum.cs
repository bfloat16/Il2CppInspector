// ReSharper disable InconsistentNaming
namespace Il2CppInspector.Next.BinaryMetadata;

public enum Il2CppTypeEnum : byte
{
    Il2CPP_TYPE_END,
    IL2CPP_TYPE_VOID,
    IL2CPP_TYPE_BOOLEAN,
    IL2CPP_TYPE_CHAR,
    IL2CPP_TYPE_I1,
    IL2CPP_TYPE_U1,
    IL2CPP_TYPE_I2,
    IL2CPP_TYPE_U2,
    IL2CPP_TYPE_I4,
    IL2CPP_TYPE_U4,
    IL2CPP_TYPE_I8,
    IL2CPP_TYPE_U8,
    IL2CPP_TYPE_R4,
    IL2CPP_TYPE_R8,
    IL2CPP_TYPE_STRING,
    IL2CPP_TYPE_PTR,
    IL2CPP_TYPE_BYREF,
    IL2CPP_TYPE_VALUETYPE,
    IL2CPP_TYPE_CLASS,
    IL2CPP_TYPE_VAR,
    IL2CPP_TYPE_ARRAY,
    IL2CPP_TYPE_GENERICINST,
    IL2CPP_TYPE_TYPEDBYREF,
    IL2CPP_TYPE_I = 0x18,
    IL2CPP_TYPE_U,
    IL2CPP_TYPE_FNPTR = 0x1b,
    IL2CPP_TYPE_OBJECT,
    IL2CPP_TYPE_SZARRAY,
    IL2CPP_TYPE_MVAR,
    IL2CPP_TYPE_CMOD_REQD,
    IL2CPP_TYPE_CMOD_OPT,
    IL2CPP_TYPE_INTERNAL,

    IL2CPP_TYPE_MODIFIER = 0x40,
    IL2CPP_TYPE_SENTINEL = 0x41,
    IL2CPP_TYPE_PINNED = 0x45,

    IL2CPP_TYPE_ENUM = 0x55,
    IL2CPP_TYPE_IL2CPP_TYPE_INDEX = 0xff,
}

public static class Il2CppTypeEnumExtensions
{
    public static bool IsTypeDefinitionEnum(this Il2CppTypeEnum value) =>
        value
            is Il2CppTypeEnum.IL2CPP_TYPE_VOID
                or Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN
                or Il2CppTypeEnum.IL2CPP_TYPE_CHAR
                or Il2CppTypeEnum.IL2CPP_TYPE_I1
                or Il2CppTypeEnum.IL2CPP_TYPE_U1
                or Il2CppTypeEnum.IL2CPP_TYPE_I2
                or Il2CppTypeEnum.IL2CPP_TYPE_U2
                or Il2CppTypeEnum.IL2CPP_TYPE_I4
                or Il2CppTypeEnum.IL2CPP_TYPE_U4
                or Il2CppTypeEnum.IL2CPP_TYPE_I8
                or Il2CppTypeEnum.IL2CPP_TYPE_U8
                or Il2CppTypeEnum.IL2CPP_TYPE_R4
                or Il2CppTypeEnum.IL2CPP_TYPE_R8
                or Il2CppTypeEnum.IL2CPP_TYPE_STRING
                or Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE
                or Il2CppTypeEnum.IL2CPP_TYPE_CLASS
                or Il2CppTypeEnum.IL2CPP_TYPE_I
                or Il2CppTypeEnum.IL2CPP_TYPE_U
                or Il2CppTypeEnum.IL2CPP_TYPE_OBJECT
                or Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF;

    public static bool IsGenericParameterEnum(this Il2CppTypeEnum value) => value is Il2CppTypeEnum.IL2CPP_TYPE_VAR or Il2CppTypeEnum.IL2CPP_TYPE_MVAR;
}
