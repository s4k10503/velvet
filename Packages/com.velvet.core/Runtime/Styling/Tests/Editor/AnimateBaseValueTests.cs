using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// What an <c>animate-*</c> loop writes on a real panel when its element's classes already give the slot a value.
    /// Tailwind's keyframes name no value at the frames they leave out, so those take the element's own: a pulse runs
    /// between its element's opacity and one half, a spin turns on from its element's rotation, a ping scales and fades
    /// from its element's scale and opacity, and a bounce lifts from its element's translation. The bundled stylesheet
    /// is attached, so a named class is read off the cascade and an arbitrary one off the inline value the
    /// reconciler wrote; every case drives <see cref="StyleAnimateDriver.ApplyFrame"/> at a chosen phase. GWT, one
    /// assert each.
    /// </summary>
    [TestFixture]
    internal sealed class AnimateBaseValueTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private (VisualElement element, StyleAnimateBinding binding) Mount(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: className, name: "card"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var element = _window.rootVisualElement[0];
            _mounted.Root.Reconciler.Context.AnimationBindings.TryGetValue(element, out var binding);
            return (element, binding);
        }

        private StyleAnimateBinding _attached;
        private VisualElement _attachedTo;

        // Mounts className without an animate-* class and attaches the loop to it directly, so the element's own
        // value is whatever the classes alone gave it. The reconciler does not know this loop; teardown detaches it.
        private (VisualElement element, StyleAnimateBinding binding) MountAndAttach(string className, AnimateMode mode)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: className, name: "card"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var element = _window.rootVisualElement[0];
            _attachedTo = element;
            _attached = StyleAnimateDriver.Attach(element, new AnimateSpec(mode, 1f), panVertical: false);
            return (element, _attached);
        }

        public override void TearDown()
        {
            VelvetTheme.IsDark = false;
            if (_attached != null)
            {
                StyleAnimateDriver.Detach(_attachedTo, _attached);
                _attached = null;
            }
            base.TearDown();
        }

        [Test]
        public void Given_APulseOverANamedOpacity_When_AQuarterThroughItsLoop_Then_ItIsHalfwayFromThatOpacityToHalf()
        {
            // Arrange
            var (element, binding) = Mount("w-[40px] h-[40px] bg-red-500 opacity-75 animate-pulse");

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0.25f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.625f).Within(1e-4f));
        }

        [Test]
        public void Given_APulseOverANamedOpacity_When_TheClassChangesUnderIt_Then_ItStartsFromTheNewOpacity()
        {
            // Arrange
            const string before = "w-[40px] h-[40px] bg-red-500 opacity-75 animate-pulse";
            const string after = "w-[40px] h-[40px] bg-red-500 opacity-50 animate-pulse";
            var (element, binding) = Mount(before);
            _mounted!.Root.Reconciler.Reconcile(
                _window.rootVisualElement,
                new VNode[] { V.Div(className: before, name: "card") },
                new VNode[] { V.Div(className: after, name: "card") });
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Given_APulseOverAnArbitraryOpacity_When_ItsLoopStarts_Then_ItStartsFromThatOpacity()
        {
            // Arrange
            var (element, binding) = Mount("w-[40px] h-[40px] bg-red-500 opacity-[.3] animate-pulse");

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.3f).Within(1e-5f));
        }

        [Test]
        public void Given_APulseWhoseElementIsGivenAnOpacityAfterItAttached_When_ItsLoopStarts_Then_ItStartsFromThatOpacity()
        {
            // Arrange — written by something other than the loop, as a re-render writes an arbitrary opacity.
            var (element, binding) = Mount("w-[40px] h-[40px] bg-red-500 animate-pulse");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);
            element.style.opacity = 0.6f;

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.6f).Within(1e-5f));
        }

        // GREEN_ON_BASE(characterization): an element with no opacity class pulses from full opacity on the base as
        // here. It is the control for the foreign-write test above: a loop that took its own frame for the
        // element's opacity would start this one from half.
        [Test]
        public void Given_APulseOverNoOpacity_When_ItsLoopStarts_Then_ItStartsFromFullOpacity()
        {
            // Arrange
            var (element, binding) = Mount("w-[40px] h-[40px] bg-red-500 animate-pulse");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_ASpinOverANamedRotation_When_AQuarterThroughItsLoop_Then_ItHasTurnedAQuarterFromThatRotation()
        {
            // Arrange
            var (element, binding) = Mount("w-[40px] h-[40px] bg-red-500 rotate-45 animate-spin");

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0.25f);

            // Assert
            Assert.That(element.style.rotate.value.angle.ToDegrees(), Is.EqualTo(135f).Within(1e-3f));
        }

        [Test]
        public void Given_ASpinOverAnArbitraryRotation_When_AQuarterThroughItsLoop_Then_ItHasTurnedAQuarterFromThatRotation()
        {
            // Arrange
            var (element, binding) = Mount("w-[40px] h-[40px] bg-red-500 rotate-[30deg] animate-spin");

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0.25f);

            // Assert
            Assert.That(element.style.rotate.value.angle.ToDegrees(), Is.EqualTo(120f).Within(1e-3f));
        }

        [Test]
        public void Given_APingAtItsEndKeyframe_When_FrameApplied_Then_TheElementIsTwiceItsSizeAndGone()
        {
            // Arrange
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500", AnimateMode.Ping);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0.75f);

            // Assert
            Assert.That((element.style.opacity.value, element.style.scale.value.value.x), Is.EqualTo((0f, 2f)));
        }

        [Test]
        public void Given_APingOverAnOpacityAndAScale_When_ItsLoopStarts_Then_ItStartsFromThoseValues()
        {
            // Arrange
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500 opacity-75 scale-50", AnimateMode.Ping);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That((element.style.opacity.value, element.style.scale.value.value.x), Is.EqualTo((0.75f, 0.5f)));
        }

        [Test]
        public void Given_APingOverAnOpacityAndAScale_When_ItReachesItsEndKeyframe_Then_ItIsGoneAndTheScaleIsDoubled()
        {
            // Arrange
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500 opacity-75 scale-50", AnimateMode.Ping);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0.75f);

            // Assert
            Assert.That((element.style.opacity.value, element.style.scale.value.value.x), Is.EqualTo((0f, 1f)));
        }

        [Test]
        public void Given_ARunningPing_When_Detached_Then_BothSlotsAreHandedBack()
        {
            // Arrange
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500", AnimateMode.Ping);
            StyleAnimateDriver.ApplyFrame(element, binding, 0.75f);

            // Act
            StyleAnimateDriver.Detach(element, binding);

            // Assert
            Assert.That((element.style.opacity.keyword, element.style.scale.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ABounceAtItsFirstKeyframe_When_FrameApplied_Then_TheElementIsAQuarterOfItsHeightUp()
        {
            // Arrange
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500", AnimateMode.Bounce);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That(element.style.translate.value.y.value, Is.EqualTo(-10f).Within(1e-3f));
        }

        [Test]
        public void Given_ABounceAtItsMiddleKeyframe_When_FrameApplied_Then_TheElementIsDown()
        {
            // Arrange
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500", AnimateMode.Bounce);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            // Assert
            Assert.That(element.style.translate.value.y.value, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void Given_ABounceOverATranslation_When_ItIsDown_Then_TheElementIsAtThatTranslation()
        {
            // Arrange
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500 translate-y-[40px]", AnimateMode.Bounce);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            // Assert
            Assert.That(element.style.translate.value.y.value, Is.EqualTo(40f).Within(1e-3f));
        }

        [Test]
        public void Given_ABounceOverAScale_When_ItIsFullyLifted_Then_TheLiftIsScaledWithTheElement()
        {
            // Arrange
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500 scale-50", AnimateMode.Bounce);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That(element.style.translate.value.y.value, Is.EqualTo(-5f).Within(1e-3f));
        }

        [Test]
        public void Given_ABounceOverARotation_When_ItIsFullyLifted_Then_TheLiftRunsAlongTheTurnedAxis()
        {
            // Arrange
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500 rotate-90", AnimateMode.Bounce);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That(element.style.translate.value.x.value, Is.EqualTo(10f).Within(1e-3f));
        }

        [Test]
        public void Given_ARunningBounce_When_Detached_Then_TheTranslateIsHandedBack()
        {
            // Arrange
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500", AnimateMode.Bounce);
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Act
            StyleAnimateDriver.Detach(element, binding);

            // Assert
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_APulseOverAHoverOpacity_When_ThePointerIsOverIt_Then_ItStartsFromTheHoverOpacity()
        {
            // Arrange
            var (element, binding) = Mount("w-[40px] h-[40px] bg-red-500 hover:opacity-50 animate-pulse");
            using (var over = PointerOverEvent.GetPooled())
            {
                element.SimulateEvent(over);
            }
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Given_APulseOverADarkOpacity_When_TheThemeTurnsDark_Then_ItStartsFromTheDarkOpacity()
        {
            // Arrange
            var (element, binding) = Mount("w-[40px] h-[40px] bg-red-500 dark:opacity-50 animate-pulse");
            VelvetTheme.IsDark = true;
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Given_ABounceOverAPercentTranslation_When_ItIsDown_Then_TheElementIsAtThatShareOfItsHeight()
        {
            // Arrange — translate-y-1/2 is half of the element's own 40px height.
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500 translate-y-1/2", AnimateMode.Bounce);

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            // Assert
            Assert.That(element.style.translate.value.y.value, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void Given_APulseUnderACrossfadeThatDrawsTheElement_When_ItsFrameIsWritten_Then_TheCrossfadesFrameIsNotItsOwn()
        {
            // Arrange — the crossfade draws the element at half of whatever opacity it is given.
            var (element, binding) = Mount("w-[40px] h-[40px] bg-red-500 animate-pulse");
            MotionOpacity.Draw(element, new LayoutIdFade(0f, 0.5f, 1f), 0f);
            try
            {
                // Act
                StyleAnimateDriver.ApplyFrame(element, binding, 0f);

                // Assert — the pulse is at full opacity here, so half of it. Its own taken from the crossfade's last
                // frame would halve it again.
                Assert.That(element.style.opacity.value, Is.EqualTo(0.5f).Within(1e-4f));
            }
            finally
            {
                MotionOpacity.Forget(element);
            }
        }

        // The probe counts nothing if the instrument is stuck, so this is the case that asserts a count above zero.
        // GREEN_ON_BASE(characterization): the probe already counts this canary's allocation.
        [Test]
        [Category("Performance")]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Arrange
            System.Action canary = () => System.GC.KeepAlive(new byte[16]);
            canary();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(canary);

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        [Category("Performance")]
        public void Given_AWarmPulseOverANamedOpacity_When_ItTicksAgain_Then_ItAllocatesNothing()
        {
            // Arrange — the first frame reads the cascade; the ticks after it find the class list unchanged.
            var (element, binding) = Mount("w-[40px] h-[40px] bg-red-500 opacity-75 animate-pulse");
            System.Action tick = () => StyleAnimateDriver.ApplyFrame(element, binding, 0.25f);
            tick();

            // Act / Assert
            Assert.That(GCAllocationProbe.MedianBlocksDuring(tick), Is.Zero);
        }

        [Test]
        [Category("Performance")]
        public void Given_AWarmBounceOverANamedScaleAndRotation_When_ItTicksAgain_Then_ItAllocatesNothing()
        {
            // Arrange
            var (element, binding) = MountAndAttach("w-[40px] h-[40px] bg-red-500 scale-50 rotate-45", AnimateMode.Bounce);
            System.Action tick = () => StyleAnimateDriver.ApplyFrame(element, binding, 0.25f);
            tick();

            // Act / Assert
            Assert.That(GCAllocationProbe.MedianBlocksDuring(tick), Is.Zero);
        }

        [Test]
        public void Given_APulseWhoseOpacityIsGivenBySomethingElse_When_ADriverOfAnotherSlotReasserts_Then_ThatOpacityIsTakenAsItsOwn()
        {
            // Arrange — a spring on rotate drives nothing the pulse writes.
            var element = new VisualElement();
            var pulse = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Pulse, 2f), panVertical: false);
            try
            {
                element.style.opacity = 0.6f;

                // Act
                StyleAnimateDriver.ReassertLoop(element, MotionTransitionSlots.Rotate);

                // Assert — the pulse has run for next to no time, so it is still at its own opacity.
                Assert.That(element.style.opacity.value, Is.EqualTo(0.6f).Within(0.01f));
            }
            finally
            {
                StyleAnimateDriver.Detach(element, pulse);
            }
        }

        [Test]
        public void Given_APulseUnderADriverOfItsOpacity_When_TheDriverReasserts_Then_TheDriversOpacityIsNotItsOwn()
        {
            // Arrange
            var element = new VisualElement();
            var pulse = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Pulse, 2f), panVertical: false);
            try
            {
                element.style.opacity = 0.6f;

                // Act
                StyleAnimateDriver.ReassertLoop(element, MotionTransitionSlots.Opacity);

                // Assert — the pulse starts from full opacity, which nothing gave this element a class for.
                Assert.That(element.style.opacity.value, Is.EqualTo(1f).Within(0.01f));
            }
            finally
            {
                StyleAnimateDriver.Detach(element, pulse);
            }
        }
    }
}
