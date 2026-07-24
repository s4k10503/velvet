using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Velvet.SourceGenerators.PurityAnalysis;

namespace Velvet.SourceGenerators.ReactiveScope
{
    /// <summary>
    /// Internal static API that statically derives, for each sub-expression in Render(), the set of symbols whose changes require recomputation.
    /// Errs toward the conservative fallback (Dependencies = null); false negatives (missed deps) are not tolerated, while false positives (all deps) are.
    /// <para>
    /// Intentionally unwired groundwork: this analysis is not referenced by any generator or analyzer yet. Auto-memoization is currently
    /// provided coarsely by the IL post-processor (which memoizes a component on the union of its hook inputs and props); this fine-grained,
    /// per-sub-expression reactive-scope analysis is the basis for a future, more precise scope inference.
    /// </para>
    /// </summary>
    internal static class ReactiveScopeAnalyzer
    {
        /// <summary>
        /// Analyzes the dependencies of a single sub-expression.
        /// </summary>
        public static ReactiveScopeResult Analyze(IOperation expression, SemanticModel semanticModel, CancellationToken ct)
        {
            if (expression is null || semanticModel is null)
            {
                return ReactiveScopeResult.Fallback(
                    ImmutableArray.Create(new ScopeDiagnostic(ScopeDiagnosticKind.UnresolvableSymbol, "<null>", null)));
            }

            ct.ThrowIfCancellationRequested();

            var purityCache = new Dictionary<IMethodSymbol, PurityResult>(SymbolEqualityComparer.Default);
            var fallback = CheckExpressionPurity(expression, semanticModel.Compilation, purityCache, ct);
            if (fallback.HasValue)
            {
                return ReactiveScopeResult.Fallback(fallback.Value);
            }

            var raw = CollectExpressionDependencies(expression, ct);

            var methodBody = FindEnclosingMethodBody(expression);
            var resolved = methodBody is null
                ? raw
                : LocalDataFlowResolver.Build(methodBody, ct).Resolve(raw);

            return ReactiveScopeResult.Pure(resolved);
        }

        /// <summary>
        /// Walks the entire Render() method and returns the analysis result for each sub-expression on the CFG.
        /// Callers look up this dictionary by IOperation key to produce cache slots.
        /// </summary>
        public static ImmutableDictionary<IOperation, ReactiveScopeResult> AnalyzeRenderMethod(
            IMethodSymbol render,
            Compilation compilation,
            CancellationToken ct)
        {
            var empty = ImmutableDictionary<IOperation, ReactiveScopeResult>.Empty;
            if (render is null || compilation is null)
            {
                return empty;
            }

            ct.ThrowIfCancellationRequested();

            var methodBody = TryGetMethodBody(render, compilation, ct);
            if (methodBody is null)
            {
                return empty;
            }

            var resolver = LocalDataFlowResolver.Build(methodBody, ct);
            var builder = ImmutableDictionary.CreateBuilder<IOperation, ReactiveScopeResult>();
            // PurityAnalyzer.Analyze walks the entire method body, so cache it per-method to handle cases where the same callee appears
            // multiple times within a single Render.
            var purityCache = new Dictionary<IMethodSymbol, PurityResult>(SymbolEqualityComparer.Default);

            foreach (var expression in EnumerateInterestingOperations(methodBody))
            {
                ct.ThrowIfCancellationRequested();

                var fallback = CheckExpressionPurity(expression, compilation, purityCache, ct);
                if (fallback.HasValue)
                {
                    builder[expression] = ReactiveScopeResult.Fallback(fallback.Value);
                    continue;
                }

                var raw = CollectExpressionDependencies(expression, ct);
                builder[expression] = ReactiveScopeResult.Pure(resolver.Resolve(raw));
            }

            var bodyFallback = CheckExpressionPurity(methodBody, compilation, purityCache, ct);
            if (bodyFallback.HasValue)
            {
                builder[methodBody] = ReactiveScopeResult.Fallback(bodyFallback.Value);
            }
            else
            {
                var bodyRaw = CfgScopeWalker.CollectMethodBodyDependencies(methodBody, ct);
                builder[methodBody] = ReactiveScopeResult.Pure(resolver.Resolve(bodyRaw));
            }

            return builder.ToImmutable();
        }

        private static ImmutableArray<ISymbol> CollectExpressionDependencies(IOperation expression, CancellationToken ct) =>
            CfgScopeWalker.CollectExpressionDependencies(expression, ct);

