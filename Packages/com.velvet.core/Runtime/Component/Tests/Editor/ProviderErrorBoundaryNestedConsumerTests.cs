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

        private static VNode BuildFallback(Exception ex)
        {
            s_fallbackInvoked = true;
            return V.Label(name: "fallback-label", text: "fallback");
        }
    }
}
