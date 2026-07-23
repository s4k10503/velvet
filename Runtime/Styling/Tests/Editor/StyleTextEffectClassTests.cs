using System.Globalization;
using System.Threading;
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

        [Test]
        public void Given_LeadingNone_When_ParsedAndApplied_Then_ProducesOneEmTag()
        {
            // Arrange
            var classNames = new[] { "leading-none" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);
            var result = StyleTextEffectClass.Apply("hi", null, null, null, effect.Leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=1em>hi</line-height>"));
        }

        [Test]
        public void Given_LeadingTight_When_ParsedAndApplied_Then_ProducesOnePointTwoFiveEmTag()
        {
            // Arrange
            var classNames = new[] { "leading-tight" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);
            var result = StyleTextEffectClass.Apply("hi", null, null, null, effect.Leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=1.25em>hi</line-height>"));
        }

        [Test]
        public void Given_LeadingSnug_When_ParsedAndApplied_Then_ProducesOnePointThreeSevenFiveEmTag()
        {
            // Arrange
            var classNames = new[] { "leading-snug" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);
            var result = StyleTextEffectClass.Apply("hi", null, null, null, effect.Leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=1.375em>hi</line-height>"));
        }

        [Test]
        public void Given_LeadingNormal_When_ParsedAndApplied_Then_ProducesOnePointFiveEmTag()
        {
            // Arrange
            var classNames = new[] { "leading-normal" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);
            var result = StyleTextEffectClass.Apply("hi", null, null, null, effect.Leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=1.5em>hi</line-height>"));
        }

        [Test]
        public void Given_LeadingRelaxed_When_ParsedAndApplied_Then_ProducesOnePointSixTwoFiveEmTag()
        {
            // Arrange
            var classNames = new[] { "leading-relaxed" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);
            var result = StyleTextEffectClass.Apply("hi", null, null, null, effect.Leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=1.625em>hi</line-height>"));
        }

        [Test]
        public void Given_LeadingLoose_When_ParsedAndApplied_Then_ProducesTwoEmTag()
        {
            // Arrange
            var classNames = new[] { "leading-loose" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);
            var result = StyleTextEffectClass.Apply("hi", null, null, null, effect.Leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=2em>hi</line-height>"));
        }

        [Test]
        public void Given_LeadingBracketPx_When_ParsedAndApplied_Then_ProducesThePxTag()
        {
            // Arrange
            var classNames = new[] { "leading-[24px]" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);
            var result = StyleTextEffectClass.Apply("hi", null, null, null, effect.Leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=24px>hi</line-height>"));
        }

        [Test]
        public void Given_NoLeadingTokens_When_Parsed_Then_LeadingIsUnsetNull()
        {
            // Arrange
            var classNames = new[] { "bg-red-500", "p-4" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);

            // Assert
            Assert.That(effect.Leading, Is.Null);
        }

        [Test]
        public void Given_LeadingBracketWithNonPxUnit_When_Parsed_Then_LeadingIsUnsetNull()
        {
            // Arrange — the bracket form accepts only px for v1; an em value inside the bracket (a
            // plausible mistake, since the named presets DO use em) must be rejected rather than
            // silently misinterpreted as a pixel count.
            var classNames = new[] { "leading-[1.5em]" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);

            // Assert
            Assert.That(effect.Leading, Is.Null);
        }

        [Test]
        public void Given_LeadingBracketWithUnparseableNumber_When_Parsed_Then_LeadingIsUnsetNull()
        {
            // Arrange — a "px" suffix present but a non-numeric head, the other malformed shape
            // TryParseLeadingBracket must reject.
            var classNames = new[] { "leading-[abcpx]" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);

            // Assert
            Assert.That(effect.Leading, Is.Null);
        }

        [Test]
        public void Given_LeadingBracketWithNegativePx_When_Parsed_Then_LeadingIsUnsetNull()
        {
            // Arrange — a negative pixel count; TryParseLeadingBracket's own px >= 0f guard must reject it
            // rather than producing a negative line-height the rich-text tag cannot express.
            var classNames = new[] { "leading-[-5px]" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);

            // Assert
            Assert.That(effect.Leading, Is.Null);
        }

        [Test]
        public void Given_LeadingBracketEmpty_When_Parsed_Then_LeadingIsUnsetNull()
        {
            // Arrange — nothing at all between the brackets, so there is no "px" suffix to even find —
            // the shortest possible malformed shape.
            var classNames = new[] { "leading-[]" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);

            // Assert
            Assert.That(effect.Leading, Is.Null);
        }

        [Test]
        public void Given_LeadingBracketWithNoNumericPart_When_Parsed_Then_LeadingIsUnsetNull()
        {
            // Arrange — the "px" suffix is present but nothing precedes it, so the numeric head is empty
            // rather than merely non-numeric (the shape Given_LeadingBracketWithUnparseableNumber covers).
            var classNames = new[] { "leading-[px]" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);

            // Assert
            Assert.That(effect.Leading, Is.Null);
        }

        [Test]
        public void Given_LeadingBracketWithTrailingSpace_When_Parsed_Then_LeadingIsUnsetNull()
        {
            // Arrange — a trailing space before the closing bracket breaks the "px"-suffix match itself
            // (the last two characters become "x ", not "px"), so this is rejected before the numeric
            // parse even runs.
            var classNames = new[] { "leading-[24px ]" };

            // Act
            var effect = StyleTextEffectClass.Parse(classNames);

            // Assert
            Assert.That(effect.Leading, Is.Null);
        }

        [Test]
        public void Given_LeadingBracketClass_When_CheckedForArbitraryLeadingClass_Then_ReturnsTrue()
        {
            // Arrange
            const string cls = "leading-[24px]";

            // Act
            var isArbitrary = StyleTextEffectClass.IsArbitraryLeadingClass(cls);

            // Assert
            Assert.That(isArbitrary, Is.True);
        }

        [Test]
        public void Given_MalformedLeadingBracketClass_When_CheckedForArbitraryLeadingClass_Then_StillReturnsTrue()
        {
            // Arrange — the prefix-only check must exclude a malformed bracket value from the USS class
            // list too, exactly like a malformed font-[...] never leaks in either (see IsArbitraryLeadingClass).
            const string cls = "leading-[1.5em]";

            // Act
            var isArbitrary = StyleTextEffectClass.IsArbitraryLeadingClass(cls);

            // Assert
            Assert.That(isArbitrary, Is.True);
        }

        [Test]
        public void Given_BangPrefixedLeadingBracketClass_When_CheckedForArbitraryLeadingClass_Then_ReturnsTrue()
        {
            // Arrange — the important-modifier bang must not defeat this guard: StripImportant strips it
            // at the 3 guard call sites AFTER this check runs, so the predicate itself has to recognize
            // the bang'd form or the bare bracket token leaks into the USS class list once the bang is gone.
            const string cls = "!leading-[24px]";

            // Act
            var isArbitrary = StyleTextEffectClass.IsArbitraryLeadingClass(cls);

            // Assert
            Assert.That(isArbitrary, Is.True);
        }

        [Test]
        public void Given_LeadingNamedPresetClass_When_CheckedForArbitraryLeadingClass_Then_ReturnsFalse()
        {
            // Arrange — a named preset is a plain (non-bracket) token and stays in the USS class list as
            // an inert token, the same established pattern uppercase/whitespace-pre-line already use.
            const string cls = "leading-relaxed";

            // Act
            var isArbitrary = StyleTextEffectClass.IsArbitraryLeadingClass(cls);

            // Assert
            Assert.That(isArbitrary, Is.False);
        }

        [Test]
        public void Given_Null_When_CheckedForArbitraryLeadingClass_Then_ReturnsFalse()
        {
            // Act
            var isArbitrary = StyleTextEffectClass.IsArbitraryLeadingClass(null);

            // Assert
            Assert.That(isArbitrary, Is.False);
        }

        [Test]
        public void Given_LeadingNull_When_Applied_Then_TextUnchanged()
        {
            // Arrange — leading defaults to null (off) so every pre-existing 4-arg Apply call site keeps
            // its behavior.

            // Act
            var result = StyleTextEffectClass.Apply("hi", null, null, null);

            // Assert
            Assert.That(result, Is.EqualTo("hi"));
        }

        [Test]
        public void Given_EmptyText_When_AppliedWithLeading_Then_StaysEmptyWithNoLineHeightTag()
        {
            // Arrange
            var leading = new LeadingValue(LeadingUnit.Em, 1.625f);

            // Act
            var result = StyleTextEffectClass.Apply("", null, null, null, leading);

            // Assert
            Assert.That(result, Is.EqualTo(""));
        }

        [Test]
        public void Given_LeadingRelaxedValue_When_Applied_Then_TheEmMultiplierUsesADotDecimalNotComma()
        {
            // Arrange — 1.625 (leading-relaxed) has a fractional part, the exact shape a comma-decimal
            // thread culture would corrupt into "1,625em" (breaking the tag's own numeric grammar) if
            // InvariantCulture were ever dropped from the ToString call. The ambient culture on any known
            // dev/CI machine (ja-JP, en-US, ...) already uses a dot, so it would not catch a dropped
            // InvariantCulture call; de-DE is forced here specifically because its decimal separator is a
            // comma, making this the one culture whose presence actually discriminates. Restored in
            // finally so the switch never leaks into another test.
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var leading = new LeadingValue(LeadingUnit.Em, 1.625f);

            try
            {
                // Act
                var result = StyleTextEffectClass.Apply("hi", null, null, null, leading);

                // Assert
                Assert.That(result, Does.Contain("1.625em"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        [Test]
        public void Given_LeadingWithUppercaseUnderlineAndWhitespacePreLine_When_Applied_Then_LeadingWrapsOutermost()
        {
            // Arrange — exercises all four axes together: PreLine collapses the space run first, then
            // Transform uppercases, then Decoration wraps in <u>, then Leading wraps OUTERMOST last — a
            // layout property independent of the string's own content, so it never interferes with <u>'s
            // own nesting.
            var leading = new LeadingValue(LeadingUnit.Em, 1.625f);

            // Act
            var result = StyleTextEffectClass.Apply(
                "hi   there", TextTransformKind.Upper, TextDecorationKind.Underline, WhitespaceCollapseKind.PreLine, leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=1.625em><u>HI THERE</u></line-height>"));
        }
    }
}
