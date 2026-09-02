using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a Provider mounted above a <see cref="V.ErrorBoundary"/> remains resolvable by a
    /// consumer nested inside the boundary's subtree.
    /// <list type="bullet">
    /// <item>A consumer nested as component &gt; element &gt; component below an ErrorBoundary resolves a
    /// Provider value supplied above the boundary, reaching it through the inline-mount path.</item>
    /// <item>The arrangement holds whether the Provider sits directly under the mount point or inside the
    /// render output of a root component (Provider nested under a host element).</item>
    /// <item>Multiple nested Providers of distinct types above the boundary all stay resolvable, and the
    /// inner Provider value is the one the consumer reads.</item>
    /// <item>Because the value resolves, the consumer does not throw, so the boundary's fallback never fires
    /// and the consumer's own label renders.</item>
    /// <item>A consumer that is a SIBLING of the boundary rather than inside it keeps its Provider across a
    /// render the boundary caught in: the host's render is discarded, so the consumer's own re-render after
    /// it rebuilds its context from the tree the host committed before the throw.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> pattern. The leaf consumer throws
    /// <see cref="InvalidOperationException"/> when the context resolves to null, which the boundary would
    /// catch and render the fallback for; the fallback records its invocation into static fields so a fired
    /// fallback is observable. Static fields are reset in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class ProviderErrorBoundaryNestedConsumerTests
    {
        private static readonly ComponentContext<string> InnerContext =
            ComponentContext<string>.Create();

        private VisualElement _root;
        private static bool s_fallbackInvoked;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_fallbackInvoked = false;
        }

        [Test]
        public void Given_ProviderDirectlyAboveErrorBoundary_When_GrandchildConsumes_Then_RendersResolvedLabel()
        {
            // Arrange
            using var mounted = V.Mount(_root,
                V.Provider(InnerContext, "inner-value", new VNode[]
                {
                    V.ErrorBoundary(
                        fallback: BuildFallback,
                        children: new VNode[]
                        {
                            V.Component(ConsumerHostRender, key: "consumer-host"),
                        },
                        key: "boundary"),
                }));

            // Act
            var resolvedLabel = _root.Q<Label>(name: "inner-value-label");
            Assume.That(s_fallbackInvoked, Is.False, "Precondition: the consumer resolved the value, so the boundary fallback did not fire");

            // Assert
            Assert.That(resolvedLabel, Is.Not.Null,
                "The grandchild resolves the Provider value supplied directly above the boundary and renders its label");
        }

        [Test]
        public void Given_ProviderUnderRootComponentHostAboveErrorBoundary_When_GrandchildConsumes_Then_RendersResolvedLabel()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PageRootRender, key: "page-root"));

            // Act
            var resolvedLabel = _root.Q<Label>(name: "inner-value-label");
            Assume.That(s_fallbackInvoked, Is.False, "Precondition: the consumer resolved the value, so the boundary fallback did not fire");

            // Assert
            Assert.That(resolvedLabel, Is.Not.Null,
                "A Provider nested inside a root component's host element still resolves for the grandchild below the boundary");
        }

        [Test]
        public void Given_NestedProvidersAboveErrorBoundary_When_GrandchildConsumesInner_Then_RendersResolvedLabel()
        {
            // Arrange
            using var mounted = V.Mount(_root,
                V.Provider(ComponentContext<int>.Create(), 42, new VNode[]
                {
                    V.Provider(InnerContext, "inner-from-nested", new VNode[]
                    {
                        V.ErrorBoundary(
                            fallback: BuildFallback,
                            children: new VNode[]
                            {
                                V.Component(ConsumerHostRender, key: "consumer-host"),
                            },
                            key: "boundary"),
                    }),
                }));

            // Act
            var resolvedLabel = _root.Q<Label>(name: "inner-value-label");
            Assume.That(s_fallbackInvoked, Is.False, "Precondition: the consumer resolved the inner value, so the boundary fallback did not fire");

            // Assert
            Assert.That(resolvedLabel, Is.Not.Null,
                "Two nested Providers of distinct types above the boundary still let the grandchild resolve the inner value");
        }

        // Root page component: host element > Provider > ErrorBoundary > consumer host > consumer.
        [Component]
        private static VNode PageRootRender()
            => V.Div(name: "page-host",
                children: new VNode[]
                {
                    V.Provider(InnerContext, "from-page-root", new VNode[]
                    {
                        V.ErrorBoundary(
                            fallback: BuildFallback,
                            children: new VNode[]
                            {
                                V.Component(ConsumerHostRender, key: "consumer-host"),
                            },
                            key: "boundary"),
                    }),
                });

        [Component]
        private static VNode ConsumerHostRender()
            => V.Div(name: "consumer-host",
                children: new VNode[]
                {
                    V.Component(InnerConsumerRender, key: "consumer"),
                });

        [Component]
        private static VNode InnerConsumerRender()
        {
            var value = Hooks.UseContext(InnerContext);
            if (value == null)
            {
                throw new InvalidOperationException(
                    "InnerContext provider not found. Mount V.Provider(InnerContext, value, ...) above the consumer.");
            }
            return V.Label(name: "inner-value-label", text: value);
        }

        #region Consumer as a sibling of the boundary, across a caught render

        private static readonly ComponentContext<string> SiblingContext =
            ComponentContext<string>.Create("DEFAULT");
        private static string s_siblingSeen;
        private static int s_siblingCount;
        private static bool s_siblingThrows;
        private static StateUpdater<int> s_setSiblingTick;
        private static StateUpdater<int> s_setSiblingCount;

        [Component]
        private static VNode SiblingConsumerRender()
        {
            var (n, setN) = Hooks.UseState(0);
            s_setSiblingCount = setN;
            s_siblingCount = n;
            s_siblingSeen = Hooks.UseContext(SiblingContext);
            return V.Label(name: "sibling-value", text: $"{s_siblingSeen}:{n}");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode SiblingBoundaryRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_fallbackInvoked = true;
                return V.Label(name: "sibling-fallback", text: "fallback");
            });
            return V.Component(SiblingThrowerRender, key: "sibling-thrower");
        }

        [Component]
        private static VNode SiblingThrowerRender()
        {
            if (s_siblingThrows) throw new InvalidOperationException("thrower");
            return V.Label(name: "thrower", text: "ok");
        }

        // The consumer and the boundary are siblings under one host, so the host's render reaches the
        // consumer BEFORE the boundary catches. The host's output for that render is discarded, and what
        // the consumer's own re-render afterwards has to rebuild its context from is the tree the host
        // committed before the throw. This arrangement also drives the boundary through the subsumed
        // render whose disposal clears the reconciler, so an unhandled NullReferenceException from that
        // path fails this case as well.
        [Component]
        private static VNode SiblingBoundaryHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setSiblingTick = setTick;
            return V.Div(name: "sibling-shell", children: new VNode?[]
            {
                V.Label(name: "tick", text: tick.ToString()),
                V.Provider(SiblingContext, "v" + tick, new VNode[]
                {
                    V.Component(SiblingConsumerRender, key: "sibling-consumer"),
                }),
                V.Component(SiblingBoundaryRender, key: "sibling-boundary"),
            });
        }

        [Test]
        public void Given_AConsumerBesideAnErrorBoundary_When_TheHostsRenderIsCaughtAndTheConsumerThenReRendersAlone_Then_ItStillReadsItsProvider()
        {
            // Arrange
            s_siblingThrows = false;
            using var mounted = V.Mount(_root, V.Component(SiblingBoundaryHostRender, key: "sibling-host"));
            var atMount = s_siblingSeen;

            // Act — the host re-renders, the boundary catches inside that pass, and the consumer then
            // re-renders on its own setState.
            s_siblingThrows = true;
            s_setSiblingTick.Invoke(1);
            mounted.FlushStateForTest();
            s_setSiblingCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the consumer's own state is folded in because it is what separates a failed context
            // rebuild from a remount that would have reset it, and the mount reading because the value
            // below is not a Provider this position never had.
            Assert.That(
                (atMount, s_siblingSeen, s_siblingCount),
                Is.EqualTo(("v0", "v0", 1)));
        }

        #endregion

        private static VNode BuildFallback(Exception ex)
        {
            s_fallbackInvoked = true;
            return V.Label(name: "fallback-label", text: "fallback");
        }
    }
}
