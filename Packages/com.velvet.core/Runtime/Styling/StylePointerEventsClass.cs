namespace Velvet
{
    // What an element's own class list says about pointer-events. Inherit is the absence of either utility, which
    // CSS spells by not declaring the property.
    internal enum PointerEventsMode
    {
        Inherit,
        None,
        Auto,
    }

    // Recognizes pointer-events-none / pointer-events-auto for PointerEventsScope. Neither has a scale or an
    // arbitrary form, so the classifier is an exact match.
    internal static class StylePointerEventsClass
    {
        private const string NoneClassName = "pointer-events-none";
        private const string AutoClassName = "pointer-events-auto";

        // The variant-payload gate's check (StyleVariantPayload), sharing Read's token names. A variant payload
        // lands bare, which is the form this matches.
        public static bool IsPointerEventsToken(string cls) => cls == NoneClassName || cls == AutoClassName;

        // An important token outranks an unmarked one wherever it sits, as an !important declaration does; between
        // two of the same weight the later decides, the order the variant gate composes its payloads in.
        public static PointerEventsMode Read(string[] classNames)
        {
            var mode = PointerEventsMode.Inherit;
            if (classNames == null)
            {
                return mode;
            }
            var modeImportant = false;
            foreach (var cls in classNames)
            {
                var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
                var declared = core == NoneClassName ? PointerEventsMode.None
                    : core == AutoClassName ? PointerEventsMode.Auto
                    : PointerEventsMode.Inherit;
                if (declared == PointerEventsMode.Inherit || (modeImportant && !important))
                {
                    continue;
                }
                mode = declared;
                modeImportant = important;
            }
            return mode;
        }
    }
}
