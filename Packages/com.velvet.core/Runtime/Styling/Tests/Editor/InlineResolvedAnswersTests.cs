using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <see cref="StyleArbitraryValueResolver.IsInlineResolved"/> answers a token the same way
    /// every time it is asked, whichever kind of token it remembers: a plain class answers false and a token
    /// resolved to inline style answers true, the second time as the first.
    /// </summary>
    [TestFixture]
    internal sealed class InlineResolvedAnswersTests
    {
        // GREEN_ON_BASE(characterization): the base answers each of these tokens the same on every ask, since it
        // remembers nothing; this holds the remembered answers to that.
        [Test]
        public void Given_PlainAndInlineResolvedTokens_When_EachIsAskedTwice_Then_EachAnswersTheSameBothTimes()
        {
            // Arrange
            var tokens = new Dictionary<string, bool>
            {
                ["flex"] = false,
                ["flex-row"] = false,
                ["bg-red-500"] = false,
                ["mt-2"] = false,
                ["w-[10px]"] = true,
                ["bg-black/50"] = true,
                ["-mt-2"] = true,
                ["mt-13"] = true,
                ["ms-4"] = true,
                ["translate-x-1/2"] = true,
            };

            // Act
            var asked = tokens.Keys
                .Select(token => $"{token}={StyleArbitraryValueResolver.IsInlineResolved(token)}")
                .Concat(tokens.Keys.Select(token => $"{token}={StyleArbitraryValueResolver.IsInlineResolved(token)}"))
                .ToArray();

            // Assert
            var expected = tokens.Select(pair => $"{pair.Key}={pair.Value}").ToArray();
            Assert.That(asked, Is.EqualTo(expected.Concat(expected).ToArray()));
        }
    }
}
