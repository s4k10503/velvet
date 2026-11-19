using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// <see cref="StyleOverrides.BackgroundRepeat"/> and <see cref="StyleOverrides.UnitySliceType"/> against the
    /// stylesheet utilities that write the same properties, with the bundled sheet attached: the override wins
    /// over a plain utility and an important one (<c>!bg-no-repeat</c>) wins over the override, as an
    /// <c>!important</c> rule wins over a <c>style</c> attribute.
    /// </summary>
    [TestFixture]
    internal sealed class StyleOverridesImportantRulePanelTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private VisualElement MountAndResolve(string className, StyleOverrides styles)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: className, styles: styles));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        private static StyleOverrides Tiled => new()
        {
            BackgroundRepeat = new StyleBackgroundRepeat(new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat)),
            UnitySliceType = new StyleEnum<SliceType>(SliceType.Tiled),
        };

        [TestCase("bg-no-repeat slice-sliced", Repeat.Repeat, SliceType.Tiled)]
        [TestCase("!bg-no-repeat !slice-sliced", Repeat.NoRepeat, SliceType.Sliced)]
        public void Given_OverridesBesideStylesheetUtilities_When_Resolved_Then_OnlyAnImportantUtilityWins(
            string className, Repeat repeat, SliceType type)
        {
            // Act
            var leaf = MountAndResolve(className, Tiled);

            // Assert
            Assert.That((leaf.resolvedStyle.backgroundRepeat.x, leaf.resolvedStyle.unitySliceType),
                Is.EqualTo((repeat, type)));
        }
    }
}
