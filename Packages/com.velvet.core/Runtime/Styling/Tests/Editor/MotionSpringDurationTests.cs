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
    /// over both, the copies that must carry what the caller set, a zero duration making any spring instant, and a
    /// play that ends once its duration has passed. Each expected stiffness and damping is Framer Motion 12's
    /// <c>findSpring</c> formulas evaluated for the same <c>duration</c> (in milliseconds), <c>bounce</c> and,
    /// where a case names one, velocity.
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

        internal enum Knob
        {
            Stiffness,
            Damping,
        }

        private static float Read(float stiffness, float damping, Knob knob)
            => knob == Knob.Stiffness ? stiffness : damping;

        private static float Physics(StyleTransitionConfig config, Knob knob)
            => Read(config.Stiffness, config.Damping, knob);

        [TestCase(Knob.Stiffness, 151.27913f)]
        [TestCase(Knob.Damping, 17.219382f)]
        public void Given_ASpringNamingOnlyADuration_When_ItsPhysicsAreRead_Then_TheyAreTheOnesThatDurationDescribes(Knob knob, float expected)
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.8f };

            // Act
            var physics = Physics(config, knob);

            // Assert — getSpringOptions({ duration: 800 }).
            Assert.That(physics, Is.EqualTo(expected).Within(1e-3).Percent);
        }

        [TestCase(Knob.Stiffness, 351.77878f)]
        [TestCase(Knob.Damping, 28.13365f)]
        public void Given_ASpringNamingADurationAndABounce_When_ItsPhysicsAreRead_Then_TheyAreTheOnesBothDescribe(Knob knob, float expected)
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f, Bounce = 0.25f };

            // Act
            var physics = Physics(config, knob);

            // Assert — getSpringOptions({ duration: 500, bounce: 0.25 }).
            Assert.That(physics, Is.EqualTo(expected).Within(1e-3).Percent);
        }

        [TestCase(Knob.Stiffness, 133.21238f)]
        [TestCase(Knob.Damping, 23.083534f)]
        public void Given_ASpringNamingOnlyABounce_When_ItsPhysicsAreRead_Then_TheyAreTheOnesItDescribesOverTheDefaultDuration(Knob knob, float expected)
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, Bounce = 0f };

            // Act
            var physics = Physics(config, knob);

            // Assert — getSpringOptions({ bounce: 0 }), whose duration defaults to 800.
            Assert.That(physics, Is.EqualTo(expected).Within(1e-3).Percent);
        }

        [TestCase(Knob.Stiffness, 85.255924f)]
        [TestCase(Knob.Damping, 18.466827f)]
        public void Given_ASpringWithANegativeBounce_When_ItsPhysicsAreRead_Then_TheyAreThoseOfACriticallyDampedSpring(Knob knob, float expected)
        {
            // Arrange — the damping ratio is held at 1, which takes the critically damped envelope.
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 1f, Bounce = -0.5f };

            // Act
            var physics = Physics(config, knob);

            // Assert — getSpringOptions({ duration: 1000, bounce: -0.5 }).
            Assert.That(physics, Is.EqualTo(expected).Within(1e-3).Percent);
        }

        [TestCase(Knob.Stiffness, 9571.0737f)]
        [TestCase(Knob.Damping, 9.783186f)]
        public void Given_ASpringBouncingPastTheDampingFloor_When_ItsPhysicsAreRead_Then_TheDampingRatioIsHeldAtItsFloor(Knob knob, float expected)
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, Bounce = 1.5f };

            // Act
            var physics = Physics(config, knob);

            // Assert — getSpringOptions({ bounce: 1.5 }).
            Assert.That(physics, Is.EqualTo(expected).Within(1e-3).Percent);
        }

        [TestCase(Knob.Stiffness, 0.96818645f)]
        [TestCase(Knob.Damping, 1.3775505f)]
        public void Given_ASpringDurationAboveTenSeconds_When_ItsPhysicsAreRead_Then_TheyAreThoseOfTenSeconds(Knob knob, float expected)
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 20f };

            // Act
            var physics = Physics(config, knob);

            // Assert — getSpringOptions({ duration: 20000 }).
            Assert.That(physics, Is.EqualTo(expected).Within(1e-3).Percent);
        }

        [TestCase(Knob.Stiffness, 968186.45f)]
        [TestCase(Knob.Damping, 1377.5505f)]
        public void Given_ASpringDurationBelowTenMilliseconds_When_ItsPhysicsAreRead_Then_TheyAreThoseOfTenMilliseconds(Knob knob, float expected)
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.001f };

            // Act
            var physics = Physics(config, knob);

            // Assert — getSpringOptions({ duration: 1 }).
            Assert.That(physics, Is.EqualTo(expected).Within(1e-3).Percent);
        }

        [TestCase(Knob.Stiffness, 353.18960f)]
        [TestCase(Knob.Damping, 28.190009f)]
        public void Given_ASpringDescribedByADurationAndABounce_When_SolvedForAVelocity_Then_ItsPhysicsAreTheOnesThatVelocityGives(
            Knob knob, float expected)
        {
            // Arrange — findSpring's underdamped envelope and derivative both read the velocity; the expected pair
            // is its formulas evaluated for duration 0.5 s, bounce 0.25 and velocity -0.2, against 351.78 / 28.13
            // at rest.
            var description = new DurationSpringDescription(0.5f, 0.25f);

            // Act
            var spring = description.At(-0.2);

            // Assert
            Assert.That(Read(spring.Stiffness, spring.Damping, knob), Is.EqualTo(expected).Within(1e-3).Percent);
        }

        [TestCase(Knob.Stiffness, 134.43614f)]
        [TestCase(Knob.Damping, 23.189320f)]
        public void Given_ACriticallyDampedSpringDescribedByADuration_When_SolvedForAVelocity_Then_ItsPhysicsAreTheOnesThatVelocityGives(
            Knob knob, float expected)
        {
            // Arrange — the critically damped branch, bounce 0, for duration 0.8 s and velocity -0.5, against
            // 133.21 / 23.08 at rest.
            var description = new DurationSpringDescription(0.8f, 0f);

            // Act
            var spring = description.At(-0.5);

            // Assert
            Assert.That(Read(spring.Stiffness, spring.Damping, knob), Is.EqualTo(expected).Within(1e-3).Percent);
        }

        [TestCase(Knob.Stiffness, 353.18960f)]
        [TestCase(Knob.Damping, 28.190009f)]
        public void Given_ASpringItsDurationDescribes_When_AChannelIsReleasedWithAVelocity_Then_ItSolvesItsPhysicsForThatVelocity(
            Knob knob, float expected)
        {
            // Arrange — a translate moving at 200 px/s, which Framer's spring hands findSpring as -0.2.
            var plan = MotionSpringClassParser.Resolve(new[] { "translate-x-[0px]" }, new[] { "translate-x-[100px]" });
            var state = MotionSpringDriver.Create(plan, stiffness: 351.77878f, damping: 28.13365f, mass: 1f,
                settleSec: 0.5, description: new DurationSpringDescription(0.5f, 0.25f))!;
            var moving = new MotionDrawnValues { TranslateX = (40f, 200f), TranslateY = (0f, 0f) };

            // Act
            MotionSpringDriver.ContinueFrom(state, moving, fromDrawn: true);

            // Assert — NaN where the channel kept the state's own physics.
            var physics = state.TranslateX!.Physics;
            Assert.That(physics is { } own ? Read(own.Stiffness, own.Damping, knob) : float.NaN,
                Is.EqualTo(expected).Within(1e-3).Percent);
        }

        [TestCase(Knob.Stiffness, 352.13362f)]
        [TestCase(Knob.Damping, 28.147835f)]
        public void Given_AnOpacitySpringItsDurationDescribes_When_ReleasedFallingWithAVelocity_Then_ItSolvesForTheValuesOwnVelocity(
            Knob knob, float expected)
        {
            // Arrange — an opacity heading for 0, which Framer hands to the browser as an easing over a travel of
            // 100, released while the value moves at 50 per second. Framer passes the value's own velocity, so
            // findSpring reads -0.05, where the driver's start velocity on its downward travel would give +0.05.
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = MotionSpringDriver.Create(plan, stiffness: 351.77878f, damping: 28.13365f, mass: 1f,
                settleSec: 0.5, description: new DurationSpringDescription(0.5f, 0.25f))!;
            var moving = new MotionDrawnValues { Opacity = (0.6f, 50f) };

            // Act
            MotionSpringDriver.ContinueFrom(state, moving, fromDrawn: true);

            // Assert — NaN where the channel kept the state's own physics.
            var physics = state.Opacity!.Physics;
            Assert.That(physics is { } own ? Read(own.Stiffness, own.Damping, knob) : float.NaN,
                Is.EqualTo(expected).Within(1e-3).Percent);
        }

        [Test]
        public void Given_AnInstantSpring_When_AskedWhetherASwapStaggeredOnItLandsAtOnce_Then_ItDoesNot()
        {
            // Arrange — Framer's instant animation waits out the delay a stagger adds as it waits out its own.
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f };

            // Act
            var landsAtOnce = StyleAnimationScheduler.LandsAtOnce(config, additionalDelaySec: 0.1f);

            // Assert
            Assert.That(landsAtOnce, Is.False);
        }

        [Test]
        public void Given_AnInstantSpring_When_AskedWhetherASwapStaggeredOnItRunsOnSwap_Then_ItDoes()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f };

            // Act
            var runsOnSwap = StyleAnimationScheduler.RunsOnSwap(config, additionalDelaySec: 0.1f);

            // Assert
            Assert.That(runsOnSwap, Is.True);
        }

        [TestCase(10, false)]
        [TestCase(20, true)]
        public void Given_AnInstantSpringVariantEnterGivenAStagger_When_TimePasses_Then_ItHasCompletedOnlyOnceTheStaggerHasRun(
            int ticks, bool expected)
        {
            // Arrange — a 0.2 s stagger: 160 ms is inside it, 320 ms past it.
            var element = AttachedElement("opacity-100");
            var scheduler = new StyleAnimationScheduler();
            var completed = false;
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f },
                onComplete: () => completed = true, additionalDelaySec: 0.2f);

            // Act
            Ticks(ticks);

            // Assert
            Assert.That(completed, Is.EqualTo(expected));
        }

        [TestCase(10, false)]
        [TestCase(20, true)]
        public void Given_AnInstantSpringVariantExitGivenAStagger_When_TimePasses_Then_ItHasCompletedOnlyOnceTheStaggerHasRun(
            int ticks, bool expected)
        {
            // Arrange
            var element = AttachedElement("opacity-100");
            var scheduler = new StyleAnimationScheduler();
            var completed = false;
            var exit = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f }
                .WithExitClasses("opacity-100", "opacity-0");
            scheduler.PlayExit(element, exit, () => completed = true, restoreFromOnCancel: true, additionalDelaySec: 0.2f);

            // Act
            Ticks(ticks);

            // Assert
            Assert.That(completed, Is.EqualTo(expected));
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

        [TestCase(Knob.Stiffness, 151.27913f)]
        [TestCase(Knob.Damping, 17.219382f)]
        public void Given_ASpringMotionGivenADuration_When_ItsTransitionsPhysicsAreRead_Then_TheyAreTheOnesThatDurationDescribes(Knob knob, float expected)
        {
            // Arrange
            var spring = new StyleTransitionConfig { Type = TransitionType.Spring };

            // Act
            var node = V.Motion(key: "m", transition: spring, duration: 0.8f);

            // Assert
            Assert.That(Physics(node.Transition!, knob), Is.EqualTo(expected).Within(1e-3).Percent);
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

        // GREEN_ON_BASE(characterization): the base counts every spring exit as animated.
        [Test]
        public void Given_ASpringWithAZeroDuration_When_AskedWhetherItsExitAnimates_Then_ItDoes()
        {
            // Arrange — an instant spring exit still lands through the spring machinery, which a delay or stagger postpones.
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f };

            // Act
            var animates = config.HasExitAnimation;

            // Assert
            Assert.That(animates, Is.True);
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

        // GREEN_ON_BASE(characterization): the base never lands a spring, repeat delay or not.
        [Test]
        public void Given_ASpringWithAZeroDurationAndARepeatDelay_When_AskedWhetherASwapOnItLandsAtOnce_Then_ItDoesNot()
        {
            // Arrange — Framer makes a zero duration instant only where no repeat delay is set.
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Spring, DurationSec = 0f, Repeat = 1f, RepeatDelaySec = 0.1f,
            };

            // Act
            var landsAtOnce = StyleAnimationScheduler.LandsAtOnce(config);

            // Assert
            Assert.That(landsAtOnce, Is.False);
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

        [TestCase(10, false)]
        [TestCase(20, true)]
        public void Given_ASpringVariantEnterWithAZeroDurationAndADelay_When_TimePasses_Then_ItHasCompletedOnlyOnceTheDelayHasRun(
            int ticks, bool expected)
        {
            // Arrange — a 0.2 s delay: 160 ms is inside it, 320 ms past it.
            var element = AttachedElement("opacity-100");
            var scheduler = new StyleAnimationScheduler();
            var completed = false;
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f, DelaySec = 0.2f },
                onComplete: () => completed = true);

            // Act
            Ticks(ticks);

            // Assert
            Assert.That(completed, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base's physics spring holds its start through the delay too.
        [Test]
        public void Given_ASpringVariantEnterWithAZeroDurationAndADelay_When_InsideTheDelay_Then_ItHoldsItsStart()
        {
            // Arrange
            var element = AttachedElement("opacity-100");
            var scheduler = new StyleAnimationScheduler();
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0f, DelaySec = 0.2f });

            // Act — 160 ms, inside the delay.
            Ticks(10);

            // Assert — the start written inline, which a play landed at once would have left unset (NaN here).
            var opacity = element.style.opacity;
            Assert.That(opacity.keyword == StyleKeyword.Undefined ? opacity.value : float.NaN, Is.EqualTo(0f));
        }

        [TestCase(28, false)]
        [TestCase(34, true)]
        public void Given_ASpringVariantEnterNamingADuration_When_TimePasses_Then_ItHasCompletedOnlyOnceThatDurationHasRun(
            int ticks, bool expected)
        {
            // Arrange — its physics alone would settle this opacity spring 0.66s in.
            var element = AttachedElement("opacity-100");
            var scheduler = new StyleAnimationScheduler();
            var completed = false;
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f },
                onComplete: () => completed = true);

            // Act — 448ms or 544ms.
            Ticks(ticks);

            // Assert
            Assert.That(completed, Is.EqualTo(expected));
        }

        [TestCase(28, false)]
        [TestCase(34, true)]
        public void Given_ASpringVariantExitNamingADuration_When_TimePasses_Then_ItHasCompletedOnlyOnceThatDurationHasRun(
            int ticks, bool expected)
        {
            // Arrange — its physics alone would settle this opacity spring 0.66s in.
            var element = AttachedElement("opacity-100");
            var scheduler = new StyleAnimationScheduler();
            var completed = false;
            var exit = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f }
                .WithExitClasses("opacity-100", "opacity-0");
            scheduler.PlayExit(element, exit, () => completed = true, restoreFromOnCancel: true);

            // Act — 448ms or 544ms.
            Ticks(ticks);

            // Assert
            Assert.That(completed, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ASpringNamingADuration_When_ItsPlaySpanIsRead_Then_ItIsThatDuration()
        {
            // Arrange — its physics alone would rest this opacity's travel-100 easing later.
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f };

            // Act
            var span = MotionPlaySpan.Of(config, new[] { "opacity-0" }, new[] { "opacity-100" });

            // Assert
            Assert.That(span, Is.EqualTo(0.5).Within(1e-6));
        }

        // An exit-shaped translate channel, 0 → 100 px on Framer's main thread, ending 0.5 s after it starts,
        // stepped ten times.
        private static MotionSpringState MidExitSettlingSpring(VisualElement element)
        {
            var plan = MotionSpringClassParser.Resolve(new[] { "translate-x-[0px]" }, new[] { "translate-x-[100px]" });
            var state = MotionSpringDriver.Create(plan, stiffness: 387.27458f, damping: 27.55101f, mass: 1f,
                settleSec: 0.5)!;
            MotionSpringDriver.ApplyCurrentValues(element, state);
            for (var i = 0; i < 10; i++)
            {
                MotionSpringDriver.Step(element, state, FixedDeltaSec);
            }
            return state;
        }

        [Test]
        public void Given_ASpringSettlingAtASetTimeMidExit_When_Retargeted_Then_ItIsReleasedWithItsVelocity()
        {
            // Arrange — Framer's getSpringOptions keeps the value's velocity for a spring its duration describes.
            var state = MidExitSettlingSpring(new VisualElement());
            var moving = state.TranslateX!.Integrator.Velocity != 0f;

            // Act
            MotionSpringDriver.Retarget(state);

            // Assert — the start velocity the reversal is released with; 0 where nothing was moving.
            Assert.That(moving ? state.TranslateX.StartVelocity : 0f, Is.Not.EqualTo(0f));
        }

        [TestCase(28, false)]
        [TestCase(33, true)]
        public void Given_ASpringSettlingAtASetTimeMidExit_When_Retargeted_Then_ItHasSettledOnlyOnceThatLongHasPassed(
            int steps, bool expected)
        {
            // Arrange
            var element = new VisualElement();
            var state = MidExitSettlingSpring(element);

            // Act — 28 steps is 0.467s after the retarget and 33 steps 0.55s.
            MotionSpringDriver.Retarget(state);
            var settled = false;
            for (var i = 0; i < steps; i++)
            {
                settled |= MotionSpringDriver.Step(element, state, FixedDeltaSec);
            }

            // Assert
            Assert.That(settled, Is.EqualTo(expected));
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

        [TestCase(29, false)]
        [TestCase(31, true)]
        public void Given_ALayoutIdMoveOnASpringNamingADuration_When_Stepped_Then_ItHasArrivedOnlyOnceThatDurationHasRun(
            int steps, bool expected)
        {
            // Arrange — its physics alone would leave this move short of its rest threshold at 0.5s.
            var progress = LayoutIdTiming.From(new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f })
                .Start();

            // Act — 29 steps is 0.483s and 31 steps 0.517s.
            var arrived = false;
            for (var i = 0; i < steps; i++)
            {
                arrived |= progress.Step(FixedDeltaSec);
            }

            // Assert
            Assert.That(arrived, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ALayoutIdMoveOnASpringNamingADuration_When_Stepped_Then_ItArrivesOnTheLayout()
        {
            // Arrange
            var progress = LayoutIdTiming.From(new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f })
                .Start();

            // Act — 29 steps is 0.483s, short of the duration.
            for (var i = 0; i < 29; i++)
            {
                progress.Step(FixedDeltaSec);
            }
            var arrived = progress.Step(FixedDeltaSec) || progress.Step(FixedDeltaSec);

            // Assert — NaN where it has not arrived.
            Assert.That(arrived ? progress.Value : float.NaN, Is.EqualTo(0f));
        }
    }
}
