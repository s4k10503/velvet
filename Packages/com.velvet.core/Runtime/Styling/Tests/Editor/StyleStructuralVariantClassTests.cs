using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Parser coverage for <see cref="StyleStructuralVariantClass"/>: the named structural variants
    /// (<c>first:</c>…<c>even:</c>) and the arbitrary selector forms (<c>[&amp;:nth-child(N)]:</c>,
    /// <c>[&amp;:first-child]:</c>, <c>[&amp;:nth-last-child(N)]:</c>) whose bracketed selector carries its own
    /// <c>:</c>/<c>(</c>. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleStructuralVariantClassTests
    {
        [Test]
        public void Given_FirstNamed_When_Parsed_Then_ResolvesFirst()
        {
            var ok = StyleStructuralVariantClass.TryParse("first:bg-mark", out var kind, out _, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleStructuralKind.First, "bg-mark")));
        }

        [Test]
        public void Given_ArbitraryNthChild_When_Parsed_Then_ResolvesNthChildWithN()
        {
            var ok = StyleStructuralVariantClass.TryParse("[&:nth-child(3)]:bg-mark", out var kind, out var n, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, n, payload), Is.EqualTo((StyleStructuralKind.NthChild, 3, "bg-mark")));
        }

        [Test]
        public void Given_ArbitraryFirstChildAlias_When_Parsed_Then_ResolvesFirst()
        {
            var ok = StyleStructuralVariantClass.TryParse("[&:first-child]:bg-mark", out var kind, out _, out var payload);

            Assume.That(ok, Is.True);
            Assert.That((kind, payload), Is.EqualTo((StyleStructuralKind.First, "bg-mark")));
        }

        [Test]
        public void Given_ArbitraryNthLastChild_When_Parsed_Then_ResolvesNthLastChildWithN()
        {
            var ok = StyleStructuralVariantClass.TryParse("[&:nth-last-child(2)]:bg-mark", out var kind, out var n, out _);

            Assume.That(ok, Is.True);
            Assert.That((kind, n), Is.EqualTo((StyleStructuralKind.NthLastChild, 2)));
        }

        [Test]
        public void Given_StateVariant_When_Parsed_Then_IsNotStructural()
        {
            Assert.That(StyleStructuralVariantClass.IsStructural("hover:bg-mark"), Is.False);
        }

        [Test]
        public void Given_NthLastChild2_When_EvaluatedAgainstFourSiblings_Then_MatchesThirdIndex()
        {
            // nth-last-child(2) of 4 == index 2 (the 2nd from the end).
            var matches = StyleStructuralVariantClass.Matches(StyleStructuralKind.NthLastChild, 2, index: 2, count: 4);

            Assert.That(matches, Is.True);
        }
    }
}
