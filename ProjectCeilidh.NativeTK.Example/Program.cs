using System;
using ProjectCeilidh.NativeTK.Attributes;

namespace ProjectCeilidh.NativeTK.Example
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var ebur128 = BindingFactory.GetFactoryForBindingType(NativeBindingType.Static).CreateBinding<IEbuR128>();
            ebur128.GetVersion(out var major, out var minor, out var patch);
            Console.WriteLine($"Version {major}.{minor}.{patch}");

            var state = ebur128.Init(2, 44100, EbuR128Modes.Global);

            Console.WriteLine($"Mode: {state.Mode}");
            Console.WriteLine($"Is allocated? {state.IsAllocated()}");

            ebur128.Destroy(ref state);

            Console.WriteLine($"Is allocated? {state.IsAllocated()}");
        }
    }

    [NativeLibraryContract("ebur128", VersionString = "1.2.4")]
    public interface IEbuR128
    {
        [NativeImport(EntryPoint = "ebur128_get_version")]
        void GetVersion(out int major, out int minor, out int patch);

        [NativeImport(EntryPoint = "ebur128_init")]
        State Init(uint channels, ulong sampleRate, EbuR128Modes ebuR128Modes);

        [NativeImport(EntryPoint = "ebur128_destroy")]
        void Destroy(ref State state);

        [NativeImport(EntryPoint = "ebur128_add_frames_short")]
        EbuR128Error AddFrames(State state, short[] src, IntPtr frames);

        [NativeImport(EntryPoint = "ebur128_add_frames_int")]
        EbuR128Error AddFrames(State state, int[] src, IntPtr frames);

        [NativeImport(EntryPoint = "ebur128_add_frames_float")]
        EbuR128Error AddFrames(State state, float[] src, IntPtr frames);

        [NativeImport(EntryPoint = "ebur128_add_frames_double")]
        EbuR128Error AddFrames(State state, double[] src, IntPtr frames);

        [NativeImport(EntryPoint = "ebur128_loudness_global")]
        EbuR128Error GetGlobalLoudness(State state, out double loudness);

        [NativeImport(EntryPoint = "ebur128_loudness_momentary")]
        EbuR128Error GetMomentaryLoudness(State state, out double loudness);

        [NativeImport(EntryPoint = "ebur128_loudness_shortterm")]
        EbuR128Error GetShortTermLoudness(State state, out double loudness);
    }

    public enum EbuR128Error
    {
        Success = 0,
        NoMem,
        InvalidMode,
        InvalidChannelIndex,
        NoChange
    }

    [Flags]
    public enum EbuR128Modes
    {
        Momentary = 1 << 0,
        ShortTerm = (1 << 1) | Momentary,
        Global = (1 << 2) | Momentary,
        LoudnessRange = (1 << 3) | ShortTerm,
        SamplePeak = (1 << 4) | Momentary,
        TruePeak = (1 << 5) | Momentary,
        Histogram = 1 << 6
    }

    public unsafe struct State
    {
        public EbuR128Modes Mode
        {
            get => _ptr->Mode;
            set => _ptr->Mode = value;
        }

        private readonly StateInternal* _ptr;

        internal State(IntPtr ptr)
        {
            _ptr = (StateInternal*) ptr;
        }

        public bool IsAllocated() => _ptr != null;

        private struct StateInternal
        {
            public EbuR128Modes Mode;
        }
    }
}
