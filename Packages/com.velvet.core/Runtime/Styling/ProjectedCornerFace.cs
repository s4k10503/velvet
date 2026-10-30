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
    /// generateVisualContent. What is painted is the element's own: an inline value it holds, else what its rules give
    /// beneath the slot written here, each colour carried as one value on the element's transition of it
    /// (<see cref="ColorCarry"/>).
    /// <para>
    /// An element that clips its content is given, in its background-image slot, a VectorImage of its border edge,
    /// placed by the background-position, background-repeat and background-size slots, which UI Toolkit stencils the
    /// content to. It is the border edge, so such an element with a border clips its content there rather than at the
    /// padding edge UI Toolkit clips to natively.
    /// </para>
    /// <para>
    /// A background image is painted as a mesh over the part of its rect inside the border edge, drawn at the size
    /// its background-size gives where that is one of the three a <c>-unity-background-scale-mode</c> spells
    /// (stretched, cover or contain, centred, unrepeated); its edge along the rounded corners is not antialiased.
    /// One drawn otherwise — repeated, positioned, sized in lengths, nine-sliced, a vector image, or a sprite sliced,
    /// packed rotated or packed tight — is not painted here, and the element's corners are left to UI Toolkit (<see cref="Draw"/>
    /// declines).
    /// </para>
    /// </remarks>
    internal static class ProjectedCornerFace
    {
        // Written to a hidden colour slot: nothing drawn, and distinct from an unset slot (SilhouetteFace.IsSentinel).
        internal static readonly Color Cleared = new(0f, 0f, 1f / 255f, 0f);

        // The colour slots, in StyleCascade.FaceColor's order: the background, then the top, right, bottom and left borders.
        private const int SlotCount = 5;
        private static readonly string[] s_properties =
            { "background-color", "border-top-color", "border-right-color", "border-bottom-color", "border-left-color" };
        private static readonly string?[] s_shorthands = { null, "border-color", "border-color", "border-color", "border-color" };

        // One colour slot hidden here: what it held of the element's own, what was last written to it here, and the
        // colour painted in its place.
        private sealed class ColorSlot
        {
            public StyleColor Own;
            public StyleColor Written;
            // A layer's suppression sentinel stands in the slot (SilhouetteFace.SuppressedColor), and that layer paints it.
            public bool Layer;
            public readonly ColorCarry Carry = new();

            public Color Value => Carry.Value;

            public bool Landed => Carry.Landed;
        }

        private sealed class State
        {
            public CornerRadii Radii;
            // The scale the element is drawn at on each axis, as the projection gave it.
            public Vector2 Scale = Vector2.one;
            public System.Action<MeshGenerationContext> OnGenerate = null!;
            public readonly ColorSlot[] Colors = { new(), new(), new(), new(), new() };

            public FaceImage? Image;
            public bool Clips;
            // Something else wrote an image or placement slot, or clipping began, since the clip's slots were written.
            public bool RewriteClip;
            // The callback list as this last left it, with the face first.
            public System.Action<MeshGenerationContext>? Installed;
            // The image slot and, where the face clips, its placement slots: what each held of the element's own and what
            // was last written to it here.
            public readonly InlineSlots Own = new();
            public StyleBackground WrittenImage;
            public StyleBackgroundPosition WrittenPositionX;
            public StyleBackgroundPosition WrittenPositionY;
            public StyleBackgroundRepeat WrittenRepeat;
            public StyleBackgroundSize WrittenSize;
            // The placement the element was drawn with as the face began, for a pass that cannot read its rules.
            public BackgroundLayer Begun;

            // The clip is baked into one of two images in turn, so that the slot's value changes with each bake.
            public readonly VectorImage?[] ClipImages = new VectorImage?[2];
            public int ClipIndex;
            public Vector2 BakedSize = new(float.NaN, float.NaN);
            public CornerRadii BakedRadii;

            // Painting stopped and the colours run on to the element's own (End), stepped by this.
            public IVisualElementScheduledItem? Tail;
            // Takes back a slot something else wrote at each panel update while this paints (Reassert).
            public IVisualElementScheduledItem? Guard;
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
            if (s_states.TryGetValue(element, out var state) && state.Tail == null)
            {
                radii = state.Radii;
                return true;
            }
            radii = default;
            return false;
        }

        // The radii TryGetRadii gives and the scale on each axis the element is drawn at with them, where that scale is
        // positive on both.
        public static bool TryGetProjection(VisualElement element, out CornerRadii radii, out Vector2 scale)
        {
            scale = Vector2.one;
            if (!TryGetRadii(element, out radii))
            {
                return false;
            }
            scale = s_states.TryGetValue(element, out var state) ? state.Scale : Vector2.one;
            return scale.x > 0f && scale.y > 0f;
        }

        // The colour a slot of the element's (StyleCascade.FaceColor's order) is drawn with while this holds it; null where
        // it does not.
        public static Color? Drawn(VisualElement element, int slot)
            => s_states.TryGetValue(element, out var state) && !state.Colors[slot].Layer ? state.Colors[slot].Value : null;

        // What the element itself holds in a colour slot this writes: inline, the slot's own value from before this wrote
        // it, else inline as it stands.
        public static StyleColor InlineOf(VisualElement element, int slot, StyleColor inline)
            => s_states.TryGetValue(element, out var state) ? state.Colors[slot].Own : inline;

        // Paints the element's face with the given radii, fitted to its box, until End, its colours carried over dtSec
        // more. Returns false, having ended any face it painted, where the element's background image is one this does
        // not paint.
        public static bool Draw(VisualElement element, in CornerRadii radii, float dtSec, Vector2? scale = null)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                if (!TryReadImage(OwnLayer(element, new InlineSlots().Take(element.style)), element.resolvedStyle, out _)) return false;
                state = Begin(element);
            }
            else if (state.Tail != null)
            {
                Resume(element, state);
            }
            else if (!ReferenceEquals(element.generateVisualContent, state.Installed))
            {
                element.generateVisualContent -= state.OnGenerate;
                InsertFirst(element, state);
            }
            if (!SyncImage(element, state))
            {
                End(element);
                return false;
            }
            for (var slot = 0; slot < SlotCount; slot++)
            {
                StepColor(element, state.Colors[slot], slot, dtSec);
                WriteColor(element, state.Colors[slot], slot, Cleared);
            }
            state.Radii = radii;
            state.Scale = scale ?? Vector2.one;
            if (state.Clips) SyncClip(element, state);
            element.MarkDirtyRepaint();
            return true;
        }

        // Stops painting and hands back the image slots at once; the colours run on from where they are drawn to the
        // element's own, written inline, and are handed back as they land.
        public static void End(VisualElement element)
        {
            if (!s_states.TryGetValue(element, out var state) || state.Tail != null) return;
            state.Guard?.Pause();
            element.generateVisualContent -= state.OnGenerate;
            HandBackImage(element, state);
            if (StepTail(element, state, 0f)) return;
            var last = 0L;
            state.Tail = element.schedule.Execute(timer =>
            {
                var dtSec = last == 0L ? 0f : (timer.now - last) / 1000f;
                last = timer.now;
                StepTail(element, state, dtSec);
            }).Every(StyleAnimateDriver.TickMs);
            element.MarkDirtyRepaint();
        }

        // Hands back every slot written here at once, for an element torn down.
        public static void Forget(VisualElement element)
        {
            if (!s_states.TryGetValue(element, out var state)) return;
            s_states.Remove(element);
            state.Tail?.Pause();
            state.Guard?.Pause();
            element.generateVisualContent -= state.OnGenerate;
            HandBackImage(element, state);
            for (var slot = 0; slot < SlotCount; slot++)
            {
                Write(element.style, slot, state.Colors[slot].Own);
            }
            MotionNativeTransitionGuard.Release(element, state);
            element.MarkDirtyRepaint();
        }

        private static State Begin(VisualElement element)
        {
            var state = new State();
            var resolved = element.resolvedStyle;
            state.Begun = new BackgroundLayer(resolved.backgroundImage, resolved.backgroundPositionX, resolved.backgroundPositionY,
                resolved.backgroundRepeat, resolved.backgroundSize);
            // Before any slot is written, so that hiding it is not a change the element's transitions run.
            MotionNativeTransitionGuard.NarrowIfIntercepted(element, state, MotionTransitionSlots.Face);
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var color = state.Colors[slot];
                color.Own = color.Written = Read(element.style, slot);
                color.Layer = IsLayerSentinel(color.Own);
                // From where it is drawn, which a transition of it running now has carried part way.
                var drawn = Resolved(resolved, slot);
                color.Carry.Land(drawn);
            }
            var style = element.style;
            state.Own.Take(style);
            state.WrittenImage = style.backgroundImage;
            state.WrittenPositionX = style.backgroundPositionX;
            state.WrittenPositionY = style.backgroundPositionY;
            state.WrittenRepeat = style.backgroundRepeat;
            state.WrittenSize = style.backgroundSize;
            state.OnGenerate = mgc => Paint(mgc, element, state);
            InsertFirst(element, state);
            s_states.Add(element, state);
            state.Guard = element.schedule.Execute(() => Reassert(element, state)).Every(0);
            return state;
        }

        // Takes the face back from a tail that was running its colours on to the element's own.
        private static void Resume(VisualElement element, State state)
        {
            state.Tail!.Pause();
            state.Tail = null;
            var style = element.style;
            state.Own.Image = state.WrittenImage = style.backgroundImage;
            state.RewriteClip = true;
            InsertFirst(element, state);
            state.Guard?.Resume();
        }

        // The projection's pass runs every StyleAnimateDriver.TickMs and a write lands at any time, so a slot something
        // else wrote is taken back here, at each update of the panel, rather than left to that pass: until the clip is
        // written back over the image slot, the element clips to its native, unrounded box
        // (Given_AnOverflowHiddenRoundedLayoutIdMotionHeldByItsDelay_When_ItsInlineBackgroundImageChanges_Then_ItsChildStaysClippedAtTheCircle).
        private static void Reassert(VisualElement element, State state)
        {
            if (state.Tail != null || !WrittenElsewhere(element.style, state)) return;
            if (!SyncImage(element, state))
            {
                End(element);
                return;
            }
            if (state.Clips) SyncClip(element, state);
            element.MarkDirtyRepaint();
        }

        private static bool WrittenElsewhere(IStyle style, State state)
            => style.backgroundImage != state.WrittenImage || style.backgroundPositionX != state.WrittenPositionX
                || style.backgroundPositionY != state.WrittenPositionY || style.backgroundRepeat != state.WrittenRepeat
                || style.backgroundSize != state.WrittenSize;

        // Steps the colours on to the element's own, writing each inline until it lands and then its own; true once
        // every one has, when the face is done with the element.
        private static bool StepTail(VisualElement element, State state, float dtSec)
        {
            var landed = true;
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var color = state.Colors[slot];
                StepColor(element, color, slot, dtSec);
                landed &= color.Layer || color.Landed;
                WriteColor(element, color, slot, color.Layer || color.Landed ? color.Own : new StyleColor(color.Value));
            }
            if (!landed) return false;
            state.Tail?.Pause();
            s_states.Remove(element);
            ReleaseOnceRestored(element, state);
            return true;
        }

        // The element's transitions take the face's slots back only once a style pass has drawn what was handed back, so
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

        // Takes the slot back as the element's own where something else wrote it since this did, and carries the colour
        // painted for it on towards the element's own over dtSec more.
        private static void StepColor(VisualElement element, ColorSlot color, int slot, float dtSec)
        {
            var held = Read(element.style, slot);
            if (held != color.Written)
            {
                color.Own = held;
                color.Layer = IsLayerSentinel(held);
            }
            if (color.Layer) return;
            var target = OwnColor(element, color, slot);
            color.Carry.Step(target, dtSec, element, s_properties[slot], s_shorthands[slot]);
        }

        // An inline value the element holds, else what its rules give beneath the slot, else where it is carried to now.
        private static Color OwnColor(VisualElement element, ColorSlot color, int slot)
        {
            if (color.Own.keyword == StyleKeyword.Undefined) return color.Own.value;
            if (StyleCascade.FaceColor(element, slot) is { } cascaded) return cascaded;
            return color.Carry.Target;
        }

        private static void WriteColor(VisualElement element, ColorSlot color, int slot, StyleColor value)
        {
            Write(element.style, slot, value);
            color.Written = value;
        }

        private static StyleColor Read(IStyle style, int slot) => slot switch
        {
            0 => style.backgroundColor,
            1 => style.borderTopColor,
            2 => style.borderRightColor,
            3 => style.borderBottomColor,
            _ => style.borderLeftColor,
        };

        private static void Write(IStyle style, int slot, StyleColor value)
        {
            switch (slot)
            {
                case 0: style.backgroundColor = value; break;
                case 1: style.borderTopColor = value; break;
                case 2: style.borderRightColor = value; break;
                case 3: style.borderBottomColor = value; break;
                default: style.borderLeftColor = value; break;
            }
        }

        private static Color Resolved(IResolvedStyle style, int slot) => slot switch
        {
            0 => style.backgroundColor,
            1 => style.borderTopColor,
            2 => style.borderRightColor,
            3 => style.borderBottomColor,
            _ => style.borderLeftColor,
        };

        private static bool SameColor(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        private static bool IsLayerSentinel(StyleColor slot) =>
            slot.keyword == StyleKeyword.Undefined && SameColor(slot.value, SilhouetteFace.SuppressedColor);

        // Takes back each image slot something else wrote since this did, and reads whether the element clips its content
        // now; reads the element's own image off its own slots and, beneath them, its rules. Where it clips, a slot
        // something else wrote has the clip's written over it again (SyncClip); where it does not, the image slot holds
        // none and the placement slots its own. False where the element's own image is one this does not paint.
        private static bool SyncImage(VisualElement element, State state)
        {
            var style = element.style;
            var own = state.Own;
            var foreign = false;
            if (style.backgroundImage != state.WrittenImage) (own.Image, foreign) = (style.backgroundImage, true);
            if (style.backgroundPositionX != state.WrittenPositionX) (own.X, foreign) = (style.backgroundPositionX, true);
            if (style.backgroundPositionY != state.WrittenPositionY) (own.Y, foreign) = (style.backgroundPositionY, true);
            if (style.backgroundRepeat != state.WrittenRepeat) (own.Repeat, foreign) = (style.backgroundRepeat, true);
            if (style.backgroundSize != state.WrittenSize) (own.Size, foreign) = (style.backgroundSize, true);
            if (!TryReadImage(OwnLayer(element, own, state.Begun), element.resolvedStyle, out var image)) return false;
            state.Image = image;
            var clips = StyleCascade.ClipsOverflow(element);
            if (clips && (foreign || !state.Clips)) state.RewriteClip = true;
            if (!clips && state.Clips) HandBackPlacement(element, state);
            state.Clips = clips;
            if (!clips) style.backgroundImage = state.WrittenImage = new StyleBackground(StyleKeyword.None);
            return true;
        }

        // The placement slots back to the element's own, which no longer place a clip.
        private static void HandBackPlacement(VisualElement element, State state)
        {
            var style = element.style;
            style.backgroundPositionX = state.WrittenPositionX = state.Own.X;
            style.backgroundPositionY = state.WrittenPositionY = state.Own.Y;
            style.backgroundRepeat = state.WrittenRepeat = state.Own.Repeat;
            TransformOriginSparing.SetBackgroundSize(element, state.WrittenSize = state.Own.Size);
        }

        // An element's own inline image slot and placement slots.
        private sealed class InlineSlots
        {
            public StyleBackground Image;
            public StyleBackgroundPosition X;
            public StyleBackgroundPosition Y;
            public StyleBackgroundRepeat Repeat;
            public StyleBackgroundSize Size;

            public InlineSlots Take(IStyle style)
            {
                (Image, X, Y) = (style.backgroundImage, style.backgroundPositionX, style.backgroundPositionY);
                (Repeat, Size) = (style.backgroundRepeat, style.backgroundSize);
                return this;
            }
        }

        // The element's own image and placement: each inline slot it holds, else what its rules give, else what it is
        // drawn with, or was as the face began.
        private static BackgroundLayer OwnLayer(VisualElement element, InlineSlots own, BackgroundLayer? drawn = null)
        {
            var resolved = element.resolvedStyle;
            var rules = StyleCascade.BackgroundOf(element) ?? drawn ?? new BackgroundLayer(resolved.backgroundImage,
                resolved.backgroundPositionX, resolved.backgroundPositionY, resolved.backgroundRepeat, resolved.backgroundSize);
            var image = own.Image.keyword == StyleKeyword.Undefined ? own.Image.value
                : own.Image.keyword is StyleKeyword.None or StyleKeyword.Initial ? default : rules.Image;
            return new BackgroundLayer(image,
                own.X.keyword == StyleKeyword.Undefined ? own.X.value : rules.X,
                own.Y.keyword == StyleKeyword.Undefined ? own.Y.value : rules.Y,
                own.Repeat.keyword == StyleKeyword.Undefined ? own.Repeat.value : rules.Repeat,
                own.Size.keyword == StyleKeyword.Undefined ? own.Size.value : rules.Size);
        }

        private static void HandBackImage(VisualElement element, State state)
        {
            var style = element.style;
            style.backgroundImage = state.Own.Image;
            if (state.Clips) HandBackPlacement(element, state);
            for (var i = 0; i < state.ClipImages.Length; i++)
            {
                if (state.ClipImages[i] is { } image) ClipPathVectorImageBaker.DestroyImage(image);
                state.ClipImages[i] = null;
            }
        }

        // A background image this paints, or none where the layer holds no image; false for one it does not paint.
        private static bool TryReadImage(in BackgroundLayer layer, IResolvedStyle resolved, out FaceImage? image)
        {
            image = null;
            var background = layer.Image;
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
                if (sprite.texture == null || sprite.border != Vector4.zero
                    || sprite.packed && (sprite.packingRotation != SpritePackingRotation.None || sprite.packingMode == SpritePackingMode.Tight))
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
            var mode = BackgroundPropertyHelper.ResolveUnityBackgroundScaleMode(layer.X, layer.Y, layer.Repeat, layer.Size, out var valid);
            if (!valid || size.x <= 0f || size.y <= 0f) return false;
            image = new FaceImage(texture, uv, size, mode, resolved.unityBackgroundImageTintColor);
            return true;
        }

        // First, so that the face lies beneath what the element paints in its own content: its text or picture, and a
        // dashed divider StyleDivideManipulator's painter draws on its edge
        // (Given_ADividedLayoutIdMotionDrawnAQuarterAcross_When_ItsFaceIsPainted_Then_ItsDashedDividerShowsOverIt). Draw
        // puts it first again on a pass that finds the list changed since.
        private static void InsertFirst(VisualElement element, State state)
        {
            element.generateVisualContent = state.OnGenerate + element.generateVisualContent;
            state.Installed = element.generateVisualContent;
        }

        // Bakes the border edge again where the box or a radius has moved since the last bake, and writes the clip and its
        // placement over the slots where it baked or something else wrote them (SyncImage).
        private static void SyncClip(VisualElement element, State state)
        {
            var size = element.layout.size;
            if (!(size.x > 0f && size.y > 0f)) return;
            var radii = BorderBandPainter.Fitted(state.Radii, size.x, size.y);
            if (!Near(size, state.BakedSize) || !Near(radii, state.BakedRadii))
            {
                var next = (state.ClipIndex + 1) % 2;
                var baked = state.ClipImages[next] ??= ScriptableObject.CreateInstance<VectorImage>();
                baked.name = "VelvetProjectedCornerClip";
                using (var painter = new Painter2D())
                {
                    painter.fillColor = ClipPathVectorImageBaker.StencilFillColor;
                    painter.BeginPath();
                    SilhouetteFace.EmitRoundedRect(new SilhouetteFace.Painter2DSink(painter), new Rect(Vector2.zero, size), radii,
                        SilhouetteFace.Shearing.None);
                    painter.ClosePath();
                    painter.Fill();
                    if (!painter.SaveToVectorImage(baked)) return;
                }
                (state.ClipIndex, state.BakedSize, state.BakedRadii, state.RewriteClip) = (next, size, radii, true);
            }
            if (!state.RewriteClip || state.ClipImages[state.ClipIndex] is not { } image) return;
            state.RewriteClip = false;
            var style = element.style;
            style.backgroundImage = state.WrittenImage = Background.FromVectorImage(image);
            style.backgroundPositionX = state.WrittenPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, 0f);
            style.backgroundPositionY = state.WrittenPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, 0f);
            style.backgroundRepeat = state.WrittenRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            TransformOriginSparing.SetBackgroundSize(element, state.WrittenSize = new BackgroundSize(size.x, size.y));
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
            var background = state.Colors[0];
            if (!background.Layer && background.Value.a > 0.004f)
            {
                SilhouetteFace.BuildShearedRoundedRect(painter, box);
                painter.fillColor = background.Value;
                painter.Fill();
            }
            if (state.Image is { } image) PaintImage(mgc, image, box);
            BorderBandPainter.Paint(painter, box, BorderSides.Of(element.resolvedStyle, Painted(state.Colors[4]),
                Painted(state.Colors[1]), Painted(state.Colors[2]), Painted(state.Colors[3])));
        }

        // A side a layer paints is left to it.
        private static Color Painted(ColorSlot color) => color.Layer ? Color.clear : color.Value;

        private static readonly List<Vector2> s_contour = new();
        private static readonly List<Vector2> s_clipped = new();
        private static readonly Vector2[] s_rect = new Vector2[4];

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

        // The outline of the part of rect inside the border edge of box, into outline.
        private static void ImageOutline(in ShearedRoundedRect box, Rect rect, List<Vector2> outline)
        {
            BorderBandPainter.BuildContour(s_contour, new Rect(0f, 0f, box.Width, box.Height),
                BorderBandPainter.Fitted(box.Radii, box.Width, box.Height), SilhouetteFace.Shearing.None);
            s_rect[0] = new Vector2(rect.xMin, rect.yMin);
            s_rect[1] = new Vector2(rect.xMax, rect.yMin);
            s_rect[2] = new Vector2(rect.xMax, rect.yMax);
            s_rect[3] = new Vector2(rect.xMin, rect.yMax);
            DashedBorderPainter.ClipConvex(s_contour, s_rect, outline);
        }

        // The rect the image is drawn in: the box, or the image's own proportions covering or contained by the box,
        // centred.
        private static Rect ImageRect(in FaceImage image, float width, float height)
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
