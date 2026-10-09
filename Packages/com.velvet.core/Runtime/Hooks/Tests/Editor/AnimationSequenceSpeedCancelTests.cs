using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>AnimationSequenceControls.Speed</c> and <c>Cancel</c>: Framer Motion's <c>speed</c> re-times the
    /// sequence's timeline and the Bezier play a step's label started, and its <c>cancel()</c> stops both, the play
    /// back at its starting values, with <c>Play</c> starting the sequence again from step 0.
    /// </summary>
    internal sealed class AnimationSequenceSpeedCancelTests : AnimationSequenceMotionTestsBase
    {
        [Test]
        public void Given_ASequenceAtSpeedTwo_When_TenFramesRun_Then_TimeSecIsTwiceTheTimeElapsed()
        {
            // Arrange
            MountCoordinator();
            Controls.Speed = 2f;

            // Act
            Frames(10);

            // Assert
            Assert.That(Controls.TimeSec, Is.EqualTo(0.32f).Within(1e-4f));
        }

        // Step 0 holds for no time, so a frame that advances the cursor at all, by however little, crosses it.
        [Test]
        public void Given_ASequenceAtSpeedZero_When_RestartedOntoAStepWithNoHoldAndFramesRun_Then_TheCursorStaysOnIt()
        {
            // Arrange
            MountCoordinator();
            PlayIntoTheFade();
            Controls.Speed = 0f;

            // Act
            Controls.Restart();
            Frames(3);

            // Assert
            Assert.That((State.StepIndex, State.CurrentLabel), Is.EqualTo((0, "hidden")));
        }

        [Test]
        public void Given_ASequencePartWayThroughABezierFade_When_ItsSpeedIsHalvedForFourFrames_Then_TheMotionMovesByTwoFramesWorth()
        {
            // Arrange
            MountCoordinator();
            PlayIntoTheFade();
            var moving = Opacity().value;

            // Act
            Controls.Speed = 0.5f;
            Frames(4);

            // Assert
            Assert.That(moving > 0f ? Opacity().value - moving : float.NaN, Is.EqualTo(0.032f).Within(1e-4f));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Given_AMountedSequence_When_ItsSpeedIsSetOutsideZeroOrMore_Then_ItThrows(float speed)
        {
            // Arrange
            MountCoordinator();

            // Act
            TestDelegate set = () => Controls.Speed = speed;

            // Assert
            Assert.That(set, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Given_ASequencePartWayThroughABezierFade_When_Cancelled_Then_TheMotionHoldsTheOpacityTheFadeStartedFrom()
        {
            // Arrange
            MountCoordinator();
            PlayIntoTheFade();
            var moving = Opacity().value;

            // Act
            Controls.Cancel();
            Frames(2);

            // Assert
            var opacity = Opacity();
            Assert.That((moving > 0f, opacity.keyword, opacity.value), Is.EqualTo((true, StyleKeyword.Undefined, 0f)));
        }

        [Test]
        public void Given_ASequenceOnAStepWithAShortHold_When_CancelledAndFramesRunPastTheHold_Then_TheCursorStays()
        {
            // Arrange
            Steps = new[]
            {
                AnimationSequenceStep.To("hidden", Instant),
                AnimationSequenceStep.To("visible", Linear(0.1f)),
                AnimationSequenceStep.To("hidden", Instant),
            };
            MountCoordinator();
            PlayIntoTheFade();
            var cancelledAt = State.StepIndex;

            // Act
            Controls.Cancel();
            Frames(20);

            // Assert
            Assert.That((cancelledAt, State.StepIndex), Is.EqualTo((1, 1)));
        }

        // The cancelled fade to visible started from half, so its held opacity is 0.5, where the reseed's fade
        // back to half has to start rather than from visible's 1.
        [Test]
        public void Given_ACancelledFade_When_RestartedOntoAStepFadingAnotherWay_Then_TheNewFadeStartsFromTheHeldOpacity()
        {
            // Arrange
            Steps = new[]
            {
                AnimationSequenceStep.To("half", Linear(0.1f)),
                AnimationSequenceStep.To("visible", Linear(1f)),
            };
            MountInitialCoordinator();
            for (var i = 0; i < 20 && State.CurrentLabel != "visible"; i++)
            {
                Frames(1);
            }
            Frames(3);
            Controls.Cancel();
            var held = Opacity().value;

            // Act
            Controls.Restart();
            Flush();

            // Assert
            Assert.That((held, State.CurrentLabel, Opacity().value), Is.EqualTo((0.5f, "half", 0.5f)));
        }

        // A reseed that leaves the label where it is starts no play, so nothing takes the held values over.
        [Test]
        public void Given_ACancelledFade_When_RestartedOntoTheSameLabel_Then_TheHeldOpacityComesOffAfterTheCommit()
        {
            // Arrange
            Steps = new[] { AnimationSequenceStep.To("visible", Linear(1f)) };
            MountInitialCoordinator();
            PlayIntoTheFade();
            Controls.Cancel();
            var held = Opacity().keyword;

            // Act
            Controls.Restart();
            Flush();

            // Assert
            Assert.That((held, Opacity().keyword == StyleKeyword.Undefined), Is.EqualTo((StyleKeyword.Undefined, false)));
        }

        // The first reseed's commit releases what the first cancel held, and only that commit: what the second
        // cancel holds stays through a later render's commit.
        [Test]
        public void Given_ASequenceCancelledRestartedAndCancelledAgain_When_ItRendersAgain_Then_TheSecondCancelsOpacityStaysHeld()
        {
            // Arrange
            MountInitialCoordinator();
            PlayIntoTheFade();
            Controls.Cancel();
            Controls.Restart();
            Flush();
            Controls.Play();
            PlayIntoTheFade();
            Controls.Cancel();

            // Act
            RenderAgain();
            Flush();

            // Assert
            var opacity = Opacity();
            Assert.That((opacity.keyword, opacity.value), Is.EqualTo((StyleKeyword.Undefined, 0f)));
        }

        private static int s_stepZeroCalls;

        [Test]
        public void Given_ACancelledSequenceOnItsSecondStep_When_Played_Then_ItRendersStepZerosLabel()
        {
            // Arrange
            MountCoordinator();
            PlayIntoTheFade();
            var cancelledOn = State.CurrentLabel;
            Controls.Cancel();

            // Act
            Controls.Play();
            Flush();

            // Assert
            Assert.That((cancelledOn, State.CurrentLabel), Is.EqualTo(("visible", "hidden")));
        }

        // A restart reseeds the sequence, which ends the cancel, so Play only resumes it.
        [Test]
        public void Given_ACancelledSequenceThatWasRestarted_When_Played_Then_StepZeroIsNotCommittedAgain()
        {
            // Arrange
            s_stepZeroCalls = 0;
            Steps = new[]
            {
                AnimationSequenceStep.Call(() => s_stepZeroCalls++),
                AnimationSequenceStep.To("visible", Linear(1f)),
            };
            MountCoordinator();
            Controls.Cancel();
            var beforeRestart = s_stepZeroCalls;
            Controls.Restart();
            var afterRestart = s_stepZeroCalls;

            // Act
            Controls.Play();

            // Assert
            Assert.That((afterRestart - beforeRestart, s_stepZeroCalls - afterRestart), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_ACancelledSequence_When_Played_Then_ItStartsAgainFromStepZero()
        {
            // Arrange
            MountCoordinator();
            PlayIntoTheFade();
            var cancelledAt = Controls.TimeSec;
            Controls.Cancel();

            // Act
            Controls.Play();

            // Assert
            Assert.That((cancelledAt > 0f, Controls.TimeSec), Is.EqualTo((true, 0f)));
        }
    }
}
