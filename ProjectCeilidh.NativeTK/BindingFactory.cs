using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Mono.Cecil;
using ProjectCeilidh.NativeTK.Attributes;
using ProjectCeilidh.NativeTK.Native;
using System.Reflection;
using Mono.Cecil.Cil;
using CustomAttributeNamedArgument = Mono.Cecil.CustomAttributeNamedArgument;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodImplAttributes = Mono.Cecil.MethodImplAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace ProjectCeilidh.NativeTK
{
    public static class BindingFactory
    {
        /// <summary>
        /// Create an implementation for a given interface, binding native methods.
        /// On compatible platforms, this is achieved using "CallIndirect"
        /// </summary>
        /// <typeparam name="T">The interface type to implement</typeparam>
        /// <returns>An instance binding the specified native interface.</returns>
        public static T CreateBinding<T>() where T : class 
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.X86) return CreateSafeBinding<T>();

            // Get the NativeLibraryLoader for this platform
            var loader = NativeLibraryLoader.GetLibraryLoaderForPlatform();

            var intTyp = typeof(T);
            
            if (!intTyp.IsInterface) throw new ArgumentException("Type argument must be an interface.");

            var libraryAttr = intTyp.GetCustomAttribute<NativeLibraryContractAttribute>();

            if (libraryAttr == null) throw new ArgumentException("Type argument must have a NativeLibraryContractAttribute");

            var handle = loader.LoadNativeLibrary(libraryAttr.LibraryName, libraryAttr.Version);

            // Create the dynamic assembly which will contain the binding
            var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(libraryAttr.LibraryName, libraryAttr.Version),
                "<Module>", ModuleKind.Dll);
            // Create the binding type
            var implTyp = new TypeDefinition("", $"{intTyp.Name}Impl", TypeAttributes.Public, asm.MainModule.TypeSystem.Object);
            implTyp.Interfaces.Add(new InterfaceImplementation(asm.MainModule.ImportReference(intTyp)));
            asm.MainModule.Types.Add(implTyp);

            // Create a default constructor for the binding type
            var implCtor = new MethodDefinition(".ctor",
                MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.Public |
                MethodAttributes.HideBySig, asm.MainModule.TypeSystem.Void);
            implTyp.Methods.Add(implCtor);
            // Simple ctor body - load `this`, call `new object()` against it
            var ctorProc = implCtor.Body.GetILProcessor();
            ctorProc.Emit(OpCodes.Ldarg_0);
            ctorProc.Emit(OpCodes.Call, asm.MainModule.ImportReference(typeof(object).GetConstructor(new Type[0])));
            ctorProc.Emit(OpCodes.Ret);

            // Implement all the methods in the interface
            foreach (var intMethod in intTyp.GetMethods())
            {
                // If the method has a special name, ignore it. This excludes property getters/setters
                if (intMethod.IsSpecialName) continue;

                // The method cannot have varargs (this actually /can/ be achieved later, but it's too complicated for now)
                if (intMethod.CallingConvention == CallingConventions.VarArgs) throw new ArgumentException("Type argument cannot contain a method with varargs");

                var intAttr = intMethod.GetCustomAttribute<NativeImportAttribute>();

                if (intAttr == null) throw new ArgumentException($"Type argument contains a method without a NativeImportAttribute ({intMethod.Name})");

                // Create the dynamic method for the implementation
                var meth = new MethodDefinition(intMethod.Name,
                    MethodAttributes.Public | MethodAttributes.Final |
                    MethodAttributes.Virtual,
                    asm.MainModule.ImportReference(intMethod.ReturnType));
                implTyp.Methods.Add(meth);

                // The body for the dynamic method
                var proc = meth.Body.GetILProcessor();

                // Generate a CallSite for the unmanaged function
                var callSite = new CallSite(asm.MainModule.ImportReference(intMethod.ReturnType));

                switch (intAttr.CallingConvention)
                {
                    case CallingConvention.Cdecl:
                        callSite.CallingConvention = MethodCallingConvention.C;
                        break;
                    case CallingConvention.FastCall:
                        callSite.CallingConvention = MethodCallingConvention.FastCall;
                        break;
                    case CallingConvention.Winapi:
                    case CallingConvention.StdCall:
                        callSite.CallingConvention = MethodCallingConvention.StdCall;
                        break;
                    case CallingConvention.ThisCall:
                        callSite.CallingConvention = MethodCallingConvention.ThisCall;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                var i = 0;
                foreach (var param in intMethod.GetParameters())
                {
                    callSite.Parameters.Add(
                        new ParameterDefinition(param.Name, (ParameterAttributes) param.Attributes,
                            asm.MainModule.ImportReference(param.ParameterType)));
                    meth.Parameters.Add(new ParameterDefinition(param.Name, (ParameterAttributes) param.Attributes,
                        asm.MainModule.ImportReference(param.ParameterType)));

                    proc.Emit(OpCodes.Ldarg, ++i);
                }

                // Load the symbol address for this function as a long, then convert to an IntPtr (native int)
                proc.Emit(OpCodes.Ldc_I8, (long)handle.GetSymbolAddress(intAttr.EntryPoint ?? intMethod.Name));
                proc.Emit(OpCodes.Conv_I);

                // Invoke the method with a CallIndirect, then return the result
                proc.Emit(OpCodes.Calli, callSite);
                proc.Emit(OpCodes.Ret);
            }

            // Implement all the properties in the interface
            foreach (var intProp in intTyp.GetProperties())
            {
                var intAttr = intProp.GetCustomAttribute<NativeImportAttribute>();

                if (intAttr == null) throw new ArgumentException($"Type argument contains a property without a NativeImportAttribute ({intProp.Name})");

                if (!intProp.PropertyType.IsByRef) throw new ArgumentException($"Type argument's properties must be ref returns ({intProp.Name})");
                if (intProp.CanWrite) throw new ArgumentException($"Type argument's properties cannot have a setter ({intProp.Name})");

                if (intProp.PropertyType.GetElementType()?.IsValueType != true) throw new ArgumentException("Type argument's properties must be a reference to a value type");

                // Generate the property and get method
                var prop = new PropertyDefinition(intProp.Name, PropertyAttributes.None,
                    asm.MainModule.ImportReference(intProp.PropertyType));
                var propMethod = new MethodDefinition($"get_{intProp.Name}",
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
                    MethodAttributes.SpecialName, asm.MainModule.ImportReference(intProp.PropertyType));
                prop.GetMethod = propMethod;
                implTyp.Properties.Add(prop);
                implTyp.Methods.Add(propMethod);

                // Generate a get body which resolves the symbol
                var getProc = propMethod.Body.GetILProcessor();

                // Load the symbol address and convert to an IntPtr (native int)
                getProc.Emit(OpCodes.Ldc_I8, (long) handle.GetSymbolAddress(intAttr.EntryPoint ?? intProp.Name));
                getProc.Emit(OpCodes.Conv_I);
                // Return this unmodified - the result is that the pointer is converted to a reference by the CLR
                getProc.Emit(OpCodes.Ret);
            }

            // Write the newly generated assembly to memory, load it, and instatiate the newly generated binding
            using (var mem = new MemoryStream())
            {
                asm.Write(mem);
                File.WriteAllBytes(@"C:\Users\olivia\Desktop\test.dll", mem.ToArray());

                var newAsm = Assembly.Load(mem.ToArray());
                var newTyp = newAsm.GetType($"{intTyp.Name}Impl");
                return (T) newTyp.GetConstructor(new Type[0])?.Invoke(new object[0]);
            }
        }

        /// <summary>
        /// Create an implementation for a given interface, binding native methods.
        /// This is achieved using delegates, translating parameter and return value annotations to allow custom marshaling.
        /// </summary>
        /// <typeparam name="T">The interface type to implement</typeparam>
        /// <returns>An instance binding the specified native interface.</returns>
        public static T CreateSafeBinding<T>()
        {
            var intTyp = typeof(T);

            if (!intTyp.IsInterface) throw new ArgumentException("Type argument must be an interface.");

            var libraryAttr = intTyp.GetCustomAttribute<NativeLibraryContractAttribute>();

            if (libraryAttr == null) throw new ArgumentException("Type argument must have a NativeLibraryContractAttribute");

            var loader = NativeLibraryLoader.GetLibraryLoaderForPlatform();
            var handle = loader.LoadNativeLibrary(libraryAttr.LibraryName, libraryAttr.Version);

            // Create the dynamic assembly which will contain the binding
            var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(libraryAttr.LibraryName, libraryAttr.Version),
                "<Module>", ModuleKind.Dll);
            // Create the binding type
            var implTyp = new TypeDefinition("", $"{intTyp.Name}Impl", TypeAttributes.Public, asm.MainModule.TypeSystem.Object);
            implTyp.Interfaces.Add(new InterfaceImplementation(asm.MainModule.ImportReference(intTyp)));
            asm.MainModule.Types.Add(implTyp);

            var handleField = new FieldDefinition("_handle", FieldAttributes.Private | FieldAttributes.InitOnly, asm.MainModule.ImportReference(typeof(NativeLibraryHandle)));
            implTyp.Fields.Add(handleField);

            // Create a default constructor for the binding type
            var implCtor = new MethodDefinition(".ctor",
                MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.Public |
                MethodAttributes.HideBySig, asm.MainModule.TypeSystem.Void)
            {
                Parameters = { new ParameterDefinition("handle", ParameterAttributes.None, asm.MainModule.ImportReference(typeof(NativeLibraryHandle))) }
            };
            implTyp.Methods.Add(implCtor);
            // Simple ctor body - load `this`, call `new object()` against it
            var ctorProc = implCtor.Body.GetILProcessor();
            ctorProc.Emit(OpCodes.Ldarg_0);
            ctorProc.Emit(OpCodes.Call, asm.MainModule.ImportReference(typeof(object).GetConstructor(new Type[0])));

            ctorProc.Emit(OpCodes.Ldarg_0);
            ctorProc.Emit(OpCodes.Ldarg_1);
            ctorProc.Emit(OpCodes.Stfld, handleField);

            // Implement all the methods in the interface
            foreach (var intMethod in intTyp.GetMethods())
            {
                // If the method has a special name, ignore it. This excludes property getters/setters
                if (intMethod.IsSpecialName) continue;

                // The method cannot have varargs (this actually /can/ be achieved later, but it's too complicated for now)
                if (intMethod.CallingConvention == CallingConventions.VarArgs) throw new ArgumentException("Type argument cannot contain a method with varargs");

                var intAttr = intMethod.GetCustomAttribute<NativeImportAttribute>();

                if (intAttr == null) throw new ArgumentException($"Type argument contains a method without a NativeImportAttribute ({intMethod.Name})");

                // Create a delegate type which
                var delegateType = new TypeDefinition("", Guid.NewGuid().ToString("N"),
                    TypeAttributes.Class
                    | TypeAttributes.NestedPrivate
                    | TypeAttributes.Sealed
                    | TypeAttributes.AnsiClass
                    | TypeAttributes.AutoClass,
                    asm.MainModule.ImportReference(typeof(MulticastDelegate)));
                implTyp.NestedTypes.Add(delegateType);

                var ptrAttr = new CustomAttribute(asm.MainModule.ImportReference(
                    typeof(UnmanagedFunctionPointerAttribute).GetConstructor(new[] {typeof(CallingConvention)})))
                {
                    ConstructorArguments =
                    {
                        new CustomAttributeArgument(asm.MainModule.ImportReference(typeof(CallingConvention)),
                            (int)intAttr.CallingConvention)
                    },
                    Properties =
                    {
                        new CustomAttributeNamedArgument(
                            nameof(UnmanagedFunctionPointerAttribute.CharSet),
                            new CustomAttributeArgument(asm.MainModule.ImportReference(typeof(CharSet)),
                                (int)intAttr.CharSet)),
                        new CustomAttributeNamedArgument(
                            nameof(UnmanagedFunctionPointerAttribute.BestFitMapping),
                            new CustomAttributeArgument(asm.MainModule.TypeSystem.Boolean, intAttr.BestFitMapping)),
                        new CustomAttributeNamedArgument(
                            nameof(UnmanagedFunctionPointerAttribute.SetLastError),
                            new CustomAttributeArgument(asm.MainModule.TypeSystem.Boolean, intAttr.SetLastError)),
                        new CustomAttributeNamedArgument(
                            nameof(UnmanagedFunctionPointerAttribute.ThrowOnUnmappableChar),
                            new CustomAttributeArgument(asm.MainModule.TypeSystem.Boolean,
                                intAttr.ThrowOnUnmappableChar))
                    }
                };
                delegateType.CustomAttributes.Add(ptrAttr);

                var delegateCtor = new MethodDefinition(".ctor",
                    MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
                    asm.MainModule.TypeSystem.Void)
                {
                    ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
                    Parameters = { new ParameterDefinition(asm.MainModule.TypeSystem.Object), new ParameterDefinition(asm.MainModule.TypeSystem.IntPtr) }
                };
                delegateType.Methods.Add(delegateCtor);

                var delegateInvoke = new MethodDefinition("Invoke",
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot |
                    MethodAttributes.Virtual, asm.MainModule.ImportReference(intMethod.ReturnType))
                {
                    ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed
                };
                delegateType.Methods.Add(delegateInvoke);

                foreach (var attr in intMethod.ReturnParameter?.CustomAttributes ?? Enumerable.Empty<CustomAttributeData>())
                {
                    var customAttr = new CustomAttribute(asm.MainModule.ImportReference(attr.Constructor));

                    foreach (var ctorArgument in attr.ConstructorArguments)
                        customAttr.ConstructorArguments.Add(new CustomAttributeArgument(asm.MainModule.ImportReference(ctorArgument.ArgumentType), ctorArgument.Value));
                    foreach (var namedArgument in attr.NamedArguments ?? Enumerable.Empty<System.Reflection.CustomAttributeNamedArgument>())
                        customAttr.Properties.Add(new CustomAttributeNamedArgument(namedArgument.MemberName, new CustomAttributeArgument(asm.MainModule.ImportReference(namedArgument.TypedValue.ArgumentType), namedArgument.TypedValue.Value)));

                    delegateInvoke.MethodReturnType.CustomAttributes.Add(customAttr);
                }

                foreach (var parameterInfo in intMethod.GetParameters())
                {
                    var parDef = new ParameterDefinition(parameterInfo.Name,
                        (ParameterAttributes) parameterInfo.Attributes,
                        asm.MainModule.ImportReference(parameterInfo.ParameterType));
                    delegateInvoke.Parameters.Add(parDef);

                    foreach (var attr in parameterInfo.CustomAttributes)
                    {
                        var customAttr = new CustomAttribute(asm.MainModule.ImportReference(attr.Constructor));

                        foreach (var ctorArgument in attr.ConstructorArguments)
                            customAttr.ConstructorArguments.Add(new CustomAttributeArgument(asm.MainModule.ImportReference(ctorArgument.ArgumentType), ctorArgument.Value));
                        foreach (var namedArgument in attr.NamedArguments)
                            customAttr.Properties.Add(new CustomAttributeNamedArgument(namedArgument.MemberName, new CustomAttributeArgument(asm.MainModule.ImportReference(namedArgument.TypedValue.ArgumentType), namedArgument.TypedValue.Value)));

                        parDef.CustomAttributes.Add(customAttr);
                    }
                }

                var field = new FieldDefinition(Guid.NewGuid().ToString("N"), FieldAttributes.InitOnly | FieldAttributes.Private, delegateType);
                implTyp.Fields.Add(field);

                var getDelegateRef = new GenericInstanceMethod(asm.MainModule.ImportReference(
                    typeof(NativeLibraryHandle).GetMethod(nameof(NativeLibraryHandle.GetDelegateForSymbol))));
                getDelegateRef.GenericArguments.Add(delegateType);

                ctorProc.Emit(OpCodes.Ldarg_0);
                ctorProc.Emit(OpCodes.Ldarg_1);
                ctorProc.Emit(OpCodes.Ldstr, intAttr.EntryPoint ?? intMethod.Name);
                ctorProc.Emit(OpCodes.Ldc_I4_1);
                ctorProc.Emit(OpCodes.Call, getDelegateRef);
                ctorProc.Emit(OpCodes.Stfld, field);

                // Create the dynamic method for the implementation
                var meth = new MethodDefinition(intMethod.Name,
                    MethodAttributes.Public | MethodAttributes.Final |
                    MethodAttributes.Virtual,
                    asm.MainModule.ImportReference(intMethod.ReturnType));
                implTyp.Methods.Add(meth);

                // The body for the dynamic method
                var proc = meth.Body.GetILProcessor();

                proc.Emit(OpCodes.Ldarg_0);
                proc.Emit(OpCodes.Ldfld, field);

                var i = 0;
                foreach (var param in intMethod.GetParameters())
                {
                    meth.Parameters.Add(new ParameterDefinition(param.Name, (ParameterAttributes)param.Attributes,
                        asm.MainModule.ImportReference(param.ParameterType)));

                    proc.Emit(OpCodes.Ldarg, ++i);
                }

                // Invoke the delegate
                proc.Emit(OpCodes.Call, delegateInvoke);
                proc.Emit(OpCodes.Ret);
            }

            // Implement all the properties in the interface
            foreach (var intProp in intTyp.GetProperties())
            {
                var intAttr = intProp.GetCustomAttribute<NativeImportAttribute>();

                if (intAttr == null) throw new ArgumentException($"Type argument contains a property without a NativeImportAttribute ({intProp.Name})");

                if (!intProp.PropertyType.IsByRef) throw new ArgumentException($"Type argument's properties must be ref returns ({intProp.Name})");
                if (intProp.CanWrite) throw new ArgumentException($"Type argument's properties cannot have a setter ({intProp.Name})");

                if (intProp.PropertyType.GetElementType()?.IsValueType != true) throw new ArgumentException("Type argument's properties must be a reference to a value type");

                // Generate the property and get method
                var prop = new PropertyDefinition(intProp.Name, PropertyAttributes.None,
                    asm.MainModule.ImportReference(intProp.PropertyType));
                var propMethod = new MethodDefinition($"get_{intProp.Name}",
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
                    MethodAttributes.SpecialName, asm.MainModule.ImportReference(intProp.PropertyType));
                prop.GetMethod = propMethod;
                implTyp.Properties.Add(prop);
                implTyp.Methods.Add(propMethod);

                // Generate a get body which resolves the symbol
                var getProc = propMethod.Body.GetILProcessor();

                // Load the symbol address and convert to an IntPtr (native int)
                getProc.Emit(OpCodes.Ldc_I8, (long)handle.GetSymbolAddress(intAttr.EntryPoint ?? intProp.Name));
                getProc.Emit(OpCodes.Conv_I);
                // Return this unmodified - the result is that the pointer is converted to a reference by the CLR
                getProc.Emit(OpCodes.Ret);
            }

            ctorProc.Emit(OpCodes.Ret);

            // Write the newly generated assembly to memory, load it, and instatiate the newly generated binding
            using (var mem = new MemoryStream())
            {
                asm.Write(mem);
                File.WriteAllBytes(@"C:\Users\olivia\Desktop\test.dll", mem.ToArray());

                var newAsm = Assembly.Load(mem.ToArray());
                var newTyp = newAsm.GetType($"{intTyp.Name}Impl");
                return (T)newTyp.GetConstructor(new[] { typeof(NativeLibraryHandle) })?.Invoke(new object[] { handle });
            }
        }
    }
}
