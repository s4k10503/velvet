using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a state update that a layout effect, a layout cleanup an update runs, or a ref setup of one root
    /// makes on a component of another root renders: before control returns from the work that ran the commit, whether
    /// or not that component already has an update queued, as React gives an update made inside a commit outside a
    /// transition the sync lane whichever root it targets and renders every root's sync work as the commit ends. An
    /// update a transition or a <c>UseEffect</c> makes there takes no sync lane, nor does one made outside every commit
    /// and handler, after a commit an exception left included. A click handler's update on another root renders where
    /// the click's flush ends, and two roots whose layout effects update each other stop at the nested-update bound
    /// without starting again at their next frame.
    /// </summary>
    [TestFixture]
    internal sealed class CommitUpdateOnAnotherRootTests
    {
        private static readonly List<MountedTree> s_mounts = new();
        private static StateUpdater<int> s_setShown;
        private static int s_shown;
        private static StateUpdater<int> s_setWriter;

        [SetUp]
        public void SetUp()
        {
            FiberStrictMode.Enabled = false;
            (s_setShown, s_shown, s_setWriter) = (default, -1, default);
            (s_setPing, s_setPong, s_exchangeRenders) = (default, default, 0);
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = s_mounts.Count - 1; i >= 0; i--) s_mounts[i].Dispose();
            s_mounts.Clear();
        }

        private static MountedTree Mount(VNode tree)
        {
            var mounted = V.Mount(new VisualElement(), tree);
            s_mounts.Add(mounted);
            return mounted;
        }

        // The other root: records the value it last rendered and hands out its setter.
        [Component]
        private static VNode ShowsItsValue()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setShown = setValue;
            s_shown = value;
            return V.Div();
        }

        private static MountedTree MountTheOtherRootThen(VNode writer)
        {
            Mount(V.Component(ShowsItsValue));
            return Mount(writer);
        }

        // --- Updates a commit makes ---

        [Component]
        private static VNode WritesFromItsLayoutEffect()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setWriter = setTick;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (tick > 0) s_setShown.Invoke(tick);
                return null;
            }), new object[] { tick });
            return V.Div();
        }

        [Test]
        public void Given_ALayoutEffectThatSetsAnotherRootsState_When_ItsRootsDrainReturns_Then_TheOtherRootHasRenderedTheUpdate()
        {
            // Arrange
            var writer = MountTheOtherRootThen(V.Component(WritesFromItsLayoutEffect));
            s_setWriter.Invoke(1);

            // Act
            writer.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_shown, Is.EqualTo(1));
        }

        // The other root's update of 5, made outside every commit, waits on its tier when the layout effect writes 1.
        [Test]
        public void Given_ALayoutEffectThatSetsAnotherRootsStateWhereAnUpdateIsQueued_When_ItsRootsDrainReturns_Then_TheOtherRootHasRenderedTheUpdate()
        {
            // Arrange
            var writer = MountTheOtherRootThen(V.Component(WritesFromItsLayoutEffect));
            s_setShown.Invoke(5);
            s_setWriter.Invoke(1);

            // Act
            writer.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — 0 where neither update rendered
            Assert.That(s_shown, Is.EqualTo(1));
        }

        // Sets the other root's state inside a transition from the layout effect of its update.
        [Component]
        private static VNode WritesInATransitionFromItsLayoutEffect()
        {
            var (tick, setTick) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_setWriter = setTick;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (tick > 0) start.Invoke(() => s_setShown.Invoke(2));
                return null;
            }), new object[] { tick });
            return V.Div();
        }

        // GREEN_ON_BASE(characterization): the base leaves a transition's update and the one queued beside it waiting.
        // React gives an update a commit makes inside a transition its transition lane, so neither is the sync lane's.
        [Test]
        public void Given_ALayoutEffectThatSetsAnotherRootsStateInATransitionWhereAnUpdateIsQueued_When_ItsRootsDrainReturns_Then_TheOtherRootHasNotRendered()
        {
            // Arrange
            var writer = MountTheOtherRootThen(V.Component(WritesInATransitionFromItsLayoutEffect));
            s_setShown.Invoke(5);
            s_setWriter.Invoke(1);

            // Act
            writer.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — 5 where the queued update rendered as the sync lane's
            Assert.That(s_shown, Is.Zero);
        }

        private static readonly Func<VisualElement, Action> s_setsTheOtherRoot = _ =>
        {
            s_setShown.Invoke(1);
            return null;
        };

        // Attaches a ref callback that sets the other root's state once its own state has changed.
        [Component]
        private static VNode WritesFromItsRefCallback()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setWriter = setTick;
            return V.Div(refCallback: tick > 0 ? s_setsTheOtherRoot : null);
        }

        [Test]
        public void Given_ARefCallbackThatSetsAnotherRootsState_When_ItsRootsDrainReturns_Then_TheOtherRootHasRenderedTheUpdate()
        {
            // Arrange
            var writer = MountTheOtherRootThen(V.Component(WritesFromItsRefCallback));
            s_setWriter.Invoke(1);

            // Act
            writer.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_shown, Is.EqualTo(1));
        }

        // The cleanup of the mount's layout effect sets the other root's state; the update's own cleanup sets nothing.
        [Component]
        private static VNode WritesFromItsLayoutCleanup()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setWriter = setTick;
            Hooks.UseLayoutEffect(() => () =>
            {
                if (tick == 0) s_setShown.Invoke(1);
            }, new object[] { tick });
            return V.Div();
        }

        [Test]
        public void Given_ALayoutCleanupThatSetsAnotherRootsState_When_ItsRootsDrainReturns_Then_TheOtherRootHasRenderedTheUpdate()
        {
            // Arrange
            var writer = MountTheOtherRootThen(V.Component(WritesFromItsLayoutCleanup));
            s_setWriter.Invoke(1);

            // Act
            writer.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_shown, Is.EqualTo(1));
        }

        // --- Updates no commit makes ---

        [Component]
        private static VNode WritesFromItsMountEffect()
        {
            Hooks.UseEffect(() =>
            {
                s_setShown.Invoke(1);
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div();
        }

        // GREEN_ON_BASE(characterization): the base leaves another root's update from a UseEffect for its next drain.
        // React runs passive effects at no higher than the default priority, so the sync lane a commit gives must not
        // reach that update.
        [Test]
        public void Given_AUseEffectThatSetsAnotherRootsState_When_ThePassiveFlushReturns_Then_TheOtherRootHasNotRendered()
        {
            // Arrange
            var writer = MountTheOtherRootThen(V.Component(WritesFromItsMountEffect));

            // Act
            writer.FlushEffectsForTest();

            // Assert
            Assert.That(s_shown, Is.Zero);
        }

        private static readonly Ref<string> s_handle = new();

        // Fails in an imperative-handle factory on its first update, which leaves the layout commit and the drain.
        [Component]
        private static VNode ThrowsFromItsCommit()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setWriter = setTick;
            Hooks.UseImperativeHandle(s_handle, () =>
            {
                if (tick == 1) throw new InvalidOperationException("handle");
                return "handle";
            });
            return V.Div();
        }

        // GREEN_ON_BASE(characterization): the base gives no sync lane to a setter called outside commits and handlers.
        // A commit an exception left must not leave the sync lane given to every update after it.
        [Test]
        public void Given_ACommitThatAnExceptionLeft_When_AnotherRootsStateIsSetOutsideEveryCommit_Then_ThatRootOwesNoSyncLane()
        {
            // Arrange
            var other = Mount(V.Component(ShowsItsValue));
            var writer = Mount(V.Component(ThrowsFromItsCommit));
            s_setWriter.Invoke(1);
            var threw = false;
            try
            {
                writer.GetSchedulerForTest().DrainImmediateForTest();
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            // Act
            s_setShown.Invoke(1);

            // Assert — null where no exception left the commit
            Assert.That(threw ? other.GetSchedulerForTest().OwesSyncLane : (bool?)null, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base gives no sync lane to a second call outside commits and handlers.
        // An update folded onto one already queued takes the lane its own call is made under, here no commit's.
        [Test]
        public void Given_AComponentWithAnUpdateQueued_When_ItsStateIsSetAgainOutsideEveryCommit_Then_ItsRootOwesNoSyncLane()
        {
            // Arrange
            var other = Mount(V.Component(ShowsItsValue));
            s_setShown.Invoke(1);

            // Act
            s_setShown.Invoke(2);

            // Assert
            Assert.That(other.GetSchedulerForTest().OwesSyncLane, Is.False);
        }

        // --- Updates a discrete handler makes ---

        [Component]
        private static VNode SetsTheOtherRootOnAClick()
            => V.Button(name: "set", onClick: () => s_setShown.Invoke(1));

        // GREEN_ON_BASE(characterization): the base renders another root a click updated where the click's flush ends.
        // FiberWorkLoop.IsInDiscreteEvent's comment states it, and the sync lane a commit gives shares its mark.
        [Test]
        public void Given_AClickHandlerThatSetsAnotherRootsState_When_TheClickReturns_Then_TheOtherRootHasRenderedTheUpdate()
        {
            // Arrange
            Mount(V.Component(ShowsItsValue));
            var host = new VisualElement();
            s_mounts.Add(V.Mount(host, V.Component(SetsTheOtherRootOnAClick)));

            // Act
            host.Q<Button>("set").SimulateClick();

            // Assert
            Assert.That(s_shown, Is.EqualTo(1));
        }

        // --- Two roots whose commits keep updating each other ---

        private static StateUpdater<int> s_setPing;
        private static StateUpdater<int> s_setPong;
        private static int s_exchangeRenders;

        // Each writes the other a new value from the layout effect of every update it renders.
        [Component]
        private static VNode Ping()
        {
            var (n, setN) = Hooks.UseState(0);
            s_setPing = setN;
            s_exchangeRenders++;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (n > 0) s_setPong.Invoke(n + 1);
                return null;
            }), new object[] { n });
            return V.Div();
        }

        [Component]
        private static VNode Pong()
        {
            var (n, setN) = Hooks.UseState(0);
            s_setPong = setN;
            s_exchangeRenders++;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (n > 0) s_setPing.Invoke(n + 1);
                return null;
            }), new object[] { n });
            return V.Div();
        }

        // The first drain's release runs the exchange until the bound refuses a root's flush, which logs once.
        [Test]
        public void Given_TwoRootsWhoseLayoutEffectsUpdateEachOtherStoppedAtTheBound_When_TheirNextFrameRuns_Then_NeitherRendersAgain()
        {
            // Arrange
            LogAssert.Expect(UnityEngine.LogType.Exception, new Regex("Maximum update depth exceeded"));
            var pong = Mount(V.Component(Pong));
            var ping = Mount(V.Component(Ping));
            s_setPing.Invoke(1);
            ping.GetSchedulerForTest().DrainImmediateForTest();
            var rendersBefore = s_exchangeRenders;

            // Act
            ping.GetSchedulerForTest().RunImmediateCallbackForTest();
            pong.GetSchedulerForTest().RunImmediateCallbackForTest();

            // Assert
            Assert.That(s_exchangeRenders - rendersBefore, Is.Zero);
        }
    }
}
