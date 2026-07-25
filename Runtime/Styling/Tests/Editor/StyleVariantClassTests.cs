using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies parsing for the whole family of variant-prefix tokens (<c>&lt;variant&gt;:&lt;payload&gt;</c>):
    /// <list type="bullet">
    /// <item><see cref="StyleVariantClass"/> — the state variants (<c>hover:</c> / <c>focus:</c> /
    /// <c>active:</c> / …, plus named <c>group-*/peer-*</c> relational forms); a ':' that occurs inside
    /// <c>[...]</c> (e.g. <c>bg-[addr:key]</c>) is not a variant separator.</item>
    /// <item><see cref="StyleAttributeVariantClass"/> — the <c>data-[...]</c> / <c>aria-[...]</c> forms: the
    /// bare-key presence test and the <c>key=value</c> equality test, whose bracketed key/value carries no
    /// <c>:</c>.</item>
    /// <item><see cref="StyleHasVariantClass"/> — the <c>has-[...]</c> inner forms
    /// (<c>has-[:checked]:</c>, <c>has-[:focus]:</c>, <c>has-[.class]:</c>) whose bracketed selector carries
    /// its own <c>:</c> / <c>.</c>.</item>
    /// <item><see cref="StyleStructuralVariantClass"/> — the named structural variants (<c>first:</c>…
    /// <c>even:</c>) and the arbitrary selector forms (<c>[&amp;:nth-child(N)]:</c>,
    /// <c>[&amp;:first-child]:</c>, <c>[&amp;:nth-last-child(N)]:</c>) whose bracketed selector carries its own
    /// <c>:</c>/<c>(</c>.</item>
    /// <item><see cref="StyleSupportsVariantClass"/> — the <c>supports-[&lt;property&gt;:&lt;value&gt;]:</c>
    /// feature-query form, whose bracketed declaration carries the property/value <c>:</c> internally; this
    /// variant is STATIC in UI Toolkit (well-formed ⇒ always-applied), so the parser only validates
    /// well-formedness, with behavior asserted in the reconciler fixture.</item>
    /// </list>
    /// Every malformed / unrecognized / cross-namespace case (an unknown prefix, an empty payload, an empty
    /// bracket, a sibling namespace's token) must fail to parse rather than being silently claimed. GWT, one
    /// assert per case.
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

        [Test]
        public void Given_FirstNamed_When_Parsed_Then_ResolvesFirst()
        {
            var ok = StyleStructuralVariantClass.TryParse("first:bg-mark", out var kind, out _, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleStructuralKind.First, "bg-mark")));
        }

        [Test]
        public void Given_ArbitraryNthChild_When_Parsed_Then_ResolvesNthChildWithN()
        {
            var ok = StyleStructuralVariantClass.TryParse("[&:nth-child(3)]:bg-mark", out var kind, out var n, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, n, payload), Is.EqualTo((StyleStructuralKind.NthChild, 3, "bg-mark")));
        }

        [Test]
        public void Given_ArbitraryFirstChildAlias_When_Parsed_Then_ResolvesFirst()
        {
            var ok = StyleStructuralVariantClass.TryParse("[&:first-child]:bg-mark", out var kind, out _, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleStructuralKind.First, "bg-mark")));
        }

        [Test]
        public void Given_ArbitraryNthLastChild_When_Parsed_Then_ResolvesNthLastChildWithN()
        {
            var ok = StyleStructuralVariantClass.TryParse("[&:nth-last-child(2)]:bg-mark", out var kind, out var n, out _);

            Assume.That(ok, Is.True);
            Assert.That((kind, n), Is.EqualTo((StyleStructuralKind.NthLastChild, 2)));
        }

        [Test]
        public void Given_StateVariant_When_Parsed_Then_IsNotStructural()
        {
            Assert.That(StyleStructuralVariantClass.IsStructural("hover:bg-mark"), Is.False);
        }

        [Test]
        public void Given_NthLastChild2_When_EvaluatedAgainstFourSiblings_Then_MatchesThirdIndex()
        {
            // nth-last-child(2) of 4 == index 2 (the 2nd from the end).
            var matches = StyleStructuralVariantClass.Matches(StyleStructuralKind.NthLastChild, 2, index: 2, count: 4);

            Assert.That(matches, Is.True);
        }

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
