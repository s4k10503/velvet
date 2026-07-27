using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Velvet.StyleTable;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins the shape of the bundled stylesheets that the utility property table is designed around, and
    /// pins the committed table against a fresh derivation from those stylesheets.
    /// </summary>
    /// <remarks>
    /// The table is derived once by a contributor and committed, so nothing at compile time re-derives it.
    /// This fixture takes that job: the license-free generator workflow runs on every change under Runtime/,
    /// so a stylesheet edit not accompanied by a regenerated table fails here. Unlike the committed analyzer
    /// assemblies, which embed the git commit id and so can never be byte-compared against a rebuild, the
    /// emitted C# is a pure function of the stylesheets — an exact comparison is available, and is used.
    ///
    /// The counts below are the inventory of shapes the derivation was written against. A stylesheet edit
    /// that grows the corpus past what was surveyed turns them red on purpose; confirm the new rules are
    /// shapes the derivation models, then update the number.
    /// </remarks>
    public sealed class BundledStyleSheetCensusTests
    {
        private const int SurveyedRuleCount = 2134;
        private const int SurveyedSingleClassRuleCount = 2094;
        private const int SurveyedDistinctPropertyNameCount = 63;
        private const int SurveyedUtilityClassCount = 2138;
        private const int SurveyedTransitionUtilityCount = 36;

        private static readonly Census Surveyed = Census.OfBundledStyleSheets();

        [Fact]
        public void Given_TheBundledStyleSheets_When_TheTableIsDerivedAfresh_Then_TheCommittedFileMatches()
        {
            // Arrange
            var sheets = BundledStyleSheets();
            Assume.NotEmpty(sheets, "the bundled stylesheets were located");

            // Act
            var derived = StyleTableTestHelper.Derive(sheets).EmittedSource;

            // Assert
            Assert.Equal(File.ReadAllText(CommittedTablePath()), derived);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_TheTableIsDerivedAfresh_Then_NoProblemIsReported()
        {
            // Arrange
            var sheets = BundledStyleSheets();
            Assume.NotEmpty(sheets, "the bundled stylesheets were located");

            // Act
            var problems = StyleTableTestHelper.Derive(sheets).Problems.Select(p => p.ToString()).ToList();

            // Assert
            Assert.Empty(problems);
        }

        [Fact]
        public void Given_TheDerivedTable_When_Compiled_Then_ItYieldsOneEntryPerUtilityClass()
        {
            // Arrange
            var sheets = BundledStyleSheets();
            Assume.NotEmpty(sheets, "the bundled stylesheets were located");

            // Act
            var count = StyleTableTestHelper.Load(StyleTableTestHelper.Derive(sheets)).Count;

            // Assert
            Assert.Equal(SurveyedUtilityClassCount, count);
        }

        [Fact]
        public void Given_TheDerivedTable_When_Compiled_Then_TwoColourUtilitiesContendForTheSameProperty()
        {
            // Arrange
            var sheets = BundledStyleSheets();
            Assume.NotEmpty(sheets, "the bundled stylesheets were located");
            var probe = StyleTableTestHelper.Load(StyleTableTestHelper.Derive(sheets));

            // Act
            var shared = probe.PropertiesOf("bg-white").Intersect(probe.PropertiesOf("bg-surface"));

            // Assert
            Assert.Equal(new[] { "background-color" }, shared);
        }

        [Fact]
        public void Given_TheDerivedTable_When_Compiled_Then_ItRecordsEveryTransitionPropertyDeclaration()
        {
            // Arrange
            var sheets = BundledStyleSheets();
            Assume.NotEmpty(sheets, "the bundled stylesheets were located");

            // Act
            var count = StyleTableTestHelper.Load(StyleTableTestHelper.Derive(sheets)).TransitionCount;

            // Assert
            Assert.Equal(SurveyedTransitionUtilityCount, count);
        }

        [Fact]
        public void Given_TheDerivedTable_When_Compiled_Then_TheTransitionUtilitiesAreInTheOrderTheSheetsDeclareThem()
        {
            // Arrange — transition-none sits between transition-all and the property-specific utilities rather
            // than ahead of both, which is the deviation from the reference cascade BundledStyleSheetOrderTests
            // exempts.
            var sheets = BundledStyleSheets();
            Assume.NotEmpty(sheets, "the bundled stylesheets were located");
            var probe = StyleTableTestHelper.Load(StyleTableTestHelper.Derive(sheets));

            // Act
            var ordered = probe.TransitionUtilitiesInCascadeOrder()
                .Where(name => name.StartsWith("transition-", StringComparison.Ordinal))
                .ToArray();

            // Assert
            Assert.Equal(
                new[]
                {
                    "transition-transform", "transition-filter", "transition-all", "transition-none",
                    "transition-opacity", "transition-colors", "transition-colors-scale",
                    "transition-colors-scale-opacity",
                },
                ordered);
        }

        [Fact]
        public void Given_TheDerivedTable_When_Compiled_Then_EveryAnimationPresetOutranksEveryTransitionUtility()
        {
            // Arrange — _animations.uss is imported last so a scheduler-applied preset replaces whatever
            // transition-* the element already carried for the length of the play.
            var sheets = BundledStyleSheets();
            Assume.NotEmpty(sheets, "the bundled stylesheets were located");
            var ordered = StyleTableTestHelper.Load(StyleTableTestHelper.Derive(sheets))
                .TransitionUtilitiesInCascadeOrder();

            // Act
            var firstPreset = ordered
                .TakeWhile(name => !name.StartsWith("anim-", StringComparison.Ordinal))
                .Count();

            // Assert
            Assert.Equal(
                ordered.Count(name => !name.StartsWith("anim-", StringComparison.Ordinal)),
                firstPreset);
        }

        [Fact]
        public void Given_TheDerivedTable_When_Compiled_Then_TransitionFilterNamesFilterAlone()
        {
            // Arrange
            var sheets = BundledStyleSheets();
            Assume.NotEmpty(sheets, "the bundled stylesheets were located");

            // Act
            var properties = StyleTableTestHelper.Load(StyleTableTestHelper.Derive(sheets))
                .TransitionPropertiesOf("transition-filter");

            // Assert
            Assert.Equal(new[] { "filter" }, properties);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_Parsed_Then_NoneIsMalformed()
        {
            // Arrange
            Assume.NotEmpty(Surveyed.Sheets, "the bundled stylesheets were located and parsed");

            // Act
            var malformed = Surveyed.Sheets
                .SelectMany(s => s.Errors.Select(e => s.Path + ": " + e.Message))
                .ToList();

            // Assert
            Assert.Empty(malformed);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_RulesAreCounted_Then_TheTotalMatchesTheSurvey()
        {
            // Arrange
            Assume.NotEmpty(Surveyed.Sheets, "the bundled stylesheets were located and parsed");

            // Act
            var ruleCount = Surveyed.Rules.Count;

            // Assert
            Assert.Equal(SurveyedRuleCount, ruleCount);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_SelectorsAreClassified_Then_SingleClassRulesMatchTheSurvey()
        {
            // Arrange
            Assume.NotEmpty(Surveyed.Rules, "the bundled stylesheets declare rules");

            // Act
            var singleClassRules = Surveyed.ShapeHistogram[SelectorShape.SingleClass];

            // Assert
            Assert.Equal(SurveyedSingleClassRuleCount, singleClassRules);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_SelectorsAreClassified_Then_SingleClassRulesRemainTheOverwhelmingMajority()
        {
            // Arrange
            Assume.NotEmpty(Surveyed.Rules, "the bundled stylesheets declare rules");

            // Act
            var share = 100.0 * Surveyed.ShapeHistogram[SelectorShape.SingleClass] / Surveyed.Rules.Count;

            // Assert
            Assert.True(
                share >= 98.0,
                $"Single-class selectors are {share:F2}% of the rules. The table treats every other shape as " +
                "an explicitly handled exception, which stops being a workable design once the exceptions are " +
                "common.");
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_SelectorsAreClassified_Then_TheExceptionsAreOnlyTheHandledShapes()
        {
            // Arrange
            var expected = new Dictionary<SelectorShape, int>
            {
                [SelectorShape.SingleClass] = 2094,
                [SelectorShape.SelectorList] = 2,
                [SelectorShape.ClassWithPseudoClass] = 32,
                [SelectorShape.ClassWithStateMarker] = 3,
                [SelectorShape.Root] = 2,
                [SelectorShape.TypeKeyed] = 1,
            };

            // Act
            var actual = Surveyed.ShapeHistogram;

            // Assert
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_AtRulesAreCollected_Then_OnlyImportIsUsed()
        {
            // Arrange
            var atRules = Surveyed.Sheets.SelectMany(s => s.AtRules).Select(a => a.Text).ToList();
            Assume.NotEmpty(atRules, "the bundled stylesheets contain at-rule statements");

            // Act
            var nonImport = atRules.Where(a => !a.StartsWith("@import", StringComparison.Ordinal)).ToList();

            // Assert
            Assert.Empty(nonImport);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_ARuleFreePartialIsFound_Then_ItIsOneOfTheKnownPayloadFreeFiles()
        {
            // Arrange
            // A class absent from the table and a class whose stylesheet declares nothing look identical from
            // the table's side. These four files declare no rules by design and each says so in its own
            // header: gap-*, the state markers and the preset stub are realised in C# rather than USS, and
            // the aggregator is nothing but @import statements.
            var expected = new[] { "StyleUtilities.uss", "_gap.uss", "_presets.uss", "_states.uss" };

            // Act
            var ruleFree = Surveyed.Sheets
                .Where(s => s.Rules.IsEmpty)
                .Select(s => Path.GetFileName(s.Path))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            // Assert
            Assert.Equal(expected, ruleFree);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_ClassNamesAreCollected_Then_NoClassCarriesTwoDifferentGates()
        {
            // Arrange
            Assume.NotEmpty(Surveyed.Rules, "the bundled stylesheets declare rules");

            // Act
            var multiGated = Surveyed.GatesByClassName
                .Where(pair => pair.Value.Count > 1)
                .Select(pair => pair.Key)
                .ToList();

            // Assert
            Assert.Empty(multiGated);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_PropertiesAreCollected_Then_TheDistinctNameCountMatchesTheSurvey()
        {
            // Arrange
            Assume.NotEmpty(Surveyed.Rules, "the bundled stylesheets declare rules");

            // Act
            var distinctNames = Surveyed.DeclaredPropertyNames.Count;

            // Assert
            Assert.Equal(SurveyedDistinctPropertyNameCount, distinctNames);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_PropertiesAreResolved_Then_EveryNameIsInTheUiToolkitVocabulary()
        {
            // Arrange
            Assume.NotEmpty(Surveyed.DeclaredPropertyNames, "the bundled stylesheets declare properties");

            // Act
            var unresolved = Surveyed.DeclaredPropertyNames
                .Where(name => !UssPropertyVocabulary.TryResolve(name, out _))
                .ToList();

            // Assert
            Assert.Empty(unresolved);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_ShorthandsAreIdentified_Then_TheyAreTheOnesTheSurveyFound()
        {
            // Arrange
            var expected = new[]
            {
                "background-position", "border-color", "border-radius", "border-width", "margin", "padding",
            };

            // Act
            var used = Surveyed.DeclaredPropertyNames
                .Where(name => UssPropertyVocabulary.TryExpandShorthand(name, out _))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            // Assert
            Assert.Equal(expected, used);
        }

        [Fact]
        public void Given_TheBundledStyleSheets_When_ShorthandsAreExpanded_Then_TheReferencedLonghandsFitTheVocabulary()
        {
            // Arrange
            Assume.NotEmpty(Surveyed.ReferencedLonghands, "the bundled stylesheets reference longhand properties");

            // Act
            var outsideVocabulary = Surveyed.ReferencedLonghands
                .Where(name => !UssPropertyVocabulary.IsLonghand(name))
                .ToList();

            // Assert
            Assert.Empty(outsideVocabulary);
        }

        [Fact]
        public void Given_TheUiToolkitLonghandVocabulary_When_Counted_Then_ItFitsThePropertySetCapacity()
        {
            // Arrange
            var vocabulary = UssPropertyVocabulary.OrderedLonghands;
            Assume.NotEmpty(vocabulary, "the longhand vocabulary is populated");

            // Act
            var count = vocabulary.Length;

            // Assert
            Assert.True(
                count <= StyleUtilityTableBuilder.PropertySetCapacity,
                $"The vocabulary holds {count} longhands but a property set holds " +
                $"{StyleUtilityTableBuilder.PropertySetCapacity}. Widen the set by emitting another backing " +
                "word; truncating would make the classes writing the dropped properties look conflict-free.");
        }

        [Fact]
        public void Given_TheUiToolkitLonghandVocabulary_When_Ordered_Then_EveryNameIsDistinct()
        {
            // Arrange
            var vocabulary = UssPropertyVocabulary.OrderedLonghands;
            Assume.NotEmpty(vocabulary, "the longhand vocabulary is populated");

            // Act
            var duplicates = vocabulary
                .GroupBy(l => l.UssName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            // Assert
            Assert.Empty(duplicates);
        }

        [Fact]
        public void Given_TheUiToolkitShorthandTable_When_Expanded_Then_EveryExpansionIsALonghand()
        {
            // Arrange
            var shorthands = new[]
            {
                "background-position", "border-color", "border-radius", "border-width", "flex", "margin",
                "padding", "transition", "-unity-background-scale-mode", "-unity-text-outline",
            };

            // Act
            var nonLonghandExpansions = shorthands
                .SelectMany(shorthand =>
                {
                    UssPropertyVocabulary.TryExpandShorthand(shorthand, out var longhands);
                    return longhands.IsDefault ? Enumerable.Empty<string>() : longhands;
                })
                .Where(name => !UssPropertyVocabulary.IsLonghand(name))
                .ToList();

            // Assert
            Assert.Empty(nonLonghandExpansions);
        }

        /// <summary>Where the committed table lives, which is also where the build script writes it.</summary>
        private static string CommittedTablePath() =>
            Path.Combine(SolutionPaths.RuntimeRoot(), "Styling", "StyleUtilityProperties.g.cs");

        /// <summary>
        /// In cascade order, the same as the build script supplies them: the derivation records which of two
        /// utilities setting one property wins, so a differently ordered input would derive a different table.
        /// </summary>
        private static StyleSheetInput[] BundledStyleSheets() =>
            UssCascadeOrder.SheetsIn(BundledStyleSheetDirectory())
                .Select(sheet => new StyleSheetInput(sheet.Path, sheet.Text))
                .ToArray();

        private static IEnumerable<string> BundledStyleSheetPaths() =>
            UssCascadeOrder.SheetsIn(BundledStyleSheetDirectory()).Select(sheet => sheet.Path);

        private static string BundledStyleSheetDirectory() =>
            Path.Combine(SolutionPaths.RuntimeRoot(), "Styles");

        /// <summary>The surveyed shapes, named so a histogram failure says which shape moved.</summary>
        private enum SelectorShape
        {
            SingleClass,
            SelectorList,
            ClassWithPseudoClass,
            ClassWithStateMarker,
            Root,
            TypeKeyed,
            Unmodelled,
        }

        private sealed class Census
        {
            private Census(
                IReadOnlyList<UssSheet> sheets,
                IReadOnlyList<UssRule> rules,
                IReadOnlyDictionary<SelectorShape, int> shapeHistogram,
                IReadOnlyDictionary<string, HashSet<UssGate>> gatesByClassName,
                IReadOnlyCollection<string> declaredPropertyNames,
                IReadOnlyCollection<string> referencedLonghands)
            {
                Sheets = sheets;
                Rules = rules;
                ShapeHistogram = shapeHistogram;
                GatesByClassName = gatesByClassName;
                DeclaredPropertyNames = declaredPropertyNames;
                ReferencedLonghands = referencedLonghands;
            }

            public IReadOnlyList<UssSheet> Sheets { get; }

            public IReadOnlyList<UssRule> Rules { get; }

            public IReadOnlyDictionary<SelectorShape, int> ShapeHistogram { get; }

            public IReadOnlyDictionary<string, HashSet<UssGate>> GatesByClassName { get; }

            /// <summary>Property names as authored, so shorthands are still distinguishable from longhands.</summary>
            public IReadOnlyCollection<string> DeclaredPropertyNames { get; }

            public IReadOnlyCollection<string> ReferencedLonghands { get; }

            public static Census OfBundledStyleSheets()
            {
                var sheets = BundledStyleSheetPaths()
                    .Select(path => UssStyleSheetParser.Parse(path, File.ReadAllText(path)))
                    .ToList();

                var rules = sheets.SelectMany(s => s.Rules).ToList();
                var histogram = new Dictionary<SelectorShape, int>();
                var gates = new Dictionary<string, HashSet<UssGate>>(StringComparer.Ordinal);
                var declared = new SortedSet<string>(StringComparer.Ordinal);
                var longhands = new SortedSet<string>(StringComparer.Ordinal);

                foreach (var rule in rules)
                {
                    var shape = ShapeOf(rule.Selector);
                    histogram[shape] = histogram.TryGetValue(shape, out var seen) ? seen + 1 : 1;

                    foreach (var target in UssSelector.Classify(rule.Selector))
                    {
                        if (target.Kind != UssSelectorKind.UtilityClass)
                        {
                            continue;
                        }
                        if (!gates.TryGetValue(target.ClassName, out var seenGates))
                        {
                            seenGates = new HashSet<UssGate>();
                            gates.Add(target.ClassName, seenGates);
                        }
                        seenGates.Add(target.Gate);
                    }

                    if (rule.Selector.StartsWith(":root", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    foreach (var declaration in rule.Declarations)
                    {
                        declared.Add(declaration.Property);
                        if (UssPropertyVocabulary.TryResolve(declaration.Property, out var expanded))
                        {
                            foreach (var longhand in expanded)
                            {
                                longhands.Add(longhand);
                            }
                        }
                    }
                }

                return new Census(sheets, rules, histogram, gates, declared, longhands);
            }

            private static SelectorShape ShapeOf(string selector)
            {
                if (selector.Contains(","))
                {
                    return SelectorShape.SelectorList;
                }
                var target = UssSelector.Classify(selector)[0];
                switch (target.Kind)
                {
                    case UssSelectorKind.RootBlock:
                        return SelectorShape.Root;
                    case UssSelectorKind.TypeKeyed:
                        return SelectorShape.TypeKeyed;
                    case UssSelectorKind.UtilityClass when target.Gate == UssGate.None:
                        return SelectorShape.SingleClass;
                    case UssSelectorKind.UtilityClass when target.Gate == UssGate.Selected:
                        return SelectorShape.ClassWithStateMarker;
                    case UssSelectorKind.UtilityClass:
                        return SelectorShape.ClassWithPseudoClass;
                    default:
                        return SelectorShape.Unmodelled;
                }
            }
        }
    }
}
