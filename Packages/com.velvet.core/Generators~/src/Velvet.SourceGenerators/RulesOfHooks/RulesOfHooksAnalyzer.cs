using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Velvet.SourceGenerators.Diagnostics;

namespace Velvet.SourceGenerators.RulesOfHooks
{
    /// <summary>
    /// Compile-time rules-of-hooks analyzer, eslint-plugin-react-hooks' <c>rules-of-hooks</c> for Velvet. A hook call
    /// is what <see cref="HookName"/> accepts. VEL101 flags one VEL102 does not report that runs only on some
    /// paths through its own function: in a branch of an if / else, a switch, a conditional expression or a
    /// short-circuit operator, in a loop, in a catch, in a try after something that may throw, past a conditional
    /// early return, or in a lambda that is not a component body or a held hook and sits inside a component or a
    /// custom hook. What a construct evaluates before it branches is not one of its branches.
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

        // The name a call is written with: `Auth.UseCurrentUser()`, a bare `UseFoo()` (a local function or a
        // static-imported hook), and either one with type arguments, `Hooks.UseState<int>(0)` or `UseCore<T>()`.
        internal static string? CalleeName(InvocationExpressionSyntax inv) => inv.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            SimpleNameSyntax name => name.Identifier.ValueText,
            _ => null,
        };

        /// <summary>
        /// The hook a call names, or null where it is not a hook call: eslint-plugin-react-hooks' <c>isHook</c>. A
        /// bare name counts by its spelling alone, and so does a member call on a receiver that is a single name
        /// starting with an uppercase letter (<c>Hooks.UseState</c>). eslint takes no longer receiver; C# spells a
        /// type through its namespace (<c>Velvet.Hooks.UseState</c>, <c>global::Velvet.Hooks.UseState</c>), so a
        /// qualified receiver counts where <paramref name="model"/> binds it to a namespace or a type. Without a
        /// model, a qualified receiver counts where every name in it starts with an uppercase letter.
        /// </summary>
        internal static string? HookName(
            InvocationExpressionSyntax inv, SemanticModel? model, CancellationToken cancellationToken)
        {
            var name = CalleeName(inv);
            if (!IsHookLikeName(name)) return null;
            if (inv.Expression is not MemberAccessExpressionSyntax member) return name;
            return member.Expression switch
            {
                IdentifierNameSyntax receiver => StartsUpper(receiver.Identifier.ValueText) ? name : null,
                MemberAccessExpressionSyntax or AliasQualifiedNameSyntax when model != null =>
                    model.GetSymbolInfo(member.Expression, cancellationToken).Symbol is INamespaceOrTypeSymbol ? name : null,
                MemberAccessExpressionSyntax or AliasQualifiedNameSyntax =>
                    IsUppercaseNameChain(member.Expression) ? name : null,
                _ => null,
            };
        }

        private static bool StartsUpper(string text) => text.Length > 0 && char.IsUpper(text[0]);

        private static bool IsUppercaseNameChain(ExpressionSyntax expression) => expression switch
        {
            IdentifierNameSyntax id => StartsUpper(id.Identifier.ValueText),
            AliasQualifiedNameSyntax alias => StartsUpper(alias.Name.Identifier.ValueText),
            MemberAccessExpressionSyntax member =>
                StartsUpper(member.Name.Identifier.ValueText) && IsUppercaseNameChain(member.Expression),
            _ => false,
        };

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx, ComponentIndex components)
        {
            if (ctx.Node is not InvocationExpressionSyntax inv) return;
            var hookName = HookName(inv, ctx.SemanticModel, ctx.CancellationToken);
            if (hookName == null)
            {
                TryReportDirectComponentCall(ctx, inv, components);
                return;
            }

            var host = EnclosingFunction(inv);
            if (host is AnonymousFunctionExpressionSyntax lambda
                && !IsComponentBody(lambda)
                && !IsHookLambda(lambda)
                && !IsSomewhereInsideComponentOrHook(ctx, inv, components))
            {
                // eslint reports a hook in a callback only where the callback sits inside a component or a hook.
                return;
            }
            if (host != null && TryNameNonHookHost(ctx, host, components) is { } hostName)
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
                var child = current;
                current = parent;
                // Method body boundaries — reached without a control-flow ancestor; OK. A lambda that is a
                // component's render body, or that a hook-named variable holds, is one too: it is the function the
                // hook belongs to.
                if (IsFunctionBoundary(current))
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

                // What decides a branch runs whichever way it decides: eslint reports none of these.
                if (IsEvaluatedFirst(current, child)) continue;

                if (TryDescribeBranch(ctx, inv, current, child, out var description)
                    || TryDescribeControlFlow(current, out description))
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

        private static bool IsFunctionBoundary(SyntaxNode node) => node switch
        {
            MethodDeclarationSyntax or LocalFunctionStatementSyntax or ConstructorDeclarationSyntax
                or PropertyDeclarationSyntax or AccessorDeclarationSyntax => true,
            AnonymousFunctionExpressionSyntax lambda => IsComponentBody(lambda) || IsHookLambda(lambda),
            _ => false,
        };

        // `child` is what a construct evaluates once, before choosing a branch or starting its loop: an if's
        // condition, a conditional expression's condition, the left operand of &&, || and ??, a foreach's
        // collection and a for's initializer.
        private static bool IsEvaluatedFirst(SyntaxNode construct, SyntaxNode child) => construct switch
        {
            IfStatementSyntax ifStatement => ifStatement.Condition == child,
            ForEachStatementSyntax forEach => forEach.Expression == child,
            ForEachVariableStatementSyntax forEach => forEach.Expression == child,
            ForStatementSyntax forStatement =>
                forStatement.Declaration == child || forStatement.Initializers.Any(initializer => initializer == child),
            ConditionalExpressionSyntax conditional => conditional.Condition == child,
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression)
                || binary.IsKind(SyntaxKind.LogicalOrExpression)
                || binary.IsKind(SyntaxKind.CoalesceExpression) => binary.Left == child,
            _ => false,
        };

        // eslint's getFunctionName for a function expression: a lambda held by a variable or assigned to a name is
        // a hook when that name is a hook's.
        private static bool IsHookLambda(AnonymousFunctionExpressionSyntax lambda) =>
            Unwrapped(lambda).Parent switch
            {
                EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } =>
                    IsHookHostName(declarator.Identifier.ValueText),
                AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } assignment
                    when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) =>
                    IsHookHostName(target.Identifier.ValueText),
                _ => false,
            };

        // The outermost of the parentheses and casts wrapped around an expression.
        internal static ExpressionSyntax Unwrapped(ExpressionSyntax expression)
        {
            while (expression.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
            {
                expression = (ExpressionSyntax)expression.Parent;
            }
            return expression;
        }

        private static bool IsComponentBody(SyntaxNode? node) =>
            node is AnonymousFunctionExpressionSyntax lambda
            && Unwrapped(lambda).Parent is ArgumentSyntax argument
            && ComponentIndex.IsComponentBodyArgument(argument);

        // eslint's isSomewhereInsideComponentOrHook: some function enclosing the call is a component or a hook.
        private static bool IsSomewhereInsideComponentOrHook(
            SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax inv, ComponentIndex components)
        {
            foreach (var ancestor in inv.Ancestors())
            {
                switch (ancestor)
                {
                    case AnonymousFunctionExpressionSyntax lambda when IsComponentBody(lambda) || IsHookLambda(lambda):
                        return true;
                    case LocalFunctionStatementSyntax local
                        when NameUnlessComponentOrHook(ctx, local, local.Identifier.ValueText, components) == null:
                    case MethodDeclarationSyntax method
                        when NameUnlessComponentOrHook(ctx, method, method.Identifier.ValueText, components) == null:
                        return true;
                }
            }
            return false;
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
            // The statement holding the hook in its own function's body. An expression-bodied lambda, local function
            // or member has no statement before the hook, and the statements around it belong to another function.
            var stmt = inv.Ancestors()
                .TakeWhile(node => node is not (AnonymousFunctionExpressionSyntax or ArrowExpressionClauseSyntax))
                .OfType<StatementSyntax>()
                .FirstOrDefault();
            // Every block between the hook and its function's body is read, so an exit before a bare block, a lock,
            // a using or a try that holds the hook counts as one before the hook.
            for (; stmt != null; stmt = EnclosingStatement(stmt))
            {
                if (stmt.Parent is not BlockSyntax block) continue;
                var index = block.Statements.IndexOf(stmt);
                if (index <= 0) continue;

                var flow = ctx.SemanticModel.AnalyzeControlFlow(block.Statements[0], block.Statements[index - 1]);
                if (flow is { Succeeded: true } && flow.ExitPoints.Length > 0)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        MemoizeDiagnostics.Vel101HookInConditional,
                        inv.GetLocation(),
                        hookName,
                        "after a conditional early return (the hook is not reached on every path)"));
                    return;
                }
            }
        }

        // The statement one level out within the same function, or null at the function's own body.
        private static StatementSyntax? EnclosingStatement(StatementSyntax statement) => statement.Parent switch
        {
            LocalFunctionStatementSyntax => null,
            StatementSyntax parent => parent,
            CatchClauseSyntax { Parent: TryStatementSyntax owner } => owner,
            FinallyClauseSyntax { Parent: TryStatementSyntax owner } => owner,
            ElseClauseSyntax { Parent: IfStatementSyntax owner } => owner,
            SwitchSectionSyntax { Parent: SwitchStatementSyntax owner } => owner,
            _ => null,
        };

        // The nearest enclosing function, or the field or property whose initializer holds the call. Read from
        // the syntax so a hook in an ordinary component or custom hook costs no binding.
        private static SyntaxNode? EnclosingFunction(InvocationExpressionSyntax inv)
        {
            foreach (var ancestor in inv.Ancestors())
            {
                switch (ancestor)
                {
                    case AnonymousFunctionExpressionSyntax:
                    case LocalFunctionStatementSyntax:
                    case BaseMethodDeclarationSyntax:
                    case AccessorDeclarationSyntax:
                    case ArrowExpressionClauseSyntax { Parent: BasePropertyDeclarationSyntax }:
                    case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax } }:
                    case EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax }:
                        return ancestor;
                }
            }
            return null;
        }

        /// <summary>
        /// The name VEL102 reports for a hook's enclosing function or member, or null where that is a component, a
        /// custom hook or a lambda. A lambda is VEL101's to judge, against the lambda itself unless it is a
        /// component's render body.
        /// </summary>
        private static string? TryNameNonHookHost(SyntaxNodeAnalysisContext ctx, SyntaxNode host, ComponentIndex components)
        {
            switch (host)
            {
                case AnonymousFunctionExpressionSyntax:
                    return null;
                case LocalFunctionStatementSyntax local:
                    return NameUnlessComponentOrHook(ctx, local, local.Identifier.ValueText, components);
                case MethodDeclarationSyntax method:
                    return NameUnlessComponentOrHook(ctx, method, method.Identifier.ValueText, components);
                case ConstructorDeclarationSyntax constructor:
                    return constructor.Identifier.ValueText;
                case BaseMethodDeclarationSyntax other:
                    return ctx.SemanticModel.GetDeclaredSymbol(other, ctx.CancellationToken)?.Name;
                case AccessorDeclarationSyntax { Parent.Parent: BasePropertyDeclarationSyntax property }:
                    return PropertyName(property);
                case ArrowExpressionClauseSyntax { Parent: BasePropertyDeclarationSyntax property }:
                    return PropertyName(property);
                case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }:
                    return declarator.Identifier.ValueText;
                case EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property }:
                    return property.Identifier.ValueText;
                default:
                    return null;
            }
        }

        private static string? NameUnlessComponentOrHook(
            SyntaxNodeAnalysisContext ctx, SyntaxNode declaration, string name, ComponentIndex components)
        {
            if (IsHookHostName(name)) return null;
            return ctx.SemanticModel.GetDeclaredSymbol(declaration, ctx.CancellationToken) is IMethodSymbol method
                && components.IsComponent(method, ctx.CancellationToken)
                ? null
                : name;
        }

        private static string PropertyName(BasePropertyDeclarationSyntax property) => property switch
        {
            PropertyDeclarationSyntax named => named.Identifier.ValueText,
            EventDeclarationSyntax @event => @event.Identifier.ValueText,
            _ => "this[]",
        };

        private static ExpressionSyntax OutsideParentheses(ExpressionSyntax expression)
        {
            while (expression.Parent is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized;
            return expression;
        }

        // A function eslint counts as a hook: one named like a hook call, or `use` itself, which Velvet spells `Use`.
        // A call to `Use` stays outside IsHookLikeName, as eslint exempts `use(...)` from the conditional checks.
        private static bool IsHookHostName(string name) => IsHookLikeName(name) || name == "Use";

        private static void TryReportDirectComponentCall(
            SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax inv, ComponentIndex components)
        {
            if (!components.MayCallComponent(inv, ctx.CancellationToken)) return;
            if (IsWholeRenderBody(inv)) return;
            if (ctx.SemanticModel.GetSymbolInfo(inv, ctx.CancellationToken).Symbol is not IMethodSymbol callee) return;
            if (!components.IsComponentCallingHooks(callee, ctx.CancellationToken)) return;
            ctx.ReportDiagnostic(Diagnostic.Create(
                MemoizeDiagnostics.Vel103ComponentCalledDirectly, inv.GetLocation(), callee.Name));
        }

        // `V.Component(() => Sheet())`: the call is the whole of a render body, so the component the lambda mounts
        // is the callee's own render with nothing of the lambda's around it.
        private static bool IsWholeRenderBody(InvocationExpressionSyntax inv) => OutsideParentheses(inv).Parent switch
        {
            AnonymousFunctionExpressionSyntax lambda => IsComponentBody(lambda),
            ReturnStatementSyntax { Parent: BlockSyntax { Statements.Count: 1, Parent: AnonymousFunctionExpressionSyntax lambda } } =>
                IsComponentBody(lambda),
            _ => false,
        };

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
            && (name[3] >= 'A' && name[3] <= 'Z' || name[3] >= '0' && name[3] <= '9');

        // The constructs whose verdict depends on which part of them holds the hook, or on what else they hold.
        private static bool TryDescribeBranch(
            SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax inv, SyntaxNode node, SyntaxNode child,
            out string description)
        {
            switch (node)
            {
                case TryStatementSyntax tryStatement:
                    // As eslint reads a try: every statement in it is reached until something that may throw has
                    // run, and with no catch a throw ends the render rather than skipping the hook.
                    description = "a try block after something that may throw";
                    return child == tryStatement.Block
                        && tryStatement.Catches.Count > 0
                        && MayThrowBefore(tryStatement.Block, inv);
                case CatchClauseSyntax { Parent: TryStatementSyntax owner }:
                    // eslint reads a catch as conditional unless its try cannot complete normally.
                    description = "a catch block (reached only when the try throws)";
                    return ctx.SemanticModel.AnalyzeControlFlow(owner.Block) is not { Succeeded: true, EndPointIsReachable: false };
                case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                    description = "a null-coalescing assignment (??=)";
                    return assignment.Right == child;
                case ConditionalAccessExpressionSyntax access:
                    description = "a null-conditional access (?.)";
                    return access.WhenNotNull == child;
                default:
                    description = string.Empty;
                    return false;
            }
        }

        // A call, an object creation, a member or element access, an await or a throw that runs before the hook in
        // the try block, read in source order; a lambda's or a local function's body runs later and is not read.
        private static bool MayThrowBefore(BlockSyntax tryBlock, InvocationExpressionSyntax inv) =>
            tryBlock.DescendantNodes(node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                .Any(node => node.Span.End <= inv.SpanStart
                    && node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax
                        or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or ConditionalAccessExpressionSyntax
                        or AwaitExpressionSyntax or ThrowStatementSyntax or ThrowExpressionSyntax);

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
                default:
                    description = null;
                    return false;
            }
        }
    }
}
