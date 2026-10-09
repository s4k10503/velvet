using System;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the render a resolved <c>Hooks.Use</c> resource gives its suspended component, under a
    /// Suspense boundary, settles on that component's lane queue.
    /// <list type="bullet">
    /// <item>An update queued on the component while it was suspended is satisfied by that render: the reveal
    /// commits it without rendering the component again, including after the component committed a transition's
    /// work earlier.</item>
    /// <item>A render that throws satisfies nothing, so the reveal renders the component again for the update.</item>
    /// <item>A lane that render asks for again survives it, so a deferred value the render left pending still
    /// commits.</item>
    /// <item>A component holding a transition's work keeps it queued through a render that suspends on a
    /// further read, so the transition's <c>isPending</c> stays up.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class AsyncResourceCompletionSettleTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_firstSource = new VelvetTaskCompletionSource<string>();
            s_secondSource = new VelvetTaskCompletionSource<string>();
            s_setTick = null;
            s_childRenderCount = 0;
            s_startTransition = default;
            s_forceIndicator = null;
            s_resourceKey = "first";
            s_keyedTask = VelvetTask.FromResult("a");
            s_throwOnRender = false;
        }

        [Test]
        public void Given_AMemoizedChildWithAnUpdateQueuedWhileSuspended_When_ItsResourceResolvesAndTheRevealFlushes_Then_ItRendersOnceAndShowsTheUpdate()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CountedHostRender, key: "host"));
            s_setTick(1);
            var rendersBefore = s_childRenderCount;

            // Act
            s_firstSource.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert — the label term pins that the one render left is the one the reveal shows
            Assert.That(
                (_root.Q<Label>(name: "content")?.text, s_childRenderCount - rendersBefore),
                Is.EqualTo(("ready-1", 1)),
                "The render the resolve gives the child satisfies the update queued while it was suspended");
        }

        // GREEN_ON_BASE(characterization): the base keeps every lane queued at a resolve, this one included.
        // The case pins that the settle added here keeps the lane this render asks for again.
        [Test]
        public void Given_AMemoizedChildDeferringAnUpdateQueuedWhileSuspended_When_ItsResourceResolvesAndTheLanesDrain_Then_TheDeferredValueCommits()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DeferringHostRender, key: "host"));
            s_setTick(1);

            // Act
            s_firstSource.TrySetResult("ready");
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>(name: "content")?.text, Is.EqualTo("ready-1-1"),
                "The Transition lane the resolve's render queues for the deferred value survives that render's settle");
        }

        // GREEN_ON_BASE(characterization): the base discharges no transition work at a resolve.
        // The case pins that the settle added here leaves a fiber holding such work alone.
        [Test]
        public void Given_TransitionWorkQueuedOnASuspendedChild_When_OneOfItsTwoReadsResolves_Then_IsPendingStaysUp()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionHostRender, key: "host"));
            s_startTransition.Invoke(() => s_setTick(1));

            // Act
            s_firstSource.TrySetResult("a");
            s_forceIndicator(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>(name: "pending")?.text, Is.EqualTo("pending"),
                "A render that suspends on the second read leaves the transition's work queued and its flag up");
        }

        [Test]
        public void Given_AMemoizedChildThatOnceHeldTransitionWork_When_ItSuspendsWithAnUpdateQueuedAndItsResourceResolves_Then_ItRendersOnce()
        {
            // Arrange — a committed transition leaves the child's enrolment list empty rather than absent
            using var mounted = V.Mount(_root, V.Component(KeyedHostRender, key: "host"));
            s_startTransition.Invoke(() => s_setTick(1));
            mounted.FlushStateForTest();
            s_resourceKey = "second";
            s_keyedTask = s_secondSource.Task;
            s_setTick(2);
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();
            var rendersBefore = s_childRenderCount;

            // Act
            s_secondSource.TrySetResult("b");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (_root.Q<Label>(name: "content")?.text, s_childRenderCount - rendersBefore),
                Is.EqualTo(("b-2", 1)),
                "Transition work the child already committed does not keep the resolve's render from settling it");
        }

        // GREEN_ON_BASE(characterization): the base never settles at a resolve, so a throwing render keeps its lanes.
        // The case pins that the settle added here is not reached from a render whose output was not retained.
        [Test]
        public void Given_AMemoizedChildWithAnUpdateQueuedWhileSuspended_When_TheRenderItsResolveGivesItThrows_Then_TheRevealRendersTheUpdate()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ThrowingHostRender, key: "host"));
            s_setTick(1);
            s_throwOnRender = true;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: resolved render failed"));

            // Act — the resolve's render throws; the reveal's does not
            s_firstSource.TrySetResult("ready");
            s_throwOnRender = false;
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>(name: "content")?.text, Is.EqualTo("ready-1"),
                "A render that threw satisfied nothing, so the reveal renders the child with the update it still owes");
        }

        #region Components

        private static VelvetTaskCompletionSource<string> s_firstSource;
        private static VelvetTaskCompletionSource<string> s_secondSource;
        private static Action<int> s_setTick;
        private static int s_childRenderCount;
        private static TransitionStarter s_startTransition;
        private static Action<int> s_forceIndicator;
        private static string s_resourceKey;
        private static VelvetTask<string> s_keyedTask;
        private static bool s_throwOnRender;

        private static VelvetTask<string> ReadFirst(CancellationToken _) => s_firstSource.Task;

        private static VelvetTask<string> ReadSecond(CancellationToken _) => s_secondSource.Task;

        // Memoized and props-less, so the reveal re-renders the child only where it is still dirty: a plain
        // component is re-rendered by its parent whatever its lanes hold.
        [Component(Memoize = true)]
        private static VNode CountedChildRender()
        {
            s_childRenderCount++;
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var value = Hooks.Use<string>(ReadFirst, "first");
            return V.Label(name: "content", text: $"{value}-{tick}");
        }

        [Component]
        private static VNode CountedHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Component(CountedChildRender, key: "child") });

        [Component(Memoize = true)]
        private static VNode DeferringChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var deferredTick = Hooks.UseDeferredValue(tick);
            var value = Hooks.Use<string>(ReadFirst, "first");
            return V.Label(name: "content", text: $"{value}-{tick}-{deferredTick}");
        }

        [Component]
        private static VNode DeferringHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Component(DeferringChildRender, key: "child") });

        [Component]
        private static VNode TwoReadChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var first = Hooks.Use<string>(ReadFirst, "first");
            var second = Hooks.Use<string>(ReadSecond, "second");
            return V.Label(name: "content", text: $"{first}{second}-{tick}");
        }

        // Declares the transition outside the Suspense, so rendering it re-walks nothing of the child.
        [Component]
        private static VNode PendingIndicatorRender()
        {
            var (isPending, startTransition) = Hooks.UseTransition();
            var (_, force) = Hooks.UseState(0);
            s_startTransition = startTransition;
            s_forceIndicator = force;
            return V.Label(name: "pending", text: isPending ? "pending" : "idle");
        }

        [Component]
        private static VNode TransitionHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(PendingIndicatorRender, key: "indicator"),
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(TwoReadChildRender, key: "child") }),
            });

        private static VelvetTask<string> ReadKeyed(CancellationToken _) => s_keyedTask;

        [Component(Memoize = true)]
        private static VNode KeyedChildRender()
        {
            s_childRenderCount++;
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var value = Hooks.Use<string>(ReadKeyed, s_resourceKey);
            return V.Label(name: "content", text: $"{value}-{tick}");
        }

        [Component]
        private static VNode KeyedHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(PendingIndicatorRender, key: "indicator"),
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(KeyedChildRender, key: "child") }),
            });

        [Component(Memoize = true)]
        private static VNode ThrowingChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var value = Hooks.Use<string>(ReadFirst, "first");
            if (s_throwOnRender) throw new InvalidOperationException("resolved render failed");
            return V.Label(name: "content", text: $"{value}-{tick}");
        }

        [Component]
        private static VNode ThrowingHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Component(ThrowingChildRender, key: "child") });

        #endregion
    }
}
