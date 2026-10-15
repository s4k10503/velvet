using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// <see cref="StyleNamedTextSize"/> holds the pixel size of each <c>--text-*</c> token for the
    /// <c>text-&lt;size&gt;/&lt;leading&gt;</c> form; this fails when the sheet moves one.
    /// </summary>
    [TestFixture]
    internal sealed class NamedTextSizeSheetTests
    {
        private const string TokensSheet = "Packages/com.velvet.core/Runtime/Styles/_tokens.uss";

        // GREEN_ON_BASE(construction): both sides here are the repository's own content. Change a size in
        // `StyleNamedTextSize.s_sizes` to `["sm"] = (15f, 1.25f / 0.875f)` and this is what reddens.
        [Test]
        public void Given_TheTokensSheet_When_ItsTextSizesAreRead_Then_TheyMatchTheMirror()
        {
            // Arrange
            var comment = new Regex(@"/\*.*?\*/", RegexOptions.Singleline);
            var token = new Regex(@"--(text-[0-9a-z]+)\s*:\s*([0-9.]+)px\s*;");
            var expected = StyleNamedTextSize.FontSizesPx()
                .Select(pair => "text-" + pair.Key + " " + pair.Value + "px");

            // Act
            var found = token.Matches(comment.Replace(File.ReadAllText(TokensSheet), " "))
                .Cast<Match>()
                .Select(match => match.Groups[1].Value + " " + match.Groups[2].Value + "px");

            // Assert
            Assert.That(string.Join("\n", found), Is.EqualTo(string.Join("\n", expected)));
        }
    }
}
