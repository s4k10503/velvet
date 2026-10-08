using System.Reflection;

namespace Velvet.Tests
{
    // FiberWorkLoop.TransitionStarvationThreshold, reflected rather than named so a fixture reading it compiles
    // against a tree that keeps the field private.
    internal static class StarvationThreshold
    {
        internal static int Read()
            => (int)typeof(FiberWorkLoop)
                .GetField("TransitionStarvationThreshold", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null);
    }
}
