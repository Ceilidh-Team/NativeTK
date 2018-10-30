using System;
using System.IO;
using System.Runtime.InteropServices;
using Mono.Cecil;
using ProjectCeilidh.NativeTK.Attributes;
using ProjectCeilidh.NativeTK.Native;
using System.Reflection;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodImplAttributes = Mono.Cecil.MethodImplAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace ProjectCeilidh.NativeTK
{
    /// <summary>
    /// The strategy used to create the native binding
    /// </summary>
    public enum NativeBindingType
    {
        /// <summary>
        /// Bind native functions using the CallIndirect opcode.
        /// This has poor compatibility and does not support marshaling options
        /// </summary>
        Indirect = 1,
        /// <summary>
        /// Bind native functions using PInvokeImpl.
        /// This has the best compatibility, supporting marshaling options, but is slightly slower.
        /// </summary>
        Static = 2
    }

    /// <summary>
    /// Contains helper methods for creating native bindings.
    /// </summary>
    public static class BindingFactory
    {
        /// <summary>
        /// Create a implementation for a given interface, binding native methods.
        /// </summary>
        /// <typeparam name="T">An interface which defines native methods to bind.</typeparam>
        /// <param name="bindingType">The type of binding to create.</param>
        /// <returns>An implementation of the contract interface with the specified native methods bound.</returns>
        public static T CreateBinding<T>(NativeBindingType bindingType = NativeBindingType.Static) where T : class
        {
            switch (bindingType)
            {
                case NativeBindingType.Indirect:
                    return CreateIndirectBinding<T>();
                case NativeBindingType.Static:
                    return CreateStaticBinding<T>();
                default:
                    throw new ArgumentOutOfRangeException(nameof(bindingType), bindingType, null);
            }
        }

        /// <summary>
        /// Create an implementation for a given interface, binding native methods.
        /// On compatible platforms, this is achieved using "CallIndirect".
        /// </summary>
        /// <typeparam name="T">The interface type to implement</typeparam>
        /// <returns>An instance binding the specified native interface.</returns>
        private static T CreateIndirectBinding<T>() where T : class 
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.X86) throw new PlatformNotSupportedException();

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
                var newAsm = Assembly.Load(mem.ToArray());
                var newTyp = newAsm.GetType($"{intTyp.Name}Impl");
                return (T) newTyp.GetConstructor(new Type[0])?.Invoke(new object[0]);
            }
        }

        /// <summary>
        /// Create an implementation for a given interface, binding native methods.
        /// This is achieved by generating a class with DllImport attributes.
        /// </summary>
        /// <typeparam name="T">The interface type to implement</typeparam>
        /// <returns>An instance binding the specified native interface.</returns>
        private static T CreateStaticBinding<T>() where T : class 
        {
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

                // Create a static method with a PInvokeImpl that points to the library / function we want
                var bindMeth = new MethodDefinition($"{intMethod.Name}Impl",
                    MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PInvokeImpl | MethodAttributes.HideBySig,
                    asm.MainModule.ImportReference(intMethod.ReturnType))
                {
                    ImplAttributes = MethodImplAttributes.PreserveSig,
                };

                {
                    var module = new ModuleReference(handle.LibraryPath);
                    asm.MainModule.ModuleReferences.Add(module);

                    var pInvokeAttributes = PInvokeAttributes.NoMangle;
                    switch (intAttr.CallingConvention)
                    {
                        case CallingConvention.Cdecl:
                            pInvokeAttributes |= PInvokeAttributes.CallConvCdecl;
                            break;
                        case CallingConvention.FastCall:
                            pInvokeAttributes |= PInvokeAttributes.CallConvFastcall;
                            break;
                        case CallingConvention.StdCall:
                            pInvokeAttributes |= PInvokeAttributes.CallConvStdCall;
                            break;
                        case CallingConvention.ThisCall:
                            pInvokeAttributes |= PInvokeAttributes.CallConvThiscall;
                            break;
                        case CallingConvention.Winapi:
                            pInvokeAttributes |= PInvokeAttributes.CallConvWinapi;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    switch (intAttr.CharSet)
                    {
                        case CharSet.Ansi:
                            pInvokeAttributes |= PInvokeAttributes.CharSetAnsi;
                            break;
                        case CharSet.Auto:
                            pInvokeAttributes |= PInvokeAttributes.CharSetAuto;
                            break;
                        case CharSet.None:
                            pInvokeAttributes |= PInvokeAttributes.CharSetNotSpec;
                            break;
                        case CharSet.Unicode:
                            pInvokeAttributes |= PInvokeAttributes.CharSetUnicode;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    if (intAttr.SetLastError) pInvokeAttributes |= PInvokeAttributes.SupportsLastError;
                    pInvokeAttributes |= intAttr.BestFitMapping
                        ? PInvokeAttributes.BestFitEnabled
                        : PInvokeAttributes.BestFitDisabled;
                    pInvokeAttributes |= intAttr.ThrowOnUnmappableChar
                        ? PInvokeAttributes.ThrowOnUnmappableCharEnabled
                        : PInvokeAttributes.ThrowOnUnmappableCharDisabled;

                    bindMeth.PInvokeInfo = new PInvokeInfo(pInvokeAttributes, intAttr.EntryPoint ?? intMethod.Name, module);
                }

                var retMarshal = intMethod.ReturnParameter?.GetCustomAttribute<MarshalAsAttribute>();
                if (retMarshal != null)
                    bindMeth.MethodReturnType.MarshalInfo = GetMarshalInfo(asm.MainModule, retMarshal);

                // Add all the parameters we want
                foreach (var parameter in intMethod.GetParameters())
                {
                    var implParam = new ParameterDefinition(parameter.Name, (ParameterAttributes) parameter.Attributes, asm.MainModule.ImportReference(parameter.ParameterType));

                    foreach (var marshal in parameter.GetCustomAttributes<MarshalAsAttribute>())
                        implParam.MarshalInfo = GetMarshalInfo(asm.MainModule, marshal);

                    bindMeth.Parameters.Add(implParam);
                }

                implTyp.Methods.Add(bindMeth);

                // Create the dynamic method for the implementation
                var meth = new MethodDefinition(intMethod.Name,
                    MethodAttributes.Public | MethodAttributes.Final |
                    MethodAttributes.Virtual,
                    asm.MainModule.ImportReference(intMethod.ReturnType));
                implTyp.Methods.Add(meth);

                // The body for the dynamic method
                var proc = meth.Body.GetILProcessor();

                var i = 0;
                foreach (var param in intMethod.GetParameters())
                {
                    meth.Parameters.Add(new ParameterDefinition(param.Name, (ParameterAttributes)param.Attributes,
                        asm.MainModule.ImportReference(param.ParameterType)));

                    proc.Emit(OpCodes.Ldarg, ++i);
                }

                // Invoke the method with a Call, then return the result
                proc.Emit(OpCodes.Call, bindMeth);
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

            // Write the newly generated assembly to memory, load it, and instatiate the newly generated binding
            using (var mem = new MemoryStream())
            {
                asm.Write(mem);
                var newAsm = Assembly.Load(mem.ToArray());
                var newTyp = newAsm.GetType($"{intTyp.Name}Impl");
                return (T)newTyp.GetConstructor(new Type[0])?.Invoke(new object[0]);
            }
        }

        /// <summary>
        /// Get the corresponding <see cref="NativeType"/> for a specific <see cref="UnmanagedType"/>.
        /// </summary>
        /// <param name="unmanagedType">The unmanaged type to convert.</param>
        /// <returns></returns>
        private static NativeType GetNativeType(UnmanagedType unmanagedType)
        {
            switch (unmanagedType)
            {
                case UnmanagedType.AnsiBStr:
                    return NativeType.ANSIBStr;
                case UnmanagedType.AsAny:
                    return NativeType.ASAny;
                case UnmanagedType.Bool:
                    return NativeType.Boolean;
                case UnmanagedType.BStr:
                    return NativeType.BStr;
                case UnmanagedType.ByValArray:
                    return NativeType.FixedArray;
                case UnmanagedType.ByValTStr:
                    return NativeType.ByValStr;
                case UnmanagedType.Currency:
                    return NativeType.Currency;
                case UnmanagedType.CustomMarshaler:
                    return NativeType.CustomMarshaler;
                case UnmanagedType.Error:
                    return NativeType.Error;
                case UnmanagedType.FunctionPtr:
                    return NativeType.Func;
                case UnmanagedType.I1:
                    return NativeType.I1;
                case UnmanagedType.I2:
                    return NativeType.I2;
                case UnmanagedType.I4:
                    return NativeType.I4;
                case UnmanagedType.I8:
                    return NativeType.I8;
                case UnmanagedType.IDispatch:
                    return NativeType.IDispatch;
                case UnmanagedType.LPArray:
                    return NativeType.Array;
                case UnmanagedType.LPStr:
                    return NativeType.LPStr;
                case UnmanagedType.LPStruct:
                    return NativeType.LPStruct;
                case UnmanagedType.LPTStr:
                    return NativeType.LPTStr;
                case UnmanagedType.LPWStr:
                    return NativeType.LPWStr;
                case UnmanagedType.R4:
                    return NativeType.R4;
                case UnmanagedType.R8:
                    return NativeType.R8;
                case UnmanagedType.SafeArray:
                    return NativeType.SafeArray;
                case UnmanagedType.Struct:
                    return NativeType.Struct;
                case UnmanagedType.SysInt:
                    return NativeType.Int;
                case UnmanagedType.SysUInt:
                    return NativeType.UInt;
                case UnmanagedType.TBStr:
                    return NativeType.TBStr;
                case UnmanagedType.U1:
                    return NativeType.U1;
                case UnmanagedType.U2:
                    return NativeType.U2;
                case UnmanagedType.U4:
                    return NativeType.U4;
                case UnmanagedType.U8:
                    return NativeType.U8;
                case UnmanagedType.VariantBool:
                    return NativeType.VariantBool;
                case UnmanagedType.HString:
                case UnmanagedType.IInspectable:
                case UnmanagedType.Interface:
                case UnmanagedType.IUnknown:
                case UnmanagedType.VBByRefStr:
                    throw new ArgumentException(nameof(unmanagedType), $"Marshal type \"{unmanagedType}\" is not supported.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(unmanagedType), unmanagedType, null);
            }
        }

        /// <summary>
        /// Convert the type of SafeArray elements from System to Cecil form
        /// </summary>
        /// <param name="varEnum">The SafeArray element type</param>
        /// <returns>An equivalent Cecil version</returns>
        private static VariantType GetSafeArrayType(VarEnum varEnum)
        {
            switch (varEnum)
            {
                case VarEnum.VT_BOOL:
                    return VariantType.Bool;
                case VarEnum.VT_BSTR:
                    return VariantType.BStr;
                case VarEnum.VT_DATE:
                    return VariantType.Date;
                case VarEnum.VT_DECIMAL:
                    return VariantType.Decimal;
                case VarEnum.VT_DISPATCH:
                    return VariantType.Dispatch;
                case VarEnum.VT_ERROR:
                    return VariantType.Error;
                case VarEnum.VT_I1:
                    return VariantType.I1;
                case VarEnum.VT_I2:
                    return VariantType.I2;
                case VarEnum.VT_I4:
                    return VariantType.I4;
                case VarEnum.VT_INT:
                    return VariantType.Int;
                case VarEnum.VT_R4:
                    return VariantType.R4;
                case VarEnum.VT_R8:
                    return VariantType.R8;
                case VarEnum.VT_UI1:
                    return VariantType.UI1;
                case VarEnum.VT_UI2:
                    return VariantType.UI2;
                case VarEnum.VT_UI4:
                    return VariantType.UI4;
                case VarEnum.VT_UINT:
                    return VariantType.UInt;
                case VarEnum.VT_UNKNOWN:
                    return VariantType.Unknown;
                case VarEnum.VT_VARIANT:
                    return VariantType.Variant;
                case VarEnum.VT_FILETIME:
                case VarEnum.VT_HRESULT:
                case VarEnum.VT_EMPTY:
                case VarEnum.VT_I8:
                case VarEnum.VT_BYREF:
                case VarEnum.VT_CARRAY:
                case VarEnum.VT_CF:
                case VarEnum.VT_CLSID:
                case VarEnum.VT_CY:
                case VarEnum.VT_ARRAY:
                case VarEnum.VT_BLOB:
                case VarEnum.VT_BLOB_OBJECT:
                case VarEnum.VT_RECORD:
                case VarEnum.VT_SAFEARRAY:
                case VarEnum.VT_STORAGE:
                case VarEnum.VT_STORED_OBJECT:
                case VarEnum.VT_STREAM:
                case VarEnum.VT_STREAMED_OBJECT:
                case VarEnum.VT_UI8:
                case VarEnum.VT_USERDEFINED:
                case VarEnum.VT_LPSTR:
                case VarEnum.VT_LPWSTR:
                case VarEnum.VT_NULL:
                case VarEnum.VT_PTR:
                case VarEnum.VT_VECTOR:
                case VarEnum.VT_VOID:
                    throw new ArgumentException(nameof(varEnum));
                default:
                    throw new ArgumentOutOfRangeException(nameof(varEnum), varEnum, null);
            }
        }

        /// <summary>
        /// Get <see cref="MarshalInfo" /> data for a given <see cref="MarshalAsAttribute"/>.
        /// </summary>
        /// <param name="module">The module where this will be applied.</param>
        /// <param name="attribute">The attribute to convert.</param>
        /// <returns>A <see cref="MarshalInfo"/> equivalent to the attribute.</returns>
        private static MarshalInfo GetMarshalInfo(ModuleDefinition module, MarshalAsAttribute attribute)
        {
            switch (attribute.Value)
            {
                case UnmanagedType.AnsiBStr:
                case UnmanagedType.AsAny:
                case UnmanagedType.Bool:
                case UnmanagedType.BStr:
                case UnmanagedType.ByValTStr:
                case UnmanagedType.Currency:
                case UnmanagedType.Error:
                case UnmanagedType.FunctionPtr:
                case UnmanagedType.HString:
                case UnmanagedType.I1:
                case UnmanagedType.I2:
                case UnmanagedType.I4:
                case UnmanagedType.I8:
                case UnmanagedType.IDispatch:
                case UnmanagedType.IInspectable:
                case UnmanagedType.Interface:
                case UnmanagedType.IUnknown:
                case UnmanagedType.LPStr:
                case UnmanagedType.LPStruct:
                case UnmanagedType.LPTStr:
                case UnmanagedType.LPWStr:
                case UnmanagedType.R4:
                case UnmanagedType.R8:
                case UnmanagedType.Struct:
                case UnmanagedType.SysInt:
                case UnmanagedType.SysUInt:
                case UnmanagedType.TBStr:
                case UnmanagedType.U1:
                case UnmanagedType.U2:
                case UnmanagedType.U4:
                case UnmanagedType.U8:
                case UnmanagedType.VariantBool:
                case UnmanagedType.VBByRefStr:
                    return new MarshalInfo(GetNativeType(attribute.Value));
                case UnmanagedType.LPArray:
                    return new ArrayMarshalInfo
                    {
                        NativeType = NativeType.Array,
                        SizeParameterIndex = attribute.SizeParamIndex
                    };
                case UnmanagedType.ByValArray:
                    return new FixedArrayMarshalInfo
                    {
                        NativeType = NativeType.FixedArray,
                        Size = attribute.SizeParamIndex,
                        ElementType = GetNativeType(attribute.ArraySubType)
                    };
                case UnmanagedType.CustomMarshaler:
                    return new CustomMarshalInfo
                    {
                        Cookie = attribute.MarshalCookie,
                        NativeType = NativeType.CustomMarshaler,
                        ManagedType = module.ImportReference(attribute.MarshalTypeRef)
                    };
                case UnmanagedType.SafeArray:
                    return new SafeArrayMarshalInfo
                    {
                        NativeType = NativeType.SafeArray,
                        ElementType = GetSafeArrayType(attribute.SafeArraySubType)
                    };
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
