#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The tile an element's background image is drawn at: the image's own size under background-size, then refitted to a
    // whole count by a round repeat. BackgroundTilePlaybackTests holds the room it gives to the one UI Toolkit draws a
    // percentage across.
    internal static class BackgroundTile
    {
        // The room a background-position percentage is of on the axis: the element's size less the tile's. Zero where the
        // element shows no image.
        internal static float Room(VisualElement element, bool vertical)
        {
            var style = element.resolvedStyle;
            if (ImageSize(style.backgroundImage) is not { } image)
            {
                return 0f;
            }
            var box = element.layout.size;
            var tile = Rounded(Sized(image, box, style.backgroundSize), box, style.backgroundSize, style.backgroundRepeat);
            var axis = vertical ? 1 : 0;
            var repeat = vertical ? style.backgroundRepeat.y : style.backgroundRepeat.x;
            var room = box[axis] - (repeat == Repeat.Round ? box[axis] / Count(box[axis], tile[axis]) : tile[axis]);
            return float.IsFinite(room) ? room : 0f;
        }

        private static Vector2? ImageSize(Background background)
        {
            if (background.texture != null)
            {
                return new Vector2(background.texture.width, background.texture.height);
            }
            if (background.sprite != null)
            {
                return background.sprite.rect.size;
            }
            if (background.renderTexture != null)
            {
                return new Vector2(background.renderTexture.width, background.renderTexture.height);
            }
            if (background.vectorImage != null)
            {
                return new Vector2(background.vectorImage.width, background.vectorImage.height);
            }
            return null;
        }

        private static Vector2 Sized(Vector2 image, Vector2 box, BackgroundSize size)
        {
            if (size.sizeType != BackgroundSizeType.Length)
            {
                // MUTANT_SURVIVES(equivalent, boundary): where the two ratios are equal, both fits give the box.
                var widthFirst = box.x / image.x < box.y / image.y;
                return widthFirst == (size.sizeType == BackgroundSizeType.Contain)
                    ? new Vector2(box.x, image.y * box.x / image.x)
                    : new Vector2(image.x * box.y / image.y, box.y);
            }
            var x = size.x;
            var y = size.y;
            if (x.IsNone() || y.IsNone())
            {
                return image;
            }
            if (y.IsAuto())
            {
                return Resolved(x, box.x) is { } width ? new Vector2(width, width * image.y / image.x) : image;
            }
            var height = Resolved(y, box.y) ?? image.y;
            return x.IsAuto()
                ? new Vector2(height * image.x / image.y, height)
                : new Vector2(Resolved(x, box.x) ?? image.x, height);
        }

        // A round repeat on one axis, with the size auto on the other, keeps the image's proportions.
        private static Vector2 Rounded(Vector2 tile, Vector2 box, BackgroundSize size, BackgroundRepeat repeat)
        {
            if (size.x.IsAuto() && repeat.y == Repeat.Round)
            {
                var height = box.y / Count(box.y, tile.y);
                return new Vector2(height * tile.x / tile.y, height);
            }
            if (size.y.IsAuto() && repeat.x == Repeat.Round)
            {
                var width = box.x / Count(box.x, tile.x);
                return new Vector2(width, width * tile.y / tile.x);
            }
            return tile;
        }

        // The whole number of tiles a round repeat fits on the axis.
        private static int Count(float box, float tile) => Math.Max(1, (int)((box + tile * 0.5f) / tile));

        private static float? Resolved(Length length, float box)
            => length.unit == LengthUnit.Percent ? box * length.value / 100f
                : length.unit == LengthUnit.Pixel ? length.value : (float?)null;
    }
}
