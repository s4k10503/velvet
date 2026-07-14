using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins UseFrame's cadence contract deterministically: the callback follows the panel's scheduler
    /// update — once per update with a positive delta — rather than a fixed minimum wall-clock
    /// interval (a 16 ms floor would swallow every firing after the first on any panel updating
    /// faster than it). The panel runs on a fake clock, so machine load cannot skew the probe.
    /// </summary>
    internal sealed class UseFramePerFrameContractTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        [Test]
        public void Given_AMountedUseFrame_When_TheSchedulerUpdatesEveryFewMilliseconds_Then_EachSpacedUpdateTicks()
        {
            // Arrange — mount on the fake clock, run the passive effect that arms the tick, and
            // absorb the arm-time firing (its delta is zero on the frozen clock, so it never counts).
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(UseFrameFakeClockHost.CountingHost, key: "root"));
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Act — four updates two fake-milliseconds apart, all inside a single 16 ms window.
            for (var i = 0; i < 4; i++)
            {
                UseFrameFakeClockHost.Ms += 2;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }

            // Assert — per-update ticking: the spaced updates each invoked the callback; a 16 ms
            // minimum interval would have allowed none of them (only 8 ms elapsed in total).
            Assert.That(UseFrameFakeClockHost.Calls, Is.GreaterThanOrEqualTo(3));
        }
    }
}
