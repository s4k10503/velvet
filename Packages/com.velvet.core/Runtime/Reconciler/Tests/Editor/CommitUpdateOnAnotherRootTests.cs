using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a state update that one root's commit makes on another root renders: before control returns
    /// from the work that ran the commit, as React gives an update made inside a commit outside a transition the sync
    /// lane whichever root it targets and renders every root's sync work as the commit ends. An update a
    /// <c>UseEffect</c> makes there takes no sync lane, nor does one made outside every commit after a commit an
    /// exception left.
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
            try
            {
                writer.GetSchedulerForTest().DrainImmediateForTest();
            }
            catch (InvalidOperationException)
            {
            }

            // Act
            s_setShown.Invoke(1);

            // Assert
            Assert.That(other.GetSchedulerForTest().OwesSyncLane, Is.False);
        }
    }
}
