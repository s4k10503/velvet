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
    /// Pins where an error goes that the panel-detach callback of an element inside a <c>V.WorldSpace</c> throws as the
    /// <c>V.WorldSpace</c> is removed: where a drain's update removes it, the boundary above the component whose render
    /// removed it catches each element's error once, as React captures what each host removal of a deletion throws from
    /// the nearest mounted ancestor, and where <see cref="MountedTree.Dispose"/> removes it, the root reports each
    /// once.
    /// That holds where the deletion waits for the pass to apply its recorded writes, where the walk takes the
    /// <c>V.WorldSpace</c> down as it removes it, and for a <c>z-*</c> element, which leaves the host's root once those
    /// writes have applied. Nothing reaches the log.
    /// </summary>
    /// <remarks>
    /// The tree mounts on a real panel, and the world-space host builds a panel of its own, which is what runs a detach
    /// callback.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldSpaceHostRemovalTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private readonly List<string> _reports = new();
        private static bool s_armed;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _reports.Clear();
            s_armed = true;
        }

        // Disarms the throwers first: where a case leaves one on a panel, as a failing case can, the disposals below
        // detach it, and a throw there would fail the case for something it does not assert.
        [TearDown]
        public void TearDown()
        {
            s_armed = false;
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        // Stands in for a V.Custom<T> whose element registers a panel-detach callback, which throws on every call. Its
        // message says whether the element attached as a relocated z-* element, so the case about one fails where the
        // arrangement stops making one, and numbers every call after the first, so a callback run twice reads as two
        // messages.
        internal sealed class DetachThrower : VisualElement
        {
            private string _arranged = "";
            private int _calls;

            public DetachThrower()
            {
                RegisterCallback<AttachToPanelEvent>(_ =>
                    _arranged = FiberZLayerCoordinator.IsRelocatedReal(this) ? ":z" : "");
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    if (!s_armed) return;
                    _calls++;
                    throw new InvalidOperationException(
                        "detach:" + name + _arranged + (_calls > 1 ? "#" + _calls : ""));
                });
            }
        }

        private sealed record Rows(string ClassName, string[] Names);

        private sealed record Holds(VNode Child);

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "B")]
        private static VNode Boundary(Holds props)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return props.Child;
        }

        private static VNode AWorldSpaceOf(Rows rows) => V.WorldSpace(Vector3.zero, key: "ws",
            children: Array.ConvertAll(rows.Names,
                name => (VNode)V.Custom<DetachThrower>(className: rows.ClassName, name: name, key: name)));

        // The null child takes the general walk, whose deletion waits for the pass to apply its recorded writes.
        [Component(Compiler = false)]
        private static VNode RemovesAWorldSpace(Rows rows)
        {
            var (shown, _) = EventLog.UseState("S", true);
            return V.Div(children: new VNode[] { shown ? AWorldSpaceOf(rows) : null });
        }

        // Its children before and after are host elements alone, which takes the fast path: the walk takes the
        // V.WorldSpace down as it removes it.
        [Component(Compiler = false)]
        private static VNode RemovesAWorldSpaceFromItsList(Rows rows)
        {
            var (shown, _) = EventLog.UseState("S", true);
            return V.Div(children: shown ? new[] { AWorldSpaceOf(rows) } : Array.Empty<VNode>());
        }

        private MountOptions Reporting() => new()
        {
            OnCaughtError = (error, info) => _reports.Add("caught:" + info.ErrorBoundary + ":" + error.Message),
            OnUncaughtError = (error, _) => _reports.Add("uncaught:" + error.Message),
        };

        private void MountUnderABoundary(VNode child, EventLog log)
            => _mounted = V.Mount(_host.Root, log.Provide(V.Component(Boundary, new Holds(child), key: "b")),
                Reporting());

        // What the act throws, and what it logs as an error, an assertion or an exception, are recorded beside the
        // reports, so either fails the case at its one assertion.
        private void Recording(Action act)
        {
            void Record(string message, string stackTrace, LogType type)
            {
                if (type is LogType.Error or LogType.Assert or LogType.Exception) _reports.Add("logged:" + message);
            }

            Application.logMessageReceived += Record;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                act();
            }
            catch (Exception exception)
            {
                _reports.Add("threw:" + exception.Message);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Application.logMessageReceived -= Record;
            }
        }

        // Equivalent rather than equal: which of the two elements leaves first is not what this pins.
        [Test]
        public void Given_ABoundaryAboveAComponentWhoseWorldSpaceHoldsTwoElementsThatThrowAsTheyDetach_When_ADrainRemovesTheWorldSpace_Then_TheBoundaryReportsEachElementsErrorOnce()
        {
            // Arrange
            var log = new EventLog();
            MountUnderABoundary(V.Component(RemovesAWorldSpace, new Rows(null, new[] { "X", "Y" }), key: "s"), log);

            // Act
            log.Setter<bool>("S").Invoke(false);
            Recording(_mounted.GetSchedulerForTest().DrainImmediateForTest);

            // Assert
            Assert.That(_reports, Is.EquivalentTo(new[] { "caught:B:detach:X", "caught:B:detach:Y" }));
        }

        // Equivalent rather than equal: which of the two elements leaves first is not what this pins.
        [Test]
        public void Given_ABoundaryAboveAComponentWhoseWorldSpaceHoldsTwoElementsThatThrowAsTheyDetach_When_ADrainRemovesTheWorldSpaceFromAListOfHostElements_Then_TheBoundaryReportsEachElementsErrorOnce()
        {
            // Arrange
            var log = new EventLog();
            MountUnderABoundary(
                V.Component(RemovesAWorldSpaceFromItsList, new Rows(null, new[] { "X", "Y" }), key: "s"), log);

            // Act
            log.Setter<bool>("S").Invoke(false);
            Recording(_mounted.GetSchedulerForTest().DrainImmediateForTest);

            // Assert
            Assert.That(_reports, Is.EquivalentTo(new[] { "caught:B:detach:X", "caught:B:detach:Y" }));
        }

        [Test]
        public void Given_ABoundaryAboveAComponentWhoseWorldSpaceHoldsAZPlacedElementThatThrowsAsItDetaches_When_ADrainRemovesTheWorldSpace_Then_TheBoundaryReportsTheErrorOnce()
        {
            // Arrange
            var log = new EventLog();
            MountUnderABoundary(
                V.Component(RemovesAWorldSpace, new Rows("absolute z-10", new[] { "Z" }), key: "s"), log);

            // Act
            log.Setter<bool>("S").Invoke(false);
            Recording(_mounted.GetSchedulerForTest().DrainImmediateForTest);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("caught:B:detach:Z:z"));
        }

        // Equivalent rather than equal: which of the two elements leaves first is not what this pins.
        [Test]
        public void Given_AWorldSpaceHoldingTwoElementsThatThrowAsTheyDetach_When_TheHandleIsDisposed_Then_TheRootReportsEachElementsErrorOnce()
        {
            // Arrange
            var mounted = V.Mount(_host.Root, AWorldSpaceOf(new Rows(null, new[] { "X", "Y" })), Reporting());

            // Act
            Recording(mounted.Dispose);

            // Assert
            Assert.That(_reports, Is.EquivalentTo(new[] { "uncaught:detach:X", "uncaught:detach:Y" }));
        }
    }
}
