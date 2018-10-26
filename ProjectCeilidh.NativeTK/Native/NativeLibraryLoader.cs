using System;
using System.IO;
using System.Linq;

namespace ProjectCeilidh.NativeTK.Native
{
    internal abstract class NativeLibraryLoader
    {
        public NativeLibraryHandle LoadNativeLibrary(string libraryName, Version version) => LoadNativeLibrary("", libraryName, version);
        
        public NativeLibraryHandle LoadNativeLibrary(string path, string libraryName, Version version)
        {
            var handle = GetNativeLibraryNames(libraryName, version)
                .Select(x => LoadNativeLibrary(Path.Combine(path, x))).FirstOrDefault(x => x != null);
            
            if (handle == null) throw new DllNotFoundException($"Could not find library \"{libraryName}.{version.Major}.{version.Minor}.{version.Build}\"");

            return handle;
        }
        
        protected abstract string[] GetNativeLibraryNames(string libraryName, Version version);
        protected abstract NativeLibraryHandle LoadNativeLibrary(string libraryName);
    }
}