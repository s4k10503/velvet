using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// What a running spring or bezier play draws on each channel and how fast that value is moving, in the
    /// channel's own units per second: what a play interrupting it starts from, whichever driver either runs on,
    /// as Framer Motion starts a value's next animation from the motion value and its velocity. Built by
    /// <see cref="MotionSpringDriver.Drawn"/> and <see cref="BezierTweenDriver.Drawn"/>.
    /// </summary>
    internal sealed class MotionDrawnValues
    {
        public (float Value, float Velocity)? Opacity;
        public (float Value, float Velocity)? TranslateX;
        public (float Value, float Velocity)? TranslateY;
        // The units the two translate values are drawn in.
        public LengthUnit TranslateXUnit;
        public LengthUnit TranslateYUnit;
        public (float Value, float Velocity)? Scale;
        // The y factor, where the play draws one of its own; null where Scale is drawn on both axes.
        public (float Value, float Velocity)? ScaleY;
        public (float Value, float Velocity)? Rotate;
        public (float Value, float Velocity)? SkewX;
        public (float Value, float Velocity)? SkewY;
        public List<(ArbitraryProperty Property, Color Color)>? Colors;
        public List<(ArbitraryProperty Property, LengthUnit Unit, float Value, float Velocity)>? Lengths;

        /// <summary>The value and velocity a driver draws on each of the transform quartet's channels, null where it has none.</summary>
        public readonly struct Channels
        {
            public readonly (float, float)? Opacity;
            public readonly (float, float)? TranslateX;
            public readonly (float, float)? TranslateY;
            public readonly (float, float)? Scale;
            public readonly (float, float)? Rotate;

            public Channels((float, float)? opacity, (float, float)? translateX, (float, float)? translateY,
                (float, float)? scale, (float, float)? rotate)
            {
                Opacity = opacity;
                TranslateX = translateX;
                TranslateY = translateY;
                Scale = scale;
                Rotate = rotate;
            }
        }

        // What a driver's channels draw, read through the selectors each driver gives for its own channel types.
        public static MotionDrawnValues Of<TColor, TLength>(
            Channels channels,
            List<TColor>? colors, System.Func<TColor, (ArbitraryProperty, Color)> drawnColor,
            List<TLength>? lengths, System.Func<TLength, (ArbitraryProperty, LengthUnit, float, float)> drawnLength)
        {
            var drawn = new MotionDrawnValues
            {
                Opacity = channels.Opacity, TranslateX = channels.TranslateX, TranslateY = channels.TranslateY,
                Scale = channels.Scale, Rotate = channels.Rotate,
            };
            if (colors != null)
            {
                drawn.Colors = colors.ConvertAll(drawnColor.Invoke);
            }
            if (lengths != null)
            {
                drawn.Lengths = lengths.ConvertAll(drawnLength.Invoke);
            }
            return drawn;
        }

        // Replaces each channel of channels this draws a color for with continued(channel, color drawn).
        public void ContinueColors<TColor>(List<TColor>? channels, System.Func<TColor, ArbitraryProperty> property,
            System.Func<TColor, Color, TColor> continued)
        {
            if (channels == null)
            {
                return;
            }
            for (var i = 0; i < channels.Count; i++)
            {
                if (ColorOf(property(channels[i])) is { } drawn)
                {
                    channels[i] = continued(channels[i], drawn);
                }
            }
        }

        public Color? ColorOf(ArbitraryProperty property)
        {
            if (Colors != null)
            {
                foreach (var color in Colors)
                {
                    if (color.Property == property) return color.Color;
                }
            }
            return null;
        }

        // The length drawn for property, and whether it is in unit; null where no channel drives property.
        public (float Value, float Velocity, bool SameUnit)? LengthOf(ArbitraryProperty property, LengthUnit unit)
        {
            if (Lengths != null)
            {
                foreach (var length in Lengths)
                {
                    if (length.Property == property) return (length.Value, length.Velocity, length.Unit == unit);
                }
            }
            return null;
        }
    }
}
