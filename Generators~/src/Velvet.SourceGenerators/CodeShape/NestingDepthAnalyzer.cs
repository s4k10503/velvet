using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Velvet.SourceGenerators.Diagnostics;

namespace Velvet.SourceGenerators.CodeShape
{
    /// <summary>
    /// Reports a member body whose control flow nests deeper than <see cref="MaxDepth"/>, in assemblies that
    /// opt in (<see cref="CodeShapeMembers"/> owns the gate and the member surface).
    /// <c>Generators~/README.md</c> owns what counts as a level and why the rule is opt-in.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal sealed class NestingDepthAnalyzer : DiagnosticAnalyzer
    {
        internal const int MaxDepth = 4;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(CodeShapeDiagnostics.Vel500NestingDepthExceeded);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            // Gating at compilation start rather than per node means an assembly that has not opted in pays
            // one attribute scan for the whole compile and registers no per-node work at all.
            context.RegisterCompilationStartAction(start =>
            {
                if (!CodeShapeMembers.OptsIntoCodeShapeRules(start.Compilation.Assembly)) return;
                start.RegisterSyntaxNodeAction(AnalyzeMember, CodeShapeMembers.MemberKinds);
            });
        }

        /// <summary>Height of the nesting tree below <paramref name="root"/>, which is itself not counted.</summary>
        internal static int Measure(SyntaxNode root)
        {
            var max = 0;
            var pending = new Stack<(SyntaxNode Node, int Depth)>();
            foreach (var child in root.ChildNodes())
            {
                pending.Push((child, 0));
            }

            while (pending.Count > 0)
            {
                var (node, depth) = pending.Pop();
                var here = OpensLevel(node) ? depth + 1 : depth;
                if (here > max)
                {
                    max = here;
                }
                foreach (var child in node.ChildNodes())
                {
                    pending.Push((child, here));
                }
            }

            return max;
        }

        private static void AnalyzeMember(SyntaxNodeAnalysisContext ctx)
        {
            foreach (var (body, nameToken, displayName) in CodeShapeMembers.BodiesOf(ctx.Node))
            {
                var depth = Measure(body);
                if (depth <= MaxDepth) continue;

                ctx.ReportDiagnostic(Diagnostic.Create(
                    CodeShapeDiagnostics.Vel500NestingDepthExceeded,
                    nameToken.GetLocation(),
                    displayName,
                    depth,
                    MaxDepth));
            }
        }

        private static bool OpensLevel(SyntaxNode node) => node switch
        {
            IfStatementSyntax => node.Parent is not ElseClauseSyntax,
            ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax =>
                true,
            SwitchStatementSyntax or TryStatementSyntax or LockStatementSyntax => true,
            // The block form only: `using var x = ...;` is a local declaration with no body and is the
            // flattening form of exactly this construct.
            UsingStatementSyntax => true,
            LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax => true,
            _ => false,
        };
    }
}
