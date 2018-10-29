using System;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.NativeTK.Native.Platform
{
    internal class MacNativeLibraryLoader : NativeLibraryLoader
    {
        private const int RTLD_NOW = 0x002;
        private const string LIBDL = "libdl";
        
        protected override string[] GetNativeLibraryNames(string libraryName, Version version)
        {
            return new[]
            {
                $"lib{libraryName}.{version.Major}.{version.Minor}.{version.Build}.dylib",
                $"lib{libraryName}.{version.Major}.{version.Minor}.dylib",
                $"lib{libraryName}.{version.Major}.dylib",
                $"lib{libraryName}.dylib"
            };
        }

        protected override NativeLibraryHandle LoadNativeLibrary(string libraryName)
        {
            var handle = dlopen(libraryName, RTLD_NOW);
            return handle == IntPtr.Zero ? null : new MacNativeLibraryHandle(libraryName, handle);
        }
        
        [DllImport(LIBDL)]
        private static extern IntPtr dlopen(string path, int flag);
        [DllImport(LIBDL)]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);
        
        private class MacNativeLibraryHandle : NativeLibraryHandle
        {
            private readonly IntPtr _handle;
            
            public MacNativeLibraryHandle(string path, IntPtr handle) : base(path)
            {
                _handle = handle;
            }

            public override IntPtr GetSymbolAddress(string symbol) => dlsym(_handle, symbol);
        }
    }
}