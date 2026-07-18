using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins on a real runtime panel that the filter bounds-spacer offsets itself by the caster's border. The
    /// spacer's left/top resolve against the padding box, while the sheared silhouette it must cover is in the
    /// caster's border-box space, so the border (parsed from the class list — here the USS-scale `border-8`, not
    /// an inline bracket value) is subtracted when positioning. Without that subtraction a border thicker than
    /// the shear overhang leaves the spacer's border-box edge inside the box, clipping the overflow.
    /// </summary>
    [Timeout(120000)]
    internal sealed class FilterSpacerBorderOffsetPlaybackTests
    {
        private RenderTexturePanelHost _host;
        private MountedTree _mounted;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            yield return null;
        }

        private static VisualElement FindSpacer(VisualElement caster)
        {
            for (var i = 0; i < caster.childCount; i++)
            {
                if (SilhouetteBoundsSpacer.IsSpacer(caster[i]))
                {
                    return caster[i];
                }
            }
            return null;
        }

        [UnityTest]
        public IEnumerator Given_ABorderThickerThanTheShearOverhang_When_Rendered_Then_TheSpacerReachesPastTheBorderBoxEdge()
        {
            // Arrange — the 8px USS-scale border exceeds the shallow -skew-x-4 overhang (~2.8px) on an 80px-tall
            // box, under a filter, so an unsubtracted border would push the spacer's border-box edge positive.
            _host = new RenderTexturePanelHost("BorderSpacerPanel", 300, 300);
            _mounted = V.Mount(_host.Root,
                V.Div(className: "w-[200px] h-[80px] -skew-x-4 border-8 hue-rotate-90", name: "box"));
            var box = _host.Root.Q<VisualElement>("box");
            Assume.That(box, Is.Not.Null, "Precondition: the element mounted");

            // Act — advance until the geometry callback has sized the spacer (bounded to survive any warmup).
            VisualElement spacer = null;
            var deadline = Time.realtimeSinceStartupAsDouble + 30.0;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                spacer = FindSpacer(box);
                if (spacer != null && spacer.resolvedStyle.width > 0f)
                {
                    break;
                }
            }
            Assume.That(spacer, Is.Not.Null, "Precondition: the filtered skewed element carries a spacer");
            Assume.That(spacer.resolvedStyle.width, Is.GreaterThan(0f), "Precondition: the spacer was sized");

            // Assert — resolvedStyle.left is the spacer's used position from the caster's border-box origin
            // (Yoga adds the parent's border to the padding-box-relative inline left). It must reach left of
            // that origin to cover the shear that leans past the corner; without subtracting the border when
            // positioning, a border thicker than the overhang would leave this positive (inside the box).
            Assert.That(spacer.resolvedStyle.left, Is.LessThan(0f));
        }
    }
}
