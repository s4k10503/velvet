#nullable enable

namespace Velvet
{
    /// <summary>The pathname, search and hash a target resolves to: React Router's <c>Path</c>, as <c>Hooks.UseResolvedPath</c> returns it.</summary>
    public sealed class ResolvedPath
    {
        /// <summary>The absolute pathname.</summary>
        public string Pathname { get; init; } = "/";

        /// <summary>The query string with its leading <c>?</c>, or the empty string.</summary>
        public string Search { get; init; } = string.Empty;

        /// <summary>The fragment with its leading <c>#</c>, or the empty string.</summary>
        public string Hash { get; init; } = string.Empty;
    }
}
