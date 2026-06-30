using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for pooled-widget state ghosting across a real reconcile recycle. A Toggle / Slider /
    /// TextField rendered with non-default state, removed (returned to <see cref="VNodePool"/>), and then
    /// recreated as a PLAIN widget rents the same pooled instance back. The pool-return reset helpers
    /// (<see cref="FiberTogglePoolHelper"/>, <see cref="FiberSliderPoolHelper"/>, <see cref="FiberTextFieldPoolHelper"/>)
    /// must scrub every widget-specific field, otherwise the plain node — which declares no value/label/range and so
    /// never overwrites them — observes the previous consumer's state. This is the same pooled-reuse ghosting class
    /// as the Button-children and tracking-letterSpacing bugs, exercised end-to-end through the reconciler rather
    /// than against the helper in isolation. TextField additionally carries a PII security contract (a recycled
    /// field must not surface a prior password / value). GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class PooledWidgetResetReuseTests : VariantCleanupTestsBase
    {
        // Mode 0 renders the configured widget; mode 1 renders nothing (the widget is removed and pooled, running
        // the reset helper); mode 2 renders a PLAIN widget that declares no state, renting the pooled one back.
        [Component]
        private static VNode Host()
        {
            var mode = Hooks.UseStore(s_store, s => s.Mode);
            return V.Div(name: "host", children: new VNode[] { s_render(mode) });
        }

        // Drives a configured → removed → plain recycle and returns the recycled element by name.
        private T Recycle<T>(Func<int, VNode> render, string name) where T : VisualElement
        {
            s_render = render;
            using var store = new ModeStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set(1);
            scheduler.DrainImmediateForTest();
            Assume.That(_root.Q<T>(name), Is.Null, "Precondition: the configured widget is removed while hidden");
            store.Set(2);
            scheduler.DrainImmediateForTest();
            var recycled = _root.Q<T>(name);
            Assume.That(recycled, Is.Not.Null, "Precondition: a plain widget was recreated from the pool");
            return recycled;
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
            var toggle = Recycle<Toggle>(
                mode => mode == 0 ? V.Toggle(name: "t", value: true)
                      : mode == 2 ? V.Toggle(name: "t")
                      : V.Fragment(Array.Empty<VNode>()),
                "t");

            // Assert — the recycled toggle does not ghost the prior checked state.
            Assert.IsFalse(toggle.value);
        }

        [Test]
        public void Given_ALabelledToggleWasRemoved_When_APlainToggleIsRecreatedFromThePool_Then_ItHasNoLabel()
        {
            // Arrange/Act — a labelled toggle is pooled and a plain toggle (no label) is rented back.
            var toggle = Recycle<Toggle>(
                mode => mode == 0 ? V.Toggle(name: "t", label: "Stale")
                      : mode == 2 ? V.Toggle(name: "t")
                      : V.Fragment(Array.Empty<VNode>()),
                "t");

            // Assert — the recycled toggle carries no leftover label.
            Assert.AreEqual(string.Empty, toggle.label);
        }

        // TextField (security-critical)

        [Test]
        public void Given_ATextFieldHeldPii_When_APlainTextFieldIsRecreatedFromThePool_Then_ItHasNoStaleValue()
        {
            // Arrange/Act — a field holding PII is pooled and a plain field (no value) is rented back.
            var field = Recycle<TextField>(
                mode => mode == 0 ? V.TextField(name: "tf", value: "secret@example.com")
                      : mode == 2 ? V.TextField(name: "tf")
                      : V.Fragment(Array.Empty<VNode>()),
                "tf");

            // Assert — the recycled field surfaces no prior value (PII must not ghost across pool reuse).
            Assert.AreEqual(string.Empty, field.value);
        }

        [Test]
        public void Given_APasswordTextFieldWasRemoved_When_APlainTextFieldIsRecreatedFromThePool_Then_ItIsNotMasked()
        {
            // Arrange/Act — a password (masked) field is pooled and a plain field is rented back.
            var field = Recycle<TextField>(
                mode => mode == 0 ? V.TextField(name: "tf", value: "pw", isPasswordField: true)
                      : mode == 2 ? V.TextField(name: "tf")
                      : V.Fragment(Array.Empty<VNode>()),
                "tf");

            // Assert — the recycled field is not still masking input from the previous consumer.
            Assert.IsFalse(field.textEdition.isPassword);
        }

        [Test]
        public void Given_ALabelledTextFieldWasRemoved_When_APlainTextFieldIsRecreatedFromThePool_Then_ItHasNoLabel()
        {
            // Arrange/Act — a labelled field is pooled and a plain field (no label) is rented back.
            var field = Recycle<TextField>(
                mode => mode == 0 ? V.TextField(name: "tf", label: "Email")
                      : mode == 2 ? V.TextField(name: "tf")
                      : V.Fragment(Array.Empty<VNode>()),
                "tf");

            // Assert — the recycled field carries no leftover label.
            Assert.AreEqual(string.Empty, field.label);
        }

        // Slider

        [Test]
        public void Given_ASliderWithCustomRangeWasRemoved_When_APlainSliderIsRecreatedFromThePool_Then_ItHasTheDefaultHighValue()
        {
            // Arrange/Act — a slider with a widened range is pooled and a plain slider (default range) is rented back.
            var slider = Recycle<Slider>(
                mode => mode == 0 ? V.Slider(name: "s", value: 80f, lowValue: 0f, highValue: 100f)
                      : mode == 2 ? V.Slider(name: "s")
                      : V.Fragment(Array.Empty<VNode>()),
                "s");

            // Assert — the recycled slider is restored to Unity's default highValue (10), not the prior 100.
            Assert.AreEqual(10f, slider.highValue);
        }

        [Test]
        public void Given_ASliderWithAValueWasRemoved_When_APlainSliderIsRecreatedFromThePool_Then_ItHasNoStaleValue()
        {
            // Arrange/Act — a slider carrying a value is pooled and a plain slider (no value) is rented back.
            var slider = Recycle<Slider>(
                mode => mode == 0 ? V.Slider(name: "s", value: 7f, lowValue: 0f, highValue: 10f)
                      : mode == 2 ? V.Slider(name: "s")
                      : V.Fragment(Array.Empty<VNode>()),
                "s");

            // Assert — the recycled slider does not ghost the prior value.
            Assert.AreEqual(0f, slider.value);
        }
    }
}
