using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Poses <c>.claude/hooks/refuse/pr_body_of_another_branch.py</c> a body of each shape it decides
    /// about, and reads the verdict it returns. <see cref="GuardCommandCoverageTests"/> asks what that
    /// guard recognises in a command and <see cref="PreExpansionPolicyTests"/> asks the single verdict
    /// its own probe poses; what it decides about a body was asked by neither. Of ten one-line changes
    /// to it, nine left both of those green — including one that accepted every body.
    /// </summary>
    /// <remarks>
    /// A refusal is matched on a phrase only its own message carries, not on the exit code alone: a
    /// check that stops firing and a check that fires for another reason both exit 2, which is how
    /// four of those ten read from outside — one of them landing on the tag for a file that will not
    /// read, which an unlabelled exit 2 would have hidden.
    /// </remarks>
    [TestFixture]
    internal sealed class PrBodyProvenanceGuardTests
    {
        private const string HookPath = ".claude/hooks/refuse/pr_body_of_another_branch.py";

        private static readonly (string Tag, string Phrase)[] Reasons =
        {
            ("missing", "does not exist"),
            ("stdin", "comes from stdin"),
            ("unexpanded-path", "still unexpanded"),
            ("moved", "changes directory"),
            ("assembled", "assembled by the shell"),
            ("no-origin", "names no issue"),
            ("unreadable", "cannot be read"),
            ("crashed", "failed to reach a verdict"),
        };

        // {DIR} is the fixture's own directory, which is not a git repository: the guard reads the body
        // and nothing else, so no case here needs one.
        private static readonly (string Command, string Expected)[] Accepted =
        {
            ("gh pr create --title x --body-file {DIR}/closes.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/refs.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/url.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/no-issue.md", "allow"),
            ("gh pr create --title x -F{DIR}/closes.md", "allow"),
            // Backticks and a `$` in an inline body are the description rather than a name for it.
            ("gh pr create --title x --body 'Closes #7. `Foo.Bar` now reads $HOME.'", "allow"),
            ("gh pr create --fill", "allow"),
            ("gh pr create --title x --template {DIR}/closes.md", "allow"),
            ("gh pr create --title x --editor", "allow"),
            ("gh pr create --title x --dry-run --body-file {DIR}/absent.md", "allow"),
            ("gh pr create --title x -h --body-file {DIR}/silent.md", "allow"),
            // A head is not read at all now. One naming a fork used to be refused, with the remedy
            // being to pass the head the command had already passed.
            ("gh pr create --title x --body-file {DIR}/closes.md --head someone:feat/x", "allow"),
            ("cd {DIR}/sub && gh pr create --title x --body-file {DIR}/closes.md", "allow"),
            ("gh pr list", "allow"),
            ("echo \"gh pr create --body-file {DIR}/silent.md\"", "allow"),
        };

        // {DIR}/relative.md is absent and {DIR}/sub/relative.md is not, so a move the guard fails to
        // see answers "missing" instead, which is what keeps the last two rows from passing on the
        // strength of the file being in the other directory.
        private static readonly (string Command, string Expected)[] Refused =
        {
            ("gh pr create --title x --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr create --title x --body-file {DIR}/colour.md", "no-origin"),
            ("gh pr create --title x --body-file {DIR}/mention.md", "no-origin"),
            ("gh pr create --title x --body 'A change to the pooled reset helper.'", "no-origin"),
            ("gh pr create --title x --web --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr create --title x --body 'Closes #7.' --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr create --title x --body-file {DIR}/absent.md", "missing"),
            // A directory exists and does not read, which is the one way to reach that branch without
            // a permission bit — and a run as root would read a file this fixture had made unreadable.
            ("gh pr create --title x --body-file {DIR}/sub", "unreadable"),
            ("gh pr create --title x --body-file -", "stdin"),
            ("gh pr create --title x --body-file $BODY", "unexpanded-path"),
            ("gh pr create --title x --body \"$(cat {DIR}/closes.md)\"", "assembled"),
            ("cd {DIR}/sub && gh pr create --title x --body-file relative.md", "moved"),
            ("builtin cd {DIR}/sub && gh pr create --title x --body-file relative.md", "moved"),
        };

        [Test]
        public void Given_ABodyNamingWhereItCameFrom_When_TheGuardAnswers_Then_EveryFormIsAllowed()
        {
            // Arrange
            var root = NewDirectory();
            (int Count, string Disagreements) observed;

            try
            {
                WriteBodies(root);

                // Act
                observed = Answers(root, Accepted);
            }
            finally
            {
                Delete(root);
            }

            // Assert — the count rides along because a driver that ran nothing disagrees with nothing.
            Assert.That(observed, Is.EqualTo((Accepted.Length, string.Empty)),
                "a guard that refuses a body naming its origin is one every pull request has to work around");
        }

        [Test]
        public void Given_ABodyThatNamesNoOriginOrCannotBeRead_When_TheGuardAnswers_Then_EachRefusalIsItsOwn()
        {
            // Arrange
            var root = NewDirectory();
            (int Count, string Disagreements) observed;

            try
            {
                WriteBodies(root);

                // Act
                observed = Answers(root, Refused);
            }
            finally
            {
                Delete(root);
            }

            // Assert
            Assert.That(observed, Is.EqualTo((Refused.Length, string.Empty)),
                "a refusal that fires for another reason gives another remedy, and exits 2 either way");
        }

        [Test]
        public void Given_AnEventTheGuardCannotRead_When_ItAnswers_Then_ItRefusesRatherThanFallingThrough()
        {
            // Arrange — a working directory that is not a string, which no check below is written for
            const string payload =
                "{\"tool_name\":\"Bash\",\"cwd\":12345,"
                + "\"tool_input\":{\"command\":\"gh pr create --title x --body-file b.md\"}}";

            // Act
            var answer = Ask(Path.GetFullPath(HookPath), payload);

            // Assert — exit 1 is not a refusal, so an unforeseen shape here runs the command anyway
            Assert.That(answer, Is.EqualTo("crashed"));
        }

        private static void WriteBodies(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            Write(root, "closes.md", "Closes #123.\n\nWhat this changes and why.\n");
            Write(root, "refs.md", "Refs #12 — the other half of that one.\n");
            Write(root, "url.md", "From https://github.com/s4k10503/velvet/issues/44, the second half.\n");
            Write(root, "no-issue.md", "No issue: found while reading the pool reset helpers.\n");
            Write(root, "silent.md", "A description that says nothing about where it came from.\n");
            // Six digits behind a `#` is a colour here, and the rule that read one as an issue would
            // have taken this body as an answer.
            Write(root, "colour.md", "The swatch this restores is #123456, measured off the panel.\n");
            // A number mentioned in prose closes nothing on merge, which is the half that hurt.
            Write(root, "mention.md", "This supersedes #448's other half, which is still open.\n");
            Write(root, Path.Combine("sub", "relative.md"), "Closes #1.\n");
        }

        private static void Write(string root, string name, string text) =>
            File.WriteAllText(Path.Combine(root, name), text);

        private static (int Count, string Disagreements) Answers(
            string root, (string Command, string Expected)[] table)
        {
            var hook = Path.GetFullPath(HookPath);
            var answered = new List<string>();
            var disagreements = new List<string>();
            foreach (var (command, expected) in table)
            {
                var posed = command.Replace("{DIR}", root);
                var answer = Answer(hook, root, posed);
                if (answer == null)
                {
                    continue;
                }

                answered.Add(answer);
                if (answer != expected)
                {
                    disagreements.Add($"{posed}\n    expected [{expected}] got [{answer}]");
                }
            }

            return (answered.Count, string.Join("\n", disagreements));
        }

        private static string Answer(string hook, string cwd, string command) =>
            Ask(hook, "{\"tool_name\":\"Bash\",\"cwd\":\"" + Escape(cwd)
                + "\",\"tool_input\":{\"command\":\"" + Escape(command) + "\"}}");

        /// <summary>"allow", the tag of the refusal the guard gave, or null when it could not be run.</summary>
        private static string Ask(string hook, string payload)
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
            start.ArgumentList.Add(hook);

            try
            {
                using var process = Process.Start(start);
                if (process == null)
                {
                    return null;
                }

                process.StandardInput.Write(payload);
                process.StandardInput.Close();
                process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(30000))
                {
                    process.Kill();
                    return null;
                }

                if (process.ExitCode == 0)
                {
                    return "allow";
                }

                var matched = Reasons
                    .Where(reason => stderr.Contains(reason.Phrase, StringComparison.Ordinal))
                    .Select(reason => reason.Tag)
                    .ToList();
                return matched.Count == 1 ? matched[0] : $"exit {process.ExitCode}: {string.Join("+", matched)}";
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string NewDirectory() =>
            Path.Combine(Path.GetTempPath(), "velvet-pr-body-" + Guid.NewGuid().ToString("N"));

        private static void Delete(string root)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
