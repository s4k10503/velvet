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
                    $"The longhand vocabulary holds {longhands.Length} properties but the generated property " +
                    $"set holds {PropertySetCapacity}. Widen the set by emitting another backing word; " +
                    "truncating would drop the properties past the limit and make the classes that write " +
                    "them look conflict-free."));
                return new StyleUtilityTableResult(
                    new StyleUtilityTable(longhands, ImmutableArray<StyleUtilityTableEntry>.Empty),
                    problems.ToImmutable());
            }

            if (sheets.Count == 0)
            {
                problems.Add(new UssProblem(
                    UssProblemCode.NoStyleSheets,
                    "No stylesheet was supplied. An empty table is indistinguishable from a table whose " +
                    "classes all conflict with nothing, so there is no safe way to emit one."));
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
                    "Could not read the stylesheet: " + error.Message +
                    ". Whatever follows is not in the table, so the classes it defines would look like they " +
                    "set nothing.",
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
                        $"'{rule.Selector}' is not a USS construct the utility property table can model. It " +
                        "models class selectors (optionally gated on a pseudo-class or the is-selected " +
                        "marker class), :root blocks, type-keyed selectors and @import statements.",
                        rule.Offset));
                    return;

                case UssSelectorKind.RootBlock:
                    foreach (var declaration in rule.Declarations)
                    {
                        if (!IsCustomProperty(declaration.Property))
                        {
                            problems.Add(sheet.ProblemAt(
                                UssProblemCode.RootDeclaresNonCustomProperty,
                                $"':root' declares '{declaration.Property}'. The table skips :root on the " +
                                "premise that it defines only custom properties, which are values other " +
                                "declarations read through var() rather than properties an element holds; a " +
                                "real property declared there applies to the root element and the exclusion " +
                                "has to be revisited.",
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
                    $"Utility class '{target.ClassName}' is already defined under gate '{entry.Gate}' and is " +
                    $"redefined under gate '{target.Gate}'. A gated rule and an ungated rule live in " +
                    "different cascade layers, so merging them into one property set would let a " +
                    "higher-priority class evict a class whose rule only applies in a state that class does " +
                    "not participate in.",
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
                        $"'{declaration.Property}'. Such a class acts on the elements whose declarations read " +
                        "it through var(), not on the element carrying the class, and the table has no way " +
                        "to express that reach.",
                        declaration.Offset));
                    continue;
                }
                if (!UssPropertyVocabulary.TryResolve(declaration.Property, out var longhands))
                {
                    problems.Add(sheet.ProblemAt(
                        UssProblemCode.UnknownProperty,
                        $"'{declaration.Property}' is not a UI Toolkit longhand or shorthand property. Every " +
                        "declared property must resolve to longhands so that two classes writing the same " +
                        "storage slot compare equal.",
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
