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
        // GREEN_ON_BASE(refactor): the base already quotes every one of these; the rewrite is bounded here so one that
        // never returns fails its case rather than holding the suite.
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
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base's rewrite already leaves every one of these as it found it.
        // The easing keyword and the step-like name are the values the cubic-bezier() and steps() match must not take.
        [TestCase(":root { --spacing: 4px; }")]
        [TestCase(":root { --a: 50%; --b: 200ms; --d: 90deg; }")]
        [TestCase(":root { --ease-out: ease-out; --ease-lin: linear; --mysteps: 3px; }")]
        [TestCase(":root { --color-brand: #ff0066; --animate-x: kf 2s linear infinite; }")]
        [TestCase(":root { --spacing-2: var(--spacing-1); --font: \"Inter\"; }")]
        [TestCase(".a { width: 1rem; }")]
        [TestCase("/* :root { --spacing: 1rem; } */ .a { color: red; }")]
        [TestCase(".a { background-image: url(\"b.png?--x:1rem;\"); }")]
        [TestCase(".a { background-image: url(--x:1rem); }")]
        [TestCase(":root { --bg: resource(a/b.png); --img: url(a/b.png); }")]
        [TestCase(":root { --a: +1; --b: -.5; --c: rgba(0, 0, 0, .5); }")]
        [TestCase(":root { --f: _a \u00e9; }")]
        [TestCase(":root { --f: blur(4px) var(--a) linear-gradient(to right, #fff, #000); }")]
        [TestCase(":root { --h: #abc #abcd #aabbcc #aabbccdd; }")]
        [TestCase(":root { --x: auto; }")]
        [TestCase(":root { --x: Var(--b); --y: BLUR(4px) Linear-Gradient(red, blue); }")]
        [TestCase(":root { --u: URL-prefix(a/b) domain(a.b); }")]
        [TestCase(":root { --x: rgb(0 0 0) rgba(0, 0, 0, .5); }")]
        [TestCase(":root{--x:!important}")]
        [TestCase(":root{--x:4px}")]
        [TestCase(".a { --x: rgb(0, 0, 0")]
        [TestCase(":root { --x: rgba(255 0 0 / 0.5) rgba(0 0 0 / 1) rgba(0, 0, 0, 1); }")]
        [TestCase(":root { --x: none() filter() url() URL(); }")]
        [TestCase(":root { --x: a\\!IMPORTANT; }")]
        [TestCase(":root { --x: var(--a, 1px) var(--a,1px) var(--a,,1px) var( --a ) var( ) Var(a); }")]
        [TestCase(":root { --x: rgb(1 /* c */ 2 3); }")]
        [TestCase(":root { --x: url(a/*c*/); }")]
        [TestCase(":root { --x: filter(url(a/*c*/)); }")]
        [TestCase(":root { --x: blur(4px) /* c */; }")]
        [TestCase(":root{--x:blur(4px)}")]
        [TestCase(":root { --x: url(  a  ); }")]
        [TestCase(":root { --x: url(\"a\" ); }")]
        [TestCase(":root { --x: url(\"a\\\"b\"); }")]
        [TestCase(":root { --x: url(a\\ b); }")]
        [TestCase(":root { --x: url(a\\31 b); }")]
        [TestCase(":root { --x: url(a\\(b); }")]
        [TestCase(":root { --x: url(a\u000bb); }")]
        [TestCase(":root { --x: url(\"a\\31\nb\"); }")]
        [TestCase(":root { --x: url(\"a\\\r\nb\"); }")]
        [TestCase(":root { --x: url(a\\31\r\nb); }")]
        [TestCase(":root { --x: \\\"url(a\")\"); --y: 2px; }\n.b { color: red; }")]
        [TestCase(":root { --x: \\\"url(b/*c); --y: 1rem; }")]
        [TestCase(":root { --x: \\31 url(b/*c); --y: 1rem; }")]
        [TestCase(":root { --x: \\31\r\nurl(b/*c); --y: 1rem; }")]
        [TestCase(":root { --x: a\\url(b/*c); --y: 1rem; }")]
        [TestCase(":root{--x:\\31 url(b c)}")]
        [TestCase(":root { --x: @\\75rl(a\")\"); --y: 2px; }\n.b { color: red; }")]
        [TestCase(".a { --x: url(a")]
        [TestCase(".a { --x: url(\"a")]
        [TestCase(":root { --x: up+1; }")]
        [TestCase(":root { --x: u 1; }")]
        public void Given_ACustomPropertyTheImporterHolds_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        // GREEN_ON_BASE(refactor): the base keeps the line count, and only the bound on the rewrite is new here.
        [Test]
        public void Given_AValueSpanningLines_When_TheSheetIsRewritten_Then_TheSheetKeepsItsLineCount()
        {
            // Arrange
            const string uss = ".a {\n  --m: calc(1rem\n    + 2px);\n  --n: 3px;\n}\n";

            // Act
            var lines = RewriteWithin(uss)?.Split('\n').Length;

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
        [TestCase(":root { --x: 4; }")]
        [TestCase(":root { --x: 4.5; }")]
        public void Given_ATextEndingInsideWhatTheRewriteReads_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        // GREEN_ON_BASE(characterization): the base already declines to rewrite each of these declarations as written.
        // An escape before a `--` makes it part of a name that is no custom property's.
        [TestCase(".a { --x\u007f: 1rem; }")]
        [TestCase(":root { \\41 --x: 1rem; }")]
        public void Given_ADeclarationTheRewriteDeclines_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        // GREEN_ON_BASE(characterization): the base already quotes each of these values and leaves the rest as written.
        // A line break between a value's tokens is written as a space, and so is a comment after a URL the lexer refuses.
        [TestCase(".a { --x: 1rem", ".a { --x: \"1rem\"")]
        [TestCase(":root { --x: 1rem\n2rem; }", ":root { --x: \"1rem 2rem\"\n; }")]
        [TestCase(":root { --x: 1rem\r\n2rem; }", ":root { --x: \"1rem  2rem\"\n; }")]
        [TestCase(":root { --x: url(a b) /* c */ 1rem; }", ":root { --x: \"url(a b)   1rem\"; }")]
        [TestCase(".a { /* c */ --x: 1rem; }", ".a { /* c */ --x: \"1rem\"; }")]
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

        [TestCase(":root { --x: 1_a; }", ":root { --x: \"1_a\"; }")]
        [TestCase(":root { --x: 1-a; }", ":root { --x: \"1-a\"; }")]
        [TestCase(":root { --x: 1\u0663; }", ":root { --x: \"1\u0663\"; }")]
        public void Given_AUnitStartedByAnUnderscoreAHyphenOrANonAsciiCharacter_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: 1px_a; }", ":root { --x: \"1px_a\"; }")]
        [TestCase(":root { --x: 1px-a; }", ":root { --x: \"1px-a\"; }")]
        [TestCase(":root { --x: 1-2; }", ":root { --x: \"1-2\"; }")]
        [TestCase(":root { --x: 3.px; }", ":root { --x: \"3.px\"; }")]
        [TestCase(":root { --x: 3.; }", ":root { --x: \"3.\"; }")]
        [TestCase(":root { --x: 1e3; }", ":root { --x: \"1e3\"; }")]
        [TestCase(":root { --x: 1e3px; }", ":root { --x: \"1e3px\"; }")]
        [TestCase(":root { --x: 1e+3px; }", ":root { --x: \"1e+3px\"; }")]
        public void Given_ANumberTheImportersLexerReadsOtherwiseThanCss_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --aspect-video: 16 / 9; }", ":root { --aspect-video: \"16 / 9\"; }")]
        [TestCase(":root { --x: .5.p; }", ":root { --x: \".5.p\"; }")]
        [TestCase(":root { --x: 3.5.p; }", ":root { --x: \"3.5.p\"; }")]
        [TestCase(":root { --x: 4px -; }", ":root { --x: \"4px -\"; }")]
        [TestCase(":root { --x: 4px +; }", ":root { --x: \"4px +\"; }")]
        [TestCase(":root { --x: 4px .; }", ":root { --x: \"4px .\"; }")]
        [TestCase(":root { --x: a !b; }", ":root { --x: \"a !b\"; }")]
        [TestCase(":root { --x: var(--a, 1 / 2); }", ":root { --x: \"var(--a, 1 / 2)\"; }")]
        [TestCase(":root { --x: (1); }", ":root { --x: \"(1)\"; }")]
        [TestCase(":root { --x: a:b; }", ":root { --x: \"a:b\"; }")]
        [TestCase(":root { --x: mysteps(3); }", ":root { --x: \"mysteps(3)\"; }")]
        [TestCase(":root { --x: RGB(0, 0, 0); }", ":root { --x: \"RGB(0, 0, 0)\"; }")]
        [TestCase(":root { --x: var(--a, foo(1)); }", ":root { --x: \"var(--a, foo(1))\"; }")]
        [TestCase(":root { --x: #zzz; }", ":root { --x: \"#zzz\"; }")]
        [TestCase(":root { --x: #12345; }", ":root { --x: \"#12345\"; }")]
        [TestCase(":root { --x: #abcxyz; }", ":root { --x: \"#abcxyz\"; }")]
        [TestCase(":root { --x: a); }", ":root { --x: \"a)\"; }")]
        [TestCase(":root { --x: var(--a)); }", ":root { --x: \"var(--a))\"; }")]
        [TestCase(":root { --x: rgb(100%, 0%, 0%); }", ":root { --x: \"rgb(100%, 0%, 0%)\"; }")]
        [TestCase(":root { --x: rgba(0, 0, 0); }", ":root { --x: \"rgba(0, 0, 0)\"; }")]
        [TestCase(":root { --x: rgb(255, 0, 0, 0.5); }", ":root { --x: \"rgb(255, 0, 0, 0.5)\"; }")]
        [TestCase(":root { --x: blur(); }", ":root { --x: \"blur()\"; }")]
        [TestCase(":root { --x: var(); }", ":root { --x: \"var()\"; }")]
        [TestCase(":root { --x: resource(); }", ":root { --x: \"resource()\"; }")]
        [TestCase(":root { --x: resource( ); }", ":root { --x: \"resource( )\"; }")]
        [TestCase(":root { --x: rgb(); }", ":root { --x: \"rgb()\"; }")]
        [TestCase(":root { --x: NONE(); }", ":root { --x: \"NONE()\"; }")]
        [TestCase(":root { --x: rgb(255 0 0 / 0.5); }", ":root { --x: \"rgb(255 0 0 / 0.5)\"; }")]
        [TestCase(":root { --x: rgb((1), 2, 3); }", ":root { --x: \"rgb((1), 2, 3)\"; }")]
        [TestCase(".a { --x: rgb(1, 2, a", ".a { --x: \"rgb(1, 2, a\"")]
        [TestCase(":root { --x: var(--a,); }", ":root { --x: \"var(--a,)\"; }")]
        [TestCase(":root { --x: var(--a, ); }", ":root { --x: \"var(--a, )\"; }")]
        [TestCase(":root { --x: var(--a, 1px, 2px); }", ":root { --x: \"var(--a, 1px, 2px)\"; }")]
        [TestCase(":root { --x: var(a); }", ":root { --x: \"var(a)\"; }")]
        [TestCase(":root { --x: var(abc); }", ":root { --x: \"var(abc)\"; }")]
        [TestCase(":root { --x: var(--); }", ":root { --x: \"var(--)\"; }")]
        [TestCase(":root { --x: var(--a b); }", ":root { --x: \"var(--a b)\"; }")]
        [TestCase(":root { --x: rgb(var(--r), 0, 0); }", ":root { --x: \"rgb(var(--r), 0, 0)\"; }")]
        [TestCase(":root { --x: rgb(a, 0, 0); }", ":root { --x: \"rgb(a, 0, 0)\"; }")]
        public void Given_ATokenTheImporterRejectsOrDrops_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: rgb(0 0 0 / 50%); }", ":root { --x: \"rgb(0 0 0 / 50%)\"; }")]
        [TestCase(":root { --x: rgb(255, 0, 0, 50%); }", ":root { --x: \"rgb(255, 0, 0, 50%)\"; }")]
        [TestCase(":root { --x: rgb(1, 2, 3, foo); }", ":root { --x: \"rgb(1, 2, 3, foo)\"; }")]
        [TestCase(":root { --x: rgb(var(--r), 0, 0, 0); }", ":root { --x: \"rgb(var(--r), 0, 0, 0)\"; }")]
        [TestCase(":root { --x: rgb(59 130 246 / var(--o)); }", ":root { --x: \"rgb(59 130 246 / var(--o))\"; }")]
        [TestCase(":root { --x: rgba(255 0 / 0 0.5); }", ":root { --x: \"rgba(255 0 / 0 0.5)\"; }")]
        [TestCase(":root { --x: rgba(255 0 0 0.5 / 1); }", ":root { --x: \"rgba(255 0 0 0.5 / 1)\"; }")]
        [TestCase(":root { --x: rgba(a, 1, 2, 3, 4); }", ":root { --x: \"rgba(a, 1, 2, 3, 4)\"; }")]
        [TestCase(":root { --x: rgba(255, 0, 0, 1, 9); }", ":root { --x: \"rgba(255, 0, 0, 1, 9)\"; }")]
        [TestCase(":root { --x: rgba(255, 0, 0, / 0.5); }", ":root { --x: \"rgba(255, 0, 0, / 0.5)\"; }")]
        [TestCase(":root { --x: rgb(5., 0, 0); }", ":root { --x: \"rgb(5., 0, 0)\"; }")]
        [TestCase(":root { --x: rgb(#123, 1, 2, 3); }", ":root { --x: \"rgb(#123, 1, 2, 3)\"; }")]
        [TestCase(":root { --x: rgb(0.5, 0, 0); }", ":root { --x: \"rgb(0.5, 0, 0)\"; }")]
        [TestCase(":root { --x: rgb(1, 2 3); }", ":root { --x: \"rgb(1, 2 3)\"; }")]
        [TestCase(":root { --x: rgba(0 0 0 0.5); }", ":root { --x: \"rgba(0 0 0 0.5)\"; }")]
        [TestCase(":root { --x: rgb(1, 2, +); }", ":root { --x: \"rgb(1, 2, +)\"; }")]
        [TestCase(":root { --x: rgba(1, 2, 3, .); }", ":root { --x: \"rgba(1, 2, 3, .)\"; }")]
        [TestCase(":root { --x: rgb(u+1 2 3); }", ":root { --x: \"rgb(u+1 2 3)\"; }")]
        public void Given_AnRgbTheImporterReadsOtherwiseThanCss_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base quotes a value holding a math function or a rem wherever it stands.
        // ReadsHeldChannels quotes these all the same, as the importer stores the colour of the numbers around them.
        [TestCase(":root { --x: rgb(0 0 0 / calc(0.5)); }", ":root { --x: \"rgb(0 0 0 / calc(0.5))\"; }")]
        [TestCase(":root { --x: rgb(calc(0.5), 0, 0, 0); }", ":root { --x: \"rgb(calc(0.5), 0, 0, 0)\"; }")]
        [TestCase(":root { --x: rgb(1rem, 0, 0, 0); }", ":root { --x: \"rgb(1rem, 0, 0, 0)\"; }")]
        public void Given_AnRgbHoldingAMathFunctionOrARem_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: blur(/* x */ 4px); }", ":root { --x: \"blur(  4px)\"; }")]
        [TestCase(":root { --x: var(--a /* c */); }", ":root { --x: \"var(--a  )\"; }")]
        [TestCase(":root { --x: linear-gradient(red /* a */, blue); }", ":root { --x: \"linear-gradient(red  , blue)\"; }")]
        [TestCase(":root { --x: filter(/* c */ a); }", ":root { --x: \"filter(  a)\"; }")]
        [TestCase(":root { --x: resource(/* c */); }", ":root { --x: \"resource( )\"; }")]
        public void Given_ACommentAmongAFunctionsArguments_When_TheSheetIsRewritten_Then_ItsValueIsQuotedWithoutIt(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: url(a b); }", ":root { --x: \"url(a b)\"; }")]
        [TestCase(":root { --x: url(a /* c */); }", ":root { --x: \"url(a /* c */)\"; }")]
        [TestCase(":root { --x: url(\"a\" /* c */); }", ":root { --x: \"url(\\\"a\\\"  )\"; }")]
        [TestCase(":root { --x: url(\"a\" b); }", ":root { --x: \"url(\\\"a\\\" b)\"; }")]
        [TestCase(":root { --x: url(a(b)); }", ":root { --x: \"url(a(b))\"; }")]
        [TestCase(":root { --x: url(a'b'); }", ":root { --x: \"url(a'b')\"; }")]
        [TestCase(":root { --x: url(a\u0001b); }", ":root { --x: \"url(a\u0001b)\"; }")]
        [TestCase(":root { --x: url(\"a\nb); }", ":root { --x: \"url(\\\"a\\a b)\"\n; }")]
        [TestCase(":root { --x: url(a\\\nb); }", ":root { --x: \"url(a\\\\\\a b)\"\n; }")]
        [TestCase(":root { --x: url(a\fb); }", ":root { --x: \"url(a\\c b)\"; }")]
        [TestCase(":root { --x: url(a\rb); }", ":root { --x: \"url(a\\d b)\"\n; }")]
        [TestCase(":root{--x:\nurl(b c)}", ":root{--x: \"url(b c)\"\n}")]
        [TestCase(":root { --x: url(a\\1234567 b); }", ":root { --x: \"url(a\\\\1234567 b)\"; }")]
        [TestCase(":root { --x: url(a\\31(b)); }", ":root { --x: \"url(a\\\\31(b))\"; }")]
        [TestCase(".a { --x: url(", ".a { --x: \"url(\"")]
        [TestCase(".a { --x: url(a ", ".a { --x: \"url(a\"")]
        [TestCase(".a { --x: url(a\\", ".a { --x: \"url(a\\\\\"")]
        [TestCase(".a { --x: url(\"a\\", ".a { --x: \"url(\\\"a\\\\\"")]
        [TestCase(":root { --x: u+1; }", ":root { --x: \"u+1\"; }")]
        [TestCase(":root { --x: U+12; }", ":root { --x: \"U+12\"; }")]
        [TestCase(":root { --x: 1px u+1; }", ":root { --x: \"1px u+1\"; }")]
        [TestCase(":root { --x: var(--a, u+1); }", ":root { --x: \"var(--a, u+1)\"; }")]
        public void Given_AUrlTheLexerRefusesOrARange_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: url(a/*c*/) 1rem; }", ":root { --x: \"url(a/*c*/) 1rem\"; }")]
        public void Given_AUrlHoldingSlashStarInAQuotedValue_When_TheSheetIsRewritten_Then_ThePathKeepsIt(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --icons: url(icons/*.svg); --y: 1rem; }", ":root { --icons: url(icons/*.svg); --y: \"1rem\"; }")]
        [TestCase(".a { background-image: url(icons/*.svg); --y: 1rem; }", ".a { background-image: url(icons/*.svg); --y: \"1rem\"; }")]
        [TestCase(":root { --hero: url(images/don't-panic.png); --gap: 1rem; }", ":root { --hero: \"url(images/don't-panic.png)\"; --gap: \"1rem\"; }")]
        [TestCase(":root { --bg: url(images/bg.png\"); --y: 1rem; }", ":root { --bg: \"url(images/bg.png\\\")\"; --y: \"1rem\"; }")]
        [TestCase(":root { --x: url(a'\\)/*b*/); }", ":root { --x: \"url(a'\\\\)/*b*/)\"; }")]
        public void Given_AnUnquotedUrlPathHoldingACommentOpenerOrAQuote_When_TheSheetIsRewritten_Then_ItEndsAtItsParenthesis(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: \\75rl(b/*c); --y: 1rem; }", ":root { --x: \\75rl(b/*c); --y: \"1rem\"; }")]
        [TestCase(":root { --x: ur\\l(b/*c); --y: 1rem; }", ":root { --x: ur\\l(b/*c); --y: \"1rem\"; }")]
        [TestCase(":root { --x: \\31  url(b/*c); --y: 1rem; }", ":root { --x: \\31  url(b/*c); --y: \"1rem\"; }")]
        [TestCase(":root { --x: \\\nurl(b/*c); --y: 1rem; }", ":root { --x: \\\nurl(b/*c); --y: \"1rem\"; }")]
        [TestCase(":root { --x: do\\00006Dain(icons/*.svg); --y: 1rem; }", ":root { --x: do\\00006Dain(icons/*.svg); --y: \"1rem\"; }")]
        public void Given_AnEscapeInAUrlFunctionsName_When_TheSheetIsRewritten_Then_ItsPathIsSteppedOverAsTheLexerReadsIt(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: domain(a\")\"); --y: 2px; }\n.b { color: red; }", ":root { --x: \"domain(a\\\")\\\")\"; --y: 2px; }\n.b { color: red; }")]
        [TestCase(":root { --x: url-prefix(a\")\"); --y: 2px; }\n.b { color: red; }", ":root { --x: \"url-prefix(a\\\")\\\")\"; --y: 2px; }\n.b { color: red; }")]
        [TestCase(":root { --x: domain(icons/*.svg); --y: 1rem; }", ":root { --x: domain(icons/*.svg); --y: \"1rem\"; }")]
        [TestCase(":root { --x: domain(a/*)*/b); --y: 2px; }", ":root { --x: \"domain(a/*)*/b)\"; --y: 2px; }")]
        public void Given_AUrlPrefixOrDomain_When_TheSheetIsRewritten_Then_ItsPathIsSteppedOverWhereTheLexerReadsIt(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(".a {\r  --m: calc(1rem\r    + 2px);\r  --x: 4px;\r}", ".a {\r  --m: \"calc(1rem     + 2px)\"\n;\r  --x: 4px;\r}")]
        public void Given_AValueALoneCarriageReturnBreaks_When_ItIsQuoted_Then_ALineFeedFollowsIt(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: url(\t\"\n); --y: 1rem; }", ":root { --x: \"url(\t\\\"\\a )\"\n; --y: \"1rem\"; }")]
        [TestCase(":root { --x: url(\"a\nb\"); }", ":root { --x: \"url(\\\"a\\a b\\\"); }\"\n")]
        [TestCase(":root { --x: url(a b) a\\;b; --y: 1rem; }", ":root { --x: \"url(a b) a\\\\;b\"; --y: \"1rem\"; }")]
        public void Given_AStringALineBreakEndsOrAnEscapedSemicolon_When_TheSheetIsRewritten_Then_TheValueEndsWhereCssEndsIt(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: 1rem\f2rem; }", ":root { --x: \"1rem 2rem\"; }")]
        public void Given_AFormFeedBetweenTokens_When_ItsValueIsQuoted_Then_ItIsWrittenAsASpace(string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: 4px !IMPORTANT; }", ":root { --x: 4px !important; }")]
        [TestCase(":root { --x: red ! Important; }", ":root { --x: red !important; }")]
        [TestCase(":root { --x: \"a /* b */ c\" !IMPORTANT; }", ":root { --x: \"a /* b */ c\" !important; }")]
        [TestCase(":root { --x: a\\\\!IMPORTANT; }", ":root { --x: a\\\\ !important; }")]
        [TestCase(":root { --x: \"a\\\nb\" !IMPORTANT; }", ":root { --x: \"a\\\nb\" !important; }")]
        public void Given_AnImportantSpelledInAnotherCase_When_TheSheetIsRewritten_Then_ItIsWrittenInLowerCase(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base already quotes it, for a unit `p` neither CSS nor the importer
        // reads; it stays quoted because the importer's lexer drops the full stop and stores `3 p`.
        [TestCase(":root { --x: 3.p; }", ":root { --x: \"3.p\"; }")]
        public void Given_AFullStopNoDigitFollowsAfterANumber_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: \"a\" 1rem; }", ":root { --x: \"\\\"a\\\" 1rem\"; }")]
        [TestCase(":root { --x: 'a' 1rem; }", ":root { --x: \"'a' 1rem\"; }")]
        [TestCase(":root { --x: \"a\" 16 / 9; }", ":root { --x: \"\\\"a\\\" 16 / 9\"; }")]
        [TestCase(":root { --x: \"a\\\"b\" 1rem; }", ":root { --x: \"\\\"a\\\\\\\"b\\\" 1rem\"; }")]
        [TestCase(":root { --x: var(--a, \"b\") 1rem; }", ":root { --x: \"var(--a, \\\"b\\\") 1rem\"; }")]
        [TestCase(":root { --x: resource(\"a\") 1rem; }", ":root { --x: \"resource(\\\"a\\\") 1rem\"; }")]
        [TestCase(":root { --x: rgb(\"a\", 0, 0); }", ":root { --x: \"rgb(\\\"a\\\", 0, 0)\"; }")]
        [TestCase(":root { --x: var(--a, \"b)\", 1px); }", ":root { --x: \"var(--a, \\\"b)\\\", 1px)\"; }")]
        [TestCase(":root { --x: \"a\nb\"; }", ":root { --x: \"\\\"a\\a b\\\"; }\"\n")]
        [TestCase(":root { --x: @url(a\")\"); --y: 2px; }\n.b { color: red; }", ":root { --x: \"@url(a\\\")\\\")\"; --y: 2px; }\n.b { color: red; }")]
        [TestCase(":root { --x: #url(a\")\"); --y: 2px; }\n.b { color: red; }", ":root { --x: \"#url(a\\\")\\\")\"; --y: 2px; }\n.b { color: red; }")]
        [TestCase(":root { --x: \0url(a\")\"); --y: 2px; }\n.b { color: red; }", ":root { --x: \"\0url(a\\\")\\\")\"; --y: 2px; }\n.b { color: red; }")]
        public void Given_AStringBesideATokenTheImporterRejects_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base already leaves each of these as written. A string is one token
        // whatever it holds, so a comma, a parenthesis or a comment opener in it is no token.
        [TestCase(":root { --font: \"Inter\", 'Segoe UI', sans-serif; }")]
        [TestCase(":root { --x: var(--a, \"b, c\"); }")]
        [TestCase(":root { --x: var(--a, \"b)\"); }")]
        [TestCase(":root { --x: var(--a, \"/* c */\"); }")]
        [TestCase(":root { --x: \"a /* c */ b\" \"1rem\"; }")]
        [TestCase(":root { --x: \"a\\\"b\"; }")]
        [TestCase(":root { --x: resource(\"a)1rem\"); }")]
        [TestCase(":root { --x: url(\"a)1rem\"); }")]
        [TestCase(".a { --x: \"abc")]
        public void Given_AValueHoldingAStringTheImporterHolds_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        [TestCase(":root { --x: rgb(300, 0, 0); }", ":root { --x: rgb(255, 0, 0); }")]
        [TestCase(":root { --x: rgb(256 0 0); }", ":root { --x: rgb(255 0 0); }")]
        [TestCase(":root { --x: rgb(+300, 0, 0); }", ":root { --x: rgb(255, 0, 0); }")]
        [TestCase(":root { --x: rgb(1, 999, 1000); }", ":root { --x: rgb(1, 255, 255); }")]
        [TestCase(":root { --x: rgb(-1, 2, 3); }", ":root { --x: rgb(0, 2, 3); }")]
        [TestCase(":root { --x: rgb(1 2 -300); }", ":root { --x: rgb(1 2 0); }")]
        [TestCase(":root { --x: rgba(1, 2, 3, 2); }", ":root { --x: rgba(1, 2, 3, 1); }")]
        [TestCase(":root { --x: rgba(1 2 3 / 1.5); }", ":root { --x: rgba(1 2 3 / 1); }")]
        [TestCase(":root { --x: rgba(1, 2, 3, -0.5); }", ":root { --x: rgba(1, 2, 3, 0); }")]
        [TestCase(":root { --x: rgba(300, 2, 3, 0.5); }", ":root { --x: rgba(255, 2, 3, 0.5); }")]
        [TestCase(":root { --x: rgb(300, 0, 0) !important; }", ":root { --x: rgb(255, 0, 0) !important; }")]
        [TestCase(":root { --x: rgb(1 /* c */ 2 300); }", ":root { --x: rgb(1   2 255); }")]
        [TestCase(":root { --x: linear-gradient(rgb(300, 0, 0), blue); }", ":root { --x: linear-gradient(rgb(255, 0, 0), blue); }")]
        [TestCase(":root { --x: blur(4px) rgb(300, 0, 0) rgba(1, 2, 3, 2); }", ":root { --x: blur(4px) rgb(255, 0, 0) rgba(1, 2, 3, 1); }")]
        [TestCase(":root { --x: var(--a, rgb(300, 0, 0)); }", ":root { --x: var(--a, rgb(255, 0, 0)); }")]
        public void Given_AnRgbChannelOrAlphaBeyondItsRange_When_TheSheetIsRewritten_Then_ItIsWrittenClamped(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base already leaves each of these as written. Each is on the edge of its
        // range or inside it, so nothing is clamped.
        [TestCase(":root { --x: rgb(255, 255, 255); }")]
        [TestCase(":root { --x: rgb(0, 0, 0); }")]
        [TestCase(":root { --x: rgb(-0, 0, 0); }")]
        [TestCase(":root { --x: rgb(+1, 2, 3) rgba(-0, 2, 3, +.5); }")]
        [TestCase(":root { --x: rgba(1, 2, 3, 1); }")]
        [TestCase(":root { --x: rgba(1, 2, 3, 1.0); }")]
        [TestCase(":root { --x: rgba(1, 2, 3, 0.0); }")]
        [TestCase(":root { --x: rgba(1, 2, 3, 0); }")]
        [TestCase(":root { --x: rgba(1 2 3 / 0.999); }")]
        public void Given_AnRgbWithinItsRange_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        // GREEN_ON_BASE(characterization): the base quotes it already, for the fraction; the channel in the text stays as
        // written, since Velvet's colour reader clamps it.
        [TestCase(":root { --x: rgb(300, 0, 0.5); }", ":root { --x: \"rgb(300, 0, 0.5)\"; }")]
        public void Given_AnRgbBeyondItsRangeTheImporterAlsoReadsOtherwiseThanCss_When_TheSheetIsRewritten_Then_ItsValueIsQuotedAsWritten(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: resource(u+1); }", ":root { --x: resource(\"u+1\"); }")]
        [TestCase(":root { --x: resource(U+1F600); }", ":root { --x: resource(\"U+1F600\"); }")]
        [TestCase(":root { --x: resource(u+?); }", ":root { --x: resource(\"u+?\"); }")]
        [TestCase(":root { --x: resource( u+1 ); }", ":root { --x: resource(\"u+1\"); }")]
        [TestCase(":root { --x: resource(img/u+1.png); }", ":root { --x: resource(\"img/u+1.png\"); }")]
        [TestCase(":root { --x: resource(u+1 /* c */); }", ":root { --x: resource(\"u+1\"); }")]
        [TestCase(":root { --x: resource(a u+1) 1px; }", ":root { --x: resource(\"au+1\") 1px; }")]
        [TestCase(":root { --x: resource(u+1) !important; }", ":root { --x: resource(\"u+1\") !important; }")]
        [TestCase(":root { --x: blur(resource(u+1)); }", ":root { --x: blur(resource(\"u+1\")); }")]
        public void Given_AResourceHoldingARange_When_TheSheetIsRewritten_Then_ItsPathIsAStringOfTheText(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        [TestCase(":root { --x: resource(/* c */ a); }", ":root { --x: resource(  a); }")]
        [TestCase(":root { --x: resource(a/* c */b); }", ":root { --x: resource(a b); }")]
        [TestCase(":root { --x: resource(a/b.png/* c */) !IMPORTANT; }", ":root { --x: resource(a/b.png ) !important; }")]
        [TestCase(":root { --x: resource(/* \" */ a) 1px; }", ":root { --x: resource(  a) 1px; }")]
        public void Given_AResourceHoldingAComment_When_TheSheetIsRewritten_Then_ItIsWrittenWithoutTheComment(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base already leaves each of these as written. A `u` after a name
        // character starts no range, and a string among the arguments is left to the importer.
        [TestCase(":root { --x: resource(menu+1); }")]
        [TestCase(":root { --x: resource(\"u+1\"); }")]
        [TestCase(":root { --x: resource(\"u+1\" u+1); }")]
        [TestCase(":root { --x: resource(up+1); }")]
        [TestCase(":root { --x: resource(a/b.png) /* c */; }")]
        public void Given_AResourceTheImporterStoresAsWritten_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        [TestCase(":root { --x: resource(icon@2x.png); }", ":root { --x: resource(\"icon@2x.png\"); }")]
        [TestCase(":root { --x: resource(img/50%.png); }", ":root { --x: resource(\"img/50%.png\"); }")]
        [TestCase(":root { --x: resource(a#b); }", ":root { --x: resource(\"a#b\"); }")]
        [TestCase(":root { --x: resource(@a); }", ":root { --x: resource(\"@a\"); }")]
        [TestCase(":root { --x: resource(url(a)); }", ":root { --x: resource(\"url(a)\"); }")]
        [TestCase(":root { --x: resource(2x/a.png); }", ":root { --x: resource(\"2x/a.png\"); }")]
        [TestCase(":root { --x: resource(a/-1x); }", ":root { --x: resource(\"a/-1x\"); }")]
        [TestCase(":root { --x: resource(a/1.5x); }", ":root { --x: resource(\"a/1.5x\"); }")]
        [TestCase(":root { --x: resource(a /* c */ @2x); }", ":root { --x: resource(\"a@2x\"); }")]
        public void Given_AResourceTheImporterStoresOtherwiseThanWritten_When_TheSheetIsRewritten_Then_ItsPathIsAStringOfTheText(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base already leaves each of these as written. None holds a token whose
        // text the importer shortens.
        [TestCase(":root { --x: resource(img/1.png); }")]
        [TestCase(":root { --x: resource(a/b-1.png); }")]
        [TestCase(":root { --x: resource(a_1x/b.png); }")]
        [TestCase(":root { --x: resource(1/2); }")]
        [TestCase(":root { --x: resource(a:b); }")]
        [TestCase(":root { --x: resource(a 1 b); }")]
        public void Given_AResourceTheImporterStoresAsWritten_When_TheSheetIsRewritten_Then_ItsPathIsLeftAlone(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        [TestCase(":root { --x: \"a\"b; }", ":root { --x: \"\\\"a\\\"b\"; }")]
        [TestCase(":root { --x: a\"b\"; }", ":root { --x: \"a\\\"b\\\"\"; }")]
        [TestCase(":root { --x: \"a\"\"b\"; }", ":root { --x: \"\\\"a\\\"\\\"b\\\"\"; }")]
        [TestCase(":root { --x: 'a'b !IMPORTANT; }", ":root { --x: \"'a'b\" !important; }")]
        public void Given_AStringGluedToAnIdentifierOrAString_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base already leaves each of these as written. The importer stores the
        // tokens of each apart, with whitespace or another kind of token between them.
        [TestCase(":root { --x: \"a\" b; }")]
        [TestCase(":root { --x: \"a\"b 1px; }")]
        [TestCase(":root { --x: \"a\", b; }")]
        [TestCase(":root { --x: \"a\"blur(4px); }")]
        [TestCase(":root { --x: \"a\"; }")]
        [TestCase(":root { --x: a b; }")]
        public void Given_AStringTheImporterStoresApartFromItsNeighbours_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        [TestCase(":root { --x: \"a\"/* c */b; }", ":root { --x: \"\\\"a\\\" b\"; }")]
        [TestCase(":root { --x: \"a\"/**/\"b\"; }", ":root { --x: \"\\\"a\\\" \\\"b\\\"\"; }")]
        [TestCase(":root { --x: a/**/\"b\"; }", ":root { --x: \"a \\\"b\\\"\"; }")]
        public void Given_AStringGluedToAnotherTokenByAComment_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(
            string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base already leaves each of these as written. A comment between a string
        // and the next token has whitespace beside it, which keeps the importer from joining them.
        [TestCase(":root { --x: \"a\" /* c */ b; }")]
        [TestCase(":root { --x: \"a\"/* c */ b; }")]
        public void Given_AStringSeparatedFromAnIdentifierByAComment_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        // GREEN_ON_BASE(characterization): the base already leaves each of these as written. A backslash outside a string
        // stands for a character the rewrite would have to decode to spell the path as a string.
        [TestCase(":root { --x: resource(a\\ b%); }")]
        [TestCase(":root { --x: resource(My\\ Folder/x@2x.png); }")]
        public void Given_AResourceHoldingAnEscapeAndATokenTheImporterShortens_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        [TestCase(":root { --x: a/**/b; }", ":root { --x: \"a b\"; }")]
        [TestCase(":root { --x: a/**/b/**/c; }", ":root { --x: \"a b c\"; }")]
        [TestCase(":root { --x: a/**/-b; }", ":root { --x: \"a -b\"; }")]
        public void Given_IdentifiersGluedByAComment_When_TheSheetIsRewritten_Then_ItsValueIsQuoted(string uss, string expected)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base already leaves each of these as written. A single identifier, a
        // comment beside whitespace and a comment before another kind of token leave the importer nothing to join.
        [TestCase(":root { --x: a/**/; }")]
        [TestCase(":root { --x: /**/a; }")]
        [TestCase(":root { --x: a /**/ b; }")]
        [TestCase(":root { --x: a/**/1px; }")]
        public void Given_ACommentThatGluesNoTwoIdentifiers_When_TheSheetIsRewritten_Then_ItIsUnchanged(string uss)
        {
            // Arrange
            // Act
            var rewritten = RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(uss));
        }

        private static readonly TimeSpan RewriteBound = TimeSpan.FromSeconds(10);

        // On a thread of its own, so a rewrite that never returns fails its case rather than holding the suite; it then
        // reads as null, which each caller fails its case on.
        internal static string RewriteWithin(string uss)
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
