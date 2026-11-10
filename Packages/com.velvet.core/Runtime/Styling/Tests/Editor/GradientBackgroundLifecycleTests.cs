using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a gradient binding leaves on its element when a play's own texture ends and when the binding is
    /// dropped.
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
    }
}
