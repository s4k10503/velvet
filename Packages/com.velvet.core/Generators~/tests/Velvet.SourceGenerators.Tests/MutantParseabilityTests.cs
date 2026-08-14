using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Parses every mutant <c>scripts/test_quality/mutation_check.py</c> generates over this package, and
    /// fails on any that the C# parser rejects where the unmutated file parses.
    /// </summary>
    /// <remarks>
    /// A mutant the compiler refuses asks the suite nothing, and the campaign has no way to tell that
    /// from a line no test covers — so the generator's health is what stands between a run and a verdict
    /// about nothing.
    /// <para/>
    /// The generator's own health guards live beside it in Python and read C# through the same model the
    /// generator does: a depth counted over parentheses, and a mask over comments and literals. That is a
    /// mirror pinned by a copy of itself, and it reported zero while ten live production lines produced
    /// cuts the parser rejects — a property pattern puts its colon inside braces at the enclosing
    /// parenthesis depth, and neither the probe nor its guard counted braces. This reads the same mutants
    /// with the compiler's own parser, which shares nothing with either.
    /// <para/>
    /// Syntax only. A mutant can parse and still not compile — a type error, an unassigned local — and
    /// answering that needs the whole compilation with its references, which is the Unity build rather
    /// than this. What it catches is the class the generator can actually create: an edit that leaves
    /// punctuation belonging to the construct around it.
    /// </remarks>
    public sealed class MutantParseabilityTests
    {
        [Fact]
        public void Given_EveryMutantThisPackageGenerates_When_ItIsParsed_Then_NoneIsRejected()
        {
            // Arrange
            var repository = RepositoryRoot();
            var mutants = Generate(repository);
            Assume.NotEmpty(mutants, "the generator emitted mutants to parse");

            // Act
            var rejected = new List<string>();
            foreach (var byFile in mutants.GroupBy(mutant => mutant.Path, StringComparer.Ordinal))
            {
                var lines = File.ReadAllLines(Path.Combine(repository, byFile.Key));
                var before = ErrorCount(string.Join("\n", lines));
                foreach (var mutant in byFile)
                {
                    if (mutant.Line < 1 || mutant.Line > lines.Length)
                    {
                        rejected.Add($"{byFile.Key}:{mutant.Line} is outside the file");
                        continue;
                    }
                    var swapped = (string[])lines.Clone();
                    swapped[mutant.Line - 1] = mutant.Text;
                    if (ErrorCount(string.Join("\n", swapped)) > before)
                    {
                        rejected.Add($"{byFile.Key}:{mutant.Line} ({mutant.Operator})\n    {mutant.Text.Trim()}");
                    }
                }
            }

            // The whole list to a file, because the assertion message is truncated and every entry is
            // a line somebody has to look at.
            if (rejected.Count > 0)
            {
                var listing = Path.Combine(Path.GetTempPath(), "velvet-unparseable-mutants.txt");
                File.WriteAllLines(listing, rejected);
                Console.WriteLine($"{rejected.Count} unparseable mutant(s); full list at {listing}");
                foreach (var entry in rejected)
                {
                    Console.WriteLine(entry);
                }
            }

            // Assert — the mutant count rides along, because a generator that emitted nothing rejects
            // nothing either, and that is the reading this exists to refuse.
            Assert.Equal(
                (true, 0),
                (mutants.Count > 1000, rejected.Count));
        }

        private static int ErrorCount(string source) =>
            CSharpSyntaxTree.ParseText(source)
                .GetDiagnostics()
                .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        private static string RepositoryRoot() =>
            Path.GetFullPath(Path.Combine(SolutionPaths.GeneratorsRoot(), "..", "..", ".."));

        private sealed record Mutant(string Path, int Line, string Operator, string Text);

        private static List<Mutant> Generate(string repository)
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
                        "the mutant generator did not run, so nothing here was parsed: " + error);
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
