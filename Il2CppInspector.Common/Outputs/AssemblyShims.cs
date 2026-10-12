/*
    Copyright 2017-2020 Perfare - https://github.com/Perfare/Il2CppDumper
    Copyright 2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

    All rights reserved.
*/

using System.Diagnostics;
using System.Runtime.CompilerServices;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using Il2CppInspector.Next;
using Il2CppInspector.Next.Metadata;
using Il2CppInspector.Reflection;
using AssemblyNameFlags = System.Reflection.AssemblyNameFlags;

#nullable enable annotations

namespace Il2CppInspector.Outputs
{
    public static class dnlibExtensions
    {
        private static readonly ConditionalWeakTable<ModuleDef, Dictionary<TypeDef, MemberRef>> attributeConstructors = new();
        private static readonly ConditionalWeakTable<ModuleDef, AnnotationBuffer> annotationBuffers = new();
        private static readonly ConditionalWeakTable<ModuleDef, Dictionary<(TypeDef Type, string Offset), CustomAttribute>> fieldOffsets = new();

        internal static void ReleaseAnnotationCache(ModuleDef module)
        {
            fieldOffsets.Remove(module);
            annotationBuffers.Remove(module);
        }

        private sealed class AnnotationBuffer
        {
            internal readonly CustomAttribute Attribute = new((ICustomAttributeType)null);
            internal readonly List<CANamedArgument> Arguments = [];
        }
        private static readonly Dictionary<string, UTF8String> attributeArgumentNames = new[]
        {
            "RVA", "Offset", "VA", "Slot", "Spec", "Flags", "ThreadStatic", "StorageTag", "StorageBase", "Data", "Token", "Name",
        }.ToDictionary(name => name, name => (UTF8String)name);

        private sealed class AttributeWriterHelper : ICustomAttributeWriterHelper
        {
            internal static readonly AttributeWriterHelper Instance = new();
            public bool MustUseAssemblyName(IType type) => true;
            public void Error(string message) => throw new InvalidOperationException(message);
        }

        // Add a default parameterless constructor that calls a specified base constructor
        public static MethodDef AddDefaultConstructor(this TypeDef type, IMethod @base)
        {
            var ctor = new MethodDefUser(
                ".ctor",
                MethodSig.CreateInstance(type.Module.CorLibTypes.Void),
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName
            );

            var ctorBody = new CilBody();
            ctorBody.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
            ctorBody.Instructions.Add(OpCodes.Call.ToInstruction(@base));
            ctorBody.Instructions.Add(OpCodes.Ret.ToInstruction());
            ctor.Body = ctorBody;

            type.Methods.Add(ctor);
            return ctor;
        }

        // Add custom attribute to item with named property arguments
        // 'module' is the module that owns 'type'; type.Module may still be null when this is called
        public static CustomAttribute AddAttribute(this IHasCustomAttribute def, ModuleDef module, TypeDef attrTypeDef, params (string prop, object value)[] args)
        {
            // If SuppressMetadata is set, our own attributes will never be generated so attrTypeDef will be null
            if (attrTypeDef == null)
                return null;

            Dictionary<(TypeDef Type, string Offset), CustomAttribute> offsets = null;
            (TypeDef Type, string Offset) offsetKey = default;
            if (attrTypeDef.Name == "FieldOffsetAttribute" && args is [{ prop: "Offset", value: string offset }])
            {
                offsets = fieldOffsets.GetOrCreateValue(module);
                offsetKey = (attrTypeDef, offset);
                if (offsets.TryGetValue(offsetKey, out var known))
                {
                    def.CustomAttributes.Add(known);
                    return known;
                }
            }

            var constructors = attributeConstructors.GetOrCreateValue(module);
            if (!constructors.TryGetValue(attrTypeDef, out var attCtorRef))
            {
                var attRef = module.Import(attrTypeDef);
                attCtorRef = new MemberRefUser(module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void), attRef);
                constructors.Add(attrTypeDef, attCtorRef);
            }

            var buffer = annotationBuffers.GetOrCreateValue(module);
            var scratch = buffer.Attribute;
            scratch.Constructor = attCtorRef;
            scratch.NamedArguments.Clear();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = ToArgument(args[i].value);
                var name = attributeArgumentNames.TryGetValue(args[i].prop, out var known) ? known : (UTF8String)args[i].prop;
                if (i == buffer.Arguments.Count)
                    buffer.Arguments.Add(new CANamedArgument());
                var named = buffer.Arguments[i];
                named.IsField = true;
                named.Type = arg.Type;
                named.Name = name;
                named.Argument = arg;
                scratch.NamedArguments.Add(named);
            }

            // dnlib snapshots the values; the reusable argument graph never enters the output model.
            var attr = new CustomAttribute(attCtorRef, CustomAttributeWriter.Write(AttributeWriterHelper.Instance, scratch));
            if (offsets?.Count < 4096)
                offsets.Add(offsetKey, attr);

            def.CustomAttributes.Add(attr);
            return attr;

