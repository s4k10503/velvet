using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the pure geometry of the filter bounds-spacer: the sheared silhouette AABB, the shear growth
    /// applied to a shadow quad, the axis-aligned union, and the trailing-spacer child count. GWT, one assert
    /// each; these need no panel.
    /// </summary>
    [TestFixture]
    internal sealed class SilhouetteBoundsSpacerTests
    {
        [Test]
        public void Given_ASkewX_When_AabbComputed_Then_ItGrowsWidthByHeightTimesTan()
        {
            // Arrange — a 100x40 box sheared on X by tan = 0.5.
            // Act
            var aabb = SilhouetteBoundsSpacer.ShearedAabb(100f, 40f, 0.5f, 0f);

            // Assert — width grows by h*|tanX| = 40*0.5 = 20 (10 each side).
            Assert.That(aabb.width, Is.EqualTo(120f).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewX_When_AabbComputed_Then_ItOverhangsLeftByHalfHeightTimesTan()
        {
            // Act
            var aabb = SilhouetteBoundsSpacer.ShearedAabb(100f, 40f, 0.5f, 0f);

            // Assert — the left overhang is -(h/2)*|tanX| = -10.
            Assert.That(aabb.xMin, Is.EqualTo(-10f).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewY_When_AabbComputed_Then_ItGrowsHeightByWidthTimesTan()
        {
            // Act
            var aabb = SilhouetteBoundsSpacer.ShearedAabb(100f, 40f, 0f, 0.25f);

            // Assert — height grows by w*|tanY| = 100*0.25 = 25.
            Assert.That(aabb.height, Is.EqualTo(65f).Within(1e-4f));
        }

        [Test]
        public void Given_NoSkew_When_AabbComputed_Then_ItIsExactlyTheBox()
        {
            // Act
            var aabb = SilhouetteBoundsSpacer.ShearedAabb(100f, 40f, 0f, 0f);

            // Assert
            Assert.That(aabb, Is.EqualTo(new Rect(0f, 0f, 100f, 40f)));
        }

        [Test]
        public void Given_TwoRects_When_Unioned_Then_TheResultEnclosesBoth()
        {
            // Arrange — the box and a shadow quad shifted up-left and larger.
            var box = new Rect(0f, 0f, 100f, 40f);
            var quad = new Rect(-30f, -20f, 160f, 100f);

            // Act
            var u = SilhouetteBoundsSpacer.Union(box, quad);

            // Assert — the union spans from the quad's min to its max (it encloses the box).
            Assert.That(u, Is.EqualTo(new Rect(-30f, -20f, 160f, 100f)));
        }

        [Test]
        public void Given_ARect_When_ExpandedForShear_Then_ItGrowsByItsOwnExtentTimesTan()
        {
            // Arrange — a 100x40 rect at origin, tan = 0.5 on X.
            var r = new Rect(0f, 0f, 100f, 40f);

            // Act
            var e = SilhouetteBoundsSpacer.ExpandForShear(r, 0.5f, 0f);

            // Assert — grows width by height*|tanX| = 20.
            Assert.That(e.width, Is.EqualTo(120f).Within(1e-4f));
        }

        [Test]
        public void Given_ContainerWithTrailingSpacer_When_Counted_Then_TheSpacerIsExcluded()
        {
            // Arrange — two rendered children then a spacer.
            var container = new VisualElement();
            container.Add(new VisualElement());
            container.Add(new VisualElement());
            var spacer = new VisualElement();
            spacer.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);
            container.Add(spacer);

            // Act / Assert
            Assert.That(SilhouetteBoundsSpacer.NonSpacerChildCount(container), Is.EqualTo(2));
        }

        [Test]
        public void Given_ContainerWithTwoTrailingSpacers_When_Counted_Then_BothAreExcluded()
        {
            // Arrange — one rendered child then two spacers (a skewed + shadowed caster).
            var container = new VisualElement();
            container.Add(new VisualElement());
            for (var i = 0; i < 2; i++)
            {
                var s = new VisualElement();
                s.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);
                container.Add(s);
            }

            // Act / Assert
            Assert.That(SilhouetteBoundsSpacer.NonSpacerChildCount(container), Is.EqualTo(1));
        }
    }
}
