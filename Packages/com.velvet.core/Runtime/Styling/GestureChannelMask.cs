using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // A play's channel a gesture state can hold: it runs on and writes nothing while Masked.
    internal interface IGestureMaskable
    {
        bool Masked { get; set; }
    }

    // A colour or length channel: the property it writes and the channel that drives it.
    internal interface IPropertyChannel
    {
        ArbitraryProperty Property { get; }
        IGestureMaskable Channel { get; }
    }

    // A spring or bezier play's state, as masking its channels for a gesture state reads it.
    internal interface IGestureMaskableState
    {
        // Whether an exit plays it (StyleLayerPriority.Exit).
        bool IsExit { set; }

        (IGestureMaskable? Opacity, IGestureMaskable? TranslateX, IGestureMaskable? TranslateY, IGestureMaskable? Scale,
            IGestureMaskable? Rotate) Scalars { get; }

        IReadOnlyList<IPropertyChannel>? ColorChannels { get; }
        IReadOnlyList<IPropertyChannel>? LengthChannels { get; }
    }

    // What the spring and bezier drivers share of holding a play's channels for a gesture state and handing them back
    // (MotionSpringDriver.ReturnChannels).
    internal static class GestureChannelMask
    {
        // Marks state, where there is one, as an exit's.
        public static void MarkExit(IGestureMaskableState? state, bool exit)
        {
            if (state != null)
            {
                state.IsExit = exit;
            }
        }

        // Stops writing the channels of state a gesture channel now holds, among named, and hands their properties
        // back to the element's own layers, while they run on: Framer Motion keeps a value a gesture state sets out of
        // the element's animate until the state ends (MotionSpringDriver.ReturnChannels).
        public static void MaskChannels(VisualElement element, IGestureMaskableState state, StyleLonghandSet named)
        {
            MaskScalars(element, named, state.Scalars);
            MaskProperties(element, state.ColorChannels, named);
            MaskProperties(element, state.LengthChannels, named);
            StyleArbitraryValueResolver.ReapplyLayeredValues(element, named);
        }

        private static void MaskProperties(VisualElement element, IReadOnlyList<IPropertyChannel>? channels,
            StyleLonghandSet named)
        {
            if (channels == null)
            {
                return;
            }
            for (var i = 0; i < channels.Count; i++)
            {
                MaskProperty(element, channels[i].Channel, channels[i].Property, named);
            }
        }

        // Masks each scalar channel named covers, nulling the slot it wrote. Translate's two axes go together.
        private static void MaskScalars(VisualElement element, StyleLonghandSet named,
            (IGestureMaskable? Opacity, IGestureMaskable? TranslateX, IGestureMaskable? TranslateY,
                IGestureMaskable? Scale, IGestureMaskable? Rotate) channels)
        {
            if (Mask(channels.Opacity, named, StyleLonghand.Opacity))
            {
                MotionOpacity.Write(element, StyleKeyword.Null);
            }
            if (Mask(channels.TranslateX, named, StyleLonghand.Translate))
            {
                Mask(channels.TranslateY, named, StyleLonghand.Translate);
                SeatedTranslate.Write(element, StyleKeyword.Null);
            }
            if (Mask(channels.Scale, named, StyleLonghand.Scale))
            {
                element.style.scale = StyleKeyword.Null;
            }
            if (Mask(channels.Rotate, named, StyleLonghand.Rotate))
            {
                element.style.rotate = StyleKeyword.Null;
            }
        }

        // Masks a colour or length channel writing property where named covers it, ending what it drove there.
        private static void MaskProperty(VisualElement element, IGestureMaskable channel, ArbitraryProperty property,
            StyleLonghandSet named)
        {
            if (!channel.Masked && StyleArbitraryLonghands.Of(property).Overlaps(named))
            {
                channel.Masked = true;
                StyleArbitraryValueResolver.ReleaseDriven(element, property);
            }
        }

        // Unmasks a scalar channel named covers, unless another gesture state still sets its slot; true where it did.
        public static bool Returns<T>(T? channel, StyleLonghandSet named, StyleLonghand longhand, ArbitraryProperty slot,
            MotionSlotContext shown)
            where T : class, IGestureMaskable
        {
            if (channel == null || !channel.Masked || !named.Contains(longhand) || shown.HoldsGesture(slot))
            {
                return false;
            }
            channel.Masked = false;
            return true;
        }

        // As Returns, for a colour or length channel writing property.
        public static bool ReturnsProperty<T>(T channel, ArbitraryProperty property, StyleLonghandSet named,
            MotionSlotContext shown)
            where T : class, IGestureMaskable
        {
            if (!channel.Masked || !StyleArbitraryLonghands.Of(property).Overlaps(named) || shown.HoldsGesture(property))
            {
                return false;
            }
            channel.Masked = false;
            return true;
        }

        private static bool Mask(IGestureMaskable? channel, StyleLonghandSet named, StyleLonghand longhand)
        {
            if (channel == null || channel.Masked || !named.Contains(longhand))
            {
                return false;
            }
            channel.Masked = true;
            return true;
        }
    }
}