            CAArgument ToArgument(object value)
            {
                return value switch
                {
                    string str => new CAArgument(module.CorLibTypes.String, str),
                    int i => new CAArgument(module.CorLibTypes.Int32, i),
                    bool b => new CAArgument(module.CorLibTypes.Boolean, b),
                    _ => throw new UnreachableException(),
                };
            }
        }
    }

    // Output module to create .NET DLLs containing type definitions
    public class AssemblyShims
    {
        // Suppress informational attributes
        public bool SuppressMetadata { get; set; }

        // .NET type model
        private readonly TypeModel model;

        // Our custom attributes
        private TypeDef addressAttribute;
        private TypeDef genericInstAddressAttribute;
        private TypeDef assemblyFlagsAttribute;
        private Dictionary<int, (ulong Address, int Spec)> genericAddresses = [];
        private TypeDef fieldOffsetAttribute;
        private TypeDef staticFieldOffsetAttribute;
        private TypeDef attributeAttribute;
        private TypeDef metadataOffsetAttribute;
        private TypeDef metadataPreviewAttribute;
        private TypeDef tokenAttribute;

        // Runtime attributes we need to add
        private TypeInfo? inlineArrayAttribute;

        // The namespace for our custom types
        private const string rootNamespace = "Il2CppInspector.DLL";

        // All modules (single-module assemblies)
        private Dictionary<Assembly, ModuleDef> modules = [];
        private Dictionary<ModuleDef, Dictionary<TypeInfo, TypeDefUser>> types = [];
        private readonly Dictionary<ModuleDef, Dictionary<TypeInfo, TypeSig>> signatureCache = [];
        private readonly Dictionary<ModuleDef, Dictionary<TypeSig, CilBody>> defaultBodies = [];

        // Custom attributes we will apply directly instead of with a custom attribute function pointer
        private Dictionary<TypeInfo, TypeDef> directApplyAttributes;

        public AssemblyShims(TypeModel model) => this.model = model;

        // Generate base DLL with our custom types
        private ModuleDef CreateBaseAssembly()
        {
            // Create DLL with our custom types
            var module = CreateAssembly("Il2CppInspector.dll");

            // Import our IL2CPP application's copy of System.Attribute
            // to avoid introducing a dependency on System.Private.CoreLib (.NET Core) from Il2CppInspector itself
            var attributeType = model.TypesByFullName["System.Attribute"];
            var attributeCtor = attributeType.DeclaredConstructors.First(c => !c.IsPublic && !c.IsStatic);
            var attributeTypeRef = GetTypeRef(module, attributeType);
            var attributeCtorRef = new MemberRefUser(attributeTypeRef.Module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void), attributeTypeRef);

            var stringField = new FieldSig(module.CorLibTypes.String);
            var boolField = new FieldSig(module.CorLibTypes.Boolean);

            // Create a type deriving from System.Attribute and add it to the assembly
            TypeDefUser createAttribute(string name)
            {
                var attribute = new TypeDefUser(rootNamespace, name, attributeTypeRef);
                attribute.Attributes = TypeAttributes.Public | TypeAttributes.BeforeFieldInit;
                module.Types.Add(attribute);
                return attribute;
            }

            // Create our custom attributes for compatibility with Il2CppDumper
            // TODO: New format with numeric values where applicable
            addressAttribute = createAttribute("AddressAttribute");
            addressAttribute.Fields.Add(new FieldDefUser("RVA", stringField, FieldAttributes.Public));
            addressAttribute.Fields.Add(new FieldDefUser("Offset", stringField, FieldAttributes.Public));
            addressAttribute.Fields.Add(new FieldDefUser("VA", stringField, FieldAttributes.Public));
            addressAttribute.Fields.Add(new FieldDefUser("Slot", stringField, FieldAttributes.Public));
            addressAttribute.AddDefaultConstructor(attributeCtorRef);

            if (model.Package.Metadata.HasGameAdapter)
            {
                genericInstAddressAttribute = createAttribute("GenericInstAddressAttribute");
                foreach (var field in new[] { "RVA", "Offset", "VA", "Spec" })
                    genericInstAddressAttribute.Fields.Add(new FieldDefUser(field, stringField, FieldAttributes.Public));
                genericInstAddressAttribute.AddDefaultConstructor(attributeCtorRef);
            }

            fieldOffsetAttribute = createAttribute("FieldOffsetAttribute");
            fieldOffsetAttribute.Fields.Add(new FieldDefUser("Offset", stringField, FieldAttributes.Public));
            fieldOffsetAttribute.AddDefaultConstructor(attributeCtorRef);

            staticFieldOffsetAttribute = createAttribute("StaticFieldOffsetAttribute");
            staticFieldOffsetAttribute.Fields.Add(new FieldDefUser("Offset", stringField, FieldAttributes.Public));
            staticFieldOffsetAttribute.Fields.Add(new FieldDefUser("ThreadStatic", boolField, FieldAttributes.Public));
            if (model.Package.Metadata.HasGameAdapter)
            {
                staticFieldOffsetAttribute.Fields.Add(new FieldDefUser("StorageTag", stringField, FieldAttributes.Public));
                staticFieldOffsetAttribute.Fields.Add(new FieldDefUser("StorageBase", stringField, FieldAttributes.Public));
                assemblyFlagsAttribute = createAttribute("AssemblyFlagsAttribute");
                assemblyFlagsAttribute.Fields.Add(new FieldDefUser("Flags", stringField, FieldAttributes.Public));
                assemblyFlagsAttribute.AddDefaultConstructor(attributeCtorRef);
            }
            staticFieldOffsetAttribute.AddDefaultConstructor(attributeCtorRef);

            attributeAttribute = createAttribute("AttributeAttribute");
            attributeAttribute.Fields.Add(new FieldDefUser("Name", stringField, FieldAttributes.Public));
            attributeAttribute.Fields.Add(new FieldDefUser("RVA", stringField, FieldAttributes.Public));
            attributeAttribute.Fields.Add(new FieldDefUser("Offset", stringField, FieldAttributes.Public));
            attributeAttribute.AddDefaultConstructor(attributeCtorRef);

            metadataOffsetAttribute = createAttribute("MetadataOffsetAttribute");
            metadataOffsetAttribute.Fields.Add(new FieldDefUser("Offset", stringField, FieldAttributes.Public));
            metadataOffsetAttribute.AddDefaultConstructor(attributeCtorRef);

            metadataPreviewAttribute = createAttribute("MetadataPreviewAttribute");
            metadataPreviewAttribute.Fields.Add(new FieldDefUser("Data", stringField, FieldAttributes.Public));
            metadataPreviewAttribute.AddDefaultConstructor(attributeCtorRef);

            tokenAttribute = createAttribute("TokenAttribute");
            tokenAttribute.Fields.Add(new FieldDefUser("Token", stringField, FieldAttributes.Public));
            tokenAttribute.AddDefaultConstructor(attributeCtorRef);

            return module;
        }

        private void GetBuiltinAttributes()
        {
            if (model.Package.Version >= MetadataVersions.V1040)
            {
                inlineArrayAttribute = model.TypesByFullName.GetValueOrDefault("System.Runtime.CompilerServices.InlineArrayAttribute");
            }
        }

        // Create a new DLL assembly definition
        private ModuleDefUser NewModule(string name)
        {
            // dnlib defaults to mscorlib 2.0; bind to the actual decoded application's core library.
            var core = model.TypesByFullName["System.Object"].Assembly.AssemblyDefinition.Aname;
            var reference = new AssemblyRefUser(CreateAssembly(core));
            return new ModuleDefUser(name, null, reference) { Kind = ModuleKind.Dll };
        }

        private ModuleDefUser CreateAssembly(string name)
        {
            // Create module
            var module = NewModule(name);

            // Create assembly
            var ourVersion = System.Reflection.Assembly.GetAssembly(typeof(Il2CppInspector)).GetName().Version;
            var asm = new AssemblyDefUser(name.Replace(".dll", ""), ourVersion);

            // Add module to assembly
            asm.Modules.Add(module);
            return module;
        }

        private AssemblyDefUser CreateAssembly(Il2CppAssemblyNameDefinition nameDefinition)
        {
            var name = model.Package.Strings[nameDefinition.NameIndex];

            Version version;
            if (nameDefinition.Major != -1 && nameDefinition.Minor != -1 && nameDefinition.Build != -1 && nameDefinition.Revision != -1)
            {
                version = new Version(nameDefinition.Major, nameDefinition.Minor, nameDefinition.Build, nameDefinition.Revision);
            }
            else
            {
                version = new Version(0, 0, 0, 0);
            }

            PublicKey? publicKey = null;
            var hasPublicKey = !model.Package.Metadata.HasGameAdapter && nameDefinition.Flags.HasFlag(AssemblyNameFlags.PublicKey);
            if (hasPublicKey)
            {
                publicKey = new PublicKey(model.Package.AssemblyPublicKeys[nameDefinition.PublicKeyIndex]);
            }

            return new AssemblyDefUser(name, version)
            {
                PublicKey = publicKey,
                Culture = model.Package.Strings[nameDefinition.CultureIndex],
                HashAlgorithm = (AssemblyHashAlgorithm)nameDefinition.HashAlg,
                HasPublicKey = hasPublicKey,
            };
        }

        private ModuleDefUser CreateAssembly(Assembly assembly)
        {
            var module = NewModule(assembly.ShortName);

            var asm = CreateAssembly(assembly.AssemblyDefinition.Aname);
            asm.Modules.Add(module);

            return module;
        }

        // Create a shallow type definition that only populates the type itself and its nested types.
        // Used for custom attributes.
        private TypeDefUser CreateTypeShallow(ModuleDef module, TypeInfo type)
        {
            // Initialize with base class
            string ns = type.IsNested ? null : type.Namespace;
            var mType = new TypeDefUser(ns, type.BaseName, GetTypeRef(module, type.BaseType)) { Attributes = (TypeAttributes)type.Attributes };

            if (mType.IsExplicitLayout || mType.IsSequentialLayout)
            {
                var packing = (int)type.Definition.Bitfield.PackingSize;
                var size = model.Package.Metadata.HasGameAdapter ? Math.Max(0, (long)type.Sizes.InstanceSize - 16) : type.Sizes.NativeSize;
                mType.ClassLayout = new ClassLayoutUser((ushort)(type.Definition.Bitfield.DefaultPackingSize || packing == 0 ? 0 : 1 << (packing - 1)), (uint)size);
            }

            // Add nested types
            foreach (var nestedType in type.DeclaredNestedTypes)
                mType.NestedTypes.Add(CreateTypeShallow(module, nestedType));

            if (!types.TryGetValue(module, out var moduleTypes))
                types.Add(module, moduleTypes = []);
            moduleTypes.Add(type, mType);

            // Add to attribute apply list if we're looking for it
            if (directApplyAttributes.ContainsKey(type))
                directApplyAttributes[type] = mType;

            return mType;
        }

        // Populate shallow type definition with all members, events, etc.
        // Type definition is done in a two-stage process so that attributes can reference the type beforehand
        private TypeDefUser PopulateType(ModuleDef module, TypeDefUser mType, TypeInfo type)
        {
            // Generic parameters
            foreach (var gp in type.GenericTypeParameters)
            {
                var p = new GenericParamUser((ushort)gp.GenericParameterPosition, (GenericParamAttributes)gp.GenericParameterAttributes, gp.Name);

                // Generic constraints (types and interfaces)
                foreach (var c in gp.GetGenericParameterConstraints())
                    p.GenericParamConstraints.Add(new GenericParamConstraintUser(GetTypeRef(module, c)));

                mType.GenericParameters.Add(p);
            }

            // Interfaces
            foreach (var @interface in type.ImplementedInterfaces)
                mType.Interfaces.Add(new InterfaceImplUser(GetTypeRef(module, @interface)));

            // Add fields
            foreach (var field in type.DeclaredFields)
                AddField(module, mType, field);

            // Add properties
            foreach (var prop in type.DeclaredProperties)
                AddProperty(module, mType, prop);

            // Add events
            foreach (var evt in type.DeclaredEvents)
                AddEvent(module, mType, evt);

            // Add methods that aren't properties or events
            var props = type.DeclaredProperties.SelectMany(p => new[] { p.GetMethod, p.SetMethod }).Where(m => m != null);
            var events = type.DeclaredEvents.SelectMany(p => new[] { p.AddMethod, p.RemoveMethod, p.RaiseMethod }).Where(m => m != null);

            foreach (var method in type.DeclaredConstructors.AsEnumerable<MethodBase>().Concat(type.DeclaredMethods).Except(props).Except(events))
                AddMethod(module, mType, method);

            // Add token attribute
            if (type.Definition.IsValid)
                mType.AddAttribute(module, tokenAttribute, ("Token", $"0x{type.MetadataToken:X8}"));

            if (model.Package.Version >= MetadataVersions.V1040)
            {
                // L-TODO: Verify if we actually need to add the attribute ourselves; it might just be preserved
                if (type.HasInlineArray && inlineArrayAttribute != null)
                {
                    var typeRef = GetTypeRef(module, inlineArrayAttribute);
                    var ctorRef = new MemberRefUser(typeRef.Module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Int32), typeRef);
                    mType.CustomAttributes.Add(new CustomAttribute(ctorRef, new CAArgument[] { new(module.CorLibTypes.Int32, type.InlineArrayLength) }));
                }
            }

            // Add custom attribute attributes
            foreach (var ca in type.CustomAttributes)
                AddCustomAttribute(module, mType, ca);

            return mType;
        }

        // Add a field to a type
        private FieldDef AddField(ModuleDef module, TypeDef mType, FieldInfo field)
        {
            var s = new FieldSig(GetTypeSig(module, field.FieldType));

            var mField = new FieldDefUser(field.Name, s, (FieldAttributes)field.Attributes);

            // Default value
            if (field.HasDefaultValue)
                mField.Constant = new ConstantUser(field.DefaultValue);
            // Add offset attribute if no default value but metadata present
            else if (field.HasFieldRVA || field.IsLiteral)
                mField.AddAttribute(module, metadataOffsetAttribute, ("Offset", $"0x{field.DefaultValueMetadataAddress:X8}"));

            // Static array initializer preview
            if (field.HasFieldRVA)
            {
                // Attempt to get field size

                var fieldSize = model.Package.Metadata.HasGameAdapter
                    ? model.Package.Metadata.GameAdapter.FieldRvaSize(model.Package.TypeReferences[model.Package.Fields[field.Index].TypeIndex], model.Package.Binary)
                    : field.FieldType.Sizes.NativeSize;
                var preview = model.Package.Metadata.ReadBytes((long)field.DefaultValueMetadataAddress, fieldSize);

                mField.InitialValue = preview;
                mField.AddAttribute(module, metadataPreviewAttribute, ("Data", Convert.ToHexString(preview)));
            }

            // Field offset
            if (!field.IsStatic)
                mField.AddAttribute(module, fieldOffsetAttribute, ("Offset", $"0x{field.Offset:X2}"));
            else if (!field.IsLiteral)
            {
                var args = new List<(string, object)> { ("ThreadStatic", field.IsThreadStatic), ("Offset", $"0x{field.Offset:X2}") };
                if (model.Package.Metadata.HasGameAdapter)
                {
                    args.Add(("StorageTag", field.StorageTag.ToString()));
                    args.Add(("StorageBase", model.Package.Metadata.GameAdapter.StorageBase((uint)field.StorageTag)));
                }
                mField.AddAttribute(module, staticFieldOffsetAttribute, args.ToArray());
            }

            // Add token attribute
            mField.AddAttribute(module, tokenAttribute, ("Token", $"0x{field.MetadataToken:X8}"));

            // Add custom attribute attributes
            foreach (var ca in field.CustomAttributes)
                AddCustomAttribute(module, mField, ca);

            if (mType.IsExplicitLayout && !field.IsStatic)
                mField.FieldOffset = (uint)field.Offset;
            mType.Fields.Add(mField);
            return mField;
        }

        // Add a property to a type
        private PropertyDef AddProperty(ModuleDef module, TypeDef mType, PropertyInfo prop)
        {
            PropertySig s;

            // Example: ZstdSharp MEM_32Bit which gets inlined using weaving and all accessors removed
            if (prop.GetMethod == null && prop.SetMethod == null)
                return null;

            // Static or instance
            if (prop.GetMethod?.IsStatic ?? prop.SetMethod.IsStatic)
                s = PropertySig.CreateStatic(GetTypeSig(module, prop.PropertyType));
            else
                s = PropertySig.CreateInstance(GetTypeSig(module, prop.PropertyType));

            var mProp = new PropertyDefUser(prop.Name, s, (PropertyAttributes)prop.Attributes);

            mProp.GetMethod = AddMethod(module, mType, prop.GetMethod);
            mProp.SetMethod = AddMethod(module, mType, prop.SetMethod);

            // Add token attribute
            // Generic properties and constructed properties (from disperate get/set methods) have no definition
            if (prop.Definition.IsValid)
                mProp.AddAttribute(module, tokenAttribute, ("Token", $"0x{prop.MetadataToken:X8}"));

            // Add custom attribute attributes
            foreach (var ca in prop.CustomAttributes)
                AddCustomAttribute(module, mProp, ca);

            // Add property to type
            mType.Properties.Add(mProp);
            return mProp;
        }

        // Add an event to a type
        private EventDef AddEvent(ModuleDef module, TypeDef mType, EventInfo evt)
        {
            var mEvent = new EventDefUser(evt.Name, GetTypeRef(module, evt.EventHandlerType), (EventAttributes)evt.Attributes);

            mEvent.AddMethod = AddMethod(module, mType, evt.AddMethod);
            mEvent.RemoveMethod = AddMethod(module, mType, evt.RemoveMethod);
            mEvent.InvokeMethod = AddMethod(module, mType, evt.RaiseMethod);

            // Add token attribute
            mEvent.AddAttribute(module, tokenAttribute, ("Token", $"0x{evt.MetadataToken:X8}"));

            // Add custom attribute attributes
            foreach (var ca in evt.CustomAttributes)
                AddCustomAttribute(module, mEvent, ca);

            // Add property to type
            mType.Events.Add(mEvent);
            return mEvent;
        }

        // Add a method to a type
        private MethodDef AddMethod(ModuleDef module, TypeDef mType, MethodBase method)
        {
            // Undefined method
            if (method == null)
                return null;

            // Return type and parameter signature
            var s = GetMethodSig(module, method);

            // Definition
            var mMethod = new MethodDefUser(method.Name, s, (MethodImplAttributes)method.MethodImplementationFlags, (MethodAttributes)method.Attributes);

            // Generic type parameters
            foreach (var gp in method.GetGenericArguments())
            {
                var p = new GenericParamUser((ushort)gp.GenericParameterPosition, (GenericParamAttributes)gp.GenericParameterAttributes, gp.Name);

                // Generic constraints (types and interfaces)
                foreach (var c in gp.GetGenericParameterConstraints())
                    p.GenericParamConstraints.Add(new GenericParamConstraintUser(GetTypeRef(module, c)));

                mMethod.GenericParameters.Add(p);
            }

            // Parameter names and default values
            foreach (var param in method.DeclaredParameters)
            {
                var p = new ParamDefUser(param.Name, (ushort)(param.Position + 1), (ParamAttributes)param.Attributes);

                if (param.HasDefaultValue)
                    p.Constant = new ConstantUser(param.DefaultValue);

                // Add offset attribute if metadata present
                if (param.DefaultValueMetadataAddress != 0)
                    p.AddAttribute(module, metadataOffsetAttribute, ("Offset", $"0x{param.DefaultValueMetadataAddress:X8}"));

                // Add custom attribute attributes
                foreach (var ca in param.CustomAttributes)
                    AddCustomAttribute(module, p, ca);

                mMethod.ParamDefs.Add(p);
            }

            if (method is MethodInfo methodInfo && method.Definition.ReturnParameterToken != 0)
            {
                mMethod.Parameters.ReturnParameter.CreateParamDef();
                var returnParam = mMethod.Parameters.ReturnParameter.ParamDef;

                foreach (var ca in methodInfo.ReturnParameter.CustomAttributes)
                    AddCustomAttribute(module, returnParam, ca);
            }

            // Everything that's not extern, abstract or a delegate type should have a method body
            if ((method.Attributes & System.Reflection.MethodAttributes.PinvokeImpl) == 0 && method.DeclaringType.BaseType?.FullName != "System.MulticastDelegate" && !method.IsAbstract)
            {
                mMethod.Body = GetDefaultBody(module, mMethod.ReturnType, mMethod.ReturnType.IsValueType || method is MethodInfo { ReturnType.IsEnum: true });
            }

            // Add token attribute
            mMethod.AddAttribute(module, tokenAttribute, ("Token", $"0x{method.MetadataToken:X8}"));

            // Add method pointer attribute
            if (method.VirtualAddress.HasValue)
            {
                var args = new List<(string, object)>
                {
                    ("RVA", (method.VirtualAddress.Value.Start - model.Package.BinaryImage.ImageBase).ToAddressString()),
                    ("Offset", $"0x{model.Package.BinaryImage.MapVATR(method.VirtualAddress.Value.Start):X}"),
                    ("VA", method.VirtualAddress.Value.Start.ToAddressString()),
                };
                if (method.Definition.Slot != ushort.MaxValue)
                    args.Add(("Slot", method.Definition.Slot.ToString()));

                mMethod.AddAttribute(module, addressAttribute, args.ToArray());
            }
            else if (genericAddresses.TryGetValue(method.Index, out var generic))
                mMethod.AddAttribute(
                    module,
                    genericInstAddressAttribute,
                    ("RVA", $"0x{generic.Address - model.Package.BinaryImage.ImageBase:X}"),
                    ("Offset", $"0x{model.Package.BinaryImage.MapVATR(generic.Address):X}"),
                    ("VA", $"0x{generic.Address:X}"),
                    ("Spec", generic.Spec.ToString())
                );

            // Add custom attribute attributes
            foreach (var ca in method.CustomAttributes)
                AddCustomAttribute(module, mMethod, ca);

            // Add method to type
            mType.Methods.Add(mMethod);
            method.ReleaseTransientParameters();
            return mMethod;
        }

        private CilBody GetDefaultBody(ModuleDef module, TypeSig returnType, bool valueType)
        {
            var isVoid = returnType.ElementType == ElementType.Void;
            var key = isVoid || valueType ? returnType : module.CorLibTypes.Object;
            if (!defaultBodies.TryGetValue(module, out var bodies))
                defaultBodies.Add(module, bodies = []);
            if (bodies.TryGetValue(key, out var known))
                return known;

            // 存根不包含特定于方法的参数、分支或调试信息。
            var body = new CilBody();
            if (!isVoid)
            {
                if (valueType)
                {
                    var result = new Local(returnType);
                    body.Variables.Add(result);
                    body.Instructions.Add(OpCodes.Ldloca_S.ToInstruction(result));
                    body.Instructions.Add(OpCodes.Initobj.ToInstruction(returnType.ToTypeDefOrRef()));
                    body.Instructions.Add(OpCodes.Ldloc_0.ToInstruction());
                }
                else
                    body.Instructions.Add(OpCodes.Ldnull.ToInstruction());
            }
            body.Instructions.Add(OpCodes.Ret.ToInstruction());
            bodies.Add(key, body);
            return body;
        }

        private MethodSig GetMethodSig(ModuleDef module, MethodBase method)
        {
            if (method.IsStatic)
                return MethodSig.CreateStatic(
                    method is MethodInfo mi ? GetTypeSig(module, mi.ReturnType) : module.CorLibTypes.Void,
                    method.DeclaredParameters.Select(p => GetTypeSig(module, p.ParameterType)).ToArray()
                );
            else
                return MethodSig.CreateInstance(
                    method is MethodInfo mi ? GetTypeSig(module, mi.ReturnType) : module.CorLibTypes.Void,
                    method.DeclaredParameters.Select(p => GetTypeSig(module, p.ParameterType)).ToArray()
                );
        }

        // Add a custom attributes attribute to an item, or the attribute itself if it is in our direct apply list
        private CustomAttribute AddCustomAttribute(ModuleDef module, IHasCustomAttribute def, CustomAttributeData ca)
        {
            if (directApplyAttributes.TryGetValue(ca.AttributeType, out var attrDef) && attrDef != null)
                return AddAttribute(def, module, attrDef, ca);

            return def.AddAttribute(
                module,
                attributeAttribute,
                ("Name", ca.AttributeType.Name),
                ("RVA", (ca.VirtualAddress.Start - model.Package.BinaryImage.ImageBase).ToAddressString()),
                ("Offset", $"0x{model.Package.BinaryImage.MapVATR(ca.VirtualAddress.Start):X}")
            );
        }

        private CustomAttribute AddAttribute(IHasCustomAttribute def, ModuleDef module, TypeDef attrTypeDef, CustomAttributeData cad)
        {
            if (cad.CtorInfo == null)
                return def.AddAttribute(module, attrTypeDef);

            var ctorInfo = cad.CtorInfo;

            var attRef = module.Import(attrTypeDef);
            var attCtor = GetMethodSig(module, ctorInfo.Ctor);
            var attCtorRef = new MemberRefUser(attrTypeDef.Module, ".ctor", attCtor, attRef);

            var attr = new CustomAttribute(attCtorRef);

            foreach (var argument in ctorInfo.Arguments)
                attr.ConstructorArguments.Add(GetArgument(argument));

            foreach (var field in ctorInfo.Fields)
                attr.NamedArguments.Add(new CANamedArgument(true, GetTypeSig(module, field.Field.FieldType), field.Field.CSharpName, GetArgument(field)));

            foreach (var property in ctorInfo.Properties)
                attr.NamedArguments.Add(new CANamedArgument(false, GetTypeSig(module, property.Property.PropertyType), property.Property.CSharpName, GetArgument(property)));

            def.CustomAttributes.Add(attr);

            return attr;

            CAArgument GetArgument(CustomAttributeArgument argument)
            {
                var typeSig = GetTypeSig(module, argument.Type);

                switch (argument.Value)
                {
                    case TypeInfo info:
                        var sig = GetTypeSig(module, info);
                        return new CAArgument(typeSig, sig);
                    case CustomAttributeArgument[] argumentArray:
                        return new CAArgument(new SZArraySig(typeSig), argumentArray.Select(GetArgument).ToList());
                    // Needed for nested arrays
                    case CustomAttributeArgument nestedArgument:
                        return GetArgument(nestedArgument);
                    default:
                        return new CAArgument(typeSig, argument.Value);
                }
            }
        }

        // Generate type recursively with all nested types and add to module
        private TypeDefUser AddType(ModuleDef module, TypeInfo type)
        {
            var mType = CreateTypeShallow(module, type);

            // Add type to module
            module.Types.Add(mType);
            return mType;
        }

        // Convert Il2CppInspector TypeInfo into type reference and import to specified module
        private ITypeDefOrRef GetTypeRef(ModuleDef module, TypeInfo type) => GetTypeSig(module, type).ToTypeDefOrRef();

        // Convert Il2CppInspector TypeInfo into type signature and import to specified module
        private TypeSig GetTypeSig(ModuleDef module, TypeInfo type)
        {
            if (type == null)
                return null;
            if (!signatureCache.TryGetValue(module, out var cache))
                signatureCache.Add(module, cache = []);
            if (!cache.TryGetValue(type, out var signature))
                cache.Add(type, signature = new Importer(module, ImporterOptions.TryToUseTypeDefs).Import(GetTypeSigImpl(module, type)));
            return signature;
        }

        // Convert Il2CppInspector TypeInfo into type signature
        private TypeSig GetTypeSigImpl(ModuleDef module, TypeInfo type)
        {
            if (type == null)
                return null;

            // Encode CLR primitive signatures as ELEMENT_TYPE_I4/I8/etc., including RVA fields.
            if (type.IsPrimitive || type.FullName is "System.String" or "System.Object")
            {
                var primitive = module.CorLibTypes.GetCorLibTypeSig(type.Namespace, type.Name, module.CorLibTypes.AssemblyRef);
                if (primitive != null)
                    return primitive;
            }

            // Generic type parameter (VAR)
            if (type.IsGenericTypeParameter)
                return new GenericVar(type.GenericParameterPosition);

            // Generic method parameter (MVAR)
            if (type.IsGenericMethodParameter)
                return new GenericMVar(type.GenericParameterPosition);

            // Array and single-dimension zero-indexed array (ARRAY / SZARRAY)
            if (type.IsArray)
                if (type.GetArrayRank() == 1)
                    return new SZArraySig(GetTypeSig(module, type.ElementType));
                else
                    return new ArraySig(GetTypeSig(module, type.ElementType), type.GetArrayRank());

            // Pointer (PTR)
            if (type.IsPointer)
                return new PtrSig(GetTypeSig(module, type.ElementType));

            // Reference (BYREF)
            if (type.IsByRef)
                return new ByRefSig(GetTypeSig(module, type.ElementType));

            // Get module that owns the type
            var typeOwnerModule = modules[type.Assembly];
            var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (typeOwnerModule == module && types.TryGetValue(module, out var localTypes) && localTypes.TryGetValue(definition, out var localType))
            {
                var localSig = localType.ToTypeSig(type.IsValueType);
                if (!type.IsGenericType)
                    return localSig;
                var genericSig = new GenericInstSig(localSig.ToClassOrValueTypeSig(), type.GenericTypeArguments.Length);
                foreach (var argument in type.GetGenericArguments())
                    genericSig.GenericArguments.Add(GetTypeSig(module, argument));
                return genericSig;
            }
            var typeOwnerModuleRef = new ModuleRefUser(typeOwnerModule);

            //Keep the same as TypeDef
            string ns = type.IsNested ? null : type.Namespace;
            // Get reference to type; use nested type as resolution scope if applicable
            var typeSig = new TypeRefUser(
                typeOwnerModule,
                ns,
                type.BaseName,
                type.DeclaringType != null ? (IResolutionScope)GetTypeRef(module, type.DeclaringType).ScopeType : typeOwnerModuleRef
            ).ToTypeSig(type.IsValueType);

            // Non-generic type (CLASS / VALUETYPE)
            if (!type.GetGenericArguments().Any())
                return typeSig;

            // Generic type requires generic arguments (GENERICINST)
            var genericInstSig = new GenericInstSig(typeSig.ToClassOrValueTypeSig(), type.GenericTypeArguments.Length);

            foreach (var gp in type.GetGenericArguments())
                genericInstSig.GenericArguments.Add(GetTypeSig(module, gp));

            return genericInstSig;
        }

        public Action<OperationProgress> ProgressCallback { get; set; }

        // Generate and save all DLLs
        public void Write(string outputPath, EventHandler<string> statusCallback = null)
        {
            var stream = model.Package.Metadata.GamePlugin?.StreamExports == true;
            long completed = 0;
            long total = model.Assemblies.Count * 3L;
            void Report(string detail) => ProgressCallback?.Invoke(new OperationProgress("Generating DummyDlls", completed, total, detail));
            Report("Preparing assemblies");
            // Create folder for DLLs
            Directory.CreateDirectory(outputPath);

            if (model.Package.Version >= MetadataVersions.V290)
            {
                // We can now apply all attributes directly.
                directApplyAttributes = model.TypesByDefinitionIndex.Where(IsAttributeType).ToDictionary(x => x, _ => (TypeDef)null);
            }
            else
            {
                // Get all custom attributes with no parameters
                // We'll add these directly to objects instead of the attribute generator function pointer
                directApplyAttributes = model
                    .TypesByDefinitionIndex.Where(t =>
                        IsAttributeType(t) && t.DeclaredFields.Count == 0 && t.DeclaredProperties.Count == 0 && t.DeclaredConstructors.Any(c => !c.IsStatic && c.DeclaredParameters.Count == 0)
                    )
                    .ToDictionary(t => t, t => (TypeDef)null);
            }

            if (model.Package.Metadata.HasGameAdapter)
                for (var i = 0; i < model.Package.MethodSpecs.Length; i++)
                {
                    var spec = model.Package.MethodSpecs[i];
                    if (model.Package.GenericMethodPointers.TryGetValue(spec, out var pointer))
                        genericAddresses.TryAdd(spec.MethodDefinitionIndex, (pointer, i));
                }

            // Used for resolving TypeRefs, needed for static array initializers
            var ctx = ModuleDef.CreateModuleContext();

            // Generate blank assemblies
            // We have to do this before adding anything else so we can reference every module
            modules.Clear();

            foreach (var asm in model.Assemblies)
            {
                // Create assembly and add primary module to list
                var module = CreateAssembly(asm);
                module.Context = ctx;
                modules.Add(asm, module);
            }

            // Generate our custom types assembly (relies on mscorlib.dll being added above)
            if (!SuppressMetadata)
            {
                var baseDll = CreateBaseAssembly();

                // Write base assembly to disk
                baseDll.Write(Path.Combine(outputPath, baseDll.Name));
            }

            // Initialize required builtin attributes we might need to add
            // ourselves
            GetBuiltinAttributes();

            // Add all types
            foreach (var asm in model.Assemblies)
            {
                statusCallback?.Invoke(this, "Preparing " + asm.ShortName);
                Report("Preparing " + asm.ShortName);
                foreach (var type in asm.DefinedTypes.Where(t => !t.IsNested))
                    AddType(modules[asm], type);
                completed++;
                Report("Preparing " + asm.ShortName);
            }

            var exportAssemblies = stream
                ? model.Assemblies.OrderByDescending(a => a.DefinedTypes.Sum(t => (long)t.Definition.MethodCount + t.Definition.FieldCount)).ToArray()
                : model.Assemblies.ToArray();
            foreach (var asm in exportAssemblies)
            {
                statusCallback?.Invoke(this, "Populating " + asm.ShortName);
                Report("Populating " + asm.ShortName);
                var module = modules[asm];

                // Add assembly custom attribute attributes (must do this after all assemblies and types are created due to type referencing)
                foreach (var ca in asm.CustomAttributes)
                    AddCustomAttribute(module, module.Assembly, ca);

                if (model.Package.Metadata.HasGameAdapter)
                    module.Assembly.AddAttribute(module, assemblyFlagsAttribute, ("Flags", $"0x{(uint)asm.AssemblyDefinition.Aname.Flags:X}"));

                // Add token attributes
                module.AddAttribute(module, tokenAttribute, ("Token", $"0x{asm.ImageDefinition.Token:X8}"));
                module.Assembly.AddAttribute(module, tokenAttribute, ("Token", $"0x{asm.MetadataToken:X8}"));

                if (types.TryGetValue(module, out var shallowTypes))
                    foreach (var (typeInfo, typeDef) in shallowTypes)
                        PopulateType(module, typeDef, typeInfo);
                completed++;
                Report("Populating " + asm.ShortName);

                if (stream)
                {
                    WriteModule(module);
                    ReleaseModuleMembers(module);
                }
            }

            // Write all assemblies to disk
            if (!stream)
            {
                foreach (var module in modules.Values)
                {
                    WriteModule(module);
                }
            }

            return;

            void WriteModule(ModuleDef asm)
            {
                statusCallback?.Invoke(this, "Generating " + asm.Name);
                Report("Generating " + asm.Name);
                asm.Write(Path.Combine(outputPath, asm.Name));
                completed++;
                Report("Generating " + asm.Name);
            }

            static bool IsAttributeType(TypeInfo type) => type != null && (type.FullName == "System.Attribute" || (type.BaseType != null && IsAttributeType(type.BaseType)));
        }

        private void ReleaseModuleMembers(ModuleDef module)
        {
            // 其他模块仍会导入类型和属性标识，但绝不会导入这些成员。
            // 保留浅层类型树、枚举存储和泛型参数，以用于类型解析。
            if (types.TryGetValue(module, out var definitions))
            {
                foreach (var definition in definitions.Values)
                {
                    var enumStorage = definition.IsEnum ? definition.Fields.FirstOrDefault(f => !f.IsStatic && !f.IsLiteral) : null;
                    definition.Fields.Clear();
                    if (enumStorage != null)
                        definition.Fields.Add(enumStorage);
                    definition.Methods.Clear();
                    definition.Properties.Clear();
                    definition.Events.Clear();
                    definition.CustomAttributes.Clear();
                }
            }
            module.CustomAttributes.Clear();
            module.Assembly.CustomAttributes.Clear();
            signatureCache.Remove(module);
            defaultBodies.Remove(module);
            dnlibExtensions.ReleaseAnnotationCache(module);
        }
    }
}
