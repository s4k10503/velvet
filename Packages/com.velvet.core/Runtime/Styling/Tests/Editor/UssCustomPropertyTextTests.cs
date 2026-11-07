using NUnit.Framework;
using Velvet.Editor;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which custom properties of a USS file are rewritten before UI Toolkit's importer reads it, and that
    /// nothing else is touched.
    /// </summary>
    internal sealed class UssCustomPropertyTextTests
    {
        [TestCase(":root { --spacing: 0.25rem; }", ":root { --spacing: \"0.25rem\"; }")]
        [TestCase(":root { --text-big: 1.5em }", ":root { --text-big: \"1.5em\"}")]
        [TestCase(":root { --radius-card: calc(var(--spacing) * 4); }", ":root { --radius-card: \"calc(var(--spacing) * 4)\"; }")]
        [TestCase(":root { --w: clamp(1rem, 5vw, 2rem); }", ":root { --w: \"clamp(1rem, 5vw, 2rem)\"; }")]
        [TestCase(":root { --spacing: -0.5rem !important; }", ":root { --spacing: \"-0.5rem\" !important; }")]
        [TestCase(":root { --ease-snappy: cubic-bezier(0.2, 0.8, 0.2, 1); }", ":root { --ease-snappy: \"cubic-bezier(0.2, 0.8, 0.2, 1)\"; }")]
        [TestCase(":root { --ease-steps: steps(4, jump-end); }", ":root { --ease-steps: \"steps(4, jump-end)\"; }")]
        [TestCase(":root{--a:1rem;--b:2rem}", ":root{--a: \"1rem\";--b: \"2rem\"}")]
        public void Given_ACustomPropertyWithAnUnkeptUnitOrAMathFunction_When_TheSheetIsRewritten_Then_ItsValueIsQuotedAsWritten(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = UssCustomPropertyText.Rewrite(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --spacing: 4px; }")]
        [TestCase(":root { --a: 50%; --b: 200ms; --c: 1e3px; --d: 90deg; }")]
        [TestCase(":root { --ease-out: ease-out; --ease-lin: linear; --mysteps: 3px; }")]
        [TestCase(":root { --color-brand: #ff0066; --animate-x: kf 2s linear infinite; }")]
        [TestCase(":root { --spacing-2: var(--spacing-1); --font: \"Inter\"; }")]
        [TestCase(".a { width: 1rem; }")]
        [TestCase("/* :root { --spacing: 1rem; } */ .a { color: red; }")]
        [TestCase(".a { background-image: url(\"b.png?--x:1rem;\"); }")]
        [TestCase(".a { background-image: url(--x:1rem); }")]
        public void Given_ACustomPropertyTheImporterHolds_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = UssCustomPropertyText.Rewrite(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        [Test]
        public void Given_AValueSpanningLines_When_TheSheetIsRewritten_Then_TheSheetKeepsItsLineCount()
        {
            // Arrange
            const string uss = ".a {\n  --m: calc(1rem\n    + 2px);\n  --n: 3px;\n}\n";

            // Act
            var lines = UssCustomPropertyText.Rewrite(uss).Split('\n').Length;

            // Assert
            Assert.That(lines, Is.EqualTo(uss.Split('\n').Length));
        }
    }
}
