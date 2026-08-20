using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Machine-checks every markdown file the repository walk reaches against the actual runtime API
    /// surface, so a doc referencing a renamed/removed <c>V.*</c> factory or <c>Hooks.*</c> hook,
    /// a path or a type that no longer exists, or an index that has drifted from the files on disk, fails a
    /// test instead of shipping silently wrong. The failure modes below have actually shipped: a guide
    /// referencing a never-implemented factory, a hook table drifting from the real hook surface, an index
    /// missing real guide files, and a type name written for a file that holds differently-named types.
    /// One more is pinned as a shape rather than as a shipped instance: a markdown span naming one of the
    /// repository's scripts and a symbol that script does not define.
    /// </summary>
    [TestFixture]
    internal sealed class DocumentationDriftTests
    {
        // "V.X" appears only in react-migration.md's React-syntax comparison tables (and its mirrored rows in
        // both README.md files) as a meta-syntactic placeholder standing in for an arbitrary user component —
        // the same role "<X/>" plays in the JSX column of the same row — not a reference to a real V.* factory.
        private static readonly HashSet<string> VReferenceAllowlist = new() { "X" };

        // Names that resolve nowhere in this repo's code for a reason. Seven groups, each a different one:
        // meta-syntactic placeholders standing in for something the reader supplies — Foo, SomeFixture,
        // MyRender, MyStore, ResolveDirection, Save, and ForTest, a suffix written as a shape; API belonging
        // to the upstream libraries Velvet mirrors, which exists there and deliberately not here; names from
        // Unity, NUnit, the BCL or the host OS — types, enum values, event names, asset labels, file and
        // directory names — that the docs mention but no source file in this repo uses as code, which for
        // UpdateForRepaint, Alloc and UE means the repo holds the name only inside a string: one handed to
        // reflection, one to the profiler, one a test's case data; names an external toolchain owns, which
        // no source file here spells and which the contributor README quotes when it says how to invoke it —
        // DOTNET_ROOT, StrykerOutput, MSB4006, ProjectReference. What each one does is the toolchain's to
        // state and has been got wrong here more than once; the reason for the entry is only that the name
        // is not code in this repository. And the analyzer identifiers, which C# holds only as string
        // literals and the corpus therefore strips.
        //
        // That last group is checked, just not here: DocumentationDiagnosticTableTests over in the
        // Generators~ suite reads the same README and compares its VEL and USS spellings, and the diagnostic
        // categories it names, against the real descriptors and against the derivation's real code range.
        // One entry sits outside even that: "VEL" is the ID space written as a shape (VEL###) rather than an
        // ID, which that guard's VEL\d{3} pattern is right not to match.
        //
        // The sixth group is code this repository owns in a format the corpus cannot resolve it from.
        // VELVET_STORY_CAPTURE_DIR is an environment variable, which C# can hold only as a string literal,
        // and the corpus strips a string for the reason StripProse gives.
        //
        // The seventh is the CHANGELOG's own headings and entry labels — Unreleased, Highlights, Added,
        // Changed, Breaking — and the date placeholder in its version heading. A document naming one is
        // telling an author what to type, not referring to code. Highlights is the one the release-note
        // builder parses, and scripts/release/test_release_notes.py builds a note for every version in the
        // shipped CHANGELOG, so it fails when that heading stops matching.
        private static readonly HashSet<string> IdentifierAllowlist = new()
        {
            "Foo", "SomeFixture", "MyRender", "MyStore", "Ndeg", "Npx", "ResolveDirection", "Inter", "CS",
            "AnimatedList", "PointerSensor", "KeyboardSensor", "MeasuringConfiguration", "Collision",
            "MultiColumnListView", "PopupWindow", "TreeView", "TabView", "ToggleButtonGroup", "Raycast",
            "GetAllocatedBytesForCurrentThread", "FocusController", "ScaleWithScreenSize", "RoslynAnalyzer",
            "UnityUIEFilter", "FocusIn", "KeyDown", "PointerDown", "Move", "Leave", "Up", "Wheel", "Enter",
            "RoslynAdditionalFileImporter", "DOTNET_ROOT", "StrykerOutput", "MSB4006", "USS001", "USS011",
            "VEL", "VEL500", "VEL501", "VEL502", "ProjectReference", "VEL503",
            "Save", "ForTest",
            "NullReferenceException", "BringToFront", "SendToBack", "SetCursor", "AllocatingGCMemory",
            "UpdateForRepaint", "Alloc", "StandaloneOSX", "MacOS", "InitTestScene", "Unity_lic", "UE",
            "SIGTERM",
            "VELVET_STORY_CAPTURE_DIR",
            "Unreleased", "Highlights", "Added", "Changed", "Breaking", "YYYY", "MM", "DD"
        };

        // Paths the tree is right not to hold: each is written at run time inside a git-ignored
        // directory — a capture harness's output, and a mutation campaign's receipt store.
        private static readonly HashSet<string> PathAllowlist =
            new() { "Logs/story-captures/", "Logs/mutation_check/" };

        private static readonly string[] SourceExtensions = { ".cs", ".uss", ".yml", ".json", ".asmdef", ".py" };

        // Non-greedy, so the match closes on the first terminator rather than the last: a greedy one takes
        // every declaration between a file's first and last comment with it.
        private static readonly Regex UssCommentPattern =
            new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        // A hash run at a line start or after whitespace. That is wider than YAML's own comment rule — a
        // hash inside a quoted scalar or a `run: |` block matches too — and the direction is why it is
        // acceptable: every consumer of the corpus reports words that are ABSENT from it, so over-stripping
        // can only add a report, never hide a name.
        private static readonly Regex YamlCommentPattern =
            new(@"(?<=^|\s)#[^\n]*", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex VReferencePattern = new(@"\bV\.([A-Z][A-Za-z0-9_]*)", RegexOptions.Compiled);
        private static readonly Regex HookReferencePattern = new(@"\bUse[A-Z]\w*", RegexOptions.Compiled);
        private static readonly Regex DocLinkPattern = new(@"\]\(([A-Za-z0-9_.-]+\.md)\)", RegexOptions.Compiled);

        // A backticked span is treated as a repo path only when it ends in one of the extensions this repo
        // actually keeps or in a directory slash. Everything the docs write with a slash for other reasons —
        // a Tailwind opacity or fraction (bg-red-500/50, w-1/2), an npm package (@dnd-kit/sortable), a Unity
        // shader name (Velvet/FilterBrightness) — carries neither, and so never reaches the filesystem check.
        // A leading dot must be followed by a segment and a slash, which admits .github/workflows/test.yml
        // while excluding a bare extension written as prose (`.uss`) and the "..." elision in a path sketch.
        // A ./ or ../ run ahead of that is admitted because a document living inside the tree it describes
        // names its siblings that way; three dots still fail, since the run allows at most two.
        private static readonly Regex PathReferencePattern = new(
            @"^(\.{1,2}/)*(\.[A-Za-z0-9_-]+/)?[A-Za-z0-9_~@][A-Za-z0-9_./~*-]*(\.(cs|uss|md|json|yml|py|sh|txt|asmdef|dll|asset|tss)|/)$",
            RegexOptions.Compiled);

        private static readonly Regex RelativePrefixPattern = new(@"^(\.{1,2}/)+", RegexOptions.Compiled);

        // A path on the machine reading the document rather than in the repository, in each of the three
        // spellings the guides use: rooted, home-relative, and a Windows drive. Each needs settling before
        // the checks below, and for different reasons: a home-relative path matches the path pattern and
        // would then be looked for in the tree, while a Windows one carries separators that pattern does
        // not admit and falls through to the identifier check, which asks whether ProgramData is a word.
        private static readonly Regex MachinePathPattern =
            new(@"^(/|~/|[A-Za-z]:\\)", RegexOptions.Compiled);

        private static readonly Regex ElisionPattern = new(@"\.{3}|…", RegexOptions.Compiled);

        // Every PascalCase word INSIDE a span, not the span as a whole: the test-mechanism names are written
        // with call parens (SimulateClick()), the memoization knobs as attributes ([Component(Compiler = false)])
        // and the scheduler reached through a lowercase chain — a whole-span match resolves none of them, which
        // leaves the part of CLAUDE.md that exists nowhere else in the repo the least checked part of it.
        private static readonly Regex IdentifierTokenPattern =
            new(@"(?<![<A-Za-z0-9_])[A-Z][A-Za-z0-9_]*", RegexOptions.Compiled);

        // A JSX element name is React's, not Velvet's: react-migration.md's comparison tables spell the React
        // column in it, and both READMEs mirror those rows.
        private static readonly Regex JsxElementPattern =
            new(@"<\s*/?\s*[A-Z][A-Za-z0-9_]*", RegexOptions.Compiled);

        // Fenced samples are removed before spans are read: their triple backticks otherwise pair with the
        // inline ones and swallow the prose between them, which both hides real references and feeds English
        // words to the identifier check. The samples themselves stay unchecked — they are the densest
        // concentration of type names in the guides, and reaching them wants a parser, not a regex.
        private static readonly Regex FencedBlockPattern =
            new(@"^```.*?^```", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);

        // An inline span may wrap a line but not a blank one: markdown allows the wrap, and a span that ran
        // past a paragraph break would be a mis-paired backtick rather than a reference.
        private static readonly Regex BacktickSpanPattern =
            new(@"`((?:[^`\n]|\n(?!\s*\n))*)`", RegexOptions.Compiled);

        // A whole span reading as one attribute of one module.
        private static readonly Regex DottedSymbolPattern =
            new(@"^([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)(\(\))?$", RegexOptions.Compiled);

        // Comments and string literals are stripped from C# before it is tokenised. A rename tool rewrites
        // declarations and call sites; it does not rewrite the prose around them, so an old name lingering in
        // a comment or a test-case label would resolve a documentation reference that names something no
        // longer there. This fixture's own allowlist is the sharpest instance — every entry in it is a string
        // literal, and unstripped it would resolve itself.
        //
        // One alternation, not two passes, because the two forms nest: `Route("Files/*")` opens a block
        // comment for a comment-first pass, which then runs to the next `*/` anywhere in the file and deletes
        // the real declarations in between, while `AppendLine("// <auto-generated/>")` is the mirror case for
        // a string-first pass. Scanning left to right with strings ordered ahead of comments is what makes a
        // delimiter inside the other form inert. Char literals lead, so a lone quote cannot open a string.
        // The raw-string alternative leads because its delimiter starts with the one the ordinary string form
        // would match. Nothing in the package uses it — Unity's runtime tree is C# 9 — but `Generators~` pins
        // an SDK that allows it, and a raw string is the idiomatic modern spelling for the verbatim analyzer
        // fixtures there, so the alternation would otherwise leak fixture source into the corpus the day one
        // is rewritten. The opening run is captured and back-referenced rather than written as three quotes,
        // because C# closes a raw string with a run of the SAME length and allows any length from three up:
        // a fixture demonstrating a raw string needs four, and against a fixed three that spelling matches
        // three of the four and desyncs everything after it.
        // The #region alternative rides in this same alternation rather than in a pass of its own, for the
        // reason the forms above need one: a label consuming a line can carry a string's closing delimiter
        // with it, and a separate earlier pass would then let the string form run past it and swallow real
        // declarations. It names region and endregion rather than any directive so that a condition token a
        // guide may cite stays in the corpus; UNITY_EDITOR is one such, and #if is where it lives.
        private const string CommentOrStringAlternation =
            "(\"{3,})(?:(?!\\1)[\\s\\S])*?\\1(?!\")|'(?:\\\\.|[^'\\\\\n])*'|@\"(?:[^\"]|\"\")*\""
            + "|\"(?:\\\\.|[^\"\\\\\n])*\"|/\\*.*?\\*/|//[^\n]*";

        private const string RegionLabelAlternation = "^[^\\S\n]*#[^\\S\n]*(?:region|endregion)[^\n]*$";

        private static readonly Regex CSharpCommentOrStringPattern = new(
            CommentOrStringAlternation + "|" + RegionLabelAlternation,
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);

        // Python, ordered the same way and for the same reason: a delimiter inside the other form has to be
        // inert. Triple quotes lead, since a docstring opens with the run an ordinary string would match and
        // holds the longest prose in these files; a prefix run covers r, b, f and their combinations, so an
        // f-string'"'"'s message does not survive as identifiers. Strings go with the comments here rather than
        // staying like YAML'"'"'s, because a hook'"'"'s refusal text is a paragraph of ordinary English — pouring it
        // in would let a document name almost anything and find it defined.
        private static readonly Regex PythonCommentOrStringPattern = new(
            "[rRbBfFuU]{0,2}(\"\"\"|''')(?:[\\s\\S])*?\\1"
            + "|[rRbBfFuU]{0,2}\"(?:\\\\.|[^\"\\\\\n])*\""
            + "|[rRbBfFuU]{0,2}'(?:\\\\.|[^'\\\\\n])*'"
            + "|#[^\n]*",
            RegexOptions.Compiled | RegexOptions.Singleline);

        [Test]
        public void Given_TheRepoSources_When_TheIdentifierCorpusIsBuilt_Then_EachFormatsCommentsAreTaken()
        {
            // Arrange — the words each stripped format contributes when left whole, which is the control,
            // and the words its comments hold. Both are re-derived here, so this reads what the corpus
            // builder did rather than a copy of how it did it.
            var word = new Regex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
            var formats = new[]
            {
                (Extension: ".uss", Comment: UssCommentPattern),
                (Extension: ".yml", Comment: YamlCommentPattern),
                // Python's strip takes strings as well, so the "came out of no comment" arm below reads
                // the same alternation the strip uses rather than a comment-only one.
                (Extension: ".py", Comment: PythonCommentOrStringPattern),
            };

            // Act — per format, so one arm going missing cannot hide behind the other.
            var unheld = new List<string>();
            foreach (var (extension, comment) in formats)
            {
                var whole = new HashSet<string>(StringComparer.Ordinal);
                var inComments = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in DocumentationCorpus.RepoEntries(includeClaude: false).Where(e =>
                             e.EndsWith(extension, StringComparison.Ordinal) && File.Exists(e)))
                {
                    var text = File.ReadAllText(entry);
                    foreach (Match token in word.Matches(text)) whole.Add(token.Value);
                    foreach (Match span in comment.Matches(text))
                    {
                        foreach (Match token in word.Matches(span.Value)) inComments.Add(token.Value);
                    }
                }
                var removed = whole.Where(w => !SourceIdentifiers(includeClaude: false).Contains(w)).ToList();
                if (removed.Count == 0)
                {
                    unheld.Add($"{extension}: nothing removed");
                }
                unheld.AddRange(removed
                    .Where(w => !inComments.Contains(w))
                    .Select(w => $"{extension}: {w} came out of no comment"));
            }

            // The subset term above reads the same pattern the strip does, so a widened pattern satisfies
            // it by widening both sides. What a widened one cannot do is leave the sheets' own selectors in
            // the corpus: it takes everything between a file's first and last comment with it.
            var selector = new Regex(@"^[^\S\n]*\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline);
            foreach (var entry in DocumentationCorpus.RepoEntries(includeClaude: false).Where(e =>
                         e.EndsWith(".uss", StringComparison.Ordinal) && File.Exists(e)))
            {
                unheld.AddRange(selector.Matches(File.ReadAllText(entry))
                    .Select(match => match.Groups[1].Value)
                    .Where(name => !SourceIdentifiers(includeClaude: false).Contains(name))
                    .Select(name => $".uss: the selector .{name} left the corpus"));
            }

            // Assert — nothing removed means that format's strip is not running; a word removed that no
            // comment holds, or a declared selector missing, means it is taking more than comments.
            Assert.That(string.Join(", ", unheld.Distinct()), Is.Empty);
        }

        [Test]
        public void Given_TheRepoSources_When_TheIdentifierCorpusIsBuilt_Then_TheRegionStripTakesOnlyLabelWords()
        {
            // Arrange — the same corpus built without the region alternative, which is the control: the
            // difference between the two sets is exactly what the strip removes, and nothing else here can
            // produce it. Comparing against the shipped set rather than re-deriving it is what makes this
            // read the corpus builder's behaviour instead of a copy of its regex.
            var word = new Regex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
            var keepingRegions = new Regex(
                CommentOrStringAlternation, RegexOptions.Singleline | RegexOptions.Multiline);
            var labelPattern = new Regex(RegionLabelAlternation, RegexOptions.Multiline);
            var withRegions = new HashSet<string>(StringComparer.Ordinal);
            var labelWords = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in DocumentationCorpus.RepoEntries(includeClaude: false).Where(
                         e => e.EndsWith(".cs", StringComparison.Ordinal) && File.Exists(e)))
            {
                var text = File.ReadAllText(entry);
                foreach (Match token in word.Matches(keepingRegions.Replace(text, " ")))
                {
                    withRegions.Add(token.Value);
                }
                foreach (Match line in labelPattern.Matches(text))
                {
                    foreach (Match token in word.Matches(line.Value)) labelWords.Add(token.Value);
                }
            }

            // Act
            var removed = withRegions.Where(w => !SourceIdentifiers(includeClaude: false).Contains(w)).ToList();
            var notFromALabel = removed
                .Where(w => !labelWords.Contains(w))
                .OrderBy(w => w, StringComparer.Ordinal);

            // Assert — a strip that is not running removes nothing, so an empty first term is the shape
            // that catches it. The second catches a strip widened by any route other than the spelling both
            // sides read; widening that spelling to every directive is caught by the case below.
            Assert.That(
                (removed.Count > 0, string.Join(", ", notFromALabel)),
                Is.EqualTo((true, string.Empty)));
        }

        [Test]
        public void Given_TheRepoSources_When_TheIdentifierCorpusIsBuilt_Then_ItKeepsEveryDirectiveCondition()
        {
            // Arrange — the conditions are read from the raw text while the corpus is built from the
            // stripped text, so a strip that widens from the two region labels to every directive takes
            // them out of one side and not the other. Deriving them rather than listing them is what keeps
            // this from being a second copy of the strip: nothing here has to be updated when a condition
            // is added or dropped.
            var directive = new Regex(@"^[^\S\n]*#[^\S\n]*(?:if|elif)\b([^\n]*)$", RegexOptions.Multiline);
            var word = new Regex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
            var conditions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in DocumentationCorpus.RepoEntries(includeClaude: false).Where(
                         e => e.EndsWith(".cs", StringComparison.Ordinal) && File.Exists(e)))
            {
                foreach (Match line in directive.Matches(File.ReadAllText(entry)))
                {
                    foreach (Match token in word.Matches(line.Groups[1].Value))
                    {
                        conditions.Add(token.Value);
                    }
                }
            }

            // Act
            var absent = conditions
                .Where(name => !SourceIdentifiers(includeClaude: false).Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal);

            // Assert — the count of conditions found rides along, because a walk that read no directive at
            // all would satisfy an emptiness check on its own.
            Assert.That(
                (conditions.Count > 0, string.Join(", ", absent)),
                Is.EqualTo((true, string.Empty)));
        }

        [Test]
        public void Given_TheMarkdownTheWalkFinds_When_ComparedAgainstTheScannedSet_Then_EveryFileIsScanned()
        {
            // Arrange — the walk that reaches .claude, which is the one the scans above resolve against.
            var walked = DocumentationCorpus.RepoEntries(includeClaude: true)
                .Where(entry => entry.EndsWith(".md", StringComparison.Ordinal) && File.Exists(entry))
                .ToList();

            // Act
            var scanned = new HashSet<string>(DocumentationCorpus.Files(), StringComparer.Ordinal);
            var unscanned = walked
                .Where(entry => !scanned.Contains(entry))
                .OrderBy(entry => entry, StringComparer.Ordinal);

            // Assert — the count rides along, because a walk that found no markdown leaves none unscanned.
            // What this refuses is the scanned set going back to a hand-written list.
            Assert.That(
                (walked.Count > 0, string.Join(", ", unscanned)),
                Is.EqualTo((true, string.Empty)));
        }

        [Test]
        public void Given_DocumentationMarkdown_When_ScannedForVDotReferences_Then_EveryReferenceExistsOnV()
        {
            // Arrange — the real V surface: every public static factory (including ones woven into V by a
            // partial file like V.Mount.cs or by the Memoized<T1..T8> source generator) plus any public
            // nested type V might declare.
            var knownVMembers = new HashSet<string>(
                typeof(V).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name)
                    .Concat(typeof(V).GetNestedTypes(BindingFlags.Public).Select(t => t.Name)));

            // Act
            var unresolved = FindUnresolvedReferences(
                VReferencePattern, text => text, name => knownVMembers.Contains(name) || VReferenceAllowlist.Contains(name),
                prefix: "V.");

            // Assert
            Assert.That(unresolved, Is.Empty,
                "Documentation references V.* members that do not exist on typeof(V):\n" + string.Join("\n", unresolved));
        }

        [Test]
        public void Given_DocumentationMarkdown_When_ScannedForBacktickedHookReferences_Then_EveryReferenceExistsOnHooks()
        {
            // Arrange — restricting extraction to backtick spans excludes react-migration.md's prose describing
            // React's own lowercase `useXxx` hooks (which never matches Use[A-Z] anyway) while also skipping
            // incidental "Use" occurrences in running text that are not meant as an API reference.
            var knownHooks = new HashSet<string>(
                typeof(Hooks).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name));

            // Act
            var unresolved = FindUnresolvedReferences(
                HookReferencePattern,
                // Fenced samples are removed here too: an inline span may now wrap a line, which lets a
                // fence's third backtick pair with the closing fence's first and turn a whole sample body
                // into one span. The V.* check above keeps scanning them, because it reads the whole text
                // rather than spans and so never sees a fence as a delimiter.
                text => string.Join("\n", BacktickSpanPattern
                    .Matches(FencedBlockPattern.Replace(text, "\n"))
                    .Select(m => m.Groups[1].Value)),
                knownHooks.Contains,
                prefix: string.Empty);

            // Assert
            Assert.That(unresolved, Is.Empty,
                "Documentation references Hooks.* members that do not exist on typeof(Hooks):\n" + string.Join("\n", unresolved));
        }

        [Test]
        public void Given_DocumentationReadmeIndex_When_ComparedAgainstDirectoryContents_Then_LinksAndFilesMatchExactly()
        {
            // Arrange
            var readmePath = Path.Combine(DocumentationCorpus.DocumentationDirectory, "README.md");
            var linkedFiles = new HashSet<string>(
                DocLinkPattern.Matches(File.ReadAllText(readmePath)).Select(m => m.Groups[1].Value));
            var actualFiles = new HashSet<string>(
                Directory.GetFiles(DocumentationCorpus.DocumentationDirectory, "*.md")
                    .Select(Path.GetFileName)
                    .Where(name => name != "README.md"));

            // Act
            var missingFromIndex = actualFiles.Except(linkedFiles).Select(f => $"missing from index: {f}");
            var deadIndexLinks = linkedFiles.Except(actualFiles).Select(f => $"dead index link (no such file): {f}");
            var diff = missingFromIndex.Concat(deadIndexLinks).ToList();

            // Assert
            Assert.That(diff, Is.Empty,
                "Documentation~/README.md's index is out of sync with the directory's actual .md files:\n" + string.Join("\n", diff));
        }

        [Test]
        public void Given_ProjectMarkdown_When_ScannedForBacktickedPaths_Then_EveryPathExistsInTheRepo()
        {
            // Arrange / Act
            var unresolved = ScanBacktickSpans((path, reference) =>
                PathReferencePattern.IsMatch(reference) && !PathAllowlist.Contains(reference)
                && !PathReferenceResolves(reference)
                    ? new[] { $"{path}: {reference}" }
                    : Array.Empty<string>());

            // Assert
            Assert.That(unresolved, Is.Empty,
                "Documentation names paths that do not exist:\n" + string.Join("\n", unresolved));
        }

        // GREEN_ON_BASE(refactor): the case is the base's own and green there. What this change does to it
        // is move its tokenisation into the derivation the allowlist guard below also reads, which must
        // leave what it reports alone.
        [Test]
        public void Given_ProjectMarkdown_When_ScannedForBacktickedIdentifiers_Then_EveryIdentifierAppearsInASource()
        {
            // Arrange / Act
            var unresolved = IdentifierTokenSpans()
                .Where(span => !SourceIdentifiers(includeClaude: true).Contains(span.Token)
                               && !IdentifierAllowlist.Contains(span.Token))
                .Select(span => $"{span.Path}: {span.Token} (in `{span.Reference}`)")
                .Distinct()
                .ToList();

            // Assert
            Assert.That(unresolved, Is.Empty,
                "Documentation names identifiers that appear in no source file:\n" + string.Join("\n", unresolved));
        }

        // GREEN_ON_BASE(characterization): the list this reads is declared above, in a file the base run
        // carries from the branch along with the case, so the base answers over the branch's own entries
        // whatever it holds. What stands in for the base run is the entry this change drops put back and
        // the case run, measured: it named ContinuousIntegrationBuild.
        [Test]
        public void Given_TheIdentifierAllowlist_When_EachEntryIsSoughtInTheScannedSpans_Then_EveryEntryIsWritten()
        {
            // Arrange
            var written = new HashSet<string>(
                IdentifierTokenSpans().Select(span => span.Token), StringComparer.Ordinal);

            // Act
            var dead = IdentifierAllowlist
                .Where(entry => !written.Contains(entry))
                .OrderBy(entry => entry, StringComparer.Ordinal);

            // Assert
            Assert.That(string.Join(", ", dead), Is.Empty,
                "Allowlist entries no scanned span writes suppress nothing, and a reader cannot tell them "
                + "apart from the load-bearing ones. Drop the entry, or land the prose that needs it in the "
                + "same change — there is no escape for one added ahead of its prose.");
        }

        // The check above and the guard over its suppression list read one derivation: a guard built from
        // its own walk of the spans stops answering for the check the day either of them moves.
        //
        // A file name is claimed by the path check first, which resolves it against the filesystem — the
        // stronger question. Without this, VNodePool.cs would also be read as two identifiers. An elision
        // leaves out the part that would say what is being named: a naming convention written as a shape, a
        // signature with its arguments dropped. The V.* and Hooks.* checks still resolve the head of a
        // dropped-argument call, so what this gives up is a span whose head is neither — and the alternative
        // is reporting the elision's own fragments.
        private static IEnumerable<(string Path, string Reference, string Token)> IdentifierTokenSpans() =>
            BacktickSpans().SelectMany(span =>
                PathReferencePattern.IsMatch(span.Reference) || ElisionPattern.IsMatch(span.Reference)
                    ? Enumerable.Empty<(string Path, string Reference, string Token)>()
                    : IdentifierTokenPattern.Matches(JsxElementPattern.Replace(span.Reference, " "))
                        .Select(token => (span.Path, span.Reference, Token: token.Value)));

        // GREEN_ON_BASE(characterization): the base's own markdown names no wrong symbol, so it is green there.
        // Not a behaviour the base has — the case is new — but a property of content the base already
        // holds on both sides of the comparison. What shows it can fail is a reference perturbed to a name
        // no script defines, measured, rather than the base run.
        [Test]
        public void Given_MarkdownNamingAScriptSymbol_When_TheSymbolIsSoughtInThatScript_Then_ItIsDefinedThere()
        {
            // Arrange / Act
            var unresolved = ScriptSymbolSpans()
                .Where(span => !span.Defined)
                .Select(span => $"{span.Path}: {span.Reference} — {span.Module}.py defines no {span.Symbol}")
                .ToList();

            // Assert
            Assert.That(unresolved, Is.Empty,
                "Documentation names symbols the script it names does not define:\n"
                + string.Join("\n", unresolved));
        }

        // What the check above runs on, and the reason it needs a floor: the population is prose. Every
        // span in it is a sentence somebody may reword, and an empty population satisfies "nothing
        // unresolved" exactly as a healthy one does — so the check would pass having asked nothing, with
        // the drift it exists for unguarded. Two rather than one, so that a population thinned to a
        // single sentence is read while there is still something left to read. A red here is the spans
        // going or the walk stopping short of them, and either wants reading before the number moves.
        private const int ScriptSymbolSpanFloor = 2;

        [Test]
        public void Given_TheScriptSymbolCheck_When_ItsLiveSpansAreCounted_Then_SomeMarkdownStillReachesIt()
        {
            // Arrange / Act
            var spans = ScriptSymbolSpans();

            // Assert
            Assert.That(spans.Count, Is.GreaterThanOrEqualTo(ScriptSymbolSpanFloor),
                "Too few documented module.symbol spans reach the check for it to answer for anything. "
                + "What still reaches it:\n"
                + string.Join("\n", spans.Select(span => $"{span.Path}: {span.Reference}")));
        }

        // Every span that reaches the resolution, with the answer beside it, so the check and its floor
        // read one derivation: a floor over a population derived separately stops answering for the check
        // the day either moves. A file name is the path check's, which resolves it against the filesystem;
        // without that, `mutation_check.py` reads here as a module and an extension.
        private static List<(string Path, string Reference, string Module, string Symbol, bool Defined)>
            ScriptSymbolSpans()
        {
            var spans = new List<(string Path, string Reference, string Module, string Symbol, bool Defined)>();
            foreach (var (path, reference) in BacktickSpans())
            {
                var dotted = DottedSymbolPattern.Match(reference);
                if (PathReferencePattern.IsMatch(reference) || !dotted.Success
                    || !ScriptSources.Value.TryGetValue(dotted.Groups[1].Value, out var source))
                {
                    continue;
                }
                var symbol = dotted.Groups[2].Value;
                spans.Add((path, reference, dotted.Groups[1].Value, symbol, DefinesSymbol(source, symbol)));
            }
            return spans.Distinct().ToList();
        }

        // GREEN_ON_BASE(characterization): what this asks about is a helper in this file, and the base
        // tree carries the file, so the case reads the branch's own arm there whatever the base holds.
        // The evidence that stands in for the base run is the import arm removed and the case run.
        //
        // An import binds into the module, so the check above has to resolve an imported name rather than
        // report it. Which name a statement binds differs by spelling, and a statement can write a name it
        // does not bind — `import a.b` writes b and binds a — so both directions are asked here.
        [Test]
        public void Given_TheImportSpellings_When_EachNameIsSoughtInTheModule_Then_OnlyABoundOneResolves()
        {
            // Arrange
            const string module = "import time\n"
                                  + "import importlib.util\n"
                                  + "import xml.etree.ElementTree as ET\n"
                                  + "from deferrals import DEFERRALS, deferred\n"
                                  + "from published_check import (\n    unpublished_reason,\n)\n";
            var source = StripProse("module.py", module);
            var bound = new[] { "time", "importlib", "ET", "DEFERRALS", "deferred", "unpublished_reason" };
            var mentioned = new[] { "util", "xml", "etree", "ElementTree", "deferrals", "published_check" };

            // Act
            var wrong = bound.Where(name => !DefinesSymbol(source, name))
                .Select(name => $"{name} is bound here and resolves to nothing")
                .Concat(mentioned.Where(name => DefinesSymbol(source, name))
                    .Select(name => $"{name} is named here but bound by nothing"))
                .ToList();

            // Assert
            Assert.That(wrong, Is.Empty, string.Join("\n", wrong));
        }

        // What the module itself exposes, which is what `module.symbol` claims: a def or a class at column
        // zero rather than at any indentation, since a method's name is the class's rather than the
        // module's. A binding counts alongside them, because a document naming a harness constant makes
        // the same claim as one naming a function, and the target may be a tuple — one hook here binds
        // three of its policy names in a single one, and an arm reading only `NAME =` reports a document
        // that names them correctly. An import counts too: it binds into the module the same as the other
        // two, so `module.imported` reaches a value.
        private static bool DefinesSymbol(string source, string symbol) =>
            Regex.IsMatch(source,
                $@"^(?:async[^\S\n]+)?(?:def|class)[^\S\n]+{Regex.Escape(symbol)}\b",
                RegexOptions.Multiline)
            || Regex.IsMatch(source,
                $@"^(?:[A-Za-z_]\w*[^\S\n]*,[^\S\n]*)*{Regex.Escape(symbol)}"
                + @"(?:[^\S\n]*,[^\S\n]*[A-Za-z_]\w*)*[^\S\n]*(?::[^=\n]+)?=",
                RegexOptions.Multiline)
            || ImportsSymbol(source, symbol);

        // Which name an import binds is a property of its spelling rather than of the text after
        // `import`, so the clause is split and each part read on its own: an alias binds only itself, a
        // dotted module binds its first segment, and everything else binds what is written. Reading the
        // clause whole would answer `util` for `import importlib.util` and `xml` for
        // `import xml.etree.ElementTree as ET`, and the walk's own scripts write both. Column zero for
        // the same reason the arms above use it — an import indented into a function binds nothing on
        // the module.
        private static readonly Regex ImportStatementPattern = new(
            @"^import[^\S\n]+(?<names>[^\n]+)"
            + @"|^from[^\S\n]+[.\w]+[^\S\n]+import[^\S\n]+(?<names>\([^)]*\)|[^\n]+)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex ImportAliasPattern =
            new(@"[^\S\n]+as[^\S\n]+([A-Za-z_]\w*)$", RegexOptions.Compiled);

        private static bool ImportsSymbol(string source, string symbol) =>
            ImportStatementPattern.Matches(source).Any(statement =>
                statement.Groups["names"].Value.Trim('(', ')').Split(',')
                    .Select(part => part.Trim())
                    .Where(part => part.Length > 0)
                    .Any(part =>
                    {
                        var alias = ImportAliasPattern.Match(part);
                        return alias.Success
                            ? alias.Groups[1].Value == symbol
                            : part.Split('.')[0] == symbol;
                    }));

        // Every Python source in the walk, keyed on its stem, with prose taken out the way StripProse takes
        // it: a def or a binding inside a string literal is not a definition, and several harness tests
        // hold a whole synthetic script that way. Stems are not unique across the tree, and a document
        // naming one means the name rather than a path, so the value joins every file that carries the stem
        // and a symbol in any of them answers for it.
        private static readonly Lazy<Dictionary<string, string>> ScriptSources = new(() =>
        {
            var texts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var entry in DocumentationCorpus.RepoEntries(includeClaude: true))
            {
                if (!entry.EndsWith(".py", StringComparison.Ordinal) || !File.Exists(entry))
                {
                    continue;
                }
                var stem = Path.GetFileNameWithoutExtension(entry);
                if (!texts.TryGetValue(stem, out var held))
                {
                    held = new List<string>();
                    texts[stem] = held;
                }
                held.Add(StripProse(entry, File.ReadAllText(entry)));
            }
            return texts.ToDictionary(pair => pair.Key, pair => string.Join("\n", pair.Value),
                                      StringComparer.Ordinal);
        });

        private static List<string> ScanBacktickSpans(Func<string, string, IEnumerable<string>> extract) =>
            BacktickSpans().SelectMany(span => extract(span.Path, span.Reference)).Distinct().ToList();

        // The walk itself, separate from reporting over it, so a caller that wants the spans rather than a
        // report reads the same one. A path on the reader's own machine is skipped outright: it names
        // nothing in the repo, so no check that reads this has anything to say about it.
        private static IEnumerable<(string Path, string Reference)> BacktickSpans()
        {
            foreach (var path in DocumentationCorpus.Files())
            {
                var prose = FencedBlockPattern.Replace(File.ReadAllText(path), "\n");
                foreach (Match span in BacktickSpanPattern.Matches(prose))
                {
                    var reference = string.Join(" ", span.Groups[1].Value.Split((char[])null!,
                        StringSplitOptions.RemoveEmptyEntries));
                    if (reference.Length == 0 || MachinePathPattern.IsMatch(reference))
                    {
                        continue;
                    }
                    yield return (path, reference);
                }
            }
        }

        // Every identifier-shaped word in the repo's own CODE. A name surviving nowhere in it was renamed or
        // deleted, which is the drift this checks for. It deliberately does not care WHERE the word occurs:
        // resolving this mix of runtime types, Unity types, generator symbols and CI variables against their
        // real declarations would need every one of those toolchains loaded into the test. What it does care
        // about is that the word is code — see the stripping patterns for why prose cannot be trusted here.
        // What is stripped per format is StripProse's to say.
        //
        // includeClaude decides whether the files under .claude are part of the answer. The markdown scans
        // below ask for them, because CONTRIBUTING.md names the hook events .claude/settings.json registers
        // and nothing else in the tree spells those as code; a caller resolving C# API against this set
        // wants the narrower reading and says so.
        internal static HashSet<string> SourceIdentifiers(bool includeClaude) =>
            (includeClaude ? ClaudeAwareIdentifiers : DocumentationIdentifiers).Value;

        private static readonly Lazy<HashSet<string>> DocumentationIdentifiers =
            new(() => BuildIdentifiers(includeClaude: false));

        private static readonly Lazy<HashSet<string>> ClaudeAwareIdentifiers =
            new(() => BuildIdentifiers(includeClaude: true));

        private static HashSet<string> BuildIdentifiers(bool includeClaude)
        {
            var words = new Regex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in DocumentationCorpus.RepoEntries(includeClaude))
            {
                if (!SourceExtensions.Any(extension => entry.EndsWith(extension, StringComparison.Ordinal))
                    || !File.Exists(entry))
                {
                    continue;
                }
                var text = StripProse(entry, File.ReadAllText(entry));
                foreach (Match match in words.Matches(text))
                {
                    identifiers.Add(match.Value);
                }
            }
            return identifiers;
        }

        // Comments are prose in every format that has them, so a name surviving only in one is a deleted
        // name as far as any caller is concerned. Strings are not: in C# a string is a label for code, while
        // in USS, YAML, JSON and an asmdef the string IS the content, and the CI variable names a document
        // cites live in exactly those. So C# loses both and the rest lose only their comments. Nothing is
        // taken from JSON or an asmdef, and no file of either carries a comment for the guard to be wrong
        // about.
        private static string StripProse(string entry, string text)
        {
            if (entry.EndsWith(".cs", StringComparison.Ordinal))
            {
                return CSharpCommentOrStringPattern.Replace(text, " ");
            }
            if (entry.EndsWith(".uss", StringComparison.Ordinal))
            {
                return UssCommentPattern.Replace(text, " ");
            }
            if (entry.EndsWith(".yml", StringComparison.Ordinal))
            {
                return YamlCommentPattern.Replace(text, " ");
            }
            if (entry.EndsWith(".py", StringComparison.Ordinal))
            {
                return PythonCommentOrStringPattern.Replace(text, " ");
            }
            return text;
        }

        // A wildcard leaf (Runtime/Styles/*.uss) resolves only when its directory actually holds a matching
        // file: the directory existing is not the claim the document made. A wildcard anywhere EARLIER in the
        // path is not resolved at all — treating it as a literal would report every such reference as missing.
        private static bool PathReferenceResolves(string reference)
        {
            // The ./ or ../ run is dropped rather than walked from the document's own directory: every other
            // reference in these files is already matched as a suffix from anywhere in the tree, so walking
            // would make one spelling of a path stricter than the other for no gain. What matters is that the
            // span reaches this check at all — unmatched by the pattern above it falls through to the
            // identifier check, which asks whether "Analyzers" is a word somewhere and never whether the file
            // named is there.
            var trimmed = RelativePrefixPattern.Replace(reference, string.Empty).TrimEnd('/');
            var separator = trimmed.LastIndexOf('/');
            var leaf = trimmed[(separator + 1)..];
            if (!leaf.Contains('*'))
            {
                return trimmed.Contains('*') || DocumentationCorpus.RepoEntries(includeClaude: true)
                    .Any(entry => IsSuffixPath(entry, trimmed));
            }
            var directory = separator < 0 ? string.Empty : trimmed[..separator];
            var extension = leaf.TrimStart('*');
            return DocumentationCorpus.RepoEntries(includeClaude: true).Any(entry =>
                entry.EndsWith(extension, StringComparison.Ordinal)
                && entry.LastIndexOf('/') >= 0
                && IsSuffixPath(entry[..entry.LastIndexOf('/')], directory));
        }

        private static bool IsSuffixPath(string entry, string suffix) =>
            entry == suffix || entry.EndsWith("/" + suffix, StringComparison.Ordinal);

        // Shared scan: for every target markdown file, project its text through `select` (identity for the
        // V.* scan, backtick-span extraction for the Hooks scan), extract every reference `pattern` matches,
        // and report ones `isKnown` rejects as "file: reference" so a failure message names both the
        // offending file and the exact unresolved identifier.
        private static List<string> FindUnresolvedReferences(
            Regex pattern, Func<string, string> select, Func<string, bool> isKnown, string prefix)
        {
            var unresolved = new List<string>();
            foreach (var path in DocumentationCorpus.Files())
            {
                var haystack = select(File.ReadAllText(path));
                foreach (Match match in pattern.Matches(haystack))
                {
                    var name = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                    if (isKnown(name))
                    {
                        continue;
                    }
                    unresolved.Add($"{path}: {prefix}{name}");
                }
            }
            return unresolved.Distinct().ToList();
        }

        [Test]
        public void Given_EveryTopLevelDirectoryHoldingMarkdown_When_TheWalkIsRead_Then_TheWalkReachesIt()
        {
            // Arrange — the walk is rooted, so a document under a root nobody added is scanned by nothing
            // and every drift guard reading this corpus passes over it in silence.
            var scanned = DocumentationCorpus.Files().ToList();

            // Act
            var unwalked = DocumentationCorpus.UnwalkedMarkdownRoots();

            // Assert — the scanned count rides along because a walk that collapsed to nothing would leave
            // this reporting no unwalked root either.
            Assert.That((scanned.Count > 20, string.Join(", ", unwalked)), Is.EqualTo((true, string.Empty)),
                "markdown under a root the walk does not reach is checked by nothing; add the root to the "
                + "walk, or to .gitignore if it is machine-local");
        }

        // GREEN_ON_BASE(characterization): the exclusion this pins is a value in DocumentationCorpus, which
        // is a test-assembly file, so the base run carries the branch's own list and answers with it. What
        // stands in for the base run is the entry removed and the case run, measured: it reported
        // scripts/.pytest_cache and the markdown under it as walked.
        [Test]
        public void Given_APytestCacheUnderAWalkedRoot_When_TheWalkRuns_Then_ItsMarkdownStaysOutOfTheCorpus()
        {
            // Arrange — both cached corpora are forced before the directory exists, so the walk every other
            // fixture reads cannot take it in. Walk is invoked directly below because that cache would
            // otherwise answer from before the directory was there.
            DocumentationCorpus.RepoEntries(includeClaude: true);
            DocumentationCorpus.RepoEntries(includeClaude: false);
            var cache = Path.GetFullPath(Path.Combine("scripts", ".pytest_cache"));
            var cacheExisted = Directory.Exists(cache);
            var readme = Path.Combine(cache, "walked-root-pytest-cache-probe.md");
            Directory.CreateDirectory(cache);
            File.WriteAllText(readme, "# pytest cache directory #\n");
            try
            {
                // Act
                var walked = (List<string>)typeof(DocumentationCorpus)
                    .GetMethod("Walk", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object[] { false })!;
                var reached = walked.Where(entry => entry.Contains(".pytest_cache", StringComparison.Ordinal));

                // Assert — that the walk descended into the root holding the cache rides along, because a
                // walk stopping short of scripts/ reports nothing under it either.
                Assert.That(
                    (walked.Any(entry => entry.StartsWith("scripts/", StringComparison.Ordinal)),
                        string.Join(", ", reached)),
                    Is.EqualTo((true, string.Empty)),
                    "a tool's cache written into a walked root enters the corpus and is scanned as prose");
            }
            finally
            {
                File.Delete(readme);
                if (!cacheExisted)
                {
                    Directory.Delete(cache, recursive: true);
                }
            }
        }
    }
}
