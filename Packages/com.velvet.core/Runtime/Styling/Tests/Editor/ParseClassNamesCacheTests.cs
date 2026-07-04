using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the caching contract of class-name parsing.
    /// <list type="bullet">
    /// <item>Parsing the same string twice returns the identical cached array instance.</item>
    /// <item>Parsing different strings returns distinct array instances.</item>
    /// <item>A null or empty string returns the shared empty array.</item>
    /// <item>A cached array holds the space-split tokens of its key.</item>
    /// <item>When the cache reaches its size bound the next distinct key logs a warning and clears the cache,
    /// then caches the triggering key, so previously cached keys re-parse to fresh instances afterward.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The cache is process-wide static, so <see cref="SetUp"/> drains it via
    /// <c>V.ClearClassNameCacheForTesting()</c> to keep other fixtures' entries from pushing past the bound.
    /// </remarks>
    [TestFixture]
    internal sealed class ParseClassNamesCacheTests
    {
        [SetUp]
        public void SetUp()
        {
            V.ClearClassNameCacheForTesting();
        }

        #region Cache behavior

        [Test]
        public void Given_SameString_When_ParsedTwice_Then_ReturnsSameArrayInstance()
        {
            // Arrange
            var first = V.ParseClassNames("btn btn--active");

            // Act
            var second = V.ParseClassNames("btn btn--active");

            // Assert
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void Given_DifferentStrings_When_Parsed_Then_ReturnDistinctArrays()
        {
            // Arrange
            var a = V.ParseClassNames("btn");

            // Act
            var b = V.ParseClassNames("label");

            // Assert
            Assert.That(b, Is.Not.SameAs(a));
        }

        [Test]
        public void Given_NullString_When_Parsed_Then_ReturnsEmptyArray()
        {
            // Act
            var result = V.ParseClassNames(null);

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Given_EmptyString_When_Parsed_Then_ReturnsEmptyArray()
        {
            // Act
            var result = V.ParseClassNames("");

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Given_MultiTokenString_When_Parsed_Then_CachedArrayHoldsSpaceSplitTokens()
        {
            // Act
            var result = V.ParseClassNames("card card--highlighted");

            // Assert
            Assert.That(result, Is.EqualTo(new[] { "card", "card--highlighted" }));
        }

        [Test]
        public void Given_AlreadyParsedString_When_ParsedAgain_Then_ReturnsSameCachedInstance()
        {
            // Arrange
            var result = V.ParseClassNames("card card--highlighted");

            // Act
            var cached = V.ParseClassNames("card card--highlighted");

            // Assert
            Assert.That(cached, Is.SameAs(result));
        }

        #endregion

        #region Cache bound

        [Test]
        public void Given_CacheAtSizeBound_When_DistinctKeyParsed_Then_TriggeringKeyStillSplitsCorrectly()
        {
            // Arrange — fill the cache to its bound, one entry per Parse call
            for (var i = 0; i < V.MaxClassNameCacheSize; i++)
            {
                _ = V.ParseClassNames($"fill-class-{i}");
            }
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("ParseClassNames cache exceeded limit"));

            // Act — this distinct key overflows the cache
            var triggering = V.ParseClassNames("overflow-trigger");

            // Assert
            Assert.That(triggering, Is.EqualTo(new[] { "overflow-trigger" }));
        }

        [Test]
        public void Given_CacheAtSizeBound_When_DistinctKeyParsed_Then_PreviouslyCachedKeyReParsesFresh()
        {
            // Arrange — fill the cache to its bound and capture an existing key's cached instance
            for (var i = 0; i < V.MaxClassNameCacheSize; i++)
            {
                _ = V.ParseClassNames($"fill-class-{i}");
            }
            var firstBeforeOverflow = V.ParseClassNames("fill-class-0");
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("ParseClassNames cache exceeded limit"));

            // Act — overflow clears the cache, then re-parse the previously cached key
            _ = V.ParseClassNames("overflow-trigger");
            var firstAfterOverflow = V.ParseClassNames("fill-class-0");

            // Assert
            Assert.That(firstAfterOverflow, Is.Not.SameAs(firstBeforeOverflow));
        }

        #endregion
    }
}
