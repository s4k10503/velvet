using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The background-image fill utilities the bundled stylesheet declares: Tailwind's <c>bg-repeat</c> family
    /// (<c>background-repeat</c>) and <c>slice-sliced</c> / <c>slice-tiled</c> (<c>-unity-slice-type</c>), read
    /// back as the panel resolves them with <c>StyleUtilities.uss</c> attached.
    /// </summary>
    /// <remarks>
    /// Each case also asks the generated utility table for the class, so a class whose value equals the
    /// engine's initial one cannot pass with no rule behind it.
    /// </remarks>
    [TestFixture]
    internal sealed class BackgroundRepeatAndSliceTypeUssTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        [TestCase("bg-repeat", Repeat.Repeat, Repeat.Repeat)]
        [TestCase("bg-no-repeat", Repeat.NoRepeat, Repeat.NoRepeat)]
        [TestCase("bg-repeat-x", Repeat.Repeat, Repeat.NoRepeat)]
        [TestCase("bg-repeat-y", Repeat.NoRepeat, Repeat.Repeat)]
        [TestCase("bg-repeat-round", Repeat.Round, Repeat.Round)]
        [TestCase("bg-repeat-space", Repeat.Space, Repeat.Space)]
        public void Given_ABackgroundRepeatClass_When_Resolved_Then_ItRepeatsAsTailwindsUtilityDoes(
            string className, Repeat x, Repeat y)
        {
            // Arrange
            var declared = StyleUtilityProperties.TryGet(className, out _);

            // Act
            var repeat = MountAndResolve(className).resolvedStyle.backgroundRepeat;

            // Assert
            Assert.That((declared, repeat.x, repeat.y), Is.EqualTo((true, x, y)));
        }

        [TestCase("slice-sliced", SliceType.Sliced)]
        [TestCase("slice-tiled", SliceType.Tiled)]
        public void Given_ASliceTypeClass_When_Resolved_Then_ItSetsTheSliceType(string className, SliceType expected)
        {
            // Arrange
            var declared = StyleUtilityProperties.TryGet(className, out _);

            // Act
            var type = MountAndResolve(className).resolvedStyle.unitySliceType;

            // Assert
            Assert.That((declared, type), Is.EqualTo((true, expected)));
        }

        // GREEN_ON_BASE(characterization): pins the engine's two slice modes, which styling-backgrounds.md's note on
        // `fill` rests on; a third mode here would be the one to map an empty centre onto.
        [Test]
        public void Given_TheEngineSliceType_When_ItsModesAreListed_Then_TheyAreSlicedAndTiledAlone()
        {
            // Act
            var modes = string.Join(",", System.Enum.GetNames(typeof(SliceType)));

            // Assert
            Assert.That(modes, Is.EqualTo("Sliced,Tiled"));
        }

        private VisualElement MountAndResolve(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: className));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }
    }
}
