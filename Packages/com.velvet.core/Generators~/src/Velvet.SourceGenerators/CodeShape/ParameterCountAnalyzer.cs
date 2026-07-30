using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Velvet.SourceGenerators.Diagnostics;

namespace Velvet.SourceGenerators.CodeShape
{
    /// <summary>
    /// Reports a declaration demanding more than <see cref="MaxParameters"/> arguments from every caller, in
    /// assemblies that opt in (<see cref="CodeShapeMembers"/> owns the gate). <c>Generators~/README.md</c>
    /// owns what counts as a parameter; what follows is why omittable ones do not.
    /// </summary>
    /// <remarks>
    /// A long list is a defect because each caller has to line the arguments up in order, and each reader has
    /// to decode a wall of untitled values. A parameter carrying a default, or a trailing <c>params</c>, does
    /// neither: it can be left out of the call entirely. Counting it would flag <c>V.Motion</c>, whose 21
    /// optional named parameters stand in for JSX props and are the intended shape of the factory surface,
    /// while charging nothing to the helper next to it that demands eight positional arguments.
    /// <para>
    /// Naming the factories directly was the alternative — by file, by declaring type, or by return type. Each
    /// exempts a helper that happens to sit among them, which is the population the rule most needs to reach.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal sealed class ParameterCountAnalyzer : DiagnosticAnalyzer
    {
        internal const int MaxParameters = 6;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(CodeShapeDiagnostics.Vel502ParameterCountExceeded);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                if (!CodeShapeMembers.OptsIntoCodeShapeRules(start.Compilation.Assembly)) return;
                start.RegisterSyntaxNodeAction(AnalyzeDeclaration, CodeShapeMembers.ParameterizedKinds);
            });
        }

        /// <summary>Parameters no caller can leave out.</summary>
        internal static int Measure(SeparatedSyntaxList<ParameterSyntax> parameters)
        {
            var count = 0;
            foreach (var parameter in parameters)
            {
                if (IsOmittable(parameter)) continue;
                count++;
            }

            return count;
        }

        private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext ctx)
        {
            var declared = CodeShapeMembers.ParametersOf(ctx.Node);
            if (declared == null) return;

            var (parameters, nameToken, displayName) = declared.Value;
            var required = Measure(parameters);
            if (required <= MaxParameters) return;

            ctx.ReportDiagnostic(Diagnostic.Create(
                CodeShapeDiagnostics.Vel502ParameterCountExceeded,
                nameToken.GetLocation(),
                displayName,
                required,
                MaxParameters));
        }

        /// <summary>
        /// Whether a call can leave the parameter out. The <c>this</c> of an extension method cannot: the
        /// receiver is written at every call site, in a fixed position, exactly as a first argument is.
        /// </summary>
        private static bool IsOmittable(ParameterSyntax parameter) =>
            parameter.Default != null || parameter.Modifiers.Any(SyntaxKind.ParamsKeyword);
    }
}
