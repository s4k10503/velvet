using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Velvet.SourceGenerators.Diagnostics;
using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators.AutoDeps
{
    /// <summary>
    /// Compares closure-captured values (locals, instance fields, and properties) inside a deps-comparing
    /// hook's lambda factory against the elements listed in the hook's <c>deps</c> argument and reports
    /// <see cref="MemoizeDiagnostics.Vel100UseEffectMissingDep"/> when a captured value is missing.
    /// Covers <c>UseEffect</c> / <c>UseLayoutEffect</c> / <c>UseCallback</c> / <c>UseImperativeHandle</c>.
    /// </summary>
    /// <remarks>
    /// The match is conservative: only <c>new[] { ... }</c> / <c>new T[] { ... }</c> deps initializers and
    /// loose <c>params</c> deps arguments are considered, and only missing captures are flagged (extra deps
    /// are ignored). Method-group factories are skipped because the captured set requires resolving the
    /// target method body. A captured local is exempt only when it originates from a stable hook return
    /// (<c>UseState</c> / <c>UseReducer</c> / <c>UseRef</c> / <c>UseMutableRef</c>).
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UseEffectExhaustiveDepsAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(MemoizeDiagnostics.Vel100UseEffectMissingDep);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.Node is not InvocationExpressionSyntax inv) return;
            if (inv.Expression is not MemberAccessExpressionSyntax member) return;
            if (!DepsHookDescriptor.TryGet(member.Name.Identifier.ValueText, out var hook)) return;

            var args = inv.ArgumentList.Arguments;
            // Need at least factory + one deps element. The no-deps overloads (factory only) carry no deps to
            // compare against and are handled by the runtime, not this lint.
            if (args.Count <= hook.DepsArgIndex) return;

            // Confirm the call binds to the descriptor's declared containing type (Velvet.Hooks for
            // useEffect/useLayoutEffect/useCallback/useMemo/useImperativeHandle; Velvet.V for V.Memoized / V.MemoizedWithKey).
            // When the lambda's deferred type inference yields OverloadResolutionFailure (Symbol == null),
            // the candidate set still names the intended overload — accept the candidate so the analyzer
            // remains useful during interactive editing too.
            var symbolInfo = ctx.SemanticModel.GetSymbolInfo(inv, ctx.CancellationToken);
            var target = symbolInfo.Symbol as IMethodSymbol
                ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            if (target is null) return;
            var containingType = target.ContainingType?.ToDisplayString();
            if (containingType != hook.ContainingTypeFullName) return;

            // Only handle lambda factories (block / expression body); method groups are not analyzed.
            if (args[hook.FactoryArgIndex].Expression is not LambdaExpressionSyntax lambda) return;
            // UseMemo / UseEffect / UseCallback / UseLayoutEffect / UseImperativeHandle and V.Memoized all take
            // PARAMETERLESS factories. The props-comparing component overload V.Memo<TProps>(Func<TProps,VNode>,
            // props, ...) takes a one-parameter (TProps) body lambda, so gating on parameter count keeps it out
            // of the deps-comparing pipeline even if a future descriptor entry shared its name.
            var lambdaParamCount = lambda switch
            {
                SimpleLambdaExpressionSyntax => 1,
                ParenthesizedLambdaExpressionSyntax paren => paren.ParameterList.Parameters.Count,
                _ => 0,
            };
            if (lambdaParamCount > 0) return;

            var depsArgs = CollectDepsArguments(args, hook);
            var depsIdentifiers = TryExtractDepsIdentifiers(depsArgs, hook.DepsAreParams);
            if (depsIdentifiers is null) return;

            foreach (var capture in CollectCapturedLocals(lambda, ctx.SemanticModel, ctx.CancellationToken))
            {
                if (depsIdentifiers.Contains(capture.LocalName)) continue;
                // The stable-hook-return exemption applies only to locals (setter / dispatch / ref bound from a
                // Velvet.Hooks call). Instance fields / properties have no such origin, so they are never exempt.
                if (capture.Symbol is ILocalSymbol stableCandidate
                    && IsStableHookReturnLocal(stableCandidate, ctx.SemanticModel, ctx.CancellationToken)) continue;
                ctx.ReportDiagnostic(Diagnostic.Create(
                    MemoizeDiagnostics.Vel100UseEffectMissingDep,
                    capture.ReportLocation,
                    capture.LocalName));
            }
        }

        private static IReadOnlyList<ArgumentSyntax> CollectDepsArguments(
            SeparatedSyntaxList<ArgumentSyntax> args,
            DepsHookDescriptor hook)
        {
            // For params deps every trailing argument from the deps slot onward is a dependency. For the
            // fixed-arity hooks there is exactly one deps argument at the deps slot.
            var result = new List<ArgumentSyntax>();
            var last = hook.DepsAreParams ? args.Count - 1 : hook.DepsArgIndex;
            for (var i = hook.DepsArgIndex; i <= last; i++)
            {
                result.Add(args[i]);
            }
            return result;
        }

        private static ImmutableHashSet<string>? TryExtractDepsIdentifiers(
            IReadOnlyList<ArgumentSyntax> depsArgs,
            bool depsAreParams)
        {
            var elements = UseEffectDepsSyntax.TryGetDepsElements(depsArgs, depsAreParams);
            if (elements is null) return null;

            var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (var element in elements)
            {
                // Simple identifier deps (`local`).
                if (element is IdentifierNameSyntax id)
                {
                    builder.Add(id.Identifier.ValueText);
                    continue;
                }
                // Member-access deps (`state.SaveSlots`) cover the captured root identifier (`state`): the
                // body's `state.SaveSlots` usage resolves through the captured root local, so a dep written
                // as `state.X` must satisfy the `state` capture. Without this unification, declaring deps as
                // `state.X, state.Y` would still flag `state` as missing on every Velvet hook call site.
                //
                // Deliberate precision-over-recall choice: collapsing a member-access dep to its ROOT
                // identifier is false-positive-free but admits a niche false-negative — when declared deps are
                // a NARROWER sub-path than what the body captures (deps `a.x`, body uses `a.b.c`), both
                // collapse to `a` and the missing `a.b.c` goes unreported. Closing that gap needs
                // path-sensitive analysis, which risks warning on correct code (false positives). A lint must
                // prioritise precision (zero false positives) over completeness, so the conservative
                // root-collapse is kept on purpose rather than matching ESLint's exact path-tracking.
                if (element is MemberAccessExpressionSyntax memberAccess)
                {
                    var current = memberAccess;
                    while (current.Expression is MemberAccessExpressionSyntax inner) current = inner;
                    if (current.Expression is IdentifierNameSyntax rootId)
                    {
                        builder.Add(rootId.Identifier.ValueText);
                    }
                }
            }
            return builder.ToImmutable();
        }

        private static IEnumerable<CapturedLocal> CollectCapturedLocals(
            LambdaExpressionSyntax lambda,
            SemanticModel semanticModel,
            System.Threading.CancellationToken ct)
        {
            var results = new List<CapturedLocal>();
            // Conservative dedupe by simple identifier name: two captures with the same name declared
            // in disjoint scopes merge into a single diagnostic site (tolerated false-negative).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var lambdaSpan = lambda.Span;

            foreach (var idNode in lambda.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                ct.ThrowIfCancellationRequested();

                // Skip the right-hand `.Name` of a member access on another object (`obj.Member`): the chain
                // root (`obj`) is the reactive value, never the trailing member name. Counting the member name
                // as its own capture would double-report member-access deps (`state.SaveSlots`) and flag stable
                // accesses like `mutableRef.Current`. A `this.`-qualified member (`this._count`) is a capture of
                // the instance member itself and is kept; an unqualified bare instance member is collected too.
                if (idNode.Parent is MemberAccessExpressionSyntax parentMember
                    && parentMember.Name == idNode
                    && parentMember.Expression is not ThisExpressionSyntax) continue;

                var symbol = semanticModel.GetSymbolInfo(idNode, ct).Symbol;
                if (!IsCapturableSymbol(symbol, lambdaSpan)) continue;

                var name = idNode.Identifier.ValueText;
                if (!seen.Add(name)) continue;

                results.Add(new CapturedLocal(name, symbol!, idNode.GetLocation()));
            }
            return results;
        }

        /// <summary>
        /// Decides whether an identifier referenced inside the hook lambda is a closure capture that should
        /// participate in exhaustive-deps checking. A reactive value is one that can change between renders:
        /// locals declared in an enclosing scope plus mutable instance fields / properties read through the
        /// component closure. The match is deliberately conservative to avoid false positives:
        /// <list type="bullet">
        /// <item>Locals are captured only when declared outside the lambda body (enclosing scope).</item>
        /// <item>Instance fields qualify; <c>static</c>, <c>const</c>, and <c>readonly</c> fields are stable and excluded.</item>
        /// <item>Instance properties qualify; <c>static</c> and init-only properties are stable and excluded.</item>
        /// <item>Methods (<see cref="IMethodSymbol"/>), types, namespaces, and parameters are never flagged.</item>
        /// <item>Both unqualified (<c>_field</c>) and <c>this.</c>-qualified (<c>this._field</c>) instance members are tracked.</item>
        /// </list>
        /// </summary>
        private static bool IsCapturableSymbol(ISymbol? symbol, TextSpan lambdaSpan)
        {
            switch (symbol)
            {
                case ILocalSymbol local:
                {
                    var declRef = local.DeclaringSyntaxReferences.FirstOrDefault();
                    if (declRef is null) return false;
                    // Captured = declared outside the lambda body (i.e. lives in an enclosing scope).
                    return !lambdaSpan.Contains(declRef.Span);
                }
                // Instance field read through the component closure. Static / const / readonly fields hold a
                // value fixed after construction, so they are not reactive between renders and must not be
                // flagged (e.g. an injected `readonly IRepository _repo`).
                case IFieldSymbol field:
                    return !field.IsStatic && !field.IsConst && !field.IsReadOnly;
                // Instance property read through the component closure. Static and init-only properties hold a
                // value fixed after construction and are excluded; a getter-only property may be a computed
                // reactive value, so it is still tracked.
                case IPropertySymbol property:
                    return !property.IsStatic && !(property.SetMethod?.IsInitOnly ?? false);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true for locals whose value originates from a stable hook return: the setter / updater
        /// from <c>UseState</c>, the dispatch from <c>UseReducer</c>, or the ref from <c>UseRef</c> /
        /// <c>UseMutableRef</c>. Those returns are stable references across renders and need not appear in
        /// deps, so they are exempt. The decision is made by
        /// tracing the local's initializer back to a <c>Velvet.Hooks</c> call (origin-based), not by the
        /// local's declared type, so a plain <c>Action</c> field/parameter that happens to share the type is
        /// still flagged.
        /// </summary>
        private static bool IsStableHookReturnLocal(
            ILocalSymbol local,
            SemanticModel semanticModel,
            System.Threading.CancellationToken ct)
        {
            foreach (var declRef in local.DeclaringSyntaxReferences)
            {
                var declNode = declRef.GetSyntax(ct);
                if (FindInitializingHookCall(declNode, out var invocation, out var tupleSlot) is false) continue;
                if (BindsToStableHookSlot(invocation, tupleSlot, semanticModel, ct)) return true;
            }
            return false;
        }

        /// <summary>
        /// Finds the hook invocation that initializes a local declaration and, for tuple deconstruction, the
        /// tuple slot the local occupies. Handles the direct form (<c>var r = Hooks.UseRef(...)</c>, reported as
        /// <paramref name="tupleSlot"/> = -1) and tuple deconstruction (<c>var (a, b) = Hooks.UseState(...)</c>,
        /// where each element's declaring syntax is its <see cref="SingleVariableDesignationSyntax"/> inside a
        /// shared parenthesized designation).
        /// </summary>
        private static bool FindInitializingHookCall(
            SyntaxNode declNode,
            out InvocationExpressionSyntax? invocation,
            out int tupleSlot)
        {
            invocation = null;
            tupleSlot = -1;

            // Direct: `Type local = <invocation>;` — the declaring syntax is a VariableDeclaratorSyntax. The
            // whole return value is bound to the local (no tuple position to distinguish).
            if (declNode is VariableDeclaratorSyntax declarator)
            {
                invocation = declarator.Initializer?.Value as InvocationExpressionSyntax;
                return invocation is not null;
            }

            // Deconstruction: `var (a, b) = <invocation>;`. The local's declaring syntax is the inner
            // SingleVariableDesignationSyntax; its index within the enclosing ParenthesizedVariableDesignation
            // is the tuple slot, and the assigned value is the right-hand side of the enclosing declaration.
            if (declNode is SingleVariableDesignationSyntax designation &&
                designation.Parent is ParenthesizedVariableDesignationSyntax parenthesized)
            {
                tupleSlot = parenthesized.Variables.IndexOf(designation);
                var assignmentValue = parenthesized
                    .AncestorsAndSelf()
                    .Select(n => n switch
                    {
                        DeclarationExpressionSyntax decl when decl.Parent is AssignmentExpressionSyntax asn && asn.Left == decl => asn.Right,
                        VariableDeclarationSyntax vd when vd.Variables.Count == 1 => vd.Variables[0].Initializer?.Value,
                        _ => null,
                    })
                    .FirstOrDefault(v => v is not null);
                invocation = assignmentValue as InvocationExpressionSyntax;
                return invocation is not null;
            }

            return false;
        }

        /// <summary>
        /// Returns true when the local bound from <paramref name="invocation"/> at <paramref name="tupleSlot"/>
        /// is a stable hook return. Tuple positions matter: <c>UseState</c> / <c>UseReducer</c> return
        /// <c>(value, setter)</c> where only the setter (slot ≥ 1) is stable — the value (slot 0) changes
        /// between renders and must still be flagged when missing from deps. <c>UseRef</c> / <c>UseMutableRef</c>
        /// return the stable ref directly (no tuple, <paramref name="tupleSlot"/> = -1).
        /// </summary>
        private static bool BindsToStableHookSlot(
            InvocationExpressionSyntax? invocation,
            int tupleSlot,
            SemanticModel semanticModel,
            System.Threading.CancellationToken ct)
        {
            if (invocation?.Expression is not MemberAccessExpressionSyntax member) return false;
            var hookName = member.Name.Identifier.ValueText;
            if (!IsStableSlot(hookName, tupleSlot)) return false;

            var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
            var symbol = symbolInfo.Symbol as IMethodSymbol
                ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            return symbol?.ContainingType?.ToDisplayString() == VelvetWellKnownNames.HooksTypeFullName;
        }

        private static bool IsStableSlot(string hookName, int tupleSlot)
        {
            // UseState/UseReducer: the setter / dispatch lives at tuple slot 1+; slot 0 (value / state) changes.
            if (hookName == VelvetWellKnownNames.UseStateMethodName
                || hookName == VelvetWellKnownNames.UseReducerMethodName)
            {
                return tupleSlot >= 1;
            }
            // UseRef/UseMutableRef return the ref directly; only the whole-value direct assignment is stable.
            if (hookName == VelvetWellKnownNames.UseRefMethodName
                || hookName == VelvetWellKnownNames.UseMutableRefMethodName)
            {
                return tupleSlot < 0;
            }
            return false;
        }

        private readonly struct CapturedLocal
        {
            public CapturedLocal(string localName, ISymbol symbol, Location reportLocation)
            {
                LocalName = localName;
                Symbol = symbol;
                ReportLocation = reportLocation;
            }
            public string LocalName { get; }
            public ISymbol Symbol { get; }
            public Location ReportLocation { get; }
        }
    }
}
