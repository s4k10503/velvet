using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies, by GPU pixel readback, that the outer box-shadow of an element that clips its content, which is
    /// painted on a sibling over the element, takes currentcolor and an em length from the element, as the browser
    /// computes them, rather than from its parent, whose text colour and font size the sibling inherits.
    /// </summary>
    [Timeout(600000)]
    internal sealed class ClippedShadowOwnStylePlaybackTests
    {
        private const int Size = 100;

        private static string s_parent;
        private static string s_child;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _frameRateScope.Dispose();
            _mounted?.Dispose();
            _host?.Dispose();
            yield return null;
        }

        [Component]
        private static VNode Render()
            => V.Div(name: "parent", className: "absolute left-0 top-0 w-[100px] h-[100px] " + s_parent, children: new VNode[]
            {
                V.Div(name: "child", className: "absolute left-[30px] top-[30px] w-[40px] h-[40px] " + s_child),
            });

        private IEnumerator Mount(string parent, string child)
        {
            (s_parent, s_child) = (parent, child);
            _host = new RenderTexturePanelHost("ClippedShadowOwnStyle", Size, Size, clear: true);
            VelvetStyleUtilities.AttachTo(_host.Root);
            _mounted = V.Mount(_host.Root, V.Component(Render, key: "root"));
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
        }

        // The colour painted dx pixels left of the child's box at its vertical middle, named; "no overlay" where the
        // child's outer layers are not painted on its sibling, which is the case these are about.
        private string ColourLeftOfChild(float dx)
        {
            var child = _host.Root.Q<VisualElement>("child");
            if (!FaceBorderOverlay.Has(child))
            {
                return "no overlay";
            }
            var bound = child.worldBound;
            var pixel = Vector2Int.FloorToInt(new Vector2(bound.xMin - dx, bound.center.y));
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));
            var p = pixels[((Size - 1 - pixel.y) * Size) + pixel.x];
            if (p.a <= 20) return "nothing";
            if (p.b > 200 && p.r < 50 && p.g < 50) return "blue";
            if (p.r > 200 && p.g < 50 && p.b < 50) return "red";
            if (p.g > 200 && p.r < 50 && p.b < 50) return "green";
            return "other " + p;
        }

        [UnityTest]
        public IEnumerator Given_AClippingChildWhoseShadowIsInItsTextColour_When_Rendered_Then_TheShadowIsInTheChildsColour()
        {
            // Arrange / Act — a red parent, a blue child, an 8px spread in currentcolor.
            yield return Mount("text-[#ff0000]", "text-[#0000ff] overflow-hidden shadow-[0_0_0_8px] shadow-current");

            // Assert — 4px out, inside the spread.
            Assert.That(ColourLeftOfChild(4f), Is.EqualTo("blue"));
        }

        [UnityTest]
        public IEnumerator Given_AClippingChildWhoseShadowSpreadIsInEms_When_Rendered_Then_ItIsMeasuredAgainstTheChildsFontSize()
        {
            // Arrange / Act — a 10px parent, a 20px child, a 1em spread.
            yield return Mount("text-[10px]", "text-[20px] overflow-hidden shadow-[0_0_0_1em_#00ff00]");

            // Assert — 15px out, inside a 20px spread and past a 10px one.
            Assert.That(ColourLeftOfChild(15f), Is.EqualTo("green"));
        }
    }
}
