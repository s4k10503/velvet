using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Velvet.SourceGenerators.Diagnostics;

namespace Velvet.SourceGenerators.RulesOfHooks
{
    /// <summary>
    /// Compile-time hook-ordering analyzer. Enforces that hooks are called unconditionally at the top
    /// level of a component by flagging any <c>Velvet.Hooks.UseXxx</c> invocation whose nearest enclosing syntax ancestor
    /// is a control-flow construct (if / else / loop / switch / short-circuit operator / conditional
    /// expression) or a nested lambda / anonymous method. The runtime positional HookIndexTable
    /// throws when hook counts differ across renders, but this static check surfaces violations at
    /// edit time without depending on runtime path coverage.
    /// </summary>
    /// <remarks>
    /// Two passes: a syntax-ancestor walk flags hooks inside a control-flow construct, and a control-flow
    /// analysis pass (<see cref="TryReportConditionalEarlyExit"/>) flags a hook that follows a conditional early
    /// return (<c>if (x) return; UseState(...);</c>) — a case the syntax walk cannot see. A conditional
    /// <c>throw</c> is not treated as a skip (it aborts the render rather than skipping the hook). This
    /// analyzer's naming-convention check is a syntax-only signal; the auto-memoization IL weaver
    /// (CompilerWeaver, under CodeGen/) is what actually verifies a called method transitively composes
    /// a hook.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal sealed class RulesOfHooksAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(MemoizeDiagnostics.Vel101HookInConditional);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.Node is not InvocationExpressionSyntax inv) return;
            // Cover both call shapes: `Auth.UseCurrentUser()` (member access) and bare `UseFoo()`
            // (identifier — a local function declared inside Render() or a static-imported hook).
            // The latter is the canonical pattern for in-method custom hooks and would silently
            // bypass the analyzer otherwise.
            var hookName = inv.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                IdentifierNameSyntax id => id.Identifier.ValueText,
                _ => null,
            };
            if (hookName == null) return;
            // Naming convention: hooks are `Use` + uppercase
            // ASCII letter + ... — covers Velvet.Hooks built-ins (UseEffect / UseState) AND
            // user-defined custom hooks (Auth.UseCurrentUser / MyHooks.UseDebounced) that wrap
            // built-ins internally. The uppercase-ASCII-4th-char check (PascalCase hook style)
            // filters "Useless" / "Used" / non-Latin scripts.
            if (!IsHookLikeName(hookName)) return;

            // Walk ancestors from the invocation up to the enclosing method declaration / local
            // function. Stop at the first control-flow-altering ancestor and report it. Sequential
            // ancestors (Block, ExpressionStatement, Argument, VariableDeclarator, etc.) are
            // transparent for hook ordering and are skipped.
            SyntaxNode current = inv;
            while (true)
            {
                current = current.Parent;
                if (current == null) return;
                // Method body boundaries — reached without a control-flow ancestor; OK.
                if (current is MethodDeclarationSyntax
                    || current is LocalFunctionStatementSyntax
                    || current is ConstructorDeclarationSyntax
                    || current is PropertyDeclarationSyntax
                    || current is AccessorDeclarationSyntax)
                {
                    // Not inside a control-flow construct — but a hook AFTER a conditional early return is still
                    // conditional. Detected via control-flow analysis (a syntax-ancestor walk cannot see it).
                    TryReportConditionalEarlyExit(ctx, inv, hookName);
                    return;
                }
                // FinallyClause runs unconditionally on every exit path so hook ordering is
                // invariant. The enclosing TryStatement would otherwise trip the flag, so short-
                // circuit here before TryDescribeControlFlow sees it.
                if (current is FinallyClauseSyntax) return;

                if (TryDescribeControlFlow(current, out var description))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        MemoizeDiagnostics.Vel101HookInConditional,
                        inv.GetLocation(),
                        hookName,
                        description));
                    return;
                }
            }
        }

        /// <summary>
        /// Flags a hook that follows a CONDITIONAL early exit (e.g. <c>if (x) return; UseState(...);</c>) in its
        /// enclosing block. The syntax-ancestor walk cannot see this — the hook is not inside a control-flow
        /// construct — so control-flow analysis is used: if the region of statements BEFORE the hook contains any
        /// exit point (a return / break / continue that jumps out), the hook is not reached on every path. A
        /// conditional <c>throw</c> is NOT an exit point, so it is correctly ignored (it aborts the render rather
        /// than skipping the hook — across successful renders the hook still runs).
        /// </summary>
        private static void TryReportConditionalEarlyExit(
            SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax inv, string hookName)
        {
            var stmt = inv.FirstAncestorOrSelf<StatementSyntax>();
            if (stmt?.Parent is not BlockSyntax block) return;
            var index = block.Statements.IndexOf(stmt);
            if (index <= 0) return;

            var flow = ctx.SemanticModel.AnalyzeControlFlow(block.Statements[0], block.Statements[index - 1]);
            if (flow is { Succeeded: true } && flow.ExitPoints.Length > 0)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    MemoizeDiagnostics.Vel101HookInConditional,
                    inv.GetLocation(),
                    hookName,
                    "after a conditional early return (the hook is not reached on every path)"));
            }
        }

        /// <summary>
        /// `Use` + uppercase-letter naming convention check.
        /// Matches PascalCase hook names like <c>UseEffect</c>, <c>UseAuth</c>, <c>UseDebounced</c>
        /// but excludes prefix-only collisions like <c>Used</c>, <c>Useless</c>, <c>Use(this T)</c>
        /// extension methods that aren't hooks. False positives on legitimate non-hook methods that
        /// happen to follow the convention (e.g., a hypothetical <c>UseBuilder()</c> utility) are
        /// an accepted trade-off — users should rename such helpers.
        /// </summary>
        private static bool IsHookLikeName(string name) =>
            name != null
            && name.Length >= 4
            && name[0] == 'U' && name[1] == 's' && name[2] == 'e'
            && name[3] >= 'A' && name[3] <= 'Z';

        private static bool TryDescribeControlFlow(SyntaxNode node, out string description)
        {
            switch (node.Kind())
            {
                case SyntaxKind.IfStatement:
                case SyntaxKind.ElseClause:
                    description = "an if/else branch";
                    return true;
                case SyntaxKind.WhileStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForEachStatement:
                case SyntaxKind.ForEachVariableStatement:
                    description = "a loop body";
                    return true;
                case SyntaxKind.SwitchSection:
                case SyntaxKind.SwitchExpressionArm:
                    description = "a switch arm";
                    return true;
                case SyntaxKind.ConditionalExpression:
                    description = "a conditional expression (a ? b : c)";
                    return true;
                case SyntaxKind.LogicalAndExpression:
                case SyntaxKind.LogicalOrExpression:
                case SyntaxKind.CoalesceExpression:
                    description = "a short-circuit operator (&&, ||, ??)";
                    return true;
                case SyntaxKind.SimpleLambdaExpression:
                case SyntaxKind.ParenthesizedLambdaExpression:
                case SyntaxKind.AnonymousMethodExpression:
                    description = "a nested lambda or anonymous method";
                    return true;
                case SyntaxKind.TryStatement:
                case SyntaxKind.CatchClause:
                    // FinallyClause is INTENTIONALLY excluded: finally runs unconditionally on every
                    // exit so hook ordering across renders is invariant. Only try-block hooks
                    // (skipped on early exception) and catch-block hooks (conditional on exception)
                    // are real hazards.
                    description = "a try or catch block (exception-path conditional)";
                    return true;
                default:
                    description = null;
                    return false;
            }
        }
    }
}
