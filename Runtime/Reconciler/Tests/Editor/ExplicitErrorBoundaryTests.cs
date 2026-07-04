using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of a function-component Error Boundary, declared as
    /// <c>[Component(IsErrorBoundary = true)]</c> with a fallback factory registered inside Render via
    /// <see cref="Hooks.UseFallback(System.Func{System.Exception, VNode})"/>.
    /// <list type="bullet">
    /// <item>A render exception propagates only to ancestor boundaries; the throwing fiber's own enclosing
    /// boundary is the nearest ancestor that opted in via <c>IsErrorBoundary = true</c>.</item>
    /// <item>When an ancestor boundary catches a child exception, its registered fallback factory runs and
    /// receives the caught exception, and the fallback VNode replaces the boundary's subtree.</item>
    /// <item>A boundary never catches an exception thrown by its own Render; that exception bubbles to an
    /// enclosing boundary instead, so the boundary's own fallback factory does not run.</item>
    /// <item>A boundary that does not register a fallback factory produces no fallback and lets the exception
    /// bubble to an enclosing boundary.</item>
    /// <item>When no enclosing boundary catches the exception, it is logged as an unhandled exception.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. The fixture's
    /// static observation fields are reset together in <see cref="SetUp"/> via <see cref="ResetBoundaryState"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class ExplicitErrorBoundaryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetBoundaryState();
        }

        [Test]
        public void Given_NoEnclosingBoundary_When_ComponentRenderThrows_Then_LogsException()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "Exception: boom");

            // Act
            using var mounted = V.Mount(_root, V.Component(ThrowingParentRender, key: "parent"));

            // Assert — LogAssert.Expect verifies the unhandled exception was logged
        }

        [Test]
        public void Given_BoundaryWrappingThrowingChild_When_ChildRenderThrows_Then_FallbackFactoryRuns()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(BoundaryWrappingThrowerRender, key: "boundary"));

            // Assert
            Assert.That(s_fallbackShown, Is.True,
                "The factory registered via Hooks.UseFallback at the boundary fires on a child exception");
        }

        [Test]
        public void Given_BoundaryWrappingThrowingChild_When_ChildRenderThrows_Then_FactoryReceivesCaughtException()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(BoundaryWrappingThrowerRender, key: "boundary"));
            Assume.That(s_fallbackShown, Is.True, "Precondition: the boundary caught the child exception");

            // Assert
            Assert.That(s_lastCaughtMessage, Is.EqualTo("boom-child"),
                "The fallback factory receives the exact exception thrown by the child render");
        }

        [Test]
        public void Given_BoundaryWhoseOwnRenderThrows_When_Mounted_Then_LogsException()
        {
            // Arrange — without an enclosing boundary, the un-self-caught exception is logged
            LogAssert.Expect(LogType.Exception, "Exception: self-boom");

            // Act
            using var mounted = V.Mount(_root, V.Component(SelfThrowingBoundaryRender, key: "self-throw"));

            // Assert — LogAssert.Expect verifies the own-Render exception was not self-caught but logged
        }

        [Test]
        public void Given_BoundaryWhoseOwnRenderThrows_When_Mounted_Then_OwnFallbackFactoryDoesNotRun()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "Exception: self-boom");

            // Act
            using var mounted = V.Mount(_root, V.Component(SelfThrowingBoundaryRender, key: "self-throw"));

            // Assert
            Assert.That(s_fallbackShown, Is.False,
                "A boundary does not catch an exception thrown by its own Render");
        }

        [Test]
        public void Given_BoundaryWithoutFallback_When_ChildRenderThrows_Then_ExceptionBubblesAndIsLogged()
        {
            // Arrange — the boundary opts in but registers no fallback factory, so the exception bubbles
            // past it to an enclosing boundary; with none present it is logged as unhandled
            LogAssert.Expect(LogType.Exception, "Exception: boom-child");

            // Act
            using var mounted = V.Mount(_root, V.Component(NoFallbackBoundaryRender, key: "no-fallback"));

            // Assert — LogAssert.Expect verifies the un-caught child exception was logged
        }

        #region Boundary observation state

        private static bool s_fallbackShown;
        private static string s_lastCaughtMessage;

        private static void ResetBoundaryState()
        {
            s_fallbackShown = false;
            s_lastCaughtMessage = null;
        }

        #endregion

        #region ThrowingParent component (no boundary; its own Render throws)

        [Component]
        private static VNode ThrowingParentRender() => throw new Exception("boom");

        #endregion

        #region BoundaryWrappingThrower component (boundary + Hooks.UseFallback wrapping a throwing child)

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryWrappingThrowerRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_fallbackShown = true;
                s_lastCaughtMessage = ex.Message;
                return V.Label(text: "caught");
            });
            return V.Fragment(new VNode[] { V.Component(ThrowingChildRender, key: "throwing-child") });
        }

        [Component]
        private static VNode ThrowingChildRender() => throw new Exception("boom-child");

        #endregion

        #region SelfThrowingBoundary component (boundary whose own Render throws)

        [Component(IsErrorBoundary = true)]
        private static VNode SelfThrowingBoundaryRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_fallbackShown = true;
                return V.Label(text: "should-not-self-catch");
            });
            throw new Exception("self-boom");
        }

        #endregion

        #region NoFallbackBoundary component (boundary opt-in but no Hooks.UseFallback call)

        [Component(IsErrorBoundary = true)]
        private static VNode NoFallbackBoundaryRender()
            => V.Fragment(new VNode[] { V.Component(ThrowingChildRender, key: "throwing-child") });

        #endregion
    }
}
