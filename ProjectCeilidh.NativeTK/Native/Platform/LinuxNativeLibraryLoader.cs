using System;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.NativeTK.Native.Platform
{
    internal class LinuxNativeLibraryLoader : NativeLibraryLoader
    {
        private const int RTLD_NOW = 0x002;
        private const string LIBDL = "libdl";
        
        protected override string[] GetNativeLibraryNames(string libraryName, Version version)
        {
            if (version == null)
                return new[]
                {
                    $"lib{libraryName}.so"
                };

            return new[]
            {
                $"lib{libraryName}.so.{version.Major}.{version.Minor}.{version.Build}",
                $"lib{libraryName}.so.{version.Major}.{version.Minor}",
                $"lib{libraryName}.so.{version.Major}"
            };
    }

        protected override NativeLibraryHandle LoadNativeLibrary(string libraryName)
        {
            var handle = dlopen(libraryName, RTLD_NOW);
            return handle == IntPtr.Zero ? null : new LinuxNativeLibraryHandle(libraryName, handle);
        }
        
        [DllImport(LIBDL)]
        private static extern IntPtr dlopen(string path, int flag);
        [DllImport(LIBDL)]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);
        
        private class LinuxNativeLibraryHandle : NativeLibraryHandle
        {
            private readonly IntPtr _handle;
            
            public LinuxNativeLibraryHandle(string path, IntPtr handle) : base(path)
            {
                _handle = handle;
            }

            public override IntPtr GetSymbolAddress(string symbol) => dlsym(_handle, symbol);
        }
    }
}