using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the readers a patch hands each class of a list allocate for the classes named below: nothing
    /// where no part of the class has to be cut out as a string, one string where the utility after a variant has to
    /// be handed on by name, to a theme lookup or as a variant parse's payload, and, where no sheet declares the theme
    /// variable a reader looks up, nothing more inside a mount's theme scope than outside it. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class ClassReadAllocationTests
    {
        // A variant whose bracket opens with the shape of a type hint, as has-[:checked]: and has-[:focus]: do.
        private const string BracketedVariant = "has-[:checked]:bg-red-500";

        // State, responsive, theme and relational variants over utilities of none of the families the readers below
        // look for.
        private static readonly string[] VariantClasses = { "hover:bg-red-500", "sm:p-2", "dark:bg-red-500", "group-hover:opacity-50" };

        private MountedTree _mounted;

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        private static int WarmBlocks(Action once)
        {
            for (var i = 0; i < 64; i++)
            {
                once();
            }
            return GCAllocationProbe.MedianBlocksDuring(once);
        }

        // The blocks once allocates while a mount's scope is open, or -1 where the arrangement left no scope open,
        // which no count can equal.
        private int WarmBlocksInScope(Action once)
        {
            _mounted = V.Mount(new VisualElement(), V.Div());
            using (StyleThemeTokens.Use(_mounted.Root.Reconciler.Context))
            {
                return StyleThemeTokens.Current == null ? -1 : WarmBlocks(once);
            }
        }

        // The blocks once allocates with no scope open, or -2 where one is, which neither a count nor
        // WarmBlocksInScope's -1 can equal.
        private static int WarmBlocksOutsideScope(Action once)
            => StyleThemeTokens.Current == null ? WarmBlocks(once) : -2;

        // A probe stuck at zero satisfies the zero guards below without measuring anything; this canary is what makes
        // their zeros mean something.
        // GREEN_ON_BASE(characterization): the probe already counts a 16-byte array allocation on the base, which is all this case asserts.
        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Act
            var blocks = GCAllocationCanary.BlocksForKnownArray();

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        public void Given_ADashedClassNoThemeFamilyOpens_When_AskedWhetherItMayBecomeAThemeClass_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleThemeClass.MayBecomeThemeClass("flex-row"));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_ANegativeThemeFamilyClass_When_AskedWhetherItMayBecomeAThemeClass_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleThemeClass.MayBecomeThemeClass("-mt-2"));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base turns a bracketed class away before it reads anything else, and allocates nothing there.
        [Test]
        public void Given_AnArbitraryVariantOverAThemeFamilyClass_When_AskedWhetherItMayBecomeAThemeClass_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleThemeClass.MayBecomeThemeClass("[&>*]:mt-2"));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AVariantOverAClassNoThemeFamilyOpens_When_AskedWhetherItMayBecomeAThemeClass_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleThemeClass.MayBecomeThemeClass("hover:flex-row"));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        // The utility after the variant is looked up by name, which takes it as a string once.
        [Test]
        public void Given_AVariantOverAThemeFamilyClass_When_AskedWhetherItMayBecomeAThemeClass_Then_OneBlockIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleThemeClass.MayBecomeThemeClass("hover:text-red-500"));

            // Assert
            Assert.That(blocks, Is.EqualTo(1));
        }

        [Test]
        public void Given_VariantClassesNamingNoTimingKeyword_When_TheirEaseCurveIsNoted_Then_NothingIsAllocated()
        {
            // Arrange
            var element = new VisualElement();
            var classNames = new[] { "hover:bg-red-500", "sm:p-2", "dark:bg-red-500", "group-hover:opacity-50" };

            // Act
            var blocks = WarmBlocks(() => StyleEaseCurve.NoteClasses(element, classNames));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AStateVariantClass_When_AskedWhetherItIsAVariant_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleVariantClass.IsVariant("hover:bg-red-500"));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AStateVariantClass_When_ParsedAsAVariant_Then_ItsPayloadIsTheOneBlockAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleVariantClass.TryParse("hover:bg-red-500", out _, out _));

            // Assert
            Assert.That(blocks, Is.EqualTo(1));
        }

        [TestCase("first:p-2")]
        [TestCase("max-3xl:p-2")]
        public void Given_NoThemeScope_When_AClassBehindAPrefixNoVariantNamesIsAskedWhetherItIsAVariant_Then_NothingIsAllocated(
            string cls)
        {
            // Act
            var blocks = WarmBlocksOutsideScope(() => StyleVariantClass.IsVariant(cls));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_VariantClassesNamingNoFilterTransition_When_ReadForOne_Then_NothingIsAllocated()
        {
            // Arrange
            var read = (Func<string[], bool>)Delegate.CreateDelegate(typeof(Func<string[], bool>),
                typeof(FiberFilterTransitionApplier).GetMethod("HasFilterTransitionClass",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));

            // Act
            var blocks = WarmBlocks(() => read(VariantClasses));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_VariantClassesNamingNoClipPath_When_AskedWhetherTheyWantAClipWrapper_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleClipPathClass.WantsClipWrapper(VariantClasses));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_VariantClassesNamingNoFilter_When_AskedWhetherTheyHoldAFilter_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleFilterValueParser.HasFilterClass(VariantClasses));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        // Measured against the filter read it opens with, so this answers for its own animate-hue read alone.
        [Test]
        public void Given_VariantClassesNamingNoFilter_When_AskedWhetherTheyCarryOne_Then_ItAllocatesWhatTheFilterReadDoes()
        {
            // Arrange
            var filterRead = WarmBlocks(() => StyleFilterValueParser.HasFilterClass(VariantClasses));

            // Act
            var blocks = WarmBlocks(() => WrapperInfrastructure.CarriesFilter(VariantClasses));

            // Assert
            Assert.That(blocks, Is.EqualTo(filterRead));
        }

        [Test]
        public void Given_VariantClassesNamingNoBorder_When_TheirBorderInsetIsRead_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => SilhouetteBoundsSpacer.BorderInset(VariantClasses, out _, out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_ABracketedValueWithNoTypeHint_When_ReadAsAColour_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleColorValueParser.TryParseColorPrefix("w-", "120px".AsSpan(), false, null, out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_ABracketedVariant_When_ParsedAsAGap_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleGapClass.TryParse(BracketedVariant, out _, out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_ABracketedVariant_When_ParsedAsASkew_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleSkewClass.TryParse(BracketedVariant, out _, out _, out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_ANegativeClassOfAnotherFamily_When_ParsedAsASkew_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleSkewClass.TryParse("-mt-2", out _, out _, out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_ABracketedVariant_When_ParsedAsALineHeight_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(() => StyleTextEffectClass.TryParseLeading(BracketedVariant, out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AMountScope_When_ADefaultBreakpointIsRead_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocksInScope(() => StyleVariantClass.BreakpointPx(StyleVariantKind.Sm));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AMountScope_When_ABreakpointOnlyASheetCanDeclareIsRead_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocksInScope(() => StyleContainerQuery.TryBreakpointPx("3xl", out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AMountScope_When_AContainerQueryIsParsed_Then_ItAllocatesWhatItDoesOutsideOne()
        {
            // Arrange
            void Parse() => StyleContainerQuery.TryParse("@md", out _);
            var outside = WarmBlocksOutsideScope(Parse);

            // Act
            var inside = WarmBlocksInScope(Parse);

            // Assert
            Assert.That(inside, Is.EqualTo(outside));
        }

        [Test]
        public void Given_AMountScope_When_AnAnimateClassIsExtracted_Then_ItAllocatesWhatItDoesOutsideOne()
        {
            // Arrange
            var classNames = new[] { "animate-spin" };
            void Extract() => StyleAnimateClass.TryExtract(classNames, out _);
            var outside = WarmBlocksOutsideScope(Extract);

            // Act
            var inside = WarmBlocksInScope(Extract);

            // Assert
            Assert.That(inside, Is.EqualTo(outside));
        }

        // The mount's sheets declare no breakpoint, so neither prefix is one TryBreakpointQuery could accept there.
        [TestCase("first:p-2")]
        [TestCase("max-3xl:p-2")]
        public void Given_AMountScope_When_AClassBehindAPrefixNoBreakpointNamesIsAskedWhetherItIsAVariant_Then_NothingIsAllocated(
            string cls)
        {
            // Act
            var blocks = WarmBlocksInScope(() => StyleVariantClass.IsVariant(cls));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AScopeDeclaringALength_When_ItsPixelsAreRead_Then_ItAllocatesWhatReadingTheDeclaredTextDoes()
        {
            // Arrange
            var scope = new StyleThemeTokens.Scope();
            scope.Values["--breakpoint-3xl"] = "120rem";
            var text = WarmBlocks(() => StyleArbitraryValueResolver.TryParsePixels("120rem".AsSpan(), out _));

            // Act
            var read = WarmBlocks(() => scope.TryGetPx("--breakpoint-3xl", out _));

            // Assert
            Assert.That(read, Is.EqualTo(text));
        }

        [Test]
        public void Given_AScopeDeclaringALengthOnce_When_ItsSinglePixelsAreRead_Then_ItAllocatesWhatReadingTheDeclaredTextDoes()
        {
            // Arrange
            var scope = new StyleThemeTokens.Scope();
            scope.Values["--spacing"] = "0.25rem";
            scope.Counts["--spacing"] = 1;
            scope.RootCounts["--spacing"] = 1;
            var text = WarmBlocks(() => StyleArbitraryValueResolver.TryParsePixels("0.25rem".AsSpan(), out _));

            // Act
            var read = WarmBlocks(() => scope.TryGetSinglePx("--spacing", out _));

            // Assert
            Assert.That(read, Is.EqualTo(text));
        }

        [Test]
        public void Given_AnImportantClassOfAnotherFamily_When_AClipPathIsExtracted_Then_NothingIsAllocated()
        {
            // Arrange
            var classNames = new[] { "!bg-red-500" };

            // Act
            var blocks = WarmBlocks(() => StyleClipPathClass.TryExtract(classNames, out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AnImportantClassOfNoThemeFamily_When_AskedWhetherItMayDeclareVariantState_Then_NothingIsAllocated()
        {
            // Arrange
            var mayDeclare = (Func<string[], bool>)Delegate.CreateDelegate(typeof(Func<string[], bool>),
                typeof(FiberNodePatcher).GetMethod("MayDeclareVariantState",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
            var classNames = new[] { "!flex" };

            // Act
            var blocks = WarmBlocks(() => mayDeclare(classNames));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AnImportantClassOfNoThemeFamily_When_AskedWhetherItDeclaresAGatePayload_Then_NothingIsAllocated()
        {
            // Arrange
            var classNames = new[] { "!flex" };

            // Act
            var blocks = WarmBlocks(() => StyleVariantPayload.DeclaresGatePayload(classNames));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AnImportantClassOfNoThemeFamily_When_ItsCustomPropertyTokensAreConfigured_Then_NothingIsAllocated()
        {
            // Arrange
            _mounted = V.Mount(new VisualElement(), V.Div());
            var configure = (Action<VisualElement, string[]>)Delegate.CreateDelegate(typeof(Action<VisualElement, string[]>),
                new FiberNodePatcher(_mounted.Root.Reconciler.Context),
                typeof(FiberNodePatcher).GetMethod("ApplyCustomPropertyConfig",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
            var element = new VisualElement();
            var classNames = new[] { "!flex" };

            // Act
            var blocks = WarmBlocks(() => configure(element, classNames));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        // Measured against the same class without its bang, so this answers for the important class alone.
        [Test]
        public void Given_AnImportantClassOfNoThemeFamily_When_TheElementIsTrackedForThemeClasses_Then_ItAllocatesWhatThePlainClassDoes()
        {
            // Arrange
            _mounted = V.Mount(new VisualElement(), V.Div());
            var track = (Action<VisualElement, string[]>)Delegate.CreateDelegate(typeof(Action<VisualElement, string[]>),
                new FiberNodePatcher(_mounted.Root.Reconciler.Context),
                typeof(FiberNodePatcher).GetMethod("TrackThemed",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
            var element = new VisualElement();
            var plain = new[] { "flex" };
            var important = new[] { "!flex" };
            var plainBlocks = WarmBlocks(() => track(element, plain));

            // Act
            var importantBlocks = WarmBlocks(() => track(element, important));

            // Assert
            Assert.That(importantBlocks, Is.EqualTo(plainBlocks));
        }
    }
}
