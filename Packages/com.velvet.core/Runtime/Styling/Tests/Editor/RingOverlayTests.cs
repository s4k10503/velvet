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
    /// band's per-corner radii follow the element's own. The geometry section also pins that the band follows
    /// both its element's own transform and an ancestor's.
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

        // The ring's band itself, a child of the overlay that covers the element's box.
        private static VisualElement RingBandOf(VisualElement element)
            => BandFor(element).Q<VisualElement>(RingOverlay.RingBandName);

        // How far along x a band paints from the element it rings. worldBound and not layout because layout
        // is the pre-transform box, and the element's OWN transform is the thing this reading exists to
        // catch: read from layout, a translated element and a still one both measure -4.
        // Scope, and the reason the ancestor case below measures absolute positions rather than calling
        // this: the band and its element are SIBLINGS, so an ancestor's TRANSLATION cancels out of the
        // difference exactly and this helper cannot see one at all. An ancestor SCALE does not cancel — it
        // multiplies the difference, so scale-150 over a ring-4 band reads -6 and not -4.
        private static float BandOffsetX(VisualElement element)
            => RingBandOf(element).worldBound.x - element.worldBound.x;

        // The same reading along y, with the same scope.
        private static float BandOffsetY(VisualElement element)
            => RingBandOf(element).worldBound.y - element.worldBound.y;

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

        [TestCase("focus:ring-2 focus:ring-4")]
        [TestCase("focus:ring-4 focus:ring-2")]
        public void Given_TwoRingWidthsBehindOneVariant_When_Focused_Then_TheOneTailwindEmitsLastWins(string rings)
        {
            // Arrange — both tokens apply at one priority, which a rule's place in Tailwind's order breaks.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Button(name: "btn", className: "w-[80px] h-[30px] " + rings, text: "ok"));
            var btn = _window.rootVisualElement.Q<VisualElement>("btn");
            ForcePanelUpdate(btn.panel);

            // Act
            using (var e = FocusEvent.GetPooled()) { btn.SimulateEvent(e); }

            // Assert
            Assert.That(RingOverlay.TryGet(btn)?.Spec.Ring.Width, Is.EqualTo((float?)4f));
        }

        // Geometry

        [Test]
        public void Given_AnArbitraryCornerRadius_When_LaidOut_Then_TheBandsOuterRadiusFollowsIt()
        {
            // Arrange & Act — an arbitrary rounded-[12px] resolves through resolvedStyle rather than the
            // rounded-* class scale, and the band's OUTER radius is that radius plus the ring width.
            var card = MountRinged("w-[100px] h-[40px] rounded-[12px] ring-4");
            var binding = RingOverlay.TryGet(card);

            // Assert
            Assert.That(RingBandShape.Spread(binding.Radii, 4f).TopLeft.x, Is.EqualTo(16f));
        }

        [Test]
        public void Given_PerCornerRadii_When_LaidOut_Then_EachCornerFollowsItsOwnRadius()
        {
            // Arrange & Act — a USS-scale per-corner class, deliberately: the class-scale FALLBACK answers
            // for the whole element (top-left representative), so an arbitrary rounded-tl-[12px] never reaches
            // it and would pass even with the fallback rounding all four corners. rounded-tl-lg does reach it.
            var card = MountRinged("w-[100px] h-[40px] rounded-tl-lg ring-4");
            var binding = RingOverlay.TryGet(card);

            // Assert — top-left carries the radius plus the ring width. rounded-lg is 8px.
            Assert.That(RingBandShape.Spread(binding.Radii, 4f).TopLeft.x, Is.EqualTo(12f));
        }

        [Test]
        public void Given_PerCornerRadii_When_LaidOut_Then_ASquareCornerStaysSquare()
        {
            // Arrange & Act — the same element; a box-shadow spread grows a corner's radius by CSS Backgrounds 3's
            // formula, so a corner with none stays square where a native border would round it by the ring width.
            var card = MountRinged("w-[100px] h-[40px] rounded-tl-lg ring-4");
            var binding = RingOverlay.TryGet(card);

            // Assert
            Assert.That(RingBandShape.Spread(binding.Radii, 4f).TopRight.x, Is.EqualTo(0f));
        }

        // The outline's band, a child of the overlay OutlineHosting places.
        private static VisualElement OutlineBandOf(VisualElement element)
            => RingOverlay.TryGet(element)?.OutlineElement;

        [Test]
        public void Given_ARingAndAnOutlineWithTheirOwnOffsets_When_LaidOut_Then_TheRingSitsAtItsOwnDistance()
        {
            // Arrange
            var card = MountRinged("w-[40px] h-[40px] ring-2 ring-offset-4 outline-2 outline-offset-1");

            // Act
            ForcePanelUpdate(card.panel);

            // Assert — the ring spreads its offset plus its width, 6px. Rounded because both are whole pixels.
            Assert.That(Mathf.Round(RingBandOf(card).worldBound.x - card.worldBound.x), Is.EqualTo(-6f));
        }

        [Test]
        public void Given_ARingAndAnOutlineWithTheirOwnOffsets_When_LaidOut_Then_TheOutlineSitsAtItsOwnDistance()
        {
            // Arrange
            var card = MountRinged("w-[40px] h-[40px] ring-2 ring-offset-4 outline-2 outline-offset-1");

            // Act
            ForcePanelUpdate(card.panel);

            // Assert — the outline its own offset plus its width, 3px.
            Assert.That(Mathf.Round(OutlineBandOf(card).worldBound.x - card.worldBound.x), Is.EqualTo(-3f));
        }

        [Test]
        public void Given_AnOutline_When_LaidOut_Then_ItsBandIsTheElementsBoxGrownByItsWidth()
        {
            // Arrange
            var card = MountRinged("w-[40px] h-[40px] outline-2");

            // Act
            ForcePanelUpdate(card.panel);

            // Assert — 2px outside each edge.
            Assert.That(OutlineBandOf(card).resolvedStyle.width, Is.EqualTo(44f));
        }

        [Test]
        public void Given_AnInsetRingBesideAnOutline_When_LaidOut_Then_TheOutlineBandIsStillSized()
        {
            // Arrange — the inset band is the element's child and never measures the element itself.
            var card = MountRinged("w-[40px] h-[40px] ring-2 ring-inset outline-2");

            // Act
            ForcePanelUpdate(card.panel);
            ForcePanelUpdate(card.panel);

            // Assert
            Assert.That(OutlineBandOf(card).resolvedStyle.width, Is.EqualTo(44f));
        }

        [Test]
        public void Given_AnOutlineNamingNoColourBesideARingColour_When_LaidOut_Then_TheOutlineIsTheTextColour()
        {
            // Arrange
            var card = MountRinged("w-[40px] h-[40px] ring-2 ring-[#0000ff] outline-2 text-[#ff0000]");

            // Act
            ForcePanelUpdate(card.panel);

            // Assert — each band takes its own family's colour: the ring the blue it names, the outline the text.
            Assert.That(RingOverlay.TryGet(card).OutlineTint, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_AnOutlineNamingNoColourBesideARingColour_When_LaidOut_Then_TheRingKeepsItsColour()
        {
            // Arrange
            var card = MountRinged("w-[40px] h-[40px] ring-2 ring-[#0000ff] outline-2 text-[#ff0000]");

            // Act
            ForcePanelUpdate(card.panel);

            // Assert
            Assert.That(RingOverlay.TryGet(card).RingTint, Is.EqualTo(Color.blue));
        }

        // Where an outline is hosted: after the content of its stacking context, clipped only by its own ancestors.

        [Test]
        public void Given_AnOutlineInsideAClippingParent_When_LaidOut_Then_AProxyStandsOverThatClip()
        {
            // Arrange
            MountClippedOutline();
            var clip = _window.rootVisualElement.Q<VisualElement>("clip");

            // Act — twice: the proxy is placed by the first pass's geometry event and laid out by the second.
            ForcePanelUpdate(clip.panel);
            ForcePanelUpdate(clip.panel);
            var proxy = RingOverlay.TryGet(_window.rootVisualElement.Q<VisualElement>("a")).OutlineOverlay.parent;

            // Assert
            Assert.That(proxy.worldBound, Is.EqualTo(clip.worldBound));
        }

        [Test]
        public void Given_AnOutlineInsideAClippingParent_When_LaidOut_Then_ItsTopLevelNodeFollowsTheContentAfterTheClip()
        {
            // Arrange — after is content outside the clip, which the outline has to paint over.
            MountClippedOutline();
            var clip = _window.rootVisualElement.Q<VisualElement>("clip");
            var wrap = _window.rootVisualElement.Q<VisualElement>("wrap");

            // Act
            ForcePanelUpdate(clip.panel);
            ForcePanelUpdate(clip.panel);
            var top = RingOverlay.TryGet(_window.rootVisualElement.Q<VisualElement>("a")).OutlineOverlay.parent;
            while (top?.parent != null && !top.parent.Contains(wrap))
            {
                top = top.parent;
            }

            // Assert
            Assert.That(top.parent.IndexOf(top), Is.GreaterThan(top.parent.IndexOf(AncestorUnder(top.parent, wrap))));
        }

        private void MountClippedOutline()
            => _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "wrap", className: "w-[300px] h-[200px]", children: new VNode[]
                {
                    V.Div(name: "clip", className: "w-[200px] h-[100px] overflow-hidden", children: new VNode[]
                    {
                        V.Div(name: "a", className: "w-[40px] h-[40px] outline-2"),
                    }),
                    V.Div(name: "after", className: "w-[40px] h-[40px]"),
                }));

        // The child of host on the way down to e.
        private static VisualElement AncestorUnder(VisualElement host, VisualElement e)
        {
            while (e != null && e.parent != host)
            {
                e = e.parent;
            }
            return e;
        }

        [Test]
        public void Given_TwoOutlinesInOneStackingContext_When_TheLaterIsPlacedFirst_Then_TheyShareOneHost()
        {
            // Arrange / Act
            var (outlineA, outlineB) = MountTwoOutlinesLaterFirst();

            // Assert
            Assert.That(outlineA?.parent, Is.SameAs(outlineB?.parent));
        }

        [Test]
        public void Given_TwoOutlinesInOneStackingContext_When_TheLaterIsPlacedFirst_Then_TheyKeepTreeOrder()
        {
            // Arrange / Act
            var (outlineA, outlineB) = MountTwoOutlinesLaterFirst();

            // Assert — a's outline painted before b's, as a comes before b.
            Assert.That(outlineA.parent.IndexOf(outlineA), Is.LessThan(outlineA.parent.IndexOf(outlineB)));
        }

        // b's outline is hosted first; a's arrives with a later render.
        private (VisualElement A, VisualElement B) MountTwoOutlinesLaterFirst()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "wrap", className: "w-[200px] h-[100px]",
                children: new VNode[]
                {
                    V.Div(name: "a", className: "w-[40px] h-[40px]"),
                    V.Div(name: "b", className: "w-[40px] h-[40px] outline-2"),
                }));
            var wrap = _window.rootVisualElement.Q<VisualElement>("wrap");
            ForcePanelUpdate(wrap.panel);
            _mounted.Render(V.Div(name: "wrap", className: "w-[200px] h-[100px]", children: new VNode[]
            {
                V.Div(name: "a", className: "w-[40px] h-[40px] outline-2"),
                V.Div(name: "b", className: "w-[40px] h-[40px] outline-2"),
            }));
            ForcePanelUpdate(wrap.panel);
            return (RingOverlay.TryGet(_window.rootVisualElement.Q<VisualElement>("a"))?.OutlineOverlay,
                RingOverlay.TryGet(_window.rootVisualElement.Q<VisualElement>("b"))?.OutlineOverlay);
        }

        [Test]
        public void Given_AHostedOutline_When_AStyleChangesOutsideItsAncestors_Then_PlacingItAllocatesNothing()
        {
            // Arrange — other's opacity changes between placements, outside the outlined card's ancestors; a re-host
            // allocates, so nothing allocated is nothing re-hosted. The canary is the instrument's own control.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "wrap", className: "w-[200px] h-[100px]", children: new VNode[]
                {
                    V.Div(name: "card", className: "w-[40px] h-[40px] outline-2"),
                    V.Div(name: "other", className: "w-[40px] h-[40px]"),
                }));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            var other = _window.rootVisualElement.Q<VisualElement>("other");
            ForcePanelUpdate(card.panel);
            var binding = RingOverlay.TryGet(card);
            var opaque = true;
            System.Action arrange = () =>
            {
                opaque = !opaque;
                other.style.opacity = opaque ? 1f : 0.5f;
                ForcePanelUpdate(card.panel);
            };
            System.Action place = () => OutlineHosting.Place(card, binding, force: false);
            arrange();
            place();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(arrange, place);

            // Assert
            Assert.That(GCAllocationCanary.BlocksForKnownArray() > 0 ? blocks : -1, Is.Zero);
        }

        [Test]
        public void Given_AHostedOutline_When_AnAncestorBeginsToFade_Then_ThatAncestorHostsIt()
        {
            // Arrange — laid out unfaded, the outline is hosted above wrap.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "wrap", className: "w-[200px] h-[100px]", children: new VNode[]
                {
                    V.Div(name: "card", className: "w-[40px] h-[40px] outline-2"),
                }));
            var wrap = _window.rootVisualElement.Q<VisualElement>("wrap");
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(wrap.panel);
            var binding = RingOverlay.TryGet(card);

            // Act — a change no event reports to the card: the next placement reads it off the ancestors.
            wrap.style.opacity = 0.5f;
            ForcePanelUpdate(wrap.panel);
            OutlineHosting.Place(card, binding, force: false);

            // Assert
            Assert.That(binding.OutlineOverlay.parent, Is.SameAs(wrap));
        }

        [Test]
        public void Given_AnOutlineWithNoCompositingAncestor_When_LaidOut_Then_ItIsHostedAboveTheWrapper()
        {
            // Arrange / Act — the control for the case above: nothing composites, so the outline is hosted above wrap.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "wrap", className: "w-[200px] h-[100px]", children: new VNode[]
                {
                    V.Div(name: "card", className: "w-[40px] h-[40px] outline-2"),
                }));
            var wrap = _window.rootVisualElement.Q<VisualElement>("wrap");
            ForcePanelUpdate(wrap.panel);

            // Assert
            Assert.That(RingOverlay.TryGet(_window.rootVisualElement.Q<VisualElement>("card")).OutlineOverlay.parent,
                Is.Not.SameAs(wrap));
        }

        [Test]
        public void Given_AnOutlineInsideAClipPathWrapper_When_LaidOut_Then_ItsProxyCarriesTheWrappersMask()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "wrap", className: "w-[200px] h-[200px]", children: new VNode[]
                {
                    V.Div(name: "shaped", className: "w-[100px] h-[100px] clip-path-[circle(50%)]", children: new VNode[]
                    {
                        V.Div(name: "card", className: "w-[40px] h-[40px] outline-2"),
                    }),
                }));
            var shaped = _window.rootVisualElement.Q<VisualElement>("shaped");

            // Act
            ForcePanelUpdate(shaped.panel);
            ForcePanelUpdate(shaped.panel);
            // The wrapper bakes its mask after the card is placed, so the next tick, which compares the mask the proxy
            // copied, is what hands it over.
            var now = 0.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(shaped.panel, () => now);
            now = 1.0;
            EditorPanelTestHelpers.DriveSchedulerOnce(shaped.panel);
            ForcePanelUpdate(shaped.panel);
            var proxy = RingOverlay.TryGet(_window.rootVisualElement.Q<VisualElement>("card")).OutlineOverlay.parent;
            var mask = shaped.parent.style.backgroundImage.value.vectorImage;

            // Assert — the wrapper's own mask is the control: it has to exist to be carried.
            Assert.That(mask != null ? proxy.style.backgroundImage.value.vectorImage : null, Is.SameAs(mask));
        }

        [Test]
        public void Given_AnOutlineInsideARoundedClip_When_TheClipsRadiusChangesAlone_Then_TheProxyFollowsOnTheNextTick()
        {
            // Arrange — the clip's rect stays put, so only the radius it is read for moves.
            MountClippedOutline();
            var clip = _window.rootVisualElement.Q<VisualElement>("clip");
            clip.style.borderTopLeftRadius = 20f;
            ForcePanelUpdate(clip.panel);
            ForcePanelUpdate(clip.panel);
            var binding = RingOverlay.TryGet(_window.rootVisualElement.Q<VisualElement>("a"));

            // Act
            clip.style.borderTopLeftRadius = 0f;
            ForcePanelUpdate(clip.panel);
            var now = 0.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(clip.panel, () => now);
            now = 1.0;
            EditorPanelTestHelpers.DriveSchedulerOnce(clip.panel);
            ForcePanelUpdate(clip.panel);

            // Assert
            Assert.That(binding.OutlineOverlay.parent.resolvedStyle.borderTopLeftRadius, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AnOutlineInATreeMountedBelowAnotherElement_When_LaidOut_Then_TheMountContainerHostsIt()
        {
            // Arrange — the container belongs to the application, and may be taken out of the panel with the tree in it.
            var top = new VisualElement { name = "top" };
            var container = new VisualElement { name = "container" };
            top.Add(container);
            _window.rootVisualElement.Add(top);
            _mounted = V.Mount(container, V.Div(name: "a", className: "w-[40px] h-[40px] outline-2"));

            // Act
            ForcePanelUpdate(container.panel);
            ForcePanelUpdate(container.panel);

            // Assert
            Assert.That(RingOverlay.TryGet(container.Q<VisualElement>("a")).OutlineOverlay.parent, Is.SameAs(container));
        }

        [Test]
        public void Given_AnOutlineInsideAPortal_When_PlacedOnAnIdleTick_Then_NothingIsReHosted()
        {
            // Arrange — the portal's content is outside the tree's mount container, whose check must not apply to it.
            RuntimeStateProbe.ClearPortalRegistry();
            var target = new VisualElement { name = "target" };
            _window.rootVisualElement.Add(target);
            FiberPortalRegistry.Register("outline-portal-target", target);
            var container = new VisualElement { name = "container" };
            _window.rootVisualElement.Add(container);
            _mounted = V.Mount(container, V.Portal("outline-portal-target", children: new VNode[]
            {
                V.Div(name: "a", className: "w-[40px] h-[40px] outline-2"),
            }));
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
            ForcePanelUpdate(container.panel);
            ForcePanelUpdate(container.panel);
            var card = _window.rootVisualElement.Q<VisualElement>("a");
            var binding = RingOverlay.TryGet(card);
            System.Action place = () => OutlineHosting.Place(card, binding, force: false);
            place();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(place);

            // Assert — a re-host allocates; the canary is the instrument's own control.
            RuntimeStateProbe.ClearPortalRegistry();
            Assert.That(GCAllocationCanary.BlocksForKnownArray() > 0 && binding != null ? blocks : -1, Is.Zero);
        }

        [Test]
        public void Given_AnOutlineHostedAtAMountContainerBelowAnotherElement_When_PlacedOnAnIdleTick_Then_NothingIsReHosted()
        {
            // Arrange — the container is not a child of the panel's root, which the idle check once required.
            var top = new VisualElement { name = "top" };
            var container = new VisualElement { name = "container" };
            top.Add(container);
            _window.rootVisualElement.Add(top);
            _mounted = V.Mount(container, V.Div(name: "a", className: "w-[40px] h-[40px] outline-2"));
            ForcePanelUpdate(container.panel);
            ForcePanelUpdate(container.panel);
            var card = container.Q<VisualElement>("a");
            var binding = RingOverlay.TryGet(card);
            System.Action place = () => OutlineHosting.Place(card, binding, force: false);
            place();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(place);

            // Assert — a re-host allocates; the canary is the instrument's own control.
            Assert.That(GCAllocationCanary.BlocksForKnownArray() > 0 ? blocks : -1, Is.Zero);
        }

        [Test]
        public void Given_AnOutlineInsideAZStackedAncestor_When_LaidOut_Then_ThatAncestorHostsIt()
        {
            // Arrange — a z-index other than auto makes a stacking context of what the element holds.
            var outline = MountOutlineUnder("w-[100px] h-[60px] relative z-10");

            // Assert
            Assert.That(outline.parent.name, Is.EqualTo("holder"));
        }

        [Test]
        public void Given_AnOutlineInsideAFlexOrderedAncestor_When_LaidOut_Then_ThatAncestorDoesNotHostIt()
        {
            // Arrange — order-* moves an item in flex order and makes no stacking context.
            var outline = MountOutlineUnder("w-[100px] h-[60px] order-1");

            // Assert
            Assert.That(outline.parent.name, Is.Not.EqualTo("holder"));
        }

        private VisualElement MountOutlineUnder(string holderClass)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "wrap", className: "flex w-[200px] h-[100px]", children: new VNode[]
                {
                    V.Div(name: "holder", className: holderClass, children: new VNode[]
                    {
                        V.Div(name: "a", className: "w-[40px] h-[40px] outline-2"),
                    }),
                    V.Div(name: "after", className: "w-[40px] h-[40px]"),
                }));
            var holder = _window.rootVisualElement.Q<VisualElement>("holder");
            ForcePanelUpdate(holder.panel);
            ForcePanelUpdate(holder.panel);
            return RingOverlay.TryGet(_window.rootVisualElement.Q<VisualElement>("a")).OutlineOverlay;
        }

        [Test]
        public void Given_AnOutlineInsideAFadedAncestor_When_LaidOut_Then_ThatAncestorHostsIt()
        {
            // Arrange — an opacity below 1 composites what it holds, so the outline stays inside it to fade with it.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "wrap", className: "w-[200px] h-[100px]", children: new VNode[]
                {
                    V.Div(name: "faded", className: "w-[100px] h-[60px] opacity-50", children: new VNode[]
                    {
                        V.Div(name: "a", className: "w-[40px] h-[40px] outline-2"),
                    }),
                }));
            var faded = _window.rootVisualElement.Q<VisualElement>("faded");

            // Act
            ForcePanelUpdate(faded.panel);
            ForcePanelUpdate(faded.panel);
            var outline = RingOverlay.TryGet(_window.rootVisualElement.Q<VisualElement>("a")).OutlineOverlay;

            // Assert
            Assert.That(outline.parent.name, Is.EqualTo("faded"));
        }

        // An inherited shadow colour reaches the children inheriting it when it changes, though their own classes
        // do not.

        [Test]
        public void Given_AChildInheritingAShadowColour_When_TheParentsColourChanges_Then_ThatChildRepaints()
        {
            // Arrange
            var (parent, inherits, _, _) = MountInheritingPair();

            // Act — the parent's colour changes with nothing re-rendered.
            BoxShadowSilhouette.RecordDeclaredColours(parent, new[] { "shadow-lg", "shadow-[#00ff00]" });
            ForcePanelUpdate(parent.panel);

            // Assert
            Assert.That(inherits.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AChildInheritingAShadowColour_When_TheParentsColourChanges_Then_AnotherChildIsNotRepainted()
        {
            // Arrange
            var (parent, _, control, _) = MountInheritingPair();

            // Act
            BoxShadowSilhouette.RecordDeclaredColours(parent, new[] { "shadow-lg", "shadow-[#00ff00]" });
            ForcePanelUpdate(parent.panel);

            // Assert
            Assert.That(control.Count, Is.Zero);
        }

        private (VisualElement Parent, Counter Inherits, Counter Control, VisualElement Child) MountInheritingPair()
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "parent", className: "w-[200px] h-[100px] shadow-lg shadow-[#ff0000]", children: new VNode[]
                {
                    V.Div(name: "inherits", className: "w-[40px] h-[40px] bg-[#ffffff] shadow-lg shadow-inherit"),
                    V.Div(name: "control", className: "w-[40px] h-[40px] bg-[#ffffff] shadow-lg"),
                }));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            var inherits = new Counter();
            var control = new Counter();
            var child = _window.rootVisualElement.Q<VisualElement>("inherits");
            child.generateVisualContent += _ => inherits.Count++;
            _window.rootVisualElement.Q<VisualElement>("control").generateVisualContent += _ => control.Count++;
            ForcePanelUpdate(parent.panel);
            inherits.Count = 0;
            control.Count = 0;
            return (parent, inherits, control, child);
        }

        private sealed class Counter
        {
            public int Count;
        }

        // Which transforms the band follows. The band is a sibling, so the element's own transform reaches
        // it only through RingOverlay.SyncElementState: at layout, and on the per-frame tick for a transform that
        // changes without moving the layout box.

        // Two ringed cards stacked in one column, identical but for what the test does to the second.
        private (VisualElement Still, VisualElement Moved) MountCardPair(string movedClassName)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "wrap", className: "w-[300px] h-[200px]", children: new VNode[]
                {
                    V.Div(name: "still", className: "w-[100px] h-[40px] ring-4"),
                    V.Div(name: "moved", className: "w-[100px] h-[40px] ring-4 " + movedClassName),
                }));
            return (_window.rootVisualElement.Q<VisualElement>("still"),
                _window.rootVisualElement.Q<VisualElement>("moved"));
        }

        [Test]
        public void Given_ARingedElement_When_ATransformMovesIt_Then_TheBandMovesWithIt()
        {
            // Arrange
            var (still, moved) = MountCardPair("translate-x-8");

            // Act
            ForcePanelUpdate(still.panel);

            // Assert — the card's 32px shift against the still card is the translate taking effect, and -4
            // is the band still sitting exactly ring-4 outside the moved card. Rounded because .Within()
            // does not reach the members of a ValueTuple under Unity's NUnit; both are whole pixels.
            Assert.That((Mathf.Round(moved.worldBound.x - still.worldBound.x), Mathf.Round(BandOffsetX(moved))),
                Is.EqualTo((32f, -4f)));
        }

        [Test]
        public void Given_ARingedElement_When_ItsTransformChangesWithoutALayoutChange_Then_TheNextFrameCarriesTheBand()
        {
            // Arrange — laid out untransformed.
            var (still, moved) = MountCardPair(string.Empty);
            ForcePanelUpdate(still.panel);

            // Act — a translate written after layout, carried to the band by its per-frame tick. The clock is
            // installed first and advanced afterwards, which is what makes the tick due.
            moved.style.translate = new Translate(32f, 0f);
            ForcePanelUpdate(still.panel);
            var now = 0.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(still.panel, () => now);
            now = 1.0;
            EditorPanelTestHelpers.DriveSchedulerOnce(still.panel);
            ForcePanelUpdate(still.panel);

            // Assert
            Assert.That((Mathf.Round(moved.worldBound.x - still.worldBound.x), Mathf.Round(BandOffsetX(moved))),
                Is.EqualTo((32f, -4f)));
        }

        [Test]
        public void Given_ARingedElementScaledFromItsCorner_When_LaidOut_Then_TheBandScalesAboutTheSameCorner()
        {
            // Arrange — the pivot is the element's top-left, which sits ring-4 inside the band's box.
            var (still, moved) = MountCardPair("origin-top-left scale-150");

            // Act
            ForcePanelUpdate(still.panel);

            // Assert — a 150px-wide card is the scale taking effect about a pivot that keeps its top-left
            // corner, and the band's 4px margin scaled about that same point is 6px on both axes. Pivoting at
            // the band's own corner instead would leave the margin at 4.
            Assert.That((Mathf.Round(moved.worldBound.width), Mathf.Round(BandOffsetX(moved)),
                    Mathf.Round(BandOffsetY(moved))),
                Is.EqualTo((150f, -6f, -6f)));
        }

        // What the element's own subtree would carry besides a transform, which the band as a sibling gets
        // only from RingOverlay.SyncElementState.

        [Test]
        public void Given_ARingedElementAtHalfOpacity_When_LaidOut_Then_TheBandIsAtHalfOpacity()
        {
            // Arrange
            var (still, moved) = MountCardPair("opacity-50");

            // Act
            ForcePanelUpdate(still.panel);
            ForcePanelUpdate(still.panel);

            // Assert — the card's own 0.5 is the class taking effect.
            Assert.That((moved.resolvedStyle.opacity, BandFor(moved).resolvedStyle.opacity), Is.EqualTo((0.5f, 0.5f)));
        }

        [Test]
        public void Given_ARingNamingNoColour_When_LaidOut_Then_TheBandIsTheTextColour()
        {
            // Arrange
            var card = MountRinged("w-[40px] h-[40px] ring-2 text-[#ff0000]");

            // Act
            ForcePanelUpdate(card.panel);

            // Assert
            Assert.That(RingOverlay.TryGet(card).RingTint, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_AnInvisibleRingedElement_When_LaidOut_Then_TheBandIsHidden()
        {
            // Arrange
            var (still, moved) = MountCardPair("invisible");

            // Act
            ForcePanelUpdate(still.panel);
            ForcePanelUpdate(still.panel);

            // Assert
            Assert.That((moved.resolvedStyle.visibility, BandFor(moved).resolvedStyle.visibility),
                Is.EqualTo((Visibility.Hidden, Visibility.Hidden)));
        }

        [Test]
        public void Given_ARingedElementShown_When_ItTurnsHidden_Then_TheBandIsNotDisplayed()
        {
            // Arrange
            var (still, moved) = MountCardPair(string.Empty);
            ForcePanelUpdate(still.panel);
            var shownBefore = BandFor(moved).resolvedStyle.display;

            // Act — a layout pass and a per-frame tick both run, so the case holds whichever of the two
            // carries the change. The clock is installed first and advanced afterwards.
            moved.AddToClassList("hidden");
            ForcePanelUpdate(still.panel);
            var now = 0.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(still.panel, () => now);
            now = 1.0;
            EditorPanelTestHelpers.DriveSchedulerOnce(still.panel);
            ForcePanelUpdate(still.panel);

            // Assert
            Assert.That((shownBefore, moved.resolvedStyle.display, BandFor(moved).resolvedStyle.display),
                Is.EqualTo((DisplayStyle.Flex, DisplayStyle.None, DisplayStyle.None)));
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
            // the claim — it sits inside the host, so the engine carries it, where the element's own
            // transform in the cases above reaches it only through SyncElementState.
            var cardShift = Mathf.Round(movedCard.worldBound.x - stillCard.worldBound.x);
            var bandShift = Mathf.Round(RingBandOf(movedCard).worldBound.x - RingBandOf(stillCard).worldBound.x);
            Assert.That((cardShift, bandShift), Is.EqualTo((32f, 32f)));
        }
    }

    /// <summary>
    /// Specifies where a <c>ring-inset</c> band lives: inside its element, ahead of the element's children, so
    /// that it paints above the element's own background and under what the element holds, as an inset
    /// box-shadow does. The band is not a child as far as the reconciler is concerned, which these cases pin
    /// through the order it keeps beside rendered children.
    /// </summary>
    internal sealed class InsetRingBandTests : PanelTestBase
    {
        private static StateUpdater<bool> s_setShown;

        private static VisualElement InsetBandIn(VisualElement element)
        {
            for (var i = 0; i < element.childCount; i++)
            {
                if (element[i].ClassListContains(RingOverlay.InsetMarkerClass))
                {
                    return element[i];
                }
            }
            return null;
        }

        // The element's children by name, the band standing as "band".
        private static string ChildOrder(VisualElement element)
        {
            var names = new System.Collections.Generic.List<string>();
            for (var i = 0; i < element.childCount; i++)
            {
                names.Add(element[i].ClassListContains(RingOverlay.InsetMarkerClass) ? "band" : element[i].name);
            }
            return string.Join(",", names);
        }

        private VisualElement MountCard(VNode root)
        {
            _mounted = V.Mount(_window.rootVisualElement, root);
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(card.panel);
            ForcePanelUpdate(card.panel);
            return card;
        }

        [Test]
        public void Given_AnInsetRing_When_Mounted_Then_NoBandIsHostedBesideTheElement()
        {
            // Arrange / Act
            MountCard(V.Div(name: "card", className: "w-[100px] h-[40px] ring-2 ring-inset"));

            // Assert
            Assert.That(RingOverlayIn(_window.rootVisualElement), Is.Null);
        }

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

        [Test]
        public void Given_AnInsetRingOverAChild_When_Mounted_Then_TheBandIsAheadOfTheChild()
        {
            // Arrange / Act
            var card = MountCard(V.Div(name: "card", className: "w-[100px] h-[40px] ring-2 ring-inset",
                children: new[] { V.Div(name: "child", className: "w-[100px] h-[40px]") }));

            // Assert
            Assert.That(ChildOrder(card), Is.EqualTo("band,child"));
        }

        [Component]
        private static VNode RenderLateChild()
        {
            var (shown, setShown) = Hooks.UseState(false);
            s_setShown = setShown;
            return V.Div(name: "card", className: "w-[100px] h-[40px] ring-2 ring-inset",
                children: new[] { shown ? V.Div(name: "late", className: "w-[100px] h-[40px]") : null });
        }

        [Test]
        public void Given_AnInsetRingOverNoChild_When_AChildIsRendered_Then_TheBandStaysAheadOfIt()
        {
            // Arrange
            var card = MountCard(V.Component(RenderLateChild));

            // Act
            s_setShown.Invoke(true);
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert
            Assert.That(ChildOrder(card), Is.EqualTo("band,late"));
        }

        [Test]
        public void Given_AnInsetRingOnABorderedElement_When_LaidOut_Then_TheBandCoversThePaddingBox()
        {
            // Arrange / Act — a 2px border on each side of a 128x64 box leaves a 124x60 padding box.
            var card = MountCard(V.Div(name: "card", className: "w-[128px] h-[64px] border-[2px] ring-2 ring-inset"));

            // Assert
            var band = InsetBandIn(card);
            Assert.That((band?.resolvedStyle.width, band?.resolvedStyle.height), Is.EqualTo(((float?)124f, (float?)60f)));
        }

        [Test]
        public void Given_AnInsetRingOnARoundedBorderedElement_When_LaidOut_Then_TheBandRoundsToThePaddingBox()
        {
            // Arrange / Act — the padding box's corner is the element's 20px less its 2px border.
            var card = MountCard(V.Div(name: "card",
                className: "w-[128px] h-[64px] rounded-[20px] border-[2px] ring-2 ring-inset"));

            // Assert
            Assert.That(RingOverlay.TryGet(card).InsideRadii.TopLeft.x, Is.EqualTo(18f));
        }

        [Test]
        public void Given_AnInsetRingOnARoundedElement_When_OnlyItsBorderWidthChanges_Then_TheBandRadiusFollowsOnTheNextTick()
        {
            // Arrange — 20px corners behind a 2px border, then the border grows to 6px: no geometry event follows.
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, UseFrameFakeClockHost.ReadFakeClock);
            var card = MountCard(V.Div(name: "card",
                className: "w-[128px] h-[64px] rounded-[20px] border-[2px] ring-2 ring-inset"));
            card.style.borderTopWidth = 6f;
            card.style.borderRightWidth = 6f;
            card.style.borderBottomWidth = 6f;
            card.style.borderLeftWidth = 6f;
            ForcePanelUpdate(card.panel);

            // Act
            UseFrameFakeClockHost.Ms += 100;
            EditorPanelTestHelpers.DriveSchedulerOnce(card.panel);
            ForcePanelUpdate(card.panel);

            // Assert
            Assert.That(RingOverlay.TryGet(card).InsideRadii.TopLeft.x, Is.EqualTo(14f));
        }

        [Test]
        public void Given_AnInsetRingedElementWithAnOpacity_When_LaidOut_Then_TheBandIsNotFadedAgain()
        {
            // Arrange — the band is the element's child, so the element's opacity reaches it once already.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "card", className: "w-[100px] h-[40px] ring-2 ring-inset"));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            card.style.opacity = 0.5f;

            // Act
            ForcePanelUpdate(card.panel);
            ForcePanelUpdate(card.panel);

            // Assert
            Assert.That(InsetBandIn(card)?.resolvedStyle.opacity, Is.EqualTo((float?)1f));
        }
    }
}
