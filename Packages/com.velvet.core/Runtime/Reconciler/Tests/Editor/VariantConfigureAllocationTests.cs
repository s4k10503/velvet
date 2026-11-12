using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the readers configuring an element's variant manipulators cut no payload out of a variant class of
    /// a kind other than the one each looks for, a container query among them, and that the container reader parses a
    /// container query once, since a configure asks each of them of every class of an element carrying variant classes.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class VariantConfigureAllocationTests
    {
        private delegate string[] ExtractVariant(string[] classNames, StyleVariantKind kind, out int[] declarations);

        private delegate bool ReadContainerToken(string token, out string spelling, out StyleContainerQuery query,
            out string payload);

        // Keyword variants of state and responsive kinds, none of them stacked, a container query or relational.
        private static readonly string[] s_classNames = { "hover:bg-red-500", "focus:bg-red-400", "sm:p-2", "dark:bg-red-500" };

        private static T Private<T>(string name) where T : Delegate
            => (T)Delegate.CreateDelegate(typeof(T),
                typeof(FiberNodePatcher).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        private static int WarmBlocks(Action once)
        {
            for (var i = 0; i < 64; i++)
            {
                once();
            }
            return GCAllocationProbe.MedianBlocksDuring(once);
        }

        // A probe stuck at zero satisfies every guard below without measuring anything; this canary is what makes
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
        public void Given_VariantClassesOfOtherKinds_When_ThePayloadsOfOneKindAreExtracted_Then_NothingIsAllocated()
        {
            // Arrange
            var extract = Private<ExtractVariant>("ExtractVariant");

            // Act
            var blocks = WarmBlocks(() => extract(s_classNames, StyleVariantKind.Active, out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_VariantClassesOfOneLayer_When_AskedWhetherOneStacksAnElementLocalInner_Then_NothingIsAllocated()
        {
            // Arrange
            var stacks = Private<Func<string[], bool>>("StacksElementLocalInner");

            // Act
            var blocks = WarmBlocks(() => stacks(s_classNames));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_VariantClassesNamingNoContainerQuery_When_TheirContainerBindingsAreBuilt_Then_NothingIsAllocated()
        {
            // Arrange
            var build = Private<Func<string[], ContainerBinding[]>>("BuildContainerBindings");

            // Act
            var blocks = WarmBlocks(() => build(s_classNames));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_VariantClassesNamingNoGroupOrPeer_When_TheirRelationalConfigsAreBuilt_Then_NothingIsAllocated()
        {
            // Arrange
            var build = Private<Func<string[], List<RelationalBindingConfig>>>("BuildRelationalConfigs");

            // Act
            var blocks = WarmBlocks(() => build(s_classNames));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        // A container query is validated whoever reads it, through its prefix cut out as a string, so this answers for its
        // payload alone.
        [Test]
        public void Given_AContainerQueryClass_When_ThePayloadsOfAStateKindAreExtracted_Then_ItAllocatesWhatValidatingItsQueryDoes()
        {
            // Arrange
            var extract = Private<ExtractVariant>("ExtractVariant");
            const string cls = "@md:p-2";
            var classNames = new[] { cls };
            var validated = WarmBlocks(() => StyleContainerQuery.TryParse(cls.Substring(0, cls.IndexOf(':')), out _));

            // Act
            var blocks = WarmBlocks(() => extract(classNames, StyleVariantKind.Hover, out _));

            // Assert
            Assert.That(blocks, Is.EqualTo(validated));
        }

        // GREEN_ON_BASE(characterization): the base parses a container query once to read its binding, as this asserts.
        [TestCase("@md:p-2", "@md")]
        [TestCase("max-md:p-2", "@max-md/*viewport")]
        [TestCase("max-[600px]:p-2", "@max-[600px]/*viewport")]
        public void Given_AContainerQueryClass_When_ItsBindingIsRead_Then_ItAllocatesWhatOneParseAndItsQueryDo(
            string cls, string spelling)
        {
            // Arrange
            var read = Private<ReadContainerToken>("TryReadContainerToken");
            var once = WarmBlocks(() =>
            {
                StyleVariantClass.TryParse(cls, out _, out _, out _);
                StyleContainerQuery.TryParse(spelling, out _);
            });

            // Act
            var blocks = WarmBlocks(() => read(cls, out _, out _, out _));

            // Assert
            Assert.That(blocks, Is.EqualTo(once));
        }
    }
}
