#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Paints the face of an element a layoutId projection draws scaled differently on each axis, its corners rounded
    /// by a radius per axis, so that they are drawn circular, as Framer's <c>correctBorderRadius</c> writes each
    /// corner <c>x% y%</c>.
    /// </summary>
    /// <remarks>
    /// UI Toolkit takes one length a corner and draws the background, the background image, the border and the
    /// overflow clip with it, so a corner it draws under such a scale is elliptical. While the projection draws one,
    /// its radius slots hold none (<see cref="LayoutIdLook.WriteRadii"/>), and the native background colour, border
    /// colours and background image are hidden and painted here instead, in the element's own
    /// generateVisualContent, ahead of anything the element paints there itself. A background or border a silhouette
    /// layer already repaints (<see cref="SilhouetteFace"/>) is left to it, and it reads the radii here.
    /// <para>
    /// An element that clips its content is given, in its background-image slot, a VectorImage of its border edge,
    /// which UI Toolkit stencils the content to. It is the border edge rather than the padding edge UI Toolkit clips to
    /// natively, since an element's own clip takes its own generated content (<see cref="RingOverlay"/>), and the
    /// border is painted here; so such an element with a border clips its content at the border edge.
    /// </para>
    /// <para>
    /// A background image is painted as a mesh over the part of its rect inside the border edge, drawn at the size
    /// its background-size gives where that is one of the three a <c>-unity-background-scale-mode</c> spells
    /// (stretched, cover or contain, centred, unrepeated); its edge along the rounded corners is not antialiased.
    /// One drawn otherwise — repeated, positioned, sized in lengths, nine-sliced, a vector image, or a sprite sliced or
    /// packed rotated — is not painted here, and the element's corners are left to UI Toolkit (<see cref="Draw"/>
    /// declines).
    /// </para>
    /// </remarks>
    internal static class ProjectedCornerFace
    {
        // Written to a hidden colour slot: nothing drawn, and distinct from an unset slot (SilhouetteFace.IsSentinel).
        internal static readonly Color Cleared = new(0f, 0f, 1f / 255f, 0f);

        private const int Sides = 4;

        private sealed class State
        {
            public CornerRadii Radii;
            public System.Action<MeshGenerationContext> OnGenerate = null!;

            // What each colour slot held before this wrote it, and the colour painted for it here. A slot a silhouette
            // layer holds its sentinel in is painted by that layer.
            public StyleColor OwnBackground;
            public bool PaintsBackground;
            public Color Background;
            public readonly StyleColor[] OwnBorders = new StyleColor[Sides];
            public bool PaintsBorder;
            public readonly Color[] Borders = new Color[Sides];

            public FaceImage? Image;
            public bool Clips;
            public bool TakesImageSlot;
            public StyleBackground OwnImage;
            public StyleBackgroundPosition OwnPositionX;
            public StyleBackgroundPosition OwnPositionY;
            public StyleBackgroundRepeat OwnRepeat;
            public StyleBackgroundSize OwnSize;
            public StyleBackground WrittenImage;

            // The clip is baked into one of two images in turn, so that the slot's value changes with each bake.
            public readonly VectorImage?[] ClipImages = new VectorImage?[2];
            public int ClipIndex;
            public Vector2 BakedSize = new(float.NaN, float.NaN);
            public CornerRadii BakedRadii;
        }

        // A background image as painted here: the texture, the part of it the image is, the size it is drawn from,
        // how it fills the box and its tint.
        internal readonly struct FaceImage
        {
            public FaceImage(Texture texture, Rect uv, Vector2 size, ScaleMode mode, Color tint)
            {
                Texture = texture;
                Uv = uv;
                Size = size;
                Mode = mode;
                Tint = tint;
            }

            public Texture Texture { get; }
            public Rect Uv { get; }
            public Vector2 Size { get; }
            public ScaleMode Mode { get; }
            public Color Tint { get; }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, State> s_states = new();

        // The radii the element is drawn with while this paints it, in its own box.
        public static bool TryGetRadii(VisualElement element, out CornerRadii radii)
        {
            if (s_states.TryGetValue(element, out var state))
            {
                radii = state.Radii;
                return true;
            }
            radii = default;
            return false;
        }

        public static bool Paints(VisualElement element) => s_states.TryGetValue(element, out _);

        // Paints the element's face with the given radii, fitted to its box, from now until End. Returns false, having
        // ended any face it painted, where the element's background image is one this does not paint.
        public static bool Draw(VisualElement element, in CornerRadii radii)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                if (!TryReadImage(element.resolvedStyle, out var image)) return false;
                state = Begin(element, image);
            }
            else if (!Resync(element, state))
            {
                End(element);
                return false;
            }
            state.Radii = radii;
            if (state.Clips) SyncClip(element, state);
            element.MarkDirtyRepaint();
            return true;
        }

        // Hands back every slot written here and stops painting.
        public static void End(VisualElement element)
        {
            if (!s_states.TryGetValue(element, out var state)) return;
            s_states.Remove(element);
            element.generateVisualContent -= state.OnGenerate;
            var style = element.style;
            style.backgroundColor = state.OwnBackground;
            style.borderTopColor = state.OwnBorders[0];
            style.borderRightColor = state.OwnBorders[1];
            style.borderBottomColor = state.OwnBorders[2];
            style.borderLeftColor = state.OwnBorders[3];
            if (state.TakesImageSlot)
            {
                style.backgroundImage = state.OwnImage;
                if (state.Clips)
                {
                    style.backgroundPositionX = state.OwnPositionX;
                    style.backgroundPositionY = state.OwnPositionY;
                    style.backgroundRepeat = state.OwnRepeat;
                    style.backgroundSize = state.OwnSize;
                }
            }
            foreach (var image in state.ClipImages)
            {
                if (image != null) ClipPathVectorImageBaker.DestroyImage(image);
            }
            ReleaseOnceRestored(element, state);
            element.MarkDirtyRepaint();
        }

        // The element's transitions take the face's slots back only once a style pass has drawn what End handed back, so
        // that the change from a hidden slot to the element's own colour is not one they run.
        private static void ReleaseOnceRestored(VisualElement element, State state)
        {
            if (element.panel == null)
            {
                MotionNativeTransitionGuard.Release(element, state);
                return;
            }
            var released = false;
            element.schedule.Execute(() =>
            {
                var resolved = element.resolvedStyle;
                if (SameColor(resolved.backgroundColor, Cleared) || SameColor(resolved.borderLeftColor, Cleared)) return;
                MotionNativeTransitionGuard.Release(element, state);
                released = true;
            }).Every(0).Until(() => released);
        }

        private static State Begin(VisualElement element, FaceImage? image)
        {
            var state = new State { Image = image, Clips = StyleCascade.ClipsOverflow(element) };
            var resolved = element.resolvedStyle;
            // Before any slot is written, so that hiding it is not a change the element's transitions run.
            MotionNativeTransitionGuard.NarrowIfIntercepted(element, state, MotionTransitionSlots.Face);
            TakeBackground(element, state, resolved.backgroundColor);
            TakeBorders(element, state, resolved, begin: true);
            state.TakesImageSlot = image != null || state.Clips;
            if (state.TakesImageSlot)
            {
                var style = element.style;
                state.OwnImage = style.backgroundImage;
                state.OwnPositionX = style.backgroundPositionX;
                state.OwnPositionY = style.backgroundPositionY;
                state.OwnRepeat = style.backgroundRepeat;
                state.OwnSize = style.backgroundSize;
                if (!state.Clips) WriteImage(element, state, new StyleBackground(StyleKeyword.None));
            }
            state.OnGenerate = mgc => Paint(mgc, element, state);
            InsertBeforeOwnPaint(element, state.OnGenerate);
            s_states.Add(element, state);
            return state;
        }

        // Takes back each slot something else wrote since the last pass, reading what it holds as the element's own.
        // False where the background image it now holds is one this does not paint.
        private static bool Resync(VisualElement element, State state)
        {
            var style = element.style;
            if (!IsCleared(style.backgroundColor)) TakeBackground(element, state, element.resolvedStyle.backgroundColor);
            if (!IsCleared(style.borderTopColor) || !IsCleared(style.borderRightColor) || !IsCleared(style.borderBottomColor)
                || !IsCleared(style.borderLeftColor))
            {
                TakeBorders(element, state, element.resolvedStyle, begin: false);
            }
            if (!state.TakesImageSlot || style.backgroundImage == state.WrittenImage) return true;
            var slot = style.backgroundImage;
            state.OwnImage = slot;
            if (slot.keyword == StyleKeyword.Undefined)
            {
                if (!TryReadImage(slot.value, element.resolvedStyle, out var image)) return false;
                state.Image = image;
            }
            else if (slot.keyword is StyleKeyword.None or StyleKeyword.Initial)
            {
                state.Image = null;
            }
            if (state.Clips) state.BakedSize = new Vector2(float.NaN, float.NaN);
            else WriteImage(element, state, new StyleBackground(StyleKeyword.None));
            return true;
        }

        private static bool IsCleared(StyleColor slot) => slot.keyword == StyleKeyword.Undefined && SameColor(slot.value, Cleared);

        private static bool SameColor(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        // resolved: the colour the element is drawn with, read only where the slot gives none of its own and it is not
        // one a hidden slot holds, which a style pass that has not run since the slot was written still shows.
        private static void TakeBackground(VisualElement element, State state, Color resolved)
        {
            var slot = element.style.backgroundColor;
            state.OwnBackground = slot;
            state.PaintsBackground = !IsLayerSentinel(slot);
            if (state.PaintsBackground) state.Background = OwnColor(slot, resolved, state.Background);
            element.style.backgroundColor = Cleared;
        }

        // Each side whose slot this has not cleared, or every side as the face begins.
        private static void TakeBorders(VisualElement element, State state, IResolvedStyle resolved, bool begin)
        {
            var style = element.style;
            TakeBorder(state, 0, style.borderTopColor, resolved.borderTopColor, begin);
            TakeBorder(state, 1, style.borderRightColor, resolved.borderRightColor, begin);
            TakeBorder(state, 2, style.borderBottomColor, resolved.borderBottomColor, begin);
            TakeBorder(state, 3, style.borderLeftColor, resolved.borderLeftColor, begin);
            state.PaintsBorder = !IsLayerSentinel(state.OwnBorders[3]);
            style.borderTopColor = Cleared;
            style.borderRightColor = Cleared;
            style.borderBottomColor = Cleared;
            style.borderLeftColor = Cleared;
        }

        private static void TakeBorder(State state, int side, StyleColor slot, Color resolved, bool begin)
        {
            if (!begin && IsCleared(slot)) return;
            state.OwnBorders[side] = slot;
            if (!IsLayerSentinel(slot)) state.Borders[side] = OwnColor(slot, resolved, state.Borders[side]);
        }

        private static bool IsLayerSentinel(StyleColor slot) =>
            slot.keyword == StyleKeyword.Undefined && SameColor(slot.value, SilhouetteFace.SuppressedColor);

        private static Color OwnColor(StyleColor slot, Color resolved, Color previous)
        {
            if (slot.keyword == StyleKeyword.Undefined) return slot.value;
            return SilhouetteFace.IsSentinel(resolved) ? previous : resolved;
        }

        private static void WriteImage(VisualElement element, State state, StyleBackground value)
        {
            element.style.backgroundImage = value;
            state.WrittenImage = value;
        }

        // A background image this paints, or none where the element has no image; false for one it does not paint.
        private static bool TryReadImage(IResolvedStyle resolved, out FaceImage? image) =>
            TryReadImage(resolved.backgroundImage, resolved, out image);

        private static bool TryReadImage(Background background, IResolvedStyle resolved, out FaceImage? image)
        {
            image = null;
            if (background.vectorImage != null) return false;
            Texture texture;
            var uv = new Rect(0f, 0f, 1f, 1f);
            Vector2 size;
            if (background.texture != null)
            {
                texture = background.texture;
                size = new Vector2(texture.width, texture.height);
            }
            else if (background.renderTexture != null)
            {
                texture = background.renderTexture;
                size = new Vector2(texture.width, texture.height);
            }
            else if (background.sprite != null)
            {
                var sprite = background.sprite;
                if (sprite.texture == null || sprite.border != Vector4.zero || sprite.packed && sprite.packingRotation != SpritePackingRotation.None)
                {
                    return false;
                }
                texture = sprite.texture;
                var rect = sprite.textureRect;
                uv = new Rect(rect.x / texture.width, rect.y / texture.height, rect.width / texture.width, rect.height / texture.height);
                size = sprite.rect.size;
            }
            else
            {
                return true;
            }
            if (resolved.unitySliceLeft != 0 || resolved.unitySliceTop != 0 || resolved.unitySliceRight != 0
                || resolved.unitySliceBottom != 0)
            {
                return false;
            }
            var mode = BackgroundPropertyHelper.ResolveUnityBackgroundScaleMode(resolved.backgroundPositionX,
                resolved.backgroundPositionY, resolved.backgroundRepeat, resolved.backgroundSize, out var valid);
            if (!valid || size.x <= 0f || size.y <= 0f) return false;
            image = new FaceImage(texture, uv, size, mode, resolved.unityBackgroundImageTintColor);
            return true;
        }

        // Ahead of the first paint the element registered itself, as a TextElement's text and an Image's picture are, and
        // of every callback after it, so that the face lies beneath them; after the callbacks before it.
        private static void InsertBeforeOwnPaint(VisualElement element, System.Action<MeshGenerationContext> paint)
        {
            System.Action<MeshGenerationContext>? before = null;
            System.Action<MeshGenerationContext>? after = null;
            if (element.generateVisualContent is { } existing)
            {
                foreach (var callback in existing.GetInvocationList())
                {
                    if (after != null || ReferenceEquals(callback.Target, element)) after += (System.Action<MeshGenerationContext>)callback;
                    else before += (System.Action<MeshGenerationContext>)callback;
                }
            }
            element.generateVisualContent = before + paint + after;
        }

        // Bakes the border edge again where the box or a radius has moved since the last bake.
        private static void SyncClip(VisualElement element, State state)
        {
            var size = element.layout.size;
            if (!(size.x > 0f && size.y > 0f)) return;
            var radii = BorderBandPainter.Fitted(state.Radii, size.x, size.y);
            if (Near(size, state.BakedSize) && Near(radii, state.BakedRadii)) return;
            var next = (state.ClipIndex + 1) % 2;
            var image = state.ClipImages[next] ??= ScriptableObject.CreateInstance<VectorImage>();
            image.name = "VelvetProjectedCornerClip";
            using (var painter = new Painter2D())
            {
                painter.fillColor = ClipPathVectorImageBaker.StencilFillColor;
                painter.BeginPath();
                SilhouetteFace.EmitRoundedRect(new SilhouetteFace.Painter2DSink(painter), new Rect(Vector2.zero, size), radii,
                    SilhouetteFace.Shearing.None);
                painter.ClosePath();
                painter.Fill();
                if (!painter.SaveToVectorImage(image)) return;
            }
            state.ClipIndex = next;
            state.BakedSize = size;
            state.BakedRadii = radii;
            var style = element.style;
            WriteImage(element, state, Background.FromVectorImage(image));
            style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, 0f);
            style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, 0f);
            style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            style.backgroundSize = new BackgroundSize(size.x, size.y);
        }

        // Within a hundredth of a pixel.
        private static bool Near(Vector2 a, Vector2 b) => Mathf.Abs(a.x - b.x) < 0.01f && Mathf.Abs(a.y - b.y) < 0.01f;

        private static bool Near(in CornerRadii a, in CornerRadii b) => Near(a.TopLeft, b.TopLeft) && Near(a.TopRight, b.TopRight)
            && Near(a.BottomRight, b.BottomRight) && Near(a.BottomLeft, b.BottomLeft);

        // Background, then its image, then the border, as CSS paints them.
        private static void Paint(MeshGenerationContext mgc, VisualElement element, State state)
        {
            var w = element.layout.width;
            var h = element.layout.height;
            if (!(w > 0f && h > 0f)) return;
            var box = new ShearedRoundedRect { Width = w, Height = h, Radii = state.Radii };
            var painter = mgc.painter2D;
            if (state.PaintsBackground && state.Background.a > 0.004f)
            {
                SilhouetteFace.BuildShearedRoundedRect(painter, box);
                painter.fillColor = state.Background;
                painter.Fill();
            }
            if (state.Image is { } image) PaintImage(mgc, image, box);
            if (state.PaintsBorder)
            {
                BorderBandPainter.Paint(painter, box, BorderSides.Of(element.resolvedStyle, state.Borders[3], state.Borders[0],
                    state.Borders[1], state.Borders[2]));
            }
        }

        private static readonly List<Vector2> s_contour = new();
        private static readonly List<Vector2> s_clipped = new();
        private static readonly List<Vector2> s_scratch = new();

        // A fan over the part of the image's rect inside the border edge.
        private static void PaintImage(MeshGenerationContext mgc, in FaceImage image, in ShearedRoundedRect box)
        {
            var rect = ImageRect(image, box.Width, box.Height);
            ImageOutline(box, rect, s_clipped);
            var count = s_clipped.Count;
            if (count < 3) return;
            var centre = Vector2.zero;
            foreach (var point in s_clipped) centre += point;
            centre /= count;
            var mesh = mgc.Allocate(count + 1, count * 3, image.Texture);
            mesh.SetNextVertex(VertexAt(centre, rect, image));
            foreach (var point in s_clipped) mesh.SetNextVertex(VertexAt(point, rect, image));
            for (var i = 0; i < count; i++)
            {
                mesh.SetNextIndex(0);
                mesh.SetNextIndex((ushort)(1 + i));
                mesh.SetNextIndex((ushort)(1 + ((i + 1) % count)));
            }
        }

        // The outline of the part of rect inside the border edge of box, clockwise, into outline.
        internal static void ImageOutline(in ShearedRoundedRect box, Rect rect, List<Vector2> outline)
        {
            BorderBandPainter.BuildContour(s_contour, new Rect(0f, 0f, box.Width, box.Height),
                BorderBandPainter.Fitted(box.Radii, box.Width, box.Height), SilhouetteFace.Shearing.None);
            outline.Clear();
            outline.AddRange(s_contour);
            for (var edge = 0; edge < 4; edge++)
            {
                ClipAgainst(outline, rect, edge);
            }
        }

        // How far p lies inside the edge of rect: 0 its left, 1 its right, 2 its top, 3 its bottom.
        private static float Inside(Vector2 p, Rect rect, int edge) => edge switch
        {
            0 => p.x - rect.xMin,
            1 => rect.xMax - p.x,
            2 => p.y - rect.yMin,
            _ => rect.yMax - p.y,
        };

        // Where the segment a-b crosses that edge's line.
        private static Vector2 Cross(Vector2 a, Vector2 b, Rect rect, int edge)
        {
            var t = edge switch
            {
                0 => (rect.xMin - a.x) / (b.x - a.x),
                1 => (rect.xMax - a.x) / (b.x - a.x),
                2 => (rect.yMin - a.y) / (b.y - a.y),
                _ => (rect.yMax - a.y) / (b.y - a.y),
            };
            return Vector2.LerpUnclamped(a, b, t);
        }

        // One Sutherland-Hodgman step: keeps the part of the polygon inside that edge.
        private static void ClipAgainst(List<Vector2> polygon, Rect rect, int edge)
        {
            s_scratch.Clear();
            for (var i = 0; i < polygon.Count; i++)
            {
                var current = polygon[i];
                var previous = polygon[(i + polygon.Count - 1) % polygon.Count];
                var currentIn = Inside(current, rect, edge) >= 0f;
                if (currentIn != Inside(previous, rect, edge) >= 0f) s_scratch.Add(Cross(previous, current, rect, edge));
                if (currentIn) s_scratch.Add(current);
            }
            polygon.Clear();
            polygon.AddRange(s_scratch);
        }

        // The rect the image is drawn in: the box, or the image's own proportions covering or contained by the box,
        // centred.
        internal static Rect ImageRect(in FaceImage image, float width, float height)
        {
            if (image.Mode == ScaleMode.StretchToFill) return new Rect(0f, 0f, width, height);
            var fit = image.Mode == ScaleMode.ScaleAndCrop
                ? Mathf.Max(width / image.Size.x, height / image.Size.y)
                : Mathf.Min(width / image.Size.x, height / image.Size.y);
            var size = image.Size * fit;
            return new Rect((width - size.x) * 0.5f, (height - size.y) * 0.5f, size.x, size.y);
        }

        // The image's v runs up where the box's y runs down.
        private static Vertex VertexAt(Vector2 point, Rect rect, in FaceImage image)
        {
            var u = (point.x - rect.xMin) / rect.width;
            var v = 1f - ((point.y - rect.yMin) / rect.height);
            return new Vertex
            {
                position = new Vector3(point.x, point.y, Vertex.nearZ),
                tint = image.Tint,
                uv = new Vector2(image.Uv.xMin + (u * image.Uv.width), image.Uv.yMin + (v * image.Uv.height)),
            };
        }
    }
}
