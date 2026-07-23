using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// End-to-end coverage for text-transform / text-decoration / whitespace-pre-line on a real reconcile: the
    /// effect cascades from the class-bearing element onto descendant text leaves (a TextNode has no class of
    /// its own), applies to an element's own Text prop, resets via normal-case (or, for pre-line, via a class
    /// list that no longer carries whitespace-pre-line — or via a descendant's own explicit whitespace-*
    /// class, which blocks the cascade rather than merely omitting it), and re-applies when the text changes.
    /// Reading <c>Label.text</c> after mount/patch needs no layout pass (the effect mutates the string);
    /// reading pre-line's inline <c>style.whiteSpace</c> needs no layout pass either (an inline C# write, not
    /// a resolved USS value) — except the one test below that deliberately reads <c>resolvedStyle</c>
    /// instead, to prove the inline write actually wins UI Toolkit's real cascade over the Label's own
    /// baked-in element-level <c>white-space</c> rule (an own USS rule otherwise beats an INHERITED value,
    /// which is why the write has to land on the leaf itself and not just an ancestor). GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class StyleTextEffectPanelTests : PanelTestBase
    {
        private static StateUpdater<string> s_setText;
        private static StateUpdater<string> s_setInnerText;
        private static StateUpdater<string> s_setPreLineClass;
        private static StateUpdater<string> s_setPoolReuseState;
        private static StateUpdater<string> s_setPreLineAncestorClass;
        private static StateUpdater<string> s_setUppercaseAncestorClass;
        private static StateUpdater<string> s_setPreLineText;
        private static StateUpdater<string> s_setUppercaseRefText;
        private static StateUpdater<string> s_setCascadeParentClass;
        private static StateUpdater<string> s_setLeadingAncestorClass;
        private static StateUpdater<string> s_setLeadingText;
        private static StateUpdater<string> s_setLeadingPoolReuseState;

        protected override Rect WindowSize => new Rect(0, 0, 400, 400);

        public override void SetUp()
        {
            s_setText = default;
            s_setPreLineClass = default;
            s_setPoolReuseState = default;
            s_setPreLineAncestorClass = default;
            s_setUppercaseAncestorClass = default;
            s_setPreLineText = default;
            s_setUppercaseRefText = default;
            s_setCascadeParentClass = default;
            s_setLeadingAncestorClass = default;
            s_setLeadingText = default;
            s_setLeadingPoolReuseState = default;
            base.SetUp();
        }

        private Label MountAndFindLabel(VNode tree)
        {
            _mounted = V.Mount(_window.rootVisualElement, tree);
            return _window.rootVisualElement.Q<Label>();
        }

        [Test]
        public void Given_UppercaseOnParent_When_Mounted_Then_ChildTextLeafIsUppercased()
        {
            // The text leaf has no class; the transform cascades from the parent (CSS inheritance).
            var label = MountAndFindLabel(V.Div(className: "uppercase", V.Text("hello")));

            Assert.That(label.text, Is.EqualTo("HELLO"));
        }

        [Test]
        public void Given_LowercaseOnLabelItself_When_Mounted_Then_OwnTextIsLowercased()
        {
            var label = MountAndFindLabel(V.Label(className: "lowercase", text: "HELLO"));

            Assert.That(label.text, Is.EqualTo("hello"));
        }

        [Test]
        public void Given_UnderlineOnParent_When_Mounted_Then_ChildTextWrappedInUTag()
        {
            var label = MountAndFindLabel(V.Div(className: "underline", V.Text("hi")));

            Assert.That(label.text, Is.EqualTo("<u>hi</u>"));
        }

        [Test]
        public void Given_NormalCaseUnderUppercase_When_Mounted_Then_InnerResetsToRawText()
        {
            // normal-case on a nearer ancestor overrides an outer uppercase (explicit reset stops inheritance).
            var label = MountAndFindLabel(
                V.Div(className: "uppercase", V.Div(className: "normal-case", V.Text("hi"))));

            Assert.That(label.text, Is.EqualTo("hi"));
        }

        [Test]
        public void Given_UppercaseParent_When_TextChanges_Then_NewTextIsReTransformed()
        {
            // A text change must re-cascade the transform onto the leaf, from the new raw value.
            Mount(V.Component(RenderCard));
            Assume.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("ONE"), "Precondition: initial transform");

            s_setText.Invoke("two");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("TWO"));
        }

        [Test]
        public void Given_UppercaseAncestor_When_InnerComponentReRendersInIsolation_Then_LeafStaysUppercased()
        {
            // The text leaf lives in an inner component (its own state) under an uppercase ancestor that does NOT
            // re-render when the inner state changes. The leaf must still re-apply the inherited transform on its
            // isolated re-render (RED before PatchText resolves the cascade itself: would show "two").
            Mount(V.Div(className: "uppercase", V.Component(RenderInner)));
            Assume.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("ONE"), "Precondition: initial cascade");

            s_setInnerText.Invoke("two");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("TWO"));
        }

        [Test]
        public void Given_AncestorUppercaseClassRemoved_When_DescendantTextUnchanged_Then_DescendantRevertsToRaw()
        {
            // The ancestor loses its LAST effect-bearing class on a patch (its own effect goes from
            // non-empty to empty), but the descendant TextNode leaf's text prop never changes across the
            // patch — nothing but the descendant walk re-resolves it (PatchText only re-applies when the text
            // prop itself changes), so a gate that skips that walk whenever the ancestor's OWN effect is now
            // empty would leave the leaf stuck showing the removed transform forever.
            Mount(V.Component(RenderUppercaseAncestorToggle));
            Assume.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("HI"), "Precondition: initial uppercase");

            s_setUppercaseAncestorClass.Invoke("");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("hi"));
        }

        [Test]
        public void Given_WhitespacePreLineOnLabelItself_When_Mounted_Then_OwnTextIsCollapsed()
        {
            var label = MountAndFindLabel(V.Label(className: "whitespace-pre-line", text: "a   b"));

            Assert.That(label.text, Is.EqualTo("a b"));
        }

        [Test]
        public void Given_WhitespacePreLineOnParent_When_Mounted_Then_ChildTextLeafIsCollapsed()
        {
            // The text leaf has no class; the collapse cascades from the parent, mirroring how
            // transform/decoration cascade from an ancestor onto a class-less V.Text leaf.
            var label = MountAndFindLabel(V.Div(className: "whitespace-pre-line", V.Text("a   b")));

            Assert.That(label.text, Is.EqualTo("a b"));
        }

        [Test]
        public void Given_WhitespacePreLineAncestorAndExplicitNowrapDescendant_When_Mounted_Then_DescendantTextNotCollapsedAndNoInlineWrite()
        {
            // An explicit whitespace-* class on a descendant must BLOCK a farther ancestor's pre-line from
            // reaching it, mirroring how normal-case / no-underline block Transform / Decoration — the
            // descendant's own explicit choice wins, it is not merely invisible to the cascade. Pinned on
            // both halves of what the cascade drives together off the one resolved value (the collapsed
            // string and the inline pre-wrap write): a descendant that opts itself out must show neither.
            Mount(V.Div(className: "whitespace-pre-line",
                V.Label(name: "explicit", className: "whitespace-nowrap", text: "a   b"),
                V.Label(name: "inherited", text: "c   d")));
            var explicitLabel = _window.rootVisualElement.Q<Label>("explicit");

            Assert.That((explicitLabel.text, explicitLabel.style.whiteSpace.keyword), Is.EqualTo(("a   b", StyleKeyword.Null)));
        }

        [Test]
        public void Given_WhitespacePreLineAncestorAndPlainSiblingDescendant_When_Mounted_Then_SiblingTextCollapsed()
        {
            // Inverse control for the test above, mounted from the identical tree: a sibling with no
            // whitespace class of its own still inherits and collapses normally, proving the block above is
            // specific to the explicit class rather than a general regression of the cascade.
            Mount(V.Div(className: "whitespace-pre-line",
                V.Label(name: "explicit", className: "whitespace-nowrap", text: "a   b"),
                V.Label(name: "inherited", text: "c   d")));

            Assert.That(_window.rootVisualElement.Q<Label>("inherited").text, Is.EqualTo("c d"));
        }

        [Test]
        public void Given_WhitespacePreLineOnLabelItself_When_Mounted_Then_InlineWhiteSpaceIsPreWrap()
        {
            // The collapsed text still needs to WRAP and render its (absent, here) newlines as breaks, so
            // pre-line also writes an inline white-space: pre-wrap — read directly off the inline style
            // (no layout pass needed), not resolvedStyle, mirroring how StyleFontResolver's own inline
            // writes are asserted elsewhere in this codebase.
            var label = MountAndFindLabel(V.Label(className: "whitespace-pre-line", text: "a   b"));

            Assert.That(label.style.whiteSpace.value, Is.EqualTo(WhiteSpace.PreWrap));
        }

        [Test]
        public void Given_WhitespacePreLineOnParent_When_Resolved_Then_ChildLeafGetsInlinePreWrap()
        {
            // The text leaf (a Label, like every UI Toolkit TextElement) carries its own element-level
            // white-space rule from the default theme/USS, which always beats an INHERITED value in the
            // cascade — so a write on the ancestor Div alone would never reach it. The leaf gets its OWN
            // inline write instead (ApplyToElement, driven by the resolved — cascade, not merely own — axis).
            // Reading resolvedStyle here (rather than the inline style directly, as the sibling test above
            // does) proves that write actually wins the real UI Toolkit cascade over the leaf's own default
            // rule, not merely that a C# field was set.
            var label = MountAndFindLabel(V.Div(className: "whitespace-pre-line", V.Text("a   b")));
            ForcePanelUpdate(label.panel);

            Assert.That(label.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.PreWrap));
        }

        [Test]
        public void Given_WhitespacePreLineOnParent_When_Mounted_Then_ParentContainerHasNoInlineWhiteSpace()
        {
            // Documents the contract rather than detecting a regression: ApplyToElement's write is gated on
            // `element is TextElement` (see its own guard), and a plain Div is not one — a container renders
            // no text of its own, so it structurally CANNOT receive the write no matter what the resolver's
            // internals do, making this assertion true by construction rather than a live probe of resolver
            // behavior. Kept as an explicit pin of that gate (className alone is not sufficient — only a
            // text-bearing element ever qualifies) rather than removed. V.Text has no wrapper, so the found
            // Label's direct hierarchy parent IS the class-bearing Div.
            var label = MountAndFindLabel(V.Div(className: "whitespace-pre-line", V.Text("a   b")));

            Assert.That(label.parent.style.whiteSpace.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_WhitespacePreLineAndExplicitWhitespacePreOnSameElement_When_Mounted_Then_NoInlineWhiteSpaceWritten()
        {
            // End-to-end pin for the same-element precedence rule: the explicit whitespace-pre class wins
            // over whitespace-pre-line locally (resolves to the None reset) — ResolveEffective finds this
            // element's own TextEffects entry before it would ever walk to an ancestor, so the resolved axis
            // is None, not PreLine, and the inline pre-wrap write must never fire; the element keeps whatever
            // whitespace-pre's own USS class rule resolves to instead.
            var label = MountAndFindLabel(V.Label(className: "whitespace-pre-line whitespace-pre", text: "a   b"));

            Assert.That(label.style.whiteSpace.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_UppercaseOnlyLabelWithRefWrittenWhiteSpace_When_Patched_Then_InlineWhiteSpaceSurvivesUntouched()
        {
            // No whitespace utility anywhere in this tree — only uppercase. The refCallback writes
            // style.whiteSpace directly at mount, a pattern the framework's own docs treat as legitimate
            // (ReconcilerContext.RefCallbacks). ApplyToElement still runs for this label on every mount/patch
            // pass (it is text-bearing and tracks a raw text) but must never touch a property it never
            // itself wrote to. No Assume precondition here (unlike the sibling pre-line tests): under the old
            // unconditional clear-when-not-PreLine write, Apply() runs immediately after the refCallback in
            // BOTH the create and patch call sites, so the ref's value was already stomped to
            // StyleKeyword.Null at MOUNT, before any patch — gating on it surviving mount would itself go
            // Inconclusive rather than cleanly RED. The single assert below, after both mount and a driven
            // patch, is the honest regression pin.
            Mount(V.Component(RenderUppercaseOnlyWithManualWhiteSpaceRef));

            s_setUppercaseRefText.Invoke("there");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(_window.rootVisualElement.Q<Label>().style.whiteSpace.value, Is.EqualTo(WhiteSpace.Pre));
        }

        [Test]
        public void Given_WhitespacePreLineClassRemoved_When_Patched_Then_TextRestoredToRaw()
        {
            // Pins the raw-text side-table re-derivation, not any own/previous gate: ApplyToElement re-applies
            // an element's OWN text (and, for PreLine, its own inline white-space) from the tracked raw and
            // the freshly resolved axis on every call, unconditionally — this label carries both its text AND
            // its class, so the same-element case never touches the own/previous gate that guards the
            // DESCENDANT walk (a separate concern — see ApplyToDescendants). The inline-clear sibling test
            // right below pins that SAME unconditional recompute, just for the whiteSpace half of it.
            Mount(V.Component(RenderPreLineToggle));
            Assume.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("a b"), "Precondition: initial collapse");

            s_setPreLineClass.Invoke("");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("a   b"));
        }

        [Test]
        public void Given_WhitespacePreLineClassRemoved_When_Patched_Then_InlineWhiteSpaceIsCleared()
        {
            // The inline pre-wrap write must not survive past the class that requested it, or the element
            // would keep wrapping/breaking like pre-line forever, off any USS fallback it might otherwise
            // resolve to.
            Mount(V.Component(RenderPreLineToggle));
            var label = _window.rootVisualElement.Q<Label>();
            Assume.That(label.style.whiteSpace.value, Is.EqualTo(WhiteSpace.PreWrap), "Precondition: inline pre-wrap set");

            s_setPreLineClass.Invoke("");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(label.style.whiteSpace.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_WhitespacePreLineTextPropPatchedToNull_When_Patched_Then_InlineWhiteSpaceIsCleared()
        {
            // The whitespace-pre-line class stays; only the Text prop patches to null (the label stays
            // mounted — no pool cycle). FiberNodePatcher drops the TextRawText entry for that transition,
            // which stops ApplyToElement from ever running for this element again — the leak this pins:
            // without an explicit clear at that same site, the inline pre-wrap write it left behind would
            // survive forever instead of clearing along with the text.
            Mount(V.Component(RenderPreLineTextPropToggle));
            var label = _window.rootVisualElement.Q<Label>();
            Assume.That(label.style.whiteSpace.value, Is.EqualTo(WhiteSpace.PreWrap), "Precondition: inline pre-wrap set");

            s_setPreLineText.Invoke((string)null);
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(label.style.whiteSpace.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AncestorPreLineClassRemoved_When_DescendantTextUnchanged_Then_DescendantRevertsToRaw()
        {
            // The failure mode this pins: a patch that removes the LAST effect-bearing class from an
            // ancestor (its own effect goes from non-empty to empty) must still re-walk descendants — a
            // descendant TextNode leaf whose own text prop is unchanged is never re-resolved by anything
            // else (PatchText only calls OnTextSet when the text prop itself changes), so without the walk
            // it would keep showing the stale collapsed string forever.
            Mount(V.Component(RenderPreLineAncestorToggle));
            Assume.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("a b"), "Precondition: initial collapse");

            s_setPreLineAncestorClass.Invoke("");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("a   b"));
        }

        [Test]
        public void Given_ThreeLevelCascade_When_Mounted_Then_LeafIsCollapsedAndUppercased()
        {
            // Grandparent carries whitespace-pre-line, parent carries uppercase (a DIFFERENT axis), and the
            // leaf itself carries neither — both effects must independently cascade THROUGH the
            // intermediate parent to reach the leaf, not just the direct grandparent-to-leaf hop the other
            // cascade tests in this fixture already cover at 2 levels.
            var label = MountAndFindLabel(V.Div(className: "whitespace-pre-line",
                V.Div(className: "uppercase", V.Text("a   b c"))));

            Assert.That(label.text, Is.EqualTo("A B C"));
        }

        [Test]
        public void Given_ThreeLevelCascadeParentUppercaseRemoved_When_Patched_Then_LeafStaysCollapsedButNotUppercased()
        {
            // Same 3-level shape as above; the patch removes ONLY the parent's uppercase (the grandparent's
            // whitespace-pre-line class is untouched) and the leaf's own text prop never changes across the
            // patch — nothing but the descendant re-walk (ApplyToDescendants, triggered by the parent's
            // effect going non-empty -> empty) re-resolves it. Pins that the grandparent's surviving axis
            // and the parent's just-removed one resolve independently through a 3-level chain.
            Mount(V.Component(RenderThreeLevelCascade));
            Assume.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("A B C"), "Precondition: initial collapse+uppercase");

            s_setCascadeParentClass.Invoke("");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("a b c"));
        }

        [Test]
        public void Given_LabelPooledWithPreLineState_When_RentedAgainWithoutTheClass_Then_NewLabelHasNoInlineWhiteSpace()
        {
            // Arrange — mount a pre-line Label, then hide it (a Label -> Div swap removes it, returning it to
            // VNodePool while it still carries the inline pre-wrap write), then show a different, unrelated
            // plain label. VNodePool's Label pool is LIFO and nothing else rents a Label in between, so this
            // deterministically hands the same instance back.
            Mount(V.Component(RenderPoolReuseHost));
            var scheduler = _mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_window.rootVisualElement.Q<Label>("leaf").style.whiteSpace.value, Is.EqualTo(WhiteSpace.PreWrap),
                "Precondition: the first label carries the inline pre-wrap write");
            s_setPoolReuseState.Invoke("hidden");
            scheduler.DrainImmediateForTest();
            Assume.That(_window.rootVisualElement.Q<Label>("leaf"), Is.Null, "Precondition: the label is pooled while hidden");

            // Act — an unrelated plain label rents the pooled instance back.
            s_setPoolReuseState.Invoke("plain");
            scheduler.DrainImmediateForTest();

            // Assert — the recycled instance carries no leftover inline pre-wrap AND no leftover collapsed
            // text from the previous consumer's side-table entry (a ghosted TextEffects/TextRawText row would
            // still show "c d" here, not the fixture's raw, uncollapsed "c   d").
            var recycled = _window.rootVisualElement.Q<Label>("leaf");
            Assert.That((recycled.style.whiteSpace.keyword, recycled.text), Is.EqualTo((StyleKeyword.Null, "c   d")));
        }

        [Test]
        public void Given_LeadingRelaxedOnParent_When_Mounted_Then_ChildTextLeafGetsTheLineHeightTag()
        {
            // Arrange & Act — the text leaf has no class; leading cascades from the parent, mirroring how
            // transform/decoration cascade from an ancestor onto a class-less V.Text leaf.
            var label = MountAndFindLabel(V.Div(className: "leading-relaxed", V.Text("hi")));

            // Assert
            Assert.That(label.text, Is.EqualTo("<line-height=1.625em>hi</line-height>"));
        }

        [Test]
        public void Given_NearerAncestorLeadingOverridesFartherAncestorLeading_When_Mounted_Then_ChildLeafUsesNearerValue()
        {
            // Arrange & Act — outer leading-loose (2), inner leading-tight (1.25) — the nearer ancestor's
            // own token wins, mirroring how a nearer normal-case overrides a farther uppercase.
            var label = MountAndFindLabel(
                V.Div(className: "leading-loose", V.Div(className: "leading-tight", V.Text("hi"))));

            // Assert
            Assert.That(label.text, Is.EqualTo("<line-height=1.25em>hi</line-height>"));
        }

        [Test]
        public void Given_LeafOwnLeadingUnderAncestorLeading_When_Mounted_Then_LeafOwnValueWins()
        {
            // Arrange & Act — the Label itself carries leading-none while its ancestor carries
            // leading-loose; ResolveEffective starts its walk at the leaf itself, so the leaf's own token
            // is the first (and winning) one found, exactly like Transform/Decoration's own-vs-inherited
            // precedence.
            var label = MountAndFindLabel(
                V.Div(className: "leading-loose", V.Label(className: "leading-none", text: "hi")));

            // Assert
            Assert.That(label.text, Is.EqualTo("<line-height=1em>hi</line-height>"));
        }

        [Test]
        public void Given_AncestorLeadingClassRemoved_When_DescendantTextUnchanged_Then_DescendantRevertsToRaw()
        {
            // Mirrors the same gate Transform/Whitespace pin above: a patch that removes the LAST
            // effect-bearing class from an ancestor (its own effect goes from non-empty to empty) must
            // still re-walk descendants — a leaf whose own text prop is unchanged is never re-resolved by
            // anything else (PatchText only calls OnTextSet when the text prop itself changes), so without
            // the walk it would keep showing the stale line-height tag forever.
            Mount(V.Component(RenderLeadingAncestorToggle));
            Assume.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("<line-height=1.625em>hi</line-height>"),
                "Precondition: initial leading");

            s_setLeadingAncestorClass.Invoke("");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("hi"));
        }

        [Test]
        public void Given_LeadingParent_When_TextChanges_Then_NewTextCarriesTheTag()
        {
            // A text change must re-cascade leading onto the leaf, from the new raw value, mirroring how
            // Transform re-wraps a changed leaf (Given_UppercaseParent_When_TextChanges_...).
            Mount(V.Component(RenderLeadingCard));
            Assume.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("<line-height=1.625em>hi</line-height>"),
                "Precondition: initial leading");

            s_setLeadingText.Invoke("there");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("<line-height=1.625em>there</line-height>"));
        }

        [Test]
        public void Given_LabelPooledWithLeadingState_When_RentedAgainWithoutTheClass_Then_NewLabelHasNoLineHeightTag()
        {
            // Arrange — mount a leading-loose Label, then hide it (a Label -> Div swap removes it,
            // returning it to VNodePool while its text still carries the <line-height> tag), then show a
            // different, unrelated plain label. VNodePool's Label pool is LIFO and nothing else rents a
            // Label in between, so this deterministically hands the same instance back, mirroring the
            // PreLine pool-reuse mechanism.
            Mount(V.Component(RenderLeadingPoolReuseHost));
            var scheduler = _mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_window.rootVisualElement.Q<Label>("leaf").text, Is.EqualTo("<line-height=2em>hi</line-height>"),
                "Precondition: the first label carries the line-height tag");
            s_setLeadingPoolReuseState.Invoke("hidden");
            scheduler.DrainImmediateForTest();
            Assume.That(_window.rootVisualElement.Q<Label>("leaf"), Is.Null, "Precondition: the label is pooled while hidden");

            // Act — an unrelated plain label rents the pooled instance back.
            s_setLeadingPoolReuseState.Invoke("plain");
            scheduler.DrainImmediateForTest();

            // Assert — the recycled instance carries no leftover line-height tag from the previous
            // consumer's side-table entry (a ghosted TextEffects/TextRawText row would still show a
            // wrapped tag here, not the fixture's raw "there").
            Assert.That(_window.rootVisualElement.Q<Label>("leaf").text, Is.EqualTo("there"));
        }

        private void Mount(VNode tree)
        {
            _mounted = V.Mount(_window.rootVisualElement, tree);
        }

        [Component]
        private static VNode RenderCard()
        {
            var (text, setText) = Hooks.UseState("one");
            s_setText = setText;
            return V.Div(className: "uppercase", V.Text(text));
        }

        [Component]
        private static VNode RenderInner()
        {
            var (text, setText) = Hooks.UseState("one");
            s_setInnerText = setText;
            return V.Text(text);
        }

        [Component]
        private static VNode RenderPreLineToggle()
        {
            var (cls, setCls) = Hooks.UseState("whitespace-pre-line");
            s_setPreLineClass = setCls;
            return V.Label(className: cls, text: "a   b");
        }

        [Component]
        private static VNode RenderPreLineAncestorToggle()
        {
            // Unlike RenderPreLineToggle, the class-bearing element and the text leaf are DIFFERENT elements
            // (a Div ancestor, a separate V.Text child) — the shape the ApplyToDescendants gate guards, as
            // opposed to the same-element ApplyToElement path RenderPreLineToggle exercises.
            var (cls, setCls) = Hooks.UseState("whitespace-pre-line");
            s_setPreLineAncestorClass = setCls;
            return V.Div(className: cls, V.Text("a   b"));
        }

        [Component]
        private static VNode RenderUppercaseAncestorToggle()
        {
            var (cls, setCls) = Hooks.UseState("uppercase");
            s_setUppercaseAncestorClass = setCls;
            return V.Div(className: cls, V.Text("hi"));
        }

        [Component]
        private static VNode RenderPreLineTextPropToggle()
        {
            // Unlike RenderPreLineToggle (which toggles the CLASS), this toggles the Text PROP itself while
            // whitespace-pre-line stays on the class list throughout — exercising the Text->null transition
            // on a still-mounted element, the one path where the owned inline write must be cleared without
            // an unmount or pool cycle ever running.
            var (text, setText) = Hooks.UseState("a   b");
            s_setPreLineText = setText;
            return V.Label(className: "whitespace-pre-line", text: text);
        }

        // Stable delegate identity (a static readonly field, not a per-render closure) so InvokeRefCallback's
        // same-identity skip means this fires exactly once, at mount — the driven patch below then exercises
        // ONLY the text-effect pass's own restraint, not a re-firing ref re-asserting the value each time.
        private static readonly Func<VisualElement, Action> s_manualWhiteSpaceRef = element =>
        {
            element.style.whiteSpace = WhiteSpace.Pre;
            return null;
        };

        [Component]
        private static VNode RenderUppercaseOnlyWithManualWhiteSpaceRef()
        {
            var (text, setText) = Hooks.UseState("hi");
            s_setUppercaseRefText = setText;
            return V.Label(className: "uppercase", text: text, refCallback: s_manualWhiteSpaceRef);
        }

        [Component]
        private static VNode RenderThreeLevelCascade()
        {
            var (parentCls, setParentCls) = Hooks.UseState("uppercase");
            s_setCascadeParentClass = setParentCls;
            return V.Div(className: "whitespace-pre-line",
                V.Div(className: parentCls, V.Text("a   b c")));
        }

        [Component]
        private static VNode RenderPoolReuseHost()
        {
            var (state, setState) = Hooks.UseState("pre-line");
            s_setPoolReuseState = setState;
            if (state == "pre-line")
            {
                return V.Label(name: "leaf", className: "whitespace-pre-line", text: "a   b");
            }
            if (state == "plain")
            {
                return V.Label(name: "leaf", text: "c   d");
            }
            return V.Div(name: "placeholder");
        }

        [Component]
        private static VNode RenderLeadingAncestorToggle()
        {
            // Unlike a same-element toggle, the class-bearing element and the text leaf are DIFFERENT
            // elements (a Div ancestor, a separate V.Text child) — the shape ApplyToDescendants guards,
            // mirroring RenderPreLineAncestorToggle / RenderUppercaseAncestorToggle above.
            var (cls, setCls) = Hooks.UseState("leading-relaxed");
            s_setLeadingAncestorClass = setCls;
            return V.Div(className: cls, V.Text("hi"));
        }

        [Component]
        private static VNode RenderLeadingCard()
        {
            var (text, setText) = Hooks.UseState("hi");
            s_setLeadingText = setText;
            return V.Div(className: "leading-relaxed", V.Text(text));
        }

        [Component]
        private static VNode RenderLeadingPoolReuseHost()
        {
            var (state, setState) = Hooks.UseState("leading");
            s_setLeadingPoolReuseState = setState;
            if (state == "leading")
            {
                return V.Label(name: "leaf", className: "leading-loose", text: "hi");
            }
            if (state == "plain")
            {
                return V.Label(name: "leaf", text: "there");
            }
            return V.Div(name: "placeholder");
        }
    }
}
