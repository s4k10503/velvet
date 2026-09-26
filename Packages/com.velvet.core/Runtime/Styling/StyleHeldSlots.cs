using System;
using System.Linq;
using UnityEngine.UIElements;

namespace Velvet
{
    // An inline slot a per-child layout manipulator — gap, grid, divide — writes straight onto an element
    // rather than through a layer. StyleArbitraryValueResolver.Hold / HandBack own what holding one means.
    internal enum HeldSlot : byte
    {
        MarginTop,
        MarginRight,
        MarginBottom,
        MarginLeft,
        Width,
        BorderTopWidth,
        BorderRightWidth,
        BorderBottomWidth,
        BorderLeftWidth,
        BorderTopColor,
        BorderRightColor,
        BorderBottomColor,
        BorderLeftColor,
    }

    // The values held on one element. Each array is indexed by slot and only allocated once a slot of its
    // style type is held, so a gap child carries the lengths alone.
    internal sealed class StyleHeldSlots
    {
        private int _mask;
        private StyleLength[]? _lengths;
        private StyleFloat[]? _floats;
        private StyleColor[]? _colors;

        public static int Bit(HeldSlot slot) => 1 << (int)slot;

        public void Set(HeldSlot slot, StyleLength value)
        {
            (_lengths ??= new StyleLength[HeldSlotGroups.SlotCount])[(int)slot] = value;
            _mask |= Bit(slot);
        }

        public void Set(HeldSlot slot, StyleFloat value)
        {
            (_floats ??= new StyleFloat[HeldSlotGroups.SlotCount])[(int)slot] = value;
            _mask |= Bit(slot);
        }

        public void Set(HeldSlot slot, StyleColor value)
        {
            (_colors ??= new StyleColor[HeldSlotGroups.SlotCount])[(int)slot] = value;
            _mask |= Bit(slot);
        }

        public void Drop(HeldSlot slot) => _mask &= ~Bit(slot);

        // Writes every held slot among slots back onto style.
        public void Reassert(IStyle style, int slots)
        {
            var due = _mask & slots;
            for (var i = 0; due != 0; i++, due >>= 1)
            {
                if ((due & 1) == 0)
                {
                    continue;
                }
                switch ((HeldSlot)i)
                {
                    case HeldSlot.MarginTop:
                        style.marginTop = _lengths![i];
                        break;
                    case HeldSlot.MarginRight:
                        style.marginRight = _lengths![i];
                        break;
                    case HeldSlot.MarginBottom:
                        style.marginBottom = _lengths![i];
                        break;
                    case HeldSlot.MarginLeft:
                        style.marginLeft = _lengths![i];
                        break;
                    case HeldSlot.Width:
                        style.width = _lengths![i];
                        break;
                    case HeldSlot.BorderTopWidth:
                        style.borderTopWidth = _floats![i];
                        break;
                    case HeldSlot.BorderRightWidth:
                        style.borderRightWidth = _floats![i];
                        break;
                    case HeldSlot.BorderBottomWidth:
                        style.borderBottomWidth = _floats![i];
                        break;
                    case HeldSlot.BorderLeftWidth:
                        style.borderLeftWidth = _floats![i];
                        break;
                    case HeldSlot.BorderTopColor:
                        style.borderTopColor = _colors![i];
                        break;
                    case HeldSlot.BorderRightColor:
                        style.borderRightColor = _colors![i];
                        break;
                    case HeldSlot.BorderBottomColor:
                        style.borderBottomColor = _colors![i];
                        break;
                    case HeldSlot.BorderLeftColor:
                        style.borderLeftColor = _colors![i];
                        break;
                }
            }
        }

