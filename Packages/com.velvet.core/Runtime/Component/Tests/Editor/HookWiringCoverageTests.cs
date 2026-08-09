using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pairs the hook scripts in <c>.claude/hooks</c> with the two files that run them — the project
    /// settings and the agent definitions — in both directions. A hook is wired by path, so a rename
    /// leaves the wiring naming nothing and a hook leaves the tree referenced by nothing, and neither
    /// shows up as a failure: a guard that stops being invoked reports exactly what a guard that passes
    /// reports. Pairing by file name cannot separate the two files that do the running: an agent
    /// definition runs a hook for one agent type, the settings for every session. A guard over state any
    /// session can move declares <c>HOOK_SCOPE</c>, and is paired against the settings alone. A
    /// <c>PreToolUse</c> guard declares the tools it acts on as <c>HOOK_TOOLS</c>, and is paired
    /// against the matcher that decides which tools reach it.
    /// </summary>
    [TestFixture]
    internal sealed class HookWiringCoverageTests
    {
        private const string HookDirectory = ".claude/hooks";

        // Settings and agent frontmatter are where a hook is given an event to fire on. A skill or a guide
        // may name one in prose, and naming it there does not run it, so those are not read here — counting
        // a prose mention as wiring is how an unwired hook would pass.
        //
        // An agent definition is both at once, which is why only its frontmatter is read: the body below it
        // is prose, and a sentence there naming a hook's path used to satisfy this as fully as a
        // registration did.
        private static IEnumerable<string> WiringFiles() =>
            new[] { SettingsFile }.Concat(AgentDefinitions());

        private const string SettingsFile = ".claude/settings.json";

        private static IEnumerable<string> AgentDefinitions()
        {
            var agents = Path.GetFullPath(".claude/agents");
            return Directory.Exists(agents)
                ? Directory.GetFiles(agents, "*.md")
                : Enumerable.Empty<string>();
        }

        private static readonly Regex HookReferencePattern =
            new(@"\.claude/hooks/([A-Za-z0-9_./-]+)", RegexOptions.Compiled);

        [Test]
        public void Given_TheHookWiring_When_EachReferencedPathIsResolved_Then_EveryOneNamesAFileThatExists()
        {
            // Arrange
            var wiring = ReadWiring();
            Assume.That(wiring, Is.Not.Empty, "no hook reference was found to check");

            // Act
            var missing = wiring
                .Where(reference => !File.Exists(Path.GetFullPath(HookDirectory + "/" + reference.Name)))
                .Select(reference => $"{reference.Source} names {HookDirectory}/{reference.Name}")
                .Distinct()
                .ToList();

            // Assert
            Assert.That(missing, Is.Empty,
                "a hook is wired by path, so one that names nothing never fires:\n" + string.Join("\n", missing));
        }

        [Test]
        public void Given_TheHookDirectory_When_EachScriptIsTracedBack_Then_EveryOneIsWiredOrSourced()
        {
            // Arrange
            var wired = new HashSet<string>(ReadWiring().Select(reference => reference.Name), StringComparer.Ordinal);
            // __pycache__ is written beside a hook by anything that imports it, so its contents are
            // output rather than scripts. Counting them made this fixture fail for the presence of a
            // sibling fixture's bytecode, which says nothing about whether a guard is wired.
            var scripts = Directory
                .GetFiles(Path.GetFullPath(HookDirectory), "*", SearchOption.AllDirectories)
                .Select(RelativeToHookDirectory)
                .Where(script => !script.Contains("__pycache__", StringComparison.Ordinal))
                .ToList();
            Assume.That(scripts, Is.Not.Empty, "the hook directory is empty");

            // A file a wired hook sources is reached the same way a wired one is, so it is not an orphan.
            // Read out of the hooks rather than exempted by directory: a shared file that stops being
            // sourced is the same dead script as one that stops being wired.
            var sourced = string.Concat(scripts
                .Where(script => wired.Contains(script))
                .Select(script => File.ReadAllText(Path.GetFullPath(HookDirectory + "/" + script))));

            // Python imports name the module, not the file, so a shared file under lib/ is reached
            // by its stem and by nothing a wiring holds. Stem matching is looser than the full name
            // — a hook quoting a stem in prose would read as sourced when it is not — which is
            // accepted because reporting a genuinely imported file as an orphan blocks the sharing
            // lib/ exists for.
            var orphans = scripts
                .Where(script => !wired.Contains(script))
                .Where(script =>
                    !sourced.Contains(Path.GetFileName(script), StringComparison.Ordinal)
                    && !sourced.Contains(Path.GetFileNameWithoutExtension(script), StringComparison.Ordinal))
                .ToList();

            // Assert
            Assert.That(orphans, Is.Empty,
                $"nothing runs these, so whatever they guard is unguarded:\n{string.Join("\n", orphans)}");
        }

        // Which sessions a guard has to cover is a judgement about what it protects, not something
        // readable off the script, so the author declares it. The declaration is a line in the hook
        // rather than a table in this fixture: a table is a second place to edit, and the edit that
        // gets forgotten leaves a new guard passing while nothing runs it for most sessions.
        private static readonly Regex SessionScopePattern =
            new(@"^HOOK_SCOPE\s*=\s*""session""\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static List<string> SessionScopedHooks() =>
            Directory
                .GetFiles(Path.GetFullPath(HookDirectory), "*.py", SearchOption.AllDirectories)
                .Where(hook => !hook.Contains("__pycache__", StringComparison.Ordinal))
                .Where(hook => SessionScopePattern.IsMatch(File.ReadAllText(hook)))
                .Select(RelativeToHookDirectory)
                .ToList();

        // Folded into the assertions below rather than left to an `Assume`: deleting a declaration
        // empties every check keyed on it, and an `Assume` would report that as inconclusive, which
        // the runner does not count as a failure.
        private static IEnumerable<string> NothingToCheck(int subjects, string reason) =>
            subjects == 0 ? new[] { reason } : Enumerable.Empty<string>();

        private const string NoScopeDeclared =
            "no hook declares HOOK_SCOPE, so neither scope check has a subject left";

        [Test]
        public void Given_TheHooksDeclaringSessionScope_When_TheSettingsAreRead_Then_EveryOneIsRegisteredThere()
        {
            // Arrange
            var declared = SessionScopedHooks();
            var settings = Path.GetFullPath(SettingsFile);
            var registered = new HashSet<string>(
                from Match match in HookReferencePattern.Matches(
                    File.Exists(settings) ? File.ReadAllText(settings) : string.Empty)
                select match.Groups[1].Value,
                StringComparer.Ordinal);

            // Act
            var unregistered = NothingToCheck(declared.Count, NoScopeDeclared)
                .Concat(declared.Where(hook => !registered.Contains(hook)))
                .ToList();

            // Assert
            Assert.That(unregistered, Is.Empty,
                "an agent definition narrows an existing registration instead of making one, so a guard "
                + $"reachable only from there leaves every session outside it unguarded:\n{string.Join("\n", unregistered)}");
        }

        [Test]
        public void Given_TheHooksDeclaringSessionScope_When_TheAgentFrontMatterIsRead_Then_NoneIsNarrowedThere()
        {
            // Arrange
            var declared = SessionScopedHooks();
            var scoped = new HashSet<string>(declared, StringComparer.Ordinal);

            // Act
            var narrowed = NothingToCheck(declared.Count, NoScopeDeclared)
                .Concat(from file in AgentDefinitions()
                        from Match match in HookReferencePattern.Matches(FrontMatter(File.ReadAllText(file)))
                        where scoped.Contains(match.Groups[1].Value)
                        select $"{RepoRelative(file)} also runs {HookDirectory}/{match.Groups[1].Value}")
                .Distinct()
                .ToList();

            // Assert
            Assert.That(narrowed, Is.Empty,
                "the same guard registered on the event and on an agent fires twice, and a reader has "
                + $"nothing telling them which of the two was meant:\n{string.Join("\n", narrowed)}");
        }

        // Which tools a `PreToolUse` guard acts on is written twice — the settings matcher routes them
        // to it, and the hook's own gate decides which of what arrives it reads. Same one-place reason
        // as HOOK_SCOPE above: the declaration is a line in the hook, and this fixture compares it
        // against the registration rather than holding a table of its own.
        private static readonly Regex ToolSetPattern =
            new(@"^HOOK_TOOLS\s*=\s*\{([^}]*)\}\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex QuotedName = new(@"""([^""]*)""", RegexOptions.Compiled);

        // Sorted because each side means a set: the order a matcher's alternation or a Python literal
        // happens to be written in is not part of what either says.
        private static string Sorted(IEnumerable<string> names) =>
            string.Join("|", names.Select(name => name.Trim()).Where(name => name.Length > 0)
                .OrderBy(name => name, StringComparer.Ordinal));

        /// <summary>The tools a hook declares, or null when it declares none.</summary>
        private static string DeclaredTools(string hookName)
        {
            var path = Path.GetFullPath(HookDirectory + "/" + hookName);
            if (!File.Exists(path))
            {
                return null;
            }

            var declaration = ToolSetPattern.Match(File.ReadAllText(path));
            return declaration.Success
                ? Sorted(from Match name in QuotedName.Matches(declaration.Groups[1].Value)
                         select name.Groups[1].Value)
                : null;
        }

        /// <summary>Each PreToolUse hook path in the settings, with the matcher that routes tools to it.</summary>
        private static List<(string Hook, string Matcher)> PreToolUseRegistrations()
        {
            var settings = Path.GetFullPath(SettingsFile);
            var text = File.Exists(settings) ? File.ReadAllText(settings) : string.Empty;
            return (from entry in JsonObjects(JsonArrayValue(text, "PreToolUse"))
                    let matcher = MatcherPattern.Match(entry)
                    from Match reference in HookReferencePattern.Matches(entry)
                    select (reference.Groups[1].Value, matcher.Success ? matcher.Groups[1].Value : string.Empty))
                .Distinct()
                .ToList();
        }

        private static readonly Regex MatcherPattern =
            new(@"""matcher""\s*:\s*""([^""]*)""", RegexOptions.Compiled);

        // A hand-rolled reader because this assembly references no JSON library. It tracks string
        // state, so a bracket inside a matcher's regex or inside a command does not end a span.
        private static string JsonArrayValue(string text, string key)
        {
            var at = text.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            var open = at < 0 ? -1 : text.IndexOf('[', at);
            if (open < 0)
            {
                return string.Empty;
            }

            var depth = 0;
            foreach (var (index, character) in OutsideStrings(text, open))
            {
                if (character == '[')
                {
                    depth++;
                }
                else if (character == ']' && --depth == 0)
                {
                    return text.Substring(open + 1, index - open - 1);
                }
            }

            return string.Empty;
        }

        private static IEnumerable<string> JsonObjects(string text)
        {
            var depth = 0;
            var start = 0;
            foreach (var (index, character) in OutsideStrings(text, 0))
            {
                if (character == '{' && depth++ == 0)
                {
                    start = index;
                }
                else if (character == '}' && --depth == 0)
                {
                    yield return text.Substring(start, index - start + 1);
                }
            }
        }

        private static IEnumerable<(int Index, char Character)> OutsideStrings(string text, int from)
        {
            var inString = false;
            for (var index = from; index < text.Length; index++)
            {
                var character = text[index];
                if (inString)
                {
                    if (character == '\\')
                    {
                        index++;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }
                }
                else if (character == '"')
                {
                    inString = true;
                }
                else
                {
                    yield return (index, character);
                }
            }
        }

        private const string NoRegistrationRead =
            "no PreToolUse registration was read out of " + SettingsFile
            + ", so neither tool-set check has a subject left";

        [Test]
        public void Given_ThePreToolUseRegistrations_When_EachHookIsRead_Then_EveryOneDeclaresItsToolSet()
        {
            // Arrange
            var registrations = PreToolUseRegistrations();

            // Act
            var silent = NothingToCheck(registrations.Count, NoRegistrationRead)
                .Concat(from registration in registrations
                        where DeclaredTools(registration.Hook) == null
                        select $"{HookDirectory}/{registration.Hook} is routed {registration.Matcher}")
                .ToList();

            // Assert
            Assert.That(silent, Is.Empty,
                "a hook declaring no tool set leaves the matcher as the only written statement of which "
                + "tools it acts on, and its gate free to disagree with that in a literal nothing "
                + $"compares:\n{string.Join("\n", silent)}");
        }

        [Test]
        public void Given_ThePreToolUseRegistrations_When_EachMatcherIsComparedWithTheHooksOwnSet_Then_TheyNameTheSameTools()
        {
            // Arrange
            var registrations = PreToolUseRegistrations();

            // Act
            var apart = NothingToCheck(registrations.Count, NoRegistrationRead)
                .Concat(from registration in registrations
                        let routed = Sorted(registration.Matcher.Split('|'))
                        let declared = DeclaredTools(registration.Hook)
                        where declared != routed
                        select $"{HookDirectory}/{registration.Hook}: matcher routes {routed}, "
                               + $"HOOK_TOOLS admits {declared ?? "nothing"}")
                .ToList();

            // Assert
            Assert.That(apart, Is.Empty,
                "a tool named on one side alone is silent both ways round — dropped from the set it "
                + "reaches the hook and falls through, dropped from the matcher it never arrives and "
                + $"the set goes on claiming it:\n{string.Join("\n", apart)}");
        }

        [Test]
        public void Given_TheHooksDeclaringAToolSet_When_TheirSourceIsRead_Then_EveryOneComparesToolNameAgainstIt()
        {
            // Arrange
            var declaring = Directory
                .GetFiles(Path.GetFullPath(HookDirectory), "*.py", SearchOption.AllDirectories)
                .Where(hook => !hook.Contains("__pycache__", StringComparison.Ordinal))
                .Where(hook => ToolSetPattern.IsMatch(File.ReadAllText(hook)))
                .ToList();

            // Act — the gate has to read the declared name, not merely sit beside it. A set the gate
            // does not consult drifts against a literal spelled out in the comparison, and the two
            // checks above go on comparing the declaration to the matcher while the hook admits
            // something else.
            var unread = NothingToCheck(declaring.Count, "no hook declares HOOK_TOOLS")
                .Concat(from hook in declaring
                        where !File.ReadLines(hook).Any(line =>
                            line.Contains("tool_name", StringComparison.Ordinal)
                            && line.Contains("HOOK_TOOLS", StringComparison.Ordinal))
                        select $"{RepoRelative(hook)} declares HOOK_TOOLS and gates on something else")
                .ToList();

            // Assert
            Assert.That(unread, Is.Empty,
                "a declaration nothing reads is the same silence as no declaration:\n"
                + string.Join("\n", unread));
        }

        // Read from the front matter alone: the body below it is the agent's prompt, where a guard can
        // be named without being wired — the distinction WiringFiles draws for skills and guides.
        private static string FrontMatter(string text)
        {
            if (!text.StartsWith("---", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
            return end < 0 ? text : text.Substring(0, end);
        }

        // A file name a hook builds a path from, rather than one a wiring names. The lookbehind is
        // what forces a whole name instead of its tail — `merged.py` out of `branch_from_unmerged.py`
        // — and it must not list `/`, or a name written inside a path stops matching at every
        // position and the reference goes unread. Three of the five in the tree are path-qualified.
        private static readonly Regex NamedScriptPattern =
            new(@"(?<![.\w-])([A-Za-z0-9_][A-Za-z0-9_-]*\.(?:py|sh|bash|awk|ps1))", RegexOptions.Compiled);

        // Where a hook's siblings live. A name resolving to neither is either a typo or a script
        // somewhere new, and both want the failure: adding the directory here is what says the
        // second one was meant.
        private static readonly string[] SearchedDirectories = { HookDirectory, "scripts" };

        [Test]
        public void Given_TheHookScripts_When_EachScriptNameTheyBuildAPathFromIsResolved_Then_EveryOneExists()
        {
            // Arrange
            // The two tests above pair hooks against what runs them, and both stayed green while one
            // hook went on naming the shell file a port had replaced with a Python one. Nothing
            // compiles a name in a string, so the guard's whole deferral path went dead: it refused
            // the creation a live deferral had been armed for, and printed the instruction to arm
            // one.
            var known = new HashSet<string>(
                SearchedDirectories
                    .Select(Path.GetFullPath)
                    .Where(Directory.Exists)
                    .SelectMany(directory => Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                    .Select(Path.GetFileName),
                StringComparer.Ordinal);
            var hooks = Directory
                .GetFiles(Path.GetFullPath(HookDirectory), "*.py", SearchOption.AllDirectories)
                .Where(hook => !hook.Contains("__pycache__", StringComparison.Ordinal))
                .ToList();
            Assume.That(hooks, Is.Not.Empty, "no hook scripts were found to read");

            // Act
            var dangling = (from hook in hooks
                            from Match match in NamedScriptPattern.Matches(File.ReadAllText(hook))
                            let named = match.Groups[1].Value
                            where !known.Contains(named)
                            select $"{RepoRelative(hook)} names {named}")
                .Distinct()
                .ToList();

            // Assert
            Assert.That(dangling, Is.Empty,
                "a name nothing compiles outlives the file it named, and the hook goes quiet rather than "
                + "failing:\n" + string.Join("\n", dangling));
        }

        // What each payload reaches. The first two are answered before any guard reads a repository, so
        // they pose only whether the script loads; the third is a command five of these guards claim, so
        // it runs the readings and the verdict — the half a benign payload returns above, where a name
        // that stopped resolving raises and the tool proceeds.
        //
        // Its verdict depends on live repository state, so only the exit codes that mean "did not reach
        // one" fail: refusing and allowing are both a guard that answered.
        private static readonly (string Label, string Payload, bool MayRefuse)[] Payloads =
        {
            ("a Read event", "{\"tool_name\":\"Read\",\"tool_input\":{\"file_path\":\"README.md\"}}", false),
            ("an unclaimed Bash command", "{\"tool_name\":\"Bash\",\"cwd\":\"CWD\",\"tool_input\":{\"command\":\"ls\"}}", false),
            ("a merge", "{\"tool_name\":\"Bash\",\"cwd\":\"CWD\",\"tool_input\":{\"command\":\"gh pr merge 1 --squash --delete-branch\"}}", true),
        };

        [Test]
        public void Given_EveryRefusingGuard_When_APayloadIsPosed_Then_ItRunsToAVerdict()
        {
            // Arrange — a PreToolUse hook that exits anything but 2 lets the tool through, so a guard whose
            // imports or calls raise is a guard that has been deleted, reporting what one that ran and
            // found nothing reports. Every wiring check above passes for it: the path resolves, the file is
            // there, the name it builds is real. Only running it separates the two.
            var guards = Directory.GetFiles(Path.GetFullPath(RefuseDirectory), "*.py")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            // Act
            var broken = (from guard in guards
                          from payload in Payloads
                          let answer = Answer(guard, payload.Payload)
                          where answer.Exit != 0 && !(payload.MayRefuse && answer.Exit == 2)
                          select $"{Path.GetFileName(guard)} on {payload.Label}: exit {answer.Exit}\n{answer.Error}")
                .ToList();

            // Assert — a floor rather than the count, because an empty directory poses nothing and
            // reports nothing broken. Raise it with the tree, the way the harness scan's floor is raised.
            Assert.That((guards.Count >= 12, string.Join("\n", broken)), Is.EqualTo((true, string.Empty)),
                "these guards did not reach a verdict, and a hook that does not reach one is not consulted");
        }

        private const string RefuseDirectory = HookDirectory + "/refuse";

        /// <summary>Runs one hook against a payload and returns its exit code with whatever it wrote.</summary>
        private static (int Exit, string Error) Answer(string hook, string payload)
        {
            var start = new System.Diagnostics.ProcessStartInfo("python3")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-B");
            start.ArgumentList.Add(hook);

            using var process = System.Diagnostics.Process.Start(start);
            if (process == null)
            {
                return (-1, "python3 did not start");
            }

            process.StandardInput.Write(payload.Replace("CWD", Path.GetFullPath(".").Replace("\\", "\\\\")));
            process.StandardInput.Close();
            var error = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(60000))
            {
                process.Kill();
                return (-1, "timed out");
            }

            return (process.ExitCode, error);
        }

        private static List<(string Name, string Source)> ReadWiring() =>
            (from file in WiringFiles()
             let path = Path.GetFullPath(file)
             where File.Exists(path)
             from Match match in HookReferencePattern.Matches(WiringText(path))
             select (match.Groups[1].Value, RepoRelative(path))).ToList();

        /// <summary>The part of a wiring file that can actually run a hook.</summary>
        private static string WiringText(string path)
        {
            var text = File.ReadAllText(path);
            return path.EndsWith(".md", StringComparison.Ordinal) ? FrontMatter(text) : text;
        }

        private static string RelativeToHookDirectory(string path) =>
            Path.GetRelativePath(Path.GetFullPath(HookDirectory), path).Replace('\\', '/');

        private static string RepoRelative(string path) =>
            Path.GetRelativePath(Path.GetFullPath("."), path).Replace('\\', '/');
    }
}
