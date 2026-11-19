using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>AnimationSequenceControls</c>' <c>TimeSec</c> on the fake clock
    /// <see cref="UseAnimationSequenceTests"/> walks the steps on. Every tick is 16ms.
    /// </summary>
    internal sealed class UseAnimationSequenceControlsTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static AnimationSequenceStep[] s_steps;
        private static bool s_loop;
        private static AnimationSequenceState s_state;
        private static AnimationSequenceControls s_controls;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_loop = false;
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
        private static VNode SequenceHost()
        {
            var (state, controls) = Hooks.UseAnimationSequence(s_steps, deps: Array.Empty<object>(), loop: s_loop);
            s_state = state;
            s_controls = controls;
            return V.Div(className: "w-[10px] h-[10px]");
        }

        private void Mount()
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(SequenceHost, key: "root"));
            _mounted.FlushEffectsForTest();
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        private void AdvanceTicks(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }
            _mounted.FlushStateForTest();
        }

        private static AnimationSequenceStep To(string label, float durationSec)
            => AnimationSequenceStep.To(label, new StyleTransitionConfig { DurationSec = durationSec });

        [Test]
        public void Given_ASequenceTenTicksIntoItsFirstHold_When_TimeSecIsRead_Then_ItIsTheTimeElapsed()
        {
            // Arrange
            s_steps = new[] { To("a", 0.5f), To("b", 0.5f) };
            Mount();

            // Act
            AdvanceTicks(10);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.16f).Within(1e-4f));
        }

        [Test]
        public void Given_ASequenceTenTicksPastAShortFirstHold_When_TimeSecIsRead_Then_ItCountsTheFirstHold()
        {
            // Arrange
            s_steps = new[] { To("a", 0.1f), To("b", 0.5f) };
            Mount();

            // Act
            AdvanceTicks(10);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.16f).Within(1e-4f));
        }

        [Test]
        public void Given_ASequenceTheClockHasRunPast_When_TimeSecIsRead_Then_ItIsTheSequencesLength()
        {
            // Arrange
            s_steps = new[] { To("a", 0.1f) };
            Mount();

            // Act
            AdvanceTicks(10);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.1f).Within(1e-4f));
        }

        [Test]
        public void Given_ALoopingSequenceOnItsSecondPass_When_TimeSecIsRead_Then_ItCountsTheFirstPassToo()
        {
            // Arrange — 240ms against a 200ms pass.
            s_steps = new[] { To("a", 0.1f), To("b", 0.1f) };
            s_loop = true;
            Mount();

            // Act
            AdvanceTicks(15);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.24f).Within(1e-4f));
        }

        [Test]
        public void Given_ASequenceIntoItsSecondHold_When_Restarted_Then_TimeSecIsZero()
        {
            // Arrange
            s_steps = new[] { To("a", 0.1f), To("b", 0.5f) };
            Mount();
            AdvanceTicks(10);

            // Act
            s_controls.Restart();

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0f));
        }
    }
}
