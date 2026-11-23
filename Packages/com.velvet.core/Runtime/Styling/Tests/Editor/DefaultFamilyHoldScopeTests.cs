using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which held elements a hold or a release of <see cref="DefaultFamilyRoots"/> decides again: the
    /// element it names and the held elements below it, and no held element elsewhere, so that holding N elements
    /// one after another does not decide the earlier ones N times. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class DefaultFamilyHoldScopeTests
    {
        private static FontAsset s_sans;

        private FontAsset _sans;

        [SetUp]
        public void SetUp()
        {
            VelvetFonts.Clear();
            _sans = s_sans != null ? s_sans : (s_sans = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            VelvetFonts.Register(new VelvetFontFamily("sans",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = _sans }));
            VelvetFonts.DefaultFamily = "sans";
        }

        [TearDown]
        public void TearDown() => VelvetFonts.Clear();

        // What a decision wrote on the element, or null where none stands: a decision that ran again writes it back
        // over the clear the cases make.
        private static FontAsset InlineFontOf(VisualElement element)
            => element.style.unityFontDefinition.keyword == StyleKeyword.Null
                ? null
                : element.style.unityFontDefinition.value.fontAsset;

        [Test]
        public void Given_AHeldElementWhoseWriteWasCleared_When_AnUnrelatedElementIsHeld_Then_TheClearedWriteIsNotDecidedAgain()
        {
            // Arrange
            var first = new VisualElement();
            var second = new VisualElement();
            using var holdingFirst = DefaultFamilyRoots.Hold(first);
            first.style.unityFontDefinition = new StyleFontDefinition(StyleKeyword.Null);

            // Act
            using var holdingSecond = DefaultFamilyRoots.Hold(second);

            // Assert
            Assert.That(InlineFontOf(first), Is.Null);
        }

        [Test]
        public void Given_AHeldElementWhoseWriteWasCleared_When_AnUnrelatedElementIsReleased_Then_TheClearedWriteIsNotDecidedAgain()
        {
            // Arrange
            var first = new VisualElement();
            var second = new VisualElement();
            using var holdingFirst = DefaultFamilyRoots.Hold(first);
            var holdingSecond = DefaultFamilyRoots.Hold(second);
            first.style.unityFontDefinition = new StyleFontDefinition(StyleKeyword.Null);

            // Act
            holdingSecond.Dispose();

            // Assert
            Assert.That(InlineFontOf(first), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base decides every held element on a hold, the one below the held element included.
        [Test]
        public void Given_AHeldElementBelowWhereAnotherIsHeld_When_TheOuterElementIsHeld_Then_TheInnerOneTakesBackItsWrite()
        {
            // Arrange
            var outer = new VisualElement();
            var inner = new VisualElement();
            outer.Add(inner);
            using var holdingInner = DefaultFamilyRoots.Hold(inner);

            // Act
            using var holdingOuter = DefaultFamilyRoots.Hold(outer);

            // Assert
            Assert.That(InlineFontOf(inner), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base decides every held element on a release, the one below the released element included.
        [Test]
        public void Given_AHeldElementBelowAnotherHeldOne_When_TheOuterElementIsReleased_Then_TheInnerOneWritesTheDefaultFamily()
        {
            // Arrange
            var outer = new VisualElement();
            var inner = new VisualElement();
            outer.Add(inner);
            var holdingOuter = DefaultFamilyRoots.Hold(outer);
            using var holdingInner = DefaultFamilyRoots.Hold(inner);

            // Act
            holdingOuter.Dispose();

            // Assert
            Assert.That(InlineFontOf(inner), Is.SameAs(_sans));
        }
    }
}
