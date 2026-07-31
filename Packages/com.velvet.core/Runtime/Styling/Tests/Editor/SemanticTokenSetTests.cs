using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the properties the semantic colour layer is built on: the two theme sets declare the same names,
    /// every colour that varies by theme is opaque, and body text clears WCAG AA against the background of
    /// its own set.
    /// </summary>
    /// <remarks>
    /// Read off <c>_tokens.uss</c> rather than off a panel: a token with no utility class of its own
    /// (<c>--color-surface-hover</c>, the border ladder) reaches no element to be measured on, and opacity is
    /// a property of the whole set rather than of the ones that happen to have a class.
    /// <para>
    /// The name-coverage case is there because the dark set is written as an override of the default one:
    /// a name only it declares has nothing to fall back to when dark mode is off.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class SemanticTokenSetTests
    {
        private const string TokenSheetPath = "Packages/com.velvet.core/Runtime/Styles/_tokens.uss";

        // WCAG 2.1 AA for body text. Computable at all only because the tokens are opaque and a background
        // is declared: a ratio against a translucent colour is a function of whatever happens to be behind it.
        private const double BodyTextContrastMinimum = 4.5;

        private static readonly string[] TextTokens = { "--color-text", "--color-text-subtle" };

        [Test]
        public void Given_TheDarkTokenSet_When_ComparedWithTheDefaultSet_Then_ItNamesNothingTheDefaultSetOmits()
        {
            // Arrange
            var light = TokensIn(":root");
            var dark = TokensIn(".dark");

            // Act
            var undefinedInLight = dark.Keys
                .Where(name => !light.ContainsKey(name))
                .OrderBy(name => name, StringComparer.Ordinal);

            // Assert
            Assert.That(
                (dark.ContainsKey("--color-surface"), string.Join(", ", undefinedInLight)),
                Is.EqualTo((true, string.Empty)));
        }

        [Test]
        public void Given_TheColoursThatVaryByTheme_When_TheirAlphaIsRead_Then_NoneIsTranslucent()
        {
            // Arrange
            var light = TokensIn(":root");
            var dark = TokensIn(".dark");

            // Act
            var translucent = dark.Keys
                .Where(name => light.ContainsKey(name))
                .Where(name => AlphaOf(dark[name]) < 1f || AlphaOf(light[name]) < 1f)
                .OrderBy(name => name, StringComparer.Ordinal);

            // Assert
            Assert.That(
                (dark.ContainsKey("--color-surface"), string.Join(", ", translucent)),
                Is.EqualTo((true, string.Empty)));
        }

        [Test]
        public void Given_EachThemeSet_When_ItsTextIsMeasuredAgainstItsBackground_Then_EveryPairClearsAaContrast()
        {
            // Arrange
            var sets = new (string Name, IReadOnlyDictionary<string, string> Tokens)[]
            {
                ("light", TokensIn(":root")),
                ("dark", TokensIn(".dark")),
            };

            // Act
            var measured = sets
                .SelectMany(set => TextTokens.Select(token => (
                    Label: set.Name + " " + token,
                    Ratio: ContrastRatio(Lookup(set.Tokens, token), Lookup(set.Tokens, "--color-background")))))
                .ToList();

            // Assert
            Assert.That(
                (measured.Count, measured.All(pair => pair.Ratio >= BodyTextContrastMinimum)),
                Is.EqualTo((sets.Length * TextTokens.Length, true)),
                string.Join(", ", measured.Select(pair => $"{pair.Label}={pair.Ratio:F2}")));
        }

        private static string Lookup(IReadOnlyDictionary<string, string> tokens, string name) =>
            tokens.TryGetValue(name, out var value) ? value : string.Empty;

        /// <summary>
        /// The declarations of one block, with comments stripped first so prose naming a token is not read as
        /// one. A block the sheet does not declare reads as empty rather than as a precondition: whether the
        /// dark set exists at all is one of the things these cases are here to fail over, and a precondition
        /// reports that as inconclusive.
        /// </summary>
        private static IReadOnlyDictionary<string, string> TokensIn(string selector)
        {
            var text = File.ReadAllText(Path.GetFullPath(TokenSheetPath));
            var uncommented = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            var block = Regex.Match(
                uncommented, Regex.Escape(selector) + @"\s*\{([^}]*)\}", RegexOptions.Singleline);

            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!block.Success)
            {
                return tokens;
            }

            foreach (Match declaration in Regex.Matches(block.Groups[1].Value, @"(--[\w-]+)\s*:\s*([^;]+);"))
            {
                tokens[declaration.Groups[1].Value] = declaration.Groups[2].Value.Trim();
            }
            return tokens;
        }

        /// <summary>1 for every spelling that carries no alpha component, including an absent declaration.</summary>
        private static float AlphaOf(string value)
        {
            var components = Components(value);
            return components.Length == 4 ? components[3] : 1f;
        }

        private static double ContrastRatio(string foreground, string background)
        {
            var first = RelativeLuminance(foreground);
            var second = RelativeLuminance(background);
            var lighter = Math.Max(first, second);
            var darker = Math.Min(first, second);
            return (lighter + 0.05) / (darker + 0.05);
        }

        /// <summary>WCAG relative luminance; NaN for a value with no rgb triple, so a missing token fails.</summary>
        private static double RelativeLuminance(string value)
        {
            var components = Components(value);
            if (components.Length < 3)
            {
                return double.NaN;
            }

            double Linear(float channel)
            {
                var srgb = channel / 255.0;
                return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Linear(components[0]))
                + (0.7152 * Linear(components[1]))
                + (0.0722 * Linear(components[2]));
        }

        private static float[] Components(string value)
        {
            var open = value.IndexOf('(');
            var close = value.LastIndexOf(')');
            if (open < 0 || close < open)
            {
                return Array.Empty<float>();
            }

            return value
                .Substring(open + 1, close - open - 1)
                .Split(',')
                .Select(component => float.Parse(component.Trim(), CultureInfo.InvariantCulture))
                .ToArray();
        }
    }
}
