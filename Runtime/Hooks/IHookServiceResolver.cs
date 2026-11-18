namespace Velvet
{
    /// <summary>
    /// Resolver abstraction that fetches services from the host DI container.
    /// Decouples DI-framework-specific implementations from the framework core.
    /// </summary>
    public interface IHookServiceResolver
    {
        /// <summary>
        /// Resolves an instance of <typeparamref name="T"/> from the host container.
        /// Implementations must return a non-null instance or throw; returning null violates the
        /// contract and will be turned into an <see cref="System.InvalidOperationException"/> by
        /// <see cref="Hooks.UseService{T}"/>.
        /// </summary>
        /// <typeparam name="T">Service contract type.</typeparam>
        /// <returns>The resolved service instance. Must not be null.</returns>
        /// <exception cref="System.InvalidOperationException">
        /// Thrown when the host container cannot provide an instance of <typeparamref name="T"/>.
        /// </exception>
        T Resolve<T>() where T : class;
    }
}
