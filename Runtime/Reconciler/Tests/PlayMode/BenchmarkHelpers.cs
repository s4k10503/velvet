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
    }
}
