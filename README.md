# NativeTK

NativeTK is a toolkit for cross-platform native bindings in .NET.

To learn how to use NativeTK, check out the example binding [here](./ProjectCeilidh.NativeTK.Example).

## Benefits

* Cross-platform: NativeTK can locate a native library using a variety of common naming schemes for each platform, and even bind specific versions of a library.
* Lightweight: NativeTK generates high-performance bindings using [Mono.Cecil](https://github.com/jbevain/cecil).

## Downsides

* Interface performance: Because NativeTK uses interfaces for abstraction, there's a slight performance cost on each call when compared with static P/Invoke methods.
  * If performance is a major concern for you, check out the benchmarks [here](./ProjectCeilidh.NativeTK.Benchmark).