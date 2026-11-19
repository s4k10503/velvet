using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// An animate-* preset on a skewed element draws its frame into the element's own rotate or scale, which the skew
    /// is composed with rather than replaced by. Frames are applied at a chosen phase as
    /// <see cref="AnimateMotionPanelTests"/> does.
    /// </summary>
    [TestFixture]
    internal sealed class SkewAnimatePanelTests : PanelTestBase
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

        [Test]
        public void Given_ASkewedSpin_When_AQuarterThroughItsLoop_Then_TheSlotsCarryTheTurnAndTheSkew()
        {
            // Arrange
            var (element, binding) = Mount("w-[40px] h-[40px] skew-x-12 animate-spin");

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0.25f);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(element),
                Is.EqualTo(SkewCompositeMath.Composite(90f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASkewedSpinBesideARotateClass_When_AQuarterThroughItsLoop_Then_TheTurnAddsToTheRotate()
        {
            // Arrange
            var (element, binding) = Mount("w-[40px] h-[40px] skew-x-12 rotate-45 animate-spin");

            // Act
            StyleAnimateDriver.ApplyFrame(element, binding, 0.25f);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(element),
                Is.EqualTo(SkewCompositeMath.Composite(135f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASkewedSpin_When_TheLoopIsDetached_Then_TheSlotsCarryTheSkewAlone()
        {
            // Arrange
            var (element, binding) = Mount("w-[40px] h-[40px] skew-x-12 animate-spin");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.25f);

            // Act
            StyleAnimateDriver.Detach(element, binding);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(element),
                Is.EqualTo(SkewCompositeMath.Composite(0f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }
    }
}
