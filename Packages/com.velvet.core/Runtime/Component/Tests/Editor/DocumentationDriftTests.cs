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
    /// Machine-checks the shipped Documentation~ guides (plus both README.md files, CLAUDE.md and the
    /// contributor README under Generators~) against the actual runtime API surface, so a doc referencing
    /// a renamed/removed <c>V.*</c> factory or <c>Hooks.*</c> hook,
    /// a path or a type that no longer exists, or an index that has drifted from the files on disk, fails a
    /// test instead of shipping silently wrong. Each check pins a failure mode that has actually shipped: a
    /// guide referencing a never-implemented factory, a hook table drifting from the real hook surface, an
    /// index missing real guide files, and a type name written for a file that holds differently-named types.
    /// </summary>
    [TestFixture]
    internal sealed class DocumentationDriftTests
    {
        // "V.X" appears only in react-migration.md's React-syntax comparison tables (and its mirrored rows in
        // both README.md files) as a meta-syntactic placeholder standing in for an arbitrary user component —
        // the same role "<X/>" plays in the JSX column of the same row — not a reference to a real V.* factory.
        private static readonly HashSet<string> VReferenceAllowlist = new() { "X" };

        // Names that resolve nowhere in this repo's code for a reason. Five groups, each a different one:
        // meta-syntactic placeholders standing in for something the reader supplies; API belonging to the
        // upstream libraries Velvet mirrors, which exists there and deliberately not here; names from
        // Unity or the BCL — types, enum values, event names, asset labels — that the docs mention but no
        // source file in this repo uses as code; names an external toolchain owns, which no source file
        // here spells and which the contributor README quotes when it says how to invoke that toolchain —
        // DOTNET_ROOT, StrykerOutput, MSB4006, ContinuousIntegrationBuild, ProjectReference. What each one
        // does is the toolchain's to state and has been got wrong here more than once; the reason for the
        // entry is only that the name is not code in this repository. And the analyzer identifiers, which
        // C# holds only as string literals and the corpus therefore strips.
        //
        // That last group is checked, just not here: DocumentationDiagnosticTableTests over in the
        // Generators~ suite reads the same README and compares its VEL and USS spellings, and the diagnostic
        // categories it names, against the real descriptors and against the derivation's real code range.
        // One entry sits outside even that: "VEL" is the ID space written as a shape (VEL###) rather than an
        // ID, which that guard's VEL\d{3} pattern is right not to match.
        private static readonly HashSet<string> IdentifierAllowlist = new()
        {
            "Foo", "SomeFixture", "MyRender", "MyStore", "Ndeg", "Npx", "ResolveDirection", "Inter", "CS",
            "AnimatedList", "PointerSensor", "KeyboardSensor", "MeasuringConfiguration", "Collision",
            "MultiColumnListView", "PopupWindow", "TreeView", "TabView", "ToggleButtonGroup", "Raycast",
            "GetAllocatedBytesForCurrentThread", "FocusController", "ScaleWithScreenSize", "RoslynAnalyzer",
            "UnityUIEFilter", "FocusIn", "KeyDown", "PointerDown", "Move", "Leave", "Up", "Wheel", "Enter",
            "RoslynAdditionalFileImporter", "DOTNET_ROOT", "StrykerOutput", "MSB4006",
            "ContinuousIntegrationBuild", "USS001", "USS011", "VEL", "VEL500", "VEL501", "VEL502",
            "ProjectReference", "VEL503"
        };

        private static readonly string[] SourceExtensions = { ".cs", ".uss", ".yml", ".json", ".asmdef" };

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
            @"^(\.{1,2}/)*(\.[A-Za-z0-9_-]+/)?[A-Za-z0-9_~@][A-Za-z0-9_./~*-]*(\.(cs|uss|md|json|yml|py|sh|txt|asmdef|dll)|/)$",
            RegexOptions.Compiled);

        private static readonly Regex RelativePrefixPattern = new(@"^(\.{1,2}/)+", RegexOptions.Compiled);

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
                var removed = whole.Where(w => !SourceIdentifiers.Value.Contains(w)).ToList();
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
                    .Where(name => !SourceIdentifiers.Value.Contains(name))
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
            var removed = withRegions.Where(w => !SourceIdentifiers.Value.Contains(w)).ToList();
            var notFromALabel = removed
                .Where(w => !labelWords.Contains(w))
                .OrderBy(w => w, StringComparer.Ordinal);

            // Assert — a strip that is not running removes nothing, so an empty first term is the shape
            // that catches it. The second catches a strip widened by any route other than the spelling both
            // sides read; widening that spelling to every directive is caught instead by the identifier
            // case, and only for as long as a guide keeps citing UNITY_EDITOR — Generators~/README.md is
            // the one that does.
            Assert.That(
                (removed.Count > 0, string.Join(", ", notFromALabel)),
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
            var unresolved = ScanBacktickSpans((label, reference) =>
                PathReferencePattern.IsMatch(reference) && !PathReferenceResolves(reference)
                    ? new[] { $"{label}: {reference}" }
                    : Array.Empty<string>());

            // Assert
            Assert.That(unresolved, Is.Empty,
                "Documentation names paths that do not exist:\n" + string.Join("\n", unresolved));
        }

        [Test]
        public void Given_ProjectMarkdown_When_ScannedForBacktickedIdentifiers_Then_EveryIdentifierAppearsInASource()
        {
            // Arrange / Act
            var unresolved = ScanBacktickSpans((label, reference) =>
                // A file name is claimed by the path check first, which resolves it against the filesystem —
                // the stronger question. Without this, VNodePool.cs would also be read as two identifiers.
                PathReferencePattern.IsMatch(reference)
                    // An elision leaves out the part that would say what is being named: a naming convention
                    // written as a shape, a signature with its arguments dropped. The V.* and Hooks.* checks
                    // still resolve the head of a dropped-argument call, so what this gives up is a span whose
                    // head is neither — and the alternative is reporting the elision's own fragments.
                    || reference.Contains("...")
                        ? Array.Empty<string>()
                        : IdentifierTokenPattern.Matches(JsxElementPattern.Replace(reference, " "))
                            .Select(token => token.Value)
                            .Where(token => !SourceIdentifiers.Value.Contains(token)
                                            && !IdentifierAllowlist.Contains(token))
                            .Select(token => $"{label}: {token} (in `{reference}`)"));

            // Assert
            Assert.That(unresolved, Is.Empty,
                "Documentation names identifiers that appear in no source file:\n" + string.Join("\n", unresolved));
        }

        // Runs `extract` over every backticked span of every target file. An absolute path is skipped outright:
        // it names something on the machine running the docs, not in the repo.
        private static List<string> ScanBacktickSpans(Func<string, string, IEnumerable<string>> extract)
        {
            var unresolved = new List<string>();
            foreach (var (path, label) in DocumentationCorpus.Files())
            {
                var prose = FencedBlockPattern.Replace(File.ReadAllText(path), "\n");
                foreach (Match span in BacktickSpanPattern.Matches(prose))
                {
                    var reference = string.Join(" ", span.Groups[1].Value.Split((char[])null!,
                        StringSplitOptions.RemoveEmptyEntries));
                    if (reference.Length == 0 || reference[0] == '/')
                    {
                        continue;
                    }
                    unresolved.AddRange(extract(label, reference));
                }
            }
            return unresolved.Distinct().ToList();
        }

        // Every identifier-shaped word in the repo's own CODE. A name surviving nowhere in it was renamed or
        // deleted, which is the drift this checks for. It deliberately does not care WHERE the word occurs:
        // resolving this mix of runtime types, Unity types, generator symbols and CI variables against their
        // real declarations would need every one of those toolchains loaded into the test. What it does care
        // about is that the word is code — see the stripping patterns for why prose cannot be trusted here.
        // What is stripped per format is StripProse's to say.
        private static readonly Lazy<HashSet<string>> SourceIdentifiers = new(() =>
        {
            var words = new Regex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in DocumentationCorpus.RepoEntries(includeClaude: false))
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
        });

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
                return trimmed.Contains('*') || DocumentationCorpus.RepoEntries(includeClaude: false)
                    .Any(entry => IsSuffixPath(entry, trimmed));
            }
            var directory = separator < 0 ? string.Empty : trimmed[..separator];
            var extension = leaf.TrimStart('*');
            return DocumentationCorpus.RepoEntries(includeClaude: false).Any(entry =>
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
            foreach (var (path, label) in DocumentationCorpus.Files())
            {
                var haystack = select(File.ReadAllText(path));
                foreach (Match match in pattern.Matches(haystack))
                {
                    var name = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                    if (isKnown(name))
                    {
                        continue;
                    }
                    unresolved.Add($"{label}: {prefix}{name}");
                }
            }
            return unresolved.Distinct().ToList();
        }
    }
}
