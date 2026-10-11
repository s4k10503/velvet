using System;

namespace Velvet
{
    // Tailwind v4's bare spacing value: --spacing is 0.25rem and a utility takes any non-negative multiple of
    // 0.25 written in canonical decimal form, so p-13, p-4.5, p-0.25 and p-17.75 resolve and p-.5, p-4.50,
    // p-05 and p-4.1 do not. A rem is 16px here (StyleArbitraryValueResolver.TryParseValue), which makes a
    // step 4px.
    internal static class StyleSpacingMultiplier
    {
        public const float PixelsPerStep = 4f;

        // The pixel length of text, or false when it is not a canonical multiple of 0.25. A multiple of 0.25 has
        // no fraction other than .25, .5 and .75, so the canonical spellings are those three and nothing else.
        public static bool TryParse(ReadOnlySpan<char> text, out float px)
        {
            px = 0f;
            var dot = text.IndexOf('.');
            var wholePart = dot < 0 ? text : text.Slice(0, dot);
            if (!StyleArbitraryValueResolver.TryParseWhole(wholePart, out var whole))
            {
                return false;
            }
            var quarters = 0;
            if (dot >= 0)
            {
                quarters = QuartersOf(text.Slice(dot + 1));
                if (quarters == 0)
                {
                    return false;
                }
            }
            px = (whole + quarters * 0.25f) * PixelsPerStep;
            return true;
        }

        // How many quarters a canonical fraction spells, 0 for anything else, which includes `0` and a
        // trailing zero: String(4) is "4", so `4.0` and `4.50` are not the spelling Tailwind reads.
        private static int QuartersOf(ReadOnlySpan<char> fraction)
        {
            if (fraction.SequenceEqual("25".AsSpan()))
            {
                return 1;
            }
            if (fraction.SequenceEqual("5".AsSpan()))
            {
                return 2;
            }
            return fraction.SequenceEqual("75".AsSpan()) ? 3 : 0;
        }
    }
}
