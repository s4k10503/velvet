using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using TextElement = UnityEngine.UIElements.TextElement;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that LineBoxFont's copy of a font lays lines out one line height apart under either of UI Toolkit's
    /// text generators, the first line included, whatever face a glyph is drawn from, and that copies nothing holds
    /// are not kept past a few.
    /// </summary>
    internal sealed class LineBoxFontTests : PanelTestBase
    {
        private readonly object _user = new();
        private readonly List<FontAsset> _acquired = new();

        public override void TearDown()
        {
            foreach (var copy in _acquired)
            {
                LineBoxFont.Release(_user, copy);
            }
            _acquired.Clear();
            VelvetFontStack.Release();
            base.TearDown();
        }

        private FontAsset Acquire(FontAsset source, float lineHeight = 24f, float fontSize = 16f)
        {
            var copy = LineBoxFont.Acquire(_user, source, lineHeight, fontSize);
            if (copy != null)
            {
                _acquired.Add(copy);
            }
            return copy;
        }

        private Label Add(TextGeneratorType generator)
        {
            var label = new Label("M");
            label.style.fontSize = 16f;
            label.style.unityTextGenerator = generator;
            _window.rootVisualElement.Add(label);
            ForcePanelUpdate(label.panel);
            return label;
        }

        private Label Laid(TextGeneratorType generator)
        {
            var label = Add(generator);
            DrawIn(label, Acquire(StyleFontUnits.AssetOf(label)!));
            return label;
        }

        private static void DrawIn(Label label, FontAsset asset)
        {
            label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(asset));
            ForcePanelUpdate(label.panel);
        }

        // A dynamic asset of the default font, owned by the case.
        private FontAsset Fresh(Label label)
            => Named(FontAsset.CreateFontAsset(StyleFontUnits.AssetOf(label)!.sourceFontFile));

        private static int s_named;

        // asset under a name of its own, since the text system finds an asset and its material by a hash of their
        // names. It is not destroyed: destroying one a case drew failed the cases after it.
        private FontAsset Named(FontAsset asset)
        {
            var name = "LineBoxFontTests " + ++s_named;
            asset.name = name;
            asset.hashCode = name.GetHashCode();
            asset.material.name = name + " Material";
            asset.materialHashCode = asset.material.name.GetHashCode();
            asset.hideFlags = HideFlags.DontSave;
            return asset;
        }

        // A static asset holding the default font's M alone, so every other character is drawn from a fallback.
        private FontAsset OnlyM(Label label)
        {
            var asset = Fresh(label);
            asset.TryAddCharacters("M");
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            return asset;
        }

        // A dynamic asset of the default font whose lines are half again as far apart as the font's own.
        private FontAsset Tall(Label label)
        {
            var asset = Fresh(label);
            var face = asset.faceInfo;
            face.ascentLine *= 1.5f;
            face.descentLine *= 1.5f;
            face.lineHeight *= 1.5f;
            asset.faceInfo = face;
            return asset;
        }

        private static float HeightOf(TextElement element, string text)
            => element.MeasureTextSize(text, float.NaN, VisualElement.MeasureMode.Undefined, float.NaN,
                VisualElement.MeasureMode.Undefined).y;

        [TestCase(TextGeneratorType.Standard)]
        [TestCase(TextGeneratorType.Advanced)]
        public void Given_ACopyAtTwentyFourPixels_When_OneLineIsMeasured_Then_ItIsTwentyFourPixelsTall(TextGeneratorType generator)
        {
            // Act
            var label = Laid(generator);

            // Assert
            Assert.That(HeightOf(label, "M"), Is.EqualTo(24f).Within(0.01f));
        }

        [TestCase(TextGeneratorType.Standard)]
        [TestCase(TextGeneratorType.Advanced)]
        public void Given_ACopyAtTwentyFourPixels_When_ThreeLinesAreMeasured_Then_TheyAreSeventyTwoPixelsTall(TextGeneratorType generator)
        {
            // Act
            var label = Laid(generator);

            // Assert
            Assert.That(HeightOf(label, "M\nM\nM"), Is.EqualTo(72f).Within(0.01f));
        }

        [Test]
        public void Given_AFontWhoseGlyphsComeFromATallerFallback_When_TwoLinesOfThemAreMeasured_Then_TheyAreFortyEightPixelsTall()
        {
            // Arrange — the standard generator, since the advanced one draws no static asset.
            var label = Add(TextGeneratorType.Standard);
            var primary = OnlyM(label);
            primary.fallbackFontAssetTable = new List<FontAsset> { Tall(label) };

            // Act
            DrawIn(label, Acquire(primary));

            // Assert
            Assert.That(HeightOf(label, "x\nx"), Is.EqualTo(48f).Within(0.01f));
        }

        [Test]
        public void Given_ACopyMade_When_AFontStackAppendsAFallbackToItsSource_Then_TheCopysTableHoldsACopyOfIt()
        {
            // Arrange
            var label = Add(TextGeneratorType.Standard);
            var primary = Fresh(label);
            var fallback = Fresh(label);
            var copy = Acquire(primary);

            // Act
            VelvetFontStack.Append(primary, new[] { fallback });

            // Assert
            Assert.That(copy.fallbackFontAssetTable.Select(LineBoxFont.SourceOf), Is.EqualTo(new[] { fallback }));
        }

        [Test]
        public void Given_AFontStacksFallbackInACopy_When_TheStackIsReleased_Then_TheCopysTableIsEmpty()
        {
            // Arrange
            var label = Add(TextGeneratorType.Standard);
            var primary = Fresh(label);
            VelvetFontStack.Append(primary, new[] { Fresh(label) });
            var copy = Acquire(primary);

            // Act
            VelvetFontStack.Release();

            // Assert
            Assert.That(copy.fallbackFontAssetTable, Is.Empty);
        }

        [Test]
        public void Given_AFontWhoseWeightTableNamesABoldFace_When_BothAreCopied_Then_TheCopysTableNamesTheBoldFacesCopy()
        {
            // Arrange
            var label = Add(TextGeneratorType.Standard);
            var regular = Fresh(label);
            var bold = Fresh(label);
            regular.fontWeightTable[7].regularTypeface = bold;

            // Act
            var copy = Acquire(regular);

            // Assert — the same copy a bold face asked for directly is, so its line height is keyed the same way.
            Assert.That(copy.fontWeightTable[7].regularTypeface, Is.SameAs(Acquire(bold)));
        }

        [Test]
        public void Given_AFontRenderedInEightBitSdf_When_ItIsCopied_Then_TheCopyIsRenderedTheSameWay()
        {
            // Arrange
            var label = Add(TextGeneratorType.Standard);
            var source = Named(FontAsset.CreateFontAsset(StyleFontUnits.AssetOf(label)!.sourceFontFile, 90, 9,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDF8, 512, 512));

            // Act
            var copy = Acquire(source);

            // Assert
            Assert.That(copy.atlasRenderMode, Is.EqualTo(UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDF8));
        }

        [Test]
        public void Given_AFontBuiltFromAFilePath_When_ItsCopyIsMeasured_Then_TwoLinesAreFortyEightPixelsTall()
        {
            // Arrange — an asset that holds no Font, only the path of the file it reads glyphs from. The standard
            // generator, since the advanced one lays out such an asset's two lines as one 19.5px line itself.
            var label = Add(TextGeneratorType.Standard);
            var path = EditorApplication.applicationContentsPath + "/Resources/Fonts/Inter-Small.ttf";
            var fromPath = Named(FontAsset.CreateFontAsset(path, 0, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024));


            // Act
            DrawIn(label, Acquire(fromPath));

            // Assert
            Assert.That(HeightOf(label, "M\nM"), Is.EqualTo(48f).Within(0.01f));
        }

        [Test]
        public void Given_CopiesAtManyLineHeightsOneAtATime_When_EachIsReleased_Then_OneCopyRemains()
        {
            // Arrange
            var label = Add(TextGeneratorType.Standard);
            var source = Fresh(label);
            var before = LineBoxFont.Count;

            // Act
            for (var i = 0; i < 24; i++)
            {
                var copy = LineBoxFont.Acquire(_user, source, 20f + i, 16f);
                LineBoxFont.Release(_user, copy);
            }

            // Assert
            Assert.That(LineBoxFont.Count, Is.EqualTo(before + 1));
        }

        [Test]
        public void Given_TwoFontsWhoseWeightTablesNameEachOtherAndWhichHaveFallbacks_When_OneIsCopied_Then_ItsTableNamesACopyOfTheOther()
        {
            // Arrange
            var label = Add(TextGeneratorType.Standard);
            var regular = Fresh(label);
            var bold = Fresh(label);
            regular.fontWeightTable[7].regularTypeface = bold;
            bold.fontWeightTable[4].regularTypeface = regular;
            regular.fallbackFontAssetTable = new List<FontAsset> { Fresh(label) };
            bold.fallbackFontAssetTable = new List<FontAsset> { Fresh(label) };

            // Act
            var copy = Acquire(regular);

            // Assert
            Assert.That(LineBoxFont.SourceOf(copy.fontWeightTable[7].regularTypeface), Is.SameAs(bold));
        }

        [Test]
        public void Given_AnIdleCopyWhoseSourceIsDestroyed_When_AnotherCopyIsAskedFor_Then_TheIdleOneIsGone()
        {
            // Arrange — a source no text was drawn in, so destroying it reaches nothing the text system holds.
            var label = Add(TextGeneratorType.Standard);
            var gone = Fresh(label);
            var other = Fresh(label);
            var before = LineBoxFont.Count;
            LineBoxFont.Release(_user, LineBoxFont.Acquire(_user, gone, 24f, 16f));
            Object.DestroyImmediate(gone);

            // Act
            Acquire(other);

            // Assert — the other's copy alone remains of the two.
            Assert.That(LineBoxFont.Count, Is.EqualTo(before + 1));
        }

        // The copies LineBoxFont keeps an entry for, by source and line height, whether or not the copy still exists.
        private static int Entries()
            => ((System.Collections.ICollection)typeof(LineBoxFont)
                .GetField("s_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null)).Count;

        // GREEN_ON_BASE(characterization): the base sweeps the entry of a copy whose source font was destroyed.
        [Test]
        public void Given_AnIdleCopyWhoseSourceIsDestroyed_When_AnotherCopyIsAskedFor_Then_NoEntryIsKeptForTheDestroyedSource()
        {
            // Arrange — a source no text was drawn in, so destroying it reaches nothing the text system holds.
            var label = Add(TextGeneratorType.Standard);
            var gone = Fresh(label);
            var other = Fresh(label);
            var before = Entries();
            LineBoxFont.Release(_user, LineBoxFont.Acquire(_user, gone, 24f, 16f));
            Object.DestroyImmediate(gone);

            // Act
            Acquire(other);

            // Assert — the other's entry alone remains of the two.
            Assert.That(Entries(), Is.EqualTo(before + 1));
        }

    }
}
