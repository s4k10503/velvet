using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a Provider value change is tracked live and delivered to function-component consumers.
    /// <list type="bullet">
    /// <item>When a Provider value changes, a consumer that reads it via <c>UseContext</c> re-renders and observes the
    /// new value.</item>
    /// <item>Live tracking reaches the consumer even when the consumer sits behind a memoized subtree.</item>
    /// <item>A plain (non-memoized) sibling re-renders because its parent re-rendered, since the props-equality bail is
    /// an opt-in gate that a plain sibling does not have.</item>
    /// <item>A memoized sibling that does not read the context with unchanged props is neither re-rendered by the props
    /// bail nor spuriously force-rendered by context live tracking, establishing context-propagation precision.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> render-target pattern with static-field exposure; per-region state is
    /// reset together in <see cref="SetUp"/>. Four parent variants exist; each test mounts exactly one, so sharing the
    /// <c>Action&lt;string&gt;</c> setter across parents is safe.
    /// </remarks>
    [TestFixture]
    internal sealed class ContextLiveTrackingTests
    {
        private static readonly ComponentContext<string> TestCtx = ComponentContext<string>.Create("default");

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetConsumer();
            ResetNonConsumer();
            ResetParent();
        }

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
