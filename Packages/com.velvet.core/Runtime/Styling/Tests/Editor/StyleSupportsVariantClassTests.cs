using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Parser coverage for <see cref="StyleSupportsVariantClass"/>: the supported
    /// <c>supports-[&lt;property&gt;:&lt;value&gt;]:</c> feature-query form — whose bracketed declaration
    /// carries the property/value <c>:</c> internally — plus the malformed cases that must not be claimed.
    /// The variant is STATIC in UI Toolkit (well-formed ⇒ always-applied), so the parser only validates
    /// well-formedness; behavior is asserted in the reconciler fixture. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleSupportsVariantClassTests
    {
        [Test]
        public void Given_PropertyValueDeclaration_When_Parsed_Then_ResolvesPropertyValueAndPayload()
        {
            var ok = StyleSupportsVariantClass.TryParse(
                "supports-[display:flex]:flex-row", out var property, out var value, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((property, value, payload), Is.EqualTo(("display", "flex", "flex-row")));
        }

        [Test]
        public void Given_ValueContainingColon_When_Parsed_Then_OnlyFirstColonSplitsDeclaration()
        {
            // The first ':' inside the bracket splits property from value, so the value may contain ':'
            // verbatim (e.g. a url() with a scheme). The variant separator is the ':' after the ']'.
            var ok = StyleSupportsVariantClass.TryParse(
                "supports-[background:url(a:b)]:bg-mark", out var property, out var value, out _);

            Assume.That(ok, Is.True);
            Assert.That((property, value), Is.EqualTo(("background", "url(a:b)")));
        }

        [Test]
        public void Given_SupportsPayload_When_Parsed_Then_PayloadIsAfterBracket()
        {
            var ok = StyleSupportsVariantClass.TryParse("supports-[display:flex]:flex-row", out _, out _, out var payload);

            Assume.That(ok, Is.True);
            Assert.That(payload, Is.EqualTo("flex-row"));
        }

        [Test]
        public void Given_StateVariant_When_Parsed_Then_IsNotSupports()
        {
            Assert.That(StyleSupportsVariantClass.IsSupports("hover:bg-mark"), Is.False);
        }

        [Test]
        public void Given_AttributeVariant_When_Parsed_Then_IsNotSupports()
        {
            // data-[..]: is a sibling bracket variant but a different prefix; it must not be claimed here.
            Assert.That(StyleSupportsVariantClass.IsSupports("data-[state=open]:bg-mark"), Is.False);
        }

        [Test]
        public void Given_SupportsWithEmptyPayload_When_Parsed_Then_IsNotClaimed()
        {
            // The ']' is the last character, so there is no payload after the variant ':'.
            Assert.That(StyleSupportsVariantClass.IsSupports("supports-[display:flex]:"), Is.False);
        }

        [Test]
        public void Given_SupportsWithNoDeclarationColon_When_Parsed_Then_IsNotClaimed()
        {
            // The bracket holds a bare token with no property:value ':', so the declaration is malformed.
            Assert.That(StyleSupportsVariantClass.IsSupports("supports-[flex]:bg-mark"), Is.False);
        }

        [Test]
        public void Given_SupportsWithEmptyProperty_When_Parsed_Then_IsNotClaimed()
        {
            // A leading ':' inside the bracket means an empty property, which is rejected.
            Assert.That(StyleSupportsVariantClass.IsSupports("supports-[:flex]:bg-mark"), Is.False);
        }

        [Test]
        public void Given_SupportsWithEmptyValue_When_Parsed_Then_IsNotClaimed()
        {
            // A trailing ':' inside the bracket (declaration "display:") means an empty value, rejected.
            Assert.That(StyleSupportsVariantClass.IsSupports("supports-[display:]:bg-mark"), Is.False);
        }

        [Test]
        public void Given_SupportsWithEmptyBracket_When_Parsed_Then_IsNotClaimed()
        {
            Assert.That(StyleSupportsVariantClass.IsSupports("supports-[]:bg-mark"), Is.False);
        }
    }
}
