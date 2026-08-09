using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for pooled-element state ghosting: a primitive returned to <see cref="VNodePool"/>
    /// must be reset so it cannot carry state its previous node set onto its next consumer. The inline-style
    /// cases pin <c>FiberElementPoolReset.ResetInlineStyle</c>, which must null EVERY inline property Velvet
    /// may set, including <c>letterSpacing</c> (written by the <c>tracking-[Npx]</c> arbitrary) — omitting it
    /// would let a recycled label keep its previous letter spacing and ghost it onto the next consumer whose
    /// node declares no <c>tracking-*</c> (the fresh node's empty oldClasses diff never clears it). The widget
    /// cases pin the per-widget reset helpers (<see cref="FiberTogglePoolHelper"/>,
    /// <see cref="FiberSliderPoolHelper"/>, <see cref="FiberTextFieldPoolHelper"/>) end-to-end through the
    /// reconciler: a Toggle / Slider / TextField rendered with non-default state, removed, and then recreated
    /// as a PLAIN widget rents the same pooled instance back, and the reset must scrub every widget-specific
    /// field or the plain node — which declares no value/label/range and so never overwrites them — observes
    /// the previous consumer's state (TextField additionally carries a PII security contract: a recycled
    /// field must not surface a prior password / value). All three are the same pooled-reuse ghosting class
    /// as the Button-children bug. GWT, one assert per case.
    /// The structural cases pin the FULL reset surface: every inline style property the reset scrubs is compared
    /// against a freshly constructed element, in both directions, so dropping a scrubbed property from the helper
    /// (reintroducing a ghost) or scrubbing a new one without extending the pinned contract list fails a test.
    /// One case additionally resets an element that is still ATTACHED to a panel: clearing an inline filter goes
    /// through the transition system, so the scrub must not turn itself into a running animation that keeps
    /// painting what it just removed. Today's callers detach before resetting, which suppresses that path on its
    /// own; the case pins the helper independently of that ordering.
    /// </summary>
    [TestFixture]
    internal sealed class PooledElementStyleGhostTests : VariantCleanupTestsBase
    {
        // Unit: the reset helper

        [Test]
        public void Given_ALabelWithInlineLetterSpacing_When_ResetForReuse_Then_LetterSpacingIsCleared()
        {
            // Arrange — a label carrying an inline letterSpacing, as tracking-[Npx] leaves on a removed label.
            var label = new Label();
            label.style.letterSpacing = 4f;
            Assume.That(label.style.letterSpacing.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: the label starts with an inline letterSpacing");

            // Act — it is reset for reuse.
            FiberLabelPoolHelper.ResetLabelForReuse(label);

            // Assert — the letterSpacing is cleared, so the recycled label cannot ghost it onto the next consumer.
            Assert.AreEqual(StyleKeyword.Null, label.style.letterSpacing.keyword);
        }

        [Test]
        public void Given_AnAttachedElementWithAFilterCoveringTransition_When_ResetForReuse_Then_NothingKeepsPaintingTheFilter()
        {
            // Arrange — an element on a real panel carrying a settled inline filter under a live whole-property
            // transition (what transition-all / a bare duration-* resolves to). Clearing an inline filter goes
            // through the transition system, so the clear can become a running animation that keeps painting
            // the filter the reset just removed; the reset must hold regardless of whether its caller detached
            // the element first. The fake clock makes the painted mid-animation value load-independent.
            using var host = new HeadlessEditorPanelHost();
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(host.Panel, () => now);
            var element = new VisualElement();
            element.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0.3f) });
            host.Root.Add(element);
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);
            var blur = new FilterFunction(FilterFunctionType.Blur);
            blur.AddParameter(new FilterParameter(12f));
            element.style.filter = new List<FilterFunction> { blur };
            now += 1.0;
            EditorPanelTestHelpers.DriveAnimationsOnce(host.Panel);
            Assume.That(element.resolvedStyle.filter, Is.Not.Empty,
                "Precondition: the element paints its inline filter before the reset");

            // Act — the shared pool reset runs, then the panel paints a frame.
            FiberElementPoolReset.ResetCommonState(element);
            now += 0.15;
            EditorPanelTestHelpers.DriveAnimationsOnce(host.Panel);

            // Assert — nothing is painting a filter any more.
            Assert.That(element.resolvedStyle.filter, Is.Empty);
        }

        [Test]
        public void Given_AnAttachedElementWithATransitionedOpacity_When_ResetForReuse_Then_TheOpacityIsAlreadyBack()
        {
            // Arrange — filter is not special: EVERY inline clear in the scrub restores its matched-rules value
            // through the transition system, so the neutralising write has to land before the whole scrub, not
            // merely before the filter clear. opacity is the probe because it is cleared early in the scrub, so
            // a write placed later would leave this one animating.
            using var host = new HeadlessEditorPanelHost();
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(host.Panel, () => now);
            var element = new VisualElement();
            element.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0.3f) });
            host.Root.Add(element);
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);
            element.style.opacity = 0.2f;
            now += 1.0;
            EditorPanelTestHelpers.DriveAnimationsOnce(host.Panel);
            Assume.That(element.resolvedStyle.opacity, Is.EqualTo(0.2f).Within(1e-3f),
                "Precondition: the element paints its inline opacity before the reset");

            // Act
            FiberElementPoolReset.ResetCommonState(element);
            now += 0.15;
            EditorPanelTestHelpers.DriveAnimationsOnce(host.Panel);

            // Assert — fully back to the default, not part-way through fading back to it.
            Assert.That(element.resolvedStyle.opacity, Is.EqualTo(1f).Within(1e-3f));
        }

        // Integration: reconcile remove → recreate a plain label from the pool

        // Mode 0 renders a label styled with tracking-[4px] (inline letterSpacing); mode 1 renders nothing
        // (the styled label is removed and returned to the pool); mode 2 renders a PLAIN label that declares no
        // tracking, so it rents the pooled tracking-styled label back.
        [Component]
        private static VNode Host()
        {
            var mode = Hooks.UseStore(s_store, s => s.Mode);
            VNode child = mode == 0
                ? V.Label(name: "text", className: "tracking-[4px]", text: "hi")
                : mode == 2
                    ? V.Label(name: "text", text: "hi")
                    : (VNode)V.Fragment(Array.Empty<VNode>());
            return V.Div(name: "host", children: new VNode[] { child });
        }

        [Test]
        public void Given_ATrackingStyledLabelWasRemoved_When_APlainLabelIsRecreatedFromThePool_Then_ItHasNoStaleLetterSpacing()
        {
            // Arrange — a tracking-[4px] label mounted (inline letterSpacing set), then removed and pooled.
            using var store = new ModeStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Act — a plain label (no tracking) is rendered, renting the pooled label back.
            store.Set(2);
            scheduler.DrainImmediateForTest();

            // Assert — the recycled label carries no leftover letter spacing.
            Assert.AreEqual(StyleKeyword.Null, _root.Q<Label>("text").style.letterSpacing.keyword);
        }

        // Integration: the most common arbitrary value (w-[Npx]) across the same recycle

        // Mode 0 renders a label sized with w-[200px] (inline width); mode 1 renders nothing (the sized label is
        // removed and pooled); mode 2 renders a PLAIN label that declares no width, renting the pooled label back.
        [Component]
        private static VNode WidthHost()
        {
            var mode = Hooks.UseStore(s_store, s => s.Mode);
            VNode child = mode == 0
                ? V.Label(name: "text", className: "w-[200px]", text: "hi")
                : mode == 2
                    ? V.Label(name: "text", text: "hi")
                    : (VNode)V.Fragment(Array.Empty<VNode>());
            return V.Div(name: "host", children: new VNode[] { child });
        }

        [Test]
        public void Given_AWidthSizedLabelWasRemoved_When_APlainLabelIsRecreatedFromThePool_Then_ItHasNoStaleWidth()
        {
            // Arrange — a w-[200px] label mounted (inline width set), then removed and pooled.
            using var store = new ModeStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(WidthHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Act — a plain label (no width) is rendered, renting the pooled label back.
            store.Set(2);
            scheduler.DrainImmediateForTest();

            // Assert — the recycled label carries no leftover inline width (arbitrary values do not ghost across reuse).
            Assert.AreEqual(StyleKeyword.Null, _root.Q<Label>("text").style.width.keyword);
        }

        // Structural: the full inline-style scrub surface, compared against a fresh element

        // Every inline style property FiberElementPoolReset.ResetInlineStyle scrubs. This list is the
        // pinned reset contract: the structural test below fails when the helper stops scrubbing a
        // listed property (a pool-reuse ghost) AND when it starts scrubbing an unlisted one (the new
        // property would otherwise have no regression coverage), so the two cannot drift silently.
        private static readonly string[] ScrubbedInlineStyleProperties =
        {
            nameof(IStyle.color),
            nameof(IStyle.backgroundColor),
            nameof(IStyle.backgroundImage),
            nameof(IStyle.backgroundSize),
            nameof(IStyle.backgroundPositionX),
            nameof(IStyle.backgroundPositionY),
            nameof(IStyle.backgroundRepeat),
            nameof(IStyle.opacity),
            nameof(IStyle.display),
            nameof(IStyle.visibility),
            nameof(IStyle.overflow),
            nameof(IStyle.width),
            nameof(IStyle.height),
            nameof(IStyle.minWidth),
            nameof(IStyle.minHeight),
            nameof(IStyle.maxWidth),
            nameof(IStyle.maxHeight),
            nameof(IStyle.marginLeft),
            nameof(IStyle.marginRight),
            nameof(IStyle.marginTop),
            nameof(IStyle.marginBottom),
            nameof(IStyle.paddingLeft),
            nameof(IStyle.paddingRight),
            nameof(IStyle.paddingTop),
            nameof(IStyle.paddingBottom),
            nameof(IStyle.borderLeftWidth),
            nameof(IStyle.borderRightWidth),
            nameof(IStyle.borderTopWidth),
            nameof(IStyle.borderBottomWidth),
            nameof(IStyle.borderLeftColor),
            nameof(IStyle.borderRightColor),
            nameof(IStyle.borderTopColor),
            nameof(IStyle.borderBottomColor),
            nameof(IStyle.borderTopLeftRadius),
            nameof(IStyle.borderTopRightRadius),
            nameof(IStyle.borderBottomLeftRadius),
            nameof(IStyle.borderBottomRightRadius),
            nameof(IStyle.flexGrow),
            nameof(IStyle.flexShrink),
            nameof(IStyle.flexBasis),
            nameof(IStyle.flexDirection),
            nameof(IStyle.flexWrap),
            nameof(IStyle.alignSelf),
            nameof(IStyle.alignItems),
            nameof(IStyle.alignContent),
            nameof(IStyle.justifyContent),
            nameof(IStyle.position),
            nameof(IStyle.left),
            nameof(IStyle.right),
            nameof(IStyle.top),
            nameof(IStyle.bottom),
            nameof(IStyle.fontSize),
            nameof(IStyle.letterSpacing),
            nameof(IStyle.unityFontDefinition),
            nameof(IStyle.unityFontStyleAndWeight),
            nameof(IStyle.unityTextAlign),
            nameof(IStyle.whiteSpace),
            nameof(IStyle.translate),
            nameof(IStyle.rotate),
            nameof(IStyle.scale),
            nameof(IStyle.aspectRatio),
            nameof(IStyle.filter),
            nameof(IStyle.transformOrigin),
            nameof(IStyle.transitionDuration),
            nameof(IStyle.transitionDelay),
            nameof(IStyle.transitionProperty),
            nameof(IStyle.transitionTimingFunction),
        };

        [Test]
        public void Given_EveryInlineStylePropertyWritten_When_ResetForReuse_Then_ScrubbedPropertiesMatchTheContractList()
        {
            // Arrange — write EVERY settable IStyle property as a real inline entry. StyleKeyword.Initial is
            // the sentinel because it is distinguishable from the unset state (which reads back as
            // StyleKeyword.Null), so "reads equal to a fresh element" after the reset means exactly "scrubbed".
            // Obsolete properties are excluded: they are synthetic shims whose getters derive from the real
            // background properties instead of owning storage, so they would always read equal to fresh.
            var element = new VisualElement();
            var fresh = new VisualElement();
            // unityMaterial is also excluded: its inline getter returns StyleKeyword.Null whenever the
            // stored material object is null, so a keyword-only sentinel cannot round-trip and the
            // property is indistinguishable from fresh without a live Material. Velvet's styling layer
            // never writes it inline.
            var properties = typeof(IStyle).GetProperties()
                .Where(p => p.CanWrite && !p.IsDefined(typeof(ObsoleteAttribute))
                    && p.Name != nameof(IStyle.unityMaterial))
                .ToArray();
            var unsettable = new List<string>();
            foreach (var property in properties)
            {
                var sentinel = MakeStyleValue(property.PropertyType, StyleKeyword.Initial);
                if (sentinel == null)
                {
                    unsettable.Add(property.Name);
                    continue;
                }
                property.SetValue(element.style, sentinel);
            }
            Assume.That(unsettable, Is.Empty, "Precondition: every IStyle property type accepts a StyleKeyword value");

            // Act — the shared pool reset runs.
            FiberElementPoolReset.ResetCommonState(element);

            // Assert — the set of properties that read back indistinguishable from a fresh element equals the
            // pinned contract list, in both directions.
            // filter cannot read back equal to fresh on this editor: its setter clears the wrong internal
            // has-inline flag on a Null assignment, so the reset instead empties the surviving list in
            // place. An empty inline filter list computes to "no filter", so it counts as scrubbed.
            var observedScrubbed = properties
                .Where(p => Equals(p.GetValue(element.style), p.GetValue(fresh.style))
                    || (p.Name == nameof(IStyle.filter)
                        && element.style.filter.value is { Count: 0 }))
                .Select(p => p.Name)
                .ToArray();
            var mismatches = ScrubbedInlineStyleProperties.Except(observedScrubbed)
                .Select(name => $"not scrubbed by the reset (pool-reuse ghost): {name}")
                .Concat(observedScrubbed.Except(ScrubbedInlineStyleProperties)
                    .Select(name => $"scrubbed by the reset but missing from the contract list: {name}"))
                .ToList();
            Assert.That(mismatches, Is.Empty);
        }

        // Structural: a plain VisualElement through ResetCommonState. Every field probed below is also a
        // compared slot in PooledElementSurfaceResetTests, which walks widgets; what only this one holds is
        // that they are reset by the SHARED method, so moving one into all five widget helpers fails here
        // and nowhere else.

        private static readonly (string Name, Func<VisualElement, object> Read)[] CommonStateProbes =
        {
            ("userData", e => e.userData),
            ("name", e => e.name),
            ("tooltip", e => e.tooltip),
            ("focusable", e => e.focusable),
            ("pickingMode", e => e.pickingMode),
            ("viewDataKey", e => e.viewDataKey),
            ("enabledSelf", e => e.enabledSelf),
        };

        [Test]
        public void Given_EveryCommonStateFieldMutated_When_ResetForReuse_Then_ElementMatchesAFreshInstance()
        {
            // Arrange — mutate the sampled fields.
            var element = new VisualElement
            {
                userData = new object(),
                name = "stale-name",
                tooltip = "stale-tooltip",
                focusable = true,
                pickingMode = PickingMode.Ignore,
                viewDataKey = "stale-view-data",
            };
            element.SetEnabled(false);
            var fresh = new VisualElement();

            // Act
            FiberElementPoolReset.ResetCommonState(element);

            // Assert — every probed field reads identically to a freshly constructed element.
            var mismatches = CommonStateProbes
                .Where(probe => !Equals(probe.Read(element), probe.Read(fresh)))
                .Select(probe => probe.Name)
                .ToList();
            Assert.That(mismatches, Is.Empty);
        }

        // Resolves a boxed style value carrying the given keyword for any IStyle property type. Every
        // Unity style struct exposes either a StyleKeyword constructor or an implicit conversion from
        // StyleKeyword; returns null when a (future) type exposes neither, surfacing it as unsettable.
        //
        // List-typed properties (StyleList<T>: filter, transition*) are the one exception: their
        // StyleKeyword-only constructor stores a null backing List<T>. Unity's inline-style write path
        // only reads that list when it is non-null (it clears/appends into it in place); when it is
        // null it stores null on the underlying StyleValueManaged and defers to a copy-from-initial-style
        // step that dereferences a per-element destination list which is not guaranteed to be allocated
        // yet on a fresh, panel-less element — a NullReferenceException. Building a real (non-null)
        // List<T> instead keeps the value in the safe, direct-store path while still reading back as
        // "set" (distinguishable from a fresh element), so it still exercises the same reset contract.
        private static object MakeStyleValue(Type styleType, StyleKeyword keyword)
        {
            if (styleType.IsGenericType && styleType.GetGenericTypeDefinition() == typeof(StyleList<>))
            {
                var elementType = styleType.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(elementType);
                var list = Activator.CreateInstance(listType);
                listType.GetMethod("Add")!.Invoke(list, new[] { Activator.CreateInstance(elementType) });
                var listCtor = styleType.GetConstructor(new[] { listType });
                return listCtor?.Invoke(new[] { list });
            }

            var ctor = styleType.GetConstructor(new[] { typeof(StyleKeyword) });
            if (ctor != null)
            {
                return ctor.Invoke(new object[] { keyword });
            }

            var implicitOperator = styleType.GetMethod(
                "op_Implicit", BindingFlags.Public | BindingFlags.Static, binder: null,
                new[] { typeof(StyleKeyword) }, modifiers: null);
            return implicitOperator?.Invoke(null, new object[] { keyword });
        }

        // Integration: pooled-widget state ghosting across a real reconcile recycle

        // Mode 0 renders the configured widget; mode 1 renders nothing (the widget is removed and pooled, running
        // the reset helper); mode 2 renders a PLAIN widget that declares no state, renting the pooled one back.
        // Named distinctly from Host/WidthHost above (a topical collision on merge): those render a fixed Label
        // shape, this one renders whatever s_render supplies for the widget under test.
        [Component]
        private static VNode WidgetHost()
        {
            var mode = Hooks.UseStore(s_store, s => s.Mode);
            return V.Div(name: "host", children: new VNode[] { s_render(mode) });
        }

        // Drives a configured → removed → plain recycle. Whether the configured widget actually went away is
        // handed back rather than assumed: each caller's assertion reads default state, which a widget that
        // was never removed and never reset can also carry, so the two only pin the reset together.
        private (bool RemovedWhileHidden, T Recycled) Recycle<T>(Func<int, VNode> render, string name)
            where T : VisualElement
        {
            s_render = render;
            using var store = new ModeStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(WidgetHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set(1);
            scheduler.DrainImmediateForTest();
            var removedWhileHidden = _root.Q<T>(name) == null;
            store.Set(2);
            scheduler.DrainImmediateForTest();
            return (removedWhileHidden, _root.Q<T>(name));
        }

        [Test]
        public void Given_AnElementWithName_When_ReconciledInPlaceToNoName_Then_TheNameIsCleared()
        {
            // In-place reuse (same position, no pool round-trip): a named element reconciled to an unnamed one of
            // the same type reuses the element. The name must clear on removal (parity with every other attribute);
            // otherwise a later Q("panel") mis-hits the stale identifier left on the reused element.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), new VNode[] { V.Div(name: "panel") });
            Assume.That(scope.Root.Q("panel"), Is.Not.Null, "Precondition: the named element is found");

            scope.Reconciler.Reconcile(scope.Root, new VNode[] { V.Div(name: "panel") }, new VNode[] { V.Div() });

            // Assert
            Assert.That(scope.Root.Q("panel"), Is.Null,
                "Removing the name prop clears it on in-place reuse (no stale identifier left on the reused element)");
        }

        // Toggle

        [Test]
        public void Given_ACheckedToggleWasRemoved_When_APlainToggleIsRecreatedFromThePool_Then_ItIsUnchecked()
        {
            // Arrange/Act — a checked toggle is pooled and a plain toggle (no value) is rented back.
            var (removedWhileHidden, toggle) = Recycle<Toggle>(
                mode => mode == 0 ? V.Toggle(name: "t", value: true)
                      : mode == 2 ? V.Toggle(name: "t")
                      : V.Fragment(Array.Empty<VNode>()),
                "t");

            // Assert — the recycled toggle does not ghost the prior checked state.
            Assert.That((removedWhileHidden, toggle.value), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ALabelledToggleWasRemoved_When_APlainToggleIsRecreatedFromThePool_Then_ItHasNoLabel()
        {
            // Arrange/Act — a labelled toggle is pooled and a plain toggle (no label) is rented back.
            var (removedWhileHidden, toggle) = Recycle<Toggle>(
                mode => mode == 0 ? V.Toggle(name: "t", label: "Stale")
                      : mode == 2 ? V.Toggle(name: "t")
                      : V.Fragment(Array.Empty<VNode>()),
                "t");

            // Assert — the recycled toggle carries no leftover label.
            Assert.That((removedWhileHidden, toggle.label), Is.EqualTo((true, string.Empty)));
        }

        // TextField (security-critical)

        [Test]
        public void Given_ATextFieldHeldPii_When_APlainTextFieldIsRecreatedFromThePool_Then_ItHasNoStaleValue()
        {
            // Arrange/Act — a field holding PII is pooled and a plain field (no value) is rented back.
            var (removedWhileHidden, field) = Recycle<TextField>(
                mode => mode == 0 ? V.TextField(name: "tf", value: "secret@example.com")
                      : mode == 2 ? V.TextField(name: "tf")
                      : V.Fragment(Array.Empty<VNode>()),
                "tf");

            // Assert — the recycled field surfaces no prior value (PII must not ghost across pool reuse).
            Assert.That((removedWhileHidden, field.value), Is.EqualTo((true, string.Empty)));
        }

        [Test]
        public void Given_APasswordTextFieldWasRemoved_When_APlainTextFieldIsRecreatedFromThePool_Then_ItIsNotMasked()
        {
            // Arrange/Act — a password (masked) field is pooled and a plain field is rented back.
            var (removedWhileHidden, field) = Recycle<TextField>(
                mode => mode == 0 ? V.TextField(name: "tf", value: "pw", isPasswordField: true)
                      : mode == 2 ? V.TextField(name: "tf")
                      : V.Fragment(Array.Empty<VNode>()),
                "tf");

            // Assert — the recycled field is not still masking input from the previous consumer.
            Assert.That((removedWhileHidden, field.textEdition.isPassword), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ALabelledTextFieldWasRemoved_When_APlainTextFieldIsRecreatedFromThePool_Then_ItHasNoLabel()
        {
            // Arrange/Act — a labelled field is pooled and a plain field (no label) is rented back.
            var (removedWhileHidden, field) = Recycle<TextField>(
                mode => mode == 0 ? V.TextField(name: "tf", label: "Email")
                      : mode == 2 ? V.TextField(name: "tf")
                      : V.Fragment(Array.Empty<VNode>()),
                "tf");

            // Assert — the recycled field carries no leftover label.
            Assert.That((removedWhileHidden, field.label), Is.EqualTo((true, string.Empty)));
        }

        // Slider

        [Test]
        public void Given_ASliderWithCustomRangeWasRemoved_When_APlainSliderIsRecreatedFromThePool_Then_ItHasTheDefaultHighValue()
        {
            // Arrange/Act — a slider with a widened range is pooled and a plain slider (default range) is rented back.
            var (removedWhileHidden, slider) = Recycle<Slider>(
                mode => mode == 0 ? V.Slider(name: "s", value: 80f, lowValue: 0f, highValue: 100f)
                      : mode == 2 ? V.Slider(name: "s")
                      : V.Fragment(Array.Empty<VNode>()),
                "s");

            // Assert — the recycled slider is restored to Unity's default highValue (10), not the prior 100.
            Assert.That((removedWhileHidden, slider.highValue), Is.EqualTo((true, 10f)));
        }

        [Test]
        public void Given_ASliderWithAValueWasRemoved_When_APlainSliderIsRecreatedFromThePool_Then_ItHasNoStaleValue()
        {
            // Arrange/Act — a slider carrying a value is pooled and a plain slider (no value) is rented back.
            var (removedWhileHidden, slider) = Recycle<Slider>(
                mode => mode == 0 ? V.Slider(name: "s", value: 7f, lowValue: 0f, highValue: 10f)
                      : mode == 2 ? V.Slider(name: "s")
                      : V.Fragment(Array.Empty<VNode>()),
                "s");

            // Assert — the recycled slider does not ghost the prior value.
            Assert.That((removedWhileHidden, slider.value), Is.EqualTo((true, 0f)));
        }
    }
}
