using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the readers a patch hands each class of a list allocate: nothing for a class their family
    /// cannot claim, and, where no sheet declares the theme variable they look up, nothing more inside a mount's theme
    /// scope than outside it. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class ClassReadAllocationTests
    {
        // A variant whose bracket opens with the shape of a type hint, as has-[:checked]: and has-[:focus]: do.
        private const string BracketedVariant = "has-[:checked]:bg-red-500";

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
    }
}
