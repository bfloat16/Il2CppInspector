/*
    Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty
    Copyright 2020 Robert Xiao - https://robertxiao.ca

    All rights reserved.
*/

using System.Reflection;

namespace Il2CppInspector.Reflection
{
    public class MethodInfo : MethodBase
    {
        public override MemberTypes MemberType => MemberTypes.Method;

        // Info about the return parameter
        private ParameterInfo returnParameter;
        public ParameterInfo ReturnParameter => returnParameter ??= Definition.IsValid
            ? new ParameterInfo(Assembly.Model.Package, -1, this)
            : ((MethodInfo)rootDefinition).ReturnParameter.SubstituteGenericArguments(this, DeclaringType.GetGenericArguments(), GetGenericArguments());

        // Return type of the method
        public TypeInfo ReturnType => returnParameter?.ParameterType ?? (Definition.IsValid
            ? Assembly.Model.TypesByReferenceIndex[Definition.ReturnType]
            : ((MethodInfo)rootDefinition).ReturnType.SubstituteGenericArguments(DeclaringType.GetGenericArguments(), GetGenericArguments()));

        internal override void ReleaseTransientParameters()
        {
            base.ReleaseTransientParameters();
            if (!Assembly.Model.RetainMethodParameters)
                returnParameter = null;
        }

        public override bool RequiresUnsafeContext => base.RequiresUnsafeContext || ReturnType.RequiresUnsafeContext;

        // IL2CPP doesn't seem to retain return type custom attributes

        public MethodInfo(Il2CppInspector pkg, int methodIndex, TypeInfo declaringType)
            : base(pkg, methodIndex, declaringType)
        {
        }

        public MethodInfo(MethodInfo methodDef, TypeInfo declaringType)
            : base(methodDef, declaringType)
        {
        }

        private MethodInfo(MethodInfo methodDef, TypeInfo[] typeArguments)
            : base(methodDef, typeArguments)
        {
        }

        protected override MethodBase MakeGenericMethodImpl(TypeInfo[] typeArguments) => new MethodInfo(this, typeArguments);

        public override string ToString() =>
            ReturnType.Name
            + " "
            + Name
            + GetFullTypeParametersString()
            + "("
            + string.Join(", ", DeclaredParameters.Select(x => x.ParameterType.IsByRef ? x.ParameterType.Name.TrimEnd('&') + " ByRef" : x.ParameterType.Name))
            + ")";
    }
}
