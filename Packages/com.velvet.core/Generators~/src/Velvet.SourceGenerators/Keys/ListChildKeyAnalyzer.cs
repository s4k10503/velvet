using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Velvet.SourceGenerators.Diagnostics;
using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators.Keys
{
    /// <summary>
    /// Reports <see cref="ReconcilerDiagnostics.Vel600ListChildWithoutKey"/>: React's missing-key warning, given at
    /// compile time because the runtime receives a built list as the same array type as a children array written
    /// out at the call site. A list is built by a <c>Select</c> selector or by <c>List&lt;T&gt;.Add</c>, and
    /// <see cref="NodeTrace"/> follows each value reaching one back to the factory calls that built it.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ListChildKeyAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(ReconcilerDiagnostics.Vel600ListChildWithoutKey);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                // A helper's body may be analyzed after the list that calls it, so every sink waits for the end.
                var bodies = new CallableBodies();
                var sinks = new ConcurrentBag<IInvocationOperation>();
                start.RegisterOperationBlockAction(bodies.Add);
                start.RegisterOperationAction(
                    ctx => { if (IsSink((IInvocationOperation)ctx.Operation)) sinks.Add((IInvocationOperation)ctx.Operation); },
                    OperationKind.Invocation);
                start.RegisterCompilationEndAction(end => Report(end, bodies, sinks));
            });
        }

        private static bool IsSink(IInvocationOperation invocation) =>
            IsLinqSelect(invocation.TargetMethod) || IsListAdd(invocation);

        private static bool IsLinqSelect(IMethodSymbol method) =>
            method.Name == nameof(System.Linq.Enumerable.Select)
            && method.ContainingType.ToDisplayString() == typeof(System.Linq.Enumerable).FullName;

        // A collection initializer's Add is implicit: like an array initializer, it is children written out. A list
        // is children only once it is made an array, so one kept to hold nodes for some other use is not a sink.
        private static bool IsListAdd(IInvocationOperation invocation) =>
            !invocation.IsImplicit
            && invocation.TargetMethod.Name == nameof(List<object>.Add)
            && invocation.TargetMethod.ContainingType.OriginalDefinition.ToDisplayString()
                == "System.Collections.Generic.List<T>"
            && invocation.Instance is ILocalReferenceOperation list
            && IsMadeAnArray(list);

        private static bool IsMadeAnArray(ILocalReferenceOperation list)
        {
            foreach (var operation in NodeTrace.Root(list).DescendantsAndSelf())
            {
                if (operation is not IInvocationOperation { TargetMethod.Name: nameof(Enumerable.ToArray) } call) continue;
                var source = call.Instance ?? (call.Arguments.Length > 0 ? call.Arguments[0].Value : null);
                while (source is IConversionOperation conversion) source = conversion.Operand;
                if (source is ILocalReferenceOperation read && SymbolEqualityComparer.Default.Equals(read.Local, list.Local))
                    return true;
            }
            return false;
        }

        private static void Report(
            CompilationAnalysisContext end, CallableBodies bodies, IEnumerable<IInvocationOperation> sinks)
        {
            var found = new List<IInvocationOperation>();
            var trace = new NodeTrace(bodies, found);
            foreach (var sink in sinks) trace.Sink(sink, IsLinqSelect(sink.TargetMethod));
            var reported = new HashSet<Location>();
            foreach (var factory in found)
            {
                if (!NodeTrace.BuildsUnkeyedNode(factory)) continue;
                var location = factory.Syntax.GetLocation();
                if (!reported.Add(location)) continue;
                end.ReportDiagnostic(Diagnostic.Create(
                    ReconcilerDiagnostics.Vel600ListChildWithoutKey, location, factory.TargetMethod.Name));
            }
        }
    }

    /// <summary>The operation blocks of the methods and local functions this compilation declares with a body.</summary>
    internal sealed class CallableBodies
    {
        private readonly ConcurrentDictionary<ISymbol, ImmutableArray<IOperation>> _bodies =
            new(SymbolEqualityComparer.Default);

        public void Add(OperationBlockAnalysisContext ctx)
        {
            if (ctx.OwningSymbol is IMethodSymbol method) _bodies[method.OriginalDefinition] = ctx.OperationBlocks;
            foreach (var block in ctx.OperationBlocks)
            {
                foreach (var operation in block.DescendantsAndSelf())
                {
                    if (operation is ILocalFunctionOperation { Body: { } body } local)
                        _bodies[local.Symbol.OriginalDefinition] = ImmutableArray.Create<IOperation>(body);
                }
            }
        }

        public ImmutableArray<IOperation> Of(IMethodSymbol method) =>
            _bodies.TryGetValue(method.OriginalDefinition, out var blocks) ? blocks : ImmutableArray<IOperation>.Empty;
    }

    /// <summary>
    /// Follows a value back to the <c>V</c> factory calls that built it: through a cast, a conditional, a local's
    /// assignments, the return of a method or local function this compilation declares, a delegate's target, and
    /// from a callee's parameter to the argument the call passed it.
    /// </summary>
    internal sealed class NodeTrace
    {
        private const string KeyParameterName = "key";

        private sealed class Frame
        {
            public Frame(IMethodSymbol callee, ImmutableArray<IArgumentOperation> arguments, Frame? caller)
            {
                Callee = callee;
                Arguments = arguments;
                Caller = caller;
            }

            public IMethodSymbol Callee { get; }
            public ImmutableArray<IArgumentOperation> Arguments { get; }
            public Frame? Caller { get; }
        }

        private readonly CallableBodies _bodies;
        private readonly List<IInvocationOperation> _found;

        // The symbols the current path is inside: a recursive helper or a self-assigned local ends its path. A path
        // is also cut at MaxDepth symbols, so how deep a trace recurses does not depend on what the helpers do.
        private const int MaxDepth = 16;

        private readonly HashSet<ISymbol> _path = new(SymbolEqualityComparer.Default);

        public NodeTrace(CallableBodies bodies, List<IInvocationOperation> found)
        {
            _bodies = bodies;
            _found = found;
        }

        public void Sink(IInvocationOperation sink, bool isSelect)
        {
            if (!isSelect)
            {
                foreach (var argument in sink.Arguments) Collect(argument.Value, null);
                return;
            }
            foreach (var argument in sink.Arguments)
            {
                foreach (var (target, blocks) in DelegateTargets(argument.Value)) Returns(target, blocks, null);
            }
        }

        public static bool BuildsUnkeyedNode(IInvocationOperation factory) =>
            factory.TargetMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                != VelvetWellKnownNames.TextNodeFullName
            && IsVNode(factory.TargetMethod.ReturnType)
            && !HasKey(factory);

        private void Collect(IOperation? value, Frame? frame)
        {
            switch (value)
            {
                case IConversionOperation conversion:
                    Collect(conversion.Operand, frame);
                    break;
                case IConditionalOperation conditional:
                    Collect(conditional.WhenTrue, frame);
                    Collect(conditional.WhenFalse, frame);
                    break;
                case ILocalReferenceOperation reference:
                    Within(reference.Local, () => { foreach (var assigned in AssignedValues(reference)) Collect(assigned, frame); });
                    break;
                case IParameterReferenceOperation parameter:
                    Collect(ArgumentFor(parameter.Parameter, frame, out var caller), caller);
                    break;
                case IInvocationOperation call:
                    Call(call, frame);
                    break;
            }
        }

        private void Call(IInvocationOperation call, Frame? frame)
        {
            if (call.TargetMethod.ContainingType.ToDisplayString() == VelvetWellKnownNames.VTypeFullName)
            {
                Factory(call, frame);
                return;
            }
            if (call.TargetMethod.MethodKind == MethodKind.DelegateInvoke)
            {
                foreach (var (target, blocks) in DelegateTargets(call.Instance))
                    Returns(target, blocks, new Frame(target, call.Arguments, frame));
                return;
            }
            Returns(call.TargetMethod, _bodies.Of(call.TargetMethod), new Frame(call.TargetMethod, call.Arguments, frame));
        }

        // V.When is declared to return VNode itself: the node it hands back is the one its factory returns.
        private void Factory(IInvocationOperation call, Frame? frame)
        {
            if (call.TargetMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                != VelvetWellKnownNames.VNodeFullName)
            {
                _found.Add(call);
                return;
            }
            foreach (var argument in call.Arguments)
            {
                if (argument.Value is IDelegateCreationOperation { Target: IAnonymousFunctionOperation factory })
                    Returns(factory.Symbol, ImmutableArray.Create<IOperation>(factory.Body), frame);
            }
        }

        private void Returns(IMethodSymbol callable, ImmutableArray<IOperation> blocks, Frame? frame) =>
            Within(callable, () =>
            {
                foreach (var block in blocks)
                {
                    foreach (var returned in ReturnedValues(block)) Collect(returned, frame);
                }
            });

        private void Within(ISymbol symbol, System.Action trace)
        {
            if (_path.Count >= MaxDepth || !_path.Add(symbol.OriginalDefinition)) return;
            try
            {
                trace();
            }
            finally
            {
                _path.Remove(symbol.OriginalDefinition);
            }
        }

        // A nested lambda or local function returns to whoever runs it, which a delegate's trace reaches on its own.
        private static IEnumerable<IOperation> ReturnedValues(IOperation operation)
        {
            foreach (var child in operation.ChildOperations)
            {
                if (child is IAnonymousFunctionOperation or ILocalFunctionOperation) continue;
                if (child is IReturnOperation { ReturnedValue: { } value }) yield return value;
                foreach (var nested in ReturnedValues(child)) yield return nested;
            }
        }

        private IEnumerable<(IMethodSymbol, ImmutableArray<IOperation>)> DelegateTargets(IOperation? value)
        {
            switch (value)
            {
                case IConversionOperation conversion:
                    return DelegateTargets(conversion.Operand);
                case IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda }:
                    return new[] { (lambda.Symbol, ImmutableArray.Create<IOperation>(lambda.Body)) };
                case IDelegateCreationOperation { Target: IMethodReferenceOperation group }:
                    return new[] { (group.Method, _bodies.Of(group.Method)) };
                case ILocalReferenceOperation reference:
                    return AssignedDelegateTargets(reference);
                default:
                    return System.Array.Empty<(IMethodSymbol, ImmutableArray<IOperation>)>();
            }
        }

        private IEnumerable<(IMethodSymbol, ImmutableArray<IOperation>)> AssignedDelegateTargets(
            ILocalReferenceOperation reference)
        {
            var targets = new List<(IMethodSymbol, ImmutableArray<IOperation>)>();
            Within(reference.Local, () =>
            {
                foreach (var assigned in AssignedValues(reference)) targets.AddRange(DelegateTargets(assigned));
            });
            return targets;
        }

        // Every value the body declaring the local assigns it, whichever one reaches the read.
        private static IEnumerable<IOperation> AssignedValues(ILocalReferenceOperation reference)
        {
            foreach (var operation in Root(reference).DescendantsAndSelf())
            {
                var assigned = operation switch
                {
                    IVariableDeclaratorOperation declarator
                        when SymbolEqualityComparer.Default.Equals(declarator.Symbol, reference.Local)
                        => declarator.Initializer?.Value,
                    ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment
                        when SymbolEqualityComparer.Default.Equals(target.Local, reference.Local)
                        => assignment.Value,
                    _ => null,
                };
                if (assigned is not null) yield return assigned;
            }
        }

        public static IOperation Root(IOperation operation)
        {
            while (operation.Parent is { } parent) operation = parent;
            return operation;
        }

        // A lambda's body reads its enclosing method's parameters, so the frame is the nearest one declaring it.
        private static IOperation? ArgumentFor(IParameterSymbol parameter, Frame? frame, out Frame? caller)
        {
            for (var current = frame; current is not null; current = current.Caller)
            {
                if (!SymbolEqualityComparer.Default.Equals(
                        current.Callee.OriginalDefinition, parameter.ContainingSymbol.OriginalDefinition)) continue;
                caller = current.Caller;
                foreach (var argument in current.Arguments)
                {
                    if (argument.Parameter?.Ordinal == parameter.Ordinal) return argument.Value;
                }
                return null;
            }
            caller = null;
            return null;
        }

        private static bool IsVNode(ITypeSymbol type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == VelvetWellKnownNames.VNodeFullName)
                    return true;
            }
            return false;
        }

        // An omitted key reaches the call as its default, a constant null, as a written null does.
        private static bool HasKey(IInvocationOperation factory)
        {
            foreach (var argument in factory.Arguments)
            {
                if (argument.Parameter?.Name != KeyParameterName) continue;
                var constant = argument.Value.ConstantValue;
                return !constant.HasValue || constant.Value is not null;
            }
            return false;
        }
    }
}
