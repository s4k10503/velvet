using System;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace Velvet
{
    /// <summary>
    /// Where the props comparers the source generator emits for <c>[Component(Memoize = true)]</c> are
    /// registered, and the one comparison they call back into.
    /// </summary>
    /// <remarks>
    /// A props type with no comparer registered keeps the comparison <see cref="ComponentAttribute.Memoize"/>
    /// documents, reached by the route its runtime has for it. The generator's member rules and what makes it
    /// emit nothing for a type are in the memoization guide.
    /// </remarks>
    public static class PropsComparerRegistry
    {
        private static readonly ConcurrentDictionary<Type, Func<object, object, bool>> s_comparers = new();

        /// <summary>
        /// Registers the comparer emitted for <paramref name="propsType"/>. Invoked from the module initializer
        /// the generator emits. Idempotent: registering the same type again replaces the comparer.
        /// </summary>
        /// <param name="propsType">The exact runtime type of the props the comparer decides.</param>
        /// <param name="comparer">Both arguments are instances of <paramref name="propsType"/>.</param>
        // Hidden from IntelliSense: this entry point is for generated code, not user code.
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Register(Type propsType, Func<object, object, bool> comparer)
        {
            s_comparers[propsType] = comparer;
        }

        /// <summary>
        /// The comparison the props walk applies to a member whose declared type does not fix its runtime type
        /// (<c>object</c>, an interface), and to a props value. Called by generated code.
        /// </summary>
        /// <param name="prev">The previous value.</param>
        /// <param name="next">The next value.</param>
        /// <returns><c>true</c> when the two are equal under the props comparison.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static bool ValueEquals(object? prev, object? next) => ComponentPropsComparer.ValueEquals(prev, next);

        internal static bool TryGet(Type propsType, out Func<object, object, bool> comparer)
            => s_comparers.TryGetValue(propsType, out comparer!);
    }
}
