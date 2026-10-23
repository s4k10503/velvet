using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// <c>font-stretch-*</c> is read as a width, never as a family named <c>stretch-…</c>, and a registered
    /// family settles the width of the face before its style and its weight, as CSS's font matching does.
    /// GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class FontStretchTests
    {
        [SetUp]
        public void SetUp() => VelvetFonts.Clear();

        [TearDown]
        public void TearDown() => VelvetFonts.Clear();

        [TestCase("font-stretch-condensed", 75f)]
        [TestCase("font-stretch-ultra-expanded", 200f)]
        [TestCase("font-stretch-normal", 100f)]
        [TestCase("font-stretch-87.5%", 87.5f)]
        [TestCase("font-stretch-[66.5%]", 66.5f)]
        public void Given_AStretchClass_When_Extracted_Then_ItIsTheWidthAndNamesNoFamily(string cls, float expected)
        {
            // Act
            StyleFontClass.TryExtract(new[] { cls }, out var intent);

            // Assert
            Assert.That((intent.HasFamily, intent.HasStretch, intent.Stretch), Is.EqualTo((false, true, expected)));
        }

        [TestCase("font-stretch-wide")]
        [TestCase("font-stretch--5%")]
        [TestCase("font-stretch-50")]
        [TestCase("font-stretch-%")]
        [TestCase("font-stretch-[]")]
        public void Given_AStretchCssRejects_When_Extracted_Then_NoFontIntentIsRead(string cls)
        {
            // Act
            var read = StyleFontClass.TryExtract(new[] { cls }, out _);

            // Assert
            Assert.That(read, Is.False);
        }

        [Test]
        public void Given_AFamilyClassBeforeAStretchClass_When_Extracted_Then_TheFamilyKeepsItsName()
        {
            // Act
            StyleFontClass.TryExtract(new[] { "font-sans", "font-stretch-condensed" }, out var intent);

            // Assert
            Assert.That(intent.Family, Is.EqualTo("sans"));
        }

        [TestCase(90f, 75f)]
        [TestCase(60f, 75f)]
        [TestCase(100f, 100f)]
        [TestCase(110f, 125f)]
        [TestCase(150f, 125f)]
        [TestCase(30f, 75f)]
        public void Given_FacesOf75100And125_When_AWidthIsRequested_Then_CssWidthOrderDecides(
            float requested, float expected)
        {
            // Arrange
            var family = new VelvetFontFamily("sans",
                new VelvetFontWeightEntry { stretch = 75f },
                new VelvetFontWeightEntry { stretch = 100f },
                new VelvetFontWeightEntry { stretch = 125f });

            // Act
            var entry = family.FindClosestWeight(VelvetFontWeight.Normal, requested);

            // Assert
            Assert.That(entry.stretch, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABoldFaceOfAnotherWidth_When_BoldAtNormalWidthIsRequested_Then_TheWidthBeatsTheWeight()
        {
            // Arrange
            var normalWidth = new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, stretch = 100f };
            var condensedBold = new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold, stretch = 75f };
            var family = new VelvetFontFamily("sans", condensedBold, normalWidth);

            // Act
            var entry = family.FindClosestWeight(VelvetFontWeight.Bold, 100f);

            // Assert
            Assert.That(entry, Is.SameAs(normalWidth));
        }

        [Test]
        public void Given_ACondensedClassOnAnElement_When_Applied_Then_TheCondensedAssetIsWrittenInline()
        {
            var normal = ScriptableObject.CreateInstance<FontAsset>();
            var condensed = ScriptableObject.CreateInstance<FontAsset>();
            try
            {
                VelvetFonts.Register(new VelvetFontFamily("sans",
                    new VelvetFontWeightEntry { upright = normal },
                    new VelvetFontWeightEntry { upright = condensed, stretch = 75f }));
                var element = new Label();

                StyleFontResolver.Apply(element, new[] { "font-sans", "font-stretch-condensed" });

                Assert.That(element.style.unityFontDefinition.value.fontAsset, Is.SameAs(condensed));
            }
            finally
            {
                Object.DestroyImmediate(normal);
                Object.DestroyImmediate(condensed);
            }
        }
    }
}
