using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the Error Boundary and props-bail flags coexist on one component:
    /// <c>[Component(IsErrorBoundary = true, Memoize = true)]</c>.
    /// <list type="bullet">
    /// <item>The metadata weaver registers the same method as both an Error Boundary and a props-bail gate.</item>
    /// <item>At runtime the boundary still catches a child render exception and shows its fallback, and the
    /// fallback receives the thrown exception (its message is observable).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The boundary body calls <c>UseFallback</c>, which is not memo-safe, so the inner auto-memoization weaver
    /// leaves the component unwoven; the boundary behavior is unaffected by the props-bail flag.
    /// </remarks>
    [TestFixture]
    internal sealed class MemoizeErrorBoundaryComboTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ComboState.Reset();
        }

        [Test]
        public void Given_BoundaryWithMemoize_When_ChildThrows_Then_FallbackFires()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CombinedBoundary.Render, key: "boundary"));
            Assume.That(ComboState.FallbackShown, Is.False, "Precondition: the normal child rendered without firing fallback");
            Assume.That(ComboState.SetTick, Is.Not.Null, "Precondition: the child rendered and wired SetTick");

            // Act
            ComboState.ThrowOnNextRender = true;
            ComboState.SetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(ComboState.FallbackShown, Is.True,
                "The boundary still catches the child exception even with the props-bail flag set");
        }

        [Test]
        public void Given_BoundaryWithMemoize_When_ChildThrows_Then_FallbackReceivesTheThrownException()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CombinedBoundary.Render, key: "boundary"));
            Assume.That(ComboState.SetTick, Is.Not.Null, "Precondition: the child rendered and wired SetTick");

            // Act
            ComboState.ThrowOnNextRender = true;
            ComboState.SetTick.Invoke(1);
            mounted.FlushStateForTest();
            Assume.That(ComboState.FallbackShown, Is.True, "Precondition: the fallback fired");

            // Assert
            Assert.That(ComboState.LastCaughtMessage, Is.EqualTo("Combo throw"),
                "The fallback receives the exact exception thrown by the child");
        }
    }

    internal static class ComboState
    {
        public static bool FallbackShown;
        public static string LastCaughtMessage;
        public static bool ThrowOnNextRender;
        public static Action<int> SetTick;

        public static void Reset()
        {
            FallbackShown = false;
            LastCaughtMessage = null;
            ThrowOnNextRender = false;
            SetTick = null;
        }
    }

    internal static class CombinedBoundary
    {
        [Component(IsErrorBoundary = true, Memoize = true)]
        public static VNode Render()
        {
            Hooks.UseFallback(ex =>
            {
                ComboState.FallbackShown = true;
                ComboState.LastCaughtMessage = ex.Message;
                return V.Label(text: "error");
            });
            return V.Component(ComboChildRenderer.Render, key: "child");
        }
    }

    internal static class ComboChildRenderer
    {
        [Component]
        public static VNode Render()
        {
            var (_, setTick) = Hooks.UseState(0);
            ComboState.SetTick = setTick;
            if (ComboState.ThrowOnNextRender) throw new InvalidOperationException("Combo throw");
            return V.Label(text: "ok");
        }
    }
}
