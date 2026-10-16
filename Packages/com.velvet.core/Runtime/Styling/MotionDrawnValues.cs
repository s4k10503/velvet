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
        public (float Value, float Velocity)? Scale;
        public (float Value, float Velocity)? Rotate;
        public List<(ArbitraryProperty Property, Color Color)>? Colors;
        public List<(ArbitraryProperty Property, LengthUnit Unit, float Value, float Velocity)>? Lengths;

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
