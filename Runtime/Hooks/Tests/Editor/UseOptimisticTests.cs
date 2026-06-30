using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseOptimistic{TState,TAction}"/> in a function component.
    /// <list type="bullet">
    /// <item>With no optimistic update outstanding, the returned state equals the pass-through state.</item>
    /// <item>Invoking addOptimistic derives the optimistic state via the apply function and shows it immediately.</item>
    /// <item>Successive addOptimistic calls compose on top of the previous optimistic value.</item>
    /// <item>When the pass-through state changes, the optimistic override is discarded and the new pass-through state is shown.</item>
    /// <item>A null apply function raises an <see cref="ArgumentNullException"/>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. A sibling
    /// UseState setter forces an urgent re-render when the pass-through state changes. Per-region static fields
    /// are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseOptimisticTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetOptimistic();
        }

        [Test]
        public void Given_NoOptimisticUpdate_When_FirstRender_Then_ReturnsPassthroughState()
        {
            // Arrange
            s_passthrough = "base";

            // Act
            using var mounted = V.Mount(_root, V.Component(OptimisticRender, key: "optimistic-init"));

            // Assert
            Assert.That(s_observed, Is.EqualTo("base"),
                "With no optimistic update outstanding, the pass-through state is returned");
        }

        [Test]
        public void Given_MountedComponent_When_AddOptimisticInvoked_Then_OptimisticStateIsShownImmediately()
        {
            // Arrange
            s_passthrough = "base";
            using var mounted = V.Mount(_root, V.Component(OptimisticRender, key: "optimistic-add"));

            // Act
            s_addOptimistic.Invoke("pending");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_observed, Is.EqualTo("base+pending"),
                "addOptimistic derives the optimistic state via the apply function and shows it");
        }

        [Test]
        public void Given_OptimisticState_When_AddOptimisticInvokedAgain_Then_ComposesOnPreviousValue()
        {
            // Arrange
            s_passthrough = "base";
            using var mounted = V.Mount(_root, V.Component(OptimisticRender, key: "optimistic-compose"));

            // Act
            s_addOptimistic.Invoke("a");
            mounted.FlushStateForTest();
            s_addOptimistic.Invoke("b");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_observed, Is.EqualTo("base+a+b"),
                "Successive addOptimistic calls compose on top of the previous optimistic value");
        }

        [Test]
        public void Given_OutstandingOptimisticState_When_PassthroughStateChanges_Then_OverrideIsDiscarded()
        {
            // Arrange
            s_passthrough = "base";
            using var mounted = V.Mount(_root, V.Component(OptimisticRender, key: "optimistic-reset"));
            s_addOptimistic.Invoke("pending");
            mounted.FlushStateForTest();
            Assume.That(s_observed, Is.EqualTo("base+pending"), "Precondition: the optimistic override is applied");

            // Act — the real update lands, changing the pass-through state
            s_passthrough = "committed";
            s_forceSetter.Invoke(s_forceValue + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_observed, Is.EqualTo("committed"),
                "When the pass-through state changes, the optimistic override is dropped");
        }

        [Test]
        public void Given_NullApplyFunction_When_UseOptimisticCalled_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => Hooks.UseOptimistic<string, string>("x", null));
        }

        #region Optimistic component

        private static string s_passthrough;
        private static string s_observed;
        private static Action<string> s_addOptimistic;
        private static int s_forceValue;
        private static Action<int> s_forceSetter;

        private static void ResetOptimistic()
        {
            s_passthrough = null;
            s_observed = null;
            s_addOptimistic = null;
            s_forceValue = 0;
            s_forceSetter = null;
        }

        [Component]
        private static VNode OptimisticRender()
        {
            // A tick used to force an urgent re-render when the pass-through state changes.
            var (tick, setTick) = Hooks.UseState(0);
            s_forceValue = tick;
            s_forceSetter = setTick;

            var (optimisticState, addOptimistic) = Hooks.UseOptimistic<string, string>(
                s_passthrough,
                (current, action) => current + "+" + action);
            s_observed = optimisticState;
            s_addOptimistic = addOptimistic;
            return V.Label(text: optimisticState ?? string.Empty);
        }

        #endregion
    }
}
