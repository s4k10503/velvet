using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Which of an <c>animate-*</c> loop and a Motion driver writing the same slot shows. A spring or bezier
    /// play keeps opacity from the loop while it drives it, and lets the loop take it back when it releases the
    /// channel; any other slot it writes, the loop's frame is written over. The loop here is mostly
    /// <c>animate-pulse</c>, whose opacity never drops below one half, against an opacity channel whose
    /// from-value is 0 — so an opacity of at least one half is the loop's write and 0 the driver's.
    /// </summary>
    [TestFixture]
    internal sealed class MotionLoopPrecedenceTests
    {
        private VisualElement _element;
        private StyleAnimateBinding _loop;
        private HeadlessEditorPanelHost _host;

        [SetUp]
        public void SetUp()
        {
            _element = new VisualElement();
            _loop = StyleAnimateDriver.Attach(_element, new AnimateSpec(AnimateMode.Pulse, 2f), panVertical: false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_loop != null)
            {
                StyleAnimateDriver.Detach(_element, _loop);
            }
            _host?.Dispose();
            _host = null;
        }

        private static MotionSpringClassParser.SpringPlan FadeIn()
            => MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });

        private static MotionSpringState Spring() => MotionSpringDriver.Create(FadeIn(), 100f, 20f, 1f);

        private static BezierTweenState Bezier() => BezierTweenDriver.Create(FadeIn(), 0.4f, 0f, 0.2f, 1f, 0.3f);

        // GREEN_ON_BASE(characterization): the base never writes the loop over a driver, so the driver's opacity
        // stands there too; `HoldAgainstLoop` left out of ApplyCurrentValues is what reddens it.
        [Test]
        public void Given_APulseLoop_When_ASpringWritesItsOpacityChannel_Then_TheSpringsOpacityStands()
        {
            // Arrange
            var state = Spring();

            // Act
            MotionSpringDriver.ApplyCurrentValues(_element, state);

            // Assert
            Assert.That(_element.style.opacity.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_APulseLoop_When_ASpringClearsItsOpacityChannel_Then_TheLoopsOpacityStands()
        {
            // Arrange
            var state = Spring();
            MotionSpringDriver.ApplyCurrentValues(_element, state);

            // Act
            MotionSpringDriver.ClearInlineOverrides(_element, state);

            // Assert
            Assert.That(_element.style.opacity.value, Is.GreaterThanOrEqualTo(0.5f));
        }

        [Test]
        public void Given_APulseLoop_When_ASpringReleasesItsOpacityChannel_Then_TheLoopsOpacityStands()
        {
            // Arrange
            var state = Spring();
            MotionSpringDriver.ApplyCurrentValues(_element, state);

            // Act
            MotionSpringDriver.ReleaseChannels(_element, state, StyleLonghandSet.Of(StyleLonghand.Opacity));

            // Assert
            Assert.That(_element.style.opacity.value, Is.GreaterThanOrEqualTo(0.5f));
        }

        // GREEN_ON_BASE(characterization): as the spring case above, over the bezier driver; `HoldAgainstLoop`
        // left out of its ApplyCurrentValues is what reddens it.
        [Test]
        public void Given_APulseLoop_When_ABezierWritesItsOpacityChannel_Then_TheBeziersOpacityStands()
        {
            // Arrange
            var state = Bezier();

            // Act
            BezierTweenDriver.ApplyCurrentValues(_element, state);

            // Assert
            Assert.That(_element.style.opacity.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_APulseLoopOnAPanelUnderASpringsOpacity_When_TheLoopTicks_Then_TheSpringsOpacityStands()
        {
            // Arrange — the loop's own tick runs on the panel scheduler, driven here on a fake clock.
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            var element = new VisualElement();
            _host.Root.Add(element);
            var pulse = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Pulse, 2f), panVertical: false);
            MotionSpringDriver.ApplyCurrentValues(element, Spring());

            // Act
            UseFrameFakeClockHost.Ms += 100;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            var opacity = element.style.opacity.value;
            StyleAnimateDriver.Detach(element, pulse);

            // Assert
            Assert.That(opacity, Is.EqualTo(0f));
        }

        [Test]
        public void Given_APulseLoop_When_ABezierClearsItsOpacityChannel_Then_TheLoopsOpacityStands()
        {
            // Arrange
            var state = Bezier();
            BezierTweenDriver.ApplyCurrentValues(_element, state);

            // Act
            BezierTweenDriver.ClearInlineOverrides(_element, state);

            // Assert
            Assert.That(_element.style.opacity.value, Is.GreaterThanOrEqualTo(0.5f));
        }

        [Test]
        public void Given_APulseLoop_When_ABezierReleasesItsOpacityChannel_Then_TheLoopsOpacityStands()
        {
            // Arrange
            var state = Bezier();
            BezierTweenDriver.ApplyCurrentValues(_element, state);

            // Act
            BezierTweenDriver.ReleaseChannels(_element, state, StyleLonghandSet.Of(StyleLonghand.Opacity));

            // Assert
            Assert.That(_element.style.opacity.value, Is.GreaterThanOrEqualTo(0.5f));
        }

        [Test]
        public void Given_ASpinLoopAQuarterThroughItsTurn_When_ASpringWritesItsRotateChannel_Then_TheRotateIsTheQuarterTurn()
        {
            // Arrange — a slow spin, started a quarter of its loop ago.
            var element = new VisualElement();
            var spin = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Spin, 1000f), panVertical: false);
            spin.StartTime = UnityEngine.Time.realtimeSinceStartupAsDouble - 250.0;
            var state = MotionSpringDriver.Create(
                MotionSpringClassParser.Resolve(new[] { "rotate-0" }, new[] { "rotate-45" }), 100f, 20f, 1f);

            // Act
            MotionSpringDriver.ApplyCurrentValues(element, state);
            var degrees = element.style.rotate.value.angle.ToDegrees();
            StyleAnimateDriver.Detach(element, spin);

            // Assert
            Assert.That(degrees, Is.EqualTo(90f).Within(0.5f));
        }

        // GREEN_ON_BASE(characterization): a detached loop leaves the slot to the driver, as the base does by
        // never reasserting a loop at all; `s_running.Remove` left out of Detach is what reddens it.
        [Test]
        public void Given_APulseLoopThatHasDetached_When_ASpringWritesItsOpacityChannel_Then_TheSpringsOpacityStands()
        {
            // Arrange
            StyleAnimateDriver.Detach(_element, _loop);
            _loop = null;
            var state = Spring();

            // Act
            MotionSpringDriver.ApplyCurrentValues(_element, state);

            // Assert
            Assert.That(_element.style.opacity.value, Is.EqualTo(0f));
        }
    }
}
