using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that <c>AnimationSequenceControls.Pause</c> and <c>Play</c> reach the Bezier play a step's label
    /// started, as Framer Motion's sequence controls reach the animations the sequence started: on the Motion
    /// handed the sequence's transition, and on a child taking its label from that Motion while its own pose names
    /// the transition it plays, including the mount enter of a child that mounts there part-way through.
    /// </summary>
    internal sealed class AnimationSequencePauseReachTests : AnimationSequenceMotionTestsBase
    {
        [Test]
        public void Given_ASequencePartWayThroughABezierFade_When_PausedForFiveFrames_Then_TheMotionsOpacityHolds()
        {
            // Arrange
            MountCoordinator();
            PlayIntoTheFade();
            var moving = Opacity().value;

            // Act
            Controls.Pause();
            Frames(5);

            // Assert
            Assert.That(moving > 0f ? Opacity().value : float.NaN, Is.EqualTo(moving));
        }

        [Test]
        public void Given_ASequencePartWayThroughAChildsBezierFade_When_PausedForFiveFrames_Then_TheChildsOpacityHolds()
        {
            // Arrange
            MountInheriting();
            PlayIntoTheFade();
            var moving = Opacity().value;

            // Act
            Controls.Pause();
            Frames(5);

            // Assert
            Assert.That(moving > 0f ? Opacity().value : float.NaN, Is.EqualTo(moving));
        }

        [Test]
        public void Given_AChildMountingUnderTheCoordinatorPartWayThroughItsMountEnter_When_PausedForFiveFrames_Then_ItsOpacityHolds()
        {
            // Arrange
            MountMidMount();
            PlayIntoTheFade();
            ChildMounted = true;
            RenderAgain();
            Frames(3);
            var moving = Opacity().value;

            // Act
            Controls.Pause();
            Frames(5);

            // Assert
            Assert.That(moving > 0f ? Opacity().value : float.NaN, Is.EqualTo(moving));
        }

        [Test]
        public void Given_APresenceChildMountingUnderTheCoordinatorPartWayThroughItsEnter_When_PausedForFiveFrames_Then_ItsOpacityHolds()
        {
            // Arrange
            MountPresence();
            PlayIntoTheFade();
            ChildMounted = true;
            RenderAgain();
            Frames(3);
            var moving = Opacity().value;

            // Act
            Controls.Pause();
            Frames(5);

            // Assert
            Assert.That(moving > 0f ? Opacity().value : float.NaN, Is.EqualTo(moving));
        }

        // The child mounts in a render of its own component alone, which rebuilds the coordinator's context
        // around it rather than reconciling the coordinator.
        [Test]
        public void Given_AChildMountedByItsOwnComponentsStatePartWayThroughItsMountEnter_When_PausedForFiveFrames_Then_ItsOpacityHolds()
        {
            // Arrange
            MountSelfMount();
            PlayIntoTheFade();
            SetChildMounted.Invoke(true);
            Frames(3);
            var moving = Opacity().value;

            // Act
            Controls.Pause();
            Frames(5);

            // Assert
            Assert.That(moving > 0f ? Opacity().value : float.NaN, Is.EqualTo(moving));
        }

        [Test]
        public void Given_APresenceChildMountedByItsOwnComponentsStatePartWayThroughItsEnter_When_PausedForFiveFrames_Then_ItsOpacityHolds()
        {
            // Arrange
            MountSelfPresence();
            PlayIntoTheFade();
            SetChildMounted.Invoke(true);
            Frames(3);
            var moving = Opacity().value;

            // Act
            Controls.Pause();
            Frames(5);

            // Assert
            Assert.That(moving > 0f ? Opacity().value : float.NaN, Is.EqualTo(moving));
        }

        // GREEN_ON_BASE(characterization): a Motion beside the coordinator plays on through a pause, as every play
        // does on the base, where Pause reaches none; the change has to hand the sequence's playback to the
        // coordinator's descendants alone.
        [Test]
        public void Given_ASequenceAndAFadeBesideItsCoordinator_When_PausedForFiveFrames_Then_TheFadeBesideItMovesOn()
        {
            // Arrange
            MountSibling();
            PlayIntoTheFade();
            OuterLabel = "visible";
            RenderAgain();
            Frames(3);
            var moving = Opacity().value;

            // Act
            Controls.Pause();
            Frames(5);

            // Assert
            Assert.That(moving > 0f ? Opacity().value : float.NaN, Is.GreaterThan(moving));
        }

        // The fade is linear over one second, so two frames of it move the opacity by 0.032; the five paused
        // frames before them would have moved it 0.08 further.
        [Test]
        public void Given_ASequencePausedPartWayThroughABezierFade_When_PlayedForTwoFrames_Then_TheMotionMovesOnByTwoFrames()
        {
            // Arrange
            MountCoordinator();
            PlayIntoTheFade();
            var moving = Opacity().value;
            Controls.Pause();
            Frames(5);

            // Act
            Controls.Play();
            Frames(2);

            // Assert
            Assert.That(moving > 0f ? Opacity().value - moving : float.NaN, Is.EqualTo(0.032f).Within(1e-4f));
        }
    }
}
