namespace Velvet
{
    // Recognizes the `text-balance` utility for StyleTextBalanceManipulator. Unlike gap-* /
    // grid-cols-*, text-balance carries no scale or arbitrary-value form — it is a bare, parameterless
    // flag — so the classifier is a single exact-match scan rather than a prefix + TryExtract pair.
    internal static class StyleTextBalanceClass
    {
        private const string ClassName = "text-balance";

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
                if (cls == ClassName)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
