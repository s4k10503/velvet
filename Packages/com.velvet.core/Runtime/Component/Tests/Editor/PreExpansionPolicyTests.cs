using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds every refusing guard to what it says it does with an operand the shell has not expanded.
    /// <para>
    /// A <c>PreToolUse</c> hook is handed the command as typed, so <c>$BRANCH</c>, a backtick and
    /// <c>$(…)</c> arrive as themselves. A guard that resolves such an operand asks about the literal,
    /// and the resolution fails — which for most of them is the pass. The check does not happen, and a
    /// guard that did not run reports exactly what one that ran and found nothing reports.
    /// </para>
    /// <para>
    /// Which way to err is not uniform and cannot be decided once: a guard over merges must refuse,
    /// because a merge is what cannot be taken back, while one whose verdict is its command's own text
    /// resolves nothing and has nothing to miss. So each guard states its own answer, and states the
    /// command that demonstrates it — a guard added without either fails here rather than joining the
    /// set silently.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class PreExpansionPolicyTests
    {
        private const string RefuseDirectory = ".claude/hooks/refuse";

        private static readonly Regex PolicyPattern =
            new(@"^UNEXPANDED_POLICY = ""(?<policy>refuse|allow|n/a)""$",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex ProbePattern =
            new(@"^UNEXPANDED_PROBE = (?<quote>['""])(?<probe>.*)\k<quote>$",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static IReadOnlyList<string> Guards() =>
            Directory.GetFiles(Path.GetFullPath(RefuseDirectory), "*.py")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

        [Test]
        public void Given_EveryRefusingGuard_When_ItsSourceIsRead_Then_ItStatesAPreExpansionPolicy()
        {
            // Arrange
            var guards = Guards();

            // Act
            var silent = guards
                .Where(path =>
                {
                    var source = File.ReadAllText(path);
                    var policy = PolicyPattern.Match(source);
                    // "n/a" is a guard that reads no shell operand at all, so it owes no probe.
                    return !policy.Success
                           || (policy.Groups["policy"].Value != "n/a" && !ProbePattern.IsMatch(source));
                })
                .Select(Path.GetFileName)
                .ToList();

            // Assert — the guard count rides along because an empty directory states nothing either.
            Assert.That((guards.Count > 5, string.Join("\n", silent)), Is.EqualTo((true, string.Empty)),
                "a guard that resolves an operand decides about the literal when the shell has not "
                + "expanded it, and that decision is usually the pass; state which way this one errs");
        }

        [Test]
        public void Given_EveryStatedPolicy_When_ItsOwnProbeIsPosed_Then_TheGuardAnswersWhatItStates()
        {
            // Arrange
            var stated = Guards()
                .Select(path => (Path: path, Source: File.ReadAllText(path)))
                .Select(entry => (entry.Path,
                    Policy: PolicyPattern.Match(entry.Source).Groups["policy"].Value,
                    Probe: ProbePattern.Match(entry.Source).Groups["probe"].Value))
                .Where(entry => entry.Policy is "refuse" or "allow")
                .ToList();

            // Act
            var disagreements = stated
                .Select(entry => (entry.Path, entry.Policy, Observed: Answer(entry.Path, entry.Probe)))
                .Where(entry => entry.Observed != entry.Policy)
                .Select(entry => $"{Path.GetFileName(entry.Path)} states {entry.Policy}, answers {entry.Observed}")
                .ToList();

            // Assert — the stated count rides along because a parse that matched nothing agrees with
            // everything, which is the same silence this fixture is about.
            Assert.That((stated.Count > 5, string.Join("\n", disagreements)), Is.EqualTo((true, string.Empty)));
        }

        /// <summary>What the guard does with the probe: "refuse", "allow", or how it failed.</summary>
        private static string Answer(string hook, string probe)
        {
            var command = probe.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var cwd = Path.GetFullPath(".");
            var payload = "{\"tool_name\":\"Bash\",\"cwd\":\"" + cwd.Replace("\\", "\\\\")
                + "\",\"tool_input\":{\"command\":\"" + command + "\"}}";

            var start = new ProcessStartInfo("python3")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // -B: nothing here reads a hook's bytecode, and leaving none keeps it that way.
            start.ArgumentList.Add("-B");
            start.ArgumentList.Add(hook);

            using var process = Process.Start(start);
            if (process == null)
            {
                return "did not start";
            }

            process.StandardInput.Write(payload);
            process.StandardInput.Close();
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(60000))
            {
                process.Kill();
                return "timed out";
            }

            return process.ExitCode switch
            {
                0 => "allow",
                2 => "refuse",
                var code => $"exit {code}",
            };
        }
    }
}
