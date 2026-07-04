using System;

namespace Velvet
{
    /// <summary>
    /// Marks a method as a per-method auto-memoization target recognized by the Source Generator.
    /// The annotated partial method is expanded into a <c>V.Memoized(...)</c> wrapper.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class MemoizeAttribute : Attribute
    {
    }
}
