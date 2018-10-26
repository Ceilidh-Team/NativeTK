using System;

namespace ProjectCeilidh.NativeTK.Attributes
{
    public class NativeLibraryContractAttribute : Attribute
    {
        public string LibraryName { get; }

        public string VersionString = "0.0.0";
        public Version Version => VersionString == null ? null : Version.Parse(VersionString);

        public NativeLibraryContractAttribute(string libraryName)
        {
            LibraryName = libraryName;
        }
    }
}
