using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the core hooks behave correctly through the functional component
    /// (<c>[Component] static VNode</c>) path.
    /// <list type="bullet">
    /// <item><see cref="Hooks.UseState"/> retains its slot value across re-renders, returning the most
    /// recent setter value, and each setter call drives exactly one re-render.</item>
    /// <item><see cref="Hooks.UseLayoutEffect"/> with empty deps runs its effect once at mount and its
    /// cleanup once at unmount.</item>
    /// <item><see cref="Hooks.UseRef"/> returns the same ref instance across re-renders of one mount; a fresh
    /// mount uses a different fiber and a different ref instance, re-running the initializer.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Each test
    /// resets the static fields of the component it exercises in its Arrange step.
    /// </remarks>
    [TestFixture]
    internal sealed class FunctionalComponentRegressionTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        #region Counter component (UseState persistence)

        private static int s_renderCount;
        private static int s_lastValue;
        private static Action<int> s_setter;

        [Component]
        public static VNode CounterRender()
        {
            s_renderCount++;
            var (value, setValue) = Hooks.UseState(0);
            s_lastValue = value;
            s_setter = setValue;
            return V.Label(text: value.ToString());
        }

        private static void ResetCounter()
        {
            s_renderCount = 0;
            s_lastValue = -1;
            s_setter = null;
        }

        [Test]
        public void Given_MountedComponent_When_FirstRender_Then_ReturnsInitialValue()
        {
            // Arrange
            ResetCounter();

            // Act
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "counter"));

            // Assert
            Assert.That(s_lastValue, Is.EqualTo(0), "The first render returns the initial value");
        }

        [Test]
        public void Given_MountedComponent_When_SetterInvoked_Then_NextRenderReturnsUpdatedValue()
        {
            // Arrange
            ResetCounter();
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "counter"));

            // Act
            s_setter(42);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_lastValue, Is.EqualTo(42), "The slot retains its value and returns the setter's value on re-render");
        }

        [Test]
        public void Given_MountedComponent_When_SetterInvokedTwice_Then_SecondValueIsRetainedAcrossRenders()
        {
            // Arrange
            ResetCounter();
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "counter"));
            s_setter(42);
            mounted.FlushStateForTest();
            Assume.That(s_lastValue, Is.EqualTo(42), "Precondition: the first update took effect");

            // Act
            s_setter(100);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_lastValue, Is.EqualTo(100), "The slot identity is preserved, so a later setter value is retained");
        }

        [Test]
        public void Given_MountedComponent_When_SetterInvoked_Then_ReRendersExactlyOnce()
        {
            // Arrange
            ResetCounter();
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "counter"));
            Assume.That(s_renderCount, Is.EqualTo(1), "Precondition: the first render ran once");

            // Act
            s_setter(42);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount, Is.EqualTo(2), "Each setter call drives exactly one re-render");
        }

        #endregion

        #region Effect component (UseLayoutEffect mount / cleanup)

        private static int s_effectRunCount;
        private static int s_effectCleanupCount;

        [Component]
        public static VNode EffectRender()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_effectRunCount++;
                return () => s_effectCleanupCount++;
            }, Array.Empty<object>());
            return V.Label(text: "effect");
        }

        [Test]
        public void Given_ComponentWithLayoutEffect_When_Mounted_Then_EffectRunsOnce()
        {
            // Arrange
            s_effectRunCount = 0;
            s_effectCleanupCount = 0;

            // Act
            using var mounted = V.Mount(_root, V.Component(EffectRender, key: "effect"));

            // Assert
            Assert.That(s_effectRunCount, Is.EqualTo(1), "UseLayoutEffect with empty deps runs once at mount");
        }

        [Test]
        public void Given_MountedComponentWithLayoutEffect_When_Unmounted_Then_CleanupRunsOnce()
        {
            // Arrange
            s_effectRunCount = 0;
            s_effectCleanupCount = 0;
            var mounted = V.Mount(_root, V.Component(EffectRender, key: "effect"));
            Assume.That(s_effectCleanupCount, Is.EqualTo(0), "Precondition: cleanup has not run before unmount");

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_effectCleanupCount, Is.EqualTo(1), "The effect cleanup runs once at unmount");
        }

        #endregion

        #region Ref component (UseRef identity)

        private static Ref<Box> s_capturedRef;

        public sealed class Box { public int Value; }

        [Component]
        public static VNode RefRender()
        {
            s_capturedRef = Hooks.UseRef(() => new Box { Value = 7 });
            return V.Label(text: s_capturedRef.Current!.Value.ToString());
        }

        [Test]
        public void Given_MountedComponent_When_ReRendered_Then_ReturnsSameRefInstance()
        {
            // Arrange
            s_capturedRef = null;
            using var mounted = V.Mount(_root, V.Component(RefRender, key: "ref"));
            var firstRef = s_capturedRef;
            Assume.That(firstRef, Is.Not.Null, "Precondition: the first render produced a ref");

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_capturedRef, Is.SameAs(firstRef), "UseRef returns the same instance across re-renders of one mount");
        }

        [Test]
        public void Given_FirstMountDisposed_When_RemountedAtSameKey_Then_UsesADifferentRefInstance()
        {
            // Arrange
            s_capturedRef = null;
            var first = V.Mount(_root, V.Component(RefRender, key: "ref"));
            var firstRef = s_capturedRef;
            first.Dispose();

            // Act
            using var second = V.Mount(_root, V.Component(RefRender, key: "ref"));

            // Assert
            Assert.That(s_capturedRef, Is.Not.SameAs(firstRef), "A fresh mount uses a different fiber and a different ref instance");
        }

        [Test]
        public void Given_FirstMountDisposed_When_RemountedAtSameKey_Then_InitializerRunsAgain()
        {
            // Arrange
            s_capturedRef = null;
            var first = V.Mount(_root, V.Component(RefRender, key: "ref"));
            first.Dispose();

            // Act
            using var second = V.Mount(_root, V.Component(RefRender, key: "ref"));

            // Assert
            Assert.That(s_capturedRef!.Current!.Value, Is.EqualTo(7), "The fresh mount re-runs the initializer, producing the initial value");
        }

        #endregion
    }
}
