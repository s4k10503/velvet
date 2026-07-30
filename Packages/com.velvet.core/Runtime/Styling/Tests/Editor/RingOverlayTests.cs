using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>ring-*</c> / <c>outline-*</c> band's behaviour now that it is hosted on a
    /// reconciler-invisible sibling overlay rather than on a structural wrapper around the ringed element.
    /// Two things the wrapper could not do are pinned here: a ring behind a VARIANT renders (attaching an
    /// overlay is not parent surgery on the element's own slot, so the variant re-sync may do it), and the
    /// band's per-corner radii follow the element's own. The band's size and position against the USS-scale
    /// radius are covered by RingGeometryPanelTests.
    /// </summary>
    internal sealed class RingOverlayTests : PanelTestBase
    {
        // The per-corner radius case needs a real USS rounded-tl-lg to reflect into resolvedStyle; without
        // the sheet every corner resolves 0 and the whole-element class-scale fallback answers instead,
        // which is exactly the path that case exists to avoid taking.
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private static VisualElement RingOverlayIn(VisualElement host)
        {
            for (var i = 0; i < host.childCount; i++)
            {
                if (host[i].ClassListContains(RingOverlay.MarkerClass))
                {
                    return host[i];
                }
            }
            return null;
        }

        private VisualElement MountRinged(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: className));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(card.panel);
            return card;
        }

        // The issue this layer was rebuilt for: a ring behind a variant used to toggle a class that drew
        // nothing, because the band needed a wrapper and only a reconcile pass may add one.

        [Test]
        public void Given_AFocusRingVariant_When_TheElementIsFocused_Then_TheBandIsHosted()
        {
            // Arrange — a focusable element whose ring exists only behind the focus: variant.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Button(name: "btn", className: "w-[80px] h-[30px] focus:ring-2", text: "ok"));
            var btn = _window.rootVisualElement.Q<VisualElement>("btn");
            ForcePanelUpdate(btn.panel);
            var hostedBefore = RingOverlayIn(btn.parent) != null;

            // Act — the focus variant fires.
            using (var e = FocusEvent.GetPooled()) { btn.SimulateEvent(e); }

            // Assert — absent, then present. Folded into one comparison rather than gating the "absent"
            // half behind an Assume: that is the behaviour under test, and a regression that left the band
            // up at all times would report Inconclusive instead of failing.
            Assert.That((hostedBefore, RingOverlayIn(btn.parent) != null), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AFocusedRingVariant_When_TheElementIsBlurred_Then_TheBandIsRemoved()
        {
            // Arrange — the same element, focused, so the band is up.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Button(name: "btn", className: "w-[80px] h-[30px] focus:ring-2", text: "ok"));
            var btn = _window.rootVisualElement.Q<VisualElement>("btn");
            ForcePanelUpdate(btn.panel);
            using (var e = FocusEvent.GetPooled()) { btn.SimulateEvent(e); }
            var hostedWhileFocused = RingOverlayIn(btn.parent) != null;

            // Act
            using (var e = BlurEvent.GetPooled()) { btn.SimulateEvent(e); }

            // Assert — present, then gone. Both halves in one comparison: asserting only the "gone" half
            // would pass just as well against a ring that never rendered in the first place.
            Assert.That((hostedWhileFocused, RingOverlayIn(btn.parent) != null), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABaseRingAndAFocusRingZero_When_Focused_Then_TheBandIsCancelled()
        {
            // The composed class source puts the base array before the variant tokens, so a variant payload
            // is later in the cascade than the base it overrides — which is what lets a variant CANCEL rather
            // than only add. ring-0 resolves width 0, i.e. no band.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Button(name: "btn", className: "w-[80px] h-[30px] ring-2 focus:ring-0", text: "ok"));
            var btn = _window.rootVisualElement.Q<VisualElement>("btn");
            ForcePanelUpdate(btn.panel);
            var hostedAtRest = RingOverlayIn(btn.parent) != null;

            // Act
            using (var e = FocusEvent.GetPooled()) { btn.SimulateEvent(e); }

            // Assert — present at rest, gone while focused.
            Assert.That((hostedAtRest, RingOverlayIn(btn.parent) != null), Is.EqualTo((true, false)));
        }

        // Geometry

        [Test]
        public void Given_AnArbitraryCornerRadius_When_LaidOut_Then_TheBandsOuterRadiusFollowsIt()
        {
            // Arrange & Act — an arbitrary rounded-[12px] resolves through resolvedStyle rather than the
            // rounded-* class scale, and the band's OUTER radius is that radius plus the ring width.
            var card = MountRinged("w-[100px] h-[40px] rounded-[12px] ring-4");
            var overlay = RingOverlayIn(card.parent);

            // Assert
            Assert.That(overlay?.resolvedStyle.borderTopLeftRadius, Is.EqualTo((float?)16f));
        }

        [Test]
        public void Given_PerCornerRadii_When_LaidOut_Then_EachCornerFollowsItsOwnRadius()
        {
            // Arrange & Act — a USS-scale per-corner class, deliberately: the class-scale FALLBACK answers
            // for the whole element (top-left representative), so an arbitrary rounded-tl-[12px] never reaches
            // it and would pass even with the fallback rounding all four corners. rounded-tl-lg does reach it.
            var card = MountRinged("w-[100px] h-[40px] rounded-tl-lg ring-4");
            var overlay = RingOverlayIn(card.parent);

            // Assert — top-left carries the radius plus the ring width; top-right carries the ring width only.
            // rounded-lg is 8px.
            Assert.That((overlay?.resolvedStyle.borderTopLeftRadius, overlay?.resolvedStyle.borderTopRightRadius),
                Is.EqualTo(((float?)12f, (float?)4f)));
        }
    }
}
