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
    /// <item>It is called once the tree has unmounted, its effects cleaned up, with an empty boundary name and the
    /// component stack starting at the component that threw.</item>
    /// <item>An effect cleanup that throws during that unmount is reported after the error the unmount is for, and
    /// those that throw while the handle is disposed are reported once every cleanup has run.</item>
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

        private MountedTree MountThrowerBesideProbe(System.Action<System.Exception, ErrorInfo> onUncaughtError)
            => V.Mount(_root, V.Component(UncaughtErrorRootTests.ThrowerBesideProbeRender, key: "host"),
                new MountOptions { OnUncaughtError = onUncaughtError });

        private static void RenderAnUpdate(MountedTree mounted)
        {
            UncaughtErrorRootTests.s_setTick.Invoke(1);
            mounted.FlushStateForTest();
        }

        [Test]
        public void Given_AnOnUncaughtErrorHandler_When_AChildsUpdateRenderThrows_Then_ItIsHandedNoBoundaryName()
        {
            // Arrange
            using var mounted = MountThrowerBesideProbe((_, info) => _reports.Add($"[{info.ErrorBoundary}]"));
            UncaughtErrorRootTests.s_throws = true;

            // Act
            RenderAnUpdate(mounted);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("[]"));
        }

        [Test]
        public void Given_AnOnUncaughtErrorHandler_When_AChildsUpdateRenderThrows_Then_ItsStackStartsAtTheComponentThatThrew()
        {
            // Arrange
            using var mounted = MountThrowerBesideProbe((_, info) => _reports.Add(info.ComponentStack.Split('\n')[0]));
            UncaughtErrorRootTests.s_throws = true;

            // Act
            RenderAnUpdate(mounted);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("    at UncaughtErrorRootTests.ThrowingRender"));
        }

        [Test]
        public void Given_AnOnUncaughtErrorHandler_When_AChildsUpdateRenderThrows_Then_ASiblingsEffectHasBeenCleanedUpWhenItIsCalled()
        {
            // Arrange
            using var mounted = MountThrowerBesideProbe((_, _) => _reports.Add($"{UncaughtErrorRootTests.s_cleanups}"));
            mounted.FlushEffectsForTest();
            UncaughtErrorRootTests.s_throws = true;

            // Act
            RenderAnUpdate(mounted);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("1"));
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
            RenderAnUpdate(mounted);

            // Assert
            Assert.That(string.Join(" / ", _reports),
                Is.EqualTo($"{UncaughtErrorRootTests.RenderMessage} / {UncaughtErrorRootTests.CleanupMessage}"));
        }

        [Test]
        public void Given_TwoEffectCleanupsThatThrow_When_TheTreeIsDisposed_Then_EachIsReportedOnceBothHaveRun()
        {
            // Arrange — each entry is how many cleanups had run when the handler was called
            var mounted = V.Mount(_root, V.Component(UncaughtErrorRootTests.TwoThrowingCleanupsRender, key: "host"),
                new MountOptions { OnUncaughtError = (_, _) => _reports.Add($"{UncaughtErrorRootTests.s_cleanups}") });
            mounted.FlushEffectsForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("2 / 2"));
        }
    }
}
