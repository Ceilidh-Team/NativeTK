using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.NativeTK.Native
{
    internal abstract class NativeLibraryHandle
    {
        public T GetDelegateForSymbol<T>(string symbol, bool throwOnError = true) where T : Delegate
        {
            var addr = GetSymbolAddress(symbol);

            if (addr == IntPtr.Zero)
            {
                if (throwOnError) throw new EntryPointNotFoundException($"Could not find symbol \"{symbol}\"");
                return default;
            }

            try
            {
                return Marshal.GetDelegateForFunctionPointer<T>(addr);
            }
            catch (MarshalDirectiveException)
            {
                if (throwOnError) throw;
                return default;
            }
        }

        public unsafe ref T GetSymbolReference<T>(string symbol) where T : struct
        {
            var addr = GetSymbolAddress(symbol);
            
            if (addr == IntPtr.Zero) throw new EntryPointNotFoundException($"Could not find symbol \"{symbol}\"");
            
            return ref Unsafe.AsRef<T>(addr.ToPointer());
        }

        protected abstract IntPtr GetSymbolAddress(string symbol);
    }
}