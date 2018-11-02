using System;

namespace ProjectCeilidh.NativeTK
{
    /// <summary>
    /// Responsible for implementing a native binding interface.
    /// </summary>
    public abstract class BindingFactory
    {
        /// <summary>
        /// The type of native binding this factory generates.
        /// </summary>
        public abstract NativeBindingType BindingType { get; }

        internal BindingFactory() { }

        /// <summary>
        /// Implement a binding contract.
        /// </summary>
        /// <typeparam name="T">The binding contract type to implement.</typeparam>
        /// <returns>An implementation of the specified binding contract.</returns>
        public T CreateBinding<T>() where T : class => (T) CreateBinding(typeof(T));

        /// <summary>
        /// Implement a binding contract.
        /// </summary>
        /// <param name="bindingContract">The binding contract type to implement.</param>
        /// <returns>An implementation of the specified binding contract.</returns>
        public abstract object CreateBinding(Type bindingContract);

        /// <summary>
        /// Get a factory that can create bindings of the specified type.
        /// </summary>
        /// <param name="bindingType">The desired strategy for creating bindings.</param>
        /// <returns>A binding factory that can create bindings using the desired strategy.</returns>
        public static BindingFactory GetFactoryForBindingType(NativeBindingType bindingType)
        {
            switch (bindingType)
            {
                case NativeBindingType.Static:
                    return new StaticBindingFactory();
                case NativeBindingType.Indirect:
                    return new IndirectBindingFactory();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
