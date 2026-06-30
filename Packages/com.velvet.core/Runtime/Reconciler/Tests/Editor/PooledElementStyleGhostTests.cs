using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for pooled-element inline-style ghosting: a primitive returned to <see cref="VNodePool"/>
    /// must be reset so it cannot carry an inline style its previous node set onto its next consumer.
    /// <c>FiberElementPoolReset.ResetInlineStyle</c> documents itself as nulling EVERY inline property Velvet may
    /// set, but originally omitted <c>letterSpacing</c> (written by the <c>tracking-[Npx]</c> arbitrary). A recycled
    /// label therefore kept its previous letter spacing and ghosted it onto the next consumer whose node declared no
    /// <c>tracking-*</c> (the fresh node's empty oldClasses diff never clears it) — the same pooled-reuse ghosting
    /// class as the Button-children bug. GWT, one assert per case.
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
    }
}
