using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when <see cref="ClassNameParseCache"/> caches a string, how long it keeps it, what it holds
    /// while strings it never caches pass through, and what caching an entry allocates. The caching contract
    /// as callers see it is <c>ParseClassNamesCacheTests</c>'.
    /// </summary>
    [TestFixture]
    internal sealed class ClassNameParseCacheTests
    {
        private const int Window = ClassNameParseCache.FirstSightingsPerWindow;
        private const int Probation = ClassNameParseCache.ProbationGenerationSize;

        private int _filler;

        [Test]
        public void Given_AStringParsedOnce_When_ParsedAgainAtTheFarEndOfProbation_Then_ItKeepsItsFirstArray()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            var first = cache.Parse("flex p-4");
            ParseFirstSightings(cache, Probation * 2 - 1);

            // Act
            var second = cache.Parse("flex p-4");

            // Assert
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void Given_AStringParsedOncePastProbation_When_ParsedAgainWithinTheWindow_Then_ItIsCachedFromThen()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            _ = cache.Parse("flex p-4");
            ParseFirstSightings(cache, Probation * 2);
            var cached = cache.Parse("flex p-4");
            ParseFirstSightings(cache, Probation * 2);

            // Act
            var again = cache.Parse("flex p-4");

            // Assert
            Assert.That(again, Is.SameAs(cached));
        }

        [Test]
        public void Given_AStringWhoseHashAgedIntoTheOlderRecord_When_ParsedAgain_Then_ItIsCachedFromThen()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            _ = cache.Parse("flex p-4");
            ParseFirstSightings(cache, Window);
            var cached = cache.Parse("flex p-4");
            ParseFirstSightings(cache, Probation * 2);

            // Act
            var again = cache.Parse("flex p-4");

            // Assert
            Assert.That(again, Is.SameAs(cached));
        }

        [Test]
        public void Given_AFullProbationGeneration_When_AnotherStringIsParsed_Then_ParsingItAgainReturnsTheSameArray()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            ParseFirstSightings(cache, Probation);

            // Act
            var overflowing = cache.Parse("overflow-trigger overflow-trigger--on");

            // Assert
            Assert.That(cache.Parse("overflow-trigger overflow-trigger--on"), Is.SameAs(overflowing));
        }

        [Test]
        public void Given_ACachedStringBesideJustUnderAWindowOfMovingStringsPerRender_When_Rendered_Then_ItKeepsItsArray()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            _ = cache.Parse("flex p-4");
            var cached = cache.Parse("flex p-4");

            // Act
            for (var render = 0; render < 6; render++)
            {
                ParseFirstSightings(cache, Window - 1);
                _ = cache.Parse("flex p-4");
            }

            // Assert
            Assert.That(cache.Parse("flex p-4"), Is.SameAs(cached));
        }

        [Test]
        public void Given_MoreStringsThanAWindowParsedInARepeatingOrder_When_ParsedOnEveryPass_Then_EachKeepsItsArray()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            var classNames = ClassNames(Window + Window / 2);
            var previousPass = Array.Empty<string[]>();
            for (var pass = 0; pass < 4; pass++)
            {
                previousPass = ParsePass(cache, classNames);
            }

            // Act
            var lastPass = ParsePass(cache, classNames);

            // Assert
            Assert.That(ReParsed(previousPass, lastPass), Is.Zero);
        }

        [Test]
        public void Given_ACachedString_When_TwoWindowsOfFirstParsesPassWithoutIt_Then_ItIsDropped()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            _ = cache.Parse("flex p-4");
            var cached = cache.Parse("flex p-4");

            // Act
            ParseFirstSightings(cache, Window * 2);

            // Assert
            Assert.That(cache.Parse("flex p-4"), Is.Not.SameAs(cached));
        }

        [Test]
        public void Given_OnlyMovingStrings_When_TenWindowsPass_Then_NoMoreThanTwoWindowsOfHashesAreEverHeld()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            var mostHeld = 0;

            // Act
            for (var i = 0; i < Window * 10; i++)
            {
                _ = cache.Parse(NextFiller());
                mostHeld = Math.Max(mostHeld, Field<HashSet<int>>(cache, "_seenRecent").Count
                    + Field<HashSet<int>>(cache, "_seenOlder").Count);
            }

            // Assert
            Assert.That(mostHeld, Is.LessThanOrEqualTo(Window * 2));
        }

        [Test]
        public void Given_OnlyMovingStrings_When_TenWindowsPass_Then_NoMoreThanTwoProbationGenerationsAreEverHeld()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            var mostHeld = 0;

            // Act
            for (var i = 0; i < Window * 10; i++)
            {
                _ = cache.Parse(NextFiller());
                mostHeld = Math.Max(mostHeld, Field<Dictionary<string, string[]>>(cache, "_probationCurrent").Count
                    + Field<Dictionary<string, string[]>>(cache, "_probationPrevious").Count);
            }

            // Assert
            Assert.That(mostHeld, Is.LessThanOrEqualTo(Probation * 2));
        }

        [Test]
        public void Given_EveryStructureHoldingSomething_When_Cleared_Then_EveryStructureIsEmpty()
        {
            // Arrange — a window of first parses moves the cached string into the older generation and fills
            // both probation generations; the string parsed twice after it lands in the newer one.
            var cache = new ClassNameParseCache();
            _ = cache.Parse("flex p-4");
            _ = cache.Parse("flex p-4");
            ParseFirstSightings(cache, Window + 1);
            _ = cache.Parse("grid p-2");
            _ = cache.Parse("grid p-2");
            var before = Held(cache);

            // Act
            cache.Clear();

            // Assert
            Assert.That((before.All(count => count > 0), string.Join(" ", Held(cache))),
                Is.EqualTo((true, "0 0 0 0 0 0 0")));
        }

        // Only that the probe moves: the two zeros below would read the same from a probe stuck at zero.
        [Test]
        public void Given_TheAllocationProbe_When_ADelegateSplitsAString_Then_ItCountsBlocks()
        {
            // Arrange
            var cache = new ClassNameParseCache();

            // Act
            var blocks = GCAllocationProbe.SampleBlocksDuring(() => cache.Parse("never-parsed p-2"));

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AStringInTheOlderGeneration_When_ParsedAgain_Then_NothingIsAllocated()
        {
            // Arrange — the measured delegate runs once against a throwaway cache first, so work done on a
            // delegate's first execution is not charged to the carry.
            var cache = CacheHoldingInOlderGeneration("flex flex-row p-4");
            Action carry = () => cache.Parse("flex flex-row p-4");
            carry();
            cache = CacheHoldingInOlderGeneration("flex flex-row p-4");
            var inOlderOnly = Field<Dictionary<string, string[]>>(cache, "_previous").ContainsKey("flex flex-row p-4")
                && !Field<Dictionary<string, string[]>>(cache, "_current").ContainsKey("flex flex-row p-4");

            // Act
            var blocks = GCAllocationProbe.SampleBlocksDuring(carry);

            // Assert
            Assert.That((inOlderOnly, blocks), Is.EqualTo((true, 0)));
        }

        [Test]
        public void Given_AStringInProbation_When_ParsedAgain_Then_NothingIsAllocated()
        {
            // Arrange — as above, the delegate's first execution is spent on a throwaway cache.
            var cache = new ClassNameParseCache();
            _ = cache.Parse("flex flex-row p-4");
            Action promote = () => cache.Parse("flex flex-row p-4");
            promote();
            cache = new ClassNameParseCache();
            _ = cache.Parse("flex flex-row p-4");
            var inProbationOnly = Field<Dictionary<string, string[]>>(cache, "_probationCurrent")
                    .ContainsKey("flex flex-row p-4")
                && !Field<Dictionary<string, string[]>>(cache, "_current").ContainsKey("flex flex-row p-4");

            // Act
            var blocks = GCAllocationProbe.SampleBlocksDuring(promote);

            // Assert
            Assert.That((inProbationOnly, blocks), Is.EqualTo((true, 0)));
        }

        private string NextFiller() => $"absolute left-[{_filler++}px]";

        private void ParseFirstSightings(ClassNameParseCache cache, int count)
        {
            for (var i = 0; i < count; i++)
            {
                _ = cache.Parse(NextFiller());
            }
        }

        private ClassNameParseCache CacheHoldingInOlderGeneration(string key)
        {
            var cache = new ClassNameParseCache();
            _ = cache.Parse(key);
            _ = cache.Parse(key);
            ParseFirstSightings(cache, Window);
            return cache;
        }

        private static string[] ClassNames(int count)
        {
            var classNames = new string[count];
            for (var i = 0; i < count; i++)
            {
                classNames[i] = $"grid-cell-{i} p-2";
            }
            return classNames;
        }

        private static string[][] ParsePass(ClassNameParseCache cache, string[] classNames)
        {
            var arrays = new string[classNames.Length][];
            for (var i = 0; i < classNames.Length; i++)
            {
                arrays[i] = cache.Parse(classNames[i]);
            }
            return arrays;
        }

        private static int ReParsed(string[][] previousPass, string[][] lastPass)
        {
            var reParsed = 0;
            for (var i = 0; i < lastPass.Length; i++)
            {
                if (!ReferenceEquals(previousPass[i], lastPass[i])) reParsed++;
            }
            return reParsed;
        }

        private static int[] Held(ClassNameParseCache cache) => new[]
        {
            Field<Dictionary<string, string[]>>(cache, "_current").Count,
            Field<Dictionary<string, string[]>>(cache, "_previous").Count,
            Field<Dictionary<string, string[]>>(cache, "_probationCurrent").Count,
            Field<Dictionary<string, string[]>>(cache, "_probationPrevious").Count,
            Field<HashSet<int>>(cache, "_seenRecent").Count,
            Field<HashSet<int>>(cache, "_seenOlder").Count,
            Field<int>(cache, "_firstSightingsThisWindow"),
        };

        private static T Field<T>(ClassNameParseCache cache, string name)
        {
            var field = typeof(ClassNameParseCache).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ClassNameParseCache).FullName, name);
            return (T)field.GetValue(cache);
        }
    }
}
