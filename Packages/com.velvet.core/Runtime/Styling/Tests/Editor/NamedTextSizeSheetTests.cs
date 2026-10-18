using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// <see cref="StyleNamedTextSize"/> holds the pixel size of each <c>--text-*</c> token, for the
    /// <c>text-&lt;size&gt;/&lt;leading&gt;</c> form, and the sheet order of the <c>.text-&lt;size&gt;</c>
    /// rules, which ranks the sizes of one element; these fail when the sheets move either.
    /// </summary>
    [TestFixture]
    internal sealed class NamedTextSizeSheetTests
    {
        private const string StylesDirectory = "Packages/com.velvet.core/Runtime/Styles/";
        private static readonly Regex s_comment = new(@"/\*.*?\*/", RegexOptions.Singleline);

        private static string ReadSheet(string name) =>
            s_comment.Replace(File.ReadAllText(StylesDirectory + name), " ");

        // The mirror's entries in rank order: name and font size, read off the private table.
        private static (string Name, float FontSizePx)[] Mirror()
        {
            var table = (IEnumerable)typeof(StyleNamedTextSize)
                .GetField("s_sizes", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            return table.Cast<object>()
                .Select(entry => (
                    (string)entry.GetType().GetField("Name")!.GetValue(entry)!,
                    (float)entry.GetType().GetField("FontSizePx")!.GetValue(entry)!))
                .ToArray();
        }

        // GREEN_ON_BASE(construction): both sides here are the repository's own content.
        // Change a size in `StyleNamedTextSize.s_sizes` to `new("sm", 15f, 1.25f / 0.875f)` and this reddens.
        [Test]
        public void Given_TheTokensSheet_When_ItsTextSizesAreRead_Then_TheyMatchTheMirror()
        {
            // Arrange
            var expected = Mirror().Select(entry => "text-" + entry.Name + " " + entry.FontSizePx + "px");

            // Act
            var found = Regex.Matches(ReadSheet("_tokens.uss"), @"--(text-[0-9a-z]+)\s*:\s*([0-9.]+)px\s*;")
                .Cast<Match>()
                .Select(match => match.Groups[1].Value + " " + match.Groups[2].Value + "px");

            // Assert
            Assert.That(string.Join("\n", found), Is.EqualTo(string.Join("\n", expected)));
        }

        // GREEN_ON_BASE(construction): both sides here are the repository's own content.
        // Swap `.text-xs` and `.text-sm` in `_typography.uss` and this reddens.
        [Test]
        public void Given_TheTypographySheet_When_ItsTextSizeRulesAreRead_Then_TheyComeInTheMirrorsRankOrder()
        {
            // Arrange
            var expected = Mirror()
                .Select(entry => "text-" + entry.Name)
                .OrderBy(name =>
                {
                    StyleNamedTextSize.TryParseLineHeight(name, out _, out var rank);
                    return rank;
                });

            // Act
            var found = Regex.Matches(ReadSheet("_typography.uss"), @"\.(text-[0-9a-z]+)\s*\{\s*font-size\s*:")
                .Cast<Match>()
                .Select(match => match.Groups[1].Value);

            // Assert
            Assert.That(string.Join("\n", found), Is.EqualTo(string.Join("\n", expected)));
        }
    }
}
