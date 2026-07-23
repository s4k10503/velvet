using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pure coverage for <see cref="StyleTextEffectClass"/>: parsing the text-transform / text-decoration /
    /// whitespace-pre-line utilities into a <see cref="TextEffect"/> (each axis nullable so an explicit reset
    /// differs from unset — an explicit whitespace-* class resolves the Whitespace axis to its own None
    /// reset, mirroring normal-case / no-underline, see Parse), and applying a resolved effect to a raw
    /// string (pre-line collapse first, then transform, then the rich-text decoration wrap). GWT, one assert
    /// per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleTextEffectClassTests
    {
        [Test]
        public void Given_Uppercase_When_Parsed_Then_TransformIsUpper()
        {
            Assert.That(StyleTextEffectClass.Parse(new[] { "uppercase" }).Transform, Is.EqualTo(TextTransformKind.Upper));
        }

        [Test]
        public void Given_NormalCase_When_Parsed_Then_TransformIsExplicitNone()
        {
            // normal-case is the explicit reset (None), distinct from unset (null) so it can stop inheritance.
            Assert.That(StyleTextEffectClass.Parse(new[] { "normal-case" }).Transform, Is.EqualTo(TextTransformKind.None));
        }

        [Test]
        public void Given_NoTextTokens_When_Parsed_Then_TransformIsUnsetNull()
        {
            Assert.That(StyleTextEffectClass.Parse(new[] { "bg-red-500", "p-4" }).Transform, Is.Null);
        }

        [Test]
        public void Given_Underline_When_Parsed_Then_DecorationIsUnderline()
        {
            Assert.That(StyleTextEffectClass.Parse(new[] { "underline" }).Decoration, Is.EqualTo(TextDecorationKind.Underline));
        }

        [Test]
        public void Given_TransformAndDecoration_When_Parsed_Then_BothAxesSet()
        {
            var e = StyleTextEffectClass.Parse(new[] { "uppercase", "underline" });

            Assert.That((e.Transform.Value, e.Decoration.Value), Is.EqualTo((TextTransformKind.Upper, TextDecorationKind.Underline)));
        }

        [Test]
        public void Given_Upper_When_Applied_Then_StringIsUppercased()
        {
            Assert.That(StyleTextEffectClass.Apply("hello", TextTransformKind.Upper, null), Is.EqualTo("HELLO"));
        }

        [Test]
        public void Given_Capitalize_When_Applied_Then_EachWordIsTitleCased()
        {
            // CSS capitalize touches only the first letter of each word; the rest is left as-is.
            Assert.That(StyleTextEffectClass.Apply("hello wORLD", TextTransformKind.Capitalize, null), Is.EqualTo("Hello WORLD"));
        }

        [Test]
        public void Given_Underline_When_Applied_Then_WrappedInUTag()
        {
            Assert.That(StyleTextEffectClass.Apply("hi", null, TextDecorationKind.Underline), Is.EqualTo("<u>hi</u>"));
        }

        [Test]
        public void Given_LineThrough_When_Applied_Then_WrappedInSTag()
        {
            Assert.That(StyleTextEffectClass.Apply("hi", null, TextDecorationKind.LineThrough), Is.EqualTo("<s>hi</s>"));
        }

        [Test]
        public void Given_TransformAndDecoration_When_Applied_Then_TransformRunsBeforeWrap()
        {
            // Transform applies to the plain text, then the decoration wraps the transformed result.
            Assert.That(StyleTextEffectClass.Apply("hi", TextTransformKind.Upper, TextDecorationKind.LineThrough), Is.EqualTo("<s>HI</s>"));
        }

        [Test]
        public void Given_EmptyText_When_Applied_Then_StaysEmptyWithNoTags()
        {
            // No empty <u></u> for empty content.
            Assert.That(StyleTextEffectClass.Apply("", TextTransformKind.Upper, TextDecorationKind.Underline), Is.EqualTo(""));
        }

        [Test]
        public void Given_NoneTransform_When_Applied_Then_TextUnchanged()
        {
            Assert.That(StyleTextEffectClass.Apply("MixedCase", TextTransformKind.None, null), Is.EqualTo("MixedCase"));
        }

        [Test]
        public void Given_WhitespacePreLine_When_Parsed_Then_WhitespaceIsPreLine()
        {
            Assert.That(StyleTextEffectClass.Parse(new[] { "whitespace-pre-line" }).Whitespace, Is.EqualTo(WhitespaceCollapseKind.PreLine));
        }

        [Test]
        public void Given_NoWhitespaceTokens_When_Parsed_Then_WhitespaceIsUnsetNull()
        {
            Assert.That(StyleTextEffectClass.Parse(new[] { "bg-red-500", "p-4" }).Whitespace, Is.Null);
        }

        [Test]
        public void Given_WhitespacePreLineAndExplicitWhitespacePreOnSameElement_When_Parsed_Then_ExplicitClassWinsWithNone()
        {
            // The direct, single-purpose whitespace-pre class wins over pre-line's collapsing rewrite when
            // both tokens sit on the same element (see the Why comment in Parse for the full rationale) —
            // and resolves to the explicit None reset, not back to unset, so it also blocks a farther
            // ancestor's pre-line from reaching this subtree.
            Assert.That(
                StyleTextEffectClass.Parse(new[] { "whitespace-pre-line", "whitespace-pre" }).Whitespace,
                Is.EqualTo(WhitespaceCollapseKind.None));
        }

        [Test]
        public void Given_WhitespaceNowrapAlone_When_Parsed_Then_WhitespaceIsExplicitNone()
        {
            // Mirrors normal-case / no-underline: an explicit whitespace-* class is tracked as the None reset
            // even with no ancestor whitespace-pre-line around, because blocking a FARTHER ancestor's
            // pre-line is the whole point of the reset — Parse cannot know at parse time whether an ancestor
            // exists to block.
            Assert.That(StyleTextEffectClass.Parse(new[] { "whitespace-nowrap" }).Whitespace, Is.EqualTo(WhitespaceCollapseKind.None));
        }

        [Test]
        public void Given_SpaceAndTabRuns_When_CollapsedForPreLine_Then_EachRunFoldsToASingleSpace()
        {
            Assert.That(
                StyleTextEffectClass.Apply("a  b\tc \t d", null, null, WhitespaceCollapseKind.PreLine),
                Is.EqualTo("a b c d"));
        }

        [Test]
        public void Given_ConsecutiveNewlines_When_CollapsedForPreLine_Then_TheyArePreserved()
        {
            // A blank line (two consecutive newlines with nothing between) survives verbatim — pre-line
            // preserves every forced break, not just single ones.
            Assert.That(
                StyleTextEffectClass.Apply("a\nb\n\nc", null, null, WhitespaceCollapseKind.PreLine),
                Is.EqualTo("a\nb\n\nc"));
        }

        [Test]
        public void Given_LeadingAndTrailingSpacesPerLine_When_CollapsedForPreLine_Then_TheyCollapseAway()
        {
            // Covers both the whole string's edges and the internal per-line edges around the shared \n.
            Assert.That(
                StyleTextEffectClass.Apply("  first line  \n  second line  ", null, null, WhitespaceCollapseKind.PreLine),
                Is.EqualTo("first line\nsecond line"));
        }

        [Test]
        public void Given_CrLfPair_When_CollapsedForPreLine_Then_CollapsesToSingleNewline()
        {
            // CSS Text Level 3 segment-break normalization: a '\r\n' pair is ONE break, equivalent to '\n' —
            // not two — and the run of spaces touching it still collapses away like any other line edge.
            Assert.That(
                StyleTextEffectClass.Apply("a  \r\nb", null, null, WhitespaceCollapseKind.PreLine),
                Is.EqualTo("a\nb"));
        }

        [Test]
        public void Given_BareCarriageReturn_When_CollapsedForPreLine_Then_TreatedAsNewline()
        {
            Assert.That(
                StyleTextEffectClass.Apply("a\rb", null, null, WhitespaceCollapseKind.PreLine),
                Is.EqualTo("a\nb"));
        }

        [Test]
        public void Given_ConsecutiveCrLfPairs_When_CollapsedForPreLine_Then_BlankLineSurvives()
        {
            Assert.That(
                StyleTextEffectClass.Apply("a\r\n\r\nb", null, null, WhitespaceCollapseKind.PreLine),
                Is.EqualTo("a\n\nb"));
        }

        [Test]
        public void Given_NonBreakingSpaceRun_When_CollapsedForPreLine_Then_NotCollapsed()
        {
            // NBSP (U+00A0) is not a collapsible whitespace character for pre-line — only literal space/tab
            // runs fold, matching CSS's own segment-break / whitespace-collapsing rules. Built via a char
            // cast instead of an embedded literal so the non-breaking space stays unambiguous in source.
            var nbsp = ((char)0x00A0).ToString();

            Assert.That(StyleTextEffectClass.Apply($"a{nbsp}{nbsp}b", null, null, WhitespaceCollapseKind.PreLine), Is.EqualTo($"a{nbsp}{nbsp}b"));
        }

        [Test]
        public void Given_AllWhitespaceInput_When_CollapsedWithDecoration_Then_ResultIsEmptyWithNoTags()
        {
            // The collapse alone can fully consume an all-whitespace string (every run in it touches a line
            // edge); the empty result must still honor the "empty text returned unchanged" contract instead
            // of wrapping an empty string in a decoration tag.
            Assert.That(
                StyleTextEffectClass.Apply("   ", null, TextDecorationKind.Underline, WhitespaceCollapseKind.PreLine),
                Is.EqualTo(""));
        }

        [Test]
        public void Given_WhitespaceNull_When_Applied_Then_SpaceRunsAreLeftUncollapsed()
        {
            // whitespace defaults to null (off) so every pre-existing 3-arg Apply call site keeps its behavior.
            Assert.That(StyleTextEffectClass.Apply("a   b", null, null), Is.EqualTo("a   b"));
        }

        [Test]
        public void Given_PreLineWithTransformAndDecoration_When_Applied_Then_CollapseRunsBeforeThem()
        {
            // Pipeline order: collapse the raw text first, then uppercase, then wrap — matching CSS's own
            // white-space-before-text-transform resolution order.
            Assert.That(
                StyleTextEffectClass.Apply("a   B", TextTransformKind.Upper, TextDecorationKind.Underline, WhitespaceCollapseKind.PreLine),
                Is.EqualTo("<u>A B</u>"));
        }
    }
}
