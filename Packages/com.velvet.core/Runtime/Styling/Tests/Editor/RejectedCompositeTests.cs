using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the families Tailwind composes through one declaration — translate, the per-axis and uniform
    /// scale, skew, and the filters — where a value the browser rejects in one utility discards the family's
    /// declaration, and with it the family's other utilities, wherever the rejected value holds its property.
    /// </summary>
    internal sealed class RejectedCompositeTests
    {
        [TestCase("translate-x-4 translate-y-[10]", "")]
        [TestCase("scale-x-[2] scale-y-[2px]", "")]
        [TestCase("scale-50 scale-x-[abc]", "")]
        [TestCase("skew-x-6 skew-y-[10]", "")]
        [TestCase("blur-sm brightness-[abc]", "")]
        [TestCase("drop-shadow-lg blur-[2]", "")]
        [TestCase("blur-[-2px]", "")]
        [TestCase("blur-sm p-4 hue-rotate-[45]", "p-4")]
        [TestCase("!translate-y-[10] translate-x-4", "")]
        [TestCase("translate-y-[10] translate-y-2 translate-x-4", "")]
        [TestCase("translate-x-4 hover:translate-y-[10]", "translate-x-4 hover:translate-x-0")]
        [TestCase("drop-shadow-lg hover:blur-[2]", "drop-shadow-lg hover:drop-shadow-none")]
        [TestCase("translate-y-[10] translate-x-4 hover:translate-y-2", "hover:translate-x-4 hover:translate-y-2")]
        [TestCase("dark:translate-x-4 hover:translate-y-[10]", "dark:translate-x-4 dark:hover:translate-x-0")]
        public void Given_AFamilyHoldingARejectedValue_When_Normalized_Then_TheFamilyIsDropped(string classes, string kept)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(kept));
        }

        // GREEN_ON_BASE(characterization): every value here is one the browser takes, so the base kept them too.
        [TestCase("translate-x-4 translate-y-[10px]")]
        [TestCase("scale-x-[2] scale-y-[50%]")]
        [TestCase("skew-x-6 skew-y-[0]")]
        [TestCase("blur-sm brightness-[1.5]")]
        [TestCase("blur-sm hue-rotate-[.5TURN]")]
        public void Given_AFamilyOfValuesTheBrowserTakes_When_Normalized_Then_ItIsKept(string classes)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(classes));
        }

        // GREEN_ON_BASE(characterization): the base kept both tokens, and Tailwind emits blur-md after blur-[2].
        [TestCase("blur-[2] blur-md")]
        [TestCase("blur-md blur-[2]")]
        public void Given_ARejectedValueAnotherHoldsThePropertyOver_When_Normalized_Then_TheFamilyIsKept(string classes)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(classes));
        }

        [Test]
        public void Given_ATranslationAndAHoverValueTheBrowserRejects_When_Hovered_Then_NothingIsTranslated()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "box", className: "translate-x-4 hover:translate-y-[10]"));
            var box = root.Q("box");
            var before = box.style.translate.value.x.value;

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                box.SimulateEvent(over);
            }

            // Assert — NaN where the translation was not drawn before the hover either.
            Assert.That(before == 16f ? box.style.translate.value.x.value : float.NaN, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ADropShadowBesideARingColourTheBrowserRejects_When_Mounted_Then_TheDropShadowIsDrawn()
        {
            // Arrange
            const string classes = "ring ring-[5PX] drop-shadow-lg";
            var root = new VisualElement();
            var ringDiscarded = !StyleRingClass.TryExtract(classes.Split(' '), out _);

            // Act
            using var mounted = V.Mount(root, V.Div(name: "box", className: classes));

            // Assert — -1 where the ring was drawn, which leaves no discarded box-shadow to survive.
            Assert.That(ringDiscarded ? root.Q("box").style.filter.value?.Count ?? 0 : -1, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base kept both tokens, as a dropped plain declaration takes no sibling.
        [TestCase("scale-x-[2] scale-[2px]")]
        [TestCase("rotate-45 rotate-[45]")]
        public void Given_APlainDeclarationTheBrowserDrops_When_Normalized_Then_ItsNeighbourStays(string classes)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(classes));
        }
    }
}
