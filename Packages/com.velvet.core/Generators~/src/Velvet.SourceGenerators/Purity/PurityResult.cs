using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Velvet.SourceGenerators.PurityAnalysis
{
    /// <summary>
    /// Three-valued result of method purity analysis. Cases that cannot be classified fall through to <see cref="Unknown"/>,
    /// which callers are expected to treat conservatively as Impure.
    /// </summary>
    internal enum Purity
    {
        Pure,
        Impure,
        Unknown,
    }

    /// <summary>
    /// Kind of detected side effect.
    /// </summary>
    /// <remarks>
    /// <see cref="ClosureCapture"/> records mutation candidates only. Captures of immutable value types
    /// (primitives, <see cref="string"/>, decimal, enums, and <c>readonly</c> structs) are treated as freshening
    /// (read-only snapshots) and are not reported as side effects. Captures of mutable reference types and
    /// non-readonly structs continue to be reported.<br/>
    /// <see cref="Loop"/> lumps for / foreach / while / do-while together. The LoopKind string is held in SymbolDisplay,
    /// so the detail is recoverable. The enum can be split if more granularity becomes necessary.
    /// </remarks>
    internal enum ImpurityKind
    {
        Assignment,
        RefOutInParam,
        Throw,
        Loop,
        KnownImpureCall,
        UnknownCall,
        VirtualCall,
        ClosureCapture,
    }

    /// <summary>
    /// One reason for impurity. Holding a Location reference directly would pin the SyntaxTree in the incremental cache,
    /// so it is held as a value-type tuple of (filePath, TextSpan, LinePositionSpan) and reconstructed via <see cref="ToLocation"/> on demand.
    /// </summary>
    internal readonly struct ImpurityReason : IEquatable<ImpurityReason>
    {
        private readonly string _filePath;
        private readonly TextSpan _textSpan;
        private readonly LinePositionSpan _lineSpan;

        public ImpurityReason(ImpurityKind kind, string symbolDisplay, Location? location)
        {
            Kind = kind;
            SymbolDisplay = symbolDisplay ?? string.Empty;
            var fileSpan = location?.GetLineSpan() ?? default;
            _filePath = fileSpan.Path ?? string.Empty;
            _textSpan = location?.SourceSpan ?? default;
            _lineSpan = fileSpan.Span;
        }

        public ImpurityKind Kind { get; }
        public string SymbolDisplay { get; }

        public Location? ToLocation() =>
            string.IsNullOrEmpty(_filePath)
                ? null
                : Location.Create(_filePath, _textSpan, _lineSpan);

        public bool Equals(ImpurityReason other) =>
            Kind == other.Kind &&
            string.Equals(SymbolDisplay, other.SymbolDisplay, StringComparison.Ordinal) &&
            string.Equals(_filePath, other._filePath, StringComparison.Ordinal) &&
            _textSpan == other._textSpan &&
            _lineSpan.Equals(other._lineSpan);

        public override bool Equals(object? obj) => obj is ImpurityReason other && Equals(other);

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
    /// Return value of <see cref="PurityAnalyzer"/>.
    /// Bundles <see cref="Purity"/> and the reasons (empty means Pure, one or more means Impure, pre-analysis short-circuit means Unknown).
    /// </summary>
    internal readonly struct PurityResult : IEquatable<PurityResult>
    {
        public PurityResult(Purity purity, ImmutableArray<ImpurityReason> reasons)
        {
            Purity = purity;
            Reasons = reasons.IsDefault ? ImmutableArray<ImpurityReason>.Empty : reasons;
        }

        public Purity Purity { get; }
        public ImmutableArray<ImpurityReason> Reasons { get; }

        public static PurityResult Pure() => new(Purity.Pure, ImmutableArray<ImpurityReason>.Empty);
        public static PurityResult Unknown() => new(Purity.Unknown, ImmutableArray<ImpurityReason>.Empty);
        public static PurityResult Impure(ImmutableArray<ImpurityReason> reasons) => new(Purity.Impure, reasons);

        public bool Equals(PurityResult other) =>
            Purity == other.Purity &&
            Reasons.SequenceEqual(other.Reasons);

        public override bool Equals(object? obj) => obj is PurityResult other && Equals(other);

        public override int GetHashCode()
        {
            if (Reasons.IsEmpty)
            {
                return (int)Purity;
            }
            return unchecked((((int)Purity * 31) + Reasons[0].GetHashCode()) * 31 + Reasons.Length);
        }
    }
}
