using System;
using System.Runtime.InteropServices;
using ProjectCeilidh.NativeTK.Attributes;
using Xunit;

namespace ProjectCeilidh.NativeTK.Tests
{
    public class BindingTests
    {
        [Fact]
        public void BindingTest()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var binding = BindingFactory.CreateBinding<ITestBindingWindows>();
                Assert.NotEqual(IntPtr.Zero, binding.GetStdHandle(-11));
                ref var _ = ref binding.GetCommandLineWRef;
            }
            else
            {
                var binding = BindingFactory.CreateBinding<ITestBindingUnix>();
                Assert.Equal(IntPtr.Zero, binding.dlopen("", 0));
                ref var _ = ref binding.dlsym;
            }
        }

        [NativeLibraryContract("dl", VersionString = "2.0.0")]
        public interface ITestBindingUnix
        {
            [NativeImport]
            ref IntPtr dlsym { get; }

            [NativeImport]
            IntPtr dlopen(string path, int flag);
        }

        [NativeLibraryContract("kernel32")]
        public interface ITestBindingWindows
        {
            [NativeImport(EntryPoint = "GetCommandLineW")]
            ref IntPtr GetCommandLineWRef { get; }

            [NativeImport(SetLastError = true)]
            IntPtr GetStdHandle(int nStdHandle);


        }
    }
}
