using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which commands each of the smaller guards acts on. Each of them accepted a spelling of the
    /// thing it exists to refuse, and a guard that does not recognise a command reports exactly what a
    /// guard with nothing to say reports — so the recognition is asserted rather than assumed.
    /// </summary>
    /// <remarks>
    /// <c>.claude/hooks/refuse/shared_git_state.py</c> answers about operands rather than about the
    /// command alone, so its table is posed against a git repository this fixture builds in a
    /// temporary directory and names as the working directory. It went uncovered while the other four
    /// were covered, and it is the one that shipped a parsing defect.
    /// <para>
    /// That guard expands a glob operand and resolves the expansion only when it is a single name, so
    /// two of the cases below put the expansion to git itself instead of to the guard. They are the
    /// premise the table's glob rows rest on, and git owns it rather than this repository.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class GuardCommandCoverageTests
    {
        private const string Driver =
            "import importlib.util,sys,os\n" +
            "sys.path.insert(0, os.path.join(os.path.dirname(sys.argv[1]), '..', 'lib'))\n" +
            "spec=importlib.util.spec_from_file_location('guard', sys.argv[1])\n" +
            "guard=importlib.util.module_from_spec(spec)\n" +
            "spec.loader.exec_module(guard)\n" +
            "answer=eval(sys.argv[2])\n" +
            "for line in sys.stdin.read().split('\\0'):\n" +
            "    if not line: continue\n" +
            "    print(answer(guard, line))\n";

        private static readonly (string Command, string Expected)[] Sweeping =
        {
            ("git add -A", "True"),
            ("git add --all", "True"),
            ("git add .", "True"),
            ("git add .; git commit -m x", "True"),
            ("git add :/", "True"),
            ("git add \".\"", "True"),
            ("if true; then git add -A; fi", "True"),
            ("/usr/bin/git add -A", "True"),
            ("git -c core.pager=cat add -A", "True"),
            ("git -C /x add -A", "True"),
            ("git stage -A", "True"),

            ("git add src/foo.cs", "False"),
            ("git add -u", "False"),
            ("git add -p", "False"),
            ("git commit -m \"do not git add -A\"", "False"),
            ("git status", "False"),
        };

        // Built at runtime so the literal command does not sit in this file, where the guard under
        // test would read its own subject out of a commit diff.
        private static (string Command, string Expected)[] Merges()
        {
            var m = "gh pr " + "merge";
            return new[]
            {
                (m + " 333", "333"),
                (m + " --squash 333", "333"),
                (m + " 333 --squash", "333"),
                (m + " --auto --squash 12", "12"),
                (m + " --delete-branch 7 --squash", "7"),
                (m, "<current>"),
                ("gh pr comment 5 --body \"then " + m + " 333\"", "-"),
                ("echo '" + m + " 333'", "-"),
                ("gh pr view 333", "-"),
                ("git status", "-"),
            };
        }

        // Read against the fixture's own repository: `main`, `feat/x`, `release.txt` and `dup` are
        // branches in it, `kept.txt` and `sub` exist in it, and `gone.txt` names neither — the shape of
        // a path git can restore that the working tree no longer holds. It also holds files named after
        // three of those branches, which is what lets a glob expand onto a ref.
        private static readonly (string Command, string Expected)[] SharedState =
        {
            ("git checkout main", "checkout"),
            ("git checkout feat/x", "checkout"),
            ("git checkout \"feat/x\"", "checkout"),
            ("git checkout HEAD", "checkout"),
            ("git checkout -f main", "checkout"),
            ("git checkout main kept.txt", "checkout"),
            ("git checkout -b kept.txt", "checkout"),
            ("git checkout -B main", "checkout"),
            ("git checkout --detach HEAD", "checkout"),
            ("git checkout -", "checkout"),
            ("git checkout main; git status", "checkout"),
            ("if true; then git checkout main; fi", "checkout"),
            ("/usr/bin/git checkout main", "checkout"),
            ("git -c core.pager=cat checkout main", "checkout"),
            ("git -C sub checkout main", "checkout"),
            // A repository git cannot open leaves the question unanswered, and an unanswered question
            // takes the refusal rather than the pass. So does an operand the shell has yet to expand,
            // whose literal text resolves to nothing and would otherwise read as a path.
            ("git -C /velvet-no-such-tree checkout gone.txt", "checkout"),
            ("git checkout $BRANCH", "checkout"),
            ("git checkout \"$BRANCH\"", "checkout"),
            ("git checkout $(cat branch.txt)", "checkout"),
            ("git checkout `cat branch.txt`", "checkout"),

            // A glob the shell rewrites into one name that is also a branch. Neither a slash nor an
            // extension tells these apart from the restores below: `feat/x` and `release.txt` are
            // branches here, and both are legal refnames.
            ("git checkout m*", "checkout"),
            ("git checkout feat/*", "checkout"),
            ("git checkout r*", "checkout"),

            ("git checkout kept.txt", "-"),
            // Globs whose expansion cannot reach the branch-switching form: `dup*` and `*.txt` widen to
            // several names, and `sub/*` to none.
            ("git checkout *", "-"),
            ("git checkout dup*", "-"),
            ("git checkout *.txt", "-"),
            ("git checkout sub/*", "-"),
            ("git checkout k*", "-"),
            ("git checkout -- m*", "-"),
            ("git checkout gone.txt", "-"),
            ("git checkout kept.txt gone.txt", "-"),
            ("git checkout -- gone.txt", "-"),
            ("git checkout --ours gone.txt", "-"),
            ("git -C sub checkout gone.txt", "-"),

            ("git switch main", "switch"),
            ("git switch -c topic", "switch"),
            ("git stash", "stash"),
            ("git stash pop", "stash"),
            ("git stash list", "-"),
            ("git stash show", "-"),

            ("git status", "-"),
            ("git commit -m \"git checkout main\"", "-"),
            ("echo 'git checkout main'", "-"),
        };

        private static readonly (string Command, string Expected)[] Creations =
        {
            ("gh issue create --title x --body y", "--label,--assignee"),
            ("gh issue create --title x --label bug --assignee @me", ""),
            ("gh issue create --title x -l bug -a @me", ""),
            ("gh issue create --title x --label=bug --assignee=@me", ""),
            ("gh issue create --title x --body \"pass --label and --assignee here\"", "--label,--assignee"),
            ("gh pr create --title x --body y", "--label"),
            ("gh pr create --title x --label ci", ""),
            ("gh pr comment 5 --body \"run gh issue create --title x\"", ""),
            ("gh issue list", ""),
        };

        // Built at runtime for the same reason as the merge table above.
        private static (string Command, string Expected)[] Undeleted()
        {
            var m = "gh pr " + "merge";
            return new[]
            {
                (m + " 333", "333"),
                (m + " 333 --squash", "333"),
                (m + " --squash 333", "333"),
                (m, "<current>"),
                (m + " 333 --squash --delete-branch", "-"),
                (m + " --delete-branch 333 --squash", "-"),
                (m + " 333 -d", "-"),
                ("gh pr comment 5 --body \"then " + m + " 333\"", "-"),
                ("gh pr view 333", "-"),
                ("git status", "-"),
            };
        }

        [Test]
        public void Given_TheMergeTable_When_TheDeletionGuardReadsEach_Then_ItNamesOnlyTheMergesLeavingABranch()
        {
            // Arrange
            var hook = Path.GetFullPath(".claude/hooks/refuse/merge_without_branch_deletion.py");
            Assume.That(File.Exists(hook), Is.True, "Precondition: the guard exists");
            var table = Undeleted();

            // Act
            const string expression =
                "lambda g,c: ','.join(t or '<current>' for t in g.merges_without_deletion(c)) or '-'";
            var answers = Ask(hook, expression, table.Select(row => row.Command));
            Assume.That(answers?.Count, Is.EqualTo(table.Length), "Precondition: one answer per command");

            var disagreements = Disagreements(table, answers);

            // Assert
            Assert.That(disagreements, Is.Empty,
                "a merge the guard does not read leaves a branch nothing can safely delete later");
        }

        [Test]
        public void Given_TheStagingTable_When_TheBlindAddGuardReadsEach_Then_ItSeesOnlyTheSweepingForms()
        {
            // Arrange
            var hook = Path.GetFullPath(".claude/hooks/refuse/blind_git_add.py");
            Assume.That(File.Exists(hook), Is.True, "Precondition: the guard exists");

            // Act
            var answers = Ask(hook, "lambda g,c: str(g.sweeps(c))", Sweeping.Select(row => row.Command));
            Assume.That(answers?.Count, Is.EqualTo(Sweeping.Length), "Precondition: one answer per command");

            var disagreements = Disagreements(Sweeping, answers);

            // Assert
            Assert.That(disagreements, Is.Empty);
        }

        [Test]
        public void Given_TheMergeTable_When_TheStaleMergeGuardReadsEach_Then_ItNamesTheRequestBeingMerged()
        {
            // Arrange
            var hook = Path.GetFullPath(".claude/hooks/refuse/stale_merge.py");
            Assume.That(File.Exists(hook), Is.True, "Precondition: the guard exists");
            var table = Merges();

            // Act
            // The no-number form means the current branch's request, which is not the same answer as
            // no merge at all — joining them both to an empty string hid one behind the other.
            const string expression =
                "lambda g,c: ','.join(t or '<current>' for t in g.merge_targets(c)) or '-'";
            var answers = Ask(hook, expression, table.Select(row => row.Command));
            Assume.That(answers?.Count, Is.EqualTo(table.Length), "Precondition: one answer per command");

            var disagreements = Disagreements(table, answers);

            // Assert
            Assert.That(disagreements, Is.Empty);
        }

        [Test]
        public void Given_TheCreationTable_When_TheMetadataGuardReadsEach_Then_ItNamesOnlyTheFlagsAbsentAsFlags()
        {
            // Arrange
            var hook = Path.GetFullPath(".claude/hooks/refuse/metadata_less_create.py");
            Assume.That(File.Exists(hook), Is.True, "Precondition: the guard exists");

            // Act
            const string expression =
                "lambda g,c: ','.join([f for k,o in g.creations(c) if '--web' not in o "
                + "for f in ((['--label'] if not g.carries(o, g.LABEL_FLAGS) else []) "
                + "+ (['--assignee'] if k=='issue' and not g.carries(o, g.ASSIGNEE_FLAGS) else []))])";
            var answers = Ask(hook, expression, Creations.Select(row => row.Command));
            Assume.That(answers?.Count, Is.EqualTo(Creations.Length), "Precondition: one answer per command");

            var disagreements = Disagreements(Creations, answers);

            // Assert
            Assert.That(disagreements, Is.Empty);
        }

        [Test]
        public void Given_TheCheckoutTable_When_TheSharedStateGuardReadsEach_Then_ItRefusesOnlyWhatCanMoveHead()
        {
            // Arrange
            var hook = Path.GetFullPath(".claude/hooks/refuse/shared_git_state.py");
            Assume.That(File.Exists(hook), Is.True, "Precondition: the guard exists");
            var root = Path.Combine(Path.GetTempPath(), "velvet-guard-" + Guid.NewGuid().ToString("N"));
            string disagreements;

            try
            {
                Assume.That(BuildRepository(root), Is.True, "Precondition: git built the fixture repository");

                // Act
                var expression = "lambda g,c: ','.join(g.refusals(c, r'" + root + "')) or '-'";
                var answers = Ask(hook, expression, SharedState.Select(row => row.Command));
                Assume.That(answers?.Count, Is.EqualTo(SharedState.Length), "Precondition: one answer per command");

                disagreements = Disagreements(SharedState, answers);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }

            // Assert
            Assert.That(disagreements, Is.Empty,
                "a checkout read wrongly either retargets a branch another worktree is on or refuses a restore");
        }

        [Test]
        public void Given_AGlobExpandingToOneRefName_When_GitReceivesTheExpansion_Then_HeadMoves()
        {
            // Arrange
            var root = Path.Combine(Path.GetTempPath(), "velvet-glob-one-" + Guid.NewGuid().ToString("N"));
            (string Before, string After) head;

            try
            {
                Assume.That(BuildRepository(root), Is.True, "Precondition: git built the fixture repository");

                // Act — `git checkout r*` reaches git as the single name the shell expanded it to
                var before = Head(root);
                Git(root, "checkout", "release.txt");
                head = (before, Head(root));
            }
            finally
            {
                Delete(root);
            }

            // Assert
            Assert.That(head, Is.EqualTo(("main", "release.txt")),
                "the guard refuses a one-name expansion because git reads that name as a branch");
        }

        [Test]
        public void Given_AGlobExpandingToSeveralRefNames_When_GitReceivesTheExpansion_Then_HeadStays()
        {
            // Arrange
            var root = Path.Combine(Path.GetTempPath(), "velvet-glob-many-" + Guid.NewGuid().ToString("N"));
            (string Before, string After, bool LeadingNameIsARef) observed;

            try
            {
                Assume.That(BuildRepository(root), Is.True, "Precondition: git built the fixture repository");

                // Act — `git checkout dup*` reaches git as both names, the first of them a branch
                var before = Head(root);
                Git(root, "checkout", "dup", "dup2");
                observed = (before, Head(root),
                    Git(root, "rev-parse", "--verify", "--quiet", "--end-of-options", "dup^{commit}") == 0);
            }
            finally
            {
                Delete(root);
            }

            // Assert
            Assert.That(observed, Is.EqualTo(("main", "main", true)),
                "the guard passes a widened expansion because git declines to read its leading name as a branch");
        }

        // Built rather than posed against this repository, whose branches and whose absent files a table
        // cannot predict, and placed outside the working tree so nothing here can reach a repository
        // another session holds.
        private static bool BuildRepository(string root)
        {
            // --template=: a template directory on the machine running this would seed the fixture with
            // hooks, and a hook that rejects the commit below would report as git being unavailable.
            if (Git(null, "init", "-q", "--template=", "-b", "main", root) != 0)
            {
                return false;
            }

            // Files named after branches, so a glob operand has something to expand onto. `dup` and
            // `dup2` are committed, because an untracked first operand is an unmatched pathspec rather
            // than the ambiguity the widened-expansion cases are posed to observe, and they are
            // committed before the branches are cut so every branch carries them.
            File.WriteAllText(Path.Combine(root, "dup"), string.Empty);
            File.WriteAllText(Path.Combine(root, "dup2"), string.Empty);
            if (Git(root, "add", "dup", "dup2") != 0)
            {
                return false;
            }

            if (Git(root, "-c", "user.email=fixture@example.invalid", "-c", "user.name=fixture",
                    "-c", "commit.gpgsign=false", "commit", "-q", "-m", "root") != 0)
            {
                return false;
            }

            foreach (var branch in new[] { "feat/x", "release.txt", "dup" })
            {
                if (Git(root, "branch", branch) != 0)
                {
                    return false;
                }
            }

            File.WriteAllText(Path.Combine(root, "kept.txt"), string.Empty);
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "main"), string.Empty);
            File.WriteAllText(Path.Combine(root, "release.txt"), string.Empty);
            Directory.CreateDirectory(Path.Combine(root, "feat"));
            File.WriteAllText(Path.Combine(root, "feat", "x"), string.Empty);
            return true;
        }

        private static void Delete(string root)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        private static string Head(string root) => GitOutput(root, "rev-parse", "--abbrev-ref", "HEAD");

        private static int Git(string directory, params string[] arguments) =>
            Run(directory, arguments, out _);

        private static string GitOutput(string directory, params string[] arguments) =>
            Run(directory, arguments, out var output) == 0 ? output.Trim() : null;

        private static int Run(string directory, string[] arguments, out string output)
        {
            var start = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (directory != null)
            {
                start.ArgumentList.Add("-C");
                start.ArgumentList.Add(directory);
            }

            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            output = string.Empty;
            try
            {
                using var process = Process.Start(start);
                if (process == null)
                {
                    return -1;
                }

                output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                return process.WaitForExit(30000) ? process.ExitCode : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static string Disagreements((string Command, string Expected)[] table, List<string> answers) =>
            string.Join("\n", table
                .Select((row, index) => (row, actual: answers[index]))
                .Where(pair => pair.actual != pair.row.Expected)
                .Select(pair => $"{pair.row.Command}\n    expected [{pair.row.Expected}] got [{pair.actual}]"));

        private static List<string> Ask(string hook, string expression, IEnumerable<string> commands)
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
            start.ArgumentList.Add(expression);

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
