using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="SilhouetteFaceStash"/>'s <c>includeBackground</c> flag. The skew / drop-shadow
    /// layers own the whole face, so they suppress and repaint BOTH the background and the border (the default
    /// <c>true</c>). The border-dashed layer restyles only the border, so it constructs with
    /// <c>includeBackground:false</c> and must never touch the background — it suppresses the border color while
    /// leaving the fill exactly as authored. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class SilhouetteFaceStashIncludeBackgroundTests
    {
        private static readonly Color Fill = new(0.2f, 0.4f, 0.6f, 1f);
        private static readonly Color Border = new(1f, 0f, 0f, 1f);

        private static VisualElement ColoredElement()
        {
            var element = new VisualElement();
            element.style.backgroundColor = Fill;
            element.style.borderLeftColor = Border;
            return element;
        }

        [Test]
        public void Given_ABorderOnlyStash_When_Stashed_Then_TheBackgroundIsUntouched()
        {
            // Arrange — a border-only stash (the border-dashed layer's shape).
            var element = ColoredElement();
            var stash = new SilhouetteFaceStash(includeBackground: false);

            // Act — off-panel with an inline border, TryStash captures + suppresses the border color only.
            stash.TryStash(element);

            // Assert — the authored fill survives (a dashed outline composes with the background, never hides it).
            Assert.That(element.style.backgroundColor.value, Is.EqualTo(Fill));
        }

        [Test]
        public void Given_ABorderOnlyStash_When_Stashed_Then_TheBorderColorIsSuppressed()
        {
            // Arrange
            var element = ColoredElement();
            var stash = new SilhouetteFaceStash(includeBackground: false);

            // Act
            stash.TryStash(element);

            // Assert — the native border color is masked with the sentinel so only our repaint shows.
            Assert.That(SilhouetteFace.IsSentinel(element.style.borderLeftColor.value), Is.True);
        }

        [Test]
        public void Given_ADefaultStash_When_Stashed_Then_BothBackgroundAndBorderAreSuppressed()
        {
            // Arrange — the default (includeBackground:true) stash pins SkewBinding / DropShadowBinding's
            // existing behavior: it owns the whole face.
            var element = ColoredElement();
            var stash = new SilhouetteFaceStash();

            // Act
            stash.TryStash(element);

            // Assert
            Assert.That(
                (SilhouetteFace.IsSentinel(element.style.backgroundColor.value),
                    SilhouetteFace.IsSentinel(element.style.borderLeftColor.value)),
                Is.EqualTo((true, true)));
        }
    }
}
