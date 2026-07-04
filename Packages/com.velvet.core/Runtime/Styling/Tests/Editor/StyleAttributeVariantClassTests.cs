using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Parser coverage for <see cref="StyleAttributeVariantClass"/>: the supported <c>data-[...]</c> /
    /// <c>aria-[...]</c> forms — the bare-key presence test (<c>data-[loading]:</c>) and the
    /// <c>key=value</c> equality test (<c>data-[state=open]:</c>) — whose bracketed key/value carries no
    /// <c>:</c>, plus the malformed cases that must not be claimed. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleAttributeVariantClassTests
    {
        [Test]
        public void Given_DataKeyValue_When_Parsed_Then_ResolvesDataKeyValueAndPayload()
        {
            var ok = StyleAttributeVariantClass.TryParse(
                "data-[state=open]:bg-mark", out var ns, out var key, out var value, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((ns, key, value, payload),
                Is.EqualTo((StyleAttributeNamespace.Data, "state", "open", "bg-mark")));
        }

        [Test]
        public void Given_DataBareKey_When_Parsed_Then_ResolvesPresenceWithNullValue()
        {
            // The bare-key form is a presence test, so the parsed value is null (no '=' in the bracket).
            var ok = StyleAttributeVariantClass.TryParse(
                "data-[loading]:opacity-50", out var ns, out var key, out var value, out _);

            Assume.That(ok, Is.True);
            Assert.That((ns, key, value), Is.EqualTo((StyleAttributeNamespace.Data, "loading", (string)null)));
        }

        [Test]
        public void Given_AriaKeyValue_When_Parsed_Then_ResolvesAriaNamespace()
        {
            var ok = StyleAttributeVariantClass.TryParse(
                "aria-[expanded=true]:rotate-180", out var ns, out var key, out var value, out _);

            Assume.That(ok, Is.True);
            Assert.That((ns, key, value), Is.EqualTo((StyleAttributeNamespace.Aria, "expanded", "true")));
        }

        [Test]
        public void Given_DataPayload_When_Parsed_Then_PayloadIsAfterBracket()
        {
            var ok = StyleAttributeVariantClass.TryParse("data-[state=open]:bg-mark", out _, out _, out _, out var payload);

            Assume.That(ok, Is.True);
            Assert.That(payload, Is.EqualTo("bg-mark"));
        }

        [Test]
        public void Given_ValueContainingEquals_When_Parsed_Then_OnlyFirstEqualsSplits()
        {
            // Only the first '=' splits key from value, so a value may itself contain '=' verbatim.
            var ok = StyleAttributeVariantClass.TryParse("data-[expr=a=b]:bg-mark", out _, out var key, out var value, out _);

            Assume.That(ok, Is.True);
            Assert.That((key, value), Is.EqualTo(("expr", "a=b")));
        }

        [Test]
        public void Given_StateVariant_When_Parsed_Then_IsNotAttribute()
        {
            Assert.That(StyleAttributeVariantClass.IsAttribute("hover:bg-mark"), Is.False);
        }

        [Test]
        public void Given_HasVariant_When_Parsed_Then_IsNotAttribute()
        {
            // has-[:checked]: is a sibling bracket variant but a different namespace; it must not be claimed here.
            Assert.That(StyleAttributeVariantClass.IsAttribute("has-[:checked]:bg-mark"), Is.False);
        }

        [Test]
        public void Given_DataWithEmptyPayload_When_Parsed_Then_IsNotClaimed()
        {
            // The ']' is the last character, so there is no payload after the variant ':'.
            Assert.That(StyleAttributeVariantClass.IsAttribute("data-[state=open]:"), Is.False);
        }

        [Test]
        public void Given_DataWithEmptyKey_When_Parsed_Then_IsNotClaimed()
        {
            // A leading '=' means an empty key, which is rejected.
            Assert.That(StyleAttributeVariantClass.IsAttribute("data-[=open]:bg-mark"), Is.False);
        }

        [Test]
        public void Given_DataWithEmptyBracket_When_Parsed_Then_IsNotClaimed()
        {
            Assert.That(StyleAttributeVariantClass.IsAttribute("data-[]:bg-mark"), Is.False);
        }

        [Test]
        public void Given_PresenceRule_When_KeyPresent_Then_Matches()
        {
            // expected == null is the presence form: matches whenever the key exists, regardless of value.
            Assert.That(StyleAttributeVariantClass.Matches(expected: null, present: true, actual: "anything"), Is.True);
        }

        [Test]
        public void Given_EqualityRule_When_ValueDiffers_Then_DoesNotMatch()
        {
            Assert.That(StyleAttributeVariantClass.Matches(expected: "open", present: true, actual: "closed"), Is.False);
        }

        [Test]
        public void Given_EmptyValueEqualityRule_When_PresentValueIsNull_Then_Matches()
        {
            // data-[state=]: tests for the empty string. A present attribute carrying no value resolves to ""
            // (HTML's valueless / boolean-attribute semantics), so a null stored value satisfies the rule.
            Assert.That(StyleAttributeVariantClass.Matches(expected: "", present: true, actual: null), Is.True);
        }

        [Test]
        public void Given_EmptyValueEqualityRule_When_KeyAbsent_Then_DoesNotMatch()
        {
            // The empty-value rule is still an equality test, not a presence test: it requires the key to exist.
            Assert.That(StyleAttributeVariantClass.Matches(expected: "", present: false, actual: null), Is.False);
        }

        [Test]
        public void Given_EmptyValueEqualityRule_When_PresentValueIsNonEmpty_Then_DoesNotMatch()
        {
            // The empty-value unification must not over-match: a non-empty value never equals "".
            Assert.That(StyleAttributeVariantClass.Matches(expected: "", present: true, actual: "open"), Is.False);
        }
    }
}
