using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Which bands a reconciled element's ring overlay draws, and in what width and colour: the ring Tailwind
    /// composes into box-shadow, the ring-offset fill over it, and the outline, each its own band. Read off the
    /// inline style the overlay writes, which needs no panel. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class RingBandReconcileTests
    {
        private static void Mount(ReconcilerScope scope, string className)
            => scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: className, name: "card") });

        // A band by its name under the overlay beside the card. Named rather than read off RingOverlay's
        // constants, so a tree that draws no such child reports null here instead of failing to build.
        private static VisualElement Band(ReconcilerScope scope, string name)
        {
            for (var i = 0; i < scope.Root.childCount; i++)
            {
                if (scope.Root[i].ClassListContains(RingOverlay.MarkerClass))
                {
                    return scope.Root[i].Q<VisualElement>(name);
                }
            }
            return null;
        }

        private static float? Width(VisualElement band)
            => band == null ? (float?)null : band.style.display.value == DisplayStyle.None ? 0f : band.style.borderTopWidth.value;

        [Test]
        public void Given_ARingAndAnOutline_When_Reconciled_Then_EachBandCarriesItsOwnWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, "ring-4 outline-2");

            // Assert
            Assert.That((Width(Band(scope, "velvet-ring-band")), Width(Band(scope, "velvet-outline-band"))),
                Is.EqualTo(((float?)4f, (float?)2f)));
        }

        [Test]
        public void Given_ARingOffsetNamingNoColour_When_Reconciled_Then_TheOffsetIsFilledWhite()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, "ring-2 ring-offset-2 ring-[#ff0000]");

            // Assert — --tw-ring-offset-color's initial value is #fff.
            var fill = Band(scope, "velvet-ring-offset-band");
            Assert.That((Width(fill), fill?.style.borderTopColor.value), Is.EqualTo(((float?)2f, (Color?)Color.white)));
        }

        [Test]
        public void Given_ARingOffsetColour_When_Reconciled_Then_TheFillTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            StyleColorValueParser.TryParseColor("#00ff00".AsSpan(), out var green);

            // Act
            Mount(scope, "ring-2 ring-offset-2 ring-offset-[#00ff00]");

            // Assert
            Assert.That(Band(scope, "velvet-ring-offset-band")?.style.borderTopColor.value, Is.EqualTo((Color?)green));
        }

        [Test]
        public void Given_ARingWithAnOffset_When_Reconciled_Then_TheRingBandSpansTheOffset()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, "ring-2 ring-offset-4");

            // Assert — the ring's spread is its width plus the offset, under the fill, so a translucent fill shows
            // the ring through it as the browser does.
            Assert.That(Width(Band(scope, "velvet-ring-band")), Is.EqualTo((float?)6f));
        }

        [Test]
        public void Given_AShadowBesideARingOffset_When_Reconciled_Then_OnlyTheOffsetFillIsDrawn()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, "shadow-lg ring-offset-2");

            // Assert — the box-shadow a shadow-* writes carries the offset fill and no ring.
            Assert.That((Width(Band(scope, "velvet-ring-offset-band")), Width(Band(scope, "velvet-ring-band"))),
                Is.EqualTo(((float?)2f, (float?)0f)));
        }
    }
}
