using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseDeferredValue{T}(T)"/> in a function component.
    /// <list type="bullet">
    /// <item>The first render returns the input value as-is.</item>
    /// <item>An urgent re-render that carries a changed input returns the previously committed value and queues the new value as pending on the transition lane.</item>
    /// <item>The next transition flush commits the pending value, so the new value is returned.</item>
    /// <item>An unrelated re-render that drains ahead of that transition flush leaves the pending value deferred.</item>
    /// <item>A deferred value fed from a prop still commits: the transition lane its body queues during the
    /// parent's subsuming render survives that render's settle, including when the request coalesces onto a
    /// lane the previous parent render already queued.</item>
    /// <item>An urgent re-render whose input is unchanged returns the current value and schedules no transition.</item>
    /// <item>Reverting the input to the committed value clears any pending value, so a later change to the same value defers again instead of committing immediately.</item>
    /// <item>The initialValue overload returns initialValue on the first render and schedules a transition that defers toward the live value; when initialValue already equals the value it commits the value with no transition.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. An urgent
    /// re-render is induced by firing a sibling UseState setter so the deferred-value hook re-evaluates under the
    /// changed input. Per-region static fields are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseDeferredValueTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetDeferred();
            ResetInitialValue();
            ResetProp();
            ResetSliced();
        }

        [Test]
        public void Given_FirstRender_When_Mounted_Then_ReturnsInputValueAsIs()
        {
            // Arrange
            s_deferredInput = "alpha";

            // Act
            using var mounted = V.Mount(_root, V.Component(DeferredRender, key: "deferred-init"));

            // Assert
            Assert.That(s_deferredObserved, Is.EqualTo("alpha"), "The first render returns the input value as-is");
        }

        [Test]
        public void Given_CommittedValue_When_UrgentReRenderCarriesNewInput_Then_ReturnsPreviousValue()
        {
            // Arrange
            s_deferredInput = "alpha";
            using var mounted = V.Mount(_root, V.Component(DeferredRender, key: "deferred-defer"));
            Assume.That(s_deferredObserved, Is.EqualTo("alpha"), "Precondition: the committed value is alpha");

            // Act — change the input and fire an urgent re-render on the Normal lane
            s_deferredInput = "beta";
            s_deferredForceSetter.Invoke(s_deferredForceValue + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_deferredObserved, Is.EqualTo("alpha"),
                "An urgent re-render returns the previous value while the new value is pending on the transition lane");
        }

        [Test]
        public void Given_PendingValue_When_TransitionFlushed_Then_CommitsPendingValue()
        {
            // Arrange
            s_deferredInput = "alpha";
            using var mounted = V.Mount(_root, V.Component(DeferredRender, key: "deferred-flush"));
            s_deferredInput = "beta";
            s_deferredForceSetter.Invoke(s_deferredForceValue + 1);
            mounted.FlushStateForTest();
            Assume.That(s_deferredObserved, Is.EqualTo("alpha"), "Precondition: beta is pending, alpha is committed");

            // Act — flush the transition lane
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_deferredObserved, Is.EqualTo("beta"),
                "The transition flush commits the pending value and returns the new value");
        }

        [Test]
        public void Given_UnchangedInput_When_UrgentReRender_Then_ReturnsCurrentValue()
        {
            // Arrange
            s_deferredInput = "alpha";
            using var mounted = V.Mount(_root, V.Component(DeferredRender, key: "deferred-same"));

            // Act — re-render without changing the input
            s_deferredForceSetter.Invoke(s_deferredForceValue + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_deferredObserved, Is.EqualTo("alpha"), "An unchanged input returns the current value");
        }

        [Test]
        public void Given_UnchangedInput_When_UrgentReRender_Then_NoTransitionRenderScheduled()
        {
            // Arrange
            s_deferredInput = "alpha";
            using var mounted = V.Mount(_root, V.Component(DeferredRender, key: "deferred-same-count"));
            var renderCountBefore = s_deferredRenderCount;

            // Act — re-render without changing the input
            s_deferredForceSetter.Invoke(s_deferredForceValue + 1);
            mounted.FlushStateForTest();

            // Assert — only the single urgent re-render is counted; no extra transition render
            Assert.That(s_deferredRenderCount - renderCountBefore, Is.EqualTo(1),
                "An unchanged input schedules no transition lane and produces no extra render");
        }

        [Test]
        public void Given_PendingValue_When_AnUnrelatedUrgentReRenderPrecedesTheTransitionFlush_Then_StillReturnsPreviousValue()
        {
            // Arrange — beta is pending on the transition lane while alpha stays committed
            s_deferredInput = "alpha";
            using var mounted = V.Mount(_root, V.Component(DeferredRender, key: "deferred-interleave"));
            s_deferredInput = "beta";
            s_deferredForceSetter.Invoke(s_deferredForceValue + 1);
            mounted.FlushStateForTest();
            Assume.That(s_deferredObserved, Is.EqualTo("alpha"), "Precondition: beta is pending, alpha is committed");

            // Act — a sibling state change re-renders on the Normal lane, which drains ahead of the
            // still-queued transition lane, with the deferred input unchanged at beta
            s_deferredForceSetter.Invoke(s_deferredForceValue + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_deferredObserved, Is.EqualTo("alpha"),
                "A re-render that is not the transition flush leaves the pending value deferred");
        }

        [Test]
        public void Given_InputRevertedToCommitted_When_ChangedAgain_Then_DefersInsteadOfCommittingImmediately()
        {
            // Arrange — alpha is committed, then beta is deferred so a pending value exists
            s_deferredInput = "alpha";
            using var mounted = V.Mount(_root, V.Component(DeferredRender, key: "deferred-osc"));
            s_deferredInput = "beta";
            s_deferredForceSetter.Invoke(s_deferredForceValue + 1);
            mounted.FlushStateForTest();
            Assume.That(s_deferredObserved, Is.EqualTo("alpha"), "Precondition: beta is pending, alpha is committed");

            // Revert the input to the committed alpha, which clears the pending beta
            s_deferredInput = "alpha";
            s_deferredForceSetter.Invoke(s_deferredForceValue + 1);
            mounted.FlushStateForTest();
            Assume.That(s_deferredObserved, Is.EqualTo("alpha"), "Precondition: the input matches the committed value");

            // Act — change the input to beta again
            s_deferredInput = "beta";
            s_deferredForceSetter.Invoke(s_deferredForceValue + 1);
            mounted.FlushStateForTest();

            // Assert — with the stale pending cleared, beta defers again rather than committing immediately
            Assert.That(s_deferredObserved, Is.EqualTo("alpha"),
                "Reverting the input clears the pending value, so a later change defers again");
        }

        [Test]
        public void Given_ADeferredProp_When_TheParentReRendersAndTheTransitionLaneDrains_Then_ItCommitsTheNewValue()
        {
            // Arrange — the deferred input arrives as a prop, so the child re-renders through the parent's
            // inline expansion rather than through a flush of its own
            using var mounted = V.Mount(_root, V.Component(PropParentRender, key: "prop-parent"));
            Assume.That(s_propObserved, Is.EqualTo("alpha"), "Precondition: the child committed the mount value");

            // Act — the parent's state change re-renders the child with the new prop, then the transition
            // lane the child queued during that render drains
            s_propSetQuery.Invoke("beta");
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_propObserved, Is.EqualTo("beta"),
                "A transition lane queued during a subsuming parent render survives to commit the deferred value");
        }

        [Test]
        public void Given_ADeferredProp_When_TwoParentRendersPrecedeTheLaneDraining_Then_ItStillCommitsTheLatestValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PropParentRender, key: "prop-parent"));
            Assume.That(s_propObserved, Is.EqualTo("alpha"), "Precondition: the child committed the mount value");

            // Act — only the parent flushes, so the child's transition lane is still queued when the second
            // parent render asks for it again and that request coalesces onto it
            s_propSetQuery.Invoke("beta");
            FiberWorkLoop.FlushState(s_propParentFiber);
            s_propSetQuery.Invoke("gamma");
            FiberWorkLoop.FlushState(s_propParentFiber);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_propObserved, Is.EqualTo("gamma"),
                "A coalesced re-request survives the settle, so the lane is still there to commit on");
        }

        [Test]
        public void Given_InitialValueDifferentFromValue_When_FirstRender_Then_ReturnsInitialValue()
        {
            // Arrange
            s_initialValueInput = "beta";

            // Act
            using var mounted = V.Mount(_root, V.Component(InitialValueDeferredRender, key: "deferred-initial"));

            // Assert
            Assert.That(s_initialValueObserved, Is.EqualTo("seed"),
                "The initialValue overload returns initialValue on the first render, not the live value");
        }

        [Test]
        public void Given_AFiberUnmounted_When_ItIsMountedAgain_Then_TheInitialValueOverloadReturnsItsInitialValueAgain()
        {
            // Arrange — the Unmount then Mount pair reuses one fiber, so a slot list the unmount leaves
            // behind is what the remount's first render reads at index 0. The transition is flushed first
            // so the surviving slot holds the deferred-toward value rather than the initial one: without
            // that, reusing the slot and taking the first-render branch both answer "seed" and the case
            // decides nothing.
            s_initialValueInput = "beta";
            var fiber = FiberRenderer.CreateRoot(InitialValueDeferredRender);
            FiberRenderer.Mount(fiber, _root);
            FiberWorkLoop.FlushState(fiber);
            Assume.That(s_initialValueObserved, Is.EqualTo("beta"),
                "Precondition: the transition committed, so the slot no longer holds initialValue");
            var rendersBefore = s_initialValueRenderCount;
            FiberRenderer.Unmount(fiber);

            // Act
            FiberRenderer.Mount(fiber, _root);

            // Assert — the render count rides along because the observed value survives the unmount, so a
            // remount that rendered nothing at all would report the first mount's answer as its own.
            Assert.That((s_initialValueRenderCount > rendersBefore, s_initialValueObserved),
                Is.EqualTo((true, "seed")),
                "A remount is a first render, so the initialValue overload returns initialValue again");
        }

        [Test]
        public void Given_InitialValueDifferentFromValue_When_TransitionFlushed_Then_DefersTowardValue()
        {
            // Arrange
            s_initialValueInput = "beta";
            using var mounted = V.Mount(_root, V.Component(InitialValueDeferredRender, key: "deferred-initial-flush"));
            Assume.That(s_initialValueObserved, Is.EqualTo("seed"), "Precondition: the first render committed initialValue");

            // Act — flush the scheduled transition lane
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_initialValueObserved, Is.EqualTo("beta"),
                "The transition flush commits the deferred value toward the live value");
        }

        [Test]
        public void Given_InitialValueEqualToValue_When_FirstRender_Then_CommitsValueWithNoTransition()
        {
            // Arrange
            s_initialValueInput = "seed";

            // Act
            using var mounted = V.Mount(_root, V.Component(InitialValueDeferredRender, key: "deferred-initial-eq"));

            // Assert — no transition lane is scheduled, so the first render is the only render
            Assert.That(s_initialValueRenderCount, Is.EqualTo(1),
                "When initialValue equals value, the first render commits value with no transition");
        }

        #region Deferred component (default comparer)

        private static string s_deferredInput;
        private static string s_deferredObserved;
        private static int s_deferredRenderCount;
        private static System.Action<int> s_deferredForceSetter;
        private static int s_deferredForceValue;

        private static void ResetDeferred()
        {
            s_deferredInput = null;
            s_deferredObserved = null;
            s_deferredRenderCount = 0;
            s_deferredForceSetter = null;
            s_deferredForceValue = 0;
        }

        [Component]
        private static VNode DeferredRender()
        {
            s_deferredRenderCount++;
            // Tick state used to trigger an urgent re-render
            var (tick, setTick) = Hooks.UseState(0);
            s_deferredForceValue = tick;
            s_deferredForceSetter = setTick;
            s_deferredObserved = Hooks.UseDeferredValue(s_deferredInput);
            return V.Label(text: s_deferredObserved ?? string.Empty);
        }

        #endregion

        [Test]
        public void Given_APendingValueUnderAParkedTransitionSlice_When_TheSliceResumes_Then_ItCommitsTheDeferredValue()
        {
            // Arrange — one transition render leaves beta pending on the child
            using var mounted = V.Mount(_root, V.Component(SlicedParentRender, key: "sliced-parent"));
            var parent = s_slicedFiber;
            // Only the parent flushes: a whole-tree flush would drain the child's own transition lane too,
            // committing beta before the parked slice this case is about ever runs
            s_slicedStart.Invoke(() => s_slicedSetQuery.Invoke("beta"));
            FiberWorkLoop.FlushState(parent);
            Assume.That(s_slicedObserved, Is.EqualTo("alpha"), "Precondition: beta is pending, alpha is committed");

            // Act — a second transition render parks on its first slice, so the child is reached only by the
            // resume rather than by the flush's own render
            s_slicedStart.Invoke(() => s_slicedSetTick.Invoke(1));
            parent.FlushStateWithTinyBudgetForTest();
            var parked = parent.HasPendingReconcileWorkForTest();
            parent.DrainTimeSlicedReconcileForTest();

            // Assert — the park travels with the assertion: without one the child is expanded by the flush's
            // own render, where the marker is set for a reason this case does not pin
            Assert.That((parked, s_slicedObserved), Is.EqualTo((true, "beta")),
                "A resumed slice answers the deferred-commit question the same way the flush that parked it did");
        }

        #region Sliced parent (a parked transition reconcile that expands the child on resume)

        private static string s_slicedObserved;
        private static StateUpdater<string> s_slicedSetQuery;
        private static StateUpdater<int> s_slicedSetTick;
        private static TransitionStarter s_slicedStart;
        private static ComponentFiber s_slicedFiber;

        private static void ResetSliced()
        {
            s_slicedObserved = null;
            s_slicedSetQuery = default;
            s_slicedSetTick = default;
            s_slicedStart = default;
            s_slicedFiber = null;
        }

        [Component]
        private static VNode SlicedParentRender()
        {
            s_slicedFiber = FiberAmbientStack.Current;
            var (query, setQuery) = Hooks.UseState("alpha");
            var (tick, setTick) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_slicedSetQuery = setQuery;
            s_slicedSetTick = setTick;
            s_slicedStart = start;
            // A top-level Fragment of host nodes is unwrapped to the flat array, so the fiber's own reconcile
            // takes the time-sliceable fast path — GeneralPathReconciler.NeedsExpansion looks only at this
            // array, and the component is one level below its last entry, so a parked slice reaches it on
            // resume rather than in the pass that parked.
            var rows = new VNode[9];
            for (var i = 0; i < rows.Length - 1; i++)
            {
                rows[i] = V.Label(name: $"row{i}", text: $"{i}-{tick}");
            }
            rows[^1] = V.Div(name: "host-of-child",
                children: new VNode[] { V.Component(SlicedChildRender, query, key: "sliced-child") });
            return V.Fragment(children: rows);
        }

        [Component]
        private static VNode SlicedChildRender(string query)
        {
            s_slicedObserved = Hooks.UseDeferredValue(query);
            return V.Label(text: s_slicedObserved ?? string.Empty);
        }

        #endregion

        #region Deferred prop component (the input arrives from a parent)

        private static string s_propObserved;
        private static StateUpdater<string> s_propSetQuery;
        private static ComponentFiber s_propParentFiber;

        private static void ResetProp()
        {
            s_propObserved = null;
            s_propSetQuery = default;
            s_propParentFiber = null;
        }

        [Component]
        private static VNode PropParentRender()
        {
            s_propParentFiber = FiberAmbientStack.Current;
            var (query, setQuery) = Hooks.UseState("alpha");
            s_propSetQuery = setQuery;
            return V.Div(children: new VNode[] { V.Component(PropChildRender, query, key: "prop-child") });
        }

        [Component]
        private static VNode PropChildRender(string query)
        {
            s_propObserved = Hooks.UseDeferredValue(query);
            return V.Label(text: s_propObserved ?? string.Empty);
        }

        #endregion

        #region InitialValue component (initialValue argument)

        private static string s_initialValueInput;
        private static string s_initialValueObserved;
        private static int s_initialValueRenderCount;

        private static void ResetInitialValue()
        {
            s_initialValueInput = null;
            s_initialValueObserved = null;
            s_initialValueRenderCount = 0;
        }

        [Component]
        private static VNode InitialValueDeferredRender()
        {
            s_initialValueRenderCount++;
            s_initialValueObserved = Hooks.UseDeferredValue(s_initialValueInput, initialValue: "seed");
            return V.Label(text: s_initialValueObserved ?? string.Empty);
        }

        #endregion
    }
}
