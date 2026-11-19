using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a <c>Hooks.UseAnimationSequence</c> step tweens a <c>V.Motion</c> that takes its transition from
    /// the pose, rather than from <c>AnimationSequenceState.CurrentTransition</c>, on UI Toolkit's own transition for
    /// that step's duration: a wipe that enters to cover and then leaves on the far side. Each frame drives the
    /// panel's scheduler, its transitions and its styles on <see cref="UseFrameFakeClockHost"/>'s fake clock.
    /// </summary>
    internal sealed class AnimationSequenceNativeTweenTests
    {
        private static readonly Dictionary<string, MotionVariant> s_poses = new()
        {
            ["away"] = "translate-x-[300px]",
            ["cover"] = "translate-x-[0px]",
            ["gone"] = "translate-x-[-300px]",
        };

        private static readonly StyleTransitionConfig s_enter = new() { DurationSec = 0.5f, Easing = EasingMode.Linear };
        private static readonly StyleTransitionConfig s_leave = new() { DurationSec = 1f, Easing = EasingMode.Linear };

        // The cover step holds for a second, twice its play's duration, so the leave step follows a play that has ended.
        private static readonly AnimationSequenceStep[] s_steps =
        {
            AnimationSequenceStep.To("cover", s_enter, holdSec: 1f),
            AnimationSequenceStep.To("gone", s_leave),
        };

        private static AnimationSequenceState s_state;

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
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

        // The configs handed here are the authored ones, which carry none of the sequence's playback.
        [Component]
        private static VNode PoseTimedHost()
        {
            var (state, _) = Hooks.UseAnimationSequence(s_steps, deps: Array.Empty<object>());
            s_state = state;
            return V.Motion(key: "m", name: "m", variants: s_poses, initial: "away", animate: state.CurrentLabel,
                transition: state.CurrentLabel == "gone" ? s_leave : s_enter, enterOnMount: false);
        }

        private void Mount()
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(PoseTimedHost, key: "root"));
            _mounted.FlushEffectsForTest();
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(_host.Panel);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private void Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
                _mounted.FlushStateForTest();
                EditorPanelTestHelpers.DriveAnimationsOnce(_host.Panel);
                EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            }
        }

        private float TranslateX() => _host.Root.Q<VisualElement>("m").resolvedStyle.translate.x;

        // GREEN_ON_BASE(characterization): the base writes the leave pose at its swap, after the play wrote its transition.
        // Replacing `StyleAnimationScheduler.RunsOnSwap(swapTransition, extraDelaySec)` with `false` in
        // `FiberNodePatcher.ResolveInlineHold` writes the pose at the render, ahead of the transition, and reddens
        // this case.
        [Test]
        public void Given_ALeaveStepTimedByItsPoseAfterTheEnterPlayEnded_When_SixteenFramesOfItRun_Then_TheMotionCoversThatShareOfItsSecond()
        {
            // Arrange — three frames into the leave step, past the frame its swap starts the transition in.
            Mount();
            for (var i = 0; i < 125 && s_state.CurrentLabel != "gone"; i++)
            {
                Frames(1);
            }
            Frames(3);
            var before = TranslateX();

            // Act
            Frames(16);

            // Assert — 256 ms of a linear second, over the 300 px from cover to gone.
            Assert.That((before - TranslateX()) / 300f, Is.EqualTo(0.256f).Within(0.02f));
        }
    }
}
