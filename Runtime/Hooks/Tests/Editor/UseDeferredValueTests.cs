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
