using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pure coverage for <see cref="StyleTextEffectClass"/>: parsing the text-transform / text-decoration
    /// utilities into a <see cref="TextEffect"/> (each axis nullable so an explicit reset differs from unset),
    /// and applying a resolved effect to a raw string (transform first, then the rich-text decoration wrap).
    /// GWT, one assert per case.
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
    }
}
