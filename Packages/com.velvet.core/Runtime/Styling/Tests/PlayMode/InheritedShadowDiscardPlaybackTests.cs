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
    /// Specifies, by GPU pixel readback, that a child whose shadow-inherit layer reads a colour its parent leaves
    /// invalid (a size's /150 beside a colour class) paints none of its box-shadow, its ring included, beside a child
    /// under a parent whose colour is valid, which paints both.
    /// </summary>
    [Timeout(600000)]
    internal sealed class InheritedShadowDiscardPlaybackTests
    {
        private const int Width = 200;
        private const int Height = 100;
        private const string Invalid = "shadow-lg/150 shadow-red-500";
        private const string Valid = "shadow-lg/50 shadow-red-500";

        private static StateUpdater<string> s_setLeft;
        private static string s_left;
        private static string s_child;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            s_setLeft = default;
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

        // Two parents side by side, each holding one child with the same classes: the left parent's classes are the
        // case's, the right one's the control's, with a valid colour.
        [Component]
        private static VNode Render()
        {
            var (left, setLeft) = Hooks.UseState(s_left);
            s_setLeft = setLeft;
            return V.Div(className: "absolute left-0 top-0 w-[200px] h-[100px]", children: new VNode[]
            {
                Pair("left", "left-[0px] " + left),
                Pair("right", "left-[100px] " + Valid),
            });
        }

        private static VNode Pair(string name, string parentClasses)
            => V.Div(name: name, className: "absolute top-[0px] w-[100px] h-[100px] " + parentClasses, children: new VNode[]
            {
                V.Div(name: name + "-child", className: "absolute left-[30px] top-[30px] w-[40px] h-[40px] bg-[#0000ff] " + s_child),
            });

        private IEnumerator Mount(string left, string child)
        {
            (s_left, s_child) = (left, child);
            _host = new RenderTexturePanelHost("InheritedShadowDiscard", Width, Height, clear: true);
            VelvetStyleUtilities.AttachTo(_host.Root);
            _mounted = V.Mount(_host.Root, V.Component(Render, key: "root"));
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
        }

        // Whether anything is painted dx pixels left of a child's box, at its vertical middle, in the half from left: the
        // target is cleared to transparent, which a layer drawn in a colour of its own, black included, is not.
        private bool PaintedLeftOf(int left, int dx)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Width, Height));
            return pixels[((Height - 1 - 50) * Width) + left + 30 - dx].a > 20;
        }

        private string Read(int dx)
            => !PaintedLeftOf(100, dx) ? "no control" : PaintedLeftOf(0, dx) ? "painted" : "nothing";

        [UnityTest]
        public IEnumerator Given_AChildInheritingAnInvalidShadowColour_When_Rendered_Then_ItsSpreadIsNotPainted()
        {
            // Arrange / Act — an 8px spread reading the inherited colour.
            yield return Mount(Invalid, "shadow-[0_0_0_8px] shadow-inherit");

            // Assert — 6px out, inside the spread.
            Assert.That(Read(6), Is.EqualTo("nothing"));
        }

        [UnityTest]
        public IEnumerator Given_AClippingChildInheritingAnInvalidShadowColour_When_Rendered_Then_ItsSpreadIsNotPainted()
        {
            // Arrange / Act — overflow-hidden paints the outer layers on a sibling over the child.
            yield return Mount(Invalid, "overflow-hidden shadow-[0_0_0_8px] shadow-inherit");

            // Assert
            Assert.That(Read(6), Is.EqualTo("nothing"));
        }

        [UnityTest]
        public IEnumerator Given_ARingedChildInheritingAnInvalidShadowColour_When_Rendered_Then_ItsRingIsNotPainted()
        {
            // Arrange / Act — the ring is part of the same box-shadow.
            yield return Mount(Invalid, "ring-4 ring-[#00ff00] shadow-[0_0_0_1px] shadow-inherit");

            // Assert — 2px out, inside the ring and past the 1px spread.
            Assert.That(Read(3), Is.EqualTo("nothing"));
        }

        [UnityTest]
        public IEnumerator Given_ARingedChildWhoseParentsColourBecomesInvalid_When_Rendered_Then_ItsRingIsNoLongerPainted()
        {
            // Arrange — painted while the parent's colour is valid.
            yield return Mount(Valid, "ring-4 ring-[#00ff00] shadow-[0_0_0_1px] shadow-inherit");
            var before = PaintedLeftOf(0, 3);

            // Act — only the parent's classes change.
            s_setLeft.Invoke(Invalid);
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Assert — the ring painted before is the control.
            Assert.That(before ? Read(3) : "not painted before", Is.EqualTo("nothing"));
        }
    }
}
