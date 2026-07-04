using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Velvet.SourceGenerators.ReactiveScope;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Helper for invoking <see cref="ReactiveScopeAnalyzer"/> from tests.
    /// Reuses the same Velvet stub / reference resolution as <see cref="PurityAnalyzerTestHelper"/>.
    /// </summary>
    internal static class ReactiveScopeAnalyzerTestHelper
    {
        public static ImmutableDictionary<IOperation, ReactiveScopeResult> AnalyzeRender(
            string source, string methodName, CancellationToken ct = default)
        {
            var compilation = PurityAnalyzerTestHelper.BuildCompilation(source);
            var method = PurityAnalyzerTestHelper.FindMethod(compilation, methodName);
            return ReactiveScopeAnalyzer.AnalyzeRenderMethod(method, compilation, ct);
        }

        /// <summary>
        /// Extracts the ReactiveScopeResult corresponding to the return statement's value within the given method.
        /// Most of the 16 fixtures can be verified using this pattern.
        /// </summary>
        public static ReactiveScopeResult AnalyzeReturnExpression(
            string source, string methodName, CancellationToken ct = default)
        {
            var compilation = PurityAnalyzerTestHelper.BuildCompilation(source);
            var method = PurityAnalyzerTestHelper.FindMethod(compilation, methodName);
            var (semanticModel, returnExpr) = FindReturnExpression(compilation, method, ct)
                ?? throw new InvalidOperationException($"return expression not found in '{methodName}'.");
            return ReactiveScopeAnalyzer.Analyze(returnExpr, semanticModel, ct);
        }

        public static HashSet<string> DependencyNames(ReactiveScopeResult result) =>
            result.Dependencies is { } deps
                ? new HashSet<string>(deps.Select(d => d.Name), StringComparer.Ordinal)
                : new HashSet<string>();

        /// <summary>
        /// For the sub-expression returned from Render, converts the dependency symbol list into AccessPath strings
        /// and returns them. A fallback (null AccessPath) is represented as "&lt;fallback:reason&gt;".
        /// </summary>
        public static ImmutableArray<string> ResolveAccessPaths(
            string source, string methodName, CancellationToken ct = default)
        {
            var compilation = PurityAnalyzerTestHelper.BuildCompilation(source);
            var method = PurityAnalyzerTestHelper.FindMethod(compilation, methodName);
            var analysis = ReactiveScopeAnalyzer.AnalyzeRenderMethod(method, compilation, ct);

            ReactiveScopeResult? bodyResult = null;
            foreach (var kv in analysis)
            {
                if (kv.Key is Microsoft.CodeAnalysis.Operations.IBlockOperation or
                    Microsoft.CodeAnalysis.Operations.IMethodBodyOperation)
                {
                    bodyResult = kv.Value;
                    break;
                }
            }

            if (bodyResult is null || bodyResult.Value.Dependencies is not { } deps)
            {
                return ImmutableArray<string>.Empty;
            }

            var renderParam = method.Parameters.Length > 0 ? method.Parameters[0] : null!;
            var componentType = method.ContainingType;
            return deps
                .Select(sym =>
                {
                    var access = ReactiveScopeAnalyzer.GetAccessPath(sym, renderParam, componentType);
                    return access.AccessPath ?? $"<fallback:{access.FallbackReason}>";
                })
                .ToImmutableArray();
        }

        private static (SemanticModel Model, IOperation Op)? FindReturnExpression(
            Compilation compilation, IMethodSymbol method, CancellationToken ct)
        {
            foreach (var syntaxRef in method.DeclaringSyntaxReferences)
            {
                var node = syntaxRef.GetSyntax(ct);
                SyntaxNode? searchRoot = node switch
                {
                    MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
                    LocalFunctionStatementSyntax lf => (SyntaxNode?)lf.Body ?? lf.ExpressionBody,
                    _ => null,
                };
                if (searchRoot is null)
                {
                    continue;
                }

                var model = compilation.GetSemanticModel(searchRoot.SyntaxTree);

                if (searchRoot is ArrowExpressionClauseSyntax arrow)
                {
                    var op = model.GetOperation(arrow.Expression, ct);
                    if (op is not null)
                    {
                        return (model, op);
                    }
                }

                foreach (var returnStmt in searchRoot.DescendantNodes().OfType<ReturnStatementSyntax>())
                {
                    if (returnStmt.Expression is { } expr)
                    {
                        var op = model.GetOperation(expr, ct);
                        if (op is not null)
                        {
                            return (model, op);
                        }
                    }
                }
            }
            return null;
        }
    }
}
