using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class SideRadiusRemainderTests
    {
        [TearDown]
        public void TearDown() => VelvetTheme.IsDark = false;

        private static VisualElement Mount(ReconcilerScope scope, string className)
        {
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[] { V.Div(className: className, name: "card") });
            return scope.Root.Q<VisualElement>("card");
        }

        [Test]
        public void Given_ALeftRadiusAndADarkTopRadius_When_TheThemeTurnsDark_Then_TheTopRadiusAndTheLeftsOtherCornerPaint()
        {
            // Arrange — the two classes share the top-left corner and neither holds the other's corners.
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "rounded-l-lg dark:rounded-t-lg");

            // Act
            VelvetTheme.IsDark = true;

            // Assert — the top class owns the shared corner, and the left class is replaced by the class for the corner
            // it still owns.
            Assert.That(
                (card.ClassListContains("rounded-t-lg"), card.ClassListContains("rounded-l-lg"),
                    card.ClassListContains("rounded-bl-lg")),
                Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_ALeftRadiusReducedToItsOtherCorner_When_TheThemeTurnsLight_Then_TheLeftRadiusIsWholeAgain()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "rounded-l-lg dark:rounded-t-lg");
            VelvetTheme.IsDark = true;
            var dark = (card.ClassListContains("rounded-t-lg"), card.ClassListContains("rounded-l-lg"),
                card.ClassListContains("rounded-bl-lg"));

            // Act
            VelvetTheme.IsDark = false;

            // Assert — the dark half says the left radius was reduced, so the light half is a restoration.
            Assert.That(
                (dark, (card.ClassListContains("rounded-t-lg"), card.ClassListContains("rounded-l-lg"),
                    card.ClassListContains("rounded-bl-lg"))),
                Is.EqualTo(((true, false, true), (false, true, false))));
        }
    }
}