        /// <summary>
        /// Converts a single dependency symbol into an access expression + type display that is evaluable in generated code.
        /// Supports cases reached via Render parameters or via component-this members; everything else returns a fallback
        /// <see cref="DependencyAccessPath"/> (AccessPath == null).
        /// </summary>
        public static DependencyAccessPath GetAccessPath(
            ISymbol symbol,
            IParameterSymbol renderParameter,
            ITypeSymbol componentType)
        {
            if (symbol is null || renderParameter is null)
            {
                return DependencyAccessPath.Fallback("null-symbol");
            }

            switch (symbol)
            {
                case IParameterSymbol p when SymbolEqualityComparer.Default.Equals(p, renderParameter):
                    return new DependencyAccessPath(
                        renderParameter.Name,
                        renderParameter.Type.ToDisplayString(Shared.SymbolDisplayFormats.AccessPath));

                case IParameterSymbol p:
                    return new DependencyAccessPath(
                        p.Name,
                        p.Type.ToDisplayString(Shared.SymbolDisplayFormats.AccessPath));

                case IPropertySymbol prop:
                    return BuildMemberAccess(prop.ContainingType, prop.Name, prop.Type, renderParameter, componentType);

                case IFieldSymbol fld:
                    return BuildMemberAccess(fld.ContainingType, fld.Name, fld.Type, renderParameter, componentType);

                case ILocalSymbol:
                    // LocalDataFlowResolver should have expanded these; reaching here means a remaining one — fall back.
                    return DependencyAccessPath.Fallback("unresolved-local");

                default:
                    return DependencyAccessPath.Fallback("unsupported-symbol");
            }
        }

        private static DependencyAccessPath BuildMemberAccess(
            INamedTypeSymbol containingType,
            string memberName,
            ITypeSymbol memberType,
            IParameterSymbol renderParameter,
            ITypeSymbol componentType)
        {
            var typeDisplay = memberType.ToDisplayString(Shared.SymbolDisplayFormats.AccessPath);

            if (SymbolEqualityComparer.Default.Equals(containingType, renderParameter.Type))
            {
                return new DependencyAccessPath($"{renderParameter.Name}.{memberName}", typeDisplay);
            }

            if (componentType is not null && IsSameOrBaseType(componentType, containingType))
            {
                return new DependencyAccessPath($"this.{memberName}", typeDisplay);
            }

            return DependencyAccessPath.Fallback("unresolvable-member");
        }

        private static bool IsSameOrBaseType(ITypeSymbol candidate, ITypeSymbol baseType)
        {
            for (var current = candidate; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }
            }
            return false;
        }

        private static IOperation? TryGetMethodBody(IMethodSymbol method, Compilation compilation, CancellationToken ct)
        {
            foreach (var syntaxRef in method.DeclaringSyntaxReferences)
            {
                var node = syntaxRef.GetSyntax(ct);
                SyntaxNode? bodyNode = node switch
                {
                    MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
                    LocalFunctionStatementSyntax lf => (SyntaxNode?)lf.Body ?? lf.ExpressionBody,
                    AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody,
                    ArrowExpressionClauseSyntax arrow => arrow,
                    _ => null,
                };
                if (bodyNode is null)
                {
                    continue;
                }

                SemanticModel model;
                try
                {
                    model = compilation.GetSemanticModel(bodyNode.SyntaxTree);
                }
                catch
                {
                    continue;
                }

                var op = model.GetOperation(bodyNode, ct);
                if (op is not null)
                {
                    return op;
                }
            }
            return null;
        }

        private static IOperation? FindEnclosingMethodBody(IOperation expression)
        {
            for (var cur = expression.Parent; cur is not null; cur = cur.Parent)
            {
                if (cur is IBlockOperation or IMethodBodyOperation)
                {
                    return cur;
                }
            }
            return null;
        }

