using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class SuspenseContainerIsolationTests
    {
        private static VelvetTaskCompletionSource<string> s_resource;
        private static StateUpdater<int> s_setCount;
        private static ComponentFiber s_counterFiber;
        private static bool s_abortThrows;
        private static int s_abortCounterCleanups;
        private static int s_abortCounterSubscriptions;
        private MountedTree _mounted;
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            s_resource = new VelvetTaskCompletionSource<string>();
            s_setCount = default;
            s_counterFiber = null;
            s_abortThrows = false;
            s_abortCounterCleanups = 0;
            s_abortCounterSubscriptions = 0;
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        [Component(Compiler = false)]
        private static VNode Counter()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            s_counterFiber = FiberAmbientStack.Current;
            return V.Label(text: "primary:" + count);
        }

        [Component(Compiler = false)]
        private static VNode Reader()
            => V.Label(text: Hooks.Use(_ => s_resource.Task));

        [Component(Compiler = false)]
        private static VNode Host()
            => V.Div(children: new VNode[]
            {
                V.Div(name: "waiting", children: new VNode[]
                {
                    V.Suspense(V.Label(text: "loading"), new VNode[]
                    {
                        V.Component(Counter), V.Component(Reader),
                    }),
                }),
                V.Div(name: "ready", children: new VNode[]
                {
                    V.Suspense(V.Label(text: "unused"), new VNode[] { V.Label(text: "ready") }),
                }),
            });

        private string Texts(string name)
            => string.Join("|", _root.Q<VisualElement>(name).Query<Label>().ToList().Select(label => label.text));

        [Test]
        public void Given_ASuspendedAndAReadyBoundaryInSeparateContainers_When_TheOffscreenCounterUpdates_Then_TheFallbackStaysVisible()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(Host));
            var before = Texts("waiting");

            // Act
            s_setCount.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((before, Texts("waiting"), Texts("ready")),
                Is.EqualTo(("loading", "loading", "ready")));
        }

        [Test]
        public void Given_AnOffscreenUpdateBesideAReadyBoundary_When_TheResourceResolves_Then_OnlyTheWaitingContainerRevealsItsUpdatedPrimary()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(Host));
            s_setCount.Invoke(1);
            _mounted.FlushStateForTest();
            var before = Texts("waiting");

            // Act
            s_resource.TrySetResult("loaded");
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((before, Texts("waiting"), Texts("ready")),
                Is.EqualTo(("loading", "primary:1|loaded", "ready")));
        }

        private static readonly ComponentContext<string> Tint = ComponentContext<string>.Create("default");
        private static StateUpdater<int> s_setFallbackCount;

        [Component(Compiler = false)]
        private static VNode FallbackCounter()
        {
            var tint = Hooks.UseContext(Tint);
            var (count, setCount) = Hooks.UseState(0);
            s_setFallbackCount = setCount;
            return V.Label(text: tint + ":" + count);
        }

        [Component(Compiler = false)]
        private static VNode ContextHost()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    V.Provider(Tint, "provided", new VNode[]
                    {
                        V.Div(children: new VNode[] { V.Component(FallbackCounter) }),
                    }),
                    new VNode[] { V.Component(Reader) }),
            });

        // GREEN_ON_BASE(characterization): container isolation must preserve the existing fallback context.
        [Test]
        public void Given_AFallbackConsumerInsideANestedHost_When_ItUpdates_Then_ItsProviderIsRestored()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(ContextHost));
            var before = _root.Q<Label>()?.text;

            // Act
            s_setFallbackCount.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((before, _root.Q<Label>()?.text), Is.EqualTo(("provided:0", "provided:1")));
        }

        private static int RootlessFallbackCount(Reconciler reconciler)
        {
            var entries = typeof(ReconcilerContext)
                .GetField("_rootlessSuspenseFallbackKeys", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(reconciler.Context);
            return (int)entries.GetType().GetProperty("Count").GetValue(entries);
        }

        [Test]
        public void Given_ARootlessSuspenseShowingFallback_When_TheReconcilerIsDisposed_Then_ItsContainerStateIsReleased()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Suspense(V.Label(text: "loading"), new VNode[] { V.Component(Reader) }),
            });
            var before = RootlessFallbackCount(scope.Reconciler);

            // Act
            scope.Reconciler.Dispose();

            // Assert
            Assert.That((before, RootlessFallbackCount(scope.Reconciler)), Is.EqualTo((1, 0)));
        }

        private static VisualElement s_sharedTarget;
        private static StateUpdater<bool> s_setShow;

        private static VNode WaitingBoundary()
            => V.Suspense(V.Label(text: "loading"), new VNode[] { V.Component(Counter), V.Component(Reader) });

        [Component(Compiler = false)]
        private static VNode PortalHost()
            => V.Div(children: new VNode[]
            {
                V.Portal(s_sharedTarget, new VNode[] { WaitingBoundary() }),
                V.Portal(s_sharedTarget, new VNode[]
                {
                    V.Suspense(V.Label(text: "unused"), new VNode[] { V.Label(text: "ready") }),
                }),
            });

        [Test]
        public void Given_TwoPortalsSharingATarget_When_AnOffscreenCounterUpdates_Then_TheOtherPortalDoesNotClearItsFallback()
        {
            // Arrange
            s_sharedTarget = new VisualElement();
            _mounted = V.Mount(_root, V.Component(PortalHost));
            var before = string.Join("|", s_sharedTarget.Query<Label>().ToList().Select(label => label.text));

            // Act
            s_setCount.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((before, string.Join("|", s_sharedTarget.Query<Label>().ToList().Select(label => label.text))),
                Is.EqualTo(("loading|ready", "loading|ready")));
        }

        [Component(Compiler = false)]
        private static VNode ConditionalHost()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.Div(children: new VNode[] { show ? WaitingBoundary() : V.Label(text: "empty") });
        }

        [Test]
        public void Given_ASuspenseRemovedFromASurvivingContainer_When_TheHostUpdates_Then_ItsFallbackStateRetires()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(ConditionalHost));
            var context = _mounted.Root.Reconciler.Context;
            var before = context.AnyBoundaryShowingFallback;

            // Act
            s_setShow.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((before, _root.Q<Label>()?.text, context.AnyBoundaryShowingFallback),
                Is.EqualTo((true, "empty", false)));
        }

        private static StateUpdater<int> s_setAbortTick;

        [Component(Compiler = false)]
        private static VNode VersionedReader(int version)
            => V.Label(text: "loaded:" + version + ":" + Hooks.Use(_ => s_resource.Task, resourceKey: version == 0 ? 0 : 1));

        [Component(Compiler = false)]
        private static VNode AbortBomb(int tick)
        {
            if (s_abortThrows) throw new InvalidOperationException("Aborted Suspense visibility test");
            return V.Label(text: "okay:" + tick);
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode AbortGuard(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "error"));
            return V.Component(AbortBomb, tick);
        }

        [Component(Compiler = false)]
        private static VNode EarlyAbortCounter()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            Hooks.UseEffect(() =>
            {
                s_abortCounterSubscriptions++;
                return () => s_abortCounterCleanups++;
            }, Array.Empty<object>());
            return V.Label(text: "primary:" + count);
        }

        [Component(Compiler = false)]
        private static VNode EarlyAbortHost()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setAbortTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(AbortGuard, tick, key: "guard"),
                V.Suspense(V.Label(text: "loading"), new VNode[]
                {
                    V.Component(EarlyAbortCounter, key: "counter"), V.Component(VersionedReader, tick, key: "reader"),
                }),
            });
        }

        [Test]
        public void Given_AHiddenCommittedPrimaryAfterAnEarlierFailingSibling_When_TheAbortedRenderRetries_Then_ItsStateSurvives()
        {
            // Arrange
            s_resource.TrySetResult("initial");
            _mounted = V.Mount(_root, V.Component(EarlyAbortHost));
            s_setCount.Invoke(1);
            _mounted.FlushStateForTest();
            var committed = string.Join("|", _root.Query<Label>().ToList().Select(label => label.text));
            s_resource = new VelvetTaskCompletionSource<string>();
            s_setAbortTick.Invoke(1);
            _mounted.FlushStateForTest();
            var before = string.Join("|", _root.Query<Label>().ToList().Select(label => label.text));

            // Act
            s_abortThrows = true;
            s_setAbortTick.Invoke(2);
            _mounted.FlushStateForTest();
            var afterAbort = string.Join("|", _root.Query<Label>().ToList().Select(label => label.text));
            s_abortThrows = false;
            s_resource.TrySetResult("value");
            s_setAbortTick.Invoke(3);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((committed, before, afterAbort, string.Join("|", _root.Query<Label>().ToList().Select(label => label.text))),
                Is.EqualTo(("okay:0|primary:1|loaded:0:initial", "okay:1|loading", "error|loading", "okay:3|primary:1|loaded:3:value")));
        }

        [Test]
        public void Given_AHiddenCommittedPrimaryAfterAnEarlierFailingSibling_When_TheRenderAborts_Then_ItsPassiveEffectStaysSubscribed()
        {
            // Arrange
            s_resource.TrySetResult("initial");
            _mounted = V.Mount(_root, V.Component(EarlyAbortHost));
            _mounted.FlushEffectsForTest();
            s_resource = new VelvetTaskCompletionSource<string>();
            s_setAbortTick.Invoke(1);
            _mounted.FlushStateForTest();
            var before = string.Join("|", _root.Query<Label>().ToList().Select(label => label.text));
            var cleanupsBefore = s_abortCounterCleanups;

            // Act
            s_abortThrows = true;
            s_setAbortTick.Invoke(2);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((before, string.Join("|", _root.Query<Label>().ToList().Select(label => label.text)),
                    s_abortCounterSubscriptions, cleanupsBefore, s_abortCounterCleanups),
                Is.EqualTo(("okay:1|loading", "error|loading", 1, 0, 0)));
        }
    }
}
