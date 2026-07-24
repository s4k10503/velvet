using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Velvet.SourceGenerators.ReactiveScope
{
    /// <summary>
    /// Visits the IOperation tree and collects referenced symbols relevant to Render() (parameter / field / property / local).
    /// On entering a lambda / local function / foreach scope, pushes the scope-local set onto a stack so locals and parameters
    /// that belong to it are excluded from the final result.
    /// readonly fields and consts are treated as immutable and excluded.
    /// </summary>
    internal sealed class SymbolCollector : OperationWalker
    {
        private readonly HashSet<ISymbol> _dependencies = new(SymbolEqualityComparer.Default);
        private readonly Stack<HashSet<ISymbol>> _scopeLocals = new();
        private readonly bool _applyScopeLocalFilter;
        private readonly CancellationToken _ct;

        public SymbolCollector(CancellationToken ct, bool applyScopeLocalFilter = true)
        {
            _ct = ct;
            _applyScopeLocalFilter = applyScopeLocalFilter;
        }

        public ImmutableArray<ISymbol> GetDependencies() =>
            // To carry on the Incremental Generator's cache key, return in a deterministic order that does not depend on the HashSet's enumeration order.
            _dependencies.OrderBy(s => s.ToDisplayString(), System.StringComparer.Ordinal).ToImmutableArray();

        public override void Visit(IOperation? operation)
        {
            _ct.ThrowIfCancellationRequested();
            base.Visit(operation);
        }

        public override void VisitParameterReference(IParameterReferenceOperation operation)
        {
            if (!IsScopeLocal(operation.Parameter))
            {
                _dependencies.Add(operation.Parameter);
            }
            base.VisitParameterReference(operation);
        }

        public override void VisitLocalReference(ILocalReferenceOperation operation)
        {
            if (operation.Local.IsConst)
            {
                return;
            }
            if (!IsScopeLocal(operation.Local))
            {
                _dependencies.Add(operation.Local);
            }
            base.VisitLocalReference(operation);
        }

        public override void VisitFieldReference(IFieldReferenceOperation operation)
        {
            // const / readonly are never reassigned at runtime, so exclude them from deps.
            var field = operation.Field;
            if (!field.IsConst && !field.IsReadOnly)
            {
                _dependencies.Add(field);
            }
            base.VisitFieldReference(operation);
        }

        public override void VisitPropertyReference(IPropertyReferenceOperation operation)
        {
            _dependencies.Add(operation.Property);
            base.VisitPropertyReference(operation);
        }

        public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation) =>
            WithScopeLocals(operation.Symbol.Parameters, () => base.VisitAnonymousFunction(operation));

        public override void VisitLocalFunction(ILocalFunctionOperation operation) =>
            WithScopeLocals(operation.Symbol.Parameters, () => base.VisitLocalFunction(operation));

        public override void VisitForEachLoop(IForEachLoopOperation operation) =>
            WithScopeLocals(operation.Locals, () => base.VisitForEachLoop(operation));

        private void WithScopeLocals(IEnumerable<ISymbol> locals, System.Action visitChildren)
        {
            if (!_applyScopeLocalFilter)
            {
                visitChildren();
                return;
            }
            var set = new HashSet<ISymbol>(locals, SymbolEqualityComparer.Default);
            _scopeLocals.Push(set);
            try
            {
                visitChildren();
            }
            finally
            {
                _scopeLocals.Pop();
            }
        }

        private bool IsScopeLocal(ISymbol symbol)
        {
            foreach (var scope in _scopeLocals)
            {
                if (scope.Contains(symbol))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
