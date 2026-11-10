using System;
using System.Runtime.ExceptionServices;
using System.Threading;
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

        // GREEN_ON_BASE(characterization): the base's rewrite already leaves every one of these as it found it; the
        // easing keyword and the step-like name added here are the values the new cubic-bezier() and steps() match must not take.
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

        // GREEN_ON_BASE(characterization): the base already leaves a custom property written outside any rule as it is.
        [TestCase("--x: 1rem;")]
        [TestCase(".a { } --x: 1rem;")]
        [TestCase("--a: 4px; --b: 1rem;")]
        public void Given_ACustomPropertyOutsideAnyRule_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        // GREEN_ON_BASE(characterization): the base already leaves as written each text or value ending inside a token it reads.
        [TestCase(".a {-")]
        [TestCase(".a { color: red; } /")]
        [TestCase("\"abc")]
        [TestCase(".a{--x")]
        [TestCase(".a { --x ")]
        [TestCase(":root { --x: 4px -; }")]
        [TestCase(":root { --x: 4px +; }")]
        [TestCase(":root { --x: 4px .; }")]
        [TestCase(":root { --x: 4; }")]
        [TestCase(":root { --x: 4.5; }")]
        [TestCase(":root { --x: 1e3; }")]
        public void Given_ATextEndingInsideWhatTheRewriteReads_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        // GREEN_ON_BASE(characterization): the base already declines to rewrite each of these declarations as written.
        [TestCase(".a { --x\u007f: 1rem; }")]
        [TestCase(":root { --x: \"a\" 1rem; }")]
        [TestCase(":root { --x: mysteps(3); }")]
        [TestCase(":root { --x: 1e+3px; }")]
        [TestCase(":root { --x: .5.p; }")]
        public void Given_ADeclarationTheRewriteDeclines_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        // GREEN_ON_BASE(characterization): the base already quotes each of these values and leaves the rest as written.
        [TestCase(".a { --x: 1rem", ".a { --x: \"1rem\"")]
        [TestCase(":root { --bg: url(a;b.png) 1rem; }", ":root { --bg: \"url(a;b.png) 1rem\"; }")]
        [TestCase(".a { --x: 1rem /; }", ".a { --x: \"1rem /\"; }")]
        [TestCase(".a { --x: 1rem/* c */2rem; }", ".a { --x: \"1rem 2rem\"; }")]
        [TestCase(".a { --f: \"x\"; --g: 1rem; }", ".a { --f: \"x\"; --g: \"1rem\"; }")]
        [TestCase(".a { --f: \"a\\\"b\"; --g: 1rem; }", ".a { --f: \"a\\\"b\"; --g: \"1rem\"; }")]
        [TestCase(".a { --x : 1rem; }", ".a { --x : \"1rem\"; }")]
        [TestCase(":root { --x: \u007f1rem; }", ":root { --x: \"\u007f1rem\"; }")]
        [TestCase(":root { --x: 1px2rem; }", ":root { --x: \"1px2rem\"; }")]
        [TestCase(":root { --x: 4e; }", ":root { --x: \"4e\"; }")]
        [TestCase(":root { --x: 4e+; }", ":root { --x: \"4e+\"; }")]
        public void Given_AnUnkeptUnitInADeclarationOfAnUnusualShape_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        private static readonly TimeSpan RewriteBound = TimeSpan.FromSeconds(10);

        // On a thread of its own, so a rewrite that never returns fails its case rather than holding the suite; it then
        // reads as null, which no expected text equals.
        private static string RewriteWithin(string uss)
        {
            string rewritten = null;
            Exception thrown = null;
            var thread = new Thread(() =>
            {
                try
                {
                    rewritten = UssCustomPropertyText.Rewrite(uss);
                }
                catch (Exception exception)
                {
                    thrown = exception;
                }
            })
            {
                IsBackground = true,
            };
            thread.Start();
            if (!thread.Join(RewriteBound))
            {
                return null;
            }
            if (thrown != null)
            {
                ExceptionDispatchInfo.Capture(thrown).Throw();
            }
            return rewritten;
        }
    }
}
