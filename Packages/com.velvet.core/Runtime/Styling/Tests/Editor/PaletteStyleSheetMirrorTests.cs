using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// The palette <see cref="VelvetPalette"/> carries in C#, against the <c>--color-*</c> tokens the bundled
    /// stylesheet declares. A utility that resolves a palette name inline reads the C# table while
    /// <c>border-{color}</c> reads the token, so the two agree only while the tables do.
    /// </summary>
    [TestFixture]
    internal sealed class PaletteStyleSheetMirrorTests
    {
        private const string PaletteSheetPath = "Packages/com.velvet.core/Runtime/Styles/_palette.uss";

        // GREEN_ON_BASE(construction): both sides are the repository's own content and agree on the base. Change
        // `["red-500"] = "#ef4444"` in VelvetPalette.cs to `"#ef4445"` and this is what reddens.
        [Test]
        public void Given_EveryColorTokenTheSheetDeclares_When_ResolvedThroughThePalette_Then_TheColorsAreTheSame()
        {
            // Arrange
            var sheet = File.ReadAllText(Path.GetFullPath(PaletteSheetPath));
            var declared = Regex.Matches(sheet, @"^\s*--color-(?<name>[a-z]+-\d+):\s*#(?<hex>[0-9a-fA-F]{6});",
                    RegexOptions.Multiline)
                .Cast<Match>()
                .Select(match => (Name: match.Groups["name"].Value,
                    Rgba: match.Groups["hex"].Value.ToUpperInvariant() + "FF"))
                .ToArray();

            // Act
            var resolved = declared
                .Select(token => VelvetPalette.TryGet(token.Name, out var color)
                    ? (token.Name, ColorUtility.ToHtmlStringRGBA(color))
                    : (token.Name, "unresolved"))
                .ToArray();

            // Assert
            Assert.That(resolved, Is.EqualTo(declared).And.Not.Empty);
        }
    }
}
