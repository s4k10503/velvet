using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a Suspense that has shown its children does when they suspend again, as React does: it keeps
    /// their elements in the tree, hidden with an inline <c>display: none</c> ahead of the fallback, and reveals
    /// the same elements when the read resolves.
    /// <list type="bullet">
    /// <item>A component inside a host element of the children keeps its state, its passive effect stays
    /// connected, and its layout effect is cleaned up while hidden, each time it is hidden, and set up again on
    /// reveal.</item>
    /// <item>A component whose read resolves shows the value on reveal, one inside a host element of the
    /// children included.</item>
    /// <item>A nested boundary still waiting keeps its own children hidden, and their layout effects down, when
    /// the enclosing one reveals.</item>
    /// <item>Removing the boundary while it hides its children unmounts them once, and a boundary put back in
    /// its place has not shown anything, so it discards what its first render built.</item>
    /// <item>An error boundary that catches in the render that hid the children leaves no hidden element
    /// written to after the catch takes it back, and a boundary above the Suspense's owner still reports a
    /// catch it took in that render.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseHiddenPrimaryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setCount = default;
            s_setOwn = default;
            s_setShown = default;
            s_source = null;
            s_layoutSetups = 0;
            s_layoutCleanups = 0;
            s_passiveCleanups = 0;
            s_setOuterOwn = default;
            s_setInnerOwn = default;
            s_outerSource = null;
            s_setOwnerTick = default;
            s_innerLayoutSetups = 0;
            s_deepLayoutSetups = 0;
            s_setRestoreShown = default;
            s_setRestoreTick = default;
            s_setGhostTick = default;
            s_ghostThrows = false;
            s_setCatchTick = default;
            s_catchFactoryRuns = 0;
        }

        [Test]
        public void Given_ARevealedBoundary_When_AChildsOwnUpdateSuspendsIt_Then_ThePrimaryElementStaysInTheTree()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var primary = _root.Q<VisualElement>("primary");

            // Act
            Resuspend(mounted);

            // Assert
            Assert.That(primary.parent, Is.SameAs(_root.Q<VisualElement>("container")),
                "React hides the children a boundary has shown rather than removing them");
        }

        [Test]
        public void Given_ARevealedBoundary_When_AChildsOwnUpdateSuspendsIt_Then_ThePrimaryElementIsDisplayedNone()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var primary = _root.Q<VisualElement>("primary");

            // Act
            Resuspend(mounted);

            // Assert
            Assert.That(primary.style.display.value, Is.EqualTo(DisplayStyle.None),
                "React hides the children a boundary has shown with display: none while the fallback shows");
        }

        // GREEN_ON_BASE(characterization): the base removes the hidden element, and the pool clears its display.
        // What this pins is that the element the boundary kept hidden has its display cleared on reveal.
        [Test]
        public void Given_ABoundaryHidingItsPrimary_When_TheReadResolves_Then_ThePrimaryElementsDisplayIsClearedAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var primary = _root.Q<VisualElement>("primary");
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(primary.style.display.keyword, Is.EqualTo(StyleKeyword.Null),
                "React takes the display: none off the children it reveals");
        }

        [Test]
        public void Given_AStatefulComponentInsideAnElementOfARevealedPrimary_When_TheBoundarySuspendsAgainAndReveals_Then_ItKeepsItsState()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(_root.Q<VisualElement>("primary")?.Q<Label>()?.text, Is.EqualTo("count:1"),
                "React keeps the state of a tree a Suspense hides");
        }

        // GREEN_ON_BASE(characterization): the base shows the resolved value in elements it creates again.
        // What this pins is that a reader kept hidden is rendered for the reveal rather than ahead of it.
        [Test]
        public void Given_AReaderInARevealedPrimary_When_ItsOwnUpdateSuspendsTheBoundaryAndItsReadResolves_Then_ItShowsTheValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(_root.Q<Label>("reader")?.text, Is.EqualTo("reader:5"),
                "The boundary that reveals the reader shows what its resolved read rendered");
        }

        [Test]
        public void Given_AReaderInsideAnElementOfARevealedPrimary_When_ItsOwnUpdateSuspendsTheBoundaryAndItsReadResolves_Then_ItShowsTheValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ElementReaderHostRender, key: "element-reader-host"));
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(_root.Q<Label>("reader")?.text, Is.EqualTo("reader:5"),
                "React reveals the reader it kept hidden with what its resolved read rendered");
        }

        [Test]
        public void Given_AComponentInsideAnElementOfARevealedPrimary_When_TheBoundarySuspendsAgain_Then_ItsPassiveEffectStaysConnected()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Resuspend(mounted);
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_passiveCleanups, Is.EqualTo(0),
                "React does not unmount the passive effects of a tree a Suspense hides");
        }

        // GREEN_ON_BASE(characterization): the base runs this cleanup when it disposes the component.
        // What this pins is that a component kept hidden inside an element has its layout effect cleaned up.
        [Test]
        public void Given_AComponentInsideAnElementOfARevealedPrimary_When_TheBoundarySuspendsAgain_Then_ItsLayoutEffectIsCleanedUp()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));

            // Act
            Resuspend(mounted);

            // Assert
            Assert.That(s_layoutCleanups, Is.EqualTo(1),
                "React cleans up the layout effects of a tree a Suspense hides");
        }

        // GREEN_ON_BASE(characterization): the base sets the effect up again when it mounts the component anew.
        // What this pins is that a component kept hidden inside an element has its layout effect set up again.
        [Test]
        public void Given_AComponentInsideAnElementOfAHiddenPrimary_When_TheBoundaryReveals_Then_ItsLayoutEffectIsSetUpAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(s_layoutSetups, Is.EqualTo(2),
                "React sets the layout effects of a tree a Suspense reveals up again, whatever their dependencies");
        }

        // GREEN_ON_BASE(characterization): the base disposes the component each time the boundary suspends.
        // What this pins is that a component the boundary revealed is hidden again, its layout effect with it.
        [Test]
        public void Given_AComponentInsideAnElementTheBoundaryRevealedAfterHidingIt_When_TheBoundarySuspendsAgain_Then_ItsLayoutEffectIsCleanedUpAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            Resuspend(mounted);
            Resolve(mounted, 5);

            // Act
            s_setOwn.Invoke(2);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_layoutCleanups, Is.EqualTo(2),
                "React cleans up the layout effects of a tree each time a Suspense hides it");
        }

        // GREEN_ON_BASE(characterization): the base runs this cleanup once, when the boundary suspends.
        // What this pins is that removing a boundary that keeps its primary hidden takes that primary with it.
        [Test]
        public void Given_ABoundaryHidingItsPrimary_When_TheBoundaryIsRemoved_Then_ThePrimaryIsUnmountedOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            mounted.FlushEffectsForTest();
            Resuspend(mounted);
            mounted.FlushEffectsForTest();

            // Act
            s_setShown.Invoke(false);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — every label is read, hidden ones included, since a hidden primary left behind shows nowhere
            Assert.That((s_passiveCleanups, AllLabelTexts()), Is.EqualTo((1, "removed")),
                "React unmounts the hidden children with the boundary");
        }

        // GREEN_ON_BASE(characterization): the base keeps no primary of a boundary that has not shown it.
        // What this pins is that a boundary put back where a revealed one was removed has not shown anything.
        [Test]
        public void Given_ARevealedBoundaryRemovedAndPutBack_When_ItsFirstRenderSuspends_Then_ItKeepsNothingHidden()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RestoreHostRender, key: "restore-host"));
            s_setRestoreShown.Invoke(false);
            mounted.FlushStateForTest();

            // Act
            s_setRestoreTick.Invoke(1);
            s_setRestoreShown.Invoke(true);
            mounted.FlushStateForTest();

            // Assert — every label is read, hidden ones included, since a kept primary would be hidden
            Assert.That(AllLabelTexts(), Is.EqualTo("loading"),
                "React discards what a Suspense's first render built when it suspends");
        }

        [Test]
        public void Given_NestedBoundariesBothHidingTheirPrimaries_When_OnlyTheOuterReadResolves_Then_TheInnerPrimaryStaysHidden()
        {
            // Arrange — the inner boundary suspends first, then the outer one
            using var mounted = V.Mount(_root, V.Component(NestedHostRender, key: "nested-host"));
            s_setInnerOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_setOuterOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            s_outerSource.TrySetResult(5);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(_root.DisplayedLabelTexts("|"), Is.EqualTo("outer:5|inner-loading"),
                "The inner boundary still waits, so React keeps its children hidden and its fallback shown");
        }

        // GREEN_ON_BASE(characterization): the base mounts the inner children again under the inner fallback.
        // What this pins is that the inner boundary, which hid a component after the outer one did, keeps it.
        [Test]
        public void Given_AComponentAnInnerBoundaryHidAfterTheOuterOneDid_When_OnlyTheOuterReadResolves_Then_ItsLayoutEffectStaysDown()
        {
            // Arrange — the outer boundary hides first; the inner one then suspends inside it
            using var mounted = HideOuterThenInner();

            // Act
            s_outerSource.TrySetResult(5);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_innerLayoutSetups, Is.EqualTo(1),
                "The inner boundary still waits, so React keeps the layout effects of its children down");
        }

        // GREEN_ON_BASE(characterization): the base disposes the inner children when the outer boundary hides.
        // What this pins is that a component inside an element of the inner primary stays with the inner boundary.
        [Test]
        public void Given_AComponentInsideAnElementAnInnerBoundaryHidAfterTheOuterOneDid_When_OnlyTheOuterReadResolves_Then_ItsLayoutEffectStaysDown()
        {
            // Arrange — the outer boundary hides first; the inner one then suspends inside it
            using var mounted = HideOuterThenInner();

            // Act
            s_outerSource.TrySetResult(5);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_deepLayoutSetups, Is.EqualTo(1),
                "The inner boundary still waits, so React keeps the layout effects of its children down");
        }

        // GREEN_ON_BASE(characterization): the base hides nothing, so it writes to no element after the catch.
        // What this pins is that the hide a caught render recorded is not applied to the elements it took back.
        [Test]
        public void Given_AnErrorBoundaryCatchingInTheRenderThatHidItsSuspensesPrimary_When_ALaterRenderCreatesALabel_Then_TheLabelIsDisplayed()
        {
            // Arrange — the catch takes back the hidden primary's rows, whose elements go back to the pool
            using var mounted = V.Mount(_root, V.Component(GhostHostRender, key: "ghost-host"), new MountOptions((_, _) => { }));
            s_ghostThrows = true;
            s_setGhostTick.Invoke(1);
            mounted.FlushStateForTest();
            s_ghostThrows = false;

            // Act
            s_setGhostTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.DisplayedLabelTexts("|"), Is.EqualTo("error|extra"),
                "A boundary's catch takes the hidden primary back, and nothing hides an element after it");
        }

        // GREEN_ON_BASE(characterization): the base forgets no catch of a boundary outside the Suspense's walk.
        // What this pins is that a Suspense keeping its primary hidden forgets only catches inside that primary.
        [Test]
        public void Given_ABoundaryAboveASuspenseOwnerCatchingAnElementCallbackErrorInAPrimaryItHides_When_ThatRenderCommits_Then_TheCatchIsReported()
        {
            // Arrange
            var reports = 0;
            using var mounted = V.Mount(_root, V.Div(children: new VNode[] { V.Component(CatchBoundaryRender, key: "outer") }),
                new MountOptions((_, _) => reports++));

            // Act
            s_setCatchTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — the factory's runs are read with it, since a callback that never ran is reported by nobody
            Assert.That((s_catchFactoryRuns, reports), Is.EqualTo((1, 1)),
                "React reports a catch the boundary above took, whatever the Suspense below it does with its render");
        }

        private static void Resuspend(MountedTree mounted)
        {
            s_setOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static void Resolve(MountedTree mounted, int value)
        {
            s_source.TrySetResult(value);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private MountedTree HideOuterThenInner()
        {
            var mounted = V.Mount(_root, V.Component(OwnershipHostRender, key: "ownership-host"));
            s_setOuterOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_setOwnerTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            return mounted;
        }

        private string AllLabelTexts() => string.Join("|", _root.Query<Label>().ToList().Select(label => label.text));

        private static StateUpdater<int> s_setCount;
        private static StateUpdater<int> s_setOwn;
        private static StateUpdater<bool> s_setShown;
        private static VelvetTaskCompletionSource<int> s_source;
        private static int s_layoutSetups;
        private static int s_layoutCleanups;
        private static int s_passiveCleanups;

        [Component]
        private static VNode CounterRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            Hooks.UseLayoutEffect(() =>
            {
                s_layoutSetups++;
                return (Action)(() => s_layoutCleanups++);
            }, Array.Empty<object>());
            Hooks.UseEffect(() => () => s_passiveCleanups++, Array.Empty<object>());
            return V.Label(text: "count:" + count);
        }

        [Component]
        private static VNode ReaderRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : (s_source = new VelvetTaskCompletionSource<int>()).Task, own);
            return V.Label(name: "reader", text: "reader:" + value);
        }

        // Pending for good once tick is not zero.
        [Component]
        private static VNode TickReaderRender(int tick)
        {
            var value = Hooks.Use<int>(_ => tick == 0
                ? VelvetTask.FromResult(0)
                : new VelvetTaskCompletionSource<int>().Task, tick);
            return V.Label(text: "tick:" + value);
        }

        // The counter sits inside a host element of the primary, which that element's own reconcile mounts; the
        // reader sits in the Suspense's own walk.
        [Component]
        private static VNode HostRender()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(name: "container", children: new VNode[]
            {
                shown
                    ? V.Suspense(
                        fallback: V.Label(text: "loading"),
                        children: new VNode[]
                        {
                            V.Div(name: "primary", children: new VNode[] { V.Component(CounterRender, key: "counter") }),
                            V.Component(ReaderRender, key: "reader"),
                        })
                    : V.Label(text: "removed"),
            });
        }

        [Component]
        private static VNode ElementReaderHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Div(name: "reader-host", children: new VNode[] { V.Component(ReaderRender, key: "reader") }),
                    }),
            });

        private static StateUpdater<int> s_setOuterOwn;
        private static StateUpdater<int> s_setInnerOwn;
        private static VelvetTaskCompletionSource<int> s_outerSource;

        [Component]
        private static VNode OuterReaderRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setOuterOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : (s_outerSource = new VelvetTaskCompletionSource<int>()).Task, own);
            return V.Label(text: "outer:" + value);
        }

        [Component]
        private static VNode InnerReaderRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setInnerOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : new VelvetTaskCompletionSource<int>().Task, own);
            return V.Label(text: "inner:" + value);
        }

        // The inner Suspense sits in the outer one's own walk, so one walk expands both.
        [Component]
        private static VNode NestedHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "outer-loading"),
                    children: new VNode[]
                    {
                        V.Component(OuterReaderRender, key: "outer"),
                        V.Suspense(
                            fallback: V.Label(text: "inner-loading"),
                            children: new VNode[]
                            {
                                V.Div(name: "inner-primary", children: new VNode[]
                                {
                                    V.Component(InnerReaderRender, key: "inner"),
                                }),
                            }),
                    }),
            });

        private static StateUpdater<int> s_setOwnerTick;
        private static int s_innerLayoutSetups;
        private static int s_deepLayoutSetups;

        [Component]
        private static VNode InnerLayoutRender()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_innerLayoutSetups++;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "inner-layout");
        }

        [Component]
        private static VNode DeepLayoutRender()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_deepLayoutSetups++;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "deep-layout");
        }

        // The inner Suspense sits inside a host element of the outer primary, so that element's own reconcile
        // expands it; one of its children sits in its own walk and one inside a host element of it.
        [Component]
        private static VNode OwnershipHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnerTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "outer-loading"),
                    children: new VNode[]
                    {
                        V.Component(OuterReaderRender, key: "outer"),
                        V.Div(name: "inner-host", children: new VNode[]
                        {
                            V.Suspense(
                                fallback: V.Label(text: "inner-loading"),
                                children: new VNode[]
                                {
                                    V.Component(InnerLayoutRender, key: "inner-layout"),
                                    V.Div(name: "deep", children: new VNode[]
                                    {
                                        V.Component(DeepLayoutRender, key: "deep-layout"),
                                    }),
                                    V.Component(TickReaderRender, tick, key: "tick-reader"),
                                }),
                        }),
                    }),
            });
        }

        private static StateUpdater<bool> s_setRestoreShown;
        private static StateUpdater<int> s_setRestoreTick;

        [Component]
        private static VNode RestoreHostRender()
        {
            var (shown, setShown) = Hooks.UseState(true);
            var (tick, setTick) = Hooks.UseState(0);
            s_setRestoreShown = setShown;
            s_setRestoreTick = setTick;
            return V.Div(children: new VNode[]
            {
                shown
                    ? V.Suspense(
                        fallback: V.Label(text: "loading"),
                        children: new VNode[]
                        {
                            V.Label(text: "restored"),
                            V.Component(TickReaderRender, tick, key: "tick-reader"),
                        })
                    : V.Label(text: "removed"),
            });
        }

        private static StateUpdater<int> s_setGhostTick;
        private static bool s_ghostThrows;

        [Component(Compiler = false)]
        private static VNode GhostThrowerRender()
        {
            if (s_ghostThrows) throw new InvalidOperationException("ghost thrower failed");
            return V.Label(text: "thrower");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode GhostBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "error"));
            return V.Fragment(new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(TickReaderRender, tick, key: "tick-reader") }),
                V.Component(GhostThrowerRender, key: "thrower"),
            });
        }

        // The boundary is rendered by this host's walk, which is where its catch takes its rows back.
        [Component(Compiler = false)]
        private static VNode GhostHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setGhostTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(GhostBoundaryRender, tick, key: "boundary"),
                tick >= 2 ? V.Label(text: "extra") : null,
            });
        }

        private static StateUpdater<int> s_setCatchTick;
        private static int s_catchFactoryRuns;

        [Component(Compiler = false)]
        private static VNode CallbackThrowingRender()
            => V.ScrollView(onCreated: _ => throw new InvalidOperationException("Element callback throw"));

        // Its own update is the pass, so the boundary above it takes the callback's error on the aborting path.
        [Component(Compiler = false)]
        private static VNode CatchHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setCatchTick = setTick;
            return V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Component(TickReaderRender, tick, key: "tick-reader"),
                    tick == 0 ? null : V.Component(CallbackThrowingRender, key: "callback"),
                });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode CatchBoundaryRender()
        {
            Hooks.UseFallback(_ =>
            {
                s_catchFactoryRuns++;
                return V.Label(text: "outer-fallback");
            });
            return V.Component(CatchHostRender, key: "host");
        }
    }
}
