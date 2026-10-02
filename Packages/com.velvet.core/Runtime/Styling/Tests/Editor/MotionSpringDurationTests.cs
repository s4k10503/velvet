using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins a spring described by <see cref="StyleTransitionConfig.DurationSec"/> and
    /// <see cref="StyleTransitionConfig.Bounce"/>: the physics it resolves to, the precedence of the physics knobs
    /// over both, the copies that must carry what the caller set, a zero duration landing at once, and a play
    /// that settles once its duration has passed. Each expected stiffness and damping is the one Framer Motion
    /// 12's <c>getSpringOptions</c> returns for the same <c>duration</c> (in milliseconds) and <c>bounce</c>.
    /// </summary>
    [TestFixture]
    internal sealed class MotionSpringDurationTests
    {
        private const float FixedDeltaSec = 1f / 60f;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private void Ticks(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _sim.FrameUpdateMs(16);
            }
        }

        private VisualElement AttachedElement(string className)
        {
            var element = new VisualElement();
            element.AddToClassList(className);
            _sim.rootVisualElement.Add(element);
            return element;
        }

        private static float[] Physics(StyleTransitionConfig config) => new[] { config.Stiffness, config.Damping };

        [Test]
        public void Given_ASpringNamingOnlyADuration_When_ItsPhysicsAreRead_Then_TheyAreTheOnesThatDurationDescribes()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.8f };

            // Act
            var physics = Physics(config);

            // Assert — getSpringOptions({ duration: 800 }).
            Assert.That(physics, Is.EqualTo(new[] { 151.27913f, 17.219382f }).Within(1e-3).Percent);
        }

        [Test]
        public void Given_ASpringNamingADurationAndABounce_When_ItsPhysicsAreRead_Then_TheyAreTheOnesBothDescribe()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f, Bounce = 0.25f };

            // Act
            var physics = Physics(config);

            // Assert — getSpringOptions({ duration: 500, bounce: 0.25 }).
            Assert.That(physics, Is.EqualTo(new[] { 351.77878f, 28.13365f }).Within(1e-3).Percent);
        }

        [Test]
        public void Given_ASpringNamingOnlyABounce_When_ItsPhysicsAreRead_Then_TheyAreTheOnesItDescribesOverTheDefaultDuration()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, Bounce = 0f };

            // Act
            var physics = Physics(config);

            // Assert — getSpringOptions({ bounce: 0 }), whose duration defaults to 800.
            Assert.That(physics, Is.EqualTo(new[] { 133.21238f, 23.083534f }).Within(1e-3).Percent);
        }

        [Test]
        public void Given_ASpringWithANegativeBounce_When_ItsPhysicsAreRead_Then_TheyAreThoseOfACriticallyDampedSpring()
        {
            // Arrange — the damping ratio is held at 1, which takes the critically damped envelope.
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 1f, Bounce = -0.5f };

            // Act
            var physics = Physics(config);

            // Assert — getSpringOptions({ duration: 1000, bounce: -0.5 }).
            Assert.That(physics, Is.EqualTo(new[] { 85.255924f, 18.466827f }).Within(1e-3).Percent);
        }

        [Test]
        public void Given_ASpringBouncingPastTheDampingFloor_When_ItsPhysicsAreRead_Then_TheDampingRatioIsHeldAtItsFloor()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, Bounce = 1.5f };

            // Act
            var physics = Physics(config);

            // Assert — getSpringOptions({ bounce: 1.5 }).
            Assert.That(physics, Is.EqualTo(new[] { 9571.0737f, 9.783186f }).Within(1e-3).Percent);
        }

        [Test]
        public void Given_ASpringDurationAboveTenSeconds_When_ItsPhysicsAreRead_Then_TheyAreThoseOfTenSeconds()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 20f };

            // Act
            var physics = Physics(config);

            // Assert — getSpringOptions({ duration: 20000 }).
            Assert.That(physics, Is.EqualTo(new[] { 0.96818645f, 1.3775505f }).Within(1e-3).Percent);
        }

        [Test]
        public void Given_ASpringDurationBelowTenMilliseconds_When_ItsPhysicsAreRead_Then_TheyAreThoseOfTenMilliseconds()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.001f };

            // Act
            var physics = Physics(config);

            // Assert — getSpringOptions({ duration: 1 }).
            Assert.That(physics, Is.EqualTo(new[] { 968186.45f, 1377.5505f }).Within(1e-3).Percent);
        }

        // GREEN_ON_BASE(characterization): the base ignores a spring's duration, so its damping stays 10 too.
        [Test]
        public void Given_ASpringNamingAStiffnessAndADuration_When_ItsDampingIsRead_Then_ItIsTheDefaultDamping()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 300f, DurationSec = 0.5f };

            // Act
            var damping = config.Damping;

            // Assert
            Assert.That(damping, Is.EqualTo(10f));
        }

        // GREEN_ON_BASE(characterization): the base ignores a spring's duration, so its stiffness stays 100 too.
        [Test]
        public void Given_ASpringMotionGivenADelay_When_ItsTransitionsStiffnessIsRead_Then_ItIsTheDefaultStiffness()
        {
            // Arrange
            var spring = new StyleTransitionConfig { Type = TransitionType.Spring };

            // Act — V.Motion tunes the transition through With, which must leave the duration unset.
            var node = V.Motion(key: "m", transition: spring, delay: 0.1f);

            // Assert
            Assert.That(node.Transition!.Stiffness, Is.EqualTo(100f));
        }

        [Test]
        public void Given_ASpringMotionGivenADuration_When_ItsTransitionsPhysicsAreRead_Then_TheyAreTheOnesThatDurationDescribes()
        {
            // Arrange
            var spring = new StyleTransitionConfig { Type = TransitionType.Spring };

            // Act
            var node = V.Motion(key: "m", transition: spring, duration: 0.8f);

            // Assert
            Assert.That(Physics(node.Transition!), Is.EqualTo(new[] { 151.27913f, 17.219382f }).Within(1e-3).Percent);
        }

        [Test]
        public void Given_ASpringNamingADurationAndABounce_When_TunedWithADelay_Then_ItKeepsTheDampingBothDescribe()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f, Bounce = 0.25f };

            // Act
            var tuned = config.With(delaySec: 0.1f);

            // Assert
            Assert.That(tuned.Damping, Is.EqualTo(28.13365f).Within(1e-3).Percent);
        }

        [Test]
        public void Given_ASpringNamingADurationAndABounce_When_RebuiltForAVariantExit_Then_ItKeepsTheDampingBothDescribe()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f, Bounce = 0.25f };

            // Act
            var exit = config.WithExitClasses("opacity-100", "opacity-0");

            // Assert
            Assert.That(exit.Damping, Is.EqualTo(28.13365f).Within(1e-3).Percent);
        }

        [Test]
        public void Given_ASpringWithAZeroDuration_When_AskedWhetherItsExitAnimates_Then_ItDoesNot()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f };

            // Act
            var animates = config.HasExitAnimation;

            // Assert
            Assert.That(animates, Is.False);
        }

        [Test]
        public void Given_ASpringWithAZeroDuration_When_AskedWhetherASwapOnItLandsAtOnce_Then_ItDoes()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f };

            // Act
            var landsAtOnce = StyleAnimationScheduler.LandsAtOnce(config);

            // Assert
            Assert.That(landsAtOnce, Is.True);
        }

        [Test]
        public void Given_ASpringVariantEnterWithAZeroDuration_When_Played_Then_ItCompletesAtOnce()
        {
            // Arrange
            var element = AttachedElement("opacity-100");
            var scheduler = new StyleAnimationScheduler();
            var completed = false;

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f },
                onComplete: () => completed = true);

            // Assert
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_ASpringVariantExitWithAZeroDuration_When_Played_Then_ItCompletesAtOnce()
        {
            // Arrange
            var element = AttachedElement("opacity-100");
            var scheduler = new StyleAnimationScheduler();
            var completed = false;
            var exit = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f }
                .WithExitClasses("opacity-100", "opacity-0");

            // Act
            scheduler.PlayExit(element, exit, () => completed = true, restoreFromOnCancel: true);

            // Assert
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_ASpringVariantEnterNamingADuration_When_TimePassesThatDuration_Then_ItCompletesThenAndNotBefore()
        {
            // Arrange — its physics alone would settle this opacity spring 0.66s in.
            var element = AttachedElement("opacity-100");
            var scheduler = new StyleAnimationScheduler();
            var completed = false;
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f },
                onComplete: () => completed = true);

            // Act — 448ms, then 544ms.
            Ticks(28);
            var completedBefore = completed;
            Ticks(6);

            // Assert
            Assert.That((completedBefore, completed), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ASpringVariantExitNamingADuration_When_TimePassesThatDuration_Then_ItCompletesThenAndNotBefore()
        {
            // Arrange — its physics alone would settle this opacity spring 0.66s in.
            var element = AttachedElement("opacity-100");
            var scheduler = new StyleAnimationScheduler();
            var completed = false;
            var exit = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f }
                .WithExitClasses("opacity-100", "opacity-0");
            scheduler.PlayExit(element, exit, () => completed = true, restoreFromOnCancel: true);

            // Act — 448ms, then 544ms.
            Ticks(28);
            var completedBefore = completed;
            Ticks(6);

            // Assert
            Assert.That((completedBefore, completed), Is.EqualTo((false, true)));
        }

        // An exit-shaped opacity channel, 1 → 0, settling 0.5s after it starts, stepped ten times.
        private static MotionSpringState MidExitSettlingSpring(VisualElement element)
        {
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = MotionSpringDriver.Create(plan, stiffness: 387.27458f, damping: 27.55101f, mass: 1f,
                settleAtSec: 0.5)!;
            MotionSpringDriver.ApplyCurrentValues(element, state);
            for (var i = 0; i < 10; i++)
            {
                MotionSpringDriver.Step(element, state, FixedDeltaSec);
            }
            return state;
        }

        [Test]
        public void Given_ASpringSettlingAtASetTimeMidExit_When_Retargeted_Then_ItsVelocityIsDropped()
        {
            // Arrange
            var state = MidExitSettlingSpring(new VisualElement());
            var velocityBefore = state.Opacity!.Integrator.Velocity;

            // Act
            MotionSpringDriver.Retarget(state);

            // Assert — moving before the retarget, still after it.
            Assert.That((velocityBefore != 0f, state.Opacity.Integrator.Velocity), Is.EqualTo((true, 0f)));
        }

        [Test]
        public void Given_ASpringSettlingAtASetTimeMidExit_When_Retargeted_Then_ItHeadsBackTowardTheValueItStartedFrom()
        {
            // Arrange
            var state = MidExitSettlingSpring(new VisualElement());

            // Act
            MotionSpringDriver.Retarget(state);

            // Assert
            Assert.That(state.Opacity!.Target, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ASpringSettlingAtASetTimeMidExit_When_Retargeted_Then_ItSettlesThatLongAfterTheRetarget()
        {
            // Arrange
            var element = new VisualElement();
            var state = MidExitSettlingSpring(element);

            // Act — 28 steps is 0.467s after the retarget and 33 steps 0.55s.
            MotionSpringDriver.Retarget(state);
            var settledEarly = false;
            for (var i = 0; i < 28; i++)
            {
                settledEarly |= MotionSpringDriver.Step(element, state, FixedDeltaSec);
            }
            var settledLater = false;
            for (var i = 0; i < 5; i++)
            {
                settledLater |= MotionSpringDriver.Step(element, state, FixedDeltaSec);
            }

            // Assert
            Assert.That((settledEarly, settledLater), Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): the base keeps every retargeted spring's velocity as well.
        [Test]
        public void Given_ASpringSettlingByItsPhysicsMidExit_When_Retargeted_Then_ItKeepsItsVelocity()
        {
            // Arrange
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 20f, mass: 1f)!;
            MotionSpringDriver.ApplyCurrentValues(element, state);
            for (var i = 0; i < 10; i++)
            {
                MotionSpringDriver.Step(element, state, FixedDeltaSec);
            }
            var velocityBefore = state.Opacity!.Integrator.Velocity;

            // Act
            MotionSpringDriver.Retarget(state);

            // Assert — moving before the retarget, at the same speed after it.
            Assert.That((velocityBefore != 0f, state.Opacity.Integrator.Velocity), Is.EqualTo((true, velocityBefore)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_ASpringWithAZeroDuration_When_AskedWhetherALayoutIdMoveOnItAnimates_Then_ItDoesNot(bool physics)
        {
            // Arrange
            var config = physics
                ? new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 170f, DurationSec = 0f }
                : new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f };

            // Act
            var animates = LayoutIdTiming.From(config).Animates();

            // Assert
            Assert.That(animates, Is.False);
        }

        [Test]
        public void Given_ALayoutIdMoveOnASpringNamingADuration_When_SteppedPastThatDuration_Then_ItArrivesThenOnTheLayout()
        {
            // Arrange — its physics alone would leave this 100px move short of its rest threshold at 0.5s.
            var progress = LayoutIdTiming.From(new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f })
                .Start(100f);

            // Act — 29 steps is 0.483s and 30 steps 0.5s.
            var arrivedEarly = false;
            for (var i = 0; i < 29; i++)
            {
                arrivedEarly |= progress.Step(FixedDeltaSec);
            }
            var arrived = progress.Step(FixedDeltaSec) || progress.Step(FixedDeltaSec);

            // Assert — on the layout once it arrives.
            Assert.That((arrivedEarly, arrived, progress.Value), Is.EqualTo((false, true, 0f)));
        }
    }
}
