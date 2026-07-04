#nullable enable
namespace Velvet
{
    /// <summary>
    /// Typed context definition that lets a value be supplied by an ancestor Provider and read by
    /// any descendant via <c>UseContext</c>.
    /// Holds the default value returned when no Provider is configured.
    /// </summary>
    /// <typeparam name="T">Type of the value carried by the context.</typeparam>
    public sealed class ComponentContext<T>
    {
        /// <summary>Value returned by <c>UseContext</c> when no Provider for this context exists above the consumer.</summary>
        public T? DefaultValue { get; }

        internal ComponentContext(T? defaultValue) => DefaultValue = defaultValue;

        /// <summary>
        /// Creates a context with the given default value.
        /// </summary>
        /// <param name="defaultValue">Value returned by <c>UseContext</c> when no Provider is configured above the consumer.</param>
        /// <returns>The created <see cref="ComponentContext{T}"/>.</returns>
        public static ComponentContext<T> Create(T? defaultValue = default) => new(defaultValue);
    }
}
