using System.Linq;
using Velvet.StyleTable;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Verifies what the utility property table records for each USS shape the bundled stylesheets contain,
    /// and that a shape outside that set fails the derivation instead of contributing an empty property set.
    /// </summary>
    /// <remarks>
    /// An empty set reads as "this class can never conflict with another", so a construct the derivation
    /// quietly skipped would not weaken the table's answer, it would reverse it. Every negative case here
    /// therefore asserts a reported problem rather than an absence.
    /// </remarks>
    public sealed class StyleUtilityTableTests
    {
        [Fact]
        public void Given_ASingleClassRule_When_TheTableIsDerived_Then_TheClassRecordsTheDeclaredLonghand()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".bg-white { background-color: rgb(255, 255, 255); }"));

            // Act
            var properties = StyleTableTestHelper.Load(run).PropertiesOf("bg-white");

            // Assert
            Assert.Equal(new[] { "background-color" }, properties);
        }

        [Fact]
        public void Given_ARuleDeclaringSeveralProperties_When_TheTableIsDerived_Then_TheClassRecordsAllOfThem()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(@"
.truncate {
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
}"));

            // Act
            var properties = StyleTableTestHelper.Load(run).PropertiesOf("truncate");

            // Assert
            Assert.Equal(new[] { "overflow", "text-overflow", "white-space" }, properties);
        }

        [Fact]
        public void Given_AClassWithNoRule_When_TheTableIsQueried_Then_TheLookupMisses()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".bg-white { background-color: rgb(255, 255, 255); }"));

            // Act
            var defined = StyleTableTestHelper.Load(run).Defines("gap-4");

            // Assert
            Assert.False(defined);
        }

        [Theory]
        [InlineData("padding", "padding-bottom,padding-left,padding-right,padding-top")]
        [InlineData("margin", "margin-bottom,margin-left,margin-right,margin-top")]
        [InlineData("border-width", "border-bottom-width,border-left-width,border-right-width,border-top-width")]
        [InlineData("border-color", "border-bottom-color,border-left-color,border-right-color,border-top-color")]
        [InlineData("border-radius", "border-bottom-left-radius,border-bottom-right-radius,border-top-left-radius,border-top-right-radius")]
        [InlineData("background-position", "background-position-x,background-position-y")]
        public void Given_AShorthandDeclaration_When_TheTableIsDerived_Then_TheClassRecordsItsLonghands(
            string shorthand, string expectedLonghands)
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(".util { " + shorthand + ": 0; }"));

            // Act
            var properties = StyleTableTestHelper.Load(run).PropertiesOf("util");

            // Assert
            Assert.Equal(expectedLonghands.Split(','), properties);
        }

        [Fact]
        public void Given_AShorthandAndOneOfItsLonghands_When_TheTableIsDerived_Then_TheTwoClassesRecordACommonProperty()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(@"
.p-4 { padding: 16px; }
.pt-2 { padding-top: 8px; }"));
            var probe = StyleTableTestHelper.Load(run);

            // Act
            var shared = probe.PropertiesOf("p-4").Intersect(probe.PropertiesOf("pt-2"));

            // Assert
            Assert.Equal(new[] { "padding-top" }, shared);
        }

        [Fact]
        public void Given_ASelectorList_When_TheTableIsDerived_Then_EveryListedClassRecordsTheBlock()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(@"
.font-thin,
.font-extralight,
.font-light { -unity-font-style: normal; }"));
            var probe = StyleTableTestHelper.Load(run);

            // Act
            var recorded = new[] { "font-thin", "font-extralight", "font-light" }
                .SelectMany(probe.PropertiesOf)
                .Distinct();

            // Assert
            Assert.Equal(new[] { "-unity-font-style" }, recorded);
        }

        [Fact]
        public void Given_APseudoClassGatedRule_When_TheTableIsDerived_Then_TheClassRecordsItsProperties()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".hover-bg-primary:hover { background-color: rgb(0, 0, 0); }"));

            // Act
            var properties = StyleTableTestHelper.Load(run).PropertiesOf("hover-bg-primary");

            // Assert
            Assert.Equal(new[] { "background-color" }, properties);
        }

        [Theory]
        [InlineData("hover", "Hover")]
        [InlineData("active", "Active")]
        [InlineData("focus", "Focus")]
        [InlineData("disabled", "Disabled")]
        public void Given_APseudoClassGatedRule_When_TheTableIsDerived_Then_TheGateIsRecorded(
            string pseudoClass, string expectedGate)
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".util:" + pseudoClass + " { opacity: 0.5; }"));

            // Act
            var gate = StyleTableTestHelper.Load(run).GateOf("util");

            // Assert
            Assert.Equal(expectedGate, gate);
        }

        [Fact]
        public void Given_AnUngatedRule_When_TheTableIsDerived_Then_TheGateIsNone()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(".opacity-50 { opacity: 0.5; }"));

            // Act
            var gate = StyleTableTestHelper.Load(run).GateOf("opacity-50");

            // Assert
            Assert.Equal("None", gate);
        }

        [Fact]
        public void Given_ARuleCompoundedWithTheSelectedMarker_When_TheTableIsDerived_Then_TheUtilityCarriesTheSelectedGate()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(
                ".selected-border-strong.is-selected { border-color: rgb(0, 0, 0); }"));

            // Act
            var gate = StyleTableTestHelper.Load(run).GateOf("selected-border-strong");

            // Assert
            Assert.Equal("Selected", gate);
        }

        [Fact]
        public void Given_ARuleCompoundedWithTheSelectedMarker_When_TheTableIsDerived_Then_TheMarkerClassGetsNoEntry()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(
                ".selected-border-strong.is-selected { border-color: rgb(0, 0, 0); }"));

            // Act
            var defined = StyleTableTestHelper.Load(run).Defines("is-selected");

            // Assert
            Assert.False(defined);
        }

        [Fact]
        public void Given_ARootBlock_When_TheTableIsDerived_Then_ItContributesNoEntry()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(@"
:root { --space-4: 16px; }
.p-4 { padding: var(--space-4); }"));

            // Act
            var count = StyleTableTestHelper.Load(run).Count;

            // Assert
            Assert.Equal(1, count);
        }

        [Fact]
        public void Given_ARootBlock_When_ItDeclaresOnlyCustomProperties_Then_NothingIsReported()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(":root { --color-surface: rgb(1, 2, 3); --space-4: 16px; }"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Empty(codes);
        }

        [Fact]
        public void Given_ARootBlock_When_ItDeclaresARealProperty_Then_TheExclusionPremiseIsReported()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(":root { color: rgb(1, 2, 3); }"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.RootDeclaresNonCustomProperty }, codes);
        }

        [Fact]
        public void Given_ATypeKeyedRule_When_TheTableIsDerived_Then_ItContributesNoEntry()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss("ScrollView:focus { border-color: rgb(0, 120, 212); border-width: 1px; }"));

            // Act
            var count = StyleTableTestHelper.Load(run).Count;

            // Assert
            Assert.Equal(0, count);
        }

        [Fact]
        public void Given_ATypeKeyedRule_When_TheTableIsDerived_Then_NothingIsReported()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss("ScrollView:focus { border-color: rgb(0, 120, 212); }"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Empty(codes);
        }

        [Fact]
        public void Given_AnImportStatement_When_TheTableIsDerived_Then_NothingIsReported()
        {
            // Arrange — the partial an aggregator names, then the aggregator, which is where the importer
            // splices it: an imported sheet precedes the importing sheet's own rules.
            var run = StyleTableTestHelper.Derive(
                new StyleSheetInput("/styles/_tokens.uss", ".opacity-50 { opacity: 0.5; }"),
                new StyleSheetInput("/styles/StyleUtilities.uss", "@import url(\"_tokens.uss\");"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Empty(codes);
        }

        [Fact]
        public void Given_AnAggregatorSuppliedAheadOfItsImports_When_TheTableIsDerived_Then_TheOrderMismatchIsReported()
        {
            // Arrange — an aggregator's own rules outrank every partial's, so a first position would record it
            // as losing ties it wins.
            var run = StyleTableTestHelper.Derive(
                new StyleSheetInput(
                    "/styles/StyleUtilities.uss",
                    "@import url(\"_tokens.uss\");\n.opacity-75 { opacity: 0.75; }"),
                new StyleSheetInput("/styles/_tokens.uss", ".opacity-50 { opacity: 0.5; }"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.StyleSheetOrderMismatch }, codes);
        }

        [Fact]
        public void Given_APartialNoAggregatorImports_When_TheTableIsDerived_Then_TheOrderMismatchIsReported()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                new StyleSheetInput("/styles/_tokens.uss", ".opacity-50 { opacity: 0.5; }"),
                new StyleSheetInput("/styles/_stray.uss", ".opacity-25 { opacity: 0.25; }"),
                new StyleSheetInput("/styles/StyleUtilities.uss", "@import url(\"_tokens.uss\");"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.StyleSheetOrderMismatch }, codes);
        }

        [Fact]
        public void Given_PartialsSuppliedAgainstTheImportOrder_When_TheTableIsDerived_Then_TheOrderMismatchIsReported()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                new StyleSheetInput("/styles/_effects.uss", ".opacity-25 { opacity: 0.25; }"),
                new StyleSheetInput("/styles/_tokens.uss", ".opacity-50 { opacity: 0.5; }"),
                new StyleSheetInput(
                    "/styles/StyleUtilities.uss",
                    "@import url(\"_tokens.uss\");\n@import url(\"_effects.uss\");"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.StyleSheetOrderMismatch }, codes);
        }

        [Fact]
        public void Given_TwoUtilitiesSettingTransitionProperty_When_TheTableIsDerived_Then_TheLaterOneIsOrderedAfter()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(@"
.transition-transform { transition-property: translate, scale, rotate; }
.transition-colors { transition-property: color; }"));

            // Act
            var ordered = StyleTableTestHelper.Load(run).TransitionUtilitiesInCascadeOrder();

            // Assert
            Assert.Equal(new[] { "transition-transform", "transition-colors" }, ordered);
        }

        [Fact]
        public void Given_AClassSettingTransitionPropertyTwice_When_TheTableIsDerived_Then_TheLaterValueWins()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(
                ".util { transition-property: opacity; transition-property: scale; }"));

            // Act
            var properties = StyleTableTestHelper.Load(run).TransitionPropertiesOf("util");

            // Assert
            Assert.Equal(new[] { "scale" }, properties);
        }

        [Fact]
        public void Given_ATransitionPropertyNamingAShorthand_When_TheTableIsDerived_Then_ItExpandsToTheLonghands()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".util { transition-property: border-color; }"));

            // Act
            var properties = StyleTableTestHelper.Load(run).TransitionPropertiesOf("util");

            // Assert
            Assert.Equal(
                new[]
                {
                    "border-bottom-color", "border-left-color", "border-right-color", "border-top-color",
                },
                properties);
        }

        [Fact]
        public void Given_TransitionPropertyNone_When_TheTableIsDerived_Then_ItNamesNoProperty()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".util { transition-property: none; }"));

            // Act
            var properties = StyleTableTestHelper.Load(run).TransitionPropertiesOf("util");

            // Assert
            Assert.Empty(properties);
        }

        [Fact]
        public void Given_TransitionPropertyAll_When_TheTableIsDerived_Then_ItNamesEveryLonghand()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".util { transition-property: all; }"));
            var probe = StyleTableTestHelper.Load(run);

            // Act
            var covered = probe.TransitionPropertiesOf("util").Count;

            // Assert
            Assert.Equal(probe.LonghandCount, covered);
        }

        [Fact]
        public void Given_AGatedTransitionPropertyDeclaration_When_TheTableIsDerived_Then_ItIsReported()
        {
            // Arrange — skipping it silently would leave the guard answering from the ungated declarations for
            // an element whose gated rule had already replaced them.
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".util:hover { transition-property: opacity; }"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.GatedTransitionProperty }, codes);
        }

        [Fact]
        public void Given_ATransitionPropertyValueCarryingAComment_When_TheTableIsDerived_Then_TheCommentIsNotAToken()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".util { transition-property: opacity /* the fade */; }"));

            // Act
            var properties = StyleTableTestHelper.Load(run).TransitionPropertiesOf("util");

            // Assert
            Assert.Equal(new[] { "opacity" }, properties);
        }

        [Fact]
        public void Given_ATransitionPropertyNamingAnUnknownProperty_When_TheTableIsDerived_Then_ItIsReported()
        {
            // Arrange — `transform` is a CSS property UI Toolkit has no storage for; a list naming it
            // transitions nothing.
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".util { transition-property: transform; }"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.UnknownTransitionProperty }, codes);
        }

        [Theory]
        [InlineData(".card .title { color: rgb(0, 0, 0); }")]
        [InlineData(".card > .title { color: rgb(0, 0, 0); }")]
        [InlineData("#card { color: rgb(0, 0, 0); }")]
        [InlineData("* { color: rgb(0, 0, 0); }")]
        [InlineData(".card:first-child { color: rgb(0, 0, 0); }")]
        [InlineData(".card.title { color: rgb(0, 0, 0); }")]
        [InlineData("Button.card { color: rgb(0, 0, 0); }")]
        [InlineData("@media (max-width: 100px) { }")]
        public void Given_AnUnmodelledUssConstruct_When_TheTableIsDerived_Then_ItIsReported(string sheet)
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(sheet));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.UnsupportedConstruct }, codes);
        }

        [Fact]
        public void Given_AnUnmodelledSelector_When_TheTableIsDerived_Then_NoTableIsEmitted()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".card .title { color: rgb(0, 0, 0); }"));

            // Act
            var emitted = run.EmittedSource;

            // Assert
            Assert.Null(emitted);
        }

        [Fact]
        public void Given_APropertyOutsideTheUiToolkitVocabulary_When_TheTableIsDerived_Then_ItIsReported()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".util { box-shadow: 0 0 4px black; }"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.UnknownProperty }, codes);
        }

        [Fact]
        public void Given_TheAllProperty_When_TheTableIsDerived_Then_ItIsReportedRatherThanExpanded()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(".util { all: initial; }"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.UnknownProperty }, codes);
        }

        [Fact]
        public void Given_AUtilityDeclaringACustomProperty_When_TheTableIsDerived_Then_ItIsReported()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss(".theme-dark { --color-surface: rgb(1, 2, 3); }"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.UtilityDeclaresCustomProperty }, codes);
        }

        [Fact]
        public void Given_AClassDefinedBothGatedAndUngated_When_TheTableIsDerived_Then_ItIsReported()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(@"
.util { color: rgb(0, 0, 0); }
.util:hover { color: rgb(1, 1, 1); }"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.ClassSpansMultipleGates }, codes);
        }

        [Fact]
        public void Given_AnUnterminatedRuleBlock_When_TheTableIsDerived_Then_ItIsReported()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(".util { color: rgb(0, 0, 0);"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.MalformedUss }, codes);
        }

        [Fact]
        public void Given_ADeclarationThatIsNotANameValuePair_When_TheTableIsDerived_Then_ItIsReported()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(".util { color rgb(0, 0, 0); }"));

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.MalformedUss }, codes);
        }

        [Fact]
        public void Given_NoStyleSheet_When_TheTableIsDerived_Then_TheEmptinessIsReported()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive();

            // Act
            var codes = run.ProblemCodes;

            // Assert
            Assert.Equal(new[] { UssProblemCode.NoStyleSheets }, codes);
        }

        [Fact]
        public void Given_AProblemInAStyleSheet_When_ItIsReported_Then_TheMessageLocatesTheOffendingRule()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(new StyleSheetInput("/styles/_x.uss", @".ok { opacity: 0.5; }
.card .title { color: rgb(0, 0, 0); }"));

            // Act
            var rendered = run.Problems.Single().ToString();

            // Assert
            Assert.StartsWith("/styles/_x.uss(2,1): USS002:", rendered);
        }

        [Fact]
        public void Given_CommentsBetweenRules_When_TheTableIsDerived_Then_TheyAreNotMistakenForDeclarations()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(@"
/* Opacity utilities */
.opacity-50 { /* half */ opacity: 0.5; }
/* trailing */"));

            // Act
            var properties = StyleTableTestHelper.Load(run).PropertiesOf("opacity-50");

            // Assert
            Assert.Equal(new[] { "opacity" }, properties);
        }

        [Fact]
        public void Given_ADeclarationWhoseValueContainsCommas_When_TheTableIsDerived_Then_ItIsReadAsOneDeclaration()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(
                ".transition-colors { transition-property: background-color, border-color, color; }"));

            // Act
            var properties = StyleTableTestHelper.Load(run).PropertiesOf("transition-colors");

            // Assert
            Assert.Equal(new[] { "transition-property" }, properties);
        }

        [Fact]
        public void Given_ACommentOnlyStyleSheet_When_TheTableIsDerived_Then_ItContributesNoEntry()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(
                StyleSheetInput.Uss("/* Marker classes only - no style rules. */"));

            // Act
            var count = StyleTableTestHelper.Load(run).Count;

            // Assert
            Assert.Equal(0, count);
        }

        [Fact]
        public void Given_TheLonghandVocabulary_When_TheTableIsEmitted_Then_ItFitsThePropertySetCapacity()
        {
            // Arrange
            var run = StyleTableTestHelper.Derive(StyleSheetInput.Uss(".opacity-50 { opacity: 0.5; }"));

            // Act
            var probe = StyleTableTestHelper.Load(run);

            // Assert
            Assert.True(
                probe.LonghandCount <= StyleUtilityTableBuilder.PropertySetCapacity,
                $"The vocabulary holds {probe.LonghandCount} longhands but a property set holds " +
                $"{StyleUtilityTableBuilder.PropertySetCapacity}.");
        }
    }
}
