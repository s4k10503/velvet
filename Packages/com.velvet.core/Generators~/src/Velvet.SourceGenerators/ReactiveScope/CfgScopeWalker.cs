using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Velvet.SourceGenerators.ReactiveScope
{
    /// <summary>
    /// Walker that traverses the <see cref="ControlFlowGraph"/> of the Render() method body and collects dependency symbols
    /// from each basic block's operations plus branch conditions.
    /// </summary>
    internal static class CfgScopeWalker
    {
        public static ImmutableArray<ISymbol> CollectMethodBodyDependencies(
            IOperation methodBody,
            CancellationToken ct)
        {
            var collector = new SymbolCollector(ct);

            var cfg = TryCreateCfg(methodBody);
            if (cfg is null)
            {
                // When the CFG cannot be built, fall back to walking the raw IOperation tree.
                collector.Visit(methodBody);
                return collector.GetDependencies();
            }

            foreach (var block in cfg.Blocks)
            {
                ct.ThrowIfCancellationRequested();

                foreach (var op in block.Operations)
                {
                    collector.Visit(op);
                }

                // If/switch branch conditions must also enter deps, otherwise condition changes would not trigger recomputation.
                if (block.BranchValue is { } branchValue)
                {
                    collector.Visit(branchValue);
                }
            }

            return collector.GetDependencies();
        }

        private static ControlFlowGraph? TryCreateCfg(IOperation methodBody)
        {
            try
            {
                return methodBody switch
                {
                    IBlockOperation block => ControlFlowGraph.Create(block),
                    IMethodBodyOperation methodBodyOp => ControlFlowGraph.Create(methodBodyOp),
                    IConstructorBodyOperation ctorBodyOp => ControlFlowGraph.Create(ctorBodyOp),
                    _ => null,
                };
            }
            catch
            {
                // CFG construction can fail on incomplete ASTs or partial implementations in flux.
                // The caller handles this conservatively via the null fallback.
                return null;
            }
        }

        /// <summary>
        /// Shortcut for collecting dependencies of a single sub-expression. Walks the IOperation tree directly without building a CFG.
        /// </summary>
        public static ImmutableArray<ISymbol> CollectExpressionDependencies(
            IOperation expression,
            CancellationToken ct)
        {
            var collector = new SymbolCollector(ct);
            collector.Visit(expression);
            return collector.GetDependencies();
        }
    }
}
