#nullable enable
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A skewed element whose rotate or scale a stylesheet of the application's own declares draws the composite of
    /// that rotate or scale with the skew.
    /// </summary>
    internal sealed class SkewStylesheetCompositePanelTests : PanelTestBase
    {
        private const string RulesPath = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/SkewStylesheetRules.uss";

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private static StyleSheet? Rules() => AssetDatabase.LoadAssetAtPath<StyleSheet>(RulesPath);

        // The slots' matrix, or NaN throughout where the rules did not load, so a missing sheet fails the comparison
        // rather than skipping it.
        private static float[] Measured(StyleSheet? rules, VisualElement caster)
            => rules != null ? SkewCompositeMath.Drawn(caster) : new[] { float.NaN, float.NaN, float.NaN, float.NaN };

        private void Attach(StyleSheet? rules)
        {
            if (rules != null)
            {
                _window.rootVisualElement.styleSheets.Add(rules);
            }
        }

        private VisualElement Mount(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "caster", className: className));
            var caster = _window.rootVisualElement.Q<VisualElement>("caster");
            ForcePanelUpdate(caster.panel);
            ForcePanelUpdate(caster.panel);
            return caster;
        }

        [Test]
        public void Given_ASkewBesideAStylesheetRotate_When_ItIsDrawn_Then_TheMatrixIsTheRotateComposedWithTheSkew()
        {
            // Arrange
            var rules = Rules();
            Attach(rules);
            var caster = Mount("w-[200px] h-[48px] skew-x-12 sheet-rotated");

            // Act
            var drawn = Measured(rules, caster);

            // Assert
            Assert.That(drawn, Is.EqualTo(SkewCompositeMath.Composite(20f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASkewBesideAStylesheetScale_When_ItIsDrawn_Then_TheMatrixIsTheScaleComposedWithTheSkew()
        {
            // Arrange
            var rules = Rules();
            Attach(rules);
            var caster = Mount("w-[200px] h-[48px] skew-x-12 sheet-scaled");

            // Act
            var drawn = Measured(rules, caster);

            // Assert
            Assert.That(drawn, Is.EqualTo(SkewCompositeMath.Composite(0f, 1.2f, 1.2f, 12f, 0f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASkewedElementWithNoRotateRule_When_AStylesheetGivingItOneIsAttached_Then_TheMatrixIsTheRotateComposedWithTheSkew()
        {
            // Arrange
            var caster = Mount("w-[200px] h-[48px] skew-x-12 sheet-late");
            var rules = Rules();

            // Act
            Attach(rules);
            ForcePanelUpdate(caster.panel);
            ForcePanelUpdate(caster.panel);

            // Assert
            Assert.That(Measured(rules, caster), Is.EqualTo(SkewCompositeMath.Composite(25f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASkewedElementWithAStylesheetRotate_When_TheStylesheetIsDetached_Then_OnlyTheSkewIsDrawn()
        {
            // Arrange
            var rules = Rules();
            Attach(rules);
            var caster = Mount("w-[200px] h-[48px] skew-x-12 sheet-rotated");

            // Act
            if (rules != null) _window.rootVisualElement.styleSheets.Remove(rules);
            ForcePanelUpdate(caster.panel);
            ForcePanelUpdate(caster.panel);

            // Assert
            Assert.That(Measured(rules, caster), Is.EqualTo(SkewCompositeMath.Composite(0f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASkewBesideAStylesheetRotateAndAnInlineRotate_When_ItIsDrawn_Then_TheInlineRotateIsComposed()
        {
            // Arrange
            var rules = Rules();
            Attach(rules);
            var caster = Mount("w-[200px] h-[48px] skew-x-12 sheet-rotated rotate-[10deg]");

            // Act
            var drawn = Measured(rules, caster);

            // Assert
            Assert.That(drawn, Is.EqualTo(SkewCompositeMath.Composite(10f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }
    }
}
