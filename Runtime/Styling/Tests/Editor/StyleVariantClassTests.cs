using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="StyleVariantClass"/> parsing of state-variant tokens
    /// (<c>&lt;variant&gt;:&lt;payload&gt;</c>):
    /// <list type="bullet">
    /// <item><c>hover:</c> / <c>focus:</c> / <c>active:</c> map to their kind; the payload is the
    /// remainder (a class or an arbitrary value).</item>
    /// <item>A ':' that occurs inside <c>[...]</c> (e.g. <c>bg-[addr:key]</c>) is not a variant
    /// separator; an unknown prefix, an empty payload, and null all fail to parse.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class StyleVariantClassTests
    {
        [Test]
        public void Given_HoverClass_When_Parsed_Then_ResolvesHoverWithPayload()
        {
            var ok = StyleVariantClass.TryParse("hover:bg-blue-500", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.Hover, "bg-blue-500")));
        }

        [Test]
        public void Given_FocusClass_When_Parsed_Then_ResolvesFocus()
        {
            var ok = StyleVariantClass.TryParse("focus:border-accent", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.Focus, "border-accent")));
        }

        [Test]
        public void Given_FocusVisibleClass_When_Parsed_Then_ResolvesFocusVisible()
        {
            var ok = StyleVariantClass.TryParse("focus-visible:ring-2", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.FocusVisible, "ring-2")));
        }

        [Test]
        public void Given_ActiveArbitraryClass_When_Parsed_Then_KeepsArbitraryPayload()
        {
            var ok = StyleVariantClass.TryParse("active:w-[200px]", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.Active, "w-[200px]")));
        }

        [Test]
        public void Given_CheckedClass_When_Parsed_Then_ResolvesChecked()
        {
            var ok = StyleVariantClass.TryParse("checked:bg-accent", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.Checked, "bg-accent")));
        }

        [Test]
        public void Given_PeerCheckedClass_When_Parsed_Then_ResolvesPeerChecked()
        {
            var ok = StyleVariantClass.TryParse("peer-checked:text-accent", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.PeerChecked, "text-accent")));
        }

        [Test]
        public void Given_GroupFocusWithinClass_When_Parsed_Then_ResolvesGroupFocusWithin()
        {
            var ok = StyleVariantClass.TryParse("group-focus-within:bg-on", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.GroupFocusWithin, "bg-on")));
        }

        [Test]
        public void Given_PeerFocusWithinClass_When_Parsed_Then_ResolvesPeerFocusWithin()
        {
            var ok = StyleVariantClass.TryParse("peer-focus-within:bg-on", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.PeerFocusWithin, "bg-on")));
        }

        [Test]
        public void Given_PlainUtility_When_Checked_Then_IsNotVariant()
        {
            Assert.That(StyleVariantClass.IsVariant("bg-blue-500"), Is.False);
        }

        [Test]
        public void Given_ArbitraryWithColonInsideBrackets_When_Checked_Then_IsNotVariant()
        {
            // The ':' belongs to the arbitrary value (bg-[addr:icon]), not a variant prefix.
            Assert.That(StyleVariantClass.IsVariant("bg-[addr:icon]"), Is.False);
        }

        [Test]
        public void Given_UnknownPrefix_When_Parsed_Then_Fails()
        {
            Assert.That(StyleVariantClass.TryParse("disabled:opacity-50", out _, out _), Is.False);
        }

        [Test]
        public void Given_EmptyPayload_When_Parsed_Then_Fails()
        {
            Assert.That(StyleVariantClass.TryParse("hover:", out _, out _), Is.False);
        }

        [Test]
        public void Given_Null_When_Parsed_Then_Fails()
        {
            Assert.That(StyleVariantClass.TryParse(null, out _, out _), Is.False);
        }

        [Test]
        public void Given_MdClass_When_Parsed_Then_ResolvesMdResponsive()
        {
            var ok = StyleVariantClass.TryParse("md:flex-row", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.Md, "flex-row")));
            Assert.That(StyleVariantClass.IsResponsive(kind), Is.True);
            Assert.That(StyleVariantClass.BreakpointPx(kind), Is.EqualTo(768f));
        }

        [Test]
        public void Given_TwoXlClass_When_Parsed_Then_ResolvesXxlResponsive()
        {
            var ok = StyleVariantClass.TryParse("2xl:p-8", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.Xxl, "p-8")));
            Assert.That(StyleVariantClass.BreakpointPx(kind), Is.EqualTo(1536f));
        }

        [Test]
        public void Given_DarkClass_When_Parsed_Then_ResolvesDark()
        {
            var ok = StyleVariantClass.TryParse("dark:bg-zinc-900", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.Dark, "bg-zinc-900")));
            Assert.That(StyleVariantClass.IsResponsive(kind), Is.False);
        }

        [Test]
        public void Given_GroupHoverClass_When_Parsed_Then_ResolvesGroupHover()
        {
            var ok = StyleVariantClass.TryParse("group-hover:bg-surface", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.GroupHover, "bg-surface")));
        }

        [Test]
        public void Given_PeerFocusClass_When_Parsed_Then_ResolvesPeerFocus()
        {
            var ok = StyleVariantClass.TryParse("peer-focus:text-accent", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.PeerFocus, "text-accent")));
        }

        [Test]
        public void Given_GroupActiveArbitrary_When_Parsed_Then_KeepsArbitraryPayload()
        {
            var ok = StyleVariantClass.TryParse("group-active:translate-x-[4px]", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.GroupActive, "translate-x-[4px]")));
        }

        [Test]
        public void Given_PeerActiveClass_When_Parsed_Then_ResolvesPeerActive()
        {
            var ok = StyleVariantClass.TryParse("peer-active:scale-95", out var kind, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.PeerActive, "scale-95")));
        }

        [Test]
        public void Given_NamedGroupHover_When_Parsed_Then_ResolvesKindNameAndPayload()
        {
            var ok = StyleVariantClass.TryParse("group-hover/sidebar:bg-on", out var kind, out var name, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, name, payload), Is.EqualTo((StyleVariantKind.GroupHover, "sidebar", "bg-on")));
        }

        [Test]
        public void Given_NamedPeerChecked_When_Parsed_Then_ResolvesKindNameAndPayload()
        {
            var ok = StyleVariantClass.TryParse("peer-checked/email:text-accent", out var kind, out var name, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, name, payload), Is.EqualTo((StyleVariantKind.PeerChecked, "email", "text-accent")));
        }

        [Test]
        public void Given_UnnamedGroupHover_When_ParsedWithNameOverload_Then_NameIsNull()
        {
            var ok = StyleVariantClass.TryParse("group-hover:bg-on", out var kind, out var name, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, name, payload), Is.EqualTo((StyleVariantKind.GroupHover, (string)null, "bg-on")));
        }

        [Test]
        public void Given_NamedRelationalToken_When_CheckedWithLegacyOverload_Then_StillRecognizedAsVariant()
        {
            // The 2-arg overload (and IsVariant) must still claim a named token so it is consumed, not leaked
            // as a literal USS class — it just discards the name.
            Assert.That(StyleVariantClass.IsVariant("group-hover/sidebar:bg-on"), Is.True);
        }

        [Test]
        public void Given_EmptyName_When_Parsed_Then_Fails()
        {
            // A '/' in the prefix with no name (group-hover/:bg-on) is not a valid token.
            Assert.That(StyleVariantClass.TryParse("group-hover/:bg-on", out _, out _, out _), Is.False);
        }

        [Test]
        public void Given_NameOnNonRelationalVariant_When_Parsed_Then_Fails()
        {
            // A name is only valid on group-*/peer-; a name on hover: is rejected.
            Assert.That(StyleVariantClass.TryParse("hover/x:bg-on", out _, out _, out _), Is.False);
        }

        [Test]
        public void Given_ColorOpacityModifierPayload_When_NamedGroupParsed_Then_PrefixSlashDoesNotConsumePayloadSlash()
        {
            // The prefix '/' (named group) and the payload's own '/' (opacity modifier) are independent: only
            // the prefix slash names the group; the payload keeps its bg-black/50 verbatim.
            var ok = StyleVariantClass.TryParse("group-hover/card:bg-black/50", out var kind, out var name, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, name, payload), Is.EqualTo((StyleVariantKind.GroupHover, "card", "bg-black/50")));
        }
    }
}
