using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <see cref="ClassNameParseCache"/> keeps a set of strings that outgrows its initial
    /// generation, and what carrying an entry between generations allocates. The caching contract as callers
    /// see it is <c>ParseClassNamesCacheTests</c>'.
    /// </summary>
    [TestFixture]
    internal sealed class ClassNameParseCacheTests
    {
        [Test]
        public void Given_MoreStringsThanOneGenerationHolds_When_ParsedOnEveryPass_Then_EachKeepsItsArray()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            var classNames = ClassNames(ClassNameParseCache.InitialGenerationSize * 5 / 4);
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
        public void Given_MoreStringsThanTwoGenerationsHold_When_ParsedOnEveryPass_Then_EachKeepsItsArray()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            var classNames = ClassNames(ClassNameParseCache.InitialGenerationSize * 4);
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
        public void Given_AFullGeneration_When_AnotherStringIsParsed_Then_ItSplitsOnSpaces()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            foreach (var className in ClassNames(ClassNameParseCache.InitialGenerationSize))
            {
                _ = cache.Parse(className);
            }

            // Act
            var tokens = cache.Parse("overflow-trigger  overflow-trigger--on");

            // Assert
            Assert.That(tokens, Is.EqualTo(new[] { "overflow-trigger", "overflow-trigger--on" }));
        }

        // The probe has to move on this path for the zero below to mean anything: a miss splits, and a split
        // allocates.
        [Test]
        public void Given_AStringNotYetParsed_When_Parsed_Then_TheProbeCountsItsSplit()
        {
            // Arrange
            var cache = new ClassNameParseCache();
            _ = cache.Parse("warm-up p-1");

            // Act
            var blocks = GCAllocationProbe.SampleBlocksDuring(() => cache.Parse("never-parsed p-2"));

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AStringInThePreviousGeneration_When_ParsedAgain_Then_NothingIsAllocated()
        {
            // Arrange — the first carry runs on a throwaway cache, so first-execution work is not charged to
            // the measured one.
            _ = CacheHoldingInPreviousGeneration("warm-up p-1").Parse("warm-up p-1");
            var cache = CacheHoldingInPreviousGeneration("flex flex-row p-4");
            var carriedFromPrevious = InGeneration(cache, "_previous", "flex flex-row p-4")
                && !InGeneration(cache, "_current", "flex flex-row p-4");

            // Act
            var blocks = GCAllocationProbe.SampleBlocksDuring(() => cache.Parse("flex flex-row p-4"));

            // Assert
            Assert.That((carriedFromPrevious, blocks), Is.EqualTo((true, 0)));
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

        // Parses the key, then fills the rest of the generation and one string past it.
        private static ClassNameParseCache CacheHoldingInPreviousGeneration(string key)
        {
            var cache = new ClassNameParseCache();
            _ = cache.Parse(key);
            for (var i = 0; i < ClassNameParseCache.InitialGenerationSize; i++)
            {
                _ = cache.Parse($"filler-{i}");
            }
            return cache;
        }

        private static bool InGeneration(ClassNameParseCache cache, string fieldName, string key)
        {
            var field = typeof(ClassNameParseCache).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ClassNameParseCache).FullName, fieldName);
            return ((Dictionary<string, string[]>)field.GetValue(cache)).ContainsKey(key);
        }
    }
}
