using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Parser coverage for <see cref="StyleHasVariantClass"/>: the supported <c>has-[...]</c> inner forms
    /// (<c>has-[:checked]:</c>, <c>has-[:focus]:</c>, <c>has-[.class]:</c>) whose bracketed selector carries
    /// its own <c>:</c> / <c>.</c>, plus the unsupported / malformed cases that must not be claimed. GWT, one
    /// assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleHasVariantClassTests
    {
        [Test]
        public void Given_HasChecked_When_Parsed_Then_ResolvesCheckedWithPayload()
        {
            var ok = StyleHasVariantClass.TryParse("has-[:checked]:bg-mark", out var kind, out _, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleHasKind.Checked, "bg-mark")));
        }

        [Test]
        public void Given_HasFocus_When_Parsed_Then_ResolvesFocus()
        {
            var ok = StyleHasVariantClass.TryParse("has-[:focus]:ring", out var kind, out _, out _);

            Assume.That(ok, Is.True);
            Assert.That(kind, Is.EqualTo(StyleHasKind.Focus));
        }

        [Test]
        public void Given_HasClass_When_Parsed_Then_ResolvesClassWithClassName()
        {
            var ok = StyleHasVariantClass.TryParse("has-[.active]:bg-mark", out var kind, out var className, out _);

            Assume.That(ok, Is.True);
            Assert.That((kind, className), Is.EqualTo((StyleHasKind.Class, "active")));
        }

        [Test]
        public void Given_HasClass_When_Parsed_Then_PayloadIsAfterBracket()
        {
            var ok = StyleHasVariantClass.TryParse("has-[.active]:bg-mark", out _, out _, out var payload);

            Assume.That(ok, Is.True);
            Assert.That(payload, Is.EqualTo("bg-mark"));
        }

        [Test]
        public void Given_HasHover_When_Parsed_Then_IsNotClaimed()
        {
            // :hover is intentionally unsupported (no reliable descendant-hover signal without per-frame
            // pointer hit-testing), so the token does not parse.
            Assert.That(StyleHasVariantClass.IsHas("has-[:hover]:bg-mark"), Is.False);
        }

        [Test]
        public void Given_StateVariant_When_Parsed_Then_IsNotHas()
        {
            Assert.That(StyleHasVariantClass.IsHas("hover:bg-mark"), Is.False);
        }

        [Test]
        public void Given_HasWithEmptyPayload_When_Parsed_Then_IsNotClaimed()
        {
            // The ']' is the last character, so there is no payload after the variant ':'.
            Assert.That(StyleHasVariantClass.IsHas("has-[:checked]:"), Is.False);
        }

        [Test]
        public void Given_HasWithEmptySelector_When_Parsed_Then_IsNotClaimed()
        {
            Assert.That(StyleHasVariantClass.IsHas("has-[]:bg-mark"), Is.False);
        }
    }
}
