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
    /// about and an event varying what those commands hold fixed, and reads the verdict it returns.
    /// <see cref="GuardCommandCoverageTests"/> asks what that guard recognises in a command
    /// and <see cref="PreExpansionPolicyTests"/> asks the single verdict its own probe poses; what it
    /// decides about a body was asked by neither. Of ten one-line changes to it, nine left both of
    /// those green — including one that accepted every body.
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
            ("two-bodies", "carries two bodies"),
            ("no-origin", "names no issue"),
            ("unreadable", "cannot be read"),
            ("crashed", "failed to reach a verdict"),
        };

        // {DIR} is the fixture's own directory, which is not a git repository: the guard reads the body
        // and nothing else, so no case here needs one.
        private static readonly (string Command, string Expected)[] Accepted =
        {
            // Every spelling of every keyword, because narrowing one of the four to the single form
            // CONTRIBUTING advertises leaves a table posing only that form green, and each of the
            // other spellings flips from allow to refuse.
            ("gh pr create --title x --body-file {DIR}/closes.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/close.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/closed.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/fixes.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/fix.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/fixed.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/resolves.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/resolve.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/resolved.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/refs.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/ref.md", "allow"),
            // A keyword against the number of an issue this answers only half of, which is a form
            // merged pull requests here already use and which was refused before the phrase was read.
            ("gh pr create --title x --body-file {DIR}/closes-part.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/url.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/no-issue.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/no-issue-indented.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/no-issue-stop.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/no-issue-later.md", "allow"),
            ("gh pr create --title x --body-file {DIR}/no-issue-lower.md", "allow"),
            ("gh pr create --title x -F{DIR}/closes.md", "allow"),
            ("gh pr create --title x -dF{DIR}/closes.md", "allow"),
            ("gh pr new --title x --body-file {DIR}/closes.md", "allow"),
            // Backticks and a `$` in an inline body are the description rather than a name for it.
            ("gh pr create --title x --body 'Closes #7. `Foo.Bar` now reads $HOME.'", "allow"),
            // Two bodies both naming an origin: whichever of them reaches the description, the
            // answer is in it, so no verdict on which is needed.
            ("gh pr create --title x --body 'Closes #7.' --body-file {DIR}/closes.md", "allow"),
            ("gh pr create --fill", "allow"),
            // A body flag given last has no value to read, which is a different way through the
            // reader than a command with no body flag at all.
            ("gh pr create --title x --body-file", "allow"),
            // A body naming nothing behind the flag, so the row answers whether `--template` is read
            // as a body at all rather than passing on the strength of what the file says.
            ("gh pr create --title x --template {DIR}/silent.md", "allow"),
            ("gh pr create --title x --editor", "allow"),
            ("gh pr create --title x --dry-run --body-file {DIR}/absent.md", "allow"),
            ("gh pr create --title x -h --body-file {DIR}/silent.md", "allow"),
            ("gh pr create --title x -dh --body-file {DIR}/silent.md", "allow"),
            ("gh pr create --title x --help --body-file {DIR}/silent.md", "allow"),
            ("gh pr create --title x --help=true --body-file {DIR}/silent.md", "allow"),
            ("gh pr create --title x --dry-run=true --body-file {DIR}/silent.md", "allow"),
            // A repeat settles on its last value, which is what turns the exemption back on here.
            ("gh pr create --title x --help=false --help --body-file {DIR}/silent.md", "allow"),
            ("gh pr new --fill", "allow"),
            // `gh pr edit` posts into the same squash message a created description lands in, so it
            // is asked the same question. An earlier version left it unasked, on the ground that a
            // guard would then refuse the remedy it hands out — but the remedy is an edit carrying
            // the answer, which the first row here allows; what the refusal table holds is an edit
            // that leaves the description silent.
            ("gh pr edit 1 --body-file {DIR}/closes.md", "allow"),
            ("gh pr edit 1 --title 'A pull request under another name'", "allow"),
            ("gh pr edit 1", "allow"),
            // A head is not read at all now. One naming a fork used to be refused, with the remedy
            // being to pass the head the command had already passed.
            ("gh pr create --title x --body-file {DIR}/closes.md --head someone:feat/x", "allow"),
            ("cd {DIR}/sub && gh pr create --title x --body-file {DIR}/closes.md", "allow"),
            ("gh pr list", "allow"),
            ("echo \"gh pr create --body-file {DIR}/silent.md\"", "allow"),
        };

        // {DIR}/relative.md is absent and {DIR}/sub/relative.md is not, so a move the guard fails to
        // see answers "missing" instead, which is what keeps the move rows from passing on the
        // strength of the file being in the other directory.
        private static readonly (string Command, string Expected)[] Refused =
        {
            ("gh pr create --title x --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr create --title x --body-file {DIR}/colour.md", "no-origin"),
            ("gh pr create --title x --body-file {DIR}/mention.md", "no-origin"),
            ("gh pr create --title x --body-file {DIR}/distant.md", "no-origin"),
            ("gh pr create --title x --body-file {DIR}/bare-no-issue.md", "no-origin"),
            ("gh pr create --title x --body-file {DIR}/mid-line.md", "no-origin"),
            ("gh pr create --title x --body 'A change to the pooled reset helper.'", "no-origin"),
            ("gh pr create --title x -b 'A change to the pooled reset helper.'", "no-origin"),
            ("gh pr create --title x --web --body-file {DIR}/silent.md", "no-origin"),
            // Either order, and its own refusal rather than the silent body's: naming one of the
            // two as the body that says nothing is a verdict on which of them reaches the
            // description, and that is the thing the command does not settle.
            ("gh pr create --title x --body 'Closes #7.' --body-file {DIR}/silent.md", "two-bodies"),
            ("gh pr create --title x --body-file {DIR}/closes.md "
             + "--body 'A change to the pooled reset helper.'", "two-bodies"),
            ("gh pr edit 1 --body-file {DIR}/closes.md "
             + "--body 'A change to the pooled reset helper.'", "two-bodies"),
            // A shell-assembled inline body beside a file that answers is two bodies before it is
            // an assembly, and the assembly refusal would ask for a file that is already passed.
            ("gh pr create --title x --body \"$(cat {DIR}/closes.md)\" "
             + "--body-file {DIR}/closes.md", "two-bodies"),
            // Two bodies agreeing is one answer, so it is judged as one.
            ("gh pr create --title x --body 'A note.' --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr create --title -h --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr create -t -h --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr create --help=false --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr create --dry-run=false --body-file {DIR}/silent.md", "no-origin"),
            // A repeat that turns the exemption off again. Reading any occurrence rather than the
            // last one exempted all three, and an exemption is decided before the body is opened,
            // so no rule below it looked at these files at all.
            ("gh pr create --title x --help --help=false --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr create --title x --dry-run --dry-run=false --body-file {DIR}/silent.md", "no-origin"),
            // `-h` and `--help` are read as one flag, so the last value is the last of both rather
            // than the last of each: keyed per token, the bare `--help` here stays an exemption of
            // its own.
            ("gh pr create --title x --help -h=false --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr new --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr edit 1 --body-file {DIR}/silent.md", "no-origin"),
            ("gh pr edit 1 --body 'A change to the pooled reset helper.'", "no-origin"),
            // Each spelling of a value gh takes, on the side where missing one lets the body through
            // unread: the accepted rows above pass whether or not the value was found. `-Fs` is the
            // shortest token that carries one attached.
            ("gh pr create --title x -F{DIR}/silent.md", "no-origin"),
            ("gh pr create --title x -dF{DIR}/silent.md", "no-origin"),
            ("gh pr create --title x --body-file={DIR}/silent.md", "no-origin"),
            ("gh pr create --title x -Fs", "no-origin"),
            // A relative path with nothing having moved is opened against the directory the command
            // runs in, not the one this guard runs in.
            ("gh pr create --title x --body-file silent.md", "no-origin"),
            // A segment carrying only an environment assignment has no command word, and is not a move.
            ("BODY=x && gh pr create --title x --body-file silent.md", "no-origin"),
            // The other side of the move set, which the rows below pin in one direction only: they
            // fail when a mover is dropped and pass when one is added.
            ("git push -u origin HEAD && gh pr create --title x --body-file silent.md", "no-origin"),
            ("echo x && gh pr create --title x --body-file silent.md", "no-origin"),
            // Sharing a prefix with a mover is not being one, either way round: `cdk` starts with
            // `cd`, and `pushd` starts with `push`. No other command word posed in this fixture does
            // either, so a comparison loosened to a prefix match answers every other row the way the
            // equality does, and only these two flip — to "moved", for a body it should have opened.
            ("cdk deploy && gh pr create --title x --body-file silent.md", "no-origin"),
            ("push && gh pr create --title x --body-file silent.md", "no-origin"),
            ("gh pr create --title x --body-file {DIR}/absent.md", "missing"),
            // A directory exists and does not read, which is the one way to reach that branch without
            // a permission bit — and a run as root would read a file this fixture had made unreadable.
            ("gh pr create --title x --body-file {DIR}/sub", "unreadable"),
            ("gh pr create --title x --body-file -", "stdin"),
            ("gh pr create --title x --body-file $BODY", "unexpanded-path"),
            ("gh pr create --title x --body \"$(cat {DIR}/closes.md)\"", "assembled"),
            ("cd {DIR}/sub && gh pr create --title x --body-file relative.md", "moved"),
            ("builtin cd {DIR}/sub && gh pr create --title x --body-file relative.md", "moved"),
            ("pushd {DIR}/sub && gh pr create --title x --body-file relative.md", "moved"),
            ("popd && gh pr create --title x --body-file relative.md", "moved"),
            // A command word spelled by path is read by its basename, the way the shared parser reads
            // one.
            ("/bin/cd {DIR}/sub && gh pr create --title x --body-file relative.md", "moved"),
            ("cd {DIR}/sub && echo x && gh pr create --title x --body-file relative.md", "moved"),
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

        // Posed as whole events rather than as commands, because each row varies something the tables
        // above hold fixed: the tool, the working directory, the type of the command, or the payload
        // being no event at all. What the guard cannot read it refuses; what is no body-posting
        // `gh pr` invocation of its it allows.
        private static readonly (string Payload, string Expected)[] Events =
        {
            // A working directory that is not a string reaches the code that opens the body.
            ("{\"tool_name\":\"Bash\",\"cwd\":12345,"
             + "\"tool_input\":{\"command\":\"gh pr create --title x --body-file b.md\"}}", "crashed"),
            // Stdin that is no event at all: refusing here would refuse every tool call in the session.
            ("this is not an event", "allow"),
            // JSON that parses but is no object, which reads nothing back and answers nothing either.
            ("[1, 2]", "allow"),
            // A command that is not text is no body update, and Bash will reject it on its own.
            ("{\"tool_name\":\"Bash\",\"cwd\":\".\",\"tool_input\":{\"command\":12345}}", "allow"),
            // A tool outside the declared set, carrying a command that would be refused under Bash, so
            // the tool name is the only thing between this row and a refusal.
            ("{\"tool_name\":\"Edit\",\"cwd\":\".\",\"tool_input\":"
             + "{\"command\":\"gh pr create --title x --body-file velvet-no-such-body.md\"}}", "allow"),
            // No working directory at all: a relative body path is opened against the one this runs in,
            // which is what keeps the missing key from reaching the opener as a null.
            ("{\"tool_name\":\"Bash\",\"tool_input\":"
             + "{\"command\":\"gh pr create --title x --body-file velvet-no-such-body.md\"}}", "missing"),
        };

        [Test]
        public void Given_AnEventRatherThanACommand_When_TheGuardAnswers_Then_ItRefusesOnlyWhatItCannotRead()
        {
            // Arrange
            var hook = Path.GetFullPath(HookPath);
            var answered = new List<string>();
            var disagreements = new List<string>();

            // Act
            foreach (var (payload, expected) in Events)
            {
                var answer = Ask(hook, payload);
                if (answer == null)
                {
                    continue;
                }

                answered.Add(answer);
                if (answer != expected)
                {
                    disagreements.Add($"{payload}\n    expected [{expected}] got [{answer}]");
                }
            }

            // Assert — exit 1 is not a refusal, so a shape that falls through runs the command anyway
            Assert.That((answered.Count, string.Join("\n", disagreements)),
                Is.EqualTo((Events.Length, string.Empty)));
        }

        private static void WriteBodies(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            // The four keywords CONTRIBUTING advertises, and the spellings of each that answer as
            // well. Only the advertised one used to be written for two of the four, so the form a
            // reader is told to use was posed by nothing.
            Write(root, "closes.md", "Closes #123.\n\nWhat this changes and why.\n");
            Write(root, "close.md", "Close #123, the half the pool reset left.\n");
            Write(root, "closed.md", "Closed #123 with the pooled reset helper.\n");
            Write(root, "fixes.md", "Fixes #12 — the half that was left.\n");
            Write(root, "fix.md", "Fix #12, measured against the pooled reset helper.\n");
            Write(root, "fixed.md", "Fixed #12 in the pooled reset helper.\n");
            Write(root, "resolves.md", "Resolves #12, measured against the pooled reset helper.\n");
            Write(root, "resolve.md", "Resolve #12 in the same pass as the reset helper.\n");
            Write(root, "resolved.md", "Resolved #12, measured against the pooled reset helper.\n");
            Write(root, "refs.md", "Refs #12 — the other half of that one.\n");
            Write(root, "ref.md", "Ref #12 — the other half of that one.\n");
            Write(root, "closes-part.md", "Closes part of #12 — the half the pool reset left.\n");
            Write(root, "url.md", "From https://github.com/s4k10503/velvet/issues/44, the second half.\n");
            Write(root, "no-issue.md", "No issue: found while reading the pool reset helpers.\n");
            // Four spellings of the same line, each free of the last: indented, ended with a full
            // stop, below the first line, and lower-case.
            Write(root, "no-issue-indented.md", "  No issue: contributor tooling only.\n");
            Write(root, "no-issue-stop.md", "No issue. Contributor tooling only.\n");
            Write(root, "no-issue-later.md", "A change to the release script.\n\nNo issue: it closes nothing.\n");
            Write(root, "no-issue-lower.md", "no issue: contributor tooling only.\n");
            // The phrase without a reason after it is the silence this asks about, in a form that
            // would satisfy a check that only looked for the phrase.
            Write(root, "bare-no-issue.md", "No issue:\n\nWhat this changes and why.\n");
            // The phrase inside a sentence says the opposite of the line, and reads the same to a
            // pattern that is not anchored at a line.
            Write(root, "mid-line.md", "There is no issue: this came out of a review round.\n");
            // A keyword and a number that are not one statement: nothing here closes #123 on merge.
            Write(root, "distant.md", "Closes the gap the pooled reset left; see #123 for the measurements.\n");
            Write(root, "silent.md", "A description that says nothing about where it came from.\n");
            // A one-character name, because an attached short value is read off the token being
            // longer than the flag and `-Fs` is the shortest token that carries one.
            Write(root, "s", "A description that says nothing about where it came from.\n");
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

                // Only exit 2 stops the command — PreToolUse runs the tool on every other non-zero
                // code — so the reason is read off the message only once the code says it refused.
                // Reading the message alone called a guard that exits 1 a refusal.
                if (process.ExitCode != 2)
                {
                    return $"exit {process.ExitCode}";
                }

                var matched = Reasons
                    .Where(reason => stderr.Contains(reason.Phrase, StringComparison.Ordinal))
                    .Select(reason => reason.Tag)
                    .ToList();
                return matched.Count == 1 ? matched[0] : $"exit 2: {string.Join("+", matched)}";
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
