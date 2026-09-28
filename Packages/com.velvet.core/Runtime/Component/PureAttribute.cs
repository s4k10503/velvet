using System;

namespace Velvet
{
    /// <summary>
    /// Marks a method as pure. Nothing in Velvet reads it.
    /// </summary>
    /// <remarks>
    /// Because <c>Inherited = false</c>, overriding methods do not inherit the base declaration.
    /// Derived methods that claim purity must explicitly re-apply the attribute.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class PureAttribute : Attribute
    {
    }
}
