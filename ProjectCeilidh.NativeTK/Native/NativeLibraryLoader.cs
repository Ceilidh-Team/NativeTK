using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ProjectCeilidh.NativeTK.Native.Platform;

namespace ProjectCeilidh.NativeTK.Native
{
    /// <summary>
    /// Encapsulates the ability to load a native library into the current process
    /// </summary>
    public abstract class NativeLibraryLoader
    {
        internal NativeLibraryLoader() { }

        /// <summary>
        /// Load a native library from the current PATH with the given name and version.
        /// </summary>
        /// <remarks>Exclude "lib" on Unix-like systems</remarks>
        /// <param name="libraryName">The name of the library to load</param>
        /// <param name="version">The version of the library to load</param>
        /// <returns>A library handle for the specified library.</returns>
        public NativeLibraryHandle LoadNativeLibrary(string libraryName, Version version) => LoadNativeLibrary("", libraryName, version);
        
        /// <summary>
        /// Load a native library from a specific location with the given name and version.
        /// </summary>
        /// <param name="path">The path to the library</param>
        /// <param name="libraryName">The name of the library to load</param>
        /// <param name="version">The version of the library to load</param>
        /// <returns>A library handle for the specified library</returns>
        public NativeLibraryHandle LoadNativeLibrary(string path, string libraryName, Version version)
        {
            var handle = GetNativeLibraryNames(libraryName, version)
                .Select(x => LoadNativeLibrary(Path.Combine(path, x))).FirstOrDefault(x => x != null);
            
            if (handle == null) throw new DllNotFoundException($"Could not find library \"{libraryName}.{version.Major}.{version.Minor}.{version.Build}\"");

            return handle;
        }
        
        protected abstract string[] GetNativeLibraryNames(string libraryName, Version version);
        protected abstract NativeLibraryHandle LoadNativeLibrary(string libraryName);

        /// <summary>
        /// Get a library loader that can load native binaries for the current platform
        /// </summary>
        /// <returns>A library loader for the current platform</returns>
        public static NativeLibraryLoader GetLibraryLoaderForPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return new LinuxNativeLibraryLoader();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return new MacNativeLibraryLoader();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new WindowsNativeLibraryLoader();

            throw new PlatformNotSupportedException();
        }
    }
}