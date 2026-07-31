namespace Velvet.SourceGenerators.Diagnostics
{
    /// <summary>
    /// The categories this assembly's diagnostic descriptors carry. AnalyzerReleases.Unshipped.md maps them
    /// onto the ID ranges they own.
    /// </summary>
    internal static class DiagnosticCategories
    {
        private const string Prefix = "Velvet.";

        // A category that lives only in a string literal is invisible to DocumentationDriftTests, which
        // strips literals from the corpus it resolves documentation references against: the name then has to
        // be excused by an allowlist entry, and that entry excuses the same word in every other document too.
        // nameof keeps the identifier the fixture resolves and the string the descriptors carry from parting.
        public const string Memoize = Prefix + nameof(Memoize);

        public const string Hooks = Prefix + nameof(Hooks);

        public const string Shape = Prefix + nameof(Shape);
    }
}
