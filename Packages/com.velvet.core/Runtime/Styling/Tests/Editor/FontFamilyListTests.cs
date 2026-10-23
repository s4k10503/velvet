using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// A font-family list (<c>font-[Inter,Noto_Sans_JP]</c>) assigns the first family that has a Font Asset and
    /// appends the assets of the families after it to that asset's fallback table, so a glyph the first lacks
    /// comes from the next. GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class FontFamilyListTests
    {
        private readonly List<FontAsset> _assets = new();

        [SetUp]
        public void SetUp() => VelvetFonts.Clear();

        [TearDown]
        public void TearDown()
        {
            VelvetFonts.Clear();
            foreach (var asset in _assets)
            {
                Object.DestroyImmediate(asset);
            }
            _assets.Clear();
        }

        private FontAsset Register(string family)
        {
            var asset = ScriptableObject.CreateInstance<FontAsset>();
            _assets.Add(asset);
            VelvetFonts.Register(new VelvetFontFamily(family,
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = asset }));
            return asset;
        }

        private static bool HasFallback(FontAsset asset, FontAsset fallback)
            => asset.fallbackFontAssetTable != null && asset.fallbackFontAssetTable.Contains(fallback);

        [Test]
        public void Given_AFamilyListBracket_When_Extracted_Then_TheIntentKeepsTheWholeList()
        {
            // Act
            StyleFontClass.TryExtract(new[] { "font-[Inter,Noto_Sans_JP]" }, out var intent);

            // Assert
            Assert.That(intent.Family, Is.EqualTo("Inter,Noto Sans JP"));
        }

        [Test]
        public void Given_TwoRegisteredFamilies_When_TheListIsResolved_Then_TheFirstIsAssignedWithTheSecondAsFallback()
        {
            // Arrange
            var latin = Register("Inter");
            var japanese = Register("Noto Sans JP");

            // Act
            var resolved = VelvetFonts.Resolve("Inter,Noto Sans JP", VelvetFontWeight.Normal, italic: false);

            // Assert
            Assert.That((resolved.Asset == latin, HasFallback(latin, japanese)), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AGenericNameFirst_When_TheListIsResolved_Then_TheFirstRegisteredFamilyIsAssigned()
        {
            // Arrange
            var latin = Register("Inter");

            // Act
            var resolved = VelvetFonts.Resolve("sans-serif,Inter", VelvetFontWeight.Normal, italic: false);

            // Assert
            Assert.That(resolved.Asset, Is.SameAs(latin));
        }

        [Test]
        public void Given_QuotedNames_When_TheListIsResolved_Then_TheQuotesAreNotPartOfTheName()
        {
            // Arrange
            var latin = Register("Inter");
            var japanese = Register("Noto Sans JP");

            // Act
            VelvetFonts.Resolve("'Inter', \"Noto Sans JP\"", VelvetFontWeight.Normal, italic: false);

            // Assert
            Assert.That(HasFallback(latin, japanese), Is.True);
        }

        [Test]
        public void Given_TheListsOfTwoFamiliesNamingEachOther_When_BothAreResolved_Then_NoFallbackLoopIsWritten()
        {
            // Arrange
            var a = Register("A");
            var b = Register("B");

            // Act
            VelvetFonts.Resolve("A,B", VelvetFontWeight.Normal, italic: false);
            VelvetFonts.Resolve("B,A", VelvetFontWeight.Normal, italic: false);

            // Assert
            Assert.That((HasFallback(a, b), HasFallback(b, a)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AFallbackAnAssetAlreadyHad_When_TheRegistryIsCleared_Then_OnlyTheOneTheListAddedIsRemoved()
        {
            // Arrange
            var latin = Register("Inter");
            var japanese = Register("Noto Sans JP");
            var own = ScriptableObject.CreateInstance<FontAsset>();
            _assets.Add(own);
            latin.fallbackFontAssetTable = new List<FontAsset> { own };
            VelvetFonts.Resolve("Inter,Noto Sans JP", VelvetFontWeight.Normal, italic: false);

            // Act
            VelvetFonts.Clear();

            // Assert
            Assert.That((HasFallback(latin, own), HasFallback(latin, japanese)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AFamilyListClass_When_Applied_Then_TheLabelGetsTheFirstFamilysAsset()
        {
            // Arrange
            var latin = Register("Inter");
            Register("Noto Sans JP");
            var label = new Label("a");

            // Act
            StyleFontResolver.Apply(label, new[] { "font-[Inter,Noto_Sans_JP]" });

            // Assert
            Assert.That(label.style.unityFontDefinition.value.fontAsset, Is.SameAs(latin));
        }
    }
}
