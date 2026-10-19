using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Reflection;

namespace Il2CppInspector
{
    internal sealed partial class Bh3Morax
    {
        public override TypeInfo ResolveGenericType(TypeModel model, Il2CppType type)
        {
            // D §3.3: the generic class table lives in startup-metadata.dat at Slot(0x02C) and the
            // instance is an Il2CppGenericInst index into the binary's array.
            var pair = GenericClass(type.Data.Value);
            if (pair.Definition < 0 || pair.Instance < 0)
            {
                return null;
            }
            if (pair.Definition >= model.TypesByDefinitionIndex.Length || pair.Instance >= model.Package.GenericInstances.Length)
                throw new InvalidDataException("BH3 generic class references an unknown definition or instance.");

            var arguments = model.ResolveGenericArguments(model.Package.GenericInstances[pair.Instance]);
            return model.TypesByDefinitionIndex[pair.Definition].MakeGenericType(arguments);
        }

        public override int FieldStorageTag(FieldInfo field) =>
            (int)(
                (
                    field.DeclaringType.GameLayoutGroup is int group
                        ? LayoutRawFieldOffset(group, field.Index - (int)field.DeclaringType.GetGenericTypeDefinition().Definition.FieldIndex)
                        : rawFieldOffsets[field.Index]
                ) >> 24
            );
    }
}
