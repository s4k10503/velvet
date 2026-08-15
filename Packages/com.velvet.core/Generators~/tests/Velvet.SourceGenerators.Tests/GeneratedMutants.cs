using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Runs <c>scripts/test_quality/mutation_check.py</c> over this package and hands back every mutant it
    /// generates, for guards that read those mutants with something other than the generator's own model
    /// of C#.
    /// </summary>
    internal static class GeneratedMutants
    {
        internal sealed record Mutant(string Path, int Line, string Operator, string Text);

        public static string RepositoryRoot() =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(SolutionPaths.GeneratorsRoot(), "..", "..", ".."));

        public static List<Mutant> Generate(string repository)
        {
            var emitted = Path.Combine(Path.GetTempPath(), "velvet-mutants-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var start = new ProcessStartInfo("python3")
                {
                    WorkingDirectory = repository,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                start.ArgumentList.Add("-B");
                start.ArgumentList.Add(Path.Combine("scripts", "test_quality", "mutation_check.py"));
                start.ArgumentList.Add("--project");
                start.ArgumentList.Add(repository);
                start.ArgumentList.Add("--emit-lines");
                start.ArgumentList.Add(emitted);

                using var process = Process.Start(start)!;
                var error = process.StandardError.ReadToEnd();
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        "the mutant generator did not run, so nothing here was read: " + error);
                }

                using var json = JsonDocument.Parse(File.ReadAllText(emitted));
                return json.RootElement.EnumerateArray()
                    .Select(item => new Mutant(
                        item.GetProperty("path").GetString()!,
                        item.GetProperty("line").GetInt32(),
                        item.GetProperty("operator").GetString()!,
                        item.GetProperty("text").GetString()!))
                    .ToList();
            }
            finally
            {
                if (File.Exists(emitted))
                {
                    File.Delete(emitted);
                }
            }
        }
    }
}
