using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <see cref="Hooks.UseContext"/> resolves a value across re-renders: both when a consumer
    /// re-renders in isolation on its own setState — without its enclosing Provider host re-rendering — and
    /// when the Provider's own value changes and must be tracked live to every consumer.
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
    /// <item>When a Provider value changes, a consumer that reads it via UseContext re-renders and observes
    /// the new value — including when the consumer sits behind a memoized subtree.</item>
    /// <item>A consumer that re-renders on its own setState AFTER such a change reads the value the
    /// Provider holds then, not the one it held when the consumer mounted.</item>
    /// <item>A plain (non-memoized) sibling re-renders because its parent re-rendered, since the
    /// props-equality bail is an opt-in gate a plain sibling does not have; a memoized sibling that does not
    /// read the context with unchanged props is neither re-rendered by the props bail nor spuriously
    /// force-rendered by context live tracking, establishing context-propagation precision.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Each
    /// isolated-re-render consumer exposes a local-state bumper that triggers an isolated re-render and
    /// records the value it last read from context. The live-tracking parents each expose a setter over the
    /// Provider value itself; five parent variants exist and each test mounts exactly one, so sharing the
    /// <c>Action&lt;string&gt;</c> setter across parents is safe. Static fields are reset in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class ContextReRenderTests
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
            ResetConsumer();
            ResetNonConsumer();
            ResetParent();
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

        #region Live tracking of a Provider value change

        private static readonly ComponentContext<string> TestCtx = ComponentContext<string>.Create("default");

        [Test]
        public void Given_ConsumerUsingContext_When_ProviderValueChanges_Then_ConsumerObservesNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ProviderParentRender, key: "parent"));
            Assume.That(s_consumerHasMounted, Is.True, "Precondition: the consumer mounted");
            Assume.That(s_consumerLastSeenValue, Is.EqualTo("initial"), "Precondition: the consumer first saw the initial value");

            // Act
            s_parentSetValue.Invoke("updated");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_consumerLastSeenValue, Is.EqualTo("updated"),
                "A Provider value change re-renders the consumer, which then observes the new value");
        }

        [Test]
        public void Given_ConsumerBehindMemoizedSubtree_When_ProviderValueChanges_Then_ConsumerObservesNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(MemoizedProviderParentRender, key: "parent"));
            Assume.That(s_consumerHasMounted, Is.True, "Precondition: the consumer mounted");
            Assume.That(s_consumerLastSeenValue, Is.EqualTo("initial"), "Precondition: the consumer first saw the initial value");

            // Act
            s_parentSetValue.Invoke("updated");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_consumerLastSeenValue, Is.EqualTo("updated"),
                "Live tracking reaches the consumer through a memoized subtree");
        }

        [Test]
        public void Given_PlainNonConsumerSibling_When_ProviderValueChanges_Then_SiblingReRendersWithParent()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(MixedChildrenParentRender, key: "parent"));
            Assume.That(s_consumerHasMounted, Is.True, "Precondition: the consumer mounted");
            Assume.That(s_nonConsumerHasMounted, Is.True, "Precondition: the sibling mounted");
            var nonConsumerRenderCountAtStart = s_nonConsumerRenderCount;

            // Act
            s_parentSetValue.Invoke("updated");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_nonConsumerRenderCount, Is.EqualTo(nonConsumerRenderCountAtStart + 1),
                "A plain sibling re-renders because its parent re-rendered, lacking the opt-in props bail");
        }

        // The consumer's own setState is taken AFTER the Provider's value has moved, so the spine rebuilds
        // the enclosing Providers once more with nothing having changed since. A rebuild that read a copy
        // of the enclosing values taken when the consumer mounted would answer with the mount-time value
        // here; reading the ancestor's committed tree answers with the value it holds now.
        // GREEN_ON_BASE(characterization): the base reads that committed tree already and answers
        // "changed". What the case pins is what a mount-time snapshot of the enclosing Providers would
        // cost, which the change keeps by going on reading the tree.
        [Test]
        public void Given_AProviderValueChangedAfterMount_When_TheConsumerReRendersOnItsOwnSetState_Then_ItReadsTheChangedValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SettableProviderSelfBumpingHostRender, key: "host"));
            var atMount = s_consumerLastSeen;
            s_parentSetValue.Invoke("changed");
            mounted.FlushStateForTest();
            var afterChange = s_consumerLastSeen;
            var rendersBeforeBump = s_consumerRenderCount;

            // Act
            s_consumerBump.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the two earlier readings are folded in because "changed" below is vacuous without
            // them: it is also what a Provider that never moved off that value would report. The render
            // count is folded in on the other side, because a consumer that did not re-render on its own
            // setState would still be holding the value its last render read.
            Assert.That(
                (atMount, afterChange, s_consumerLastSeen, s_consumerRenderCount > rendersBeforeBump),
                Is.EqualTo(("initial", "changed", "changed", true)));
        }

        [Component]
        private static VNode SettableProviderSelfBumpingHostRender()
        {
            var (value, setValue) = Hooks.UseState("initial");
            s_parentSetValue = setValue;
            return V.Provider(ThemeContext, value, new VNode[]
            {
                V.Component(SelfBumpingConsumerRender, key: "consumer"),
            });
        }

        [Test]
        public void Given_MemoizedNonConsumerSibling_When_ProviderValueChanges_Then_SiblingIsNotForceRendered()
        {
            // Arrange — a memoized sibling with unchanged props that does not read the context
            using var mounted = V.Mount(_root, V.Component(MemoizedNonConsumerParentRender, key: "parent"));
            Assume.That(s_consumerHasMounted, Is.True, "Precondition: the consumer mounted");
            Assume.That(s_nonConsumerHasMounted, Is.True, "Precondition: the sibling mounted");
            var nonConsumerRenderCountAtStart = s_nonConsumerRenderCount;

            // Act
            s_parentSetValue.Invoke("updated");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_nonConsumerRenderCount, Is.EqualTo(nonConsumerRenderCountAtStart),
                "A memoized sibling that does not read the context is not force-rendered on a Provider change");
        }

        #endregion

        #region Consumer component (subscribes to TestCtx via UseContext)

        private static bool s_consumerHasMounted;
        private static string s_consumerLastSeenValue;

        private static void ResetConsumer()
        {
            s_consumerHasMounted = false;
            s_consumerLastSeenValue = null;
        }

        [Component]
        private static VNode ConsumerRender()
        {
            s_consumerHasMounted = true;
            s_consumerLastSeenValue = Hooks.UseContext(TestCtx);
            return V.Label(text: s_consumerLastSeenValue);
        }

        #endregion

        #region NonConsumer component (sibling that does not call UseContext)

        private static bool s_nonConsumerHasMounted;
        private static int s_nonConsumerRenderCount;

        private static void ResetNonConsumer()
        {
            s_nonConsumerHasMounted = false;
            s_nonConsumerRenderCount = 0;
        }

        [Component]
        private static VNode NonConsumerRender()
        {
            s_nonConsumerHasMounted = true;
            s_nonConsumerRenderCount++;
            return V.Label(text: "static");
        }

        // Memoized non-consumer: with unchanged props it bails the parent-driven re-render, and context live tracking
        // must not force-render it because it does not call UseContext.
        [Component(Memoize = true)]
        private static VNode MemoizedNonConsumerRender()
        {
            s_nonConsumerHasMounted = true;
            s_nonConsumerRenderCount++;
            return V.Label(text: "static");
        }

        #endregion

        #region Parent components (Provider variants — each test mounts only one)

        // Each test mounts only one of the parents, so sharing s_parentSetValue is safe; the setter type is identical
        // for all parents (Action<string>).
        private static Action<string> s_parentSetValue;

        private static void ResetParent()
        {
            s_parentSetValue = null;
        }

        [Component]
        private static VNode ProviderParentRender()
        {
            var (value, setValue) = Hooks.UseState("initial");
            s_parentSetValue = setValue;
            return V.Provider(TestCtx, value, new VNode[]
            {
                V.Component(ConsumerRender, key: "consumer"),
            });
        }

        [Component]
        private static VNode MemoizedProviderParentRender()
        {
            var (value, setValue) = Hooks.UseState("initial");
            s_parentSetValue = setValue;
            return V.Provider(TestCtx, value, new VNode[]
            {
                V.Memoized(() => V.Component(ConsumerRender, key: "consumer"), Array.Empty<object>()),
            });
        }

        [Component]
        private static VNode MixedChildrenParentRender()
        {
            var (value, setValue) = Hooks.UseState("initial");
            s_parentSetValue = setValue;
            return V.Provider(TestCtx, value, new VNode[]
            {
                V.Component(ConsumerRender, key: "consumer"),
                V.Component(NonConsumerRender, key: "non-consumer"),
            });
        }

        [Component]
        private static VNode MemoizedNonConsumerParentRender()
        {
            var (value, setValue) = Hooks.UseState("initial");
            s_parentSetValue = setValue;
            return V.Provider(TestCtx, value, new VNode[]
            {
                V.Component(ConsumerRender, key: "consumer"),
                V.Component(MemoizedNonConsumerRender, key: "non-consumer"),
            });
        }

        #endregion
    }
}
