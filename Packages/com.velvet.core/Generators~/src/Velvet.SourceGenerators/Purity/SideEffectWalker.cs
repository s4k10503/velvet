using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Velvet.SourceGenerators.PurityAnalysis
{
    /// <summary>
    /// Walks the method body's <see cref="IOperation"/> tree and collects side effects as <see cref="ImpurityReason"/> entries.
    /// </summary>
    /// <remarks>
    /// When <paramref name="remainingDepth"/> &gt; 0, invocations whose callee would otherwise be reported as UnknownCall
    /// are recursed into via <see cref="PurityAnalyzer.AnalyzeCore"/>. Pure callees absorb silently; Impure callees
    /// propagate as KnownImpureCall. Cycles are broken with a shared visited set.
    /// </remarks>
    internal sealed class SideEffectWalker : OperationWalker
    {
        private readonly ImmutableArray<ImpurityReason>.Builder _reasons = ImmutableArray.CreateBuilder<ImpurityReason>();
        private readonly IMethodSymbol _owner;
        private readonly CancellationToken _ct;
        private readonly Compilation? _compilation;
        private readonly int _remainingDepth;
        private readonly HashSet<IMethodSymbol>? _visited;
        private int _closureDepth;

        public SideEffectWalker(IMethodSymbol owner, CancellationToken ct)
            : this(owner, ct, compilation: null, remainingDepth: 0, visited: null)
        {
        }

        public SideEffectWalker(
            IMethodSymbol owner,
            CancellationToken ct,
            Compilation? compilation,
            int remainingDepth,
            HashSet<IMethodSymbol>? visited)
        {
            _owner = owner;
            _ct = ct;
            _compilation = compilation;
            _remainingDepth = remainingDepth;
            _visited = visited;
        }

        public ImmutableArray<ImpurityReason> Reasons => _reasons.ToImmutable();

        public override void Visit(IOperation? operation)
        {
            _ct.ThrowIfCancellationRequested();
            switch (operation)
            {
                case ILoopOperation loop:
                    Add(ImpurityKind.Loop, loop.LoopKind.ToString(), loop.Syntax?.GetLocation());
                    break;
                case IAssignmentOperation assignment:
                    InspectAssignment(assignment.Target);
                    break;
                case IIncrementOrDecrementOperation increment:
                    InspectAssignment(increment.Target);
                    break;
            }
            base.Visit(operation);
        }

        public override void VisitThrow(IThrowOperation operation)
        {
            var symbol = operation.Exception?.Type?.ToDisplayString() ?? "throw";
            Add(ImpurityKind.Throw, symbol, operation.Syntax?.GetLocation());
            base.VisitThrow(operation);
        }

        public override void VisitInvocation(IInvocationOperation operation)
        {
            if (operation.TargetMethod is { } target)
            {
                ClassifyInvocation(target, operation.Syntax?.GetLocation());
            }
            base.VisitInvocation(operation);
        }

        public override void VisitObjectCreation(IObjectCreationOperation operation)
        {
            // User-defined / unknown constructors are assumed Pure; only record known types (e.g. Random)
            // that KnownPurityDatabase marks Impure. Constructor call-graph propagation is intentionally excluded;
            // method invocations are handled via ClassifyInvocation.
            if (operation.Constructor is { } ctor &&
                KnownPurityDatabase.TryClassify(ctor, out var kind) &&
                kind == KnownPurity.Impure)
            {
                Add(ImpurityKind.KnownImpureCall, ctor.ToDisplayString(), operation.Syntax?.GetLocation());
            }
            base.VisitObjectCreation(operation);
        }

        public override void VisitPropertyReference(IPropertyReferenceOperation operation)
        {
            if (operation.Property is { } prop && KnownPurityDatabase.IsImpureProperty(prop))
            {
                Add(ImpurityKind.KnownImpureCall, prop.ToDisplayString(), operation.Syntax?.GetLocation());
            }
            base.VisitPropertyReference(operation);
        }

        public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation)
        {
            _closureDepth++;
            try
            {
                base.VisitAnonymousFunction(operation);
            }
            finally
            {
                _closureDepth--;
            }
        }

        public override void VisitLocalReference(ILocalReferenceOperation operation)
        {
            if (_closureDepth > 0 && IsCapturedFromOwner(operation.Local) && !IsImmutableCapture(operation.Local.Type))
            {
                Add(ImpurityKind.ClosureCapture, operation.Local.ToDisplayString(), operation.Syntax?.GetLocation());
            }
            base.VisitLocalReference(operation);
        }

        public override void VisitParameterReference(IParameterReferenceOperation operation)
        {
            if (_closureDepth > 0 && IsCapturedFromOwner(operation.Parameter) && !IsImmutableCapture(operation.Parameter.Type))
            {
                Add(ImpurityKind.ClosureCapture, operation.Parameter.ToDisplayString(), operation.Syntax?.GetLocation());
            }
            base.VisitParameterReference(operation);
        }

        /// <summary>
        /// True for captures that are safe to read but cannot mutate the captured slot from inside the closure.
        /// Primitive value types, <see cref="string"/>, decimal, enums, <c>readonly</c> structs, and
        /// <see cref="System.Nullable{T}"/> wrappers around immutable T are treated as freshening
        /// (read-only snapshot of the value). Mutable reference types and non-readonly structs are treated as
        /// mutation candidates.
        /// </summary>
        /// <remarks>
        /// Known limitation: <c>record class</c> instances are reported as Impure even when all properties are
        /// <c>init</c>-only, because they are reference types (<c>IsValueType == false</c>).
        /// </remarks>
        private static bool IsImmutableCapture(ITypeSymbol? type)
        {
            if (type is null)
            {
                return false;
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_Char:
                case SpecialType.System_String:
                    return true;
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                return true;
            }

            // Nullable<T> wraps a value type; treat as immutable when T itself is immutable (e.g. int? / Color?).
            if (type is INamedTypeSymbol named &&
                named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                named.TypeArguments.Length == 1)
            {
                return IsImmutableCapture(named.TypeArguments[0]);
            }

            // readonly struct / readonly record struct cannot be mutated through the captured copy.
            if (type.IsValueType && type.IsReadOnly)
            {
                return true;
            }

            return false;
        }

        private void ClassifyInvocation(IMethodSymbol target, Location? location)
        {
            if (KnownPurityDatabase.TryClassify(target, out var kind))
            {
                if (kind == KnownPurity.Impure)
                {
                    Add(ImpurityKind.KnownImpureCall, target.ToDisplayString(), location);
                }
                return;
            }

            if (target.IsAbstract || target.IsVirtual || target.IsOverride)
            {
                Add(ImpurityKind.VirtualCall, target.ToDisplayString(), location);
                return;
            }

            // Cycle detection runs regardless of remaining depth: if the symbol is on the active call stack the
            // recursive edge itself does not introduce side effects, so it must be skipped silently in both
            // self-recursion and mutual recursion (A → B → A) cases. The cycle's purity is decided by the other
            // operations inside each body.
            if (_visited is not null && _visited.Contains(target))
            {
                return;
            }

            // The switch below only returns for Pure/Impure; an Unknown callee result falls through to the
            // UnknownCall add at the bottom, so the caller always sees at least one reason when propagation
            // through the callee is inconclusive.
            if (_remainingDepth > 0 && _compilation is not null && _visited is not null)
            {
                _visited.Add(target);

                var calleeResult = PurityAnalyzer.AnalyzeCore(target, _compilation, _ct, _remainingDepth, _visited);
                switch (calleeResult.Purity)
                {
                    case Purity.Pure:
                        return;
                    case Purity.Impure:
                        Add(ImpurityKind.KnownImpureCall, target.ToDisplayString(), location);
                        return;
                }
            }

            Add(ImpurityKind.UnknownCall, target.ToDisplayString(), location);
        }

        private void InspectAssignment(IOperation target)
        {
            switch (target)
            {
                case IFieldReferenceOperation fieldRef:
                    Add(ImpurityKind.Assignment, fieldRef.Field.ToDisplayString(), target.Syntax?.GetLocation());
                    break;
                case IPropertyReferenceOperation propRef when !IsObjectInitializerReceiver(propRef.Instance):
                    Add(ImpurityKind.Assignment, propRef.Property.ToDisplayString(), target.Syntax?.GetLocation());
                    break;
                case IArrayElementReferenceOperation arrRef:
                    Add(ImpurityKind.Assignment, arrRef.ArrayReference.Type?.ToDisplayString() ?? "array", target.Syntax?.GetLocation());
                    break;
                case IParameterReferenceOperation paramRef when paramRef.Parameter.RefKind is RefKind.Ref or RefKind.Out:
                    Add(ImpurityKind.RefOutInParam, paramRef.Parameter.ToDisplayString(), target.Syntax?.GetLocation());
                    break;
                case ITupleOperation tuple:
                    foreach (var element in tuple.Elements)
                    {
                        InspectAssignment(element);
                    }
                    break;
            }
        }

        private bool IsCapturedFromOwner(ISymbol referenced) =>
            SymbolEqualityComparer.Default.Equals(referenced.ContainingSymbol, _owner);

        /// <summary>
        /// ImplicitReceiver appears in any of: object/collection initializers, with expressions, and implicit this.
        /// Currently all are treated as initializers and excluded (assignments to a freshly created object are not considered side effects).
        /// </summary>
        /// <remarks>
        /// Strict detection that captures property assignments inside with expressions as Impure is planned as a future extension.
        /// </remarks>
        private static bool IsObjectInitializerReceiver(IOperation? instance) =>
            instance is IInstanceReferenceOperation inst &&
            inst.ReferenceKind == InstanceReferenceKind.ImplicitReceiver;

        private void Add(ImpurityKind kind, string symbolDisplay, Location? location) =>
            _reasons.Add(new ImpurityReason(kind, symbolDisplay, location));
    }
}
