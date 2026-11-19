using System;
using System.IO;
using System.Linq;

namespace Velvet.StyleTable
{
    /// <summary>
    /// Derives the utility class → property table from the bundled stylesheets and writes it as C# source,
    /// derives the corner-radius declaration sheet beside them, and derives the palette's tokens and C# table from
    /// its OKLCH source.
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
        private const string PaletteSourceOption = "--palette-source";
        private const string PaletteTableOutputOption = "--palette-output";

        private const int Ok = 0;
        private const int DerivationFailed = 1;
        private const int UsageError = 2;

        private static int Main(string[] args)
        {
            if (!TryParseArguments(args, out var options, out var usage))
            {
                Console.Error.WriteLine(usage);
                return UsageError;
            }

            if (!Directory.Exists(options.StylesDirectory) || !File.Exists(options.PaletteSourcePath))
            {
                Console.Error.WriteLine(
                    $"error: no stylesheet directory at '{options.StylesDirectory}' or no palette source at " +
                    $"'{options.PaletteSourcePath}'.");
                return UsageError;
            }

            // The palette's tokens are written into one of the sheets the table is derived from, so the table reads
            // the sheet as the palette leaves it rather than as it stood.
            var palettePath = Path.Combine(options.StylesDirectory, PaletteDerivation.SheetFileName);
            var palette = PaletteDerivation.Build(
                File.ReadAllText(options.PaletteSourcePath), File.ReadAllText(palettePath));
            var sheets = UssCascadeOrder.SheetsIn(options.StylesDirectory)
                .Select(sheet => Path.GetFileName(sheet.Path) == PaletteDerivation.SheetFileName
                    ? new UssSourceText(sheet.Path, palette.EmittedSheet)
                    : sheet)
                .ToList();
            var result = StyleUtilityTableBuilder.Build(sheets);
            var radii = CornerRadiusDeclarationSheet.Build(sheets);
            var problems = palette.Problems.AddRange(result.Problems).AddRange(radii.Problems);
            if (problems.Length > 0)
            {
                foreach (var problem in problems)
                {
                    Console.Error.WriteLine(problem.ToString());
                }
                Console.Error.WriteLine(
                    $"error: no output was written; {problems.Length} problem(s) above must be resolved first.");
                return DerivationFailed;
            }

            Report("palette tokens", palettePath, WriteIfChanged(palettePath, palette.EmittedSheet));
            Report("palette table", options.PaletteTablePath,
                WriteIfChanged(options.PaletteTablePath, palette.EmittedTable));
            Report($"{result.Table.Entries.Length} utility classes from {sheets.Count} stylesheet(s)", options.OutputPath,
                WriteIfChanged(options.OutputPath, StyleUtilityTableEmitter.Emit(result.Table)));
            Report("corner radius declarations", options.RadiusOutputPath,
                WriteIfChanged(options.RadiusOutputPath, radii.EmittedSheet));
            return Ok;
        }

        private static void Report(string what, string path, bool changed) =>
            Console.WriteLine($"[Velvet.StyleTable] {what} -> {path}{(changed ? "" : " (unchanged)")}");

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

        private static bool TryParseArguments(string[] args, out Options options, out string usage)
        {
            options = new Options();
            usage = $"usage: Velvet.StyleTable {StylesOption} <directory> {OutputOption} <file.g.cs> " +
                $"{RadiusOutputOption} <file.uss> {PaletteSourceOption} <file.css> {PaletteTableOutputOption} <file.g.cs>";

            for (var i = 0; i + 1 < args.Length; i += 2)
            {
                switch (args[i])
                {
                    case StylesOption:
                        options.StylesDirectory = args[i + 1];
                        break;
                    case OutputOption:
                        options.OutputPath = args[i + 1];
                        break;
                    case RadiusOutputOption:
                        options.RadiusOutputPath = args[i + 1];
                        break;
                    case PaletteSourceOption:
                        options.PaletteSourcePath = args[i + 1];
                        break;
                    case PaletteTableOutputOption:
                        options.PaletteTablePath = args[i + 1];
                        break;
                    default:
                        return false;
                }
            }
            return options.StylesDirectory.Length > 0 && options.OutputPath.Length > 0
                && options.RadiusOutputPath.Length > 0 && options.PaletteSourcePath.Length > 0
                && options.PaletteTablePath.Length > 0;
        }

        private sealed class Options
        {
            public string StylesDirectory { get; set; } = string.Empty;

            public string OutputPath { get; set; } = string.Empty;

            public string RadiusOutputPath { get; set; } = string.Empty;

            public string PaletteSourcePath { get; set; } = string.Empty;

            public string PaletteTablePath { get; set; } = string.Empty;
        }
    }
}
