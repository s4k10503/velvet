using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Velvet.SourceGenerators.ReactiveScope
{
    /// <summary>
    /// Resolver that expands local variables inside the Render() body into the "set of base symbols of their assignment sources".
    /// Replaces the <see cref="ILocalSymbol"/>s gathered by <see cref="SymbolCollector"/> with the symbols referenced on the RHS that
    /// initialize / assign each local, so a derived local is normalized back to the base inputs it depends on.
    /// </summary>
    internal sealed class LocalDataFlowResolver
    {
        private readonly Dictionary<ILocalSymbol, ImmutableArray<ISymbol>> _localToBase;

        private LocalDataFlowResolver(Dictionary<ILocalSymbol, ImmutableArray<ISymbol>> map)
        {
            _localToBase = map;
        }

        public static LocalDataFlowResolver Build(IOperation methodBody, CancellationToken ct)
        {
            var collector = new LocalAssignmentCollector(ct);
            collector.Visit(methodBody);
            return new LocalDataFlowResolver(collector.BuildMap());
        }

        /// <summary>
        /// Recursively expands any <see cref="ILocalSymbol"/>s in the input symbol set into base symbols.
        /// Cycles are cut by a visited set; the cycling local passes through unexpanded.
        /// </summary>
        public ImmutableArray<ISymbol> Resolve(ImmutableArray<ISymbol> symbols)
        {
            var result = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var visited = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            foreach (var sym in symbols)
            {
                Expand(sym, result, visited);
            }
            // Equals uses SequenceEqual (order-sensitive), so stabilize the order using the same key as SymbolCollector.GetDependencies.
            return result.OrderBy(s => s.ToDisplayString(), System.StringComparer.Ordinal).ToImmutableArray();
        }

        private void Expand(ISymbol symbol, HashSet<ISymbol> sink, HashSet<ILocalSymbol> visited)
        {
            if (symbol is ILocalSymbol local)
            {
                if (!visited.Add(local))
                {
                    return;
                }
                if (_localToBase.TryGetValue(local, out var bases))
                {
                    foreach (var b in bases)
                    {
                        Expand(b, sink, visited);
                    }
                }
                else
                {
                    sink.Add(local);
                }
                return;
            }
            sink.Add(symbol);
        }

        private sealed class LocalAssignmentCollector : OperationWalker
        {
            private readonly Dictionary<ILocalSymbol, HashSet<ISymbol>> _map = new(SymbolEqualityComparer.Default);
            private readonly CancellationToken _ct;

            public LocalAssignmentCollector(CancellationToken ct)
            {
                _ct = ct;
            }

            public Dictionary<ILocalSymbol, ImmutableArray<ISymbol>> BuildMap()
            {
                var result = new Dictionary<ILocalSymbol, ImmutableArray<ISymbol>>(SymbolEqualityComparer.Default);
                foreach (var kv in _map)
                {
                    result[kv.Key] = kv.Value.ToImmutableArray();
                }
                return result;
            }

            public override void Visit(IOperation? operation)
            {
                _ct.ThrowIfCancellationRequested();
                base.Visit(operation);
            }

            public override void VisitVariableDeclarator(IVariableDeclaratorOperation operation)
            {
                var initializer = operation.Initializer?.Value ?? operation.GetVariableInitializer()?.Value;
                if (initializer is not null)
                {
                    RecordAssignment(operation.Symbol, initializer);
                }
                base.VisitVariableDeclarator(operation);
            }

            public override void VisitSimpleAssignment(ISimpleAssignmentOperation operation)
            {
                if (operation.Target is ILocalReferenceOperation localRef)
                {
                    RecordAssignment(localRef.Local, operation.Value);
                }
                base.VisitSimpleAssignment(operation);
            }

            public override void VisitCompoundAssignment(ICompoundAssignmentOperation operation)
            {
                if (operation.Target is ILocalReferenceOperation localRef)
                {
                    Add(localRef.Local, localRef.Local);
                    RecordAssignment(localRef.Local, operation.Value);
                }
                base.VisitCompoundAssignment(operation);
            }

            public override void VisitForEachLoop(IForEachLoopOperation operation)
            {
                foreach (var local in operation.Locals)
                {
                    RecordAssignment(local, operation.Collection);
                }
                base.VisitForEachLoop(operation);
            }

            private void RecordAssignment(ILocalSymbol target, IOperation value)
            {
                // Reuse SymbolCollector with scope-local filtering disabled. Include assignment sources via lambdas in the base expansion as well.
                var collector = new SymbolCollector(_ct, applyScopeLocalFilter: false);
                collector.Visit(value);
                foreach (var sym in collector.GetDependencies())
                {
                    Add(target, sym);
                }
            }

            private void Add(ILocalSymbol target, ISymbol source)
            {
                if (!_map.TryGetValue(target, out var set))
                {
                    set = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                    _map[target] = set;
                }
                set.Add(source);
            }
        }
    }

    internal static class VariableDeclaratorExtensions
    {
        public static IVariableInitializerOperation? GetVariableInitializer(this IVariableDeclaratorOperation declarator)
        {
            if (declarator.Initializer is not null)
            {
                return declarator.Initializer;
            }
            if (declarator.Parent is IVariableDeclarationOperation decl)
            {
                return decl.Initializer;
            }
            return null;
        }
    }
}
