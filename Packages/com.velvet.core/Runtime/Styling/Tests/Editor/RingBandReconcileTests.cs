using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Which bands a reconciled element's ring binding draws, how wide, and in what colour: the ring Tailwind
    /// composes into box-shadow, the ring-offset fill over it and the outline. Read off the binding, which needs no
    /// panel. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class RingBandReconcileTests
    {
        private static RingBinding Bind(ReconcilerScope scope, string className)
        {
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: className, name: "card") });
            return RingOverlay.TryGet(scope.Root.Q<VisualElement>("card"));
        }

        // How wide band paints: NaN where it is hidden or there is no band, which no width equals.
        private static float Shown(VisualElement band, float width)
            => band == null || band.style.display.value == DisplayStyle.None ? float.NaN : width;

        [Test]
        public void Given_ARingAndAnOutline_When_Reconciled_Then_TheRingBandCarriesTheRingWidth()
        {
            using var scope = new ReconcilerScope();

            var binding = Bind(scope, "ring-4 outline-2");

            Assert.That(Shown(binding?.RingElement, binding?.Spec.RingSpread ?? 0f), Is.EqualTo(4f));
        }

        [Test]
        public void Given_ARingAndAnOutline_When_Reconciled_Then_TheOutlineBandCarriesTheOutlineWidth()
        {
            using var scope = new ReconcilerScope();

            var binding = Bind(scope, "ring-4 outline-2");

            Assert.That(Shown(binding?.OutlineElement, binding?.Spec.Outline.Width ?? 0f), Is.EqualTo(2f));
        }

        [Test]
        public void Given_AnOutlineAlone_When_Reconciled_Then_TheRingBandIsHidden()
        {
            using var scope = new ReconcilerScope();

            var binding = Bind(scope, "outline-2");

            Assert.That(binding.RingElement.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Given_ARingOffsetNamingNoColour_When_Reconciled_Then_TheOffsetIsFilledWhite()
        {
            using var scope = new ReconcilerScope();

            var binding = Bind(scope, "ring-2 ring-offset-2 ring-[#ff0000]");

            // --tw-ring-offset-color's initial value is #fff.
            Assert.That(binding.OffsetTint, Is.EqualTo(Color.white));
        }

        [Test]
        public void Given_ARingOffsetColour_When_Reconciled_Then_TheFillTakesIt()
        {
            using var scope = new ReconcilerScope();
            StyleColorValueParser.TryParseColor("#00ff00".AsSpan(), out var green);

            var binding = Bind(scope, "ring-2 ring-offset-2 ring-offset-[#00ff00]");

            Assert.That(binding.OffsetTint, Is.EqualTo(green));
        }

        [Test]
        public void Given_AShadowBesideARingOffset_When_Reconciled_Then_TheOffsetFillIsDrawn()
        {
            using var scope = new ReconcilerScope();

            var binding = Bind(scope, "shadow-lg ring-offset-2");

            // The box-shadow a shadow-* writes carries the offset fill and no ring.
            Assert.That(Shown(binding?.OffsetElement, binding?.Spec.OffsetSpread ?? 0f), Is.EqualTo(2f));
        }

        [Test]
        public void Given_AShadowBesideARingOffset_When_Reconciled_Then_NoRingBandIsDrawn()
        {
            using var scope = new ReconcilerScope();

            var binding = Bind(scope, "shadow-lg ring-offset-2");

            Assert.That(binding.RingElement.style.display.value, Is.EqualTo(DisplayStyle.None));
        }
    }
}
