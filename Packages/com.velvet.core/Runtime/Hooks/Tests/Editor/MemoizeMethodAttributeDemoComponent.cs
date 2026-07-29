namespace Velvet.Tests
{
    /// <summary>
    /// Demo partial class used to E2E-verify automatic V.Memoized expansion driven by the [MemoizeMethod] attribute.
    /// MemoizeMethodGenerator fills in each partial method with V.Memoized(() => *_Impl(...), ...).
    /// </summary>
    internal sealed partial class MemoizeMethodAttributeDemoComponent
    {
        public int Arity1ImplCallCount { get; private set; }
        public int Arity3ImplCallCount { get; private set; }
        public int Arity6ImplCallCount { get; private set; }

        [MemoizeMethod]
        public partial VNode BuildArity1(string title);

        [MemoizeMethod]
        public partial VNode BuildArity3(string title, int count, bool visible);

        [MemoizeMethod]
        public partial VNode BuildArity6(int a, int b, int c, int d, int e, int f);

        public VNode BuildArity1_Impl(string title)
        {
            Arity1ImplCallCount++;
            return V.Label(text: title);
        }

        public VNode BuildArity3_Impl(string title, int count, bool visible)
        {
            Arity3ImplCallCount++;
            return V.Label(text: $"{title}:{count}:{visible}");
        }

        // Six rather than the eight [MemoizeMethod] supports: seven and eight are unreachable inside an
        // assembly that opts into the code-shape rules, which every assembly of this package does. The
        // generator's arity-8 emission stays pinned by its own snapshot test, and V.Memoized<T1..T8> by
        // VMemoTests.
        public VNode BuildArity6_Impl(int a, int b, int c, int d, int e, int f)
        {
            Arity6ImplCallCount++;
            return V.Label(text: $"{a}|{b}|{c}|{d}|{e}|{f}");
        }
    }
}
