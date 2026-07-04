using System;

namespace Velvet.SourceGenerators.ReactiveScope
{
    /// <summary>
    /// Value type that projects a ReactiveScope dependency symbol onto an access-expression string and a type display
    /// that can be evaluated in generated code.
    /// </summary>
    /// <remarks>
    /// Placing the <see cref="Microsoft.CodeAnalysis.ISymbol"/> contained in <see cref="ReactiveScopeResult.Dependencies"/>
    /// directly onto <see cref="IncrementalValuesProvider{T}"/> would pin the Compilation and break the cache, so we convert it
    /// into this value type before propagating it through the pipeline.
    /// A null <see cref="AccessPath"/> means conversion failure (fallback); the reason tag is recorded in <see cref="FallbackReason"/>.
    /// </remarks>
    internal readonly struct DependencyAccessPath : IEquatable<DependencyAccessPath>
    {
        public DependencyAccessPath(string? accessPath, string typeDisplay, string? fallbackReason = null)
        {
            AccessPath = accessPath;
            TypeDisplay = typeDisplay ?? string.Empty;
            FallbackReason = fallbackReason;
        }

        /// <summary>
        /// Dependency expression evaluated in generated code. null means conversion failure (the corresponding slot falls back).
        /// </summary>
        public string? AccessPath { get; }

        /// <summary>
        /// Type display for the dependency expression. Fully-qualified, intended to be substituted directly into ValueTuple type arguments.
        /// Empty string on fallback (carries no meaning then).
        /// </summary>
        public string TypeDisplay { get; }

        /// <summary>
        /// Reason tag for conversion failure (null-symbol / unresolved-local / unresolvable-member, etc.). null on success.
        /// </summary>
        public string? FallbackReason { get; }

        public bool IsFallback => string.IsNullOrEmpty(AccessPath);

        public static DependencyAccessPath Fallback(string reason) =>
            new(accessPath: null, typeDisplay: string.Empty, fallbackReason: reason);

        public bool Equals(DependencyAccessPath other) =>
            string.Equals(AccessPath, other.AccessPath, StringComparison.Ordinal) &&
            string.Equals(TypeDisplay, other.TypeDisplay, StringComparison.Ordinal) &&
            string.Equals(FallbackReason, other.FallbackReason, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is DependencyAccessPath other && Equals(other);

        public override int GetHashCode()
        {
            var hash = AccessPath is null ? 0 : StringComparer.Ordinal.GetHashCode(AccessPath);
            hash = unchecked(hash * 31 + StringComparer.Ordinal.GetHashCode(TypeDisplay));
            if (FallbackReason is not null)
            {
                hash = unchecked(hash * 31 + StringComparer.Ordinal.GetHashCode(FallbackReason));
            }
            return hash;
        }
    }
}
