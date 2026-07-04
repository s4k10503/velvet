#nullable enable
namespace Velvet
{
    /// <summary>
    /// Provides the host <see cref="IHookServiceResolver"/> to descendant components through the
    /// Velvet Provider tree. <see cref="Hooks.UseService{T}"/> reads <see cref="Ref"/> and throws
    /// when no Provider has supplied a resolver.
    /// </summary>
    public static class HookServiceContext
    {
        /// <summary>
        /// Context reference used by <see cref="Hooks.UseService{T}"/>. The default value is null
        /// so that a missing Provider produces an explicit failure instead of a silent
        /// <see cref="System.NullReferenceException"/>.
        /// </summary>
        public static readonly ComponentContext<IHookServiceResolver?> Ref
            = ComponentContext<IHookServiceResolver?>.Create(defaultValue: null);
    }
}
