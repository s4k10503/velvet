using System.Collections.Generic;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="Hooks.UseFrame(System.Action{float}, int)"/> on a mount's <see cref="MotionClock"/>: the
    /// delta is the distance the clock moved, so a clock that holds still invokes nothing while the panel keeps
    /// ticking, and <c>Hooks.UseAnimationSequence</c>, which walks its steps on that delta, holds with it.
    /// </summary>
    internal sealed class UseFrameMotionClockTests
    {
        private const double FrameSec = HeldMotionClock.FrameSec;

        private static readonly List<float> s_deltas = new();
        private static AnimationSequenceState s_state;

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_deltas.Clear();
            s_state = default;
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
        private static VNode DeltaRecorder()
        {
            Hooks.UseFrame(dt => s_deltas.Add(dt));
            return V.Div(className: "w-[10px] h-[10px]");
        }

        private static readonly AnimationSequenceStep[] s_steps =
        {
            AnimationSequenceStep.To("a", holdSec: (float)(4 * FrameSec)),
            AnimationSequenceStep.To("b"),
        };

        [Component]
        private static VNode SequenceHost()
        {
            var (state, _) = Hooks.UseAnimationSequence(s_steps, deps: System.Array.Empty<object>());
            s_state = state;
            return V.Div(className: "w-[10px] h-[10px]");
        }

        // Mounts on the fake panel clock and arms UseFrame's tick, whose first pass takes the baseline.
        private void Mount(System.Func<VNode> component, MotionClock clock)
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(component, key: "root"), new MountOptions { MotionClock = clock });
            _mounted.FlushEffectsForTest();
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        private void PanelFrames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }
        }

        [Test]
        public void Given_AUseFrameOnAHeldClock_When_ThePanelTicksAndThenTheClockStepsAFrame_Then_ItIsInvokedOnceWithThatFrame()
        {
            // Arrange
            var clock = new HeldMotionClock();
            Mount(DeltaRecorder, clock);

            // Act
            PanelFrames(10);
            clock.Now += FrameSec;
            PanelFrames(1);

            // Assert
            Assert.That(s_deltas, Is.EqualTo(new[] { (float)FrameSec }));
        }

        [Test]
        public void Given_ASequenceOnAHeldClock_When_ThePanelTicksPastItsHoldAndThenTheClockCoversIt_Then_ItAdvancesOnlyThen()
        {
            // Arrange
            var clock = new HeldMotionClock();
            Mount(SequenceHost, clock);

            // Act
            PanelFrames(20);
            _mounted.FlushStateForTest();
            var held = s_state.CurrentLabel;
            clock.Now += 4 * FrameSec;
            PanelFrames(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { held, s_state.CurrentLabel }, Is.EqualTo(new[] { "a", "b" }));
        }
    }
}
