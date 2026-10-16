using System;

namespace Velvet
{
    // Whether the one box-shadow Tailwind composes from an element's ring-*, ring-offset-*, shadow-*, inset-ring-* and
    // inset-shadow-* utilities holds a value the browser rejects, which discards the whole declaration: the ring, the
    // offset fill, every shadow and the inset layers go together. A colour or width is rejected where it is no colour
    // or no length, a shadow value where it is no box-shadow, and a colour where its opacity modifier, or the shadow
    // size's beside it, lies outside 0..100%, which leaves Tailwind's color-mix() invalid. A colour only matters to a
    // layer that reads it, so each is weighed against the layer it colours. Classes are read in the order
    // StyleRingClass.InEmitOrder gives, the last of each property winning.
    internal struct BoxShadowDiscard
    {
        private bool _declared;
        private bool _ring;
        private bool _ringWidthRejected;
        private bool _ringColourRejected;
        private bool _offsetWidth;
        private bool _offsetColourRejected;
        private Layer _shadow;
        private bool _insetRing;
        private bool _insetRingWidthRejected;
        private bool _insetRingColourRejected;
        private Layer _insetShadow;

        // A blurred layer: its size, whether that size reads its colour property, and that property's verdict.
        private struct Layer
        {
            public bool ValueRejected;
            public bool ReadsColour;
            public bool ScaleInvalid;
            public bool ColourSet;
            public bool ColourRejected;

            public bool Discards => ValueRejected || (ReadsColour && ColourSet && (ScaleInvalid || ColourRejected));
        }

        internal static bool Of(string[]? classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            var state = default(BoxShadowDiscard);
            foreach (var cls in classNames)
            {
                if (!string.IsNullOrEmpty(cls))
                {
                    state.Read(cls);
                }
            }
            return state._declared
                && ((state._ring && (state._ringWidthRejected || state._ringColourRejected))
                    || (state._offsetWidth && state._offsetColourRejected)
                    || state._shadow.Discards
                    || (state._insetRing && (state._insetRingWidthRejected || state._insetRingColourRejected))
                    || state._insetShadow.Discards);
        }

        private void Read(string cls)
        {
            if (cls == "ring" || cls == "inset-ring")
            {
                _declared = true;
                if (cls == "ring")
                {
                    (_ring, _ringWidthRejected) = (true, false);
                }
                else
                {
                    (_insetRing, _insetRingWidthRejected) = (true, false);
                }
            }
            else if (StyleRingClass.TryMatchPrefix(cls, "ring-offset-", out var offset))
            {
                ReadBandPart(offset!, "ring-offset", ref _offsetWidth, ref _offsetColourRejected, out _);
            }
            else if (StyleRingClass.TryMatchPrefix(cls, "inset-ring-", out var insetRing))
            {
                ReadBand(insetRing!, "inset-ring", ref _insetRing, ref _insetRingWidthRejected, ref _insetRingColourRejected);
            }
            else if (cls != "ring-inset" && StyleRingClass.TryMatchPrefix(cls, "ring-", out var ring))
            {
                ReadBand(ring!, "ring", ref _ring, ref _ringWidthRejected, ref _ringColourRejected);
            }
            else if (cls.StartsWith("inset-shadow-", StringComparison.Ordinal))
            {
                ReadLayer(cls, "inset-shadow-", ref _insetShadow);
            }
            else if (StyleShadowClass.IsBoxShadowClass(cls))
            {
                ReadLayer(cls, "shadow-", ref _shadow);
            }
        }

        private void ReadBand(string suffix, string root, ref bool band, ref bool widthRejected, ref bool colourRejected)
        {
            var width = false;
            ReadBandPart(suffix, root, ref width, ref colourRejected, out var rejected);
            if (width)
            {
                _declared = band = true;
                widthRejected = rejected;
            }
        }

        // One ring-, ring-offset- or inset-ring- suffix as Tailwind types it: a width (rejected when the browser would
        // reject it) or a colour (rejected likewise); a palette name it does not know sets neither.
        private static void ReadBandPart(string suffix, string root, ref bool width, ref bool colourRejected,
            out bool widthRejected)
        {
            widthRejected = false;
            if (StyleRingClass.TryParseInteger(suffix, out _))
            {
                width = true;
                return;
            }
            if (!StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner))
            {
                Weigh(StyleColorValueParser.VerdictOfColourValue(suffix, 0), ref colourRejected);
                return;
            }
            if (!StyleArbitraryValueResolver.TrySplitTypeHint(inner, out var hint, out inner))
            {
                return;
            }
            if (hint != null ? StyleColorValueParser.HintSelectsNonColour(root, hint) : TailwindDataTypes.IsLength(inner))
            {
                width = true;
                widthRejected = !StyleArbitraryValueResolver.TryParseValue(inner, out _, out _)
                    && !StyleLengthExpression.TryParse(inner, out _);
                return;
            }
            colourRejected = !StyleColorValueParser.TryParseColor(inner, out _);
        }

        // A shadow-* or inset-shadow-* class: a colour, or a size, which writes the box-shadow.
        private void ReadLayer(string cls, string prefix, ref Layer layer)
        {
            switch (StyleShadowClass.VerdictOfShadowColour(cls, prefix))
            {
                case StyleColorValueParser.ColourVerdict.Valid:
                    (layer.ColourSet, layer.ColourRejected) = (true, false);
                    return;
                case StyleColorValueParser.ColourVerdict.Rejected:
                    (layer.ColourSet, layer.ColourRejected) = (true, true);
                    return;
            }
            if (!StyleShadowClass.DeclaresBoxShadow(cls, prefix))
            {
                return;
            }
            _declared = true;
            layer.ValueRejected = StyleShadowClass.HoldsRejectedValue(cls, prefix);
            layer.ReadsColour = !StyleShadowClass.TrySplitSizeModifier(cls, out var size, out _) || size != prefix + "none";
            layer.ScaleInvalid = !StyleShadowClass.TrySizeColourScale(cls, out _);
        }

        private static void Weigh(StyleColorValueParser.ColourVerdict verdict, ref bool rejected)
        {
            if (verdict != StyleColorValueParser.ColourVerdict.None)
            {
                rejected = verdict == StyleColorValueParser.ColourVerdict.Rejected;
            }
        }
    }
}
