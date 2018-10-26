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
                Assert.NotNull(binding.GetCommandLineA());
                ref var _ = ref binding.GetCommandLineW;
            }
            else
            {
                var binding = BindingFactory.CreateBinding<ITestBindingUnix>();
                Assert.Equal(IntPtr.Zero, binding.dlopen("", 0));
                ref var _ = ref binding.dlsym;
            }
        }

        [NativeLibraryContract("dl", VersionString = "1.0.0")]
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
            [NativeImport]
            ref IntPtr GetCommandLineW { get; }

            [NativeImport(SetLastError = true)]
            string GetCommandLineA();


        }
    }
}
