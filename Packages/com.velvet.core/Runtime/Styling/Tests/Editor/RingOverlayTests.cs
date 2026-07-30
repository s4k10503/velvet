using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>ring-*</c> / <c>outline-*</c> band's behaviour now that it is hosted on a
    /// reconciler-invisible sibling overlay rather than on a structural wrapper around the ringed element.
    /// Two things the wrapper could not do are pinned here: a ring behind a VARIANT renders (attaching an
    /// overlay is not parent surgery on the element's own slot, so the variant re-sync may do it), and the
    /// band's per-corner radii follow the element's own. The geometry section also pins which transforms the
    /// band follows, which is what the <c>V.Motion</c> exclusion in <see cref="FiberNodeFactory"/> rests on.
    /// </summary>
    internal sealed class RingOverlayTests : PanelTestBase
    {
        // The per-corner radius case needs a real USS rounded-tl-lg to reflect into resolvedStyle; without
        // the sheet every corner resolves 0 and the whole-element class-scale fallback answers instead,
        // which is exactly the path that case exists to avoid taking.
        protected override void LoadStyleSheets() => _window.rootVisualElement.LoadBundledStyleUtilitiesForTest();

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

        // The band belonging to one particular element, rather than the first in the parent: the transform
        // cases put two ringed elements under one host.
        private static VisualElement BandFor(VisualElement element)
        {
            var host = element.parent;
            var next = host.IndexOf(element) + 1;
            return next < host.childCount && host[next].ClassListContains(RingOverlay.MarkerClass)
                ? host[next]
                : null;
        }

        // How far along x a band paints from the element it rings. worldBound and not layout because layout
        // is the pre-transform box, and the element's OWN transform is the thing this reading exists to
        // catch: read from layout, a translated element and a still one both measure -4.
        // Scope, and the reason the ancestor case below measures absolute positions rather than calling
        // this: the band and its element are SIBLINGS, so an ancestor's TRANSLATION cancels out of the
        // difference exactly and this helper cannot see one at all. An ancestor SCALE does not cancel — it
        // multiplies the difference, so scale-150 over a ring-4 band reads -6 and not -4.
        private static float BandOffsetX(VisualElement element)
            => BandFor(element).worldBound.x - element.worldBound.x;

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

        // Which transforms the band follows. These two are what the V.Motion ring exclusion rests on, so a
        // change that made the band track its own element's transform should turn the first of them red and
        // be taken as licence to lift that exclusion.

        [Test]
        public void Given_ARingedElement_When_ATransformMovesIt_Then_TheBandStaysOnTheUntransformedBox()
        {
            // Arrange — two ringed cards under one host, identical but for the transform on the second.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "wrap", className: "w-[300px] h-[200px]", children: new VNode[]
                {
                    V.Div(name: "still", className: "w-[100px] h-[40px] ring-4"),
                    V.Div(name: "moved", className: "w-[100px] h-[40px] ring-4 translate-x-8"),
                }));
            var still = _window.rootVisualElement.Q<VisualElement>("still");
            var moved = _window.rootVisualElement.Q<VisualElement>("moved");

            // Act
            ForcePanelUpdate(still.panel);

            // Assert — the untransformed card is the control: -4 is a band sitting exactly ring-4 outside
            // its element, so any state in which the translate did not take effect reports -4 twice and
            // fails here rather than reading as evidence of a band that followed. The transformed card
            // measures the whole of its own 32px translate further out, because the band is placed from the
            // laid-out box and is not in the subtree the transform composites over.
            //
            // Rounded because .Within() does not reach the members of a ValueTuple under Unity's NUnit —
            // the comparison is exact, and both quantities are whole pixels by construction.
            Assert.That((Mathf.Round(BandOffsetX(still)), Mathf.Round(BandOffsetX(moved))),
                Is.EqualTo((-4f, -36f)));
        }

        [Test]
        public void Given_ARingedElement_When_AnAncestorCarriesTheTransform_Then_TheBandMovesWithIt()
        {
            // Arrange — two identically laid-out hosts, one translated, each holding one ringed card. Two
            // hosts and not one, because band and card are siblings: their DIFFERENCE cancels an ancestor's
            // translation exactly, so the only reading that can see one is an absolute position compared
            // across a transformed and an untransformed copy of the same arrangement.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "wrap", className: "w-[300px] h-[200px]", children: new VNode[]
                {
                    V.Div(name: "stillHost", className: "w-[300px] h-[60px]", children: new VNode[]
                    {
                        V.Div(name: "stillCard", className: "w-[100px] h-[40px] ring-4"),
                    }),
                    V.Div(name: "movedHost", className: "w-[300px] h-[60px] translate-x-8", children: new VNode[]
                    {
                        V.Div(name: "movedCard", className: "w-[100px] h-[40px] ring-4"),
                    }),
                }));
            var stillCard = _window.rootVisualElement.Q<VisualElement>("stillCard");
            var movedCard = _window.rootVisualElement.Q<VisualElement>("movedCard");

            // Act
            ForcePanelUpdate(stillCard.panel);

            // Assert — the card's own 32px shift is the control: it is the ancestor transform actually
            // resolving and moving the subtree, so a state in which the translate never took effect reports
            // 0 here and fails rather than reading as evidence. The band shifts by the same 32px, which is
            // the claim — it sits inside the host and is carried by the transform, unlike the element's own
            // transform in the case above. This is what makes wrapping a V.Motion around a ringed Div work.
            var cardShift = Mathf.Round(movedCard.worldBound.x - stillCard.worldBound.x);
            var bandShift = Mathf.Round(BandFor(movedCard).worldBound.x - BandFor(stillCard).worldBound.x);
            Assert.That((cardShift, bandShift), Is.EqualTo((32f, 32f)));
        }
    }
}
