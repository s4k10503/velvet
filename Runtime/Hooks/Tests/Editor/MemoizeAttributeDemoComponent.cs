namespace Velvet.Tests
{
    /// <summary>
    /// Demo partial class used to E2E-verify automatic V.Memoized expansion driven by the [Memoize] attribute.
    /// MemoizeMethodGenerator fills in each partial method with V.Memoized(() => *_Impl(...), ...).
    /// </summary>
    internal sealed partial class MemoizeAttributeDemoComponent
    {
        public int Arity1ImplCallCount { get; private set; }
        public int Arity3ImplCallCount { get; private set; }
        public int Arity8ImplCallCount { get; private set; }

        [Memoize]
        public partial VNode BuildArity1(string title);

        [Memoize]
        public partial VNode BuildArity3(string title, int count, bool visible);

        [Memoize]
        public partial VNode BuildArity8(int a, int b, int c, int d, int e, int f, int g, int h);

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

        public VNode BuildArity8_Impl(int a, int b, int c, int d, int e, int f, int g, int h)
        {
            Arity8ImplCallCount++;
            return V.Label(text: $"{a}|{b}|{c}|{d}|{e}|{f}|{g}|{h}");
        }
    }
}
