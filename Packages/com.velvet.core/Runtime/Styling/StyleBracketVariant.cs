namespace Velvet
{
    // Shared split prologue for the bracketed variant parsers (data-/aria-[..]:, has-[..]:, supports-[..]:,
    // and the structural [&:..]: arbitrary form). The inner selector's own ':' / '.' / '=' / '(' live inside
    // the brackets, so the variant separator is the ':' that immediately follows the ']'. Splits a token into
    // its inner (between prefixLength and the ']') and its payload (after that ':'), rejecting a token with no
    // bracket-colon, an empty inner, or an empty payload — so a change to the parse contract (e.g. an escaped
    // ']') is made in one place rather than across every bracket parser.
    internal static class StyleBracketVariant
    {
        public static bool TrySplitBracket(string token, int prefixLength, out string inner, out string payload)
        {
            var close = token.IndexOf(']');
            if (close < 0 || close + 1 >= token.Length || token[close + 1] != ':')
            {
                inner = null;
                payload = null;
                return false;
            }

            inner = token.Substring(prefixLength, close - prefixLength);
            payload = token.Substring(close + 2);
            if (inner.Length == 0 || payload.Length == 0)
            {
                inner = null;
                payload = null;
                return false;
            }
            return true;
        }
    }
}
