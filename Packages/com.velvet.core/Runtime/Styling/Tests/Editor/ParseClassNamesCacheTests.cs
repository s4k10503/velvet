using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the caching contract of class-name parsing.
    /// <list type="bullet">
    /// <item>Parsing the same string twice returns the identical cached array instance.</item>
    /// <item>Parsing different strings returns distinct array instances.</item>
    /// <item>A null or empty string returns the shared empty array.</item>
    /// <item>A cached array holds the space-split tokens of its key.</item>
    /// <item>A thousand-odd constant strings parsed on every pass keep their arrays, and so do three hundred
    /// parsed beside as many moving arbitrary values.</item>
    /// <item>A string parsed on every render keeps its array while a moving arbitrary value is parsed beside
    /// it, and the moving value's earlier strings are released.</item>
    /// <item>Parsing a moving value for thousands of renders logs nothing.</item>
    /// <item>Draining the cache makes an already-parsed string parse to a fresh array instance.</item>
    /// </list>
    /// Also specifies the contract of the <see cref="StyleClassNames"/> class-name builder
    /// (<see cref="StyleClassNames.Class"/> joins its parts with a single space, skips <c>null</c>/empty parts,
    /// and <see cref="StyleClassNames.When"/> returns the class name when its condition is true and
    /// <c>null</c> when false, so it composes directly as a part of <c>Class</c>), and the underscore-for-space
    /// arbitrary-value convention for functional color notation: a className string splits on spaces, so a
    /// bracketed value embeds its spaces as underscores, and the rgb()/rgba() grammar must restore them before
    /// parsing — without the substitution the underscore form of a copy-pasted "rgb(0, 128, 255)" fails byte
    /// parsing on its "_128"/"_255" channels, the class silently falls back to a no-op USS class, and the color
    /// is never applied.
    /// </summary>
    /// <remarks>
    /// The cache is process-wide static, so <see cref="SetUp"/> drains it via
    /// <see cref="ClassNameCacheTestAccess.ClearForTest"/>: another fixture's cached entries, probation,
    /// remembered hashes and window count would otherwise decide what these cases measure.
    /// </remarks>
    [TestFixture]
    internal sealed class ParseClassNamesCacheTests
    {
        [SetUp]
        public void SetUp()
        {
            ClassNameCacheTestAccess.ClearForTest();
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
        public void Given_OverAThousandConstantClassStrings_When_ParsedOnEveryPass_Then_EachKeepsItsArray()
        {
            // Arrange
            const int count = 1024;
            var classNames = new string[count];
            for (var i = 0; i < count; i++)
            {
                classNames[i] = $"grid-cell-{i} p-2";
            }
            var previousPass = new string[count][];
            var lastPass = new string[count][];
            for (var pass = 0; pass < 4; pass++)
            {
                for (var i = 0; i < count; i++)
                {
                    previousPass[i] = V.ParseClassNames(classNames[i]);
                }
            }

            // Act
            for (var i = 0; i < count; i++)
            {
                lastPass[i] = V.ParseClassNames(classNames[i]);
            }

            // Assert
            var reParsed = 0;
            for (var i = 0; i < count; i++)
            {
                if (!ReferenceEquals(previousPass[i], lastPass[i])) reParsed++;
            }
            Assert.That(reParsed, Is.Zero);
        }

        [Test]
        public void Given_AsManyMovingValuesAsStableStringsPerPass_When_ParsedOnEveryPass_Then_EachStableStringKeepsItsArray()
        {
            // Arrange
            const int count = 300;
            var classNames = new string[count];
            for (var i = 0; i < count; i++)
            {
                classNames[i] = $"grid-cell-{i} p-2";
            }
            var previousPass = new string[count][];
            var lastPass = new string[count][];
            var x = 0;
            for (var pass = 0; pass < 4; pass++)
            {
                for (var i = 0; i < count; i++)
                {
                    previousPass[i] = V.ParseClassNames(classNames[i]);
                    _ = V.ParseClassNames($"absolute left-[{x++}px] top-[4px]");
                }
            }

            // Act
            for (var i = 0; i < count; i++)
            {
                lastPass[i] = V.ParseClassNames(classNames[i]);
                _ = V.ParseClassNames($"absolute left-[{x++}px] top-[4px]");
            }

            // Assert
            var reParsed = 0;
            for (var i = 0; i < count; i++)
            {
                if (!ReferenceEquals(previousPass[i], lastPass[i])) reParsed++;
            }
            Assert.That(reParsed, Is.Zero);
        }

        [Test]
        public void Given_AMovingArbitraryValue_When_AStableStringIsParsedEveryRender_Then_TheStableStringKeepsItsArray()
        {
            // Arrange
            var first = V.ParseClassNames("flex flex-row p-4");

            // Act
            for (var x = 0; x < 5000; x++)
            {
                _ = V.ParseClassNames($"absolute left-[{x}px] top-[4px]");
                _ = V.ParseClassNames("flex flex-row p-4");
            }

            // Assert
            Assert.That(V.ParseClassNames("flex flex-row p-4"), Is.SameAs(first));
        }

        // GREEN_ON_BASE(characterization): a moving value's old strings were released before this change too.
        [Test]
        public void Given_AMovingArbitraryValue_When_AThousandMoreValuesHavePassed_Then_AnEarlierValueIsReleased()
        {
            // Arrange
            for (var x = 0; x < 4000; x++)
            {
                _ = V.ParseClassNames($"absolute left-[{x}px] top-[4px]");
                _ = V.ParseClassNames("flex flex-row p-4");
            }
            var probe = V.ParseClassNames("absolute left-[4000px] top-[4px]");

            // Act
            for (var x = 4001; x <= 5000; x++)
            {
                _ = V.ParseClassNames($"absolute left-[{x}px] top-[4px]");
                _ = V.ParseClassNames("flex flex-row p-4");
            }

            // Assert
            Assert.That(V.ParseClassNames("absolute left-[4000px] top-[4px]"), Is.Not.SameAs(probe));
        }

        [Test]
        public void Given_AMovingArbitraryValue_When_ParsedForThousandsOfRenders_Then_NothingIsLogged()
        {
            // Arrange
            var logged = new List<string>();
            void OnLog(string message, string stackTrace, LogType type) => logged.Add(message);
            Application.logMessageReceived += OnLog;

            // Act
            try
            {
                for (var x = 0; x < 5000; x++)
                {
                    _ = V.ParseClassNames($"absolute left-[{x}px] top-[4px]");
                    _ = V.ParseClassNames("flex flex-row p-4");
                }
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            // Assert
            Assert.That(logged, Is.Empty);
        }

        #endregion

        #region Cache drain

        [Test]
        public void Given_ParsedString_When_CacheDrained_Then_SameStringReParsesToFreshInstance()
        {
            // Arrange
            var beforeDrain = V.ParseClassNames("drain-probe drain-probe--active");

            // Act
            ClassNameCacheTestAccess.ClearForTest();
            var afterDrain = V.ParseClassNames("drain-probe drain-probe--active");

            // Assert
            Assert.That(afterDrain, Is.Not.SameAs(beforeDrain));
        }

        #endregion

        #region StyleClassNames.Class

        [Test]
        public void Given_NoParts_When_ClassBuilt_Then_ReturnsEmptyString()
        {
            // Act
            var result = StyleClassNames.Class();

            // Assert
            Assert.That(result, Is.EqualTo(""));
        }

        [Test]
        public void Given_SinglePart_When_ClassBuilt_Then_ReturnsThatPart()
        {
            // Act
            var result = StyleClassNames.Class("btn");

            // Assert
            Assert.That(result, Is.EqualTo("btn"));
        }

        [Test]
        public void Given_MultipleParts_When_ClassBuilt_Then_JoinsWithSingleSpace()
        {
            // Act
            var result = StyleClassNames.Class("btn", "btn--primary", "btn--lg");

            // Assert
            Assert.That(result, Is.EqualTo("btn btn--primary btn--lg"));
        }

        [Test]
        public void Given_NullParts_When_ClassBuilt_Then_SkipsNullParts()
        {
            // Act
            var result = StyleClassNames.Class("btn", null, "btn--primary");

            // Assert
            Assert.That(result, Is.EqualTo("btn btn--primary"));
        }

        [Test]
        public void Given_EmptyParts_When_ClassBuilt_Then_SkipsEmptyParts()
        {
            // Act
            var result = StyleClassNames.Class("btn", "", "btn--primary");

            // Assert
            Assert.That(result, Is.EqualTo("btn btn--primary"));
        }

        [Test]
        public void Given_AllNullOrEmptyParts_When_ClassBuilt_Then_ReturnsEmptyString()
        {
            // Act
            var result = StyleClassNames.Class(null, "", null, "");

            // Assert
            Assert.That(result, Is.EqualTo(""));
        }

        #endregion

        #region StyleClassNames.When

        [Test]
        public void Given_TrueCondition_When_WhenEvaluated_Then_ReturnsClassName()
        {
            // Act
            var result = StyleClassNames.When(true, "active");

            // Assert
            Assert.That(result, Is.EqualTo("active"));
        }

        [Test]
        public void Given_FalseCondition_When_WhenEvaluated_Then_ReturnsNull()
        {
            // Act
            var result = StyleClassNames.When(false, "active");

            // Assert
            Assert.That(result, Is.Null);
        }

        #endregion

        #region StyleClassNames integration

        [Test]
        public void Given_WhenResultsAsParts_When_ClassBuilt_Then_KeepsOnlyTrueConditionedClasses()
        {
            // Act
            var result = StyleClassNames.Class(
                "btn",
                StyleClassNames.When(true, "btn--active"),
                StyleClassNames.When(false, "btn--disabled"));

            // Assert
            Assert.That(result, Is.EqualTo("btn btn--active"));
        }

        #endregion

        #region Color underscore convention

        [Test]
        public void Given_RgbWithUnderscoreSpacing_When_Parsed_Then_ResolvesChannels()
        {
            // Act — the underscore form of "rgb(0, 128, 255)".
            var ok = StyleArbitraryValueResolver.TryParse("bg-[rgb(0,_128,_255)]", out var s);

            // Assert — recognized as an arbitrary background color with the spaced channels resolved.
            Assert.That((ok, s.Property, s.Color.g, s.Color.b),
                Is.EqualTo((true, ArbitraryProperty.BackgroundColor, 128f / 255f, 1f)));
        }

        [Test]
        public void Given_RgbaWithUnderscoreSpacing_When_Parsed_Then_ResolvesAlpha()
        {
            // Act — the underscore form of "rgba(255, 0, 0, 0.5)".
            var ok = StyleArbitraryValueResolver.TryParse("bg-[rgba(255,_0,_0,_0.5)]", out var s);

            // Assert — the alpha channel survives the underscore substitution.
            Assert.That((ok, s.Color.r, s.Color.a), Is.EqualTo((true, 1f, 0.5f)));
        }

        #endregion
    }
}
