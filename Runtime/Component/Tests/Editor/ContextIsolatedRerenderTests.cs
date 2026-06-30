using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <see cref="Hooks.UseContext"/> resolves a value when a consumer re-renders in
    /// isolation on its own setState — without its enclosing Provider host re-rendering.
    /// <list type="bullet">
    /// <item>Context is read live from the cursor, not from a value pinned on the consumer at mount, so an
    /// isolated re-render reconstructs the spine of enclosing Providers before the body reads it.</item>
    /// <item>Spine reconstruction descends through element subtrees and intermediate components, so a deeply
    /// nested consumer still reads its ancestor Provider value on its own re-render.</item>
    /// <item>With nested Providers of the same context, reconstruction pushes outer then inner, so the live
    /// top is the nearest Provider value and it masks the outer.</item>
    /// <item>Multiple stacked Providers of distinct types are each reconstructed onto the cursor, so a
    /// multi-context consumer reads every live value.</item>
    /// <item>With no enclosing Provider, the reconstructed cursor is empty and the read returns the context
    /// default rather than throwing or reading a stale value.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Each
    /// consumer exposes a local-state bumper that triggers an isolated re-render and records the value it
    /// last read from context. Static fields are reset in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class ContextIsolatedRerenderTests
    {
        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("default");
        private static readonly ComponentContext<int> CountContext = ComponentContext<int>.Create(0);

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_consumerLastSeen = null;
            s_consumerBump = default;
            s_consumerRenderCount = 0;
            s_secondCtxLastSeen = 0;
        }

        private static string s_consumerLastSeen;
        // Typed as Action<int> so the fixture is agnostic to the UseState setter return shape
        // (the setter is implicitly convertible to Action<int> regardless).
        private static Action<int> s_consumerBump;
        private static int s_consumerRenderCount;
        private static int s_secondCtxLastSeen;

        [Test]
        public void Given_DirectProvider_When_ConsumerReRendersOnOwnSetState_Then_StillReadsProvidedValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DirectProviderHostRender, key: "host"));
            Assume.That(s_consumerLastSeen, Is.EqualTo("provided"), "Precondition: the consumer reads the Provider value on mount");
            var renderCountAtStart = s_consumerRenderCount;

            // Act
            s_consumerBump.Invoke(1);
            mounted.FlushStateForTest();
            Assume.That(s_consumerRenderCount, Is.GreaterThan(renderCountAtStart),
                "Precondition: the consumer actually re-rendered on its own setState");

            // Assert
            Assert.That(s_consumerLastSeen, Is.EqualTo("provided"),
                "An isolated re-render reconstructs the enclosing Provider and reads the live value, not the default");
        }

        [Test]
        public void Given_DeepProvider_When_ConsumerReRendersOnOwnSetState_Then_ReadsAncestorValueAcrossElementScopes()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DeepProviderHostRender, key: "host"));
            Assume.That(s_consumerLastSeen, Is.EqualTo("deep"), "Precondition: the deep consumer reads the Provider value on mount");

            // Act
            s_consumerBump.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_consumerLastSeen, Is.EqualTo("deep"),
                "Reconstruction descends through element scopes and intermediate components to re-push the Provider");
        }

        [Test]
        public void Given_MaskingProviders_When_ConsumerReRendersOnOwnSetState_Then_ReadsNearestValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(MaskingProviderHostRender, key: "host"));
            Assume.That(s_consumerLastSeen, Is.EqualTo("inner"), "Precondition: the consumer reads the inner Provider value on mount");

            // Act
            s_consumerBump.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_consumerLastSeen, Is.EqualTo("inner"),
                "Reconstruction pushes outer then inner, so the nearest Provider value remains the live top");
        }

        [Test]
        public void Given_StackedDistinctContexts_When_ConsumerReRendersOnOwnSetState_Then_ReadsBothLiveValues()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(MultiContextHostRender, key: "host"));
            Assume.That(
                (s_consumerLastSeen, s_secondCtxLastSeen),
                Is.EqualTo(("theme-v", 7)),
                "Precondition: the consumer reads both Provider values on mount");

            // Act
            s_consumerBump.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (s_consumerLastSeen, s_secondCtxLastSeen),
                Is.EqualTo(("theme-v", 7)),
                "Both stacked Providers of distinct types are reconstructed onto the cursor");
        }

        [Test]
        public void Given_NoProvider_When_ConsumerReRendersOnOwnSetState_Then_ReadsDefaultValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NoProviderHostRender, key: "host"));
            Assume.That(s_consumerLastSeen, Is.EqualTo("default"), "Precondition: the consumer reads the default on mount");

            // Act
            s_consumerBump.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_consumerLastSeen, Is.EqualTo("default"),
                "With no enclosing Provider the reconstructed cursor is empty and the read returns the default");
        }

        #region Consumer components

        /// <summary>A consumer that reads ThemeContext and exposes its own local-state bumper.</summary>
        [Component]
        private static VNode SelfBumpingConsumerRender()
        {
            var (_, bump) = Hooks.UseState(0);
            s_consumerBump = bump;
            s_consumerRenderCount++;
            s_consumerLastSeen = Hooks.UseContext(ThemeContext);
            return V.Label(text: s_consumerLastSeen);
        }

        [Component]
        private static VNode MultiConsumerRender()
        {
            var (_, bump) = Hooks.UseState(0);
            s_consumerBump = bump;
            s_consumerLastSeen = Hooks.UseContext(ThemeContext);
            s_secondCtxLastSeen = Hooks.UseContext(CountContext);
            return V.Label(text: $"{s_consumerLastSeen}:{s_secondCtxLastSeen}");
        }

        #endregion

        #region Provider host trees

        [Component]
        private static VNode DirectProviderHostRender()
            => V.Provider(ThemeContext, "provided", new VNode[]
            {
                V.Component(SelfBumpingConsumerRender, key: "consumer"),
            });

        // Provider -> element -> intermediate component -> element -> consumer: the consumer sits several
        // fibers below the Provider, across element subtrees that each open a fresh reconcile scope.
        [Component]
        private static VNode DeepProviderHostRender()
            => V.Provider(ThemeContext, "deep", new VNode[]
            {
                V.Div(name: "wrapper", children: new VNode[]
                {
                    V.Component(IntermediateRender, key: "intermediate"),
                }),
            });

        [Component]
        private static VNode IntermediateRender()
            => V.Div(name: "intermediate-host", children: new VNode[]
            {
                V.Component(SelfBumpingConsumerRender, key: "consumer"),
            });

        // Outer Provider("outer") wraps inner Provider("inner") wraps the consumer.
        [Component]
        private static VNode MaskingProviderHostRender()
            => V.Provider(ThemeContext, "outer", new VNode[]
            {
                V.Provider(ThemeContext, "inner", new VNode[]
                {
                    V.Component(SelfBumpingConsumerRender, key: "consumer"),
                }),
            });

        // Two different context types provided above the consumer.
        [Component]
        private static VNode MultiContextHostRender()
            => V.Provider(ThemeContext, "theme-v", new VNode[]
            {
                V.Provider(CountContext, 7, new VNode[]
                {
                    V.Component(MultiConsumerRender, key: "multi"),
                }),
            });

        // No Provider above the consumer.
        [Component]
        private static VNode NoProviderHostRender()
            => V.Div(name: "host", children: new VNode[]
            {
                V.Component(SelfBumpingConsumerRender, key: "consumer"),
            });

        #endregion
    }
}
