#nullable enable
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// How a <see cref="Router"/> starts: React Router's <c>createMemoryRouter</c> options for its in-memory
    /// history.
    /// </summary>
    public sealed class RouterOptions
    {
        /// <summary>
        /// The history the router starts with, each an absolute path with its query and fragment, oldest first, as
        /// <c>initialEntries</c>. Null is <c>["/"]</c>. The first entry's key is <c>"default"</c>.
        /// </summary>
        public IReadOnlyList<string>? InitialEntries { get; init; }

        /// <summary>
        /// The entry of <see cref="InitialEntries"/> the router starts on, as <c>initialIndex</c>, clamped into the
        /// list. Null is the last.
        /// </summary>
        public int? InitialIndex { get; init; }
    }
}
