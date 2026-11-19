using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Velvet.SourceGenerators.Diagnostics;

namespace Velvet.SourceGenerators.CodeShape
{
    /// <summary>
    /// Reports a member body making more than <see cref="MaxBranches"/> branching decisions, in assemblies
    /// that opt in (<see cref="CodeShapeMembers"/> owns the gate and the member surface).
    /// <c>Generators~/README.md</c> owns what counts as a branch; what follows is why it disagrees with the
    /// depth definition where it does.
    /// </summary>
    /// <remarks>
    /// The two measure opposite things. Depth is a property of the deepest path and treats an <c>else if</c>
    /// chain and the expression-level branch forms as transparent, since each is the flattening device a
    /// depth limit pushes code toward. Count is a property of the whole body, and those same forms are
    /// exactly what a flattened dense parser is made of — so every arm of a chain, every <c>case</c>, and
    /// every <c>&amp;&amp;</c>, <c>?:</c>, <c>??</c> and <c>switch</c>-expression arm is charged. A rule that
    /// let them through would be satisfied by rewriting nesting into width and would measure nothing.
    /// <para>
    /// Branches inside a lambda or local function count toward the body that declares it, for the reason its
    /// sibling gives for measuring a nested function's depth in place.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal sealed class BranchCountAnalyzer : DiagnosticAnalyzer
    {
        internal const int MaxBranches = 20;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(CodeShapeDiagnostics.Vel501BranchCountExceeded);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                if (!CodeShapeMembers.OptsIntoCodeShapeRules(start.Compilation.Assembly)) return;
                start.RegisterSyntaxNodeAction(AnalyzeMember, CodeShapeMembers.MemberKinds);
            });
        }

        /// <summary>Decisions made anywhere below <paramref name="root"/>, which is itself not counted.</summary>
        internal static int Measure(SyntaxNode root)
        {
            var count = 0;
            foreach (var node in root.DescendantNodes())
            {
                if (IsBranch(node)) count++;
            }

            return count;
        }

        private static void AnalyzeMember(SyntaxNodeAnalysisContext ctx)
        {
            // Per body rather than summed across the declaration: two field declarators each holding a
            // lambda are two separate pieces of code, and summing them would report a member neither half
            // exceeds the limit on, at a name only one of them has.
            foreach (var (body, nameToken, displayName) in CodeShapeMembers.BodiesOf(ctx.Node))
            {
                var branches = Measure(body);
                if (branches <= MaxBranches) continue;

                ctx.ReportDiagnostic(Diagnostic.Create(
                    CodeShapeDiagnostics.Vel501BranchCountExceeded,
                    nameToken.GetLocation(),
                    displayName,
                    branches,
                    MaxBranches));
            }
        }

        private static bool IsBranch(SyntaxNode node) => node switch
        {
            IfStatementSyntax => true,
            ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax =>
                true,
            ConditionalExpressionSyntax => true,
            BinaryExpressionSyntax binary => binary.IsKind(SyntaxKind.LogicalAndExpression)
                || binary.IsKind(SyntaxKind.LogicalOrExpression)
                || binary.IsKind(SyntaxKind.CoalesceExpression),
            AssignmentExpressionSyntax assignment => assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression),
            CaseSwitchLabelSyntax => true,
            CasePatternSwitchLabelSyntax label => !AlwaysMatches(label.Pattern),
            SwitchExpressionArmSyntax arm => !AlwaysMatches(arm.Pattern),
            CatchClauseSyntax => true,
            // A filter is a second decision on top of the type test its clause already carries, and it is
            // the one that can send the exception on to an outer handler.
            CatchFilterClauseSyntax or WhenClauseSyntax => true,
            // `and` / `or` between patterns, for the reason `&&` / `||` between expressions are charged.
            // `not` inverts one test rather than adding a second, so `UnaryPatternSyntax` is not here.
            BinaryPatternSyntax => true,
            _ => false,
        };

        /// <summary>
        /// Whether the pattern rejects nothing, which is what makes the arm or label carrying it the
        /// construct's fallback rather than a decision — the same reason the trailing <c>else</c> and
        /// <c>default:</c> are not charged. A <c>var</c> pattern belongs here beside <c>_</c> because it
        /// always matches too: it binds a name, which is not a test. Without it a <c>switch</c> closed by
        /// <c>var _ =&gt;</c> would cost one more than the identical <c>switch</c> closed by <c>_ =&gt;</c>.
        /// </summary>
        private static bool AlwaysMatches(PatternSyntax pattern) =>
            pattern is DiscardPatternSyntax or VarPatternSyntax;
    }
}
