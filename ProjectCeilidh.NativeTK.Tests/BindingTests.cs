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
            var factory = BindingFactory.GetFactoryForBindingType(bindingType);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var binding = factory.CreateBinding<ITestBindingWindows>();
                Assert.NotEqual(IntPtr.Zero, binding.GetStdHandle(-11));
                ref var _ = ref binding.GetCommandLineWRef;
            }
            else
            {
                var binding = factory.CreateBinding<ITestBindingUnix>();
                Assert.Equal(IntPtr.Zero, binding.dlopen("", 0));
                ref var _ = ref binding.dlsym;
            }
        }

        [NativeLibraryContract("dl")]
        public interface ITestBindingUnix
        {
            [NativeImport]
            ref IntPtr dlsym { get; }

            [NativeImport]
            [return: MarshalAs(UnmanagedType.SysInt)]
            IntPtr dlopen([MarshalAs(UnmanagedType.LPStr)] string path, [MarshalAs(UnmanagedType.I4)] int flag);
        }

        [NativeLibraryContract("kernel32")]
        public interface ITestBindingWindows
        {
            [NativeImport(EntryPoint = "GetCommandLineW")]
            ref IntPtr GetCommandLineWRef { get; }

            [NativeImport(SetLastError = true, CallingConvention = CallingConvention.Winapi)]
            [return: MarshalAs(UnmanagedType.SysInt)]
            IntPtr GetStdHandle([MarshalAs(UnmanagedType.I4)] int nStdHandle);
        }
    }
}
