using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// against the matcher that decides which tools reach it and against its own gate's behaviour.
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

        // GREEN_ON_BASE(construction): this change adds a settings entry and the file it names in one commit.
        // The base holds neither, so what it reads is its own registrations against its own scripts. Misspell
        // the new entry's path as `filter_naming_no_fixtures.py` and this reddens; no base run misspells it.
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

        // GREEN_ON_BASE(construction): this case fails for a guard added and never wired.
        // The base's directory holds no script of this change's, so it sweeps its own and finds them wired. Add
        // an unwired `refuse/nothing_runs_this.py` and this reddens, which no run of the base can arrange.
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
            HookScripts()
                .Where(hook => SessionScopePattern.IsMatch(File.ReadAllText(hook)))
                .Select(RelativeToHookDirectory)
                .ToList();

        private static List<string> HookScripts() =>
            Directory
                .GetFiles(Path.GetFullPath(HookDirectory), "*.py", SearchOption.AllDirectories)
                .Where(hook => !hook.Contains("__pycache__", StringComparison.Ordinal))
                .OrderBy(hook => hook, StringComparer.Ordinal)
                .ToList();

        // Folded into the assertions below rather than left to an `Assume`: deleting a declaration
        // empties every check keyed on it, and an `Assume` would report that as inconclusive, which
        // the runner does not count as a failure.
        private static IEnumerable<string> NothingToCheck(int subjects, string reason) =>
            subjects == 0 ? new[] { reason } : Enumerable.Empty<string>();

        private const string NoScopeDeclared =
            "no hook declares HOOK_SCOPE, so neither scope check has a subject left";

        // GREEN_ON_BASE(construction): the guard this change adds declares no `HOOK_SCOPE`.
        // So the subjects here are the base's own declarations against the base's own settings, a set this branch
        // does not add to. Unregister a guard that declares one and this reddens; a base run performs no such edit.
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

        // GREEN_ON_BASE(construction): the same absent declaration, read against the agent front matter instead.
        // This change names its guard in no agent's front matter either, so both sides stay the base's. Name a
        // session-scoped guard in `.claude/agents/velvet-implementer.md` and this reddens, which no base run arranges.
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
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal));

        private static List<string> DeclaringHooks() =>
            HookScripts().Where(hook => ToolSetPattern.IsMatch(File.ReadAllText(hook))).ToList();

        /// <summary>The tools a hook declares, or null when it names none.</summary>
        private static string DeclaredTools(string hookName)
        {
            var path = Path.GetFullPath(HookDirectory + "/" + hookName);
            if (!File.Exists(path))
            {
                return null;
            }

            var declaration = ToolSetPattern.Match(File.ReadAllText(path));
            if (!declaration.Success)
            {
                return null;
            }

            // An empty literal is a gate that admits no tool at all, which is the silence the
            // declaration check below reports. Answered as "declares none" rather than as an empty
            // set, because an empty string is not null and that check would pass the hook.
            var declared = Sorted(from Match name in QuotedName.Matches(declaration.Groups[1].Value)
                                  select name.Groups[1].Value);
            return declared.Length > 0 ? declared : null;
        }

        /// <summary>Each PreToolUse hook path in the settings, with every matcher entry that routes to it.</summary>
        private static List<(string Hook, List<string> Matchers)> PreToolUseRegistrations()
        {
            var settings = Path.GetFullPath(SettingsFile);
            var text = File.Exists(settings) ? File.ReadAllText(settings) : string.Empty;
            // Grouped by hook, because one guard may be registered under several entries and every one
            // of them reaches the same gate. Compared entry by entry, each would be held to the whole
            // declared set on its own and a guard split across two entries could not be written green.
            return (from entry in JsonObjects(JsonArrayValue(text, "PreToolUse"))
                    let matcher = MatcherPattern.Match(entry)
                    from Match reference in HookReferencePattern.Matches(entry)
                    select (Hook: reference.Groups[1].Value,
                            Matcher: matcher.Success ? matcher.Groups[1].Value : string.Empty))
                .Distinct()
                .GroupBy(registration => registration.Hook, StringComparer.Ordinal)
                .Select(hook => (hook.Key, hook.Select(registration => registration.Matcher).ToList()))
                .ToList();
        }

        private static readonly Regex MatcherPattern =
            new(@"""matcher""\s*:\s*""([^""]*)""", RegexOptions.Compiled);

        // A matcher is a regular expression, and the comparison below reads it as an alternation of
        // plain tool names. A matcher naming no tool of its own has no answer here that would stay
        // true: giving one means holding Claude Code's whole tool list in this fixture, where nothing
        // updates it when the product gains a tool. It is refused instead, so a guard acting on
        // several tools names them — `*`, an empty matcher, and a registration carrying no "matcher"
        // key, which reaches here as the empty one.
        private static readonly Regex ToolNamePattern = new(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

        /// <summary>The tools a hook's matcher entries route to it, or null when one names no set.</summary>
        private static string RoutedTools(IEnumerable<string> matchers)
        {
            var names = matchers.SelectMany(matcher => matcher.Split('|')).ToList();
            return names.Count > 0 && names.All(name => ToolNamePattern.IsMatch(name))
                ? Sorted(names)
                : null;
        }

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

        // GREEN_ON_BASE(construction): this change registers a guard and declares that guard's `HOOK_TOOLS` at once.
        // The base carries neither half, so it reads its own registrations against its own sources. Delete the new
        // guard's `HOOK_TOOLS` line and this reddens; no run of the base can delete one.
        [Test]
        public void Given_ThePreToolUseRegistrations_When_EachHookIsRead_Then_EveryOneDeclaresItsToolSet()
        {
            // Arrange
            var registrations = PreToolUseRegistrations();

            // Act
            var silent = NothingToCheck(registrations.Count, NoRegistrationRead)
                .Concat(from registration in registrations
                        where DeclaredTools(registration.Hook) == null
                        select $"{HookDirectory}/{registration.Hook} is routed "
                               + Quoted(registration.Matchers))
                .ToList();

            // Assert
            Assert.That(silent, Is.Empty,
                "a hook naming no tool leaves the matcher as the only written statement of which "
                + "tools it acts on, and its gate free to disagree with that in a literal nothing "
                + $"compares:\n{string.Join("\n", silent)}");
        }

        // GREEN_ON_BASE(construction): the new entry's matcher and the set its script declares both say `Bash`.
        // The pair arrived with this change and the base has neither half of it, so it compares its own pairs.
        // Drop `Bash` from either side of the new pair and this reddens, which no base run can perform.
        [Test]
        public void Given_ThePreToolUseRegistrations_When_EachMatcherIsComparedWithTheHooksOwnSet_Then_TheyNameTheSameTools()
        {
            // Arrange
            var registrations = PreToolUseRegistrations();

            // Act — a side that names no set is reported rather than compared with the other.
            var apart = NothingToCheck(registrations.Count, NoRegistrationRead)
                .Concat(from registration in registrations
                        let routed = RoutedTools(registration.Matchers)
                        let declared = DeclaredTools(registration.Hook)
                        where routed == null || declared == null
                              || !string.Equals(routed, declared, StringComparison.Ordinal)
                        select $"{HookDirectory}/{registration.Hook}: matcher {Quoted(registration.Matchers)} "
                               + $"routes {routed ?? "no set of tool names"}, "
                               + $"HOOK_TOOLS admits {declared ?? "nothing"}")
                .ToList();

            // Assert
            Assert.That(apart, Is.Empty,
                "a tool named on one side alone is silent both ways round — dropped from the set it "
                + "reaches the hook and falls through, dropped from the matcher it never arrives and "
                + $"the set goes on claiming it:\n{string.Join("\n", apart)}");
        }

        private static string Quoted(IEnumerable<string> matchers) =>
            string.Join(" and ", matchers.Select(matcher => "\"" + matcher + "\""));

        // GREEN_ON_BASE(construction): the gate this change adds reads `HOOK_TOOLS` rather than a literal.
        // The base holds no gate of this change's, so it reads the ones it does hold against the sets beside them.
        // Replace the new gate's `HOOK_TOOLS` with a literal and this reddens; no base run rewrites a gate.
        [Test]
        public void Given_TheHooksDeclaringAToolSet_When_TheirSourceIsRead_Then_EveryOneComparesToolNameAgainstIt()
        {
            // Arrange
            var declaring = DeclaringHooks();

            // Act — the gate has to read the declared name, not merely sit beside it. A set the gate
            // does not consult drifts against a literal spelled out in the comparison, and the two
            // checks above go on comparing the declaration to the matcher while the hook admits
            // something else.
            var unread = NothingToCheck(declaring.Count, NoToolSetDeclared)
                .Concat(from hook in declaring
                        where !WithoutProse(File.ReadAllText(hook)).Split('\n').Any(line =>
                            line.Contains("tool_name", StringComparison.Ordinal)
                            && line.Contains("HOOK_TOOLS", StringComparison.Ordinal))
                        select $"{RepoRelative(hook)} declares HOOK_TOOLS and gates on something else")
                .ToList();

            // Assert
            Assert.That(unread, Is.Empty,
                "a declaration nothing reads is the same silence as no declaration:\n"
                + string.Join("\n", unread));
        }

        private const string NoToolSetDeclared = "no hook declares HOOK_TOOLS";

        /// <summary>A hook's source with its comments and its triple-quoted spans dropped.</summary>
        private static string WithoutProse(string source)
        {
            // Prose satisfied the reading above: a comment naming tool_name and HOOK_TOOLS together
            // passed it while the hook's gate compared a literal. Only the triple-quoted spans go
            // with the comments — the gate's own subject is the string "tool_name", so dropping every
            // string literal would take the line being looked for along with the prose.
            var kept = new StringBuilder(source.Length);
            var index = 0;
            while (index < source.Length)
            {
                var character = source[index];
                if (character == '#')
                {
                    while (index < source.Length && source[index] != '\n')
                    {
                        index++;
                    }
                }
                else if (TripleQuoteAt(source, index))
                {
                    var end = source.IndexOf(source.Substring(index, 3), index + 3, StringComparison.Ordinal);
                    index = end < 0 ? source.Length : end + 3;
                    kept.Append('\n');
                }
                else if (character == '"' || character == '\'')
                {
                    var end = index + 1;
                    while (end < source.Length && source[end] != character)
                    {
                        end += source[end] == '\\' ? 2 : 1;
                    }

                    end = Math.Min(end, source.Length - 1);
                    kept.Append(source, index, end - index + 1);
                    index = end + 1;
                }
                else
                {
                    kept.Append(character);
                    index++;
                }
            }

            return kept.ToString();
        }

        private static bool TripleQuoteAt(string source, int index) =>
            index + 2 < source.Length
            && (source[index] == '"' || source[index] == '\'')
            && source[index + 1] == source[index]
            && source[index + 2] == source[index];

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

        // GREEN_ON_BASE(construction): the subjects here are the hook scripts the tree itself holds.
        // The base holds none of this change's, so the one name the new guard spells is read on the branch alone.
        // Misspell the `base_red_check.py` it builds a path to and this reddens, which is an edit no base run makes.
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
            var hooks = HookScripts();
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
            ("an unclaimed Bash command", "{\"tool_name\":\"Bash\",\"cwd\":\"%PROJECT%\",\"tool_input\":{\"command\":\"ls\"}}", false),
            ("a merge", "{\"tool_name\":\"Bash\",\"cwd\":\"%PROJECT%\",\"tool_input\":{\"command\":\"gh pr merge 1 --squash --delete-branch\"}}", true),
        };

        // GREEN_ON_BASE(construction): this sweep's subject is whatever the refuse directory holds.
        // The base's directory holds no guard of this change's, so the base measured nothing about the new one.
        // Break an import in `filter_naming_no_fixture.py` and this reddens; no base run breaks one.
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

        // One payload each declaring guard has an answer to, between them. Which guard answers which
        // is not asserted: the check below needs each guard to answer something under a tool name it
        // is routed and nothing at all under one it is not, so a payload no guard answers costs a run
        // rather than a wrong verdict.
        //
        // %SCRATCH% is a directory this fixture makes, and writes a released CHANGELOG into for the
        // payload that names one. Pointed at the checkout instead, a guard reading branch state answers one way on main and
        // another on a branch, and the probe would pass or fail with whichever tree the suite happened
        // to run in. %PROJECT% is named by the one payload whose guard reads the repository's own
        // top-level directories.
        private static readonly (string Label, string Body)[] GatePayloads =
        {
            ("a merge naming an unexpanded pull request",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":{\"command\":\"gh pr merge $PR --squash\"}"),
            ("a sweeping git add",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":{\"command\":\"git add -A\"}"),
            ("a stash",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":{\"command\":\"git stash\"}"),
            ("a branch creation",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":{\"command\":\"git checkout -b probe\"}"),
            ("a Library seed whose source is unexpanded",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":{\"command\":\"rsync -a $OTHER/Library/ Library/\"}"),
            ("a commit scoped by an unexpanded pathspec",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":{\"command\":\"git commit -m probe -- $PATHS\"}"),
            ("a commit message read from an unexpanded path",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":{\"command\":\"git commit -F $MSG\"}"),
            ("an issue creation carrying no metadata",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":{\"command\":\"gh issue create --title probe\"}"),
            ("a background command naming a repository path relatively",
             "\"cwd\":\"%PROJECT%\",\"tool_input\":{\"command\":\"python3 scripts/release/release_notes.py\","
             + "\"run_in_background\":true}"),
            // Backgrounded, repeating, and over a file the watcher writes — the three the poller
            // guard needs together. It runs no program of the watcher's, so the file name is the
            // whole subject.
            ("a backgrounded wait on the watcher's ready file",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":"
             + "{\"command\":\"until [ -s ~/.velvet-pr-ready ]; do sleep 60; done\","
             + "\"run_in_background\":true}"),
            ("a pull request created from a body file that is not there",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":"
             + "{\"command\":\"gh pr create --title x --body-file velvet-no-such-body.md\"}"),
            ("an entry filed into a released section",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":{\"file_path\":\"%SCRATCH%/CHANGELOG.md\","
             + "\"old_string\":\"- As shipped.\","
             + "\"new_string\":\"- As shipped.\\n\\n- Smuggled in.\"}"),
            ("an amend of a commit git cannot place",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":{\"command\":\"git commit --amend --no-edit\"}"),
            // Both spellings of the edit in one payload, because the guard that answers it is routed
            // two tools and each is posed separately: the content is what a Write reads, and the
            // pair beneath it is what an Edit reads.
            ("a declaration whose first line breaks off",
             "\"cwd\":\"%SCRATCH%\",\"tool_input\":{\"file_path\":\"%SCRATCH%/DeclaringTests.cs\","
             + "\"content\":" + Quoted(DeclaringFixture + FragmentaryDeclaration) + ","
             + "\"old_string\":" + Quoted(SettledDeclaration) + ","
             + "\"new_string\":" + Quoted(FragmentaryDeclaration) + "}"),
            // %PROJECT% rather than %SCRATCH%, this being the payload whose subject is a file the
            // repository tracks: the scratch directory is created without a git repository in it,
            // so a guard reading git for that would answer about a path in no tree at all.
            ("a shell command rewriting a tracked file",
             "\"cwd\":\"%PROJECT%\",\"tool_input\":{\"command\":"
             + "\"sed -i '' -e s/a/b/ Packages/com.velvet.core/CHANGELOG.md\"}"),
            // %PROJECT% again, and for the same reason: the guard answering this reads which fixture
            // classes the test sources declare, and the scratch directory holds none, where it stands
            // down rather than refusing every filter posed outside a Unity project.
            ("a test run filtered to a class nothing declares",
             "\"cwd\":\"%PROJECT%\",\"tool_input\":{\"command\":"
             + "\"Unity -runTests -batchmode -testFilter Velvet.Tests.NoFixtureIsCalledThis\"}"),
        };

        // A declaration reading as a claim on its own line, and one the reader would take
        // mid-clause. The second ends on a word no English clause ends on, which is the half of
        // "does not stand alone" a script can decide.
        //
        // The marker is assembled rather than spelled, because base_red_check.py counts one per
        // line it occurs on and would read these two as declarations this fixture wrote over no
        // case at all.
        private const string Marker = "GREEN_ON" + "_BASE(characterization)";
        private const string SettledDeclaration =
            "        // " + Marker + ": the base already separates these two.\n";
        private const string FragmentaryDeclaration =
            "        // " + Marker + ": the base already separates these two and\n"
            + "        // the branch does not change that.\n";
        private const string DeclaringFixture =
            "namespace Velvet.Tests\n{\n    internal sealed class DeclaringTests\n    {\n";

        // No matcher in the settings routes this, so a guard that answers under it has a gate that is
        // reading something other than the event's tool name.
        private const string UnroutedTool = "VelvetNoToolIsCalledThis";

        // GREEN_ON_BASE(construction): the row this change appends to `GatePayloads` sits last in the table.
        // So on the base each guard settles on the row it settled on without this branch, and the guard the row was
        // added for is absent there. Invert the new gate's `tool_name` comparison and this reddens, which no base
        // run can arrange.
        [Test]
        public void Given_TheHooksDeclaringAToolSet_When_EachIsPosedUnderARoutedNameAndAnUnroutedOne_Then_OnlyTheRoutedOneAnswers()
        {
            // Arrange
            var probes = (from hook in DeclaringHooks()
                          from tool in (DeclaredTools(RelativeToHookDirectory(hook)) ?? string.Empty)
                              .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                          select (Hook: hook, Tool: tool)).ToList();

            // Act — which way the gate points is what nothing else here reads. Every check above holds
            // for a gate with its `not` dropped: the declaration is there, it equals the matcher, and
            // the line still names both. What separates the two is that the inverted gate returns
            // before its readings for exactly the tools it exists to read, and runs them for the rest.
            var wrong = NothingToCheck(probes.Count, "no hook declares a tool to pose a payload under")
                .Concat(probes.SelectMany(probe => GateFaults(probe.Hook, probe.Tool)))
                .ToList();

            // Assert
            Assert.That(wrong, Is.Empty,
                "answering none of them is a gate returning before its readings for a tool it is routed "
                + "— what an inverted gate does — a GatePayloads table holding nothing that guard "
                + "decides about, which is what a guard added since the table was last extended wants a "
                + "row for, or a guard exiting neither 0 nor 2 under any of them, which is one raising "
                + "rather than deciding. Answering one posed under a tool nothing routes is a gate "
                + $"reading something other than the event's tool name:\n{string.Join("\n", wrong)}");
        }

        private static IEnumerable<string> GateFaults(string hook, string tool)
        {
            var answering = GatePayloads.FirstOrDefault(
                payload => Answered(hook, Posed(payload.Body, tool)));
            if (answering.Body == null)
            {
                yield return $"{RepoRelative(hook)} answers none of the {GatePayloads.Length} "
                             + $"payloads posed as {tool}";
            }
            else if (Answered(hook, Posed(answering.Body, UnroutedTool)))
            {
                yield return $"{RepoRelative(hook)} answers {answering.Label} posed as {UnroutedTool}, "
                             + "which nothing routes to it";
            }
        }

        private static string Posed(string body, string toolName) =>
            "{\"tool_name\":\"" + toolName + "\"," + body + "}";

        /// <summary>Whether a hook answered a payload rather than returning before its readings.</summary>
        private static bool Answered(string hook, string payload)
        {
            // Not the exit code alone. blind_git_add.py refuses by printing a deny decision and exiting
            // 0, so a reading that took 0 for silence would score its refusal as a gate that returned.
            // What a hook writes merely by being loaded is subtracted rather than scored: a warning
            // raised at import lands before the gate reads the tool name, so it would answer under
            // every name including the one nothing routes, and the check above would read that as a
            // gate pointing the wrong way.
            var answer = Probe(hook, payload);
            return answer.Exit == 2 || (answer.Exit == 0 && Wrote(answer) != Loading(hook));
        }

        // Both readings above go through here, because the baseline one subtracts from the other is
        // comparable only when the two were measured in the same environment. The project directory
        // joins HOME in it: a guard scoping its reading by the session's own checkout is answering a
        // question about whichever tree the suite was started from, and a session exporting one would
        // move the probe's verdict without touching a hook.
        private static (int Exit, string Output, string Error) Probe(string hook, string payload) =>
            Answer(hook, payload, home: ScratchDirectory, projectDirectory: ScratchDirectory);

        private static string Wrote((int Exit, string Output, string Error) answer) =>
            answer.Output.Trim() + "\n" + answer.Error.Trim();

        private static readonly Dictionary<string, string> LoadingOutput =
            new(StringComparer.Ordinal);

        /// <summary>What a hook writes for an event naming no tool, measured once per hook.</summary>
        private static string Loading(string hook)
        {
            if (!LoadingOutput.TryGetValue(hook, out var written))
            {
                // The payload has to parse. One that does not measures what a hook writes when it
                // cannot read the event, and a hook letting that error raise then has a traceback
                // subtracted from the silence it keeps for every payload it can read — so each of
                // those scores as an answer, including under the name nothing routes, which the check
                // above reads as a gate not reading the tool name at all.
                written = Wrote(Probe(hook, "{}"));
                LoadingOutput[hook] = written;
            }

            return written;
        }

        private const string RefuseDirectory = HookDirectory + "/refuse";

        private const string ProjectToken = "%PROJECT%";
        private const string ScratchToken = "%SCRATCH%";

        private static string ScratchDirectory;

        [OneTimeSetUp]
        public void MakeScratchDirectory()
        {
            ScratchDirectory = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "velvet-hook-gate-" + Guid.NewGuid().ToString("N"))).FullName;
            WriteReleasedChangelog(ScratchDirectory);
            File.WriteAllText(Path.Combine(ScratchDirectory, "DeclaringTests.cs"),
                              DeclaringFixture + SettledDeclaration);
            WriteWatcherState(ScratchDirectory);
            // A baseline is comparable only with answers measured under the same HOME, and the
            // directory HOME is pointed at is replaced here.
            LoadingOutput.Clear();
        }

        [OneTimeTearDown]
        public void RemoveScratchDirectory() => Directory.Delete(ScratchDirectory, true);

        /// <summary>The watcher's files under the HOME the probes run against: alive, one sitting.</summary>
        private static void WriteWatcherState(string home)
        {
            // Two guards read these, and the heartbeat alone leaves one of them silent: the poller
            // guard answers only while the heartbeat says something is watching, and the
            // sitting-pull-request guard, once it does, stops refusing over the watcher and needs a
            // pull request past its grace period instead. That is what the ready record is for —
            // measured, without it that guard answers none of the payloads below.
            //
            // The names are written rather than derived, and what pins them is the poller guard
            // alone: renamed in `scripts/pr/watcher_state.py`, it reads a heartbeat that is not
            // there and answers none of the payloads, so the gate check fails. Measured at the same
            // rename, the sitting-pull-request guard keeps answering — over nothing watching rather
            // than over a pull request — so it holds nothing here.
            var seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            File.WriteAllText(Path.Combine(home, ".velvet-pr-watch.heartbeat"), $"{seconds} {pid}\n");
            File.WriteAllText(Path.Combine(home, ".velvet-pr-ready"), $"1 {seconds - 3600}\n");
        }

        private static string Resolved(string payload) =>
            payload
                .Replace(ProjectToken, Path.GetFullPath(".").Replace("\\", "\\\\"))
                .Replace(ScratchToken, ScratchDirectory.Replace("\\", "\\\\"));

        private const string ClosedVersionGuard = RefuseDirectory + "/changelog_into_closed_version.py";

        // A released section, and the edits that reach each of the closed-version guard's readings.
        private const string ReleasedHeading = "## [1.0.0] - 2026-01-01";
        private const string ReleasedSection = ReleasedHeading + "\n\n### Fixed\n\n- As shipped.\n";
        private const string ReleasedChangelog =
            "# Changelog\n\n## [Unreleased]\n\n### Fixed\n\n- Not yet released.\n\n" + ReleasedSection;

        private const string ShippedEntry = "- As shipped.\n";
        private const string ShippedEntryPlusOne = "- As shipped.\n\n- Smuggled in.\n";
        private const string ShippedEntryReworded = "- As shipped, reworded.\n";

        // The released section's subsection, and the same one with a smuggled entry indented above its
        // first column-0 bullet. `release_notes.py` emits that entry with the rest of the block.
        private const string ShippedSubsection = "### Fixed\n\n" + ShippedEntry;
        private const string ShippedSubsectionUnderANestedEntry =
            "### Fixed\n\n  - Smuggled in.\n\n" + ShippedEntry;

        // An entry below a version heading written in a form `release_notes.py`'s heading pattern does
        // not match, which leaves that entry inside the released section that module publishes. The
        // guard reads with the same pattern, so it has to see the entry in the same place.
        private const string ShippedEntryAboveAHeadingSplitAcrossLines =
            ShippedEntry + "\n##\n[9.9.9]\n\n- Smuggled in.\n";

        // A second heading for the released version, carrying nothing. It goes above the real one
        // because the first heading matching a version is the half a note is rebuilt from — the
        // guard's own docstring owns why that makes it the whole note. Carrying nothing is what
        // makes this the case only the duplicate-heading reading answers: a fabricated bullet is a
        // line the section did not carry, which the published-lines reading refuses on its own.
        private const string ReleasedHeadingBelowAnEmptyOne =
            ReleasedHeading + "\n\n### Fixed\n\n" + ReleasedHeading;

        // GREEN_ON_BASE(characterization): the base already refuses an edit made in a worktree.
        // This change writes no line of `changelog_into_closed_version.py`, the guard the case poses, and the
        // `GatePayloads` row it adds to this fixture is read by none of the closed-version cases.
        [Test]
        public void Given_ACheckoutAndAWorktreeOfIt_When_TheClosedVersionGuardIsPosedAnEditInTheWorktree_Then_ItRefuses()
        {
            // Arrange — this repository does its branch work in worktrees outside the project
            // directory, and a worktree is where scoping by containment and scoping by the shared git
            // dir give different answers: the file is in the repository the session is for, and under
            // no path the project directory holds.
            var stem = Path.Combine(Path.GetTempPath(), "velvet-hook-" + Guid.NewGuid().ToString("N"));
            var checkout = stem + "-checkout";
            var worktree = stem + "-worktree";
            try
            {
                Directory.CreateDirectory(checkout);
                Git(checkout, "init", "-q", ".");
                Git(checkout, "-c", "user.email=hooks@velvet.test", "-c", "user.name=hooks",
                    "commit", "-q", "--allow-empty", "-m", "root");
                Git(checkout, "worktree", "add", "-q", worktree, "--detach");

                var changelog = WriteReleasedChangelog(worktree);

                // Act
                var answer = PoseEdit(checkout, changelog, ShippedEntry, ShippedEntryPlusOne);

                // Assert — the arrangement rides in the comparison because both halves of it are what
                // make the exit code mean anything: a worktree git did not link is an ordinary directory,
                // and one inside the checkout is reached by containment as well.
                Assert.That(
                    (Linked: File.Exists(Path.Combine(worktree, ".git")),
                     Outside: !worktree.StartsWith(checkout, StringComparison.Ordinal),
                     answer.Exit),
                    Is.EqualTo((true, true, 2)),
                    "an entry filed into a released section is refused in the project directory and "
                    + $"allowed everywhere the work happens:\n{answer.Error}");
            }
            finally
            {
                Remove(worktree);
                Remove(checkout);
            }
        }

        // GREEN_ON_BASE(characterization): the base already refuses a duplicated version heading.
        // This change writes no line of `changelog_into_closed_version.py`, the guard the case poses.
        [Test]
        public void Given_AnEditWritingASecondHeadingForAReleasedVersion_When_TheClosedVersionGuardReadsIt_Then_ItRefuses()
        {
            // Arrange
            var home = Scratch("-repository");
            try
            {
                Repository(home);
                var changelog = WriteReleasedChangelog(home);

                // Act
                var answer = PoseEdit(home, changelog, ReleasedHeading, ReleasedHeadingBelowAnEmptyOne);

                // Assert
                Assert.That(answer.Exit, Is.EqualTo(2),
                    "a second heading for a released version is the whole published note for that "
                    + $"version, and nothing under the real one has to move to make it so:\n{answer.Error}");
            }
            finally
            {
                Remove(home);
            }
        }

        // GREEN_ON_BASE(characterization): the base already refuses a substitution in a released section.
        // This change writes no line of `changelog_into_closed_version.py`, the guard the case poses.
        [Test]
        public void Given_AnEditSubstitutingTheOnlyEntryOfAReleasedSection_When_TheClosedVersionGuardReadsIt_Then_ItRefuses()
        {
            // Arrange — one entry out and one in, so the section's bullet count is what it was. The
            // substituted text is not what shipped, which is the whole difference between this edit
            // and a rewrap.
            var home = Scratch("-repository");
            try
            {
                Repository(home);
                var changelog = WriteReleasedChangelog(home);

                // Act
                var answer = PoseEdit(home, changelog, ShippedEntry, ShippedEntryReworded);

                // Assert
                Assert.That(answer.Exit, Is.EqualTo(2),
                    $"a published note now says something it did not say when it shipped:\n{answer.Error}");
            }
            finally
            {
                Remove(home);
            }
        }

        // GREEN_ON_BASE(characterization): the base already refuses an entry indented above a section's first bullet.
        // This change writes no line of `changelog_into_closed_version.py`, the guard the case poses.
        [Test]
        public void Given_AnEntryNestedAboveAReleasedSectionsFirstColumnZeroBullet_When_TheClosedVersionGuardReadsIt_Then_ItRefuses()
        {
            // Arrange — the indent is the whole case. A reading that counts a section's top-level list
            // items covers none of the text above the first of them, so an entry placed there is in the
            // published note and outside the comparison.
            var home = Scratch("-repository");
            try
            {
                Repository(home);
                var changelog = WriteReleasedChangelog(home);

                // Act
                var answer = PoseEdit(home, changelog, ShippedSubsection, ShippedSubsectionUnderANestedEntry);

                // Assert
                Assert.That(answer.Exit, Is.EqualTo(2),
                    "an indented entry is published from a released section like any other, and this "
                    + $"one arrived after the release:\n{answer.Error}");
            }
            finally
            {
                Remove(home);
            }
        }

        // GREEN_ON_BASE(characterization): the base already refuses an entry under a heading the notes do not read.
        // This change writes no line of `changelog_into_closed_version.py`, the guard the case poses.
        [Test]
        public void Given_AnEntryBelowAHeadingTheReleaseNotesDoNotRead_When_TheClosedVersionGuardReadsIt_Then_ItRefuses()
        {
            // Arrange — a heading form that ends the released section for a second grammar and not for
            // the one that publishes, which is how text ends up read by nobody and published anyway.
            // The guard has no grammar of its own for such a pair to disagree with.
            var home = Scratch("-repository");
            try
            {
                Repository(home);
                var changelog = WriteReleasedChangelog(home);

                // Act
                var answer = PoseEdit(home, changelog, ShippedEntry, ShippedEntryAboveAHeadingSplitAcrossLines);

                // Assert
                Assert.That(answer.Exit, Is.EqualTo(2),
                    "a version heading nothing publishes takes an entry out of the released section it "
                    + $"is published in:\n{answer.Error}");
            }
            finally
            {
                Remove(home);
            }
        }

        // GREEN_ON_BASE(characterization): the base already refuses where the project directory cannot be placed.
        // This change writes no line of `changelog_into_closed_version.py`, the guard the case poses.
        [Test]
        public void Given_AProjectDirectoryGitCannotPlace_When_TheClosedVersionGuardIsPosedAGrowthEdit_Then_ItRefuses()
        {
            // Arrange — a scoping question with no answer, where standing down is indistinguishable
            // from having looked and found nothing. Whether git placed the directory rides in the
            // comparison rather than gating it: a temporary directory that turned out to sit inside
            // some repository reaches the same exit code by the ordinary scoping path, so without
            // that term the case would pass while pinning nothing.
            var home = Scratch("-no-repository");
            try
            {
                Directory.CreateDirectory(home);
                var changelog = WriteReleasedChangelog(home);

                // Act
                var answer = PoseEdit(home, changelog, ShippedEntry, ShippedEntryPlusOne);

                // Assert
                Assert.That(
                    (Placed: Git(home, "rev-parse", "--git-common-dir") == 0, answer.Exit),
                    Is.EqualTo((false, 2)),
                    $"an unreadable project directory drops a real refusal:\n{answer.Error}");
            }
            finally
            {
                Remove(home);
            }
        }

        // GREEN_ON_BASE(characterization): the base already stands down for a changelog in no repository of ours.
        // This change writes no line of `changelog_into_closed_version.py`, the guard the case poses.
        [Test]
        public void Given_AChangelogInNoRepository_When_TheClosedVersionGuardIsPosedAGrowthEditFromAPlacedProject_Then_ItStandsDown()
        {
            // Arrange — git placing the project dir and not the target is the ordinary reading of a
            // file outside any repository, not a failure to read one, and this is what says the two
            // halves of the scoping are deliberately asymmetric. Making the target half fail closed
            // too would leave `in_scope` with no way to answer no except git naming another
            // repository, and every CHANGELOG.md outside one would be policed by this guard.
            var project = Scratch("-project");
            var home = Scratch("-no-repository");
            try
            {
                Repository(project);
                Directory.CreateDirectory(home);
                var changelog = WriteReleasedChangelog(home);

                // Act
                var answer = PoseEdit(project, changelog, ShippedEntry, ShippedEntryPlusOne);

                // Assert
                Assert.That(answer.Exit, Is.EqualTo(0),
                    $"another tree's CHANGELOG is not this guard's to refuse:\n{answer.Error}");
            }
            finally
            {
                Remove(home);
                Remove(project);
            }
        }

        // GREEN_ON_BASE(characterization): the base already names the deletion rather than a rename.
        // This change writes no line of `changelog_into_closed_version.py`, the guard the case poses.
        [Test]
        public void Given_AnEditDeletingAReleasedSectionOutright_When_TheClosedVersionGuardRefusesIt_Then_ItDoesNotNameARename()
        {
            // Arrange — the refusal is the only thing a reader acts on, so a mechanism it names that
            // the edit did not perform sends them looking for a change they never wrote. Three edits
            // reach this refusal, and deleting the section is the one neither half of a
            // rename-or-undate wording describes.
            var home = Scratch("-repository");
            try
            {
                Repository(home);
                var changelog = WriteReleasedChangelog(home);

                // Act
                var answer = PoseEdit(home, changelog, ReleasedSection, string.Empty);

                // Assert
                Assert.That(
                    (answer.Exit, Renames: answer.Error.Contains("renaming", StringComparison.Ordinal)),
                    Is.EqualTo((2, false)),
                    $"the refusal describes an edit that was not made:\n{answer.Error}");
            }
            finally
            {
                Remove(home);
            }
        }

        private static string Scratch(string role) =>
            Path.Combine(Path.GetTempPath(), "velvet-hook-" + Guid.NewGuid().ToString("N") + role);

        private static void Repository(string path)
        {
            Directory.CreateDirectory(path);
            Git(path, "init", "-q", ".");
        }

        private static string WriteReleasedChangelog(string directory)
        {
            var changelog = Path.Combine(directory, "CHANGELOG.md");
            File.WriteAllText(changelog, ReleasedChangelog);
            return changelog;
        }

        private static (int Exit, string Output, string Error) PoseEdit(
            string projectDirectory, string changelog, string oldString, string newString)
        {
            var payload = "{\"tool_name\":\"Edit\",\"cwd\":" + Quoted(Path.GetDirectoryName(changelog))
                + ",\"tool_input\":{\"file_path\":" + Quoted(changelog)
                + ",\"old_string\":" + Quoted(oldString)
                + ",\"new_string\":" + Quoted(newString) + "}}";
            return Answer(Path.GetFullPath(ClosedVersionGuard), payload,
                          projectDirectory: projectDirectory);
        }

        /// <summary>Runs git in a directory and returns its exit code, or -1 if it never answered.</summary>
        private static int Git(string cwd, params string[] arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(start);
            if (process == null)
            {
                return -1;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(60000))
            {
                process.Kill();
                return -1;
            }

            return process.ExitCode;
        }

        private static void Remove(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string Quoted(string value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

        /// <summary>Runs one hook against a payload and returns its exit code with whatever it wrote.</summary>
        private static (int Exit, string Output, string Error) Answer(
            string hook, string payload, string home = null, string projectDirectory = null)
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
            if (home != null)
            {
                // edit_while_a_ready_pr_sits.py reads the pull-request watcher's files out of the home
                // directory, so a caller wanting an answer that does not depend on what the developer's
                // watcher last wrote supplies one of its own.
                start.Environment["HOME"] = home;
            }

            if (projectDirectory != null)
            {
                start.Environment["CLAUDE_PROJECT_DIR"] = projectDirectory;
            }

            using var process = System.Diagnostics.Process.Start(start);
            if (process == null)
            {
                return (-1, string.Empty, "python3 did not start");
            }

            process.StandardInput.Write(Resolved(payload));
            process.StandardInput.Close();
            // Drained together: reading one to EOF first deadlocks against a guard that fills the
            // other's pipe buffer, and the wait below never runs to time that out.
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000))
            {
                process.Kill();
                return (-1, string.Empty, "timed out");
            }

            return (process.ExitCode, output.Result, error.Result);
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
