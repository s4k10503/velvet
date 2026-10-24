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

        private static StyleSheet Rules()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(RulesPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the test's own rules load");
            return sheet;
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
            _window.rootVisualElement.styleSheets.Add(Rules());
            var caster = Mount("w-[200px] h-[48px] skew-x-12 sheet-rotated");

            // Act
            var drawn = SkewCompositeMath.Drawn(caster);

            // Assert
            Assert.That(drawn, Is.EqualTo(SkewCompositeMath.Composite(20f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASkewBesideAStylesheetScale_When_ItIsDrawn_Then_TheMatrixIsTheScaleComposedWithTheSkew()
        {
            // Arrange
            _window.rootVisualElement.styleSheets.Add(Rules());
            var caster = Mount("w-[200px] h-[48px] skew-x-12 sheet-scaled");

            // Act
            var drawn = SkewCompositeMath.Drawn(caster);

            // Assert
            Assert.That(drawn, Is.EqualTo(SkewCompositeMath.Composite(0f, 1.2f, 1.2f, 12f, 0f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASkewedElementWithNoRotateRule_When_AStylesheetGivingItOneIsAttached_Then_TheMatrixIsTheRotateComposedWithTheSkew()
        {
            // Arrange
            var caster = Mount("w-[200px] h-[48px] skew-x-12 sheet-late");

            // Act
            _window.rootVisualElement.styleSheets.Add(Rules());
            ForcePanelUpdate(caster.panel);
            ForcePanelUpdate(caster.panel);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(caster), Is.EqualTo(SkewCompositeMath.Composite(25f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base composed no stylesheet rotate, so it draws the skew alone either way.
        [Test]
        public void Given_ASkewedElementWithAStylesheetRotate_When_TheStylesheetIsDetached_Then_OnlyTheSkewIsDrawn()
        {
            // Arrange
            var rules = Rules();
            _window.rootVisualElement.styleSheets.Add(rules);
            var caster = Mount("w-[200px] h-[48px] skew-x-12 sheet-rotated");

            // Act
            _window.rootVisualElement.styleSheets.Remove(rules);
            ForcePanelUpdate(caster.panel);
            ForcePanelUpdate(caster.panel);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(caster), Is.EqualTo(SkewCompositeMath.Composite(0f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): an inline rotate outranks a stylesheet's, which the base composed the same way.
        [Test]
        public void Given_ASkewBesideAStylesheetRotateAndAnInlineRotate_When_ItIsDrawn_Then_TheInlineRotateIsComposed()
        {
            // Arrange
            _window.rootVisualElement.styleSheets.Add(Rules());
            var caster = Mount("w-[200px] h-[48px] skew-x-12 sheet-rotated rotate-[10deg]");

            // Act
            var drawn = SkewCompositeMath.Drawn(caster);

            // Assert
            Assert.That(drawn, Is.EqualTo(SkewCompositeMath.Composite(10f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }
    }
}
