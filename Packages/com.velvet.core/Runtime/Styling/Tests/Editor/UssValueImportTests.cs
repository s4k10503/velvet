#nullable enable
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how UI Toolkit's own importer reads the tokens of a custom property, which UssCustomPropertyText reads the
    /// same way to decide what it quotes.
    /// </summary>
    internal sealed class UssValueImportTests
    {
        private const string Name = "--x";

        private static bool FailsTheSheet(string value) => Imported(value, sheet => sheet.importedWithErrors);

        // The property's stored value spelled back as CSS, or null where the importer failed the sheet or stored no
        // such property.
        private static string? StoredText(string value) => Imported(value, sheet => sheet.importedWithErrors ? null : Spelled(sheet));

        private static T Imported<T>(string value, Func<StyleSheet, T> read)
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            try
            {
                var impl = Activator.CreateInstance(StockStyleSheetImport.Impl!, true)!;
                StockStyleSheetImport.Import!.Invoke(impl, new object[] { sheet, ":root { " + Name + ": " + value + "; }" });
                return read(sheet);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        private static string? Spelled(StyleSheet sheet)
        {
            var rules = EngineMember.StyleSheetRules.ResolveProperty()?.GetValue(sheet) as Array;
            foreach (var rule in rules ?? Array.Empty<object>())
            {
                var properties = EngineMember.StyleRuleProperties.ResolveProperty()?.GetValue(rule) as Array;
                foreach (var property in properties ?? Array.Empty<object>())
                {
                    if (EngineMember.StylePropertyName.ResolveProperty()?.GetValue(property) as string == Name
                        && EngineMember.StylePropertyValues.ResolveProperty()?.GetValue(property) is Array handles)
                    {
                        return StyleCustomProperties.Spell(sheet, handles);
                    }
                }
            }
            return null;
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a number is an engine fact the base meets too.
        // These are numbers its lexer reads as CSS does, which UssCustomPropertyText leaves unquoted.
        [TestCase("4px")]
        [TestCase("1.5px")]
        [TestCase("-3px")]
        public void Given_ANumberTheLexerReadsAsCssDoes_When_TheStockImporterReadsTheRawSheet_Then_ItStoresItAsWritten(string value)
        {
            // Arrange
            // Act
            var stored = StoredText(value);

            // Assert
            Assert.That(stored, Is.EqualTo(value));
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a number is an engine fact the base meets too.
        // One case for each reading of UssCustomPropertyText.MisreadsNumber that fails the sheet: no exponent, and a
        // unit started at a `-`, a `_` and a non-ASCII character.
        [TestCase("1e3px")]
        [TestCase("1-2")]
        [TestCase("1_a")]
        [TestCase("1٣")]
        public void Given_ANumberTheLexerReadsAsAnUnkeptUnit_When_TheStockImporterReadsTheRawSheet_Then_ItFailsTheSheet(string value)
        {
            // Arrange
            // Act
            var failed = FailsTheSheet(value);

            // Assert
            Assert.That(failed, Is.True);
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a number is an engine fact the base meets too.
        // The splits UssCustomPropertyText quotes for: a full stop no digit follows is dropped after an integer, signed
        // or not, and a unit ends at its first character that is not an ASCII letter.
        [TestCase("3.px", "3 px")]
        [TestCase("-3.px", "-3 px")]
        [TestCase("1px_a", "1px _a")]
        public void Given_ANumberTheLexerEndsElsewhereThanCss_When_TheStockImporterReadsTheRawSheet_Then_ItStoresOtherTokens(
            string value, string expected)
        {
            // Arrange
            // Act
            var stored = StoredText(value);

            // Assert
            Assert.That(stored, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a token is an engine fact the base meets too.
        // The tokens UssCustomPropertyText.HasUnheldToken takes for ones that fail the sheet: a delimiter, at the top
        // level or among a function's arguments, a bracket no function name opens, a hex colour whose digits number
        // other than three, four, six or eight, an rgb() or rgba() whose plain numbers are not as many as its
        // channels, and a function with no arguments; and an `!important` in another case, which
        // UssCustomPropertyText writes in lower case.
        [TestCase("16 / 9")]
        [TestCase(".5.p")]
        [TestCase("3.5.p")]
        [TestCase("4px -")]
        [TestCase("a !b")]
        [TestCase("var(--a, 1 / 2)")]
        [TestCase("(1)")]
        [TestCase("#zzz")]
        [TestCase("#12345")]
        [TestCase("rgb(100%, 0%, 0%)")]
        [TestCase("rgba(0, 0, 0)")]
        [TestCase("rgb(255, 0, 0, 0.5)")]
        [TestCase("blur()")]
        [TestCase("var()")]
        [TestCase("4px !IMPORTANT")]
        public void Given_ATokenTheImporterRejects_When_TheStockImporterReadsTheRawSheet_Then_ItFailsTheSheet(string value)
        {
            // Arrange
            // Act
            var failed = FailsTheSheet(value);

            // Assert
            Assert.That(failed, Is.True);
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a token is an engine fact the base meets too.
        // UssCustomPropertyText.HasUnheldToken quotes a function outside the ones it lists because the importer drops
        // it: rgb() is read only as written.
        [TestCase("mysteps(3)")]
        [TestCase("RGB(0, 0, 0)")]
        public void Given_AFunctionTheImporterDoesNotKnow_When_TheStockImporterReadsTheRawSheet_Then_ItStoresNothing(string value)
        {
            // Arrange
            // Act
            var stored = StoredText(value);

            // Assert
            Assert.That(stored, Is.Empty);
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a token is an engine fact the base meets too.
        // UssCustomPropertyText.HasUnheldToken leaves a function it lists, whatever its case, and a hex colour of
        // three, four, six or eight digits unquoted because the importer stores them.
        [TestCase("blur(4px)", "blur(4px)")]
        [TestCase("BLUR(4px)", "blur(4px)")]
        [TestCase("Var(--b)", "var(--b)")]
        [TestCase("#ff0066", "#ff0066ff")]
        public void Given_AKnownFunctionOrAParsableHexColour_When_TheStockImporterReadsTheRawSheet_Then_ItStoresIt(
            string value, string expected)
        {
            // Arrange
            // Act
            var stored = StoredText(value);

            // Assert
            Assert.That(stored, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a token is an engine fact the base meets too.
        // UssCustomPropertyText quotes an rgba() of more numbers than its channels because the importer drops those
        // past the fourth.
        [Test]
        public void Given_AnRgbaOfFiveNumbers_When_TheStockImporterReadsTheRawSheet_Then_ItStoresTheColourOfTheFirstFour()
        {
            // Arrange
            // Act
            var stored = StoredText("rgba(255, 0, 0, 1, 9)");

            // Assert
            Assert.That(stored, Is.EqualTo("#ff0000ff"));
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a token is an engine fact the base meets too.
        // UssCustomPropertyText.HasUnheldToken quotes a hex colour a name character follows because the importer's
        // lexer ends the colour at its last hex digit, where CSS reads the hash on.
        [Test]
        public void Given_AHexColourANameCharacterFollows_When_TheStockImporterReadsTheRawSheet_Then_ItStoresOtherTokens()
        {
            // Arrange
            // Act
            var stored = StoredText("#abcxyz");

            // Assert
            Assert.That(stored, Is.EqualTo("#aabbccff xyz"));
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a token is an engine fact the base meets too.
        // UssCustomPropertyText.HasUnheldToken quotes such a value all the same.
        [Test]
        public void Given_IdentifiersAndDelimitersAlone_When_TheStockImporterReadsTheRawSheet_Then_ItStoresThemAsOneString()
        {
            // Arrange
            // Act
            var stored = StoredText("a.b");

            // Assert
            Assert.That(stored, Is.EqualTo("a.b"));
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a token is an engine fact the base meets too.
        // UssCustomPropertyText.HasUnheldToken quotes a colon because the importer drops it.
        [Test]
        public void Given_AColon_When_TheStockImporterReadsTheRawSheet_Then_ItStoresTheTokensAroundIt()
        {
            // Arrange
            // Act
            var stored = StoredText("a:b");

            // Assert
            Assert.That(stored, Is.EqualTo("a b"));
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a token is an engine fact the base meets too.
        // UssCustomPropertyText.HasUnheldToken leaves what resource() encloses unread because the importer stores it
        // as the path whatever delimiters it holds.
        [Test]
        public void Given_AResourcePathHoldingDelimiters_When_TheStockImporterReadsTheRawSheet_Then_ItStoresThePath()
        {
            // Arrange
            // Act
            var stored = StoredText("resource(a/b.png)");

            // Assert
            Assert.That(stored, Is.EqualTo("resource(\"a/b.png\")"));
        }
    }
}
