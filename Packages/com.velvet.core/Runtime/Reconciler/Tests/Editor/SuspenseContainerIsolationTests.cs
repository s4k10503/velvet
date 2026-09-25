using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class SuspenseContainerIsolationTests
    {
        private static VelvetTaskCompletionSource<string> s_resource;
        private static VelvetTaskCompletionSource<string> s_portalResourceA;
        private static VelvetTaskCompletionSource<string> s_portalResourceB;
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
            s_portalResourceA = new VelvetTaskCompletionSource<string>();
            s_portalResourceB = new VelvetTaskCompletionSource<string>();
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
            RuntimeStateProbe.ClearPortalRegistry();
        }

        private const string SuspenseRetargetId = "suspense-retarget-target";

        private static int SuspenseFallbackEntriesForContainer(ReconcilerContext ctx, VisualElement container)
        {
            var map = (Dictionary<ComponentFiber,
                    Dictionary<(VisualElement? Container, VisualElement? PortalScope, string Position), SuspenseNode>>)
                typeof(ReconcilerContext)
                    .GetField("_suspenseFallbackKeys", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(ctx);
            var count = 0;
            foreach (var entry in map.Values)
            {
                foreach (var key in entry.Keys)
                {
                    if (ReferenceEquals(key.Container, container)) count++;
                }
            }
            return count;
        }

        private static (ComponentFiber Boundary, VisualElement? Container, VisualElement? PortalScope, string Position)
            FirstBoundedFallbackKey(ReconcilerContext ctx)
        {
            var map = (Dictionary<ComponentFiber,
                    Dictionary<(VisualElement? Container, VisualElement? PortalScope, string Position), SuspenseNode>>)
                typeof(ReconcilerContext)
                    .GetField("_suspenseFallbackKeys", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(ctx);
            foreach (var boundary in map)
            {
                foreach (var key in boundary.Value.Keys)
                {
                    return (boundary.Key, key.Container, key.PortalScope, key.Position);
                }
            }
            throw new InvalidOperationException("No bounded suspense fallback entry was recorded.");
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
        private static VNode PortalAReader()
            => V.Label(text: Hooks.Use(_ => s_portalResourceA.Task));

        [Component(Compiler = false)]
        private static VNode PortalBReader()
            => V.Label(text: Hooks.Use(_ => s_portalResourceB.Task));

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

        private static StateUpdater<int> s_setHostTick;

        [Component(Compiler = false)]
        private static VNode StatefulHost()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setHostTick = setTick;
            return V.Component(Host);
        }

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

        [Test]
        public void Given_ARootlessSuspenseShowingFallback_When_TheResourceResolves_Then_ItsContainerStateIsReleased()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Suspense(V.Label(text: "loading"), new VNode[] { V.Component(Reader) }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var before = RootlessFallbackCount(scope.Reconciler);

            // Act
            s_resource.TrySetResult("loaded");
            scope.Reconciler.Reconcile(scope.Root, tree, tree);

            // Assert
            Assert.That((before, RootlessFallbackCount(scope.Reconciler)), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_ABoundedSuspenseShowingFallback_When_TheTreeIsDisposed_Then_ItsBoundaryStateIsReleased()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(Host));
            var reconciler = _mounted.Root.Reconciler;
            var before = reconciler.Context.AnyBoundaryShowingFallback;

            // Act
            _mounted.Dispose();
            _mounted = null;

            // Assert
            Assert.That((before, reconciler.Context.AnyBoundaryShowingFallback), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ASuspendedBoundary_When_TheResourceResolves_Then_AnyBoundaryShowingFallbackIsFalse()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(Host));
            var context = _mounted.Root.Reconciler.Context;
            Assume.That(context.AnyBoundaryShowingFallback, Is.True);

            // Act
            s_resource.TrySetResult("loaded");
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(context.AnyBoundaryShowingFallback, Is.False);
        }

        [Test]
        public void Given_ABoundedSuspenseFallbackEntry_When_ClearSuspenseStateRuns_Then_TheTableIsEmpty()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(Host));
            var context = _mounted.Root.Reconciler.Context;
            var before = context.AnyBoundaryShowingFallback;

            // Act
            context.ClearSuspenseState();

            // Assert
            Assert.That((before, context.AnyBoundaryShowingFallback), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_OneBoundaryWithFallbackInOneContainer_When_IsSuspenseFallbackShownQueriesAnotherContainerAtTheSameKey_Then_ItReturnsFalse()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(Host));
            var context = _mounted.Root.Reconciler.Context;
            var sample = FirstBoundedFallbackKey(context);
            var ready = _root.Q<VisualElement>("ready");

            // Act
            var shownOnReady = context.IsSuspenseFallbackShown(
                sample.Boundary, ready, sample.PortalScope, sample.Position);

            // Assert
            Assert.That((Texts("waiting"), shownOnReady), Is.EqualTo(("loading", false)));
        }

        [Test]
        public void Given_OneSuspendedAndOneReadyBoundary_When_TheHostReRenders_Then_TheReadyContainerStaysResolved()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(StatefulHost));
            var context = _mounted.Root.Reconciler.Context;
            Assume.That((Texts("waiting"), Texts("ready")), Is.EqualTo(("loading", "ready")));
            Assume.That(context.AnyBoundaryShowingFallback, Is.True);

            // Act
            s_setHostTick.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (Texts("waiting"), Texts("ready"), context.AnyBoundaryShowingFallback),
                Is.EqualTo(("loading", "ready", true)));
        }

        [Component(Compiler = false)]
        private static VNode ResolvedPrimaryContextHost()
            => V.Suspense(
                V.Provider(Tint, "fallback", new VNode[] { V.Label(text: "loading") }),
                new VNode[]
                {
                    V.Provider(Tint, "primary", new VNode[] { V.Component(FallbackCounter) }),
                });

        [Test]
        public void Given_AResolvedPrimarySubtree_When_ItsConsumerUpdates_Then_ThePrimaryProviderIsRestored()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(ResolvedPrimaryContextHost));
            var before = _root.Q<Label>()?.text;

            // Act
            s_setFallbackCount.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((before, _root.Q<Label>()?.text), Is.EqualTo(("primary:0", "primary:1")));
        }

        private static VisualElement s_sharedTarget;
        private static StateUpdater<bool> s_setShow;
        private static StateUpdater<bool> s_setShowWaiting;

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

        private static StateUpdater<bool> s_setShowWaitingPortal;

        [Component(Compiler = false)]
        private static VNode ConditionalPortalHost()
        {
            var (showWaitingPortal, setShowWaitingPortal) = Hooks.UseState(true);
            s_setShowWaitingPortal = setShowWaitingPortal;
            return V.Div(children: new VNode[]
            {
                showWaitingPortal
                    ? V.Portal(s_sharedTarget, new VNode[] { WaitingBoundary() })
                    : V.Label(text: "gone"),
                V.Portal(s_sharedTarget, new VNode[]
                {
                    V.Suspense(V.Label(text: "unused"), new VNode[] { V.Label(text: "ready") }),
                }),
            });
        }

        [Test]
        public void Given_ASuspendedBoundaryInARemovedPortal_When_TheHostUpdates_Then_ThatPortalsFallbackStateIsPruned()
        {
            // Arrange
            s_sharedTarget = new VisualElement();
            _mounted = V.Mount(_root, V.Component(ConditionalPortalHost));
            var context = _mounted.Root.Reconciler.Context;
            Assume.That(context.AnyBoundaryShowingFallback, Is.True);
            var ready = string.Join("|", s_sharedTarget.Query<Label>().ToList().Select(label => label.text));

            // Act
            s_setShowWaitingPortal.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((ready, context.AnyBoundaryShowingFallback,
                    string.Join("|", s_sharedTarget.Query<Label>().ToList().Select(label => label.text))),
                Is.EqualTo(("loading|ready", false, "ready")));
        }

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

        private static StateUpdater<int> s_setPortalAFallbackCount;
        private static StateUpdater<int> s_setPortalBFallbackCount;

        [Component(Compiler = false)]
        private static VNode PortalAFallbackCounter()
        {
            var tint = Hooks.UseContext(Tint);
            var (count, setCount) = Hooks.UseState(0);
            s_setPortalAFallbackCount = setCount;
            return V.Label(text: tint + ":" + count);
        }

        [Component(Compiler = false)]
        private static VNode PortalBFallbackCounter()
        {
            var tint = Hooks.UseContext(Tint);
            var (count, setCount) = Hooks.UseState(0);
            s_setPortalBFallbackCount = setCount;
            return V.Label(text: tint + ":" + count);
        }

        private static VNode PortalContextWaitingBoundary(string tint, Func<VNode> counter, Func<VNode> reader)
            => V.Suspense(
                V.Provider(Tint, tint, new VNode[] { V.Component(counter) }),
                new VNode[] { V.Component(reader) });

        [Component(Compiler = false)]
        private static VNode DualPortalContextHost()
            => V.Div(children: new VNode[]
            {
                V.Portal(s_sharedTarget, new VNode[] { PortalContextWaitingBoundary("portal-a", PortalAFallbackCounter, PortalAReader) }),
                V.Portal(s_sharedTarget, new VNode[] { PortalContextWaitingBoundary("portal-b", PortalBFallbackCounter, PortalBReader) }),
            });

        [Test]
        public void Given_TwoPortalsWithFallbackConsumers_When_OneFallbackUpdates_Then_TheOtherKeepsItsProvider()
        {
            // Arrange
            s_sharedTarget = new VisualElement();
            _mounted = V.Mount(_root, V.Component(DualPortalContextHost));
            var before = string.Join("|", s_sharedTarget.Query<Label>().ToList().Select(label => label.text));

            // Act
            s_setPortalAFallbackCount.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((before, string.Join("|", s_sharedTarget.Query<Label>().ToList().Select(label => label.text))),
                Is.EqualTo(("portal-a:0|portal-b:0", "portal-a:1|portal-b:0")));
        }

        [Component(Compiler = false)]
        private static VNode SplitHost()
        {
            var (showWaiting, setShowWaiting) = Hooks.UseState(true);
            s_setShowWaiting = setShowWaiting;
            return V.Div(children: new VNode[]
            {
                showWaiting
                    ? V.Div(name: "waiting", children: new VNode[]
                    {
                        V.Suspense(V.Label(text: "loading"), new VNode[]
                        {
                            V.Component(Counter), V.Component(Reader),
                        }),
                    })
                    : V.Label(text: "no-wait"),
                V.Div(name: "ready", children: new VNode[]
                {
                    V.Suspense(V.Label(text: "unused"), new VNode[] { V.Label(text: "ready") }),
                }),
            });
        }

        [Test]
        public void Given_ASuspendedBoundaryInARemovedWaitingContainer_When_TheHostUpdates_Then_ThatContainersFallbackStateIsPruned()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(SplitHost));
            var context = _mounted.Root.Reconciler.Context;
            Assume.That(context.AnyBoundaryShowingFallback, Is.True);
            Assume.That(Texts("ready"), Is.EqualTo("ready"));

            // Act
            s_setShowWaiting.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((Texts("ready"), context.AnyBoundaryShowingFallback), Is.EqualTo(("ready", false)));
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

        [Test]
        public void Given_ASuspenseThatWasRemovedAndRestored_When_ItSuspendsAgain_Then_ItsFallbackRenders()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(ConditionalHost));
            var before = _root.Q<Label>()?.text;
            s_setShow.Invoke(false);
            _mounted.FlushStateForTest();
            s_resource = new VelvetTaskCompletionSource<string>();

            // Act
            s_setShow.Invoke(true);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((before, _root.Q<Label>()?.text), Is.EqualTo(("loading", "loading")));
        }

        [Component(Compiler = false)]
        private static VNode RegistryPortalSuspenseHost()
            => V.Portal(SuspenseRetargetId, new VNode[] { WaitingBoundary() });

        [Test]
        public void Given_ASuspendedBoundaryInARetargetedRegistryPortal_When_TheIdNamesADifferentElement_Then_ItsPriorContainerFallbackEntryIsReleased()
        {
            // Arrange
            var overlay = new VisualElement();
            var other = new VisualElement();
            FiberPortalRegistry.Register(SuspenseRetargetId, overlay);
            _mounted = V.Mount(_root, V.Component(RegistryPortalSuspenseHost));
            var context = _mounted.Root.Reconciler.Context;
            var before = SuspenseFallbackEntriesForContainer(context, overlay);

            // Act
            s_resource = new VelvetTaskCompletionSource<string>();
            LogAssert.Expect(LogType.Warning,
                $"[FiberPortalRegistry] Id \"{SuspenseRetargetId}\" is already registered. Overwriting.");
            FiberPortalRegistry.Register(SuspenseRetargetId, other);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((before, SuspenseFallbackEntriesForContainer(context, overlay)),
                Is.EqualTo((1, 0)));
        }

        private static StateUpdater<int> s_setReadyTintCount;

        [Component(Compiler = false)]
        private static VNode ReadyTintCounter()
        {
            var tint = Hooks.UseContext(Tint);
            var (count, setCount) = Hooks.UseState(0);
            s_setReadyTintCount = setCount;
            return V.Label(name: "ready-counter", text: tint + ":" + count);
        }

        [Component(Compiler = false)]
        private static VNode OppositeBranchContextHost()
            => V.Div(children: new VNode[]
            {
                V.Div(name: "waiting", children: new VNode[]
                {
                    V.Suspense(
                        V.Provider(Tint, "waiting-fallback", new VNode[] { V.Component(FallbackCounter) }),
                        new VNode[] { V.Component(Reader) }),
                }),
                V.Div(name: "ready", children: new VNode[]
                {
                    V.Suspense(
                        V.Provider(Tint, "ignored", new VNode[] { V.Label(text: "loading") }),
                        new VNode[]
                        {
                            V.Provider(Tint, "ready-primary", new VNode[] { V.Component(ReadyTintCounter) }),
                        }),
                }),
            });

        [Test]
        public void Given_OppositeSuspenseBranchesInTwoContainers_When_TheReadyPrimaryConsumerUpdates_Then_ItKeepsItsPrimaryProvider()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(OppositeBranchContextHost));
            var readyCounter = _root.Q<VisualElement>("ready").Q<Label>(name: "ready-counter");
            var atMount = readyCounter?.text;
            var waitingBefore = Texts("waiting");

            // Act
            s_setReadyTintCount.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (waitingBefore, atMount, readyCounter?.text),
                Is.EqualTo(("waiting-fallback:0", "ready-primary:0", "ready-primary:1")));
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
