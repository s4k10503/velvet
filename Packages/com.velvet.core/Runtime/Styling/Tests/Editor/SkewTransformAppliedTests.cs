using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class SkewTransformAppliedTests
    {
        private static VisualElement Mount(ReconcilerScope scope, string className)
        {
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[] { V.Div(className: className, name: "card") });
            return scope.Root.Q<VisualElement>("card");
        }

        private static (bool Transformed, bool Painted, StyleKeyword Rotate) Read(ReconcilerScope scope, VisualElement card)
        {
            var context = scope.Reconciler.Context;
            return (context.SkewTransformed.ContainsKey(card), context.SkewBindings.ContainsKey(card),
                card.style.rotate.keyword);
        }

        [Test]
        public void Given_APlainSkew_When_Mounted_Then_ItIsTheElementsTransformAndNothingIsPainted()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var card = Mount(scope, "skew-x-12");

            // Assert
            Assert.That(Read(scope, card), Is.EqualTo((true, false, StyleKeyword.Undefined)));
        }

        [Test]
        public void Given_ASkewBesideARotate_When_Mounted_Then_ItStaysATransformAndNothingIsPainted()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var card = Mount(scope, "skew-x-12 rotate-45");

            // Assert
            Assert.That(Read(scope, card), Is.EqualTo((true, false, StyleKeyword.Undefined)));
        }

        [TestCase("skew-x-12 rotate-45", 45f, 1f, 1f, 12f, 0f, TestName = "Given_ASkewBesideARotateClass_When_Mounted_Then_TheSlotsCarryTheComposite")]
        [TestCase("skew-x-12 -rotate-45", -45f, 1f, 1f, 12f, 0f, TestName = "Given_ASkewBesideANegativeRotateClass_When_Mounted_Then_TheSlotsCarryTheComposite")]
        [TestCase("skew-x-12 rotate-[30deg]", 30f, 1f, 1f, 12f, 0f, TestName = "Given_ASkewBesideAnArbitraryRotate_When_Mounted_Then_TheSlotsCarryTheComposite")]
        [TestCase("skew-6 scale-110", 0f, 1.1f, 1.1f, 6f, 6f, TestName = "Given_ASkewBesideAScaleClass_When_Mounted_Then_TheSlotsCarryTheComposite")]
        [TestCase("skew-x-12 scale-110 scale-x-50", 0f, 0.5f, 1.1f, 12f, 0f, TestName = "Given_ASkewBesideAnAxisScaleOverAUniformOne_When_Mounted_Then_TheSlotsCarryTheComposite")]
        [TestCase("-skew-y-6 rotate-45 scale-x-[-1]", 45f, -1f, 1f, 0f, -6f, TestName = "Given_ASkewBesideARotateAndAMirroringScale_When_Mounted_Then_TheSlotsCarryTheComposite")]
        public void Given_ASkewBesideRotateAndScale_When_Mounted_Then_TheSlotsCarryTheComposite(
            string className, float rotateDeg, float scaleX, float scaleY, float xDeg, float yDeg)
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var card = Mount(scope, className);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(card),
                Is.EqualTo(SkewCompositeMath.Composite(rotateDeg, scaleX, scaleY, xDeg, yDeg)).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewedElement_When_ARotateClassIsAddedByAPatch_Then_TheSlotsCarryTheComposite()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "skew-x-12", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new VNode[] { V.Div(className: "skew-x-12 rotate-45", name: "card") });

            // Assert
            Assert.That(SkewCompositeMath.Drawn(card),
                Is.EqualTo(SkewCompositeMath.Composite(45f, 1f, 1f, 12f, 0f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewBesideARotate_When_TheRotateClassIsRemovedByAPatch_Then_TheSlotsCarryTheShear()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "skew-x-12 rotate-45", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new VNode[] { V.Div(className: "skew-x-12", name: "card") });

            // Assert
            Assert.That(SkewCompositeMath.Drawn(card),
                Is.EqualTo(SkewCompositeMath.Composite(0f, 1f, 1f, 12f, 0f)).Within(1e-4f));
        }

        [TestCase(true, 45f, TestName = "Given_ASkewedElement_When_AVariantPutsARotateOn_Then_TheSlotsCarryTheComposite")]
        [TestCase(false, 0f, TestName = "Given_ASkewedElementUnderAVariantRotate_When_TheVariantLeaves_Then_TheSlotsCarryTheShear")]
        public void Given_ASkewedElement_When_AVariantTogglesARotate_Then_TheSlotsFollow(bool on, float rotateDeg)
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "skew-x-12");
            if (!on)
            {
                StyleVariantPayload.Apply(card, new[] { "rotate-45" }, true, StyleLayerPriority.Hover);
            }

            // Act
            StyleVariantPayload.Apply(card, new[] { "rotate-45" }, on, StyleLayerPriority.Hover);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(card),
                Is.EqualTo(SkewCompositeMath.Composite(rotateDeg, 1f, 1f, 12f, 0f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewedElement_When_TheSkewIsRemoved_Then_ItsTransformSlotsAreGivenBack()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "skew-x-12", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new VNode[] { V.Div(className: "p-1", name: "card") });

            // Assert
            Assert.That(Read(scope, card), Is.EqualTo((false, false, StyleKeyword.Null)));
        }
    }
}
