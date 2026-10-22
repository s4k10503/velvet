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
    /// out at the call site. <see cref="ListSinks"/> finds where a list is built, and <see cref="NodeTrace"/>
    /// follows each value reaching one back to the factory calls that built it.
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
                var sinks = new ListSinks();
                start.RegisterOperationBlockAction(bodies.Add);
                start.RegisterOperationAction(ctx => sinks.AddInvocation((IInvocationOperation)ctx.Operation), OperationKind.Invocation);
                start.RegisterOperationAction(ctx => sinks.AddItem(((IReturnOperation)ctx.Operation).ReturnedValue), OperationKind.YieldReturn);
                start.RegisterOperationAction(ctx => sinks.AddAssignment((ISimpleAssignmentOperation)ctx.Operation), OperationKind.SimpleAssignment);
                start.RegisterCompilationEndAction(end => Report(end, bodies, sinks));
            });
        }

        private static void Report(CompilationAnalysisContext end, CallableBodies bodies, ListSinks sinks)
        {
            var found = new List<IInvocationOperation>();
            sinks.Trace(new NodeTrace(bodies, found));
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

    /// <summary>
    /// The places a list of children is built: a <c>Select</c> selector's return, a <c>yield return</c>, a value
    /// stored into an element of an array that is handed on, and a <c>List&lt;T&gt;.Add</c> whose list is made an array.
    /// </summary>
    internal sealed class ListSinks
    {
        private const string ListFullName = "System.Collections.Generic.List<T>";

        private readonly ConcurrentBag<IInvocationOperation> _selects = new();
        private readonly ConcurrentBag<IInvocationOperation> _adds = new();
        private readonly ConcurrentBag<IOperation> _items = new();

        // Calls that pass a List<T>, read when an Add's list is a parameter of the method it sits in.
        private readonly ConcurrentBag<IInvocationOperation> _listCalls = new();

        public void AddInvocation(IInvocationOperation invocation)
        {
            if (IsLinqSelect(invocation.TargetMethod)) _selects.Add(invocation);
            else if (IsListAdd(invocation)) _adds.Add(invocation);
            if (invocation.TargetMethod.Parameters.Any(parameter => IsList(parameter.Type))) _listCalls.Add(invocation);
        }

        public void AddItem(IOperation? value)
        {
            if (value is not null) _items.Add(value);
        }

        // An array is children once it is handed to a V factory or returned, so one filled for some other use, such as
        // a pool's, is not a sink.
        public void AddAssignment(ISimpleAssignmentOperation assignment)
        {
            if (assignment.Target is IArrayElementReferenceOperation { ArrayReference: ILocalReferenceOperation array }
                && IsHandedOn(array))
                _items.Add(assignment.Value);
        }

        private static bool IsHandedOn(ILocalReferenceOperation array)
        {
            foreach (var operation in NodeTrace.Root(array).DescendantsAndSelf())
            {
                var handed = operation switch
                {
                    IReturnOperation { ReturnedValue: { } value } => value,
                    IArgumentOperation { Parent: IInvocationOperation call } argument
                        when call.TargetMethod.ContainingType.ToDisplayString() == VelvetWellKnownNames.VTypeFullName
                        => argument.Value,
                    _ => null,
                };
                if (Reads(handed, array.Local)) return true;
            }
            return false;
        }

        private static bool Reads(IOperation? value, ILocalSymbol local)
        {
            while (value is IConversionOperation conversion) value = conversion.Operand;
            return value is ILocalReferenceOperation read && SymbolEqualityComparer.Default.Equals(read.Local, local);
        }

        public void Trace(NodeTrace trace)
        {
            foreach (var select in _selects) trace.Selector(select);
            foreach (var item in _items) trace.Item(item, null);
            var callers = _listCalls.ToLookup(
                call => call.TargetMethod.OriginalDefinition, SymbolEqualityComparer.Default);
            foreach (var add in _adds)
            {
                foreach (var via in ReachingCalls(add.Instance, callers, new HashSet<ISymbol>(SymbolEqualityComparer.Default)))
                {
                    foreach (var argument in add.Arguments) trace.Item(argument.Value, via);
                }
            }
        }

        private static bool IsLinqSelect(IMethodSymbol method) =>
            method.Name == nameof(Enumerable.Select)
            && method.ContainingType.ToDisplayString() == typeof(Enumerable).FullName;

        // A collection initializer's Add is implicit: like an array initializer, it is children written out.
        private static bool IsListAdd(IInvocationOperation invocation) =>
            !invocation.IsImplicit
            && invocation.TargetMethod.Name == nameof(List<object>.Add)
            && IsList(invocation.TargetMethod.ContainingType);

        private static bool IsList(ITypeSymbol type) =>
            type.OriginalDefinition.ToDisplayString() == ListFullName;

        // A list is children only once it is made an array, so one kept to hold nodes for some other use is not a
        // sink. A list that is a parameter is followed to each call passing it; the Add's own arguments are then read
        // through that call, whose arguments a parameter among them stands for.
        private static IEnumerable<IInvocationOperation?> ReachingCalls(
            IOperation? list, ILookup<ISymbol?, IInvocationOperation> callers, HashSet<ISymbol> path)
        {
            while (list is IConversionOperation conversion) list = conversion.Operand;
            if (list is ILocalReferenceOperation local)
            {
                if (IsMadeAnArray(local)) yield return null;
                yield break;
            }
            if (list is not IParameterReferenceOperation { Parameter: var parameter }) yield break;
            var method = parameter.ContainingSymbol.OriginalDefinition;
            if (!path.Add(method)) yield break;
            try
            {
                foreach (var call in callers[method])
                {
                    var passed = call.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == parameter.Ordinal);
                    if (ReachingCalls(passed?.Value, callers, path).Any()) yield return call;
                }
            }
            finally
            {
                path.Remove(method);
            }
        }

        private static bool IsMadeAnArray(ILocalReferenceOperation list)
        {
            foreach (var operation in NodeTrace.Root(list).DescendantsAndSelf())
            {
                if (operation is not IInvocationOperation { TargetMethod.Name: nameof(Enumerable.ToArray) } call) continue;
                if (Reads(call.Instance ?? (call.Arguments.Length > 0 ? call.Arguments[0].Value : null), list.Local))
                    return true;
            }
            return false;
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

        // The symbols the current path is inside. A path ends at a symbol it is already inside: the set does not grow
        // there, so MaxDepth, which counts its members, would never stop a helper that calls itself. A path is also cut
        // at MaxDepth symbols, so how deep a trace recurses does not depend on what the helpers do.
        private const int MaxDepth = 16;

        private readonly HashSet<ISymbol> _path = new(SymbolEqualityComparer.Default);

        public NodeTrace(CallableBodies bodies, List<IInvocationOperation> found)
        {
            _bodies = bodies;
            _found = found;
        }

        public void Selector(IInvocationOperation select)
        {
            foreach (var argument in select.Arguments)
            {
                foreach (var (target, blocks) in DelegateTargets(argument.Value)) Returns(target, blocks, null);
            }
        }

        public void Item(IOperation value, IInvocationOperation? via) =>
            Collect(value, via is null ? null : new Frame(via.TargetMethod, via.Arguments, null));

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
