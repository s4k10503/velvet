namespace Velvet.Tests
{
    // Kept out of StyleRingClassTests so that fixture holds no changed line outside a case, which would leave
    // none of its cases reading the base tree.
    internal static class RingSpecReader
    {
        // Read by reflection so a tree whose spec has no such field reports it as false rather than failing to build.
        internal static bool TakesTextColour(RingSpec spec)
            => typeof(RingSpec).GetField("CurrentColor")?.GetValue(spec) is true;
    }
}
