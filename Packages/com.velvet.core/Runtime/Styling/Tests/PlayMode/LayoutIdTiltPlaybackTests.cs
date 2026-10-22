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
    /// Pins, by real GPU pixel readback on a screen panel, how UI Toolkit draws and picks an element rotated about an axis
    /// in the panel's plane, which LayoutIdTilt relies on, and the layoutId Motion it draws that way: one turned 45 degrees
    /// inside an outer one held over a box a quarter as wide as its layout.
    /// </summary>
    /// <remarks>
    /// Each case asserts one word naming what the panel did, or why it could not be read.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class LayoutIdTiltPlaybackTests
    {
        private const int Size = 300;

        private static readonly StyleTransitionConfig s_held = new() { DurationSec = 1f, DelaySec = 30f };

        private static StateUpdater<int> s_setStep;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            s_setStep = default;
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

        private Color32 PixelAt(Vector2 point)
        {
            var pixel = Vector2Int.FloorToInt(point);
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));
            return pixels[((Size - 1 - pixel.y) * Size) + pixel.x];
        }

        private static bool IsRed(Color32 p) => p.r > 140 && p.g < 90 && p.b < 90;

        private static bool IsBlack(Color32 p) => p.r < 40 && p.g < 40 && p.b < 40;

        // A red 100px square at (100, 100) on black, turned 60 degrees about an axis in the panel's plane.
        private IEnumerator MountTilted(string name, Vector3 axis)
        {
            _host = new RenderTexturePanelHost(name, Size, Size);
            _host.Root.style.backgroundColor = Color.black;
            var tilted = new VisualElement { name = "tilted" };
            tilted.style.position = Position.Absolute;
            (tilted.style.left, tilted.style.top, tilted.style.width, tilted.style.height) = (100f, 100f, 100f, 100f);
            tilted.style.backgroundColor = Color.red;
            tilted.style.rotate = new Rotate(new Angle(60f), axis);
            _host.Root.Add(tilted);
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
        }

        // GREEN_ON_BASE(characterization): a fact of the engine's, which LayoutIdTilt relies on and the base does not use.
        [UnityTest]
        public IEnumerator Given_AnElementTiltedOutOfAScreenPanel_When_ItIsDrawn_Then_ItIsDrawnFlattened()
        {
            // Arrange / Act
            yield return MountTilted("TiltDrawn", Vector3.right);

            // Assert — turned about x, its sides along y foreshortened to half about its centre at y 150: drawn from y 125
            // to 175. 128 is 44px from the centre along its own y, where its depth is 38px; 115 lies inside its box unturned.
            var (inside, outside) = (PixelAt(new Vector2(150f, 128f)), PixelAt(new Vector2(150f, 115f)));
            var word = IsRed(inside) && IsBlack(outside) ? "flattened"
                : IsBlack(inside) && IsBlack(outside) ? "clipped by its depth"
                : IsRed(inside) && IsRed(outside) ? "drawn unturned"
                : "other";
            Assert.That(word, Is.EqualTo("flattened"));
        }

        // GREEN_ON_BASE(characterization): a fact of the engine's, which motion.md states for a tilted layoutId Motion.
        [UnityTest]
        public IEnumerator Given_AnElementTiltedOutOfAScreenPanel_When_APointerIsBesideItsFaceInsideItsBounds_Then_ThePanelPicksIt()
        {
            // Arrange — turned about the diagonal through its top left and bottom right corners, it is drawn as a
            // parallelogram filling its box along that diagonal and leaving the box's bottom left corner bare.
            yield return MountTilted("TiltPicked", new Vector3(1f, 1f, 0f).normalized);

            // Act — 45px left of and below its centre.
            var point = new Vector2(105f, 195f);
            var picked = _host.Root.panel.Pick(point)?.name == "tilted";

            // Assert
            var drawn = PixelAt(point);
            var word = picked && IsBlack(drawn) ? "picked beside its face"
                : picked && IsRed(drawn) ? "picked on its face"
                : "not picked";
            Assert.That(word, Is.EqualTo("picked beside its face"));
        }

        [Component]
        private static VNode TurnedInsideStretchedRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var size = step == 0 ? "w-[200px] h-[200px]" : "w-[800px] h-[200px]";
            return V.Motion(name: "outer", layoutId: "outer", transition: s_held,
                className: $"absolute left-[40px] top-[40px] {size}",
                children: new VNode[]
                {
                    V.Motion(name: "inner", layoutId: "inner", transition: s_held,
                        className: "absolute left-[50px] top-[50px] w-[100px] h-[100px] bg-[#ff0000] rotate-45"),
                });
        }

        [UnityTest]
        public IEnumerator Given_ALayoutIdMotionTurnedInsideOneStretchedOnOneAxis_When_ItIsDrawn_Then_ItIsARotatedSquareAtItsOwnSize()
        {
            // Arrange — the outer one laid out four times as wide, its delay holding it over its 200px box.
            _host = new RenderTexturePanelHost("TiltMotion", Size, Size);
            _host.Root.style.backgroundColor = Color.black;
            VelvetStyleUtilities.AttachTo(_host.Root);
            _mounted = V.Mount(_host.Root, V.Component(TurnedInsideStretchedRender, key: "root"));
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);

            // Act
            s_setStep.Invoke(1);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert — about its centre, a square of side 100 turned 45 degrees reaches 70.7px along each axis: 60px left
            // is inside it. Its own matrix with the outer one's stretch at an angle to it draws its x axis 40px out at
            // (28, 113) from the centre, outside that square.
            var outer = _host.Root.Q<VisualElement>("outer");
            var inner = _host.Root.Q<VisualElement>("inner");
            var held = Mathf.Abs(outer.worldBound.width - 200f) < 0.5f;
            var centre = (Vector2)inner.worldTransform.MultiplyPoint3x4(new Vector3(50f, 50f, 0f));
            var (square, sheared) = (PixelAt(centre + new Vector2(-60f, 0f)), PixelAt(centre + new Vector2(28f, 113f)));
            var word = !held ? "outer not held"
                : IsRed(square) && IsBlack(sheared) ? "rotated square"
                : IsBlack(square) && IsRed(sheared) ? "sheared"
                : "other";
            Assert.That(word, Is.EqualTo("rotated square"));
        }
    }
}
