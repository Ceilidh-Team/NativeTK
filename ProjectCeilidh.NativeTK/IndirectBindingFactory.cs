using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using ProjectCeilidh.NativeTK.Attributes;
using ProjectCeilidh.NativeTK.Native;

namespace ProjectCeilidh.NativeTK
{
    public class IndirectBindingFactory : BindingFactory
    {
        public override NativeBindingType BindingType => NativeBindingType.Indirect;

        public override object CreateBinding(Type contractType)
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.X86) throw new PlatformNotSupportedException();

            // Get the NativeLibraryLoader for this platform
            var loader = NativeLibraryLoader.GetLibraryLoaderForPlatform();

            if (!contractType.IsInterface) throw new ArgumentException("Type argument must be an interface.");

            var libraryAttr = contractType.GetCustomAttribute<NativeLibraryContractAttribute>();

            if (libraryAttr == null) throw new ArgumentException("Type argument must have a NativeLibraryContractAttribute");

            var handle = loader.LoadNativeLibrary(libraryAttr.LibraryName, libraryAttr.Version);

            // Create the dynamic assembly which will contain the binding
            var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(libraryAttr.LibraryName, libraryAttr.Version),
                "<Module>", ModuleKind.Dll);
            // Create the binding type
            var implTyp = new TypeDefinition("", $"{contractType.Name}Impl", Mono.Cecil.TypeAttributes.Public, asm.MainModule.TypeSystem.Object);
            implTyp.Interfaces.Add(new InterfaceImplementation(asm.MainModule.ImportReference(contractType)));
            asm.MainModule.Types.Add(implTyp);

            // Create a default constructor for the binding type
            var implCtor = new MethodDefinition(".ctor",
                Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName | Mono.Cecil.MethodAttributes.Public |
                Mono.Cecil.MethodAttributes.HideBySig, asm.MainModule.TypeSystem.Void);
            implTyp.Methods.Add(implCtor);
            // Simple ctor body - load `this`, call `new object()` against it
            var ctorProc = implCtor.Body.GetILProcessor();
            ctorProc.Emit(OpCodes.Ldarg_0);
            ctorProc.Emit(OpCodes.Call, asm.MainModule.ImportReference(typeof(object).GetConstructor(new Type[0])));
            ctorProc.Emit(OpCodes.Ret);

            // Implement all the methods in the interface
            foreach (var intMethod in contractType.GetMethods())
            {
                // If the method has a special name, ignore it. This excludes property getters/setters
                if (intMethod.IsSpecialName) continue;

                // The method cannot have varargs (this actually /can/ be achieved later, but it's too complicated for now)
                if (intMethod.CallingConvention == CallingConventions.VarArgs) throw new ArgumentException("Type argument cannot contain a method with varargs");

                var intAttr = intMethod.GetCustomAttribute<NativeImportAttribute>();

                if (intAttr == null) throw new ArgumentException($"Type argument contains a method without a NativeImportAttribute ({intMethod.Name})");

                // Create the dynamic method for the implementation
                var meth = new MethodDefinition(intMethod.Name,
                    Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Final |
                    Mono.Cecil.MethodAttributes.Virtual,
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
                        new ParameterDefinition(param.Name, (Mono.Cecil.ParameterAttributes)param.Attributes,
                            asm.MainModule.ImportReference(param.ParameterType)));
                    meth.Parameters.Add(new ParameterDefinition(param.Name, (Mono.Cecil.ParameterAttributes)param.Attributes,
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
            foreach (var intProp in contractType.GetProperties())
            {
                var intAttr = intProp.GetCustomAttribute<NativeImportAttribute>();

                if (intAttr == null) throw new ArgumentException($"Type argument contains a property without a NativeImportAttribute ({intProp.Name})");

                if (!intProp.PropertyType.IsByRef) throw new ArgumentException($"Type argument's properties must be ref returns ({intProp.Name})");
                if (intProp.CanWrite) throw new ArgumentException($"Type argument's properties cannot have a setter ({intProp.Name})");

                if (intProp.PropertyType.GetElementType()?.IsValueType != true) throw new ArgumentException("Type argument's properties must be a reference to a value type");

                // Generate the property and get method
                var prop = new PropertyDefinition(intProp.Name, Mono.Cecil.PropertyAttributes.None,
                    asm.MainModule.ImportReference(intProp.PropertyType));
                var propMethod = new MethodDefinition($"get_{intProp.Name}",
                    Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.Final |
                    Mono.Cecil.MethodAttributes.SpecialName, asm.MainModule.ImportReference(intProp.PropertyType));
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

            // Write the newly generated assembly to memory, load it, and instatiate the newly generated binding
            using (var mem = new MemoryStream())
            {
                asm.Write(mem);
                var newAsm = Assembly.Load(mem.ToArray());
                var newTyp = newAsm.GetType($"{contractType.Name}Impl");
                return newTyp.GetConstructor(new Type[0])?.Invoke(new object[0]);
            }
        }
    }
}
