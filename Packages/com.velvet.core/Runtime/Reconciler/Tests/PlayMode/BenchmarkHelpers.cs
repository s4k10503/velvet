namespace Velvet.Tests.Performance
{
    internal static class BenchmarkHelpers
    {
        // Context instance shared by every expansion child so a Provider push/pop pair surrounds each
        // one. A single instance is enough: the benchmark measures the walk, not context resolution.
        private static readonly ComponentContext<string> s_expansionContext =
            ComponentContext<string>.Create("default");

        internal static VNode[] BuildLabelNodes(int count, string prefix = "item-")
        {
            var nodes = new VNode[count];
            for (int i = 0; i < count; i++)
            {
                nodes[i] = V.Label(text: $"{prefix}{i}");
            }
            return nodes;
        }

        // Mixed Label/Button leaves for benchmarks exercising VNodePool's per-widget-type recycle path
        // (both types are poolable primitives, so an unmount round-trips each through its own pool).
        internal static VNode[] BuildLabelAndButtonNodes(int countEach)
        {
            var nodes = new VNode[countEach * 2];
            for (int i = 0; i < countEach; i++)
            {
                nodes[i] = V.Label(text: $"label-{i}");
                nodes[countEach + i] = V.Button(text: $"button-{i}");
            }
            return nodes;
        }

        // Children whose every node type forces the inline-expansion walk: Provider (transparent, pushes
        // context), Fragment (transparent), Component (renders, emits no element of its own). Keep all
        // three — a flat host-leaf array routes to the Indexed/Keyed fast path and never enters the
        // expansion walk at all, so a Label-only benchmark measures none of this code.
        internal static VNode[] BuildExpansionNodes(int count, string prefix = "expand-")
        {
            var nodes = new VNode[count];
            for (int i = 0; i < count; i++)
            {
                // Constant value across every built array. A changed value makes the walk dispatch a
                // context-propagation traversal, and this fixture reconciles onto a bare VisualElement
                // with no root fiber to propagate from — the assertion that fires there is logged, and an
                // unhandled log fails the test outright.
                nodes[i] = V.Provider(s_expansionContext, "scoped", new VNode?[]
                {
                    V.Fragment(new VNode?[]
                    {
                        V.Component(RenderExpansionRow, new ExpansionRowProps(i, prefix), key: $"row-{i}"),
                    }),
                });
            }
            return nodes;
        }

        // A reference type, so the auto-memoization keyed on props sees a fresh instance per build the
        // way it does for a real component's record-class props; the record's own Equals is not what
        // that comparison reads. A record struct would also box on every V.Component call.
        internal sealed record ExpansionRowProps(int Index, string Prefix);

        // The Div's own child must stay a component, not a Label: committing this row re-enters the
        // reconciler for the Div's children, and only a child that needs expanding makes that re-entry
        // rent a SECOND walk while the outer one is still live. That nesting is the whole reason the walk
        // state is rented per entry instead of cached on the reconciler, so a Label-only Div would leave
        // the hazard unmeasured.
        [Component]
        private static VNode RenderExpansionRow(ExpansionRowProps props)
            => V.Div(children: new VNode?[] { V.Component(RenderExpansionLeaf, props, key: "leaf") });

        [Component]
        private static VNode RenderExpansionLeaf(ExpansionRowProps props)
            => V.Label(text: $"{props.Prefix}{props.Index}");
    }
}
