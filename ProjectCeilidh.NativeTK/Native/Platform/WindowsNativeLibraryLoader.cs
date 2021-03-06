using System;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.NativeTK.Native.Platform
{
    internal class WindowsNativeLibraryLoader : NativeLibraryLoader
    {
        private const string KERNEL32 = "kernel32";
        
        protected override string[] GetNativeLibraryNames(string libraryName, Version version)
        {
            var archDescription = IntPtr.Size == 4 ? "x86" : "x64";

            if (version == null)
                return new[]
                {
                    $"{libraryName}.dll",
                    $"lib{libraryName}.dll",
                    $"{libraryName}_{archDescription}.dll",
                    $"lib{libraryName}_{archDescription}.dll"
                };

            return new[]
            {
                $"{libraryName}-{version.Major}.dll",
                $"lib{libraryName}-{version.Major}.dll",
                $"{libraryName}-{version.Major}_{archDescription}.dll",
                $"lib{libraryName}-{version.Major}_{archDescription}.dll",
                $"{libraryName}.dll",
                $"lib{libraryName}.dll",
                $"{libraryName}_{archDescription}.dll",
                $"lib{libraryName}_{archDescription}.dll"
            };
        }

        protected override NativeLibraryHandle LoadNativeLibrary(string libraryName)
        {
            var handle = LoadLibrary(libraryName);
            return handle == IntPtr.Zero ? null : new WindowsNativeLibraryHandle(libraryName, handle);
        }

        [DllImport(KERNEL32, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        [DllImport(KERNEL32, CharSet = CharSet.Ansi, BestFitMapping = false)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
        
        private class WindowsNativeLibraryHandle : NativeLibraryHandle
        {
            private readonly IntPtr _handle;
            
            public WindowsNativeLibraryHandle(string path, IntPtr handle) : base(path)
            {
                _handle = handle;
            }

            public override IntPtr GetSymbolAddress(string symbol) => GetProcAddress(_handle, symbol);
        }
    }
}