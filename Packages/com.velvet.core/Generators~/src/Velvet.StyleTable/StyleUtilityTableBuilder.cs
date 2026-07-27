using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

        public static StyleUtilityTableResult Build(IReadOnlyCollection<UssSourceText> sheets)
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
                    new StyleUtilityTable(longhands, ImmutableArray<StyleUtilityTableEntry>.Empty),
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

            var accumulated = new Dictionary<string, MutableEntry>(StringComparer.Ordinal);
            foreach (var source in sheets.OrderBy(s => s.Path, StringComparer.Ordinal))
            {
                CollectSheet(UssStyleSheetParser.Parse(source.Path, source.Text), bitOf, accumulated, problems);
            }

            var entries = accumulated.Values
                .OrderBy(e => e.ClassName, StringComparer.Ordinal)
                .Select(e => new StyleUtilityTableEntry(e.ClassName, e.Gate, e.Word0, e.Word1))
                .ToImmutableArray();

            return new StyleUtilityTableResult(
                new StyleUtilityTable(longhands, entries),
                problems.ToImmutable());
        }

        private static void CollectSheet(
            UssSheet sheet,
            Dictionary<string, int> bitOf,
            Dictionary<string, MutableEntry> accumulated,
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
                    CollectRule(sheet, rule, target, bitOf, accumulated, problems);
                }
            }
        }

        private static void CollectRule(
            UssSheet sheet,
            UssRule rule,
            UssSelectorTarget target,
            Dictionary<string, int> bitOf,
            Dictionary<string, MutableEntry> accumulated,
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
                        if (!IsCustomProperty(declaration.Property))
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
                if (IsCustomProperty(declaration.Property))
                {
                    problems.Add(sheet.ProblemAt(
                        UssProblemCode.UtilityDeclaresCustomProperty,
                        $"Utility class '{target.ClassName}' declares custom property " +
                        $"'{declaration.Property}'. It acts on whatever reads it through var(), not on the " +
                        "element carrying the class, which the table cannot express.",
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
            }
        }

        private static bool IsCustomProperty(string property) =>
            property.StartsWith("--", StringComparison.Ordinal);

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

    internal sealed class StyleUtilityTable
    {
        public StyleUtilityTable(
            ImmutableArray<UssLonghand> longhands,
            ImmutableArray<StyleUtilityTableEntry> entries)
        {
            Longhands = longhands;
            Entries = entries;
        }

        /// <summary>The longhand vocabulary in bit order: index <c>i</c> is bit <c>i</c> of a property set.</summary>
        public ImmutableArray<UssLonghand> Longhands { get; }

        /// <summary>One entry per utility class, ordered by class name.</summary>
        public ImmutableArray<StyleUtilityTableEntry> Entries { get; }
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
