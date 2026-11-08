using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// <see cref="TextLineBox.AlignClassesInSheetOrder"/> mirrors the bundled rules that write -unity-text-align;
    /// this fails when a sheet adds, removes, reorders or changes one of them.
    /// </summary>
    [TestFixture]
    internal sealed class TextAlignSheetOrderTests
    {
        private const string StylesDirectory = "Packages/com.velvet.core/Runtime/Styles";

        // GREEN_ON_BASE(construction): both sides here are the repository's own content.
        // Swap the first two entries of `AlignClassesInSheetOrder` and this reddens.
        [Test]
        public void Given_TheBundledSheets_When_TheirTextAlignRulesAreReadInCascadeOrder_Then_TheyMatchTheMirror()
        {
            // Arrange
            var comment = new Regex(@"/\*.*?\*/", RegexOptions.Singleline);
            var rule = new Regex(@"\.([A-Za-z0-9_-]+)\s*\{([^}]*)\}");
            var declaration = new Regex(@"(?:^|[;\s])-unity-text-align\s*:\s*([a-z-]+)");
            var import = new Regex(@"@import\s+url\(""([^""]+)""\)");
            var expected = TextLineBox.AlignClassesInSheetOrder.Select(pair => pair.Key + " " + UssKeyword(pair.Value)).ToArray();

            // Act
            var found = import.Matches(File.ReadAllText(StylesDirectory + "/StyleUtilities.uss"))
                .Cast<Match>()
                .SelectMany(sheet => rule.Matches(comment.Replace(File.ReadAllText(StylesDirectory + "/" + sheet.Groups[1].Value), " "))
                    .Cast<Match>()
                    .Select(match => (match, value: declaration.Match(match.Groups[2].Value)))
                    .Where(pair => pair.value.Success)
                    .Select(pair => pair.match.Groups[1].Value + " " + pair.value.Groups[1].Value))
                .ToArray();

            // Assert
            Assert.That(string.Join("\n", found), Is.EqualTo(string.Join("\n", expected)));
        }

        private static string UssKeyword(TextAnchor value) => value switch
        {
            TextAnchor.UpperLeft => "upper-left",
            TextAnchor.UpperCenter => "upper-center",
            TextAnchor.UpperRight => "upper-right",
            TextAnchor.MiddleLeft => "middle-left",
            TextAnchor.MiddleCenter => "middle-center",
            TextAnchor.MiddleRight => "middle-right",
            TextAnchor.LowerLeft => "lower-left",
            TextAnchor.LowerCenter => "lower-center",
            _ => "lower-right",
        };
    }
}
