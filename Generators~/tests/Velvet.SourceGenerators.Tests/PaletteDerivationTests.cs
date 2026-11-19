using System.Linq;
using Velvet.StyleTable;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Specifies <see cref="PaletteDerivation"/>: how an OKLCH colour becomes the sRGB a token declares, and what the
    /// derivation refuses rather than writes. The committed outputs are compared with a fresh derivation in
    /// <see cref="BundledStyleSheetCensusTests"/>.
    /// </summary>
    public sealed class PaletteDerivationTests
    {
        private const string Sheet = ":root {\n    --color-red-500: #ef4444;\n}\n\n.bg-red-500 { background-color: var(--color-red-500); }\n";

        // Expected values are culori 4.0.1's conversion of the same colours, clipped per channel.
        [Theory]
        [InlineData(97.1, 0.013, 17.38, "#fef2f2")]
        [InlineData(63.7, 0.237, 25.331, "#fb2c36")]
        public void Given_AnOklchColourInsideSrgb_When_Converted_Then_ItIsThatSrgbColour(
            double lightness, double chroma, double hue, string expected)
        {
            // Act
            var hex = PaletteDerivation.ToSrgbHex(lightness / 100.0, chroma, hue);

            // Assert
            Assert.Equal(expected, hex);
        }

        [Fact]
        public void Given_AnOklchColourOutsideSrgb_When_Converted_Then_EachChannelIsClippedToTheGamut()
        {
            // Act — oklch(62.3% 0.214 259.815) has a blue channel above 1 in sRGB.
            var hex = PaletteDerivation.ToSrgbHex(0.623, 0.214, 259.815);

            // Assert
            Assert.Equal("#2b7fff", hex);
        }

        [Fact]
        public void Given_AColourWhoseHueIsNone_When_Derived_Then_ItsTokenIsTheGrayOfItsLightness()
        {
            // Act
            var result = PaletteDerivation.Build("--color-zinc-50: oklch(98.5% 0 none);", Sheet);

            // Assert
            Assert.Contains($"    --color-zinc-50: {PaletteDerivation.ToSrgbHex(0.985, 0.0, 0.0)};\n", result.EmittedSheet);
        }

        [Fact]
        public void Given_ASheetAroundItsRootBlock_When_Derived_Then_OnlyTheRootBlockIsRewritten()
        {
            // Act
            var result = PaletteDerivation.Build("--color-red-500: oklch(63.7% 0.237 25.331);", Sheet);

            // Assert
            Assert.Equal(Sheet.Replace("#ef4444", PaletteDerivation.ToSrgbHex(0.637, 0.237, 25.331)), result.EmittedSheet);
        }

        [Fact]
        public void Given_AColourNotWrittenInOklchBesideOneThatIs_When_Derived_Then_TheDerivationRefusesIt()
        {
            // Act
            var result = PaletteDerivation.Build(
                "--color-red-500: oklch(63.7% 0.237 25.331);\n--color-red-600: #e7000b;", Sheet);

            // Assert
            Assert.Equal(new[] { UssProblemCode.MalformedUss }, result.Problems.Select(p => p.Code));
        }

        [Fact]
        public void Given_ASheetWithoutARootBlock_When_Derived_Then_TheDerivationRefusesIt()
        {
            // Act
            var result = PaletteDerivation.Build(
                "--color-red-500: oklch(63.7% 0.237 25.331);", ".bg-red-500 { background-color: red; }\n");

            // Assert
            Assert.Equal(new[] { UssProblemCode.MalformedUss }, result.Problems.Select(p => p.Code));
        }
    }
}
