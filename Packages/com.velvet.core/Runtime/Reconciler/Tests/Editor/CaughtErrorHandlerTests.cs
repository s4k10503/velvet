using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="MountOptions.OnCaughtError"/>.
    /// <list type="bullet">
    /// <item>It receives the exception the boundary caught, the boundary's name and the component stack.</item>
    /// <item>It runs once per catch, whether the error came from a render on mount or on update, a layout or
    /// passive effect's setup, an effect cleanup, a ref setup, an element's creation callback, a faulted
    /// resource, or a Suspense retry.</item>
    /// <item>Where a boundary's own fallback content throws and an ancestor boundary catches that, it runs
    /// once, for the ancestor.</item>
    /// <item>An exception it throws is logged, and the mount that reported the catch still completes.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class CaughtErrorHandlerTests
    {
        private const string Reported = "boom | fallback";

        private readonly List<string> _calls = new();
        private VisualElement _root;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _calls.Clear();
            _mounted = null;
            s_setTick = default;
            s_source = null;
        }

        [TearDown]
        public void TearDown() => _mounted?.Dispose();

        private MountedTree Mount(Func<VNode> child)
        {
            s_child = child;
            return V.Mount(
                _root,
                V.Component(BoundaryRender, key: "boundary"),
                new MountOptions((exception, _) => _calls.Add(exception.Message)));
        }

        private string Outcome() => $"{string.Join(", ", _calls)} | {_root.FindFirstLabel()?.text}";

        [Test]
        public void Given_AnOnCaughtErrorHandler_When_ABoundaryCatches_Then_ItReceivesTheCaughtExceptionAndTheComponentStack()
        {
            // Arrange
            var thrown = new InvalidOperationException("boom");
            (Exception Exception, ErrorInfo Info) received = default;
            s_child = () => V.Component(ThrowOnRender, thrown, key: "child");

            // Act
            using var mounted = V.Mount(
                _root,
                V.Component(BoundaryRender, key: "boundary"),
                new MountOptions((exception, info) => received = (exception, info)));

            // Assert
            Assert.That(
                $"{ReferenceEquals(received.Exception, thrown)} | {received.Info?.ErrorBoundary}"
                + $" | {received.Info?.ComponentStack.Split('\n')[0]}",
                Is.EqualTo("True | CaughtErrorHandlerTests.BoundaryRender |     at CaughtErrorHandlerTests.ThrowOnRender"));
        }

        [Test]
        public void Given_AChildWhoseRenderThrowsOnMount_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Act
            using var mounted = Mount(() => V.Component(ThrowOnRender, new InvalidOperationException("boom"), key: "child"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhoseRenderThrowsOnUpdate_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Arrange
            using var mounted = Mount(() => V.Component(ThrowOnUpdateRender, key: "child"));

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhoseLayoutEffectThrows_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Act
            using var mounted = Mount(() => V.Component(ThrowInLayoutEffectRender, key: "child"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhosePassiveEffectThrows_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Arrange
            using var mounted = Mount(() => V.Component(ThrowInPassiveEffectRender, key: "child"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhoseEffectCleanupThrows_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Arrange
            using var mounted = Mount(() => V.Component(ThrowInCleanupRender, key: "child"));

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhoseRefSetupThrows_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Act
            using var mounted = Mount(() => V.Component(ThrowInRefRender, key: "child"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhoseElementCreationCallbackThrows_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Act
            using var mounted = Mount(() => V.Component(ThrowInOnCreatedRender, key: "child"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildReadingAFaultedResource_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Arrange
            s_source = new VelvetTaskCompletionSource<string>();
            s_source.TrySetException(new InvalidOperationException("boom"));

            // Act
            using var mounted = Mount(() => V.Component(ReadResourceRender, key: "child"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_ASuspendedChildWhoseResourceFaults_When_TheRetryThrowsIntoTheBoundary_Then_TheHandlerRunsOnce()
        {
            // Arrange
            s_source = new VelvetTaskCompletionSource<string>();
            using var mounted = Mount(() => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Component(ReadResourceRender, key: "child") }));

            // Act
            s_source.TrySetException(new InvalidOperationException("boom"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_ABoundaryWhoseFallbackContentThrows_When_AnAncestorBoundaryCatchesThat_Then_TheHandlerRunsOnceForTheAncestor()
        {
            // Act
            using var mounted = Mount(() => V.Component(InnerBoundaryRender, key: "inner"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo("fallback content boom | fallback"));
        }

        [Test]
        public void Given_AnOnCaughtErrorHandlerThatThrows_When_ABoundaryCatchesALayoutEffectsError_Then_TheMountCompletesWithTheFallback()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: handler boom");
            s_child = () => V.Component(ThrowInLayoutEffectRender, key: "child");

            // Act
            try
            {
                _mounted = V.Mount(
                    _root,
                    V.Component(BoundaryRender, key: "boundary"),
                    new MountOptions((_, _) => throw new InvalidOperationException("handler boom")));
            }
            catch (InvalidOperationException)
            {
            }

            // Assert
            Assert.That($"{_mounted != null} | {_root.FindFirstLabel()?.text}", Is.EqualTo("True | fallback"));
        }

        [Test]
        public void Given_NullOptions_When_Mounted_Then_ItThrowsArgumentNullException()
        {
            // Act
            TestDelegate mount = () => V.Mount(_root, V.Label(text: "ok"), null);

            // Assert
            Assert.That(mount, Throws.ArgumentNullException);
        }

        private static Func<VNode> s_child;
        private static StateUpdater<int> s_setTick;
        private static VelvetTaskCompletionSource<string> s_source;

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return s_child();
        }

        [Component]
        private static VNode ThrowOnRender(Exception exception) => throw exception;

        [Component]
        private static VNode ThrowOnUpdateRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            if (tick > 0) throw new InvalidOperationException("boom");
            return V.Label(text: "ok");
        }

        [Component]
        private static VNode ThrowInLayoutEffectRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() => throw new InvalidOperationException("boom")), Array.Empty<object>());
            return V.Label(text: "ok");
        }

        [Component]
        private static VNode ThrowInPassiveEffectRender()
        {
            Hooks.UseEffect((Func<Action>)(() => throw new InvalidOperationException("boom")), Array.Empty<object>());
            return V.Label(text: "ok");
        }

        [Component]
        private static VNode ThrowInCleanupRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseLayoutEffect(() => () => throw new InvalidOperationException("boom"), new object[] { tick });
            return V.Label(text: "ok");
        }

        [Component]
        private static VNode ThrowInRefRender()
            => V.Label(text: "ok", refCallback: _ => throw new InvalidOperationException("boom"));

        [Component]
        private static VNode ThrowInOnCreatedRender()
            => V.ScrollView(onCreated: _ => throw new InvalidOperationException("boom"));

        [Component]
        private static VNode ReadResourceRender() => V.Label(text: Hooks.Use(() => s_source.Task));

        [Component(IsErrorBoundary = true)]
        private static VNode InnerBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Component(ThrowOnRender, new InvalidOperationException("fallback content boom"),
                key: "inner-fallback"));
            return V.Component(ThrowOnRender, new InvalidOperationException("boom"), key: "child");
        }
    }
}
