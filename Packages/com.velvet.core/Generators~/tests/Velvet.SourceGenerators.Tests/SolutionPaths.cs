using System;
using System.IO;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Locates checked-in directories that tests read from, anchored on the solution file rather than on a
    /// fixed number of parent hops, so CI and local build-output layouts resolve the same way.
    /// </summary>
    internal static class SolutionPaths
    {
        public static string GeneratorsRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Velvet.SourceGenerators.sln")))
            {
                dir = dir.Parent;
            }
            if (dir == null)
            {
                throw new InvalidOperationException(
                    "Could not locate Generators~ root (Velvet.SourceGenerators.sln) above " + AppContext.BaseDirectory);
            }
            return dir.FullName;
        }

        /// <summary>
        /// The Velvet runtime sources. Guards that re-derive the runtime surface parse these, so a moved tree
        /// must throw here rather than let those guards pass with nothing to compare against.
        /// </summary>
        public static string RuntimeRoot()
        {
            var root = Path.GetFullPath(Path.Combine(GeneratorsRoot(), "..", "Runtime"));
            if (!Directory.Exists(root))
            {
                throw new InvalidOperationException($"Velvet runtime sources not found at '{root}'.");
            }
            return root;
        }
    }
}
