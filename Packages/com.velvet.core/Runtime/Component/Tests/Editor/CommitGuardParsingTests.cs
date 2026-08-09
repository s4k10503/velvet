using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which commands <c>.claude/hooks/refuse/commit_failing_fast_checks.py</c> reads as a commit,
    /// and what it reads out of each: the repository the command names, whether the working tree is
    /// being committed, and any pathspecs. Those three decide what content is audited, so a
    /// misreading is not a missed refusal but an audit of the wrong bytes.
    /// </summary>
    [TestFixture]
    internal sealed class CommitGuardParsingTests
    {
        private const string HookPath = ".claude/hooks/refuse/commit_failing_fast_checks.py";

        // Expected is "directory|all|pathspecs" per invocation, joined by ';'. Empty for no commit.
        private static readonly (string Command, string Expected)[] Table =
        {
            ("git commit -m x", "|False|"),
            ("git commit -am wip", "|True|"),
            ("git commit -amwip", "|True|"),
            ("git commit -a -m wip", "|True|"),
            ("git commit --all -m wip", "|True|"),
            ("git commit --message=wip", "|False|"),
            ("git commit -q -m x", "|False|"),
            ("git commit m.py -m wip", "|False|m.py"),
            ("git commit -m \"wip\" -- a.py b.py", "|False|a.py,b.py"),
            ("git -C /elsewhere commit -m x", "/elsewhere|False|"),
            ("git -C \"/quoted path\" commit -m x", "/quoted path|False|"),
            ("FOO=1 git commit -m x", "|False|"),
            ("/usr/bin/git commit -m x", "|False|"),
            ("git add . && git commit -m x", "|False|"),

            ("git commit -m \"git commit -m nope\"", "|False|"),
            ("echo \"git commit -m x\"", ""),
            ("git status", ""),
            ("git log --oneline", ""),
        };

        private const string Driver =
            "import importlib.util,sys,os\n" +
            "sys.path.insert(0, os.path.join(os.path.dirname(sys.argv[1]), 'lib'))\n" +
            "spec=importlib.util.spec_from_file_location('guard', sys.argv[1])\n" +
            "guard=importlib.util.module_from_spec(spec)\n" +
            "spec.loader.exec_module(guard)\n" +
            "for line in sys.stdin.read().split('\\0'):\n" +
            "    if not line: continue\n" +
            "    found=guard.commit_invocations(line)\n" +
            "    print(';'.join('%s|%s|%s' % (d or '', a, ','.join(p)) for d,a,p in found))\n";

        [Test]
        public void Given_TheCommandTable_When_TheCommitGuardParsesEach_Then_ItReadsTheRepositoryAllFlagAndPathspecs()
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
                .Select(pair => $"{pair.row.Command}\n    expected [{pair.row.Expected}] got [{pair.actual}]")
                .ToList();

            // Assert
            Assert.That(string.Join("\n", disagreements), Is.Empty,
                "the audit reads what these three answers name, so a misreading checks the wrong bytes");
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
