using System;
using System.IO;
using System.Runtime.InteropServices;
using Mono.Cecil;
using ProjectCeilidh.NativeTK.Attributes;
using ProjectCeilidh.NativeTK.Native;
using System.Reflection;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;

namespace ProjectCeilidh.NativeTK
{
    public static class BindingFactory
    {
        /// <summary>
        /// Create an implementation for a given interface, binding native methods.
        /// </summary>
        /// <typeparam name="T">The interface type to implement</typeparam>
        /// <returns>An instance binding the specified native interface.</returns>
        public static T CreateBinding<T>() where T : class 
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.X86) throw new PlatformNotSupportedException("Cannot use \"CALLI\" on x86 platforms");

            // Get the NativeLibraryLoader for this platform
            var loader = NativeLibraryLoader.GetLibraryLoaderForPlatform();

            var intTyp = typeof(T);
            
            if (!intTyp.IsInterface) throw new ArgumentException();

            var libraryAttr = intTyp.GetCustomAttribute<NativeLibraryContractAttribute>();

            var handle = loader.LoadNativeLibrary(libraryAttr.LibraryName, libraryAttr.Version);

            var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(libraryAttr.LibraryName, libraryAttr.Version),
                "<Module>", ModuleKind.Dll);
            var implTyp = new TypeDefinition("", $"{intTyp.Name}Impl", Mono.Cecil.TypeAttributes.Public, asm.MainModule.TypeSystem.Object);
            implTyp.Interfaces.Add(new InterfaceImplementation(asm.MainModule.ImportReference(intTyp)));
            asm.MainModule.Types.Add(implTyp);

            var implCtor = new MethodDefinition(".ctor", MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.Public | MethodAttributes.HideBySig, asm.MainModule.TypeSystem.Void);
            implTyp.Methods.Add(implCtor);
            var ctorProc = implCtor.Body.GetILProcessor();
            ctorProc.Emit(OpCodes.Ldarg_0);
            ctorProc.Emit(OpCodes.Call, asm.MainModule.ImportReference(typeof(object).GetConstructor(new Type[0])));
            ctorProc.Emit(OpCodes.Ret);

            foreach (var intMethod in intTyp.GetRuntimeMethods())
            {
                if (intMethod.IsSpecialName) continue;

                if (intMethod.CallingConvention == CallingConventions.VarArgs) throw new ArgumentException();

                var intAttr = intMethod.GetCustomAttribute<NativeImportAttribute>();

                var meth = new MethodDefinition(intMethod.Name,
                    MethodAttributes.Public | MethodAttributes.Final |
                    MethodAttributes.Virtual,
                    asm.MainModule.ImportReference(intMethod.ReturnType));
                implTyp.Methods.Add(meth);
                var proc = meth.Body.GetILProcessor();

                proc.Emit(OpCodes.Ldc_I8, (long) handle.GetSymbolAddress(intAttr.EntryPoint ?? intMethod.Name));
                proc.Emit(OpCodes.Conv_I);

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
                        new ParameterDefinition(asm.MainModule.ImportReference(param.ParameterType)));

                    proc.Emit(OpCodes.Ldarg, ++i);
                }

                proc.Emit(OpCodes.Calli, callSite);
                proc.Emit(OpCodes.Ret);
            }

            foreach (var intProp in intTyp.GetRuntimeProperties())
            {
                var intAttr = intProp.GetCustomAttribute<NativeImportAttribute>();

                if (intProp.CanWrite) throw new ArgumentException();

                if (intProp.PropertyType.IsByRef)
                {
                    if (intProp.PropertyType.GetElementType()?.IsValueType != true) throw new ArgumentException();

                    var prop = new PropertyDefinition(intProp.Name, PropertyAttributes.None,
                        asm.MainModule.ImportReference(intProp.PropertyType));
                    var propMethod = new MethodDefinition($"get_{intProp.Name}",
                        MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
                        MethodAttributes.SpecialName, asm.MainModule.ImportReference(intProp.PropertyType));
                    prop.GetMethod = propMethod;
                    implTyp.Properties.Add(prop);
                    implTyp.Methods.Add(propMethod);

                    var getProc = propMethod.Body.GetILProcessor();

                    getProc.Emit(OpCodes.Ldc_I8, (long) handle.GetSymbolAddress(intAttr.EntryPoint ?? intProp.Name));
                    getProc.Emit(OpCodes.Conv_I);
                    getProc.Emit(OpCodes.Ret);
                }
                else
                {
                    if (!intProp.PropertyType.IsValueType) throw new ArgumentException();

                    var prop = new PropertyDefinition(intProp.Name, PropertyAttributes.None,
                        asm.MainModule.ImportReference(intProp.PropertyType));
                    var propMethod = new MethodDefinition($"get_{intProp.Name}",
                        MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
                        MethodAttributes.SpecialName, asm.MainModule.ImportReference(intProp.PropertyType));
                    prop.GetMethod = propMethod;
                    implTyp.Properties.Add(prop);
                    implTyp.Methods.Add(propMethod);

                    var getProc = propMethod.Body.GetILProcessor();

                    getProc.Emit(OpCodes.Ldc_I8, (long)handle.GetSymbolAddress(intAttr.EntryPoint ?? intProp.Name));
                    getProc.Emit(OpCodes.Conv_I);
                    getProc.Emit(OpCodes.Ldobj, asm.MainModule.ImportReference(intProp.PropertyType));
                    getProc.Emit(OpCodes.Ret);
                }
            }

            using (var mem = new MemoryStream())
            {
                asm.Write(mem);
                var newAsm = Assembly.Load(mem.ToArray());
                var newTyp = newAsm.GetType($"{intTyp.Name}Impl");
                return (T) newTyp.GetConstructor(new Type[0])?.Invoke(new object[0]);
            }
        }
    }
}
