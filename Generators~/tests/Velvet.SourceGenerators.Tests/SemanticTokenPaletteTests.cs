using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins the role tokens of <c>_tokens.uss</c> that are palette colours to the palette token they take, so a palette
    /// change that leaves one of them behind fails here.
    /// </summary>
    /// <remarks>
    /// USS writes the role tokens as <c>rgb()</c> rather than as <c>var()</c> of the palette token, which is what
    /// <c>SemanticTokenSetTests</c> reads their contrast and alpha off, so the link between the two is held here.
    /// </remarks>
    public sealed class SemanticTokenPaletteTests
    {
        private static readonly (string Block, string Token, string Palette)[] Mapped =
        {
            (":root", "--color-surface", "slate-100"),
            (":root", "--color-highlight", "slate-300"),
            (":root", "--color-border", "slate-200"),
            (":root", "--color-accent", "sky-600"),
            (":root", "--color-accent-strong", "sky-700"),
            (":root", "--color-text", "slate-900"),
            (":root", "--color-text-subtle", "slate-500"),
            (":root", "--color-primary", "blue-600"),
            (":root", "--color-primary-hover", "blue-700"),
            (":root", "--color-primary-active", "blue-800"),
            (":root", "--color-primary-soft", "blue-100"),
            (":root", "--color-primary-faint", "blue-50"),
            (":root", "--color-danger", "red-600"),
            (":root", "--color-danger-hover", "red-700"),
            (":root", "--color-danger-active", "red-800"),
            (":root", "--color-success", "green-600"),
            (":root", "--color-success-hover", "green-700"),
            (":root", "--color-success-active", "green-800"),
            (":root", "--color-neutral", "slate-50"),
            (":root", "--color-surface-hover", "slate-200"),
            (":root", "--color-border-hover", "slate-400"),
            (":root", "--color-border-active", "slate-300"),
            (".dark", "--color-background", "slate-900"),
        };

        [Fact]
        public void Given_ARoleTokenTakenFromThePalette_When_ComparedWithItsPaletteToken_Then_TheColoursAreTheSame()
        {
            // Arrange
            var styles = Path.Combine(SolutionPaths.RuntimeRoot(), "Styles");
            var tokens = File.ReadAllText(Path.Combine(styles, "_tokens.uss"));
            var palette = File.ReadAllText(Path.Combine(styles, "_palette.uss"));

            // Act
            var differing = Mapped
                .Where(entry => RoleHex(tokens, entry.Block, entry.Token) != PaletteHex(palette, entry.Palette))
                .Select(entry => $"{entry.Block} {entry.Token} is not {entry.Palette}")
                .ToList();

            // Assert
            Assert.Empty(differing);
        }

        private static string RoleHex(string sheet, string block, string token)
        {
            var body = Regex.Match(sheet, Regex.Escape(block) + @"\s*\{([^}]*)\}").Groups[1].Value;
            var rgb = Regex.Match(body, Regex.Escape(token) + @":\s*rgb\((\d+),\s*(\d+),\s*(\d+)\);");
            return rgb.Success
                ? string.Concat(Enumerable.Range(1, 3).Select(group =>
                    int.Parse(rgb.Groups[group].Value, CultureInfo.InvariantCulture).ToString("x2", CultureInfo.InvariantCulture)))
                : "undeclared";
        }

        private static string PaletteHex(string sheet, string name)
        {
            var hex = Regex.Match(sheet, "--color-" + Regex.Escape(name) + @":\s*#([0-9a-f]{6});");
            return hex.Success ? hex.Groups[1].Value : "missing";
        }
    }
}
