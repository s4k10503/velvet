using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Velvet.SourceGenerators.Diagnostics;

namespace Velvet.SourceGenerators.RulesOfHooks
{
    /// <summary>
    /// Compile-time rules-of-hooks analyzer, eslint-plugin-react-hooks' <c>rules-of-hooks</c> for Velvet. A hook is
    /// a call named <c>Use</c> followed by an uppercase ASCII letter. VEL101 flags one VEL102 does not report whose
    /// nearest enclosing syntax ancestor is a control-flow construct (if / else / loop / switch /
    /// short-circuit operator / conditional expression / try / catch) or a lambda that is not a component body.
    /// VEL102 flags one whose nearest enclosing function is neither a component nor a custom hook, and VEL103 a
    /// direct call of a component whose body calls a hook. The runtime positional HookIndexTable throws when hook
    /// counts differ across renders, but this static check surfaces violations at edit time without depending on
    /// runtime path coverage.
    /// </summary>
    /// <remarks>
    /// VEL101 has two passes: a syntax-ancestor walk flags hooks inside a control-flow construct, and a
    /// control-flow analysis pass (<see cref="TryReportConditionalEarlyExit"/>) flags a hook that follows a
    /// conditional early return (<c>if (x) return; UseState(...);</c>) — a case the syntax walk cannot see. A
    /// conditional <c>throw</c> is not treated as a skip (it aborts the render rather than skipping the hook).
    /// As in eslint, a host that is neither a component nor a hook draws VEL102 alone: its hooks are wrong
    /// wherever they sit in it, so the conditional checks are not asked there. This analyzer's
    /// naming-convention check is a syntax-only signal; the auto-memoization IL weaver (CompilerWeaver, under
    /// CodeGen/) is what actually verifies a called method transitively composes a hook.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal sealed class RulesOfHooksAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                MemoizeDiagnostics.Vel101HookInConditional,
                MemoizeDiagnostics.Vel102HookOutsideComponentOrHook,
                MemoizeDiagnostics.Vel103ComponentCalledDirectly);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                var components = new ComponentIndex(start.Compilation);
                start.RegisterSyntaxNodeAction(
                    ctx => AnalyzeInvocation(ctx, components), SyntaxKind.InvocationExpression);
            });
        }

        // Covers both call shapes: `Auth.UseCurrentUser()` (member access) and bare `UseFoo()` (identifier — a
        // local function declared inside Render() or a static-imported hook).
        internal static string? CalleeName(InvocationExpressionSyntax inv) => inv.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => null,
        };

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx, ComponentIndex components)
        {
            if (ctx.Node is not InvocationExpressionSyntax inv) return;
            var hookName = CalleeName(inv);
            if (hookName == null) return;
            if (!IsHookLikeName(hookName))
            {
                TryReportDirectComponentCall(ctx, inv, components);
                return;
            }

            var host = ctx.SemanticModel.GetEnclosingSymbol(inv.SpanStart, ctx.CancellationToken);
            if (TryNameNonHookHost(host, components) is { } hostName)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    MemoizeDiagnostics.Vel102HookOutsideComponentOrHook, inv.GetLocation(), hookName, hostName));
                return;
            }

            // Walk ancestors from the invocation up to the enclosing method declaration / local
            // function. Stop at the first control-flow-altering ancestor and report it. Sequential
            // ancestors (Block, ExpressionStatement, Argument, VariableDeclarator, etc.) are
            // transparent for hook ordering and are skipped.
            SyntaxNode current = inv;
            while (true)
            {
                if (current.Parent is not { } parent) return;
                current = parent;
                // Method body boundaries — reached without a control-flow ancestor; OK. A lambda that is a
                // component's render body is one too: it is the function the hook belongs to.
                if (current is MethodDeclarationSyntax
                    || current is LocalFunctionStatementSyntax
                    || current is ConstructorDeclarationSyntax
                    || current is PropertyDeclarationSyntax
                    || current is AccessorDeclarationSyntax
                    || IsComponentBody(current, ctx))
                {
                    // Not inside a control-flow construct — but a hook AFTER a conditional early return is still
                    // conditional. Detected via control-flow analysis (a syntax-ancestor walk cannot see it).
                    TryReportConditionalEarlyExit(ctx, inv, hookName);
                    return;
                }
                // A finally block runs on every exit path, so its own TryStatement does not make the hook
                // conditional: step over that one statement and keep walking, since a construct around the
                // try still can.
                if (current is FinallyClauseSyntax { Parent: TryStatementSyntax tryStatement })
                {
                    current = tryStatement;
                    continue;
                }

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

        private static bool IsComponentBody(SyntaxNode node, SyntaxNodeAnalysisContext ctx) =>
            node is AnonymousFunctionExpressionSyntax { Parent: ArgumentSyntax argument }
            && ComponentIndex.IsComponentBodyArgument(argument, ctx.SemanticModel, ctx.CancellationToken);

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
        /// The name VEL102 reports for a hook's nearest enclosing function or member, or null where that is a
        /// component, a custom hook or a lambda. A lambda is VEL101's to judge, against the lambda itself unless it
        /// is a component's render body.
        /// </summary>
        private static string? TryNameNonHookHost(ISymbol? host, ComponentIndex components)
        {
            switch (host)
            {
                case IMethodSymbol { MethodKind: MethodKind.AnonymousFunction }:
                    return null;
                case IMethodSymbol method:
                    if (IsHookLikeName(method.Name) || components.IsComponent(method)) return null;
                    return method.AssociatedSymbol?.Name
                        ?? (method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
                            ? method.ContainingType.Name
                            : method.Name);
                case IFieldSymbol field:
                    // An auto-property initializer arrives as its backing field: Given_HookInPropertyInitializer_When_Analyzed_Then_ReportsVel102NamingTheProperty.
                    return field.AssociatedSymbol?.Name ?? field.Name;
                default:
                    return null;
            }
        }

        private static void TryReportDirectComponentCall(
            SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax inv, ComponentIndex components)
        {
            if (ctx.SemanticModel.GetSymbolInfo(inv, ctx.CancellationToken).Symbol is not IMethodSymbol callee) return;
            if (!components.IsComponentCallingHooks(callee)) return;
            ctx.ReportDiagnostic(Diagnostic.Create(
                MemoizeDiagnostics.Vel103ComponentCalledDirectly, inv.GetLocation(), callee.Name));
        }

        /// <summary>
        /// `Use` + uppercase-letter naming convention check.
        /// Matches PascalCase hook names like <c>UseEffect</c>, <c>UseAuth</c>, <c>UseDebounced</c>
        /// but excludes prefix-only collisions like <c>Used</c>, <c>Useless</c>, <c>Use(this T)</c>
        /// extension methods that aren't hooks. False positives on legitimate non-hook methods that
        /// happen to follow the convention (e.g., a hypothetical <c>UseBuilder()</c> utility) are
        /// an accepted trade-off — users should rename such helpers.
        /// </summary>
        internal static bool IsHookLikeName(string? name) =>
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
