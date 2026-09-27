using System;
using System.IO;

namespace Velvet.StyleTable
{
    /// <summary>
    /// Derives the utility class → property table from the bundled stylesheets and writes it as C# source, and
    /// derives the corner-radius declaration sheet beside them.
    /// </summary>
    /// <remarks>
    /// This runs once per contributor, not once per consumer compile. The table is a function of package
    /// content alone — a consumer never edits the bundled stylesheets and nothing about their compilation can
    /// change the answer — so deriving it inside every user's build would re-parse two thousand rules to
    /// reproduce a constant. The output is committed like the analyzer assemblies are, and the derivation is
    /// re-run against the stylesheets by the test suite, which is what keeps the committed copy honest.
    /// </remarks>
    internal static class Program
    {
        private const string StylesOption = "--styles";
        private const string OutputOption = "--output";
        private const string RadiusOutputOption = "--radius-output";

        private const int Ok = 0;
        private const int DerivationFailed = 1;
        private const int UsageError = 2;

        private static int Main(string[] args)
        {
            if (!TryParseArguments(args, out var stylesDirectory, out var outputPath, out var radiusOutputPath, out var usage))
            {
                Console.Error.WriteLine(usage);
                return UsageError;
            }

            if (!Directory.Exists(stylesDirectory))
            {
                Console.Error.WriteLine($"error: no stylesheet directory at '{stylesDirectory}'.");
                return UsageError;
            }

            var sheets = UssCascadeOrder.SheetsIn(stylesDirectory);
            var result = StyleUtilityTableBuilder.Build(sheets);
            var radii = CornerRadiusDeclarationSheet.Build(sheets);
            var problems = result.Problems.AddRange(radii.Problems);
            if (problems.Length > 0)
            {
                foreach (var problem in problems)
                {
                    Console.Error.WriteLine(problem.ToString());
                }
                Console.Error.WriteLine(
                    $"error: neither output was written; {problems.Length} problem(s) above must be resolved first.");
                return DerivationFailed;
            }

            var source = StyleUtilityTableEmitter.Emit(result.Table);
            var changed = WriteIfChanged(outputPath, source);
            Console.WriteLine(
                $"[Velvet.StyleTable] {result.Table.Entries.Length} utility classes from {sheets.Count} " +
                $"stylesheet(s) -> {outputPath}{(changed ? "" : " (unchanged)")}");
            var radiiChanged = WriteIfChanged(radiusOutputPath, radii.EmittedSheet);
            Console.WriteLine(
                $"[Velvet.StyleTable] corner radius declarations -> {radiusOutputPath}" +
                $"{(radiiChanged ? "" : " (unchanged)")}");
            return Ok;
        }

        /// <summary>
        /// Leaves the file alone when the derivation reproduced it, so a no-op build does not restamp an asset
        /// Unity would then reimport.
        /// </summary>
        private static bool WriteIfChanged(string path, string source)
        {
            if (File.Exists(path) && string.Equals(File.ReadAllText(path), source, StringComparison.Ordinal))
            {
                return false;
            }
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, source);
            return true;
        }

        private static bool TryParseArguments(
            string[] args, out string stylesDirectory, out string outputPath, out string radiusOutputPath,
            out string usage)
        {
            stylesDirectory = string.Empty;
            outputPath = string.Empty;
            radiusOutputPath = string.Empty;
            usage = $"usage: Velvet.StyleTable {StylesOption} <directory> {OutputOption} <file.g.cs> " +
                $"{RadiusOutputOption} <file.uss>";

            for (var i = 0; i + 1 < args.Length; i += 2)
            {
                switch (args[i])
                {
                    case StylesOption:
                        stylesDirectory = args[i + 1];
                        break;
                    case OutputOption:
                        outputPath = args[i + 1];
                        break;
                    case RadiusOutputOption:
                        radiusOutputPath = args[i + 1];
                        break;
                    default:
                        return false;
                }
            }
            return stylesDirectory.Length > 0 && outputPath.Length > 0 && radiusOutputPath.Length > 0;
        }
    }
}
