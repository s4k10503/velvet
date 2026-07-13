using System.Diagnostics;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins UseFrame's cadence contract deterministically: the callback follows the panel's scheduler
    /// update — once per update with a positive delta — rather than a fixed minimum wall-clock
    /// interval, so several scheduler updates spaced a few milliseconds apart each tick the callback
    /// (a 16 ms floor would swallow every firing after the first on any panel updating faster than it).
    /// </summary>
    internal sealed class UseFramePerFrameContractTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static int s_calls;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_calls = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        [Component]
        private static VNode CountingHost()
        {
            Hooks.UseFrame(_ => s_calls++);
            return V.Div(className: "w-[10px] h-[10px]");
        }

        // The timer scheduler is internal engine surface: walk the panel's type chain for its
        // "scheduler" property and pump one update — the exact call a live panel issues once per frame.
        private static void DriveSchedulerOnce(IPanel panel)
        {
            object scheduler = null;
            for (var t = panel.GetType(); t != null && scheduler == null; t = t.BaseType)
            {
                var prop = t.GetProperty(
                    "scheduler",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (prop != null)
                {
                    scheduler = prop.GetValue(panel);
                }
            }
            Assume.That(scheduler, Is.Not.Null, "Precondition: the panel exposes a timer scheduler");
            var update = scheduler.GetType().GetMethod(
                "UpdateScheduledEvents",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assume.That(update, Is.Not.Null, "Precondition: the scheduler exposes UpdateScheduledEvents");
            update.Invoke(scheduler, null);
        }

        [Test]
        public void Given_AMountedUseFrame_When_TheSchedulerUpdatesEveryFewMilliseconds_Then_EachSpacedUpdateTicks()
        {
            // Arrange — mount, run the passive effect that arms the tick, and absorb the first firing
            // (its delta spans the arm-to-now gap, not an update-to-update one).
            _mounted = V.Mount(_host.Root, V.Component(CountingHost, key: "root"));
            _mounted.FlushEffectsForTest();
            DriveSchedulerOnce(_host.Panel);
            var clock = Stopwatch.StartNew();

            // Act — pump a handful of updates ~2 ms apart, all inside a single 16 ms window.
            for (var i = 0; i < 4; i++)
            {
                Thread.Sleep(2);
                DriveSchedulerOnce(_host.Panel);
            }
            Assume.That(clock.ElapsedMilliseconds, Is.LessThan(16),
                "Precondition: every update landed inside one 16 ms window");

            // Assert — per-update ticking: the spaced updates each invoked the callback; a 16 ms
            // minimum interval would have allowed at most the window's first firing.
            Assert.That(s_calls, Is.GreaterThanOrEqualTo(3));
        }
    }
}
