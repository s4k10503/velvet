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

        // How many value handles the importer stored for the property, or -1 where it failed the sheet or stored no
        // such property.
        private static int StoredValueCount(string value) => Imported(value, sheet => sheet.importedWithErrors ? -1 : ValueCount(sheet));

        // The property's stored value spelled back as CSS, or null where the importer failed the sheet or stored no
        // such property.
        private static string? StoredText(string value) => Imported(value, sheet => sheet.importedWithErrors ? null : Spelled(sheet));

        private static T Imported<T>(string value, Func<StyleSheet, T> read) => ImportedSheet(":root { " + Name + ": " + value + "; }", read);

        private static T ImportedSheet<T>(string uss, Func<StyleSheet, T> read)
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            try
            {
                var impl = Activator.CreateInstance(StockStyleSheetImport.Impl!, true)!;
                StockStyleSheetImport.Import!.Invoke(impl, new object[] { sheet, uss });
                return read(sheet);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        private static int ValueCount(StyleSheet sheet)
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
                        return handles.Length;
                    }
                }
            }
            return -1;
        }

        // The line the importer recorded for the property, or -1 where it stored no such property.
        private static int PropertyLine(StyleSheet sheet)
        {
            var rules = EngineMember.StyleSheetRules.ResolveProperty()?.GetValue(sheet) as Array;
            foreach (var rule in rules ?? Array.Empty<object>())
            {
                var properties = EngineMember.StyleRuleProperties.ResolveProperty()?.GetValue(rule) as Array;
                foreach (var property in properties ?? Array.Empty<object>())
                {
                    if (EngineMember.StylePropertyName.ResolveProperty()?.GetValue(property) as string == Name
                        && property.GetType().GetProperty("line")?.GetValue(property) is int line)
                    {
                        return line;
                    }
                }
            }
            return -1;
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
        // other than three, four, six or eight, an rgb() of other than three number tokens or an rgba() of fewer than
        // four, a range such as `u+1` counting as none, a function with nothing between its parentheses other than
        // none(), filter() and the URL functions, a var() whose arguments ValidateVarFunction refuses, and a range; a
        // comment among the arguments of blur(), var() and linear-gradient(), which
        // UssCustomPropertyText.HasCommentInArguments takes for one; a URL path the lexer raises an error on, which
        // UssCustomPropertyText.UrlEnd takes for one; an `!important` in another case, which UssCustomPropertyText
        // writes in lower case; and a line break or a form feed in a string, which UssCustomPropertyText escapes inside
        // the quotes it writes.
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
        [TestCase("resource()")]
        [TestCase("rgb()")]
        [TestCase("4px !IMPORTANT")]
        [TestCase("var(--a,)")]
        [TestCase("var(--a, 1px, 2px)")]
        [TestCase("var(a)")]
        [TestCase("var(--)")]
        [TestCase("var(--a b)")]
        [TestCase("rgb(u+1 2 3)")]
        [TestCase("blur(/* x */ 4px)")]
        [TestCase("var(--a /* c */)")]
        [TestCase("linear-gradient(red /* a */, blue)")]
        [TestCase("u+1")]
        [TestCase("U+12")]
        [TestCase("var(--a, u+1)")]
        [TestCase("url(a b)")]
        [TestCase("url(a /* c */)")]
        [TestCase("url(\"a\" /* c */)")]
        [TestCase("url(\"a\" b)")]
        [TestCase("url(a(b))")]
        [TestCase("url(a'b')")]
        [TestCase("url(a\u0001b)")]
        [TestCase("url(\"a\nb\")")]
        [TestCase("url(a\\\nb)")]
        [TestCase("url(a\\1234567 b)")]
        [TestCase("domain(a\")\")")]
        [TestCase("\"a\nb\"")]
        [TestCase("\"a\fb\"")]
        [TestCase("\"a\rb\"")]
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
        // UssCustomPropertyText.HasUnheldToken leaves a function it lists, whatever its case, an rgb() or rgba() of
        // three integers and an alpha, a comment among them included and an alpha with a fraction leaving the channels
        // scaled, a hex colour of three, four, six or eight digits, and a `!` a backslash escapes unquoted because the
        // importer stores them.
        [TestCase("blur(4px)", "blur(4px)")]
        [TestCase("BLUR(4px)", "blur(4px)")]
        [TestCase("Var(--b)", "var(--b)")]
        [TestCase("#ff0066", "#ff0066ff")]
        [TestCase("rgba(255 0 0 / 0)", "#ff000000")]
        [TestCase("rgb(1 /* c */ 2 3)", "#010203ff")]
        [TestCase("rgba(1, 2, 3, 0.5)", "#01020380")]
        [TestCase("rgba(1 2 3 / 0.5)", "#01020380")]
        [TestCase("a\\!IMPORTANT", "a!IMPORTANT")]
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
        // UssCustomPropertyText.ReadsHeldChannels quotes these because the importer stores another colour than CSS
        // reads: TryCreateColorFromFunctionToken counts number tokens alone, up to four, and scales no channel when
        // one of the first three holds a fraction.
        [TestCase("rgb(0 0 0 / 50%)", "#000000ff")]
        [TestCase("rgb(0 0 0 / calc(0.5))", "#000000ff")]
        [TestCase("rgb(59 130 246 / var(--o))", "#3b82f6ff")]
        [TestCase("rgb(1, 2, 3, foo)", "#010203ff")]
        [TestCase("rgb(1e3, 2, 3)", "#030203ff")]
        [TestCase("rgba(255 0 / 0 0)", "#ff000000")]
        [TestCase("rgba(255, 0, 0, 1, 9)", "#ff0000ff")]
        [TestCase("rgb(1.0, 2, 3)", "#ffffffff")]
        public void Given_AnRgbCssReadsOtherwise_When_TheStockImporterReadsTheRawSheet_Then_ItStoresTheColourOfItsNumbers(
            string value, string expected)
        {
            // Arrange
            // Act
            var stored = StoredText(value);

            // Assert
            Assert.That(stored, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): how the stock importer reads a token is an engine fact the base meets too.
        // UssCustomPropertyText leaves these unquoted because the importer stores a value for them rather than failing
        // the sheet or dropping them.
        [TestCase("none()")]
        [TestCase("filter()")]
        [TestCase("url()")]
        [TestCase("url-prefix()")]
        [TestCase("domain()")]
        [TestCase("var(--a, 1px)")]
        [TestCase("var(--a,,1px)")]
        [TestCase("var( )")]
        [TestCase("url(a/*c*/)")]
        [TestCase("resource(/* c */ a)")]
        [TestCase("url(  a  )")]
        [TestCase("url(\"a\" )")]
        [TestCase("url(\"a\\\"b\")")]
        [TestCase("url(a\\ b)")]
        [TestCase("url(a\\31 b)")]
        [TestCase("url(a\\(b)")]
        [TestCase("url(a\u000bb)")]
        [TestCase("url(\"a\\31\nb\")")]
        [TestCase("url(\"a\\\r\nb\")")]
        [TestCase("url(a\\31\r\nb)")]
        [TestCase("domain(icons/*.svg)")]
        [TestCase("up+1")]
        [TestCase("u 1")]
        public void Given_AValueTheImporterHolds_When_TheStockImporterReadsTheRawSheet_Then_ItStoresSomeValue(string value)
        {
            // Arrange
            // Act
            var count = StoredValueCount(value);

            // Assert
            Assert.That(count, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(characterization): the stock importer's reading of a sheet ending inside a URL is an engine fact.
        // UssCustomPropertyText.UrlEnd reads where such a URL ends as the lexer does.
        [TestCase(":root { --x: url(", true)]
        [TestCase(":root { --x: url(a ", true)]
        [TestCase(":root { --x: url(a\\", true)]
        [TestCase(":root { --x: url(\"a\\", true)]
        [TestCase(":root { --x: url(a", false)]
        [TestCase(":root { --x: url(\"a", false)]
        public void Given_ASheetEndingInsideAUrl_When_TheStockImporterReadsIt_Then_ItFailsTheSheetWhereTheLexerRaisesAnError(
            string uss, bool expected)
        {
            // Arrange
            // Act
            var failed = ImportedSheet(uss, sheet => sheet.importedWithErrors);

            // Assert
            Assert.That(failed, Is.EqualTo(expected));
        }

        [TestCase("url(a b)", "url(a b)")]
        [TestCase("url(a /* c */)", "url(a /* c */)")]
        [TestCase("url(\"a\" /* c */)", "url(\"a\"  )")]
        [TestCase("url(\"a\nb)", "url(\"a\nb)")]
        [TestCase("url(a\\\nb)", "url(a\\\nb)")]
        [TestCase("url(a\fb)", "url(a\fb)")]
        [TestCase("url(a\rb)", "url(a\rb)")]
        [TestCase("url(images/don't-panic.png)", "url(images/don't-panic.png)")]
        [TestCase("u+1", "u+1")]
        public void Given_AValueTheImporterFailsTheSheetOn_When_TheRewrittenSheetIsImported_Then_ItStoresItsText(
            string value, string expected)
        {
            // Arrange
            var uss = UssCustomPropertyTextTests.RewriteWithin(":root { " + Name + ": " + value + "; }") ?? string.Empty;

            // Act
            var stored = ImportedSheet(uss, sheet => sheet.importedWithErrors ? null : Spelled(sheet));

            // Assert
            Assert.That(stored, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): how the stock importer counts lines is an engine fact the base meets too.
        // UssCustomPropertyText.LineBreaks counts a CR as a line, and a CR and the LF after it as one, as the lexer does.
        [TestCase("\r")]
        [TestCase("\r\n")]
        public void Given_ACarriageReturnAloneOrBeforeALineFeed_When_TheStockImporterReadsTheRawSheet_Then_ItCountsOneLine(string lineBreak)
        {
            // Arrange
            var uss = ":root {" + lineBreak + "  --a: 4px;" + lineBreak + "  " + Name + ": 4px;" + lineBreak + "}";

            // Act
            var line = ImportedSheet(uss, PropertyLine);

            // Assert
            Assert.That(line, Is.EqualTo(3));
        }

        [Test]
        public void Given_AQuotedValueACarriageReturnBreaks_When_TheRewrittenSheetIsImported_Then_ThePropertyAfterItKeepsItsLine()
        {
            // Arrange
            const string uss = ".a {\r  --m: calc(1rem\r    + 2px);\r  " + Name + ": 4px;\r}";
            var rewritten = UssCustomPropertyTextTests.RewriteWithin(uss) ?? string.Empty;

            // Act
            var line = ImportedSheet(rewritten, PropertyLine);

            // Assert
            Assert.That(line, Is.EqualTo(4));
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
