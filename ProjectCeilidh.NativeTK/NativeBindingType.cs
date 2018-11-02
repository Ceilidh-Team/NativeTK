namespace ProjectCeilidh.NativeTK
{
    /// <summary>
    /// The strategy used to create the native binding
    /// </summary>
    public enum NativeBindingType
    {
        /// <summary>
        /// Bind native functions using the CallIndirect opcode.
        /// This has poor compatibility and does not support marshaling options
        /// </summary>
        Indirect = 1,
        /// <summary>
        /// Bind native functions using PInvokeImpl.
        /// This has the best compatibility, supporting marshaling options, but is slightly slower.
        /// </summary>
        Static = 2
    }
}
