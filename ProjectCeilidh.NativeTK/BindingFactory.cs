using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using ProjectCeilidh.NativeTK.Attributes;

namespace ProjectCeilidh.NativeTK
{
    public class BindingFactory
    {
        public T CreateBinding<T>()
        {
            var intTyp = typeof(T);
            
            if (!intTyp.IsInterface) throw new ArgumentException();

            var name = Guid.NewGuid().ToString();

            var asm = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
            var mod = asm.DefineDynamicModule(name);
            var typ = mod.DefineType($"{intTyp.Name}Impl");
            typ.AddInterfaceImplementation(intTyp);

            foreach (var intMethod in intTyp.GetMethods())
            {
                if (intMethod.IsGenericMethodDefinition) throw new ArgumentException();
                
                var attr = intMethod.GetCustomAttribute<NativeImportAttribute>();
                
                if (attr == null) throw new ArgumentException();

                var delegateType = mod.DefineType(Guid.NewGuid().ToString(),
                    TypeAttributes.Class
                    | TypeAttributes.Public
                    | TypeAttributes.Sealed
                    | TypeAttributes.AnsiClass
                    | TypeAttributes.AutoClass,
                    typeof(MulticastDelegate));

                delegateType.SetCustomAttribute(
                    new CustomAttributeBuilder(
                        typeof(UnmanagedFunctionPointerAttribute).GetConstructor(new[] {typeof(CallingConvention)}),
                        new object[]
                        {
                            attr.CallingConvention
                        },
                        new[]
                        {
                            typeof(UnmanagedFunctionPointerAttribute).GetField(nameof(UnmanagedFunctionPointerAttribute
                                .CharSet)),
                            typeof(UnmanagedFunctionPointerAttribute).GetField(nameof(UnmanagedFunctionPointerAttribute
                                .BestFitMapping)),
                            typeof(UnmanagedFunctionPointerAttribute).GetField(nameof(UnmanagedFunctionPointerAttribute
                                .SetLastError)),
                            typeof(UnmanagedFunctionPointerAttribute).GetField(nameof(UnmanagedFunctionPointerAttribute
                                .ThrowOnUnmappableChar))
                        }, new object[]
                        {
                            attr.CharSet,
                            attr.BestFitMapping,
                            attr.SetLastError,
                            attr.ThrowOnUnmappableChar
                        }));

                var delegateCtor = delegateType.DefineConstructor(
                    MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
                    CallingConventions.Standard, new[] {typeof(object), typeof(IntPtr)});
                delegateCtor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

                var delegateInvoke = delegateType.DefineMethod("Invoke",
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot |
                    MethodAttributes.Virtual, intMethod.ReturnType,
                    intMethod.GetParameters().Select(x => x.ParameterType).ToArray());

                var invokeRetBuilder = delegateInvoke.DefineParameter(0, ParameterAttributes.Retval, "");
                
                foreach (var retAttr in intMethod.ReturnParameter.GetCustomAttributesData())
                {
                    invokeRetBuilder.SetCustomAttribute(new CustomAttributeBuilder(retAttr.Constructor,
                        retAttr.ConstructorArguments.Select(x => x.Value).ToArray(),
                        retAttr.NamedArguments.Where(x => !x.IsField).Select(x => x.MemberInfo).Cast<PropertyInfo>().ToArray(),
                        retAttr.NamedArguments.Where(x => !x.IsField).Select(x => x.TypedValue.Value).ToArray(),
                        retAttr.NamedArguments.Where(x => x.IsField).Select(x => x.MemberInfo).Cast<FieldInfo>().ToArray(),
                        retAttr.NamedArguments.Where(x => !x.IsField).Select(x => x.TypedValue.Value).ToArray()));
                }

                for (var i = 0; i < delegateInvoke.GetParameters().Length; i++)
                {
                    var par = delegateInvoke.GetParameters()[i];

                    var parBuilder = delegateInvoke.DefineParameter(i + 1, par.Attributes, par.Name);

                    foreach (var parAttr in par.GetCustomAttributesData())
                    {
                        parBuilder.SetCustomAttribute(new CustomAttributeBuilder(parAttr.Constructor,
                            parAttr.ConstructorArguments.Select(x => x.Value).ToArray(),
                            parAttr.NamedArguments.Where(x => !x.IsField).Select(x => x.MemberInfo).Cast<PropertyInfo>().ToArray(),
                            parAttr.NamedArguments.Where(x => !x.IsField).Select(x => x.TypedValue.Value).ToArray(),
                            parAttr.NamedArguments.Where(x => x.IsField).Select(x => x.MemberInfo).Cast<FieldInfo>().ToArray(),
                            parAttr.NamedArguments.Where(x => !x.IsField).Select(x => x.TypedValue.Value).ToArray()));
                    }
                }

                var implField = typ.DefineField(Guid.NewGuid().ToString(), delegateType,
                    FieldAttributes.Private | FieldAttributes.Static);

                var implMethod = typ.DefineMethod(intMethod.Name, MethodAttributes.Public | MethodAttributes.HideBySig,
                    CallingConventions.Standard, intMethod.ReturnType,
                    intMethod.GetParameters().Select(x => x.ParameterType).ToArray());

                for (var i = 0; i < intMethod.GetParameters().Length; i++)
                {
                    var intPar = intMethod.GetParameters()[i];

                    var implParam = implMethod.DefineParameter(i + 1, intPar.Attributes, intPar.Name);
                    implParam.SetConstant(intPar.DefaultValue);
                }

                var implGen = implMethod.GetILGenerator();
                
                implGen.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, );
                // TODO: generate invoke. Achive this with calli?
            }
        }
    }
}
