using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a deletion leaves for the passive phase after its commit, where a removed component's
    /// <c>UseEffect</c> cleanup runs: an element the deletion removed stays out of the pool until that cleanup has
    /// run, and goes back to it then; the deletion arms a drain for the cleanup, and one a parked transition made
    /// arms another once the transition commits; and where no drain can run it, the cleanup runs at once. A later
    /// deletion, in another tree, removes only the rows its own render drops. <c>DeletionCommitStateTests</c> owns
    /// what the deletion itself keeps once it has ended.
    /// </summary>
    [TestFixture]
    internal sealed class DeletionLeftoverTests
    {
        private const string CleanupText = "written by a removed component's cleanup";

        private static Button s_capturedButton;
        private static StateUpdater<bool> s_setShown;
        private static StateUpdater<bool> s_setLeadingShown;
        private static ComponentFiber s_slicedList;
        private static int s_generation;

        [SetUp]
        public void SetUp()
        {
            s_capturedButton = null;
            s_slicedList = null;
            s_generation = 0;
            s_setups = 0;
            s_cleanups = 0;
        }

        // Its cleanup writes the button its setup captured, the way an effect keeps the node a ref held.
        [Component(Compiler = false)]
        private static VNode CapturingButton()
        {
            var button = Hooks.UseRef<Button>();
            Hooks.UseEffect(() =>
            {
                var captured = button.Current;
                s_capturedButton = captured;
                return () => captured.text = CleanupText;
            }, Array.Empty<object>());
            return V.Button(text: "a", refCallback: button.SetElement);
        }

        [Component(Compiler = false)]
        private static VNode PlainButton()
        {
            var button = Hooks.UseRef<Button>();
            Hooks.UseLayoutEffect(() =>
            {
                s_capturedButton = button.Current;
                return default(Action);
            }, Array.Empty<object>());
            return V.Button(text: "a", refCallback: button.SetElement);
        }

        [Component(Compiler = false)]
        private static VNode LaterButton() => V.Button(text: "b", name: "later");

        // The removed button's element reconciles ahead of the later button, which the same pass mounts.
        [Component(Compiler = false)]
        private static VNode MovedButton()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Fragment(
                V.Div(children: new VNode[] { shown ? V.Component(CapturingButton, key: "a") : null }),
                shown ? null : V.Component(LaterButton, key: "b"));
        }

        [Component(Compiler = false)]
        private static VNode HiddenCapturingButton()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(children: new VNode[] { shown ? V.Component(CapturingButton, key: "a") : null });
        }

        [Component(Compiler = false)]
        private static VNode HiddenPlainButton()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(children: new VNode[] { shown ? V.Component(PlainButton, key: "a") : null });
        }

        [Component(Compiler = false)]
        private static VNode CleanedUp()
        {
            Hooks.UseEffect(() => () => s_cleanups++, Array.Empty<object>());
            return V.Div();
        }

        // Keyed elements at the top level keep the list's own reconcile on the path that time-slices, as in
        // ParkedPassResumeTests; the first row holds the child the list's transition deletes, and nothing the
        // list renders has a passive effect left once it has.
        [Component(Compiler = false)]
        private static VNode SlicedRows()
        {
            s_slicedList = FiberAmbientStack.Current;
            var rows = new VNode[4];
            for (var i = 0; i < rows.Length; i++)
            {
                VNode cell = i == 0 && s_generation == 0 ? V.Component(CleanedUp, key: "d") : V.Label(text: "row" + i);
                rows[i] = V.Div(key: "row" + i, children: new VNode[] { cell });
            }
            return V.Fragment(children: rows);
        }

        private static int s_setups;
        private static int s_cleanups;

        [Component(Compiler = false)]
        private static VNode Counted()
        {
            Hooks.UseEffect(() =>
            {
                s_setups++;
                return () => s_cleanups++;
            }, Array.Empty<object>());
            return V.Label(text: "counted");
        }

        // A list of two rows, each a component with a passive cleanup.
        [Component(Compiler = false)]
        private static VNode CountedRows()
            => V.VirtualList(
                items: new[] { "A", "B" },
                keySelector: item => item,
                itemHeight: 50f,
                renderer: item => V.Component(Counted, key: item),
                overscan: 0);

        [Component(Compiler = false)]
        private static VNode NamedRow(string name) => V.Div(name: name);

        // Two rows ahead of one that stays, which a render removes.
        [Component(Compiler = false)]
        private static VNode LeadingRowsRemoved()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setLeadingShown = setShown;
            return V.Fragment(
                shown ? V.Component(NamedRow, "x", key: "x") : null,
                shown ? V.Component(NamedRow, "y", key: "y") : null,
                V.Component(NamedRow, "z", key: "z"));
        }

        // A row after two that stay, which a render removes.
        [Component(Compiler = false)]
        private static VNode TrailingRowRemoved()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Fragment(
                V.Component(NamedRow, "p", key: "p"),
                V.Component(NamedRow, "q", key: "q"),
                shown ? V.Component(NamedRow, "r", key: "r") : null);
        }

        private static int ButtonsPooled()
        {
            var pool = typeof(VNodePool).GetField("s_buttonPool", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null);
            return (int)pool.GetType().GetProperty("Count")!.GetValue(pool);
        }

        private static void EmptyTheButtonPool()
        {
            while (ButtonsPooled() > 0) VNodePool.RentButton();
        }

        private static string Which(Button rented)
            => s_capturedButton == null ? "no button captured"
                : ReferenceEquals(rented, s_capturedButton) ? "the removed button" : "another button";

        // GREEN_ON_BASE(characterization): the base runs a removed component's passive cleanup in the deletion itself.
        // That is ahead of the removal that pools the button.
        [Test]
        public void Given_ARemovedComponentWhoseCleanupWritesTheButtonItCaptured_When_TheSamePassMountsAButton_Then_TheNewButtonKeepsItsText()
        {
            // Arrange — with nothing pooled, the removed button is the one the next rent would hand out
            EmptyTheButtonPool();
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(MovedButton));
            mounted.DrainForTest();

            // Act
            s_setShown.Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(root.Q<Button>("later").text, Is.EqualTo("b"));
        }

        [Test]
        public void Given_ARemovedButtonWhoseComponentsPassiveCleanupIsQueued_When_AButtonIsRented_Then_ItIsAnotherButton()
        {
            // Arrange
            EmptyTheButtonPool();
            using var mounted = V.Mount(new VisualElement(), V.Component(HiddenCapturingButton));
            mounted.DrainForTest();
            s_setShown.Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            var rented = VNodePool.RentButton();

            // Assert
            Assert.That(Which(rented), Is.EqualTo("another button"));
        }

        // GREEN_ON_BASE(characterization): the base pools the button as the commit removes it, ahead of the drain.
        [Test]
        public void Given_ARemovedButtonWhoseComponentsPassiveCleanupWasQueued_When_TheCleanupHasRun_Then_TheButtonIsRentedAgain()
        {
            // Arrange
            EmptyTheButtonPool();
            using var mounted = V.Mount(new VisualElement(), V.Component(HiddenCapturingButton));
            mounted.DrainForTest();
            s_setShown.Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            mounted.FlushEffectsForTest();
            var rented = VNodePool.RentButton();

            // Assert
            Assert.That(Which(rented), Is.EqualTo("the removed button"));
        }

        // GREEN_ON_BASE(characterization): the base pools a removed button as the commit removes it.
        [Test]
        public void Given_ARemovedButtonWhoseComponentQueuedNoPassiveCleanup_When_AButtonIsRented_Then_ItIsTheRemovedButton()
        {
            // Arrange
            EmptyTheButtonPool();
            using var mounted = V.Mount(new VisualElement(), V.Component(HiddenPlainButton));
            mounted.DrainForTest();
            s_setShown.Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            var rented = VNodePool.RentButton();

            // Assert
            Assert.That(Which(rented), Is.EqualTo("the removed button"));
        }

        // GREEN_ON_BASE(characterization): the base pools the button once, as the commit removes it.
        [Test]
        public void Given_AButtonPooledOnceItsComponentsCleanupRan_When_ALaterDrainRuns_Then_TheButtonIsNotPooledAgain()
        {
            // Arrange — the removed button is rented out again, and the component mounts once more on another one
            EmptyTheButtonPool();
            using var mounted = V.Mount(new VisualElement(), V.Component(HiddenCapturingButton));
            mounted.DrainForTest();
            s_setShown.Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            mounted.FlushEffectsForTest();
            var removed = s_capturedButton;
            var firstRent = VNodePool.RentButton();

            // Act
            s_setShown.Invoke(true);
            mounted.DrainForTest();
            var secondRent = VNodePool.RentButton();

            // Assert — a first rent that was not the removed button leaves the second nothing to repeat, so that
            // reading is a sentinel.
            Assert.That(ReferenceEquals(firstRent, removed) ? ReferenceEquals(secondRent, removed) : (bool?)null,
                Is.False);
        }

        // GREEN_ON_BASE(characterization): the base pools the button as the commit removes it, ahead of the disposal.
        [Test]
        public void Given_ARemovedButtonWhoseComponentsPassiveCleanupIsQueued_When_TheTreeIsDisposed_Then_TheButtonIsRentedAgain()
        {
            // Arrange
            EmptyTheButtonPool();
            var mounted = V.Mount(new VisualElement(), V.Component(HiddenCapturingButton));
            mounted.DrainForTest();
            s_setShown.Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            mounted.Dispose();
            var rented = VNodePool.RentButton();

            // Assert
            Assert.That(Which(rented), Is.EqualTo("the removed button"));
        }

        [Test]
        public void Given_ADeletionATransitionHeldWhileParked_When_TheTransitionCommits_Then_APassiveDrainIsArmedForIt()
        {
            // Arrange — the drain armed for the deletion runs while the transition is parked, and finds it held
            using var mounted = V.Mount(new VisualElement(), V.Component(SlicedRows));
            mounted.DrainForTest();
            var context = mounted.Root.Reconciler!.Context;
            s_generation = 1;
            s_slicedList.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_slicedList.FlushStateWithTinyBudgetForTest();
            typeof(FiberEffects).GetMethod("DrainPassiveEffects", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { context });
            var heldWithNoDrainArmed = s_slicedList.HasPendingReconcileWorkForTest()
                && !context.PassiveEffectDrainScheduled;

            // Act
            s_slicedList.DrainTimeSlicedReconcileForTest();

            // Assert — a pass that never parked, or a drain still armed, reads true either way, so that reading is a
            // sentinel.
            Assert.That(heldWithNoDrainArmed ? context.PassiveEffectDrainScheduled : (bool?)null, Is.True);
        }

        [Test]
        public void Given_AListRowWithAPassiveCleanup_When_ARangeUpdateThatPlacesNoRowDropsIt_Then_APassiveDrainIsArmed()
        {
            // Arrange — the rows render from range updates outside any pass, the way a scroll renders them
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CountedRows));
            mounted.DrainForTest();
            var context = mounted.Root.Reconciler!.Context;
            var list = context.VirtualListControllers[root.Q<ScrollView>()];
            list.UpdateVisibleRange(scrollY: 0f, viewportHeight: 100f);
            mounted.DrainForTest();
            var armedBefore = context.PassiveEffectDrainScheduled;

            // Act
            list.UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);

            // Assert — a drain still armed from the rows' mount reads true either way, so that reading is a sentinel.
            Assert.That(armedBefore ? (bool?)null : context.PassiveEffectDrainScheduled, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base runs a deleted component's passive cleanup in the deletion itself.
        [Test]
        public void Given_ABareReconcilerThatDeletesAComponentWithAPassiveCleanup_When_TheDeletionCommits_Then_TheCleanupHasRun()
        {
            // Arrange — nothing arms a drain for a bare reconciler, so the mount's setup is flushed by hand
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var shown = new VNode[] { V.Component(Counted, key: "c") };
            reconciler.Reconcile(root, Array.Empty<VNode>(), shown);
            FiberEffects.FlushPendingPassiveEffects(reconciler.Context);

            // Act
            reconciler.Reconcile(root, shown, Array.Empty<VNode>());

            // Assert — a setup that never ran has no cleanup to run, so that reading is a sentinel.
            Assert.That(s_setups == 1 ? s_cleanups : -1, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base clears a fiber's effects as it unmounts, so its remount sets up.
        [Test]
        public void Given_AFiberUnmountedWithAPassiveEffect_When_ItIsMountedAgain_Then_TheEffectSetsUpAgain()
        {
            // Arrange — the Unmount then Mount pair reuses one fiber, as UseDeferredValueTests' remount does
            var root = new VisualElement();
            var fiber = FiberRenderer.CreateRoot(Counted);
            FiberRenderer.Mount(fiber, root);
            FiberEffects.FlushPendingPassiveEffects(fiber);
            FiberRenderer.Unmount(fiber);
            var setupsBefore = s_setups;

            // Act
            FiberRenderer.Mount(fiber, root);
            FiberEffects.FlushPendingPassiveEffects(fiber);

            // Assert
            Assert.That(s_setups - setupsBefore, Is.EqualTo(1));
        }

        [Test]
        public void Given_ADeletionAParkedTransitionHolds_When_TheTreeIsDisposed_Then_ItsPassiveCleanupRuns()
        {
            // Arrange
            var mounted = V.Mount(new VisualElement(), V.Component(SlicedRows));
            mounted.DrainForTest();
            s_generation = 1;
            s_slicedList.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_slicedList.FlushStateWithTinyBudgetForTest();
            mounted.FlushEffectsForTest();
            var heldWhileParked = s_slicedList.HasPendingReconcileWorkForTest() && s_cleanups == 0;

            // Act
            mounted.Dispose();

            // Assert — a cleanup that ran before the disposal reads the same, so that reading is a sentinel.
            Assert.That(heldWhileParked ? s_cleanups : -1, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base removes only the rows each render drops.
        [Test]
        public void Given_ATreeThatRemovedTwoLeadingRows_When_AnotherTreeRemovesItsLastRow_Then_ThatTreeKeepsItsOtherRows()
        {
            // Arrange
            using var first = V.Mount(new VisualElement(), V.Component(LeadingRowsRemoved));
            first.DrainForTest();
            s_setLeadingShown.Invoke(false);
            first.DrainForTest();
            var root = new VisualElement();
            using var second = V.Mount(root, V.Component(TrailingRowRemoved));
            second.DrainForTest();

            // Act
            s_setShown.Invoke(false);
            second.DrainForTest();

            // Assert
            Assert.That(string.Join(",", new[] { "p", "q", "r" }.Where(name => root.Q(name) != null)),
                Is.EqualTo("p,q"));
        }
    }
}
