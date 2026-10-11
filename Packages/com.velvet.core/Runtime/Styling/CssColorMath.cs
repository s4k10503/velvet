using System;
using UnityEngine;

namespace Velvet
{
    // The colour spaces a CSS colour function writes in.
    internal enum CssColorSpace
    {
        Srgb,
        Hsl,
        Hwb,
    }

    // A colour as CSS Color 4 holds it before display: three components in the reference range of its space
    // and an alpha.
    internal struct CssColor
    {
        internal CssColorSpace Space;
        internal double C0;
        internal double C1;
        internal double C2;
        internal double Alpha;

        internal CssColor(CssColorSpace space, double c0, double c1, double c2, double alpha)
        {
            Space = space;
            C0 = c0;
            C1 = c1;
            C2 = c2;
            Alpha = alpha;
        }

        internal double this[int index]
        {
            get => index switch { 0 => C0, 1 => C1, 2 => C2, _ => Alpha };
            set
            {
                switch (index)
                {
                    case 0: C0 = value; break;
                    case 1: C1 = value; break;
                    case 2: C2 = value; break;
                    default: Alpha = value; break;
                }
            }
        }
    }

    // CSS Color 4's conversions in double precision, ported from the specification's sample code.
    // GradientBackground keeps its own float copy because the bake shader has to agree with it bit for bit;
    // this one only has to agree with the specification.
    internal static class CssColorMath
    {
        internal static bool IsPolar(CssColorSpace space) => space is CssColorSpace.Hsl or CssColorSpace.Hwb;

        // The sRGB colour a display shows, each channel clipped to [0, 1].
        internal static Color ToDisplay(CssColor color)
        {
            var (r, g, b) = ToSrgb(color);
            return new Color((float)Math.Clamp(r, 0, 1), (float)Math.Clamp(g, 0, 1), (float)Math.Clamp(b, 0, 1),
                (float)color.Alpha);
        }

        internal static double NormalizeHue(double degrees)
        {
            var h = degrees % 360.0;
            // MUTANT_SURVIVES(equivalent): a hue of 360 and a hue of 0 are one colour to every reader of a hue.
            return h < 0 ? h + 360.0 : h;
        }

        private static (double r, double g, double b) ToSrgb(CssColor color)
        {
            switch (color.Space)
            {
                case CssColorSpace.Hsl:
                    return HslToSrgb(color.C0, color.C1, color.C2);
                case CssColorSpace.Hwb:
                    return HwbToSrgb(color.C0, color.C1, color.C2);
                default:
                    return (color.C0, color.C1, color.C2);
            }
        }

        private static (double r, double g, double b) HslToSrgb(double hue, double saturation, double lightness)
        {
            var s = saturation / 100;
            var l = lightness / 100;
            double F(double n)
            {
                var k = (n + (hue / 30)) % 12;
                var a = s * Math.Min(l, 1 - l);
                return l - (a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)));
            }
            return (F(0), F(8), F(4));
        }

        private static (double r, double g, double b) HwbToSrgb(double hue, double whiteness, double blackness)
        {
            var w = whiteness / 100;
            var k = blackness / 100;
            // MUTANT_SURVIVES(equivalent): at a sum of exactly 1 the scaled branch gives the same gray, w in each channel.
            if (w + k >= 1)
            {
                var gray = w / (w + k);
                return (gray, gray, gray);
            }
            var (r, g, b) = HslToSrgb(hue, 100, 50);
            var scale = 1 - w - k;
            return ((r * scale) + w, (g * scale) + w, (b * scale) + w);
        }
    }
}
