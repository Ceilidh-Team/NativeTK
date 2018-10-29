using System;
using System.Runtime.InteropServices;
using ProjectCeilidh.NativeTK.Attributes;
using Xunit;

namespace ProjectCeilidh.NativeTK.Tests
{
    public class BindingTests
    {
        [Theory]
        [InlineData(NativeBindingType.Indirect)]
        [InlineData(NativeBindingType.Static)]
        public void BindingTest(NativeBindingType bindingType)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var binding = BindingFactory.CreateBinding<ITestBindingWindows>(bindingType);
                Assert.NotEqual(IntPtr.Zero, binding.GetStdHandle(-11));
                ref var _ = ref binding.GetCommandLineWRef;
            }
            else
            {
                var binding = BindingFactory.CreateBinding<ITestBindingUnix>(bindingType);
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

            [NativeImport(SetLastError = true, CallingConvention = CallingConvention.Winapi)]
            IntPtr GetStdHandle(int nStdHandle);
        }
    }
}
