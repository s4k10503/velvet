namespace Velvet
{
    // Recognizes the `text-balance` utility for StyleTextBalanceManipulator. Unlike gap-* /
    // grid-cols-*, text-balance carries no scale or arbitrary-value form — it is a bare, parameterless
    // flag — so the classifier is a single exact-match scan rather than a prefix + TryExtract pair.
    internal static class StyleTextBalanceClass
    {
        private const string ClassName = "text-balance";

        // Single-token half of HasTextBalanceClass. Its own predicate so the token name has ONE
        // definition — both the array scan below and the variant-payload gate (StyleVariantPayload)
        // resolve the family through here.
        public static bool IsTextBalanceToken(string cls) => cls == ClassName;

        // Cheap early-out gate: true when classNames carries the exact `text-balance` token. No
        // allocation — used to skip manipulator attach/lookup on the common element with no such class.
        public static bool HasTextBalanceClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsTextBalanceToken(cls))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
