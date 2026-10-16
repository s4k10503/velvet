using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="MountOptions.OnUncaughtError"/>, over the components <see cref="UncaughtErrorRootTests"/>
    /// declares.
    /// <list type="bullet">
    /// <item>It is called once the tree has unmounted, with the error, an empty boundary name and the component stack
    /// starting at the component that threw.</item>
    /// <item>An effect cleanup that throws during that unmount is reported after the error the unmount is for, and
    /// one that throws while the handle is disposed is reported too.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class UncaughtErrorHandlerTests
    {
        private readonly List<string> _reports = new();
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _reports.Clear();
            UncaughtErrorRootTests.ResetComponents();
        }

        [Test]
        public void Given_AnOnUncaughtErrorHandler_When_AChildsUpdateRenderThrows_Then_ItIsHandedTheErrorNoBoundaryAndTheStack()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(UncaughtErrorRootTests.ThrowerBesideProbeRender, key: "host"),
                new MountOptions
                {
                    OnUncaughtError = (exception, info) => _reports.Add(
                        $"{exception.Message} | {info.ErrorBoundary} | {info.ComponentStack.Split('\n')[0]}"),
                });
            UncaughtErrorRootTests.s_throws = true;

            // Act
            UncaughtErrorRootTests.s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(" / ", _reports),
                Is.EqualTo($"{UncaughtErrorRootTests.RenderMessage} |  |     at UncaughtErrorRootTests.ThrowingRender"));
        }

        [Test]
        public void Given_AnOnUncaughtErrorHandler_When_AChildsUpdateRenderThrows_Then_TheTreeHasUnmountedWhenItIsCalled()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(UncaughtErrorRootTests.ThrowerBesideProbeRender, key: "host"),
                new MountOptions
                {
                    OnUncaughtError = (_, _) =>
                        _reports.Add($"{_root.childCount} children, {UncaughtErrorRootTests.s_cleanups} cleanups"),
                });
            mounted.FlushEffectsForTest();
            UncaughtErrorRootTests.s_throws = true;

            // Act
            UncaughtErrorRootTests.s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("0 children, 1 cleanups"));
        }

        [Test]
        public void Given_ACleanupThatThrowsWhileTheTreeUnmounts_When_AChildsUpdateRenderThrows_Then_TheRenderErrorIsReportedFirst()
        {
            // Arrange
            using var mounted = V.Mount(_root,
                V.Component(UncaughtErrorRootTests.ThrowerBesideThrowingCleanupRender, key: "host"),
                new MountOptions { OnUncaughtError = (exception, _) => _reports.Add(exception.Message) });
            mounted.FlushEffectsForTest();
            UncaughtErrorRootTests.s_throws = true;

            // Act
            UncaughtErrorRootTests.s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(" / ", _reports),
                Is.EqualTo($"{UncaughtErrorRootTests.RenderMessage} / {UncaughtErrorRootTests.CleanupMessage}"));
        }

        [Test]
        public void Given_AnEffectCleanupThatThrows_When_TheTreeIsDisposed_Then_TheHandlerIsHandedIt()
        {
            // Arrange — the component is the root's own child, so the walk from it reaches the root directly
            var mounted = V.Mount(_root, V.Component(UncaughtErrorRootTests.ThrowingCleanupRender, key: "cleanup"),
                new MountOptions { OnUncaughtError = (exception, _) => _reports.Add(exception.Message) });
            mounted.FlushEffectsForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo(UncaughtErrorRootTests.CleanupMessage));
        }
    }
}
