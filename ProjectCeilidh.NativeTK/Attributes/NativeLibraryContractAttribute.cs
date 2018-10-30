using System;

namespace ProjectCeilidh.NativeTK.Attributes
{
    /// <summary>
    /// Designate an interface as being a contract for a native library.
    /// </summary>
    /// <inheritdoc />
    [AttributeUsage(AttributeTargets.Interface)]
    public class NativeLibraryContractAttribute : Attribute
    {
        /// <summary>
        /// The name of the library. Exclude the "lib" prefix for UNIX systems.
        /// </summary>
        public string LibraryName { get; }

        /// <summary>
        /// The version of the library, as a string.
        /// </summary>
        public string VersionString = "0.0.0";
        /// <summary>
        /// A version object reperesenting the version string.
        /// </summary>
        public Version Version => VersionString == null ? null : Version.Parse(VersionString);

        public NativeLibraryContractAttribute(string libraryName)
        {
            LibraryName = libraryName;
        }
    }
}
