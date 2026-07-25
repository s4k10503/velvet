using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>clip-path-[…]</c> utility parser (<see cref="StyleClipPathClass"/>): the
    /// arbitrary-value convention (underscores stand in for spaces), the CSS
    /// <c>&lt;basic-shape&gt;</c> grammar subset (<c>polygon</c> / <c>circle</c> / <c>ellipse</c> /
    /// <c>inset</c>), cascade behavior (last recognized class wins; <c>clip-path-none</c> overrides),
    /// and rejection of unparseable values; and <see cref="ClipPathVectorImageBaker"/>, which bakes a
    /// parsed spec into a runtime <c>VectorImage</c> whose analytic bounds place the shape where the CSS
    /// says, resolving percentages against the element box (circle radius against the CSS reference-box
    /// diagonal), refusing to bake degenerate shapes (the geometry sync then hides the subtree — CSS clips
    /// everything for an empty shape), and keeping the saved image's tight bounds in agreement with the
    /// analytic bounds the background is positioned by. GWT, one assert per case; the fixture owns and
    /// destroys the baked image in TearDown.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathClassParityTests
    {
        private VectorImage _image;
        private Rect _bounds;

        [TearDown]
        public void TearDown()
        {
            ClipPathVectorImageBaker.DestroyImage(_image);
            _image = null;
        }

        // Parses the CSS shape and bakes it at (width, height), storing the image on the fixture
        // for TearDown.
        private VectorImage Bake(string css, float width, float height)
        {
            var ok = StyleClipPathClass.TryParseShape(css, out var spec);
            Assume.That(ok, Is.True);
            _image = ClipPathVectorImageBaker.Bake(spec, width, height, out _bounds);
            return _image;
        }

        // VectorImage.size is an internal field on this editor version (exposed publicly in later Unity), so
        // read it reflectively — the bake contract still needs the saved image's tight extent to compare.
        private static Vector2 ImageSize(VectorImage image)
        {
            var field = typeof(VectorImage).GetField("size",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, "VectorImage must expose a 'size' field");
            return (Vector2)field.GetValue(image);
        }

        // Gates

        [Test]
        public void Given_NoClipClass_When_Gated_Then_HasClipPathClassIsFalse()
        {
            // Arrange
            var classes = new[] { "w-full", "rounded-lg", "shadow-lg" };

            // Act / Assert
            Assert.That(StyleClipPathClass.HasClipPathClass(classes), Is.False);
        }

        [Test]
        public void Given_AClipClass_When_Gated_Then_HasClipPathClassIsTrue()
        {
            // Arrange
            var classes = new[] { "w-full", "clip-path-[circle(50%)]" };

            // Act / Assert
            Assert.That(StyleClipPathClass.HasClipPathClass(classes), Is.True);
        }

        [Test]
        public void Given_ABaseClip_When_WrapGateChecked_Then_WantsWrapper()
        {
            Assert.That(StyleClipPathClass.WantsClipWrapper(new[] { "clip-path-[circle(50%)]" }), Is.True);
        }

        [Test]
        public void Given_OnlyClipPathNone_When_WrapGateChecked_Then_DoesNotWantWrapper()
        {
            // clip-path-none resolves to no clip, so it must not force a wrapper by itself.
            Assert.That(StyleClipPathClass.WantsClipWrapper(new[] { "clip-path-none" }), Is.False);
        }

        [Test]
        public void Given_AHoverClip_When_WrapGateChecked_Then_WantsWrapper()
        {
            // A variant clip needs the wrapper up-front (so the hover shape can light up without wrap-on-event).
            Assert.That(StyleClipPathClass.WantsClipWrapper(new[] { "hover:clip-path-[circle(50%)]" }), Is.True);
        }

        [Test]
        public void Given_OnlyHoverClipPathNone_When_WrapGateChecked_Then_DoesNotWantWrapper()
        {
            // A clip-path-none variant payload only CLEARS a clip; with no active clip anywhere, no wrapper.
            Assert.That(StyleClipPathClass.WantsClipWrapper(new[] { "hover:clip-path-none" }), Is.False);
        }

        [Test]
        public void Given_AStackedHoverClip_When_WrapGateChecked_Then_WantsWrapper()
        {
            // A STACKED variant (dark:hover:clip-path-[…]) must be peeled to the leaf clip, else it would
            // silently never wrap (and so never clip).
            Assert.That(StyleClipPathClass.WantsClipWrapper(new[] { "dark:hover:clip-path-[circle(50%)]" }), Is.True);
        }

        // polygon()

        [Test]
        public void Given_ATrianglePolygon_When_Extracted_Then_KindIsPolygon()
        {
            // Arrange
            var classes = new[] { "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec.Kind == ClipPathKind.Polygon, Is.True);
        }

        [Test]
        public void Given_ATrianglePolygon_When_Extracted_Then_ThreePointPairsAreParsed()
        {
            // Arrange
            var classes = new[] { "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert: x,y interleaved ⇒ 6 entries for 3 vertices.
            Assert.That(spec.PolygonPoints.Length, Is.EqualTo(6));
        }

        [Test]
        public void Given_UnderscoreSeparatedValues_When_Extracted_Then_PercentValueIsParsed()
        {
            // Arrange: underscores stand in for the spaces of `polygon(50% 0%, …)` (arbitrary-value convention).
            var classes = new[] { "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert: first vertex x is 50%.
            Assert.That(spec.PolygonPoints[0].IsPercent && spec.PolygonPoints[0].Value == 50f, Is.True);
        }

        [Test]
        public void Given_AnEvenOddPolygon_When_Extracted_Then_FillRuleIsOddEven()
        {
            // Arrange
            var classes = new[] { "clip-path-[polygon(evenodd,50%_0%,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.FillRule, Is.EqualTo(FillRule.OddEven));
        }

        [Test]
        public void Given_ATwoPointPolygon_When_Extracted_Then_NotRecognized()
        {
            // Arrange: CSS polygon() needs at least 3 vertices.
            var classes = new[] { "clip-path-[polygon(0%_0%,100%_100%)]" };

            // Act / Assert
            Assert.That(StyleClipPathClass.TryExtract(classes, out _), Is.False);
        }

        // circle()

        [Test]
        public void Given_ACircleWithoutRadius_When_Extracted_Then_ExtentDefaultsToClosestSide()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle()]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.RadiusXExtent, Is.EqualTo(ClipPathExtent.ClosestSide));
        }

        [Test]
        public void Given_ACircleWithPxRadius_When_Extracted_Then_RadiusIsLengthInPx()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle(40px)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.RadiusXExtent == ClipPathExtent.Length
                && !spec.RadiusX.IsPercent && spec.RadiusX.Value == 40f, Is.True);
        }

        [Test]
        public void Given_ACircleAtRightBottom_When_Extracted_Then_CenterFollowsKeywords()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle(50%_at_right_bottom)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert: right ⇒ x 100%, bottom ⇒ y 100%.
            Assert.That(spec.CenterX.Value == 100f && spec.CenterY.Value == 100f, Is.True);
        }

        [Test]
        public void Given_ACircleWithoutPosition_When_Extracted_Then_CenterDefaultsTo50Percent()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle(50%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.CenterX.IsPercent && spec.CenterX.Value == 50f, Is.True);
        }

        // ellipse()

        [Test]
        public void Given_AnEllipseWithRadii_When_Extracted_Then_RadiiResolvePerAxis()
        {
            // Arrange
            var classes = new[] { "clip-path-[ellipse(50%_35%_at_50%_50%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.RadiusX.Value == 50f && spec.RadiusY.Value == 35f, Is.True);
        }

        // inset()

        [Test]
        public void Given_ATwoValueInset_When_Extracted_Then_ShorthandExpandsLikeCss()
        {
            // Arrange: inset(10px 20%) ⇒ top/bottom 10px, right/left 20%.
            var classes = new[] { "clip-path-[inset(10px_20%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.InsetLeft.IsPercent && spec.InsetLeft.Value == 20f
                && !spec.InsetBottom.IsPercent && spec.InsetBottom.Value == 10f, Is.True);
        }

        [Test]
        public void Given_AnInsetWithRound_When_Extracted_Then_CornerRadiiAreExpanded()
        {
            // Arrange: round 8px 16px ⇒ tl/br 8, tr/bl 16.
            var classes = new[] { "clip-path-[inset(0px_round_8px_16px)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.CornerRadii[0].Value == 8f && spec.CornerRadii[1].Value == 16f
                && spec.CornerRadii[2].Value == 8f && spec.CornerRadii[3].Value == 16f, Is.True);
        }

        [Test]
        public void Given_AnInsetWithoutRound_When_Extracted_Then_CornerRadiiAreNull()
        {
            // Arrange
            var classes = new[] { "clip-path-[inset(10px)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.CornerRadii, Is.Null);
        }

        // Stretch invariance (the geometry sync's rescale-instead-of-rebake fast path)

        [Test]
        public void Given_AnAllPercentPolygon_When_Extracted_Then_ItIsStretchInvariant()
        {
            // Arrange
            var classes = new[] { "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.StretchInvariant, Is.True);
        }

        [Test]
        public void Given_APolygonWithAPixelCoordinate_When_Extracted_Then_ItIsNotStretchInvariant()
        {
            // Arrange: one px coordinate pins the shape to absolute pixels.
            var classes = new[] { "clip-path-[polygon(50%_0px,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.StretchInvariant, Is.False);
        }

        [Test]
        public void Given_ACircle_When_Extracted_Then_ItIsNeverStretchInvariant()
        {
            // Arrange: circle() % radii resolve against the diagonal — not per-axis linear.
            var classes = new[] { "clip-path-[circle(50%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.StretchInvariant, Is.False);
        }

        // Cascade

        [Test]
        public void Given_TwoClipClasses_When_Extracted_Then_TheLastOneWins()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle(50%)]", "clip-path-[inset(10px)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.Kind, Is.EqualTo(ClipPathKind.Inset));
        }

        [Test]
        public void Given_AClipFollowedByNone_When_Extracted_Then_NoClipIsWanted()
        {
            // Arrange: clip-path-none overrides the earlier clip in the cascade.
            var classes = new[] { "clip-path-[circle(50%)]", "clip-path-none" };

            // Act / Assert
            Assert.That(StyleClipPathClass.TryExtract(classes, out _), Is.False);
        }

        // Rejection

        [Test]
        public void Given_AnUnknownShapeFunction_When_Extracted_Then_NotRecognized()
        {
            // Arrange: path() is not in the supported subset.
            var classes = new[] { "clip-path-[path(M0_0L10_10)]" };

            // Act / Assert
            Assert.That(StyleClipPathClass.TryExtract(classes, out _), Is.False);
        }

        [Test]
        public void Given_AMalformedValue_When_Extracted_Then_NotRecognized()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle(abc)]" };

            // Act / Assert
            Assert.That(StyleClipPathClass.TryExtract(classes, out _), Is.False);
        }

        // Baking

        [Test]
        public void Given_SameShapeBakedAtManySizes_When_Cached_Then_OnlyOneImageIsRetained()
        {
            // The per-binding cache is keyed by SHAPE, so a non-stretch-invariant clip animated through many
            // sizes keeps ONE image (the latest) — not one per size, which would leak until teardown.
            StyleClipPathClass.TryExtract(new[] { "clip-path-[circle(50px)]" }, out var spec);
            var binding = new ClipPathBinding(new VisualElement());
            binding.GetOrBake(spec, 100f, 100f, out _, out _);
            binding.GetOrBake(spec, 150f, 150f, out _, out _);
            binding.GetOrBake(spec, 200f, 200f, out _, out _);

            var cache = (System.Collections.IDictionary)typeof(ClipPathBinding)
                .GetField("_bakeCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(binding);
            var count = cache.Count;
            binding.DisposeImage(); // destroy the retained image so the test leaks nothing

            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ATrianglePolygon_When_Baked_Then_AnImageIsProduced()
        {
            // Arrange / Act
            var image = Bake("polygon(50% 0%, 100% 100%, 0% 100%)", 200f, 100f);

            // Assert
            Assert.That(image, Is.Not.Null);
        }

        [Test]
        public void Given_ATrianglePolygon_When_Baked_Then_BoundsSpanTheFullBox()
        {
            // Arrange: the triangle touches all four edges of a 200x100 box.
            // Act
            Bake("polygon(50% 0%, 100% 100%, 0% 100%)", 200f, 100f);

            // Assert
            Assert.That(_bounds, Is.EqualTo(new Rect(0f, 0f, 200f, 100f)));
        }

        [Test]
        public void Given_ABakedShape_When_Saved_Then_TightImageSizeMatchesAnalyticBounds()
        {
            // Arrange: the geometry sync positions the background by the ANALYTIC path bounds, which is
            // only correct while SaveToVectorImage's tight bounds agree with them. Guard the contract.
            // Act
            var image = Bake("polygon(50% 0%, 100% 100%, 0% 100%)", 200f, 100f);
            Assume.That(image, Is.Not.Null);

            // Assert: tessellation may add a sub-pixel AA fringe; anything larger means misplacement.
            var size = ImageSize(image);
            Assert.That(Mathf.Abs(size.x - _bounds.width) < 1.5f
                && Mathf.Abs(size.y - _bounds.height) < 1.5f, Is.True);
        }

        [Test]
        public void Given_ACircleWithClosestSide_When_BakedInAWideBox_Then_RadiusIsHalfTheShortSide()
        {
            // Arrange: closest-side from the center of a 200x100 box ⇒ r = 50.
            // Act
            Bake("circle()", 200f, 100f);

            // Assert: bounds = center ± r ⇒ (50, 0, 100, 100).
            Assert.That(_bounds, Is.EqualTo(new Rect(50f, 0f, 100f, 100f)));
        }

        [Test]
        public void Given_ACircleWithPercentRadius_When_Baked_Then_RadiusResolvesAgainstTheCssDiagonal()
        {
            // Arrange: CSS circle() % radius basis is sqrt(w² + h²) / sqrt(2); for 300x400 that is
            // 500 / 1.41421… ≈ 353.55, so 50% ⇒ r ≈ 176.78.
            // Act
            Bake("circle(50%)", 300f, 400f);

            // Assert
            Assert.That(_bounds.width / 2f, Is.EqualTo(176.7767f).Within(0.01f));
        }

        [Test]
        public void Given_AnInsetWithRound_When_Baked_Then_BoundsAreTheInsetBox()
        {
            // Arrange / Act
            Bake("inset(10px 20px round 8px)", 200f, 100f);

            // Assert
            Assert.That(_bounds, Is.EqualTo(new Rect(20f, 10f, 160f, 80f)));
        }

        [Test]
        public void Given_AnInsetWhoseEdgesCross_When_Baked_Then_NoImageIsProduced()
        {
            // Arrange: 60% from both left and right leaves a zero-area box — CSS proportionally
            // reduces the offsets to an EMPTY shape (the element renders nothing); the baker
            // reports it by refusing to bake, and the geometry sync hides the subtree.
            // Act
            var image = Bake("inset(0px 60%)", 200f, 100f);

            // Assert
            Assert.That(image, Is.Null);
        }

        [Test]
        public void Given_AZeroAreaPolygon_When_Baked_Then_NoImageIsProduced()
        {
            // Arrange: all vertices on one horizontal line — an empty shape (clips everything).
            // Act
            var image = Bake("polygon(0% 0%, 50% 0%, 100% 0%)", 200f, 100f);

            // Assert
            Assert.That(image, Is.Null);
        }

        [Test]
        public void Given_AStretchInvariantShape_When_BoundsComputedAtANewSize_Then_TheyScaleWithTheBox()
        {
            // Arrange: the geometry sync's rescale-instead-of-rebake fast path positions a reused
            // bake by TryComputeBounds at the new size — it must agree with what a fresh bake
            // would produce.
            var ok = StyleClipPathClass.TryParseShape("polygon(50% 0%, 100% 100%, 0% 100%)", out var spec);
            Assume.That(ok && spec.StretchInvariant, Is.True);

            // Act
            var computed = ClipPathVectorImageBaker.TryComputeBounds(spec, 400f, 300f, out var bounds);
            Assume.That(computed, Is.True);

            // Assert
            Assert.That(bounds, Is.EqualTo(new Rect(0f, 0f, 400f, 300f)));
        }
    }

    /// <summary>
    /// End-to-end coverage for clip-path STATE VARIANTS (<c>hover:clip-path-[…]</c>). The stencil wrapper is
    /// created up-front (WantsClipWrapper) and persists; the shape is none at rest and lights up when the
    /// variant's state turns on (the manipulator toggles the payload, StyleVariantPayload re-resolves the mask
    /// from the live class list). The per-binding bake cache makes a return to a previously-baked shape
    /// re-tessellation-free. Driven on a laid-out <see cref="UnityEditor.EditorWindow"/> panel; hover via a
    /// simulated PointerOver/Out through the manipulator's callback registry. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathVariantPanelTests : PanelTestBase
    {
        private const string HoverTriangle = "w-[100px] h-[100px] hover:clip-path-[polygon(50%_0%,100%_100%,0%_100%)]";

        // This fixture lays out at a 400x400 box (smaller than the base default).
        protected override Rect WindowSize => new Rect(0, 0, 400, 400);

        // Mounts the hover-clip card, forces a layout pass (so the box size is known for the bake), and returns
        // the inner element + its clip binding. The card is wrapped, so it is the wrapper's child.
        private (VisualElement card, ClipPathBinding binding) MountHoverClip() => MountClip(HoverTriangle);

        private (VisualElement card, ClipPathBinding binding) MountClip(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: className, name: "card"));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(card.panel);
            _mounted.Root.Reconciler.Context.ClipPathBindings.TryGetValue(card, out var binding);
            return (card, binding);
        }

        private static void Hover(VisualElement el)
        {
            using var e = PointerOverEvent.GetPooled();
            el.SimulateEvent(e);
        }

        private static void Unhover(VisualElement el)
        {
            using var e = PointerOutEvent.GetPooled();
            el.SimulateEvent(e);
        }

        [Test]
        public void Given_HoverClipVariant_When_Mounted_Then_WrapperExistsWithNoMaskAtRest()
        {
            var (_, binding) = MountHoverClip();

            Assume.That(binding, Is.Not.Null, "Precondition: a variant clip wraps the element up-front");
            // At rest (not hovered) the variant clip is inactive — no shape resolved.
            Assert.That(binding.Spec, Is.Null);
        }

        [Test]
        public void Given_HoverClipVariant_When_Hovered_Then_MaskIsBaked()
        {
            var (card, binding) = MountHoverClip();

            Hover(card);

            Assert.That(binding.Image, Is.Not.Null);
        }

        [Test]
        public void Given_HoverClipVariant_When_Unhovered_Then_MaskIsCleared()
        {
            var (card, binding) = MountHoverClip();
            Hover(card);
            Assume.That(binding.Image, Is.Not.Null, "Precondition: hover baked a mask");

            Unhover(card);

            Assert.That(binding.Image, Is.Null);
        }

        [Test]
        public void Given_HoverClipVariant_When_ReHovered_Then_BakeIsReusedFromCache()
        {
            var (card, binding) = MountHoverClip();
            Hover(card);
            var first = binding.Image;
            Assume.That(first, Is.Not.Null, "Precondition: first hover baked a mask");
            Unhover(card);

            Hover(card);

            // The same VectorImage instance is reused (per-binding cache hit) — no re-tessellation on re-hover.
            Assert.That(binding.Image, Is.SameAs(first));
        }

        [Test]
        public void Given_HoverClipVariant_When_AtRest_Then_WrapperOverflowIsVisible()
        {
            // No mask at rest ⇒ no clipping at all: the wrapper must not rectangle-clip the unclipped element.
            var (_, binding) = MountHoverClip();

            Assert.That(binding.Wrapper.style.overflow.value, Is.EqualTo(Overflow.Visible));
        }

        [Test]
        public void Given_HoverClipVariant_When_Hovered_Then_WrapperOverflowIsHidden()
        {
            // Active mask ⇒ stencil-clip: overflow hidden is half of the UIR mask combination.
            var (card, binding) = MountHoverClip();

            Hover(card);

            Assert.That(binding.Wrapper.style.overflow.value, Is.EqualTo(Overflow.Hidden));
        }

        [Test]
        public void Given_BaseClipPlusHoverNone_When_Hovered_Then_MaskIsCleared()
        {
            // A base clip with hover:clip-path-none must CLEAR the mask on hover (the none payload overrides
            // the base in the live cascade) and restore it on hover-out.
            var (card, binding) = MountClip("w-[100px] h-[100px] clip-path-[circle(50%)] hover:clip-path-none");
            Assume.That(binding.Image, Is.Not.Null, "Precondition: the base clip baked a mask at rest");

            Hover(card);

            Assert.That(binding.Image, Is.Null);
        }
    }
}
