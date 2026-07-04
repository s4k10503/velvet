using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the runtime inner-memoization slot API on functional components —
    /// <c>Hooks.TryGetMemoizedVNode</c> / <c>Hooks.StoreMemoizedVNode</c>, the same calls the inner
    /// auto-memoization weaver injects.
    /// <list type="bullet">
    /// <item>The first render of a component always misses the slot cache (the slot is freshly allocated), so
    /// the body takes the pure-build + store path.</item>
    /// <item>A discarded render-phase attempt that stores a transient memo does not poison the committed
    /// baseline: when a render-phase setState normalizes the value back to the committed one, that settled
    /// attempt is a cache hit and does not rebuild.</item>
    /// <item>Both slot APIs are render-scoped: calling either outside of Render raises an
    /// <see cref="InvalidOperationException"/>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Static fields expose the build counters and the setter captured during render; they are reset together
    /// in <see cref="SetUp"/>. The components reproduce the woven calls by hand because the IL-shape of the
    /// woven form is asserted separately in <c>CompilerILPostProcessorE2ETests</c>.
    /// </remarks>
    [TestFixture]
    internal sealed class MemoizeFunctionalE2ETests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_renderCount = 0;
            s_memoRebuildCount = 0;
            s_memoRenderCount = 0;
            s_memoSetPhase = null;
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
    }
}
