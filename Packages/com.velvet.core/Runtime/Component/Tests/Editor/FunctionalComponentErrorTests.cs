using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what happens when a functional component's Render() throws, both with and without an
    /// enclosing Error Boundary.
    /// <list type="bullet">
    /// <item>An exception thrown during Render() of a component with no fallback is caught and emitted via
    /// the log; it is not rethrown to the mount caller.</item>
    /// <item>A throw does not increment the component's render count and does not replace the previously
    /// committed DOM — the prior subtree is retained.</item>
    /// <item>After a throw, a subsequent successful render renders again and increments the render count from
    /// the last successful render.</item>
    /// <item>When a fallback factory registered via <see cref="Hooks.UseFallback"/> itself throws, the
    /// boundary logs the factory failure and bubbles the original child exception up to the next enclosing
    /// boundary, which catches the child's exception (not the fallback's).</item>
    /// <item>A fallback factory receives the original exception thrown by the child, and when it also takes an
    /// <see cref="ErrorInfo"/> it sees a component stack that lists the throwing fiber first and walks up
    /// through ancestors to the catching boundary.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Exceptions
    /// thrown in components without a fallback reach the root path and are emitted via
    /// <c>Debug.LogException</c>, so they are asserted with <see cref="LogAssert"/> rather than
    /// <c>Assert.Throws</c>. Per-region static fields are reset by <c>Reset{Region}()</c> helpers.
    /// </remarks>
    [TestFixture]
    internal sealed class FunctionalComponentErrorTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetThrowing();
        }

        [Test]
        public void Given_ThrowingComponent_When_MountedWithThrow_Then_LogsException()
        {
            // Arrange
            s_throwingShouldThrow = true;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*Test exception"));

            // Act
            using var mounted = V.Mount(_root, V.Component(ThrowingRender, key: "throw"));

            // Assert — LogAssert.Expect verifies the render exception was logged, not rethrown
        }

        [Test]
        public void Given_SuccessfullyMountedComponent_When_NextRenderThrows_Then_PreviousDomIsRetained()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ThrowingRender, key: "throw"));
            Assume.That(_root.childCount, Is.EqualTo(1), "Precondition: the first render committed one child");
            s_throwingShouldThrow = true;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*Test exception"));

            // Act
            s_throwingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(1), "The previous DOM is retained after a render error");
        }

        [Test]
        public void Given_RenderThrows_When_Flushed_Then_RenderCountDoesNotIncrement()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ThrowingRender, key: "throw"));
            Assume.That(s_throwingRenderCount, Is.EqualTo(1), "Precondition: one successful render happened on mount");
            s_throwingShouldThrow = true;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*Test exception"));

            // Act
            s_throwingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_throwingRenderCount, Is.EqualTo(1), "A throw does not increment the render count");
        }

        [Test]
        public void Given_ComponentThatThrewOnMount_When_RecoversAndReRenders_Then_RendersAgain()
        {
            // Arrange
            s_throwingShouldThrow = true;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*Test exception"));
            using var mounted = V.Mount(_root, V.Component(ThrowingRender, key: "throw"));
            Assume.That(s_throwingRenderCount, Is.EqualTo(0), "Precondition: the mount throw left the render count at zero");

            // Act
            s_throwingShouldThrow = false;
            s_throwingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (_root.childCount, s_throwingRenderCount),
                Is.EqualTo((1, 1)),
                "A successful render after an error commits one child and yields a render count of one");
        }

        [Test]
        public void Given_MountedComponent_When_SuccessfullyReRendered_Then_RenderCountIncrements()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ThrowingRender, key: "throw"));
            Assume.That(s_throwingRenderCount, Is.EqualTo(1), "Precondition: one successful render happened on mount");

            // Act
            s_throwingShouldThrow = false;
            s_throwingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_throwingRenderCount, Is.EqualTo(2), "Each successful render increments the render count");
        }

        [Test]
        public void Given_InnerBoundaryFallbackFactoryThrows_When_ChildThrows_Then_OuterBoundaryFallbackFires()
        {
            // Arrange — the inner boundary's fallback factory throws, so its failure is logged and the
            // original child exception bubbles to the outer boundary. The inner failure produces three
            // ordered log lines (factory-threw error, boundary error, the fallback's own exception).
            ResetBubbleUp();
            LogAssert.Expect(LogType.Error, new Regex("FiberRenderer: RenderFallback factory threw"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[ErrorBoundary\] An exception occurred"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*Inner fallback boom"));

            // Act
            using var mounted = V.Mount(_root, V.Component(OuterFallbackEbRender, key: "outer-eb"));

            // Assert
            Assert.That(s_bubbleUpOuterFallbackCount, Is.EqualTo(1),
                "The outer boundary's fallback fires once because the inner boundary re-threw through its own factory");
        }

        [Test]
        public void Given_InnerBoundaryFallbackFactoryThrows_When_ChildThrows_Then_OuterBoundaryCapturesOriginalChildException()
        {
            // Arrange
            ResetBubbleUp();
            LogAssert.Expect(LogType.Error, new Regex("FiberRenderer: RenderFallback factory threw"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[ErrorBoundary\] An exception occurred"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*Inner fallback boom"));

            // Act
            using var mounted = V.Mount(_root, V.Component(OuterFallbackEbRender, key: "outer-eb"));

            // Assert
            Assert.That(
                s_bubbleUpCapturedAtOuter,
                Is.InstanceOf<InvalidOperationException>().And.Message.Contains("Child error"),
                "The outer boundary captures the original child exception, not the inner fallback's exception");
        }

        [Test]
        public void Given_FallbackReceivesErrorInfo_When_ChildThrows_Then_ComponentStackListsThrowingFiberFirst()
        {
            // Arrange
            ResetErrorInfoCapture();
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*Child error"));

            // Act
            using var mounted = V.Mount(_root, V.Component(ErrorInfoCaptureEbRender, key: "info-eb"));
            Assume.That(s_errorInfoLastInfo, Is.Not.Null, "Precondition: the fallback captured an ErrorInfo");

            // Assert
            Assert.That(s_errorInfoLastInfo.ComponentStack, Does.Contain("AlwaysThrowingChildRender"),
                "The component stack lists the throwing fiber as its first line");
        }

        [Test]
        public void Given_FallbackReceivesErrorInfo_When_ChildThrows_Then_ComponentStackWalksUpToCatchingBoundary()
        {
            // Arrange
            ResetErrorInfoCapture();
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*Child error"));

            // Act
            using var mounted = V.Mount(_root, V.Component(ErrorInfoCaptureEbRender, key: "info-eb"));
            Assume.That(s_errorInfoLastInfo, Is.Not.Null, "Precondition: the fallback captured an ErrorInfo");

            // Assert
            Assert.That(s_errorInfoLastInfo.ComponentStack, Does.Contain("ErrorInfoCaptureEbRender"),
                "The component stack walks up the parent chain to include the catching boundary");
        }

        [Test]
        public void Given_FallbackReceivesErrorInfo_When_ChildThrows_Then_FallbackReceivesOriginalChildException()
        {
            // Arrange
            ResetErrorInfoCapture();
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*Child error"));

            // Act
            using var mounted = V.Mount(_root, V.Component(ErrorInfoCaptureEbRender, key: "info-eb"));

            // Assert
            Assert.That(
                s_errorInfoLastException,
                Is.InstanceOf<InvalidOperationException>().And.Message.Contains("Child error"),
                "The fallback factory receives the original exception thrown by the child");
        }

        #region ErrorInfo capture EB

        private static Exception s_errorInfoLastException;
        private static ErrorInfo s_errorInfoLastInfo;

        private static void ResetErrorInfoCapture()
        {
            s_errorInfoLastException = null;
            s_errorInfoLastInfo = null;
        }

        [Component(IsErrorBoundary = true)]
        private static VNode ErrorInfoCaptureEbRender()
        {
            Hooks.UseFallback((ex, info) =>
            {
                s_errorInfoLastException = ex;
                s_errorInfoLastInfo = info;
                // Return null to bubble up so the original exception surfaces at the root log path.
                return null;
            });
            return V.Component(AlwaysThrowingChildRender, key: "throwing-child-info");
        }

        #endregion

        #region BubbleUp components (outer + inner EB + throwing child)

        private static int s_bubbleUpOuterFallbackCount;
        private static Exception s_bubbleUpCapturedAtOuter;

        private static void ResetBubbleUp()
        {
            s_bubbleUpOuterFallbackCount = 0;
            s_bubbleUpCapturedAtOuter = null;
        }

        [Component(IsErrorBoundary = true)]
        private static VNode OuterFallbackEbRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_bubbleUpOuterFallbackCount++;
                s_bubbleUpCapturedAtOuter = ex;
                return V.Label(text: "outer fallback");
            });
            return V.Component(InnerFallbackEbRender, key: "inner-eb");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode InnerFallbackEbRender()
        {
            Hooks.UseFallback(_ => throw new InvalidOperationException("Inner fallback boom"));
            return V.Component(AlwaysThrowingChildRender, key: "throwing-child");
        }

        [Component]
        private static VNode AlwaysThrowingChildRender()
            => throw new InvalidOperationException("Child error");

        #endregion

        #region Throwing component (conditional exception; RenderCount counts only successful renders)

        private static bool s_throwingShouldThrow;
        private static int s_throwingRenderCount;
        private static Action<int> s_throwingSetTick;

        private static void ResetThrowing()
        {
            s_throwingShouldThrow = false;
            s_throwingRenderCount = 0;
            s_throwingSetTick = null;
        }

        [Component]
        private static VNode ThrowingRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_throwingSetTick = setTick;
            if (s_throwingShouldThrow)
                throw new InvalidOperationException("Test exception");
            s_throwingRenderCount++;
            return V.Label(text: "ok");
        }

        #endregion
    }
}
