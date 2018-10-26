using System;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.NativeTK.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public class NativeImportAttribute : Attribute
    {
        public CallingConvention CallingConvention = CallingConvention.Cdecl;
        public CharSet CharSet = CharSet.Ansi;
        public string EntryPoint;
        public bool BestFitMapping = true;
        public bool SetLastError = false;
        public bool ThrowOnUnmappableChar = true;
    }
}