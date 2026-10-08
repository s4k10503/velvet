using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// <see cref="StyleTextEffectClass.WhiteSpaceClassesInSheetOrder"/> mirrors the bundled rules that write
    /// white-space; this fails when a sheet adds, removes, reorders or changes one of them.
    /// </summary>
    [TestFixture]
    internal sealed class WhiteSpaceSheetOrderTests
    {
        private const string StylesDirectory = "Packages/com.velvet.core/Runtime/Styles";

        [Test]
        public void Given_TheBundledSheets_When_TheirWhiteSpaceRulesAreRead_Then_TheyMatchTheMirrorInOrder()
        {
            // Arrange
            var comment = new Regex(@"/\*.*?\*/", RegexOptions.Singleline);
            var rule = new Regex(@"\.([A-Za-z0-9_-]+)\s*\{([^}]*)\}");
            var declaration = new Regex(@"(?:^|[;\s])white-space\s*:\s*([a-z-]+)");
            // The preflight sheet sorts first. Its two rules hand white-space to the parent and are no class a
            // className names, so the mirror does not carry them.
            var expected = new[] { "_preflight.uss velvet-label unset", "_preflight.uss velvet-button unset" }
                .Concat(StyleTextEffectClass.WhiteSpaceClassesInSheetOrder
                    .Select(pair => "_typography.uss " + pair.Key + " " + UssKeyword(pair.Value)))
                .ToArray();

            // Act
            var found = Directory.GetFiles(StylesDirectory, "*.uss")
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .SelectMany(path => rule.Matches(comment.Replace(File.ReadAllText(path), " "))
                    .Cast<Match>()
                    .Select(match => (match, value: declaration.Match(match.Groups[2].Value)))
                    .Where(pair => pair.value.Success)
                    .Select(pair => Path.GetFileName(path) + " " + pair.match.Groups[1].Value + " "
                        + pair.value.Groups[1].Value))
                .ToArray();

            // Assert
            Assert.That(string.Join("\n", found), Is.EqualTo(string.Join("\n", expected)));
        }

        private static string UssKeyword(WhiteSpace value) => value switch
        {
            WhiteSpace.Normal => "normal",
            WhiteSpace.NoWrap => "nowrap",
            WhiteSpace.Pre => "pre",
            _ => "pre-wrap",
        };
    }
}
