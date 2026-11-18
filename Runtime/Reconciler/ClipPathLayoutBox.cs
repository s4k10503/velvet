#nullable enable
using System;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // The clip wrapper takes its inner element's place in the parent's layout outright: it carries the element's
    // classes and the inline values Velvet writes to place or size the element, the element keeps its paint,
    // and the element fills the wrapper. Declarations are moved rather than resolved values copied, so the wrapper
    // resolves them against the element's real parent in the same style pass the element would have.
    internal static class ClipPathLayoutBox
    {
        private enum Role
        {
            // Places, sizes or hides the element in its parent: moved onto the wrapper, held neutral on the element.
            Outer,
            // Would paint on the wrapper or offset the element inside it: held inert on the wrapper.
            Reset,
            // Written by FiberClipPathApplier for the mask.
            Owned,
            // Copied onto the wrapper and left on the element too.
            Shared,
            // Left to the mirrored classes: an inherited one computes on the element as it would without the
            // wrapper, and the rest style text, picking, padding, slices or a transform, none of which the
            // wrapper has.
            Kept,
        }

        private readonly struct Entry
        {
            public readonly StyleLonghand Longhand;
            public readonly Role Role;
            // Outer and Shared: copies the inline value from one style to the other.
            public readonly Action<IStyle, IStyle>? Transfer;
            // Outer: the value held on the element. Reset: the value held on the wrapper.
            public readonly Action<IStyle>? Write;

            public Entry(StyleLonghand longhand, Role role, Action<IStyle, IStyle>? move = null, Action<IStyle>? write = null)
            {
                Longhand = longhand;
                Role = role;
                Transfer = move;
                Write = write;
            }
        }

        // One entry per USS longhand; ClipPathLayoutBoxRoleTests fails when a longhand has none or two.
        private static readonly Entry[] s_entries =
        {
            Outer(StyleLonghand.Position, (f, t) => t.position = f.position, s => s.position = Position.Relative),
            Outer(StyleLonghand.Top, (f, t) => t.top = f.top, s => s.top = StyleKeyword.Auto),
            Outer(StyleLonghand.Right, (f, t) => t.right = f.right, s => s.right = StyleKeyword.Auto),
            Outer(StyleLonghand.Bottom, (f, t) => t.bottom = f.bottom, s => s.bottom = StyleKeyword.Auto),
            Outer(StyleLonghand.Left, (f, t) => t.left = f.left, s => s.left = StyleKeyword.Auto),
            Outer(StyleLonghand.MarginTop, (f, t) => t.marginTop = f.marginTop, s => s.marginTop = 0f),
            Outer(StyleLonghand.MarginRight, (f, t) => t.marginRight = f.marginRight, s => s.marginRight = 0f),
            Outer(StyleLonghand.MarginBottom, (f, t) => t.marginBottom = f.marginBottom, s => s.marginBottom = 0f),
            Outer(StyleLonghand.MarginLeft, (f, t) => t.marginLeft = f.marginLeft, s => s.marginLeft = 0f),
            Outer(StyleLonghand.Width, (f, t) => t.width = f.width, s => s.width = StyleKeyword.Auto),
            Outer(StyleLonghand.Height, (f, t) => t.height = f.height, s => s.height = StyleKeyword.Auto),
            Outer(StyleLonghand.MinWidth, (f, t) => t.minWidth = f.minWidth, s => s.minWidth = StyleKeyword.Auto),
            Outer(StyleLonghand.MinHeight, (f, t) => t.minHeight = f.minHeight, s => s.minHeight = StyleKeyword.Auto),
            Outer(StyleLonghand.MaxWidth, (f, t) => t.maxWidth = f.maxWidth, s => s.maxWidth = StyleKeyword.None),
            Outer(StyleLonghand.MaxHeight, (f, t) => t.maxHeight = f.maxHeight, s => s.maxHeight = StyleKeyword.None),
            // The element fills the wrapper along its column by growing and shrinking into it, and across it by
            // stretching.
            Outer(StyleLonghand.FlexGrow, (f, t) => t.flexGrow = f.flexGrow, s => s.flexGrow = 1f),
            Outer(StyleLonghand.FlexShrink, (f, t) => t.flexShrink = f.flexShrink, s => s.flexShrink = 1f),
            Outer(StyleLonghand.FlexBasis, (f, t) => t.flexBasis = f.flexBasis, s => s.flexBasis = StyleKeyword.Auto),
            Outer(StyleLonghand.AlignSelf, (f, t) => t.alignSelf = f.alignSelf, s => s.alignSelf = Align.Stretch),
            Outer(StyleLonghand.AspectRatio, (f, t) => t.aspectRatio = f.aspectRatio, s => s.aspectRatio = StyleKeyword.Auto),
            // An inline display: none has to hide the wrapper, or the wrapper keeps the element's slot.
            Outer(StyleLonghand.Display, (f, t) => t.display = f.display, s => s.display = StyleKeyword.Null),

            Reset(StyleLonghand.FlexDirection, s => s.flexDirection = FlexDirection.Column),
            Reset(StyleLonghand.FlexWrap, s => s.flexWrap = Wrap.NoWrap),
            Reset(StyleLonghand.JustifyContent, s => s.justifyContent = Justify.FlexStart),
            Reset(StyleLonghand.AlignItems, s => s.alignItems = Align.Stretch),
            Reset(StyleLonghand.AlignContent, s => s.alignContent = Align.FlexStart),
            Reset(StyleLonghand.PaddingTop, s => s.paddingTop = 0f),
            Reset(StyleLonghand.PaddingRight, s => s.paddingRight = 0f),
            Reset(StyleLonghand.PaddingBottom, s => s.paddingBottom = 0f),
            Reset(StyleLonghand.PaddingLeft, s => s.paddingLeft = 0f),
            Reset(StyleLonghand.BorderTopWidth, s => s.borderTopWidth = 0f),
            Reset(StyleLonghand.BorderRightWidth, s => s.borderRightWidth = 0f),
            Reset(StyleLonghand.BorderBottomWidth, s => s.borderBottomWidth = 0f),
            Reset(StyleLonghand.BorderLeftWidth, s => s.borderLeftWidth = 0f),
            Reset(StyleLonghand.BorderTopColor, s => s.borderTopColor = UnityEngine.Color.clear),
            Reset(StyleLonghand.BorderRightColor, s => s.borderRightColor = UnityEngine.Color.clear),
            Reset(StyleLonghand.BorderBottomColor, s => s.borderBottomColor = UnityEngine.Color.clear),
            Reset(StyleLonghand.BorderLeftColor, s => s.borderLeftColor = UnityEngine.Color.clear),
            // A radius would round the mask as well as the wrapper's own paint.
            Reset(StyleLonghand.BorderTopLeftRadius, s => s.borderTopLeftRadius = 0f),
            Reset(StyleLonghand.BorderTopRightRadius, s => s.borderTopRightRadius = 0f),
            Reset(StyleLonghand.BorderBottomLeftRadius, s => s.borderBottomLeftRadius = 0f),
            Reset(StyleLonghand.BorderBottomRightRadius, s => s.borderBottomRightRadius = 0f),
            Reset(StyleLonghand.BackgroundColor, s => s.backgroundColor = UnityEngine.Color.clear),
            // FiberClipPathApplier writes the mask over this whenever a clip is active.
            Reset(StyleLonghand.BackgroundImage, s => s.backgroundImage = StyleKeyword.None),
            Reset(StyleLonghand.UnityBackgroundImageTintColor, s => s.unityBackgroundImageTintColor = UnityEngine.Color.white),
            // Slices would nine-slice the mask image.
            Reset(StyleLonghand.UnitySliceTop, s => s.unitySliceTop = 0),
            Reset(StyleLonghand.UnitySliceRight, s => s.unitySliceRight = 0),
            Reset(StyleLonghand.UnitySliceBottom, s => s.unitySliceBottom = 0),
            Reset(StyleLonghand.UnitySliceLeft, s => s.unitySliceLeft = 0),
            Reset(StyleLonghand.UnityMaterial, s => s.unityMaterial = StyleKeyword.None),
            // The element keeps these itself; on the wrapper as well they would apply twice.
            Reset(StyleLonghand.Opacity, s => s.opacity = 1f),
            Reset(StyleLonghand.Translate, s => s.translate = StyleKeyword.None),
            Reset(StyleLonghand.Rotate, s => s.rotate = StyleKeyword.None),
            Reset(StyleLonghand.Scale, s => s.scale = StyleKeyword.None),
            Reset(StyleLonghand.Filter, s => s.filter = new System.Collections.Generic.List<FilterFunction>()),

            new(StyleLonghand.BackgroundPositionX, Role.Owned),
            new(StyleLonghand.BackgroundPositionY, Role.Owned),
            new(StyleLonghand.BackgroundRepeat, Role.Owned),
            new(StyleLonghand.BackgroundSize, Role.Owned),
            new(StyleLonghand.Overflow, Role.Owned),
            new(StyleLonghand.Visibility, Role.Owned),

            new(StyleLonghand.Color, Role.Kept),
            new(StyleLonghand.FontSize, Role.Kept),
            new(StyleLonghand.LetterSpacing, Role.Kept),
            new(StyleLonghand.WordSpacing, Role.Kept),
            new(StyleLonghand.WhiteSpace, Role.Kept),
            new(StyleLonghand.TextShadow, Role.Kept),
            new(StyleLonghand.UnityFont, Role.Kept),
            new(StyleLonghand.UnityFontDefinition, Role.Kept),
            new(StyleLonghand.UnityFontStyle, Role.Kept),
            new(StyleLonghand.UnityParagraphSpacing, Role.Kept),
            new(StyleLonghand.UnityTextAlign, Role.Kept),
            new(StyleLonghand.UnityTextGenerator, Role.Kept),
            new(StyleLonghand.UnityTextOutlineColor, Role.Kept),
            new(StyleLonghand.UnityTextOutlineWidth, Role.Kept),
            new(StyleLonghand.UnityEditorTextRenderingMode, Role.Kept),
            new(StyleLonghand.UnityTextAutoSize, Role.Kept),
            new(StyleLonghand.TextOverflow, Role.Kept),
            new(StyleLonghand.UnityTextOverflowPosition, Role.Kept),
            new(StyleLonghand.Cursor, Role.Kept),
            new(StyleLonghand.UnityOverflowClipBox, Role.Kept),
            new(StyleLonghand.UnitySliceScale, Role.Kept),
            new(StyleLonghand.UnitySliceType, Role.Kept),
            new(StyleLonghand.TransformOrigin, Role.Kept),
            // The wrapper animates the element's layout with the transition classes it mirrors and the duration
            // StyleArbitraryValueResolver writes to both; an inline transition a Motion or the animation
            // scheduler writes stays on the element.
            new(StyleLonghand.TransitionProperty, Role.Kept),
            new(StyleLonghand.TransitionDuration, Role.Shared, (f, t) => t.transitionDuration = f.transitionDuration),
            new(StyleLonghand.TransitionTimingFunction, Role.Kept),
            new(StyleLonghand.TransitionDelay, Role.Kept),
        };

        private static readonly StyleLonghandSet s_outer = BuildOuterSet();

        // Indexed by ArbitraryProperty: whether every longhand the property writes is an outer one.
        private static readonly bool[] s_outerProperty = BuildOuterProperties();

        private static readonly ConditionalWeakTable<VisualElement, ClipPathBinding> s_byInner = new();
        private static readonly ConditionalWeakTable<VisualElement, ClipPathBinding> s_byWrapper = new();

        private static Entry Outer(StyleLonghand longhand, Action<IStyle, IStyle> move, Action<IStyle> neutral)
            => new(longhand, Role.Outer, move, neutral);

        private static Entry Reset(StyleLonghand longhand, Action<IStyle> inert)
            => new(longhand, Role.Reset, write: inert);

        // The element the parent lays out in the element's place. Registrations are never dropped, so the parent
        // check is what stops an unwrapped or pooled element writing onto the wrapper it left.
        internal static VisualElement Of(VisualElement element)
        {
            var binding = Registered(s_byInner, element);
            return binding != null && element.parent == binding.Wrapper ? binding.Wrapper : element;
        }

        // The inverse of Of: the clipped element a clip wrapper stands in for, else outer itself.
        internal static VisualElement InnerOf(VisualElement outer)
        {
            var binding = Registered(s_byWrapper, outer);
            return binding != null ? binding.Inner : outer;
        }

        // The element an inline write of property to element belongs on.
        internal static VisualElement BoxFor(VisualElement element, ArbitraryProperty property)
            => s_outerProperty[(int)property] ? Of(element) : element;

        internal static IStyle StyleFor(VisualElement element, ArbitraryProperty property) => BoxFor(element, property).style;

        // Runs once the element is the wrapper's child: moves the element's outer-layout inline values onto the
        // wrapper, holds the element neutral, holds the wrapper's paint inert and mirrors the classes.
        internal static void Adopt(VisualElement element, ClipPathBinding binding)
        {
            s_byInner.AddOrUpdate(element, binding);
            s_byWrapper.AddOrUpdate(binding.Wrapper, binding);

            var inner = element.style;
            var outer = binding.Wrapper.style;
            foreach (var entry in s_entries)
            {
                if (entry.Role == Role.Outer)
                {
                    entry.Transfer!(inner, outer);
                    entry.Write!(inner);
                }
                else if (entry.Role == Role.Shared)
                {
                    entry.Transfer!(inner, outer);
                }
                else if (entry.Role == Role.Reset)
                {
                    entry.Write!(outer);
                }
            }
            SyncClasses(element);
        }

        // The reverse of Adopt, run while the element is still the wrapper's child: hands the wrapper's
        // outer-layout inline values back to the element, including any a slot writer put on the wrapper.
        internal static void Release(VisualElement element, ClipPathBinding binding)
        {
            var inner = element.style;
            var outer = binding.Wrapper.style;
            foreach (var entry in s_entries)
            {
                if (entry.Role == Role.Outer)
                {
                    entry.Transfer!(outer, inner);
                }
            }
        }

        // Brings the wrapper's class list level with the element's live one. Only the classes this mirror added
        // are ever removed, so a class something else put on the wrapper stays.
        internal static void SyncClasses(VisualElement element)
        {
            var binding = Registered(s_byInner, element);
            // MUTANT_SURVIVES(equivalent, clause removed): an element leaves its wrapper by an unwrap or a removal,
            // and both take the wrapper out of the tree, so classes mirrored onto it after that reach nothing.
            if (binding == null || element.parent != binding.Wrapper)
            {
                return;
            }
            var wrapper = binding.Wrapper;
            var mirrored = binding.MirroredClasses;
            var live = new System.Collections.Generic.HashSet<string>(element.GetClasses());
            if (mirrored != null)
            {
                foreach (var cls in mirrored)
                {
                    if (!live.Contains(cls))
                    {
                        wrapper.RemoveFromClassList(cls);
                    }
                }
            }
            foreach (var cls in live)
            {
                if (mirrored == null || !mirrored.Contains(cls))
                {
                    wrapper.AddToClassList(cls);
                }
            }
            binding.MirroredClasses = live;
        }

        private static ClipPathBinding? Registered(ConditionalWeakTable<VisualElement, ClipPathBinding> table,
            VisualElement? key)
        {
            if (key == null)
            {
                return null;
            }
            return table.TryGetValue(key, out var binding) ? binding : null;
        }

        private static StyleLonghandSet BuildOuterSet()
        {
            var set = StyleLonghandSet.Empty;
            foreach (var entry in s_entries)
            {
                if (entry.Role == Role.Outer)
                {
                    set = set.Union(StyleLonghandSet.Of(entry.Longhand));
                }
            }
            return set;
        }

        private static bool[] BuildOuterProperties()
        {
            var properties = (ArbitraryProperty[])Enum.GetValues(typeof(ArbitraryProperty));
            var outer = new bool[properties.Length];
            foreach (var property in properties)
            {
                var set = StyleArbitraryLonghands.Of(property);
                outer[(int)property] = set.Union(s_outer) == s_outer;
            }
            return outer;
        }
    }
}
