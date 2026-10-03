using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Reflection;

namespace Il2CppInspector
{
    internal sealed partial class ZzzMorax
    {
        public override TypeInfo ResolveGenericType(TypeModel model, Il2CppType type)
        {
            var pair = GenericClass(type.Data.Value);
            if (pair.Definition < 0)
            {
                return null;
            }

            var arguments = model.ResolveGenericArguments(model.Package.GenericInstances[pair.Instance]);
            var result = model.TypesByDefinitionIndex[pair.Definition].MakeGenericType(arguments);
            // TypeRefs are still being populated; enums resolve their scalar layout after this pass.
            if (!result.IsEnum && GenericLayoutGroups.TryGetValue(checked((int)type.Data.Value), out var group))
            {
                result.SetGameLayoutGroup(group);
            }

            return result;
        }

        public override int FieldStorageTag(FieldInfo field) =>
            (int)(
                (
                    field.DeclaringType.GameLayoutGroup is int group
                        ? LayoutRawFieldOffset(group, field.Index - (int)field.DeclaringType.GetGenericTypeDefinition().Definition.FieldIndex)
                        : RawFieldOffsets[field.Index]
                ) >> 24
            );
    }
}
