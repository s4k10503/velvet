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
    /// Pins, by real GPU pixel readback, the corners of a layoutId Motion drawn scaled differently on each axis: a
    /// 200px square laid out again at 800×200, held over its old box by a delay, so drawn at a quarter across and
    /// whole down. Framer draws each pixel radius circular there (its <c>correctBorderRadius</c> writes it per axis);
    /// one length a corner divided by the scales' geometric mean draws a 48px radius as an ellipse 24 across and 96
    /// down.
    /// </summary>
    /// <remarks>
    /// Each case samples the pixel whose centre lies deepest on the stated side of every shape it names, each shape
    /// derived from the declared radius and the measured box, and reports the reading as a word, so that a reading
    /// whose pixel cannot be separated, or a box not drawn where its delay holds it, fails on its own word.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class ProjectedCornerFacePlaybackTests
    {
        private const int Size = 300;
        private const float Radius = 48f;
        // Past the half pixel either way that antialiasing reaches, and the half pixel a centre lies from the point.
        private const float Separation = 1.5f;

        private static readonly StyleTransitionConfig s_held = new() { DurationSec = 1f, DelaySec = 30f };

        private static StateUpdater<int> s_setStep;
        private static string s_cardClasses;
        private static VNode[] s_children;
        private static string s_containerClasses;
        private static VNode s_sibling;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;
        private Texture2D _texture;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            s_setStep = default;
            s_cardClasses = "";
            s_children = null;
            s_containerClasses = null;
            s_sibling = null;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _frameRateScope.Dispose();
            _mounted?.Dispose();
            _host?.Dispose();
            if (_texture != null) Object.Destroy(_texture);
            yield return null;
        }

        private static VNode CardRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var size = step == 0 ? "w-[200px] h-[200px]" : "w-[800px] h-[200px]";
            return V.Div(className: s_containerClasses, children: new VNode[]
            {
                V.Motion(name: "card", layoutId: "card", transition: s_held,
                    className: $"absolute left-[40px] top-[40px] {size} rounded-[48px] {s_cardClasses}", children: s_children),
                s_sibling,
            });
        }

        // Mounts the card, lets it settle, gives it the wide box and lets the move start.
        private IEnumerator MountAndWiden(string name, System.Action<VisualElement> configure = null)
        {
            _host = new RenderTexturePanelHost(name, Size, Size);
            _host.Root.style.backgroundColor = Color.black;
            VelvetStyleUtilities.AttachTo(_host.Root);
            _mounted = V.Mount(_host.Root, V.Component(CardRender, key: "root"));
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
            configure?.Invoke(Card);
            yield return WaitRealtimeDraining(0.3, _host.TargetTexture);
            s_setStep.Invoke(1);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);
        }

        private VisualElement Card => _host.Root.Q<VisualElement>("card");

        // The drawn box's top-left corner, where the delay holds it over its old 200px box.
        private Vector2 DrawnCorner => Card.worldBound.min;

        private bool DrawnOverItsOldBox => Mathf.Abs(Card.worldBound.width - 200f) < 0.5f && Mathf.Abs(Card.worldBound.height - 200f) < 0.5f;

        private Color32 PixelAt(Vector2Int pixel)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));
            return pixels[((Size - 1 - pixel.y) * Size) + pixel.x];
        }

        private static bool IsBlue(Color32 p) => p.b > 140 && p.r < 90 && p.g < 90;

        private static bool IsRed(Color32 p) => p.r > 140 && p.g < 90 && p.b < 90;

        private static bool IsGreen(Color32 p) => p.g > 140 && p.r < 90 && p.b < 90;

        private static bool IsBlack(Color32 p) => p.r < 40 && p.g < 40 && p.b < 40;

        // The circle Framer draws the card's corner as.
        private TopLeftCorner Circle => new(DrawnCorner, new Vector2(Radius, Radius));

        // The ellipse one length a corner draws it as under a quarter across and whole down: the radius over the
        // geometric mean of the two, a half, drawn at each.
        private TopLeftCorner Ellipse => new(DrawnCorner, new Vector2(Radius * 0.5f, Radius * 2f));

        // A box's top-left corner rounded by an ellipse of the given radii, over the region right of and below it.
        private readonly struct TopLeftCorner
        {
            private readonly Vector2 _corner;
            private readonly Vector2 _radii;

            public TopLeftCorner(Vector2 corner, Vector2 radii)
            {
                _corner = corner;
                _radii = radii;
            }

            // How far inside the shape p lies, negative outside: exact off the corner's arc, and on it the first-order
            // approximation to the distance from an ellipse.
            public float Depth(Vector2 p)
            {
                var d = p - _corner;
                if (d.x >= _radii.x || d.y >= _radii.y) return Mathf.Min(d.x, d.y);
                var q = d - _radii;
                var k0 = new Vector2(q.x / _radii.x, q.y / _radii.y).magnitude;
                var k1 = new Vector2(q.x / (_radii.x * _radii.x), q.y / (_radii.y * _radii.y)).magnitude;
                return -k0 * (k0 - 1f) / k1;
            }
        }

        // The pixel near the corner whose centre lies deepest inside each of inside and outside each of outside, and
        // that depth.
        private Vector2Int Deepest(TopLeftCorner[] inside, TopLeftCorner[] outside, out float depth)
        {
            var origin = Vector2Int.FloorToInt(DrawnCorner);
            var best = origin;
            depth = float.NegativeInfinity;
            for (var row = origin.y - 30; row < origin.y + 120; row++)
            {
                for (var col = origin.x - 30; col < origin.x + 120; col++)
                {
                    var centre = new Vector2(col + 0.5f, row + 0.5f);
                    var least = float.PositiveInfinity;
                    foreach (var shape in inside) least = Mathf.Min(least, shape.Depth(centre));
                    foreach (var shape in outside) least = Mathf.Min(least, -shape.Depth(centre));
                    if (least <= depth) continue;
                    (depth, best) = (least, new Vector2Int(col, row));
                }
            }
            return best;
        }

        // What the panel drew at the pixel deepest inside every one of inside and outside every one of outside, named
        // by the first test it meets; a word naming why it could not be read otherwise.
        private string Read(TopLeftCorner[] inside, TopLeftCorner[] outside, params (string Word, System.Func<Color32, bool> Test)[] words)
        {
            if (!DrawnOverItsOldBox) return "not drawn over its old box";
            var pixel = Deepest(inside, outside, out var depth);
            if (depth < Separation) return "unseparable";
            var colour = PixelAt(pixel);
            foreach (var (word, test) in words)
            {
                if (test(colour)) return word;
            }
            return "other";
        }

        // Inside the card's face, well clear of its corner: what tells a face painted at all from none.
        private Color32 Interior => PixelAt(Vector2Int.FloorToInt(DrawnCorner + new Vector2(100f, 100f)));

        [UnityTest]
        public IEnumerator Given_ARoundedLayoutIdMotionDrawnAQuarterAcross_When_ItsFaceIsPainted_Then_ItFillsItsCornerOutToACircle()
        {
            // Arrange / Act
            s_cardClasses = "bg-[#0000ff]";
            yield return MountAndWiden("FaceCircle");

            // Assert — blue inside the circle where the ellipse would have left it bare.
            Assert.That(Read(new[] { Circle }, new[] { Ellipse }, ("blue", IsBlue), ("black", IsBlack)), Is.EqualTo("blue"));
        }

        [UnityTest]
        public IEnumerator Given_ARoundedLayoutIdMotionDrawnAQuarterAcross_When_ItsFaceIsPainted_Then_NothingIsPaintedOutsideTheCircle()
        {
            // Arrange / Act
            s_cardClasses = "bg-[#0000ff]";
            yield return MountAndWiden("FaceBeyondCircle");

            // Assert — black outside the circle where the ellipse would have painted, while its face is painted.
            var painted = IsBlue(Interior);
            Assert.That(painted ? Read(new[] { Ellipse }, new[] { Circle }, ("blue", IsBlue), ("black", IsBlack)) : "no face",
                Is.EqualTo("black"));
        }

        [UnityTest]
        public IEnumerator Given_AnOverflowHiddenRoundedLayoutIdMotionDrawnAQuarterAcross_When_ItsChildFillsIt_Then_TheChildIsShownOutToTheCircle()
        {
            // Arrange / Act — a red child filling a clipping card.
            s_cardClasses = "overflow-hidden";
            s_children = new VNode[] { V.Div(className: "w-full h-full bg-[#ff0000]") };
            yield return MountAndWiden("ClipCircle");

            // Assert
            Assert.That(Read(new[] { Circle }, new[] { Ellipse }, ("red", IsRed), ("black", IsBlack)), Is.EqualTo("red"));
        }

        [UnityTest]
        public IEnumerator Given_AnOverflowHiddenRoundedLayoutIdMotionDrawnAQuarterAcross_When_ItsChildFillsIt_Then_TheChildIsClippedAtTheCircle()
        {
            // Arrange / Act — a red child filling a clipping card.
            s_cardClasses = "overflow-hidden";
            s_children = new VNode[] { V.Div(className: "w-full h-full bg-[#ff0000]") };
            yield return MountAndWiden("ClipBeyondCircle");

            // Assert — clipped outside the circle where the ellipse would have shown it, while the child shows inside.
            var shown = IsRed(Interior);
            Assert.That(shown ? Read(new[] { Ellipse }, new[] { Circle }, ("red", IsRed), ("black", IsBlack)) : "no child",
                Is.EqualTo("black"));
        }

        [UnityTest]
        public IEnumerator Given_ABorderedRoundedLayoutIdMotionDrawnAQuarterAcross_When_ItsBorderIsPainted_Then_NothingIsPaintedOutsideTheCircle()
        {
            // Arrange / Act — an eight-pixel red border, drawn eight pixels deep along the top.
            s_cardClasses = "bg-[#0000ff] border-[8px] border-[#ff0000]";
            yield return MountAndWiden("BorderBeyondCircle");

            // Assert — black outside the circle where the ellipse's border band would have run, while its top band is red.
            var band = IsRed(PixelAt(Vector2Int.FloorToInt(DrawnCorner + new Vector2(100f, 4f))));
            var topBand = new TopLeftCorner(DrawnCorner + new Vector2(-1000f, 8f), new Vector2(0f, 0f));
            Assert.That(band ? Read(new[] { Ellipse }, new[] { Circle, topBand }, ("red", IsRed), ("black", IsBlack)) : "no band",
                Is.EqualTo("black"));
        }

        [UnityTest]
        public IEnumerator Given_ARoundedLayoutIdMotionWithABackgroundImageDrawnAQuarterAcross_When_ItsFaceIsPainted_Then_NoImageIsPaintedOutsideTheCircle()
        {
            // Arrange / Act — a green texture given as the card's background image.
            _texture = new Texture2D(4, 4);
            var green = new Color32[16];
            for (var i = 0; i < green.Length; i++) green[i] = new Color32(0, 255, 0, 255);
            _texture.SetPixels32(green);
            _texture.Apply();
            yield return MountAndWiden("ImageBeyondCircle", card => card.style.backgroundImage = new StyleBackground(_texture));

            // Assert — black outside the circle where a square or elliptical corner would show the image, while the image
            // fills the face.
            var shown = IsGreen(Interior);
            Assert.That(shown ? Read(new[] { Ellipse }, new[] { Circle }, ("green", IsGreen), ("black", IsBlack)) : "no image",
                Is.EqualTo("black"));
        }

        [UnityTest]
        public IEnumerator Given_ARingedRoundedLayoutIdMotionDrawnAQuarterAcross_When_ItsBandIsPainted_Then_NothingIsPaintedOutsideItsCornersPerAxis()
        {
            // Arrange / Act — an eight-pixel green band, drawn two across and eight down. Its outer corner, the card's 48
            // over each axis's scale grown by eight, is drawn 50 across and 56 down; one length a corner draws it 26
            // across and 104 down, or 14 across and 56 down where the band read the card's radius before the move.
            s_cardClasses = "bg-[#0000ff] ring-8 ring-[#00ff00]";
            yield return MountAndWiden("RingCorner");
            var bandCorner = DrawnCorner - new Vector2(2f, 8f);

            // Assert — black where either band rounded alike would have run and the one rounded per axis does not,
            // outside the card, while the band runs along its top.
            var banded = IsGreen(PixelAt(Vector2Int.FloorToInt(DrawnCorner + new Vector2(100f, -4f))));
            var reading = Read(new[] { new TopLeftCorner(bandCorner, new Vector2(26f, 104f)), new TopLeftCorner(bandCorner, new Vector2(14f, 56f)) },
                new[] { Circle, new TopLeftCorner(bandCorner, new Vector2(50f, 56f)) }, ("green", IsGreen), ("black", IsBlack));
            Assert.That(banded ? reading : "no band", Is.EqualTo("black"));
        }

        // GREEN_ON_BASE(characterization): the base leaves a moving Motion's background to UI Toolkit, which paints the
        // colour its classes give. Painting the colour the face began with, as reading the resolved style beneath a slot
        // it has hidden does, reddens this case.
        [UnityTest]
        public IEnumerator Given_ARoundedLayoutIdMotionDrawnAQuarterAcross_When_ItsClassesChangeItsBackgroundMidMove_Then_TheFaceIsPaintedInTheNewColour()
        {
            // Arrange — blue.
            s_cardClasses = "bg-[#0000ff]";
            yield return MountAndWiden("ColourChange");

            // Act — red, with the box unchanged.
            s_cardClasses = "bg-red-500";
            s_setStep.Invoke(2);
            yield return WaitRealtimeDraining(0.3, _host.TargetTexture);

            // Assert
            Assert.That(IsRed(Interior), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base leaves a moving Motion's face to UI Toolkit, which paints it beneath
        // everything generated in the Motion's content. Appending the face's callback in ProjectedCornerFace.InsertFirst,
        // after the divider's painter, reddens this case: the face then covers the dashes.
        [UnityTest]
        public IEnumerator Given_ADividedLayoutIdMotionDrawnAQuarterAcross_When_ItsFaceIsPainted_Then_ItsDashedDividerShowsOverIt()
        {
            // Arrange / Act — blue, the first child of a container dividing its children with eight-pixel green dashes.
            s_cardClasses = "bg-[#0000ff]";
            s_containerClasses = "divide-y-[8px] divide-[#00ff00] divide-dashed";
            s_sibling = V.Div(className: "w-[10px] h-[10px]");
            yield return MountAndWiden("DividerOverFace");

            // Assert — green along the middle of its bottom edge, clear of its rounded corners.
            var bottom = Card.worldBound.yMax;
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));
            var dashes = 0;
            for (var row = Mathf.FloorToInt(bottom) - 6; row < Mathf.FloorToInt(bottom) - 2; row++)
            {
                for (var col = Mathf.FloorToInt(DrawnCorner.x) + 60; col < Mathf.FloorToInt(DrawnCorner.x) + 140; col++)
                {
                    dashes += IsGreen(pixels[((Size - 1 - row) * Size) + col]) ? 1 : 0;
                }
            }
            Assert.That(dashes, Is.GreaterThan(0));
        }
    }
}
