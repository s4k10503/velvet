namespace Velvet.TestUtilities
{
    /// <summary>
    /// Shared EditMode fake-clock harness for <c>Hooks.UseFrame</c> specs: a manually-advanced
    /// millisecond counter paired with the single <c>[Component]</c> that counts each UseFrame
    /// invocation. <c>[Component]</c> methods must be static, so the counter is static too — callers
    /// reset it in their own <c>[SetUp]</c> to keep fixtures isolated from each other.
    /// </summary>
    public static class UseFrameFakeClockHost
    {
        public static int Calls;
        public static long Ms;

        public static void Reset()
        {
            Calls = 0;
            Ms = 1000;
        }

        // The per-panel time function reports SECONDS as a double (the ms-facing surface multiplies
        // by 1000); the fake clock is kept in milliseconds and converted here for readability.
        public static double ReadFakeClock() => Ms / 1000.0;

        [Component]
        public static VNode CountingHost()
        {
            Hooks.UseFrame(_ => Calls++);
            return V.Div(className: "w-[10px] h-[10px]");
        }
    }
}
