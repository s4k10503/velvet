using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural regression coverage for the wrapper-less PAINT layers — skew, drop shadow, gradient,
    /// animate-* motion and border-dashed / -dotted — when the class that gates one arrives through a
    /// <b>variant</b> rather than as a literal token. A payload is added straight to the element's live class
    /// list by its manipulator when the signal fires; it never reaches the reconciled class array these
    /// layers are otherwise configured from, so <c>dark:shadow-lg</c> would paint nothing until an unrelated
    /// re-render happened to bring the bare token in literally.
    /// </summary>
    /// <remarks>
    /// Each case asserts the style write the layer actually makes — the face-suppression sentinel a
    /// silhouette installs so it can repaint the face itself, the baked gradient texture, the pan oversize —
    /// rather than class membership, which is true whether or not the layer ever ran. <c>dark:</c> needs no
    /// panel: the conditional manipulator subscribes to <see cref="VelvetTheme"/>'s theme signal when it
    /// attaches to the element and evaluates it off-panel, so a bare reconciler crosses the same side channel
    /// a breakpoint crossing uses. GWT, one assert per case.
    /// </remarks>
    [TestFixture]
    internal sealed class VariantGatedPaintThemeTests
    {
        // The pan axis of animate-gradient is oversized to this percentage; the gradient's own stretch-to-fill
        // is 100, so the two are distinguishable on the same style slot.
        private const float PanOversizePercent = 200f;

        [TearDown]
        public void TearDown() => VelvetTheme.IsDark = false;

        private static VisualElement Mount(ReconcilerScope scope, string className)
        {
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[] { V.Div(className: className, name: "card") });
            return scope.Root.Q<VisualElement>("card");
        }

        [Test]
        public void Given_ADarkGatedSkew_When_TheThemeTurnsDark_Then_TheSilhouetteTakesTheFace()
        {
            // Arrange — an inline background gives the stash something to capture off-panel; the sheared
            // silhouette then masks it with the sentinel so only its own repaint shows.
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "bg-[#FFFFFF] dark:-skew-x-6");
            Assume.That(SilhouetteFace.IsSentinel(card.style.backgroundColor.value), Is.False,
                "Precondition: nothing shears the face while the theme is light");

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(SilhouetteFace.IsSentinel(card.style.backgroundColor.value), Is.True);
        }

        [Test]
        public void Given_ADarkGatedSkewApplied_When_TheThemeTurnsLight_Then_TheFaceIsReturned()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "bg-[#FFFFFF] dark:-skew-x-6");
            VelvetTheme.IsDark = true;
            // Both edges in one assertion: an element that never sheared at all would satisfy the off-edge on
            // its own, so "was masked, then was not" is the whole claim.
            var maskedWhileDark = SilhouetteFace.IsSentinel(card.style.backgroundColor.value);

            // Act
            VelvetTheme.IsDark = false;

            // Assert — the detach releases the suppression, so the native background resolves again.
            Assert.That((maskedWhileDark, SilhouetteFace.IsSentinel(card.style.backgroundColor.value)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADarkGatedShadow_When_TheThemeTurnsDark_Then_TheShadowPaintTakesTheFace()
        {
            // Arrange — an UPRIGHT shadow caster owns its face: the paint masks the native chrome and
            // repaints an opaque fill over the shadow quad so only the outer halo shows.
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "bg-[#FFFFFF] dark:shadow-lg");
            Assume.That(SilhouetteFace.IsSentinel(card.style.backgroundColor.value), Is.False,
                "Precondition: nothing shadows the card while the theme is light");

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(SilhouetteFace.IsSentinel(card.style.backgroundColor.value), Is.True);
        }

        [Test]
        public void Given_ADarkGatedShadowApplied_When_TheThemeTurnsLight_Then_TheFaceIsReturned()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "bg-[#FFFFFF] dark:shadow-lg");
            VelvetTheme.IsDark = true;
            var maskedWhileDark = SilhouetteFace.IsSentinel(card.style.backgroundColor.value);

            // Act
            VelvetTheme.IsDark = false;

            // Assert — both edges, since a shadow that never attached would satisfy the off-edge alone.
            Assert.That((maskedWhileDark, SilhouetteFace.IsSentinel(card.style.backgroundColor.value)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADarkGatedGradient_When_TheThemeTurnsDark_Then_TheBakedTextureIsApplied()
        {
            // Arrange — the whole gradient (shape activator and both stops) is behind the variant, so the
            // reconciled array carries no gradient token at all.
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "dark:bg-gradient-to-r dark:from-red-500 dark:to-blue-500");
            Assume.That(card.style.backgroundImage.value.texture, Is.Null,
                "Precondition: no gradient is baked while the theme is light");

            // Act
            VelvetTheme.IsDark = true;

            // Assert — the gradient is realised as a baked texture on the element's own background-image.
            Assert.That(card.style.backgroundImage.value.texture, Is.Not.Null);
        }

        [Test]
        public void Given_ADarkGatedGradientApplied_When_TheThemeTurnsLight_Then_TheTextureIsCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "dark:bg-gradient-to-r dark:from-red-500 dark:to-blue-500");
            VelvetTheme.IsDark = true;
            var bakedWhileDark = card.style.backgroundImage.value.texture != null;

            // Act
            VelvetTheme.IsDark = false;

            // Assert — both edges, since a gradient that never baked would satisfy the off-edge alone.
            Assert.That((bakedWhileDark, card.style.backgroundImage.value.texture != null),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADarkGatedAnimateOverALiteralGradient_When_TheThemeTurnsDark_Then_ThePanAxisIsOversized()
        {
            // Arrange — a pan motion needs a gradient to pan, so the gradient is literal and only the motion
            // is variant-gated. Attaching the driver oversizes the pan axis immediately (the per-frame tick
            // only moves the position), which makes the attach observable without a running player loop.
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "bg-gradient-to-r from-red-500 to-blue-500 dark:animate-gradient");
            Assume.That(card.style.backgroundSize.value.x.value, Is.EqualTo(100f),
                "Precondition: the un-panned gradient stretches to fill");

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(card.style.backgroundSize.value.x.value, Is.EqualTo(PanOversizePercent));
        }

        [Test]
        public void Given_ADarkGatedBorderDashed_When_TheThemeTurnsDark_Then_TheBorderColorIsSuppressed()
        {
            // Arrange — the dashed outline paints itself, so the native border COLOR is masked (the width is
            // left real, keeping the box's border gutter).
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "border-[#FFFFFF] dark:border-dashed");
            Assume.That(SilhouetteFace.IsSentinel(card.style.borderLeftColor.value), Is.False,
                "Precondition: the border is solid and unmasked while the theme is light");

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(SilhouetteFace.IsSentinel(card.style.borderLeftColor.value), Is.True);
        }

        [Test]
        public void Given_ADarkGatedBorderDashedApplied_When_TheThemeTurnsLight_Then_TheBorderColorIsUnmasked()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "border-[#FFFFFF] dark:border-dashed");
            VelvetTheme.IsDark = true;
            var maskedWhileDark = SilhouetteFace.IsSentinel(card.style.borderLeftColor.value);

            // Act
            VelvetTheme.IsDark = false;

            // Assert — both edges: the detach releases the mask, and an outline that never painted would
            // satisfy the released half on its own.
            Assert.That((maskedWhileDark, SilhouetteFace.IsSentinel(card.style.borderLeftColor.value)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADarkGatedShadowAndBorderDashed_When_TheThemeTurnsDark_Then_TheDashedLayerDefersToTheShadow()
        {
            // Arrange — two families arrive in the same toggle. The shadow owns the whole face and repaints a
            // solid border itself, so the dashed layer must stand down; that only holds if the re-run applies
            // them in the reconcile path's order (shadow before border-style).
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "bg-[#FFFFFF] dark:shadow-lg dark:border-dashed");

            // Act
            VelvetTheme.IsDark = true;

            // Assert — the shadow arrived AND the dashed layer stood down; an element where neither ran
            // would satisfy the second half alone.
            var context = scope.Reconciler.Context;
            Assert.That((context.ShadowBindings.ContainsKey(card), context.BorderStyleBindings.ContainsKey(card)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ATrackedElementWithALitPayload_When_ReRenderedUnchanged_Then_TheClassSourceIsReused()
        {
            // Arrange — the composed source is what every gated pass reads, and these passes run on EVERY
            // patch, so a re-render that changes nothing must not rebuild it. The parse cache is drained so
            // the second render hands over a freshly allocated array with the same tokens, which is what a
            // component rebuilding its VNode tree every render actually produces.
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "bg-[#FFFFFF] dark:shadow-lg", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");
            VelvetTheme.IsDark = true;
            var before = scope.Reconciler.Context.VariantGateClasses[card].Resolved;
            ClassNameCacheTestAccess.ClearForTest();
            var second = new VNode[] { V.Div(className: "bg-[#FFFFFF] dark:shadow-lg", name: "card") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, second);

            // Assert — the same array instance, so the patch composed nothing and allocated nothing.
            Assert.That(scope.Reconciler.Context.VariantGateClasses[card].Resolved, Is.SameAs(before));
        }

        [Test]
        public void Given_ADarkGatedGradientOnAMotion_When_TheThemeTurnsDark_Then_TheBakedTextureIsApplied()
        {
            // Arrange — a Motion paints none of the three silhouette layers, but it does carry the straight
            // background-image gradient, so a variant has to reach that pass on a Motion exactly as it does
            // on an element.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[]
                {
                    V.Motion(className: "dark:bg-gradient-to-r dark:from-red-500 dark:to-blue-500",
                        name: "mover")
                });
            var mover = scope.Root.Q<VisualElement>("mover");
            Assume.That(mover.style.backgroundImage.value.texture, Is.Null,
                "Precondition: no gradient is baked while the theme is light");

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(mover.style.backgroundImage.value.texture, Is.Not.Null);
        }

        [Test]
        public void Given_ADarkGatedShadowOnAMotion_When_TheThemeTurnsDark_Then_NoShadowPaintIsAttached()
        {
            // Arrange — a Motion never paints a drop shadow (the silhouette paints stand down on a Motion, so
            // nothing would reconcile one afterwards), and a variant must not be a way in.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[] { V.Motion(className: "bg-[#FFFFFF] dark:shadow-lg", name: "mover") });

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(0));
        }
    }

    /// <summary>
    /// The CREATE-time resolve, which is a separate seam from the re-sync every other fixture here drives.
    /// Most variant families cannot reach it: the factory builds the element detached and runs every applier
    /// before inserting it, so a <c>dark:</c> payload (evaluated on a theme change) and an <c>md:</c> one
    /// (guarded on a resolved panel width) are both still off when the create-time resolve runs — they arrive
    /// later, through attach. The families evaluated from the element's own placed subtree during create
    /// (<c>has-[.class]:</c> here, and equally structural / <c>has-[:checked]:</c> / <c>data-</c> / <c>aria-</c>
    /// / <c>supports-</c>) are the ones whose payload is already on the live class list at that moment, and so
    /// the ones that pin it.
    /// </summary>
    [TestFixture]
    internal sealed class VariantGatedPaintCreatePassTests
    {
        [Test]
        public void Given_AHasClassGatedShadow_When_TheElementMounts_Then_TheShadowPaintsWithoutAnyLaterRender()
        {
            // Arrange — the matching descendant is present from the first render, so the has- pass lights the
            // payload while the element is still being created.
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[]
                {
                    V.Div(className: "bg-[#FFFFFF] has-[.flag]:shadow-lg", name: "card",
                        children: new VNode[] { V.Div("flag") })
                });

            // Assert — a create pass that read the reconciled array alone would carry no shadow token, and
            // nothing re-runs the paint afterwards, so the element would mount unshadowed and stay that way.
            var card = scope.Root.Q<VisualElement>("card");
            Assert.That(scope.Reconciler.Context.ShadowBindings.ContainsKey(card), Is.True);
        }
    }

    /// <summary>
    /// A gate toggle must not disturb an ALREADY-ATTACHED silhouette's face suppression. Both silhouette layers
    /// stash the element's face colours and write a sentinel over the shared inline slot so they can repaint the
    /// face themselves; on a class change with a USS-driven (not <c>bg-[…]</c>) stash they release that
    /// suppression and wait to re-stash, because a USS colour can move beneath the sentinel unseen. The re-stash
    /// runs from <c>CustomStyleResolvedEvent</c> (which UI Toolkit dispatches only for an element declaring
    /// custom properties — Velvet's utilities consume <c>var(--…)</c> without declaring any), from
    /// <c>GeometryChangedEvent</c>, or from the next patch. A variant toggle fires none of the three: it changes
    /// no rect and need never be followed by a patch, so a release taken here is permanent and the native
    /// rectangle paints behind the silhouette for good.
    /// </summary>
    /// <remarks>
    /// A real panel is required in both directions: off-panel with no inline colour the stash never forms at
    /// all, so the release branch is unreachable and the bug invisible. Each case asserts BOTH edges, since an
    /// element whose suppression never applied would satisfy the post-toggle half on its own.
    /// </remarks>
    [TestFixture]
    internal sealed class VariantGatedPaintFaceSuppressionPanelTests : PanelTestBase
    {
        public override void TearDown()
        {
            VelvetTheme.IsDark = false;
            base.TearDown();
        }

        // Mounts a card and drives the stash the way a live host does: the create-time attempt bails (the element
        // is not parented yet, so there is no resolved colour to capture), and the layer's GeometryChangedEvent
        // hook takes it once layout has run. Geometry events neither bubble nor trickle, so it is delivered to
        // the card itself rather than the panel root.
        private VisualElement MountAndStash(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: className));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(card.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            card.SimulateEvent(evt);
            return card;
        }

        [Test]
        public void Given_ALiteralSkewOverAUssFace_When_AnUnrelatedGateTokenToggles_Then_TheFaceStaysSuppressed()
        {
            // Arrange — the skew and the face colour are both literal, so the toggle changes neither: only
            // `shadow-lg` arrives. A USS face colour (not a bracket value) is what puts the stash on the
            // releasing branch.
            var card = MountAndStash("bg-blue-500 -skew-x-6 dark:shadow-lg");
            var suppressedAtRest = SilhouetteFace.IsSentinel(card.style.backgroundColor.value);

            // Act
            VelvetTheme.IsDark = true;

            // Assert — released suppression means the un-sheared blue rectangle resolves again and paints
            // behind the slant, permanently.
            Assert.That((suppressedAtRest, SilhouetteFace.IsSentinel(card.style.backgroundColor.value)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ALiteralUprightShadowOverAUssFace_When_AnUnrelatedGateTokenToggles_Then_TheFaceStaysSuppressed()
        {
            // Arrange — an upright shadow caster owns the whole face too. Its sync passes a hardcoded "classes
            // changed", so it reaches the releasing branch on EVERY re-sync, not only on a real class change.
            var card = MountAndStash("bg-blue-500 shadow-lg dark:gap-4");
            var suppressedAtRest = SilhouetteFace.IsSentinel(card.style.backgroundColor.value);

            // Act
            VelvetTheme.IsDark = true;

            // Assert — a released face lets the native blue rectangle resolve again behind the repainted fill.
            Assert.That((suppressedAtRest, SilhouetteFace.IsSentinel(card.style.backgroundColor.value)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ALiteralBorderDashedOverAUssBorder_When_AnUnrelatedGateTokenToggles_Then_TheBorderStaysSuppressed()
        {
            // Arrange — the border-only stash takes the same releasing branch, reached here through a layout
            // gate token rather than a paint one, so the trigger is not specific to the paint sequence.
            var card = MountAndStash("border border-gray-300 border-dashed dark:gap-4");
            var suppressedAtRest = SilhouetteFace.IsSentinel(card.style.borderLeftColor.value);

            // Act
            VelvetTheme.IsDark = true;

            // Assert — a released border lets the native solid stroke resolve again under the dashed paint.
            Assert.That((suppressedAtRest, SilhouetteFace.IsSentinel(card.style.borderLeftColor.value)),
                Is.EqualTo((true, true)));
        }
    }

    /// <summary>
    /// The same contract driven by a real breakpoint crossing rather than a theme flip. A responsive payload
    /// is applied by the conditional manipulator when the resolved scope width crosses the breakpoint, which
    /// only a real panel can produce — so these run inside a live <see cref="UnityEditor.EditorWindow"/>
    /// panel (via <see cref="PanelTestBase"/>) sized per test, force the layout pass so
    /// <c>resolvedStyle.width</c> resolves, then deliver a <see cref="GeometryChangedEvent"/> so the
    /// manipulator re-evaluates. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class VariantGatedPaintPanelTests : PanelTestBase
    {
        private const float MdBreakpoint = 768f;
        private const float WidePanel = 1000f;   // >= md
        private const float NarrowPanel = 500f;  // < md

        // Mounts a card at the given panel width and resolves the breakpoint against it.
        private VisualElement MountAndResolveAt(float width, string className)
        {
            _window.position = new Rect(0, 0, width, 600);
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: className));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            ResolveAt(width, card);
            return card;
        }

        // Sets the panel width, forces the layout pass, then fires a GeometryChangedEvent on the panel root
        // so the responsive manipulator re-reads the width source.
        private void ResolveAt(float width, VisualElement card)
        {
            _window.position = new Rect(0, 0, width, 600);
            ForcePanelUpdate(card.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            card.panel.visualTree.SimulateEvent(evt);
        }

        [Test]
        public void Given_AResponsiveShadow_When_TheRootIsWiderThanMd_Then_TheShadowPaintTakesTheFace()
        {
            // Arrange / Act
            var card = MountAndResolveAt(WidePanel, "bg-[#FFFFFF] md:shadow-lg");
            Assume.That(card.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert
            Assert.That(SilhouetteFace.IsSentinel(card.style.backgroundColor.value), Is.True);
        }

        [Test]
        public void Given_AResponsiveShadowActiveWide_When_ThePanelShrinksBelowMd_Then_TheFaceIsReturned()
        {
            // Arrange
            var card = MountAndResolveAt(WidePanel, "bg-[#FFFFFF] md:shadow-lg");
            var maskedWhileWide = SilhouetteFace.IsSentinel(card.style.backgroundColor.value);

            // Act
            ResolveAt(NarrowPanel, card);

            // Assert — both edges, since a shadow that never attached would satisfy the narrow half alone.
            Assert.That((maskedWhileWide, SilhouetteFace.IsSentinel(card.style.backgroundColor.value)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AResponsiveGradient_When_TheRootIsWiderThanMd_Then_TheBakedTextureIsApplied()
        {
            // Arrange / Act
            var card = MountAndResolveAt(WidePanel, "md:bg-gradient-to-r md:from-red-500 md:to-blue-500");
            Assume.That(card.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert
            Assert.That(card.style.backgroundImage.value.texture, Is.Not.Null);
        }

        [Test]
        public void Given_AResponsiveBorderDashed_When_TheRootIsWiderThanMd_Then_TheBorderColorIsSuppressed()
        {
            // Arrange / Act
            var card = MountAndResolveAt(WidePanel, "border-[#FFFFFF] md:border-dashed");
            Assume.That(card.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert
            Assert.That(SilhouetteFace.IsSentinel(card.style.borderLeftColor.value), Is.True);
        }

        [Test]
        public void Given_AResponsiveShadowAndBorderDashed_When_TheRootIsWiderThanMd_Then_TheDashedLayerDefersToTheShadow()
        {
            // Arrange / Act — two families arrive in the same toggle, here on the ATTACH edge: a responsive
            // payload is still off while the element is built detached, and lights as it joins the panel. The
            // shadow owns the whole face and repaints a solid border itself, so the dashed layer must stand
            // down — which only holds if the two are applied in the reconcile path's order.
            var card = MountAndResolveAt(WidePanel, "bg-[#FFFFFF] md:shadow-lg md:border-dashed");
            var context = _mounted.Root.Reconciler.Context;

            // Assert — the shadow arrived AND the dashed layer stood down; an element where neither ran
            // would satisfy the second half alone.
            Assert.That((context.ShadowBindings.ContainsKey(card), context.BorderStyleBindings.ContainsKey(card)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AResponsiveSkew_When_TheRootIsNarrowerThanMd_Then_TheFaceIsUntouched()
        {
            // Arrange / Act — below the breakpoint the payload never applies, so the composed class source
            // must resolve to exactly what the element literally declares.
            var card = MountAndResolveAt(NarrowPanel, "bg-[#FFFFFF] md:-skew-x-6");
            Assume.That(card.panel.visualTree.resolvedStyle.width, Is.LessThan(MdBreakpoint),
                "Precondition: the panel root resolved below the md breakpoint");

            // Assert
            Assert.That(SilhouetteFace.IsSentinel(card.style.backgroundColor.value), Is.False);
        }
    }
}