        /// <summary>
        /// Enumerates the sub-expressions we want to analyze: return values, invocations, object creations,
        /// conditional/switch expressions, and binary operations.
        /// We pick meaningful units rather than leaf nodes so that cache slots are sliced per expression.
        /// </summary>
        private static IEnumerable<IOperation> EnumerateInterestingOperations(IOperation root)
        {
            var stack = new Stack<IOperation>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var op = stack.Pop();
                switch (op)
                {
                    case IReturnOperation ret when ret.ReturnedValue is not null:
                        yield return ret.ReturnedValue;
                        break;
                    case IInvocationOperation:
                    case IObjectCreationOperation:
                    case IConditionalOperation:
                    case ISwitchExpressionOperation:
                    case IBinaryOperation:
                        yield return op;
                        break;
                }
                foreach (var child in op.ChildOperations)
                {
                    stack.Push(child);
                }
            }
        }

        /// <summary>
        /// Checks the given expression for anything that would make treating it as a pure function of its
        /// dependencies unsound: calls where <see cref="PurityAnalyzer"/> returns Impure / Unknown, virtual /
        /// abstract / dynamic dispatch, and directly observable side effects (field/property/array/ref-out
        /// assignment, event subscription, lock, await).
        /// Returns the conservative fallback reason set if any are found; otherwise null.
        /// </summary>
        private static ImmutableArray<ScopeDiagnostic>? CheckExpressionPurity(
            IOperation expression,
            Compilation compilation,
            Dictionary<IMethodSymbol, PurityResult> purityCache,
            CancellationToken ct)
        {
            var inspector = new FallbackReasonCollector(compilation, purityCache, ct);
            inspector.Visit(expression);
            return inspector.Reasons.Count == 0
                ? null
                : inspector.Reasons.ToImmutableArray();
        }

        private sealed class FallbackReasonCollector : OperationWalker
        {
            private readonly Compilation _compilation;
            private readonly Dictionary<IMethodSymbol, PurityResult> _purityCache;
            private readonly CancellationToken _ct;
            public List<ScopeDiagnostic> Reasons { get; } = new();

            public FallbackReasonCollector(
                Compilation compilation,
                Dictionary<IMethodSymbol, PurityResult> purityCache,
                CancellationToken ct)
            {
                _compilation = compilation;
                _purityCache = purityCache;
                _ct = ct;
            }

            public override void Visit(IOperation? operation)
            {
                _ct.ThrowIfCancellationRequested();
                base.Visit(operation);
            }

            public override void VisitDynamicInvocation(IDynamicInvocationOperation operation)
            {
                AddDynamicReason("dynamic invocation", operation.Syntax?.GetLocation());
                base.VisitDynamicInvocation(operation);
            }

            public override void VisitDynamicMemberReference(IDynamicMemberReferenceOperation operation)
            {
                AddDynamicReason(operation.MemberName ?? "dynamic", operation.Syntax?.GetLocation());
                base.VisitDynamicMemberReference(operation);
            }

            public override void VisitDynamicObjectCreation(IDynamicObjectCreationOperation operation)
            {
                AddDynamicReason("dynamic object creation", operation.Syntax?.GetLocation());
                base.VisitDynamicObjectCreation(operation);
            }

            public override void VisitDynamicIndexerAccess(IDynamicIndexerAccessOperation operation)
            {
                AddDynamicReason("dynamic indexer", operation.Syntax?.GetLocation());
                base.VisitDynamicIndexerAccess(operation);
            }

            private void AddDynamicReason(string symbolDisplay, Location? location) =>
                Reasons.Add(new ScopeDiagnostic(ScopeDiagnosticKind.DynamicAccess, symbolDisplay, location));

            public override void VisitInvocation(IInvocationOperation operation)
            {
                ClassifyMethod(operation.TargetMethod, operation.Syntax?.GetLocation());
                base.VisitInvocation(operation);
            }

            public override void VisitObjectCreation(IObjectCreationOperation operation)
            {
                if (operation.Constructor is { } ctor)
                {
                    ClassifyMethod(ctor, operation.Syntax?.GetLocation());
                }
                base.VisitObjectCreation(operation);
            }

            public override void VisitPropertyReference(IPropertyReferenceOperation operation)
            {
                if (operation.Property is { } prop && KnownPurityDatabase.IsImpureProperty(prop))
                {
                    Reasons.Add(new ScopeDiagnostic(
                        ScopeDiagnosticKind.Impure,
                        prop.ToDisplayString(),
                        operation.Syntax?.GetLocation()));
                }
                base.VisitPropertyReference(operation);
            }

            public override void VisitSimpleAssignment(ISimpleAssignmentOperation operation)
            {
                RecordAssignmentIfSideEffecting(operation.Target, operation.Syntax?.GetLocation());
                base.VisitSimpleAssignment(operation);
            }

            public override void VisitCompoundAssignment(ICompoundAssignmentOperation operation)
            {
                RecordAssignmentIfSideEffecting(operation.Target, operation.Syntax?.GetLocation());
                base.VisitCompoundAssignment(operation);
            }

            public override void VisitIncrementOrDecrement(IIncrementOrDecrementOperation operation)
            {
                RecordAssignmentIfSideEffecting(operation.Target, operation.Syntax?.GetLocation());
                base.VisitIncrementOrDecrement(operation);
            }

            public override void VisitCoalesceAssignment(ICoalesceAssignmentOperation operation)
            {
                // `x ??= y` is also an IAssignmentOperation; if the target is a field/property it is a side effect.
                RecordAssignmentIfSideEffecting(operation.Target, operation.Syntax?.GetLocation());
                base.VisitCoalesceAssignment(operation);
            }

            public override void VisitDeconstructionAssignment(IDeconstructionAssignmentOperation operation)
            {
                // For `(a, b.Prop) = tuple`, inspect each LHS tuple element for observable side effects.
                InspectDeconstructionTarget(operation.Target, operation.Syntax?.GetLocation());
                base.VisitDeconstructionAssignment(operation);
            }

            public override void VisitEventAssignment(IEventAssignmentOperation operation)
            {
                // `e += handler` / `e -= handler` are observable side effects (subscribe / unsubscribe).
                var symbol = (operation.EventReference as IEventReferenceOperation)?.Event?.ToDisplayString() ?? "event";
                Reasons.Add(new ScopeDiagnostic(
                    ScopeDiagnosticKind.Impure,
                    symbol,
                    operation.Syntax?.GetLocation()));
                base.VisitEventAssignment(operation);
            }

            public override void VisitLock(ILockOperation operation)
            {
                // `lock` acquires a Monitor (a side effect). If it appears inside Render, always fall back conservatively.
                Reasons.Add(new ScopeDiagnostic(
                    ScopeDiagnosticKind.Impure,
                    "lock",
                    operation.Syntax?.GetLocation()));
                base.VisitLock(operation);
            }

            public override void VisitAwait(IAwaitOperation operation)
            {
                // Render assumes synchronous execution. `await` is a temporal side effect, so fall back.
                Reasons.Add(new ScopeDiagnostic(
                    ScopeDiagnosticKind.Impure,
                    "await",
                    operation.Syntax?.GetLocation()));
                base.VisitAwait(operation);
            }

            private void InspectDeconstructionTarget(IOperation target, Location? location)
            {
                if (target is ITupleOperation tuple)
                {
                    foreach (var element in tuple.Elements)
                    {
                        InspectDeconstructionTarget(element, element.Syntax?.GetLocation() ?? location);
                    }
                    return;
                }
                RecordAssignmentIfSideEffecting(target, location);
            }

            private void RecordAssignmentIfSideEffecting(IOperation target, Location? location)
            {
                // Assignments to local variables are expanded by later analysis as derived variables of the incoming values, so they are not treated as side effects.
                // Fields / properties / array elements / out/ref params are observable side effects, so fall back to null.
                switch (target)
                {
                    case IFieldReferenceOperation fieldRef:
                        Reasons.Add(new ScopeDiagnostic(
                            ScopeDiagnosticKind.Impure,
                            fieldRef.Field.ToDisplayString(),
                            location));
                        break;
                    case IPropertyReferenceOperation propRef:
                        Reasons.Add(new ScopeDiagnostic(
                            ScopeDiagnosticKind.Impure,
                            propRef.Property.ToDisplayString(),
                            location));
                        break;
                    case IArrayElementReferenceOperation arrRef:
                        Reasons.Add(new ScopeDiagnostic(
                            ScopeDiagnosticKind.Impure,
                            arrRef.ArrayReference.Type?.ToDisplayString() ?? "array",
                            location));
                        break;
                    case IParameterReferenceOperation paramRef when paramRef.Parameter.RefKind is RefKind.Ref or RefKind.Out:
                        Reasons.Add(new ScopeDiagnostic(
                            ScopeDiagnosticKind.Impure,
                            paramRef.Parameter.ToDisplayString(),
                            location));
                        break;
                }
            }

            private void ClassifyMethod(IMethodSymbol target, Location? location)
            {
                if (target is null)
                {
                    Reasons.Add(new ScopeDiagnostic(ScopeDiagnosticKind.UnresolvableSymbol, "<null>", location));
                    return;
                }

                if (target.MethodKind is MethodKind.AnonymousFunction or MethodKind.LocalFunction)
                {
                    return;
                }

                if (!_purityCache.TryGetValue(target, out var result))
                {
                    result = PurityAnalyzer.Analyze(target, _compilation, _ct);
                    _purityCache[target] = result;
                }
                switch (result.Purity)
                {
                    case Purity.Pure:
                        return;
                    case Purity.Impure:
                        Reasons.Add(new ScopeDiagnostic(
                            ScopeDiagnosticKind.Impure,
                            target.ToDisplayString(),
                            location));
                        return;
                    case Purity.Unknown:
                        if (target.IsAbstract || target.IsVirtual || target.IsOverride)
                        {
                            Reasons.Add(new ScopeDiagnostic(
                                ScopeDiagnosticKind.Virtual,
                                target.ToDisplayString(),
                                location));
                        }
                        else
                        {
                            Reasons.Add(new ScopeDiagnostic(
                                ScopeDiagnosticKind.UnknownCall,
                                target.ToDisplayString(),
                                location));
                        }
                        return;
                }
            }
        }
    }
}
