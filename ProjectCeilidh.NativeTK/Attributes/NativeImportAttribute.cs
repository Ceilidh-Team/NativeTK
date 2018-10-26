using System;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.NativeTK.Attributes
{
    public class NativeImportAttribute : Attribute
    {
        public string LibraryName { get; }
        
        public string VersionString;
        public Version Version => VersionString == null ? null : Version.Parse(VersionString);
        public CallingConvention CallingConvention = CallingConvention.Cdecl;
        public CharSet CharSet = CharSet.Ansi;
        public string EntryPoint;
        public bool BestFitMapping = true;
        public bool SetLastError = false;
        public bool ThrowOnUnmappableChar = true;
        
        public NativeImportAttribute(string libraryName)
        {
            LibraryName = libraryName;
        }
    }
}