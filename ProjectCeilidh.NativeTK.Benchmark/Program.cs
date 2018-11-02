using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ProjectCeilidh.NativeTK.Attributes;

namespace ProjectCeilidh.NativeTK.Benchmark
{
    public class Program
    {
        [CoreJob]
        [RankColumn, MedianColumn]
        public class Benchmark
        {
            private static readonly ITestBinding IndirectBinding = BindingFactory.GetFactoryForBindingType(NativeBindingType.Indirect).CreateBinding<ITestBinding>();
            private static readonly ITestBinding StaticBinding = BindingFactory.GetFactoryForBindingType(NativeBindingType.Static).CreateBinding<ITestBinding>();

            [Benchmark]
            public void Indirect()
            {
                var ptr = IndirectBinding.GlobalAlloc(0, (IntPtr) 1024);
                IndirectBinding.GlobalFree(ptr);
            }

            [Benchmark]
            public void Static()
            {
                var ptr = StaticBinding.GlobalAlloc(0, (IntPtr)1024);
                StaticBinding.GlobalFree(ptr);
            }

            [Benchmark(Baseline = true)]
            public void Baseline()
            {
                var ptr = GlobalAlloc(0, (IntPtr)1024);
                GlobalFree(ptr);
            }

            [DllImport("kernel32", CallingConvention = CallingConvention.Winapi)]
            private static extern IntPtr GlobalAlloc(uint uFlags, IntPtr dwBytes);
            
            [DllImport("Kernel32", CallingConvention = CallingConvention.Winapi)]
            private static extern IntPtr GlobalFree(IntPtr hMem);
        }

        private static void Main()
        {
            BenchmarkRunner.Run<Benchmark>();
        }

        [NativeLibraryContract("kernel32")]
        public interface ITestBinding
        {
            [NativeImport(CallingConvention = CallingConvention.Winapi)]
            IntPtr GlobalAlloc(uint uFlags, IntPtr dwBytes);

            [NativeImport(CallingConvention = CallingConvention.Winapi)]
            IntPtr GlobalFree(IntPtr hMem);
        }
    }
}
