using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Velvet.SourceGenerators.PurityAnalysis
{
    /// <summary>
    /// Method-local purity / side-effect analysis with bounded call-graph propagation.
    /// Internal static API called from Reactive Scope analysis and Analyzer paths. Does not generate code.
    /// </summary>
    /// <remarks>
    /// Call graph propagation: when an invocation lands on an UnknownCall, the analyzer recurses into the callee
    /// up to <c>maxDepth</c> levels. Pure callees are silently absorbed; Impure callees propagate as KnownImpureCall.
    /// Cycles are broken with a visited set keyed on <see cref="SymbolEqualityComparer.Default"/>.
    /// </remarks>
    internal static class PurityAnalyzer
    {
        /// <summary>
        /// Default call-graph propagation depth from the public entry point. The walker subtracts 1 for the body
        /// itself and guards on <c>remainingDepth &gt; 0</c>, so this value of 2 expands callees one level deep
        /// (caller body + 1 callee layer). Increase to 3+ to recurse further; switch the walker's visited set to a
        /// path-local enter/exit model first to avoid diamond-graph false negatives at higher depths.
        /// </summary>
        private const int DefaultMaxDepth = 2;

        private const string PureAttributeShortName = "PureAttribute";
        private static readonly string[] PureAttributeContainingNamespaces =
        {
            "System.Diagnostics.Contracts",
            "JetBrains.Annotations",
            "Velvet",
        };

        public static PurityResult Analyze(IMethodSymbol method, Compilation compilation, CancellationToken ct) =>
            AnalyzeCore(method, compilation, ct, DefaultMaxDepth, visited: null);

        /// <summary>
        /// Recursive entry used both by the public API (with default depth) and by <see cref="SideEffectWalker"/>
        /// when propagating callee purity. <paramref name="remainingDepth"/> reaches 0 → callees are no longer recursed.
        /// </summary>
        internal static PurityResult AnalyzeCore(
            IMethodSymbol method,
            Compilation compilation,
            CancellationToken ct,
            int remainingDepth,
            HashSet<IMethodSymbol>? visited)
        {
            if (method is null || compilation is null)
            {
                return PurityResult.Unknown();
            }

            ct.ThrowIfCancellationRequested();

            if (HasPureAttribute(method))
            {
                return PurityResult.Pure();
            }

            if (KnownPurityDatabase.TryClassify(method, out var known))
            {
                return known == KnownPurity.Pure
                    ? PurityResult.Pure()
                    : PurityResult.Impure(ImmutableArray.Create(
                        new ImpurityReason(ImpurityKind.KnownImpureCall, method.ToDisplayString(), null)));
            }

            if (method.IsAbstract || method.IsExtern || method.IsVirtual || method.IsOverride)
            {
                return PurityResult.Unknown();
            }

            var syntax = TryGetMethodDeclaration(method, ct);
            if (syntax is null)
            {
                return PurityResult.Unknown();
            }

            SemanticModel semanticModel;
            try
            {
                semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
            }
            catch
            {
                return PurityResult.Unknown();
            }

            IOperation? bodyOperation;
            try
            {
                bodyOperation = semanticModel.GetOperation(syntax, ct);
            }
            catch
            {
                return PurityResult.Unknown();
            }

            if (bodyOperation is null)
            {
                return PurityResult.Unknown();
            }

            // Propagation context shared with the walker. Depth - 1 because the body itself is the current depth level.
            var walkerVisited = visited ?? new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            walkerVisited.Add(method);

            var walker = new SideEffectWalker(method, ct, compilation, Math.Max(0, remainingDepth - 1), walkerVisited);
            try
            {
                walker.Visit(bodyOperation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return PurityResult.Unknown();
            }

            var reasons = walker.Reasons;
            return reasons.IsEmpty
                ? PurityResult.Pure()
                : PurityResult.Impure(reasons);
        }

        private static bool HasPureAttribute(IMethodSymbol method)
        {
            foreach (var attr in method.GetAttributes())
            {
                var attributeClass = attr.AttributeClass;
                if (attributeClass is null || !string.Equals(attributeClass.Name, PureAttributeShortName, StringComparison.Ordinal))
                {
                    continue;
                }
                var containingNs = attributeClass.ContainingNamespace?.ToDisplayString();
                if (containingNs is null)
                {
                    continue;
                }
                foreach (var target in PureAttributeContainingNamespaces)
                {
                    if (string.Equals(containingNs, target, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static SyntaxNode? TryGetMethodDeclaration(IMethodSymbol method, CancellationToken ct)
        {
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                var node = reference.GetSyntax(ct);
                if (node is MethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax or ArrowExpressionClauseSyntax)
                {
                    return node;
                }
                if (node is BaseMethodDeclarationSyntax)
                {
                    return node;
                }
            }
            return null;
        }
    }
}
