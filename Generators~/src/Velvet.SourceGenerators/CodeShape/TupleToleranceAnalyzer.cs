using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Velvet.SourceGenerators.Diagnostics;

namespace Velvet.SourceGenerators.CodeShape
{
    /// <summary>
    /// Reports a tolerance chained onto an NUnit equality whose expected value is a <c>ValueTuple</c>, in
    /// assemblies that opt in (<see cref="CodeShapeMembers"/> owns the gate).
    /// </summary>
    /// <remarks>
    /// NUnit's comparer chain has no entry for <c>ValueTuple</c>, so the pair falls through to the expected
    /// value's own <c>IEquatable&lt;T&gt;</c>, which is not handed the <c>ref Tolerance</c> the numeric path
    /// receives. The assertion is then bit-exact equality and its failure message still prints the tolerance,
    /// so nothing at run time distinguishes it from one that applied.
    /// <para>
    /// The expected value's type decides, rather than the argument's syntax: a tuple held in a local reaches
    /// the same fall-through as a parenthesised literal, and asking the semantic model costs no false
    /// positive because a type either is a tuple or is not.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal sealed class TupleToleranceAnalyzer : DiagnosticAnalyzer
    {
        private const string ToleranceMethod = "Within";

        private const string EqualityMethod = "EqualTo";

        private const string ConstraintRootNamespace = "NUnit";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(CodeShapeDiagnostics.Vel503ToleranceNeverApplied);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                if (!CodeShapeMembers.OptsIntoCodeShapeRules(start.Compilation.Assembly)) return;
                start.RegisterSyntaxNodeAction(AnalyzeTolerance, SyntaxKind.InvocationExpression);
            });
        }

        private static void AnalyzeTolerance(SyntaxNodeAnalysisContext ctx)
        {
            var tolerance = (InvocationExpressionSyntax)ctx.Node;
            var name = InvokedNameOf(tolerance);
            if (name == null || name.Identifier.ValueText != ToleranceMethod) return;

            var expected = ExpectedValueOf(tolerance, ctx.SemanticModel, ctx.CancellationToken);
            if (expected == null) return;

            var type = ctx.SemanticModel.GetTypeInfo(expected, ctx.CancellationToken).Type;
            if (type is not INamedTypeSymbol { IsTupleType: true } tuple) return;

            ctx.ReportDiagnostic(Diagnostic.Create(
                CodeShapeDiagnostics.Vel503ToleranceNeverApplied,
                name.GetLocation(),
                tuple.ToDisplayString()));
        }

        /// <summary>
        /// The expected value of the NUnit equality the tolerance is chained onto, or <c>null</c> where the
        /// chain reaches none — the tolerance belongs to some other fluent surface, or the constraint was
        /// built in a separate statement, which no single expression can be followed across.
        /// </summary>
        /// <remarks>
        /// The walk starts at the tolerance itself rather than one link lower. Both reach the same equality,
        /// since a <c>Within</c> is never an <c>EqualTo</c>; what differs is how much of the suite covers the
        /// caller's name check. Starting here, deleting that check misreports a bare
        /// <c>Is.EqualTo(&lt;tuple&gt;)</c> as its own tolerance, so the no-tolerance case catches it too;
        /// starting one link lower, only an equality chained into a second constraint does.
        /// </remarks>
        private static ExpressionSyntax? ExpectedValueOf(
            InvocationExpressionSyntax tolerance, SemanticModel model, CancellationToken cancellationToken)
        {
            ExpressionSyntax? current = tolerance;
            while (current != null)
            {
                if (current is not InvocationExpressionSyntax call)
                {
                    current = (current as MemberAccessExpressionSyntax)?.Expression;
                    continue;
                }

                if (IsEqualityConstraint(call, model, cancellationToken))
                    return call.ArgumentList.Arguments[0].Expression;
                current = ReceiverOf(call);
            }

            return null;
        }

        /// <summary>
        /// The declaring namespace is what separates NUnit's constraint from any other fluent
        /// <c>EqualTo</c>/<c>Within</c> pair, whose comparison this rule knows nothing about.
        /// </summary>
        private static bool IsEqualityConstraint(
            InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken)
        {
            var name = InvokedNameOf(call);
            if (name == null || name.Identifier.ValueText != EqualityMethod) return false;
            if (call.ArgumentList.Arguments.Count != 1) return false;

            var symbol = model.GetSymbolInfo(call, cancellationToken).Symbol;
            return symbol != null && RootNamespaceOf(symbol) == ConstraintRootNamespace;
        }

        private static ExpressionSyntax? ReceiverOf(InvocationExpressionSyntax call) =>
            (call.Expression as MemberAccessExpressionSyntax)?.Expression;

        /// <summary>
        /// The method name at the call, whether or not anything is written in front of it. A
        /// <c>using static</c> import leaves the equality with no receiver at all, and a rule reading only
        /// the qualified form would pass over an assertion that traps exactly as the qualified one does.
        /// </summary>
        private static SimpleNameSyntax? InvokedNameOf(InvocationExpressionSyntax call) =>
            call.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name,
                SimpleNameSyntax simple => simple,
                _ => null,
            };

        private static string? RootNamespaceOf(ISymbol symbol)
        {
            var space = symbol.ContainingNamespace;
            while (space is { IsGlobalNamespace: false, ContainingNamespace.IsGlobalNamespace: false })
            {
                space = space.ContainingNamespace;
            }

            return space is { IsGlobalNamespace: false } ? space.Name : null;
        }
    }
}
