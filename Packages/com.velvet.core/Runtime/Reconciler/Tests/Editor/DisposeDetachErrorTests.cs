using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where an error goes that an element's panel-detach callback throws while <see cref="MountedTree.Dispose"/>
    /// removes the tree's rows: to <see cref="MountOptions.OnUncaughtError"/> once the unmount is done, with
    /// <c>Dispose</c> returning, as React's <c>root.unmount()</c> captures what a removal throws at the root and reports
    /// it after the unmount commits. Two elements' errors are each reported, as are an element's and an effect
    /// cleanup's, and so is one thrown where the disposal waited for another root's commit to end. A boundary the
    /// disposal tears down catches none of them.
    /// </summary>
    /// <remarks>
    /// The tree mounts on a real panel, which is what runs a detach callback.
    /// </remarks>
    [TestFixture]
    internal sealed class DisposeDetachErrorTests
    {
        private HeadlessEditorPanelHost _host;
        private VisualElement _mountPoint;
        private readonly List<string> _reports = new();
        private static int s_detached;
        private static MountedTree s_disposedByAnotherRoot;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _mountPoint = new VisualElement();
            _host.Root.Add(_mountPoint);
            _reports.Clear();
            s_detached = 0;
            s_disposedByAnotherRoot = null;
        }

        [TearDown]
        public void TearDown()
        {
            s_disposedByAnotherRoot = null;
            _host?.Dispose();
            _host = null;
        }

        // Stands in for a V.Custom<T> whose element registers a panel-detach callback that throws.
        internal sealed class DetachThrower : VisualElement
        {
            public DetachThrower()
            {
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    s_detached++;
                    throw new InvalidOperationException("detach:" + name);
                });
            }
        }

        [Component(Compiler = false)]
        private static VNode LogsItsCleanup()
        {
            var log = EventLog.UseLog();
            Hooks.UseEffect((Func<Action>)(() => () => log.Add("eff-x:C")), Array.Empty<object>());
            return V.Div();
        }

        [Component(Compiler = false)]
        private static VNode ThrowsFromItsCleanup()
        {
            Hooks.UseEffect((Func<Action>)(() => () => throw new InvalidOperationException("cleanup:C")),
                Array.Empty<object>());
            return V.Div();
        }

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "B")]
        private static VNode CatchesAroundAThrower()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Custom<DetachThrower>(name: "X", key: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode DisposesAnotherRoot()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_disposedByAnotherRoot.Dispose();
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div();
        }

        // What Dispose throws is recorded as an entry, so a throw fails the case at its one assertion.
        private static void DisposeRecordingWhatItThrows(MountedTree mounted, Action<string> record)
        {
            try
            {
                mounted.Dispose();
            }
            catch (Exception exception)
            {
                record("threw:" + exception.Message);
            }
        }

        [Test]
        public void Given_ATreeWhoseElementThrowsAsItDetachesBesideAComponentWhoseEffectCleanupLogs_When_TheHandleIsDisposed_Then_TheCleanupRunsAndTheRootReportsTheErrorOnceAfterIt()
        {
            // Arrange
            var log = new EventLog();
            var mounted = V.Mount(_mountPoint, log.Provide(V.Fragment(
                V.Custom<DetachThrower>(name: "X", key: "thrower"),
                V.Component(LogsItsCleanup, key: "logs"))), log.ReportingErrors());
            mounted.FlushEffectsForTest();

            // Act
            DisposeRecordingWhatItThrows(mounted, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:C uncaught:detach:X"));
        }

        // Equivalent rather than equal: which of the two rows detaches first is not what this pins.
        [Test]
        public void Given_TwoElementsThatThrowAsTheyDetach_When_TheHandleIsDisposed_Then_EachErrorIsReportedOnceBothHaveDetached()
        {
            // Arrange — each report carries how many detach callbacks had run when the handler was called
            var mounted = V.Mount(_mountPoint, V.Fragment(
                    V.Custom<DetachThrower>(name: "X", key: "x"),
                    V.Custom<DetachThrower>(name: "Y", key: "y")),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message + "@" + s_detached) });

            // Act
            DisposeRecordingWhatItThrows(mounted, _reports.Add);

            // Assert
            Assert.That(_reports, Is.EquivalentTo(new[] { "detach:X@2", "detach:Y@2" }));
        }

        // Equivalent rather than equal: the order of the two reports is not what this pins.
        [Test]
        public void Given_AnElementThatThrowsAsItDetachesBesideAComponentWhoseEffectCleanupThrows_When_TheHandleIsDisposed_Then_BothErrorsAreReported()
        {
            // Arrange
            var mounted = V.Mount(_mountPoint, V.Fragment(
                    V.Custom<DetachThrower>(name: "X", key: "thrower"),
                    V.Component(ThrowsFromItsCleanup, key: "cleanup")),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });
            mounted.FlushEffectsForTest();

            // Act
            DisposeRecordingWhatItThrows(mounted, _reports.Add);

            // Assert
            Assert.That(_reports, Is.EquivalentTo(new[] { "cleanup:C", "detach:X" }));
        }

        [Test]
        public void Given_ABoundaryAboveAnElementThatThrowsAsItDetaches_When_TheHandleIsDisposed_Then_TheRootReportsTheErrorAndTheBoundaryCatchesNothing()
        {
            // Arrange
            var log = new EventLog();
            var mounted = V.Mount(_mountPoint, log.Provide(V.Component(CatchesAroundAThrower, key: "boundary")),
                log.ReportingErrors());

            // Act
            DisposeRecordingWhatItThrows(mounted, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("uncaught:detach:X"));
        }

        [Test]
        public void Given_ATreeWhoseElementThrowsAsItDetaches_When_ALayoutEffectOfAnotherRootDisposesItsHandle_Then_TheRootReportsTheError()
        {
            // Arrange
            s_disposedByAnotherRoot = V.Mount(_mountPoint, V.Custom<DetachThrower>(name: "X", key: "thrower"),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });

            // Act
            using var other = V.Mount(new VisualElement(), V.Component(DisposesAnotherRoot));

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("detach:X"));
        }
    }
}
