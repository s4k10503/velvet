using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins BackgroundTile.Room, by real GPU pixel readback, to the tile UI Toolkit draws a background image at: the
    /// image placed at a percentage on one element, and at Room's share of it in pixels on another of the same size
    /// and sizing, shows its red half and then its blue half from the same column. Once for each way background-size
    /// and a round repeat size the tile, and for each kind of image whose own size Room reads.
    /// </summary>
    [Timeout(600000)]
    internal sealed class BackgroundTilePlaybackTests
    {
        private const int Width = 120;
        private const int Height = 60;

        private RenderTexturePanelHost _host;
        private readonly List<UnityEngine.Object> _made = new();
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
            _host?.Dispose();
            _host = null;
            foreach (var made in _made)
            {
                UnityEngine.Object.Destroy(made);
            }
            _made.Clear();
            yield return null;
        }

        private T Kept<T>(T made) where T : UnityEngine.Object
        {
            _made.Add(made);
            return made;
        }

        // Red on its left half and blue on its right, or stacked, red on its top half and blue below, sampled without
        // blending its texels.
        private Texture2D Halves(int width, int height, bool stacked = false)
        {
            var image = Kept(new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            });
            var texels = new Color32[width * height];
            for (var i = 0; i < texels.Length; i++)
            {
                // Texel rows run bottom-up.
                var red = stacked ? i / width >= height / 2 : i % width < width / 2;
                texels[i] = red ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255);
            }
            image.SetPixels32(texels);
            image.Apply();
            return image;
        }

        private static readonly BackgroundSize Natural = new(Length.Auto(), Length.Auto());

        private static Action<IStyle> SizedAs(BackgroundSize size) => style => style.backgroundSize = size;

        // Two elements of Width by Height showing image as sized sizes it, one above the other, or beside it for the
        // vertical axis: the first at percent along the axis, the second at Room's share of it in pixels, read once the
        // panel has laid the second out. Yields until the panel has drawn both.
        private IEnumerator Drawn(Background image, Action<IStyle> sized, bool vertical, float percent)
        {
            _host = new RenderTexturePanelHost(nameof(BackgroundTilePlaybackTests), vertical ? Width * 2 : Width,
                vertical ? Height : Height * 2, clear: true);
            var byPercent = Showing(image, sized, 0f, 0f);
            var byPixels = vertical ? Showing(image, sized, Width, 0f) : Showing(image, sized, 0f, Height);
            Place(byPercent, vertical, Length.Percent(percent));
            yield return WaitFramesDraining(2, _host.TargetTexture);
            Place(byPixels, vertical, BackgroundTile.Room(byPixels, vertical) * percent / 100f);
            yield return WaitFramesDraining(4, _host.TargetTexture);
        }

        private IEnumerator AtFullWidth(Background image, Action<IStyle> sized) => Drawn(image, sized, false, 100f);

        private VisualElement Showing(Background image, Action<IStyle> sized, float left, float top)
        {
            var element = new VisualElement();
            element.style.position = Position.Absolute;
            element.style.left = left;
            element.style.top = top;
            element.style.width = Width;
            element.style.height = Height;
            element.style.backgroundImage = new StyleBackground(image);
            element.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
            element.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            sized(element.style);
            _host.Root.Add(element);
            return element;
        }

        private static void Place(VisualElement element, bool vertical, Length offset)
        {
            if (vertical)
            {
                element.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, offset);
                return;
            }
            element.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, offset);
        }

        private Color32[] Pixels() => RenderTexturePixelReader.ReadPixels(_host.TargetTexture,
            new RectInt(0, 0, _host.TargetTexture.width, _host.TargetTexture.height));

        private static bool IsBlue(Color32 pixel) => pixel.b > 140 && pixel.r < 90 && pixel.g < 90;

        // Readback rows run bottom-up while the panel's run top-down, so the row index is mirrored.
        private Color32 At(Color32[] pixels, int x, int y)
            => pixels[(_host.TargetTexture.height - 1 - y) * _host.TargetTexture.width + x];

        // Where the first blue pixel after the first red one lies among length pixels, or -1.
        private static int BlueAfterRed(Func<int, Color32> pixelAt, int length)
        {
            var red = false;
            for (var at = 0; at < length; at++)
            {
                var pixel = pixelAt(at);
                red |= RenderTexturePixelReader.IsRedPixel(pixel);
                if (red && IsBlue(pixel))
                {
                    return at;
                }
            }
            return -1;
        }

        // The first red row down the panel's column x, or -1.
        private int FirstRedRow(Color32[] pixels, int x)
        {
            for (var y = 0; y < _host.TargetTexture.height; y++)
            {
                if (RenderTexturePixelReader.IsRedPixel(At(pixels, x, y)))
                {
                    return y;
                }
            }
            return -1;
        }

        private static int Apart(int byPercent, int byPixels)
            => byPercent >= 0 && byPixels >= 0 ? Math.Abs(byPixels - byPercent) : int.MaxValue;

        // How many columns apart the two elements' blue halves start along the row through the middle of each.
        private int ColumnsApart()
        {
            var pixels = Pixels();
            return Apart(BlueAfterRed(x => At(pixels, x, Height / 2), Width),
                BlueAfterRed(x => At(pixels, x, Height + Height / 2), Width));
        }

        // How many rows apart the two elements' red halves start down a column ten pixels into each.
        private int RowsApart()
        {
            var pixels = Pixels();
            return Apart(FirstRedRow(pixels, 10), FirstRedRow(pixels, Width + 10));
        }

        [UnityTest]
        public IEnumerator Given_AContainedImage_When_PlacedAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneColumn()
        {
            // Arrange — a square image contained in 120px by 60px is 60px wide.
            var image = Background.FromTexture2D(Halves(20, 20));

            // Act
            yield return AtFullWidth(image, SizedAs(new BackgroundSize(BackgroundSizeType.Contain)));

            // Assert
            Assert.That(ColumnsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_AWideContainedImage_When_PlacedDownAtAPercentAndAtRoomsShareOfIt_Then_BothTurnRedOnOneRow()
        {
            // Arrange — a 4:1 image contained in 120px by 60px is 30px high.
            var image = Background.FromTexture2D(Halves(40, 10));

            // Act
            yield return Drawn(image, SizedAs(new BackgroundSize(BackgroundSizeType.Contain)), true, 100f);

            // Assert
            Assert.That(RowsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_ACoveringImage_When_PlacedAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneColumn()
        {
            // Arrange — a 4:1 image covering 120px by 60px is 240px wide; a quarter along keeps its halves' edge in
            // the box, where contain would put it elsewhere.
            var image = Background.FromTexture2D(Halves(40, 10));

            // Act
            yield return Drawn(image, SizedAs(new BackgroundSize(BackgroundSizeType.Cover)), false, 25f);

            // Assert
            Assert.That(ColumnsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_ATallCoveringImage_When_PlacedDownAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneRow()
        {
            // Arrange — a 1:4 image covering 120px by 60px is 480px high; 45% down keeps its halves' edge in the box,
            // where contain would put it elsewhere.
            var image = Background.FromTexture2D(Halves(10, 40, stacked: true));

            // Act
            yield return Drawn(image, SizedAs(new BackgroundSize(BackgroundSizeType.Cover)), true, 45f);
            var pixels = Pixels();

            // Assert
            Assert.That(Apart(BlueAfterRed(y => At(pixels, 10, y), Height), BlueAfterRed(y => At(pixels, Width + 10, y), Height)),
                Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_ABackgroundSizeInPixels_When_PlacedAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneColumn()
        {
            // Arrange
            var image = Background.FromTexture2D(Halves(40, 20));

            // Act
            yield return AtFullWidth(image, SizedAs(new BackgroundSize(Length.Pixels(30f), Length.Pixels(10f))));

            // Assert
            Assert.That(ColumnsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_ABackgroundWidthInPercent_When_PlacedAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneColumn()
        {
            // Arrange
            var image = Background.FromTexture2D(Halves(40, 20));

            // Act
            yield return AtFullWidth(image, SizedAs(new BackgroundSize(Length.Percent(50f), Length.Auto())));

            // Assert
            Assert.That(ColumnsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_ABackgroundHeightAlone_When_PlacedAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneColumn()
        {
            // Arrange
            var image = Background.FromTexture2D(Halves(40, 20));

            // Act
            yield return AtFullWidth(image, SizedAs(new BackgroundSize(Length.Auto(), Length.Pixels(15f))));

            // Assert
            Assert.That(ColumnsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_ABackgroundSizeWithANoneHeight_When_PlacedAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneColumn()
        {
            // Arrange
            var image = Background.FromTexture2D(Halves(40, 20));

            // Act
            yield return AtFullWidth(image, SizedAs(new BackgroundSize(Length.Pixels(25f), Length.None())));

            // Assert
            Assert.That(ColumnsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_AHorizontalRoundRepeat_When_PlacedAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneColumn()
        {
            // Arrange — three 35px tiles round to 40px.
            var image = Background.FromTexture2D(Halves(40, 20));

            // Act
            yield return AtFullWidth(image, style =>
            {
                style.backgroundSize = new BackgroundSize(Length.Pixels(35f), Length.Pixels(10f));
                style.backgroundRepeat = new BackgroundRepeat(Repeat.Round, Repeat.NoRepeat);
            });

            // Assert
            Assert.That(ColumnsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_AVerticalRoundRepeatOfANaturalSize_When_PlacedAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneColumn()
        {
            // Arrange — two 30px rows in 60px widen a 40px by 25px image to 48px.
            var image = Background.FromTexture2D(Halves(40, 25));

            // Act
            yield return AtFullWidth(image, style =>
            {
                style.backgroundSize = Natural;
                style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.Round);
            });

            // Assert
            Assert.That(ColumnsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_AHorizontalRoundRepeatOfAnAutoHeight_When_PlacedDownAtAPercentAndAtRoomsShareOfIt_Then_BothTurnRedOnOneRow()
        {
            // Arrange — the image's 35px width rounds to 40px, and its height to 25px with it.
            var image = Background.FromTexture2D(Halves(40, 25));

            // Act
            yield return Drawn(image, style =>
            {
                style.backgroundSize = new BackgroundSize(Length.Pixels(35f), Length.Auto());
                style.backgroundRepeat = new BackgroundRepeat(Repeat.Round, Repeat.NoRepeat);
            }, true, 100f);

            // Assert
            Assert.That(RowsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_ASpriteAtItsOwnSize_When_PlacedAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneColumn()
        {
            // Arrange
            var sprite = Kept(Sprite.Create(Halves(40, 20), new Rect(0f, 0f, 40f, 20f), Vector2.zero, 100f, 0,
                SpriteMeshType.FullRect));

            // Act
            yield return AtFullWidth(Background.FromSprite(sprite), SizedAs(Natural));

            // Assert
            Assert.That(ColumnsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_ARenderTextureAtItsOwnSize_When_PlacedAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneColumn()
        {
            // Arrange
            var texture = Kept(new RenderTexture(40, 20, 0) { filterMode = FilterMode.Point });
            Graphics.Blit(Halves(40, 20), texture);

            // Act
            yield return AtFullWidth(Background.FromRenderTexture(texture), SizedAs(Natural));

            // Assert
            Assert.That(ColumnsApart(), Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_AVectorImageAtItsOwnSize_When_PlacedAtAPercentAndAtRoomsShareOfIt_Then_BothTurnBlueOnOneColumn()
        {
            // Arrange
            var image = Kept(ScriptableObject.CreateInstance<VectorImage>());
            using (var painter = new Painter2D())
            {
                painter.fillColor = new Color(1f, 0f, 0f, 1f);
                painter.BeginPath();
                painter.MoveTo(Vector2.zero);
                painter.LineTo(new Vector2(20f, 0f));
                painter.LineTo(new Vector2(20f, 20f));
                painter.LineTo(new Vector2(0f, 20f));
                painter.ClosePath();
                painter.Fill();
                painter.fillColor = new Color(0f, 0f, 1f, 1f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(20f, 0f));
                painter.LineTo(new Vector2(40f, 0f));
                painter.LineTo(new Vector2(40f, 20f));
                painter.LineTo(new Vector2(20f, 20f));
                painter.ClosePath();
                painter.Fill();
                painter.SaveToVectorImage(image);
            }

            // Act
            yield return AtFullWidth(Background.FromVectorImage(image), SizedAs(Natural));

            // Assert
            Assert.That(ColumnsApart(), Is.LessThanOrEqualTo(1));
        }
    }
}
