using System;

namespace Velvet
{
    /// <summary>
    /// Marks a method as a per-method auto-memoization target recognized by the Source Generator.
    /// The annotated partial method is expanded into a <c>V.Memoized(...)</c> wrapper.
    /// </summary>
    /// <remarks>
    /// Unrelated to <see cref="ComponentAttribute.Memoize"/>: this attribute drives per-method wrapping by the
    /// Source Generator, while <see cref="ComponentAttribute.Memoize"/> is a per-component props-bail flag at
    /// the reconcile boundary. The two share a name but govern independent mechanisms.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class MemoizeMethodAttribute : Attribute
    {
    }
}
