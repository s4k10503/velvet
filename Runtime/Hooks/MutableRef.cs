#nullable enable
namespace Velvet
{
    /// <summary>
    /// Mutable slot holder with no type constraint, for non-element values held across renders.
    /// </summary>
    /// <seealso cref="Ref{T}"/>
    /// <typeparam name="T">Stored value type. May be a value type or a reference type.</typeparam>
    public sealed class MutableRef<T>
    {
        /// <summary>
        /// Creates a new <see cref="MutableRef{T}"/> seeded with <paramref name="initial"/>.
        /// </summary>
        public MutableRef(T initial)
        {
            Current = initial;
        }

        /// <summary>
        /// Current value. Writes do not trigger a re-render.
        /// </summary>
        public T Current { get; set; }
    }
}
