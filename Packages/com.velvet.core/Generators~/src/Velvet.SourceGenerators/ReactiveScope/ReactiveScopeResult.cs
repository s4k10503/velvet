using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Velvet.SourceGenerators.ReactiveScope
{
    /// <summary>
    /// Reactive Scope diagnostic kinds. Classifies the reason for a null fallback.
    /// </summary>
    internal enum ScopeDiagnosticKind
    {
        UnknownCall,
        Impure,
        Virtual,
        DynamicAccess,
        UnresolvableSymbol,
    }

    /// <summary>
    /// One reason that Reactive Scope analysis fell back to null.
    /// Holding Location directly would pin the SyntaxTree in the Incremental Generator cache, so as with <see cref="ImpurityReason"/>
    /// it is decomposed into (filePath, TextSpan, LinePositionSpan).
    /// </summary>
    internal readonly struct ScopeDiagnostic : IEquatable<ScopeDiagnostic>
    {
        private readonly string _filePath;
        private readonly TextSpan _textSpan;
        private readonly LinePositionSpan _lineSpan;

        public ScopeDiagnostic(ScopeDiagnosticKind kind, string symbolDisplay, Location? location)
        {
            Kind = kind;
            SymbolDisplay = symbolDisplay ?? string.Empty;
            var fileSpan = location?.GetLineSpan() ?? default;
            _filePath = fileSpan.Path ?? string.Empty;
            _textSpan = location?.SourceSpan ?? default;
            _lineSpan = fileSpan.Span;
        }

        public ScopeDiagnosticKind Kind { get; }
        public string SymbolDisplay { get; }

        public Location? ToLocation() =>
            string.IsNullOrEmpty(_filePath)
                ? null
                : Location.Create(_filePath, _textSpan, _lineSpan);

        public bool Equals(ScopeDiagnostic other) =>
            Kind == other.Kind &&
            string.Equals(SymbolDisplay, other.SymbolDisplay, StringComparison.Ordinal) &&
            string.Equals(_filePath, other._filePath, StringComparison.Ordinal) &&
            _textSpan == other._textSpan &&
            _lineSpan.Equals(other._lineSpan);

        public override bool Equals(object? obj) => obj is ScopeDiagnostic other && Equals(other);

        public override int GetHashCode()
        {
            var hash = (int)Kind;
            hash = unchecked(hash * 31 + StringComparer.Ordinal.GetHashCode(SymbolDisplay));
            hash = unchecked(hash * 31 + (string.IsNullOrEmpty(_filePath) ? 0 : StringComparer.Ordinal.GetHashCode(_filePath)));
            hash = unchecked(hash * 31 + _textSpan.Start);
            return hash;
        }
    }

    /// <summary>
    /// Reactive Scope analysis result.
    /// A null <see cref="Dependencies"/> is interpreted as the conservative fallback = "all dependencies", so the corresponding scope
    /// is not memoized. False positives (all-deps) are tolerated; false negatives (missed deps) are not.
    /// </summary>
    internal readonly struct ReactiveScopeResult : IEquatable<ReactiveScopeResult>
    {
        public ReactiveScopeResult(ImmutableArray<ISymbol>? dependencies, ImmutableArray<ScopeDiagnostic> reasons)
        {
            Dependencies = dependencies;
            Reasons = reasons.IsDefault ? ImmutableArray<ScopeDiagnostic>.Empty : reasons;
        }

        /// <summary>
        /// Set of dependency symbols. null represents the conservative fallback.
        /// </summary>
        /// <remarks>
        /// <see cref="ISymbol"/> references <see cref="Compilation"/>, so placing it directly onto an <c>IncrementalValuesProvider</c>
        /// would pin the Compilation and break the cache. <see cref="ReactiveScopeAnalyzer.GetAccessPath"/> converts each symbol
        /// into the cache-safe <see cref="DependencyAccessPath"/> before propagating it through a pipeline.
        /// </remarks>
        public ImmutableArray<ISymbol>? Dependencies { get; }

        /// <summary>
        /// Reasons that led to a null fallback. Empty when the dependency set is determined.
        /// </summary>
        public ImmutableArray<ScopeDiagnostic> Reasons { get; }

        public static ReactiveScopeResult Empty() =>
            new(ImmutableArray<ISymbol>.Empty, ImmutableArray<ScopeDiagnostic>.Empty);

        public static ReactiveScopeResult Pure(ImmutableArray<ISymbol> dependencies) =>
            new(dependencies, ImmutableArray<ScopeDiagnostic>.Empty);

        public static ReactiveScopeResult Fallback(ImmutableArray<ScopeDiagnostic> reasons) =>
            new(null, reasons);

        public bool Equals(ReactiveScopeResult other)
        {
            if (Dependencies.HasValue != other.Dependencies.HasValue)
            {
                return false;
            }
            if (Dependencies.HasValue &&
                !Dependencies.Value.SequenceEqual(other.Dependencies!.Value, SymbolEqualityComparer.Default))
            {
                return false;
            }
            return Reasons.SequenceEqual(other.Reasons);
        }

        public override bool Equals(object? obj) => obj is ReactiveScopeResult other && Equals(other);

        public override int GetHashCode()
        {
            var hash = Dependencies.HasValue ? Dependencies.Value.Length : -1;
            if (Dependencies.HasValue)
            {
                foreach (var dep in Dependencies.Value)
                {
                    // ToDisplayString gives a fully-qualified representation that disambiguates same-named symbols. Match the sort key used elsewhere.
                    hash = unchecked(hash * 31 + StringComparer.Ordinal.GetHashCode(dep.ToDisplayString()));
                }
            }
            hash = unchecked(hash * 31 + Reasons.Length);
            foreach (var reason in Reasons)
            {
                hash = unchecked(hash * 31 + reason.GetHashCode());
            }
            return hash;
        }
    }
}
