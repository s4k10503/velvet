using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Velvet.StyleTable
{
    /// <summary>
    /// Derives the utility class → longhand property table from the bundled stylesheets.
    /// </summary>
    internal static class StyleUtilityTableBuilder
    {
        /// <summary>Bit width of the generated property set. Two 64-bit words.</summary>
        public const int PropertySetCapacity = 128;

        private const string TransitionProperty = "transition-property";

        /// <summary>
        /// Derives the table from <paramref name="sheets"/>, which arrive in cascade order and are not
        /// re-sorted here — the transition table records position, so the order IS part of the answer. A
        /// caller that hands them over in some other order is caught by the <c>@import</c> cross-check.
        /// </summary>
        public static StyleUtilityTableResult Build(IReadOnlyList<UssSourceText> sheets)
        {
            var problems = ImmutableArray.CreateBuilder<UssProblem>();
            var longhands = UssPropertyVocabulary.OrderedLonghands;
            if (longhands.Length > PropertySetCapacity)
            {
                problems.Add(new UssProblem(
                    UssProblemCode.VocabularyExceedsCapacity,
                    $"The longhand vocabulary holds {longhands.Length} properties but a property set holds " +
                    $"{PropertySetCapacity}. Emit another backing word in StyleLonghandSet."));
                return new StyleUtilityTableResult(
                    new StyleUtilityTable(
                        longhands,
                        ImmutableArray<StyleUtilityTableEntry>.Empty,
                        ImmutableArray<StyleTransitionTableEntry>.Empty),
                    problems.ToImmutable());
            }

            if (sheets.Count == 0)
            {
                problems.Add(new UssProblem(
                    UssProblemCode.NoStyleSheets,
                    "No stylesheet was supplied. Point --styles at the directory holding the USS partials."));
            }

            var bitOf = new Dictionary<string, int>(longhands.Length, StringComparer.Ordinal);
            for (var i = 0; i < longhands.Length; i++)
            {
                bitOf[longhands[i].UssName] = i;
            }

            var parsed = sheets.Select(s => UssStyleSheetParser.Parse(s.Path, s.Text)).ToList();
            ReportOrderThatContradictsTheImports(parsed, problems);

            var accumulated = new Dictionary<string, MutableEntry>(StringComparer.Ordinal);
            var transitions = new List<StyleTransitionTableEntry>();
            foreach (var sheet in parsed)
            {
                CollectSheet(sheet, bitOf, accumulated, transitions, problems);
            }

            var entries = accumulated.Values
                .OrderBy(e => e.ClassName, StringComparer.Ordinal)
                .Select(e => new StyleUtilityTableEntry(e.ClassName, e.Gate, e.Word0, e.Word1))
                .ToImmutableArray();

            return new StyleUtilityTableResult(
                new StyleUtilityTable(longhands, entries, transitions.ToImmutableArray()),
                problems.ToImmutable());
        }

        /// <summary>
        /// Checks the order the sheets arrived in against the aggregator's <c>@import</c> list, which is the
        /// order the importer concatenates them in. Skipped when no sheet imports anything, so a derivation
        /// over one hand-written stylesheet has no aggregator to answer to.
        /// </summary>
        private static void ReportOrderThatContradictsTheImports(
            IReadOnlyList<UssSheet> parsed, ImmutableArray<UssProblem>.Builder problems)
        {
            var imported = parsed
                .SelectMany(sheet => sheet.AtRules)
                .SelectMany(atRule => UssCascadeOrder.ImportedNames(atRule.Text))
                .Select(Path.GetFileName)
                .ToList();
            if (imported.Count == 0)
            {
                return;
            }

            var supplied = parsed
                .Where(sheet => sheet.AtRules.IsEmpty)
                .Select(sheet => Path.GetFileName(sheet.Path))
                .ToList();
            if (!supplied.SequenceEqual(imported, StringComparer.Ordinal))
            {
                problems.Add(new UssProblem(
                    UssProblemCode.StyleSheetOrderMismatch,
                    $"The sheets were supplied as [{string.Join(", ", supplied)}] but the @import list reads " +
                    $"[{string.Join(", ", imported)}]. Cascade order is the order they arrive in, so a sheet " +
                    "the aggregator does not import — or one supplied out of import order — would be recorded " +
                    "as beating rules it actually loses to."));
                return;
            }
            ReportAggregatorSuppliedAheadOfItsImports(parsed, problems);
        }

        /// <summary>
        /// Checks that each aggregator arrived after every partial it imports, which is where the importer
        /// puts it: an imported sheet is spliced in ahead of the importing sheet's own rules.
        /// </summary>
        private static void ReportAggregatorSuppliedAheadOfItsImports(
            IReadOnlyList<UssSheet> parsed, ImmutableArray<UssProblem>.Builder problems)
        {
            for (var position = 0; position < parsed.Count; position++)
            {
                var imports = parsed[position].AtRules
                    .SelectMany(atRule => UssCascadeOrder.ImportedNames(atRule.Text))
                    .Select(Path.GetFileName)
                    .ToList();
                if (imports.Count == 0)
                {
                    continue;
                }
                var lastImport = parsed
                    .Select((sheet, index) => (Name: Path.GetFileName(sheet.Path), Index: index))
                    .Where(candidate => imports.Contains(candidate.Name, StringComparer.Ordinal))
                    .Select(candidate => candidate.Index)
                    .Max();
                if (lastImport < position)
                {
                    continue;
                }
                problems.Add(new UssProblem(
                    UssProblemCode.StyleSheetOrderMismatch,
                    $"'{Path.GetFileName(parsed[position].Path)}' was supplied ahead of a partial it imports. " +
                    "An aggregator's own rules outrank every partial's, so supplying it first would record it " +
                    "as losing ties it wins."));
            }
        }

        private static void CollectSheet(
            UssSheet sheet,
            Dictionary<string, int> bitOf,
            Dictionary<string, MutableEntry> accumulated,
            List<StyleTransitionTableEntry> transitions,
            ImmutableArray<UssProblem>.Builder problems)
        {
            foreach (var error in sheet.Errors)
            {
                problems.Add(sheet.ProblemAt(
                    UssProblemCode.MalformedUss,
                    "Could not read the stylesheet: " + error.Message + ".",
                    error.Offset));
            }

            foreach (var atRule in sheet.AtRules)
            {
                // @import composes the aggregator out of the partials and declares nothing itself, so the
                // partials it names carry every property the table needs.
                if (!atRule.Text.StartsWith("@import", StringComparison.Ordinal))
                {
                    problems.Add(sheet.ProblemAt(
                        UssProblemCode.UnsupportedConstruct,
                        $"'{atRule.Text}' is not a USS construct the utility property table can model.",
                        atRule.Offset));
                }
            }

            foreach (var rule in sheet.Rules)
            {
                foreach (var target in UssSelector.Classify(rule.Selector))
                {
                    CollectRule(sheet, rule, target, bitOf, accumulated, transitions, problems);
                }
            }
        }

        private static void CollectRule(
            UssSheet sheet,
            UssRule rule,
            UssSelectorTarget target,
            Dictionary<string, int> bitOf,
            Dictionary<string, MutableEntry> accumulated,
            List<StyleTransitionTableEntry> transitions,
            ImmutableArray<UssProblem>.Builder problems)
        {
            switch (target.Kind)
            {
                case UssSelectorKind.Unsupported:
                    problems.Add(sheet.ProblemAt(
                        UssProblemCode.UnsupportedConstruct,
                        $"'{rule.Selector}' is not a selector the table can model. It models a class, " +
                        "optionally gated on a pseudo-class or the is-selected marker, plus :root blocks, " +
                        "type-keyed selectors and @import.",
                        rule.Offset));
                    return;

                case UssSelectorKind.RootBlock:
                    foreach (var declaration in rule.Declarations)
                    {
                        if (!declaration.IsCustomProperty)
                        {
                            problems.Add(sheet.ProblemAt(
                                UssProblemCode.RootDeclaresNonCustomProperty,
                                $"':root' declares '{declaration.Property}'. The table skips :root because " +
                                "custom properties are values var() reads, not properties an element holds; " +
                                "a real property there needs a deliberate decision.",
                                declaration.Offset));
                        }
                    }
                    return;

                case UssSelectorKind.TypeKeyed:
                    return;
            }

            // A theme block is excluded for the reason :root is: what it declares is read through var(), so
            // it holds no property a utility could contend with it for.
            if (rule.IsTokenBlock)
            {
                return;
            }

            if (!accumulated.TryGetValue(target.ClassName, out var entry))
            {
                entry = new MutableEntry(target.ClassName, target.Gate);
                accumulated.Add(target.ClassName, entry);
            }
            else if (entry.Gate != target.Gate)
            {
                problems.Add(sheet.ProblemAt(
                    UssProblemCode.ClassSpansMultipleGates,
                    $"Utility class '{target.ClassName}' is defined under gate '{entry.Gate}' and again " +
                    $"under gate '{target.Gate}'. A gated and an ungated rule are different cascade layers " +
                    "and cannot share one property set.",
                    rule.Offset));
                return;
            }

            foreach (var declaration in rule.Declarations)
            {
                if (declaration.IsCustomProperty)
                {
                    problems.Add(sheet.ProblemAt(
                        UssProblemCode.UtilityDeclaresCustomProperty,
                        $"Utility class '{target.ClassName}' declares custom property " +
                        $"'{declaration.Property}' beside properties it sets on the element itself. A rule " +
                        "declaring custom properties alone is a theme block and is excluded like :root; " +
                        "one class in both roles is what the table cannot express.",
                        declaration.Offset));
                    continue;
                }
                if (!UssPropertyVocabulary.TryResolve(declaration.Property, out var longhands))
                {
                    problems.Add(sheet.ProblemAt(
                        UssProblemCode.UnknownProperty,
                        $"'{declaration.Property}' is not a UI Toolkit longhand or shorthand. Add it to " +
                        "UssPropertyVocabulary if a newer Unity introduced it.",
                        declaration.Offset));
                    continue;
                }
                foreach (var longhand in longhands)
                {
                    entry.Set(bitOf[longhand]);
                }
                if (!string.Equals(declaration.Property, TransitionProperty, StringComparison.Ordinal))
                {
                    continue;
                }
                if (target.Gate != UssGate.None)
                {
                    problems.Add(sheet.ProblemAt(
                        UssProblemCode.GatedTransitionProperty,
                        $"Utility class '{target.ClassName}' declares transition-property under gate " +
                        $"'{target.Gate}'. MotionNativeTransitionGuard answers from an element's class list " +
                        "alone, where a gate's state is unknowable, so it would keep answering from the " +
                        "ungated declarations and suspend an element this rule had already stopped " +
                        "transitioning.",
                        declaration.Offset));
                    continue;
                }
                CollectTransitionDeclaration(sheet, target.ClassName, declaration, bitOf, transitions, problems);
            }
        }

        /// <summary>
        /// Records what a <c>transition-property</c> declaration names, at the position it was declared. A
        /// class that declares it more than once moves to the later position, by the rule
        /// <c>MotionNativeTransitionGuard.DeclaredSlots</c> states.
        /// </summary>
        private static void CollectTransitionDeclaration(
            UssSheet sheet,
            string className,
            UssDeclaration declaration,
            Dictionary<string, int> bitOf,
            List<StyleTransitionTableEntry> transitions,
            ImmutableArray<UssProblem>.Builder problems)
        {
            var word0 = 0UL;
            var word1 = 0UL;
            foreach (var token in declaration.Value.Split(','))
            {
                var name = token.Trim();
                if (name.Length == 0 || string.Equals(name, "none", StringComparison.Ordinal))
                {
                    continue;
                }
                if (string.Equals(name, "all", StringComparison.Ordinal)
                    || string.Equals(name, "initial", StringComparison.Ordinal))
                {
                    foreach (var bit in bitOf.Values)
                    {
                        Set(bit, ref word0, ref word1);
                    }
                    continue;
                }
                if (!UssPropertyVocabulary.TryResolve(name, out var longhands))
                {
                    problems.Add(sheet.ProblemAt(
                        UssProblemCode.UnknownTransitionProperty,
                        $"Utility class '{className}' transitions '{name}', which is neither a UI Toolkit " +
                        "property nor the all/none keyword. A name the engine does not know transitions " +
                        "nothing, so recording it would describe a transition that never runs.",
                        declaration.Offset));
                    return;
                }
                foreach (var longhand in longhands)
                {
                    Set(bitOf[longhand], ref word0, ref word1);
                }
            }
            transitions.RemoveAll(entry => string.Equals(entry.ClassName, className, StringComparison.Ordinal));
            transitions.Add(new StyleTransitionTableEntry(className, word0, word1));
        }

        private static void Set(int bit, ref ulong word0, ref ulong word1)
        {
            if (bit < 64)
            {
                word0 |= 1UL << bit;
            }
            else
            {
                word1 |= 1UL << (bit - 64);
            }
        }

        private sealed class MutableEntry
        {
            public MutableEntry(string className, UssGate gate)
            {
                ClassName = className;
                Gate = gate;
            }

            public string ClassName { get; }

            public UssGate Gate { get; }

            public ulong Word0 { get; private set; }

            public ulong Word1 { get; private set; }

            public void Set(int bit)
            {
                if (bit < 64)
                {
                    Word0 |= 1UL << bit;
                }
                else
                {
                    Word1 |= 1UL << (bit - 64);
                }
            }
        }
    }

    /// <summary>A stylesheet's path and text.</summary>
    internal readonly struct UssSourceText
    {
        public UssSourceText(string path, string text)
        {
            Path = path;
            Text = text;
        }

        public string Path { get; }

        public string Text { get; }
    }

    internal readonly struct StyleUtilityTableEntry
    {
        public StyleUtilityTableEntry(string className, UssGate gate, ulong word0, ulong word1)
        {
            ClassName = className;
            Gate = gate;
            Word0 = word0;
            Word1 = word1;
        }

        public string ClassName { get; }

        public UssGate Gate { get; }

        public ulong Word0 { get; }

        public ulong Word1 { get; }
    }

    /// <summary>One utility's <c>transition-property</c> declaration: the properties its value names.</summary>
    internal readonly struct StyleTransitionTableEntry
    {
        public StyleTransitionTableEntry(string className, ulong word0, ulong word1)
        {
            ClassName = className;
            Word0 = word0;
            Word1 = word1;
        }

        public string ClassName { get; }

        public ulong Word0 { get; }

        public ulong Word1 { get; }
    }

    internal sealed class StyleUtilityTable
    {
        public StyleUtilityTable(
            ImmutableArray<UssLonghand> longhands,
            ImmutableArray<StyleUtilityTableEntry> entries,
            ImmutableArray<StyleTransitionTableEntry> transitions)
        {
            Longhands = longhands;
            Entries = entries;
            Transitions = transitions;
        }

        /// <summary>The longhand vocabulary in bit order: index <c>i</c> is bit <c>i</c> of a property set.</summary>
        public ImmutableArray<UssLonghand> Longhands { get; }

        /// <summary>One entry per utility class, ordered by class name.</summary>
        public ImmutableArray<StyleUtilityTableEntry> Entries { get; }

        /// <summary>The utilities that declare <c>transition-property</c>, in cascade order.</summary>
        public ImmutableArray<StyleTransitionTableEntry> Transitions { get; }
    }

    internal readonly struct StyleUtilityTableResult
    {
        public StyleUtilityTableResult(StyleUtilityTable table, ImmutableArray<UssProblem> problems)
        {
            Table = table;
            Problems = problems;
        }

        public StyleUtilityTable Table { get; }

        public ImmutableArray<UssProblem> Problems { get; }
    }
}
