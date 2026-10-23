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
        public void Given_ASkewBesideARotate_When_Mounted_Then_ThePaintedLayerKeepsIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var card = Mount(scope, "skew-x-12 rotate-45");

            // Assert
            Assert.That(Read(scope, card), Is.EqualTo((false, true, StyleKeyword.Null)));
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
