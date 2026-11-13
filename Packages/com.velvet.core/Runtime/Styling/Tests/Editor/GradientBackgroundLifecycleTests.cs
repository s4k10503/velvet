using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a gradient binding leaves on its element when a play's own texture ends and when the binding is
    /// dropped, and that two bindings of one spec show one bake.
    /// </summary>
    internal sealed class GradientBackgroundLifecycleTests
    {
        private static readonly GradientSpec s_spec = new()
        {
            Type = GradientType.Linear,
            Stops = new[] { new GradientStop(Color.black, 0f), new GradientStop(Color.white, 1f) },
        };

        private VisualElement _element;
        private GradientBinding _binding;

        [SetUp]
        public void SetUp()
        {
            _element = new VisualElement();
            _binding = GradientBackground.Bind(_element, s_spec);
        }

        [TearDown]
        public void TearDown()
        {
            GradientBackground.Detach(_element, _binding);
            GradientBackground.Clear(_element);
        }

        // GREEN_ON_BASE(characterization): the base writes the binding's texture back when a play's own texture ends.
        [Test]
        public void Given_AGradientShownThroughAPlaysOwnTexture_When_ThePlayEnds_Then_TheElementShowsTheBindingsTexture()
        {
            // Arrange
            GradientBackground.RebindOwned(_element, _binding, s_spec);

            // Act
            GradientBackground.EndOwned(_element, _binding);

            // Assert
            Assert.That(StyleArbitraryValueResolver.UtilityBackgroundImage(_element).value.texture,
                Is.SameAs(_binding.Texture));
        }

        // GREEN_ON_BASE(characterization): the base shows a play's own texture as soon as the play draws it.
        [Test]
        public void Given_AGradientShownThroughTheCache_When_APlayDrawsItsOwnTexture_Then_TheElementShowsThatTexture()
        {
            // Act
            GradientBackground.RebindOwned(_element, _binding, s_spec);

            // Assert
            Assert.That(StyleArbitraryValueResolver.UtilityBackgroundImage(_element).value.texture,
                Is.SameAs(_binding.Owned));
        }

        // GREEN_ON_BASE(characterization): the base destroys a play's texture once the next frame's replaces it.
        [Test]
        public void Given_APlaysOwnTexture_When_TheNextFrameDrawsAnother_Then_TheFirstIsDestroyed()
        {
            // Arrange
            GradientBackground.RebindOwned(_element, _binding, s_spec);
            var first = _binding.Owned;

            // Act
            GradientBackground.RebindOwned(_element, _binding, s_spec);

            // Assert — Unity's own equality, which a destroyed texture compares null under; false where the first play
            // drew no texture at all.
            Assert.That(first is not null && first == null, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base stops a detached gradient's relative-length poll.
        [Test]
        public void Given_ABindingPollingItsRelativeLengths_When_ItIsDetached_Then_ItKeepsNoPoll()
        {
            // Arrange
            StyleRelativeLengths.SyncPoll(ref _binding.Poll, _element, true, () => { });

            // Act
            GradientBackground.Detach(_element, _binding);

            // Assert
            Assert.That(_binding.Poll, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base shares one cached bake between two bindings of a spec that ignores the box.
        [Test]
        public void Given_AGradientBoundToOneElement_When_AnotherIsBoundToTheSameSpec_Then_ItShowsTheSameTexture()
        {
            // Arrange
            var other = new VisualElement();

            // Act
            var binding = GradientBackground.Bind(other, s_spec);

            // Assert
            try
            {
                Assert.That(binding.Texture, Is.SameAs(_binding.Texture));
            }
            finally
            {
                GradientBackground.Detach(other, binding);
                GradientBackground.Clear(other);
            }
        }
    }
}
