using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the default-on inner auto-memoization weaver's behavior across the shapes it must handle: the
    /// raw runtime slot API it compiles down to (<c>Hooks.TryGetMemoizedVNode</c> / <c>Hooks.StoreMemoizedVNode</c>),
    /// how it keys its dependency array on a component's captured inputs (props and <c>UseContext</c> values)
    /// under Velvet's <c>ObjectIs</c> equality, and the two-element <c>UseState</c> destructuring binding as
    /// an input alongside the discard form.
    /// <list type="bullet">
    /// <item>The first render of a component always misses the slot cache (freshly allocated), so the body takes
    /// the pure-build + store path.</item>
    /// <item>A discarded render-phase attempt that stores a transient memo does not poison the committed
    /// baseline: when a render-phase setState normalizes the value back to the committed one, that settled
    /// attempt is a cache hit and does not rebuild. Both slot APIs are render-scoped: calling either outside of
    /// Render raises an <see cref="InvalidOperationException"/>.</item>
    /// <item>A captured <c>record class</c> prop is held by instance: re-rendering with the same instance is a
    /// cache hit; a changed prop value or a fresh-but-equal instance is a miss, because <c>ObjectIs</c> compares
    /// such an input by instance, not by content. A captured context value is keyed the same way.</item>
    /// <item>The idiomatic two-element <c>var (value, setValue) = Hooks.UseState(...)</c> binding is captured the
    /// same way as the value-only discard form: Item1 (the value) is a dependency and Item2 (the
    /// reference-stable setter) is not, so the component is woven and memoizes its VNode build.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The components either reproduce the woven calls by hand or are woven at build time; either way the
    /// IL-shape itself is asserted separately in <c>CompilerILPostProcessorE2ETests</c>. A re-render of the
    /// parent / Provider host reconciles the child fiber, which reuses its committed VNode while every captured
    /// input stays <c>ObjectIs</c>-equal. The rebuild counters sit in the build region, so they advance only on a
    /// miss.
    /// </remarks>
    [TestFixture]
    internal sealed class MemoizeWovenBehaviorE2ETests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_renderCount = 0;
            s_memoRebuildCount = 0;
            s_memoRenderCount = 0;
            s_memoSetPhase = null;
            s_propsChildRebuildCount = 0;
            s_propsParentSetTick = null;
            s_childProps = null;
            s_providerBumpTick = null;
            s_themeForTick = null;
            s_contextRebuildCount = 0;
            s_tupleChildRebuildCount = 0;
            s_tupleParentSetTick = null;
            s_childSetSuffix = null;
            s_stableProps = null;
        }

        private static int s_renderCount;
        private static int s_memoRebuildCount;
        private static int s_memoRenderCount;
        private static Action<int> s_memoSetPhase;

        [Component]
        public static VNode DirectMemoRender()
        {
            // Reproduce the calls the inner auto-memoization weaver injects: run hooks every render, build the
            // deps array, check for a hit, and on a miss build the VNode purely and write it back.
            var (value, _) = Hooks.UseState(0);
            var deps = new object[] { value };
            if (Hooks.TryGetMemoizedVNode(deps, out var slotIdx, out var cached))
            {
                return cached;
            }
            s_renderCount++;
            var result = V.Label(text: value.ToString());
            Hooks.StoreMemoizedVNode(slotIdx, deps, result);
            return result;
        }

        // A render-phase setState normalizes an odd phase to the next even phase in one re-run, so the memo dep
        // swings to "transient" on the discarded attempt and back to the committed "settled" on the settled one.
        [Component]
        public static VNode RenderPhaseOscillationMemoRender()
        {
            s_memoRenderCount++;
            var (phase, setPhase) = Hooks.UseState(0);
            s_memoSetPhase = setPhase;
            if (phase % 2 == 1)
            {
                setPhase.Invoke(phase + 1);
            }
            var dep = phase % 2 == 1 ? "transient" : "settled";
            var deps = new object[] { dep };
            if (Hooks.TryGetMemoizedVNode(deps, out var slotIdx, out var cached))
            {
                return cached;
            }
            s_memoRebuildCount++;
            var result = V.Label(text: dep);
            Hooks.StoreMemoizedVNode(slotIdx, deps, result);
            return result;
        }

        [Test]
        public void Given_FreshFiber_When_FirstRender_Then_MissesAndBuildsOnce()
        {
            // Act — the slot cache is freshly allocated on mount, so the first render always misses.
            using var mounted = V.Mount(_root, V.Component(DirectMemoRender, key: "direct"));

            // Assert
            Assert.That(s_renderCount, Is.EqualTo(1),
                "The first render exercises the cache-miss path (pure build + store)");
        }

        [Test]
        public void Given_RenderPhaseOscillation_When_ValueSettlesToCommitted_Then_DoesNotRebuildMemo()
        {
            // Arrange — mount builds once (miss) for the committed dep "settled".
            using var mounted = V.Mount(_root, V.Component(RenderPhaseOscillationMemoRender, key: "osc"));
            Assume.That(s_memoRebuildCount, Is.EqualTo(1), "Precondition: mount built once for the committed dep");

            // Act — set an odd phase; the body re-runs once for the discarded "transient" attempt and once more
            // when the render-phase setState settles the phase back to even ("settled").
            s_memoSetPhase.Invoke(1);
            mounted.FlushStateForTest();
            Assume.That(s_memoRenderCount, Is.EqualTo(3), "Precondition: 1 mount + 2 render-phase attempts (phase 1 -> 2)");

            // Assert — the discarded attempt's store does not poison the committed baseline, so the settled
            // attempt is a hit. Only the mount build and the discarded attempt's build count.
            Assert.That(s_memoRebuildCount, Is.EqualTo(2),
                "The settled attempt reuses the committed memo instead of rebuilding a third time");
        }

        [Test]
        public void Given_OutsideOfRender_When_StoreMemoizedVNodeCalled_Then_Throws()
        {
            // Act + Assert
            Assert.Throws<InvalidOperationException>(() =>
                Hooks.StoreMemoizedVNode(0, Array.Empty<object>(), null));
        }

        [Test]
        public void Given_OutsideOfRender_When_TryGetMemoizedVNodeCalled_Then_Throws()
        {
            // Act + Assert
            Assert.Throws<InvalidOperationException>(() =>
                Hooks.TryGetMemoizedVNode(Array.Empty<object>(), out _, out _));
        }

        private static int s_propsChildRebuildCount;
        private static Action<int> s_propsParentSetTick;
        private static Func<int, ChildProps> s_childProps;

        private sealed record ChildProps(string Label);

        // Props-driven woven child. UseState supplies the hook boundary the weaver keys on; the prop record is
        // prepended to the deps array. The rebuild counter sits in the build region, so it advances only on a miss.
        [Component]
        private static VNode WovenPropsChild(ChildProps p)
        {
            var (suffix, _) = Hooks.UseState("");
            s_propsChildRebuildCount++;
            return V.Label(name: "child", text: p.Label + suffix);
        }

        [Component]
        private static VNode PropsParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_propsParentSetTick = setTick;
            return V.Component(WovenPropsChild, s_childProps(tick), key: "child");
        }

        [Test]
        public void Given_SamePropInstance_When_ParentReRenders_Then_ChildDoesNotRebuild()
        {
            // Arrange — the parent hands the child the SAME record instance on every render.
            var stable = new ChildProps("a");
            s_childProps = _ => stable;
            using var mounted = V.Mount(_root, V.Component(PropsParent, key: "parent"));
            Assume.That(s_propsChildRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the child");

            // Act
            s_propsParentSetTick(1);
            mounted.FlushStateForTest();

            // Assert — same instance + unchanged hook value is ObjectIs-equal -> cache hit.
            Assert.That(s_propsChildRebuildCount, Is.EqualTo(1),
                "A reference-identical prop and unchanged hook value is a cache hit, so the body does not rebuild");
        }

        [Test]
        public void Given_ChangedPropValue_When_ParentReRenders_Then_ChildRebuilds()
        {
            // Arrange — the child's prop value tracks the parent tick, so each re-render hands a different Label.
            s_childProps = tick => new ChildProps($"a{tick}");
            using var mounted = V.Mount(_root, V.Component(PropsParent, key: "parent"));
            Assume.That(s_propsChildRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the child");

            // Act
            s_propsParentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_propsChildRebuildCount, Is.EqualTo(2),
                "A changed prop value is not ObjectIs-equal -> cache miss, so the body rebuilds");
        }

        [Test]
        public void Given_ChangedPropValue_When_ParentReRenders_Then_ChildDisplaysNewLabel()
        {
            // Arrange
            s_childProps = tick => new ChildProps($"a{tick}");
            using var mounted = V.Mount(_root, V.Component(PropsParent, key: "parent"));
            Assume.That(_root.Q<Label>(name: "child")?.text, Is.EqualTo("a0"), "Precondition: initial label is a0");

            // Act
            s_propsParentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>(name: "child")?.text, Is.EqualTo("a1"),
                "The rebuilt child renders the new prop value");
        }

        // GREEN_ON_BASE(characterization): this branch changes no production code — it corrects the
        // arrangement comment, which stated the keying rule in a form that is false for a string input —
        // so the case is green on both sides. What shows it can fail is the reference fall-through of
        // ObjectIs.AreEqualObjects perturbed to return true, measured: the fresh instance is then a memo
        // hit and the child does not rebuild.
        [Test]
        public void Given_FreshPropInstanceWithEqualValues_When_ParentReRenders_Then_ChildRebuilds()
        {
            // Arrange — a fresh record class instance with identical members on every render. The inner memo
            // keys such an input on instance, so a fresh-but-equal one is a miss (sound: the framework
            // reconciles the child on each parent re-render, so a miss costs a rebuild and never a stale tree).
            s_childProps = _ => new ChildProps("a");
            using var mounted = V.Mount(_root, V.Component(PropsParent, key: "parent"));
            Assume.That(s_propsChildRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the child");

            // Act
            s_propsParentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_propsChildRebuildCount, Is.EqualTo(2),
                "A distinct record class instance is not the same instance -> ObjectIs miss -> rebuild");
        }

        private static readonly ComponentContext<string> ThemeContext =
            ComponentContext<string>.Create("default");

        private static Action<int> s_providerBumpTick;
        private static Func<int, string> s_themeForTick;
        private static int s_contextRebuildCount;

        // Context-driven woven consumer. UseContext is the hook boundary AND the captured input: the live value
        // is keyed into the deps array. The rebuild counter advances only on a miss.
        [Component]
        private static VNode WovenContextConsumer()
        {
            var theme = Hooks.UseContext(ThemeContext);
            s_contextRebuildCount++;
            return V.Label(name: "ctx", text: theme);
        }

        // The host owns an unrelated tick. Bumping it forces a host re-render (reconciling the Provider and the
        // consumer) while s_themeForTick controls whether the provided value actually changes.
        [Component]
        private static VNode ThemeProviderHost()
        {
            var (tick, bump) = Hooks.UseState(0);
            s_providerBumpTick = bump;
            return V.Provider(ThemeContext, s_themeForTick(tick), new VNode[]
            {
                V.Component(WovenContextConsumer, key: "consumer"),
            });
        }

        [Test]
        public void Given_UnchangedContextValue_When_HostReRenders_Then_ConsumerDoesNotRebuild()
        {
            // Arrange — the provided value is the same string regardless of the host tick.
            s_themeForTick = _ => "light";
            using var mounted = V.Mount(_root, V.Component(ThemeProviderHost, key: "host"));
            Assume.That(s_contextRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the consumer");

            // Act — the host re-renders on the bump, but the captured context dep is unchanged.
            s_providerBumpTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_contextRebuildCount, Is.EqualTo(1),
                "An unchanged context value is ObjectIs-equal -> cache hit, so the consumer does not rebuild");
        }

        [Test]
        public void Given_ChangedContextValue_When_HostReRenders_Then_ConsumerRebuilds()
        {
            // Arrange — the provided value tracks the host tick, so the bump hands a different context value.
            s_themeForTick = tick => tick == 0 ? "light" : "dark";
            using var mounted = V.Mount(_root, V.Component(ThemeProviderHost, key: "host"));
            Assume.That(s_contextRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the consumer");

            // Act
            s_providerBumpTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_contextRebuildCount, Is.EqualTo(2),
                "A changed context value is not ObjectIs-equal -> cache miss, so the consumer rebuilds");
        }

        [Test]
        public void Given_ChangedContextValue_When_HostReRenders_Then_ConsumerDisplaysNewValue()
        {
            // Arrange
            s_themeForTick = tick => tick == 0 ? "light" : "dark";
            using var mounted = V.Mount(_root, V.Component(ThemeProviderHost, key: "host"));
            Assume.That(_root.Q<Label>(name: "ctx")?.text, Is.EqualTo("light"), "Precondition: initial value is light");

            // Act
            s_providerBumpTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>(name: "ctx")?.text, Is.EqualTo("dark"),
                "The rebuilt consumer renders the new context value");
        }

        private static int s_tupleChildRebuildCount;
        private static Action<int> s_tupleParentSetTick;
        private static Action<string> s_childSetSuffix;
        private static ChildProps s_stableProps;

        // TWO-ELEMENT binding (the idiomatic form). The setter is captured into a static so it is genuinely
        // USED — that makes Roslyn emit the full `call -> dup -> ldfld Item1 -> stloc -> ldfld Item2 -> stXxx`
        // deconstruction (not the elided unused-setter form). Item1 (suffix) is the sound dep; the setter is
        // reference-stable. The rebuild counter sits in the build region, advancing only on a memo miss.
        [Component]
        private static VNode WovenTupleChild(ChildProps p)
        {
            var (suffix, setSuffix) = Hooks.UseState("");
            s_childSetSuffix = setSuffix;
            s_tupleChildRebuildCount++;
            return V.Label(name: "child", text: p.Label + suffix);
        }

        [Component]
        private static VNode TupleParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_tupleParentSetTick = setTick;
            return V.Component(WovenTupleChild, s_stableProps, key: "child");
        }

        [Test]
        public void Given_ATwoElementUseStateBinding_When_ParentReRendersWithSameProps_Then_TheChildIsMemoizedAndDoesNotRebuild()
        {
            // Arrange — the parent hands the child the SAME record instance on every render, so the only thing that
            // decides hit vs miss is whether the two-element-binding child is actually woven.
            s_stableProps = new ChildProps("a");
            using var mounted = V.Mount(_root, V.Component(TupleParent, key: "parent"));
            Assume.That(s_tupleChildRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the child");

            // Act — re-render the parent without changing the child's prop or hook value.
            s_tupleParentSetTick(1);
            mounted.FlushStateForTest();

            // Assert — a two-element `var (x, setX) = UseState(...)` component must be woven (auto-memoized) just like
            // the discard form, so the unchanged-input re-render is a cache hit and the body does not rebuild.
            Assert.That(s_tupleChildRebuildCount, Is.EqualTo(1),
                "Two-element UseState binding must be auto-memoized: unchanged inputs -> cache hit -> no rebuild");
        }
    }
}
