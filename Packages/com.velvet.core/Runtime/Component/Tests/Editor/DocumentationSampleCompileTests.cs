using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Compiles every documentation sample that opts in, against the assembly a consumer actually binds to.
    /// <para>
    /// Nothing compiled them before, and <c>DocumentationDriftTests</c> strips fenced blocks by design — so
    /// a sample could name a parameter that does not exist or omit a required override and no guard
    /// anywhere noticed. The canonical store sample carried two such errors at once, and they were found by
    /// extracting the block and running a compiler by hand.
    /// </para>
    /// <para>
    /// Opt-in rather than universal, because the samples are fragments: loose members, a bare statement at a
    /// provider site, host-DI attributes that are not Velvet APIs. A harness that guesses scaffolding fails
    /// about its own scaffolding, which is the fixture-repairs-the-defect shape. Marking a block declares
    /// that it compiles as written under the preamble below, and makes that claim the author's rather than
    /// the harness's.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class DocumentationSampleCompileTests
    {
        // The fence's info string. A block opts in by carrying it; every other block is prose as far as
        // this fixture is concerned.
        private static readonly Regex CompilableBlockPattern = new(
            @"^```csharp\s+compile\s*$(?<body>.*?)^```\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.Singleline);

        // Everything a marked block may assume, and nothing else. A sample needing more says so in itself.
        private const string Preamble =
            "using System;\nusing System.Collections.Generic;\nusing UnityEngine;\n"
            + "using UnityEngine.UIElements;\nusing Velvet;\n";

        /// <summary>The editor's own compiler host, so nothing here depends on what is on PATH.</summary>
        /// <remarks>
        /// A test run has an editor by construction, and the editor ships both the host and the compiler it
        /// uses for the project itself. Reaching for <c>dotnet</c> instead would add an environment
        /// dependency to a job that has none.
        /// <para>
        /// Both are searched for under <c>applicationContentsPath</c> rather than composed from it. A
        /// composed path was macOS's bundle layout and did not exist on the Linux runner, and replacing one
        /// composition with another only moves the guess: the subtree under the contents directory differs
        /// per platform too. Searching asks the installation what it has.
        /// </para>
        /// </remarks>
        private static string Locate(string leaf, string requiredSegment = "")
        {
            var contents = UnityEditor.EditorApplication.applicationContentsPath;
            // A leaf name is not always unique under the installation — netstandard.dll sits in five places
            // and only the reference assembly under ref/ is the one to compile against. The caller says
            // which by naming a path segment, so the choice is stated rather than left to enumeration order.
            var found = Directory.EnumerateFiles(contents, leaf, SearchOption.AllDirectories)
                .Where(path => requiredSegment.Length == 0
                               || path.Replace('\\', '/').Contains("/" + requiredSegment + "/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .FirstOrDefault();
            // Loud rather than silent: a fixture that quietly skips because it could not find its compiler
            // reports exactly what one that ran and found nothing reports.
            Assert.That(found, Is.Not.Null, $"the editor at {contents} does not carry {leaf}");
            return found!;
        }

        private static IEnumerable<(string Path, string Body)> MarkedSamples()
        {
            foreach (var document in DocumentationCorpus.Files())
            {
                var text = File.ReadAllText(document);
                foreach (Match block in CompilableBlockPattern.Matches(text))
                {
                    yield return (document, block.Groups["body"].Value);
                }
            }
        }

        // GREEN_ON_BASE(refactor): the samples compile against what a consumer references, and none of
        // them needed the dependency this change stops adding.
        [Test]
        public void Given_EveryMarkedDocumentationSample_When_CompiledAgainstTheShippedAssembly_Then_ItCompiles()
        {
            // Arrange
            var samples = MarkedSamples().ToList();
            var references = References();

            // Act
            var failures = new List<string>();
            foreach (var (document, body) in samples)
            {
                var diagnostics = Compile(Preamble + body, references);
                if (diagnostics.Length > 0)
                {
                    failures.Add($"{document}:\n{diagnostics}");
                }
            }

            // Assert — the sample count rides along because an unmarked corpus compiles nothing and reports
            // no failure, which is this guard not running rather than this guard passing.
            Assert.That((samples.Count > 0, string.Join("\n\n", failures)), Is.EqualTo((true, string.Empty)),
                "a documentation sample marked compilable does not compile against the assembly a reader "
                + "would bind to");
        }

        private static IReadOnlyList<string> References()
        {
            return new List<string>
            {
                Locate("netstandard.dll", "ref"),
                Locate("UnityEngine.CoreModule.dll", "UnityEngine"),
                Locate("UnityEngine.UIElementsModule.dll", "UnityEngine"),
                Path.GetFullPath("Library/ScriptAssemblies/Velvet.dll"),
            };
        }

        /// <summary>Compiler output for one source, or empty when it compiled.</summary>
        private static string Compile(string source, IReadOnlyList<string> references)
        {
            var directory = Path.Combine(Path.GetTempPath(), "velvet-doc-sample-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "Sample.cs");
            File.WriteAllText(sourcePath, source);

            var start = new ProcessStartInfo(Locate("netcorerun"))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(Locate("csc.dll"));
            start.ArgumentList.Add("-nologo");
            start.ArgumentList.Add("-nostdlib");
            start.ArgumentList.Add("-target:library");
            start.ArgumentList.Add("-nullable:enable");
            start.ArgumentList.Add("-out:" + Path.Combine(directory, "Sample.dll"));
            foreach (var reference in references.Where(File.Exists))
            {
                start.ArgumentList.Add("-r:" + reference);
            }
            start.ArgumentList.Add(sourcePath);

            try
            {
                using var process = Process.Start(start);
                if (process == null)
                {
                    return "the compiler did not start";
                }

                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (!process.WaitForExit(120000))
                {
                    process.Kill();
                    return "the compiler timed out";
                }

                // csc reports warnings on stdout too, so the exit code decides and the text explains.
                return process.ExitCode == 0 ? string.Empty : Trim(output, sourcePath);
            }
            finally
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                    // A temp directory the OS still holds is not this fixture's problem to report.
                }
            }
        }

        private static string Trim(string output, string sourcePath)
        {
            var builder = new StringBuilder();
            foreach (var line in output.Split('\n').Where(line => line.Contains("error", StringComparison.Ordinal)))
            {
                builder.AppendLine("  " + line.Replace(sourcePath, "sample").Trim());
            }
            return builder.ToString();
        }
    }
}
