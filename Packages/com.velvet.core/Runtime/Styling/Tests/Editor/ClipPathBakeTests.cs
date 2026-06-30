using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="ClipPathVectorImageBaker"/>: a parsed spec bakes into a runtime
    /// <c>VectorImage</c> whose analytic bounds place the shape where the CSS says, percentages
    /// resolve against the element box (circle radius against the CSS reference-box diagonal),
    /// degenerate shapes refuse to bake (the geometry sync then hides the subtree — CSS clips
    /// everything for an empty shape), and the saved image's tight bounds agree with the analytic
    /// bounds the background is positioned by. GWT, one assert per case; the fixture owns and
    /// destroys the baked image in TearDown.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathBakeTests
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
}
