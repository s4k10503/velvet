using System;

namespace Velvet
{
    /// <summary>
    /// Marker attribute used by the Source Generator / Analyzer to treat a method as pure.
    /// Velvet can declare purity standalone, without depending on
    /// <see cref="System.Diagnostics.Contracts.PureAttribute"/> or JetBrains Annotations.
    /// The declaration is trusted and not verified.
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
