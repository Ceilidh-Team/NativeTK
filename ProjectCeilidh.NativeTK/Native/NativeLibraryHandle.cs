using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.NativeTK.Native
{
    public abstract class NativeLibraryHandle
    {
        internal NativeLibraryHandle() { }

        /// <summary>
        /// Get a delegate for the function represented by a given symbol.
        /// </summary>
        /// <typeparam name="T">The type of the delegate this symbol is</typeparam>
        /// <param name="symbol">The symbol to resolve</param>
        /// <param name="throwOnError">If true, an exception will be thrown if the entry point is not found. False, null will be returned.</param>
        /// <returns>A delegate that can be called to invoke the unmanaged method.</returns>
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

        /// <summary>
        /// Get a reference to the static variable for a specific symbol.
        /// </summary>
        /// <typeparam name="T">The type of the static variable</typeparam>
        /// <param name="symbol">The name of the symbol representing the variable</param>
        /// <returns>A reference to the specified static variable</returns>
        public unsafe ref T GetSymbolReference<T>(string symbol) where T : struct
        {
            var addr = GetSymbolAddress(symbol);
            
            if (addr == IntPtr.Zero) throw new EntryPointNotFoundException($"Could not find symbol \"{symbol}\"");
            
            return ref Unsafe.AsRef<T>(addr.ToPointer());
        }

        /// <summary>
        /// Get the address of a specific symbol
        /// </summary>
        /// <param name="symbol">The name of the symbol to get an address for</param>
        /// <returns>The address of the symbol - <see cref="IntPtr.Zero"/> if it wasn't found</returns>
        public abstract IntPtr GetSymbolAddress(string symbol);
    }
}