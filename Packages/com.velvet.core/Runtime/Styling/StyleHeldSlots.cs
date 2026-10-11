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

        // The held slots that give way to a layer of the element's own — see StyleArbitraryValueResolver.Yield.
        private int _yieldMask;

        // The yielding slots that give way only to an important layer.
        private int _importantOnlyMask;
        private StyleLength[]? _lengths;
        private StyleFloat[]? _floats;
        private StyleColor[]? _colors;

        public static int Bit(HeldSlot slot) => 1 << (int)slot;

        public void Set(HeldSlot slot, StyleLength value)
        {
            (_lengths ??= new StyleLength[HeldSlotGroups.SlotCount])[(int)slot] = value;
            _mask |= Bit(slot);
            _yieldMask &= ~Bit(slot);
        }

        public void Set(HeldSlot slot, StyleFloat value)
        {
            (_floats ??= new StyleFloat[HeldSlotGroups.SlotCount])[(int)slot] = value;
            _mask |= Bit(slot);
            _yieldMask &= ~Bit(slot);
        }

        public void Set(HeldSlot slot, StyleColor value)
        {
            (_colors ??= new StyleColor[HeldSlotGroups.SlotCount])[(int)slot] = value;
            _mask |= Bit(slot);
            _yieldMask &= ~Bit(slot);
        }

        public void Drop(HeldSlot slot) => _mask &= ~Bit(slot);

        public void SetYield(HeldSlot slot, bool importantOnly)
        {
            _yieldMask |= Bit(slot);
            _importantOnlyMask = importantOnly ? _importantOnlyMask | Bit(slot) : _importantOnlyMask & ~Bit(slot);
        }

        public bool YieldsOnlyToImportant(HeldSlot slot) => (_importantOnlyMask & Bit(slot)) != 0;

        public bool Yields(HeldSlot slot) => (_yieldMask & _mask & Bit(slot)) != 0;

        public bool IsHeld(HeldSlot slot) => (_mask & Bit(slot)) != 0;

        // Writes every held slot among slots back onto style.
        public void Reassert(IStyle style, int slots)
        {
            var due = _mask & slots;
            for (var i = 0; due != 0; i++, due >>= 1)
            {
                if ((due & 1) != 0)
                {
                    Write(style, (HeldSlot)i, _lengths?[i] ?? default, _floats?[i] ?? default, _colors?[i] ?? default);
                }
            }
        }

        public static void WriteNull(IStyle style, HeldSlot slot)
            => Write(style, slot, StyleKeyword.Null, StyleKeyword.Null, StyleKeyword.Null);

        // Writes what the layer winner resolves to into slot alone, whatever else the winner's property fans
        // out to. length is the winner's length as measured on the element, for a length slot.
        public static void WriteLayered(IStyle style, HeldSlot slot, StyleLength length, in ArbitraryStyle winner)
            => Write(style, slot, length, new StyleFloat(winner.Value), new StyleColor(winner.Color));

        // Each slot takes the one of the three values its style type carries.
        private static void Write(IStyle style, HeldSlot slot, StyleLength length, StyleFloat width, StyleColor color)
        {
            switch (slot)
            {
                case HeldSlot.MarginTop:
                    style.marginTop = length;
                    break;
                case HeldSlot.MarginRight:
                    style.marginRight = length;
                    break;
                case HeldSlot.MarginBottom:
                    style.marginBottom = length;
                    break;
                case HeldSlot.MarginLeft:
                    style.marginLeft = length;
                    break;
                case HeldSlot.Width:
                    style.width = length;
                    break;
                case HeldSlot.BorderTopWidth:
                    style.borderTopWidth = width;
                    break;
                case HeldSlot.BorderRightWidth:
                    style.borderRightWidth = width;
                    break;
                case HeldSlot.BorderBottomWidth:
                    style.borderBottomWidth = width;
                    break;
                case HeldSlot.BorderLeftWidth:
                    style.borderLeftWidth = width;
                    break;
                case HeldSlot.BorderTopColor:
                    style.borderTopColor = color;
                    break;
                case HeldSlot.BorderRightColor:
                    style.borderRightColor = color;
                    break;
                case HeldSlot.BorderBottomColor:
                    style.borderBottomColor = color;
                    break;
                case HeldSlot.BorderLeftColor:
                    style.borderLeftColor = color;
                    break;
            }
        }
    }

    // Which arbitrary-value layers bear on a held slot, derived from StyleArbitraryLonghands (the longhands
    // each property writes, pinned by StyleArbitraryLonghandTableTests) rather than listed here.
    internal static class HeldSlotGroups
    {
        public const int SlotCount = (int)HeldSlot.BorderLeftColor + 1;

        public static readonly HeldSlot[] EverySlot = (HeldSlot[])Enum.GetValues(typeof(HeldSlot));

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

        public static StyleLonghand LonghandOf(HeldSlot slot) => s_longhands[(int)slot];

        private static readonly int[] s_slotsOf = BuildSlotsOf();
        private static readonly ArbitraryProperty[][] s_writersOf = BuildWritersOf();

        // The held slots property's layer writes; 0 when it writes none.
        public static int SlotsOf(ArbitraryProperty property) => s_slotsOf[(int)property];

        // Every property whose layer writes slot, broad before narrow.
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
                var bit = 1 << slot;
                // OrderByDescending is stable, so equally broad properties keep enum order.
                writersOf[slot] = s_properties
                    .Where(property => (s_slotsOf[(int)property] & bit) != 0)
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
