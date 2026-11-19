using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the chain that selects a token set: <see cref="VelvetTheme.IsDark"/>, the class
    /// <see cref="VelvetStyleUtilities.BindThemeTo"/> puts on the root the sheet is attached to, and the
    /// <c>.dark</c> block that keys on it. The <c>dark:</c> variants already answer to the same flag, so a
    /// break anywhere along here leaves an application half-switched.
    /// </summary>
    [TestFixture]
    internal sealed class ThemeTokenSelectionPanelTests : PanelTestBase
    {
        private bool _darkBefore;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        public override void SetUp()
        {
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = false;
            base.SetUp();
        }

        public override void TearDown()
        {
            base.TearDown();
            VelvetTheme.IsDark = _darkBefore;
        }

        [Test]
        public void Given_AnElementCarryingTheBackgroundUtility_When_DarkModeTurnsOn_Then_ItResolvesADarkerOpaqueColour()
        {
            // Arrange
            var leaf = MountAndResolve("bg-background");
            var light = leaf.resolvedStyle.backgroundColor;

            // Act
            VelvetTheme.IsDark = true;
            ForcePanelUpdate(leaf.panel);
            var dark = leaf.resolvedStyle.backgroundColor;

            // Assert — the alpha terms carry the reading that the sheet resolved the utility at all: an
            // unresolved background-color is transparent, and two transparent readings would agree about
            // luminance.
            Assert.That(
                (Luminance(dark) < Luminance(light), light.a, dark.a),
                Is.EqualTo((true, 1f, 1f)),
                $"light={light} dark={dark}");
        }

        [Test]
        public void Given_ADarkModeApplication_When_TheFlagTurnsBackOff_Then_TheRootStopsCarryingTheThemeClass()
        {
            // Arrange
            VelvetTheme.IsDark = true;
            var carriedWhileDark =
                _window.rootVisualElement.ClassListContains(VelvetStyleUtilities.DarkThemeClass);

            // Act
            VelvetTheme.IsDark = false;

            // Assert
            Assert.That(
                (carriedWhileDark,
                    _window.rootVisualElement.ClassListContains(VelvetStyleUtilities.DarkThemeClass)),
                Is.EqualTo((true, false)));
        }

        private static float Luminance(Color color) =>
            (0.2126f * color.r) + (0.7152f * color.g) + (0.0722f * color.b);
    }
}
