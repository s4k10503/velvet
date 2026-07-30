using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the shape/outline utility-className → reconciler structure contract, covering the
    /// <c>clip-path-*</c> stencil-mask wrapper and the <c>ring-*</c> / <c>outline-*</c> band together,
    /// since a clip suppresses a ring and that precedence is part of the contract.
    /// <list type="bullet">
    /// <item><c>clip-path-*</c>: UI Toolkit (6000.3) has no USS <c>clip-path</c>; its supported
    /// arbitrary-shape mask is an overflow-hidden element with a vector background, so a
    /// <c>clip-path-[…]</c> class makes Velvet wrap the element in a masking wrapper
    /// (<see cref="FiberWrapperElementAppliers.ClipPathWrapperClass"/>) that carries the baked shape. It is
    /// the only remaining structural wrapper.</item>
    /// <item><c>ring-*</c> / <c>outline-*</c>: UI Toolkit (6000.3) has no CSS box-shadow / outline either,
    /// so Velvet paints the band on a native-border overlay hosted as a reconciler-invisible SIBLING of the
    /// ringed element (<see cref="RingOverlay"/>) — no GPU shader, unlike the soft drop shadow, and nothing
    /// added to the element's own slot. Width / color come from the utility scale; the corner radius follows
    /// the element's rounded-*.</item>
    /// <item>An active clip suppresses both the ring and the drop-shadow paint (CSS clip-path clips an
    /// outline and a box-shadow alike). Neither of those two is a wrapper, so they compose freely with each
    /// other and with a user <c>wrapElement</c>.</item>
    /// <item>Removing the class (or unmounting) destroys any baked resource (the VectorImage for clip),
    /// removes the wrapper, and takes the ring overlay out of the parent, with no residue.</item>
    /// </list>
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathWrapTests
    {
        private const string Triangle = "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]";

        private static VisualElement Wrapper(VisualElement root) => root[0];

        // The ring band is a sibling of the ringed element, not a child of a wrapper, so it is found by its
        // marker rather than at a fixed index.
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

        private static void Mount(ReconcilerScope scope, VNode[] tree)
            => scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

        // Creation

        [Test]
        public void Given_AClipClass_When_Reconciled_Then_ElementIsWrappedInAClipWrapper()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert
            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.True);
        }

        [Test]
        public void Given_AClipClass_When_Reconciled_Then_WrapperHidesOverflow()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert: overflow:hidden is half of the stencil-mask combination (vector bg + hidden).
            Assert.That(Wrapper(scope.Root).style.overflow.value, Is.EqualTo(Overflow.Hidden));
        }

        [Test]
        public void Given_AClipClass_When_Reconciled_Then_TheInnerElementIsTheWrappersChild()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert
            Assert.That(Wrapper(scope.Root)[0].name, Is.EqualTo("card"));
        }

        [Test]
        public void Given_AClipClass_When_Reconciled_Then_ABindingIsTracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_NoClipClass_When_Reconciled_Then_ElementIsNotWrapped()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "rounded-2xl", name: "plain") });

            // Assert
            Assert.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.False);
        }

        [Test]
        public void Given_ClipPathNone_When_Reconciled_Then_ElementIsNotWrapped()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "clip-path-none", name: "plain") });

            // Assert
            Assert.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.False);
        }

        [Test]
        public void Given_ClipOnMotion_When_Reconciled_Then_NoClipWrapperIsCreated()
        {
            // Arrange — a Motion carrying a clip-path utility. Motion does not auto-wrap: a structural
            // wrapper would become the AnimatePresence enter/exit anchor while the transition stays on the
            // inner Motion.
            using var scope = new ReconcilerScope();
            LogAssert.Expect(LogType.Warning, new Regex(@"clip-path-\* utility on a Motion is ignored"));

            // Act
            Mount(scope, new VNode[] { V.Motion(Triangle, key: "m") });

            // Assert
            Assert.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.False);
        }

        // Clip suppresses the shadow paint (CSS clip-path clips the box-shadow too)

        [Test]
        public void Given_ClipAndShadowClasses_When_Reconciled_Then_TheClipWrapperWins()
        {
            // Arrange: the clip is a structural wrapper; the shadow is a paint suppressed while clipped.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: $"shadow-lg {Triangle}", name: "card") });

            // Assert
            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.True);
        }

        [Test]
        public void Given_ClipAndShadowClasses_When_Reconciled_Then_NoShadowPaintIsAttached()
        {
            // Arrange: CSS clip-path clips the box-shadow too, so the shadow paint self-suppresses on the clip.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: $"shadow-lg {Triangle}", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AShadowedElement_When_ClipClassAddedByPatch_Then_TheShadowPaintIsDetached()
        {
            // Arrange: shadow paint first, then a patch adds the clip — the clip clips the box-shadow, so the
            // shadow patch (running after the clip patch, seeing clipActive) detaches the paint.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "shadow-lg", name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(1));

            // Act
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: $"shadow-lg {Triangle}", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AClippedElement_When_ClipReplacedByShadowOnPatch_Then_TheShadowPaintTakesOver()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(1));

            // Act: the clip class goes away and a shadow class appears in the same render.
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: "shadow-lg", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(1));
        }

        // Patch: spec change

        [Test]
        public void Given_AClippedElement_When_ShapeChangedByPatch_Then_TheBindingSpecFollows()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);

            // Act: triangle → circle on the same element (linear patch, no key change).
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: "clip-path-[circle(50%)]", name: "card") });

            // Assert
            var inner = Wrapper(scope.Root)[0];
            Assert.That(scope.Reconciler.Context.ClipPathBindings[inner].Spec.Kind,
                Is.EqualTo(ClipPathKind.Circle));
        }

        // Patch: class addition / removal

        [Test]
        public void Given_APlainElement_When_ClipClassAddedByPatch_Then_ElementIsWrappedInPlace()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "", name: "card") };
            Mount(scope, before);
            Assume.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.False);

            // Act
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert
            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.True);
        }

        [Test]
        public void Given_AClippedElement_When_ClipClassRemovedByPatch_Then_WrapperIsRemoved()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            Assume.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.True);

            // Act
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: "", name: "card") });

            // Assert: the inner element took the wrapper's slot; no wrapper residue.
            Assert.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.False);
        }

        [Test]
        public void Given_AClippedElement_When_ClipClassRemovedByPatch_Then_BindingIsUntracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(1));

            // Act
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: "", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(0));
        }

        // Patch: steady state

        [Test]
        public void Given_AClippedElement_When_RepatchedWithTheSameClass_Then_TheSpecInstanceIsReused()
        {
            // Arrange: the patch fast path compares the winning clip token against the live binding's
            // Spec.Source before parsing — an unchanged class list must not rebuild the spec.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            var inner = Wrapper(scope.Root)[0];
            var specBefore = scope.Reconciler.Context.ClipPathBindings[inner].Spec;

            // Act: a re-render carries the identical clip class.
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert
            Assert.That(ReferenceEquals(specBefore, scope.Reconciler.Context.ClipPathBindings[inner].Spec), Is.True);
        }

        // Reconciler disposal

        [Test]
        public void Given_AClippedElement_When_ReconcilerDisposed_Then_ClipBindingsAreReleased()
        {
            // Arrange — a still-mounted clipped element. Root disposal never routes live elements
            // through FiberElementCleaner, so Dispose itself must release the clip bindings (and
            // destroy any baked VectorImage), symmetric with its ShadowBindings teardown.
            var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Triangle, name: "card") });
            Assume.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(1));

            // Act
            scope.Dispose();

            // Assert
            Assert.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(0));
        }

        // Unmount

        [Test]
        public void Given_AClippedElement_When_Unmounted_Then_BindingIsUntracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(1));

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, Array.Empty<VNode>());

            // Assert
            Assert.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AClippedElement_When_Unmounted_Then_WrapperToInnerMapIsEmpty()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.WrapperToInnerMap.Count, Is.EqualTo(1));

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, Array.Empty<VNode>());

            // Assert
            Assert.That(scope.Reconciler.Context.WrapperToInnerMap.Count, Is.EqualTo(0));
        }

        // User wrapElement opt-out

        [Test]
        public void Given_AUserWrapElementWithClipClass_When_Patched_Then_NotDoubleWrapped()
        {
            // Arrange: an element with BOTH a user wrapElement and a clip-path class. The create path
            // returns the user's wrapper and opts out of the className clip (no ClipPathBinding).
            using var scope = new ReconcilerScope();
            Func<VisualElement, VisualElement> wrap = el =>
            {
                var w = new VisualElement();
                w.Add(el);
                return w;
            };
            var before = new VNode[] { V.Button(className: Triangle, wrapElement: wrap, key: "b") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(0));

            // Act: a re-render patches the same element.
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Button(className: Triangle, wrapElement: wrap, key: "b") });

            // Assert: patch must honor the opt-out and NOT stack a clip wrapper on the user wrapper.
            Assert.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(0));
        }

        // Ring / outline overlay

        [Test]
        public void Given_Ring2_When_Reconciled_Then_TheElementItselfStillOccupiesItsSlot()
        {
            // The property the sibling-overlay model exists for: a ring adds nothing to the element's own
            // slot, so every layout relationship it has with its parent is the one it declared.
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "ring-2", name: "card") });

            Assert.That(scope.Root[0].name, Is.EqualTo("card"));
        }

        [Test]
        public void Given_Ring2_When_Reconciled_Then_TheOverlayIsHostedBesideTheElement()
        {
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "ring-2", name: "card") });

            Assert.That(RingOverlayIn(scope.Root), Is.Not.Null);
        }

        [Test]
        public void Given_ARingedElement_When_Reconciled_Then_TheOverlayIsNotCountedAsARenderedChild()
        {
            // The overlay rides SilhouetteBoundsSpacer's reconciler-invisible-child predicate, which is what
            // keeps the child reconciler's slot indexing, the structural variants and [&>*]: from seeing it.
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "ring-2", name: "card") });

            Assert.That(SilhouetteBoundsSpacer.NonSpacerChildCount(scope.Root), Is.EqualTo(1));
        }

        [Test]
        public void Given_Ring2_When_Reconciled_Then_OverlayCarriesTheRingWidth()
        {
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "ring-2", name: "card") });

            Assert.That(RingOverlayIn(scope.Root).style.borderTopWidth.value, Is.EqualTo(2f));
        }

        [Test]
        public void Given_BareRing_When_Reconciled_Then_OverlayUsesDefaultBlueRingColor()
        {
            using var scope = new ReconcilerScope();
            // Tailwind's default ring color is blue-500 at 0.5 alpha (the palette token resolves opaque).
            VelvetPalette.TryResolveColorToken("blue-500", out var blue);
            blue.a = 0.5f;

            Mount(scope, new VNode[] { V.Div(className: "ring", name: "card") });

            Assert.That(RingOverlayIn(scope.Root).style.borderTopColor.value, Is.EqualTo(blue));
        }

        [Test]
        public void Given_RingWithColor_When_Reconciled_Then_OverlayUsesThatColor()
        {
            using var scope = new ReconcilerScope();
            VelvetPalette.TryResolveColorToken("red-500", out var red);

            Mount(scope, new VNode[] { V.Div(className: "ring-2 ring-red-500", name: "card") });

            Assert.That(RingOverlayIn(scope.Root).style.borderRightColor.value, Is.EqualTo(red));
        }

        [Test]
        public void Given_Outline2_When_Reconciled_Then_TheOverlayIsHosted()
        {
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "outline-2", name: "card") });

            Assert.That(RingOverlayIn(scope.Root), Is.Not.Null);
        }

        [Test]
        public void Given_ARingedElement_When_RingClassRemovedByPatch_Then_TheOverlayLeavesTheParent()
        {
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "ring-2", name: "card") };
            Mount(scope, before);
            var hostedWhileRinged = RingOverlayIn(scope.Root) != null;

            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Div(className: "", name: "card") });

            // Hosted, then gone — one comparison, because asserting only "gone" would pass equally against
            // a ring that was never hosted at all.
            Assert.That((hostedWhileRinged, RingOverlayIn(scope.Root) != null), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ShadowAndRingTogether_When_Reconciled_Then_BothLayersAttach()
        {
            // Neither layer is a wrapper any more, so they compose with no precedence between them at all.
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "shadow-lg ring-2", name: "card") });

            Assert.That((RingOverlayIn(scope.Root) != null, DropShadowSilhouette.TryGet(scope.Root[0]) != null),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_RingOnMotion_When_Reconciled_Then_NoRingBindingIsCreated()
        {
            // A Motion stands down: the band is placed from the element's laid-out box, which the Motion's
            // own transform does not move (see FiberNodeFactory.WarnIgnoredMotionUtilities). Warned about
            // rather than silently dropped, like the shadow-* / clip-path-* / z-* gates on a Motion.
            using var scope = new ReconcilerScope();
            LogAssert.Expect(LogType.Warning, new Regex(@"ring-\*.*on a Motion is ignored"));

            Mount(scope, new VNode[] { V.Motion("ring-2", key: "m") });

            Assert.That(scope.Reconciler.Context.RingBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_APlainElement_When_RingClassAddedByPatch_Then_TheOverlayIsHosted()
        {
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "", name: "card") };
            Mount(scope, before);
            var hostedWhileRingless = RingOverlayIn(scope.Root) != null;

            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Div(className: "ring-2", name: "card") });

            Assert.That((hostedWhileRingless, RingOverlayIn(scope.Root) != null), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ARingedElement_When_ShadowClassAddedByPatch_Then_TheRingOverlayIsKept()
        {
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "ring-2", name: "card") };
            Mount(scope, before);
            var hostedBeforeShadow = RingOverlayIn(scope.Root) != null;

            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: "ring-2 shadow-lg", name: "card") });

            // Hosted before AND after: "hosted after" alone would also hold if the ring had only just been
            // created by this patch, which is not what the case is about.
            Assert.That((hostedBeforeShadow, RingOverlayIn(scope.Root) != null), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ARingedElement_When_ClipClassAddedByPatch_Then_TheRingIsSuppressed()
        {
            // clip-path clips an outline in CSS, so the clip suppresses the ring — now a plain suppression
            // gate rather than a contest for the one structural wrapper slot.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "ring-2", name: "card") };
            Mount(scope, before);
            var boundBeforeClip = scope.Reconciler.Context.RingBindings.Count;

            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(className: "ring-2 " + Triangle, name: "card"),
            });

            Assert.That((boundBeforeClip, scope.Reconciler.Context.RingBindings.Count), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_TwoRingedSiblings_When_Reconciled_Then_EachBandSitsDirectlyAfterItsOwnElement()
        {
            // Adjacent child order is what gives a band its own element's paint position rather than a
            // position above every later sibling. It is a NECESSARY condition for that, not a sufficient
            // one — whether UI Toolkit paints an absolutely-positioned sibling in child order against an
            // in-flow one is a separate, unmeasured question — so this pins the structure only.
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[]
            {
                V.Div(className: "ring-2", name: "a", key: "a"),
                V.Div(className: "ring-2", name: "b", key: "b"),
            });

            var order = new List<string>();
            for (var i = 0; i < scope.Root.childCount; i++)
            {
                order.Add(scope.Root[i].ClassListContains(RingOverlay.MarkerClass)
                    ? "band"
                    : scope.Root[i].name);
            }
            Assert.That(order, Is.EqualTo(new[] { "a", "band", "b", "band" }));
        }

        [Test]
        public void Given_TwoRingedSiblings_When_Reconciled_Then_NeitherBandIsCountedAsARenderedChild()
        {
            // LogicalChildSlots.Count, not the superseded physical NonSpacerChildCount: the bands sit
            // ADJACENT to their elements rather than in a trailing run — which is what gives each band its
            // own element's paint position — and a count that only trims a trailing run reports 3 here.
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[]
            {
                V.Div(className: "ring-2", name: "a", key: "a"),
                V.Div(className: "ring-2", name: "b", key: "b"),
            });

            Assert.That(LogicalChildSlots.Count(scope.Root), Is.EqualTo(2));
        }

        [Test]
        public void Given_ThreeRingedSiblings_When_TheMiddleOneIsRemoved_Then_TheSurvivorsKeepTheirSlotsAndBands()
        {
            // A keyed removal is where a miscounted invisible child surfaces: the reconciler addresses slots
            // [0, NonSpacerChildCount), so a band counted as a rendered child mis-pairs the survivors —
            // measured, an adjacent-placement build left slot 1 holding "c" instead of a band. Pins the
            // surviving children AND the band count together, since either can be right while the other is not.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: "ring-2", name: "a", key: "a"),
                V.Div(className: "ring-2", name: "b", key: "b"),
                V.Div(className: "ring-2", name: "c", key: "c"),
            };
            Mount(scope, before);

            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(className: "ring-2", name: "a", key: "a"),
                V.Div(className: "ring-2", name: "c", key: "c"),
            });

            var rendered = new List<string>();
            var bands = 0;
            for (var i = 0; i < scope.Root.childCount; i++)
            {
                if (scope.Root[i].ClassListContains(RingOverlay.MarkerClass))
                {
                    bands++;
                }
                else
                {
                    rendered.Add(scope.Root[i].name);
                }
            }
            Assert.That((string.Join(",", rendered), bands), Is.EqualTo(("a,c", 2)));
        }

        [Test]
        public void Given_AZManagedRingedElement_When_Reconciled_Then_TheBandFollowsItIntoItsLayerContainer()
        {
            // A z-* absolute element is relocated out of its ordinary slot into a layer container, leaving a
            // placeholder behind. The band is hosted on the element's parent, so it has to follow — placement
            // is drained at the reconcile boundary, AFTER the relocation, and re-derives the host from the
            // element rather than caching the parent it had at create time.
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "absolute z-10 ring-2", name: "card") });

            var card = scope.Root.Q<VisualElement>("card");
            Assert.That(RingOverlayIn(card.parent), Is.Not.Null);
        }

        [Test]
        public void Given_ARingedElement_When_Unmounted_Then_NoRingBindingResidueRemains()
        {
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "ring-2", name: "card") };
            Mount(scope, tree);
            var boundWhileMounted = scope.Reconciler.Context.RingBindings.Count;

            scope.Reconciler.Reconcile(scope.Root, tree, Array.Empty<VNode>());

            Assert.That((boundWhileMounted, scope.Reconciler.Context.RingBindings.Count), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_ARingedElement_When_Unmounted_Then_TheOverlayLeavesTheParent()
        {
            // The overlay lives in the PARENT, not in the unmounted element's own subtree, so it does not
            // leave with it — a teardown that only dropped the binding would strand a live band.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "ring-2", name: "card") };
            Mount(scope, tree);
            var hostedWhileMounted = RingOverlayIn(scope.Root) != null;

            scope.Reconciler.Reconcile(scope.Root, tree, Array.Empty<VNode>());

            Assert.That((hostedWhileMounted, RingOverlayIn(scope.Root) != null), Is.EqualTo((true, false)));
        }
    }
}
