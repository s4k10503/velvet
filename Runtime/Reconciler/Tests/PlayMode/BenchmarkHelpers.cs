namespace Velvet.Tests.Performance
{
    internal static class BenchmarkHelpers
    {
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
    }
}
