using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which commands <c>.claude/hooks/refuse/branch_from_unmerged.py</c> reads as creating a
    /// branch. The guard's only failure mode is silence — a spelling it does not recognise exits 0 with
    /// no output, which is what a guard with nothing to say also does — so the coverage is asserted
    /// rather than assumed. Eleven of the fifteen spellings below were missed at once, quoting the
    /// branch name among them.
    /// </summary>
    [TestFixture]
    internal sealed class BranchGuardParsingTests
    {
        private const string HookPath = ".claude/hooks/refuse/branch_from_unmerged.py";

        // Expected value is the comma-joined names the guard should extract, empty for no creation.
        private static readonly (string Command, string Expected)[] Table =
        {
            ("git checkout -b topic", "topic"),
            ("git checkout -b \"topic\"", "topic"),
            ("git checkout -b 'topic'", "topic"),
            ("git switch -c \"topic\"", "topic"),
            ("git branch \"topic\"", "topic"),
            ("git checkout -B topic", "topic"),
            ("git switch -C topic", "topic"),
            ("git switch --force-create topic", "topic"),
            ("git checkout -btopic", "topic"),
            ("git switch --create=topic", "topic"),
            ("git checkout -b topic 2>&1", "topic"),
            ("git checkout -b topic > /dev/null", "topic"),
            ("git branch topic >> /tmp/log", "topic"),
            ("git checkout -q -b topic", "topic"),
            ("FOO=1 git checkout -b topic", "topic"),
            ("/usr/bin/git checkout -b topic", "topic"),
            ("if true; then git checkout -b topic; fi", "topic"),
            ("(git checkout -b topic)", "topic"),
            ("cd /x && git checkout -b topic", "topic"),
            ("git checkout -b first && git checkout -b second", "first,second"),
            ("git -C /elsewhere checkout -b topic", "topic"),

            ("git branch", ""),
            ("git branch && git status", ""),
            ("git branch &", ""),
            ("git branch -a", ""),
            ("git branch -d old", ""),
            ("git branch --list", ""),
            ("git branch 2>&1", ""),
            ("git status", ""),
            ("git checkout main", ""),
            ("git switch main", ""),
            ("git commit -m \"git checkout -b nope\"", ""),
            ("gh pr create --body \"run git checkout -b nope\"", ""),
        };

        // One process for the whole table: the guard is imported once and asked about each command,
        // which keeps the fixture at a single spawn rather than one per row.
        private const string Driver =
            "import importlib.util,sys\n" +
            "spec=importlib.util.spec_from_file_location('guard', sys.argv[1])\n" +
            "guard=importlib.util.module_from_spec(spec)\n" +
            "spec.loader.exec_module(guard)\n" +
            "for line in sys.stdin.read().split('\\0'):\n" +
            "    if not line: continue\n" +
            "    print(','.join(made[0] for made in guard.creations(line)))\n";

        [Test]
        public void Given_TheCommandTable_When_TheBranchGuardParsesEach_Then_ItNamesExactlyTheBranchesCreated()
        {
            // Arrange
            var hook = Path.GetFullPath(HookPath);
            Assume.That(File.Exists(hook), Is.True, $"Precondition: {HookPath} exists");

            // Act
            var parsed = RunDriver(hook, Table.Select(row => row.Command));
            Assume.That(parsed, Is.Not.Null, "Precondition: python3 ran the guard");
            Assume.That(parsed.Count, Is.EqualTo(Table.Length), "Precondition: one answer per command");

            var disagreements = Table
                .Select((row, index) => (row, actual: parsed[index]))
                .Where(pair => pair.actual != pair.row.Expected)
                .Select(pair =>
                    $"{pair.row.Command}\n    expected [{pair.row.Expected}] got [{pair.actual}]")
                .ToList();

            // Assert
            Assert.That(string.Join("\n", disagreements), Is.Empty,
                "a spelling the guard does not read as a creation is refused by nothing and says so to nobody");
        }

        private static List<string> RunDriver(string hook, IEnumerable<string> commands)
        {
            var start = new ProcessStartInfo("python3")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // -B: importing the guard would otherwise leave a __pycache__ beside it, which the
            // wiring guard reads as a script nothing runs.
            start.ArgumentList.Add("-B");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(Driver);
            start.ArgumentList.Add(hook);

            try
            {
                using var process = Process.Start(start);
                if (process == null)
                {
                    return null;
                }

                process.StandardInput.Write(string.Join("\0", commands));
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(30000) || process.ExitCode != 0)
                {
                    return null;
                }

                var lines = output.Replace("\r\n", "\n").Split('\n').ToList();
                if (lines.Count > 0 && lines[^1].Length == 0)
                {
                    lines.RemoveAt(lines.Count - 1);
                }

                return lines;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
