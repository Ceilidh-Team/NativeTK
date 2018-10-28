using System;
using System.Runtime.InteropServices;
using ProjectCeilidh.NativeTK.Attributes;
using Xunit;

namespace ProjectCeilidh.NativeTK.Tests
{
    public class BindingTests
    {
        [return: MarshalAs(UnmanagedType.SysInt)]
        private delegate IntPtr TestDelegate([MarshalAs(UnmanagedType.I4)] int i);

        [Fact]
        public void UnsafeBindingTest()
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

        [Fact]
        public void SafeBindingTest()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var binding = BindingFactory.CreateSafeBinding<ITestBindingWindows>();
                Assert.NotEqual(IntPtr.Zero, binding.GetStdHandle(-11));
                ref var _ = ref binding.GetCommandLineWRef;
            }
            else
            {
                var binding = BindingFactory.CreateSafeBinding<ITestBindingUnix>();
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