        public static void WriteNull(IStyle style, HeldSlot slot)
        {
            switch (slot)
            {
                case HeldSlot.MarginTop:
                    style.marginTop = StyleKeyword.Null;
                    break;
                case HeldSlot.MarginRight:
                    style.marginRight = StyleKeyword.Null;
                    break;
                case HeldSlot.MarginBottom:
                    style.marginBottom = StyleKeyword.Null;
                    break;
                case HeldSlot.MarginLeft:
                    style.marginLeft = StyleKeyword.Null;
                    break;
                case HeldSlot.Width:
                    style.width = StyleKeyword.Null;
                    break;
                case HeldSlot.BorderTopWidth:
                    style.borderTopWidth = StyleKeyword.Null;
                    break;
                case HeldSlot.BorderRightWidth:
                    style.borderRightWidth = StyleKeyword.Null;
                    break;
                case HeldSlot.BorderBottomWidth:
                    style.borderBottomWidth = StyleKeyword.Null;
                    break;
                case HeldSlot.BorderLeftWidth:
                    style.borderLeftWidth = StyleKeyword.Null;
                    break;
                case HeldSlot.BorderTopColor:
                    style.borderTopColor = StyleKeyword.Null;
                    break;
                case HeldSlot.BorderRightColor:
                    style.borderRightColor = StyleKeyword.Null;
                    break;
                case HeldSlot.BorderBottomColor:
                    style.borderBottomColor = StyleKeyword.Null;
                    break;
                case HeldSlot.BorderLeftColor:
                    style.borderLeftColor = StyleKeyword.Null;
                    break;
            }
        }
    }

    // Which arbitrary-value layers bear on a held slot, derived from StyleArbitraryLonghands (the longhands
    // each property writes, pinned by StyleArbitraryLonghandTableTests) rather than listed here.
    internal static class HeldSlotGroups
    {
        public const int SlotCount = (int)HeldSlot.BorderLeftColor + 1;

        private static readonly StyleLonghand[] s_longhands =
        {
            StyleLonghand.MarginTop, StyleLonghand.MarginRight, StyleLonghand.MarginBottom, StyleLonghand.MarginLeft,
            StyleLonghand.Width,
            StyleLonghand.BorderTopWidth, StyleLonghand.BorderRightWidth,
            StyleLonghand.BorderBottomWidth, StyleLonghand.BorderLeftWidth,
            StyleLonghand.BorderTopColor, StyleLonghand.BorderRightColor,
            StyleLonghand.BorderBottomColor, StyleLonghand.BorderLeftColor,
        };

        private static readonly ArbitraryProperty[] s_properties =
            (ArbitraryProperty[])Enum.GetValues(typeof(ArbitraryProperty));

        private static readonly int[] s_slotsOf = BuildSlotsOf();
        private static readonly ArbitraryProperty[][] s_writersOf = BuildWritersOf();

        // The held slots property's layer writes; 0 when it writes none.
        public static int SlotsOf(ArbitraryProperty property) => s_slotsOf[(int)property];

        // Every property whose layer can decide what a hand-back of slot leaves on the element: the ones
        // writing slot, and, closed over, the ones writing any other longhand those write. Broad before
        // narrow, so re-resolving them in order leaves a longhand that a narrower property names on that
        // property's resolution rather than on a shorthand's.
        public static ArbitraryProperty[] WritersOf(HeldSlot slot) => s_writersOf[(int)slot];

        private static int[] BuildSlotsOf()
        {
            var slotsOf = new int[s_properties.Length];
            foreach (var property in s_properties)
            {
                var written = StyleArbitraryLonghands.Of(property);
                for (var slot = 0; slot < SlotCount; slot++)
                {
                    if (written.Contains(s_longhands[slot]))
                    {
                        slotsOf[(int)property] |= 1 << slot;
                    }
                }
            }
            return slotsOf;
        }

        private static ArbitraryProperty[][] BuildWritersOf()
        {
            var writersOf = new ArbitraryProperty[SlotCount][];
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var family = StyleLonghandSet.Of(s_longhands[slot]);
                while (true)
                {
                    var grown = family;
                    foreach (var property in s_properties)
                    {
                        var written = StyleArbitraryLonghands.Of(property);
                        if (written.Overlaps(family))
                        {
                            grown = grown.Union(written);
                        }
                    }
                    if (grown == family)
                    {
                        break;
                    }
                    family = grown;
                }

                // OrderByDescending is stable, so equally broad properties keep enum order.
                writersOf[slot] = s_properties
                    .Where(property => StyleArbitraryLonghands.Of(property).Overlaps(family))
                    .OrderByDescending(Breadth)
                    .ToArray();
            }
            return writersOf;
        }

        private static int Breadth(ArbitraryProperty property)
            => Enum.GetValues(typeof(StyleLonghand)).Cast<StyleLonghand>()
                .Count(StyleArbitraryLonghands.Of(property).Contains);
    }
}
