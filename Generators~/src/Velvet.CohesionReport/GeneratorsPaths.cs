using System;
using System.IO;

namespace Velvet.CohesionReport
{
    /// <summary>
    /// Locates checked-in directories relative to the solution file, so CI and local build-output layouts
    /// resolve the same way.
    /// </summary>
    internal static class GeneratorsPaths
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
    }
}
