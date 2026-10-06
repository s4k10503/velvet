using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Resolve before cancellation or class swaps remove starting values and ownership.
    internal sealed class MotionSlotContext
    {
        private readonly VisualElement _element;
        private readonly Func<ArbitraryProperty, bool> _drivenByRunningPlay;
        private readonly HashSet<string> _swapped = new(StringComparer.Ordinal);
        private readonly HashSet<ArbitraryProperty> _inlineBySwap = new();
        private readonly HashSet<ArbitraryProperty> _importantInlineBySwap = new();

        private MotionSlotContext(VisualElement element, Func<ArbitraryProperty, bool> drivenByRunningPlay)
        {
            _element = element;
            _drivenByRunningPlay = drivenByRunningPlay;
        }

        public static MotionSlotContext? Read(VisualElement element, string[]? fromClasses, string[]? toClasses,
            Func<ArbitraryProperty, bool> drivenByRunningPlay)
        {
            if (element.panel == null)
            {
                return null;
            }
            var context = new MotionSlotContext(element, drivenByRunningPlay);
            context.NoteSwapped(fromClasses);
            context.NoteSwapped(toClasses);
            return context;
        }

        private void NoteSwapped(string[]? classes)
        {
            if (classes == null)
            {
                return;
            }
            foreach (var cls in classes)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
                _swapped.Add(core);
                if (StyleArbitraryValueResolver.IsInlineResolved(core) && StyleArbitraryValueResolver.TryParse(core, out var style))
                {
                    _inlineBySwap.Add(style.Property);
                    if (important) _importantInlineBySwap.Add(style.Property);
                }
            }
        }

        public IEnumerable<string> RestingClasses()
        {
            var resting = new List<string>();
            foreach (var cls in _element.GetClasses())
            {
                if (!_swapped.Contains(cls))
                {
                    resting.Add(StyleArbitraryValueResolver.TryGetProjection(_element)?.HasImportantClass(cls) == true
                        ? "!" + cls : cls);
                }
            }
            return resting;
        }

        // Running-play frames are current-value samples instead of resting holders.
        public bool HoldsInlineOutsideSwap(ArbitraryProperty slot)
        {
            if (_drivenByRunningPlay(slot)) return false;
            var longhands = StyleArbitraryLonghands.Of(slot);
            var owner = Owner(slot);
            var style = owner.style;
            var hasInline = slot == ArbitraryProperty.BorderColor
                ? HoldsBorderEdge(owner, StyleLonghand.BorderTopColor, style.borderTopColor)
                    || HoldsBorderEdge(owner, StyleLonghand.BorderRightColor, style.borderRightColor)
                    || HoldsBorderEdge(owner, StyleLonghand.BorderBottomColor, style.borderBottomColor)
                    || HoldsBorderEdge(owner, StyleLonghand.BorderLeftColor, style.borderLeftColor)
                : TryReadInline(slot, out _) && !HasRunningNativeTransition(slot);
            if (!hasInline) return false;
            foreach (var property in _inlineBySwap)
            {
                if (StyleArbitraryLonghands.Of(property).Overlaps(longhands))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HoldsBorderEdge(VisualElement owner, StyleLonghand longhand, StyleColor inline)
            => inline.keyword == StyleKeyword.Undefined && !HasRunningNativeTransition(owner, longhand);

        public bool HasImportantInlineOutsideSwap(ArbitraryProperty slot)
            => StyleArbitraryValueResolver.HasImportantInlineOutside(_element,
                StyleArbitraryLonghands.Of(slot), _importantInlineBySwap);

        public bool TryReadCurrent(ArbitraryProperty slot, LengthUnit unit, out ArbitraryStyle value)
        {
            if (slot == ArbitraryProperty.BorderColor)
            {
                return TryReadBorderColor(out value);
            }
            if (HasRunningNativeTransition(slot))
            {
                return TryReadResolved(slot, out value)
                    && (MotionPropertyClassParser.IsColor(slot) || value.Unit == unit || TryConvert(slot, ref value, unit));
            }
            if (slot == ArbitraryProperty.Scale && Owner(slot).style.scale.keyword == StyleKeyword.Undefined)
            {
                return TryReadInline(slot, out value);
            }
            if (TryReadInline(slot, out value))
            {
                return MotionPropertyClassParser.IsColor(slot) || value.Unit == unit || TryConvert(slot, ref value, unit);
            }
            if (!TryReadResolved(slot, out value))
            {
                return false;
            }
            return MotionPropertyClassParser.IsColor(slot) || value.Unit == unit || TryConvert(slot, ref value, unit);
        }

        private bool HasRunningNativeTransition(ArbitraryProperty slot)
        {
            if (_drivenByRunningPlay(slot)) return false;
            var owner = Owner(slot);
            var longhands = StyleArbitraryLonghands.Of(slot);
            // MUTANT_SURVIVES(equivalent, boundary): with 88 longhands, no mapped set claims the extra index, so the native query is never reached.
            for (var i = 0; i < s_nativePropertyIds.Length; i++)
            {
                var longhand = (StyleLonghand)i;
                if (longhands.Contains(longhand) && HasRunningNativeTransition(owner, longhand)) return true;
            }
            return false;
        }

        private static bool HasRunningNativeTransition(VisualElement owner, StyleLonghand longhand)
            => owner.panel != null && s_nativePropertyIds[(int)longhand] is { } id
                && ReadNativeMemberOrNull(s_hasRunningNative, owner, new[] { id }) is true;

        private static object? ReadNativeMemberOrNull(MemberInfo? member, object owner, object[]? arguments = null)
        {
            try
            {
                return member switch
                {
                    MethodInfo method => method.Invoke(owner, arguments),
                    PropertyInfo property => property.GetValue(owner),
                    _ => null,
                };
            }
            catch (Exception exception) when (exception is TargetInvocationException or TargetException
                or ArgumentException or MemberAccessException or NotSupportedException)
            {
                return null;
            }
        }

        private static readonly MethodInfo? s_hasRunningNative = EngineMember.HasRunningStyleAnimation.ResolveMethod();
        private static readonly PropertyInfo? s_nativePropertyId = EngineMember.StylePropertyNameId.ResolveProperty();
        private static readonly object?[] s_nativePropertyIds = NativePropertyIds();

        private static object?[] NativePropertyIds()
        {
            var ids = new object?[StyleUtilityProperties.LonghandCount];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = ReadNativeMemberOrNull(s_nativePropertyId, new StylePropertyName(StyleUtilityProperties.UssName((StyleLonghand)i)));
            }
            return ids;
        }

        private VisualElement Owner(ArbitraryProperty slot)
            => ReferenceEquals(ClipPathLayoutBox.StyleFor(_element, slot), _element.style)
                ? _element
                : ClipPathLayoutBox.Of(_element);

        private bool TryReadInline(ArbitraryProperty slot, out ArbitraryStyle value)
        {
            var style = Owner(slot).style;
            value = default;
            switch (slot)
            {
                case ArbitraryProperty.Opacity:
                    return Float(slot, style.opacity, out value);
                case ArbitraryProperty.TranslateX:
                case ArbitraryProperty.TranslateY:
                {
                    var translate = style.translate;
                    if (translate.keyword != StyleKeyword.Undefined)
                    {
                        return false;
                    }
                    var axis = slot == ArbitraryProperty.TranslateX ? translate.value.x : translate.value.y;
                    value = new ArbitraryStyle(slot, axis.value, axis.unit);
                    return true;
                }
                case ArbitraryProperty.Scale:
                {
                    var scale = style.scale;
                    if (scale.keyword != StyleKeyword.Undefined || !scale.value.value.x.Equals(scale.value.value.y))
                    {
                        return false;
                    }
                    value = new ArbitraryStyle(slot, scale.value.value.x, LengthUnit.Pixel);
                    return true;
                }
                case ArbitraryProperty.Rotate:
                {
                    var rotate = style.rotate;
                    if (rotate.keyword != StyleKeyword.Undefined)
                    {
                        return false;
                    }
                    value = new ArbitraryStyle(slot, rotate.value.angle.ToDegrees(), LengthUnit.Pixel);
                    return true;
                }
                case ArbitraryProperty.TextColor:
                    return Color(slot, style.color, out value);
                case ArbitraryProperty.BackgroundColor:
                    return Color(slot, style.backgroundColor, out value);
                case ArbitraryProperty.BorderTopWidth:
                    return Float(slot, style.borderTopWidth, out value);
                case ArbitraryProperty.BorderRightWidth:
                    return Float(slot, style.borderRightWidth, out value);
                case ArbitraryProperty.BorderBottomWidth:
                    return Float(slot, style.borderBottomWidth, out value);
                case ArbitraryProperty.BorderLeftWidth:
                    return Float(slot, style.borderLeftWidth, out value);
                default:
                    return s_inlineLengths.TryGetValue(slot, out var read) && Length(slot, read(style), out value);
            }
        }

        private bool TryReadResolved(ArbitraryProperty slot, out ArbitraryStyle value)
        {
            var resolved = Owner(slot).resolvedStyle;
            value = default;
            switch (slot)
            {
                case ArbitraryProperty.Opacity:
                    return Pixels(slot, resolved.opacity, out value);
                case ArbitraryProperty.TranslateX:
                    return Pixels(slot, resolved.translate.x, out value);
                case ArbitraryProperty.TranslateY:
                    return Pixels(slot, resolved.translate.y, out value);
                case ArbitraryProperty.Scale:
                {
                    var scale = resolved.scale.value;
                    return scale.x.Equals(scale.y) && Pixels(slot, scale.x, out value);
                }
                case ArbitraryProperty.Rotate:
                    return Pixels(slot, resolved.rotate.angle.ToDegrees(), out value);
                case ArbitraryProperty.TextColor:
                    value = new ArbitraryStyle(slot, resolved.color);
                    return true;
                case ArbitraryProperty.BackgroundColor:
                    value = new ArbitraryStyle(slot, resolved.backgroundColor);
                    return true;
                default:
                    return s_resolvedLengths.TryGetValue(slot, out var read) && Pixels(slot, read(resolved), out value);
            }
        }

        private bool TryReadBorderColor(out ArbitraryStyle value)
        {
            var owner = Owner(ArbitraryProperty.BorderColor);
            var style = owner.style;
            var resolved = owner.resolvedStyle;
            var native = !_drivenByRunningPlay(ArbitraryProperty.BorderColor);
            var top = ReadBorderEdge(owner, native, StyleLonghand.BorderTopColor, style.borderTopColor, resolved.borderTopColor);
            var right = ReadBorderEdge(owner, native, StyleLonghand.BorderRightColor, style.borderRightColor, resolved.borderRightColor);
            var bottom = ReadBorderEdge(owner, native, StyleLonghand.BorderBottomColor, style.borderBottomColor, resolved.borderBottomColor);
            var left = ReadBorderEdge(owner, native, StyleLonghand.BorderLeftColor, style.borderLeftColor, resolved.borderLeftColor);
            value = new ArbitraryStyle(ArbitraryProperty.BorderColor, top);
            return top.Equals(right) && top.Equals(bottom) && top.Equals(left);
        }

        private static Color ReadBorderEdge(VisualElement owner, bool native, StyleLonghand longhand, StyleColor inline, Color resolved)
            => native && HasRunningNativeTransition(owner, longhand) || inline.keyword != StyleKeyword.Undefined
                ? resolved : inline.value;

        private bool TryConvert(ArbitraryProperty slot, ref ArbitraryStyle value, LengthUnit unit)
        {
            var parent = Owner(slot).hierarchy.parent;
            var horizontal = slot is ArbitraryProperty.Width or ArbitraryProperty.MinWidth or ArbitraryProperty.MaxWidth;
            var vertical = slot is ArbitraryProperty.Height or ArbitraryProperty.MinHeight or ArbitraryProperty.MaxHeight;
            if (parent == null || (!horizontal && !vertical))
            {
                return false;
            }
            var basis = horizontal ? parent.contentRect.width : parent.contentRect.height;
            if (!(basis > 0f) || float.IsInfinity(basis))
            {
                return false;
            }
            var pixels = value.Unit == LengthUnit.Percent ? value.Value / 100f * basis : value.Value;
            value = new ArbitraryStyle(slot, unit == LengthUnit.Percent ? pixels / basis * 100f : pixels, unit);
            return true;
        }

        private static bool Float(ArbitraryProperty slot, StyleFloat style, out ArbitraryStyle value)
        {
            value = new ArbitraryStyle(slot, style.value, LengthUnit.Pixel);
            return style.keyword == StyleKeyword.Undefined;
        }

        private static bool Color(ArbitraryProperty slot, StyleColor style, out ArbitraryStyle value)
        {
            value = new ArbitraryStyle(slot, style.value);
            return style.keyword == StyleKeyword.Undefined;
        }

        private static bool Length(ArbitraryProperty slot, StyleLength style, out ArbitraryStyle value)
        {
            value = new ArbitraryStyle(slot, style.value.value, style.value.unit);
            // MUTANT_SURVIVES(equivalent, clause removed): the Auto/None exclusions are redundant on the current 25 mapped public getter paths;
            // Given_FlaggedLengths_When_ReadInline_Then_TheGettersNormalizeNonNumericKeywords pins the keyword boundary.
            return style.keyword == StyleKeyword.Undefined && !style.value.IsAuto() && !style.value.IsNone();
        }

        private static bool Pixels(ArbitraryProperty slot, float pixels, out ArbitraryStyle value)
        {
            value = new ArbitraryStyle(slot, pixels, LengthUnit.Pixel);
            return !float.IsNaN(pixels) && !float.IsInfinity(pixels);
        }

        private static float Keyworded(StyleFloat value) => value.keyword == StyleKeyword.Undefined ? value.value : float.NaN;

        private static readonly Dictionary<ArbitraryProperty, Func<IStyle, StyleLength>> s_inlineLengths = new()
        {
            [ArbitraryProperty.Width] = s => s.width,
            [ArbitraryProperty.Height] = s => s.height,
            [ArbitraryProperty.MinWidth] = s => s.minWidth,
            [ArbitraryProperty.MinHeight] = s => s.minHeight,
            [ArbitraryProperty.MaxWidth] = s => s.maxWidth,
            [ArbitraryProperty.MaxHeight] = s => s.maxHeight,
            [ArbitraryProperty.FlexBasis] = s => s.flexBasis,
            [ArbitraryProperty.Top] = s => s.top,
            [ArbitraryProperty.Right] = s => s.right,
            [ArbitraryProperty.Bottom] = s => s.bottom,
            [ArbitraryProperty.Left] = s => s.left,
            [ArbitraryProperty.PaddingTop] = s => s.paddingTop,
            [ArbitraryProperty.PaddingRight] = s => s.paddingRight,
            [ArbitraryProperty.PaddingBottom] = s => s.paddingBottom,
            [ArbitraryProperty.PaddingLeft] = s => s.paddingLeft,
            [ArbitraryProperty.MarginTop] = s => s.marginTop,
            [ArbitraryProperty.MarginRight] = s => s.marginRight,
            [ArbitraryProperty.MarginBottom] = s => s.marginBottom,
            [ArbitraryProperty.MarginLeft] = s => s.marginLeft,
            [ArbitraryProperty.BorderTopLeftRadius] = s => s.borderTopLeftRadius,
            [ArbitraryProperty.BorderTopRightRadius] = s => s.borderTopRightRadius,
            [ArbitraryProperty.BorderBottomLeftRadius] = s => s.borderBottomLeftRadius,
            [ArbitraryProperty.BorderBottomRightRadius] = s => s.borderBottomRightRadius,
            [ArbitraryProperty.FontSize] = s => s.fontSize,
            [ArbitraryProperty.LetterSpacing] = s => s.letterSpacing,
        };

        private static readonly Dictionary<ArbitraryProperty, Func<IResolvedStyle, float>> s_resolvedLengths = new()
        {
            [ArbitraryProperty.Width] = s => s.width,
            [ArbitraryProperty.Height] = s => s.height,
            [ArbitraryProperty.MinWidth] = s => Keyworded(s.minWidth),
            [ArbitraryProperty.MinHeight] = s => Keyworded(s.minHeight),
            [ArbitraryProperty.MaxWidth] = s => Keyworded(s.maxWidth),
            [ArbitraryProperty.MaxHeight] = s => Keyworded(s.maxHeight),
            [ArbitraryProperty.FlexBasis] = s => Keyworded(s.flexBasis),
            [ArbitraryProperty.Top] = s => s.top,
            [ArbitraryProperty.Right] = s => s.right,
            [ArbitraryProperty.Bottom] = s => s.bottom,
            [ArbitraryProperty.Left] = s => s.left,
            [ArbitraryProperty.PaddingTop] = s => s.paddingTop,
            [ArbitraryProperty.PaddingRight] = s => s.paddingRight,
            [ArbitraryProperty.PaddingBottom] = s => s.paddingBottom,
            [ArbitraryProperty.PaddingLeft] = s => s.paddingLeft,
            [ArbitraryProperty.MarginTop] = s => s.marginTop,
            [ArbitraryProperty.MarginRight] = s => s.marginRight,
            [ArbitraryProperty.MarginBottom] = s => s.marginBottom,
            [ArbitraryProperty.MarginLeft] = s => s.marginLeft,
            [ArbitraryProperty.BorderTopLeftRadius] = s => s.borderTopLeftRadius,
            [ArbitraryProperty.BorderTopRightRadius] = s => s.borderTopRightRadius,
            [ArbitraryProperty.BorderBottomLeftRadius] = s => s.borderBottomLeftRadius,
            [ArbitraryProperty.BorderBottomRightRadius] = s => s.borderBottomRightRadius,
            [ArbitraryProperty.BorderTopWidth] = s => s.borderTopWidth,
            [ArbitraryProperty.BorderRightWidth] = s => s.borderRightWidth,
            [ArbitraryProperty.BorderBottomWidth] = s => s.borderBottomWidth,
            [ArbitraryProperty.BorderLeftWidth] = s => s.borderLeftWidth,
            [ArbitraryProperty.FontSize] = s => s.fontSize,
            [ArbitraryProperty.LetterSpacing] = s => Keyworded(s.letterSpacing),
        };
    }
}
