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
    }
}
