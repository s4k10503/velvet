using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <see cref="StyleVariantClass"/> reads a container query token (<c>@md:p-4</c>): the kind it
    /// is, the payload under it, the prefix it hands back so a stacked variant can tell one query from another,
    /// and the tokens that stay ordinary classes.
    /// </summary>
    [TestFixture]
    internal sealed class ContainerVariantParseTests
    {
        [TestCase("@md:p-4", "@md", "p-4")]
        [TestCase("@max-lg:p-4", "@max-lg", "p-4")]
        [TestCase("@md/main:p-4", "@md/main", "p-4")]
        [TestCase("@min-[475px]:p-4", "@min-[475px]", "p-4")]
        [TestCase("@[40rem]:p-4", "@[40rem]", "p-4")]
        [TestCase("@md:hover:bg-red-500", "@md", "hover:bg-red-500")]
        [TestCase("@md:w-[10px]", "@md", "w-[10px]")]
        [TestCase("@md:bg-[addr:key]", "@md", "bg-[addr:key]")]
        public void Given_AContainerToken_When_Parsed_Then_ItIsAContainerKindWithItsPrefixAndPayload(
            string token, string expectedPrefix, string expectedPayload)
        {
            // Act
            var parsed = StyleVariantClass.TryParse(token, out var kind, out var prefix, out var payload);

            // Assert
            Assert.That((parsed, kind, prefix, payload),
                Is.EqualTo((true, StyleVariantKind.Container, expectedPrefix, expectedPayload)));
        }

        [Test]
        public void Given_AContainerToken_When_ParsedThroughThePublicOverload_Then_ItIsAContainerKindWithItsPayload()
        {
            // Act
            var parsed = StyleVariantClass.TryParse("@md/main:p-4", out var kind, out var payload);

            // Assert
            Assert.That((parsed, kind, payload), Is.EqualTo((true, StyleVariantKind.Container, "p-4")));
        }

        [Test]
        public void Given_ACalcThreshold_When_Parsed_Then_ItIsAContainerKindWithItsPrefix()
        {
            // Act
            var parsed = StyleVariantClass.TryParse("@min-[calc(10rem+2px)]:p-4", out var kind, out var prefix,
                out var payload);

            // Assert
            Assert.That((parsed, kind, prefix, payload),
                Is.EqualTo((true, StyleVariantKind.Container, "@min-[calc(10rem+2px)]", "p-4")));
        }

        [TestCase("@container")]
        [TestCase("@container/main")]
        [TestCase("@md")]
        [TestCase("@md:")]
        [TestCase("@:p-4")]
        [TestCase("@foo:p-4")]
        [TestCase("@8xl:p-4")]
        [TestCase("@container:p-4")]
        [TestCase("@md/:p-4")]
        [TestCase("@min-[5px:p-4")]
        [TestCase("@min-[a:b]:p-4")]
        [TestCase("@md//a:p-4")]
        public void Given_ATokenThatIsNotAContainerQuery_When_Parsed_Then_ItStaysAnOrdinaryClass(string token)
        {
            // Act
            var isVariant = StyleVariantClass.IsVariant(token);

            // Assert
            Assert.That(isVariant, Is.False);
        }

        [Test]
        public void Given_AContainerToken_When_AskedWhetherItIsAVariant_Then_ItIs()
        {
            // Act
            var isVariant = StyleVariantClass.IsVariant("@sm:p-4");

            // Assert
            Assert.That(isVariant, Is.True);
        }

        [Test]
        public void Given_TheContainerKind_When_AskedForABreakpoint_Then_ItHasNoneAndIsNotResponsive()
        {
            // Act
            var answers = (StyleVariantClass.BreakpointPx(StyleVariantKind.Container),
                StyleVariantClass.IsResponsive(StyleVariantKind.Container));

            // Assert
            Assert.That(answers, Is.EqualTo((0f, false)));
        }

        [Test]
        public void Given_TheContainerKind_When_AskedWhetherItIsRelational_Then_ItIsNot()
        {
            // Act
            var relational = StyleVariantClass.RelationalOf(StyleVariantKind.Container);

            // Assert
            Assert.That(relational, Is.Null);
        }
    }
}
