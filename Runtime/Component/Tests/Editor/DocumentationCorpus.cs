using System.Collections.Generic;
using System.IO;

namespace Velvet.Tests
{
    /// <summary>
    /// The markdown files this suite machine-checks. One enumeration serves both the fixture that scans
    /// them and the fixture that asks whether CI starts for a change to one, so neither can be widened
    /// without the other following.
    /// </summary>
    internal static class DocumentationCorpus
    {
        // Unity's CWD during a test run is the project root (see CLAUDE.md), so these resolve the same way
        // whether the suite runs from the Editor or from -runTests batchmode.
        internal static string DocumentationDirectory =>
            Path.GetFullPath("Packages/com.velvet.core/Documentation~");

        // Yields (path, label) pairs. The label disambiguates the two identically-named README.md files
        // (repo root vs the package) in failure messages, since Path.GetFileName alone collapses both to
        // the same string.
        internal static IEnumerable<(string Path, string Label)> Files()
        {
            foreach (var file in Directory.GetFiles(DocumentationDirectory, "*.md"))
            {
                yield return (file, "Documentation~/" + Path.GetFileName(file));
            }
            yield return (Path.GetFullPath("README.md"), "README.md (repo root)");
            yield return (Path.GetFullPath("Packages/com.velvet.core/README.md"), "Packages/com.velvet.core/README.md");
            yield return (Path.GetFullPath("CLAUDE.md"), "CLAUDE.md");
            yield return (Path.GetFullPath("Packages/com.velvet.core/Generators~/README.md"),
                "Generators~/README.md");
        }
    }
}
